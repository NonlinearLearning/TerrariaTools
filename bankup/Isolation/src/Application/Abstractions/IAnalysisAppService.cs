using Application.Contracts.Analysis;

namespace Application.Abstractions;

/// <summary>
/// 分析应用服务。
/// </summary>
public interface IAnalysisAppService
{
    Task<AnalysisCpgSnapshotDto> BuildCpgSnapshotAsync(
        BuildAnalysisCpgSnapshotRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisCompositeLayerSnapshotDto> BuildCompositeSnapshotAsync(
        BuildCompositeLayerSnapshotRequest request,
        CancellationToken cancellationToken = default);
}
