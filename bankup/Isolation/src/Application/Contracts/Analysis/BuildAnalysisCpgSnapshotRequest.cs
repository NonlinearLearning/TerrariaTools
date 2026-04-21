using Application.Contracts;

namespace Application.Contracts.Analysis;

/// <summary>
/// 构建 CPG 快照请求。
/// </summary>
public sealed class BuildAnalysisCpgSnapshotRequest
{
    public Guid RunCorrelationId { get; init; }

    /// <summary>
    /// 获取或设置工作区标识。
    /// </summary>
    public Guid WorkspaceContextId { get; init; }

    /// <summary>
    /// 获取或设置最小分析目标。
    /// </summary>
    public ContractMinimumAnalysisTarget MinimumTarget { get; init; } = ContractMinimumAnalysisTarget.Method;

    /// <summary>
    /// 获取或设置入口符号。
    /// </summary>
    public string EntrySymbol { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置分析深度。
    /// </summary>
    public int Depth { get; init; } = 2;
}
