namespace NL.Concurrency;

public interface IConcurrencyPool
{
    Task<IReadOnlyList<TResult>> SelectOrderedAsync<TResult>(
        int itemCount,
        int maxDegreeOfParallelism,
        Func<int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TResult>> SelectCpuBoundOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, TResult> workItem,
        CancellationToken cancellationToken = default);

    void CommitOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TResult> workItem,
        Action<TResult, int> commit,
        Func<TResult, int>? retainedRecordCount = null,
        CancellationToken cancellationToken = default);

    void CommitTwoStageOrdered<TSource, TCollected, TPrepared, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TCollected> collect,
        Func<TCollected, int, TPrepared> prepare,
        Func<TPrepared, int, TResult> solve,
        Action<TResult, int> commit,
        Func<TCollected, int>? collectedRetainedRecordCount = null,
        Func<TResult, int>? resultRetainedRecordCount = null,
        CancellationToken cancellationToken = default);

    Task ForEachAsync<TSource>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default);

    Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
        IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
        int maxDegreeOfParallelism,
        IComparer<TNode> readyOrder,
        CancellationToken cancellationToken = default)
        where TNode : notnull;
}
