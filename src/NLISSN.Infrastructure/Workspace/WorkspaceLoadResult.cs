namespace NLISSN.Infrastructure.Workspace;

public sealed record WorkspaceLoadResult(
  WorkspaceSolutionSnapshot? Snapshot,
  IReadOnlyList<WorkspaceInputDiagnostic> Diagnostics)
{
    public bool IsSuccess => Snapshot is not null &&
      Diagnostics.All(diagnostic => diagnostic.Severity != WorkspaceDiagnosticSeverity.Error);
}
