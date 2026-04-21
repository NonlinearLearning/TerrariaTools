using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 面向数据流反向搜索的查询引擎。
///
/// 旧版类型名仅叫 `Engine`，在 `Domain.Analysis.Engine.*` 新命名空间下会与
/// `Engine` 命名空间本身冲突，因此显式收敛为语义化名称。
/// </summary>
public sealed class DataFlowQueryEngine
{
    private readonly QueryEngine innerEngine;

    /// <summary>
    /// 使用目标图初始化引擎。
    /// </summary>
    public DataFlowQueryEngine(CpgGraph graph)
    {
        innerEngine = new QueryEngine(graph);
    }

    /// <summary>
    /// 从 sink 反向搜索可达 source。
    /// </summary>
    public IReadOnlyList<DataFlowPath> Backwards(
        IEnumerable<long> sinkNodeIds,
        IEnumerable<long> sourceNodeIds,
        int maxDepth = 64)
    {
        return innerEngine.BackwardFromSinks(sinkNodeIds, sourceNodeIds, maxDepth);
    }
}
