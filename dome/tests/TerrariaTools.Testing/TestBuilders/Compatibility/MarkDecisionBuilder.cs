using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class MarkDecisionCompatibilityBuilder
{
    private ModelPrimitives.TargetIdentity _target = new PlanTargetCompatibilityBuilder().Build();
    private ModelPrimitives.TargetLocator _locator = new(0, 1, "Run");
    private ModelPrimitives.PlanActionKind _actionKind = ModelPrimitives.PlanActionKind.Delete;
    private string _ruleId = "test-rule";
    private string _reasonText = "test reason";
    private string? _payload;
    private ModelPrimitives.DecisionOrigin _origin = ModelPrimitives.DecisionOrigin.Rule;
    private ModelPrimitives.DecisionCategory _category = ModelPrimitives.DecisionCategory.Delete;

    /// <summary>
    /// 设置目标标识。
    /// </summary>
    public MarkDecisionCompatibilityBuilder WithTarget(ModelPrimitives.TargetIdentity target)
    {
        _target = target;
        return this;
    }

    /// <summary>
    /// 设置目标定位器。
    /// </summary>
    public MarkDecisionCompatibilityBuilder WithLocator(ModelPrimitives.TargetLocator locator)
    {
        _locator = locator;
        return this;
    }

    /// <summary>
    /// 设置计划动作及其可选载荷。
    /// </summary>
    public MarkDecisionCompatibilityBuilder WithAction(ModelPrimitives.PlanActionKind actionKind, string? payload = null)
    {
        _actionKind = actionKind;
        _payload = payload;
        return this;
    }

    /// <summary>
    /// 设置规则标识与原因文本。
    /// </summary>
    public MarkDecisionCompatibilityBuilder WithReason(string ruleId, string reasonText)
    {
        _ruleId = ruleId;
        _reasonText = reasonText;
        return this;
    }

    /// <summary>
    /// 设置决策来源与分类。
    /// </summary>
    public MarkDecisionCompatibilityBuilder WithOrigin(
        ModelPrimitives.DecisionOrigin origin,
        ModelPrimitives.DecisionCategory category = ModelPrimitives.DecisionCategory.Delete)
    {
        _origin = origin;
        _category = category;
        return this;
    }

    /// <summary>
    /// 构建标记决策实例。
    /// </summary>
    public ModelRules.MarkDecision Build() => new(
        _target,
        _locator,
        new ModelPrimitives.PlanAction(_actionKind, _payload),
        new ModelRules.PlanReason(
            _ruleId,
            _reasonText,
            Origin: _origin,
            Category: _category));
}

