using Application.Contracts;

namespace Application.Contracts.Analysis;

/// <summary>
/// 构建由既有 Analysis 支撑的 CPG 快照请求。
/// </summary>
public sealed class BuildAnalysisBackedCpgSnapshotRequest
{
    public Guid WorkspaceContextId { get; set; }

    public string? SourcePath { get; set; }

    public ContractAnalysisSourceKind SourceKind { get; set; } = ContractAnalysisSourceKind.Unknown;

    public ContractMinimumAnalysisTarget MinimumTarget { get; set; } = ContractMinimumAnalysisTarget.Method;

    public string EntrySymbol { get; set; } = string.Empty;

    public int Depth { get; set; } = 2;
}
