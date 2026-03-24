namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 将仅被内部调用的公共方法降为私有方法。
/// </summary>
public sealed class PublicMethodPrivatizationRule : IMethodRule
{
    /// <summary>
    /// 评估方法节点并生成可见性收缩决策。
    /// </summary>
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
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate),
            new ModelRules.PlanReason(
                "method-privatization",
                "Public method has only internal references and can be reduced to private.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.VisibilityChange));
    }

    /// <summary>
    /// 判断方法是否满足可见性收缩的前置条件。
    /// </summary>
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

    /// <summary>
    /// 将函数节点转换为方法级目标标识。
    /// </summary>
    private static ModelPrimitives.TargetIdentity ToMethodTarget(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            ModelPrimitives.TargetKind.Method);

    /// <summary>
    /// 将函数节点转换为方法级目标定位信息。
    /// </summary>
    private static ModelPrimitives.TargetLocator ToMethodLocator(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName,
            new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength));
}

/// <summary>
/// 删除没有任何内部或外部引用的私有方法。
/// </summary>
public sealed class UnusedMethodRule : IMethodRule
{
    /// <summary>
    /// 评估方法节点并生成删除决策。
    /// </summary>
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
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "unused-method",
                "Method has no internal or external references.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }

    /// <summary>
    /// 将函数节点转换为方法级目标标识。
    /// </summary>
    private static ModelPrimitives.TargetIdentity ToMethodTarget(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.DocumentPath,
            functionNode.MemberId,
            functionNode.MemberKind,
            ModelPrimitives.TargetKind.Method);

    /// <summary>
    /// 将函数节点转换为方法级目标定位信息。
    /// </summary>
    private static ModelPrimitives.TargetLocator ToMethodLocator(ModelAnalysis.FunctionNodeRef functionNode) =>
        new(
            functionNode.SpanStart,
            functionNode.SpanLength,
            functionNode.DisplayName,
            new ModelPrimitives.TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength));
}

/// <summary>
/// 删除没有引用的私有字段和属性。
/// </summary>
public sealed class UnusedMemberRule : IMemberTargetRule
{
    /// <summary>
    /// 评估成员目标并生成删除决策。
    /// </summary>
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
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "unused-member",
                $"{target.Target.MemberKind} has no references.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }
}

/// <summary>
/// 删除没有引用且不在继承链中的私有类型。
/// </summary>
public sealed class UnusedClassRule : IClassRule
{
    /// <summary>
    /// 评估类型目标并生成删除决策。
    /// </summary>
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
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.Delete),
            new ModelRules.PlanReason(
                "class-mark",
                "Class has no references and is not protected by inheritance.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Delete));
    }

    /// <summary>
    /// 判断类型是否属于需要保留的框架类型。
    /// </summary>
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

/// <summary>
/// 为类型生成公共方法重排决策。
/// </summary>
public sealed class PublicMethodOrderingRule : IClassRule
{
    /// <summary>
    /// 评估类型目标并生成重排决策。
    /// </summary>
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
            new ModelPrimitives.PlanAction(ModelPrimitives.PlanActionKind.ReorderPublicMethods),
            new ModelRules.PlanReason(
                "public-method-order",
                "Reorder public ordinary methods within the type.",
                Origin: ModelPrimitives.DecisionOrigin.Cleanup,
                Category: ModelPrimitives.DecisionCategory.Reorder));
    }
}
