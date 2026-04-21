namespace Domain.Rewrite.Artifacts;

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
