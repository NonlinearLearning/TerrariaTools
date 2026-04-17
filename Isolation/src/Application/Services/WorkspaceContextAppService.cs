using Application.Abstractions;
using Application.Contracts.Workspaces;
using Application.Mappers;
using Domain.Workspaces;

namespace Application.Services;

/// <summary>
/// 工作区上下文应用服务实现。
/// </summary>
public sealed class WorkspaceContextAppService : IWorkspaceContextAppService
{
    private readonly IWorkspaceContextRepository workspaceContextRepository;

    public WorkspaceContextAppService(IWorkspaceContextRepository workspaceContextRepository)
    {
        this.workspaceContextRepository = workspaceContextRepository;
    }

    /// <inheritdoc />
    public async Task<WorkspaceContextDto> CreateAsync(
        CreateWorkspaceContextRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = WorkspaceContext.Create(
            request.SolutionPath,
            request.LanguageVersion);

        foreach (ProjectItemDto project in request.Projects)
        {
            workspaceContext.AddProject(new ProjectDescriptor(project.Name, project.Path));
        }

        foreach (string document in request.Documents)
        {
            workspaceContext.AddDocument(DocumentPath.Create(document));
        }

        foreach (ReferenceItemDto reference in request.References)
        {
            workspaceContext.AddReference(new ReferenceDescriptor(reference.Name, reference.Version));
        }

        await workspaceContextRepository.AddAsync(workspaceContext, cancellationToken);
        return ContractMapper.Map(workspaceContext);
    }

    /// <inheritdoc />
    public async Task<WorkspaceContextDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        WorkspaceContext? workspaceContext = await workspaceContextRepository.GetAsync(id, cancellationToken);
        return workspaceContext is null ? null : ContractMapper.Map(workspaceContext);
    }
}
