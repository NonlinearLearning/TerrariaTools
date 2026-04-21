using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;

namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 提供变量使用切片入口。
///
/// 这个类型对应 Joern `UsageSlicing.scala` 的核心目标：
/// 从声明节点出发，收集引用它的标识符和这些标识符所在的调用。
/// </summary>
public sealed class UsageSlicing
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化使用切片入口。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public UsageSlicing(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 计算某个声明节点的使用切片。
    /// </summary>
    /// <param name="declarationNodeId">声明节点编号。</param>
    /// <returns>可序列化的数据流切片。</returns>
    public DataFlowSlice CalculateForDeclaration(long declarationNodeId)
    {
        CpgNode declarationNode = graph.GetNode(declarationNodeId);
        List<CpgNode> nodes = new() { declarationNode };
        List<CpgEdge> edges = new();

        foreach (CpgEdge refEdge in graph.GetIncomingEdges(declarationNodeId, CpgEdgeKind.Ref))
        {
            CpgNode identifierNode = graph.GetNode(refEdge.SourceId);
            nodes.Add(identifierNode);
            edges.Add(refEdge);

            CpgNode? parentCall = FindAstParentCall(identifierNode);
            if (parentCall is not null)
            {
                nodes.Add(parentCall);
            }
        }

        return new DataFlowSlice(
            nodes.GroupBy(node => node.Id).Select(group => new SliceNode(group.First())),
            edges.GroupBy(edge => (edge.SourceId, edge.TargetId, edge.Kind, edge.Label))
                .Select(group => new SliceEdge(group.First())));
    }

    private CpgNode? FindAstParentCall(CpgNode node)
    {
        if (!node.TryGetProperty<long>("AstParentId", out long parentId))
        {
            return null;
        }

        CpgNode parentNode = graph.GetNode(parentId);
        if (parentNode.Kind == CpgNodeKind.Call)
        {
            return parentNode;
        }

        return parentNode.TryGetProperty<long>("AstParentId", out _)
            ? FindAstParentCall(parentNode)
            : null;
    }
}
