namespace Domain.Analysis;

/// <summary>
/// 表示最小节点的位置范围。
/// </summary>
public sealed record LocationRange(
    string DocumentPath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
