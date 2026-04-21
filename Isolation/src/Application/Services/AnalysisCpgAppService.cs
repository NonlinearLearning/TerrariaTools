using Application.Abstractions;
using Application.Contracts.Analysis;
using Application.Mappers;
using Domain.Analysis;
using Domain.Workspaces;
using Logic.Analysis;

namespace Application.Services;

/// <summary>
/// CPG 分析应用服务实现。
/// </summary>
public sealed class AnalysisCpgAppService : IAnalysisCpgAppService
{
    private readonly IWorkspaceContextRepository workspaceContextRepository;
    private readonly IAnalysisSnapshotRepository analysisSnapshotRepository;
    private readonly IAnalysisCpgGateway analysisCpgGateway;
    private readonly IAnalysisInputDescriptorBuilder analysisInputDescriptorBuilder;

    public AnalysisCpgAppService(
        IWorkspaceContextRepository workspaceContextRepository,
        IAnalysisSnapshotRepository analysisSnapshotRepository,
        IAnalysisCpgGateway analysisCpgGateway,
        IAnalysisInputDescriptorBuilder analysisInputDescriptorBuilder)
    {
        this.workspaceContextRepository = workspaceContextRepository;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.analysisCpgGateway = analysisCpgGateway;
        this.analysisInputDescriptorBuilder = analysisInputDescriptorBuilder;
    }


    public async Task<AnalysisCpgSnapshotDto> BuildAnalysisBackedCpgSnapshotAsync(
        BuildAnalysisBackedCpgSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        AnalysisInputDescriptor inputDescriptor = analysisInputDescriptorBuilder.Build(
            workspaceContext,
            request.SourcePath,
            ContractMapper.Map(request.SourceKind));

        AnalysisCpgSnapshot snapshot = await analysisCpgGateway.BuildCpgSnapshotAsync(
            inputDescriptor,
            ContractMapper.Map(request.MinimumTarget),
            request.EntrySymbol,
            request.Depth,
            cancellationToken);

        await analysisSnapshotRepository.AddCpgSnapshotAsync(snapshot, cancellationToken);
        return ContractMapper.Map(snapshot);
    }

    private async Task<WorkspaceContext> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken)
    {
        WorkspaceContext? workspaceContext = await workspaceContextRepository.GetAsync(id, cancellationToken);
        if (workspaceContext is null)
        {
            throw new InvalidOperationException($"未找到工作区上下文：{id}");
        }

        return workspaceContext;
    }
}
