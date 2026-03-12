using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 表示依赖分析的统计结果，包含图的结构指标和拓扑信息。
    /// </summary>
    public class DependencyAnalysisResult
    {
        /// <summary>
        /// 依赖图中的总节点数（符号数量）。
        /// </summary>
        public int NodeCount { get; set; }

        /// <summary>
        /// 依赖图中的总边数（依赖关系数量）。
        /// </summary>
        public int EdgeCount { get; set; }

        /// <summary>
        /// 指示依赖图中是否存在环（循环依赖）。
        /// </summary>
        public bool HasCycles { get; set; }

        /// <summary>
        /// 强连通分量（SCC）列表。每个内部列表代表一个强连通分量中的所有节点名称。
        /// 如果存在环，SCC 将包含多个节点。
        /// </summary>
        public List<List<string>>? StrongConnectedComponents { get; set; }

        /// <summary>
        /// 拓扑排序结果列表。如果图中有环，此属性可能不完整或未定义。
        /// 表示一种线性的依赖顺序，被依赖的节点排在后面。
        /// </summary>
        public List<string>? TopologicalSort { get; set; }

        /// <summary>
        /// 分析过程中发生的错误信息。如果无错误，则为 null。
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// 高级代码分析器，作为 Analysis 模块的门面（Facade）。
    /// 整合了静态依赖分析、动态调用链分析、字段提取和类型冲突检测等多个子系统。
    /// </summary>
    public class AdvancedCodeAnalyzer
    {
        /// <summary>
        /// Roslyn 解决方案对象，代表当前分析的代码库。
        /// </summary>
        private readonly Solution _solution;

        /// <summary>
        /// 静态代码依赖分析器接口。
        /// </summary>
        private readonly ICodeDependencyAnalyzer _staticAnalyzer;

        /// <summary>
        /// 动态调用链分析器接口。
        /// </summary>
        private readonly ICallChainAnalyzer _dynamicAnalyzer;

        /// <summary>
        /// 玩家字段提取器接口（特定业务逻辑）。
        /// </summary>
        private readonly IPlayerFieldExtractor _fieldExtractor;

        /// <summary>
        /// 类型冲突分析器接口。
        /// </summary>
        private readonly ITypeConflictAnalyzer _typeConflictAnalyzer;

        /// <summary>
        /// 日志记录器。
        /// </summary>
        private readonly ILogger<AdvancedCodeAnalyzer> _logger;

        /// <summary>
        /// 构造函数：使用默认实现初始化所有分析器组件。
        /// </summary>
        /// <param name="solution">待分析的 Roslyn 解决方案。</param>
        public AdvancedCodeAnalyzer(Solution solution)
            : this(
                solution,
                new CodeStatementBuildGraph(solution), // 默认静态分析器
                new CallChainAnalyzer(solution),      // 默认动态分析器
                new PlayerFieldExtractor(solution),   // 默认字段提取器
                new TypeConflictAnalyzer(),           // 默认冲突分析器
                NullLogger<AdvancedCodeAnalyzer>.Instance) // 默认空日志记录器
        {
        }

        /// <summary>
        /// 构造函数：允许注入自定义的分析器实现（用于测试或扩展）。
        /// </summary>
        /// <param name="solution">待分析的 Roslyn 解决方案。</param>
        /// <param name="staticAnalyzer">静态依赖分析器实例。</param>
        /// <param name="dynamicAnalyzer">动态调用链分析器实例。</param>
        /// <param name="fieldExtractor">玩家字段提取器实例。</param>
        /// <param name="typeConflictAnalyzer">类型冲突分析器实例。</param>
        /// <param name="logger">日志记录器（可选）。</param>
        public AdvancedCodeAnalyzer(
            Solution solution,
            ICodeDependencyAnalyzer staticAnalyzer,
            ICallChainAnalyzer dynamicAnalyzer,
            IPlayerFieldExtractor fieldExtractor,
            ITypeConflictAnalyzer typeConflictAnalyzer,
            ILogger<AdvancedCodeAnalyzer>? logger = null)
        {
            // 参数校验：确保所有核心组件不为空。
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _staticAnalyzer = staticAnalyzer ?? throw new ArgumentNullException(nameof(staticAnalyzer));
            _dynamicAnalyzer = dynamicAnalyzer ?? throw new ArgumentNullException(nameof(dynamicAnalyzer));
            _fieldExtractor = fieldExtractor ?? throw new ArgumentNullException(nameof(fieldExtractor));
            _typeConflictAnalyzer = typeConflictAnalyzer ?? throw new ArgumentNullException(nameof(typeConflictAnalyzer));
            _logger = logger ?? NullLogger<AdvancedCodeAnalyzer>.Instance;
        }

        /// <summary>
        /// 公开静态分析器构建的依赖图。
        /// </summary>
        public DependencyGraph Graph => _staticAnalyzer.Graph;

        /// <summary>
        /// 异步查找解决方案中所有项目的入口点（通常是 Main 方法）。
        /// </summary>
        /// <returns>入口点符号列表。</returns>
        public async Task<List<ISymbol>> FindEntryPointsAsync()
        {
            var entryPoints = new List<ISymbol>();
            foreach (var project in _solution.Projects)
            {
                // 获取项目的编译对象。这是一个耗时操作，可能会触发 Roslyn 编译。
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                {
                    continue;
                }

                // 获取编译单元的入口点（Main 方法）。
                var entryPoint = compilation.GetEntryPoint(default);
                if (entryPoint != null)
                {
                    entryPoints.Add(entryPoint);
                }
            }

            return entryPoints;
        }

        /// <summary>
        /// 异步查找名称包含特定模式的类作为入口点。
        /// </summary>
        /// <param name="pattern">类名匹配模式（字符串包含匹配）。</param>
        /// <returns>匹配类的符号列表。</returns>
        public async Task<List<ISymbol>> FindEntryPointsAsync(string pattern)
        {
            var entryPoints = new List<ISymbol>();
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                {
                    continue;
                }

                // 遍历所有语法树，查找类声明。
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var model = compilation.GetSemanticModel(tree);
                    // 获取语法树的根节点。
                    var root = await tree.GetRootAsync();
                    // 使用 LINQ 查询所有类声明语法节点。
                    var classes = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();

                    foreach (var cls in classes)
                    {
                        // 字符串包含匹配（Ordinal 比较，区分大小写）。
                        if (!cls.Identifier.Text.Contains(pattern, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // 获取类声明对应的语义符号。
                        var symbol = model.GetDeclaredSymbol(cls);
                        if (symbol != null)
                        {
                            entryPoints.Add(symbol);
                        }
                    }
                }
            }

            return entryPoints;
        }

        /// <summary>
        /// 异步分析一组种子符号的递归依赖。
        /// </summary>
        /// <param name="seeds">起始符号集合。</param>
        /// <returns>表示异步操作的任务。</returns>
        public async Task AnalyzeRecursiveDependenciesAsync(IEnumerable<ISymbol> seeds)
        {
            // 委托给静态分析器执行递归分析。
            await _staticAnalyzer.AnalyzeRecursiveAsync(seeds);
        }

        /// <summary>
        /// 异步分析指定方法（由类型名和方法名确定）的递归依赖，并返回统计结果。
        /// </summary>
        /// <param name="typeName">包含方法的类型全名。</param>
        /// <param name="methodName">方法名称。</param>
        /// <returns>包含图统计信息的分析结果对象。</returns>
        public async Task<DependencyAnalysisResult> AnalyzeRecursiveDependenciesAsync(string typeName, string methodName)
        {
            var result = new DependencyAnalysisResult();
            try
            {
                ISymbol? targetSymbol = null;
                // 遍历所有项目查找目标类型和方法。
                foreach (var project in _solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null)
                    {
                        continue;
                    }

                    // 通过元数据名称查找类型符号。
                    var typeSymbol = compilation.GetTypeByMetadataName(typeName);
                    if (typeSymbol == null)
                    {
                        continue;
                    }

                    // 查找类型中指定名称的成员（取第一个匹配项）。
                    // 注意：这里未处理重载情况，默认取第一个。
                    targetSymbol = typeSymbol.GetMembers(methodName).FirstOrDefault();
                    if (targetSymbol != null)
                    {
                        break;
                    }
                }

                // 如果未找到目标符号，返回错误信息。
                if (targetSymbol == null)
                {
                    result.Error = $"未找到目标方法: {typeName}.{methodName}";
                    return result;
                }

                // 执行递归静态分析。
                await _staticAnalyzer.AnalyzeRecursiveAsync(new[] { targetSymbol });

                // 填充统计结果。
                result.NodeCount = Graph.AllNodes.Count();
                result.EdgeCount = 0; // TODO: 当前实现未统计边数，建议后续从 Graph 获取。

                // 计算强连通分量 (SCC) 以检测循环依赖。
                var sccs = Graph.FindSCCs();
                result.StrongConnectedComponents = sccs.Select(scc => scc.Select(n => n.FullName).ToList()).ToList();
                // 如果任何 SCC 包含超过 1 个节点，说明存在环。
                result.HasCycles = sccs.Any(scc => scc.Count > 1);

                // 如果无环，则执行拓扑排序。
                if (!result.HasCycles)
                {
                    result.TopologicalSort = Graph.TopologicalSort().Select(n => n.FullName).ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                // 捕获异常并记录日志，封装为 AnalysisException 返回。
                _logger.LogError(ex, "为 {TypeName}.{MethodName} 执行 AnalyzeRecursiveDependenciesAsync 失败", typeName, methodName);
                var wrapped = new AnalysisException("依赖分析失败。", ex);
                result.Error = wrapped.Message;
                return result;
            }
        }

        /// <summary>
        /// 执行全量分析流程：包含字段提取、静态依赖分析、动态日志增强和冲突检测。
        /// </summary>
        /// <param name="seedSymbol">分析的起始种子符号。</param>
        /// <param name="dynamicLogs">可选的动态调用日志路径列表。</param>
        /// <returns>包含图、字段和冲突信息的完整分析结果。</returns>
        public async Task<FullAnalysisResult> PerformFullAnalysisAsync(ISymbol seedSymbol, IEnumerable<string>? dynamicLogs = null)
        {
            var result = new FullAnalysisResult();

            // 1. 执行字段提取分析，获取关键字段引用。
            var fieldResult = await _fieldExtractor.AnalyzeAsync();

            // 2. 准备所有种子符号（初始种子 + 提取出的字段引用）。
            // 使用 SymbolEqualityComparer.Default 确保符号比较的正确性。
            var allSeeds = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { seedSymbol };
            foreach (var sym in fieldResult.ReferencedSymbols)
            {
                allSeeds.Add(sym);
            }

            // 3. 执行静态递归依赖分析。
            await _staticAnalyzer.AnalyzeRecursiveAsync(allSeeds);

            // 4. 如果提供了动态日志，应用动态分析结果增强依赖图。
            if (dynamicLogs != null)
            {
                foreach (var log in dynamicLogs)
                {
                    await _dynamicAnalyzer.ApplyToGraphAsync(Graph, log);
                }
            }

            // 5. 执行全局类型冲突分析。
            var conflicts = await _typeConflictAnalyzer.AnalyzeGlobalConflictsAsync(_solution);
            foreach (var c in conflicts)
            {
                result.ConflictIdentifiers.Add(c);
            }

            // 6. 组装最终结果。
            result.Graph = Graph;
            foreach (var f in fieldResult.ReferencedFieldNames)
            {
                result.PlayerFields.Add(f);
            }

            return result;
        }
    }

    /// <summary>
    /// 全量分析结果的数据容器。
    /// </summary>
    public class FullAnalysisResult
    {
        /// <summary>
        /// 构建完成的依赖图。
        /// </summary>
        public DependencyGraph Graph { get; set; } = null!;

        /// <summary>
        /// 提取出的玩家字段名称集合。
        /// </summary>
        public HashSet<string> PlayerFields { get; set; } = new();

        /// <summary>
        /// 识别出的全局冲突类型标识符集合。
        /// </summary>
        public HashSet<string> ConflictIdentifiers { get; set; } = new();
    }
}
