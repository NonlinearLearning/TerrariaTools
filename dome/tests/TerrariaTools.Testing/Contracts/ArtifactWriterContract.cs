using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
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
        var report = new ApplicationAbstractions.RunReport(
            true,
            ModelPrimitives.FailureCode.None,
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            null,
            Array.Empty<ApplicationAbstractions.ConflictSummary>(),
            new ApplicationAbstractions.RiskSummary(0, Array.Empty<string>()),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            false,
            Array.Empty<ApplicationAbstractions.WorkspaceLoadDiagnostic>(),
            null);

        var analysis = new ModelAnalysis.AnalysisResultModel(
            Array.Empty<ModelAnalysis.AnalysisTarget>(),
            Array.Empty<ModelAnalysis.AnalysisEdge>(),
            new ModelAnalysis.TypeDependencyGraph(Array.Empty<ModelAnalysis.TypeNodeRef>(), Array.Empty<ModelAnalysis.TypeDependencyEdge>()),
            new ModelAnalysis.FunctionDependencyGraph(Array.Empty<ModelAnalysis.FunctionNodeRef>(), Array.Empty<ModelAnalysis.FunctionDependencyEdge>()),
            new ModelAnalysis.StatementDependencyGraph(Array.Empty<string>(), Array.Empty<ModelAnalysis.StatementDependencyEdge>()),
            ModelPrimitives.StatementGraphMaterialization.SnapshotOnly,
            ModelPrimitives.FunctionGraphMaterialization.None);
        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "in", "out", ModelPrimitives.RunMode.Standard),
            Array.Empty<ModelPlanning.PlannedChange>(),
            Array.Empty<ModelPlanning.PlanConflict>());

        await writer.WriteAnalysisAsync(Path.Combine(rootPath, "analysis.json"), analysis, CancellationToken.None);
        await writer.WritePlanAsync(Path.Combine(rootPath, "audit-plan.json"), plan, CancellationToken.None);
        await writer.WriteReportAsync(Path.Combine(rootPath, "report.json"), report, CancellationToken.None);

        Assert.True(true);
    }
}
