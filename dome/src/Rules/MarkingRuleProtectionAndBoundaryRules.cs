namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed class HighRiskProtectionRule : IProtectionRule
{
    public bool Blocks(ModelAnalysis.AnalysisTarget target) => target.IsHighRisk;
}

public sealed class ObjectInitializerProtectionRule : IProtectionRule
{
    public bool Blocks(ModelAnalysis.AnalysisTarget target) => target.IsObjectInitializerAssignment;
}

public sealed class InvocationBoundaryPromotionRule : IBoundaryPromotionRule
{
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
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete, decision.Action.Payload),
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

public sealed class ParentBlockPiercingScopeRule : IStatementScopeRule
{
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
