namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed class PublicMethodPrivatizationRule : IMethodRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode)
    {
        if (!TryGetEligibleMethod(context, functionNode, out var info) || !info.IsPublic)
        {
            yield break;
        }

        if (!context.MemberCleanup.HasInternalMethodReferences(functionNode.MemberId) ||
            context.MemberCleanup.HasExternalMethodReferences(functionNode.MemberId))
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            ToMethodTarget(functionNode),
            ToMethodLocator(functionNode),
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate),
            new ModelRules.PlanReason(
                "method-privatization",
                "Public method has only internal references and can be reduced to private.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.VisibilityChange));
    }

    private static bool TryGetEligibleMethod(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode, out ModelAnalysis.MemberCleanupSymbolInfo info)
    {
        info = null!;
        if (functionNode.MemberKind != ModelPrimitives.MemberKind.Method ||
            context.MemberCleanup.GetSymbolInfo(functionNode.MemberId.Value) is not { } symbolInfo ||
            !symbolInfo.IsOrdinaryMethod ||
            symbolInfo.IsStatic ||
            symbolInfo.IsAbstract ||
            symbolInfo.IsVirtual ||
            symbolInfo.IsOverride ||
            symbolInfo.IsExtern ||
            symbolInfo.IsEntryPointLike ||
            symbolInfo.IsInInterfaceType ||
            symbolInfo.IsPartialType ||
            context.Inheritance.ImplementsInterfaceMember(functionNode.MemberId.Value) ||
            context.Inheritance.IsOverrideMember(functionNode.MemberId.Value))
        {
            return false;
        }

        var typeInfo = context.MemberCleanup.GetTypeInfo(symbolInfo.DeclaringTypeId);
        if (typeInfo == null ||
            typeInfo.IsPartial ||
            typeInfo.IsInterface ||
            typeInfo.IsInInheritanceChain)
        {
            return false;
        }

        info = symbolInfo;
        return true;
    }

    private static ModelPrimitives.TargetIdentity ToMethodTarget(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            ModelPrimitives.TargetKind.Method);

    private static ModelPrimitives.TargetLocator ToMethodLocator(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName,
            new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength));
}

public sealed class UnusedMethodRule : IMethodRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode)
    {
        if (functionNode.MemberKind != ModelPrimitives.MemberKind.Method ||
            context.MemberCleanup.GetSymbolInfo(functionNode.MemberId.Value) is not { } info ||
            !info.IsPrivate ||
            !info.IsOrdinaryMethod ||
            info.IsStatic ||
            info.IsAbstract ||
            info.IsVirtual ||
            info.IsOverride ||
            info.IsExtern ||
            info.IsEntryPointLike ||
            info.IsInInterfaceType ||
            info.IsPartialType ||
            info.IsNestedType ||
            context.Inheritance.ImplementsInterfaceMember(functionNode.MemberId.Value) ||
            context.Inheritance.IsOverrideMember(functionNode.MemberId.Value))
        {
            yield break;
        }

        var typeInfo = context.MemberCleanup.GetTypeInfo(info.DeclaringTypeId);
        if (typeInfo == null ||
            typeInfo.IsPartial ||
            typeInfo.IsInterface ||
            typeInfo.IsInInheritanceChain)
        {
            yield break;
        }

        if (context.MemberCleanup.HasInternalMethodReferences(functionNode.MemberId) ||
            context.MemberCleanup.HasExternalMethodReferences(functionNode.MemberId))
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            ToMethodTarget(functionNode),
            ToMethodLocator(functionNode),
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "unused-method",
                "Method has no internal or external references.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }

    private static ModelPrimitives.TargetIdentity ToMethodTarget(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            ModelPrimitives.TargetKind.Method);

    private static ModelPrimitives.TargetLocator ToMethodLocator(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName,
            new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength));
}

public sealed class UnusedMemberRule : IMemberTargetRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target)
    {
        if (target.Target.TargetKind is not (ModelPrimitives.TargetKind.Field or ModelPrimitives.TargetKind.Property) ||
            context.MemberCleanup.GetSymbolInfo(target.Target.MemberId.Value) is not { } info ||
            !info.IsPrivate ||
            info.IsStatic ||
            info.IsAbstract ||
            info.IsOverride ||
            info.IsInInterfaceType ||
            info.IsPartialType ||
            info.IsNestedType)
        {
            yield break;
        }

        var typeInfo = context.MemberCleanup.GetTypeInfo(info.DeclaringTypeId);
        if (typeInfo == null ||
            typeInfo.IsPartial ||
            typeInfo.IsInterface ||
            typeInfo.IsInInheritanceChain)
        {
            yield break;
        }

        if (context.MemberCleanup.HasAnyReferences(target.Target.MemberId.Value))
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "unused-member",
                $"{target.Target.MemberKind} has no references.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }
}

public sealed class UnusedClassRule : IClassRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target)
    {
        var typeId = target.Target.MemberId.Value;
        var typeInfo = context.MemberCleanup.GetTypeInfo(typeId);
        if (target.Target.TargetKind != ModelPrimitives.TargetKind.Class ||
            typeInfo == null ||
            target.IsHighRisk ||
            typeInfo.IsPublic ||
            typeInfo.IsAbstract ||
            typeInfo.IsStatic ||
            typeInfo.IsPartial ||
            typeInfo.IsInterface ||
            typeInfo.IsInInheritanceChain ||
            context.References.HasReferences(typeId) ||
            IsKnownFrameworkType(context, typeId))
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "class-mark",
                "Class has no references and is not protected by inheritance.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }

    private static bool IsKnownFrameworkType(ModelAnalysis.AnalysisContext context, string typeId)
    {
        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, typeId, StringComparison.Ordinal) &&
            edge.Kind is ModelAnalysis.TypeDependencyKind.Inherits or ModelAnalysis.TypeDependencyKind.Implements &&
            (edge.TargetTypeId.Contains("GenPass", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenShape", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenAction", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("GenStructure", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("NetSocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("ISocialModule", StringComparison.Ordinal) ||
             edge.TargetTypeId.Contains("IItemDropRule", StringComparison.Ordinal)));
    }
}

public sealed class PublicMethodOrderingRule : IClassRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target)
    {
        if (target.Target.TargetKind != ModelPrimitives.TargetKind.Class ||
            context.MemberCleanup.GetTypeInfo(target.Target.MemberId.Value) is not { } typeInfo ||
            typeInfo.IsPartial)
        {
            yield break;
        }

        var publicMethods = context.MemberCleanup.GetReorderablePublicMethods(target.Target.MemberId.Value);
        if (publicMethods.Count < 2)
        {
            yield break;
        }

        yield return new ModelRules.MarkDecision(
            target.Target,
            target.Locator,
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.ReorderPublicMethods),
            new ModelRules.PlanReason(
                "public-method-order",
                "Reorder public ordinary methods within the type.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Reorder));
    }
}
