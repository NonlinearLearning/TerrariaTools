using System.Reflection;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class NLCPGGraphIndexStorageContractTests
{
  [Fact]
  public void Freeze_UsesOneCanonicalNodeArrayAndOrdinalBuckets()
  {
    var graph = new NLCPGGraph();
    var first = graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "first",
      FilePath: "index.cs",
      SpanStart: 10,
      SpanEnd: 15));
    var second = graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "second",
      FilePath: "index.cs",
      SpanStart: 20,
      SpanEnd: 26));
    graph.AddEdge(first, second, NLCPGEdgeKind.DataFlow);
    graph.FreezeQueryIndex();

    var index = typeof(NLCPGGraph)
      .GetField("_queryIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
      .GetValue(graph)!;
    var orderedNodes = GetProperty(index, "OrderedNodes", BindingFlags.Instance | BindingFlags.NonPublic);
    var canonicalNodes = GetProperty(index, "CanonicalNodes", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.Same(orderedNodes, canonicalNodes);

    AssertOrdinalBuckets(index, "NodesByKind");
    AssertOrdinalBuckets(index, "NodesByFilePath");

    Assert.Equal(
      new[] { "first", "second" },
      graph.GetNodes(NLCPGNodeKind.Operation).Select(graph.ResolveName));
    Assert.Equal(
      new[] { "first", "second" },
      graph.GetNodesInFileSpan("index.cs", 0, 100).Select(graph.ResolveName));
  }

  private static object GetProperty(object instance, string name, BindingFlags flags)
  {
    return instance.GetType().GetProperty(name, flags)!.GetValue(instance)!;
  }

  private static void AssertOrdinalBuckets(object index, string propertyName)
  {
    var buckets = (System.Collections.IDictionary)GetProperty(
      index,
      propertyName,
      BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotEmpty(buckets);
    foreach (var value in buckets.Values)
    {
      Assert.IsType<int[]>(value);
    }
  }
}
