using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    public class SwitchAnalysisResult
    {
        public List<CaseAnalysisResult> Cases { get; set; } = new();
    }

    public class CaseAnalysisResult
    {
        public string Labels { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> VariableReferences { get; set; } = new();
        public List<string> FunctionCalls { get; set; } = new();
    }

    public class SwitchFlowAnalyzer
    {
        private readonly Compilation _compilation;

        public SwitchFlowAnalyzer(Compilation compilation)
        {
            _compilation = compilation;
        }

        public async Task<SwitchAnalysisResult> AnalyzeAsync(string typeName, string methodName)
        {
            var result = new SwitchAnalysisResult();

            var typeSymbol = _compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol == null) return result;

            var methodSymbol = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
            if (methodSymbol == null) return result;

            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null) return result;

            var methodDeclaration = (MethodDeclarationSyntax)await syntaxReference.GetSyntaxAsync();
            var switchStatement = methodDeclaration.DescendantNodes().OfType<SwitchStatementSyntax>().FirstOrDefault();

            if (switchStatement == null) return result;

            var semanticModel = _compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
            var messageIdMap = BuildMessageIdMap();

            foreach (var section in switchStatement.Sections)
            {
                var caseResult = AnalyzeSection(section, semanticModel, messageIdMap);
                result.Cases.Add(caseResult);
            }

            return result;
        }

        private CaseAnalysisResult AnalyzeSection(SwitchSectionSyntax section, SemanticModel model, Dictionary<int, string> messageIdMap)
        {
            var result = new CaseAnalysisResult();

            // 1. Labels
            var labels = string.Join(", ", section.Labels.OfType<CaseSwitchLabelSyntax>().Select(l => l.Value.ToString()));
            if (string.IsNullOrEmpty(labels)) labels = "default";
            result.Labels = labels;

            // 2. Description
            result.Description = GetDescription(section, messageIdMap);

            // 3. Variable References
            result.VariableReferences = GetVariableReferences(section, model);

            // 4. Function Calls
            result.FunctionCalls = GetAllInvocations(section);

            return result;
        }

        private Dictionary<int, string> BuildMessageIdMap()
        {
            var map = new Dictionary<int, string>();
            var type = _compilation.GetTypeByMetadataName("Terraria.ID.MessageID");
            if (type == null) return map;

            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsConst && field.HasConstantValue)
                {
                    if (field.ConstantValue is int val && !map.ContainsKey(val))
                    {
                        map[val] = field.Name;
                    }
                    else if (field.ConstantValue is byte valByte && !map.ContainsKey((int)valByte))
                    {
                        map[(int)valByte] = field.Name;
                    }
                }
            }
            return map;
        }

        private string GetDescription(SwitchSectionSyntax section, Dictionary<int, string> messageIdMap)
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

            AddComments(section.GetLeadingTrivia());

            var firstLabel = section.Labels.FirstOrDefault();
            if (firstLabel != null)
            {
                if (firstLabel is CaseSwitchLabelSyntax caseLabel)
                    AddComments(caseLabel.Keyword.LeadingTrivia);
                else if (firstLabel is DefaultSwitchLabelSyntax defaultLabel)
                    AddComments(defaultLabel.Keyword.LeadingTrivia);
            }

            var description = string.Join("; ", comments);

            if (string.IsNullOrEmpty(description) && section.Labels.FirstOrDefault() is CaseSwitchLabelSyntax label)
            {
                if (int.TryParse(label.Value.ToString(), out int val) && messageIdMap.TryGetValue(val, out string? name))
                {
                    description = $"[MessageID.{name}]";
                }
            }

            return description;
        }

        private List<string> GetVariableReferences(SwitchSectionSyntax section, SemanticModel model)
        {
            var variables = new HashSet<string>();
            var identifiers = section.DescendantNodes().OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                var symbol = model.GetSymbolInfo(id).Symbol;
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
                invocations.Add(node.Expression.ToString());
            }
            return invocations.OrderBy(i => i).ToList();
        }
    }
}
