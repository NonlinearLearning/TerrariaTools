namespace Domain.Analysis;

/// <summary>
/// 定义 CPG 分析网关。
/// </summary>
public interface IAnalysisCpgGateway
{
    /// <summary>
    /// 通过分析输入构建 CPG 快照。
    /// </summary>
    Task<AnalysisCpgSnapshot> BuildCpgSnapshotAsync(
        AnalysisInputDescriptor inputDescriptor,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth,
        CancellationToken cancellationToken = default);
}
