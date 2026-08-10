using Microsoft.CodeAnalysis;

namespace NLISSN.Infrastructure.Workspace;

public enum WorkspaceGeneratedSourceKind
{
    None,
    MsBuildCompileItem,
    IntermediateOutput,
    RoslynSourceGenerator,
    Unknown
}

public sealed record WorkspaceDocumentSnapshot(
  string FilePath,
  string Source,
  SyntaxTree SyntaxTree,
  bool IsGenerated,
  WorkspaceGeneratedSourceKind GeneratedSourceKind,
  bool CanWrite);
