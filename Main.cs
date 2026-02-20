/**
 * 该文件是程序的入口点。
 * 负责调用已封装的重构逻辑。
 */

using System;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using TerrariaTools.RewriteCodeExpressions;

namespace TerrariaTools
{
    /// <summary>
    /// 程序主入口类
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            MSBuildLocator.RegisterDefaults();
            var totalStartTime = DateTime.Now;
            try
            {
                // 硬编码路径仅用于当前开发阶段
                // string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
                // string solutionPath = @"d:\ProjectItem\SourceCode\Net\TerrariaTools\TerrariaToolsTemp.slnx";
                var loader = new Load();

                Console.WriteLine("==================================================");
                Console.WriteLine($"[信息] 启动重构流程: {totalStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine("==================================================");

                // 直接运行数据流分析示例
                string targetPath = @"D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln";

                // 如果文件不存在，回退到交互模式
                if (System.IO.File.Exists(targetPath))
                {
                    Console.WriteLine($"[自动] 检测到目标项目，直接启动分析: {targetPath}");
                    // await new Example.DataFlowAnalysisExample().RunAsync(targetPath);
                    await new Example.SwitchFlowAnalysisExample().RunAsync(targetPath);
                }
                else
                {
                    Console.WriteLine("[提示] 未找到硬编码路径，进入交互模式。");
                    await ExampleEntryPoint.Run(args);
                }

                var totalElapsed = DateTime.Now - totalStartTime;
                Console.WriteLine("\n==================================================");
                Console.WriteLine($"[完成] 所有重构任务已结束。");
                Console.WriteLine($"[统计] 总运行耗时: {totalElapsed.TotalMinutes:F1} 分钟");
                Console.WriteLine("==================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n[致命错误] 程序运行过程中发生未捕获的异常:");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
