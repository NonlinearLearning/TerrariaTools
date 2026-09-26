using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

public sealed class CpgFragmentReducerContractTests
{
    [Fact]
    public void ReduceInto_IsIndependentOfFragmentArrivalOrder_AndDeduplicatesStableFacts()
    {
        var firstAnchor = Anchor(0, 4, StableNodeRole.Operation);
        var secondAnchor = Anchor(5, 9, StableNodeRole.Operation);
        var missingBoundary = Anchor(10, 12, StableNodeRole.Symbol);
        var first = Descriptor(firstAnchor);
        var second = Descriptor(secondAnchor);
        var edge = new CpgEdgeCandidate(
          firstAnchor,
          secondAnchor,
          NLCPGEdgeKind.OpChild,
          StructuredLabel: null,
          ContextId: null,
          CallSiteContext: null);
        var duplicateEdge = edge;
        var unavailableEdge = new CpgEdgeCandidate(
          secondAnchor,
          missingBoundary,
          NLCPGEdgeKind.OpResolvesToSymbol,
          StructuredLabel: null,
          ContextId: null,
          CallSiteContext: null);
        var fragments = new[]
        {
            Fragment(
              2,
              1,
              new[] { second },
              new[] { duplicateEdge },
              new CpgBoundaryReference(
                firstAnchor,
                "first-operation",
                CpgBoundaryReferenceKind.Local,
                IsAvailable: true)),
            Fragment(
              1,
              0,
              new[] { first, first },
              new[] { edge, unavailableEdge },
              new CpgBoundaryReference(
                secondAnchor,
                "second-operation",
                CpgBoundaryReferenceKind.Local,
                IsAvailable: true),
              new CpgBoundaryReference(
                missingBoundary,
                "missing-symbol",
                CpgBoundaryReferenceKind.Unknown,
                IsAvailable: false)),
        };

        var firstGraph = new NLCPGGraph();
        var firstMetrics = new CpgFragmentReducer().ReduceInto(firstGraph, fragments);
        firstGraph.FreezeQueryIndex();
        var secondGraph = new NLCPGGraph();
        var secondMetrics = new CpgFragmentReducer().ReduceInto(secondGraph, fragments.Reverse().ToArray());
        secondGraph.FreezeQueryIndex();

        Assert.Equal(2, firstGraph.Nodes.Count);
        Assert.Single(firstGraph.Edges);
        Assert.Equal(1, firstMetrics.DeduplicatedNodeCount);
        Assert.Equal(1, firstMetrics.DeduplicatedEdgeCount);
        Assert.Equal(1, firstMetrics.SkippedUnavailableBoundaryEdgeCount);
        Assert.Equal(firstMetrics, secondMetrics);
        Assert.Equal(Describe(firstGraph), Describe(secondGraph));
    }

    private static LocalCpgFragment Fragment(
      long batchId,
      int stableOrder,
      IReadOnlyList<CpgNodeDescriptor> nodes,
      IReadOnlyList<CpgEdgeCandidate> edges,
      params CpgBoundaryReference[] boundaries)
    {
        return new LocalCpgFragment(
          batchId,
          "reducer.cs",
          stableOrder,
          nodes,
          edges,
          Array.Empty<CpgMethodSummary>(),
          boundaries,
          CpgFragmentMetrics.Empty,
          Array.Empty<CpgDiagnostic>());
    }

    private static CpgNodeDescriptor Descriptor(StableNodeAnchor anchor)
    {
        return new CpgNodeDescriptor(
          anchor,
          NLCPGNodeKind.Operation,
          NameId: 0,
          FullNameId: 0,
          SignatureId: 0,
          DispatchKind: null,
          TypeFullNameId: 0,
          FilePathId: anchor.FilePathId,
          SpanStart: anchor.SpanStart,
          SpanEnd: anchor.SpanEnd,
          IsImplicit: false);
    }

    private static StableNodeAnchor Anchor(int start, int end, StableNodeRole role)
    {
        return new StableNodeAnchor(
          NLCPGNodeKind.Operation,
          FilePathId: 1,
          start,
          end,
          role,
          Ordinal: 0,
          ExtraKeyId: 1);
    }

    private static string[] Describe(NLCPGGraph graph)
    {
        var nodes = graph.Nodes.Select(node => string.Join(
          "|",
          node.NodeId,
          node.Kind,
          node.StableAnchor,
          node.SpanStart,
          node.SpanEnd));
        var edges = graph.Edges.Select(edge => string.Join(
          "|",
          edge.SourceNodeId,
          edge.TargetNodeId,
          edge.Kind));
        return nodes.Concat(edges).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
