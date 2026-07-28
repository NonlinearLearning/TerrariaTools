namespace NL.Concurrency;

public sealed record DependencyExecutionResult<TNode, TResult>(
    IReadOnlyDictionary<TNode, TResult> Results,
    int PeakReadyWorkItemCount,
    int PeakConcurrentWorkItemCount)
    where TNode : notnull;
