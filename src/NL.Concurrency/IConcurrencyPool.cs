namespace NL.Concurrency;

/// <summary>
/// 定义有序并发选择、提交和依赖图调度能力。
/// </summary>
public interface IConcurrencyPool
{
    /// <summary>
    /// 按索引顺序并发执行工作，并按原始顺序返回结果。
    /// </summary>
    /// <typeparam name="TResult">工作结果类型。</typeparam>
    /// <param name="itemCount">要处理的工作项总数。</param>
    /// <param name="maxDegreeOfParallelism">允许的最大并发度。</param>
    /// <param name="workItem">按索引执行单个工作项的异步委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入索引顺序一致的结果集合。</returns>
    Task<IReadOnlyList<TResult>> SelectOrderedAsync<TResult>(
        int itemCount,
        int maxDegreeOfParallelism,
        Func<int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按输入顺序并发执行工作，并按原始顺序返回结果。
    /// </summary>
    /// <typeparam name="TSource">输入项类型。</typeparam>
    /// <typeparam name="TResult">工作结果类型。</typeparam>
    /// <param name="sources">要处理的输入集合。</param>
    /// <param name="maxDegreeOfParallelism">允许的最大并发度。</param>
    /// <param name="workItem">处理单个输入项的异步委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入顺序一致的结果集合。</returns>
    Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task<TResult>> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按输入顺序执行处理器密集型工作，并按原始顺序返回结果。
    /// </summary>
    /// <typeparam name="TSource">输入项类型。</typeparam>
    /// <typeparam name="TResult">工作结果类型。</typeparam>
    /// <param name="sources">要处理的输入集合。</param>
    /// <param name="maxDegreeOfParallelism">允许的最大并发度。</param>
    /// <param name="workItem">处理单个输入项的同步委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入顺序一致的结果集合。</returns>
    Task<IReadOnlyList<TResult>> SelectCpuBoundOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, TResult> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 并发计算结果后，按输入顺序串行提交结果。
    /// </summary>
    /// <typeparam name="TSource">输入项类型。</typeparam>
    /// <typeparam name="TResult">工作结果类型。</typeparam>
    /// <param name="sources">要处理的输入集合。</param>
    /// <param name="options">工作窗口和保留策略。</param>
    /// <param name="workItem">生成单个结果的同步委托。</param>
    /// <param name="commit">按顺序提交结果的委托。</param>
    /// <param name="retainedRecordCount">返回单个结果保留记录数的委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    void CommitOrdered<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        ConcurrencyWindowOptions options,
        Func<TSource, int, TResult> workItem,
        Action<TResult, int> commit,
        Func<TResult, int>? retainedRecordCount = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 先收集和预处理，再并发求解并按输入顺序提交结果。
    /// </summary>
    /// <typeparam name="TSource">输入项类型。</typeparam>
    /// <typeparam name="TCollected">收集阶段结果类型。</typeparam>
    /// <typeparam name="TPrepared">预处理阶段结果类型。</typeparam>
    /// <typeparam name="TResult">最终结果类型。</typeparam>
    /// <param name="sources">要处理的输入集合。</param>
    /// <param name="options">工作窗口和保留策略。</param>
    /// <param name="collect">收集单个输入项中间结果的委托。</param>
    /// <param name="prepare">预处理中间结果的委托。</param>
    /// <param name="solve">生成最终结果的委托。</param>
    /// <param name="commit">按顺序提交最终结果的委托。</param>
    /// <param name="collectedRetainedRecordCount">返回收集结果保留记录数的委托。</param>
    /// <param name="resultRetainedRecordCount">返回最终结果保留记录数的委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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

    /// <summary>
    /// 按输入顺序分配索引，并在受限并发度下异步执行每个工作项。
    /// </summary>
    /// <typeparam name="TSource">输入项类型。</typeparam>
    /// <param name="sources">要处理的输入集合。</param>
    /// <param name="maxDegreeOfParallelism">允许的最大并发度。</param>
    /// <param name="workItem">处理单个输入项的异步委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示整体执行过程的任务。</returns>
    Task ForEachAsync<TSource>(
        IReadOnlyList<TSource> sources,
        int maxDegreeOfParallelism,
        Func<TSource, int, CancellationToken, Task> workItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按依赖关系调度工作图，并返回每个节点的执行结果与调度峰值。
    /// </summary>
    /// <typeparam name="TNode">依赖图节点标识类型。</typeparam>
    /// <typeparam name="TResult">节点执行结果类型。</typeparam>
    /// <param name="workItems">依赖图中的工作项集合。</param>
    /// <param name="maxDegreeOfParallelism">允许的最大并发度。</param>
    /// <param name="readyOrder">多个节点同时就绪时的排序器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含节点结果与调度峰值的执行结果。</returns>
    Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(
        IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems,
        int maxDegreeOfParallelism,
        IComparer<TNode> readyOrder,
        CancellationToken cancellationToken = default)
        where TNode : notnull;
}
