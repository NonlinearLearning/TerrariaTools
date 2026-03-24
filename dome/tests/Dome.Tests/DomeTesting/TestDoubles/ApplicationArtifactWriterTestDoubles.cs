using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Dome.Tests.Testing.TestDoubles;

public sealed class RecordingApplicationArtifactWriter : ApplicationAbstractions.IArtifactWriter
{
    public List<(string Path, ModelAnalysis.AnalysisResultModel View)> AnalysisWrites { get; } = [];

    public List<(string Path, ModelPlanning.AuditPlan Plan)> PlanWrites { get; } = [];

    public List<(string Path, ModelExecution.RunReport Report)> ReportWrites { get; } = [];

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

    public Task WriteReportAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken)
    {
        ReportWrites.Add((path, report));
        return Task.CompletedTask;
    }
}




