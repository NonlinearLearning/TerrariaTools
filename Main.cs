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
                string solutionPath = @"d:\ProjectItem\SourceCode\Net\TerrariaTools\TerrariaToolsTemp.slnx";
                var loader = new Load();

                Console.WriteLine("==================================================");
                Console.WriteLine($"[信息] 启动重构流程: {totalStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine("==================================================");

                // 运行依赖分析示例
                var example = new Example.DependencyAnalysisExample();
                // 注意：这里使用我们刚创建的临时解决方案文件
                await example.RunAsync(solutionPath, "TerrariaTools.Analysis.DependencyGraph", "FindSCCs");

                /*
                // // 1. 执行类重构逻辑（例如：删除未引用的类）
                // await ClassRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);

                // 2. 执行基于名称的方法重构逻辑（例如：处理包含 "Draw" 的方法，不区分大小写）
                Console.WriteLine("\n[信息] 正在启动基于名称的方法重构 (匹配包含 'Draw' 的方法)...");
                await NameBasedMethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader, "Draw");

                // 3. 执行常规方法重构逻辑（例如：删除未引用方法、私有化仅内部引用的方法）
                // await MethodRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);

                // 4. 执行条件表达式重构（移除 netMode == 1）
               // await ConditionRefactorer.ExecuteSolutionRefactoringAsync(solutionPath, loader);
               */

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
