namespace Domain.Analysis.Engine.Core;

/// <summary>
/// 为前端和各类 pass 提供统一的图构建入口。
///
/// Joern 中很多 pass 通过 `DiffGraphBuilder` 批量追加修改。
/// 当前这版 C# 实现暂时不单独引入 diff graph，但仍然保留一个
/// builder 类型，原因是：
/// 1. 调用方式更接近 Joern，迁移思路更稳定。
/// 2. 未来如果要改成差量构建，不需要重写所有 pass 接口。
/// </summary>
public sealed class CpgGraphBuilder
{
    /// <summary>
    /// 使用目标图初始化一个构建器。
    /// </summary>
    /// <param name="graph">当前要被修改的图。</param>
    public CpgGraphBuilder(CpgGraph graph)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 获取当前正在构建的图。
    /// </summary>
    public CpgGraph Graph { get; }

    /// <summary>
    /// 创建一个新节点。
    /// </summary>
    public CpgNode CreateNode(CpgNodeKind kind) => Graph.CreateNode(kind);

    /// <summary>
    /// 新增一条边。
    /// </summary>
    public CpgEdge AddEdge(long sourceId, long targetId, CpgEdgeKind kind, string label = "") =>
        Graph.AddEdge(sourceId, targetId, kind, label);
}
