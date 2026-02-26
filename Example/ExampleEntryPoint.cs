/**
 * 该文件是程序的入口点。
 * 负责调用已封装的重构逻辑。
 */

using System;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Services;
using Example;
using System.Linq;

namespace TerrariaTools
{
    /// <summary>
    /// 示例程序入口类 (仅用于演示或特定测试场景)
    /// </summary>
    public class ExampleEntryPoint
    {
        private readonly ToolDiscoveryService _toolService;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public ExampleEntryPoint(ToolDiscoveryService toolService, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _toolService = toolService;
            _settings = settings;
        }

        public async Task RunAsync(string[] args)
        {
            var tools = _toolService.GetAllTools();

            // 如果提供了命令行参数，直接运行对应工具
            if (args != null && args.Length > 0 && int.TryParse(args[0], out int toolIndex))
            {
                if (toolIndex >= 1 && toolIndex <= tools.Count)
                {
                    var tool = tools[toolIndex - 1];
                    Console.WriteLine($"自动启动: {tool.Name}...");
                    string? defaultPath = _settings.Value.DefaultSolutionPath;

                    if (string.IsNullOrEmpty(defaultPath) || !System.IO.File.Exists(defaultPath))
                    {
                        if (!string.IsNullOrEmpty(defaultPath))
                        {
                            Console.WriteLine($"警告: 默认路径 {defaultPath} 不存在。");
                        }
                        defaultPath = null;
                    }

                    await tool.RunAsync(defaultPath);
                    return;
                }
            }

            while (true)
            {
                // Console.Clear();
                Console.WriteLine("==================================================");
                Console.WriteLine("   TerrariaTools 示例运行器");
                Console.WriteLine("==================================================");
                Console.WriteLine("可用工具:");

                for (int i = 0; i < tools.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {tools[i].Name} - {tools[i].Description}");
                }

                Console.WriteLine("==================================================");
                Console.WriteLine("0. 退出");
                Console.WriteLine("==================================================");
                Console.Write($"请输入选项 (0-{tools.Count}): ");

                string? input = Console.ReadLine();
                if (input == "0") break;

                if (int.TryParse(input, out int index) && index >= 1 && index <= tools.Count)
                {
                    var tool = tools[index - 1];
                    Console.WriteLine();
                    Console.WriteLine($"正在启动: {tool.Name}...");
                    try
                    {
                        // 尝试获取硬编码路径或让工具自己提示
                        // 这里我们不再传递路径，让工具的 RunAsync 处理 null 的情况
                        string? defaultPath = _settings.Value.DefaultSolutionPath;
                        if (string.IsNullOrEmpty(defaultPath) || !System.IO.File.Exists(defaultPath)) defaultPath = null;

                        await tool.RunAsync(defaultPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"运行出错: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
                else
                {
                    Console.WriteLine("无效选项，按任意键重试...");
                }

                Console.WriteLine("\n按任意键返回菜单...");
                Console.ReadKey();
            }
        }

        static string GetSolutionPathFromUser()
        {
            Console.WriteLine("请输入解决方案路径 (直接回车使用默认路径):");
            string? path = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(path))
            {
                // return @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
                return @"D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln";
            }
            return path.Trim('"', ' ');
        }
    }
}
