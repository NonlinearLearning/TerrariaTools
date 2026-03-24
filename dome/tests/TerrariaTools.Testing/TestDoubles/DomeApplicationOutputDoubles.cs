using TerrariaTools.Dome.Adapters.Runtime.Process;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

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
    public List<(string OutputPath, ArtifactPlan ArtifactPlan, CorePlanning.AuditPlan? Plan, ModelExecution.RunReport Report, CoreAnalysis.AnalysisResultModel? AnalysisView)> Calls { get; } = [];
    public string? FailureMessage { get; set; }

    public Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        CorePlanning.AuditPlan? plan,
        ModelExecution.RunReport report,
        CoreAnalysis.AnalysisResultModel? analysisView,
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




