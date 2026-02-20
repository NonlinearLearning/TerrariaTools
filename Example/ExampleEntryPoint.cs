/**
 * 该文件是程序的入口点。
 * 负责调用已封装的重构逻辑。
 */

using System;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions;
using Example;

namespace TerrariaTools
{
    /// <summary>
    /// 示例程序入口类 (仅用于演示或特定测试场景)
    /// </summary>
    class ExampleEntryPoint
    {
        public static async Task Run(string[] args)
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("==================================================");
                Console.WriteLine("   TerrariaTools 示例运行器");
                Console.WriteLine("==================================================");
                Console.WriteLine("请选择要运行的示例:");
                Console.WriteLine("1. 表达式重写示例 (Expression Rewrite)");
                Console.WriteLine("2. 解决方案重构示例 (Solution Refactoring)");
                Console.WriteLine("3. 依赖分析示例 (Dependency Analysis)");
                Console.WriteLine("4. 自定义重写器示例 (Custom Rewriter)");
                Console.WriteLine("5. 语义重构工作流 (Semantic Refactoring)");
                Console.WriteLine("6. 差分测试工作流 (Differential Testing)");
                Console.WriteLine("7. 异步代码重构示例 (Async Refactoring) [New]");
                Console.WriteLine("8. 高级分析示例 (Advanced Analysis) [New]");
                Console.WriteLine("9. 交互式重构示例 (Interactive Refactoring) [New]");
                Console.WriteLine("10. 数据流分析示例 (Data Flow Analysis) [New]");
                Console.WriteLine("11. Switch 流程分析示例 (Switch Flow Analysis) [New]");
                Console.WriteLine("0. 退出");
                Console.WriteLine("==================================================");
                Console.Write("请输入选项 (0-11): ");

                string? input = Console.ReadLine();
                if (input == "0") break;

                Console.WriteLine();
                try
                {
                    switch (input)
                    {
                        case "1":
                            Console.WriteLine("正在运行: 表达式重写示例...");
                            new Example.ExpressionRewriteExample().Run();
                            break;
                        case "2":
                            Console.WriteLine("正在运行: 解决方案重构示例...");
                            string slnPath2 = GetSolutionPathFromUser();
                            if (!string.IsNullOrEmpty(slnPath2))
                            {
                                await new Example.SolutionRefactoringExample().RunAsync(slnPath2);
                            }
                            break;
                        case "3":
                            Console.WriteLine("正在运行: 依赖分析示例...");
                            string slnPath3 = GetSolutionPathFromUser();
                            if (!string.IsNullOrEmpty(slnPath3))
                            {
                                await new Example.DependencyAnalysisExample().RunAsync(slnPath3, "TerrariaTools.Analysis.DependencyGraph", "FindSCCs");
                            }
                            break;
                        case "4":
                            Console.WriteLine("正在运行: 自定义重写器示例...");
                            new Example.CustomRewriterExample().Run();
                            break;
                        case "5":
                            Console.WriteLine("正在运行: 语义重构工作流...");
                            string slnPath5 = GetSolutionPathFromUser();
                            if (!string.IsNullOrEmpty(slnPath5))
                            {
                                await new Example.SemanticRefactoringWorkflow().RunAsync(slnPath5);
                            }
                            break;
                        case "6":
                            Console.WriteLine("正在运行: 差分测试工作流...");
                            new Example.DifferentialTestingWorkflow().Run();
                            break;
                        case "7":
                            Console.WriteLine("正在运行: 异步代码重构示例...");
                            new Example.AsyncRefactoringExample().Run();
                            break;
                        case "8":
                            Console.WriteLine("正在运行: 高级分析示例...");
                            string slnPath8 = GetSolutionPathFromUser();
                            if (!string.IsNullOrEmpty(slnPath8))
                            {
                                await new Example.AdvancedAnalysisExample().RunAsync(slnPath8);
                            }
                            break;
                        case "9":
                            Console.WriteLine("正在运行: 交互式重构示例...");
                            new Example.InteractiveRefactoringExample().Run();
                            break;
                        case "10":
                            Console.WriteLine("正在运行: 数据流分析示例...");
                            // 提示用户输入特定路径，或者使用默认的
                            Console.Write("请输入包含 Terraria.MessageBuffer 的项目/解决方案路径: ");
                            string? dfPath = Console.ReadLine();
                            if (string.IsNullOrWhiteSpace(dfPath))
                            {
                                // 尝试智能推断：如果用户之前提到的路径存在
                                string userHintPath = @"D:\lodes\TR\Backup\New1.27\1.45\TR\Terraria\Terraria.csproj";
                                if (System.IO.File.Exists(userHintPath))
                                {
                                    dfPath = userHintPath;
                                    Console.WriteLine($"[自动检测] 使用路径: {dfPath}");
                                }
                                else
                                {
                                    dfPath = GetSolutionPathFromUser(); // 回退到通用逻辑
                                }
                            }

                            if (!string.IsNullOrEmpty(dfPath))
                            {
                                await new Example.DataFlowAnalysisExample().RunAsync(dfPath);
                            }
                            break;
                        case "11":
                            Console.WriteLine("正在运行: Switch 流程分析示例...");
                            Console.Write("请输入包含 Terraria.MessageBuffer 的项目路径: ");
                            string? sfPath = Console.ReadLine();
                            if (string.IsNullOrWhiteSpace(sfPath))
                            {
                                string defaultPath = @"D:\lodes\TR\Backup\New1.27\1.45\TR\Terraria\Terraria.csproj";
                                if (System.IO.File.Exists(defaultPath)) sfPath = defaultPath;
                                else sfPath = GetSolutionPathFromUser();
                            }
                            if (!string.IsNullOrEmpty(sfPath))
                            {
                                await new Example.SwitchFlowAnalysisExample().RunAsync(sfPath);
                            }
                            break;
                        default:
                            Console.WriteLine("无效的选项，请重试。");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[错误] 示例运行失败: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                Console.WriteLine("\n按任意键继续...");
                Console.ReadKey();
            }
        }
        private static string GetSolutionPathFromUser()
        {
            Console.Write("请输入解决方案路径 (留空使用默认): ");
            string? slnPath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(slnPath))
            {
                // 默认路径：优先查找当前目录下的 .slnx 或 .sln
                var files = System.IO.Directory.GetFiles(System.Environment.CurrentDirectory, "*.slnx");
                if (files.Length == 0)
                {
                    files = System.IO.Directory.GetFiles(System.Environment.CurrentDirectory, "*.sln");
                }

                if (files.Length > 0)
                {
                    slnPath = files[0];
                    Console.WriteLine($"[提示] 使用默认解决方案: {slnPath}");
                }
                else
                {
                    // 硬编码备用路径 (仅开发环境)
                    string devPath = @"d:\ProjectItem\SourceCode\Net\TerrariaTools\TerrariaTools.slnx";
                    if (System.IO.File.Exists(devPath))
                    {
                        slnPath = devPath;
                        Console.WriteLine($"[提示] 使用开发环境默认路径: {slnPath}");
                    }
                    else
                    {
                        Console.WriteLine("[警告] 未找到默认解决方案文件，请手动输入。");
                        return "";
                    }
                }
            }

            if (!System.IO.File.Exists(slnPath))
            {
                Console.WriteLine($"[错误] 文件不存在: {slnPath}");
                return "";
            }

            return slnPath;
        }
    }
}
