using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeRewriteExecutor : ApplicationAbstractions.IRewriteExecutor
{
    private readonly ModelExecution.RewriteOutput _result;

    public FakeRewriteExecutor(ModelExecution.RewriteOutput result)
    {
        _result = result;
    }

    public List<(ModelExecution.RewriteInput Input, CorePlanning.AuditPlan Plan)> Calls { get; } = [];

    public Task<ModelExecution.RewriteOutput> ExecuteAsync(
        ModelExecution.RewriteInput input,
        CancellationToken cancellationToken)
    {
        Calls.Add((input, input.Plan));
        return Task.FromResult(_result);
    }
}




