using System.Collections.ObjectModel;

namespace NLISSN.Core.Performance;

public sealed record WorkspaceProjectPerformanceFacts
{
  public WorkspaceProjectPerformanceFacts(
    string projectId,
    string projectPath,
    string projectName,
    string? targetFramework,
    DirectoryPerformanceFacts? directory,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
    ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

    ProjectId = projectId;
    ProjectPath = projectPath;
    ProjectName = projectName;
    TargetFramework = targetFramework;
    Directory = directory;
    Status = status;
    ErrorKind = errorKind;
  }

  public string ProjectId { get; }

  public string ProjectPath { get; }

  public string ProjectName { get; }

  public string? TargetFramework { get; }

  public DirectoryPerformanceFacts? Directory { get; }

  public PerformanceStatus Status { get; }

  public string? ErrorKind { get; }
}

public sealed record WorkspacePerformanceFacts
{
  public WorkspacePerformanceFacts(
    string itemId,
    IReadOnlyList<WorkspaceProjectPerformanceFacts>? projects = null,
    PerformanceAggregateSummary? projectSummary = null,
    PerformanceStageSample? stage = null,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

    ItemId = itemId;
    Projects = new ReadOnlyCollection<WorkspaceProjectPerformanceFacts>(
      (projects ?? Array.Empty<WorkspaceProjectPerformanceFacts>()).ToArray());
    ProjectSummary = projectSummary;
    Stage = stage;
    Status = status;
    ErrorKind = errorKind;
  }

  public string ItemId { get; }

  public IReadOnlyList<WorkspaceProjectPerformanceFacts> Projects { get; }

  public PerformanceAggregateSummary? ProjectSummary { get; }

  public PerformanceStageSample? Stage { get; }

  public PerformanceStatus Status { get; }

  public string? ErrorKind { get; }
}
