namespace NLISSN.Infrastructure.Workspace;

public sealed record WorkspaceSolutionSnapshot(
  string InputPath,
  bool IsSolution,
  string SolutionDirectory,
  string? SelectedProjectPath,
  IReadOnlyList<WorkspaceProjectSnapshot> Projects,
  IReadOnlyList<WorkspaceInputDiagnostic> Diagnostics,
  string Fingerprint);
