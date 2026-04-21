using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为方法引用节点补齐到方法声明的关系。
///
/// 这里处理的是“拿到方法本身”的场景，而不是“调用方法”的场景。
/// 例如委托赋值、方法组引用，更适合走这条关系。
/// </summary>
public sealed class BuildMethodReferencePass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> methodsBySymbolId = builder.Graph
            .GetNodes(CpgNodeKind.Method)
            .Where(node => node.TryGetProperty<string>("DeclaredSymbolId", out string? symbolId) &&
                           !string.IsNullOrWhiteSpace(symbolId))
            .GroupBy(node => node.TryGetProperty<string>("DeclaredSymbolId", out string? symbolId) ? symbolId! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode methodRefNode in builder.Graph.GetNodes(CpgNodeKind.MethodRef))
        {
            if (!methodRefNode.TryGetProperty<string>("ReferencedSymbolId", out string? referencedSymbolId) ||
                string.IsNullOrWhiteSpace(referencedSymbolId))
            {
                continue;
            }

            if (!methodsBySymbolId.TryGetValue(referencedSymbolId, out CpgNode? methodNode))
            {
                continue;
            }

            bool relationExists = builder.Graph
                .GetOutgoingEdges(methodRefNode.Id, CpgEdgeKind.MethodRef)
                .Any(edge => edge.TargetId == methodNode.Id);

            if (!relationExists)
            {
                builder.AddEdge(methodRefNode.Id, methodNode.Id, CpgEdgeKind.MethodRef);
            }
        }
    }
}
