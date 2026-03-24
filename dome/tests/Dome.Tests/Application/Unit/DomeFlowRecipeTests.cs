using TerrariaTools.Dome.Application.Composition;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Core.Analysis;
using TerrariaTools.Dome.Core.Planning;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeFlowRecipeTests
{
    [Fact]
    public void AnalyzeOnly_UsesLoadAnalyzeResultOrder()
    {
        var slots = CreateSlots();

        var recipe = DomeFlowRecipes.AnalyzeOnly(slots);

        Assert.Equal(["load", "analyze", "result"], recipe.RequiredSlots);
    }

    [Fact]
    public void PlanOnly_UsesLoadAnalyzeRuleDecisionResultOrder()
    {
        var slots = CreateSlots();

        var recipe = DomeFlowRecipes.PlanOnly(slots);

        Assert.Equal(["load", "analyze", "rule", "decision", "result"], recipe.RequiredSlots);
    }

    [Fact]
    public void Standard_UsesLoadAnalyzeRuleDecisionResultOrder()
    {
        var slots = CreateSlots();

        var recipe = DomeFlowRecipes.Standard(slots);

        Assert.Equal(["load", "analyze", "rule", "decision", "result"], recipe.RequiredSlots);
    }

    [Fact]
    public void ReplaceAnalyze_SwapsOnlyTheAnalyzeSlot()
    {
        var slots = CreateSlots();
        var recipe = DomeFlowRecipes.Standard(slots);
        var replacement = new StubAnalyzeSlot();

        var replaced = recipe.ReplaceAnalyze(replacement);

        Assert.Same(slots.Load, replaced.Load);
        Assert.Same(replacement, replaced.Analyze);
        Assert.Same(slots.Rule, replaced.Rule);
        Assert.Same(slots.Decision, replaced.Decision);
        Assert.Same(slots.Result, replaced.Result);
    }

    [Fact]
    public void DecorateResult_WrapsOnlyTheResultSlot()
    {
        var slots = CreateSlots();
        var recipe = DomeFlowRecipes.Standard(slots);

        var decorated = recipe.DecorateResult(inner => new DecoratingResultSlot(inner));

        Assert.Same(slots.Load, decorated.Load);
        Assert.Same(slots.Analyze, decorated.Analyze);
        Assert.Same(slots.Rule, decorated.Rule);
        Assert.Same(slots.Decision, decorated.Decision);
        Assert.IsType<DecoratingResultSlot>(decorated.BuildResultSlot());
    }

    [Fact]
    public void ReplaceAnalyze_AdapterCanHidePrivateModelsBehindStableContract()
    {
        var recipe = DomeFlowRecipes.Standard(CreateSlots());
        var replacement = new SpecialAnalyzeSlotAdapter(new StubSpecialAnalyzeEngine());

        var replaced = recipe.ReplaceAnalyze(replacement);
        var executeMethod = replaced.Analyze.GetType().GetMethod(nameof(IAnalyzeSlot.ExecuteAsync));

        Assert.NotNull(executeMethod);
        Assert.IsAssignableFrom<IAnalyzeSlot>(replaced.Analyze);
        Assert.IsAssignableFrom<IRuleSlot>(replaced.Rule);
        Assert.IsAssignableFrom<IDecisionSlot>(replaced.Decision);
        Assert.IsAssignableFrom<IResultSlot>(replaced.Result);
        Assert.Collection(
            executeMethod!.GetParameters(),
            parameter => Assert.Equal(typeof(AnalyzeInput), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IFlowExecutionContext), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
        Assert.Equal(typeof(Task<AnalyzeOutput>), executeMethod.ReturnType);
    }

    [Fact]
    public void DecorateResult_BuildStages_KeepsFixedTopology()
    {
        var recipe = DomeFlowRecipes.Standard(CreateSlots())
            .DecorateResult(inner => new DecoratingResultSlot(inner));

        var stages = DomeFlowRecipes.BuildStages(
            recipe,
            new RunReportBuilder(),
            new ArtifactPlanBuilder(),
            new NullArtifactEmissionService(),
            new NullDomeProgressReporter());

        Assert.Equal(["LoadSlotStage", "AnalyzeSlotStage", "RuleSlotStage", "DecisionSlotStage", "ResultSlotStage"], stages.Select(stage => stage.GetType().Name).ToArray());
    }

    private static DomeFlowSlots CreateSlots() =>
        new(
            new StubLoadSlot(),
            new StubAnalyzeSlot(),
            new StubRuleSlot(),
            new StubDecisionSlot(),
            new StubResultSlot());

    private sealed class StubLoadSlot : ILoadSlot
    {
        public Task<LoadOutput> ExecuteAsync(LoadInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAnalyzeSlot : IAnalyzeSlot
    {
        public Task<AnalyzeOutput> ExecuteAsync(AnalyzeInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubRuleSlot : IRuleSlot
    {
        public Task<RuleOutput> ExecuteAsync(RuleInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubDecisionSlot : IDecisionSlot
    {
        public Task<DecisionOutput> ExecuteAsync(DecisionInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubResultSlot : IResultSlot
    {
        public Task<RunResult> ExecuteAsync(ResultInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DecoratingResultSlot(IResultSlot inner) : IResultSlot
    {
        public IResultSlot Inner { get; } = inner;

        public Task<RunResult> ExecuteAsync(ResultInput input, IFlowExecutionContext executionContext, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(input, executionContext, cancellationToken);
    }

    private sealed class NullArtifactEmissionService : IArtifactEmissionService
    {
        public Task EmitAsync(
            string outputPath,
            ArtifactPlan artifactPlan,
            AuditPlan? plan,
            RunReport report,
            AnalysisResultModel? analysisView,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NullDomeProgressReporter : IDomeProgressReporter
    {
        public void Report(string message)
        {
        }
    }

    private interface ISpecialAnalyzeEngine
    {
        Task<SpecialAnalyzeResponse> ExecuteAsync(
            SpecialAnalyzeRequest request,
            CancellationToken cancellationToken);
    }

    private sealed record SpecialAnalyzeRequest(string InputPath, int DocumentCount);

    private sealed record SpecialAnalyzeResponse(AnalyzeOutput Output);

    private sealed class StubSpecialAnalyzeEngine : ISpecialAnalyzeEngine
    {
        public Task<SpecialAnalyzeResponse> ExecuteAsync(
            SpecialAnalyzeRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SpecialAnalyzeSlotAdapter(ISpecialAnalyzeEngine inner) : IAnalyzeSlot
    {
        public async Task<AnalyzeOutput> ExecuteAsync(
            AnalyzeInput input,
            IFlowExecutionContext executionContext,
            CancellationToken cancellationToken)
        {
            var request = new SpecialAnalyzeRequest(
                input.Load.Request.InputPath,
                input.Load.Workspace.Documents.Count);
            var response = await inner.ExecuteAsync(request, cancellationToken);
            return response.Output;
        }
    }
}
