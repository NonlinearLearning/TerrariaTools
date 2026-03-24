using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class RecordingArtifactCompatibilityWriter : ApplicationAbstractions.IArtifactWriter
{
    public List<(string Path, CoreAnalysis.AnalysisResultModel View)> AnalysisWrites { get; } = [];

    public List<(string Path, CorePlanning.AuditPlan Plan)> PlanWrites { get; } = [];

    public List<(string Path, ModelExecution.RunReport Report)> ReportWrites { get; } = [];

    public Task WriteAnalysisAsync(string path, CoreAnalysis.AnalysisResultModel view, CancellationToken cancellationToken)
    {
        AnalysisWrites.Add((path, view));
        return Task.CompletedTask;
    }

    public Task WritePlanAsync(string path, CorePlanning.AuditPlan plan, CancellationToken cancellationToken)
    {
        PlanWrites.Add((path, plan));
        return Task.CompletedTask;
    }

    public Task WriteReportAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken)
    {
        ReportWrites.Add((path, report));
        return Task.CompletedTask;
    }
}
