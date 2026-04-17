using Analysis.Core;

namespace Analysis.Passes.DataFlow;

/// <summary>
/// 根据 `BuildReachingDefinitionsPass` 的结果补齐最小 DDG。
///
/// 这里沿用 Joern 的做法，用 `ReachingDef` 边表达数据依赖，
/// 不额外发明一套新的边类型。
/// </summary>
public sealed class BuildDdgPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode node in builder.Graph.Nodes)
        {
            if (!node.TryGetProperty<DataFlowDefinition[]>(
                    BuildReachingDefinitionsPass.IncomingDefinitionsPropertyName,
                    out DataFlowDefinition[]? definitions) ||
                definitions is null)
            {
                continue;
            }

            foreach (DataFlowDefinition definition in definitions)
            {
                bool relationExists = builder.Graph
                    .GetOutgoingEdges(definition.SourceNodeId, CpgEdgeKind.ReachingDef)
                    .Any(edge =>
                        edge.TargetId == node.Id &&
                        string.Equals(edge.Label, definition.Label, StringComparison.Ordinal));
                if (!relationExists)
                {
                    builder.AddEdge(definition.SourceNodeId, node.Id, CpgEdgeKind.ReachingDef, definition.Label);
                }
            }
        }
    }
}
