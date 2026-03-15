namespace TerrariaTools.Dome.Rules;

using TerrariaTools.Dome.Core;

public interface IMemberTargetRule
{
    IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target);
}

public sealed class PublicMethodPrivatizationRule : IMethodRule
{
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode)
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

        yield return MarkDecision.ForTarget(
            new PlanTarget(
                functionNode.DocumentPath,
                functionNode.MemberId,
                functionNode.MemberKind,
                TargetKind.Method,
                functionNode.SpanStart,
                functionNode.SpanLength,
                functionNode.DisplayName,
                new TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
            PlanActionKind.ChangeVisibilityToPrivate,
            "method-privatization",
            "Public method has only internal references and can be reduced to private.",
            origin: DecisionOrigin.Cleanup);
    }

    private static bool TryGetEligibleMethod(AnalysisContext context, FunctionNodeRef functionNode, out MemberCleanupSymbolInfo info)
    {
        info = null!;
        if (functionNode.MemberKind != MemberKind.Method ||
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
}

public sealed class UnusedMethodRule : IMethodRule
{
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, FunctionNodeRef functionNode)
    {
        if (functionNode.MemberKind != MemberKind.Method ||
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

        yield return MarkDecision.ForTarget(
            new PlanTarget(
                functionNode.DocumentPath,
                functionNode.MemberId,
                functionNode.MemberKind,
                TargetKind.Method,
                functionNode.SpanStart,
                functionNode.SpanLength,
                functionNode.DisplayName,
                new TargetResolutionKey(functionNode.SpanStart, functionNode.SpanLength)),
            PlanActionKind.Delete,
            "unused-method",
            "Method has no internal or external references.",
            origin: DecisionOrigin.Cleanup);
    }
}

public sealed class UnusedMemberRule : IMemberTargetRule
{
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target)
    {
        if (target.Target.TargetKind is not (TargetKind.Field or TargetKind.Property) ||
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

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.Delete,
            "unused-member",
            $"{target.Target.MemberKind} has no references.",
            origin: DecisionOrigin.Cleanup);
    }
}

public sealed class UnusedClassRule : IClassRule
{
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target)
    {
        var typeId = target.Target.MemberId.Value;
        var typeInfo = context.MemberCleanup.GetTypeInfo(typeId);
        if (target.Target.TargetKind != TargetKind.Class ||
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

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.Delete,
            "class-mark",
            "Class has no references and is not protected by inheritance.",
            origin: DecisionOrigin.Cleanup);
    }

    private static bool IsKnownFrameworkType(AnalysisContext context, string typeId)
    {
        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, typeId, StringComparison.Ordinal) &&
            edge.Kind is TypeDependencyKind.Inherits or TypeDependencyKind.Implements &&
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
    public IEnumerable<MarkDecision> Evaluate(AnalysisContext context, AnalysisTarget target)
    {
        if (target.Target.TargetKind != TargetKind.Class ||
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

        yield return MarkDecision.ForTarget(
            target.Target,
            PlanActionKind.ReorderPublicMethods,
            "public-method-order",
            "Reorder public ordinary methods within the type.",
            origin: DecisionOrigin.Cleanup);
    }
}
