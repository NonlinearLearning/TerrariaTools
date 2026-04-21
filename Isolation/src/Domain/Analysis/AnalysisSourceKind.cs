namespace Domain.Analysis;

/// <summary>
/// 表示分析输入来源类型。
/// </summary>
public enum AnalysisSourceKind
{
    Unknown = 0,
    Solution = 1,
    Project = 2,
    Directory = 3,
    SourceFile = 4,
}
