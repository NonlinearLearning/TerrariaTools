namespace Analysis.Core;

/// <summary>
/// 保存分析模块使用的内存态代码属性图。
///
/// 这是阶段一的核心产物：
/// - 前端负责创建基础节点和边。
/// - 阶段一 pass 负责补齐结构事实。
/// - 阶段二 pass 负责补齐语义关系。
///
/// 这里故意不用复杂图引擎，主要原因有三个：
/// 1. 当前仓库还没有现成的图基础设施。
/// 2. 用户明确要求先落核心代码，不先做外围平台。
/// 3. 先把语义模型跑通，比先引入存储复杂度更重要。
/// </summary>
public sealed class CpgGraph
{
    private readonly Dictionary<long, CpgNode> nodesById = new();
    private readonly List<CpgEdge> edges = new();
    private long nextNodeId = 1;

    /// <summary>
    /// 获取所有节点。
    /// </summary>
    public IReadOnlyCollection<CpgNode> Nodes => nodesById.Values;

    /// <summary>
    /// 获取所有边。
    /// </summary>
    public IReadOnlyList<CpgEdge> Edges => edges;

    /// <summary>
    /// 创建一个新节点并注册到图中。
    /// </summary>
    /// <param name="kind">新节点的 schema 类型。</param>
    /// <returns>创建后的节点对象。</returns>
    public CpgNode CreateNode(CpgNodeKind kind)
    {
        CpgNode node = new(nextNodeId++, kind);
        nodesById.Add(node.Id, node);
        return node;
    }

    /// <summary>
    /// 在两个已有节点之间新增一条有向边。
    /// </summary>
    /// <param name="sourceId">起点节点编号。</param>
    /// <param name="targetId">终点节点编号。</param>
    /// <param name="kind">边类型。</param>
    /// <param name="label">边标签。当前主要给数据流边承载变量名。</param>
    /// <returns>创建后的边对象。</returns>
    public CpgEdge AddEdge(long sourceId, long targetId, CpgEdgeKind kind, string label = "")
    {
        EnsureNodeExists(sourceId);
        EnsureNodeExists(targetId);

        CpgEdge edge = new(sourceId, targetId, kind, label);
        edges.Add(edge);
        return edge;
    }

    /// <summary>
    /// 删除满足条件的边。
    /// </summary>
    /// <param name="sourceId">起点节点编号。</param>
    /// <param name="targetId">终点节点编号。</param>
    /// <param name="kind">边类型。</param>
    public void RemoveEdge(long sourceId, long targetId, CpgEdgeKind kind)
    {
        edges.RemoveAll(edge => edge.SourceId == sourceId && edge.TargetId == targetId && edge.Kind == kind);
    }

    /// <summary>
    /// 按节点编号获取一个节点。
    /// </summary>
    /// <param name="nodeId">图内部节点编号。</param>
    /// <returns>对应节点。</returns>
    public CpgNode GetNode(long nodeId)
    {
        EnsureNodeExists(nodeId);
        return nodesById[nodeId];
    }

    /// <summary>
    /// 按节点类型筛选节点。
    /// </summary>
    /// <param name="kind">要筛选的节点类型。</param>
    /// <returns>所有匹配节点。</returns>
    public IEnumerable<CpgNode> GetNodes(CpgNodeKind kind)
    {
        return nodesById.Values.Where(node => node.Kind == kind);
    }

    /// <summary>
    /// 获取某个节点的出边。
    /// </summary>
    /// <param name="sourceId">起点节点编号。</param>
    /// <param name="kind">可选的边类型过滤条件；为空时返回全部出边。</param>
    /// <returns>匹配的出边集合。</returns>
    public IEnumerable<CpgEdge> GetOutgoingEdges(long sourceId, CpgEdgeKind? kind = null)
    {
        EnsureNodeExists(sourceId);

        return kind is null
            ? edges.Where(edge => edge.SourceId == sourceId)
            : edges.Where(edge => edge.SourceId == sourceId && edge.Kind == kind.Value);
    }

    /// <summary>
    /// 获取某个节点的入边。
    /// </summary>
    /// <param name="targetId">终点节点编号。</param>
    /// <param name="kind">可选的边类型过滤条件；为空时返回全部入边。</param>
    /// <returns>匹配的入边集合。</returns>
    public IEnumerable<CpgEdge> GetIncomingEdges(long targetId, CpgEdgeKind? kind = null)
    {
        EnsureNodeExists(targetId);

        return kind is null
            ? edges.Where(edge => edge.TargetId == targetId)
            : edges.Where(edge => edge.TargetId == targetId && edge.Kind == kind.Value);
    }

    private void EnsureNodeExists(long nodeId)
    {
        if (!nodesById.ContainsKey(nodeId))
        {
            throw new InvalidOperationException($"节点 '{nodeId}' 不存在。");
        }
    }
}
