using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Application;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class RuntimePipelineTypesLegacyTests
{
    [Fact]
    public void TerrariaRuntimePipelineContext_StoresRequestAndAllowsSingleAssignments()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeRunRequest(@"C:\repo\TerrariaServer.sln", @"C:\out");
        var context = new TerrariaRuntimePipelineContext(request);
        var layout = ApplicationAbstractions.TerrariaRuntimeLayout.Create(request);
        var report = CreateRunReport();
        var buildSummary = CreateBuildSummary();

        Assert.Equal(request, context.Request);

        context.SetLayout(layout);
        context.SetReportPath("report.json");
        context.SetReport(report);
        context.UpdateReport(report with { Message = "updated" });
        context.SetBuildSummary(buildSummary);

        Assert.Equal(layout, context.Layout);
        Assert.Equal("report.json", context.ReportPath);
        Assert.Equal("updated", context.Report!.Message);
        Assert.Equal(buildSummary, context.BuildSummary);
    }

    [Fact]
    public void TerrariaRuntimePipelineContext_ThrowsOnDuplicateAssignments()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeRunRequest(@"C:\repo\TerrariaServer.sln", @"C:\out");
        var context = new TerrariaRuntimePipelineContext(request);

        context.SetLayout(ApplicationAbstractions.TerrariaRuntimeLayout.Create(request));
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetLayout(ApplicationAbstractions.TerrariaRuntimeLayout.Create(request))).Message);

        context.SetReportPath("report.json");
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetReportPath("other.json")).Message);

        context.SetReport(CreateRunReport());
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetReport(CreateRunReport())).Message);

        context.SetBuildSummary(CreateBuildSummary());
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetBuildSummary(CreateBuildSummary() with { BuildExitCode = 1 })).Message);
    }

    [Fact]
    public void TerrariaRuntimePipelineContext_BlocksMutationAfterTerminalState()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeRunRequest(@"C:\repo\TerrariaServer.sln", @"C:\out");
        var context = new TerrariaRuntimePipelineContext(request)
        {
            TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success(@"C:\out", "report.json"))
        };

        Assert.Contains("terminal", Assert.Throws<InvalidOperationException>(() => context.SetReportPath("late.json")).Message);
    }

    [Fact]
    public void ShadowExtractionPipelineContext_StoresDerivedValuesAndAllowsSingleAssignments()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(@"C:\repo\TerrariaServer.sln", @"C:\shadow", "Seed");
        var context = new ShadowExtractionPipelineContext(request);
        var input = CreateInputResolution(request);
        var analysis = CreateShadowAnalysis(input);
        var closurePlan = new ShadowClosurePlan(["Sample.cs"], [new ModelPrimitives.MemberId("Sample.Player.Run()")], new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal), 1);
        var writeResult = new ShadowWorkspaceWriteResult(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Sample.cs"] = "class C {}" },
            new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, ["A"], [], []));
        var report = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport("Seed", "Sample.Player.Seed()", ["Sample.cs"], ["Sample.Player.Run()"], new ModelAnalysis.AdvancedAnalysisSummary(), 1, new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, ["A"], [], []));
        var buildSummary = CreateBuildSummary();

        Assert.Equal(request, context.Request);
        Assert.Equal(ApplicationAbstractions.TerrariaRuntimeShadowLayout.Create(request).OutputRootPath, context.OutputRootPath);

        context.SetInputResolution(input);
        context.SetAnalysis(analysis);
        context.SetClosurePlan(closurePlan);
        context.SetWorkspaceWriteResult(writeResult);
        context.SetReport(report);
        context.UpdateReport(report with { SeedMemberName = "UpdatedSeed" });
        context.SetBuildSummary(buildSummary);
        context.SetReportPath("shadow-report.json");

        Assert.Equal(input, context.InputResolution);
        Assert.Equal(analysis, context.Analysis);
        Assert.Equal(closurePlan, context.ClosurePlan);
        Assert.Equal(writeResult, context.WorkspaceWriteResult);
        Assert.Equal("UpdatedSeed", context.Report!.SeedMemberName);
        Assert.Equal(buildSummary, context.BuildSummary);
        Assert.Equal("shadow-report.json", context.ReportPath);
    }

    [Fact]
    public void ShadowExtractionPipelineContext_ThrowsOnDuplicateAssignments()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(@"C:\repo\TerrariaServer.sln", @"C:\shadow", "Seed");
        var context = new ShadowExtractionPipelineContext(request);
        var input = CreateInputResolution(request);

        context.SetInputResolution(input);
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetInputResolution(input)).Message);

        context.SetAnalysis(CreateShadowAnalysis(input));
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetAnalysis(CreateShadowAnalysis(input))).Message);

        var closurePlan = new ShadowClosurePlan([], [], new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal), 0);
        context.SetClosurePlan(closurePlan);
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetClosurePlan(closurePlan)).Message);

        var writeResult = new ShadowWorkspaceWriteResult(new Dictionary<string, string>(StringComparer.Ordinal), new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(0, 0, 0, [], [], []));
        context.SetWorkspaceWriteResult(writeResult);
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetWorkspaceWriteResult(writeResult)).Message);

        var report = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport("Seed", "Sample.Player.Seed()", [], [], new ModelAnalysis.AdvancedAnalysisSummary(), 0, new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(0, 0, 0, [], [], []));
        context.SetReport(report);
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetReport(report)).Message);

        context.SetBuildSummary(CreateBuildSummary());
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetBuildSummary(CreateBuildSummary())).Message);

        context.SetReportPath("shadow-report.json");
        Assert.Contains("already set", Assert.Throws<InvalidOperationException>(() => context.SetReportPath("other.json")).Message);
    }

    [Fact]
    public void ShadowExtractionPipelineContext_BlocksMutationAfterTerminalState()
    {
        var request = new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest(@"C:\repo\TerrariaServer.sln", @"C:\shadow", "Seed");
        var context = new ShadowExtractionPipelineContext(request)
        {
            TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success(@"C:\shadow", "report.json"))
        };

        Assert.Contains("terminal", Assert.Throws<InvalidOperationException>(() => context.SetReportPath("late.json")).Message);
    }

    private static ShadowExtractionInputResolution CreateInputResolution(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request)
    {
        var loadResult = ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ApplicationAbstractions.SourceDocumentSet("Sample.cs", string.Empty, [new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", "class C {}")]),
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            "test");
        return new ShadowExtractionInputResolution(request, ApplicationAbstractions.TerrariaRuntimeShadowLayout.Create(request), loadResult);
    }

    private static ShadowExtractionAnalysis CreateShadowAnalysis(ShadowExtractionInputResolution input) =>
        new(
            input,
            null!,
            null!,
            new ModelAnalysis.FunctionNodeRef(new ModelPrimitives.MemberId("Sample.Player.Seed()"), ModelPrimitives.MemberKind.Method, "Sample.Player", "Seed", "Sample.cs", 0, 4, false, true, true, true, "void"),
            []);

    private static ApplicationAbstractions.TerrariaRuntimeBuildSummary CreateBuildSummary() =>
        new(true, 0, "dotnet build", @"C:\out\workspace", @"C:\out\dependency-env", @"C:\repo\TerrariaServer.sln", "ok", string.Empty);

    private static ApplicationAbstractions.RunReport CreateRunReport() =>
        new(
            true,
            ModelPrimitives.FailureCode.None,
            0,
            0,
            0,
            0,
            [],
            null,
            [],
            new ApplicationAbstractions.RiskSummary(0, []),
            new ApplicationAbstractions.PlanCoverageSummary(0, 0, []),
            null,
            null,
            null,
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            false,
            [],
            null);
}
