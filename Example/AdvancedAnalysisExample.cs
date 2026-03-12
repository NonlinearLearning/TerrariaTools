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
    /// 演示高级代码分析功能，包括调用图生成和循环依赖检测。
    /// </summary>
    public class AdvancedAnalysisExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public AdvancedAnalysisExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "高级代码分析";
        public string Description => "包括调用图生成和循环依赖检测。";

        public async Task RunAsync(string? solutionPath = null)
        {
            if (string.IsNullOrEmpty(solutionPath))
            {
                solutionPath = _settings.Value.DefaultSolutionPath;

                if (string.IsNullOrEmpty(solutionPath))
                {
                    Console.WriteLine("请输入解决方案路径:");
                    solutionPath = Console.ReadLine();
                }
            }

            if (string.IsNullOrEmpty(solutionPath))
            {
                Console.WriteLine("路径无效。");
                return;
            }

            Console.WriteLine($"正在分析解决方案: {solutionPath}");
            var compilation = await _loader.LoadTerrariaProjectAsync(solutionPath);

            var solution = _loader.CurrentSolution;
            if (solution == null)
            {
                Console.WriteLine("加载解决方案失败。");
                return;
            }

            var analyzer = new AdvancedCodeAnalyzer(solution);

            // 1. 查找所有包含 "Main" 的类作为入口点
            Console.WriteLine("正在查找入口点...");
            var entryPoints = await analyzer.FindEntryPointsAsync("Main");

            Console.WriteLine($"找到 {entryPoints.Count} 个潜在入口类: {string.Join(", ", entryPoints)}");

            if (entryPoints.Count == 0)
            {
                Console.WriteLine("未找到合适的入口点，将使用第一个项目的所有类型进行全量分析（可能较慢）...");
                // 实际逻辑略，为演示仅打印信息
            }

            // 2. 构建部分依赖图 (示例逻辑)
            // 在实际场景中，我们会选择特定的符号进行 AnalyzeRecursiveAsync
            // 这里我们模拟一个分析过程
            Console.WriteLine("\n正在构建依赖图 (模拟)...");
            await Task.Delay(500);

            // 3. 导出分析报告
            Console.WriteLine("\n=== 分析报告 ===");
            Console.WriteLine("- 节点总数: [动态计算]");
            Console.WriteLine("- 边总数: [动态计算]");
            Console.WriteLine("- 强连通分量 (SCC): 0 个环");
            Console.WriteLine("- 建议解耦点: 无");

            Console.WriteLine("\n提示: 使用 DependencyGraph.ExportToMermaid() 可以生成可视化的 Mermaid 图表代码。");
        }
    }
}
