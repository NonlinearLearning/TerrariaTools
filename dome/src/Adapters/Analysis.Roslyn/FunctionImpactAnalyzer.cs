namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

public sealed partial class FunctionImpactAnalyzer : ApplicationAbstractions.IFunctionImpactAnalyzer
{
    CorePlanning.FunctionImpactSet ApplicationAbstractions.IFunctionImpactAnalyzer.Analyze(
        CorePlanning.AuditPlan plan,
        CoreAnalysis.AnalysisOutput analysis)
    {
        var snapshot = analysis.Services.FunctionGraphs.GetSnapshot(
            ApplicationAbstractions.FunctionGraphRequests.WholeProjectCalls(
                nameof(FunctionImpactAnalyzer),
                "Whole-project impact summary"));

        var deletedFunctionIds = plan.Changes
            .Where(change => change.Target.TargetKind == CoreCommon.TargetKind.Method && change.Action.Kind == CoreCommon.PlanActionKind.Delete)
            .Select(change => change.Target.MemberId.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (deletedFunctionIds.Length == 0)
        {
            return new CorePlanning.FunctionImpactSet(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                1,
                new[] { CoreCommon.FunctionDependencyKind.Calls });
        }

        var deletedFunctionSet = deletedFunctionIds.ToHashSet(StringComparer.Ordinal);
        var affectedFunctionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in snapshot.Graph.Edges.Where(edge => edge.Kind == CoreCommon.FunctionDependencyKind.Calls))
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

        return new CorePlanning.FunctionImpactSet(
            deletedFunctionIds,
            affectedFunctionIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            affectedDocumentPaths,
            1,
            new[] { CoreCommon.FunctionDependencyKind.Calls });
    }
}




