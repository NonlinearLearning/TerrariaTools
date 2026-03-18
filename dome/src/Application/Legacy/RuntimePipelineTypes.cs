namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

// Runtime and shadow extraction remain the explicit Application-layer legacy/Core boundary.
// Standard DomeApplication pipeline must not depend on these pipeline contexts.

internal sealed class TerrariaRuntimePipelineContext : PipelineContextBase
{
    public TerrariaRuntimePipelineContext(ApplicationAbstractions.TerrariaRuntimeRunRequest request)
    {
        Request = request;
    }

    public ApplicationAbstractions.TerrariaRuntimeRunRequest Request { get; }

    public ApplicationAbstractions.TerrariaRuntimeLayout? Layout { get; private set; }

    public string? ReportPath { get; private set; }

    public ApplicationAbstractions.RunReport? Report { get; private set; }

    public ApplicationAbstractions.TerrariaRuntimeBuildSummary? BuildSummary { get; private set; }

    internal void SetLayout(ApplicationAbstractions.TerrariaRuntimeLayout layout)
    {
        EnsureCanMutate("set runtime layout");
        Layout = Layout == null ? layout : throw new InvalidOperationException("Runtime layout is already set.");
    }

    internal void SetReportPath(string reportPath)
    {
        EnsureCanMutate("set report path");
        ReportPath = ReportPath == null ? reportPath : throw new InvalidOperationException("Report path is already set.");
    }

    internal void SetReport(ApplicationAbstractions.RunReport report)
    {
        EnsureCanMutate("set report");
        Report = Report == null ? report : throw new InvalidOperationException("Report is already set.");
    }

    internal void UpdateReport(ApplicationAbstractions.RunReport report)
    {
        EnsureCanMutate("update report");
        Report = report;
    }

    internal void SetBuildSummary(ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary)
    {
        EnsureCanMutate("set build summary");
        BuildSummary = BuildSummary == null ? buildSummary : throw new InvalidOperationException("Build summary is already set.");
    }
}

internal sealed class ShadowExtractionPipelineContext : PipelineContextBase
{
    public ShadowExtractionPipelineContext(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request)
    {
        Request = request;
        OutputRootPath = ApplicationAbstractions.TerrariaRuntimeShadowLayout.Create(request).OutputRootPath;
    }

    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest Request { get; }

    public string OutputRootPath { get; }

    public ShadowExtractionInputResolution? InputResolution { get; private set; }

    public ShadowExtractionAnalysis? Analysis { get; private set; }

    public ShadowClosurePlan? ClosurePlan { get; private set; }

    public ShadowWorkspaceWriteResult? WorkspaceWriteResult { get; private set; }

    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport? Report { get; private set; }

    public ApplicationAbstractions.TerrariaRuntimeBuildSummary? BuildSummary { get; private set; }

    public string? ReportPath { get; private set; }

    internal void SetInputResolution(ShadowExtractionInputResolution inputResolution)
    {
        EnsureCanMutate("set input resolution");
        InputResolution = InputResolution == null ? inputResolution : throw new InvalidOperationException("Input resolution is already set.");
    }

    internal void SetAnalysis(ShadowExtractionAnalysis analysis)
    {
        EnsureCanMutate("set analysis");
        Analysis = Analysis == null ? analysis : throw new InvalidOperationException("Analysis is already set.");
    }

    internal void SetClosurePlan(ShadowClosurePlan closurePlan)
    {
        EnsureCanMutate("set closure plan");
        ClosurePlan = ClosurePlan == null ? closurePlan : throw new InvalidOperationException("Closure plan is already set.");
    }

    internal void SetWorkspaceWriteResult(ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        EnsureCanMutate("set workspace write result");
        WorkspaceWriteResult = WorkspaceWriteResult == null ? workspaceWriteResult : throw new InvalidOperationException("Workspace write result is already set.");
    }

    internal void SetReport(ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report)
    {
        EnsureCanMutate("set report");
        Report = Report == null ? report : throw new InvalidOperationException("Report is already set.");
    }

    internal void UpdateReport(ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report)
    {
        EnsureCanMutate("update report");
        Report = report;
    }

    internal void SetBuildSummary(ApplicationAbstractions.TerrariaRuntimeBuildSummary buildSummary)
    {
        EnsureCanMutate("set build summary");
        BuildSummary = BuildSummary == null ? buildSummary : throw new InvalidOperationException("Build summary is already set.");
    }

    internal void SetReportPath(string reportPath)
    {
        EnsureCanMutate("set report path");
        ReportPath = ReportPath == null ? reportPath : throw new InvalidOperationException("Report path is already set.");
    }
}
