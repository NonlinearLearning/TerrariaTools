using Analysis.Core;

namespace Analysis.Query;

/// <summary>
/// 提供进入图查询能力的统一入口。
///
/// 这个类型对应 Joern 查询入口的最小替代：
/// - 不实现完整 DSL；
/// - 只提供当前阶段最需要的节点查询和数据流查询入口；
/// - 保持只读，不在查询过程中修改图。
/// </summary>
public sealed class CpgQuery
{
    private readonly CpgGraph graph;

    /// <summary>
    /// 使用目标图初始化查询入口。
    /// </summary>
    /// <param name="graph">要查询的图。</param>
    public CpgQuery(CpgGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>
    /// 获取节点查询入口。
    /// </summary>
    /// <returns>新的节点查询对象。</returns>
    public NodeQuery Nodes()
    {
        return new NodeQuery(graph, graph.Nodes);
    }

    /// <summary>
    /// 获取最小数据流查询入口。
    /// </summary>
    /// <returns>新的数据流查询对象。</returns>
    public DataFlowQuery DataFlow()
    {
        return new DataFlowQuery(graph);
    }
}
