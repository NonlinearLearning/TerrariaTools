namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛前端图构建过程中的稳定命名、分派和兜底规则。
/// </summary>
public static class FrontendGraphConventions
{
    /// <summary>
    /// 未知占位值。
    /// </summary>
    public const string Unknown = "<unknown>";

    /// <summary>
    /// 静态分派。
    /// </summary>
    public const string StaticDispatch = "STATIC_DISPATCH";

    /// <summary>
    /// 动态分派。
    /// </summary>
    public const string DynamicDispatch = "DYNAMIC_DISPATCH";

    /// <summary>
    /// 内存文件占位值。
    /// </summary>
    public const string MemoryFilePath = "<memory>";

    /// <summary>
    /// 字符串类型全名。
    /// </summary>
    public const string StringTypeFullName = "string";

    /// <summary>
    /// 字符串格式化操作名。
    /// </summary>
    public const string FormatStringOperator = "formatString";

    /// <summary>
    /// 数组初始化操作名。
    /// </summary>
    public const string ArrayInitializerOperator = "arrayInitializer";

    /// <summary>
    /// 集合初始化操作名。
    /// </summary>
    public const string CollectionInitializerOperator = "collectionInitializer";

    /// <summary>
    /// switch 表达式操作名。
    /// </summary>
    public const string SwitchExpressionOperator = "switchExpression";

    /// <summary>
    /// 模式匹配操作名。
    /// </summary>
    public const string IsPatternOperator = "isPattern";

    /// <summary>
    /// 强制转换操作名。
    /// </summary>
    public const string CastOperator = "cast";

    /// <summary>
    /// Dispose 调用名。
    /// </summary>
    public const string DisposeMethodName = "Dispose";

    /// <summary>
    /// 构造成员全名。
    /// </summary>
    public static string BuildMemberFullName(string? containingTypeFullName, string? memberName)
    {
        return $"{SymbolFormattingRules.NormalizeTypeFullName(containingTypeFullName)}.{NormalizeSimpleName(memberName)}";
    }

    /// <summary>
    /// 构造稳定操作标识。
    /// </summary>
    public static string BuildOperationId(string? filePath, int spanStart, int rawKind)
    {
        string normalizedFilePath = string.IsNullOrWhiteSpace(filePath) ? MemoryFilePath : filePath.Trim();
        return $"{normalizedFilePath}:{spanStart}:{rawKind}";
    }

    /// <summary>
    /// 构造 using 语句生成的 Dispose 调用代码。
    /// </summary>
    public static string BuildDisposeCallCode(string? localName)
    {
        return string.IsNullOrWhiteSpace(localName) ? "Dispose()" : $"{localName.Trim()}.Dispose()";
    }

    /// <summary>
    /// 构造导入别名。
    /// </summary>
    public static string BuildImportAlias(string? explicitAlias, string? importedEntity)
    {
        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            return explicitAlias.Trim();
        }

        string normalizedImportedEntity = NormalizeSimpleName(importedEntity);
        int separatorIndex = normalizedImportedEntity.LastIndexOf('.');
        return separatorIndex < 0 ? normalizedImportedEntity : normalizedImportedEntity[(separatorIndex + 1)..];
    }

    /// <summary>
    /// 构造 lambda 合成方法名。
    /// </summary>
    public static string BuildLambdaName(int ordinal)
    {
        return $"<lambda>{ordinal}";
    }

    /// <summary>
    /// 构造 lambda 兜底方法全名。
    /// </summary>
    public static string BuildLambdaFallbackFullName(string? fileName, string? lambdaName)
    {
        return $"{NormalizeSimpleName(fileName)}::{NormalizeSimpleName(lambdaName)}";
    }

    /// <summary>
    /// 构造 lambda 兜底符号标识。
    /// </summary>
    public static string BuildLambdaFallbackSymbolId(string? operationId)
    {
        return $"lambda:{NormalizeSimpleName(operationId)}";
    }

    /// <summary>
    /// 构造字段访问的兜底全名。
    /// </summary>
    public static string BuildFallbackFieldFullName(string? receiverCode, string? memberName)
    {
        string receiver = string.IsNullOrWhiteSpace(receiverCode) ? Unknown : receiverCode.Trim();
        return $"{receiver}.{NormalizeSimpleName(memberName)}";
    }

    /// <summary>
    /// 从调用文本中解析接收者代码和名称。
    /// </summary>
    public static InvocationReceiverInfo? TryParseReceiverFromInvocationText(string? invocationText)
    {
        if (string.IsNullOrWhiteSpace(invocationText))
        {
            return null;
        }

        int separatorIndex = invocationText.LastIndexOf('.');
        if (separatorIndex <= 0)
        {
            return null;
        }

        string receiverCode = invocationText[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(receiverCode))
        {
            return null;
        }

        int nameSeparatorIndex = receiverCode.LastIndexOf('.');
        string receiverName = nameSeparatorIndex < 0 ? receiverCode : receiverCode[(nameSeparatorIndex + 1)..];
        return new InvocationReceiverInfo(receiverName, receiverCode);
    }

    /// <summary>
    /// 构造属性访问器名称。
    /// </summary>
    public static string BuildPropertyAccessorName(string? accessorKeyword, string? propertyName)
    {
        string keyword = NormalizeSimpleName(accessorKeyword);
        string normalizedPropertyName = NormalizeSimpleName(propertyName);

        return keyword switch
        {
            "get" => $"get_{normalizedPropertyName}",
            "set" => $"set_{normalizedPropertyName}",
            _ => keyword,
        };
    }

    /// <summary>
    /// 根据布尔条件返回稳定分派类型。
    /// </summary>
    public static string GetDispatchType(bool isStatic, bool isReducedExtensionMethod = false)
    {
        return isStatic || isReducedExtensionMethod ? StaticDispatch : DynamicDispatch;
    }

    /// <summary>
    /// 根据语法种类名称映射复合赋值运算符。
    /// </summary>
    public static string? TryGetCompoundAssignmentOperator(string? syntaxKindName)
    {
        return syntaxKindName switch
        {
            "AddAssignmentExpression" => "+",
            "SubtractAssignmentExpression" => "-",
            "MultiplyAssignmentExpression" => "*",
            "DivideAssignmentExpression" => "/",
            "ModuloAssignmentExpression" => "%",
            "AndAssignmentExpression" => "&",
            "OrAssignmentExpression" => "|",
            "ExclusiveOrAssignmentExpression" => "^",
            "LeftShiftAssignmentExpression" => "<<",
            "RightShiftAssignmentExpression" => ">>",
            _ => null,
        };
    }

    private static string NormalizeSimpleName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
    }
}

/// <summary>
/// 表示从调用文本中解析出的接收者信息。
/// </summary>
/// <param name="Name">接收者名称。</param>
/// <param name="Code">接收者代码。</param>
public sealed record InvocationReceiverInfo(string Name, string Code);
