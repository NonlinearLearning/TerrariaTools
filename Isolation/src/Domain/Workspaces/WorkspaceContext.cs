using Domain.Common;

namespace Domain.Workspaces;

/// <summary>
/// 表示事件风暴中的工作区上下文聚合根。
/// </summary>
public sealed class WorkspaceContext : AggregateRoot<Guid>
{
    private readonly List<ProjectDescriptor> projects;
    private readonly List<DocumentPath> documents;
    private readonly List<ReferenceDescriptor> references;

    private WorkspaceContext(Guid id, string solutionPath, string languageVersion)
        : base(id)
    {
        SolutionPath = solutionPath;
        LanguageVersion = languageVersion;
        LoadedAt = DateTimeOffset.UtcNow;
        projects = new List<ProjectDescriptor>();
        documents = new List<DocumentPath>();
        references = new List<ReferenceDescriptor>();
    }

    /// <summary>
    /// 获取解决方案路径。
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    /// 获取语言版本。
    /// </summary>
    public string LanguageVersion { get; }

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

    /// <summary>
    /// 创建工作区上下文。
    /// </summary>
    /// <param name="solutionPath">解决方案路径。</param>
    /// <param name="languageVersion">语言版本。</param>
    /// <returns>聚合根实例。</returns>
    public static WorkspaceContext Create(string solutionPath, string languageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageVersion);
        return new WorkspaceContext(Guid.NewGuid(), solutionPath.Trim(), languageVersion.Trim());
    }

    /// <summary>
    /// 增加项目描述。
    /// </summary>
    /// <param name="project">项目描述。</param>
    public void AddProject(ProjectDescriptor project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (projects.Any(item => string.Equals(item.Path, project.Path, StringComparison.OrdinalIgnoreCase)))
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
        if (documents.Contains(documentPath))
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
        ArgumentNullException.ThrowIfNull(reference);

        if (references.Any(item => string.Equals(item.Name, reference.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        references.Add(reference);
    }
}
