namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 保护高风险目标不被后续规则直接处理。
/// </summary>
public sealed class HighRiskProtectionRule : IProtectionRule
{
    /// <summary>
    /// 判断目标是否因高风险而被保护。
    /// </summary>
    public bool Blocks(ModelAnalysis.AnalysisTarget target) => target.IsHighRisk;
}

/// <summary>
/// 保护对象初始化赋值目标不被后续规则直接处理。
/// </summary>
public sealed class ObjectInitializerProtectionRule : IProtectionRule
{
    /// <summary>
    /// 判断目标是否因对象初始化赋值而被保护。
    /// </summary>
    public bool Blocks(ModelAnalysis.AnalysisTarget target) => target.IsObjectInitializerAssignment;
}

/// <summary>
/// 将满足条件的语句删除决策提升为方法删除候选。
/// </summary>
public sealed class InvocationBoundaryPromotionRule : IBoundaryPromotionRule
{
    /// <summary>
    /// 评估调用边界是否需要执行决策提升。
    /// </summary>
    public IEnumerable<ModelRules.MarkDecision> Evaluate(
        ModelAnalysis.AnalysisContext context,
        ModelAnalysis.AnalysisTarget target,
        ModelRules.MarkDecision decision)
    {
        if (target.Target.TargetKind != ModelPrimitives.TargetKind.Statement ||
            decision.Action.Kind != ModelPrimitives.PlanActionKind.Delete ||
            decision.Reason.Origin == ModelPrimitives.DecisionOrigin.Propagation ||
            string.Equals(decision.Reason.RuleId, "dataflow-propagation", StringComparison.Ordinal) ||
            target.InvokedMemberIds.Count != 1)
        {
            yield break;
        }

        var invokedMemberId = target.InvokedMemberIds[0];
        if (!context.FunctionIndex.NodesByMemberId.TryGetValue(invokedMemberId.Value, out var functionNode) ||
            functionNode.MemberKind != ModelPrimitives.MemberKind.Method ||
            !functionNode.IsPrivate ||
            !functionNode.HasBody ||
            context.Inheritance.IsOverrideMember(invokedMemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(invokedMemberId.Value))
        {
            yield break;
        }

        var remainingReferences = context.References.GetReferencingFunctions(invokedMemberId.Value)
            .Select(memberId => memberId.Value)
            .ToHashSet(StringComparer.Ordinal);
        remainingReferences.Remove(target.Target.MemberId.Value);
        if (remainingReferences.Count > 0)
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            new ModelPrimitives.TargetIdentity(
                functionNode.DocumentPath,
                functionNode.MemberId,
                functionNode.MemberKind,
                ModelPrimitives.TargetKind.Method),
            new ModelPrimitives.TargetLocator(
                functionNode.SpanStart,
                functionNode.SpanLength,
                functionNode.DisplayName,
                new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.Delete, decision.Action.Payload),
            new ModelRules.PlanReason(
                "boundary-promotion",
                "Invocation delete crossed the statement boundary and was promoted to a method delete candidate.",
                decision.TargetKey,
                target.Locator.DisplayText,
                [invokedMemberId.Value],
                [functionNode.DisplayName],
                SourceMemberId: target.Target.MemberId.Value,
                BoundaryKind: ModelPrimitives.BoundaryKind.Invocation,
                TriggeredSymbolKeys: [invokedMemberId.Value],
                Origin: ModelPrimitives.DecisionOrigin.BoundaryPromotion));
    }
}

/// <summary>
/// 在存在跨块依赖时选择父级穿透作用域。
/// </summary>
public sealed class ParentBlockPiercingScopeRule : IStatementScopeRule
{
    /// <summary>
    /// 为种子目标选择语句传播作用域。
    /// </summary>
    public ModelPrimitives.StatementScopeMode SelectScopeMode(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget seedTarget)
    {
        if (seedTarget.Target.TargetKind != ModelPrimitives.TargetKind.Statement ||
            seedTarget.IsHighRisk ||
            seedTarget.IsObjectInitializerAssignment ||
            seedTarget.IsSanitizingAssignment ||
            string.IsNullOrEmpty(seedTarget.ScopeId) ||
            string.IsNullOrEmpty(seedTarget.ParentScopeId))
        {
            return ModelPrimitives.StatementScopeMode.MinimalBlock;
        }

        var sameScopeDefinitions = context.View.Targets
            .Where(target =>
                target.Target.MemberId == seedTarget.Target.MemberId &&
                string.Equals(target.ScopeId, seedTarget.ScopeId, StringComparison.Ordinal) &&
                target.Locator.SpanStart < seedTarget.Locator.SpanStart)
            .SelectMany(target => target.DefinesSymbols)
            .Select(symbol => symbol.SymbolKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in seedTarget.UsesSymbols)
        {
            if (symbol.DeclaringMemberId != seedTarget.Target.MemberId)
            {
                continue;
            }

            if (symbol.SymbolKind == ModelAnalysis.SymbolKindRef.Parameter)
            {
                return ModelPrimitives.StatementScopeMode.ParentBlockPiercing;
            }

            if (symbol.SymbolKind == ModelAnalysis.SymbolKindRef.Local && !sameScopeDefinitions.Contains(symbol.SymbolKey))
            {
                return ModelPrimitives.StatementScopeMode.ParentBlockPiercing;
            }
        }

        return ModelPrimitives.StatementScopeMode.MinimalBlock;
    }
}
