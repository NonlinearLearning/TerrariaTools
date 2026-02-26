using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using TerrariaTools.Analysis;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 演示如何使用 CodeDependencyAnalyzer 执行递归依赖分析。
    /// 对应“重写思路.txt”中的第二阶段核心逻辑。
    /// </summary>
    public class DependencyAnalysisExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public DependencyAnalysisExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "依赖分析";
        public string Description => "执行代码依赖分析，查找循环依赖和调用链。";

        public async Task RunAsync(string? solutionPath = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
            {
                solutionPath = _settings.Value.DefaultSolutionPath;

                if (string.IsNullOrEmpty(solutionPath))
                {
                    Console.WriteLine("请输入解决方案路径 (直接回车使用默认):");
                    solutionPath = Console.ReadLine();
                }
            }

            Console.WriteLine("请输入目标类型全名 (默认: TerrariaTools.Analysis.DependencyGraph):");
            string? targetTypeName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(targetTypeName)) targetTypeName = "TerrariaTools.Analysis.DependencyGraph";

            Console.WriteLine("请输入目标方法名 (默认: FindSCCs):");
            string? targetMethodName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(targetMethodName)) targetMethodName = "FindSCCs";

            // 1. 加载解决方案
            var solution = await _loader.LoadSolutionAsync(solutionPath ?? string.Empty);

            if (solution == null)
            {
                Console.WriteLine("加载解决方案失败。");
                return;
            }

            // 2. 执行高级分析
            var analyzer = new AdvancedCodeAnalyzer(solution);
            Console.WriteLine($"[分析] 开始分析 {targetTypeName}.{targetMethodName} 的依赖关系...");
            
            var result = await analyzer.AnalyzeRecursiveDependenciesAsync(targetTypeName, targetMethodName);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine($"[错误] {result.Error}");
                return;
            }

            // 3. 输出结果
            Console.WriteLine($"[统计] 节点总数: {result.NodeCount}");
            Console.WriteLine($"[统计] 边总数: {result.EdgeCount}");

            if (result.HasCycles)
            {
                Console.WriteLine($"[结果] 发现 {result.StrongConnectedComponents.Count} 个循环依赖环。");
                foreach (var cycle in result.StrongConnectedComponents)
                {
                    Console.WriteLine("  - 环: " + string.Join(" -> ", cycle));
                }
                Console.WriteLine("[警告] 图中存在环，无法进行全局拓扑排序，需按 SCC 块处理。");
            }
            else
            {
                Console.WriteLine($"[结果] 拓扑排序完成，共 {result.TopologicalSort.Count} 个独立节点。");
            }

            // 4.3 后向切片示例 (查找依赖于某个特定字段的所有代码)
            // var slice = analyzer.GetBackwardSlice(someFieldSymbol);
        }
    }
}
