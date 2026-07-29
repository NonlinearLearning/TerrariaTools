namespace NL.Concurrency;

/// <summary>
/// 包含依赖图执行结果以及观测到的调度峰值。
/// </summary>
/// <typeparam name="TNode">依赖图节点标识类型。</typeparam>
/// <typeparam name="TResult">节点执行结果类型。</typeparam>
/// <param name="Results">每个节点对应的执行结果。</param>
/// <param name="PeakReadyWorkItemCount">执行过程中观测到的峰值就绪工作项数量。</param>
/// <param name="PeakConcurrentWorkItemCount">执行过程中观测到的峰值并发工作项数量。</param>
public sealed record DependencyExecutionResult<TNode, TResult>(
    IReadOnlyDictionary<TNode, TResult> Results,
    int PeakReadyWorkItemCount,
    int PeakConcurrentWorkItemCount)
    where TNode : notnull;
