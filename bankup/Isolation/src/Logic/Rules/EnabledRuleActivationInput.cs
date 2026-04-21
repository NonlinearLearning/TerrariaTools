using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 表示启用规则构造时的可选覆盖项。
/// </summary>
public sealed class EnabledRuleActivationInput
{
    /// <summary>
    /// 获取或初始化规则编码。
    /// </summary>
    public string RuleCode { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化显示名称覆盖值。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 获取或初始化目标类型覆盖值。
    /// </summary>
    public IReadOnlyCollection<RuleTargetKind>? TargetKinds { get; init; }

    /// <summary>
    /// 获取或初始化阶段范围覆盖值。
    /// </summary>
    public IReadOnlyCollection<RuleStageScope>? StageScopes { get; init; }

    /// <summary>
    /// 获取或初始化边界覆盖值。
    /// </summary>
    public RuleBoundary? Boundary { get; init; }

    /// <summary>
    /// 获取或初始化传播许可覆盖值。
    /// </summary>
    public RulePropagationAllowance? PropagationAllowance { get; init; }
}
