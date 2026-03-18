namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed class DirectiveSeedRule : ISeedRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target)
    {
        if (target.HasMarkedExpressionSeed && !IsControlFlowTarget(target.StatementKind))
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            var ruleId = IsControlFlowTarget(target.StatementKind)
                ? "controlflow-mark"
                : directive.RuleId;
            var reasonText = ruleId == "controlflow-mark"
                ? "Directive matched a control-flow target."
                : directive.ReasonText;

            yield return new ModelRules.MarkDecision(
                target.Target,
                target.Locator,
                new ModelPlanning.PlanAction(directive.ActionKind, directive.Payload),
                new ModelRules.PlanReason(
                    ruleId,
                    reasonText,
                    Origin: ModelPrimitives.DecisionOrigin.Seed));
        }
    }

    private static bool IsControlFlowTarget(ModelPrimitives.StatementKindRef statementKind) =>
        statementKind is ModelPrimitives.StatementKindRef.If or
            ModelPrimitives.StatementKindRef.While or
            ModelPrimitives.StatementKindRef.For or
            ModelPrimitives.StatementKindRef.Return;
}

public sealed class ExpressionProjectionRule : IExpressionProjectionRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisTarget target)
    {
        if (!target.HasMarkedExpressionSeed ||
            target.Directives.Count == 0 ||
            target.Target.TargetKind != ModelPrimitives.TargetKind.Statement ||
            target.StatementKind is ModelPrimitives.StatementKindRef.If or
                ModelPrimitives.StatementKindRef.While or
                ModelPrimitives.StatementKindRef.For or
                ModelPrimitives.StatementKindRef.Return ||
            target.IsHighRisk ||
            target.IsObjectInitializerAssignment)
        {
            yield break;
        }

        foreach (var directive in target.Directives)
        {
            yield return new ModelRules.MarkDecision(
                target.Target,
                target.Locator,
                new ModelPlanning.PlanAction(directive.ActionKind, directive.Payload),
                new ModelRules.PlanReason(
                    "expression-mark",
                    "Directive matched an expression-bearing statement and was projected to the statement target.",
                    RelatedSymbolNames: target.MarkedExpressionKinds,
                    Origin: ModelPrimitives.DecisionOrigin.Projection));
        }
    }
}

public sealed class SanitizationPropagationRule : IPropagationRule
{
    public bool CanPropagate(ModelAnalysis.AnalysisTarget target, ModelAnalysis.SymbolRef usedSymbol, ModelRules.MarkDecision sourceDecision)
    {
        return !target.IsSanitizingAssignment;
    }
}
