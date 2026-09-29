using NLCPG.Contracts;
using NLCPG.Model;
using System.Runtime.CompilerServices;

namespace NLCPG.Persistence;

public static class CpgFrozenShardGraphReader
{
    private static readonly ConditionalWeakTable<CpgFrozenShard, NodeIndex> NodeIndexes = new();

    /// 仅还原遍历到 <paramref name="targetNodeId"/> 所需的记录。
    /// 分片负载仍由存储完整反序列化，但这避免了在有界切片查询期间为无关分片记录构建
    /// 索引图。
    public static CpgFrozenShardIncomingProjection ReadIncomingProjection(CpgFrozenShard shard, NodeId targetNodeId, IReadOnlySet<NLCPGEdgeKind> allowedEdgeKinds, int maxEdges)
    {
        return ReadIncomingProjection(shard, targetNodeId, allowedEdgeKinds, maxEdges, new StringInterner());
    }

    // 由调用方提供 interner，使多个局部投影可以安全合并到同一张图。
    internal static CpgFrozenShardIncomingProjection ReadIncomingProjection(
        CpgFrozenShard shard,
        NodeId targetNodeId,
        IReadOnlySet<NLCPGEdgeKind> allowedEdgeKinds,
        int maxEdges,
        StringInterner stringInterner)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentNullException.ThrowIfNull(allowedEdgeKinds);
        ArgumentNullException.ThrowIfNull(stringInterner);

        var nodeIndex = NodeIndexes.GetValue(shard, static source => new NodeIndex(source));
        var selectedEdges = new List<NLCPGEdge>();
        var requiredNodeIds = new HashSet<NodeId>();
        if (nodeIndex.TryGetByNodeId(targetNodeId, out var targetNode))
        {
            requiredNodeIds.Add(targetNodeId);
        }
        if (targetNode is not null &&
            CpgFrozenShardIncomingEdgeIndex.TryGet(shard, out var incomingEdgeOffsets, out var incomingEdgeIndexes))
        {
            for (var position = incomingEdgeOffsets[targetNode.LocalIndex];
                 position < incomingEdgeOffsets[targetNode.LocalIndex + 1];
                 position += 1)
            {
                var edge = shard.Edges[incomingEdgeIndexes[position]];
                var sourceNodeId = new NodeId(nodeIndex.GetByLocalIndex(edge.SourceLocalIndex).NodeId);
                if (!Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                    !allowedEdgeKinds.Contains(kind))
                {
                    continue;
                }

                selectedEdges.Add(new NLCPGEdge(
                  sourceNodeId,
                  targetNodeId,
                  kind,
                  ParseLabel(edge.Label, edge.FlowSummaryLabel),
                  edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
                  CreateCallSiteContext(edge)));
                requiredNodeIds.Add(sourceNodeId);
                requiredNodeIds.Add(targetNodeId);
                if (selectedEdges.Count >= maxEdges)
                {
                    break;
                }
            }
        }
        else
        {
            foreach (var edge in shard.Edges)
            {
                var sourceNodeId = new NodeId(nodeIndex.GetByLocalIndex(edge.SourceLocalIndex).NodeId);
                var targetId = new NodeId(nodeIndex.GetByLocalIndex(edge.TargetLocalIndex).NodeId);
                if (targetId != targetNodeId || !Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                    !allowedEdgeKinds.Contains(kind))
                {
                    continue;
                }

                selectedEdges.Add(new NLCPGEdge(
                  sourceNodeId,
                  targetId,
                  kind,
                  ParseLabel(edge.Label, edge.FlowSummaryLabel),
                  edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
                  CreateCallSiteContext(edge)));
                requiredNodeIds.Add(sourceNodeId);
                requiredNodeIds.Add(targetId);
                if (selectedEdges.Count >= maxEdges)
                {
                    break;
                }
            }
        }

        foreach (var edge in shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
        {
            var targetId = new NodeId(edge.TargetNodeId);
            if (targetId != targetNodeId || !Enum.TryParse<NLCPGEdgeKind>(edge.Kind, out var kind) ||
                !allowedEdgeKinds.Contains(kind))
            {
                continue;
            }

            selectedEdges.Add(CreateBoundaryEdge(edge));
            requiredNodeIds.Add(new NodeId(edge.SourceNodeId));
            requiredNodeIds.Add(targetId);
            if (selectedEdges.Count >= maxEdges)
            {
                break;
            }
        }

        var nodes = requiredNodeIds
          .Select(nodeId => nodeIndex.TryGetByNodeId(nodeId, out var node) ? node : null)
          .Where(node => node is not null)
          .Select(node => node!)
          .OrderBy(node => node.LocalIndex)
          .Select(node => CreateNode(node, stringInterner))
          .ToDictionary(node => node.NodeId!.Value);
        return new CpgFrozenShardIncomingProjection(nodes, selectedEdges);
    }

    // 合并多个冻结分片及其边界边，重建完整冻结图。
    public static NLCPGGraph ReadGraph(IEnumerable<CpgFrozenShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        var nodes = new Dictionary<NodeId, NLCPGNode>();
        var stringInterner = new StringInterner();
        var edges = new HashSet<NLCPGEdge>();
        var orderedShards = shards
          .OrderBy(shard => shard.Lookup.Fragment.Kind, StringComparer.Ordinal)
          .ThenBy(shard => shard.Lookup.Fragment.SpanStart)
          .ThenBy(shard => shard.Lookup.Fragment.SpanLength)
          .ToArray();
        AccumulateShardGraphs(orderedShards, nodes, edges, stringInterner);

        foreach (var boundaryEdge in orderedShards
          .SelectMany(shard => shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId))
        {
            var sourceNodeId = new NodeId(boundaryEdge.SourceNodeId);
            var targetNodeId = new NodeId(boundaryEdge.TargetNodeId);
            if (!nodes.ContainsKey(sourceNodeId) || !nodes.ContainsKey(targetNodeId))
            {
                throw new InvalidDataException("A CPG boundary edge references a node that was not restored.");
            }

            edges.Add(CreateBoundaryEdge(boundaryEdge));
        }

        return NLCPGGraph.CreateFrozen(nodes.Values, edges, stringInterner);
    }

    // (i)/(ii) 共用的归并核心：按调用方给定的分片序，把每个分片的节点与边并入调用方提供的容器。
    // 容器由调用方持有，故它可自行决定何时物化数组、何时解除引用——这正是本项要收敛的重复段。
    //
    // 语义与原先两处各自展开的循环逐条一致：
    //   · 节点用 TryAdd：先到者胜，重复 NodeId 不视为冲突；
    //   · 边用 UnionWith：HashSet 值相等去重，同一条边跨分片重复只算一次；
    //   · 跨分片共用一个 stringInterner，不得每分片新建。
    // 节点插入序不影响最终 OrderedNodes（CreateFrozen 内会按 NodeId 重排，
    // NLCPGGraphIndex.cs:436），但会影响 ImportMutableFacts 的 AddNode 顺序
    // （NLCPGGraph.cs:429-477），故这里必须原样保持调用方传入的分片序，不得另行排序。
    //
    // 边界边不在此处：三处调用方的"边界边缺失端点"策略不同（(i) 抛 InvalidDataException、
    // (ii) 静默跳过），故边界边仍由各自处理。
    internal static void AccumulateShardGraphs(
      IEnumerable<CpgFrozenShard> orderedShards,
      Dictionary<NodeId, NLCPGNode> nodes,
      HashSet<NLCPGEdge> edges,
      StringInterner stringInterner)
    {
        ArgumentNullException.ThrowIfNull(orderedShards);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(stringInterner);
        foreach (var shard in orderedShards)
        {
            var graph = ReadGraph(shard, stringInterner);
            MergeInto(nodes, edges, graph.Nodes, graph.Edges);
        }
    }

    // 归并语义的最小核：(i)/(ii)/(iii) 三处都以"节点 TryAdd、边 Add"的同一规则并入同一对容器。
    // 单独抽出是因为 (iii)（NLCPGSliceQuery.LoadFrontierGraphAsync）的归并**交错在逐跳遍历与
    // 访问预算之中**，且并入的是逐锚点的 incoming 投影而非整分片图，无法整体套用上面的按分片循环；
    // 但它每次并入的那两行规则与本核逐字相同，故直接复用本核，避免第三份同义实现。
    internal static void MergeInto(
      Dictionary<NodeId, NLCPGNode> nodes,
      HashSet<NLCPGEdge> edges,
      IEnumerable<NLCPGNode> shardNodes,
      IEnumerable<NLCPGEdge> shardEdges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(shardNodes);
        ArgumentNullException.ThrowIfNull(shardEdges);
        foreach (var node in shardNodes)
        {
            nodes.TryAdd(node.NodeId!.Value, node);
        }

        edges.UnionWith(shardEdges);
    }

    internal static CpgFrozenShardGraphFacts ReadMutableFacts(IEnumerable<CpgFrozenShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        var accumulator = CreateMutableFactsAccumulator();
        var orderedShards = shards
          .OrderBy(shard => shard.Lookup.Fragment.Kind, StringComparer.Ordinal)
          .ThenBy(shard => shard.Lookup.Fragment.SpanStart)
          .ThenBy(shard => shard.Lookup.Fragment.SpanLength)
          .ToArray();
        foreach (var shard in orderedShards)
        {
            accumulator.Add(shard);
        }

        return accumulator.Complete();
    }

    internal static MutableFactsAccumulator CreateMutableFactsAccumulator()
    {
        return new MutableFactsAccumulator();
    }

    // 从单个冻结分片恢复一张只读图。
    public static NLCPGGraph ReadGraph(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return ReadGraph(shard, new StringInterner());
    }

    internal static NLCPGGraph ReadGraph(CpgFrozenShard shard, StringInterner stringInterner)
    {
        var nodes = shard.Nodes
          .OrderBy(node => node.LocalIndex)
          .Select(node => CreateNode(node, stringInterner))
          .ToArray();
        var nodeIdsByLocalIndex = shard.Nodes.ToDictionary(node => node.LocalIndex, node => new NodeId(node.NodeId));
        var edges = shard.Edges.Select(edge => new NLCPGEdge(
          nodeIdsByLocalIndex[edge.SourceLocalIndex],
          nodeIdsByLocalIndex[edge.TargetLocalIndex],
          Enum.Parse<NLCPGEdgeKind>(edge.Kind),
          ParseLabel(edge.Label, edge.FlowSummaryLabel),
          edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
          CreateCallSiteContext(edge))).ToArray();
        return NLCPGGraph.CreateFrozen(nodes, edges, stringInterner);
    }

    // 读取单个分片记录的全部边界边。
    public static IReadOnlyList<NLCPGEdge> ReadBoundaryEdges(CpgFrozenShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return (shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId)
          .Select(CreateBoundaryEdge)
          .ToArray();
    }

    private static NLCPGEdgeLabel? ParseLabel(string? label, CpgFrozenFlowSummaryLabel? flowSummaryLabel)
    {
        if (flowSummaryLabel is not null)
        {
            return flowSummaryLabel.ToEdgeLabel();
        }

        if (label is null)
        {
            return null;
        }

        const string bridgePrefix = "interprocedural-bridge:";
        const string relationPrefix = "decision-relation:";
        if (label.StartsWith(bridgePrefix, StringComparison.Ordinal))
        {
            return NLCPGEdgeLabel.ForInterproceduralBridge(
              Enum.Parse<NLCPGInterproceduralBridgeKind>(label[bridgePrefix.Length..]));
        }

        if (label.StartsWith(relationPrefix, StringComparison.Ordinal))
        {
            return NLCPGEdgeLabel.ForDecisionRelation(
              Enum.Parse<NLCPGDecisionRelationKind>(label[relationPrefix.Length..]));
        }

        throw new InvalidDataException("The CPG shard contains an unknown structured edge label.");
    }

    private sealed class NodeIndex
    {
        private readonly Dictionary<uint, CpgFrozenNode> _nodesByNodeId = new();
        private readonly Dictionary<int, CpgFrozenNode> _nodesByLocalIndex = new();

        internal NodeIndex(CpgFrozenShard shard)
        {
            foreach (var node in shard.Nodes)
            {
                if (!_nodesByNodeId.TryAdd(node.NodeId, node) ||
                    !_nodesByLocalIndex.TryAdd(node.LocalIndex, node))
                {
                    throw new InvalidDataException("The CPG shard contains duplicate local nodes.");
                }
            }
        }

        internal bool TryGetByNodeId(NodeId nodeId, out CpgFrozenNode? node)
        {
            return _nodesByNodeId.TryGetValue(nodeId.Value, out node);
        }

        internal CpgFrozenNode GetByLocalIndex(int localIndex)
        {
            if (_nodesByLocalIndex.TryGetValue(localIndex, out var node))
            {
                return node;
            }

            throw new InvalidDataException("The CPG shard contains an orphan local edge endpoint.");
        }
    }

    private static NLCPGNode CreateNode(CpgFrozenNode node, StringInterner stringInterner)
    {
        var kind = Enum.Parse<NLCPGNodeKind>(node.Kind);
        return new NLCPGNode(
          kind,
          NameId: stringInterner.Intern(node.Name),
          FullNameId: stringInterner.Intern(node.FullName),
          SignatureId: stringInterner.Intern(node.Signature),
          FilePathId: stringInterner.Intern(node.FilePath),
          SpanStart: node.SpanStart,
          SpanEnd: node.SpanEnd,
          IsImplicit: node.IsImplicit,
          NodeId: new NodeId(node.NodeId),
          StableAnchor: new StableNodeAnchor(
            kind, node.StableFilePathId, node.StableSpanStart,
            node.StableSpanEnd, (StableNodeRole)node.StableRole, node.StableOrdinal, node.StableExtraKeyId));
    }

    private static NLCPGCallSiteContext? CreateCallSiteContext(CpgFrozenEdge edge)
    {
        if (edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
            !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null)
        {
            return null;
        }

        return new NLCPGCallSiteContext(
          edge.CallSiteFilePath,
          edge.CallSiteSpanStart.Value,
          edge.CallSiteSpanEnd.Value,
          edge.CallSiteDisplayName);
    }

    private static NLCPGEdge CreateBoundaryEdge(CpgFrozenBoundaryEdge edge)
    {
        NLCPGCallSiteContext? callSiteContext = edge.CallSiteFilePath is null || !edge.CallSiteSpanStart.HasValue ||
          !edge.CallSiteSpanEnd.HasValue || edge.CallSiteDisplayName is null
          ? null
          : new NLCPGCallSiteContext(
            edge.CallSiteFilePath,
            edge.CallSiteSpanStart.Value,
            edge.CallSiteSpanEnd.Value,
            edge.CallSiteDisplayName);
        return new NLCPGEdge(
          new NodeId(edge.SourceNodeId),
          new NodeId(edge.TargetNodeId),
          Enum.Parse<NLCPGEdgeKind>(edge.Kind),
          ParseLabel(edge.Label, edge.FlowSummaryLabel),
          edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
          callSiteContext);
    }

    internal sealed class MutableFactsAccumulator
    {
        private readonly StringInterner _stringInterner = new();
        private readonly Dictionary<NodeId, NLCPGNode> _nodes = new();
        private readonly HashSet<NLCPGEdge> _edges = new();

        internal void Add(CpgFrozenShard shard)
        {
            ArgumentNullException.ThrowIfNull(shard);
            var nodeIdsByLocalIndex = new Dictionary<int, NodeId>(shard.Nodes.Count);
            foreach (var frozenNode in shard.Nodes.OrderBy(node => node.LocalIndex))
            {
                if (!nodeIdsByLocalIndex.TryAdd(frozenNode.LocalIndex, new NodeId(frozenNode.NodeId)))
                {
                    throw new InvalidDataException("The CPG shard contains duplicate local nodes.");
                }

                var node = CreateNode(frozenNode, _stringInterner);
                if (!node.NodeId.HasValue)
                {
                    throw new InvalidDataException("Persisted CPG nodes require NodeIds.");
                }

                if (!_nodes.TryAdd(node.NodeId.Value, node) && _nodes[node.NodeId.Value] != node)
                {
                    throw new InvalidDataException("The CPG shards contain conflicting nodes with the same NodeId.");
                }
            }

            foreach (var frozenEdge in shard.Edges)
            {
                _edges.Add(CreateEdge(frozenEdge, nodeIdsByLocalIndex));
            }

            foreach (var boundaryEdge in shard.BoundaryEdges ?? Array.Empty<CpgFrozenBoundaryEdge>())
            {
                _edges.Add(CreateBoundaryEdge(boundaryEdge));
            }
        }

        internal CpgFrozenShardGraphFacts Complete()
        {
            foreach (var edge in _edges)
            {
                if (!_nodes.ContainsKey(edge.SourceNodeId) || !_nodes.ContainsKey(edge.TargetNodeId))
                {
                    throw new InvalidDataException("A CPG edge references a node that was not restored.");
                }
            }

            return new CpgFrozenShardGraphFacts(_nodes.Values.ToArray(), _edges.ToArray(), _stringInterner);
        }
    }

    private static NLCPGEdge CreateEdge(
      CpgFrozenEdge edge,
      IReadOnlyDictionary<int, NodeId> nodeIdsByLocalIndex)
    {
        if (!nodeIdsByLocalIndex.TryGetValue(edge.SourceLocalIndex, out var sourceNodeId) ||
            !nodeIdsByLocalIndex.TryGetValue(edge.TargetLocalIndex, out var targetNodeId))
        {
            throw new InvalidDataException("The CPG shard contains an orphan local edge endpoint.");
        }

        return new NLCPGEdge(
          sourceNodeId,
          targetNodeId,
          Enum.Parse<NLCPGEdgeKind>(edge.Kind),
          ParseLabel(edge.Label, edge.FlowSummaryLabel),
          edge.ContextId is null ? null : new NLCPGContextId(edge.ContextId),
          CreateCallSiteContext(edge));
    }
}

public sealed record CpgFrozenShardIncomingProjection(
  IReadOnlyDictionary<NodeId, NLCPGNode> Nodes,
  IReadOnlyList<NLCPGEdge> IncomingEdges);

internal readonly record struct CpgFrozenShardGraphFacts(
  IReadOnlyList<NLCPGNode> Nodes,
  IReadOnlyList<NLCPGEdge> Edges,
  StringInterner StringInterner);
