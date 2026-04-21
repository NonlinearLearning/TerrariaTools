namespace Domain.Workspaces;

/// <summary>
/// 定义工作区上下文仓储。
/// </summary>
public interface IWorkspaceContextRepository
{
    /// <summary>
    /// 新增工作区上下文。
    /// </summary>
    /// <param name="workspaceContext">工作区上下文。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>异步任务。</returns>
    Task AddAsync(WorkspaceContext workspaceContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据标识获取工作区上下文。
    /// </summary>
    /// <param name="id">工作区标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>工作区上下文。</returns>
    Task<WorkspaceContext?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
