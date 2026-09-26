using NLCPG.Builder;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class PartitionPerformanceDiagnosticsTests
{
  private const string Source = """
    public sealed class Sample
    {
      public int First(int value)
      {
        var copy = value + 1;
        return copy;
      }

      public int Second(int value)
      {
        var copy = value + 2;
        return copy;
      }
    }
    """;

  [Fact]
  public void DiagnosticMode_ReportsCollectionAndMaterializationFacts_WithoutChangingGraph()
  {
    var sink = new RecordingPartitionPerformanceEventSink();
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = 2,
      PerformanceDiagnostics = NLCPGPerformanceDiagnosticsMode.Diagnostic,
      PartitionPerformanceEventSink = sink,
    };

    var graph = new NLCPGBuilder(options).BuildFromSource(Source, "diagnostic.cs");

    Assert.NotEmpty(graph.Nodes);
    Assert.NotEmpty(sink.Events);
    Assert.Contains(sink.Events, item => item.StageId == PartitionPerformanceStageId.SyntaxCollection);
    Assert.Contains(sink.Events, item => item.StageId == PartitionPerformanceStageId.SyntaxMaterialization);
    Assert.Contains(sink.Events, item => item.StageId == PartitionPerformanceStageId.OperationCollection);
    Assert.Contains(sink.Events, item => item.StageId == PartitionPerformanceStageId.OperationMaterialization);
    Assert.All(sink.Events, item =>
    {
      Assert.False(string.IsNullOrWhiteSpace(item.PartitionId));
      Assert.True(item.PartitionIndex >= 0);
      Assert.True(item.InputCount >= 0);
      Assert.True(item.OutputCount >= 0);
      Assert.True(item.WallElapsedMilliseconds >= 0);
      Assert.True(item.AccumulatedElapsedMilliseconds >= 0);
      Assert.Null(item.QueueWaitMilliseconds);
      Assert.NotNull(item.RequestedMaxDegreeOfParallelism);
    });
  }

  [Fact]
  public void NormalMode_DoesNotPublishHighCardinalityPartitionEvents()
  {
    var sink = new RecordingPartitionPerformanceEventSink();
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = 2,
      PartitionPerformanceEventSink = sink,
    };

    _ = new NLCPGBuilder(options).BuildFromSource(Source, "normal.cs");

    Assert.Empty(sink.Events);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(16)]
  public void DiagnosticMode_DifferentDegreesOfParallelism_PreserveGraphSnapshot(int dop)
  {
    var baseline = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = 1,
    }).BuildFromSource(Source, "snapshot.cs");

    var actual = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = dop,
      PerformanceDiagnostics = NLCPGPerformanceDiagnosticsMode.Diagnostic,
      PartitionPerformanceEventSink = new RecordingPartitionPerformanceEventSink(),
    }).BuildFromSource(Source, "snapshot.cs");

    Assert.Equal(DescribeGraph(baseline), DescribeGraph(actual));
  }

  private static string[] DescribeGraph(NLCPG.Model.NLCPGGraph graph)
  {
    var nodes = graph.Nodes.Select(node => string.Join(
      "|",
      node.NodeId,
      node.Kind,
      graph.ResolveDisplayKind(node),
      graph.ResolveName(node),
      graph.ResolveFullName(node),
      graph.ResolveSignature(node),
      graph.ResolveFilePath(node),
      node.SpanStart,
      node.SpanEnd));
    var edges = graph.Edges.Select(edge => string.Join(
      "|",
      edge.SourceNodeId,
      edge.TargetNodeId,
      edge.Kind,
      edge.ContextId,
      edge.StructuredLabel?.StableKey));
    return nodes.Concat(edges).OrderBy(item => item, StringComparer.Ordinal).ToArray();
  }

  private sealed class RecordingPartitionPerformanceEventSink : IPartitionPerformanceEventSink
  {
    public List<PartitionPerformanceEvent> Events { get; } = [];

    public void Record(PartitionPerformanceEvent performanceEvent)
    {
      Events.Add(performanceEvent);
    }
  }
}
