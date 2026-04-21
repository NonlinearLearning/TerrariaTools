using Domain.Common;
using Domain.Rules;
using Domain.Workspaces.Events;

namespace Domain.Workspaces;

/// <summary>
/// 表示事件风暴中的工作区上下文聚合根。
/// </summary>
public sealed class WorkspaceContext : AggregateRoot<Guid>
{
    private static readonly RuleExecutionPolicy MinimalFallbackRuleSetPolicy = new(
        RuleParticipationMode.Candidate,
        RuleConflictMode.PreferHigherPriority,
        RuleFailureMode.Warn,
        RuleSafetyLevel.Balanced,
        RuleEvidenceMode.AttachReason);

    private readonly List<ProjectDescriptor> projects;
    private readonly List<DocumentPath> documents;
    private readonly List<ReferenceDescriptor> references;
    private bool isPrepared;

    private WorkspaceContext(
        Guid id,
        SolutionPath solutionPath,
        string languageVersion,
        RunMode runMode,
        InputDescriptor inputDescriptor,
        RuleSet ruleSet)
        : base(id)
    {
        SolutionPathValue = solutionPath;
        LanguageVersion = languageVersion;
        RunMode = runMode;
        InputDescriptor = inputDescriptor;
        RuleSet = ruleSet;
        LoadedAt = DateTimeOffset.UtcNow;
        projects = new List<ProjectDescriptor>();
        documents = new List<DocumentPath>();
        references = new List<ReferenceDescriptor>();
    }

    /// <summary>
    /// 获取解决方案路径。
    /// </summary>
    public string SolutionPath => SolutionPathValue.Value;

    /// <summary>
    /// 获取解决方案路径值对象。
    /// </summary>
    public SolutionPath SolutionPathValue { get; }

    /// <summary>
    /// 获取语言版本。
    /// </summary>
    public string LanguageVersion { get; }

    /// <summary>
    /// 获取运行模式。
    /// </summary>
    public RunMode RunMode { get; }

    /// <summary>
    /// 获取输入描述。
    /// </summary>
    public InputDescriptor InputDescriptor { get; }

    /// <summary>
    /// 获取规则集合。
    /// </summary>
    public RuleSet RuleSet { get; }

    /// <summary>
    /// 获取加载时间。
    /// </summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>
    /// 获取项目集合。
    /// </summary>
    public IReadOnlyCollection<ProjectDescriptor> Projects => projects.AsReadOnly();

    /// <summary>
    /// 获取文档集合。
    /// </summary>
    public IReadOnlyCollection<DocumentPath> Documents => documents.AsReadOnly();

    /// <summary>
    /// 获取引用集合。
    /// </summary>
    public IReadOnlyCollection<ReferenceDescriptor> References => references.AsReadOnly();

    public bool IsPrepared => isPrepared;

    /// <summary>
    /// 创建工作区上下文。
    /// </summary>
    /// <param name="solutionPath">解决方案路径。</param>
    /// <param name="languageVersion">语言版本。</param>
    /// <returns>聚合根实例。</returns>
    public static WorkspaceContext Create(
        string solutionPath,
        string languageVersion,
        RunMode runMode = RunMode.FullWorkflow,
        InputDescriptor? inputDescriptor = null,
        RuleSet? ruleSet = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageVersion);
        Domain.Workspaces.SolutionPath resolvedSolutionPath = Domain.Workspaces.SolutionPath.Create(solutionPath);
        RuleSet resolvedRuleSet = ruleSet ?? CreateMinimalFallbackRuleSet();
        InputDescriptor resolvedInputDescriptor = inputDescriptor ??
            InputDescriptor.Create(InputOrigin.Solution, resolvedSolutionPath.Value, runMode, resolvedRuleSet);
        if (resolvedInputDescriptor.RunMode != runMode)
        {
            throw new InvalidOperationException("输入描述的运行模式必须与工作区上下文一致。");
        }

        if (!string.Equals(resolvedInputDescriptor.RuleSet.Name, resolvedRuleSet.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("输入描述中的规则集必须与工作区上下文规则集一致。");
        }

        return new WorkspaceContext(
            Guid.NewGuid(),
            resolvedSolutionPath,
            languageVersion.Trim(),
            runMode,
            resolvedInputDescriptor,
            resolvedRuleSet);
    }

    private static RuleSet CreateMinimalFallbackRuleSet()
    {
        // 这里只保留聚合构造安全所需的最小兜底。
        // workspace / marking / workflow 的策略型默认规则继续由 Logic 层统一装配。
        return RuleSet.Create("default", MinimalFallbackRuleSetPolicy);
    }

    /// <summary>
    /// 增加项目描述。
    /// </summary>
    /// <param name="project">项目描述。</param>
    public void AddProject(ProjectDescriptor project)
    {
        RegisterProject(project);
    }

    /// <summary>
    /// 注册项目描述。
    /// </summary>
    public void RegisterProject(ProjectDescriptor project)
    {
        ArgumentNullException.ThrowIfNull(project);
        string normalizedProjectPath = SolutionPathValue.ResolveWorkspacePath(project.Path);

        if (projects.Any(item =>
                string.Equals(
                    SolutionPathValue.ResolveWorkspacePath(item.Path),
                    normalizedProjectPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        projects.Add(project);
    }

    /// <summary>
    /// 增加文档路径。
    /// </summary>
    /// <param name="documentPath">文档路径。</param>
    public void AddDocument(DocumentPath documentPath)
    {
        RegisterDocument(documentPath);
    }

    /// <summary>
    /// 注册文档路径。
    /// </summary>
    public void RegisterDocument(DocumentPath documentPath)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        string normalizedDocumentPath = SolutionPathValue.ResolveWorkspacePath(documentPath.Value);
        if (documents.Any(item =>
                string.Equals(
                    SolutionPathValue.ResolveWorkspacePath(item.Value),
                    normalizedDocumentPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        documents.Add(documentPath);
    }

    /// <summary>
    /// 增加引用描述。
    /// </summary>
    /// <param name="reference">引用描述。</param>
    public void AddReference(ReferenceDescriptor reference)
    {
        RegisterReference(reference);
    }

    /// <summary>
    /// 注册引用描述。
    /// </summary>
    public void RegisterReference(ReferenceDescriptor reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (references.Any(item => string.Equals(item.Name, reference.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        references.Add(reference);
    }

    public void Prepare(Guid correlationId)
    {
        ValidateConsistency();
        isPrepared = true;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("WorkspacePrepared", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new WorkspacePreparedDomainEvent(
            Id,
            resolvedCorrelationId,
            SolutionPath,
            projects.Count,
            documents.Count));
    }

    /// <summary>
    /// 校验工作区上下文输入一致性。
    /// </summary>
    public void ValidateConsistency()
    {
        if (InputDescriptor.RunMode != RunMode)
        {
            throw new InvalidOperationException("工作区运行模式与输入描述不一致。");
        }

        if (!string.Equals(InputDescriptor.RuleSet.Name, RuleSet.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("工作区规则集与输入描述规则集不一致。");
        }
    }

    /// <summary>
    /// 解析分析源路径。
    /// </summary>
    public string ResolveAnalysisSourcePath(string? requestedSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(requestedSourcePath))
        {
            return requestedSourcePath.Trim();
        }

        ProjectDescriptor? project = projects.FirstOrDefault();
        if (project is not null)
        {
            return SolutionPathValue.ResolveWorkspacePath(project.Path);
        }

        DocumentPath document = documents.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(document.Value))
        {
            return SolutionPathValue.ResolveWorkspacePath(document.Value);
        }

        return SolutionPath;
    }

    /// <summary>
    /// 解析用于分析的文档路径集合。
    /// </summary>
    public IReadOnlyCollection<string> ResolveAnalysisDocuments(int maxCount)
    {
        int count = Math.Max(maxCount, 1);
        string[] resolvedDocuments = documents
            .Take(count)
            .Select(document => SolutionPathValue.ResolveWorkspacePath(document.Value))
            .ToArray();

        return resolvedDocuments.Length > 0
            ? resolvedDocuments
            : [SolutionPath];
    }
}
