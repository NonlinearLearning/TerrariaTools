namespace NL.Concurrency;

/// <summary>
/// 描述一个依赖图节点、其前置节点，以及前置节点完成后执行的工作。
/// </summary>
/// <typeparam name="TNode">依赖图节点标识类型。</typeparam>
/// <typeparam name="TResult">节点执行结果类型。</typeparam>
/// <param name="Node">当前工作项对应的节点。</param>
/// <param name="Dependencies">当前节点依赖的前置节点集合。</param>
/// <param name="ExecuteAsync">在前置节点结果可用后执行当前节点的异步委托。</param>
public sealed record DependencyWorkItem<TNode, TResult>(
    TNode Node,
    IReadOnlyList<TNode> Dependencies,
    Func<IReadOnlyDictionary<TNode, TResult>, CancellationToken, Task<TResult>> ExecuteAsync)
    where TNode : notnull;
