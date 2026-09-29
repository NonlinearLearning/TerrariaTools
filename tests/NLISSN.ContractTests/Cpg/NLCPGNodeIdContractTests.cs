using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class NLCPGNodeIdContractTests
{
  [Fact]
  public void GraphNodeAndEdgeModels_AreReadonlyValueTypesWithoutTextCarrier()
  {
    Assert.True(typeof(NLCPGNode).IsValueType);
    Assert.True(typeof(NLCPGEdge).IsValueType);
    Assert.Null(typeof(NLCPGNode).GetProperty("Text"));
    Assert.Null(typeof(NLCPGNode).GetField("Text"));
  }

  [Fact]
  public void DeterministicNodeIdTable_Create_AssignsStableIdsIndependentOfInputOrder()
  {
    var first = new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      FilePathId: 2,
      SpanStart: 10,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);
    var second = new StableNodeAnchor(
      NLCPGNodeKind.CallSite,
      FilePathId: 1,
      SpanStart: 8,
      SpanEnd: 8,
      StableNodeRole.CallSite,
      Ordinal: 0,
      ExtraKeyId: 3);
    var third = new StableNodeAnchor(
      NLCPGNodeKind.Method,
      FilePathId: 1,
      SpanStart: 0,
      SpanEnd: 20,
      StableNodeRole.Method,
      Ordinal: 0,
      ExtraKeyId: 2);

    var ordered = DeterministicNodeIdTable.Create(new[] { first, second, third });
    var shuffled = DeterministicNodeIdTable.Create(new[] { third, first, second });
    var expectedOrder = new[] { first, second, third }
      .OrderBy(anchor => anchor.Kind)
      .ThenBy(anchor => anchor.FilePathId)
      .ThenBy(anchor => anchor.SpanStart)
      .ThenBy(anchor => anchor.SpanEnd)
      .ThenBy(anchor => anchor.Role)
      .ThenBy(anchor => anchor.Ordinal)
      .ThenBy(anchor => anchor.ExtraKeyId)
      .ToArray();

    Assert.Equal(ordered.Snapshot(), shuffled.Snapshot());
    for (var index = 0; index < expectedOrder.Length; index += 1)
    {
      Assert.Equal(new NodeId((uint)index + 1), ordered.Snapshot()[expectedOrder[index]]);
    }
  }

  [Fact]
  public void DeterministicNodeIdTable_Create_ExposesReadOnlyPreallocatedIds()
  {
    var firstByStableOrder = new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: 0,
      SpanEnd: 20,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 2);
    var middleByStableOrder = new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: 8,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 3);
    var lastByStableOrder = new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      FilePathId: 2,
      SpanStart: 10,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);

    var allocation = DeterministicNodeIdTable.Create(new[]
    {
      lastByStableOrder,
      firstByStableOrder,
      middleByStableOrder,
    });

    Assert.Equal(new NodeId(1), allocation.GetRequiredId(firstByStableOrder));
    Assert.Equal(new NodeId(2), allocation.GetRequiredId(middleByStableOrder));
    Assert.Equal(new NodeId(3), allocation.GetRequiredId(lastByStableOrder));
    Assert.True(allocation.Contains(middleByStableOrder));
  }

  [Fact]
  public void FreezeQueryIndex_WithPreallocatedIds_PreservesSuppliedNodeIds()
  {
    var firstAnchor = CreateSyntaxAnchor(spanStart: 0, spanEnd: 4);
    var secondAnchor = CreateSyntaxAnchor(spanStart: 5, spanEnd: 9);
    var allocation = DeterministicNodeIdTable.Create(new[] { secondAnchor, firstAnchor });
    var graph = new NLCPGGraph(allocation);

    var second = graph.AddNode(CreateSyntaxNode(secondAnchor, "second"), stableAnchor: secondAnchor);
    var first = graph.AddNode(CreateSyntaxNode(firstAnchor, "first"), stableAnchor: firstAnchor);
    graph.AddEdge(first, second, NLCPGEdgeKind.SyntaxChild);
    graph.FreezeQueryIndex();

    Assert.Equal(allocation.GetRequiredId(firstAnchor), Assert.Single(graph.Nodes, node => graph.ResolveName(node) == "first").NodeId);
    Assert.Equal(allocation.GetRequiredId(secondAnchor), Assert.Single(graph.Nodes, node => graph.ResolveName(node) == "second").NodeId);
    Assert.Equal(
      allocation.GetRequiredId(firstAnchor),
      Assert.Single(graph.Edges).SourceNodeId);
  }

  [Fact]
  public void AddNode_WithPreallocatedIds_RejectsUnknownAnchorBeforeGraphMutation()
  {
    var allocatedAnchor = CreateSyntaxAnchor(spanStart: 0, spanEnd: 4);
    var graph = new NLCPGGraph(DeterministicNodeIdTable.Create(new[] { allocatedAnchor }));
    var unallocatedAnchor = CreateSyntaxAnchor(spanStart: 5, spanEnd: 9);
    var unallocatedNode = CreateSyntaxNode(unallocatedAnchor, "unallocated");

    Assert.Throws<InvalidOperationException>(() => graph.AddNode(unallocatedNode, stableAnchor: unallocatedAnchor));
    Assert.Empty(graph.Nodes);
  }

  [Fact]
  public void BuildFromSource_WithCompatibilityPreallocatedIds_MatchesLegacyAcrossDegreesOfParallelism()
  {
    var baseline = BuildNodeAndEdgeSnapshot(CreateBuilder(maxDegreeOfParallelism: 1));

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new NLCPGBuilder(CreateBuilderOptions(maxDegreeOfParallelism) with
      {
        UsePreallocatedNodeIds = true,
      });
      var candidate = BuildNodeAndEdgeSnapshot(builder);

      Assert.Equal(baseline, candidate);
    }
  }

  [Fact]
  public void StreamingDescriptorContracts_ContainOnlyStableGraphData()
  {
    var assembly = typeof(NLCPGBuilder).Assembly;
    var nodeDescriptor = assembly.GetType("NLCPG.Builder.Streaming.CpgNodeDescriptor");
    var edgeCandidate = assembly.GetType("NLCPG.Builder.Streaming.CpgEdgeCandidate");
    Assert.NotNull(nodeDescriptor);
    Assert.NotNull(edgeCandidate);

    Assert.Contains(nodeDescriptor!.GetProperties(), property => property.Name == "Anchor");
    Assert.Contains(nodeDescriptor.GetProperties(), property => property.Name == "DispatchKind");
    Assert.Contains(nodeDescriptor.GetProperties(), property => property.Name == "TypeFullNameId");
    Assert.Contains(edgeCandidate!.GetProperties(), property => property.Name == "SourceAnchor");
    Assert.Contains(edgeCandidate.GetProperties(), property => property.Name == "TargetAnchor");
    Assert.All(
      nodeDescriptor.GetProperties().Concat(edgeCandidate.GetProperties()),
      property => Assert.False(
        property.PropertyType.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true ||
        property.PropertyType == typeof(NLCPGGraph)));
  }

  [Fact]
  public void InternalCpgDomainCarriers_AreNonPublicValueTypes()
  {
    var assembly = typeof(NLCPGBuilder).Assembly;
    var typeNames = new[]
    {
      "NLCPG.Builder.Streaming.CpgFragmentOwnership",
      "NLCPG.Builder.Streaming.CrossShardSummary",
      "NLCPG.Builder.Streaming.SkeletonShardPublisher+PendingCandidateBuckets",
      "NLCPG.Builder.Streaming.SkeletonShardPublisher+BoundaryBucket",
      "NLCPG.Model.NLCPGGraph+PendingEdge",
      "NLCPG.Model.NLCPGGraph+MutableGraphFacts",
      "NLCPG.Persistence.CpgFrozenShardGraphFacts",
      "NLCPG.Builder.Passes.InterproceduralPlanRef",
      "NLCPG.Builder.NLCPGBuilder+DefinitionFact",
      "NLCPG.Analysis.CpgRelationQueryService+QueryKey",
      "NLCPG.Analysis.NLCPGSliceQuery+QueryKey",
      "NLCPG.Persistence.Sqlite.SqliteCpgShardCatalog+RoutingIndexCacheKey",
      "NLCPG.Builder.CpgRestoreMetrics",
      "NLCPG.Builder.CpgBaseRestoreResult",
      "NLCPG.Builder.CpgShardExportRequest",
    };

    foreach (var typeName in typeNames)
    {
      var type = assembly.GetType(typeName, throwOnError: true)!;
      Assert.True(type.IsValueType, $"{typeName} must be a value type.");
      Assert.False(type.IsPublic || type.IsNestedPublic, $"{typeName} must remain non-public.");
    }
  }

  [Fact]
  public void InternalAnalysisDomainCarriers_AreNonPublicValueTypes()
  {
    var assembly = typeof(NLISSN.Core.Propagation.PropagationFactKey).Assembly;
    var typeNames = new[]
    {
      "NLISSN.Core.Propagation.PropagationFactKey",
      "NLISSN.Core.Propagation.PropagationFixedPointExecutor+PropagationSourceFact",
      "NLISSN.Core.Propagation.PropagationFixedPointExecutor+PropagationWorkItemPriority",
      "NLISSN.Core.Decision.AnalysisEvidenceCollector+PendingNode",
      "NLISSN.Core.Decision.AnalysisEvidenceCollector+PendingEdge",
    };

    foreach (var typeName in typeNames)
    {
      var type = assembly.GetType(typeName, throwOnError: true)!;
      Assert.True(type.IsValueType, $"{typeName} must be a value type.");
      Assert.False(type.IsPublic || type.IsNestedPublic, $"{typeName} must remain non-public.");
    }
  }

  [Fact]
  public void OperationFragmentFacts_ExposeOnlyImmutableDescriptorCandidates()
  {
    var assembly = typeof(NLCPGBuilder).Assembly;
    var factsType = assembly.GetType("NLCPG.Builder.Streaming.OperationFragmentFacts");
    Assert.NotNull(factsType);

    var properties = factsType!.GetProperties(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.Public |
      System.Reflection.BindingFlags.NonPublic);
    var fields = factsType.GetFields(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.Public |
      System.Reflection.BindingFlags.NonPublic);

    Assert.Contains(properties, property => property.Name == "NodeDescriptors");
    Assert.Contains(properties, property => property.Name == "EdgeCandidates");
    Assert.DoesNotContain(properties, property => property.Name == "OperationRecords");
    Assert.All(
      properties.Cast<System.Reflection.MemberInfo>().Concat(fields),
      member => Assert.False(ContainsRoslynOrGraphReference(GetMemberType(member))));
  }

  [Fact]
  public void NLCPGBuilder_DoesNotRetainGlobalOperationToNodeCaches()
  {
    var fields = typeof(NLCPGBuilder).GetFields(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.NonPublic);

    Assert.DoesNotContain(fields, field => field.Name == "_operationNodes");
    Assert.DoesNotContain(fields, field => field.Name == "_operationOwningMethods");
  }

  [Fact]
  public void LocalFlowCandidateSet_ContainsOnlyStableEdgeCandidates()
  {
    var localFlowCandidateSet = typeof(NLCPGBuilder).Assembly.GetType(
      "NLCPG.Builder.Streaming.LocalFlowCandidateSet");
    Assert.NotNull(localFlowCandidateSet);

    var properties = localFlowCandidateSet!.GetProperties(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.Public |
      System.Reflection.BindingFlags.NonPublic);
    Assert.Contains(properties, property => property.Name == "EdgeCandidates");
    Assert.All(properties, property => Assert.False(ContainsRoslynOrGraphReference(property.PropertyType)));
  }

  [Fact]
  public void StreamingWriters_DoNotRetainWriteOnlyPublicationLogs()
  {
    var assembly = typeof(NLCPGBuilder).Assembly;
    var publisher = assembly.GetType("NLCPG.Builder.Streaming.SkeletonShardPublisher");
    Assert.NotNull(publisher);
    var session = assembly.GetType("NLCPG.Builder.CpgShardBuildSession");
    Assert.NotNull(session);
    var writer = assembly.GetType("NLCPG.Persistence.Sqlite.CpgCatalogBatchWriter");
    Assert.NotNull(writer);

    // 这些字段只被写入、从不被读取，且随构建规模无界增长。
    Assert.DoesNotContain(DeclaredFieldNames(publisher!), name => name == "_publishedOrders");
    Assert.DoesNotContain(DeclaredFieldNames(publisher!), name => name == "_publishedKinds");
    Assert.DoesNotContain(DeclaredFieldNames(session!), name => name == "_stagedLocations");
    Assert.DoesNotContain(DeclaredFieldNames(session!), name => name == "_storeLockWaitMilliseconds");
    Assert.DoesNotContain(DeclaredFieldNames(writer!), name => name == "_maxQueueDepth");
  }

  [Fact]
  public void CpgShardBuildCoordinator_HasNoDeadSpanHelper()
  {
    var coordinator = typeof(NLCPGBuilder).Assembly.GetType("NLCPG.Builder.CpgShardBuildCoordinator");
    Assert.NotNull(coordinator);

    Assert.DoesNotContain(
      coordinator!.GetMethods(
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.DeclaredOnly),
      method => method.Name == "IsInside");
  }

  private static IEnumerable<string> DeclaredFieldNames(Type type)
  {
    return type.GetFields(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.Static |
      System.Reflection.BindingFlags.NonPublic |
      System.Reflection.BindingFlags.Public |
      System.Reflection.BindingFlags.DeclaredOnly)
      .Select(field => field.Name);
  }

  [Fact]
  public void StableNodeIdentityFactory_ReusesStableAnchorAcrossGraphLifetimes()
  {
    var identityFactory = new StableNodeIdentityFactory();
    var first = identityFactory.GetStableAnchor(new NLCPGNode(
      NLCPGNodeKind.Method,
      NameId: 1,
      FilePathId: 2,
      SpanStart: 0,
      SpanEnd: 10));
    var second = identityFactory.GetStableAnchor(new NLCPGNode(
      NLCPGNodeKind.Method,
      NameId: 1,
      FilePathId: 2,
      SpanStart: 0,
      SpanEnd: 10));

    Assert.Equal(first, second);
  }

  [Fact]
  public void AddNodeAndEdge_BackfillsNodeIdsWithoutChangingDisplayFields()
  {
    var graph = new NLCPGGraph();
    var source = new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source");
    var sink = new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "sink");

    var materializedSource = graph.AddNode(source);
    var materializedSink = graph.AddNode(sink);
    graph.AddEdge(
      materializedSource,
      materializedSink,
      NLCPGEdgeKind.DataFlow,
      contextId: new NLCPGContextId("flow"));
    graph.FreezeQueryIndex();

    var edge = Assert.Single(graph.Edges);
    materializedSource = Assert.Single(graph.Nodes, node => graph.ResolveName(node) == "source");
    materializedSink = Assert.Single(graph.Nodes, node => graph.ResolveName(node) == "sink");
    Assert.Equal("source", graph.ResolveName(materializedSource));
    Assert.Equal("sink", graph.ResolveName(materializedSink));
    Assert.Equal(new NodeId(1), materializedSource.NodeId);
    Assert.Equal(new NodeId(2), materializedSink.NodeId);
    Assert.NotNull(materializedSource.StableAnchor);
    Assert.NotNull(materializedSink.StableAnchor);
    Assert.Equal(materializedSource.NodeId!.Value, edge.SourceNodeId);
    Assert.Equal(materializedSink.NodeId!.Value, edge.TargetNodeId);
    Assert.Equal(materializedSource, graph.GetNode(materializedSource.NodeId!.Value));
    Assert.Equal("source", graph.ResolveName(graph.GetNode(materializedSource.NodeId.Value)!.Value));
  }

  [Fact]
  public void BuildFromSource_RepeatedBuilds_PreserveLegacyToNodeIdMapping()
  {
    var baseline = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));

    for (var iteration = 0; iteration < 3; iteration += 1)
    {
      var candidate = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));
      Assert.Equal(baseline, candidate);
    }
  }

  [Fact]
  public void BuildFromSource_DifferentDegreesOfParallelism_PreserveLegacyToNodeIdMapping()
  {
    var baseline = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism: 1));

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var candidate = BuildStableNodeKeyToNodeIdMap(CreateBuilder(maxDegreeOfParallelism));
      Assert.Equal(baseline, candidate);
    }
  }

  private static Dictionary<string, uint> BuildStableNodeKeyToNodeIdMap(NLCPGBuilder builder)
  {
    var graph = builder.BuildFromSource(
      """
      namespace Demo;

      public sealed class Sample
      {
        private int _offset;

        public int Run(int seed)
        {
          var local = seed + _offset;
          if (local > 0)
          {
            return Helper(local);
          }

          return local - 1;
        }

        private int Helper(int value) => value + 1;
      }
      """,
      "nodeid-stability.cs");

    return graph.Nodes
      .OrderBy(node => BuildNodeContractKey(graph, node), StringComparer.Ordinal)
      .ToDictionary(
        node => BuildNodeContractKey(graph, node),
        node => Assert.NotNull(node.NodeId).Value,
        StringComparer.Ordinal);
  }

  private static Type GetMemberType(System.Reflection.MemberInfo member)
  {
    return member switch
    {
      System.Reflection.PropertyInfo property => property.PropertyType,
      System.Reflection.FieldInfo field => field.FieldType,
      _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };
  }

  private static bool ContainsRoslynOrGraphReference(Type type)
  {
    if (type.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true ||
        type == typeof(NLCPGGraph))
    {
      return true;
    }

    return type.IsArray
      ? ContainsRoslynOrGraphReference(type.GetElementType()!)
      : type.IsGenericType && type.GenericTypeArguments.Any(ContainsRoslynOrGraphReference);
  }

  private static string BuildNodeContractKey(NLCPGGraph graph, NLCPGNode node)
  {
    return string.Join(
      "|",
      node.Kind,
      graph.ResolveDisplayKind(node),
      graph.ResolveName(node),
      graph.ResolveFullName(node),
      graph.ResolveSignature(node),
      graph.ResolveFilePath(node),
      node.SpanStart,
      node.SpanEnd,
      node.IsImplicit,
      node.StableAnchor?.Role,
      node.StableAnchor?.Ordinal,
      node.StableAnchor?.ExtraKeyId);
  }

  private static NLCPGBuilder CreateBuilder(int maxDegreeOfParallelism)
  {
    return new NLCPGBuilder(CreateBuilderOptions(maxDegreeOfParallelism));
  }

  private static NLCPGBuilderOptions CreateBuilderOptions(int maxDegreeOfParallelism)
  {
    return new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1,
      SyntaxPassMode: NLCPGSyntaxPassMode.Partitioned,
      SyntaxLargeFileLineThreshold: 1);
  }

  private static string BuildNodeAndEdgeSnapshot(bool usePreallocatedNodeIds, int maxDegreeOfParallelism)
  {
    return BuildNodeAndEdgeSnapshot(new NLCPGBuilder(CreateBuilderOptions(maxDegreeOfParallelism) with
    {
      UsePreallocatedNodeIds = usePreallocatedNodeIds,
    }));
  }

  private static string BuildNodeAndEdgeSnapshot(NLCPGBuilder owner)
  {
    var graph = owner.BuildFromSource(
      """
      namespace Demo;

      public sealed class Sample
      {
        private int _offset;

        public int Run(int seed)
        {
          var value = seed + _offset;
          while (value > 0)
          {
            value -= 1;
          }

          return Helper(value);
        }

        private int Helper(int value) => value + 1;
      }
      """,
      "preallocated-nodeids.cs");

    var nodeLines = graph.Nodes
      .OrderBy(node => node.NodeId)
      .Select(node => $"N|{node.NodeId}|{node.StableAnchor}");
    var edgeLines = graph.Edges
      .OrderBy(edge => edge.SourceNodeId)
      .ThenBy(edge => edge.TargetNodeId)
      .ThenBy(edge => edge.Kind)
      .Select(edge => $"E|{edge.SourceNodeId}|{edge.TargetNodeId}|{edge.Kind}|{edge.StructuredLabel}|{edge.ContextId}");
    return string.Join(Environment.NewLine, nodeLines.Concat(edgeLines));
  }

  private static StableNodeAnchor CreateSyntaxAnchor(int spanStart, int spanEnd)
  {
    return new StableNodeAnchor(
      NLCPGNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: spanStart,
      SpanEnd: spanEnd,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);
  }

  private static NLCPGNodeDraft CreateSyntaxNode(StableNodeAnchor anchor, string name)
  {
    return new NLCPGNodeDraft(NLCPGNodeKind.SyntaxNode, Name: name);
  }
}
