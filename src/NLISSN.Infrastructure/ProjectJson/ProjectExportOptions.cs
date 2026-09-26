using NLISSN.Infrastructure.Workspace;

namespace NLCPG.ProjectJson;

public sealed record ProjectExportOptions
{
    public ProjectExportOptions(
      string ProjectPath,
      string? OutputPath = null,
      string? TargetFramework = null,
      string Configuration = "Debug",
      string Platform = "AnyCPU",
      WorkspaceRestoreMode RestoreMode = WorkspaceRestoreMode.Disabled,
      bool IncludeGenerated = false,
      int MaxDegreeOfParallelism = 12,
      bool ResumeExistingOutput = false,
      long ProjectMemoryBudgetBytes = 64L * 1024 * 1024,
      long ProjectBaselineReservationBytes = 8L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProjectPath);
        if (MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));
        }

        if (ProjectMemoryBudgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectMemoryBudgetBytes));
        }

        if (ProjectBaselineReservationBytes < 0 ||
            ProjectBaselineReservationBytes >= ProjectMemoryBudgetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(ProjectBaselineReservationBytes));
        }

        this.ProjectPath = ProjectPath;
        this.OutputPath = OutputPath;
        this.TargetFramework = TargetFramework;
        this.Configuration = Configuration;
        this.Platform = Platform;
        this.RestoreMode = RestoreMode;
        this.IncludeGenerated = IncludeGenerated;
        this.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
        this.ResumeExistingOutput = ResumeExistingOutput;
        this.ProjectMemoryBudgetBytes = ProjectMemoryBudgetBytes;
        this.ProjectBaselineReservationBytes = ProjectBaselineReservationBytes;
    }

    public string ProjectPath { get; }

    public string? OutputPath { get; }

    public string? TargetFramework { get; }

    public string Configuration { get; }

    public string Platform { get; }

    public WorkspaceRestoreMode RestoreMode { get; }

    public bool IncludeGenerated { get; }

    public int MaxDegreeOfParallelism { get; }

    public bool ResumeExistingOutput { get; }

    public long ProjectMemoryBudgetBytes { get; }

    public long ProjectBaselineReservationBytes { get; }

    public int ProjectWorkerCount => Math.Min(12, Math.Max(1, MaxDegreeOfParallelism));

    public int LocalBuilderDegreeOfParallelism => 1;

    public string FullProjectPath => Path.GetFullPath(ProjectPath);

    public string FullOutputPath => Path.GetFullPath(
      OutputPath ?? Path.Combine(
        Path.GetDirectoryName(FullProjectPath)!,
        "Build",
        "NLCPG-json"));

    public int EffectiveMaxDegreeOfParallelism => ProjectWorkerCount;
}
