namespace Analysis.Semantic;

/// <summary>
/// 保存 semanticcpg 包级默认值。
///
/// Joern `package.scala` 主要提供查询显示时的默认宽度。
/// 当前 C# 版先保留这个稳定默认值，供后续导出或调试输出复用。
/// </summary>
public static class SemanticCpgPackage
{
    /// <summary>
    /// 获取默认输出宽度。
    /// </summary>
    public const int DefaultAvailableWidth = 120;
}
