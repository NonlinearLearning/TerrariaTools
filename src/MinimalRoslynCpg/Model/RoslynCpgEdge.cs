using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

public sealed record RoslynCpgEdge(string SourceId, string TargetId, RoslynCpgEdgeKind Kind);
