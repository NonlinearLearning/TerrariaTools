using TerrariaTools.Dome.Application;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;

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
    public List<(string OutputPath, ArtifactPlan ArtifactPlan, ModelPlanning.AuditPlan? Plan, ApplicationAbstractions.RunReport Report, ModelAnalysis.AnalysisResultModel? AnalysisView)> Calls { get; } = [];
    public string? FailureMessage { get; set; }

    public Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        ModelPlanning.AuditPlan? plan,
        ApplicationAbstractions.RunReport report,
        ModelAnalysis.AnalysisResultModel? analysisView,
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
