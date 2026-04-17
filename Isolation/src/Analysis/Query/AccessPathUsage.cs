using Analysis.Core;

namespace Analysis.Query;

/// <summary>
/// 把表达式节点拆成“被跟踪的基础节点 + 访问路径”。
///
/// 对应 Joern `AccessPathUsage.scala`。Joern 面向多语言 AST，这里先覆盖 C# CPG
/// 当前会产生的标识符、成员和调用节点。
/// </summary>
public sealed class AccessPathUsage
{
    private AccessPathUsage(long? baseNodeId, IReadOnlyList<string> accessPath)
    {
        BaseNodeId = baseNodeId;
        AccessPath = accessPath;
    }

    /// <summary>
    /// 获取访问路径的基础节点编号。
    /// </summary>
    public long? BaseNodeId { get; }

    /// <summary>
    /// 获取从基础节点向外展开的成员路径。
    /// </summary>
    public IReadOnlyList<string> AccessPath { get; }

    /// <summary>
    /// 从节点提取访问路径。
    /// </summary>
    public static AccessPathUsage FromNode(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        (CpgNode? baseNode, List<string> path) = Extract(graph, node);
        return new AccessPathUsage(baseNode?.Id, Normalize(path));
    }

    private static (CpgNode? BaseNode, List<string> ReversedPath) Extract(CpgGraph graph, CpgNode node)
    {
        if (node.Kind == CpgNodeKind.Identifier || node.Kind == CpgNodeKind.Local || node.Kind == CpgNodeKind.MethodParameterIn)
        {
            return (node, new List<string>());
        }

        if (node.Kind == CpgNodeKind.Member)
        {
            CpgNode? parent = graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast)
                .Select(edge => graph.GetNode(edge.SourceId))
                .FirstOrDefault();
            (CpgNode? baseNode, List<string> path) = parent is null
                ? (null, new List<string>())
                : Extract(graph, parent);
            path.Add(NodeName(node));
            return (baseNode, path);
        }

        if (node.Kind == CpgNodeKind.Call && IsMemberAccess(node))
        {
            CpgNode? receiver = graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast)
                .Select(edge => graph.GetNode(edge.TargetId))
                .FirstOrDefault();
            (CpgNode? baseNode, List<string> path) = receiver is null
                ? (null, new List<string>())
                : Extract(graph, receiver);
            path.Add(NodeName(node));
            return (baseNode, path);
        }

        return (node, new List<string>());
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> path)
    {
        return path.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
    }

    private static bool IsMemberAccess(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) &&
               (string.Equals(name, "<operator>.fieldAccess", StringComparison.Ordinal) ||
                string.Equals(name, "<operator>.indirectFieldAccess", StringComparison.Ordinal));
    }

    private static string NodeName(CpgNode node)
    {
        return node.TryGetProperty<string>("Name", out string? name) ? name ?? string.Empty : string.Empty;
    }
}
