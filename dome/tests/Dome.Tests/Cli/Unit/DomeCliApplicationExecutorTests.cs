using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using TerrariaTools.Dome.Cli;
using Xunit;

namespace TerrariaTools.Dome.Tests.Cli;

public sealed class DomeCliApplicationExecutorTests
{
    [Fact]
    public async Task RunAsync_StandardInvocation_UsesStandardRunner()
    {
        var standard = new FakeStandardRunner();
        var runtime = new FakeRuntimeRunner();
        var shadow = new FakeShadowRunner();
        var executor = new DomeCliApplicationExecutor(standard, runtime, shadow);

        var result = await executor.RunAsync(
            DomeCliInvocation.Standard(new ApplicationAbstractions.RunRequest("in", "out", Array.Empty<string>(), TerrariaTools.Dome.Application.Ports.RunMode.Standard)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(standard.Calls);
        Assert.Empty(runtime.Calls);
        Assert.Empty(shadow.Calls);
    }

    [Fact]
    public async Task RunAsync_RuntimeInvocation_UsesRuntimeRunner()
    {
        var standard = new FakeStandardRunner();
        var runtime = new FakeRuntimeRunner();
        var shadow = new FakeShadowRunner();
        var executor = new DomeCliApplicationExecutor(standard, runtime, shadow);

        var result = await executor.RunAsync(
            DomeCliInvocation.Runtime(new ApplicationAbstractions.TerrariaRuntimeRunRequest("input.sln", "out")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(standard.Calls);
        Assert.Single(runtime.Calls);
        Assert.Empty(shadow.Calls);
    }

    [Fact]
    public async Task RunAsync_ShadowInvocation_UsesShadowRunner()
    {
        var standard = new FakeStandardRunner();
        var runtime = new FakeRuntimeRunner();
        var shadow = new FakeShadowRunner();
        var executor = new DomeCliApplicationExecutor(standard, runtime, shadow);

        var result = await executor.RunAsync(
            DomeCliInvocation.ShadowExtraction(new ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest("input.sln", "out", "Seed")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(standard.Calls);
        Assert.Empty(runtime.Calls);
        Assert.Single(shadow.Calls);
    }

    private sealed class FakeStandardRunner : IDomeApplicationRunner
    {
        public List<ApplicationAbstractions.RunRequest> Calls { get; } = [];

        public Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(ModelExecution.RunResult.Success("out", null));
        }
    }

    private sealed class FakeRuntimeRunner : ITerrariaRuntimeApplicationRunner
    {
        public List<ApplicationAbstractions.TerrariaRuntimeRunRequest> Calls { get; } = [];

        public Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.TerrariaRuntimeRunRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(ModelExecution.RunResult.Success("out", null));
        }
    }

    private sealed class FakeShadowRunner : ITerrariaRuntimeShadowExtractionApplicationRunner
    {
        public List<ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest> Calls { get; } = [];

        public Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(ModelExecution.RunResult.Success("out", null));
        }
    }
}




