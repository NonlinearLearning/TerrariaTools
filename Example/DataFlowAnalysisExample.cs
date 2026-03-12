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
    /// <summary>
    /// 演示如何分析特定方法的数据流，
    /// 识别并分类其使用的外部变量（字段/属性）和局部变量。
    /// </summary>
    public class DataFlowAnalysisExample : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> _settings;

        public DataFlowAnalysisExample(IWorkspaceLoader loader, Microsoft.Extensions.Options.IOptions<TerrariaTools.Configuration.RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public string Name => "数据流分析";
        public string Description => "分析特定方法的数据流，识别并分类其使用的变量。";

        public async Task RunAsync(string? projectOrSolutionPath = null)
        {
            if (string.IsNullOrEmpty(projectOrSolutionPath))
            {
                projectOrSolutionPath = _settings.Value.DefaultSolutionPath;

                if (string.IsNullOrEmpty(projectOrSolutionPath))
                {
                    Console.WriteLine("请输入项目或解决方案路径:");
                    projectOrSolutionPath = Console.ReadLine();
                }
            }

            if (string.IsNullOrEmpty(projectOrSolutionPath))
            {
                Console.WriteLine("路径无效。");
                return;
            }

            Console.WriteLine($"[分析] 正在加载: {projectOrSolutionPath}");

            var compilation = await _loader.LoadTerrariaProjectAsync(projectOrSolutionPath);

            if (compilation == null)
            {
                Console.WriteLine("[错误] 无法获取编译单元。");
                return;
            }

            // 查找 MessageBuffer 类
            Console.WriteLine("[分析] 正在查找 Terraria.MessageBuffer 类型...");

            var messageBufferType = compilation.GetTypeByMetadataName("Terraria.MessageBuffer");
            if (messageBufferType == null && _loader.CurrentSolution != null)
            {
                // 尝试不带命名空间查找
                Console.WriteLine("[提示] 未找到全名 Terraria.MessageBuffer，尝试模糊搜索...");
                foreach (var proj in _loader.CurrentSolution.Projects)
                {
                    compilation = await proj.GetCompilationAsync();
                    if (compilation == null) continue;

                    var symbol = compilation.GetSymbolsWithName("MessageBuffer", SymbolFilter.Type).FirstOrDefault() as INamedTypeSymbol;
                    if (symbol != null)
                    {
                        messageBufferType = symbol;
                        break;
                    }
                }
            }

            if (messageBufferType == null)
            {
                Console.WriteLine("[错误] 未能在解决方案中找到 MessageBuffer 类。");
                return;
            }

            // 查找 GetData 方法
            var getDataMethodSymbol = messageBufferType.GetMembers("GetData").OfType<IMethodSymbol>().FirstOrDefault();
            if (getDataMethodSymbol == null)
            {
                Console.WriteLine("[错误] 未在 MessageBuffer 中找到 GetData 方法。");
                return;
            }

            Console.WriteLine($"[分析] 找到目标方法: {getDataMethodSymbol.ToDisplayString()}");

            // 获取源码位置
            var syntaxReference = getDataMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null)
            {
                Console.WriteLine("[错误] 无法获取方法的源代码引用（可能是元数据引用）。");
                return;
            }

            // 使用分析器进行分析
            if (compilation == null)
            {
                Console.WriteLine("[错误] 编译对象为空。");
                return;
            }
            var analyzer = new TerrariaTools.Analysis.DataFlowAnalyzer(compilation);
            var result = await analyzer.AnalyzeAsync("Terraria.MessageBuffer", "GetData");

            // 打印结果
            Console.WriteLine("\n=== 分析结果: GetData ===");

            Console.WriteLine("\n[变量全表 (按名称排序)]");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine("{0,-40} | {1,-15} | {2,-20}", "变量名", "类型", "定义来源");
            Console.WriteLine(new string('-', 80));

            foreach (var item in result.Variables)
            {
                // 截断过长的名称以保持对齐
                string name = item.Name.Length > 38 ? item.Name.Substring(0, 35) + "..." : item.Name;
                Console.WriteLine("{0,-40} | {1,-15} | {2,-20}", name, item.Kind, item.Source);
            }
            Console.WriteLine(new string('-', 80));

            // 详细分类统计
            Console.WriteLine($"\n[统计] 参数: {result.ParameterCount}, 局部变量: {result.LocalVariableCount}, 外部变量: {result.ExternalVariableCount}");

            // Roslyn 数据流分析概览
            Console.WriteLine("\n[Roslyn 数据流分析概览]");
            if (result.Succeeded)
            {
                Console.WriteLine($"  - 读取的变量数 (ReadInside): {result.ReadInsideCount}");
                Console.WriteLine($"  - 写入的变量数 (WrittenInside): {result.WrittenInsideCount}");
                Console.WriteLine($"  - 外部捕获的变量 (Captured): {result.CapturedCount}");

                Console.WriteLine("\n  [被捕获的外部变量 (Captured)]:");
                foreach (var captured in result.CapturedVariables)
                {
                    Console.WriteLine($"    - {captured}");
                }

                Console.WriteLine("\n  [未声明即使用的变量 (DataFlowIn - 可能是外部输入)]:");
                foreach (var input in result.DataFlowInVariables)
                {
                    Console.WriteLine($"    - {input}");
                }
            }
            else
            {
                Console.WriteLine("  - 数据流分析失败 (可能是代码存在编译错误或无方法体)");
            }
        }
    }
}
