using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Application;
using RoslynPrototype.Testing.TestCodeSet.Cpg;
using RoslynPrototype.Testing.TestInfrastructure;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgPropertyEquivalenceTests
{
  [Property(MaxTest = 8, Replay = "12345,67891", Arbitrary = [typeof(GeneratedFixtureArbitraries)])]
  public Property BuildFromSource_GeneratedFixture_PreservesSerialSemantics(
    GeneratedCSharpFixture fixture)
  {
    var serial = Build(fixture, 1, persistence: null);
    var persistedRoot = Path.Combine(Path.GetTempPath(), "cpg-property", Guid.NewGuid().ToString("N"));
    try
    {
      var parallel = Build(fixture, 8, persistence: null);
      var persisted = Build(
        fixture,
        8,
        new CpgPersistenceOptions(persistedRoot, fixture.Id, StreamingMode: true));
      var snapshots = new Dictionary<string, CpgExecutionSnapshot>(StringComparer.Ordinal)
      {
        ["serial"] = CreateSnapshot(serial),
        ["parallel"] = CreateSnapshot(parallel),
        ["persisted"] = CreateSnapshot(persisted),
      };
      try
      {
        CpgExecutionSnapshotComparer.AssertEquivalent(snapshots["serial"], snapshots["parallel"]);
        CpgExecutionSnapshotComparer.AssertEquivalent(snapshots["serial"], snapshots["persisted"]);
        return true.ToProperty();
      }
      catch
      {
        FailureArtifactWriter.Write(
          Path.Combine("Build", "TestResults", "property-failures"),
          fixture,
          new Dictionary<string, string>
          {
            ["dop"] = "8",
            ["persistence"] = "true",
            ["replay"] = "12345,67891",
          },
          snapshots);
        throw;
      }
    }
    finally
    {
      if (Directory.Exists(persistedRoot))
      {
        Directory.Delete(persistedRoot, recursive: true);
      }
    }
  }

  private static RoslynCpgGraph Build(
    GeneratedCSharpFixture fixture,
    int maxDegreeOfParallelism,
    CpgPersistenceOptions? persistence)
  {
    var trees = fixture.Files
      .OrderBy(file => file.Key, StringComparer.Ordinal)
      .Select(file => CSharpSyntaxTree.ParseText(file.Value, path: file.Key))
      .ToArray();
    var primaryTree = trees.Single(tree =>
      string.Equals(tree.FilePath, fixture.PrimaryFileName, StringComparison.Ordinal));
    var semanticModel = RoslynCompilationFactory.CreateCompilation(trees).GetSemanticModel(primaryTree);
    return new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = maxDegreeOfParallelism,
      Persistence = persistence,
    }).BuildFromSemanticModel(
      semanticModel,
      primaryTree.GetRoot(),
      fixture.PrimarySource,
      fixture.PrimaryFileName);
  }

  private static CpgExecutionSnapshot CreateSnapshot(RoslynCpgGraph graph)
  {
    return new CpgExecutionSnapshot(
      graph.GraphSnapshotVersion,
      graph.Nodes.Select(node => $"{node.NodeId}:{node.Kind}:{node.DisplayKind}:{node.FilePath}:{node.SpanStart}:{node.SpanEnd}").ToArray(),
      graph.Edges.Select(edge => $"{edge.SourceNodeId}>{edge.TargetNodeId}:{edge.Kind}:{edge.ContextId}").ToArray(),
      [], [], [], [], string.Empty, string.Empty);
  }
}

public static class GeneratedFixtureArbitraries
{
  public static Arbitrary<GeneratedCSharpFixture> Fixture()
  {
    return Arb.From(Gen.Choose(0, 63).Select(GeneratedCSharpFixture.Create));
  }
}
