using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class RecordingArtifactWriter : IArtifactWriter
{
    public List<(string Path, AnalysisResultModel View)> AnalysisWrites { get; } = [];

    public List<(string Path, AuditPlan Plan)> PlanWrites { get; } = [];

    public List<(string Path, RunReport Report)> ReportWrites { get; } = [];

    public Task WriteAnalysisAsync(string path, AnalysisResultModel view, CancellationToken cancellationToken)
    {
        AnalysisWrites.Add((path, view));
        return Task.CompletedTask;
    }

    public Task WritePlanAsync(string path, AuditPlan plan, CancellationToken cancellationToken)
    {
        PlanWrites.Add((path, plan));
        return Task.CompletedTask;
    }

    public Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken)
    {
        ReportWrites.Add((path, report));
        return Task.CompletedTask;
    }
}
