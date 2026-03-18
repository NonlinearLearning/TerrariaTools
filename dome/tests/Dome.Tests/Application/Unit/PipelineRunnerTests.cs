using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using TerrariaTools.Dome.Application;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task RunAsync_ExecutesStagesInOrder()
    {
        var context = new TestPipelineContext();
        var runner = new PipelineRunner<TestPipelineContext>(
        [
            new DelegatePipelineStage<TestPipelineContext>("one", (ctx, _) =>
            {
                ctx.VisitedStages.Add("one");
                return Task.CompletedTask;
            }),
            new DelegatePipelineStage<TestPipelineContext>("two", (ctx, _) =>
            {
                ctx.VisitedStages.Add("two");
                return Task.CompletedTask;
            })
        ]);

        await runner.RunAsync(context, CancellationToken.None);

        Assert.Equal(["one", "two"], context.VisitedStages);
        Assert.Equal(["one", "two"], context.StageTraces.Select(static trace => trace.StageName).ToArray());
    }

    [Fact]
    public async Task RunAsync_StopsAfterContextBecomesTerminal()
    {
        var context = new TestPipelineContext();
        var runner = new PipelineRunner<TestPipelineContext>(
        [
            new DelegatePipelineStage<TestPipelineContext>("one", (ctx, _) =>
            {
                ctx.VisitedStages.Add("one");
                ctx.TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success("out", "report.json"));
                return Task.CompletedTask;
            }),
            new DelegatePipelineStage<TestPipelineContext>("two", (ctx, _) =>
            {
                ctx.VisitedStages.Add("two");
                return Task.CompletedTask;
            })
        ]);

        await runner.RunAsync(context, CancellationToken.None);

        Assert.Equal(["one"], context.VisitedStages);
        Assert.Single(context.StageTraces);
    }

    [Fact]
    public async Task RunAsync_PropagatesStageExceptions()
    {
        var context = new TestPipelineContext();
        var runner = new PipelineRunner<TestPipelineContext>(
        [
            new DelegatePipelineStage<TestPipelineContext>("boom", (_, _) => throw new InvalidOperationException("broken"))
        ]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(context, CancellationToken.None));

        Assert.Equal("broken", ex.Message);
        var trace = Assert.Single(context.StageTraces);
        Assert.True(trace.Failed);
        Assert.Equal("boom", trace.StageName);
    }

    [Fact]
    public void TerminalState_SetTwice_Throws()
    {
        var context = new TestPipelineContext();
        context.BeginStage("one", 0);
        context.TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success("out", "report.json"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            context.TerminalState = new PipelineTerminalState(ApplicationAbstractions.RunResult.Success("out", "report.json")));

        Assert.Contains("already terminal", ex.Message);
    }

    private sealed class TestPipelineContext : PipelineContextBase
    {
        public List<string> VisitedStages { get; } = [];
    }

    private sealed class DelegatePipelineStage<TContext> : PipelineStage<TContext>
        where TContext : class, IPipelineContext
    {
        private readonly Func<TContext, CancellationToken, Task> _handler;

        public DelegatePipelineStage(string stageName, Func<TContext, CancellationToken, Task> handler)
        {
            StageName = stageName;
            _handler = handler;
        }

        public override string StageName { get; }

        public override Task ExecuteAsync(TContext context, CancellationToken cancellationToken) => _handler(context, cancellationToken);
    }
}
