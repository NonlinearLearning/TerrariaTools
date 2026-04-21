namespace Domain.Analysis.Engine.Core;

/// <summary>
/// 表示图中一条有向边。
///
/// 当前边模型保持极简，只保留：
/// - 起点
/// - 终点
/// - 边类型
///
/// 这样做是有意为之：
/// Joern 的很多核心 pass 首先依赖的是边标签，而不是复杂的边属性。
/// 当前阶段先把“关系种类”做稳定，比一开始就引入复杂边模型更重要。
/// </summary>
public sealed class CpgEdge
{
    /// <summary>
    /// 初始化一条边。
    /// </summary>
    /// <param name="sourceId">起点节点编号。</param>
    /// <param name="targetId">终点节点编号。</param>
    /// <param name="kind">关系类型。</param>
    /// <param name="label">边标签。当前主要给 `ReachingDef` 这类边承载变量名。</param>
    public CpgEdge(long sourceId, long targetId, CpgEdgeKind kind, string label = "")
    {
        SourceId = sourceId;
        TargetId = targetId;
        Kind = kind;
        Label = label ?? string.Empty;
    }

    /// <summary>
    /// 获取起点节点编号。
    /// </summary>
    public long SourceId { get; }

    /// <summary>
    /// 获取终点节点编号。
    /// </summary>
    public long TargetId { get; }

    /// <summary>
    /// 获取边的关系类别。
    /// </summary>
    public CpgEdgeKind Kind { get; }

    /// <summary>
    /// 获取边标签。
    /// </summary>
    public string Label { get; }
}
