namespace TerrariaTools.Dome.Core.Cpg;

public static class NodeIdFactory
{
    private static string BuildMethodPath(string? containingTypeName, string? methodName)
    {
        return string.IsNullOrWhiteSpace(containingTypeName)
            ? methodName ?? "unknown"
            : $"{containingTypeName}.{methodName}";
    }

    public static string TypeDecl(string? typeFullName)
    {
        return $"type:{typeFullName}";
    }

    public static string Method(string? containingTypeName, string? methodName)
    {
        return string.IsNullOrWhiteSpace(containingTypeName)
            ? $"method:{methodName}"
            : $"method:{containingTypeName}.{methodName}";
    }

    public static string MethodReturn(string? containingTypeName, string? methodName)
    {
        return $"method-return:{BuildMethodPath(containingTypeName, methodName)}";
    }

    public static string Block(string? containingTypeName, string? methodName)
    {
        return $"block:{BuildMethodPath(containingTypeName, methodName)}";
    }

    public static string Call(string? containingTypeName, string? ownerMethodName, string? targetMethodName, int order)
    {
        string resolvedTargetMethodName = string.IsNullOrWhiteSpace(targetMethodName) ? "unknown" : targetMethodName;
        return $"call:{BuildMethodPath(containingTypeName, ownerMethodName)}:{resolvedTargetMethodName}:{order}";
    }

    public static string MethodParameterIn(string? containingTypeName, string? methodName, string? parameterName, int order)
    {
        return $"param-in:{BuildMethodPath(containingTypeName, methodName)}:{parameterName}:{order}";
    }

    public static string MethodParameterOut(string? containingTypeName, string? methodName, string? parameterName, int order)
    {
        return $"param-out:{BuildMethodPath(containingTypeName, methodName)}:{parameterName}:{order}";
    }

    public static string Local(string? containingTypeName, string? methodName, string? localName, int order)
    {
        return $"local:{BuildMethodPath(containingTypeName, methodName)}:{localName}:{order}";
    }

    public static string Return(string? containingTypeName, string? methodName, int order)
    {
        return $"return:{BuildMethodPath(containingTypeName, methodName)}:{order}";
    }

    public static string ControlStructure(string? containingTypeName, string? methodName, int order)
    {
        return $"control-structure:{BuildMethodPath(containingTypeName, methodName)}:{order}";
    }

    public static string FieldIdentifier(
        string? containingTypeName,
        string? ownerMethodName,
        string? targetMethodName,
        int callIndex,
        int argumentIndex)
    {
        string resolvedTargetMethodName = string.IsNullOrWhiteSpace(targetMethodName) ? "unknown" : targetMethodName;
        return $"field-identifier:{BuildMethodPath(containingTypeName, ownerMethodName)}:{resolvedTargetMethodName}:{callIndex}:{argumentIndex}";
    }

    public static string FieldIdentifierReceiver(
        string? containingTypeName,
        string? ownerMethodName,
        string? targetMethodName,
        int callIndex)
    {
        string resolvedTargetMethodName = string.IsNullOrWhiteSpace(targetMethodName) ? "unknown" : targetMethodName;
        return $"field-identifier-receiver:{BuildMethodPath(containingTypeName, ownerMethodName)}:{resolvedTargetMethodName}:{callIndex}";
    }

    public static string MethodReturnTypeRef(string? containingTypeName, string? methodName, string? typeFullName)
    {
        return $"type-ref:method:{BuildMethodPath(containingTypeName, methodName)}:return:{typeFullName}";
    }
}
