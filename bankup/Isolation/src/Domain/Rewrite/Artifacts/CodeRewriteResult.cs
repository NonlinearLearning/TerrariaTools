namespace Domain.Rewrite.Artifacts;

/// <summary>
/// 表示代码重写结果。
/// </summary>
public sealed class CodeRewriteResult
{
    private readonly List<string> diagnostics = new();

    private CodeRewriteResult(CodeRewriteArtifact artifact)
    {
        Artifact = artifact;
    }

    public CodeRewriteArtifact Artifact { get; }

    /// <summary>
    /// 获取重写动作类型。
    /// </summary>
    public CodeRewriteKind RewriteKind => Artifact.RewriteKind;

    /// <summary>
    /// 获取目标名称。
    /// </summary>
    public string TargetName => Artifact.TargetName;

    /// <summary>
    /// 获取目标名称值对象。
    /// </summary>
    public Domain.Common.TargetName TargetNameValue => Artifact.TargetNameValue;

    /// <summary>
    /// 获取重写后的源码。
    /// </summary>
    public string SourceCode => Artifact.Source.SourceCode;

    /// <summary>
    /// 获取是否发生改动。
    /// </summary>
    public bool Changed => Artifact.Changed;

    /// <summary>
    /// 获取诊断信息。
    /// </summary>
    public IReadOnlyCollection<string> Diagnostics => diagnostics.AsReadOnly();

    /// <summary>
    /// 创建代码重写结果。
    /// </summary>
    public static CodeRewriteResult Create(
        CodeRewriteKind rewriteKind,
        string targetName,
        string sourceCode,
        bool changed)
    {
        return Create(rewriteKind, Domain.Common.TargetName.Create(targetName), sourceCode, changed);
    }

    public static CodeRewriteResult Create(
        CodeRewriteKind rewriteKind,
        Domain.Common.TargetName targetName,
        string sourceCode,
        bool changed)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new CodeRewriteResult(new CodeRewriteArtifact(
            rewriteKind,
            targetName,
            new RewriteArtifactSource(sourceCode),
            changed));
    }

    /// <summary>
    /// 增加诊断信息。
    /// </summary>
    public void AddDiagnostic(string diagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        diagnostics.Add(diagnostic.Trim());
    }
}
