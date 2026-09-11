using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Rules;

/// 仅从已收束到局部定义点的标记继续传播到同一可执行作用域内的引用。
[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class SymbolReferencePropagationRule : ExpressionFlowPropagationRuleBase
{
    private static readonly RuleProducesContract SymbolReferenceProduces = new(new[]
    {
        new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactKind.FlowSymbolReference)
    });

    private static readonly RuleConsumesContract LocalDefinitionConsumes = new(new[]
    {
        new RuleConsumedSyntax(new[] { SyntaxKind.VariableDeclarator }, RuleFactKind.FlowLocalDefinition)
    });

public override string RuleId { get; } = "propagate.target.symbol-reference";
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
        var references = markedSymbols
          .SelectMany(pair => context.LocalSymbolReferences
            .GetReferences(pair.Value.SourceMark.SyntaxNode, pair.Key)
            .Select(reference => (Reference: reference, SourceMark: pair.Value.SourceMark)))
          .OrderBy(item => item.Reference.SpanStart);
        foreach (var (reference, sourceMark) in references)
        {
            if (!knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked definition.",
                factKind: RuleFactKind.FlowSymbolReference),
              sourceMark,
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
                symbols.Add(symbol, new MarkedLocalDefinition(mark));
            }
        }

        return symbols;
    }

    private sealed record MarkedLocalDefinition(MarkRecord SourceMark);
}
