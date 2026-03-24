using TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 表示规则执行期间的上下文数据。
/// </summary>
public sealed record RuleExecutionContext(
    string Requester,
    TargetIdentity? SeedTarget,
    StatementScopeMode StatementScopeMode,
    CancellationToken CancellationToken,
    string? Reason = null);

/// <summary>
/// 表示规则层的指令动作。
/// </summary>
public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

/// <summary>
/// 表示规则决策的原因说明。
/// </summary>
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

/// <summary>
/// 表示传播产生的证据信息。
/// </summary>
public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

/// <summary>
/// 表示传播链中的单步跳转。
/// </summary>
public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

/// <summary>
/// 表示某个规则决策附带的传播链。
/// </summary>
public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

/// <summary>
/// 表示单条规则决策。
/// </summary>
public sealed record MarkDecision(
    TargetIdentity Target,
    TargetLocator Locator,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null)
{
    /// <summary>
    /// 获取用于唯一标识目标决策位置的稳定键。
    /// </summary>
    public string TargetKey => $"{Target.IdentityKey}|{Locator.EffectiveResolutionKey.SpanStart}|{Locator.EffectiveResolutionKey.SpanLength}";
}

/// <summary>
/// 表示规则执行输出的决策分组。
/// </summary>
public sealed record DecisionSet(
    IReadOnlyList<MarkDecision> InitialDecisions,
    IReadOnlyList<MarkDecision> PredictedDecisions)
{
    /// <summary>
    /// 获取初始决策与预测决策合并后的完整集合。
    /// </summary>
    public IReadOnlyList<MarkDecision> AllDecisions { get; } = InitialDecisions.Concat(PredictedDecisions).ToArray();
}
