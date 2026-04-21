namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 控制流边。
/// </summary>
public sealed record CpgFlow(
    string FromNodeId,
    string ToNodeId,
    CpgFlowKind FlowKind);
