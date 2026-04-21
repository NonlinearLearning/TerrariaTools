namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛表达式节点构建中的纯命名与兜底规则。
/// </summary>
public static class ExpressionNodeConventions
{
    /// <summary>
    /// 为 lambda 构造稳定身份信息。
    /// </summary>
    public static LambdaIdentity BuildLambdaIdentity(
        int ordinal,
        string? fileName,
        string? resolvedFullName,
        string? resolvedSignature,
        string? resolvedSymbolId,
        string? operationId)
    {
        string lambdaName = FrontendGraphConventions.BuildLambdaName(ordinal);
        string lambdaFullName = string.IsNullOrWhiteSpace(resolvedFullName)
            ? FrontendGraphConventions.BuildLambdaFallbackFullName(fileName, lambdaName)
            : resolvedFullName.Trim();
        string lambdaSignature = string.IsNullOrWhiteSpace(resolvedSignature)
            ? FrontendGraphConventions.Unknown
            : resolvedSignature.Trim();
        string declaredSymbolId = string.IsNullOrWhiteSpace(resolvedSymbolId)
            ? FrontendGraphConventions.BuildLambdaFallbackSymbolId(operationId)
            : resolvedSymbolId.Trim();
        return new LambdaIdentity(lambdaName, lambdaFullName, lambdaSignature, declaredSymbolId);
    }

    /// <summary>
    /// 构造 lambda 的兜底签名。
    /// </summary>
    public static string BuildLambdaFallbackSignature(string? returnTypeFullName, IEnumerable<string?> parameterTypeFullNames)
    {
        ArgumentNullException.ThrowIfNull(parameterTypeFullNames);
        string parameterTypes = string.Join(", ", parameterTypeFullNames.Select(Normalize));
        return $"{Normalize(returnTypeFullName)} ({parameterTypes})";
    }

    /// <summary>
    /// 构造属性调用兜底元数据。
    /// </summary>
    public static FallbackCallMetadata BuildFallbackCallMetadata()
    {
        return new FallbackCallMetadata(
            FrontendGraphConventions.Unknown,
            FrontendGraphConventions.Unknown,
            FrontendGraphConventions.Unknown,
            FrontendGraphConventions.DynamicDispatch);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? FrontendGraphConventions.Unknown : value.Trim();
    }
}

/// <summary>
/// 表示 lambda 的稳定身份信息。
/// </summary>
public sealed record LambdaIdentity(
    string Name,
    string FullName,
    string Signature,
    string DeclaredSymbolId);

/// <summary>
/// 表示调用节点的兜底元数据。
/// </summary>
public sealed record FallbackCallMetadata(
    string MethodFullName,
    string Signature,
    string TypeFullName,
    string DispatchType);
