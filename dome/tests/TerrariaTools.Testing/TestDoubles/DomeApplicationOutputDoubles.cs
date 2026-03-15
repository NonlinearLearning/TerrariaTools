using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeRewriteOutputStore : IRewriteOutputStore
{
    public List<(string OutputRootPath, string RelativePath, string RewrittenSource)> Writes { get; } = [];
    public string? FailureMessage { get; set; }

    public Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken)
    {
        if (FailureMessage != null)
        {
            throw new InvalidOperationException(FailureMessage);
        }

        Writes.Add((outputRootPath, relativePath, rewrittenSource));
        return Task.CompletedTask;
    }
}

public sealed class FakeArtifactEmissionService : IArtifactEmissionService
{
    public List<(string OutputPath, ArtifactPlan ArtifactPlan, AuditPlan? Plan, RunReport Report, AnalysisResultModel? AnalysisView)> Calls { get; } = [];
    public string? FailureMessage { get; set; }

    public Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        AuditPlan? plan,
        RunReport report,
        AnalysisResultModel? analysisView,
        CancellationToken cancellationToken)
    {
        if (FailureMessage != null)
        {
            throw new InvalidOperationException(FailureMessage);
        }

        Calls.Add((outputPath, artifactPlan, plan, report, analysisView));
        return Task.CompletedTask;
    }
}
