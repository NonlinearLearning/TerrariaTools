using NLISSN.Core.Performance;

namespace NLISSN.Application;

public static class WorkspacePerformanceFactAggregator
{
  public static string CreateProjectId(string projectPath, string? targetFramework)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
    return $"{Path.GetFullPath(projectPath)}|{targetFramework ?? "unknown"}";
  }

  public static WorkspacePerformanceFacts Aggregate(
    string itemId,
    IReadOnlyList<WorkspaceProjectPerformanceFacts> projects,
    PerformanceStageSample? stage = null,
    PerformanceStatus status = PerformanceStatus.Completed,
    string? errorKind = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
    ArgumentNullException.ThrowIfNull(projects);

    var orderedProjects = projects
      .OrderBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
      .ThenBy(project => project.TargetFramework, StringComparer.OrdinalIgnoreCase)
      .ThenBy(project => project.ProjectId, StringComparer.Ordinal)
      .ToArray();
    var measurements = orderedProjects
      .Where(project => project.Status == PerformanceStatus.Completed)
      .Select(project => new
      {
        project.ProjectId,
        WallElapsedMs = project.Directory?.Stage?.WallElapsedMs,
        AccumulatedElapsedMs = project.Directory?.Stage?.AccumulatedElapsedMs
      })
      .Where(measurement => measurement.WallElapsedMs.HasValue)
      .Select(measurement => new
      {
        measurement.ProjectId,
        WallElapsedMs = measurement.WallElapsedMs!.Value,
        AccumulatedElapsedMs = measurement.AccumulatedElapsedMs
      })
      .ToArray();
    var summary = new PerformanceAggregateSummary(
      orderedProjects.Length,
      orderedProjects.Count(project => project.Status == PerformanceStatus.Completed),
      measurements.Length == 0 ? null : measurements.Sum(measurement => measurement.WallElapsedMs),
      measurements.Length == 0 ? null : measurements.Max(measurement => measurement.WallElapsedMs),
      measurements.Any(measurement => measurement.AccumulatedElapsedMs.HasValue)
        ? measurements
          .Where(measurement => measurement.AccumulatedElapsedMs.HasValue)
          .Sum(measurement => measurement.AccumulatedElapsedMs!.Value)
        : null,
      measurements.Any(measurement => measurement.AccumulatedElapsedMs.HasValue)
        ? measurements
          .Where(measurement => measurement.AccumulatedElapsedMs.HasValue)
          .Max(measurement => measurement.AccumulatedElapsedMs!.Value)
        : null,
      measurements
        .OrderByDescending(measurement => measurement.WallElapsedMs)
        .ThenBy(measurement => measurement.ProjectId, StringComparer.Ordinal)
        .Select(measurement => measurement.ProjectId)
        .FirstOrDefault(),
      measurements.Length == 0
        ? null
        : measurements.Max(measurement => measurement.WallElapsedMs));

    return new WorkspacePerformanceFacts(
      itemId,
      orderedProjects,
      summary,
      stage,
      status,
      errorKind);
  }
}
