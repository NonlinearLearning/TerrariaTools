using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 影子类生成器。
    /// 负责协调依赖分析和成员切片，生成最终的“精简版”源码。
    /// </summary>
    public class ShadowClassGenerator
    {
        private readonly Solution _solution;
        private readonly CodeDependencyAnalyzer _analyzer;
        private readonly CallGraphBuilder _callGraphBuilder;
        
        // 性能优化：缓存各个程序集的类型映射 (SimpleName -> FullNames)
        // 使用 AssemblyIdentity 作为 Key，确保跨 Compilation 也能正确复用缓存
        private static readonly Dictionary<AssemblyIdentity, Dictionary<string, HashSet<string>>> _assemblyTypeMapCache = 
            new Dictionary<AssemblyIdentity, Dictionary<string, HashSet<string>>>();

        public ShadowClassGenerator(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _analyzer = new CodeDependencyAnalyzer(solution);
            _callGraphBuilder = new CallGraphBuilder(solution);
        }

        /// <summary>
        /// 生成针对特定入口点（如 DedServ 方法）的精简版项目源码。
        /// </summary>
        /// <param name="seedSymbol">分析起点符号</param>
        /// <returns>返回包含精简代码的文档集合（路径 -> 内容）</returns>
        public async Task<Dictionary<string, string>> GenerateShadowSourceAsync(ISymbol seedSymbol)
        {
            // 0. 动态冲突域分析
            // 获取种子所在的 Compilation（通常是主项目）
            var seedCompilation = _solution.Projects.FirstOrDefault() != null 
                ? await _solution.Projects.First().GetCompilationAsync() 
                : null;
            
            HashSet<string>? dynamicRiskIdentifiers = null;
            if (seedCompilation != null)
            {
                dynamicRiskIdentifiers = AnalyzeConflicts(seedCompilation);
                Console.WriteLine($"[Conflict Analysis] Found {dynamicRiskIdentifiers.Count} potential conflict identifiers.");
            }

            // 1. 构建全局调用图
            await _callGraphBuilder.BuildAsync();

            // 2. 执行基于入口点的动作分析
            var roots = new List<IMethodSymbol>();
            if (seedSymbol is IMethodSymbol methodSeed) roots.Add(methodSeed);
            var actions = _callGraphBuilder.AnalyzeMethods(roots);

            // 3. 执行递归依赖分析，获取所有必要的符号（方法、字段、属性、类型等）
            // 我们需要以所有被保留的方法作为种子，因为这些方法中的每一个都可能引入新的类型依赖
            var keptMethods = actions.Where(kv => kv.Value != CallGraphBuilder.GraphMethodAction.Delete).Select(kv => kv.Key).ToList();

            var userSeeds = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var entryPoints = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var m in keptMethods) userSeeds.Add(m);
            if (seedSymbol != null) userSeeds.Add(seedSymbol);

            // 关键修正：额外包含所有带有初始化器的静态字段作为种子
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync();
                    var model = compilation.GetSemanticModel(tree);

                    var staticFieldsWithInitializers = root.DescendantNodes()
                        .OfType<FieldDeclarationSyntax>()
                        .Where(f => f.Modifiers.Any(SyntaxKind.StaticKeyword))
                        .SelectMany(f => f.Declaration.Variables)
                        .Where(v => v.Initializer != null);

                    foreach (var fieldVar in staticFieldsWithInitializers)
                    {
                        var symbol = model.GetDeclaredSymbol(fieldVar);
                        if (symbol != null)
                        {
                            // 静态字段初始化器也视为用户种子，因为它们是自发运行的
                            userSeeds.Add(symbol);
                        }
                    }
                }

                // 自动查找程序入口点 (Main 方法)
                var entryPoint = compilation.GetEntryPoint(CancellationToken.None);
                if (entryPoint != null)
                {
                    entryPoints.Add(entryPoint);
                }
            }

            // 汇总所有种子进行分析
            var allSeeds = new HashSet<ISymbol>(userSeeds, SymbolEqualityComparer.Default);
            foreach (var ep in entryPoints) allSeeds.Add(ep);

            await _analyzer.AnalyzeRecursiveAsync(allSeeds);

            // 过滤必要符号：
            // 1. 包含从 userSeeds 出发的所有下游依赖
            // 2. 仅包含从 entryPoints 到 userSeeds 路径上的符号（向上查找到 Main）
            var necessarySymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            // A. 下游依赖
            foreach (var seed in userSeeds)
            {
                var node = _analyzer.Graph.GetOrAddNode(seed);
                foreach (var reachable in _analyzer.Graph.DFS(node))
                {
                    necessarySymbols.Add(reachable.Symbol);
                }
            }

            // B. 从入口点到种子的路径
            var nodesLeadingToUserSeeds = new HashSet<SymbolNode>(_analyzer.Graph.NodeComparer);
            foreach (var seed in userSeeds)
            {
                var node = _analyzer.Graph.GetOrAddNode(seed);
                foreach (var ancestor in _analyzer.Graph.DFS(node, reverse: true))
                {
                    nodesLeadingToUserSeeds.Add(ancestor);
                }
            }

            foreach (var ep in entryPoints)
            {
                // 强制保留入口点本身
                necessarySymbols.Add(ep);

                var epNode = _analyzer.Graph.GetOrAddNode(ep);
                foreach (var reachableFromEP in _analyzer.Graph.DFS(epNode))
                {
                    if (nodesLeadingToUserSeeds.Contains(reachableFromEP))
                    {
                        necessarySymbols.Add(reachableFromEP.Symbol);
                    }
                }
            }

            var result = new Dictionary<string, string>();

            // 4. 识别受影响的文档（包含必要符号或被标记动作符号的文档）
            var affectedDocuments = new HashSet<DocumentId>();

            // 添加必要符号所在的文档
            foreach (var symbol in necessarySymbols)
            {
                foreach (var location in symbol.Locations)
                {
                    if (location.IsInSource)
                    {
                        var doc = _solution.GetDocument(location.SourceTree);
                        if (doc != null) affectedDocuments.Add(doc.Id);
                    }
                }
            }

            // 添加被标记动作符号所在的文档
            foreach (var symbol in actions.Keys)
            {
                foreach (var location in symbol.Locations)
                {
                    if (location.IsInSource)
                    {
                        var doc = _solution.GetDocument(location.SourceTree);
                        if (doc != null) affectedDocuments.Add(doc.Id);
                    }
                }
            }

            // 5. 对每个受影响的文档执行成员级切片 (并行化处理)
            var actionDict = actions.ToDictionary(kv => (ISymbol)kv.Key, kv => kv.Value, SymbolEqualityComparer.Default);
            var tasks = affectedDocuments.Select(async docId =>
            {
                var doc = _solution.GetDocument(docId)!;
                var semanticModel = await doc.GetSemanticModelAsync();
                var root = await doc.GetSyntaxRootAsync();

                if (semanticModel == null || root == null) return;

                // 执行重写
                var rewriter = new MemberSlicingRewriter(semanticModel, actionDict, necessarySymbols, dynamicRiskIdentifiers);
                var newRoot = rewriter.Visit(root);

                if (newRoot != null)
                {
                    // 仅在明确标记有潜在冲突时才执行诊断驱动的修复 (有针对性的语义修复)
                    if (rewriter.HasModifiedAmbiguousSymbols)
                    {
                        var tempTree = newRoot.SyntaxTree;
                        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
                        var tempCompilation = CSharpCompilation.Create("TempAssembly", new[] { tempTree }, _solution.Projects.First().MetadataReferences, compilationOptions);
                        
                        var tempModel = tempCompilation.GetSemanticModel(tempTree);
                        var diagnostics = tempModel.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                        if (diagnostics.Any())
                        {
                            var fixupRewriter = new SemanticFixupRewriter(diagnostics);
                            newRoot = fixupRewriter.Visit(newRoot);
                        }
                    }

                    // 格式化代码 (大类格式化非常耗时，可以考虑在必要时禁用或异步处理)
                    var formattedNode = Microsoft.CodeAnalysis.Formatting.Formatter.Format(newRoot, _solution.Workspace);
                    
                    lock (result)
                    {
                        result[doc.FilePath ?? doc.Name] = formattedNode.ToFullString();
                    }
                }
            });

            await Task.WhenAll(tasks);

            return result;
        }

        private HashSet<string> AnalyzeConflicts(Compilation compilation)
        {
            var conflictIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            var typeCounts = new ConcurrentDictionary<string, ConcurrentBag<string>>(StringComparer.Ordinal);
            
            // 收集所有涉及到的程序集（包含当前程序集和所有引用的程序集）
            var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default) { compilation.Assembly };
            foreach (var reference in compilation.References)
            {
                var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
                if (symbol is IAssemblySymbol asm)
                {
                    assemblies.Add(asm);
                }
            }

            // 限制扫描范围：跳过一些不常用的系统程序集
            var filteredAssemblies = assemblies.Where(asm => {
                string name = asm.Name;
                if (name.StartsWith("System.") && !IsCoreSystemAssembly(name)) return false;
                if (name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }).ToList();

            Parallel.ForEach(filteredAssemblies, assembly =>
            {
                Dictionary<string, HashSet<string>> assemblyMap;
                
                // 检查缓存
                bool needsScan = false;
                lock (_assemblyTypeMapCache)
                {
                    if (!_assemblyTypeMapCache.TryGetValue(assembly.Identity, out assemblyMap!))
                    {
                        needsScan = true;
                    }
                }

                if (needsScan)
                {
                    // 扫描程序集
                    assemblyMap = ScanAssemblyTypes(assembly);
                    lock (_assemblyTypeMapCache)
                    {
                        _assemblyTypeMapCache[assembly.Identity] = assemblyMap;
                    }
                }

                // 将该程序集的类型合并到主计数器中
                foreach (var kv in assemblyMap)
                {
                    var bag = typeCounts.GetOrAdd(kv.Key, _ => new ConcurrentBag<string>());
                    foreach (var fullName in kv.Value)
                    {
                        bag.Add(fullName);
                    }
                }
            });

            // 识别出现次数多于 1 次的简单类型名称（冲突标识符）
            foreach (var kv in typeCounts)
            {
                if (kv.Value.Count > 1)
                {
                    conflictIdentifiers.Add(kv.Key);
                }
            }
            
            return conflictIdentifiers;
        }

        private bool IsCoreSystemAssembly(string name)
        {
            // 保留核心系统程序集，因为它们经常包含基础冲突（如 Task, Action 等）
            return name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System.Core", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System.Collections", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System.Linq", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("System.Threading.Tasks", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 扫描程序集中的所有公开类型，并构建 SimpleName -> FullNames 的映射
        /// </summary>
        private Dictionary<string, HashSet<string>> ScanAssemblyTypes(IAssemblySymbol assembly)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(assembly.GlobalNamespace);

            while (stack.Count > 0)
            {
                var ns = stack.Pop();
                
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol subNs)
                    {
                        stack.Push(subNs);
                    }
                    else if (member is INamedTypeSymbol type)
                    {
                        // 忽略泛型实例（我们只关心原始名称冲突）和编译器生成的名称
                        if (type.Name.Contains("<")) continue;
                        
                        // 仅关注可能通过 using 冲突的公共或内部类型
                        if (type.DeclaredAccessibility != Accessibility.Public && 
                            type.DeclaredAccessibility != Accessibility.Internal) continue;

                        if (!map.TryGetValue(type.Name, out var set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            map[type.Name] = set;
                        }
                        // 使用 ToDisplayString 包含完整的命名空间，用于区分不同命名空间下的同名类型
                        set.Add(type.ToDisplayString());
                    }
                }
            }
            return map;
        }

        private class SemanticFixupRewriter : CSharpSyntaxRewriter
        {
            private readonly HashSet<int> _errorSpans;

            public SemanticFixupRewriter(IEnumerable<Diagnostic> diagnostics)
            {
                // 我们收集所有错误发生的起始位置
                _errorSpans = new HashSet<int>(diagnostics.Select(d => d.Location.SourceSpan.Start));
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                // 优化：上下文过滤
                // 如果标识符是限定名的一部分（作为右侧），或者是别名定义，则无需处理
                if (node.Parent is QualifiedNameSyntax qns && qns.Right == node)
                 {
                     return base.VisitIdentifierName(node);
                 }
                 if (node.Parent is AliasQualifiedNameSyntax aqns && aqns.Name == node)
                 {
                     return base.VisitIdentifierName(node);
                 }
                 if (node.Parent is UsingDirectiveSyntax || node.Parent is NameEqualsSyntax)
                {
                    return base.VisitIdentifierName(node);
                }
                // 关键修正：如果父节点是成员访问表达式且当前节点是其名称部分（Name 属性），不进行全限定替换。
                // 因为 CSharpSyntaxRewriter.VisitMemberAccessExpression 要求其 Name 属性必须是 SimpleNameSyntax，
                // 且成员访问本身已经提供了接收者（如 obj.Method），替换名称部分为全限定名会导致 InvalidCastException 或语法错误。
                if (node.Parent is MemberAccessExpressionSyntax maes && maes.Name == node)
                {
                    return base.VisitIdentifierName(node);
                }

                // 检查当前节点是否位于某个错误的范围内
                // 简单的检查 SpanStart 是否匹配可能不够，因为错误可能覆盖整个节点
                bool isErrorNode = _errorSpans.Any(start => node.Span.Contains(start) || (node.Parent != null && node.Parent.Span.Contains(start)));

                if (isErrorNode)
                {
                    // 尝试从 Annotation 获取原始符号信息
                    var annotation = node.GetAnnotations(MemberSlicingRewriter.OriginalSymbolAnnotationKind).FirstOrDefault();
                    if (annotation != null && !string.IsNullOrEmpty(annotation.Data))
                    {
                        var originalName = annotation.Data;
                        // 只有当原始名和当前名不同（且确实是全限定名）时才替换
                        if (originalName != node.Identifier.Text && originalName.Contains("."))
                        {
                            // 使用 ParseName 生成全限定名节点，并保留原有 trivia (空格、注释等)
                            return SyntaxFactory.ParseName(originalName).WithTriviaFrom(node);
                        }
                    }
                }
                return base.VisitIdentifierName(node);
            }
        }
    }
}
