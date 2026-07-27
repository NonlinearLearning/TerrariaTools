using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Analysis;

/// 定义反向 CPG 切片使用的有界边集合和遍历限制。
public sealed record NLCPGSliceQueryOptions(
    IReadOnlySet<NLCPGEdgeKind> AllowedEdgeKinds,
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxCallDepth = 0,
    int MaxVisitedNodes = int.MaxValue,
    int MaxVisitedEdges = int.MaxValue,
    int MaxCachedStates = 4096,
    int MaxCallerFanout = int.MaxValue);

/// 定义参与查询语义和缓存的所有遍历限制。
public sealed record NLCPGTraversalBudget(
    int MaxHops,
    int MaxPaths,
    int MaxDefinitions,
    int MaxVisitedNodes,
    int MaxVisitedEdges);

/// 表示 CPG 切片查询找到的一条稳定源到汇路径。
public sealed record NLCPGSlicePath(
    NodeId SourceNodeId,
    NodeId SinkNodeId,
    IReadOnlyList<NodeId> NodeIds);

public sealed record CpgShardUnavailableResult(NodeId NodeId, string Reason);

/// 包含 CPG 切片查询的有界且确定性结果。
public sealed record NLCPGSliceResult(
    IReadOnlyList<NLCPGSlicePath> Paths,
    bool WasTruncated,
    string? TruncationReason,
    long VisitedNodeCount,
    long VisitedEdgeCount,
    IReadOnlyList<CpgShardUnavailableResult>? UnavailableShards = null);
