using Domain.Common.Events;

namespace Domain.Workspaces.Events;

/// <summary>
/// 表示工作区已准备完成。
/// </summary>
public sealed class WorkspacePreparedDomainEvent : DomainEventBase
{
    public WorkspacePreparedDomainEvent(
        Guid workspaceContextId,
        Guid correlationId,
        string solutionPath,
        int projectCount,
        int documentCount)
        : base(
            "WorkspacePrepared",
            "Input/Workspace",
            workspaceContextId,
            correlationId,
            null,
            $"工作区已准备：{solutionPath}，项目 {projectCount} 个，文档 {documentCount} 个。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentOutOfRangeException.ThrowIfNegative(projectCount);
        ArgumentOutOfRangeException.ThrowIfNegative(documentCount);
        SolutionPath = solutionPath.Trim();
        ProjectCount = projectCount;
        DocumentCount = documentCount;
    }

    public string SolutionPath { get; }

    public int ProjectCount { get; }

    public int DocumentCount { get; }
}
