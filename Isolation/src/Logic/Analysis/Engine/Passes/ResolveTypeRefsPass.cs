using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 为节点补齐“引用到哪个类型声明”的关系。
///
/// 这个 pass 参考的是 Joern 的 `TypeRefPass`：
/// - 源节点根据 `TypeDeclFullName` 之类的属性找目标类型声明；
/// - 找到后写入一条 `Ref` 关系。
///
/// 当前实现先做最小版：
/// - 只依赖字符串全名匹配；
/// - 只处理已经带有 `TypeDeclFullName` 属性的节点；
/// - 不引入复杂的延迟解析与外部依赖补全。
/// </summary>
public sealed class ResolveTypeRefsPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        IReadOnlyDictionary<string, CpgNode> typeDecls = builder.Graph
            .GetNodes(CpgNodeKind.TypeDecl)
            .Where(node => node.TryGetProperty<string>("FullName", out _))
            .ToDictionary(
                node => node.TryGetProperty<string>("FullName", out string? name) ? name! : string.Empty,
                node => node,
                StringComparer.Ordinal);

        foreach (CpgNode source in builder.Graph.Nodes)
        {
            if (!source.TryGetProperty<string>("TypeDeclFullName", out string? typeDeclFullName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(typeDeclFullName))
            {
                continue;
            }

            if (typeDecls.TryGetValue(typeDeclFullName, out CpgNode? target))
            {
                bool relationExists = builder.Graph
                    .GetOutgoingEdges(source.Id, CpgEdgeKind.Ref)
                    .Any(edge => edge.TargetId == target.Id);

                if (!relationExists)
                {
                    builder.AddEdge(source.Id, target.Id, CpgEdgeKind.Ref);
                }
            }
        }
    }
}
