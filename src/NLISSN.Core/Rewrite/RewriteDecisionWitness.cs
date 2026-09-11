using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Decision;

namespace NLISSN.Core.Rewrite;

/// <summary>
/// Captures final-decision evidence used to authorize rewrite-plan operations.
/// </summary>
public sealed record RewriteDecisionWitness(
    string RuleId,
    string ContractId,
    DecisionActionKind Action,
    string FilePath,
    TextSpan Anchor,
    IReadOnlyList<TextSpan> AuthorizedSpans,
    IReadOnlyList<RewriteControlFlowEffect> Effects,
    IReadOnlyList<TextSpan>? PreservedSpans = null)
{
    public IReadOnlyList<TextSpan> PreservationFrame => PreservedSpans ?? Array.Empty<TextSpan>();
}
