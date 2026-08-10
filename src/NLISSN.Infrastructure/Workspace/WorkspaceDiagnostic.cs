namespace NLISSN.Infrastructure.Workspace;

public enum WorkspaceDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record WorkspaceInputDiagnostic(
  string Code,
  WorkspaceDiagnosticSeverity Severity,
  string Message,
  string? Path = null,
  string? ProjectPath = null,
  string? TargetFramework = null);
