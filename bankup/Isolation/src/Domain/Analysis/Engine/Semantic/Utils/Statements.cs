using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Semantic.Utils;

/// <summary>
/// 统计 CPG 中的方法级语句。
///
/// 对应 Joern `semanticcpg/utils/Statements.scala`。
/// </summary>
public static class Statements
{
    /// <summary>
    /// 统计所有方法的顶层表达式数量。
    /// </summary>
    public static long CountAll(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetNodes(CpgNodeKind.Method)
            .SelectMany(method => graph.GetOutgoingEdges(method.Id, CpgEdgeKind.Ast))
            .Select(edge => graph.GetNode(edge.TargetId))
            .Count(IsStatementNode);
    }

    private static bool IsStatementNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.Local
            or CpgNodeKind.Identifier
            or CpgNodeKind.Literal;
    }
}
