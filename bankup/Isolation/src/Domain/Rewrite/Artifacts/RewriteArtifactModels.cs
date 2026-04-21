using Domain.Common;

namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示通用改写产物源码。
/// </summary>
public sealed class RewriteArtifactSource
{
    public RewriteArtifactSource(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        SourceCode = sourceCode;
    }

    public string SourceCode { get; }
}

/// <summary>
/// 表示成员切片产物。
/// </summary>
public sealed class MemberSliceArtifact
{
    public MemberSliceArtifact(string className, string rootMemberName, RewriteArtifactSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMemberName);
        ArgumentNullException.ThrowIfNull(source);
        ClassName = className.Trim();
        RootMemberName = rootMemberName.Trim();
        Source = source;
    }

    public string ClassName { get; }

    public string RootMemberName { get; }

    public RewriteArtifactSource Source { get; }
}

/// <summary>
/// 表示影子类产物。
/// </summary>
public sealed class ShadowClassArtifact
{
    public ShadowClassArtifact(string className, string shadowClassName, RewriteArtifactSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowClassName);
        ArgumentNullException.ThrowIfNull(source);
        ClassName = className.Trim();
        ShadowClassName = shadowClassName.Trim();
        Source = source;
    }

    public string ClassName { get; }

    public string ShadowClassName { get; }

    public RewriteArtifactSource Source { get; }
}

/// <summary>
/// 表示最小运行闭包产物。
/// </summary>
public sealed class RuntimeClosureArtifact
{
    public RuntimeClosureArtifact(string className, string rootMethodName, string closureClassName, RewriteArtifactSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootMethodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(closureClassName);
        ArgumentNullException.ThrowIfNull(source);
        ClassName = className.Trim();
        RootMethodName = rootMethodName.Trim();
        ClosureClassName = closureClassName.Trim();
        Source = source;
    }

    public string ClassName { get; }

    public string RootMethodName { get; }

    public string ClosureClassName { get; }

    public RewriteArtifactSource Source { get; }
}

/// <summary>
/// 表示代码改写产物。
/// </summary>
public sealed class CodeRewriteArtifact
{
    public CodeRewriteArtifact(CodeRewriteKind rewriteKind, string targetName, RewriteArtifactSource source, bool changed)
        : this(rewriteKind, Domain.Common.TargetName.Create(targetName), source, changed)
    {
    }

    public CodeRewriteArtifact(CodeRewriteKind rewriteKind, TargetName targetName, RewriteArtifactSource source, bool changed)
    {
        ArgumentNullException.ThrowIfNull(source);
        RewriteKind = rewriteKind;
        TargetNameValue = targetName;
        Source = source;
        Changed = changed;
    }

    public CodeRewriteKind RewriteKind { get; }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public RewriteArtifactSource Source { get; }

    public bool Changed { get; }
}
