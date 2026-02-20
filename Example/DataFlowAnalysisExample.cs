using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Example
{
    /// <summary>
    /// 演示如何分析特定方法（如 MessageBuffer.GetData）的数据流，
    /// 识别并分类其使用的外部变量（字段/属性）和局部变量。
    /// </summary>
    public class DataFlowAnalysisExample
    {
        public async Task RunAsync(string projectOrSolutionPath)
        {
            Console.WriteLine($"[分析] 正在加载: {projectOrSolutionPath}");

            using var workspace = MSBuildWorkspace.Create();
            // 忽略加载错误，仅关注能加载的部分
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = true;

            Solution solution;
            if (projectOrSolutionPath.EndsWith(".sln"))
            {
                solution = await workspace.OpenSolutionAsync(projectOrSolutionPath);
            }
            else
            {
                var project = await workspace.OpenProjectAsync(projectOrSolutionPath);
                solution = project.Solution;
            }

            // 查找 MessageBuffer 类
            Console.WriteLine("[分析] 正在查找 Terraria.MessageBuffer 类型...");
            var compilation = await solution.Projects.First().GetCompilationAsync();
            if (compilation == null)
            {
                Console.WriteLine("[错误] 无法获取编译单元。");
                return;
            }

            var messageBufferType = compilation.GetTypeByMetadataName("Terraria.MessageBuffer");
            if (messageBufferType == null)
            {
                // 尝试不带命名空间查找
                Console.WriteLine("[提示] 未找到全名 Terraria.MessageBuffer，尝试模糊搜索...");
                foreach (var proj in solution.Projects)
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

            var methodDeclaration = (MethodDeclarationSyntax)await syntaxReference.GetSyntaxAsync();
            var semanticModel = compilation!.GetSemanticModel(methodDeclaration.SyntaxTree);

            // 开始分析
            AnalyzeVariables(methodDeclaration, semanticModel);
        }

        private void AnalyzeVariables(MethodDeclarationSyntax method, SemanticModel model)
        {
            var externalVariables = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var localVariables = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var parameters = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            // 1. 收集参数
            foreach (var param in method.ParameterList.Parameters)
            {
                var symbol = model.GetDeclaredSymbol(param);
                if (symbol != null) parameters.Add(symbol);
            }

            // 2. 遍历方法体中的所有标识符
            var identifiers = method.DescendantNodes().OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                var symbol = model.GetSymbolInfo(id).Symbol;
                if (symbol == null) continue;

                // 排除类型引用、命名空间引用、方法组等
                if (symbol is ITypeSymbol || symbol is INamespaceSymbol || symbol is IMethodSymbol)
                    continue;

                switch (symbol.Kind)
                {
                    case SymbolKind.Field:
                    case SymbolKind.Property:
                    case SymbolKind.Event:
                        // 外部变量 (类成员)
                        externalVariables.Add(symbol);
                        break;

                    case SymbolKind.Local:
                        // 局部变量
                        localVariables.Add(symbol);
                        break;

                    case SymbolKind.Parameter:
                        // 参数引用
                        parameters.Add(symbol);
                        break;
                }
            }

            // 3. 打印结果
            Console.WriteLine("\n=== 分析结果: GetData ===");
            
            // 汇总列表，使用 HashSet 去重
            Console.WriteLine("\n[变量全表 (按名称排序)]");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine("{0,-40} | {1,-15} | {2,-20}", "变量名", "类型", "定义来源");
            Console.WriteLine(new string('-', 80));

            // 使用 Dictionary<string, (string Name, string Kind, string Source)> 以变量名为键进行去重
            var symbolDict = new Dictionary<string, (string Name, string Kind, string Source)>();

            foreach (var p in parameters)
            {
                if (!symbolDict.ContainsKey(p.Name))
                    symbolDict[p.Name] = (p.Name, "Parameter", "Parameter");
            }
            
            foreach (var l in localVariables)
            {
                // 如果已存在（如参数），则不覆盖，或者根据优先级决定
                if (!symbolDict.ContainsKey(l.Name))
                {
                    var typeName = (l as ILocalSymbol)?.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "Unknown";
                    symbolDict[l.Name] = (l.Name, "Local", typeName);
                }
            }

            foreach (var e in externalVariables)
            {
                var container = e.ContainingType.Name;
                var displayName = $"{container}.{e.Name}";
                var kind = e.Kind == SymbolKind.Field ? "Field" : "Property";
                
                if (!symbolDict.ContainsKey(displayName))
                {
                    symbolDict[displayName] = (displayName, "External", kind);
                }
            }

            // 输出
            foreach (var item in symbolDict.Values.OrderBy(x => x.Name))
            {
                // 截断过长的名称以保持对齐
                string name = item.Name.Length > 38 ? item.Name.Substring(0, 35) + "..." : item.Name;
                Console.WriteLine("{0,-40} | {1,-15} | {2,-20}", name, item.Kind, item.Source);
            }
            Console.WriteLine(new string('-', 80));

            // 详细分类统计 (可选保留)
            Console.WriteLine($"\n[统计] 参数: {parameters.Count}, 局部变量: {localVariables.Count}, 外部变量: {externalVariables.Count}");

            // 4. 数据流分析 (Roslyn DataFlowAnalysis)
            if (method.Body != null)
            {
                Console.WriteLine("\n[Roslyn 数据流分析概览]");
                var dataFlow = model.AnalyzeDataFlow(method.Body);
                
                if (dataFlow.Succeeded)
                {
                    Console.WriteLine($"  - 读取的变量数 (ReadInside): {dataFlow.ReadInside.Length}");
                    Console.WriteLine($"  - 写入的变量数 (WrittenInside): {dataFlow.WrittenInside.Length}");
                    Console.WriteLine($"  - 外部捕获的变量 (Captured): {dataFlow.Captured.Length}");
                    
                    Console.WriteLine("\n  [被捕获的外部变量 (Captured)]:");
                    foreach(var captured in dataFlow.Captured.Select(s => s.Name).Distinct())
                    {
                        Console.WriteLine($"    - {captured}");
                    }
                    
                    Console.WriteLine("\n  [未声明即使用的变量 (DataFlowIn - 可能是外部输入)]:");
                    foreach(var input in dataFlow.DataFlowsIn.Select(s => s.Name).Distinct())
                    {
                        Console.WriteLine($"    - {input}");
                    }
                }
                else
                {
                    Console.WriteLine("  - 数据流分析失败 (可能是代码存在编译错误)");
                }
            }
        }
    }
}
