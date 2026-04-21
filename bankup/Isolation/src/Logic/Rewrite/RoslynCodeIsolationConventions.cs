using Domain.Rewrite.Artifacts;

namespace Logic.Rewrite;

/// <summary>
/// 收敛 Roslyn 代码隔离适配器使用的命名、诊断和错误消息规则。
/// </summary>
public static class RoslynCodeIsolationConventions
{
    public const string CompilationAssemblyName = "Isolation.Roslyn.CodeIsolation";

    public const string RewriteCompletedDiagnostic = "Roslyn rewrite completed.";

    public const string ShadowClassSuffix = "Shadow";

    public const string RuntimeClosureClassSuffix = "RuntimeClosure";

    public const string TrustedPlatformAssembliesKey = "TRUSTED_PLATFORM_ASSEMBLIES";

    public static string BuildMemberTargetName(string className, string memberName)
    {
        return $"{className}.{memberName}";
    }

    public static string BuildShadowClassName(string className)
    {
        return $"{className}{ShadowClassSuffix}";
    }

    public static string BuildRuntimeClosureClassName(string className)
    {
        return $"{className}{RuntimeClosureClassSuffix}";
    }

    public static string BuildDeleteClassFailedMessage(string className)
    {
        return $"删除类型失败：{className}";
    }

    public static string BuildDeleteMethodFailedMessage(string className, string methodName)
    {
        return $"删除方法失败：{BuildMemberTargetName(className, methodName)}";
    }

    public static string BuildClassNotFoundMessage(string className)
    {
        return $"未找到类型：{className}";
    }

    public static string BuildMethodNotFoundMessage(string className, string methodName)
    {
        return $"未找到方法：{BuildMemberTargetName(className, methodName)}";
    }

    public static string BuildMethodSymbolResolveFailedMessage(string className, string methodName)
    {
        return $"无法解析方法符号：{BuildMemberTargetName(className, methodName)}";
    }

    public static string BuildTrustedPlatformAssembliesMissingMessage()
    {
        return $"未找到 {TrustedPlatformAssembliesKey}。";
    }

    public static CodeRewriteResult AddCompletedDiagnostic(CodeRewriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.AddDiagnostic(RewriteCompletedDiagnostic);
        return result;
    }
}
