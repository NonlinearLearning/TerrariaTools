using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 构建函数调用依赖图
    /// </summary>
    public class FunctionBuildGraph
    {
        /// <summary>
        /// Roslyn 解决方案对象
        /// </summary>
        private readonly Solution _solution;

        /// <summary>
        /// 构造函数，注入解决方案
        /// </summary>
        /// <param name="solution">Roslyn 解决方案</param>
        public FunctionBuildGraph(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        /// <summary>
        /// 异步构建调用图
        /// </summary>
        /// <param name="progress">进度报告对象（可选）</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task BuildAsync(IProgress<string>? progress = null)
        {
            progress?.Report("正在构建调用图...");
            // 模拟耗时操作，实际应执行 AST 遍历或语义分析
            await Task.Delay(100);
        }

        /// <summary>
        /// 分析方法集合的操作策略
        /// </summary>
        /// <param name="roots">入口方法集合</param>
        /// <param name="aggressive">是否启用激进分析模式</param>
        /// <param name="enableRatioAnalysis">是否启用比例分析</param>
        /// <returns>方法符号到操作枚举的映射字典</returns>
        public Dictionary<IMethodSymbol, GraphMethodAction> AnalyzeMethods(IEnumerable<IMethodSymbol> roots, bool aggressive = false, bool enableRatioAnalysis = false)
        {
            // 简单模拟逻辑，实际应根据图遍历结果决定操作
            // 使用 SymbolEqualityComparer.Default 确保符号比较的正确性
            return new Dictionary<IMethodSymbol, GraphMethodAction>(SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// AnalyzeMethods 的重载，默认分析空集合
        /// </summary>
        /// <param name="aggressive">是否启用激进分析模式</param>
        /// <param name="enableRatioAnalysis">是否启用比例分析</param>
        /// <returns>方法符号到操作枚举的映射字典</returns>
        public Dictionary<IMethodSymbol, GraphMethodAction> AnalyzeMethods(bool aggressive = false, bool enableRatioAnalysis = false)
        {
            return AnalyzeMethods(Enumerable.Empty<IMethodSymbol>(), aggressive, enableRatioAnalysis);
        }

        /// <summary>
        /// 获取解决方案中声明的所有方法
        /// </summary>
        /// <remarks>警告：此属性会触发全量遍历，大型项目中可能性能较差</remarks>
        public IEnumerable<IMethodSymbol> AllDeclaredMethods
        {
            get
            {
                var methods = new List<IMethodSymbol>();
                foreach (var project in _solution.Projects)
                {
                    // 同步获取编译对象，可能会阻塞线程
                    var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
                    if (compilation == null) continue;
                    foreach (var tree in compilation.SyntaxTrees)
                    {
                        var model = compilation.GetSemanticModel(tree);
                        var root = tree.GetRootAsync().GetAwaiter().GetResult();
                        // 查找所有方法声明语法节点
                        var decls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                        foreach (var decl in decls)
                        {
                            // 获取方法声明的语义符号
                            var symbol = model.GetDeclaredSymbol(decl) as IMethodSymbol;
                            if (symbol != null) methods.Add(symbol);
                        }
                    }
                }
                return methods;
            }
        }

        /// <summary>
        /// 获取从根节点可达的所有方法
        /// </summary>
        /// <param name="roots">根节点集合</param>
        /// <returns>可达方法集合</returns>
        public IEnumerable<IMethodSymbol> GetReachableMethods(IEnumerable<IMethodSymbol> roots) => roots;

        /// <summary>
        /// 定义对图方法的操作枚举
        /// </summary>
        public enum GraphMethodAction
        {
            /// <summary>无操作</summary>
            None,
            /// <summary>删除方法</summary>
            Delete,
            /// <summary>修改为私有</summary>
            Privatize,
            /// <summary>生成桩代码（空实现）</summary>
            Stub,
            /// <summary>清空方法体</summary>
            ClearBody
        }
    }

    /// <summary>
    /// 解析运行时方法调用日志并与静态代码分析结果进行对比的工具
    /// 用于识别实际执行路径、死代码以及验证静态依赖图。
    /// </summary>
    public class CallChainAnalyzer : ICallChainAnalyzer
    {
        /// <summary>
        /// 解决方案对象
        /// </summary>
        private readonly Solution _solution;

        /// <summary>
        /// 正则表达式：用于解析日志行
        /// </summary>
        private static readonly Regex LogRegex = new Regex(@"\[(?<time>.*?)\]\s+\[ENTER\]\s+(?<method>.*)", RegexOptions.Compiled);

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="solution">解决方案</param>
        public CallChainAnalyzer(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        /// <summary>
        /// 解析日志文件并返回原始调用条目列表
        /// </summary>
        /// <param name="logPath">日志文件绝对路径</param>
        /// <returns>调用条目列表</returns>
        public async Task<List<CallEntry>> ParseLogAsync(string logPath)
        {
            var entries = new List<CallEntry>();
            if (!File.Exists(logPath)) return entries;

            // 在后台线程执行文件读取和解析，避免阻塞主线程
            await Task.Run(() =>
            {
                // 逐行读取，适用于大文件，避免一次性加载到内存
                foreach (var line in File.ReadLines(logPath))
                {
                    var match = LogRegex.Match(line);
                    if (match.Success)
                    {
                        entries.Add(new CallEntry
                        {
                            Timestamp = match.Groups["time"].Value,
                            MethodFullName = match.Groups["method"].Value.Trim()
                        });
                    }
                }
            });
            return entries;
        }

        /// <summary>
        /// 将日志中的方法全名映射到 Roslyn 的 IMethodSymbol
        /// </summary>
        /// <param name="methodFullNames">方法全名列表</param>
        /// <returns>方法全名到符号列表的映射字典</returns>
        public async Task<Dictionary<string, List<IMethodSymbol>>> MapMethodsToSymbolsAsync(IEnumerable<string> methodFullNames)
        {
            var mapping = new Dictionary<string, List<IMethodSymbol>>();
            var uniqueNames = methodFullNames.Distinct().ToList();

            // 预加载所有项目的编译信息以提高性能，避免在循环中重复获取
            var projectCompilations = await GetProjectCompilationsAsync();

            foreach (var fullName in uniqueNames)
            {
                // 查找对应的符号列表（可能存在重载）
                var symbols = await FindMethodSymbolsInternalAsync(fullName, projectCompilations);
                if (symbols.Any())
                {
                    mapping[fullName] = symbols;
                }
            }
            return mapping;
        }

        /// <summary>
        /// 根据方法全名寻找符号
        /// </summary>
        /// <param name="methodFullName">方法全名</param>
        /// <returns>符号列表</returns>
        public async Task<List<IMethodSymbol>> FindMethodSymbolAsync(string methodFullName)
        {
            var projectCompilations = await GetProjectCompilationsAsync();
            return await FindMethodSymbolsInternalAsync(methodFullName, projectCompilations);
        }

        /// <summary>
        /// 辅助方法：批量获取所有项目的 Compilation 对象
        /// </summary>
        /// <returns>项目与编译对象的元组列表</returns>
        private async Task<List<(Project Project, Compilation Compilation)>> GetProjectCompilationsAsync()
        {
            var projectCompilations = new List<(Project Project, Compilation Compilation)>();
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation != null)
                {
                    projectCompilations.Add((project, compilation));
                }
            }
            return projectCompilations;
        }

        /// <summary>
        /// 内部方法：在已加载的编译单元中查找方法符号
        /// </summary>
        /// <param name="fullName">方法全名</param>
        /// <param name="projectCompilations">项目与编译对象的列表</param>
        /// <returns>符号列表</returns>
        private async Task<List<IMethodSymbol>> FindMethodSymbolsInternalAsync(string fullName, List<(Project Project, Compilation Compilation)> projectCompilations)
        {
            var symbols = new List<IMethodSymbol>();

            // 假设格式为 Namespace.Type.Method
            var lastDotIndex = fullName.LastIndexOf('.');
            if (lastDotIndex <= 0) return symbols;

            var methodName = fullName.Substring(lastDotIndex + 1);
            var typeName = fullName.Substring(0, lastDotIndex);

            foreach (var pc in projectCompilations)
            {
                // 尝试直接通过元数据名称查找类型
                var type = pc.Compilation.GetTypeByMetadataName(typeName);
                if (type == null)
                {
                    // 尝试模糊搜索，应对类型全名不匹配的情况
                    var typeParts = typeName.Split('.');
                    var typeShortName = typeParts.Last();
                    var fuzzyTypes = pc.Compilation.GetSymbolsWithName(n => n == typeShortName, SymbolFilter.Type)
                        .OfType<INamedTypeSymbol>();

                    foreach (var ft in fuzzyTypes)
                    {
                        // 简单的后缀匹配策略
                        if (ft.ToDisplayString().EndsWith(typeName))
                        {
                            type = ft;
                            break;
                        }
                    }
                }

                if (type != null)
                {
                    // 在类型中查找指定名称的成员，并过滤出方法符号
                    var members = type.GetMembers(methodName).OfType<IMethodSymbol>();
                    symbols.AddRange(members);
                }
            }
            // 去重并返回
            return symbols.Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>().ToList();
        }

        /// <summary>
        /// 将动态调用链应用到现有的依赖图中，并标记活跃节点
        /// </summary>
        /// <param name="graph">依赖图</param>
        /// <param name="logPath">日志文件路径</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task ApplyToGraphAsync(DependencyGraph graph, string logPath)
        {
            var entries = await ParseLogAsync(logPath);
            var methodNames = entries.Select(e => e.MethodFullName).Distinct();
            var mapping = await MapMethodsToSymbolsAsync(methodNames);

            foreach (var kvp in mapping)
            {
                foreach (var symbol in kvp.Value)
                {
                    // 获取或添加节点，并设置动态可达标记
                    var node = graph.GetOrAddNode(symbol);
                    node.IsDynamicallyReached = true;
                }
            }
        }

        /// <summary>
        /// 生成动态分析报告
        /// </summary>
        /// <param name="entries">调用条目列表</param>
        /// <param name="mapping">方法名到符号的映射</param>
        /// <returns>动态分析报告</returns>
        public DynamicAnalysisReport GenerateReport(List<CallEntry> entries, Dictionary<string, List<IMethodSymbol>> mapping)
        {
            var report = new DynamicAnalysisReport();
            report.TotalCalls = entries.Count;

            var uniqueCalledNames = entries.Select(e => e.MethodFullName).Distinct().ToList();
            report.UniqueMethodsCalled = uniqueCalledNames.Count;

            // 统计每个方法的调用次数
            report.CallCounts = entries.GroupBy(e => e.MethodFullName)
                .ToDictionary(g => g.Key, g => g.Count());

            // 识别无法映射到源码的符号
            report.UnmappedMethods = uniqueCalledNames
                .Where(name => !mapping.ContainsKey(name))
                .ToList();

            // 获取所有成功映射的符号
            report.MappedSymbols = mapping.Values.SelectMany(s => s)
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<IMethodSymbol>()
                .ToList();

            return report;
        }

        /// <summary>
        /// 与静态分析结果对比，找出潜在死代码
        /// </summary>
        /// <param name="allStaticMethods">静态分析识别出的所有方法</param>
        /// <param name="dynamicMethods">动态日志中出现的所有方法</param>
        /// <returns>潜在死代码方法列表</returns>
        public List<IMethodSymbol> FindPotentialDeadCode(IEnumerable<IMethodSymbol> allStaticMethods, IEnumerable<IMethodSymbol> dynamicMethods)
        {
            var dynamicSet = new HashSet<IMethodSymbol>(dynamicMethods, SymbolEqualityComparer.Default);
            // 差集运算：Static - Dynamic = DeadCode
            return allStaticMethods
                .Where(m => !dynamicSet.Contains(m))
                .ToList();
        }
    }

    /// <summary>
    /// 表示单次方法调用。
    /// </summary>
    public class CallEntry
    {
        // 调用时间戳。
        public string Timestamp { get; set; } = string.Empty;
        // 调用的方法全名。
        public string MethodFullName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 动态分析结果报告。
    /// </summary>
    public class DynamicAnalysisReport
    {
        // 总调用次数。
        public int TotalCalls { get; set; }
        // 唯一被调用的方法数量。
        public int UniqueMethodsCalled { get; set; }
        // 每个方法的调用次数统计字典。
        public Dictionary<string, int> CallCounts { get; set; } = new();
        // 无法映射到源代码的方法名称列表。
        public List<string> UnmappedMethods { get; set; } = new();
        // 成功映射到源代码的方法符号列表。
        public List<IMethodSymbol> MappedSymbols { get; set; } = new();
    }
}
