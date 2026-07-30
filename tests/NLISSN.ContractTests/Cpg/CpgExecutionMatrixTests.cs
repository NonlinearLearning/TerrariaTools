using NLCPG.Builder;
using NLCPG.Model;
using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using RoslynPrototype.Testing.TestInfrastructure;
using NLISSN.Rules;
using Xunit;
using Xunit.Sdk;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgExecutionMatrixTests
{
  private const string FixtureId = "control-flow-references-call";
  private const string Source = SObjectExpressionSources.TargetNameSource;

  [Theory]
  [CombinatorialData]
  public void BuildFromSource_ExecutionMatrix_MatchesSerialBaseline([CombinatorialValues(1, 8, 12, 14, 16)] int maxDegreeOfParallelism, bool persistenceEnabled, CpgPersistenceDurabilityMode durabilityMode, [CombinatorialValues(1, 2)] int maxConcurrentShardFileWrites)
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-execution-matrix", Guid.NewGuid().ToString("N"));
    try
    {
      var serial = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = 1,
      }).BuildFromSource(Source, "matrix.cs");
      var serialAnalysis = Analyze(1);
      var options = NLCPGBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = maxDegreeOfParallelism,
        Persistence = persistenceEnabled
          ? new CpgPersistenceOptions(
            root,
            $"{FixtureId}-dop-{maxDegreeOfParallelism}-{durabilityMode}-{maxConcurrentShardFileWrites}",
            DurabilityMode: durabilityMode,
            StreamingMode: true,
            MaxConcurrentShardFileWrites: maxConcurrentShardFileWrites)
          : null,
      };
      var actual = new NLCPGBuilder(options).BuildFromSource(Source, "matrix.cs");
      var actualAnalysis = Analyze(maxDegreeOfParallelism);

      try
      {
        var serialSnapshot = CreateSnapshot(serial, serialAnalysis);
        var actualSnapshot = CreateSnapshot(actual, actualAnalysis);
        Assert.NotEmpty(serialSnapshot.DirectMarks);
        Assert.NotEmpty(serialSnapshot.PropagatedMarks);
        Assert.NotEmpty(serialSnapshot.Decisions);
        Assert.NotEmpty(serialSnapshot.RewrittenSource);
        Assert.NotEmpty(serialSnapshot.DiffText);
        CpgExecutionSnapshotComparer.AssertEquivalent(serialSnapshot, actualSnapshot);
      }
      catch (InvalidOperationException exception)
      {
        throw new XunitException(
          $"fixture={FixtureId};dop={maxDegreeOfParallelism};persistence={persistenceEnabled};" +
          $"durability={durabilityMode};writerConcurrency={maxConcurrentShardFileWrites}; {exception.Message}");
      }
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  private static PrototypeAnalysisResult Analyze(int maxDegreeOfParallelism)
  {
    return CreateApplication().Analyze(
      Source,
      "matrix.cs",
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["target-name"] = "s",
        ["max-degree-of-parallelism"] = maxDegreeOfParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["enable-group-parallelism"] = "true",
      });
  }

  private static  ApplicationService CreateApplication()
  {
    return new ApplicationService(RuleRegistry.CreateDefaultRules());
  }

  private static CpgExecutionSnapshot CreateSnapshot(NLCPGGraph graph, PrototypeAnalysisResult analysis)
  {
    return new CpgExecutionSnapshot(
      graph.GraphSnapshotVersion,
      graph.Nodes.Select(node =>
        $"{node.NodeId}:{node.Kind}:{node.DisplayKind}:{node.FilePath}:{node.SpanStart}:{node.SpanEnd}").ToArray(),
      graph.Edges.Select(edge =>
        $"{edge.SourceNodeId}>{edge.TargetNodeId}:{edge.Kind}:{edge.ContextId}").ToArray(),
      analysis.SeedMarks.Select(mark =>
        $"{mark.RuleId}:{mark.SyntaxNode.SpanStart}:{mark.SyntaxNode.Span.End}:{mark.SyntaxNode.RawKind}:{mark.Reason}").ToArray(),
      analysis.PropagatedMarks.Select(mark =>
        $"{mark.RuleId}:{mark.Mark.SyntaxNode.SpanStart}:{mark.Mark.SyntaxNode.Span.End}:{mark.Depth}:{mark.SourceMark.SyntaxNode.SpanStart}").ToArray(),
      analysis.Decisions.Select(decision =>
        $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.End}:{decision.FinalNode.RawKind}:{decision.Reason}:{decision.ReplacementNode?.SpanStart}").ToArray(),
      (analysis.Diagnostics ?? []).Select(diagnostic =>
        $"{diagnostic.Id}:{diagnostic.Severity}:{diagnostic.FilePath}:{diagnostic.Start}:{diagnostic.End}:{diagnostic.Message}").ToArray(),
      analysis.RewrittenSource ?? string.Empty,
      analysis.Diff.ToString());
  }
}
