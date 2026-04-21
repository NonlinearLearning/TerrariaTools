using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为带别名目标的类型声明补齐 `ALIAS_OF` 关系。
///
/// 这个 pass 对齐 Joern `AliasLinkerPass` 的最小职责：
/// - 从 `TYPE_DECL.AliasTypeFullName` 找到目标 `TYPE`；
/// - 建立 `ALIAS_OF` 边。
/// </summary>
public sealed class BuildAliasRelationPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> typeNodesByFullName = builder.Graph
            .GetNodes(CpgNodeKind.Type)
            .Where(node => node.TryGetProperty<string>("FullName", out string? fullName) &&
                           !string.IsNullOrWhiteSpace(fullName))
            .GroupBy(node => node.TryGetProperty<string>("FullName", out string? fullName) ? fullName! : string.Empty)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (CpgNode typeDeclNode in builder.Graph.GetNodes(CpgNodeKind.TypeDecl))
        {
            if (!typeDeclNode.TryGetProperty<string>("AliasTypeFullName", out string? aliasTypeFullName) ||
                string.IsNullOrWhiteSpace(aliasTypeFullName) ||
                !typeNodesByFullName.TryGetValue(aliasTypeFullName, out CpgNode? aliasedTypeNode))
            {
                continue;
            }

            bool edgeExists = builder.Graph
                .GetOutgoingEdges(typeDeclNode.Id, CpgEdgeKind.AliasOf)
                .Any(edge => edge.TargetId == aliasedTypeNode.Id);

            if (!edgeExists)
            {
                builder.AddEdge(typeDeclNode.Id, aliasedTypeNode.Id, CpgEdgeKind.AliasOf);
            }
        }
    }
}
