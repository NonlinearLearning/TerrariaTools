using Domain.Execution;
using Domain.Workspaces;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewriteResultValueObjectTests
{
    [Fact]
    public void RewriteResult_supporting_types_normalize_inputs_and_keep_string_projection()
    {
        FileChange fileChange = new(
            DocumentPath.Create("demo.cs"),
            " changed ",
            ["  PlayerTools.Entry  ", "PlayerTools.Helper"]);
        ExecutionTrace trace = new(Guid.NewGuid(), "  PlanExecutor  ", "  执行成功  ", DateTimeOffset.UtcNow);
        ExecutionFailure failure = new(Guid.NewGuid(), "  Conflict  ", "  需要人工处理  ", true);

        Assert.Equal("changed", fileChange.Summary);
        Assert.Equal(["PlayerTools.Entry", "PlayerTools.Helper"], fileChange.AffectedTargets);
        Assert.Equal(["PlayerTools.Entry", "PlayerTools.Helper"], fileChange.AffectedTargetValues.Select(static item => item.Value).ToArray());
        Assert.Equal("PlanExecutor", trace.StepName);
        Assert.Equal("执行成功", trace.Message);
        Assert.Equal("Conflict", failure.FailureType);
        Assert.Equal("需要人工处理", failure.Message);
        Assert.True(failure.Retryable);
    }

    [Fact]
    public void RewriteResult_records_terminal_failure_and_blocks_follow_up_mutation_after_completion()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();

        result.StartExecution(correlationId);
        result.AddExecutionFailure(new ExecutionFailure(Guid.NewGuid(), "Conflict", "需要人工处理", false));
        result.CompleteExecution(correlationId);

        Assert.True(result.HasTerminalFailure());
        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.Throws<InvalidOperationException>(() =>
            result.AddExecutionTrace(new ExecutionTrace(Guid.NewGuid(), "PlanExecutor", "completed", DateTimeOffset.UtcNow)));
    }
}
