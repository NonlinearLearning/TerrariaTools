using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using TerrariaTools.Analysis;

namespace Example
{
    /// <summary>
    /// 演示如何使用 CodeDependencyAnalyzer 执行递归依赖分析。
    /// 对应“重写思路.txt”中的第二阶段核心逻辑。
    /// </summary>
    public class DependencyAnalysisExample
    {
        public async Task RunAsync(string solutionPath, string targetMethodName)
        {
            // 1. 加载解决方案
            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            // 2. 查找种子符号 (例如 MessageBuffer.GetData)
            var project = solution.Projects.First();
            var compilation = await project.GetCompilationAsync();
            var targetType = compilation?.GetTypeByMetadataName("Terraria.MessageBuffer");
            var seedSymbol = targetType?.GetMembers(targetMethodName).FirstOrDefault();

            if (seedSymbol == null)
            {
                Console.WriteLine($"未找到目标符号: {targetMethodName}");
                return;
            }

            // 3. 执行递归分析
            var analyzer = new CodeDependencyAnalyzer(solution);
            Console.WriteLine($"[分析] 开始从 {seedSymbol.ToDisplayString()} 进行递归依赖发现...");
            await analyzer.AnalyzeRecursiveAsync(seedSymbol);

            // 4. 应用图算法
            var graph = analyzer.Graph;
            
            // 4.1 识别循环依赖 (SCC)
            var sccs = graph.FindSCCs();
            var cycles = sccs.Where(s => s.Count > 1).ToList();
            Console.WriteLine($"[结果] 发现 {cycles.Count} 个循环依赖环。");

            // 4.2 拓扑排序 (处理 DAG 部分)
            try 
            {
                var sorted = graph.TopologicalSort();
                Console.WriteLine($"[结果] 拓扑排序完成，共 {sorted.Count} 个独立节点。");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("[警告] 图中存在环，无法进行全局拓扑排序，需按 SCC 块处理。");
            }

            // 4.3 后向切片示例 (查找依赖于某个特定字段的所有代码)
            // var slice = analyzer.GetBackwardSlice(someFieldSymbol);
        }
    }
}
