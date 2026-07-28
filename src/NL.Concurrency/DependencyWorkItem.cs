namespace NL.Concurrency;

public sealed record DependencyWorkItem<TNode, TResult>(
    TNode Node,
    IReadOnlyList<TNode> Dependencies,
    Func<IReadOnlyDictionary<TNode, TResult>, CancellationToken, Task<TResult>> ExecuteAsync)
    where TNode : notnull;
