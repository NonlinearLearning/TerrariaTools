namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛前端控制结构和最小控制流构建时使用的稳定命名约定。
/// </summary>
public static class FrontendControlFlowConventions
{
    /// <summary>
    /// 普通语句块名称。
    /// </summary>
    public const string Block = "BLOCK";

    /// <summary>
    /// switch section 块名称。
    /// </summary>
    public const string SwitchSection = "SWITCH_SECTION";

    /// <summary>
    /// if 控制结构名称。
    /// </summary>
    public const string If = "IF";

    /// <summary>
    /// return 控制结构名称。
    /// </summary>
    public const string Return = "RETURN";

    /// <summary>
    /// while 控制结构名称。
    /// </summary>
    public const string While = "WHILE";

    /// <summary>
    /// do 控制结构名称。
    /// </summary>
    public const string Do = "DO";

    /// <summary>
    /// for 控制结构名称。
    /// </summary>
    public const string For = "FOR";

    /// <summary>
    /// foreach 控制结构名称。
    /// </summary>
    public const string Foreach = "FOREACH";

    /// <summary>
    /// try 控制结构名称。
    /// </summary>
    public const string Try = "TRY";

    /// <summary>
    /// catch 控制结构名称。
    /// </summary>
    public const string Catch = "CATCH";

    /// <summary>
    /// finally 控制结构名称。
    /// </summary>
    public const string Finally = "FINALLY";

    /// <summary>
    /// switch 控制结构名称。
    /// </summary>
    public const string Switch = "SWITCH";

    /// <summary>
    /// throw 控制结构名称。
    /// </summary>
    public const string Throw = "THROW";

    /// <summary>
    /// break 控制结构名称。
    /// </summary>
    public const string Break = "BREAK";

    /// <summary>
    /// continue 控制结构名称。
    /// </summary>
    public const string Continue = "CONTINUE";

    /// <summary>
    /// return 终止流标签。
    /// </summary>
    public const string ReturnTerminal = "return";

    /// <summary>
    /// throw 终止流标签。
    /// </summary>
    public const string ThrowTerminal = "throw";

    /// <summary>
    /// break 终止流标签。
    /// </summary>
    public const string BreakTerminal = "break";

    /// <summary>
    /// continue 终止流标签。
    /// </summary>
    public const string ContinueTerminal = "continue";

    /// <summary>
    /// 根据语法种类名称返回默认控制结构名称。
    /// </summary>
    public static string BuildDefaultControlType(string? syntaxKindName)
    {
        return string.IsNullOrWhiteSpace(syntaxKindName)
            ? FrontendGraphConventions.Unknown
            : syntaxKindName.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// 判断是否为 return 终止流。
    /// </summary>
    public static bool IsReturnTerminal(string? terminalKind)
    {
        return string.Equals(terminalKind, ReturnTerminal, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断是否为 break 终止流。
    /// </summary>
    public static bool IsBreakTerminal(string? terminalKind)
    {
        return string.Equals(terminalKind, BreakTerminal, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断是否为 continue 终止流。
    /// </summary>
    public static bool IsContinueTerminal(string? terminalKind)
    {
        return string.Equals(terminalKind, ContinueTerminal, StringComparison.Ordinal);
    }
}
