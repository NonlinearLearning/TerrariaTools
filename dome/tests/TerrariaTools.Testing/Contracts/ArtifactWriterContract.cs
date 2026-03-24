using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class ArtifactWriterContract
{
    public static async Task AssertWritesArtifactsAsync(ApplicationAbstractions.IArtifactWriter writer)
    {
        var root = Path.Combine(Path.GetTempPath(), "TerrariaTools.Testing", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await AssertWritesArtifactsAsyncAtPath(writer, root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static async Task AssertWritesArtifactsAsyncAtPath(ApplicationAbstractions.IArtifactWriter writer, string rootPath)
    {
        var report = new ModelExecution.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            null,
            Array.Empty<ModelExecution.ConflictSummary>(),
            new ModelExecution.RiskSummary(0, Array.Empty<string>()),
            new ModelExecution.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            false,
            Array.Empty<ModelExecution.WorkspaceDiagnosticInfo>(),
            null);

        var analysis = new CoreAnalysis.AnalysisResultModel(
            Array.Empty<CoreAnalysis.AnalysisTarget>(),
            Array.Empty<CoreAnalysis.AnalysisEdge>(),
            new CoreAnalysis.TypeDependencyGraph(Array.Empty<CoreAnalysis.TypeNodeRef>(), Array.Empty<CoreAnalysis.TypeDependencyEdge>()),
            new CoreAnalysis.FunctionDependencyGraph(Array.Empty<CoreAnalysis.FunctionNodeRef>(), Array.Empty<CoreAnalysis.FunctionDependencyEdge>()),
            new CoreAnalysis.StatementDependencyGraph(Array.Empty<string>(), Array.Empty<CoreAnalysis.StatementDependencyEdge>()),
            CoreCommon.StatementGraphMaterialization.SnapshotOnly,
            CoreCommon.FunctionGraphMaterialization.None);
        var plan = new CorePlanning.AuditPlan(
            new CorePlanning.PlanMetadata("dome", "1", "in", "out", CoreCommon.RunMode.Standard),
            Array.Empty<CorePlanning.PlannedChange>(),
            Array.Empty<CorePlanning.PlanConflict>());

        await writer.WriteAnalysisAsync(Path.Combine(rootPath, "analysis.json"), analysis, CancellationToken.None);
        await writer.WritePlanAsync(Path.Combine(rootPath, "audit-plan.json"), plan, CancellationToken.None);
        await writer.WriteReportAsync(Path.Combine(rootPath, "report.json"), report, CancellationToken.None);

        Assert.True(true);
    }
}




