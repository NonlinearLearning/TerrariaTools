namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示最小运行闭包产物。
/// </summary>
public sealed class RuntimeClosureArtifact
{
    public RuntimeClosureArtifact(
        string className,
        string rootMethodName,
        string closureClassName,
        RewriteArtifactSource source)
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
