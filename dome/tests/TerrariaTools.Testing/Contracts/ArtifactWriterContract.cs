using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Testing.Contracts;

public static class ArtifactWriterContract
{
    public static async Task AssertWritesArtifactsAsync(IArtifactWriter writer)
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

    public static async Task AssertWritesArtifactsAsyncAtPath(IArtifactWriter writer, string rootPath)
    {
        var report = new RunReport(
            true,
            FailureCode.None,
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            null,
            Array.Empty<ConflictSummary>(),
            new RiskSummary(0, Array.Empty<string>()),
            new PlanCoverageSummary(0, 0, Array.Empty<string>()),
            null,
            null,
            null,
            WorkspaceLoadMode.SourceOnly,
            false,
            Array.Empty<WorkspaceLoadDiagnostic>(),
            null);

        var analysis = new AnalysisResultModel(
            Array.Empty<AnalysisTarget>(),
            Array.Empty<AnalysisEdge>(),
            new TypeDependencyGraph(Array.Empty<TypeNodeRef>(), Array.Empty<TypeDependencyEdge>()),
            new FunctionDependencyGraph(Array.Empty<FunctionNodeRef>(), Array.Empty<FunctionDependencyEdge>()),
            new StatementDependencyGraph(Array.Empty<string>(), Array.Empty<StatementDependencyEdge>()),
            StatementGraphMaterialization.SnapshotOnly,
            FunctionGraphMaterialization.None);
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "in", "out", RunMode.Standard),
            Array.Empty<PlannedChange>(),
            Array.Empty<PlanConflict>());

        await writer.WriteAnalysisAsync(Path.Combine(rootPath, "analysis.json"), analysis, CancellationToken.None);
        await writer.WritePlanAsync(Path.Combine(rootPath, "audit-plan.json"), plan, CancellationToken.None);
        await writer.WriteReportAsync(Path.Combine(rootPath, "report.json"), report, CancellationToken.None);

        Assert.True(true);
    }
}
