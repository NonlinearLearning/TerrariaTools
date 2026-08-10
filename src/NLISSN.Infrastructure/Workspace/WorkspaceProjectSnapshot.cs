using Microsoft.CodeAnalysis.CSharp;

namespace NLISSN.Infrastructure.Workspace;

public sealed record WorkspaceProjectSnapshot(
  string ProjectPath,
  string ProjectName,
  string AssemblyName,
  string? TargetFramework,
  string Configuration,
  string Platform,
  CSharpCompilation Compilation,
  CSharpParseOptions ParseOptions,
  CSharpCompilationOptions CompilationOptions,
  IReadOnlyList<string> PreprocessorSymbols,
  IReadOnlyList<WorkspaceDocumentSnapshot> Documents,
  IReadOnlyList<WorkspaceReferenceSnapshot> References,
  int AnalyzerReferenceCount,
  string Fingerprint);
