namespace TerrariaTools.Dome.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

public sealed partial class FunctionImpactAnalyzer : ApplicationAbstractions.IFunctionImpactAnalyzer
{
    ModelPlanning.FunctionImpactSet ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze(
        ModelPlanning.AuditPlan plan,
        ModelAnalysis.AnalysisServices services,
        ModelAnalysis.FunctionGraphRequest request) =>
        ((ApplicationAbstractions.IFunctionImpactAnalyzer)this).Analyze(plan, services.FunctionGraphs.GetSnapshot(request));

    ModelPlanning.FunctionImpactSet ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze(
        ModelPlanning.AuditPlan plan,
        ModelAnalysis.FunctionGraphSnapshot snapshot)
    {
        var deletedFunctionIds = plan.Changes
            .Where(change => change.Target.TargetKind == ModelPrimitives.TargetKind.Method && change.Action.Kind == ModelPrimitives.PlanActionKind.Delete)
            .Select(change => change.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (deletedFunctionIds.Length == 0)
        {
            return new ModelPlanning.FunctionImpactSet(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                1,
                new[] { ModelPrimitives.FunctionDependencyKind.Calls });
        }

        var deletedFunctionSet = deletedFunctionIds.ToHashSet(StringComparer.Ordinal);
        var affectedFunctionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in snapshot.Graph.Edges.Where(edge => edge.Kind == ModelPrimitives.FunctionDependencyKind.Calls))
        {
            if (deletedFunctionSet.Contains(edge.TargetMemberId.Value) &&
                !deletedFunctionSet.Contains(edge.SourceMemberId.Value))
            {
                affectedFunctionIds.Add(edge.SourceMemberId.Value);
            }

            if (deletedFunctionSet.Contains(edge.SourceMemberId.Value) &&
                !deletedFunctionSet.Contains(edge.TargetMemberId.Value))
            {
                affectedFunctionIds.Add(edge.TargetMemberId.Value);
            }
        }

        var affectedDocumentPaths = snapshot.Graph.Nodes
            .Where(node => affectedFunctionIds.Contains(node.MemberId.Value))
            .Select(node => node.DocumentPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new ModelPlanning.FunctionImpactSet(
            deletedFunctionIds,
            affectedFunctionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            affectedDocumentPaths,
            1,
            new[] { ModelPrimitives.FunctionDependencyKind.Calls });
    }
}
