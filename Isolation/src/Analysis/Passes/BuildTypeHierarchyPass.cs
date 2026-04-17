using Analysis.Core;

namespace Analysis.Passes;

/// <summary>
/// 为类型声明补齐继承关系。
///
/// 这个 pass 对应 Joern 的 `TypeHierarchyPass`：
/// - 类型声明节点上通常会带有父类型全名列表；
/// - pass 负责把这些字符串事实转换成图边。
///
/// 当前实现约定：
/// - `InheritsFromTypeFullNames` 属性保存字符串列表；
/// - 目标节点是 `TYPE` 节点；
/// - 关系类型统一写成 `InheritsFrom`。
/// </summary>
public sealed class BuildTypeHierarchyPass : CpgPass
{
    /// <inheritdoc />
    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> types = builder.Graph
            .GetNodes(CpgNodeKind.Type)
            .Where(node => node.TryGetProperty<string>("FullName", out _))
            .ToDictionary(
                node => node.TryGetProperty<string>("FullName", out string? name) ? name! : string.Empty,
                node => node,
                StringComparer.Ordinal);

        foreach (CpgNode typeDecl in builder.Graph.GetNodes(CpgNodeKind.TypeDecl))
        {
            if (!typeDecl.TryGetProperty<IReadOnlyCollection<string>>("InheritsFromTypeFullNames", out IReadOnlyCollection<string>? baseTypes))
            {
                continue;
            }

            foreach (string baseType in baseTypes)
            {
                if (string.IsNullOrWhiteSpace(baseType))
                {
                    continue;
                }

                if (types.TryGetValue(baseType, out CpgNode? baseTypeNode))
                {
                    bool relationExists = builder.Graph
                        .GetOutgoingEdges(typeDecl.Id, CpgEdgeKind.InheritsFrom)
                        .Any(edge => edge.TargetId == baseTypeNode.Id);

                    if (!relationExists)
                    {
                        builder.AddEdge(typeDecl.Id, baseTypeNode.Id, CpgEdgeKind.InheritsFrom);
                    }
                }
            }
        }
    }
}
