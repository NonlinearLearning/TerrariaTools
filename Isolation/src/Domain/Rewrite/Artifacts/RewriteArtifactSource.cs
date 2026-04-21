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
