using Application.Abstractions;
using Application.Contracts.Analysis;
using Application.Mappers;
using Domain.Analysis;
using Domain.Workspaces;

namespace Application.Services;

/// <summary>
/// 分析应用服务实现。
/// </summary>
public sealed class AnalysisAppService : IAnalysisAppService
{
    private readonly IWorkspaceContextRepository workspaceContextRepository;
    private readonly IAnalysisSnapshotRepository analysisSnapshotRepository;
    private readonly IAnalysisSnapshotFactory analysisSnapshotFactory;

    public AnalysisAppService(
        IWorkspaceContextRepository workspaceContextRepository,
        IAnalysisSnapshotRepository analysisSnapshotRepository,
        IAnalysisSnapshotFactory analysisSnapshotFactory)
    {
        this.workspaceContextRepository = workspaceContextRepository;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.analysisSnapshotFactory = analysisSnapshotFactory;
    }

    /// <inheritdoc />
    public async Task<AnalysisCpgSnapshotDto> BuildCpgSnapshotAsync(
        BuildAnalysisCpgSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        AnalysisCpgSnapshot snapshot = analysisSnapshotFactory.BuildCpgSnapshot(
            workspaceContext,
            request.MinimumTarget,
            request.EntrySymbol,
            request.Depth);
        await analysisSnapshotRepository.AddCpgSnapshotAsync(snapshot, cancellationToken);
        return ContractMapper.Map(snapshot);
    }

    /// <inheritdoc />
    public async Task<AnalysisCompositeLayerSnapshotDto> BuildCompositeSnapshotAsync(
        BuildCompositeLayerSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        AnalysisCompositeLayerSnapshot snapshot = analysisSnapshotFactory.BuildCompositeSnapshot(
            workspaceContext,
            request.CompositionName,
            request.Depth,
            request.LayerNames);
        await analysisSnapshotRepository.AddCompositeSnapshotAsync(snapshot, cancellationToken);
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
