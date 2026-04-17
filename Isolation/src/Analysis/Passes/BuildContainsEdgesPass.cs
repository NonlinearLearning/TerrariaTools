using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 将结构型 AST 边提升成拥有者视角的 CONTAINS 边。
///
/// 这个 pass 对应 Joern 的 `ContainsEdgePass` 方向。
/// 当前实现采取最小策略：
/// - 直接复用已有的 `Ast` 边；
/// - 只要父节点和子节点存在结构关系，就补一条 `Contains` 边。
///
/// 这样虽然比 Joern 的完整实现更朴素，但已经足够支撑：
/// - 从文件遍历到命名空间；
/// - 从类型遍历到方法；
/// - 从方法遍历到内部表达式与语句。
/// </summary>
public sealed class BuildContainsEdgesPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgEdge astEdge in builder.Graph.Edges.Where(edge => edge.Kind == CpgEdgeKind.Ast).ToArray())
        {
            bool relationExists = builder.Graph
                .GetOutgoingEdges(astEdge.SourceId, CpgEdgeKind.Contains)
                .Any(edge => edge.TargetId == astEdge.TargetId);

            if (!relationExists)
            {
                builder.AddEdge(astEdge.SourceId, astEdge.TargetId, CpgEdgeKind.Contains);
            }
        }
    }
}
