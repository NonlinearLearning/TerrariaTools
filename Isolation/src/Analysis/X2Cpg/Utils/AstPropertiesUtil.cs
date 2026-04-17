using Analysis.Frontend.AstModel;

namespace Analysis.X2Cpg.Utils;

/// <summary>
/// 读取 AST 根节点属性。
///
/// 对应 Joern `AstPropertiesUtil.scala`。
/// </summary>
public static class AstPropertiesUtil
{
    public static string? RootType(Ast ast) => RootProperty(ast, "TypeFullName");

    public static string? RootCode(Ast ast) => RootProperty(ast, "Code");

    public static string? RootName(Ast ast) => RootProperty(ast, "Name");

    public static string RootCodeOrEmpty(Ast ast) => RootCode(ast) ?? string.Empty;

    private static string? RootProperty(Ast ast, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(ast);
        return ast.Root is not null && ast.Root.TryGetProperty<string>(propertyName, out string? value)
            ? value
            : null;
    }
}
