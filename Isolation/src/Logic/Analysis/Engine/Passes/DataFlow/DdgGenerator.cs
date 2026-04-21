using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 根据 reaching definition 解生成 `ReachingDef` 边。
///
/// 对应 Joern `DdgGenerator.scala`。本地版本直接写入内存图，不经过 diff graph。
/// </summary>
public sealed class DdgGenerator
{
    /// <summary>
    /// 将求解结果写入图。
    /// </summary>
    public void Generate(
        CpgGraphBuilder builder,
        DataFlowSolution<CpgNode, IReadOnlySet<DataFlowDefinition>> solution)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(solution);

        foreach ((CpgNode node, IReadOnlySet<DataFlowDefinition> definitions) in solution.In)
        {
            foreach (DataFlowDefinition definition in definitions)
            {
                if (definition.SourceNodeId == node.Id)
                {
                    continue;
                }

                bool exists = builder.Graph.GetOutgoingEdges(definition.SourceNodeId, CpgEdgeKind.ReachingDef)
                    .Any(edge => edge.TargetId == node.Id &&
                                 string.Equals(edge.Label, definition.Label, StringComparison.Ordinal));
                if (!exists)
                {
                    builder.AddEdge(definition.SourceNodeId, node.Id, CpgEdgeKind.ReachingDef, definition.Label);
                }
            }
        }
    }
}
