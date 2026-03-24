namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 根据目标上的显式指令生成种子决策。
/// </summary>
public sealed class DirectiveSeedRule : ISeedRule
{
    /// <summary>
    /// 评估目标并生成种子决策。
    /// </summary>
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
                new ModelPrimitives.PlanAction(directive.ActionKind, directive.Payload),
                new ModelRules.PlanReason(
                    ruleId,
                    reasonText,
                    Origin: ModelPrimitives.DecisionOrigin.Seed));
        }
    }

    /// <summary>
    /// 判断语句是否属于控制流目标。
    /// </summary>
    private static bool IsControlFlowTarget(ModelPrimitives.StatementKindRef statementKind) =>
        statementKind is ModelPrimitives.StatementKindRef.If or
            ModelPrimitives.StatementKindRef.While or
            ModelPrimitives.StatementKindRef.For or
            ModelPrimitives.StatementKindRef.Return;
}

/// <summary>
/// 将表达式级标记投影为语句级决策。
/// </summary>
public sealed class ExpressionProjectionRule : IExpressionProjectionRule
{
    /// <summary>
    /// 评估目标并生成表达式投影决策。
    /// </summary>
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
                new ModelPrimitives.PlanAction(directive.ActionKind, directive.Payload),
                new ModelRules.PlanReason(
                    "expression-mark",
                    "Directive matched an expression-bearing statement and was projected to the statement target.",
                    RelatedSymbolNames: target.MarkedExpressionKinds,
                    Origin: ModelPrimitives.DecisionOrigin.Projection));
        }
    }
}

/// <summary>
/// 阻止经过净化赋值语句的传播继续向后扩散。
/// </summary>
public sealed class SanitizationPropagationRule : IPropagationRule
{
    /// <summary>
    /// 判断当前目标是否允许继续传播。
    /// </summary>
    public bool CanPropagate(ModelAnalysis.AnalysisTarget target, ModelAnalysis.SymbolRef usedSymbol, ModelRules.MarkDecision sourceDecision)
    {
        return !target.IsSanitizingAssignment;
    }
}
