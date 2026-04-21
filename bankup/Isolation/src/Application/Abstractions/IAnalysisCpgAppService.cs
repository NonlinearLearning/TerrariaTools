using Application.Contracts.Analysis;

namespace Application.Abstractions;

/// <summary>
/// CPG 分析应用服务。
/// </summary>
public interface IAnalysisCpgAppService
{
    Task<AnalysisCpgSnapshotDto> BuildAnalysisBackedCpgSnapshotAsync(
        BuildAnalysisBackedCpgSnapshotRequest request,
        CancellationToken cancellationToken = default);
}
