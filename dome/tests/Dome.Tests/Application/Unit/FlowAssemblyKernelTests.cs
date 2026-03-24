using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Application.Pipeline;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class FlowAssemblyKernelTests
{
    [Fact]
    public void Build_MissingRequiredSlot_Throws()
    {
        var recipe = new FlowRecipe<TestPipelineContext>(["load", "analyze", "result"]);
        var builder = new FlowBuilder<TestPipelineContext>(recipe);
        builder.Use("load", static () => new DelegatePipelineStage<TestPipelineContext>("load", (_, _) => Task.CompletedTask));
        builder.Use("result", static () => new DelegatePipelineStage<TestPipelineContext>("result", (_, _) => Task.CompletedTask));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("analyze", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_DuplicateSlotReplacement_Throws()
    {
        var recipe = new FlowRecipe<TestPipelineContext>(["load"]);
        var builder = new FlowBuilder<TestPipelineContext>(recipe);
        builder.Use("load", static () => new DelegatePipelineStage<TestPipelineContext>("load-1", (_, _) => Task.CompletedTask));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Use("load", static () => new DelegatePipelineStage<TestPipelineContext>("load-2", (_, _) => Task.CompletedTask)));

        Assert.Contains("load", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Use_UnknownRecipeSlot_Throws()
    {
        var recipe = new FlowRecipe<TestPipelineContext>(["load"]);
        var builder = new FlowBuilder<TestPipelineContext>(recipe);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Use("analyze", static () => new DelegatePipelineStage<TestPipelineContext>("analyze", (_, _) => Task.CompletedTask)));

        Assert.Contains("analyze", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decorate_UnknownRecipeSlot_Throws()
    {
        var recipe = new FlowRecipe<TestPipelineContext>(["load"]);
        var builder = new FlowBuilder<TestPipelineContext>(recipe);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Decorate(
                "result",
                inner => new DecoratorPipelineStage<TestPipelineContext>(
                    "decorator",
                    inner,
                    static _ => { },
                    static _ => { })));

        Assert.Contains("result", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Build_Decorators_AreAppliedInRegistrationOrder()
    {
        var recipe = new FlowRecipe<TestPipelineContext>(["load"]);
        var builder = new FlowBuilder<TestPipelineContext>(recipe);
        builder.Use("load", static () => new DelegatePipelineStage<TestPipelineContext>("load", (context, _) =>
        {
            context.VisitedStages.Add("core");
            return Task.CompletedTask;
        }));
        builder.Decorate("load", inner => new DecoratorPipelineStage<TestPipelineContext>(
            "decorator-a",
            inner,
            static context => context.VisitedStages.Add("before-a"),
            static context => context.VisitedStages.Add("after-a")));
        builder.Decorate("load", inner => new DecoratorPipelineStage<TestPipelineContext>(
            "decorator-b",
            inner,
            static context => context.VisitedStages.Add("before-b"),
            static context => context.VisitedStages.Add("after-b")));

        var context = new TestPipelineContext();
        var runner = new PipelineRunner<TestPipelineContext>(builder.Build());

        await runner.RunAsync(context, CancellationToken.None);

        Assert.Equal(["before-a", "before-b", "core", "after-b", "after-a"], context.VisitedStages);
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

    private sealed class DecoratorPipelineStage<TContext>(
        string stageName,
        IPipelineStage<TContext> inner,
        Action<TContext> before,
        Action<TContext> after) : PipelineStage<TContext>
        where TContext : class, IPipelineContext
    {
        public override string StageName => stageName;

        public override async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
        {
            before(context);
            await inner.ExecuteAsync(context, cancellationToken);
            after(context);
        }
    }
}
