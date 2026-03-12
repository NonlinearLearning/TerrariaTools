using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 符号引用投影器，负责将 Roslyn 符号转换为内部的 SymbolRef 表示。
/// </summary>
internal static class SymbolRefProjector
{
    /// <summary>
    /// 将 Roslyn 符号投影为 SymbolRef。
    /// </summary>
    /// <param name="symbol">Roslyn 符号。</param>
    /// <param name="declaringMemberId">声明该符号的成员 ID。</param>
    /// <returns>投影后的符号引用，如果符号为空则返回 null。</returns>
    public static SymbolRef? Project(ISymbol? symbol, MemberId declaringMemberId)
    {
        if (symbol == null)
        {
            return null;
        }

        var declarationSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var spanStart = declarationSyntax?.SpanStart ?? -1;
        var spanLength = declarationSyntax?.Span.Length ?? 0;

        return new SymbolRef(
            BuildSymbolKey(symbol, declaringMemberId),
            symbol.Name,
            MapKind(symbol),
            declaringMemberId,
            spanStart,
            spanLength);
    }

    /// <summary>
    /// 投影声明的变量符号。
    /// </summary>
    /// <param name="statement">局部变量声明语句。</param>
    /// <param name="variable">变量声明符。</param>
    /// <param name="model">语义模型。</param>
    /// <param name="declaringMemberId">声明该变量的成员 ID。</param>
    /// <returns>投影后的符号引用。</returns>
    public static SymbolRef? ProjectDeclared(LocalDeclarationStatementSyntax statement, VariableDeclaratorSyntax variable, SemanticModel model, MemberId declaringMemberId)
    {
        var symbol = model.GetDeclaredSymbol(variable);
        return Project(symbol, declaringMemberId);
    }

    /// <summary>
    /// 投影使用的标识符符号。
    /// </summary>
    /// <param name="identifier">标识符名称语法。</param>
    /// <param name="model">语义模型。</param>
    /// <param name="declaringMemberId">使用该标识符的成员 ID。</param>
    /// <returns>投影后的符号引用。</returns>
    public static SymbolRef? ProjectUsed(IdentifierNameSyntax identifier, SemanticModel model, MemberId declaringMemberId)
    {
        var symbol = model.GetSymbolInfo(identifier).Symbol;
        return Project(symbol, declaringMemberId);
    }

    /// <summary>
    /// 构建符号的唯一键。
    /// </summary>
    /// <param name="symbol">Roslyn 符号。</param>
    /// <param name="declaringMemberId">声明该符号的成员 ID。</param>
    /// <returns>符号的唯一键字符串。</returns>
    private static string BuildSymbolKey(ISymbol symbol, MemberId declaringMemberId)
    {
        if (symbol is ILocalSymbol or IParameterSymbol)
        {
            var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var spanStart = syntax?.SpanStart ?? -1;
            var spanLength = syntax?.Span.Length ?? 0;
            return $"{declaringMemberId.Value}|{MapKind(symbol)}|{symbol.Name}|{spanStart}|{spanLength}";
        }

        return symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    /// <summary>
    /// 映射符号类型。
    /// </summary>
    /// <param name="symbol">Roslyn 符号。</param>
    /// <returns>映射后的符号类型引用。</returns>
    private static SymbolKindRef MapKind(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol => SymbolKindRef.Local,
            IParameterSymbol => SymbolKindRef.Parameter,
            IFieldSymbol => SymbolKindRef.Field,
            IPropertySymbol => SymbolKindRef.Property,
            _ => SymbolKindRef.Unknown
        };
    }
}
