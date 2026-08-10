using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 仅从已收束到局部定义点的标记继续传播到同一可执行作用域内的引用。
public sealed class SymbolReferencePropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleProducesContract SymbolReferenceProduces = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.FlowSymbolReference)
    });

    private static readonly RuleConsumesContract LocalDefinitionConsumes = new(new[]
    {
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactPorts.FlowLocalDefinition)
    });

    public override string CapabilityId { get; } = "propagate.target.symbol-reference";
    public override string RuleId { get; } = "DEL-SOBJ-PROP-SYMBOL-001";
    public override RuleConsumesContract Consumes => LocalDefinitionConsumes;
    public override RuleProducesContract Produces => SymbolReferenceProduces;
    public override string Name { get; } = "Propagate s-object marks from marked definitions to symbol references";

    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var markedSymbols = BuildMarkedLocalDefinitions(context, seedMarks);
        if (markedSymbols.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks.Select(mark => BuildNodeKey(mark.SyntaxNode)).ToHashSet();
        foreach (var reference in context.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var referencedSymbol = ResolveReferencedSymbol(context, reference);
            if (referencedSymbol is null ||
                !markedSymbols.TryGetValue(referencedSymbol, out var markedDefinition) ||
                markedDefinition.ExecutableScope is null ||
                !ReferenceEquals(markedDefinition.ExecutableScope, FindContainingExecutableScope(reference)) ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked definition.",
                semanticTag: RuleFactPorts.FlowSymbolReference),
              markedDefinition.SourceMark,
              1);
        }
    }

    private static Dictionary<ISymbol, MarkedLocalDefinition> BuildMarkedLocalDefinitions(IPropagationRuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkedLocalDefinition>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (mark.SyntaxNode is not VariableDeclaratorSyntax variableDeclarator ||
                mark.OutputKind != RuleOutputKind.LocalDefinitionFromInitializer)
            {
                continue;
            }

            var symbol = context.SemanticModel.GetDeclaredSymbol(variableDeclarator);
            if (symbol is ILocalSymbol && !symbols.ContainsKey(symbol))
            {
                symbols.Add(symbol, new MarkedLocalDefinition(mark, FindContainingExecutableScope(variableDeclarator)));
            }
        }

        return symbols;
    }

    private static ISymbol? ResolveReferencedSymbol(IPropagationRuleContext context, IdentifierNameSyntax identifierName)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(identifierName).Symbol;
        return symbol is ILocalSymbol or IParameterSymbol ? symbol : null;
    }

    private static SyntaxNode? FindContainingExecutableScope(SyntaxNode node) => node.AncestorsAndSelf().FirstOrDefault(ancestor =>
      ancestor is MethodDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax or
      OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax or AccessorDeclarationSyntax or
      AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private sealed record MarkedLocalDefinition(MarkRecord SourceMark, SyntaxNode? ExecutableScope);
}
