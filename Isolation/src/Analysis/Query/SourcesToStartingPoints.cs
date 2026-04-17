using Analysis.Core;

namespace Analysis.Query;

/// <summary>
/// 把 source 节点集合规范化为查询起点集合。
///
/// 当前实现很轻，只负责去重和合法性校验。
/// 这样后续如果要按类型过滤 source，或补充 source 标签，
/// 不需要改动引擎主体。
/// </summary>
public sealed class SourcesToStartingPoints
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化转换器。
    /// </summary>
    /// <param name="graph">目标图。</param>
    public SourcesToStartingPoints(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 规范化 source 节点集合。
    /// </summary>
    /// <param name="sourceNodeIds">原始 source 节点集合。</param>
    /// <returns>去重后的合法 source 集合。</returns>
    public IReadOnlySet<long> Normalize(IEnumerable<long> sourceNodeIds)
    {
        ArgumentNullException.ThrowIfNull(sourceNodeIds);

        HashSet<long> result = new();
        foreach (long sourceNodeId in sourceNodeIds)
        {
            _ = graph.GetNode(sourceNodeId);
            result.Add(sourceNodeId);
        }

        return result;
    }
}
