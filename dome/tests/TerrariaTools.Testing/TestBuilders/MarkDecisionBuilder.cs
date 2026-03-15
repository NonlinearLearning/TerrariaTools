using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// 标记决策构建器。
/// </summary>
public sealed class MarkDecisionBuilder
{
    private PlanTarget _target = new PlanTargetBuilder().Build();
    private PlanActionKind _actionKind = PlanActionKind.Delete;
    private string _ruleId = "test-rule";
    private string _reasonText = "test reason";
    private string? _payload;
    private DecisionOrigin _origin = DecisionOrigin.Rule;
    private DecisionCategory? _category;

    /// <summary>
    /// 设置目标对象。
    /// </summary>
    /// <param name="target">计划目标。</param>
    /// <returns>当前构建器。</returns>
    public MarkDecisionBuilder WithTarget(PlanTarget target)
    {
        _target = target;
        return this;
    }

    /// <summary>
    /// 设置动作类型与可选负载。
    /// </summary>
    /// <param name="actionKind">动作类型。</param>
    /// <param name="payload">动作负载。</param>
    /// <returns>当前构建器。</returns>
    public MarkDecisionBuilder WithAction(PlanActionKind actionKind, string? payload = null)
    {
        _actionKind = actionKind;
        _payload = payload;
        return this;
    }

    /// <summary>
    /// 设置规则编号与原因文本。
    /// </summary>
    /// <param name="ruleId">规则编号。</param>
    /// <param name="reasonText">原因文本。</param>
    /// <returns>当前构建器。</returns>
    public MarkDecisionBuilder WithReason(string ruleId, string reasonText)
    {
        _ruleId = ruleId;
        _reasonText = reasonText;
        return this;
    }

    /// <summary>
    /// 设置决策来源与可选分类。
    /// </summary>
    /// <param name="origin">决策来源。</param>
    /// <param name="category">决策分类。</param>
    /// <returns>当前构建器。</returns>
    public MarkDecisionBuilder WithOrigin(DecisionOrigin origin, DecisionCategory? category = null)
    {
        _origin = origin;
        _category = category;
        return this;
    }

    /// <summary>
    /// 构建标记决策。
    /// </summary>
    /// <returns>构建后的标记决策。</returns>
    public MarkDecision Build() => MarkDecision.ForTarget(
        _target,
        _actionKind,
        _ruleId,
        _reasonText,
        payload: _payload,
        origin: _origin,
        category: _category);
}
