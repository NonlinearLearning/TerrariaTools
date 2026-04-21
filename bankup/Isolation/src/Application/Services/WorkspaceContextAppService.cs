using Application.Abstractions;
using Application.Contracts;
using Application.Contracts.Workspaces;
using Application.Mappers;
using Domain.Workspaces;
using Logic.Workspaces;

namespace Application.Services;

/// <summary>
/// 工作区上下文应用服务实现。
/// </summary>
public sealed class WorkspaceContextAppService : IWorkspaceContextAppService
{
    private readonly IWorkspaceContextBuilder workspaceContextBuilder;
    private readonly IWorkspaceContextRepository workspaceContextRepository;

    public WorkspaceContextAppService(
        IWorkspaceContextBuilder workspaceContextBuilder,
        IWorkspaceContextRepository workspaceContextRepository)
    {
        this.workspaceContextBuilder = workspaceContextBuilder;
        this.workspaceContextRepository = workspaceContextRepository;
    }


    public async Task<WorkspaceContextDto> CreateAsync(
        CreateWorkspaceContextRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = workspaceContextBuilder.Build(new WorkspaceContextBuildInput
        {
            SolutionPath = request.SolutionPath,
            LanguageVersion = request.LanguageVersion,
            RunMode = ContractMapper.Map(request.RunMode),
            Projects = request.Projects
                .Select(project => new ProjectDescriptor(project.Name, project.Path))
                .ToArray(),
            Documents = request.Documents.ToArray(),
            References = request.References
                .Select(reference => new ReferenceDescriptor(reference.Name, reference.Version))
                .ToArray(),
            RuleInputs =
                request.RuleSet?.EnabledRules
                    .Select(static rule => new WorkspaceEnabledRuleInput
                    {
                        RuleCode = rule.RuleCode,
                        DisplayName = rule.DisplayName,
                    })
                    .ToArray()
                    ?? Array.Empty<WorkspaceEnabledRuleInput>(),
        });

        await workspaceContextRepository.AddAsync(workspaceContext, cancellationToken);
        return ContractMapper.Map(workspaceContext);
    }


    public async Task<WorkspaceContextDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        WorkspaceContext? workspaceContext = await workspaceContextRepository.GetAsync(id, cancellationToken);
        return workspaceContext is null ? null : ContractMapper.Map(workspaceContext);
    }
}
