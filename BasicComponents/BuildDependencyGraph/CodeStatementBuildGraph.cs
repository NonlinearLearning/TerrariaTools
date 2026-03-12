using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Buildalyzer;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.MSBuild;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 语句依赖构建
    /// 用于类似于流分析但比流分析更强强大的功能
    /// </summary>
    public class CodeStatementBuildGraph : ICodeDependencyAnalyzer
    {
        /// <summary>
        /// 解决方案对象
        /// </summary>
        private readonly Solution _solution;

        /// <summary>
        /// 依赖图对象
        /// </summary>
        private readonly DependencyGraph _graph = new DependencyGraph();

        /// <summary>
        /// 已处理符号的缓存
        /// </summary>
        private readonly ConcurrentDictionary<ISymbol, byte> _processedSymbols = new ConcurrentDictionary<ISymbol, byte>(SymbolEqualityComparer.Default);

        /// <summary>
        /// 待处理符号队列
        /// </summary>
        private readonly ConcurrentQueue<ISymbol> _pendingSymbols = new ConcurrentQueue<ISymbol>();

        /// <summary>
        /// 队列信号量，用于控制并行处理
        /// </summary>
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);

        /// <summary>
        /// 最大并行度
        /// </summary>
        private readonly int _maxDegreeOfParallelism;

        /// <summary>
        /// 剩余待处理项计数
        /// </summary>
        private int _remainingWorkItems;

        /// <summary>
        /// 默认最大并行度
        /// </summary>
        private const int DefaultMaxDegreeOfParallelism = 8;

        /// <summary>
        /// 分析模式枚举
        /// </summary>
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="solution">解决方案</param>
        /// <param name="maxDegreeOfParallelism">最大并行度</param>
        public CodeStatementBuildGraph(Solution solution, int maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);
        }

        /// <summary>
        /// 使用 MSBuildWorkspace 加载解决方案
        /// </summary>
        /// <param name="solutionPath">解决方案文件路径</param>
        /// <returns>代码依赖分析器实例</returns>
        public static async Task<CodeStatementBuildGraph> CreateFromSolutionAsync(string solutionPath)
        {
            var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            return new CodeStatementBuildGraph(solution);
        }

        /// <summary>
        /// 获取分析生成的依赖图
        /// </summary>
        public DependencyGraph Graph => _graph;

        /// <summary>
        /// 全局分析入口
        /// </summary>
        /// <param name="mode">分析模式</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task AnalyzeSolutionAsync(CodeDependencyAnalyzer.AnalysisMode mode = CodeDependencyAnalyzer.AnalysisMode.Standard)
        {
            var seeds = new List<ISymbol>();

            if (mode == CodeDependencyAnalyzer.AnalysisMode.Aggressive)
            {
                // 仅寻找 Main 方法作为种子
                foreach (var project in _solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    var mainMethod = compilation.GetEntryPoint(default);
                    if (mainMethod != null) seeds.Add(mainMethod);
                }
            }
            else
            {
                // 默认模式下，将所有 Public 成员作为种子
                foreach (var project in _solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    foreach (var tree in compilation.SyntaxTrees)
                    {
                        var model = compilation.GetSemanticModel(tree);
                        var root = await tree.GetRootAsync();

                        var memberDeclarations = root.DescendantNodes()
                            .OfType<MemberDeclarationSyntax>();

                        foreach (var member in memberDeclarations)
                        {
                            var symbol = model.GetDeclaredSymbol(member);
                            if (symbol != null && symbol.DeclaredAccessibility == Accessibility.Public)
                            {
                                seeds.Add(symbol);
                            }
                        }
                    }
                }
            }

            if (seeds.Count > 0)
            {
                await AnalyzeRecursiveAsync(seeds);
            }
        }

        /// <summary>
        /// 递归分析单个种子符号
        /// </summary>
        /// <param name="seedSymbol">种子符号</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task AnalyzeRecursiveAsync(ISymbol seedSymbol)
        {
            if (seedSymbol == null) return;
            EnqueueSymbol(seedSymbol);
            await ProcessQueueAsync();
        }

        /// <summary>
        /// 递归分析种子符号集合
        /// </summary>
        /// <param name="seeds">种子符号集合</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task AnalyzeRecursiveAsync(IEnumerable<ISymbol> seeds)
        {
            if (seeds == null) return;
            foreach (var seed in seeds) EnqueueSymbol(seed);
            await ProcessQueueAsync();
        }

        /// <summary>
        /// 将符号加入处理队列
        /// </summary>
        /// <param name="symbol">待处理符号</param>
        private void EnqueueSymbol(ISymbol symbol)
        {
            if (symbol == null) return;
            if (_processedSymbols.TryAdd(symbol, 0))
            {
                _pendingSymbols.Enqueue(symbol);
                Interlocked.Increment(ref _remainingWorkItems);
                _queueSignal.Release();
                _graph.GetOrAddNode(symbol);
            }
        }

        /// <summary>
        /// 处理符号队列
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        private async Task ProcessQueueAsync()
        {
            if (Volatile.Read(ref _remainingWorkItems) == 0)
            {
                return;
            }

            var workers = Enumerable.Range(0, _maxDegreeOfParallelism)
                .Select(_ => Task.Run(ProcessWorkerAsync))
                .ToArray();

            await Task.WhenAll(workers);
        }

        /// <summary>
        /// 工作线程处理逻辑
        /// </summary>
        /// <returns>表示异步操作的任务</returns>
        private async Task ProcessWorkerAsync()
        {
            while (true)
            {
                await _queueSignal.WaitAsync().ConfigureAwait(false);

                if (!_pendingSymbols.TryDequeue(out var symbol))
                {
                    if (Volatile.Read(ref _remainingWorkItems) == 0)
                    {
                        return;
                    }

                    continue;
                }

                try
                {
                    await ProcessSymbolAsync(symbol).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理符号 {symbol} 时出错: {ex.Message}");
                }
                finally
                {
                    if (Interlocked.Decrement(ref _remainingWorkItems) == 0)
                    {
                        // 唤醒所有工人以观察完成情况并退出
                        for (var i = 0; i < _maxDegreeOfParallelism; i++)
                        {
                            _queueSignal.Release();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 处理单个符号并提取其依赖
        /// </summary>
        /// <param name="symbol">待处理符号</param>
        /// <returns>表示异步操作的任务</returns>
        private async Task ProcessSymbolAsync(ISymbol symbol)
        {
            if (symbol is IAssemblySymbol assembly)
            {
                foreach (var attr in assembly.GetAttributes()) AddAttributeDependency(symbol, attr);
                return;
            }

            var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference == null)
            {
                if (symbol is INamedTypeSymbol namedType)
                {
                    foreach (var arg in namedType.TypeArguments) AddDependency(symbol, arg);
                }
                return;
            }

            if (symbol.ContainingType != null) AddDependency(symbol, symbol.ContainingType);

            if (symbol is IMethodSymbol method)
            {
                AddDependency(symbol, method.ReturnType);
                foreach (var param in method.Parameters) AddDependency(symbol, param.Type);
                foreach (var typeParam in method.TypeParameters)
                {
                    foreach (var constraint in typeParam.ConstraintTypes) AddDependency(symbol, constraint);
                }

                if (method.IsOverride && method.OverriddenMethod != null) AddDependency(symbol, method.OverriddenMethod);

                foreach (var impl in method.ExplicitInterfaceImplementations) AddDependency(symbol, impl);

                // 使用 Roslyn Flow 分析方法体
                await AnalyzeMethodFlowAsync(method);
            }
            else if (symbol is IPropertySymbol property)
            {
                AddDependency(symbol, property.Type);
                if (property.OverriddenProperty != null) AddDependency(symbol, property.OverriddenProperty);
                foreach (var impl in property.ExplicitInterfaceImplementations) AddDependency(symbol, impl);
            }
            else if (symbol is IFieldSymbol field)
            {
                AddDependency(symbol, field.Type);
            }
            else if (symbol is INamedTypeSymbol type)
            {
                if (type.BaseType != null) AddDependency(symbol, type.BaseType);
                foreach (var iface in type.Interfaces) AddDependency(symbol, iface);
                foreach (var arg in type.TypeArguments) AddDependency(symbol, arg);
                foreach (var member in type.GetMembers())
                {
                    if (member.IsImplicitlyDeclared) continue;
                    AddDependency(symbol, member);
                }
            }

            foreach (var attr in symbol.GetAttributes()) AddAttributeDependency(symbol, attr);

            var syntaxNode = await reference.GetSyntaxAsync();
            var semanticModel = await GetSemanticModelAsync(syntaxNode.SyntaxTree);

            if (semanticModel != null)
            {
                foreach (var descendant in syntaxNode.DescendantNodes())
                {
                    AnalyzeNode(symbol, descendant, semanticModel);
                }
            }
        }

        /// <summary>
        /// 集成 Roslyn Flow 分析
        /// </summary>
        /// <param name="method">待分析的方法符号</param>
        /// <returns>表示异步操作的任务</returns>
        private async Task AnalyzeMethodFlowAsync(IMethodSymbol method)
        {
            var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference == null) return;

            var syntax = await reference.GetSyntaxAsync();
            if (syntax is BaseMethodDeclarationSyntax methodSyntax && methodSyntax.Body != null)
            {
                var model = await GetSemanticModelAsync(syntax.SyntaxTree);
                if (model == null) return;

                // 构建控制流图 (CFG)
                var cfg = ControlFlowGraph.Create(methodSyntax, model);
                foreach (var block in cfg.Blocks)
                {
                    foreach (var operation in block.Operations)
                    {
                        // 遍历操作以提取依赖
                        AnalyzeOperation(method, operation);
                    }
                }
            }
        }

        /// <summary>
        /// 分析操作并提取依赖
        /// </summary>
        /// <param name="from">来源符号</param>
        /// <param name="operation">Roslyn 操作对象</param>
        private void AnalyzeOperation(ISymbol from, IOperation operation)
        {
            if (operation == null) return;

            // 提取操作中引用的符号
            if (operation is IInvocationOperation invocation)
            {
                AddDependency(from, invocation.TargetMethod);
            }
            else if (operation is IFieldReferenceOperation fieldRef)
            {
                AddDependency(from, fieldRef.Field);
            }
            else if (operation is IPropertyReferenceOperation propRef)
            {
                AddDependency(from, propRef.Property);
            }

            foreach (var child in operation.Children)
            {
                AnalyzeOperation(from, child);
            }
        }

        /// <summary>
        /// 异步获取语义模型
        /// </summary>
        /// <param name="tree">语法树</param>
        /// <returns>语义模型</returns>
        private async Task<SemanticModel?> GetSemanticModelAsync(SyntaxTree tree)
        {
            var doc = _solution.GetDocument(tree);
            return doc != null ? await doc.GetSemanticModelAsync() : null;
        }

        /// <summary>
        /// 分析语法节点并提取依赖
        /// </summary>
        /// <param name="symbol">当前处理的符号</param>
        /// <param name="node">语法节点</param>
        /// <param name="semanticModel">语义模型</param>
        private void AnalyzeNode(ISymbol symbol, SyntaxNode node, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(node);
            var s = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (s != null && !SymbolEqualityComparer.Default.Equals(s, symbol))
            {
                AddDependency(symbol, s);
            }

            if (node is ExpressionSyntax expr)
            {
                var typeInfo = semanticModel.GetTypeInfo(expr);
                if (typeInfo.Type != null) AddDependency(symbol, typeInfo.Type);
            }
        }

        /// <summary>
        /// 添加特性依赖
        /// </summary>
        /// <param name="from">来源符号</param>
        /// <param name="attr">特性数据</param>
        private void AddAttributeDependency(ISymbol from, AttributeData attr)
        {
            if (attr.AttributeClass != null) AddDependency(from, attr.AttributeClass);
            if (attr.AttributeConstructor != null) AddDependency(from, attr.AttributeConstructor);
        }

        /// <summary>
        /// 添加符号间的依赖关系
        /// </summary>
        /// <param name="from">来源符号</param>
        /// <param name="to">目标符号</param>
        private void AddDependency(ISymbol from, ISymbol? to)
        {
            if (to == null) return;
            var target = to.OriginalDefinition;
            _graph.AddDependency(from, target);
            EnqueueSymbol(target);
        }
    }
}
