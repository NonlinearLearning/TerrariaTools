using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Analysis.Roslyn;

/// <summary>
/// 元数据类型ID构建器，用于构建类型标识符。
/// </summary>
internal static class MetadataTypeIdBuilder
{
    /// <summary>
    /// 根据类型符号构建类型ID。
    /// </summary>
    /// <param name="typeSymbol">类型符号。</param>
    /// <returns>类型ID字符串。</returns>
    public static string Build(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "Unknown";
    }
}
