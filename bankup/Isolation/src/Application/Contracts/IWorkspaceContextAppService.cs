using Application.Contracts.Workspaces;

namespace Application.Abstractions;

/// <summary>
/// 工作区上下文应用服务。
/// </summary>
public interface IWorkspaceContextAppService
{
    Task<WorkspaceContextDto> CreateAsync(
        CreateWorkspaceContextRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceContextDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
