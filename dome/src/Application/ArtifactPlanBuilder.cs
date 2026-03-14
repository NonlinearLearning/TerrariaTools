namespace TerrariaTools.Dome.Application;

public sealed record ArtifactPlan(
    bool WriteAnalysis,
    bool WritePlan,
    bool WriteReport,
    IReadOnlyList<string> GeneratedArtifacts,
    IReadOnlyList<string> RewrittenDocuments);

public sealed class ArtifactPlanBuilder
{
    public ArtifactPlan BuildWorkspaceLoadFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    public ArtifactPlan BuildAnalysisFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    public ArtifactPlan BuildAnalyzeOnlySuccess() =>
        new(true, false, true, new[] { "analysis.json", "report.json" }, Array.Empty<string>());

    public ArtifactPlan BuildPlanCompileFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    public ArtifactPlan BuildPlanOnlySuccess() =>
        new(false, true, true, new[] { "audit-plan.json", "report.json" }, Array.Empty<string>());

    public ArtifactPlan BuildRewriteFailure(IReadOnlyList<string> rewrittenDocuments) =>
        new(
            false,
            true,
            true,
            new[] { "audit-plan.json" }
                .Concat(rewrittenDocuments)
                .Append("report.json")
                .ToArray(),
            rewrittenDocuments.ToArray());

    public ArtifactPlan BuildStandardSuccess(IReadOnlyList<string> rewrittenDocuments) =>
        new(
            false,
            true,
            true,
            new[] { "audit-plan.json" }
                .Concat(rewrittenDocuments)
                .Append("report.json")
                .ToArray(),
            rewrittenDocuments.ToArray());
}
