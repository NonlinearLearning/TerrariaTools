using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TerrariaTools.Analysis;
using TerrariaTools.Services;

namespace Example
{
    /// <summary>
    /// 演示如何结合运行时日志 (call_chain.log) 进行依赖分析。
    /// 实现了“读取call_chain进行依赖分析”的需求。
    /// </summary>
    public class CallChainAnalysisExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public CallChainAnalysisExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "动态调用链分析";
        public string Description => "分析log中的运行时轨迹，识别冗余代码并验证静态分析。";

        public async Task RunAsync(string? solutionPath = null)
        {
            // 1. 确定路径
            if (string.IsNullOrEmpty(solutionPath))
            {
                solutionPath = _settings.Value.DefaultSolutionPath;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    Console.WriteLine("请输入解决方案路径:");
                    solutionPath = Console.ReadLine();
                }
            }

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "call_chain.log");
            if (!File.Exists(logPath))
            {
                // 尝试向上级目录查找 (开发环境下)
                var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "call_chain.log")))
                {
                    currentDir = currentDir.Parent;
                }

                if (currentDir != null)
                {
                    logPath = Path.Combine(currentDir.FullName, "call_chain.log");
                }
                else
                {
                    Console.WriteLine("请输入 call_chain.log 的路径 (或将文件放在程序根目录):");
                    logPath = Console.ReadLine() ?? "";
                }
            }

            if (!File.Exists(logPath))
            {
                Console.WriteLine($"[错误] 找不到日志文件: {logPath}");
                return;
            }

            // 2. 加载解决方案
            Console.WriteLine($"[加载] 正在加载解决方案: {solutionPath}");
            var solution = await _loader.LoadSolutionAsync(solutionPath ?? string.Empty);
            if (solution == null)
            {
                Console.WriteLine("[错误] 解决方案加载失败。");
                return;
            }

            // 3. 执行分析
            var analyzer = new CallChainAnalyzer(solution);

            Console.WriteLine($"[分析] 正在解析日志: {Path.GetFileName(logPath)}...");
            var entries = await analyzer.ParseLogAsync(logPath);
            if (!entries.Any())
            {
                Console.WriteLine("[警告] 日志文件为空或格式不正确。");
                return;
            }

            Console.WriteLine($"[分析] 正在映射 {entries.Select(e => e.MethodFullName).Distinct().Count()} 个唯一方法到源码符号...");
            var mapping = await analyzer.MapMethodsToSymbolsAsync(entries.Select(e => e.MethodFullName));

            // 4. 生成并显示报告
            var report = analyzer.GenerateReport(entries, mapping);

            Console.WriteLine("\n" + new string('=', 40));
            Console.WriteLine("      动态调用分析报告");
            Console.WriteLine(new string('=', 40));
            Console.WriteLine($"总调用次数     : {report.TotalCalls}");
            Console.WriteLine($"唯一方法数     : {report.UniqueMethodsCalled}");
            Console.WriteLine($"成功映射符号数 : {report.MappedSymbols.Count}");
            Console.WriteLine($"无法映射方法数 : {report.UnmappedMethods.Count}");

            if (report.UnmappedMethods.Any())
            {
                Console.WriteLine("\n[无法映射的方法示例] (可能是外部库、系统库或匿名方法):");
                foreach (var method in report.UnmappedMethods.Take(5))
                {
                    Console.WriteLine($"  - {method}");
                }
            }

            Console.WriteLine("\n[调用频率 TOP 10]:");
            var topCalls = report.CallCounts.OrderByDescending(kv => kv.Value).Take(10);
            foreach (var call in topCalls)
            {
                Console.WriteLine($"  - {call.Value,-6} 次 : {call.Key}");
            }

            // 5. 结合静态分析识别死代码 (三方对比：动态 vs 全局静态 vs DedServ 静态)
            Console.WriteLine("\n" + new string('-', 40));
            Console.WriteLine("是否执行深度三方对比分析？(y/n)");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                // A. 全局静态分析
                Console.WriteLine("[分析] 1/2 正在构建全局静态调用图...");
                var staticBuilder = new FunctionBuildGraph(solution);
                await staticBuilder.BuildAsync();
                var allProjectMethods = staticBuilder.AllDeclaredMethods;

                // B. DedServ 针对性分析 (使用 FunctionBuildGraph 的路径)
                Console.WriteLine("[分析] 2/2 正在从全局图中提取 DedServ 依赖链...");
                string seedName = "Terraria.Main.DedServ";
                var seeds = await analyzer.FindMethodSymbolAsync(seedName);
                HashSet<IMethodSymbol> dedServStaticMethods = new(SymbolEqualityComparer.Default);

                if (seeds.Any())
                {
                    var seed = seeds.First();
                    // 复用全局分析的结果，查找可达方法
                    dedServStaticMethods = staticBuilder.GetReachableMethods([seed]).ToHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                }
                else
                {
                    Console.WriteLine($"[警告] 未能在项目中找到种子方法: {seedName}");
                }

                // C. 动态分析过滤 (仅保留项目内的方法)
                var dynamicInProject = report.MappedSymbols
                    .Intersect(allProjectMethods, SymbolEqualityComparer.Default)
                    .Cast<IMethodSymbol>()
                    .ToHashSet(SymbolEqualityComparer.Default);

                // D. 计算差异
                var unusedGlobal = allProjectMethods.Except(dynamicInProject, SymbolEqualityComparer.Default).Cast<IMethodSymbol>().ToList();
                var unusedDedServ = dedServStaticMethods.Except(dynamicInProject, SymbolEqualityComparer.Default).Cast<IMethodSymbol>().ToList();

                // 计算 DedServ 中被执行的方法
                var usedInDedServ = dedServStaticMethods.Intersect(dynamicInProject, SymbolEqualityComparer.Default).Cast<IMethodSymbol>().ToList();

                // 6. 显示三方对比报告
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("                三方对比分析报告");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine($"{"分析维度",-20} | {"方法总数",-10} | {"动态覆盖",-10} | {"未执行数",-10}");
                Console.WriteLine(new string('-', 60));
                Console.WriteLine($"{"1. 动态轨迹(项目内)",-20} | {dynamicInProject.Count,-10} | {"100%",-10} | {0,-10}");
                Console.WriteLine($"{"2. 全局静态分析",-20} | {allProjectMethods.Count(),-10} | {CalculatePercent(dynamicInProject.Count, allProjectMethods.Count()),-10} | {unusedGlobal.Count,-10}");
                Console.WriteLine($"{"3. DedServ 依赖链",-20} | {dedServStaticMethods.Count,-10} | {CalculatePercent(usedInDedServ.Count, dedServStaticMethods.Count),-10} | {unusedDedServ.Count,-10}");
                Console.WriteLine(new string('=', 60));

                if (unusedDedServ.Any())
                {
                    Console.WriteLine($"\n[关键发现] 在 DedServ 依赖链中，有 {unusedDedServ.Count} 个方法在运行时从未被调用。");
                    Console.WriteLine("这些方法是精简 DedServ 版本的首选重构目标。");
                }

                // 7. 写入详细日志
                string deadCodeLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dead_code_comparison.log");
                try
                {
                    var logLines = new List<string> {
                        $"三方对比报告 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"解决方案: {solutionPath}",
                        $"--------------------------------------------------",
                        $"[1] 动态执行(项目内): {dynamicInProject.Count}",
                        $"[2] 全局静态方法: {allProjectMethods.Count()} (未执行: {unusedGlobal.Count})",
                        $"[3] DedServ 依赖方法: {dedServStaticMethods.Count} (未执行: {unusedDedServ.Count})",
                        $"--------------------------------------------------",
                        $"\n=== DedServ 依赖链中未执行的方法 ==="
                    };
                    logLines.AddRange(unusedDedServ.Select(m => m.ToDisplayString()));

                    logLines.Add("\n=== 全局静态中未执行的方法 (Top 100) ===");
                    logLines.AddRange(unusedGlobal.Take(100).Select(m => m.ToDisplayString()));

                    File.WriteAllLines(deadCodeLogPath, logLines);
                    Console.WriteLine($"\n[完成] 详细的对比清单已保存至: {deadCodeLogPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[错误] 写入日志失败: {ex.Message}");
                }

                Console.WriteLine("\n提示: 动态覆盖率反映了运行时日志对静态预测路径的实际验证程度。");
            }
        }

        private string CalculatePercent(int part, int total)
        {
            if (total == 0) return "0%";
            double percent = (double)part / total * 100;
            return $"{percent:F1}%";
        }
    }
}
