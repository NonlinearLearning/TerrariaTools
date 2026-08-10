namespace NLISSN.Infrastructure.Workspace;

public enum WorkspaceRestoreMode
{
  Disabled,
  Enabled
}

public enum WorkspaceGeneratedSourceMode
{
  Include,
  Exclude
}

public enum WorkspaceGeneratorMode
{
  Disabled,
  Enabled
}

public sealed record WorkspaceInputOptions(
  string Path,
  string? ProjectPath = null,
  string? TargetFramework = null,
  string Configuration = "Debug",
  string Platform = "AnyCPU",
  WorkspaceRestoreMode RestoreMode = WorkspaceRestoreMode.Disabled,
  WorkspaceGeneratedSourceMode GeneratedSourceMode = WorkspaceGeneratedSourceMode.Include,
  WorkspaceGeneratorMode GeneratorMode = WorkspaceGeneratorMode.Disabled,
  bool RequireCleanCompilation = true,
  string? TargetDocumentPath = null)
{
  public string SolutionRoot => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
}
