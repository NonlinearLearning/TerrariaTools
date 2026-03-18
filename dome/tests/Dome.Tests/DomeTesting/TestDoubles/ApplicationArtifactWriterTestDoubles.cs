using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

public sealed class RecordingApplicationArtifactWriter : ApplicationAbstractions.IArtifactWriter
{
    public List<(string Path, ModelAnalysis.AnalysisResultModel View)> AnalysisWrites { get; } = [];

    public List<(string Path, ModelPlanning.AuditPlan Plan)> PlanWrites { get; } = [];

    public List<(string Path, ApplicationAbstractions.RunReport Report)> ReportWrites { get; } = [];

    public Task WriteAnalysisAsync(string path, ModelAnalysis.AnalysisResultModel view, CancellationToken cancellationToken)
    {
        AnalysisWrites.Add((path, view));
        return Task.CompletedTask;
    }

    public Task WritePlanAsync(string path, ModelPlanning.AuditPlan plan, CancellationToken cancellationToken)
    {
        PlanWrites.Add((path, plan));
        return Task.CompletedTask;
    }

    public Task WriteReportAsync(string path, ApplicationAbstractions.RunReport report, CancellationToken cancellationToken)
    {
        ReportWrites.Add((path, report));
        return Task.CompletedTask;
    }
}
