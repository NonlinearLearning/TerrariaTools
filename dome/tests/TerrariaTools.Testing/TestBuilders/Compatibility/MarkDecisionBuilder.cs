using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native mark decisions.
/// </summary>
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

    public MarkDecisionCompatibilityBuilder WithTarget(ModelPrimitives.TargetIdentity target)
    {
        _target = target;
        return this;
    }

    public MarkDecisionCompatibilityBuilder WithLocator(ModelPrimitives.TargetLocator locator)
    {
        _locator = locator;
        return this;
    }

    public MarkDecisionCompatibilityBuilder WithAction(ModelPrimitives.PlanActionKind actionKind, string? payload = null)
    {
        _actionKind = actionKind;
        _payload = payload;
        return this;
    }

    public MarkDecisionCompatibilityBuilder WithReason(string ruleId, string reasonText)
    {
        _ruleId = ruleId;
        _reasonText = reasonText;
        return this;
    }

    public MarkDecisionCompatibilityBuilder WithOrigin(
        ModelPrimitives.DecisionOrigin origin,
        ModelPrimitives.DecisionCategory category = ModelPrimitives.DecisionCategory.Delete)
    {
        _origin = origin;
        _category = category;
        return this;
    }

    public ModelRules.MarkDecision Build() => new(
        _target,
        _locator,
        new ModelPlanning.PlanAction(_actionKind, _payload),
        new ModelRules.PlanReason(
            _ruleId,
            _reasonText,
            Origin: _origin,
            Category: _category));
}
