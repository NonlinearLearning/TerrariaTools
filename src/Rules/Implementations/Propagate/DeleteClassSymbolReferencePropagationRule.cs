using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public sealed class DeleteClassSymbolReferencePropagationRule : RuleDefinitionPropagate
{
    public override string RuleId { get; } = DeleteClassRuleIds.LocalReferencePropagationRuleId;

    public override string GroupKey { get; } = DeleteClassRuleIds.GroupKey;

    public override string Name { get; } = "Propagate delete-class local declarators to same-scope references";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
      new[]
      {
        SyntaxKind.IdentifierName
      };

    public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
        var markedSymbols = BuildMarkedLocalDefinitions(context, seedMarks);
        if (markedSymbols.Count == 0)
        {
            yield break;
        }

        var knownKeys = seedMarks
          .Select(mark => BuildNodeKey(mark.SyntaxNode))
          .ToHashSet();
        var references = markedSymbols
          .SelectMany(pair => context.LocalSymbolReferences
            .GetReferences(pair.Value.SyntaxNode, pair.Key)
            .Select(reference => (Reference: reference, SourceMark: pair.Value)))
          .OrderBy(item => item.Reference.SpanStart);
        foreach (var (reference, sourceMark) in references)
        {
            if (reference.SpanStart <= sourceMark.SyntaxNode.SpanStart ||
                !knownKeys.Add(BuildNodeKey(reference)))
            {
                continue;
            }

            yield return new PropagatedMarkRecord(
              RuleId,
              MarkRecordFactory.Create(
                RuleId,
                reference,
                $"Symbol reference '{reference.Identifier.ValueText}' resolves to a marked delete-class local definition."),
              sourceMark,
              1);
        }
    }

    private static Dictionary<ISymbol, MarkRecord> BuildMarkedLocalDefinitions(RuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var symbols = new Dictionary<ISymbol, MarkRecord>(SymbolEqualityComparer.Default);
        foreach (var mark in marks)
        {
            if (!IsObjectCreationDefinitionMark(mark))
            {
                continue;
            }

            var symbol = ResolveDeclaredLocalSymbol(context, mark.SyntaxNode);
            if (symbol is null || symbols.ContainsKey(symbol))
            {
                continue;
            }

            symbols.Add(symbol, mark);
        }

        return symbols;
    }

    private static bool IsObjectCreationDefinitionMark(MarkRecord mark)
    {
        return mark.SyntaxNode is VariableDeclaratorSyntax &&
          mark.Reason.Contains(
            "Object creation initializer is marked",
            StringComparison.Ordinal);
    }

    private static ISymbol? ResolveDeclaredLocalSymbol(RuleContext context, SyntaxNode node)
    {
        var symbol = node is VariableDeclaratorSyntax variableDeclarator
          ? context.SemanticModel.GetDeclaredSymbol(variableDeclarator)
          : null;

        return symbol is ILocalSymbol ? symbol : null;
    }

    private static (int Start, int Length, int RawKind) BuildNodeKey(SyntaxNode syntaxNode)
    {
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }
}
