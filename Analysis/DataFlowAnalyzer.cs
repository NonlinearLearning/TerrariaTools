using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    public class DataFlowAnalysisResult
    {
        public List<VariableInfo> Variables { get; set; } = new();
        public int ParameterCount => Variables.Count(v => v.Kind == "Parameter");
        public int LocalVariableCount => Variables.Count(v => v.Kind == "Local");
        public int ExternalVariableCount => Variables.Count(v => v.Kind == "External");

        // Roslyn Data Flow Analysis
        public bool Succeeded { get; set; }
        public int ReadInsideCount { get; set; }
        public int WrittenInsideCount { get; set; }
        public int CapturedCount { get; set; }
        public List<string> CapturedVariables { get; set; } = new();
        public List<string> DataFlowInVariables { get; set; } = new();
    }

    public class VariableInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty; // Parameter, Local, External
        public string Source { get; set; } = string.Empty; // Type name, or "Parameter"
        public string OriginalKind { get; set; } = string.Empty; // Field, Property, Local, Parameter
    }

    public class DataFlowAnalyzer
    {
        private readonly Compilation _compilation;

        public DataFlowAnalyzer(Compilation compilation)
        {
            _compilation = compilation;
        }

        public async Task<DataFlowAnalysisResult> AnalyzeAsync(string typeName, string methodName)
        {
            var result = new DataFlowAnalysisResult();

            var typeSymbol = _compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol == null)
            {
                 var symbols = _compilation.GetSymbolsWithName(typeName.Split('.').Last(), SymbolFilter.Type);
                 typeSymbol = symbols.FirstOrDefault() as INamedTypeSymbol;
            }

            if (typeSymbol == null) return result;

            var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
            if (methodSymbol == null) return result;

            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null) return result;

            var methodDeclaration = (MethodDeclarationSyntax)await syntaxReference.GetSyntaxAsync();
            var semanticModel = _compilation.GetSemanticModel(methodDeclaration.SyntaxTree);

            return AnalyzeVariables(methodDeclaration, semanticModel);
        }

        private DataFlowAnalysisResult AnalyzeVariables(MethodDeclarationSyntax method, SemanticModel model)
        {
            var result = new DataFlowAnalysisResult();
            var symbolDict = new Dictionary<string, VariableInfo>();

            // 1. Collect Parameters
            foreach (var param in method.ParameterList.Parameters)
            {
                var symbol = model.GetDeclaredSymbol(param);
                if (symbol != null)
                {
                    var info = new VariableInfo
                    {
                        Name = symbol.Name,
                        Kind = "Parameter",
                        Source = "Parameter",
                        OriginalKind = "Parameter"
                    };
                    symbolDict[symbol.Name] = info;
                }
            }

            // 2. Traverse identifiers
            var identifiers = method.DescendantNodes().OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                var symbol = model.GetSymbolInfo(id).Symbol;
                if (symbol == null) continue;

                if (symbol is ITypeSymbol || symbol is INamespaceSymbol || symbol is IMethodSymbol)
                    continue;

                VariableInfo? info = null;

                switch (symbol.Kind)
                {
                    case SymbolKind.Field:
                    case SymbolKind.Property:
                    case SymbolKind.Event:
                        // External
                        var container = symbol.ContainingType.Name;
                        var displayName = $"{container}.{symbol.Name}";
                        info = new VariableInfo
                        {
                            Name = displayName,
                            Kind = "External",
                            Source = symbol.Kind.ToString(),
                            OriginalKind = symbol.Kind.ToString()
                        };
                        break;

                    case SymbolKind.Local:
                        // Local
                         var typeName = (symbol as ILocalSymbol)?.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "Unknown";
                        info = new VariableInfo
                        {
                            Name = symbol.Name,
                            Kind = "Local",
                            Source = typeName,
                            OriginalKind = "Local"
                        };
                        break;

                    case SymbolKind.Parameter:
                        break;
                }

                if (info != null)
                {
                    if (!symbolDict.ContainsKey(info.Name))
                    {
                        symbolDict[info.Name] = info;
                    }
                }
            }

            result.Variables = symbolDict.Values.OrderBy(v => v.Name).ToList();

            // 3. Roslyn Data Flow Analysis
            if (method.Body != null)
            {
                var dataFlow = model.AnalyzeDataFlow(method.Body);
                if (dataFlow != null)
                {
                    result.Succeeded = dataFlow.Succeeded;

                    if (dataFlow.Succeeded)
                    {
                        result.ReadInsideCount = dataFlow.ReadInside.Length;
                        result.WrittenInsideCount = dataFlow.WrittenInside.Length;
                        result.CapturedCount = dataFlow.Captured.Length;

                        foreach (var captured in dataFlow.Captured.Select(s => s?.Name).Where(n => n != null).Distinct())
                        {
                            result.CapturedVariables.Add(captured!);
                        }

                        foreach (var input in dataFlow.DataFlowsIn.Select(s => s?.Name).Where(n => n != null).Distinct())
                        {
                            result.DataFlowInVariables.Add(input!);
                        }
                    }
                }
            }

            return result;
        }
    }
}
