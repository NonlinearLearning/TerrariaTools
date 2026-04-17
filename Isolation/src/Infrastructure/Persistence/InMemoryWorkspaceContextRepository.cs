using Domain.Workspaces;

namespace Infrastructure.Persistence;

/// <summary>
/// 内存版工作区上下文仓储。
/// </summary>
public sealed class InMemoryWorkspaceContextRepository : IWorkspaceContextRepository
{
    private readonly Dictionary<Guid, WorkspaceContext> storage = new();

    /// <inheritdoc />
    public Task AddAsync(WorkspaceContext workspaceContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspaceContext);
        storage[workspaceContext.Id] = workspaceContext;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<WorkspaceContext?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        storage.TryGetValue(id, out WorkspaceContext? workspaceContext);
        return Task.FromResult(workspaceContext);
    }
}
