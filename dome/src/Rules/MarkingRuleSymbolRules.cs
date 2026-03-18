namespace TerrariaTools.Dome.Rules;

using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

public sealed partial class FunctionMarkingRule : IMethodRule
{
    private static readonly HashSet<string> KnownFrameworkEntrypointNames =
        new(StringComparer.Ordinal)
        {
            "Initialize",
            "Shutdown",
            "Connect",
            "Send",
            "Receive",
            "StartListening",
            "Apply",
            "ApplyPass",
            "TryDroppingItem",
            "CanDrop",
            "Find",
            "Perform",
            "Load",
            "OnBegin",
            "OnEnd",
            "OnActivate",
            "Draw",
            "Recalculate"
        };

    private static readonly string[] KnownFrameworkTypeMarkers =
    [
        "GenPass",
        "GenShape",
        "GenAction",
        "GenStructure",
        "NetSocialModule",
        "IItemDropRule",
        "ISocialModule"
    ];

    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode)
    {
        if (functionNode.MemberKind != ModelPrimitives.MemberKind.Method || !functionNode.IsPrivate)
        {
            yield break;
        }

        if (context.Inheritance.IsOverrideMember(functionNode.MemberId.Value) ||
            context.Inheritance.ImplementsInterfaceMember(functionNode.MemberId.Value))
        {
            yield break;
        }

        if (IsKnownFrameworkEntrypoint(context, functionNode))
        {
            yield break;
        }

        var hasReferences = context.References.HasReferences(functionNode.MemberId.Value);
        if (!hasReferences)
        {
            yield return new ModelRules.MarkDecision(
                ToMethodTarget(functionNode),
                ToMethodLocator(functionNode),
                new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete),
                new ModelRules.PlanReason(
                    "function-mark",
                    "Method has no references and is not protected by inheritance or interface implementation."));
            yield break;
        }

        if (!functionNode.ReturnsVoid && functionNode.HasBody && !functionNode.HasStatements)
        {
            yield return new ModelRules.MarkDecision(
                ToMethodTarget(functionNode),
                ToMethodLocator(functionNode),
                new ModelPlanning.PlanAction(
                    ModelPrimitives.PlanActionKind.AddReturn,
                    DefaultValueFormatter.Format(functionNode.ReturnTypeDisplay)),
                new ModelRules.PlanReason(
                    "function-mark",
                    "Referenced non-void method has an empty body and requires a default return.",
                    Origin: ModelPrimitives.DecisionOrigin.Rule,
                    Category: ModelPrimitives.DecisionCategory.AddReturn));
        }
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

    private static bool IsKnownFrameworkEntrypoint(ModelAnalysis.AnalysisContext context, ModelAnalysis.FunctionNodeRef functionNode)
    {
        var methodName = TryGetMethodName(functionNode.MemberId.Value);
        if (string.IsNullOrEmpty(methodName) || !KnownFrameworkEntrypointNames.Contains(methodName))
        {
            return IsProgramEntrypoint(functionNode, methodName);
        }

        return context.View.TypeGraph.Edges.Any(edge =>
            string.Equals(edge.SourceTypeId, functionNode.DeclaringTypeId, StringComparison.Ordinal) &&
            edge.Kind is ModelAnalysis.TypeDependencyKind.Inherits or ModelAnalysis.TypeDependencyKind.Implements &&
            KnownFrameworkTypeMarkers.Any(marker => edge.TargetTypeId.Contains(marker, StringComparison.Ordinal)));
    }

    private static bool IsProgramEntrypoint(ModelAnalysis.FunctionNodeRef functionNode, string methodName)
    {
        if (!string.Equals(methodName, "Main", StringComparison.Ordinal))
        {
            return false;
        }

        return functionNode.ReturnsVoid &&
               (functionNode.MemberId.Value.EndsWith(".Main(string[])", StringComparison.Ordinal) ||
                functionNode.MemberId.Value.EndsWith(".Main()", StringComparison.Ordinal));
    }

    private static string TryGetMethodName(string memberId)
    {
        var parameterStart = memberId.IndexOf('(');
        if (parameterStart <= 0)
        {
            return string.Empty;
        }

        var lastDot = memberId.LastIndexOf('.', parameterStart);
        if (lastDot < 0 || lastDot + 1 >= parameterStart)
        {
            return string.Empty;
        }

        return memberId.Substring(lastDot + 1, parameterStart - lastDot - 1);
    }
}

public sealed partial class ClassMarkingRule : IClassRule
{
    public IEnumerable<ModelRules.MarkDecision> Evaluate(ModelAnalysis.AnalysisContext context, ModelAnalysis.AnalysisTarget target)
    {
        var typeId = target.Target.MemberId.Value;
        if (target.Target.TargetKind != ModelPrimitives.TargetKind.Class)
        {
            yield break;
        }

        if (target.IsHighRisk ||
            context.Inheritance.IsInInheritanceChain(typeId) ||
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
                Origin: ModelPrimitives.DecisionOrigin.Rule,
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
