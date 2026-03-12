using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;
using TerrariaTools.Configuration;
using TerrariaTools.Services;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 综合 GetData 分析器
    /// 专门用于分析 Terraria 协议处理中的 GetData 方法，
    /// 识别数据包读取模式和潜在的异常。
    /// </summary>
    public class ComprehensiveGetDataAnalyzer : ITool
    {
        private readonly IWorkspaceLoader _loader;
        private readonly IOptions<RefactoringSettings> _settings;

        public string Name => "Comprehensive GetData Analysis";
        public string Description => "Analyze variable and call usage inside GetData method";

        public ComprehensiveGetDataAnalyzer(IWorkspaceLoader loader, IOptions<RefactoringSettings> settings)
        {
            _loader = loader;
            _settings = settings;
        }

        public async Task RunAsync(string? projectOrSolutionPath = null)
        {
            var (solutionPath, messageBufferPath) = ResolveInput(projectOrSolutionPath);

            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                Console.WriteLine("[Error] Solution/project path is not provided.");
                return;
            }

            Console.WriteLine($"[Analysis] Loading: {solutionPath}");
            var compilation = await _loader.LoadTerrariaProjectAsync(solutionPath);
            if (compilation == null)
            {
                Console.WriteLine("[Error] Failed to load compilation.");
                return;
            }

            var result = await AnalyzeInternalAsync(messageBufferPath, compilation);

            Console.WriteLine(new string('=', 100));
            Console.WriteLine(result);
            Console.WriteLine(new string('=', 100));
            Console.WriteLine("[Success] Analysis finished.");
        }

        private (string? SolutionPath, string? MessageBufferPath) ResolveInput(string? input)
        {
            var defaultSolution = _settings.Value.DefaultSolutionPath;
            var defaultMessageBuffer = _settings.Value.MessageBufferFilePath;

            if (string.IsNullOrWhiteSpace(input))
            {
                return (defaultSolution, defaultMessageBuffer);
            }

            // CLI override format: <solutionPath>|<messageBufferPath>
            var parts = input.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0], parts[1]);
            }

            // If a .cs path is provided, treat it as MessageBuffer override and keep default solution.
            if (input.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return (defaultSolution, input);
            }

            return (input, defaultMessageBuffer);
        }

        private async Task<string> AnalyzeInternalAsync(string? configuredMessageBufferPath, Compilation compilation)
        {
            var tree = TryResolveMessageBufferTree(configuredMessageBufferPath, compilation);
            if (tree == null)
            {
                return "# Error: MessageBuffer.cs not found in compilation and no valid override path provided.";
            }

            var root = await tree.GetRootAsync();
            var semanticModel = compilation.GetSemanticModel(tree);

            var method = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "GetData");

            if (method == null)
            {
                return "# Error: GetData method not found.";
            }

            var variableTable = AnalyzeVariables(method, semanticModel);
            var functionTable = AnalyzeFunctions(method, semanticModel);

            var sb = new StringBuilder();
            sb.AppendLine("[Variable Table]");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(string.Format(" {0,-42} | {1,-17} | {2}", "Name", "Category", "Type"));
            sb.AppendLine("--------------------------------------------------------------------------------");

            foreach (var v in variableTable.OrderBy(v => v.Name).ThenBy(v => v.FirstDefine))
            {
                var category = v.Category.Contains("External", StringComparison.OrdinalIgnoreCase) ? "External" : "Local";
                var typeShort = v.Type.Contains('.') ? v.Type.Split('.').Last() : v.Type;
                sb.AppendLine(string.Format(" {0,-42} | {1,-17} | {2}", v.Name, category, typeShort));
            }

            sb.AppendLine();
            sb.AppendLine("[Function Table]");
            sb.AppendLine("---------------------------------------------------------------------------------------------------------------------------");
            sb.AppendLine(string.Format(" {0,-60} | {1,-30} | {2}", "Signature", "Arguments", "AssignedTo"));
            sb.AppendLine("---------------------------------------------------------------------------------------------------------------------------");

            foreach (var f in functionTable.OrderBy(f => f.Signature).ThenBy(f => f.Location))
            {
                var sig = f.Signature.Length > 60 ? f.Signature[..57] + "..." : f.Signature;
                var args = f.Arguments.Length > 30 ? f.Arguments[..27] + "..." : f.Arguments;
                var ret = f.ReturnValueAssignment.Length > 15 ? f.ReturnValueAssignment[..12] + "..." : f.ReturnValueAssignment;

                sb.AppendLine(string.Format(" {0,-60} | {1,-30} | {2}", sig, args, ret));
            }

            return sb.ToString();
        }

        private static SyntaxTree? TryResolveMessageBufferTree(string? configuredMessageBufferPath, Compilation compilation)
        {
            var byFileName = compilation.SyntaxTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("MessageBuffer.cs", StringComparison.OrdinalIgnoreCase));

            if (byFileName != null)
            {
                return byFileName;
            }

            if (!string.IsNullOrWhiteSpace(configuredMessageBufferPath))
            {
                var byFullPath = compilation.SyntaxTrees
                    .FirstOrDefault(t => string.Equals(t.FilePath, configuredMessageBufferPath, StringComparison.OrdinalIgnoreCase));
                if (byFullPath != null)
                {
                    return byFullPath;
                }
            }

            return null;
        }

        private List<VariableRecord> AnalyzeVariables(MethodDeclarationSyntax method, SemanticModel model)
        {
            var records = new Dictionary<ISymbol, VariableRecord>(SymbolEqualityComparer.Default);
            var switchStatement = method.DescendantNodes().OfType<SwitchStatementSyntax>().FirstOrDefault();
            var allNodes = method.DescendantNodesAndSelf().ToList();

            foreach (var node in allNodes)
            {
                ISymbol? symbol = null;
                var isDefinition = false;

                if (node is ParameterSyntax ps)
                {
                    symbol = model.GetDeclaredSymbol(ps);
                    isDefinition = true;
                }
                else if (node is VariableDeclaratorSyntax vds)
                {
                    symbol = model.GetDeclaredSymbol(vds);
                    isDefinition = true;
                }
                else if (node is SingleVariableDesignationSyntax svds)
                {
                    symbol = model.GetDeclaredSymbol(svds);
                    isDefinition = true;
                }
                else if (node is IdentifierNameSyntax ins)
                {
                    var info = model.GetSymbolInfo(ins);
                    symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                }

                if (symbol == null || IsIgnoredSymbol(symbol))
                {
                    continue;
                }

                if (!records.TryGetValue(symbol, out var record))
                {
                    record = new VariableRecord
                    {
                        Name = GetSymbolName(symbol),
                        Type = GetSymbolType(symbol),
                        Category = GetCategory(symbol),
                        UsedInSwitch = false
                    };
                    records[symbol] = record;
                }

                var pos = node.GetLocation().GetMappedLineSpan();
                var posStr = $"{System.IO.Path.GetFileName(pos.Path)}:{pos.StartLinePosition.Line + 1}:{pos.StartLinePosition.Character + 1}";

                if (isDefinition || record.FirstDefine == null)
                {
                    record.FirstDefine ??= posStr;
                }

                record.LastUse = posStr;

                if (switchStatement != null && node.Ancestors().Contains(switchStatement))
                {
                    record.UsedInSwitch = true;
                }
            }

            return records.Values.ToList();
        }

        private List<FunctionRecord> AnalyzeFunctions(MethodDeclarationSyntax method, SemanticModel model)
        {
            var records = new List<FunctionRecord>();
            var switchStatement = method.DescendantNodes().OfType<SwitchStatementSyntax>().FirstOrDefault();

            foreach (var node in method.DescendantNodesAndSelf())
            {
                IMethodSymbol? methodSymbol = null;
                string? location = null;
                string? args = null;
                string? retVal = null;

                if (node is InvocationExpressionSyntax ies)
                {
                    var info = model.GetSymbolInfo(ies);
                    methodSymbol = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

                    if (methodSymbol != null)
                    {
                        location = GetLocationString(ies);
                        args = string.Join(", ", ies.ArgumentList.Arguments.Select(a => a.ToString()));
                        retVal = GetReturnValueAssignment(ies);
                    }
                }
                else if (node is ObjectCreationExpressionSyntax oces)
                {
                    var info = model.GetSymbolInfo(oces);
                    methodSymbol = info.Symbol as IMethodSymbol;
                    if (methodSymbol != null)
                    {
                        location = GetLocationString(oces);
                        args = string.Join(", ", oces.ArgumentList?.Arguments.Select(a => a.ToString()) ?? Enumerable.Empty<string>());
                        retVal = GetReturnValueAssignment(oces);
                    }
                }

                if (methodSymbol != null && location != null)
                {
                    var inSwitch = switchStatement != null && node.Ancestors().Contains(switchStatement);
                    records.Add(new FunctionRecord
                    {
                        Signature = methodSymbol.ToDisplayString(),
                        Location = location,
                        Arguments = args ?? string.Empty,
                        ReturnValueAssignment = retVal ?? "N/A",
                        InSwitch = inSwitch
                    });
                }
            }

            return records;
        }

        private static bool IsIgnoredSymbol(ISymbol symbol)
        {
            return symbol is INamespaceSymbol || symbol is ITypeSymbol || symbol is IMethodSymbol;
        }

        private static string GetSymbolName(ISymbol symbol)
        {
            if (symbol is IFieldSymbol or IPropertySymbol)
            {
                return $"{symbol.ContainingType.Name}.{symbol.Name}";
            }

            return symbol.Name;
        }

        private static string GetSymbolType(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol ls => ls.Type.ToDisplayString(),
                IParameterSymbol ps => ps.Type.ToDisplayString(),
                IFieldSymbol fs => fs.Type.ToDisplayString(),
                IPropertySymbol prs => prs.Type.ToDisplayString(),
                _ => "Unknown"
            };
        }

        private static string GetCategory(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol => "Local Variable",
                IParameterSymbol => "Local Variable (Parameter)",
                _ => "External Variable"
            };
        }

        private static string GetLocationString(SyntaxNode node)
        {
            var pos = node.GetLocation().GetMappedLineSpan();
            return $"{System.IO.Path.GetFileName(pos.Path)}:{pos.StartLinePosition.Line + 1}:{pos.StartLinePosition.Character + 1}";
        }

        private static string GetReturnValueAssignment(ExpressionSyntax expression)
        {
            var parent = expression.Parent;
            if (parent is EqualsValueClauseSyntax evc && evc.Parent is VariableDeclaratorSyntax vds)
            {
                return vds.Identifier.Text;
            }

            if (parent is AssignmentExpressionSyntax aes)
            {
                return aes.Left.ToString();
            }

            return "N/A";
        }

        private sealed class VariableRecord
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string? FirstDefine { get; set; }
            public string? LastUse { get; set; }
            public bool UsedInSwitch { get; set; }
        }

        private sealed class FunctionRecord
        {
            public string Signature { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Arguments { get; set; } = string.Empty;
            public string ReturnValueAssignment { get; set; } = string.Empty;
            public bool InSwitch { get; set; }
        }
    }
}
