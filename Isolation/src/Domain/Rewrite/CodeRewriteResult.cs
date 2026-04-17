namespace Domain.Rewrite;

/// <summary>
/// 表示代码重写结果。
/// </summary>
public sealed class CodeRewriteResult
{
    private readonly List<string> diagnostics = new();

    private CodeRewriteResult(
        CodeRewriteKind rewriteKind,
        string targetName,
        string sourceCode,
        bool changed)
    {
        RewriteKind = rewriteKind;
        TargetName = targetName;
        SourceCode = sourceCode;
        Changed = changed;
    }

    /// <summary>
    /// 获取重写动作类型。
    /// </summary>
    public CodeRewriteKind RewriteKind { get; }

    /// <summary>
    /// 获取目标名称。
    /// </summary>
    public string TargetName { get; }

    /// <summary>
    /// 获取重写后的源码。
    /// </summary>
    public string SourceCode { get; }

    /// <summary>
    /// 获取是否发生改动。
    /// </summary>
    public bool Changed { get; }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(sourceCode);
        return new CodeRewriteResult(rewriteKind, targetName.Trim(), sourceCode, changed);
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
