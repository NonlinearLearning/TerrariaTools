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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    private readonly Dictionary<IOperation, NLCPGNode> _operationNodesByOperation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IInvocationOperation, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IReadOnlyList<IMethodSymbol>> _resolvedCallTargetsByDispatchShape =
      new(StringComparer.Ordinal);
    private readonly Dictionary<string, NLCPGNode> _propertyAccessorCallSiteNodesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _pendingOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, SyntaxSemanticFacts> _partitionedSyntaxFacts = new(ReferenceEqualityComparer.Instance);
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private readonly List<NLCPGDataFlowMethodMetrics> _dataFlowMethodMetrics = new();
    private IReadOnlyList<OperationRootPlan>? _operationRootPlans;
    private SyntaxNode? _operationRootPlanRoot;
    private SemanticModel? _operationRootPlanSemanticModel;
    private int _operationNodeCacheHitCount;
    private int _operationNodeCacheMissCount;
    private int _operationRootCacheHitCount;
    private int _operationRootCacheMissCount;
    private readonly Dictionary<string, long> _passElapsedMilliseconds = new(StringComparer.Ordinal);
    private readonly NLCPGBuilderOptions _options;
    private readonly IConcurrencyPool _concurrencyPool;

    public NLCPGFlowSummaryMetrics LastFlowSummaryMetrics { get; private set; } = NLCPGFlowSummaryMetrics.Empty;

    public NLCPGBuildMetrics LastBuildMetrics { get; private set; } = NLCPGBuildMetrics.Empty;

    private sealed record CapabilityBuildPlan(
        NLCPGCapability ResolvedCapabilities,
        bool EmitSyntaxTokens,
        bool EmitReferences,
        bool EmitTypeReferences,
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

        var sourceInput = NLCPGBuildContext.CreateSourceSemanticInput(source, filePath);
        var identityFactory = new StableNodeIdentityFactory();
        var preflightStopwatch = Stopwatch.StartNew();
        var preflightBuilder = new NLCPGBuilder(CreateAnchorDiscoveryOptions());
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateAnchorDiscovery(
            sourceInput.SemanticModel,
            sourceInput.Root,
            source,
            filePath,
            identityFactory,
            collector.Add));
        preflightStopwatch.Stop();

        var allocation = collector.CreateAllocation();
        var graph = Build(NLCPGBuildContext.Create(
          sourceInput.SemanticModel,
          sourceInput.Root,
          source,
          filePath,
          allocation,
          identityFactory));
        LastBuildMetrics = LastBuildMetrics with
        {
            AnchorDiscoveryAnchorCount = collector.Count,
            AnchorDiscoveryElapsedMilliseconds = preflightStopwatch.ElapsedMilliseconds,
            AnchorDiscoveryPassElapsedMilliseconds = CopyStageElapsedMilliseconds(
              preflightBuilder.LastBuildMetrics.PassElapsedMilliseconds),
            BuildInventoryMetrics = LastBuildMetrics.BuildInventoryMetrics! with
            {
                PreallocatedAnchorDiff = CpgBuildInventory.CompareAnchors(
                  collector.Anchors,
                  graph.Nodes),
            },
        };
        return graph;
    }

    // 复用外部提供的语义模型与语法根来构建 CPG。
    public NLCPGGraph BuildFromSemanticModel(SemanticModel semanticModel, SyntaxNode root, string source, string filePath)
    {
        if (!RequiresPreallocatedNodeIds())
        {
            return Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath));
        }

        var identityFactory = new StableNodeIdentityFactory();
        var preflightStopwatch = Stopwatch.StartNew();
        var preflightBuilder = new NLCPGBuilder(CreateAnchorDiscoveryOptions());
        var collector = new CpgStableAnchorCollector();
        _ = preflightBuilder.Build(NLCPGBuildContext.CreateAnchorDiscovery(
            semanticModel,
            root,
            source,
            filePath,
            identityFactory,
            collector.Add));
        preflightStopwatch.Stop();

        var allocation = collector.CreateAllocation();
        var graph = Build(NLCPGBuildContext.Create(semanticModel, root, source, filePath, allocation, identityFactory));
        LastBuildMetrics = LastBuildMetrics with
        {
            AnchorDiscoveryAnchorCount = collector.Count,
            AnchorDiscoveryElapsedMilliseconds = preflightStopwatch.ElapsedMilliseconds,
            AnchorDiscoveryPassElapsedMilliseconds = CopyStageElapsedMilliseconds(
              preflightBuilder.LastBuildMetrics.PassElapsedMilliseconds),
            BuildInventoryMetrics = LastBuildMetrics.BuildInventoryMetrics! with
            {
                PreallocatedAnchorDiff = CpgBuildInventory.CompareAnchors(
                  collector.Anchors,
                  graph.Nodes),
            },
        };
        return graph;
    }

    private NLCPGGraph Build(NLCPGBuildContext context)
    {
        var buildStopwatch = Stopwatch.StartNew();
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
        _operationNodesByOperation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
        _dataFlowMethodMetrics.Clear();
        _operationRootPlans = null;
        _operationRootPlanRoot = null;
        _operationRootPlanSemanticModel = null;
        _operationNodeCacheHitCount = 0;
        _operationNodeCacheMissCount = 0;
        _operationRootCacheHitCount = 0;
        _operationRootCacheMissCount = 0;
        _passElapsedMilliseconds.Clear();
        CpgShardBuildCoordinator? persistenceCoordinator = null;
        NLCPGPersistenceMetrics? persistenceMetrics = null;
        var persistenceHit = false;
        if (_options.Persistence is not null)
        {
            persistenceCoordinator = new CpgShardBuildCoordinator(_options.Persistence, _concurrencyPool);
            CpgBaseRestoreResult? restoredBase = null;
            var restoreStopwatch = Stopwatch.StartNew();
            MeasureStage(
              "PersistenceRestore",
              () => restoredBase = persistenceCoordinator
                .TryRestoreBaseAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult());
            restoreStopwatch.Stop();
            var restoreMetrics = persistenceCoordinator.LastRestoreMetrics;
            persistenceMetrics = NLCPGPersistenceMetrics.Empty with
            {
                RestoreAttempted = true,
                RestoreHit = restoredBase is not null,
                RestoreElapsedMilliseconds = restoreStopwatch.ElapsedMilliseconds,
                CatalogReadMilliseconds = restoreMetrics.CatalogReadMilliseconds,
                ShardReadMilliseconds = restoreMetrics.ShardReadMilliseconds,
                RestoredShardCount = restoreMetrics.RestoredShardCount,
                RestoredShardBytes = restoreMetrics.RestoredShardBytes,
                RestoreFactsElapsedMilliseconds = restoreMetrics.RestoreFactsElapsedMilliseconds,
                RestoreFactsAllocatedBytes = restoreMetrics.RestoreFactsAllocatedBytes,
            };
            _passElapsedMilliseconds["PersistenceRestoreFacts"] =
              restoreMetrics.RestoreFactsElapsedMilliseconds;
            if (restoredBase is not null)
            {
                var graphImportAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                MeasureStage(
                  "PersistenceGraphImport",
                  () => context.Graph.ImportMutableFacts(restoredBase.Facts.Nodes, restoredBase.Facts.Edges));
                persistenceMetrics = persistenceMetrics with
                {
                    RestoreGraphImportElapsedMilliseconds = _passElapsedMilliseconds["PersistenceGraphImport"],
                    RestoreGraphImportAllocatedBytes =
                      GC.GetTotalAllocatedBytes(precise: false) - graphImportAllocatedBefore,
                };
                restoredBase = null;
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
            MeasureStage(
              "Syntax",
              () => RunSyntaxPass(
                context,
                usePartitionedSyntaxPass,
                operationBuildStrategy.OperationRoots,
                buildPlan));

            if (buildPlan.RequiresMethodModel)
            {
                MeasureStage(
                  "MethodModel",
                  () => MethodDecorationPass.Instance.Run(this, context));

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

                MeasureStage(
                  "Operation",
                  () =>
                  {
                      RunPartitionedOperationPass(context, operationBuildStrategy.OperationRoots, streamingPublisher);
                      CompleteOperationBackedSyntaxTypes(context);
                  });

                if (streamingPublisher is not null)
                {
                    // 操作分片均已按源顺序写入后，补齐基础节点和跨分片邻接表，再一次性发布会话。
                    NLCPGPersistenceMetrics? streamingMetrics = null;
                    MeasureStage(
                      "StreamingBasePublish",
                      () => streamingMetrics = streamingPublisher
                        .CompleteBaseAsync(context, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult());
                    persistenceMetrics = MergePersistenceMetrics(persistenceMetrics, streamingMetrics!);
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
            MeasureStage("FreezeQueryIndex", () => context.Graph.FreezeQueryIndex());
            ReleaseTransientBuilderState();

            if (_options.Persistence is not null && !streamingPersistenceCompleted && !persistenceHit)
            {
                NLCPGPersistenceMetrics? writeMetrics = null;
                MeasureStage(
                  "PersistenceWrite",
                  () => writeMetrics = persistenceCoordinator!
                    .PersistAsync(context, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
                persistenceMetrics = MergePersistenceMetrics(persistenceMetrics, writeMetrics!);
                streamingPublisher = null;
            }

            if (persistenceMetrics is not null)
            {
                persistenceMetrics = persistenceMetrics with
                {
                    Provenance = CreatePersistenceProvenance(context, buildPlan),
                };
            }

            CpgBuildInventoryMetrics? buildInventoryMetrics = null;
            MeasureStage(
              "BuildInventoryAudit",
              () => buildInventoryMetrics = CpgBuildInventory.Create(context).Metrics);
            buildStopwatch.Stop();
            LastBuildMetrics = new NLCPGBuildMetrics(
              _operationNodeCacheHitCount,
              _operationNodeCacheMissCount,
              _operationRootCacheHitCount,
              _operationRootCacheMissCount,
              context.OperationInventory.Count,
              context.Graph.Nodes.Count,
              context.Graph.Edges.Count,
              buildStopwatch.ElapsedMilliseconds,
              PassElapsedMilliseconds: new Dictionary<string, long>(
                _passElapsedMilliseconds,
                StringComparer.Ordinal),
              PersistenceMetrics: persistenceMetrics,
              DataFlowMethodMetrics: _dataFlowMethodMetrics.ToArray(),
              BuildInventoryMetrics: buildInventoryMetrics);
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

    private NLCPGBuilderOptions CreateAnchorDiscoveryOptions()
    {
        var buildPlan = ResolveCapabilityBuildPlan();
        return _options with
        {
            Persistence = null,
            UsePreallocatedNodeIds = false,
            RequestedCapabilities = new[] { buildPlan.ResolvedCapabilities },
        };
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
        _operationNodesByOperation.Clear();
        _resolvedCallTargetsByInvocation.Clear();
        _resolvedCallTargetsByDispatchShape.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _partitionedSyntaxFacts.Clear();
        _declaredTypes.Clear();
        _operationRootPlans = null;
        _operationRootPlanRoot = null;
        _operationRootPlanSemanticModel = null;
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
            EmitSyntaxTokens: (resolved & NLCPGCapability.SyntaxToken) != 0,
            EmitReferences: (resolved & NLCPGCapability.Reference) != 0,
            EmitTypeReferences: (resolved & NLCPGCapability.TypeRef) != 0,
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

        MeasureStage(pass.Name, () => pass.Run(this, context));
    }

    private void MeasureStage(string stageName, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            _passElapsedMilliseconds[stageName] = stopwatch.ElapsedMilliseconds;
        }
    }

    private static NLCPGPersistenceMetrics MergePersistenceMetrics(
      NLCPGPersistenceMetrics? restoreMetrics,
      NLCPGPersistenceMetrics persistMetrics)
    {
        return persistMetrics with
        {
            RestoreAttempted = restoreMetrics?.RestoreAttempted ?? false,
            RestoreHit = restoreMetrics?.RestoreHit ?? false,
            RestoreElapsedMilliseconds = restoreMetrics?.RestoreElapsedMilliseconds ?? 0,
            CatalogReadMilliseconds = restoreMetrics?.CatalogReadMilliseconds ?? 0,
            ShardReadMilliseconds = restoreMetrics?.ShardReadMilliseconds ?? 0,
            RestoredShardCount = restoreMetrics?.RestoredShardCount ?? 0,
            RestoredShardBytes = restoreMetrics?.RestoredShardBytes ?? 0,
            RestoreFactsElapsedMilliseconds = restoreMetrics?.RestoreFactsElapsedMilliseconds ?? 0,
            RestoreFactsAllocatedBytes = restoreMetrics?.RestoreFactsAllocatedBytes ?? 0,
            RestoreGraphImportElapsedMilliseconds = restoreMetrics?.RestoreGraphImportElapsedMilliseconds ?? 0,
            RestoreGraphImportAllocatedBytes = restoreMetrics?.RestoreGraphImportAllocatedBytes ?? 0,
        };
    }

    private CpgPersistenceProvenance? CreatePersistenceProvenance(
      NLCPGBuildContext context,
      CapabilityBuildPlan buildPlan)
    {
        if (_options.Persistence is not { } persistence)
        {
            return null;
        }

        var dataFlowOptions = _options.EffectiveDataFlowOptions;
        var interproceduralOptions = _options.EffectiveInterproceduralDataFlowOptions;
        var flowSummaryOptions = _options.EffectiveFlowSummaryOptions;
        var fingerprintInput = string.Join(
          "\u001F",
          ((int)buildPlan.ResolvedCapabilities).ToString(CultureInfo.InvariantCulture),
          _options.EnableReferencedSymbolTypeReuse ? "1" : "0",
          _options.EnableOperationBackedSyntaxTypes ? "1" : "0",
          _options.SyntaxPassMode.ToString(),
          dataFlowOptions.MaxDefinitionsPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.MaxFlowNodesPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.MaxCandidateEdgesPerMethod.ToString(CultureInfo.InvariantCulture),
          dataFlowOptions.OverflowBehavior.ToString(),
          interproceduralOptions.MaxCallTargetsPerSite.ToString(CultureInfo.InvariantCulture),
          interproceduralOptions.MaxBoundaryEdgesPerMethod.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerCallSite.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerMethod.ToString(CultureInfo.InvariantCulture),
          flowSummaryOptions.MaxMappingsPerBuild.ToString(CultureInfo.InvariantCulture));
        var compilerIdentity = typeof(CSharpCompilation).Assembly.FullName ??
          typeof(CSharpCompilation).Assembly.GetName().Name ??
          "unknown";

        return new CpgPersistenceProvenance(
          HashText(context.Source),
          persistence.ProfileHash,
          persistence.SchemaVersion,
          compilerIdentity,
          HashText(fingerprintInput));
    }

    private static string HashText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, long>? CopyStageElapsedMilliseconds(
      IReadOnlyDictionary<string, long>? elapsedMilliseconds)
    {
        return elapsedMilliseconds is null
          ? null
          : new Dictionary<string, long>(elapsedMilliseconds, StringComparer.Ordinal);
    }

    private static void AddPendingEdgeIndex<TKey>(
      Dictionary<TKey, List<NLCPGGraph.PendingEdge>> index,
      TKey key,
      NLCPGGraph.PendingEdge edge)
      where TKey : notnull
    {
        if (!index.TryGetValue(key, out var edges))
        {
            edges = new List<NLCPGGraph.PendingEdge>();
            index[key] = edges;
        }

        edges.Add(edge);
    }

    internal void RunInterproceduralDataFlowPass(NLCPGBuildContext context)
    {
        var graph = context.Graph;
        var callTargetEdgesBySource = new Dictionary<NLCPGNode, List<NLCPGGraph.PendingEdge>>(
          ReferenceEqualityComparer.Instance);
        var dataFlowEdgesByTarget = new Dictionary<NLCPGNode, List<NLCPGGraph.PendingEdge>>(
          ReferenceEqualityComparer.Instance);
        var argumentDataFlowEdgesByMethod = new Dictionary<string, List<NLCPGGraph.PendingEdge>>(
          StringComparer.Ordinal);
        var returnDataFlowEdgesByMethod = new Dictionary<string, List<NLCPGGraph.PendingEdge>>(
          StringComparer.Ordinal);
        foreach (var edge in graph.PendingEdges)
        {
            if (edge.Kind == NLCPGEdgeKind.CallTargets)
            {
                AddPendingEdgeIndex(callTargetEdgesBySource, edge.SourceNode, edge);
                continue;
            }

            if (edge.Kind != NLCPGEdgeKind.DataFlow)
            {
                continue;
            }

            AddPendingEdgeIndex(dataFlowEdgesByTarget, edge.TargetNode, edge);
            if (_methodOwnerSymbolKeysByBoundaryNode.TryGetValue(edge.TargetNode, out var methodSymbolKey))
            {
                if (edge.TargetNode.Kind == NLCPGNodeKind.MethodParameter)
                {
                    AddPendingEdgeIndex(argumentDataFlowEdgesByMethod, methodSymbolKey, edge);
                }
                else if (edge.TargetNode.Kind == NLCPGNodeKind.MethodReturn)
                {
                    AddPendingEdgeIndex(returnDataFlowEdgesByMethod, methodSymbolKey, edge);
                }
            }
        }
        var plans = new List<InterproceduralDataFlowPlan>();
        var summaryBudget = new FlowSummaryBudget(_options.EffectiveFlowSummaryOptions);
        var recordedReturnMethods = new HashSet<string>(StringComparer.Ordinal);
        var options = _options.EffectiveInterproceduralDataFlowOptions;
        var methodBoundaryNodes = graph.Nodes
          .Where(node => node.Kind is NLCPGNodeKind.MethodParameter or NLCPGNodeKind.MethodReturn)
          .ToArray();
        var internalMethodSymbolKeys = methodBoundaryNodes
          .Select(node => _methodOwnerSymbolKeysByBoundaryNode.TryGetValue(node, out var key) ? key : null)
          .Where(key => key is not null)
          .Select(key => key!)
          .ToHashSet(StringComparer.Ordinal);

        foreach (var callSite in graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.CallSite)
          .OrderBy(node => node.FullName, StringComparer.Ordinal)
          .ThenBy(node => node.SpanStart)
          .ThenBy(NodeSortKey, StringComparer.Ordinal))
        {
            var targets = callTargetEdgesBySource.TryGetValue(callSite, out var callTargetEdges)
              ? callTargetEdges
              .Select(edge => edge.TargetNode)
              .Distinct()
              .OrderBy(target => target.FullName, StringComparer.Ordinal)
              .ThenBy(NodeSortKey, StringComparer.Ordinal)
              .ToArray()
              : Array.Empty<NLCPGNode>();
            if (targets.Length == 0)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("UnresolvedTarget");
                continue;
            }

            if (targets.Length > options.MaxCallTargetsPerSite)
            {
                summaryBudget.RecordCut("AmbiguousTarget");
                continue;
            }

            var targetMethodNode = targets[0];
            if (!TryGetSymbolKey(targetMethodNode, out var targetMethodSymbolKey))
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("UnresolvedTarget");
                continue;
            }

            var hasInternalBoundary = internalMethodSymbolKeys.Contains(targetMethodSymbolKey);
            if (!hasInternalBoundary)
            {
                AddExternalSummaryMappings(callSite, graph, summaryBudget);
                summaryBudget.RecordCut("ExternalTarget");
                continue;
            }

            IReadOnlyList<NLCPGGraph.PendingEdge> argumentEdges = argumentDataFlowEdgesByMethod.TryGetValue(
              targetMethodSymbolKey,
              out var indexedArgumentEdges)
              ? indexedArgumentEdges
              : Array.Empty<NLCPGGraph.PendingEdge>();
            IReadOnlyList<NLCPGGraph.PendingEdge> returnToCallEdges = dataFlowEdgesByTarget.TryGetValue(
              callSite,
              out var indexedReturnToCallEdges)
              ? indexedReturnToCallEdges
              : Array.Empty<NLCPGGraph.PendingEdge>();
            var callSitePlans = argumentEdges
              .Select(edge => new InterproceduralDataFlowPlan(
                callSite,
                targetMethodNode,
                edge.SourceNode,
                edge.TargetNode,
                NLCPGInterproceduralBridgeKind.ArgumentToParameter,
                ParseArgumentOrdinal(edge.TargetNode)))
              .Concat(returnToCallEdges
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
                if (returnDataFlowEdgesByMethod.TryGetValue(
                  targetMethodSymbolKey,
                  out var indexedReturnDataFlowEdges))
                {
                    callSitePlans.AddRange(indexedReturnDataFlowEdges
                  .Select(edge => new InterproceduralDataFlowPlan(
                    callSite,
                    targetMethodNode,
                    edge.SourceNode,
                    edge.TargetNode,
                    NLCPGInterproceduralBridgeKind.ReturnToMethodReturn)));
                }
            }

            if (callSitePlans.Count == 0)
            {
                summaryBudget.RecordCut("MissingIntraFacts");
                continue;
            }

            plans.AddRange(callSitePlans.Take(options.MaxBoundaryEdgesPerMethod));
            if (callSitePlans.Count > options.MaxBoundaryEdgesPerMethod)
            {
                summaryBudget.RecordCut("BoundaryEdgeBudget");
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
            budget.BeginCallSite();
            budget.RecordRejected(ResolvedCallFlowStatus.Unknown);
            budget.RecordCut("MissingResolver");
            return;
        }

        var invocation = _callSiteNodesByInvocation
          .FirstOrDefault(pair => pair.Value == callSite).Key;
        if (invocation is null)
        {
            budget.RecordCut("MissingInvocationOperation");
            return;
        }

        var context = BuildPendingNodeCallSiteContext(callSite);
        budget.BeginCallSite();
        var resolvedMappings = _options.CallFlowResolver.ResolveAll(invocation).ToArray();
        if (resolvedMappings.Length == 0)
        {
            budget.RecordRejected(ResolvedCallFlowStatus.Unknown);
            budget.RecordCut("MissingSummary");
            return;
        }

        foreach (var resolved in resolvedMappings.Where(result =>
          !result.IsResolved ||
          result.Mapping is null ||
          result.Mapping.Kind == FlowSummaryMappingKind.Block))
        {
            budget.RecordRejected(resolved);
        }

        foreach (var resolved in resolvedMappings
          .Where(result =>
            result.IsResolved &&
            result.Mapping is not null &&
            result.Mapping.Kind != FlowSummaryMappingKind.Block)
          .OrderBy(result => result.MethodKey.StableKey, StringComparer.Ordinal)
          .ThenBy(result => result.Mapping!.Source.Kind)
          .ThenBy(result => result.Mapping!.Source.ParameterOrdinal)
          .ThenBy(result => result.Mapping!.Target.Kind)
          .ThenBy(result => result.Mapping!.Target.ParameterOrdinal)
          .ThenBy(result => result.Mapping!.Kind)
          .DistinctBy(result => new
          {
              result.MethodKey.StableKey,
              result.Resolution,
              Mapping = result.Mapping!
          }))
        {
            var source = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping!.Source, graph);
            var target = ResolveInvocationEndpointNode(invocation, callSite, resolved.Mapping.Target, graph);
            if (source is null || target is null)
            {
                budget.RejectedEndpoints += 1;
                budget.RecordCut("SummaryEndpointUnavailable");
                continue;
            }

            if (!budget.TryConsume(resolved.MethodKey.StableKey))
            {
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
        if (argument is null || !IsCompatibleArgumentEndpoint(argument, endpoint))
        {
            return null;
        }

        return GetOrCreateOperationNode(argument.Value, graph);
    }

    private static bool IsCompatibleArgumentEndpoint(
      IArgumentOperation argument,
      FlowSummaryEndpoint endpoint)
    {
        if (argument.Parameter is null)
        {
            return false;
        }

        return endpoint.Kind switch
        {
            FlowSummaryEndpointKind.Parameter => argument.Parameter.RefKind == RefKind.None,
            FlowSummaryEndpointKind.RefParameter => argument.Parameter.RefKind == RefKind.Ref,
            FlowSummaryEndpointKind.OutParameter => argument.Parameter.RefKind == RefKind.Out,
            _ => false,
        };
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

    private sealed class FlowSummaryBudget
    {
        private readonly NLCPGFlowSummaryOptions _options;
        private readonly Dictionary<string, int> _methodCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _cutReasons = new(StringComparer.Ordinal);
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

        internal void RecordCut(string reason)
        {
            _cutReasons[reason] = _cutReasons.TryGetValue(reason, out var count)
                ? count + 1
                : 1;
        }

        internal bool TryConsume(string methodKey)
        {
            var methodCount = _methodCounts.GetValueOrDefault(methodKey);
            if (_callSiteCount >= _options.MaxMappingsPerCallSite ||
                ResolvedMappings >= _options.MaxMappingsPerBuild ||
                methodCount >= _options.MaxMappingsPerMethod)
            {
                TruncatedMappings += 1;
                if (_callSiteCount >= _options.MaxMappingsPerCallSite)
                {
                    RecordCut("SummaryMappingsPerCallSite");
                }

                if (ResolvedMappings >= _options.MaxMappingsPerBuild)
                {
                    RecordCut("SummaryMappingsPerBuild");
                }

                if (methodCount >= _options.MaxMappingsPerMethod)
                {
                    RecordCut("SummaryMappingsPerMethod");
                }

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

        internal void RecordRejected(ResolvedCallFlow resolved)
        {
            var status = resolved.Status;
            if (status == ResolvedCallFlowStatus.Resolved && resolved.Mapping is null)
            {
                status = ResolvedCallFlowStatus.SignatureMismatch;
            }

            if (resolved.Mapping?.Kind == FlowSummaryMappingKind.Block)
            {
                status = ResolvedCallFlowStatus.Blocked;
            }

            RecordRejected(status);
        }

        internal void RecordRejected(ResolvedCallFlowStatus status)
        {
            switch (status)
            {
                case ResolvedCallFlowStatus.Unknown:
                    UnknownCalls += 1;
                    RecordCut("SummaryUnknown");
                    break;
                case ResolvedCallFlowStatus.SignatureMismatch:
                    SignatureMismatches += 1;
                    RecordCut("SummarySignatureMismatch");
                    break;
                case ResolvedCallFlowStatus.Blocked:
                    BlockedMappings += 1;
                    RecordCut("SummaryBlocked");
                    break;
            }
        }

        internal NLCPGFlowSummaryMetrics ToMetrics() => new(
          ResolvedMappings,
          UnknownCalls,
          SignatureMismatches,
          BlockedMappings,
          RejectedEndpoints,
          TruncatedMappings)
        {
            CutReasons = _cutReasons
              .OrderBy(entry => entry.Key, StringComparer.Ordinal)
              .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        };
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
        if (_operationNodesByOperation.TryGetValue(operation, out var cachedNode))
        {
            _operationNodeCacheHitCount += 1;
            return cachedNode;
        }

        _operationNodeCacheMissCount += 1;
        var kind = MapOperationKind(operation);
        var operationNode = graph.AddNode(new NLCPGNode(
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
        _operationNodesByOperation[operation] = operationNode;
        return operationNode;
    }

    internal IOperation? GetOperationRoot(NLCPGBuildContext context, SyntaxNode bodySyntax)
    {
        if (context.TryGetOperationRoot(bodySyntax, out var cachedOperation))
        {
            _operationRootCacheHitCount += 1;
            return cachedOperation;
        }

        _operationRootCacheMissCount += 1;
        var operation = context.SemanticModel.GetOperation(bodySyntax);
        if (operation is not null)
        {
            context.RegisterOperationRoot(operation);
        }

        return operation;
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
