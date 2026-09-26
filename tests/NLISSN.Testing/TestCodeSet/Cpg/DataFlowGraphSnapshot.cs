using System.Text.Json;
using NLCPG.Model;

namespace RoslynPrototype.Tests.TestCodeSet.Cpg;

public static class DataFlowGraphSnapshot {
  // Resolve graph-owned text IDs, preserve enumeration order, include all node/edge metadata.
  public static string Capture(NLCPGGraph graph) {
    return JsonSerializer.Serialize(new {
      Nodes = graph.Nodes.Select(node => new {
        node.Kind, Name = graph.ResolveName(node), FullName = graph.ResolveFullName(node),
        Signature = graph.ResolveSignature(node), node.DispatchKind,
        TypeFullName = graph.ResolveTypeFullName(node), FilePath = graph.ResolveFilePath(node),
        node.SpanStart, node.SpanEnd, node.IsImplicit, node.NodeId, node.StableAnchor,
      }).ToArray(),
      Edges = graph.Edges.ToArray(),
    });
  }
}
