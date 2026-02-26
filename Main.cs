/**
 * 该文件是程序的入口点。
 * 负责调用已封装的重构逻辑。
 */

using System;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using TerrariaTools.RewriteCodeExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TerrariaTools.Services;
using TerrariaTools.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TerrariaTools
{
    /// <summary>
    /// 程序主入口类
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. 注册 MSBuild 实例 (必须在任何 MSBuildWorkspace 创建之前)
            MSBuildLocator.RegisterDefaults();

            var totalStartTime = DateTime.Now;

            try
            {
                // 2. 加载配置
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                IConfiguration configuration = builder.Build();

                // 3. 配置依赖注入容器
                var services = new ServiceCollection();

                // 注册配置
                services.Configure<RefactoringSettings>(configuration.GetSection("Refactoring"));

                // 注册核心服务
                services.AddSingleton<IWorkspaceLoader, WorkspaceLoader>();
                services.AddTransient<ToolDiscoveryService>();
                services.AddTransient<ExampleEntryPoint>();

                // 注册业务逻辑管理器
                services.AddTransient<SolutionRefactoringManager>();

                // 自动注册所有实现了 ITool 接口的工具
                var interfaceType = typeof(ITool);
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                
                // 确保包含当前程序集
                var currentAssembly = Assembly.GetExecutingAssembly();
                if (!assemblies.Contains(currentAssembly))
                {
                    assemblies = assemblies.Append(currentAssembly).ToArray();
                }

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        var toolTypes = assembly.GetTypes()
                            .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                        foreach (var type in toolTypes)
                        {
                            services.AddTransient(typeof(ITool), type);
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // 忽略加载失败的程序集
                        continue;
                    }
                }

                var serviceProvider = services.BuildServiceProvider();

                Console.WriteLine("==================================================");
                Console.WriteLine($"[信息] 启动重构流程: {totalStartTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine("==================================================");

                // 4. 运行应用程序
                var app = serviceProvider.GetRequiredService<ExampleEntryPoint>();
                await app.RunAsync(args);

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
