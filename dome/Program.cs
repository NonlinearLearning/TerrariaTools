using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TerrariaTools.Configuration;
using TerrariaTools.Services;

using TerrariaTools.Rules.Dome;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Rules.Dome.Mark.StaticRules;
using TerrariaTools.Rules.Dome.Mark.ContextRules;
using System.Reflection;
using System.Linq;

namespace TerrariaTools.Dome
{
    public class DomeProgram
    {
        public static async Task Main(string[] args)
        {
            // Build the Host and DI Container
            using IHost host = CreateHostBuilder(args).Build();

            Console.WriteLine("请选择要运行的 Demo:");
            Console.WriteLine("1. SyntaxAnalysisDemo (语法分析示例)");
            Console.WriteLine("2. StatementDependencyDemo (语句依赖分析示例)");
            Console.WriteLine("3. OperationDependencyDemo (语义操作分析示例)");
            Console.WriteLine("4. ThreeLayerCollaborationDemo (三层协作深度分析示例)");
            Console.WriteLine("5. InheritanceAnalysisDemo (继承关系综合分析示例)");
            Console.WriteLine("6. MethodCallAnalysisDemo (函数调用依赖与循环检测示例)");
            Console.WriteLine("7. StatementDataFlowDemo (语句级数据流依赖分析示例)");
            Console.WriteLine("8. SymbolFinderDemo (跨文件符号引用追踪示例)");
            Console.WriteLine("9. ControlFlowAnalysisDemo (控制流分析示例)");
            Console.WriteLine("10. AdvancedAnalysisDemo (高级综合分析 - 5个案例)");
            Console.WriteLine("输入序号并按回车:");

            string input = Console.ReadLine();
            switch (input)
            {
                case "1":
                    SyntaxAnalysisDemo.Run();
                    break;
                case "2":
                    StatementDependencyDemo.Run();
                    break;
                case "3":
                    OperationDependencyDemo.Run();
                    break;
                case "4":
                    ThreeLayerCollaborationDemo.Run();
                    break;
                case "5":
                    InheritanceAnalysisDemo.Run();
                    break;
                case "6":
                    MethodCallAnalysisDemo.Run();
                    break;
                case "7":
                    StatementDataFlowDemo.Run();
                    break;
                case "8":
                    await SymbolFinderDemo.RunAsync();
                    break;
                case "9":
                    ControlFlowAnalysisDemo.Run();
                    break;
                case "10":
                    await AdvancedAnalysisDemo.RunAsync();
                    break;
                default:
                    Console.WriteLine("无效的选择。");
                    break;
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                })
                .ConfigureServices((hostContext, services) =>
                {

                });
    }
}
