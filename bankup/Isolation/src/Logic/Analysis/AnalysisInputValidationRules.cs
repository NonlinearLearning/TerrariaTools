using Domain.Analysis;

namespace Logic.Analysis;

/// <summary>
/// 收敛分析输入类型与路径匹配规则。
/// </summary>
public static class AnalysisInputValidationRules
{
    /// <summary>
    /// 判断输入类型与路径是否匹配。
    /// </summary>
    public static bool IsSourceKindMatched(AnalysisSourceKind sourceKind, string? sourcePath)
    {
        if (sourceKind == AnalysisSourceKind.Unknown)
        {
            return true;
        }

        string extension = Path.GetExtension(sourcePath ?? string.Empty);
        return sourceKind switch
        {
            AnalysisSourceKind.Solution => extension.Equals(".sln", StringComparison.OrdinalIgnoreCase),
            AnalysisSourceKind.Project => extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase),
            AnalysisSourceKind.SourceFile => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase),
            AnalysisSourceKind.Directory => string.IsNullOrEmpty(extension),
            _ => true,
        };
    }

    /// <summary>
    /// 构造输入类型不匹配时的错误消息。
    /// </summary>
    public static string BuildSourceKindMismatchMessage(AnalysisSourceKind sourceKind, string? sourcePath)
    {
        return $"分析输入类型 {sourceKind} 与路径 '{sourcePath}' 不匹配。";
    }
}
