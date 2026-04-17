namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 调用边。
/// </summary>
public sealed record CpgCall(
    string FromNodeId,
    string ToNodeId,
    CpgCallKind CallKind,
    string TargetSymbol);
