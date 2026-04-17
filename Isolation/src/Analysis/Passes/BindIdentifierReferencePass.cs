using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 把标识符节点绑定到真实声明节点。
///
/// 这里优先使用前端写入的稳定符号标识，而不是按名字猜。
/// 这样同名局部变量、参数、成员同时存在时也不容易串线。
/// </summary>
public sealed class BindIdentifierReferencePass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> declarationNodes = builder.Graph.Nodes
            .Where(node => node.TryGetProperty<string>("DeclaredSymbolId", out string? symbolId) &&
                           !string.IsNullOrWhiteSpace(symbolId))
            .GroupBy(node => node.TryGetProperty<string>("DeclaredSymbolId", out string? symbolId) ? symbolId! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode identifierNode in builder.Graph.GetNodes(CpgNodeKind.Identifier))
        {
            if (!identifierNode.TryGetProperty<string>("ReferencedSymbolId", out string? referencedSymbolId) ||
                string.IsNullOrWhiteSpace(referencedSymbolId))
            {
                continue;
            }

            if (!declarationNodes.TryGetValue(referencedSymbolId, out CpgNode? targetNode))
            {
                continue;
            }

            bool relationExists = builder.Graph
                .GetOutgoingEdges(identifierNode.Id, CpgEdgeKind.Ref)
                .Any(edge => edge.TargetId == targetNode.Id);

            if (!relationExists)
            {
                builder.AddEdge(identifierNode.Id, targetNode.Id, CpgEdgeKind.Ref);
            }
        }
    }
}
