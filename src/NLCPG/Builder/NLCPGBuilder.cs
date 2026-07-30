using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NL.Concurrency;
using NLCPG.Builder.Preallocation;
using NLCPG.Builder.Passes;
using NLCPG.Builder.Streaming;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;
using System.Diagnostics;

namespace NLCPG.Builder;

/// 从单个源码文件构建最小 Roslyn 风格代码属性图。
public sealed partial class NLCPGBuilder
{
    private static readonly IReadOnlyList<INLCPGPass> LegacyPipeline = new INLCPGPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
        OperationPass.Instance,
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };
    private static readonly IReadOnlyList<INLCPGPass> PartitionedPreOperationPipeline = new INLCPGPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
    };
    private static readonly IReadOnlyList<INLCPGPass> PartitionedPostOperationPipeline = new INLCPGPass[]
    {
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };

    private readonly Dictionary<SyntaxNode, NLCPGNode> _syntaxNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, NLCPGNode> _symbolNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _typeDeclNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _methodNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _methodParameterNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _methodReturnNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _methodEntryNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _methodExitNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<NLCPGNode, string> _symbolKeysByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NLCPGNode, string> _methodOwnerSymbolKeysByBoundaryNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NLCPGNode, int> _methodParameterOrdinalsByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByNameAndSignature = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<INamedTypeSymbol>> _baseTypeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<NLCPGNode, HashSet<NLCPGNode>> _cfgPredecessorsByNode = new();
    private readonly Dictionary<NLCPGNode, HashSet<NLCPGNode>> _cfgSuccessorsByNode = new();
    private readonly Dictionary<IInvocationOperation, NLCPGNode> _callSiteNodesByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IInvocationOperation, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByDispatchShape =
      new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _propertyAccessorCallSiteNodesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _pendingOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, SyntaxSemanticFacts> _partitionedSyntaxFacts = new(ReferenceEqualityComparer.Instance);
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private readonly NLCPGBuilderOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;

    public NLCPGFlowSummaryMetrics LastFlowSummaryMetrics { get; private set; } = NLCPGFlowSummaryMetrics.Empty;

    private sealed record CapabilityBuildPlan(
        NLCPGCapability ResolvedCapabilities,
        bool RequiresMethodModel,
        bool RequiresCallTargets,
        bool RequiresCfg,
        bool RequiresDataFlow,
        bool RequiresInterproceduralDataFlow,
        bool RequiresDominance,
        bool RequiresControlDependence);

    private sealed record LoopControlTargets(IOperation? ContinueTarget, IOperation? BreakTarget);
    private sealed record DefinitionFact(string LocationKey, string? BaseKey, string Category, string? PathKey = null);
    private sealed record DataFlowOperationIndex(
        IReadOnlyDictionary<IOperation, NLCPGNode> NodesByOperation);

    // 以给定选项初始化构图器；未提供时使用默认配置。
    public NLCPGBuilder(NLCPGBuilderOptions? options = null, IConcurrencyPool? concurrencyPool = null)
    {
        _options = options ?? NLCPGBuilderOptions.CreateDefault();
        _concurrencyPool = concurrencyPool ?? new BoundedConcurrencyPool();
    }

    // 从源码文本直接创建语义模型并构建冻结后的 CPG。
    public NLCPGGraph BuildFromSource(string source, string filePath = "input.cs")
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(NLCPGBuildContext.CreateFromSource(source, filePath));
        }

        var identityFactory = new StableNodeIdentityFactory();
        var preflightBuilder = new NLCPGBuilder(_options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
        });
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateFromSourceAnchorDiscovery(
            source,
            filePath,
            identityFactory,
            collector.Add));

        var allocation = collector.CreateAllocation();
        return Build(NLCPGBuildContext.CreateFromSource(source, filePath, allocation, identityFactory));
    }

    // 复用外部提供的语义模型与语法根来构建 CPG。
    public NLCPGGraph BuildFromSemanticModel(SemanticModel semanticModel, SyntaxNode root, string source, string filePath)
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath));
        }

        var identityFactory = new StableNodeIdentityFactory();
        var preflightBuilder = new NLCPGBuilder(_options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
        });
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateAnchorDiscovery(
            semanticModel,
            root,
            source,
            filePath,
            identityFactory,
            collector.Add));

        var allocation = collector.CreateAllocation();
        return Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath, allocation, identityFactory));
    }

    private NLCPGGraph Build(NLCPGBuildContext context)
    {
        _syntaxNodes.Clear();
        _symbolNodes.Clear();
        _typeDeclNodes.Clear();
        _methodNodes.Clear();
        _methodParameterNodes.Clear();
        _methodReturnNodes.Clear();
        _methodEntryNodes.Clear();
        _methodExitNodes.Clear();
        _symbolKeysByNode.Clear();
        _methodOwnerSymbolKeysByBoundaryNode.Clear();
        _methodParameterOrdinalsByNode.Clear();
        _methodSymbolsByFullName.Clear();
        _methodSymbolsByNameAndSignature.Clear();
        _baseTypeCache.Clear();
        _cfgPredecessorsByNode.Clear();
        _cfgSuccessorsByNode.Clear();
        _callSiteNodesByInvocation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
        var persistenceHit = false;
        if (_options.Persistence is not null)
        {
            var restoredBase = new CpgShardBuildCoordinator(_options.Persistence, _concurrencyPool)
                .TryRestoreBaseAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (restoredBase is not null)
            {
                context.Graph.ImportMutableFacts(restoredBase.Facts.Nodes, restoredBase.Facts.Edges);
                persistenceHit = true;
            }
        }
        var buildPlan = ResolveCapabilityBuildPlan();
        var operationBuildStrategy = buildPlan.RequiresMethodModel
            ? CreateOperationBuildStrategy(context)
            : new OperationBuildStrategy(
                NLCPGBuilderMode.Partitioned,
                UsePartitionedOperationBuild: false,
                SourceLineCount: context.Source.Count(character => character == '\n') + 1,
                OperationRoots: Array.Empty<OperationRootPlan>());
        var usePartitionedSyntaxPass = ShouldUsePartitionedSyntaxPass(context, operationBuildStrategy.OperationRoots);
        SkeletonShardPublisher? streamingPublisher = null;
        var streamingPersistenceCompleted = false;

        try
        {
            RunSyntaxPass(context, usePartitionedSyntaxPass, operationBuildStrategy.OperationRoots);

            if (buildPlan.RequiresMethodModel)
            {
                MethodDecorationPass.Instance.Run(this, context);

                if (_options.Persistence?.StreamingMode == true && !persistenceHit)
                {
                    // 先写入不可见的基础分片；会话完成前，查询端看不到这个构建。
                    streamingPublisher = SkeletonShardPublisher.BeginAsync(
                        _options.Persistence,
                        context,
                        CancellationToken.None)
                      .GetAwaiter()
                      .GetResult();
                }

                RunPartitionedOperationPass(context, operationBuildStrategy.OperationRoots, streamingPublisher);
                CompleteOperationBackedSyntaxTypes(context);

                if (streamingPublisher is not null)
                {
                    // 操作分片均已按源顺序写入后，补齐基础节点和跨分片邻接表，再一次性发布会话。
                    streamingPublisher
                      .CompleteBaseAsync(context, CancellationToken.None)
                      .GetAwaiter()
                      .GetResult();
                    streamingPublisher = null;
                    streamingPersistenceCompleted = true;
                }
            }

            RunOptionalPass(buildPlan.RequiresCallTargets, CallGraphPass.Instance, context);
            RunOptionalPass(buildPlan.RequiresMethodModel, MemberAccessPass.Instance, context);
            RunOptionalPass(buildPlan.RequiresCfg, ControlFlowPass.Instance, context);
            RunOptionalPass(buildPlan.RequiresDataFlow, DataFlowPass.Instance, context);
            RunOptionalPass(buildPlan.RequiresInterproceduralDataFlow, InterproceduralDataFlowPass.Instance, context);
            RunOptionalPass(buildPlan.RequiresDominance, DominancePass.Instance, context);
            RunOptionalPass(buildPlan.RequiresControlDependence, ControlDependencePass.Instance, context);

            // Freeze 后图进入查询态，释放仅服务于构建过程的 Roslyn 映射和临时缓存。
            context.Graph.FreezeQueryIndex();
            ReleaseTransientBuilderState();

            if (_options.Persistence is not null && !streamingPersistenceCompleted && !persistenceHit)
            {
                new CpgShardBuildCoordinator(_options.Persistence, _concurrencyPool)
                  .PersistAsync(context, CancellationToken.None)
                  .GetAwaiter()
                  .GetResult();
                streamingPublisher = null;
            }

            return context.Graph;
        }
        finally
        {
            if (streamingPublisher is not null)
            {
                streamingPublisher.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private bool RequiresPreallocatedNodeIds()
    {
        return _options.UsePreallocatedNodeIds || _options.Persistence?.StreamingMode == true;
    }

    private void ReleaseTransientBuilderState()
    {
        _syntaxNodes.Clear();
        _symbolNodes.Clear();
        _typeDeclNodes.Clear();
        _methodNodes.Clear();
        _methodParameterNodes.Clear();
        _methodReturnNodes.Clear();
        _methodEntryNodes.Clear();
        _methodExitNodes.Clear();
        _symbolKeysByNode.Clear();
        _methodOwnerSymbolKeysByBoundaryNode.Clear();
        _methodParameterOrdinalsByNode.Clear();
        _methodSymbolsByFullName.Clear();
        _methodSymbolsByNameAndSignature.Clear();
        _baseTypeCache.Clear();
        _cfgPredecessorsByNode.Clear();
        _cfgSuccessorsByNode.Clear();
        _callSiteNodesByInvocation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
    }

    private CapabilityBuildPlan ResolveCapabilityBuildPlan()
    {
        var requestedCapabilities = _options.RequestedCapabilities;
        var resolved = requestedCapabilities is null
            ? NLCPGCapability.Default
            : requestedCapabilities.Aggregate(NLCPGCapability.None, (current, capability) => current | capability);
        if ((resolved & ~NLCPGCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.RequestedCapabilities), "The requested CPG capability set contains an unknown value.");
        }

        if ((resolved & (NLCPGCapability.MethodModel |
                         NLCPGCapability.CallTargets |
                         NLCPGCapability.Cfg |
                         NLCPGCapability.DataFlow |
                         NLCPGCapability.InterproceduralDataFlow |
                         NLCPGCapability.Dominance |
                         NLCPGCapability.ControlDependence)) != 0)
        {
            resolved |= NLCPGCapability.MethodModel;
        }

        if ((resolved & NLCPGCapability.DataFlow) != 0)
        {
            resolved |= NLCPGCapability.CallTargets | NLCPGCapability.Cfg;
        }

        if ((resolved & NLCPGCapability.InterproceduralDataFlow) != 0)
        {
            resolved |= NLCPGCapability.DataFlow |
                        NLCPGCapability.CallTargets |
                        NLCPGCapability.MethodModel |
                        NLCPGCapability.QueryIndex;
        }

        if ((resolved & NLCPGCapability.Dominance) != 0)
        {
            resolved |= NLCPGCapability.Cfg;
        }

        if ((resolved & NLCPGCapability.ControlDependence) != 0)
        {
            resolved |= NLCPGCapability.Dominance | NLCPGCapability.Cfg;
        }

        if (resolved != NLCPGCapability.None)
        {
            resolved |= NLCPGCapability.SyntaxSemantic;
        }

        return new CapabilityBuildPlan(
            resolved,
            RequiresMethodModel: (resolved & NLCPGCapability.MethodModel) != 0,
            RequiresCallTargets: (resolved & NLCPGCapability.CallTargets) != 0,
            RequiresCfg: (resolved & NLCPGCapability.Cfg) != 0,
            RequiresDataFlow: (resolved & NLCPGCapability.DataFlow) != 0,
            RequiresInterproceduralDataFlow: (resolved & NLCPGCapability.InterproceduralDataFlow) != 0,
            RequiresDominance: (resolved & NLCPGCapability.Dominance) != 0,
            RequiresControlDependence: (resolved & NLCPGCapability.ControlDependence) != 0);
    }

    private void RunOptionalPass(bool shouldRun, INLCPGPass pass, NLCPGBuildContext context)
    {
        if (!shouldRun)
        {
            return;
        }

        pass.Run(this, context);
    }

    internal void RunInterproceduralDataFlowPass(NLCPGBuildContext context)
    {
        var graph = context.Graph;
        var dataFlowEdges = graph.PendingEdges
          .Where(edge => edge.Kind == NLCPGEdgeKind.DataFlow)
          .ToArray();
        var plans = new List<InterproceduralDataFlowPlan>();
        var summaryBudget = new FlowSummaryBudget(_options.EffectiveFlowSummaryOptions);
        var recordedReturnMethods = new HashSet<string>(StringComparer.Ordinal);
        var cuts = new Dictionary<string, int>(StringComparer.Ordinal);
        var options = _options.EffectiveInterproceduralDataFlowOptions;
        var methodBoundaryNodes = graph.Nodes
          .Where(node => node.Kind is NLCPGNodeKind.MethodParameter or NLCPGNodeKind.MethodReturn)
          .ToArray();

        foreach (var callSite in graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.CallSite)
          .OrderBy(node => node.FullName, StringComparer.Ordinal)
          .ThenBy(node => node.SpanStart)
          .ThenBy(NodeSortKey, StringComparer.Ordinal))
        {
            var targets = graph.PendingEdges
              .Where(edge =>
                edge.Kind == NLCPGEdgeKind.CallTargets &&
                ReferenceEquals(edge.SourceNode, callSite))
              .Select(edge => edge.TargetNode)
              .Distinct()
              .OrderBy(target => target.FullName, StringComparer.Ordinal)
              .ThenBy(NodeSortKey, StringComparer.Ordinal)
              .ToArray();
            if (targets.Length == 0)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                RecordCut(cuts, "UnresolvedTarget");
                continue;
            }

            if (targets.Length > options.MaxCallTargetsPerSite)
            {
                RecordCut(cuts, "AmbiguousTarget");
                continue;
            }

            var targetMethodNode = targets[0];
            if (!TryGetSymbolKey(targetMethodNode, out var targetMethodSymbolKey))
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                RecordCut(cuts, "UnresolvedTarget");
                continue;
            }

            var hasInternalBoundary = methodBoundaryNodes.Any(node =>
              IsMethodBoundaryNode(node, targetMethodSymbolKey));
            if (!hasInternalBoundary)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                RecordCut(cuts, "ExternalTarget");
                continue;
            }

            var callSitePlans = dataFlowEdges
              .Where(edge =>
                edge.TargetNode.Kind == NLCPGNodeKind.MethodParameter &&
                IsMethodBoundaryNode(edge.TargetNode, targetMethodSymbolKey))
              .Select(edge => new InterproceduralDataFlowPlan(
                callSite,
                targetMethodNode,
                edge.SourceNode,
                edge.TargetNode,
                NLCPGInterproceduralBridgeKind.ArgumentToParameter,
                ParseArgumentOrdinal(edge.TargetNode)))
              .Concat(dataFlowEdges
                .Where(edge =>
                  edge.SourceNode.Kind == NLCPGNodeKind.MethodReturn &&
                  ReferenceEquals(edge.TargetNode, callSite) &&
                  IsMethodBoundaryNode(edge.SourceNode, targetMethodSymbolKey))
                .Select(edge => new InterproceduralDataFlowPlan(
                  callSite,
                  targetMethodNode,
                  edge.SourceNode,
                  edge.TargetNode,
                  NLCPGInterproceduralBridgeKind.MethodReturnToCallResult)))
              .ToList();
            if (recordedReturnMethods.Add(targetMethodNode.FullName ?? string.Empty))
            {
                callSitePlans.AddRange(dataFlowEdges
                  .Where(edge =>
                    edge.TargetNode.Kind == NLCPGNodeKind.MethodReturn &&
                    IsMethodBoundaryNode(edge.TargetNode, targetMethodSymbolKey))
                  .Select(edge => new InterproceduralDataFlowPlan(
                    callSite,
                    targetMethodNode,
                    edge.SourceNode,
                    edge.TargetNode,
                    NLCPGInterproceduralBridgeKind.ReturnToMethodReturn)));
            }

            if (callSitePlans.Count == 0)
            {
                RecordCut(cuts, "MissingIntraFacts");
                continue;
            }

            plans.AddRange(callSitePlans.Take(options.MaxBoundaryEdgesPerMethod));
            if (callSitePlans.Count > options.MaxBoundaryEdgesPerMethod)
            {
                RecordCut(cuts, "BoundaryEdgeBudget");
            }
        }

        var orderedPlans = plans
          .Distinct()
          .OrderBy(plan => NodeSortKey(plan.CallSiteNode), StringComparer.Ordinal)
          .ThenBy(plan => NodeSortKey(plan.TargetMethodNode), StringComparer.Ordinal)
          .ThenBy(plan => plan.ArgumentOrdinal)
          .ThenBy(plan => plan.BridgeKind)
          .ThenBy(plan => NodeSortKey(plan.SourceNode), StringComparer.Ordinal)
          .ThenBy(plan => NodeSortKey(plan.TargetNode), StringComparer.Ordinal)
          .ToArray();
        foreach (var plan in orderedPlans)
        {
            var callSiteContext = BuildPendingNodeCallSiteContext(plan.CallSiteNode);
            graph.AddEdge(
                plan.SourceNode,
                plan.TargetNode,
                NLCPGEdgeKind.InterproceduralDataFlow,
                NLCPGEdgeLabel.ForInterproceduralBridge(plan.BridgeKind),
                callSiteContext.ToContextId(),
                callSiteContext);
        }

        LastFlowSummaryMetrics = summaryBudget.ToMetrics();

    }

    private void AddExternalSummaryMappings(NLCPGNode callSite, NLCPGGraph graph, FlowSummaryBudget budget)
    {
        if (_options.CallFlowResolver is null)
        {
            return;
        }

        var invocation = _callSiteNodesByInvocation
          .FirstOrDefault(pair => pair.Value == callSite).Key;
        if (invocation is null)
        {
            return;
        }

        var context = BuildPendingNodeCallSiteContext(callSite);
        budget.BeginCallSite();
        var resolvedMappings = _options.CallFlowResolver.ResolveAll(invocation);
        foreach (var resolved in resolvedMappings.Where(result => !result.IsResolved || result.Mapping is null))
        {
            budget.RecordRejected(resolved.Status);
        }

        foreach (var resolved in resolvedMappings
          .Where(result => result.IsResolved && result.Mapping is not null)
          .OrderBy(result => result.MethodKey.StableKey, StringComparer.Ordinal)
          .ThenBy(result => result.Mapping!.Source.Kind)
          .ThenBy(result => result.Mapping!.Source.ParameterOrdinal)
          .ThenBy(result => result.Mapping!.Target.Kind)
          .ThenBy(result => result.Mapping!.Target.ParameterOrdinal))
        {
            if (!budget.TryConsume(resolved.MethodKey.StableKey))
            {
                continue;
            }

            var source = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping!.Source, graph);
            var target = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping.Target, graph);
            if (source is null || target is null)
            {
                budget.RejectedEndpoints += 1;
                continue;
            }

            graph.AddEdge(
              source,
              target,
              NLCPGEdgeKind.InterproceduralDataFlow,
              NLCPGEdgeLabel.ForFlowSummaryBridge(
                NLCPGInterproceduralBridgeKind.SummaryMapping,
                resolved.Resolution,
                resolved.MethodKey.StableKey,
                resolved.Mapping.Source,
                resolved.Mapping.Target),
              context.ToContextId(),
              context);
        }
    }

    private NLCPGNode? ResolveInvocationEndpointNode(
      IInvocationOperation invocation,
      NLCPGNode callSite,
      FlowSummaryEndpoint endpoint,
      NLCPGGraph graph)
    {
        if (endpoint.Kind == FlowSummaryEndpointKind.Return)
        {
            return callSite;
        }

        if (endpoint.Kind == FlowSummaryEndpointKind.Receiver)
        {
            return invocation.Instance is null ? null : GetOrCreateOperationNode(invocation.Instance, graph);
        }

        var argument = invocation.Arguments.FirstOrDefault(candidate =>
          candidate.Parameter?.Ordinal == endpoint.ParameterOrdinal);
        return argument is null ? null : GetOrCreateOperationNode(argument.Value, graph);
    }

    private bool IsMethodBoundaryNode(NLCPGNode boundaryNode, string targetMethodSymbolKey)
    {
        return _methodOwnerSymbolKeysByBoundaryNode.TryGetValue(boundaryNode, out var boundaryMethodSymbolKey) &&
          string.Equals(boundaryMethodSymbolKey, targetMethodSymbolKey, StringComparison.Ordinal);
    }

    private int ParseArgumentOrdinal(NLCPGNode methodParameterNode)
    {
        return _methodParameterOrdinalsByNode.TryGetValue(methodParameterNode, out var ordinal)
          ? ordinal
          : -1;
    }

    private static string NodeSortKey(NLCPGNode node)
    {
        return $"{node.Kind}|{node.FullName}|{node.Name}|{node.FilePath}|{node.SpanStart}|{node.SpanEnd}";
    }

    private static NLCPGCallSiteContext BuildPendingNodeCallSiteContext(NLCPGNode node)
    {
        return new NLCPGCallSiteContext(
          node.FilePath ?? string.Empty,
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          node.FullName ?? node.Name ?? node.DisplayKind);
    }

    private bool TryGetSymbolKey(NLCPGNode node, out string symbolKey)
    {
        return _symbolKeysByNode.TryGetValue(node, out symbolKey!);
    }

    private static void RecordCut(IDictionary<string, int> cuts, string reason)
    {
        cuts[reason] = cuts.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    private sealed class FlowSummaryBudget
    {
        private readonly NLCPGFlowSummaryOptions _options;
        private readonly Dictionary<string, int> _methodCounts = new(StringComparer.Ordinal);
        private int _callSiteCount;

        internal FlowSummaryBudget(NLCPGFlowSummaryOptions options)
        {
            _options = options;
            _options.Validate();
        }

        internal int ResolvedMappings { get; private set; }
        internal int UnknownCalls { get; private set; }
        internal int SignatureMismatches { get; private set; }
        internal int BlockedMappings { get; private set; }
        internal int RejectedEndpoints { get; set; }
        internal int TruncatedMappings { get; private set; }

        internal bool TryConsume(string methodKey)
        {
            var methodCount = _methodCounts.GetValueOrDefault(methodKey);
            if (_callSiteCount >= _options.MaxMappingsPerCallSite ||
                ResolvedMappings >= _options.MaxMappingsPerBuild ||
                methodCount >= _options.MaxMappingsPerMethod)
            {
                TruncatedMappings += 1;
                return false;
            }

            _methodCounts[methodKey] = methodCount + 1;
            _callSiteCount += 1;
            ResolvedMappings += 1;
            return true;
        }

        internal void BeginCallSite()
        {
            _callSiteCount = 0;
        }

        internal void RecordRejected(ResolvedCallFlowStatus status)
        {
            switch (status)
            {
                case ResolvedCallFlowStatus.Unknown:
                    UnknownCalls += 1;
                    break;
                case ResolvedCallFlowStatus.SignatureMismatch:
                    SignatureMismatches += 1;
                    break;
                case ResolvedCallFlowStatus.Blocked:
                    BlockedMappings += 1;
                    break;
            }
        }

        internal NLCPGFlowSummaryMetrics ToMetrics() => new(
          ResolvedMappings, UnknownCalls, SignatureMismatches, BlockedMappings, RejectedEndpoints, TruncatedMappings);
    }

    private void RunPipeline(IReadOnlyList<INLCPGPass> pipeline, NLCPGBuildContext context)
    {
        foreach (var pass in pipeline)
        {
            pass.Run(this, context);
        }
    }

    private void CompleteOperationBackedSyntaxTypes(NLCPGBuildContext context)
    {
        foreach (var syntax in _pendingOperationSyntaxTypeNodes.ToArray())
        {
            if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode))
            {
                continue;
            }

            var typeSymbol = context.SemanticModel.GetTypeInfo(syntax).Type;
            AddTypeEdges(syntaxNode, typeSymbol, context.Graph);
        }

        _pendingOperationSyntaxTypeNodes.Clear();
    }

    private void AddOperationBackedSyntaxTypeEdge(IOperation operation, NLCPGGraph graph)
    {
        if (!_pendingOperationSyntaxTypeNodes.Remove(operation.Syntax) ||
            !_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
        {
            return;
        }

        if (operation.Type is null)
        {
            _pendingOperationSyntaxTypeNodes.Add(operation.Syntax);
            return;
        }

        AddTypeEdges(syntaxNode, operation.Type, graph);
    }

    private void AddControlFlowEdge(NLCPGNode sourceNode, NLCPGNode targetNode, NLCPGEdgeKind edgeKind, NLCPGGraph graph)
    {
        graph.AddEdge(sourceNode, targetNode, edgeKind);
        if (edgeKind is not (NLCPGEdgeKind.CfgNext or NLCPGEdgeKind.CfgTrue or NLCPGEdgeKind.CfgFalse))
        {
            return;
        }

        AddCfgNeighbor(_cfgSuccessorsByNode, sourceNode, targetNode);
        AddCfgNeighbor(_cfgPredecessorsByNode, targetNode, sourceNode);
    }

    private IReadOnlyCollection<NLCPGNode> GetCachedCfgPredecessors(NLCPGNode node)
    {
        return _cfgPredecessorsByNode.TryGetValue(node, out var predecessors)
          ? predecessors
          : Array.Empty<NLCPGNode>();
    }

    private IReadOnlyCollection<NLCPGNode> GetCachedCfgSuccessors(NLCPGNode node)
    {
        return _cfgSuccessorsByNode.TryGetValue(node, out var successors)
          ? successors
          : Array.Empty<NLCPGNode>();
    }

    private static void AddCfgNeighbor(Dictionary<NLCPGNode, HashSet<NLCPGNode>> neighborsByNode, NLCPGNode node, NLCPGNode neighborNode)
    {
        if (!neighborsByNode.TryGetValue(node, out var neighbors))
        {
            neighbors = new HashSet<NLCPGNode>();
            neighborsByNode[node] = neighbors;
        }

        neighbors.Add(neighborNode);
    }

    private static string PropertyAccessorCallSiteKey(IPropertyReferenceOperation propertyReference, IMethodSymbol accessorMethod)
    {
        return $"{PropertyAccessorCallSitePrefix}:{BuildStableFilePath(propertyReference.Syntax.SyntaxTree.FilePath)}:{propertyReference.Syntax.SpanStart}:{propertyReference.Syntax.Span.End}:{ComposeInvocationMethodFullName(accessorMethod)}";
    }

    private const string PropertyAccessorCallSitePrefix = "callsite-property";

    private static string BuildStableFilePath(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
          ? string.Empty
          : Path.GetFullPath(filePath);
    }

    private static string ComposeOperationPath(IOperation operation)
    {
        var segments = new Stack<int>();
        for (var current = operation; current.Parent is not null; current = current.Parent)
        {
            var childIndex = 0;
            var found = false;
            foreach (var child in current.Parent.ChildOperations)
            {
                if (ReferenceEquals(child, current))
                {
                    found = true;
                    break;
                }

                childIndex += 1;
            }

            segments.Push(found ? childIndex : -1);
        }

        return segments.Count == 0 ? "root" : string.Join(".", segments);
    }


    private NLCPGNode GetOrCreateOperationNode(IOperation operation, NLCPGGraph graph)
    {
        var kind = MapOperationKind(operation);
        return graph.AddNode(new NLCPGNode(
          Kind: kind,
          DisplayKind: operation.Kind.ToString(),
          Name: ResolveOperationName(operation),
          FullName: ResolveOperationFullName(operation),
          Signature: ResolveOperationSignature(operation),
          TypeFullName: ComposeTypeFullName(operation.Type),
          FilePath: operation.Syntax.SyntaxTree.FilePath,
          SpanStart: operation.Syntax.SpanStart,
          SpanEnd: operation.Syntax.Span.End,
          IsImplicit: operation.IsImplicit));
    }

    private static DataFlowOperationIndex CreateDataFlowOperationIndex(IReadOnlyList<OperationInventoryEntry> operationInventory, IReadOnlyCollection<IOperation> methodRoots)
    {
        var nodesByOperation = new Dictionary<IOperation, NLCPGNode>(
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);
        var methodBlockSet = new HashSet<IOperation>(
            methodRoots,
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);

        foreach (var entry in operationInventory)
        {
            if (!methodBlockSet.Contains(entry.MethodRoot))
            {
                continue;
            }

            nodesByOperation[entry.Operation] = entry.Node;
        }

        return new DataFlowOperationIndex(nodesByOperation);
    }

    private static IEnumerable<IOperation> EnumerateOperations(NLCPGBuildContext context)
    {
        return context.OperationInventory.Select(entry => entry.Operation);
    }

    private void AddDeclaredSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(syntax);
        AddDeclaredSymbolEdges(syntaxNode, symbol, graph);
    }

    private static bool CanDeclareSymbol(SyntaxNode syntax)
    {
        return syntax is BaseNamespaceDeclarationSyntax or
          BaseTypeDeclarationSyntax or
          DelegateDeclarationSyntax or
          BaseMethodDeclarationSyntax or
          LocalFunctionStatementSyntax or
          AccessorDeclarationSyntax or
          PropertyDeclarationSyntax or
          IndexerDeclarationSyntax or
          EventDeclarationSyntax or
          EnumMemberDeclarationSyntax or
          VariableDeclaratorSyntax or
          ParameterSyntax or
          TypeParameterSyntax or
          SingleVariableDesignationSyntax or
          UsingDirectiveSyntax or
          ExternAliasDirectiveSyntax or
          LabeledStatementSyntax or
          ForEachStatementSyntax or
          ForEachVariableStatementSyntax or
          CatchDeclarationSyntax or
          FromClauseSyntax or
          JoinClauseSyntax or
          LetClauseSyntax or
          QueryContinuationSyntax;
    }

    private void AddDeclaredSymbolEdges(NLCPGNode syntaxNode, ISymbol? symbol, NLCPGGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, NLCPGEdgeKind.DeclaresSymbol);

        if (symbol is INamedTypeSymbol declaredTypeSymbol)
        {
            _declaredTypes.Add(declaredTypeSymbol);
            var typeDeclNode = GetOrCreateTypeDeclNode(declaredTypeSymbol, graph);
            graph.AddEdge(syntaxNode, typeDeclNode, NLCPGEdgeKind.SyntaxChild);
            graph.AddEdge(typeDeclNode, symbolNode, NLCPGEdgeKind.DeclaresSymbol);
            graph.AddEdge(typeDeclNode, symbolNode, NLCPGEdgeKind.RefersToType);
        }

        if (symbol.ContainingSymbol is not null && symbol.ContainingSymbol.Kind != SymbolKind.NetModule)
        {
            var containerNode = GetOrCreateSymbolNode(symbol.ContainingSymbol, graph);
            graph.AddEdge(containerNode, symbolNode, NLCPGEdgeKind.ContainsSymbol);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            foreach (var baseType in namedType.Interfaces.Cast<ITypeSymbol>().Append(namedType.BaseType).Where(x => x is not null))
            {
                var baseTypeNode = GetOrCreateSymbolNode(baseType!, graph);
                graph.AddEdge(symbolNode, baseTypeNode, NLCPGEdgeKind.BaseType);
                var typeDeclNode = GetOrCreateTypeDeclNode(namedType, graph);
                graph.AddEdge(typeDeclNode, baseTypeNode, NLCPGEdgeKind.InheritsFrom);
            }
        }

        if (symbol is IMethodSymbol declaredMethodSymbol && declaredMethodSymbol.ReturnType is not null)
        {
            var returnTypeNode = GetOrCreateSymbolNode(declaredMethodSymbol.ReturnType, graph);
            graph.AddEdge(symbolNode, returnTypeNode, NLCPGEdgeKind.ReturnsType);
        }

        AddTypeEdges(symbolNode, SymbolTypeOf(symbol), graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel)
    {
        if (!CanReferenceSymbol(syntax))
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(syntax).Symbol;
        AddReferencedSymbolEdges(syntax, syntaxNode, symbol, graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, NLCPGNode syntaxNode, ISymbol? symbol, NLCPGGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, NLCPGEdgeKind.ReferencesSymbol);

        var referenceNode = graph.AddNode(new NLCPGNode(
          Kind: NLCPGNodeKind.Reference,
          DisplayKind: nameof(NLCPGNodeKind.Reference),
          Name: syntaxNode.Name,
          FullName: symbolNode.FullName,
          TypeFullName: symbolNode.TypeFullName,
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, referenceNode, NLCPGEdgeKind.SyntaxChild);
        graph.AddEdge(referenceNode, symbolNode, NLCPGEdgeKind.Ref);
        AddEvalTypeEdge(referenceNode, SymbolTypeOf(symbol), graph);
    }

    private void AddTypeEdges(NLCPGNode sourceNode, ITypeSymbol? typeSymbol, NLCPGGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, NLCPGEdgeKind.HasType);
    }

    private void AddEvalTypeEdge(NLCPGNode sourceNode, ITypeSymbol? typeSymbol, NLCPGGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, NLCPGEdgeKind.EvalType);
    }

    private void AddTypeReferenceEdges(SyntaxNode syntax, NLCPGNode syntaxNode, NLCPGGraph graph, SemanticModel semanticModel, ITypeSymbol? resolvedTypeSymbol = null)
    {
        if (syntax is not TypeSyntax and not ObjectCreationExpressionSyntax and not BaseTypeSyntax)
        {
            return;
        }

        var typeSymbol = resolvedTypeSymbol ?? syntax switch
        {
            TypeSyntax typeSyntax => semanticModel.GetTypeInfo(typeSyntax).Type,
            ObjectCreationExpressionSyntax creation => semanticModel.GetTypeInfo(creation).Type,
            BaseTypeSyntax baseType => semanticModel.GetTypeInfo(baseType.Type).Type,
            _ => null,
        };
        if (typeSymbol is null)
        {
            return;
        }

        var typeRefNode = graph.AddNode(new NLCPGNode(
          Kind: NLCPGNodeKind.TypeRef,
          DisplayKind: nameof(NLCPGNodeKind.TypeRef),
          Name: typeSymbol.Name,
          FullName: ComposeTypeFullName(typeSymbol),
          TypeFullName: ComposeTypeFullName(typeSymbol),
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, typeRefNode, NLCPGEdgeKind.SyntaxChild);
        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(typeRefNode, typeNode, NLCPGEdgeKind.RefersToType);
    }

    private NLCPGNode GetOrCreateSymbolNode(ISymbol symbol, NLCPGGraph graph)
    {
        var symbolKey = SymbolId(symbol);
        if (_symbolNodes.TryGetValue(symbolKey, out var existing))
        {
            return existing;
        }

        var symbolNode = graph.AddNode(new NLCPGNode(
          Kind: MapSymbolKind(symbol),
          DisplayKind: symbol.Kind.ToString(),
          Name: symbol.Name,
          FullName: ComposeFullName(symbol),
          Signature: ComposeSignature(symbol),
          DispatchKind: symbol is IMethodSymbol methodDispatchSymbol ? ComposeMethodDispatchKind(methodDispatchSymbol) : null,
          TypeFullName: ComposeTypeFullName(SymbolTypeOf(symbol)),
          FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
        _symbolKeysByNode[symbolNode] = symbolKey;
        _symbolNodes[symbolKey] = symbolNode;
        if (symbol is IMethodSymbol registeredMethodSymbol)
        {
            RegisterMethodSymbol(registeredMethodSymbol);
        }

        return symbolNode;
    }

    private NLCPGNode GetOrCreateTypeDeclNode(INamedTypeSymbol symbol, NLCPGGraph graph)
    {
        var key = $"typedecl:{ComposeFullName(symbol)}";
        if (_typeDeclNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var typeDeclNode = graph.AddNode(new NLCPGNode(
          Kind: NLCPGNodeKind.TypeDecl,
          DisplayKind: nameof(NLCPGNodeKind.TypeDecl),
          Name: symbol.Name,
          FullName: ComposeFullName(symbol),
          Signature: ComposeTypeParameterSignature(symbol),
          TypeFullName: ComposeTypeFullName(symbol),
          FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
        _typeDeclNodes[key] = typeDeclNode;
        return typeDeclNode;
    }


    private static NLCPGEdgeKind SelectOperationEdge(IOperation parent, IOperation child)
    {
        return parent switch
        {
            IInvocationOperation when child is IArgumentOperation => NLCPGEdgeKind.OpArgument,
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, child) => NLCPGEdgeKind.OpInstance,
            IFieldReferenceOperation fieldReference when ReferenceEquals(fieldReference.Instance, child) => NLCPGEdgeKind.OpInstance,
            IPropertyReferenceOperation when child is IArgumentOperation => NLCPGEdgeKind.OpArgument,
            IPropertyReferenceOperation propertyReference when ReferenceEquals(propertyReference.Instance, child) => NLCPGEdgeKind.OpInstance,
            IReturnOperation when ReferenceEquals(parent.ChildOperations.FirstOrDefault(), child) => NLCPGEdgeKind.OpTarget,
            IConditionalOperation conditional when ReferenceEquals(conditional.Condition, child) => NLCPGEdgeKind.OpCondition,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenTrue, child) => NLCPGEdgeKind.OpWhenTrue,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenFalse, child) => NLCPGEdgeKind.OpWhenFalse,
            ILoopOperation loop when ReferenceEquals(loop.Body, child) => NLCPGEdgeKind.OpBody,
            _ => NLCPGEdgeKind.OpChild,
        };
    }

    private static NLCPGNodeKind MapSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Namespace => NLCPGNodeKind.SymbolNamespace,
            SymbolKind.NamedType => NLCPGNodeKind.SymbolType,
            SymbolKind.Method => NLCPGNodeKind.SymbolMethod,
            SymbolKind.Property => NLCPGNodeKind.SymbolProperty,
            SymbolKind.Field => NLCPGNodeKind.SymbolField,
            SymbolKind.Local => NLCPGNodeKind.SymbolLocal,
            SymbolKind.Parameter => NLCPGNodeKind.SymbolParameter,
            _ => NLCPGNodeKind.SymbolUnknown,
        };
    }

    private static NLCPGNodeKind MapOperationKind(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation => NLCPGNodeKind.OpBlock,
            IInvocationOperation => NLCPGNodeKind.OpInvocation,
            IArgumentOperation => NLCPGNodeKind.OpArgument,
            IBinaryOperation => NLCPGNodeKind.OpBinary,
            IAssignmentOperation => NLCPGNodeKind.OpAssignment,
            ILocalReferenceOperation => NLCPGNodeKind.OpLocalReference,
            IParameterReferenceOperation => NLCPGNodeKind.OpParameterReference,
            IFieldReferenceOperation => NLCPGNodeKind.OpFieldReference,
            IPropertyReferenceOperation => NLCPGNodeKind.OpPropertyReference,
            ILiteralOperation => NLCPGNodeKind.OpLiteral,
            IReturnOperation => NLCPGNodeKind.OpReturn,
            IBranchOperation branch when branch.BranchKind == BranchKind.Break => NLCPGNodeKind.OpBreak,
            IBranchOperation branch when branch.BranchKind == BranchKind.Continue => NLCPGNodeKind.OpContinue,
            ISwitchOperation => NLCPGNodeKind.OpSwitch,
            ITryOperation => NLCPGNodeKind.OpTry,
            ICatchClauseOperation => NLCPGNodeKind.OpCatch,
            IConditionalOperation => NLCPGNodeKind.OpConditional,
            ILoopOperation => NLCPGNodeKind.OpLoop,
            _ => NLCPGNodeKind.Operation,
        };
    }

    private static ISymbol? ResolveOperationSymbol(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null,
        };
    }

    private static ITypeSymbol? SymbolTypeOf(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            ITypeSymbol type => type,
            _ => null,
        };
    }

    private static bool CanReferenceSymbol(SyntaxNode syntax)
    {
        return syntax is IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax;
    }

    private static string SymbolId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
          ?? $"symbol:{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:{symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? -1}";
    }

    private static string ComposeFullName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string ComposeTypeFullName(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal)
          ?? string.Empty;
    }

    private static string SyntaxId(SyntaxNode syntax, string filePath)
    {
        return $"syntax:{Path.GetFullPath(filePath)}:{syntax.RawKind}:{syntax.SpanStart}:{syntax.Span.End}";
    }

    private static string TokenId(SyntaxToken token, string filePath)
    {
        return $"token:{Path.GetFullPath(filePath)}:{token.RawKind}:{token.SpanStart}:{token.Span.End}";
    }

    private static string ResolveOperationName(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.Name,
            ILocalReferenceOperation localReference => localReference.Local.Name,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.Name,
            IFieldReferenceOperation fieldReference => fieldReference.Field.Name,
            IPropertyReferenceOperation propertyReference => propertyReference.Property.Name,
            _ => operation.Kind.ToString(),
        };
    }

    private static string? ResolveOperationFullName(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeFullName(symbol) : null;
    }

    private static string? ResolveOperationSignature(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeSignature(symbol) : null;
    }


    private static IMethodSymbol CanonicalMethodSymbol(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is null)
        {
            return methodSymbol;
        }

        return methodSymbol.ReducedFrom;
    }

    private static string ComposeMethodFullName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeInvocationMethodFullName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeInvocationMethodName(methodSymbol)}:{ComposeInvocationSignature(methodSymbol)}";
    }

    private static string ComposeMethodSignature(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeInvocationSignature(IMethodSymbol methodSymbol)
    {
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeMethodLookupKey(IMethodSymbol methodSymbol)
    {
        return $"{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeMethodName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation &&
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            var implementedMethod = methodSymbol.ExplicitInterfaceImplementations[0];
            return $"{ComposeTypeFullName(implementedMethod.ContainingType)}.{implementedMethod.Name}";
        }

        return methodSymbol.Name;
    }

    private static string ComposeInvocationMethodName(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        return methodSymbol.Name;
    }

    private static string ComposeMethodInstantiationKey(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.TypeArguments.Length == 0 && methodSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = methodSymbol.TypeArguments.Length > 0
          ? methodSymbol.TypeArguments.Select(ComposeGenericTypeIdentity)
          : methodSymbol.TypeParameters.Select(parameter => $"{parameter.Ordinal}:{parameter.Name}");
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposeGenericTypeIdentity(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            ITypeParameterSymbol typeParameter => $"{typeParameter.Ordinal}:{typeParameter.Name}",
            _ => ComposeTypeFullName(typeSymbol),
        };
    }

    private static string ComposeSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => ComposeMethodSignature(methodSymbol),
            INamedTypeSymbol typeSymbol => ComposeTypeParameterSignature(typeSymbol),
            IPropertySymbol propertySymbol => ComposePropertySignature(propertySymbol),
            IFieldSymbol fieldSymbol => ComposeTypeFullName(fieldSymbol.Type),
            ILocalSymbol localSymbol => ComposeTypeFullName(localSymbol.Type),
            IParameterSymbol parameterSymbol => ComposeTypeFullName(parameterSymbol.Type),
            _ => string.Empty,
        };
    }

    private static NLCPGDispatchKind ComposeCallDispatchKind(IMethodSymbol methodSymbol, bool hasInstance = true)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? NLCPGDispatchFlags.Internal
          : NLCPGDispatchFlags.External) |
          NLCPGDispatchFlags.Dispatch;
        if (methodSymbol.IsExtensionMethod)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags |
              NLCPGDispatchFlags.Extension |
              (hasInstance ? NLCPGDispatchFlags.Instance : NLCPGDispatchFlags.Static));
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.InterfaceImplementation);
        }

        if (!hasInstance || methodSymbol.IsStatic)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Static);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Interface | NLCPGDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsOverride)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Override | NLCPGDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Virtual | NLCPGDispatchFlags.Dispatch);
        }

        return new NLCPGDispatchKind(
          NLCPGDispatchCategory.Method,
          flags | NLCPGDispatchFlags.Static);
    }

    private static NLCPGDispatchKind ComposePropertyAccessorDispatchKind(IMethodSymbol methodSymbol, bool hasInstance)
    {
        var baseDispatch = ComposeCallDispatchKind(methodSymbol, hasInstance);
        var isIndexer = methodSymbol.AssociatedSymbol is IPropertySymbol { Parameters.Length: > 0 };
        var accessorFlags = isIndexer ? NLCPGDispatchFlags.Indexer : NLCPGDispatchFlags.None;
        if (methodSymbol.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertyGet
            };
        }

        if (methodSymbol.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertySet
            };
        }

        return methodSymbol.AssociatedSymbol is IPropertySymbol
          ? baseDispatch with
          {
            Flags = baseDispatch.Flags | accessorFlags | NLCPGDispatchFlags.PropertyAccessor
          }
          : baseDispatch;
    }

    private static NLCPGDispatchKind ComposeResolvedDispatchKind(IMethodSymbol resolvedMethod, IMethodSymbol requestedMethod, ITypeSymbol? receiverType, NLCPGDispatchKind baseDispatchKind)
    {
        if (!IsInternalMethod(resolvedMethod))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.ExternalFallback
            };
        }

        if (string.Equals(ComposeMethodFullName(resolvedMethod), ComposeMethodFullName(requestedMethod), StringComparison.Ordinal))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Exact
            };
        }

        if (receiverType is INamedTypeSymbol namedReceiverType && resolvedMethod.ContainingType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(resolvedMethod.ContainingType, namedReceiverType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.ReceiverExact
                };
            }

            if (InheritsFrom(namedReceiverType, resolvedMethod.ContainingType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Hierarchy
                };
            }
        }

        return baseDispatchKind with
        {
          Flags = baseDispatchKind.Flags | NLCPGDispatchFlags.Fallback
        };
    }

    private static NLCPGDispatchKind ComposeMethodDispatchKind(IMethodSymbol methodSymbol)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? NLCPGDispatchFlags.Internal
          : NLCPGDispatchFlags.External) |
          NLCPGDispatchFlags.Definition;
        if (methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is not null)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Extension);
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.InterfaceImplementation);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Interface);
        }

        if (methodSymbol.IsOverride)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Override);
        }

        if (methodSymbol.IsAbstract)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Abstract);
        }

        if (methodSymbol.IsVirtual)
        {
            return new NLCPGDispatchKind(
              NLCPGDispatchCategory.Method,
              flags | NLCPGDispatchFlags.Virtual);
        }

        return new NLCPGDispatchKind(
          NLCPGDispatchCategory.Method,
          flags |
          (methodSymbol.IsStatic ? NLCPGDispatchFlags.Static : NLCPGDispatchFlags.Instance));
    }

    private static bool IsInternalMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Locations.Any(location => location.IsInSource);
    }

    private static string ComposeTypeParameterSignature(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeArguments.Length == 0 && typeSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = typeSymbol.TypeArguments.Length > 0
          ? typeSymbol.TypeArguments.Select(ComposeTypeFullName)
          : typeSymbol.TypeParameters.Select(parameter => parameter.Name);
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposePropertySignature(IPropertySymbol propertySymbol)
    {
        if (propertySymbol.Parameters.Length == 0)
        {
            return ComposeTypeFullName(propertySymbol.Type);
        }

        var parameterTypes = string.Join(",", propertySymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        return $"{ComposeTypeFullName(propertySymbol.Type)}[{parameterTypes}]";
    }


    private static bool InheritsFrom(INamedTypeSymbol candidateType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateType, targetType))
        {
            return true;
        }

        foreach (var baseType in candidateType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, targetType))
            {
                return true;
            }
        }

        for (var current = candidateType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetType))
            {
                return true;
            }
        }

        return false;
    }

    private static string NameOfMethod(BaseMethodDeclarationSyntax declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => declaration.Kind().ToString(),
        };
    }

    private static string Shorten(string text, int maxLength = 120)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    internal static IReadOnlyList<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return Array.Empty<MetadataReference>();
        }

        return trustedPlatformAssemblies
          .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
          .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
          .Select(path => MetadataReference.CreateFromFile(path))
          .ToList();
    }
}
