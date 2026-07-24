using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Builder.Preallocation;
using MinimalRoslynCpg.Builder.Passes;
using MinimalRoslynCpg.Builder.Streaming;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using System.Diagnostics;

namespace MinimalRoslynCpg.Builder;

/// <summary>
/// 从单个源码文件构建最小 Roslyn 风格代码属性图。
/// </summary>
public sealed partial class RoslynCpgBuilder
{
    private static readonly IReadOnlyList<IRoslynCpgPass> LegacyPipeline = new IRoslynCpgPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
        OperationPass.Instance,
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };
    private static readonly IReadOnlyList<IRoslynCpgPass> PartitionedPreOperationPipeline = new IRoslynCpgPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
    };
    private static readonly IReadOnlyList<IRoslynCpgPass> PartitionedPostOperationPipeline = new IRoslynCpgPass[]
    {
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };

    private readonly Dictionary<SyntaxNode, RoslynCpgNode> _syntaxNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RoslynCpgNode> _symbolNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _typeDeclNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodParameterNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodReturnNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodEntryNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodExitNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<RoslynCpgNode, string> _symbolKeysByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RoslynCpgNode, string> _methodOwnerSymbolKeysByBoundaryNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RoslynCpgNode, int> _methodParameterOrdinalsByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByNameAndSignature = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<INamedTypeSymbol>> _baseTypeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<RoslynCpgNode, HashSet<RoslynCpgNode>> _cfgPredecessorsByNode = new();
    private readonly Dictionary<RoslynCpgNode, HashSet<RoslynCpgNode>> _cfgSuccessorsByNode = new();
    private readonly Dictionary<IInvocationOperation, RoslynCpgNode> _callSiteNodesByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RoslynCpgNode> _propertyAccessorCallSiteNodesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _pendingOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, SyntaxSemanticFacts> _partitionedSyntaxFacts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SyntaxNode> _matchedOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _operationBackedTypeInfoFallbackCountBySyntaxKind = new(StringComparer.Ordinal);
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private readonly RoslynCpgBuilderOptions _options;
    private int _operationBackedTypeInfoResolvedCount;
    private int _operationBackedTypeInfoFallbackCount;
    private int _operationBackedTypeInfoMissingOperationCount;
    private int _operationBackedTypeInfoNullOperationTypeCount;
    private int _operationChildBufferRentCount;
    private int _peakBufferedOperationFragmentCount;
    private RoslynCpgOrderedWorkWindowTelemetry _operationOrderedWindow = RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
    private RoslynCpgOrderedWorkWindowTelemetry _cfgSensitiveOrderedWindow = RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
    private int _releasedOperationFragmentCount;
    private bool _releasedBuilderOperationState;
    private long _operationBackedTypeInfoFallbackElapsedTicks;
    private RoslynCpgSyntaxPassTelemetry _syntaxPassTelemetry = RoslynCpgSyntaxPassTelemetry.CreateDefault();
    private RoslynCpgMethodDecorationTelemetry _methodDecorationTelemetry = RoslynCpgMethodDecorationTelemetry.CreateDefault();
    private RoslynCpgDataFlowPassTelemetry _dataFlowPassTelemetry = RoslynCpgDataFlowPassTelemetry.CreateDefault();
    private RoslynCpgInterproceduralDataFlowTelemetry _interproceduralDataFlowTelemetry = RoslynCpgInterproceduralDataFlowTelemetry.CreateDefault();

    private sealed record CapabilityBuildPlan(
        RoslynCpgCapability ResolvedCapabilities,
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
        IReadOnlyDictionary<IOperation, RoslynCpgNode> NodesByOperation,
        IReadOnlyDictionary<IOperation, IMethodSymbol> OwningMethods);

    public RoslynCpgBuilder(RoslynCpgBuilderOptions? options = null)
    {
        _options = options ?? RoslynCpgBuilderOptions.CreateDefault();
    }

    public RoslynCpgBuildTelemetry LastBuildTelemetry { get; private set; } = RoslynCpgBuildTelemetry.CreateDefault();

    /// <summary>
    /// 解析源码、收集语法和操作，再补 CFG 与 DataFlow 叠加层。
    /// </summary>
    public RoslynCpgGraph BuildFromSource(string source, string filePath = "input.cs")
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(RoslynCpgBuildContext.CreateFromSource(source, filePath));
        }

        var preallocationStopwatch = Stopwatch.StartNew();
        var identityFactory = new StableNodeIdentityFactory();
        var preflightBuilder = new RoslynCpgBuilder(_options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
        });
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(RoslynCpgBuildContext.CreateFromSourceAnchorDiscovery(
            source,
            filePath,
            identityFactory,
            collector.Add));

        var allocation = collector.CreateAllocation();
        preallocationStopwatch.Stop();
        var preallocation = new RoslynCpgPreallocationTelemetry(
            UsedCompatibilityPreflight: false,
            StableAnchorCount: allocation.Count,
            ElapsedMilliseconds: preallocationStopwatch.ElapsedMilliseconds,
            UsedAnchorDiscovery: true);

        return Build(
            RoslynCpgBuildContext.CreateFromSource(source, filePath, allocation, identityFactory),
            preallocation);
    }

    public RoslynCpgGraph BuildFromSemanticModel(SemanticModel semanticModel, SyntaxNode root, string source, string filePath)
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(RoslynCpgBuildContext.Create(semanticModel, root, source, filePath));
        }

        var preallocationStopwatch = Stopwatch.StartNew();
        var identityFactory = new StableNodeIdentityFactory();
        var preflightBuilder = new RoslynCpgBuilder(_options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
        });
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(RoslynCpgBuildContext.CreateAnchorDiscovery(
            semanticModel,
            root,
            source,
            filePath,
            identityFactory,
            collector.Add));

        var allocation = collector.CreateAllocation();
        preallocationStopwatch.Stop();
        var preallocation = new RoslynCpgPreallocationTelemetry(
            UsedCompatibilityPreflight: false,
            StableAnchorCount: allocation.Count,
            ElapsedMilliseconds: preallocationStopwatch.ElapsedMilliseconds,
            UsedAnchorDiscovery: true);

        return Build(
            RoslynCpgBuildContext.Create(semanticModel, root, source, filePath, allocation, identityFactory),
            preallocation);
    }

    private RoslynCpgGraph Build(
        RoslynCpgBuildContext context,
        RoslynCpgPreallocationTelemetry? preallocation = null)
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
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _matchedOperationSyntaxTypeNodes.Clear();
        _operationBackedTypeInfoFallbackCountBySyntaxKind.Clear();
        _declaredTypes.Clear();
        _operationBackedTypeInfoResolvedCount = 0;
        _operationBackedTypeInfoFallbackCount = 0;
        _operationBackedTypeInfoMissingOperationCount = 0;
        _operationBackedTypeInfoNullOperationTypeCount = 0;
        _operationChildBufferRentCount = 0;
        _peakBufferedOperationFragmentCount = 0;
        _operationOrderedWindow = RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
        _cfgSensitiveOrderedWindow = RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
        _releasedOperationFragmentCount = 0;
        _releasedBuilderOperationState = false;
        _operationBackedTypeInfoFallbackElapsedTicks = 0;
        _syntaxPassTelemetry = RoslynCpgSyntaxPassTelemetry.CreateDefault();
        _methodDecorationTelemetry = RoslynCpgMethodDecorationTelemetry.CreateDefault();
        _dataFlowPassTelemetry = RoslynCpgDataFlowPassTelemetry.CreateDefault();
        _interproceduralDataFlowTelemetry = RoslynCpgInterproceduralDataFlowTelemetry.CreateDefault();
        LastBuildTelemetry = RoslynCpgBuildTelemetry.CreateDefault();
        if (_options.Persistence is not null)
        {
            var restoredGraph = new CpgShardBuildCoordinator(_options.Persistence)
                .TryRestoreAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (restoredGraph is not null)
            {
                LastBuildTelemetry = RoslynCpgBuildTelemetry.CreateDefault() with
                {
                    GraphSnapshotVersion = restoredGraph.GraphSnapshotVersion,
                    GraphNodeCount = restoredGraph.Nodes.Count,
                    GraphEdgeCount = restoredGraph.Edges.Count,
                    ExecutedPassNames = Array.Empty<string>(),
                    SkippedPassNames = Array.Empty<string>(),
                    Preallocation = preallocation,
                };
                return restoredGraph;
            }
        }
        var buildPlan = ResolveCapabilityBuildPlan();
        var executedPassNames = new List<string>();
        var skippedPassNames = new List<string>();
        long syntaxBuildElapsedMilliseconds = 0;
        long operationBuildElapsedMilliseconds = 0;
        long freezeQueryIndexElapsedMilliseconds = 0;
        var streamingFragments = RoslynCpgStreamingFragmentTelemetry.CreateDefault();
        var persistenceTelemetry = CpgPersistenceTelemetry.CreateDefault();
        var operationBuildStrategy = buildPlan.RequiresMethodModel
            ? CreateOperationBuildStrategy(context)
            : new OperationBuildStrategy(
                RoslynCpgBuilderMode.Partitioned,
                UsePartitionedOperationBuild: false,
                SourceLineCount: context.Source.Count(character => character == '\n') + 1,
                OperationRoots: Array.Empty<OperationRootPlan>());
        var usePartitionedSyntaxPass = ShouldUsePartitionedSyntaxPass(context, operationBuildStrategy.OperationRoots);
        SkeletonShardPublisher? streamingPublisher = null;

        try
        {
        var syntaxBuildStopwatch = Stopwatch.StartNew();
        RunSyntaxPass(context, usePartitionedSyntaxPass, operationBuildStrategy.OperationRoots);
        syntaxBuildStopwatch.Stop();
        syntaxBuildElapsedMilliseconds = syntaxBuildStopwatch.ElapsedMilliseconds;
        executedPassNames.Add(nameof(SyntaxPass));

        if (buildPlan.RequiresMethodModel)
        {
            var operationBuildStopwatch = Stopwatch.StartNew();
            MethodDecorationPass.Instance.Run(this, context);
            executedPassNames.Add(nameof(MethodDecorationPass));

            if (_options.Persistence?.StreamingMode == true)
            {
                streamingPublisher = SkeletonShardPublisher.BeginAsync(
                    _options.Persistence,
                    context,
                    CancellationToken.None)
                  .GetAwaiter()
                  .GetResult();
            }

            RunPartitionedOperationPass(context, operationBuildStrategy.OperationRoots, streamingPublisher);
            CompleteOperationBackedSyntaxTypes(context);
            operationBuildStopwatch.Stop();
            operationBuildElapsedMilliseconds = operationBuildStopwatch.ElapsedMilliseconds;
            executedPassNames.Add(nameof(OperationPass));
        }
        else
        {
            skippedPassNames.Add(nameof(MethodDecorationPass));
            skippedPassNames.Add(nameof(OperationPass));
        }

        RunOptionalPass(buildPlan.RequiresCallTargets, CallGraphPass.Instance, context, executedPassNames, skippedPassNames);
        RunOptionalPass(buildPlan.RequiresMethodModel, MemberAccessPass.Instance, context, executedPassNames, skippedPassNames);
        RunOptionalPass(buildPlan.RequiresCfg, ControlFlowPass.Instance, context, executedPassNames, skippedPassNames);
        RunOptionalPass(buildPlan.RequiresDataFlow, DataFlowPass.Instance, context, executedPassNames, skippedPassNames);
        RunOptionalPass(
            buildPlan.RequiresInterproceduralDataFlow,
            InterproceduralDataFlowPass.Instance,
            context,
            executedPassNames,
            skippedPassNames);
        RunOptionalPass(buildPlan.RequiresDominance, DominancePass.Instance, context, executedPassNames, skippedPassNames);
        RunOptionalPass(buildPlan.RequiresControlDependence, ControlDependencePass.Instance, context, executedPassNames, skippedPassNames);

        var freezeTelemetry = context.Graph.FreezeQueryIndex();
        freezeQueryIndexElapsedMilliseconds = freezeTelemetry.TotalElapsedMilliseconds;
        ReleaseTransientBuilderState();

        if (_options.Persistence is not null)
        {
            var persistenceResult = streamingPublisher is null
              ? new CpgShardBuildCoordinator(_options.Persistence)
                .PersistAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult()
              : streamingPublisher
                .CompleteAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            streamingPublisher = null;
            streamingFragments = persistenceResult.StreamingFragments;
            persistenceTelemetry = persistenceResult.Persistence;
        }

        LastBuildTelemetry = new RoslynCpgBuildTelemetry(
          _options.BuildMode,
          operationBuildStrategy.ExecutedMode,
          operationBuildStrategy.UsePartitionedOperationBuild,
          usePartitionedSyntaxPass,
          operationBuildStrategy.SourceLineCount,
          operationBuildStrategy.UsePartitionedOperationBuild ? operationBuildStrategy.OperationRoots.Count : 0,
          _options.EffectiveMaxDegreeOfParallelism,
          OperationBuildElapsedMilliseconds: operationBuildElapsedMilliseconds,
          SyntaxBuildElapsedMilliseconds: syntaxBuildElapsedMilliseconds,
          DataFlowBuildElapsedMilliseconds: _dataFlowPassTelemetry.TotalElapsedMilliseconds,
          FreezeQueryIndexElapsedMilliseconds: freezeQueryIndexElapsedMilliseconds,
          FreezeTelemetry: freezeTelemetry,
          _syntaxPassTelemetry,
          _methodDecorationTelemetry,
          _dataFlowPassTelemetry,
          OperationChildBufferRentCount: Volatile.Read(ref _operationChildBufferRentCount),
          ResolvedCapabilities: EnumerateCapabilities(buildPlan.ResolvedCapabilities),
          ExecutedPassNames: executedPassNames,
          SkippedPassNames: skippedPassNames,
          GraphSnapshotVersion: "capability-v1",
          InterproceduralDataFlowTelemetry: _interproceduralDataFlowTelemetry,
          GraphNodeCount: context.Graph.Nodes.Count,
          GraphEdgeCount: context.Graph.CurrentEdgeCount,
          StreamingFragments: streamingFragments,
          OperationFragments: new RoslynCpgOperationFragmentTelemetry(
            operationBuildStrategy.OperationRoots.Count,
            _releasedOperationFragmentCount,
            _peakBufferedOperationFragmentCount,
            _releasedBuilderOperationState),
          Preallocation: preallocation,
          Persistence: persistenceTelemetry,
          OperationOrderedWindow: _operationOrderedWindow,
          CfgSensitiveOrderedWindow: _cfgSensitiveOrderedWindow,
          AdmissionTelemetry: _options.AdmissionTelemetry);
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
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _matchedOperationSyntaxTypeNodes.Clear();
        _declaredTypes.Clear();
        _releasedBuilderOperationState = true;
    }

    private CapabilityBuildPlan ResolveCapabilityBuildPlan()
    {
        var requestedCapabilities = _options.RequestedCapabilities;
        var resolved = requestedCapabilities is null
            ? RoslynCpgCapability.Default
            : requestedCapabilities.Aggregate(RoslynCpgCapability.None, (current, capability) => current | capability);
        if ((resolved & ~RoslynCpgCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.RequestedCapabilities), "The requested CPG capability set contains an unknown value.");
        }

        if ((resolved & (RoslynCpgCapability.MethodModel |
                         RoslynCpgCapability.CallTargets |
                         RoslynCpgCapability.Cfg |
                         RoslynCpgCapability.DataFlow |
                         RoslynCpgCapability.InterproceduralDataFlow |
                         RoslynCpgCapability.Dominance |
                         RoslynCpgCapability.ControlDependence)) != 0)
        {
            resolved |= RoslynCpgCapability.MethodModel;
        }

        if ((resolved & RoslynCpgCapability.DataFlow) != 0)
        {
            resolved |= RoslynCpgCapability.CallTargets | RoslynCpgCapability.Cfg;
        }

        if ((resolved & RoslynCpgCapability.InterproceduralDataFlow) != 0)
        {
            resolved |= RoslynCpgCapability.DataFlow |
                        RoslynCpgCapability.CallTargets |
                        RoslynCpgCapability.MethodModel |
                        RoslynCpgCapability.QueryIndex;
        }

        if ((resolved & RoslynCpgCapability.Dominance) != 0)
        {
            resolved |= RoslynCpgCapability.Cfg;
        }

        if ((resolved & RoslynCpgCapability.ControlDependence) != 0)
        {
            resolved |= RoslynCpgCapability.Dominance | RoslynCpgCapability.Cfg;
        }

        if (resolved != RoslynCpgCapability.None)
        {
            resolved |= RoslynCpgCapability.SyntaxSemantic;
        }

        return new CapabilityBuildPlan(
            resolved,
            RequiresMethodModel: (resolved & RoslynCpgCapability.MethodModel) != 0,
            RequiresCallTargets: (resolved & RoslynCpgCapability.CallTargets) != 0,
            RequiresCfg: (resolved & RoslynCpgCapability.Cfg) != 0,
            RequiresDataFlow: (resolved & RoslynCpgCapability.DataFlow) != 0,
            RequiresInterproceduralDataFlow: (resolved & RoslynCpgCapability.InterproceduralDataFlow) != 0,
            RequiresDominance: (resolved & RoslynCpgCapability.Dominance) != 0,
            RequiresControlDependence: (resolved & RoslynCpgCapability.ControlDependence) != 0);
    }

    private static IReadOnlyList<RoslynCpgCapability> EnumerateCapabilities(RoslynCpgCapability capabilities)
    {
        return Enum.GetValues<RoslynCpgCapability>()
            .Where(capability => capability != RoslynCpgCapability.None &&
                                 capability != RoslynCpgCapability.Default &&
                                 capability != RoslynCpgCapability.All &&
                                 (capabilities & capability) == capability)
            .OrderBy(capability => capability)
            .ToList();
    }

    private void RunOptionalPass(
        bool shouldRun,
        IRoslynCpgPass pass,
        RoslynCpgBuildContext context,
        List<string> executedPassNames,
        List<string> skippedPassNames)
    {
        if (!shouldRun)
        {
            skippedPassNames.Add(pass.Name);
            return;
        }

        pass.Run(this, context);
        executedPassNames.Add(pass.Name);
    }

    internal void RunInterproceduralDataFlowPass(RoslynCpgBuildContext context)
    {
        var graph = context.Graph;
        var dataFlowEdges = graph.PendingEdges
          .Where(edge => edge.Kind == RoslynCpgEdgeKind.DataFlow)
          .ToArray();
        var plans = new List<InterproceduralDataFlowPlan>();
        var recordedReturnMethods = new HashSet<string>(StringComparer.Ordinal);
        var cuts = new Dictionary<string, int>(StringComparer.Ordinal);
        var options = _options.EffectiveInterproceduralDataFlowOptions;
        var methodBoundaryNodes = graph.Nodes
          .Where(node => node.Kind is RoslynCpgNodeKind.MethodParameter or RoslynCpgNodeKind.MethodReturn)
          .ToArray();

        foreach (var callSite in graph.Nodes
          .Where(node => node.Kind == RoslynCpgNodeKind.CallSite)
          .OrderBy(node => node.FullName, StringComparer.Ordinal)
          .ThenBy(node => node.SpanStart)
          .ThenBy(NodeSortKey, StringComparer.Ordinal))
        {
            var targets = graph.PendingEdges
              .Where(edge =>
                edge.Kind == RoslynCpgEdgeKind.CallTargets &&
                ReferenceEquals(edge.SourceNode, callSite))
              .Select(edge => edge.TargetNode)
              .Distinct()
              .OrderBy(target => target.FullName, StringComparer.Ordinal)
              .ThenBy(NodeSortKey, StringComparer.Ordinal)
              .ToArray();
            if (targets.Length == 0)
            {
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
                RecordCut(cuts, "UnresolvedTarget");
                continue;
            }

            var hasInternalBoundary = methodBoundaryNodes.Any(node =>
              IsMethodBoundaryNode(node, targetMethodSymbolKey));
            if (!hasInternalBoundary)
            {
                RecordCut(cuts, "ExternalTarget");
                continue;
            }

            var callSitePlans = dataFlowEdges
              .Where(edge =>
                edge.TargetNode.Kind == RoslynCpgNodeKind.MethodParameter &&
                IsMethodBoundaryNode(edge.TargetNode, targetMethodSymbolKey))
              .Select(edge => new InterproceduralDataFlowPlan(
                callSite,
                targetMethodNode,
                edge.SourceNode,
                edge.TargetNode,
                RoslynCpgInterproceduralBridgeKind.ArgumentToParameter,
                ParseArgumentOrdinal(edge.TargetNode)))
              .Concat(dataFlowEdges
                .Where(edge =>
                  edge.SourceNode.Kind == RoslynCpgNodeKind.MethodReturn &&
                  ReferenceEquals(edge.TargetNode, callSite) &&
                  IsMethodBoundaryNode(edge.SourceNode, targetMethodSymbolKey))
                .Select(edge => new InterproceduralDataFlowPlan(
                  callSite,
                  targetMethodNode,
                  edge.SourceNode,
                  edge.TargetNode,
                  RoslynCpgInterproceduralBridgeKind.MethodReturnToCallResult)))
              .ToList();
            if (recordedReturnMethods.Add(targetMethodNode.FullName ?? string.Empty))
            {
                callSitePlans.AddRange(dataFlowEdges
                  .Where(edge =>
                    edge.TargetNode.Kind == RoslynCpgNodeKind.MethodReturn &&
                    IsMethodBoundaryNode(edge.TargetNode, targetMethodSymbolKey))
                  .Select(edge => new InterproceduralDataFlowPlan(
                    callSite,
                    targetMethodNode,
                    edge.SourceNode,
                    edge.TargetNode,
                    RoslynCpgInterproceduralBridgeKind.ReturnToMethodReturn)));
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
                RoslynCpgEdgeKind.InterproceduralDataFlow,
                RoslynCpgEdgeLabel.ForInterproceduralBridge(plan.BridgeKind),
                callSiteContext.ToContextId(),
                callSiteContext);
        }

        _interproceduralDataFlowTelemetry = new RoslynCpgInterproceduralDataFlowTelemetry(
          orderedPlans.Length,
          cuts.Values.Sum(),
          new Dictionary<string, int>(cuts, StringComparer.Ordinal));
    }

    private bool IsMethodBoundaryNode(RoslynCpgNode boundaryNode, string targetMethodSymbolKey)
    {
        return _methodOwnerSymbolKeysByBoundaryNode.TryGetValue(boundaryNode, out var boundaryMethodSymbolKey) &&
          string.Equals(boundaryMethodSymbolKey, targetMethodSymbolKey, StringComparison.Ordinal);
    }

    private int ParseArgumentOrdinal(RoslynCpgNode methodParameterNode)
    {
        return _methodParameterOrdinalsByNode.TryGetValue(methodParameterNode, out var ordinal)
          ? ordinal
          : -1;
    }

    private static string NodeSortKey(RoslynCpgNode node)
    {
        return $"{node.Kind}|{node.FullName}|{node.Name}|{node.FilePath}|{node.SpanStart}|{node.SpanEnd}";
    }

    private static RoslynCpgCallSiteContext BuildPendingNodeCallSiteContext(RoslynCpgNode node)
    {
        return new RoslynCpgCallSiteContext(
          node.FilePath ?? string.Empty,
          node.SpanStart ?? -1,
          node.SpanEnd ?? -1,
          node.FullName ?? node.Name ?? node.DisplayKind);
    }

    private bool TryGetSymbolKey(RoslynCpgNode node, out string symbolKey)
    {
        return _symbolKeysByNode.TryGetValue(node, out symbolKey!);
    }

    private static void RecordCut(IDictionary<string, int> cuts, string reason)
    {
        cuts[reason] = cuts.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    private void RunPipeline(IReadOnlyList<IRoslynCpgPass> pipeline, RoslynCpgBuildContext context)
    {
        foreach (var pass in pipeline)
        {
            pass.Run(this, context);
        }
    }

    private void CompleteOperationBackedSyntaxTypes(RoslynCpgBuildContext context)
    {
        foreach (var syntax in _pendingOperationSyntaxTypeNodes.ToArray())
        {
            if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode))
            {
                continue;
            }

            var fallbackStartTimestamp = Stopwatch.GetTimestamp();
            var typeSymbol = context.SemanticModel.GetTypeInfo(syntax).Type;
            _operationBackedTypeInfoFallbackElapsedTicks += Stopwatch.GetTimestamp() - fallbackStartTimestamp;
            AddTypeEdges(syntaxNode, typeSymbol, context.Graph);
            _operationBackedTypeInfoFallbackCount += 1;
            if (!_matchedOperationSyntaxTypeNodes.Contains(syntax))
            {
                _operationBackedTypeInfoMissingOperationCount += 1;
            }

            var syntaxKind = syntax.Kind().ToString();
            _operationBackedTypeInfoFallbackCountBySyntaxKind.TryGetValue(syntaxKind, out var fallbackCount);
            _operationBackedTypeInfoFallbackCountBySyntaxKind[syntaxKind] = fallbackCount + 1;
        }

        _pendingOperationSyntaxTypeNodes.Clear();
        UpdateOperationBackedSyntaxTypeTelemetry();
    }

    private void AddOperationBackedSyntaxTypeEdge(IOperation operation, RoslynCpgGraph graph)
    {
        if (!_pendingOperationSyntaxTypeNodes.Remove(operation.Syntax) ||
            !_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
        {
            return;
        }

        if (operation.Type is null)
        {
            _matchedOperationSyntaxTypeNodes.Add(operation.Syntax);
            _pendingOperationSyntaxTypeNodes.Add(operation.Syntax);
            _operationBackedTypeInfoNullOperationTypeCount += 1;
            return;
        }

        _matchedOperationSyntaxTypeNodes.Add(operation.Syntax);
        AddTypeEdges(syntaxNode, operation.Type, graph);
        _operationBackedTypeInfoResolvedCount += 1;
    }

    private void UpdateOperationBackedSyntaxTypeTelemetry()
    {
        _syntaxPassTelemetry = _syntaxPassTelemetry with
        {
            OperationBackedTypeInfoResolvedCount = _operationBackedTypeInfoResolvedCount,
            OperationBackedTypeInfoFallbackCount = _operationBackedTypeInfoFallbackCount,
            OperationBackedTypeInfoFallbackElapsedMilliseconds =
                (long)Stopwatch.GetElapsedTime(0, _operationBackedTypeInfoFallbackElapsedTicks).TotalMilliseconds,
            OperationBackedTypeInfoFallbackElapsedTicks = _operationBackedTypeInfoFallbackElapsedTicks,
            OperationBackedTypeInfoMissingOperationCount = _operationBackedTypeInfoMissingOperationCount,
            OperationBackedTypeInfoNullOperationTypeCount = _operationBackedTypeInfoNullOperationTypeCount,
            OperationBackedTypeInfoFallbackCountBySyntaxKind =
                new Dictionary<string, int>(_operationBackedTypeInfoFallbackCountBySyntaxKind, StringComparer.Ordinal),
        };
    }

    private void AddControlFlowEdge(
      RoslynCpgNode sourceNode,
      RoslynCpgNode targetNode,
      RoslynCpgEdgeKind edgeKind,
      RoslynCpgGraph graph)
    {
        graph.AddEdge(sourceNode, targetNode, edgeKind);
        if (edgeKind is not (RoslynCpgEdgeKind.CfgNext or RoslynCpgEdgeKind.CfgTrue or RoslynCpgEdgeKind.CfgFalse))
        {
            return;
        }

        AddCfgNeighbor(_cfgSuccessorsByNode, sourceNode, targetNode);
        AddCfgNeighbor(_cfgPredecessorsByNode, targetNode, sourceNode);
    }

    private IReadOnlyCollection<RoslynCpgNode> GetCachedCfgPredecessors(RoslynCpgNode node)
    {
        return _cfgPredecessorsByNode.TryGetValue(node, out var predecessors)
          ? predecessors
          : Array.Empty<RoslynCpgNode>();
    }

    private IReadOnlyCollection<RoslynCpgNode> GetCachedCfgSuccessors(RoslynCpgNode node)
    {
        return _cfgSuccessorsByNode.TryGetValue(node, out var successors)
          ? successors
          : Array.Empty<RoslynCpgNode>();
    }

    private static void AddCfgNeighbor(
      Dictionary<RoslynCpgNode, HashSet<RoslynCpgNode>> neighborsByNode,
      RoslynCpgNode node,
      RoslynCpgNode neighborNode)
    {
        if (!neighborsByNode.TryGetValue(node, out var neighbors))
        {
            neighbors = new HashSet<RoslynCpgNode>();
            neighborsByNode[node] = neighbors;
        }

        neighbors.Add(neighborNode);
    }

    private static string PropertyAccessorCallSiteKey(
      IPropertyReferenceOperation propertyReference,
      IMethodSymbol accessorMethod)
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


    private RoslynCpgNode GetOrCreateOperationNode(IOperation operation, RoslynCpgGraph graph)
    {
        var kind = MapOperationKind(operation);
        return graph.AddNode(new RoslynCpgNode(
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

    private DataFlowOperationIndex CreateDataFlowOperationIndex(
        IReadOnlyList<IBlockOperation> methodBlocks,
        IReadOnlyDictionary<IOperation, IMethodSymbol> owningMethodsByMethodBlock,
        RoslynCpgGraph graph)
    {
        var nodesByOperation = new Dictionary<IOperation, RoslynCpgNode>(
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);
        var owningMethods = new Dictionary<IOperation, IMethodSymbol>(
            (IEqualityComparer<IOperation>)ReferenceEqualityComparer.Instance);

        foreach (var methodBlock in methodBlocks)
        {
            owningMethodsByMethodBlock.TryGetValue(methodBlock, out var owningMethod);
            foreach (var operation in methodBlock.DescendantsAndSelf())
            {
                nodesByOperation[operation] = GetOrCreateOperationNode(operation, graph);
                if (owningMethod is not null)
                {
                    owningMethods[operation] = owningMethod;
                }
            }
        }

        return new DataFlowOperationIndex(nodesByOperation, owningMethods);
    }

    private static IEnumerable<IOperation> EnumerateOperationRoots(RoslynCpgBuildContext context)
    {
        foreach (var rootPlan in GetOperationRootPlans(context.Root, context.SemanticModel))
        {
            var rootOperation = context.SemanticModel.GetOperation(rootPlan.BodySyntax);
            if (rootOperation is null)
            {
                continue;
            }

            foreach (var operation in rootOperation.DescendantsAndSelf())
            {
                yield return operation;
            }
        }
    }

    private void AddDeclaredSymbolEdges(SyntaxNode syntax, RoslynCpgNode syntaxNode, RoslynCpgGraph graph, SemanticModel semanticModel)
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

    private void AddDeclaredSymbolEdges(RoslynCpgNode syntaxNode, ISymbol? symbol, RoslynCpgGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, RoslynCpgEdgeKind.DeclaresSymbol);

        if (symbol is INamedTypeSymbol declaredTypeSymbol)
        {
            _declaredTypes.Add(declaredTypeSymbol);
            var typeDeclNode = GetOrCreateTypeDeclNode(declaredTypeSymbol, graph);
            graph.AddEdge(syntaxNode, typeDeclNode, RoslynCpgEdgeKind.SyntaxChild);
            graph.AddEdge(typeDeclNode, symbolNode, RoslynCpgEdgeKind.DeclaresSymbol);
            graph.AddEdge(typeDeclNode, symbolNode, RoslynCpgEdgeKind.RefersToType);
        }

        if (symbol.ContainingSymbol is not null && symbol.ContainingSymbol.Kind != SymbolKind.NetModule)
        {
            var containerNode = GetOrCreateSymbolNode(symbol.ContainingSymbol, graph);
            graph.AddEdge(containerNode, symbolNode, RoslynCpgEdgeKind.ContainsSymbol);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            foreach (var baseType in namedType.Interfaces.Cast<ITypeSymbol>().Append(namedType.BaseType).Where(x => x is not null))
            {
                var baseTypeNode = GetOrCreateSymbolNode(baseType!, graph);
                graph.AddEdge(symbolNode, baseTypeNode, RoslynCpgEdgeKind.BaseType);
                var typeDeclNode = GetOrCreateTypeDeclNode(namedType, graph);
                graph.AddEdge(typeDeclNode, baseTypeNode, RoslynCpgEdgeKind.InheritsFrom);
            }
        }

        if (symbol is IMethodSymbol declaredMethodSymbol && declaredMethodSymbol.ReturnType is not null)
        {
            var returnTypeNode = GetOrCreateSymbolNode(declaredMethodSymbol.ReturnType, graph);
            graph.AddEdge(symbolNode, returnTypeNode, RoslynCpgEdgeKind.ReturnsType);
        }

        AddTypeEdges(symbolNode, SymbolTypeOf(symbol), graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, RoslynCpgNode syntaxNode, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
        if (!CanReferenceSymbol(syntax))
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(syntax).Symbol;
        AddReferencedSymbolEdges(syntax, syntaxNode, symbol, graph);
    }

    private void AddReferencedSymbolEdges(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      ISymbol? symbol,
      RoslynCpgGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, RoslynCpgEdgeKind.ReferencesSymbol);

        var referenceNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.Reference,
          DisplayKind: nameof(RoslynCpgNodeKind.Reference),
          Name: syntaxNode.Name,
          FullName: symbolNode.FullName,
          TypeFullName: symbolNode.TypeFullName,
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, referenceNode, RoslynCpgEdgeKind.SyntaxChild);
        graph.AddEdge(referenceNode, symbolNode, RoslynCpgEdgeKind.Ref);
        AddEvalTypeEdge(referenceNode, SymbolTypeOf(symbol), graph);
    }

    private void AddTypeEdges(RoslynCpgNode sourceNode, ITypeSymbol? typeSymbol, RoslynCpgGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, RoslynCpgEdgeKind.HasType);
    }

    private void AddEvalTypeEdge(RoslynCpgNode sourceNode, ITypeSymbol? typeSymbol, RoslynCpgGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, RoslynCpgEdgeKind.EvalType);
    }

    private void AddTypeReferenceEdges(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      ITypeSymbol? resolvedTypeSymbol = null)
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

        var typeRefNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.TypeRef,
          DisplayKind: nameof(RoslynCpgNodeKind.TypeRef),
          Name: typeSymbol.Name,
          FullName: ComposeTypeFullName(typeSymbol),
          TypeFullName: ComposeTypeFullName(typeSymbol),
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd));
        graph.AddEdge(syntaxNode, typeRefNode, RoslynCpgEdgeKind.SyntaxChild);
        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(typeRefNode, typeNode, RoslynCpgEdgeKind.RefersToType);
    }

    private RoslynCpgNode GetOrCreateSymbolNode(ISymbol symbol, RoslynCpgGraph graph)
    {
        var symbolKey = SymbolId(symbol);
        if (_symbolNodes.TryGetValue(symbolKey, out var existing))
        {
            return existing;
        }

        var symbolNode = graph.AddNode(new RoslynCpgNode(
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

    private RoslynCpgNode GetOrCreateTypeDeclNode(INamedTypeSymbol symbol, RoslynCpgGraph graph)
    {
        var key = $"typedecl:{ComposeFullName(symbol)}";
        if (_typeDeclNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var typeDeclNode = graph.AddNode(new RoslynCpgNode(
          Kind: RoslynCpgNodeKind.TypeDecl,
          DisplayKind: nameof(RoslynCpgNodeKind.TypeDecl),
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


    private static RoslynCpgEdgeKind SelectOperationEdge(IOperation parent, IOperation child)
    {
        return parent switch
        {
            IInvocationOperation when child is IArgumentOperation => RoslynCpgEdgeKind.OpArgument,
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IFieldReferenceOperation fieldReference when ReferenceEquals(fieldReference.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IPropertyReferenceOperation when child is IArgumentOperation => RoslynCpgEdgeKind.OpArgument,
            IPropertyReferenceOperation propertyReference when ReferenceEquals(propertyReference.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IReturnOperation when ReferenceEquals(parent.ChildOperations.FirstOrDefault(), child) => RoslynCpgEdgeKind.OpTarget,
            IConditionalOperation conditional when ReferenceEquals(conditional.Condition, child) => RoslynCpgEdgeKind.OpCondition,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenTrue, child) => RoslynCpgEdgeKind.OpWhenTrue,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenFalse, child) => RoslynCpgEdgeKind.OpWhenFalse,
            ILoopOperation loop when ReferenceEquals(loop.Body, child) => RoslynCpgEdgeKind.OpBody,
            _ => RoslynCpgEdgeKind.OpChild,
        };
    }

    private static RoslynCpgNodeKind MapSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Namespace => RoslynCpgNodeKind.SymbolNamespace,
            SymbolKind.NamedType => RoslynCpgNodeKind.SymbolType,
            SymbolKind.Method => RoslynCpgNodeKind.SymbolMethod,
            SymbolKind.Property => RoslynCpgNodeKind.SymbolProperty,
            SymbolKind.Field => RoslynCpgNodeKind.SymbolField,
            SymbolKind.Local => RoslynCpgNodeKind.SymbolLocal,
            SymbolKind.Parameter => RoslynCpgNodeKind.SymbolParameter,
            _ => RoslynCpgNodeKind.SymbolUnknown,
        };
    }

    private static RoslynCpgNodeKind MapOperationKind(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation => RoslynCpgNodeKind.OpBlock,
            IInvocationOperation => RoslynCpgNodeKind.OpInvocation,
            IArgumentOperation => RoslynCpgNodeKind.OpArgument,
            IBinaryOperation => RoslynCpgNodeKind.OpBinary,
            IAssignmentOperation => RoslynCpgNodeKind.OpAssignment,
            ILocalReferenceOperation => RoslynCpgNodeKind.OpLocalReference,
            IParameterReferenceOperation => RoslynCpgNodeKind.OpParameterReference,
            IFieldReferenceOperation => RoslynCpgNodeKind.OpFieldReference,
            IPropertyReferenceOperation => RoslynCpgNodeKind.OpPropertyReference,
            ILiteralOperation => RoslynCpgNodeKind.OpLiteral,
            IReturnOperation => RoslynCpgNodeKind.OpReturn,
            IBranchOperation branch when branch.BranchKind == BranchKind.Break => RoslynCpgNodeKind.OpBreak,
            IBranchOperation branch when branch.BranchKind == BranchKind.Continue => RoslynCpgNodeKind.OpContinue,
            ISwitchOperation => RoslynCpgNodeKind.OpSwitch,
            ITryOperation => RoslynCpgNodeKind.OpTry,
            ICatchClauseOperation => RoslynCpgNodeKind.OpCatch,
            IConditionalOperation => RoslynCpgNodeKind.OpConditional,
            ILoopOperation => RoslynCpgNodeKind.OpLoop,
            _ => RoslynCpgNodeKind.Operation,
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

    private static RoslynCpgDispatchKind ComposeCallDispatchKind(IMethodSymbol methodSymbol, bool hasInstance = true)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? RoslynCpgDispatchFlags.Internal
          : RoslynCpgDispatchFlags.External) |
          RoslynCpgDispatchFlags.Dispatch;
        if (methodSymbol.IsExtensionMethod)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags |
              RoslynCpgDispatchFlags.Extension |
              (hasInstance ? RoslynCpgDispatchFlags.Instance : RoslynCpgDispatchFlags.Static));
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.InterfaceImplementation);
        }

        if (!hasInstance || methodSymbol.IsStatic)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Static);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Interface | RoslynCpgDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsOverride)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Override | RoslynCpgDispatchFlags.Dispatch);
        }

        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Virtual | RoslynCpgDispatchFlags.Dispatch);
        }

        return new RoslynCpgDispatchKind(
          RoslynCpgDispatchCategory.Method,
          flags | RoslynCpgDispatchFlags.Static);
    }

    private static RoslynCpgDispatchKind ComposePropertyAccessorDispatchKind(IMethodSymbol methodSymbol, bool hasInstance)
    {
        var baseDispatch = ComposeCallDispatchKind(methodSymbol, hasInstance);
        var isIndexer = methodSymbol.AssociatedSymbol is IPropertySymbol { Parameters.Length: > 0 };
        var accessorFlags = isIndexer ? RoslynCpgDispatchFlags.Indexer : RoslynCpgDispatchFlags.None;
        if (methodSymbol.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | RoslynCpgDispatchFlags.PropertyGet
            };
        }

        if (methodSymbol.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return baseDispatch with
            {
              Flags = baseDispatch.Flags | accessorFlags | RoslynCpgDispatchFlags.PropertySet
            };
        }

        return methodSymbol.AssociatedSymbol is IPropertySymbol
          ? baseDispatch with
          {
            Flags = baseDispatch.Flags | accessorFlags | RoslynCpgDispatchFlags.PropertyAccessor
          }
          : baseDispatch;
    }

    private static RoslynCpgDispatchKind ComposeResolvedDispatchKind(
      IMethodSymbol resolvedMethod,
      IMethodSymbol requestedMethod,
      ITypeSymbol? receiverType,
      RoslynCpgDispatchKind baseDispatchKind)
    {
        if (!IsInternalMethod(resolvedMethod))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | RoslynCpgDispatchFlags.ExternalFallback
            };
        }

        if (string.Equals(ComposeMethodFullName(resolvedMethod), ComposeMethodFullName(requestedMethod), StringComparison.Ordinal))
        {
            return baseDispatchKind with
            {
              Flags = baseDispatchKind.Flags | RoslynCpgDispatchFlags.Exact
            };
        }

        if (receiverType is INamedTypeSymbol namedReceiverType && resolvedMethod.ContainingType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(resolvedMethod.ContainingType, namedReceiverType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | RoslynCpgDispatchFlags.ReceiverExact
                };
            }

            if (InheritsFrom(namedReceiverType, resolvedMethod.ContainingType))
            {
                return baseDispatchKind with
                {
                  Flags = baseDispatchKind.Flags | RoslynCpgDispatchFlags.Hierarchy
                };
            }
        }

        return baseDispatchKind with
        {
          Flags = baseDispatchKind.Flags | RoslynCpgDispatchFlags.Fallback
        };
    }

    private static RoslynCpgDispatchKind ComposeMethodDispatchKind(IMethodSymbol methodSymbol)
    {
        var flags = (IsInternalMethod(methodSymbol)
          ? RoslynCpgDispatchFlags.Internal
          : RoslynCpgDispatchFlags.External) |
          RoslynCpgDispatchFlags.Definition;
        if (methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is not null)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Extension);
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.InterfaceImplementation);
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Interface);
        }

        if (methodSymbol.IsOverride)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Override);
        }

        if (methodSymbol.IsAbstract)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Abstract);
        }

        if (methodSymbol.IsVirtual)
        {
            return new RoslynCpgDispatchKind(
              RoslynCpgDispatchCategory.Method,
              flags | RoslynCpgDispatchFlags.Virtual);
        }

        return new RoslynCpgDispatchKind(
          RoslynCpgDispatchCategory.Method,
          flags |
          (methodSymbol.IsStatic ? RoslynCpgDispatchFlags.Static : RoslynCpgDispatchFlags.Instance));
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
