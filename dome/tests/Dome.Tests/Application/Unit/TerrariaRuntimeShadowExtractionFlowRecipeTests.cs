using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class TerrariaRuntimeShadowExtractionFlowRecipeTests
{
    [Fact]
    public void Standard_UsesResolveAnalyzeBuildClosureWriteWorkspaceBuildPersistOrder()
    {
        var recipe = TerrariaRuntimeShadowExtractionFlowRecipes.Standard(CreateSlots());

        Assert.Equal(
            ["resolve-input", "analyze", "build-closure", "write-workspace", "build", "persist"],
            recipe.RequiredSlots);
    }

    [Fact]
    public void BuildStages_UsesShadowExtractionPipelineContext()
    {
        var recipe = TerrariaRuntimeShadowExtractionFlowRecipes.Standard(CreateSlots());

        var stages = TerrariaRuntimeShadowExtractionFlowRecipes.BuildStages(recipe);

        Assert.All(stages, stage => Assert.IsAssignableFrom<IPipelineStage<ShadowExtractionPipelineContext>>(stage));
    }

    [Fact]
    public async Task BuildStages_InvokesClosureBeforeWorkspaceWrite()
    {
        var events = new List<string>();
        var recipe = TerrariaRuntimeShadowExtractionFlowRecipes.Standard(CreateRecordingSlots(events));
        var stages = TerrariaRuntimeShadowExtractionFlowRecipes.BuildStages(recipe);
        var runner = new PipelineRunner<ShadowExtractionPipelineContext>(stages);
        var context = new ShadowExtractionPipelineContext(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed"));

        await runner.RunAsync(context, CancellationToken.None);

        Assert.True(
            events.FindIndex(static value => string.Equals(value, "build-closure", StringComparison.Ordinal)) <
            events.FindIndex(static value => string.Equals(value, "write-workspace", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BuildStages_KeepsBuildAndPersistAsTerminalSteps()
    {
        var events = new List<string>();
        var recipe = TerrariaRuntimeShadowExtractionFlowRecipes.Standard(CreateRecordingSlots(events));
        var stages = TerrariaRuntimeShadowExtractionFlowRecipes.BuildStages(recipe);
        var runner = new PipelineRunner<ShadowExtractionPipelineContext>(stages);
        var context = new ShadowExtractionPipelineContext(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed"));

        await runner.RunAsync(context, CancellationToken.None);

        Assert.Equal(["build", "persist"], events.TakeLast(2).ToArray());
        Assert.NotNull(context.TerminalState);
    }

    private static TerrariaRuntimeShadowExtractionFlowSlots CreateSlots() =>
        new(
            new StubResolveInputSlot(),
            new StubAnalyzeSlot(),
            new StubBuildClosureSlot(),
            new StubWriteWorkspaceSlot(),
            new StubBuildSlot(),
            new StubPersistSlot());

    private static TerrariaRuntimeShadowExtractionFlowSlots CreateRecordingSlots(List<string> events) =>
        new(
            new RecordingResolveInputSlot(events),
            new RecordingAnalyzeSlot(events),
            new RecordingBuildClosureSlot(events),
            new RecordingWriteWorkspaceSlot(events),
            new RecordingBuildSlot(events),
            new RecordingPersistSlot(events));

    private static ShadowExtractionInputResolution CreateInputResolution(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request)
    {
        var layout = new ApplicationAbstractions.TerrariaRuntimeShadowLayout(
            request.SolutionPath,
            "source",
            request.OutputRootPath,
            "workspace",
            "artifacts",
            "dependency",
            "workspace\\input.sln");
        var source = new ModelAnalysis.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var loadResult = ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ModelAnalysis.AnalysisInput(
                new ModelAnalysis.SourceDocumentSet("Main.cs", string.Empty, [source]),
                ModelAnalysis.AnalysisInputMode.SourceOnly),
            ApplicationAbstractions.WorkspaceLoadMode.SourceOnly,
            "Stub");
        return new ShadowExtractionInputResolution(request, layout, loadResult);
    }

    private static ShadowExtractionAnalysis CreateAnalysis(ShadowExtractionInputResolution inputResolution)
    {
        var source = new ModelAnalysis.SourceDocument("Main.cs", "Main.cs", "class C {}");
        var output = new ApplicationAnalysisCompatibilityResultBuilder().BuildEngineResult(source);
        return new ShadowExtractionAnalysis(inputResolution, output);
    }

    private static ShadowClosurePlan CreateClosurePlan() =>
        new(
            new ModelAnalysis.FunctionNodeRef(
                new ModelPrimitives.MemberId("Sample.Main.Run()"),
                ModelPrimitives.MemberKind.Method,
                "Sample.Main",
                "Run",
                "Main.cs",
                0,
                0,
                false,
                true,
                true,
                true,
                "void"),
            ["Main.cs"],
            [new ModelPrimitives.MemberId("Sample.Main.Run()")],
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
            1);

    private static ShadowWorkspaceWriteResult CreateWriteResult() =>
        new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Main.cs"] = "code" },
            new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, ["A"], [], []));

    private static ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport CreateReport() =>
        new(
            "Seed",
            "Sample.Main.Run()",
            ["Main.cs"],
            ["Sample.Main.Run()"],
            new ModelAnalysis.AdvancedAnalysisSummary(),
            1,
            new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(1, 0, 0, [], [], []));

    private sealed class StubResolveInputSlot : ITerrariaRuntimeShadowResolveInputSlot
    {
        public Task<ShadowResolveInputOutput> ExecuteAsync(ShadowResolveInputInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAnalyzeSlot : ITerrariaRuntimeShadowAnalyzeSlot
    {
        public Task<ShadowAnalyzeOutput> ExecuteAsync(ShadowAnalyzeInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubBuildClosureSlot : ITerrariaRuntimeShadowBuildClosureSlot
    {
        public Task<ShadowBuildClosureOutput> ExecuteAsync(ShadowBuildClosureInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubWriteWorkspaceSlot : ITerrariaRuntimeShadowWriteWorkspaceSlot
    {
        public Task<ShadowWriteWorkspaceOutput> ExecuteAsync(ShadowWriteWorkspaceInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubBuildSlot : ITerrariaRuntimeShadowBuildSlot
    {
        public Task<ShadowBuildOutput> ExecuteAsync(ShadowBuildInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubPersistSlot : ITerrariaRuntimeShadowPersistSlot
    {
        public Task<ModelExecution.RunResult> ExecuteAsync(ShadowPersistInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingResolveInputSlot(List<string> events) : ITerrariaRuntimeShadowResolveInputSlot
    {
        public Task<ShadowResolveInputOutput> ExecuteAsync(ShadowResolveInputInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("resolve-input");
            return Task.FromResult(new ShadowResolveInputOutput(CreateInputResolution(input.Request)));
        }
    }

    private sealed class RecordingAnalyzeSlot(List<string> events) : ITerrariaRuntimeShadowAnalyzeSlot
    {
        public Task<ShadowAnalyzeOutput> ExecuteAsync(ShadowAnalyzeInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("analyze");
            return Task.FromResult(new ShadowAnalyzeOutput(CreateAnalysis(input.ResolveInput.InputResolution!)));
        }
    }

    private sealed class RecordingBuildClosureSlot(List<string> events) : ITerrariaRuntimeShadowBuildClosureSlot
    {
        public Task<ShadowBuildClosureOutput> ExecuteAsync(ShadowBuildClosureInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("build-closure");
            return Task.FromResult(new ShadowBuildClosureOutput(input.Analyze.Analysis, CreateClosurePlan()));
        }
    }

    private sealed class RecordingWriteWorkspaceSlot(List<string> events) : ITerrariaRuntimeShadowWriteWorkspaceSlot
    {
        public Task<ShadowWriteWorkspaceOutput> ExecuteAsync(ShadowWriteWorkspaceInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("write-workspace");
            return Task.FromResult(new ShadowWriteWorkspaceOutput(input.BuildClosure, CreateWriteResult()));
        }
    }

    private sealed class RecordingBuildSlot(List<string> events) : ITerrariaRuntimeShadowBuildSlot
    {
        public Task<ShadowBuildOutput> ExecuteAsync(ShadowBuildInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("build");
            return Task.FromResult(
                new ShadowBuildOutput(
                    input.WriteWorkspace,
                    CreateReport(),
                    new ApplicationAbstractions.TerrariaRuntimeBuildSummary(
                        true,
                        0,
                        "dotnet build",
                        "workspace",
                        "dependency",
                        "workspace\\input.sln",
                        string.Empty,
                        string.Empty)));
        }
    }

    private sealed class RecordingPersistSlot(List<string> events) : ITerrariaRuntimeShadowPersistSlot
    {
        public Task<ModelExecution.RunResult> ExecuteAsync(ShadowPersistInput input, ApplicationAbstractions.IFlowExecutionContext executionContext, CancellationToken cancellationToken)
        {
            events.Add("persist");
            return Task.FromResult(ModelExecution.RunResult.Success("out", "artifacts\\shadow-report.json"));
        }
    }
}
