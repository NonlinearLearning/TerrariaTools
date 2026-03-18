using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeRewriteExecutor : ApplicationAbstractions.IRewriteExecutor
{
    private readonly ApplicationAbstractions.RewriteExecutionResult _result;

    public FakeRewriteExecutor(ApplicationAbstractions.RewriteExecutionResult result)
    {
        _result = result;
    }

    public List<(ApplicationAbstractions.SourceDocumentSet SourceSet, ModelPlanning.AuditPlan Plan)> Calls { get; } = [];

    public Task<ApplicationAbstractions.RewriteExecutionResult> ExecuteAsync(
        ApplicationAbstractions.SourceDocumentSet sourceSet,
        ModelPlanning.AuditPlan plan,
        CancellationToken cancellationToken)
    {
        Calls.Add((sourceSet, plan));
        return Task.FromResult(_result);
    }
}
