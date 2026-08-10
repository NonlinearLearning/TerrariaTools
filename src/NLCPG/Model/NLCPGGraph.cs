using NLCPG.Contracts;

namespace NLCPG.Model;

/// 管理NLCPG 的内存节点集和边集。
public sealed class NLCPGGraph
{
    private readonly Dictionary<StableNodeAnchor, NLCPGNode> _mutableNodesByAnchor = new();
    private readonly Dictionary<NodeId, NLCPGNode> _nodesByNodeId = new();
    private readonly HashSet<NLCPGEdge> _edges = new();
    private readonly PendingEdgeBuffer _pendingEdges = new();
    private readonly Dictionary<string, string> _sourceByPath = new(StringComparer.Ordinal);
    private readonly StableNodeIdentityFactory _identityFactory;
    private readonly DeterministicNodeIdTable? _preallocatedNodeIds;
    private readonly Action<StableNodeAnchor>? _anchorDiscoveryObserver;
    private NLCPGGraphIndex? _queryIndex;

    // 初始化可变 CPG 图，并可选接入预分配 NodeId 与锚点发现回调。
    public NLCPGGraph(DeterministicNodeIdTable? preallocatedNodeIds = null, StableNodeIdentityFactory? identityFactory = null, Action<StableNodeAnchor>? anchorDiscoveryObserver = null)
    {
        _preallocatedNodeIds = preallocatedNodeIds;
        _identityFactory = identityFactory ?? new StableNodeIdentityFactory();
        _anchorDiscoveryObserver = anchorDiscoveryObserver;
    }

    internal static NLCPGGraph CreateAnchorDiscovery(StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
    {
        ArgumentNullException.ThrowIfNull(identityFactory);
        ArgumentNullException.ThrowIfNull(observeAnchor);
        return new NLCPGGraph(identityFactory: identityFactory, anchorDiscoveryObserver: observeAnchor);
    }

    public IReadOnlyCollection<NLCPGNode> Nodes => _queryIndex is null ? _mutableNodesByAnchor.Values : _nodesByNodeId.Values;

    public IReadOnlyCollection<NLCPGEdge> Edges => _edges;

    internal int CurrentEdgeCount => _queryIndex is null ? _pendingEdges.Count : _edges.Count;

    internal IReadOnlyCollection<PendingEdge> PendingEdges => _pendingEdges.Materialize(_mutableNodesByAnchor);

    internal DeterministicNodeIdTable RequirePreallocatedNodeIds()
    {
        return _preallocatedNodeIds ?? throw new InvalidOperationException(
          "Streaming shard publication requires preallocated NodeIds.");
    }

    internal MutableGraphFacts SnapshotMutableFacts()
    {
        EnsureMutable();
        return new MutableGraphFacts(
          _mutableNodesByAnchor.Values.ToArray(),
          _pendingEdges.Materialize(_mutableNodesByAnchor));
    }

    public bool HasQueryIndex => _queryIndex is not null;

    public string GraphSnapshotVersion => RequireQueryIndex().SnapshotVersion;

    // 由已冻结的节点和边重建只读查询图。
    public static NLCPGGraph CreateFrozen(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        var graph = new NLCPGGraph();
        foreach (var node in nodes)
        {
            if (!node.NodeId.HasValue)
            {
                throw new ArgumentException("Frozen CPG nodes require NodeId values.", nameof(nodes));
            }

            graph._nodesByNodeId.Add(node.NodeId.Value, node);
        }

        foreach (var edge in edges)
        {
            if (!graph._nodesByNodeId.ContainsKey(edge.SourceNodeId) ||
                !graph._nodesByNodeId.ContainsKey(edge.TargetNodeId))
            {
                throw new ArgumentException("Frozen CPG edges require known endpoints.", nameof(edges));
            }

            graph._edges.Add(edge);
        }

        graph._queryIndex = NLCPGGraphIndex.Create(graph._nodesByNodeId.Values, graph._edges);
        return graph;
    }

    // 向可变图中加入节点，并按稳定锚点做去重合并。
    public NLCPGNode AddNode(NLCPGNode node)
    {
        EnsureMutable();
        var materializedNode = MaterializeCompatibilityIdentity(node);
        var stableAnchor = materializedNode.StableAnchor!.Value;
        if (_anchorDiscoveryObserver is not null)
        {
            _anchorDiscoveryObserver(stableAnchor);
            return materializedNode;
        }

        if (!_mutableNodesByAnchor.TryGetValue(stableAnchor, out var existing))
        {
            _mutableNodesByAnchor[stableAnchor] = materializedNode;
            return materializedNode;
        }

        var merged = MergeNode(existing, materializedNode);
        _mutableNodesByAnchor[stableAnchor] = merged;
        return merged;
    }

    internal void ImportMutableFacts(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        EnsureMutable();
        var nodesById = new Dictionary<NodeId, NLCPGNode>();
        foreach (var node in nodes)
        {
            if (!node.NodeId.HasValue)
            {
                throw new InvalidDataException("Persisted CPG nodes require NodeIds.");
            }

            nodesById[node.NodeId.Value] = AddNode(node);
        }

        foreach (var edge in edges)
        {
            if (!nodesById.TryGetValue(edge.SourceNodeId, out var source) ||
                !nodesById.TryGetValue(edge.TargetNodeId, out var target))
            {
                throw new InvalidDataException("A persisted CPG edge references a node that was not restored.");
            }

            AddEdge(source, target, edge.Kind, edge.StructuredLabel, edge.ContextId, edge.CallSiteContext);
        }
    }

    // 向可变图加入一条边，并确保端点节点已经物化入图。
    public void AddEdge(NLCPGNode source, NLCPGNode target, NLCPGEdgeKind kind, NLCPGEdgeLabel? structuredLabel = null, NLCPGContextId? contextId = null, NLCPGCallSiteContext? callSiteContext = null)
    {
        EnsureMutable();
        var materializedSource = AddNode(source);
        var materializedTarget = AddNode(target);
        if (_anchorDiscoveryObserver is not null)
        {
            return;
        }

        _pendingEdges.Add(
          materializedSource,
          materializedTarget,
          kind,
          structuredLabel,
          contextId,
          callSiteContext);
    }

    // 批量提交已由图物化的节点对，避免 overlay 展开时重复执行节点身份合并。
    internal void AddKnownNodeCartesianEdges(
        IReadOnlyList<NLCPGNode> sourceNodes,
        IReadOnlyList<NLCPGNode> targetNodes,
        NLCPGEdgeKind kind)
    {
        EnsureMutable();
        if (_anchorDiscoveryObserver is not null)
        {
            return;
        }

        foreach (var sourceNode in sourceNodes)
        {
            foreach (var targetNode in targetNodes)
            {
                _pendingEdges.Add(
                  sourceNode,
                  targetNode,
                  kind,
                  null,
                  null,
                  null);
            }
        }
    }

    // 枚举指定节点种类的所有图节点。
    public IEnumerable<NLCPGNode> NodesByKind(NLCPGNodeKind kind)
    {
        return _queryIndex is null
            ? _mutableNodesByAnchor.Values.Where(node => node.Kind == kind)
            : GetNodes(kind);
    }

    // 按 NodeId 读取冻结图中的单个节点。
    public NLCPGNode? GetNode(NodeId nodeId)
    {
        return _nodesByNodeId.TryGetValue(nodeId, out var node) ? node : null;
    }

    // 记录源码文本，供节点展示时按跨度回切原文。
    public void RegisterSource(string filePath, string source)
    {
        EnsureMutable();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _sourceByPath[filePath] = source;

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(fullPath, filePath, StringComparison.Ordinal))
        {
            _sourceByPath[fullPath] = source;
        }
    }

    // 返回节点的展示文本，优先使用显式文本，再回退到源码切片或名称。
    public string GetDisplayText(NLCPGNode node)
    {
        if (node.Text is not null)
        {
            return node.Text;
        }

        if (TryResolveSourceSlice(node, out var sourceText))
        {
            return sourceText;
        }

        return node.FullName ?? node.Name ?? node.DisplayKind;
    }

    // 为当前图分配确定性 NodeId 并生成只读查询索引。
    public void FreezeQueryIndex()
    {
        if (_queryIndex is not null)
        {
            return;
        }

        AssignDeterministicNodeIds();
        _queryIndex = NLCPGGraphIndex.Create(_nodesByNodeId.Values, _edges);
    }

    // 返回指定节点发出的全部边。
    public IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: true);
    }

    // 返回流入指定节点的全部边。
    public IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId)
    {
        return GetAdjacency(nodeId, useOutgoingEdges: false);
    }

    // 返回流入指定节点且边种类匹配的边。
    public IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetIncomingEdges(nodeId, kind);
    }

    // 返回由指定节点发出且边种类匹配的边。
    public IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetOutgoingEdges(nodeId, kind);
    }

    // 返回指定边种类的全部边。
    public IReadOnlyList<NLCPGEdge> GetEdges(NLCPGEdgeKind kind)
    {
        var index = RequireQueryIndex();
        return index.GetEdges(kind);
    }

    // 返回指定节点种类的全部冻结节点。
    public IReadOnlyList<NLCPGNode> GetNodes(NLCPGNodeKind kind)
    {
        var index = RequireQueryIndex();
        return index.NodesByKind.TryGetValue(kind, out var nodes) ? nodes : Array.Empty<NLCPGNode>();
    }

    // 返回引用指定符号节点的全部图节点。
    public IReadOnlyList<NLCPGNode> GetSymbolReferences(NodeId symbolNodeId)
    {
        return GetIncomingEdges(symbolNodeId, NLCPGEdgeKind.Ref)
            .Select(edge => _nodesByNodeId[edge.SourceNodeId])
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 返回由指定方法节点拥有的调用点节点。
    public IReadOnlyList<NLCPGNode> GetMethodOwnedCallSites(NodeId methodNodeId)
    {
        return GetOutgoingEdges(methodNodeId, NLCPGEdgeKind.ContainsSymbol)
            .Select(edge => _nodesByNodeId[edge.TargetNodeId])
            .Where(node => node.Kind == NLCPGNodeKind.CallSite)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 返回完全落在指定文件跨度内的节点。
    public IReadOnlyList<NLCPGNode> GetNodesInFileSpan(string filePath, int start, int end)
    {
        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The span must be a valid half-open interval.");
        }

        var index = RequireQueryIndex();
        if (!index.TryGetNodesByFilePath(filePath, out var nodes))
        {
            return Array.Empty<NLCPGNode>();
        }

        return nodes.Where(node => node.SpanStart >= start && node.SpanEnd <= end)
            .OrderBy(node => node.NodeId)
            .ToArray();
    }

    // 为一组边种类计算稳定的掩码哈希。
    public int GetEdgeMaskId(IReadOnlySet<NLCPGEdgeKind> edgeKinds)
    {
        ArgumentNullException.ThrowIfNull(edgeKinds);
        var hash = new HashCode();
        foreach (var kind in edgeKinds.OrderBy(kind => kind))
        {
            hash.Add((int)kind);
        }

        return hash.ToHashCode();
    }

    // 返回当前节点发出的控制依赖边。
    public IReadOnlyList<NLCPGEdge> Controls(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.ControlDependence)
            .ToArray();
    }

    // 返回控制当前节点的控制依赖边。
    public IReadOnlyList<NLCPGEdge> ControlledBy(NodeId nodeId)
    {
        return GetIncomingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.ControlDependence)
            .ToArray();
    }

    // 返回当前节点支配到的节点边。
    public IReadOnlyList<NLCPGEdge> Dominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.Dominates)
            .ToArray();
    }

    // 返回当前节点后支配到的节点边。
    public IReadOnlyList<NLCPGEdge> PostDominates(NodeId nodeId)
    {
        return GetOutgoingEdges(nodeId)
            .Where(edge => edge.Kind == NLCPGEdgeKind.PostDominates)
            .ToArray();
    }

    // 从锚点出发按跳数与方向抽取局部子图视图。
    public NLCPGLocalView ExtractLocalView(NodeId anchorNodeId, int hops, NLCPGViewDirection direction = NLCPGViewDirection.Both, IReadOnlyCollection<NLCPGEdgeKind>? edgeKinds = null)
    {
        if (hops < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hops), "Hops must be non-negative.");
        }

        _ = RequireQueryIndex();
        if (!_nodesByNodeId.TryGetValue(anchorNodeId, out var anchor))
        {
            throw new ArgumentException($"Unknown anchor node id: {anchorNodeId}", nameof(anchorNodeId));
        }

        var allowedKinds = edgeKinds is null ? null : new HashSet<NLCPGEdgeKind>(edgeKinds);
        var index = RequireQueryIndex();
        var visitedNodeIds = new HashSet<NodeId> { anchorNodeId };
        var frontierNodeIds = new HashSet<NodeId> { anchorNodeId };

        for (var depth = 0; depth < hops; depth += 1)
        {
            var nextFrontierNodeIds = new HashSet<NodeId>();
            foreach (var nodeId in frontierNodeIds)
            {
                ExpandFrom(nodeId, direction, index, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
            }

            frontierNodeIds = nextFrontierNodeIds;
            if (frontierNodeIds.Count == 0)
            {
                break;
            }
        }

        var localNodes = visitedNodeIds
          .Select(nodeId => _nodesByNodeId[nodeId])
          .OrderBy(node => node.NodeId)
          .ToArray();
        var localEdges = index.OrderedEdges
          .Where(edge =>
            (allowedKinds is null || allowedKinds.Contains(edge.Kind)) &&
            visitedNodeIds.Contains(edge.SourceNodeId) &&
            visitedNodeIds.Contains(edge.TargetNodeId))
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind)
          .ThenBy(edge => edge.TargetNodeId)
          .ToArray();
        return new NLCPGLocalView(anchor, hops, localNodes, localEdges);
    }

    private static void ExpandFrom(NodeId nodeId, NLCPGViewDirection direction, NLCPGGraphIndex index, HashSet<NLCPGEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        if (direction is NLCPGViewDirection.Both or NLCPGViewDirection.Outgoing)
        {
            ExpandNeighbors(nodeId, index.GetOutgoingEdges(nodeId), useOutgoingTarget: true, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }

        if (direction is NLCPGViewDirection.Both or NLCPGViewDirection.Incoming)
        {
            ExpandNeighbors(nodeId, index.GetIncomingEdges(nodeId), useOutgoingTarget: false, allowedKinds, visitedNodeIds, nextFrontierNodeIds);
        }
    }

    private static void ExpandNeighbors(NodeId nodeId, IReadOnlyList<NLCPGEdge> edges, bool useOutgoingTarget, HashSet<NLCPGEdgeKind>? allowedKinds, ISet<NodeId> visitedNodeIds, ISet<NodeId> nextFrontierNodeIds)
    {
        foreach (var edge in edges)
        {
            if (allowedKinds is not null && !allowedKinds.Contains(edge.Kind))
            {
                continue;
            }

            var neighborId = useOutgoingTarget ? edge.TargetNodeId : edge.SourceNodeId;
            if (visitedNodeIds.Add(neighborId))
            {
                nextFrontierNodeIds.Add(neighborId);
            }
        }
    }

    private IReadOnlyList<NLCPGEdge> GetAdjacency(NodeId nodeId, bool useOutgoingEdges)
    {
        var index = RequireQueryIndex();
        return useOutgoingEdges ? index.GetOutgoingEdges(nodeId) : index.GetIncomingEdges(nodeId);
    }

    private NLCPGGraphIndex RequireQueryIndex()
    {
        return _queryIndex ?? throw new InvalidOperationException("The graph query index is unavailable until the graph has been frozen.");
    }

    private bool TryResolveSourceSlice(NLCPGNode node, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(node.FilePath) ||
            !node.SpanStart.HasValue ||
            !node.SpanEnd.HasValue)
        {
            return false;
        }

        if (!TryGetSource(node.FilePath, out var source))
        {
            return false;
        }

        var start = node.SpanStart.Value;
        var end = node.SpanEnd.Value;
        if (start < 0 || end < start || end > source.Length)
        {
            return false;
        }

        text = source[start..end];
        return true;
    }

    private bool TryGetSource(string filePath, out string source)
    {
        if (_sourceByPath.TryGetValue(filePath, out source!))
        {
            return true;
        }

        var fullPath = Path.GetFullPath(filePath);
        return _sourceByPath.TryGetValue(fullPath, out source!);
    }

    private NLCPGNode MaterializeCompatibilityIdentity(NLCPGNode node)
    {
        var stableAnchor = _identityFactory.GetStableAnchor(node);
        if (_preallocatedNodeIds is not null)
        {
            if (!_preallocatedNodeIds.Contains(stableAnchor))
            {
                throw new InvalidOperationException(
                    $"Stable anchor for '{DescribeNode(node)}' was not included in the supplied preallocated NodeId table.");
            }

            var nodeId = _preallocatedNodeIds.GetRequiredId(stableAnchor);
            if (node.NodeId.HasValue && node.NodeId.Value != nodeId)
            {
                throw new InvalidOperationException(
                    $"Preallocated NodeId for '{DescribeNode(node)}' does not match the supplied NodeId.");
            }

            return node with
            {
                NodeId = nodeId,
                StableAnchor = stableAnchor,
            };
        }

        if (node.NodeId.HasValue && node.StableAnchor.HasValue)
        {
            return node;
        }

        return node with
        {
            NodeId = node.NodeId,
            StableAnchor = stableAnchor,
        };
    }

    private static NLCPGNode MergeNode(NLCPGNode existing, NLCPGNode candidate)
    {
        return existing with
        {
            DisplayKind = string.IsNullOrEmpty(candidate.DisplayKind) ? existing.DisplayKind : candidate.DisplayKind,
            Name = candidate.Name ?? existing.Name,
            FullName = candidate.FullName ?? existing.FullName,
            Signature = candidate.Signature ?? existing.Signature,
            DispatchKind = candidate.DispatchKind ?? existing.DispatchKind,
            TypeFullName = candidate.TypeFullName ?? existing.TypeFullName,
            FilePath = candidate.FilePath ?? existing.FilePath,
            SpanStart = candidate.SpanStart ?? existing.SpanStart,
            SpanEnd = candidate.SpanEnd ?? existing.SpanEnd,
            Text = candidate.Text ?? existing.Text,
            IsImplicit = existing.IsImplicit || candidate.IsImplicit,
        };
    }

    private void AssignDeterministicNodeIds()
    {
        var anchoredNodes = _mutableNodesByAnchor.Values
          .Select(node =>
          {
              var anchor = _identityFactory.GetStableAnchor(node);
              return (Node: node, Anchor: anchor);
          })
          .ToArray();
        var nodeIdTable = _preallocatedNodeIds ?? DeterministicNodeIdTable.Create(anchoredNodes.Select(entry => entry.Anchor));
        if (_preallocatedNodeIds is not null &&
            (_preallocatedNodeIds.Count != anchoredNodes.Length ||
             anchoredNodes.Any(entry => !_preallocatedNodeIds.Contains(entry.Anchor))))
        {
            throw new InvalidOperationException("The supplied preallocated NodeId table must exactly match the graph's stable anchors.");
        }
        var remappedNodes = anchoredNodes
          .Select(entry =>
          {
              if (!nodeIdTable.TryGetNodeId(entry.Anchor, out var nodeId))
              {
                  throw new InvalidOperationException($"Failed to resolve deterministic NodeId for '{DescribeNode(entry.Node)}'.");
              }

              return entry.Node with
              {
                  NodeId = nodeId,
                  StableAnchor = entry.Anchor,
              };
          })
          .ToDictionary(node => node.StableAnchor!.Value);
        var remappedEdges = _pendingEdges.Materialize(_mutableNodesByAnchor)
          .Select(edge =>
          {
              var sourceNode = remappedNodes[edge.SourceNode.StableAnchor!.Value];
              var targetNode = remappedNodes[edge.TargetNode.StableAnchor!.Value];
              return new NLCPGEdge(
                sourceNode.NodeId!.Value,
                targetNode.NodeId!.Value,
                edge.Kind,
                edge.StructuredLabel,
                edge.ContextId,
                edge.CallSiteContext);
          })
          .ToArray();

        _mutableNodesByAnchor.Clear();
        _nodesByNodeId.Clear();
        foreach (var node in remappedNodes.Values)
        {
            _nodesByNodeId[node.NodeId!.Value] = node;
        }

        _edges.Clear();
        foreach (var edge in remappedEdges)
        {
            _edges.Add(edge);
        }
    }

    private static string DescribeNode(NLCPGNode node)
    {
        return node.FullName ??
          node.Name ??
          $"{node.Kind}:{node.FilePath}:{node.SpanStart}:{node.SpanEnd}";
    }

    private static StableNodeRole MapStableNodeRole(NLCPGNodeKind kind)
    {
        return kind switch
        {
            NLCPGNodeKind.SyntaxNode => StableNodeRole.SyntaxNode,
            NLCPGNodeKind.SyntaxToken => StableNodeRole.SyntaxToken,
            NLCPGNodeKind.Operation => StableNodeRole.Operation,
            NLCPGNodeKind.Reference => StableNodeRole.Reference,
            NLCPGNodeKind.TypeRef => StableNodeRole.TypeReference,
            NLCPGNodeKind.TypeDecl => StableNodeRole.TypeDeclaration,
            NLCPGNodeKind.Method => StableNodeRole.Method,
            NLCPGNodeKind.MethodParameter => StableNodeRole.MethodParameter,
            NLCPGNodeKind.MethodReturn => StableNodeRole.MethodReturn,
            NLCPGNodeKind.MethodEntry => StableNodeRole.MethodEntry,
            NLCPGNodeKind.MethodExit => StableNodeRole.MethodExit,
            NLCPGNodeKind.CallSite => StableNodeRole.CallSite,
            NLCPGNodeKind.MemberAccess => StableNodeRole.MemberAccess,
            NLCPGNodeKind.SymbolMethod or
            NLCPGNodeKind.SymbolParameter or
            NLCPGNodeKind.SymbolLocal or
            NLCPGNodeKind.SymbolField or
            NLCPGNodeKind.SymbolProperty or
            NLCPGNodeKind.SymbolType or
            NLCPGNodeKind.SymbolUnknown => StableNodeRole.Symbol,
            _ => StableNodeRole.None,
        };
    }

    private void EnsureMutable()
    {
        if (_queryIndex is not null)
        {
            throw new InvalidOperationException("The graph is frozen and cannot be mutated.");
        }
    }

    // 以稳定锚点和边元数据去重，延迟创建 PendingEdge 对象到需要读取或冻结时。
    private sealed class PendingEdgeBuffer
    {
        private readonly Dictionary<PendingEdgeKey, int> _ordinals = new();
        private readonly List<BufferedPendingEdge> _items = new();

        internal int Count => _items.Count;

        internal void Add(
          NLCPGNode sourceNode,
          NLCPGNode targetNode,
          NLCPGEdgeKind kind,
          NLCPGEdgeLabel? structuredLabel,
          NLCPGContextId? contextId,
          NLCPGCallSiteContext? callSiteContext)
        {
            var sourceAnchor = sourceNode.StableAnchor!.Value;
            var targetAnchor = targetNode.StableAnchor!.Value;
            var key = new PendingEdgeKey(
              sourceAnchor,
              targetAnchor,
              kind,
              structuredLabel,
              contextId,
              callSiteContext);
            if (_ordinals.ContainsKey(key))
            {
                return;
            }

            _ordinals.Add(key, _items.Count);
            _items.Add(new BufferedPendingEdge(
              sourceAnchor,
              targetAnchor,
              kind,
              structuredLabel,
              contextId,
              callSiteContext));
        }

        internal IReadOnlyList<PendingEdge> Materialize(
          IReadOnlyDictionary<StableNodeAnchor, NLCPGNode> nodesByAnchor)
        {
            var pendingEdges = new PendingEdge[_items.Count];
            for (var index = 0; index < _items.Count; index += 1)
            {
                var item = _items[index];
                pendingEdges[index] = new PendingEdge(
                  nodesByAnchor[item.SourceAnchor],
                  nodesByAnchor[item.TargetAnchor],
                  item.Kind,
                  item.StructuredLabel,
                  item.ContextId,
                  item.CallSiteContext);
            }

            return pendingEdges;
        }

        private readonly record struct PendingEdgeKey(
          StableNodeAnchor SourceAnchor,
          StableNodeAnchor TargetAnchor,
          NLCPGEdgeKind Kind,
          NLCPGEdgeLabel? StructuredLabel,
          NLCPGContextId? ContextId,
          NLCPGCallSiteContext? CallSiteContext);

        private readonly record struct BufferedPendingEdge(
          StableNodeAnchor SourceAnchor,
          StableNodeAnchor TargetAnchor,
          NLCPGEdgeKind Kind,
          NLCPGEdgeLabel? StructuredLabel,
          NLCPGContextId? ContextId,
          NLCPGCallSiteContext? CallSiteContext);
    }

    internal sealed record PendingEdge(
      NLCPGNode SourceNode,
      NLCPGNode TargetNode,
      NLCPGEdgeKind Kind,
      NLCPGEdgeLabel? StructuredLabel,
      NLCPGContextId? ContextId,
      NLCPGCallSiteContext? CallSiteContext);

    internal sealed record MutableGraphFacts(
      IReadOnlyList<NLCPGNode> Nodes,
      IReadOnlyList<PendingEdge> PendingEdges);
}
