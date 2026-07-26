using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Deletion.Rules;

namespace Deletion.Core.Marking;

public abstract class SObjectAtomicExpressionMarkRuleBase : RuleDefinitionMark
{
    private const string DeleteSObjectGroupKey = "DEL-SOBJ";

    public override string GroupKey { get; } = DeleteSObjectGroupKey;

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
