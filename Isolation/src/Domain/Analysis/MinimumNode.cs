namespace Domain.Analysis;

/// <summary>
/// 表示 CPG 中的最小节点。
/// </summary>
public sealed record MinimumNode(
    string NodeId,
    string DisplayName,
    CpgType NodeType,
    LocationRange LocationRange);
