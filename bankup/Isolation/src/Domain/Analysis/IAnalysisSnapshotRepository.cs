namespace Domain.Analysis;

/// <summary>
/// 定义分析快照仓储。
/// </summary>
public interface IAnalysisSnapshotRepository
{
    /// <summary>
    /// 新增 CPG 快照。
    /// </summary>
    Task AddCpgSnapshotAsync(AnalysisCpgSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 CPG 快照。
    /// </summary>
    Task<AnalysisCpgSnapshot?> GetCpgSnapshotAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增组成层快照。
    /// </summary>
    Task AddCompositeSnapshotAsync(AnalysisCompositeLayerSnapshot snapshot, CancellationToken cancellationToken = default);
}
