using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 根据节点上预先记录的父节点编号补 AST 边。
///
/// 这个 pass 参考的是 Joern 的 `AstLinkerPass`。
/// 当前实现约定：
/// - 子节点如果带有 `AstParentId` 属性；
/// - 且该父节点存在于图中；
/// - 则补一条 `Ast` 边。
///
/// 这样设计的目的是把“节点创建”和“结构链接”分开，
/// 让前端构建器可以先专注产出事实，再由 pass 统一收边。
/// </summary>
public sealed class LinkAstPass : CpgPass
{

    protected override void Execute(CpgGraphBuilder builder)
    {
        foreach (CpgNode node in builder.Graph.Nodes)
        {
            if (!node.TryGetProperty<long>("AstParentId", out long parentId))
            {
                continue;
            }

            bool parentExists;
            try
            {
                builder.Graph.GetNode(parentId);
                parentExists = true;
            }
            catch (InvalidOperationException)
            {
                parentExists = false;
            }

            if (!parentExists)
            {
                continue;
            }

            bool relationExists = builder.Graph
                .GetOutgoingEdges(parentId, CpgEdgeKind.Ast)
                .Any(edge => edge.TargetId == node.Id);

            if (!relationExists)
            {
                builder.AddEdge(parentId, node.Id, CpgEdgeKind.Ast);
            }
        }
    }
}
