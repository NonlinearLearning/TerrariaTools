namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;

public interface IRewriteOutputStore
{
    Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken);
}

public sealed class FileSystemRewriteOutputStore : IRewriteOutputStore
{
    public async Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(outputRootPath, "rewritten", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, rewrittenSource, cancellationToken);
    }
}

public interface IArtifactEmissionService
{
    Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        AuditPlan? plan,
        RunReport report,
        AnalysisResultModel? analysisView,
        CancellationToken cancellationToken);
}

public sealed class ArtifactEmissionService(IArtifactWriter artifactWriter) : IArtifactEmissionService
{
    public async Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        AuditPlan? plan,
        RunReport report,
        AnalysisResultModel? analysisView,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPath);

        if (artifactPlan.WriteAnalysis && analysisView != null)
        {
            await artifactWriter.WriteAnalysisAsync(Path.Combine(outputPath, "analysis.json"), analysisView, cancellationToken);
        }

        if (artifactPlan.WritePlan && plan != null)
        {
            await artifactWriter.WritePlanAsync(Path.Combine(outputPath, "audit-plan.json"), plan, cancellationToken);
        }

        if (artifactPlan.WriteReport)
        {
            await artifactWriter.WriteReportAsync(Path.Combine(outputPath, "report.json"), report, cancellationToken);
        }
    }
}
