using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Services;

namespace Example
{
    public class SwitchFlowAnalysisExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public SwitchFlowAnalysisExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "Switch 流程分析";
        public string Description => "分析 MessageBuffer.GetData 方法中的 switch-case 逻辑，输出变量引用和函数调用。";

        public async Task RunAsync(string? path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                path = _settings.Value.DefaultSolutionPath;

                if (string.IsNullOrEmpty(path))
                {
                    Console.WriteLine("请输入项目或解决方案路径:");
                    path = Console.ReadLine();
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("路径无效。");
                return;
            }

            Console.WriteLine($"正在加载: {path}...");
            var compilation = await _loader.LoadTerrariaProjectAsync(path);

            if (compilation == null)
            {
                Console.WriteLine("无法获取编译信息。");
                return;
            }

            Console.WriteLine("正在分析 MessageBuffer.GetData 方法...");

            // 获取语义模型
            var analyzer = new TerrariaTools.Analysis.SwitchFlowAnalyzer(compilation);
            var result = await analyzer.AnalyzeAsync("Terraria.MessageBuffer", "GetData");

            if (result.Cases.Count == 0)
            {
                 Console.WriteLine("未找到 Switch 语句或分析失败。");
                 return;
            }

            Console.WriteLine($"找到 switch 语句，开始分析 {result.Cases.Count} 个 case 分支...");
            Console.WriteLine();

            foreach (var caseResult in result.Cases)
            {
                Console.WriteLine($"Case {caseResult.Labels}: {caseResult.Description}");
                Console.WriteLine(new string('-', 50));

                // 1. 变量引用
                Console.WriteLine("  [变量引用]:");
                if (caseResult.VariableReferences.Any())
                {
                    foreach (var v in caseResult.VariableReferences)
                    {
                        Console.WriteLine($"    - {v}");
                    }
                }
                else
                {
                    Console.WriteLine("    (无)");
                }

                // 2. 函数调用
                Console.WriteLine("  [函数调用]:");
                if (caseResult.FunctionCalls.Any())
                {
                    foreach (var inv in caseResult.FunctionCalls)
                    {
                        Console.WriteLine($"    - {inv}");
                    }
                }
                else
                {
                    Console.WriteLine("    (无)");
                }

                Console.WriteLine();
            }
        }
    }
}
