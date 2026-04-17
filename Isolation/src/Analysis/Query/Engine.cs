using Analysis.Core;

namespace Analysis.Query;

/// <summary>
/// Joern `Engine.scala` 的同名 C# 入口。
///
/// 已有 `QueryEngine` 负责实际搜索。这个类型保留 Joern 文件级命名，
/// 让“指定文件映射”可以一一对应。
/// </summary>
public sealed class Engine
{
    private readonly QueryEngine innerEngine;

    /// <summary>
    /// 使用目标图初始化引擎。
    /// </summary>
    public Engine(CpgGraph graph)
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
