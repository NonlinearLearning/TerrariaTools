using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Language.DataFlow.DotExtension;

/// <summary>
/// 输出 DDG 子图的 DOT 文本。
///
/// 对应 Joern `dotextension/DdgNodeDot.scala`。该功能用于调试数据流边，
/// 不依赖外部 Graphviz，只负责生成文本。
/// </summary>
public static class DdgNodeDot
{
    /// <summary>
    /// 为指定起点生成 DDG DOT。
    /// </summary>
    public static string ToDot(CpgGraph graph, IEnumerable<CpgNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);

        HashSet<long> selected = nodes.Select(node => node.Id).ToHashSet();
        IEnumerable<CpgEdge> edges = graph.Edges
            .Where(edge => edge.Kind == CpgEdgeKind.ReachingDef)
            .Where(edge => selected.Contains(edge.SourceId) || selected.Contains(edge.TargetId));

        return "digraph ddg {" + Environment.NewLine +
               string.Join(Environment.NewLine, edges.Select(edge => FormatEdge(graph, edge))) +
               Environment.NewLine + "}";
    }

    private static string FormatEdge(CpgGraph graph, CpgEdge edge)
    {
        return $"  \"{Label(graph.GetNode(edge.SourceId))}\" -> \"{Label(graph.GetNode(edge.TargetId))}\" [label=\"{edge.Label}\"];";
    }

    private static string Label(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) && !string.IsNullOrWhiteSpace(name)
            ? $"{node.Kind}:{name}"
            : $"{node.Kind}:{node.Id}";
    }
}
