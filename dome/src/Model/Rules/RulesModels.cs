using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Model.Rules;

public sealed record RuleExecutionContext(
    string Requester,
    TargetIdentity? SeedTarget,
    StatementScopeMode StatementScopeMode,
    CancellationToken CancellationToken,
    string? Reason = null);

public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

public sealed record PlanReason(
    string RuleId,
    string ReasonText,
    string? SourceTargetKey = null,
    string? SourceTargetDisplayText = null,
    IReadOnlyList<string>? RelatedSymbolKeys = null,
    IReadOnlyList<string>? RelatedSymbolNames = null,
    string? Severity = null,
    string? SourceMemberId = null,
    BoundaryKind? BoundaryKind = null,
    IReadOnlyList<string>? TriggeredSymbolKeys = null,
    DecisionOrigin Origin = DecisionOrigin.Rule,
    DecisionCategory Category = DecisionCategory.Delete);

public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

public sealed record MarkDecision(
    TargetIdentity Target,
    TargetLocator Locator,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null)
{
    public string TargetKey => $"{Target.IdentityKey}|{Locator.EffectiveResolutionKey.SpanStart}|{Locator.EffectiveResolutionKey.SpanLength}";
}
