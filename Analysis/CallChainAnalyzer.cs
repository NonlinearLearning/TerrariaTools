using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 解析运行时方法调用日志（如 call_chain.log）并与静态代码分析结果进行对比的工具。
    /// 用于识别实际执行路径、死代码以及验证静态依赖图。
    /// </summary>
    public class CallChainAnalyzer
    {
        private readonly Solution _solution;
        private static readonly Regex LogRegex = new Regex(@"\[(?<time>.*?)\]\s+\[ENTER\]\s+(?<method>.*)", RegexOptions.Compiled);

        public CallChainAnalyzer(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        /// <summary>
        /// 解析日志文件并返回原始调用条目列表。
        /// </summary>
        public async Task<List<CallEntry>> ParseLogAsync(string logPath)
        {
            var entries = new List<CallEntry>();
            if (!File.Exists(logPath)) return entries;

            await Task.Run(() => {
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
        /// 将日志中的方法全名映射到 Roslyn 的 IMethodSymbol。
        /// </summary>
        public async Task<Dictionary<string, List<IMethodSymbol>>> MapMethodsToSymbolsAsync(IEnumerable<string> methodFullNames)
        {
            var mapping = new Dictionary<string, List<IMethodSymbol>>();
            var uniqueNames = methodFullNames.Distinct().ToList();

            // 预加载所有项目的编译信息以提高性能
            var projectCompilations = await GetProjectCompilationsAsync();
            
            foreach (var fullName in uniqueNames)
            {
                var symbols = await FindMethodSymbolsInternalAsync(fullName, projectCompilations);
                if (symbols.Any())
                {
                    mapping[fullName] = symbols;
                }
            }
            return mapping;
        }

        /// <summary>
        /// 根据方法全名寻找符号。
        /// </summary>
        public async Task<List<IMethodSymbol>> FindMethodSymbolAsync(string methodFullName)
        {
            var projectCompilations = await GetProjectCompilationsAsync();
            return await FindMethodSymbolsInternalAsync(methodFullName, projectCompilations);
        }

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

        private async Task<List<IMethodSymbol>> FindMethodSymbolsInternalAsync(string fullName, List<(Project Project, Compilation Compilation)> projectCompilations)
        {
            var symbols = new List<IMethodSymbol>();
            
            // 格式通常为 Namespace.Type.Method
            var lastDotIndex = fullName.LastIndexOf('.');
            if (lastDotIndex <= 0) return symbols;

            var methodName = fullName.Substring(lastDotIndex + 1);
            var typeName = fullName.Substring(0, lastDotIndex);

            foreach (var pc in projectCompilations)
            {
                var type = pc.Compilation.GetTypeByMetadataName(typeName);
                if (type != null)
                {
                    var members = type.GetMembers(methodName).OfType<IMethodSymbol>();
                    symbols.AddRange(members);
                }
                else
                {
                    // 尝试模糊搜索
                    var typeParts = typeName.Split('.');
                    var typeShortName = typeParts.Last();
                    var fuzzyTypes = pc.Compilation.GetSymbolsWithName(n => n == typeShortName, SymbolFilter.Type)
                        .OfType<INamedTypeSymbol>();
                    
                    foreach (var ft in fuzzyTypes)
                    {
                        if (ft.ToDisplayString().EndsWith(typeName))
                        {
                            symbols.AddRange(ft.GetMembers(methodName).OfType<IMethodSymbol>());
                        }
                    }
                }
            }
            return symbols.Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>().ToList();
        }

        /// <summary>
        /// 生成动态分析报告。
        /// </summary>
        public DynamicAnalysisReport GenerateReport(List<CallEntry> entries, Dictionary<string, List<IMethodSymbol>> mapping)
        {
            var report = new DynamicAnalysisReport();
            report.TotalCalls = entries.Count;
            
            var uniqueCalledNames = entries.Select(e => e.MethodFullName).Distinct().ToList();
            report.UniqueMethodsCalled = uniqueCalledNames.Count;

            // 统计调用次数
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
        /// 与静态分析结果对比，找出“潜在死代码”（在静态图中存在但从未在动态日志中执行的代码）。
        /// </summary>
        public List<IMethodSymbol> FindPotentialDeadCode(IEnumerable<IMethodSymbol> allStaticMethods, IEnumerable<IMethodSymbol> dynamicMethods)
        {
            var dynamicSet = new HashSet<IMethodSymbol>(dynamicMethods, SymbolEqualityComparer.Default);
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
        public string Timestamp { get; set; } = string.Empty;
        public string MethodFullName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 动态分析结果报告。
    /// </summary>
    public class DynamicAnalysisReport
    {
        public int TotalCalls { get; set; }
        public int UniqueMethodsCalled { get; set; }
        public Dictionary<string, int> CallCounts { get; set; } = new();
        public List<string> UnmappedMethods { get; set; } = new();
        public List<IMethodSymbol> MappedSymbols { get; set; } = new();
    }
}
