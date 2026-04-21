using Application.Abstractions;
using Application.Contracts.Analysis;
using Application.Mappers;
using Domain.Analysis;
using Domain.Workspaces;
using Logic.Analysis;
using Logic.Analysis.Events;

namespace Application.Services;

/// <summary>
/// 分析应用服务实现。
/// </summary>
public sealed class AnalysisAppService : IAnalysisAppService
{
    private readonly IWorkspaceContextRepository workspaceContextRepository;
    private readonly IAnalysisSnapshotRepository analysisSnapshotRepository;
    private readonly IAnalysisSnapshotComposer analysisSnapshotComposer;
    private readonly IAnalysisDomainEventPublisher? analysisDomainEventPublisher;

    public AnalysisAppService(
        IWorkspaceContextRepository workspaceContextRepository,
        IAnalysisSnapshotRepository analysisSnapshotRepository,
        IAnalysisSnapshotComposer analysisSnapshotComposer,
        IAnalysisDomainEventPublisher? analysisDomainEventPublisher = null)
    {
        this.workspaceContextRepository = workspaceContextRepository;
        this.analysisSnapshotRepository = analysisSnapshotRepository;
        this.analysisSnapshotComposer = analysisSnapshotComposer;
        this.analysisDomainEventPublisher = analysisDomainEventPublisher;
    }


    public async Task<AnalysisCpgSnapshotDto> BuildCpgSnapshotAsync(
        BuildAnalysisCpgSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        AnalysisCpgSnapshot snapshot = analysisSnapshotComposer.BuildCpgSnapshot(
            workspaceContext,
            ContractMapper.Map(request.MinimumTarget),
            request.EntrySymbol,
            request.Depth);
        await analysisSnapshotRepository.AddCpgSnapshotAsync(snapshot, cancellationToken);
        analysisDomainEventPublisher?.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = request.RunCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = request.EntrySymbol,
            Depth = request.Depth,
        });
        return ContractMapper.Map(snapshot);
    }


    public async Task<AnalysisCompositeLayerSnapshotDto> BuildCompositeSnapshotAsync(
        BuildCompositeLayerSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContext workspaceContext = await GetWorkspaceAsync(request.WorkspaceContextId, cancellationToken);
        AnalysisCompositeLayerSnapshot snapshot = analysisSnapshotComposer.BuildCompositeSnapshot(
            workspaceContext,
            request.CompositionName,
            request.Depth,
            request.LayerNames);
        await analysisSnapshotRepository.AddCompositeSnapshotAsync(snapshot, cancellationToken);
        analysisDomainEventPublisher?.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = request.RunCorrelationId,
            WorkspaceContext = workspaceContext,
            CompositeSnapshot = snapshot,
            EntrySymbol = request.CompositionName,
            Depth = request.Depth,
        });
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
