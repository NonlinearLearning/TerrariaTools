using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Query;

namespace Logic.Analysis.Engine.Language.DataFlow;

/// <summary>
/// 数据流节点遍历扩展。
///
/// 对应 Joern `ExtendedCfgNode.scala`。它把普通 `Traversal` 提升为可执行
/// `ddgIn`、`reachableBy` 和 `reachableByFlows` 的数据流遍历。
/// </summary>
public sealed class ExtendedCfgNode
{
    private readonly Traversal traversal;

    /// <summary>
    /// 使用当前节点遍历初始化数据流扩展。
    /// </summary>
    public ExtendedCfgNode(Traversal traversal)
    {
        this.traversal = traversal ?? throw new ArgumentNullException(nameof(traversal));
    }

    /// <summary>
    /// 沿 `ReachingDef` 入边访问数据流前驱。
    /// </summary>
    public Traversal DdgIn()
    {
        return traversal.DdgIn();
    }

    /// <summary>
    /// 返回能到达当前 sink 的 source 节点。
    /// </summary>
    public IReadOnlyList<CpgNode> ReachableBy(Traversal sourceTraversal)
    {
        ArgumentNullException.ThrowIfNull(sourceTraversal);

        IReadOnlyList<DataFlowPath> paths = ReachableByInternal(sourceTraversal);
        HashSet<long> sourceIds = sourceTraversal.ToList().Select(node => node.Id).ToHashSet();
        return paths.Select(path => traversal.Graph.GetNode(path.NodeIds.First()))
            .Where(node => sourceIds.Contains(node.Id))
            .DistinctBy(node => node.Id)
            .ToArray();
    }

    /// <summary>
    /// 返回能解释 source 到当前 sink 的数据流路径。
    /// </summary>
    public IReadOnlyList<Path> ReachableByFlows(Traversal sourceTraversal)
    {
        ArgumentNullException.ThrowIfNull(sourceTraversal);

        return ReachableByInternal(sourceTraversal)
            .Select(path => new Path(RemoveConsecutiveDuplicates(path.NodeIds.Select(traversal.Graph.GetNode))))
            .DistinctBy(path => string.Join(",", path.Elements.Select(node => node.Id)))
            .ToArray();
    }

    /// <summary>
    /// 返回带完整节点序列的查询结果。
    /// </summary>
    public IReadOnlyList<DataFlowPath> ReachableByDetailed(Traversal sourceTraversal)
    {
        ArgumentNullException.ThrowIfNull(sourceTraversal);
        return ReachableByInternal(sourceTraversal);
    }

    private IReadOnlyList<DataFlowPath> ReachableByInternal(Traversal sourceTraversal)
    {
        long[] sinks = traversal.ToList().Select(node => node.Id).Distinct().Order().ToArray();
        long[] sources = sourceTraversal.ToList().Select(node => node.Id).Distinct().Order().ToArray();
        return new DataFlowQueryEngine(traversal.Graph).Backwards(sinks, sources);
    }

    private static IReadOnlyList<CpgNode> RemoveConsecutiveDuplicates(IEnumerable<CpgNode> nodes)
    {
        List<CpgNode> result = new();
        foreach (CpgNode node in nodes)
        {
            if (result.Count == 0 || result[^1].Id != node.Id)
            {
                result.Add(node);
            }
        }

        return result;
    }
}

/// <summary>
/// 提供 `Traversal` 到 `ExtendedCfgNode` 的转换入口。
/// </summary>
public static class ExtendedCfgNodeExtensions
{
    /// <summary>
    /// 将普通遍历转换为数据流遍历。
    /// </summary>
    public static ExtendedCfgNode AsExtendedCfgNode(this Traversal traversal)
    {
        return new ExtendedCfgNode(traversal);
    }
}
