using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;

namespace Rules;

public interface RuleDefinition
{
    string RuleId { get; }

    string Name { get; }

    IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; }

    IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; }

    IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; }

    IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; }

    IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);

    IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks);

    IEnumerable<DecisionUnit> Propose(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks);
}
