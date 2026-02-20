using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System.IO;

namespace Example
{
    public class SwitchFlowAnalysisExample
    {
        public async Task RunAsync(string path)
        {
            Console.WriteLine($"正在加载: {path}...");
            using var workspace = MSBuildWorkspace.Create();

            // 跳过未识别的项目
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = true;

            Compilation? compilation = null;

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await workspace.OpenSolutionAsync(path);
                // 优先查找 Terraria 项目，如果没找到则尝试找第一个项目
                var project = solution.Projects.FirstOrDefault(p => p.Name == "Terraria" || p.Name == "TerrariaServer")
                              ?? solution.Projects.FirstOrDefault();
                if (project != null)
                {
                    Console.WriteLine($"已选择项目: {project.Name}");
                    compilation = await project.GetCompilationAsync();
                }
            }
            else
            {
                var project = await workspace.OpenProjectAsync(path);
                compilation = await project.GetCompilationAsync();
            }

            if (compilation == null)
            {
                Console.WriteLine("无法获取编译信息。");
                return;
            }

            Console.WriteLine("正在查找 MessageBuffer.GetData 方法...");
            var messageBufferType = compilation.GetTypeByMetadataName("Terraria.MessageBuffer");
            if (messageBufferType == null)
            {
                Console.WriteLine("未找到 Terraria.MessageBuffer 类。");
                return;
            }

            var getDataMethodSymbol = messageBufferType.GetMembers("GetData").OfType<IMethodSymbol>().FirstOrDefault();
            if (getDataMethodSymbol == null)
            {
                Console.WriteLine("未找到 GetData 方法。");
                return;
            }

            var syntaxReference = getDataMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null)
            {
                Console.WriteLine("无法获取方法源码。");
                return;
            }

            // 构建 MessageID 映射表
            var messageIdMap = new Dictionary<int, string>();
            var messageIdType = compilation.GetTypeByMetadataName("Terraria.ID.MessageID");
            if (messageIdType != null)
            {
                foreach (var field in messageIdType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (field.IsConst && field.HasConstantValue)
                    {
                        if (field.ConstantValue is int val)
                        {
                            if (!messageIdMap.ContainsKey(val))
                            {
                                messageIdMap[val] = field.Name;
                            }
                        }
                        else if (field.ConstantValue is byte valByte)
                        {
                             if (!messageIdMap.ContainsKey((int)valByte))
                            {
                                messageIdMap[(int)valByte] = field.Name;
                            }
                        }
                    }
                }
            }

            var methodDeclaration = (MethodDeclarationSyntax)await syntaxReference.GetSyntaxAsync();
            var switchStatement = methodDeclaration.DescendantNodes().OfType<SwitchStatementSyntax>().FirstOrDefault();

            if (switchStatement == null)
            {
                Console.WriteLine("GetData 方法中未找到 switch 语句。");
                return;
            }

            // 获取语义模型
            var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);

            Console.WriteLine($"找到 switch 语句，开始分析 {switchStatement.Sections.Count} 个 case 分支...");
            Console.WriteLine();

            foreach (var section in switchStatement.Sections)
            {
                var labels = string.Join(", ", section.Labels.OfType<CaseSwitchLabelSyntax>().Select(l => l.Value.ToString()));
                if (string.IsNullOrEmpty(labels)) labels = "default";

                // 尝试从 Case 标签的前导注释中提取描述
                var description = GetDescriptionFromTrivia(section);

                // 尝试从 Case 值匹配 MessageID 常量名
                if (string.IsNullOrEmpty(description) && section.Labels.FirstOrDefault() is CaseSwitchLabelSyntax label)
                {
                    if (int.TryParse(label.Value.ToString(), out int val))
                    {
                        if (messageIdMap.TryGetValue(val, out string? name))
                        {
                            description = $"[MessageID.{name}]";
                        }
                    }
                }

                Console.WriteLine($"Case {labels}: {description}");
                Console.WriteLine(new string('-', 50));

                // 1. 变量引用
                var variableRefs = GetVariableReferences(section, semanticModel);
                Console.WriteLine("  [变量引用]:");
                if (variableRefs.Any())
                {
                    foreach (var v in variableRefs)
                    {
                        Console.WriteLine($"    - {v}");
                    }
                }
                else
                {
                    Console.WriteLine("    (无)");
                }

                // 2. 函数调用
                var invocations = GetAllInvocations(section);
                Console.WriteLine("  [函数调用]:");
                if (invocations.Any())
                {
                    foreach (var inv in invocations)
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

        private List<string> GetVariableReferences(SwitchSectionSyntax section, SemanticModel model)
        {
            var variables = new HashSet<string>();
            var identifiers = section.DescendantNodes().OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                // 必须使用 GetSymbolInfo 来获取符号信息
                var symbolInfo = model.GetSymbolInfo(id);
                var symbol = symbolInfo.Symbol;

                if (symbol != null)
                {
                    switch (symbol.Kind)
                    {
                        case SymbolKind.Local:
                            variables.Add($"{symbol.Name} (Local)");
                            break;
                        case SymbolKind.Parameter:
                            variables.Add($"{symbol.Name} (Parameter)");
                            break;
                        case SymbolKind.Field:
                            variables.Add($"{symbol.ContainingType.Name}.{symbol.Name} (Field)");
                            break;
                        case SymbolKind.Property:
                            variables.Add($"{symbol.ContainingType.Name}.{symbol.Name} (Property)");
                            break;
                    }
                }
            }
            return variables.OrderBy(v => v).ToList();
        }

        private List<string> GetAllInvocations(SwitchSectionSyntax section)
        {
            var invocations = new HashSet<string>();
            var nodes = section.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var node in nodes)
            {
                var expr = node.Expression.ToString();
                invocations.Add(expr);
            }
            return invocations.OrderBy(i => i).ToList();
        }


        private string GetDescriptionFromTrivia(SwitchSectionSyntax section)
        {
            var comments = new List<string>();

            void AddComments(SyntaxTriviaList triviaList)
            {
                foreach (var trivia in triviaList)
                {
                    if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                    {
                        var text = trivia.ToString().Trim('/', '*', ' ');
                        if (!string.IsNullOrWhiteSpace(text) && !comments.Contains(text))
                        {
                            comments.Add(text);
                        }
                    }
                }
            }

            // 1. 尝试 Section 的前导 Trivia
            AddComments(section.GetLeadingTrivia());

            // 2. 尝试第一个 Label 的关键字的前导 Trivia (有时会附着在这里)
            var firstLabel = section.Labels.FirstOrDefault();
            if (firstLabel != null)
            {
                // 对于 CaseSwitchLabelSyntax (case x:)
                if (firstLabel is CaseSwitchLabelSyntax caseLabel)
                {
                     AddComments(caseLabel.Keyword.LeadingTrivia);
                }
                // 对于 DefaultSwitchLabelSyntax (default:)
                else if (firstLabel is DefaultSwitchLabelSyntax defaultLabel)
                {
                    AddComments(defaultLabel.Keyword.LeadingTrivia);
                }
            }

            return string.Join("; ", comments);
        }

        private string GetKeyInvocations(SwitchSectionSyntax section)
        {
            var invocations = section.DescendantNodes()
                                     .OfType<InvocationExpressionSyntax>()
                                     .Select(inv =>
                                     {
                                         var method = inv.Expression.ToString();
                                         // 简化显示，只显示方法名或类.方法名
                                         return method;
                                     })
                                     .Distinct()
                                     .Where(m => !m.StartsWith("global::")) // 过滤掉一些冗长的 global 前缀
                                     .ToList();

            // 过滤一些常见的非关键调用，或者只保留 NetMessage 相关
            var keyInvocations = invocations.Where(m => m.Contains("NetMessage") || m.Contains("BootPlayer") || m.Contains("TrySendData")).Take(3).ToList();

            if (keyInvocations.Count == 0 && invocations.Count > 0)
            {
                return invocations.First() + (invocations.Count > 1 ? "..." : "");
            }

            return string.Join(", ", keyInvocations) + (invocations.Count > keyInvocations.Count ? "..." : "");
        }
    }
}
