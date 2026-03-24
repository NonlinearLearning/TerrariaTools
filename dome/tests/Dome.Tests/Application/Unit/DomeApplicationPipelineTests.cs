using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationPipelineTests
{
    [Fact]
    public async Task RunAsync_DelegatesToPipelineRunnerAndReturnsTerminalResult()
    {
        var observedRequests = new List<ApplicationAbstractions.RunRequest>();
        var runner = new DelegatePipelineRunner<DomePipelineContext>((context, _) =>
        {
            observedRequests.Add(context.Request);
            context.TerminalState = new PipelineTerminalState(ModelExecution.RunResult.Success(context.Request.OutputPath, Path.Combine(context.Request.OutputPath, "report.json")));
            return Task.CompletedTask;
        });
        var app = new DomeApplication(runner);
        var request = new ApplicationAbstractions.RunRequest("input", "out", Array.Empty<string>(), ModelPrimitives.RunMode.PlanOnly);

        var result = await app.RunAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(observedRequests);
        Assert.Equal(request.InputPath, observedRequests[0].InputPath);
        Assert.Equal(request.OutputPath, observedRequests[0].OutputPath);
        Assert.Equal(ModelPrimitives.RunMode.PlanOnly, observedRequests[0].Mode);
        Assert.Equal("out", result.OutputPath);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenPipelineDoesNotProduceTerminalResult()
    {
        var app = new DomeApplication(new DelegatePipelineRunner<DomePipelineContext>((_, _) => Task.CompletedTask));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.RunAsync(new ApplicationAbstractions.RunRequest("input", "out", Array.Empty<string>(), ModelPrimitives.RunMode.Standard), CancellationToken.None));

        Assert.Equal("Dome pipeline completed without producing a terminal result.", ex.Message);
    }

    private sealed class DelegatePipelineRunner<TContext>(Func<TContext, CancellationToken, Task> handler) : IPipelineRunner<TContext>
        where TContext : class, IPipelineContext
    {
        public Task RunAsync(TContext context, CancellationToken cancellationToken) => handler(context, cancellationToken);
    }
}




