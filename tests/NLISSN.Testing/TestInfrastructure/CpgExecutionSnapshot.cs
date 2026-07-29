namespace RoslynPrototype.Testing.TestInfrastructure;

public sealed record CpgExecutionSnapshot(
  string GraphSnapshotVersion,
  IReadOnlyList<string> Nodes,
  IReadOnlyList<string> GraphEdges,
  IReadOnlyList<string> DirectMarks,
  IReadOnlyList<string> PropagatedMarks,
  IReadOnlyList<string> Decisions,
  IReadOnlyList<string> Diagnostics,
  string RewrittenSource,
  string DiffText);
