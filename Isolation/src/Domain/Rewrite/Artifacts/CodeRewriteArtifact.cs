using Domain.Common;

namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示代码改写产物。
/// </summary>
public sealed class CodeRewriteArtifact
{
    public CodeRewriteArtifact(
        CodeRewriteKind rewriteKind,
        string targetName,
        RewriteArtifactSource source,
        bool changed)
        : this(rewriteKind, Domain.Common.TargetName.Create(targetName), source, changed)
    {
    }

    public CodeRewriteArtifact(
        CodeRewriteKind rewriteKind,
        TargetName targetName,
        RewriteArtifactSource source,
        bool changed)
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
