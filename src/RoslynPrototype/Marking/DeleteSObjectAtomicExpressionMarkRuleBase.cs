using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rules;

namespace RoslynPrototype.Marking;

public abstract class DeleteSObjectAtomicExpressionMarkRuleBase : RuleDefinitionMark
{
    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    protected abstract SyntaxKind MarkKind { get; }

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { MarkKind };

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteSObjectMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          AllowedMarkNodeKinds);
    }
}
