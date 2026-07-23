using MinimalRoslynCpg.Builder;
using RoslynPrototype.Testing.TestInfrastructure;
using VerifyXunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgReviewedSnapshotTests
{
  [Fact]
  public Task BuildFromSource_ComplexFragment_MatchesReviewedSnapshot()
  {
    const string source = "class Example { int Add(int left, int right) => left + right; int Run(int value) => Add(value, 2); }";
    var graph = new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault()).BuildFromSource(source, "reviewed.cs");
    var projection = CpgSnapshotNormalizer.Normalize(graph.Edges.Select(edge =>
      $"source={edge.SourceNodeId} target={edge.TargetNodeId} kind={edge.Kind} context={edge.ContextId}"));
    return Verify(projection.Take(3));
  }

  [Fact]
  public Task RewritePlanProjection_MatchesReviewedSnapshot()
  {
    return Verify(CpgSnapshotNormalizer.Normalize([
      "operation=replace anchor=42 replacement=value + 1 elapsedMs=2",
      "operation=delete anchor=17 path=C:\\temp\\plan.json",
    ]));
  }

  [Fact]
  public Task CatalogManifestProjection_MatchesReviewedSnapshot()
  {
    return Verify(CpgSnapshotNormalizer.Normalize([
      "build=alpha shard=method span=12-30 ts=2026-07-23T03:00:00Z",
      "build=alpha shard=file-skeleton span=0-60 elapsedMs=4",
    ]));
  }
}
