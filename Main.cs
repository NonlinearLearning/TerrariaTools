/**
 * 该文件是程序的入口点。
 * 负责调用已封装的重构逻辑。
 */

using System;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.DynamicAnalysis;

namespace TerrariaTools
{
    /// <summary>
    /// 程序主入口类
    /// </summary>
    class Program
    {
       public static async Task Main(string[] args)
        {
            // 设置控制台编码为 UTF-8 以支持中文显示
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            var totalStartTime = DateTime.Now;
            try
            {
                // 硬编码路径仅用于当前开发阶段
                string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
                var loader = new Load();

                Console.WriteLine("==================================================");
                Console.WriteLine($"[信息] 启动调用链注入流程: {totalStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine("==================================================");

                // 1. 执行全量调用链注入 (Roslyn) - 如果已经注入过可以注释掉
                // var injector = new CallChainInjector(solutionPath, loader);
                // await injector.ExecuteInjectionAsync();

                // 2. 执行代码裁剪 (直接使用日志中的函数列表)
                string logPath = @"D:\ProjectItem\SourceCode\Net\TerrariaTools\call_chain.log";
                var pruner = new CodePruner(solutionPath, loader);
                var validFunctions = CodePruner.LoadFunctionsFromLog(logPath);
                await pruner.ExecutePruningAsync(validFunctions);

                var totalElapsed = DateTime.Now - totalStartTime;
                Console.WriteLine("\n==================================================");
                Console.WriteLine($"[完成] 调用链注入任务已结束。");
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
