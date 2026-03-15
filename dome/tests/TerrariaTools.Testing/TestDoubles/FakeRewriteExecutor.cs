using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeRewriteExecutor : IRewriteExecutor
{
    private readonly RewriteExecutionResult _result;

    public FakeRewriteExecutor(RewriteExecutionResult result)
    {
        _result = result;
    }

    public List<(RewriteExecutionDocumentContext Context, AuditPlan Plan)> Calls { get; } = [];

    public Task<RewriteExecutionResult> ExecuteAsync(
        RewriteExecutionDocumentContext context,
        AuditPlan plan,
        CancellationToken cancellationToken)
    {
        Calls.Add((context, plan));
        return Task.FromResult(_result);
    }
}
