using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RoslynCpgNodeIdContractTests
{
  [Fact]
  public void DeterministicNodeIdTable_Create_AssignsStableIdsIndependentOfInputOrder()
  {
    var first = new StableNodeAnchor(
      RoslynCpgNodeKind.SyntaxNode,
      FilePathId: 2,
      SpanStart: 10,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);
    var second = new StableNodeAnchor(
      RoslynCpgNodeKind.CallSite,
      FilePathId: 1,
      SpanStart: 8,
      SpanEnd: 8,
      StableNodeRole.CallSite,
      Ordinal: 0,
      ExtraKeyId: 3);
    var third = new StableNodeAnchor(
      RoslynCpgNodeKind.Method,
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
      RoslynCpgNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: 0,
      SpanEnd: 20,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 2);
    var middleByStableOrder = new StableNodeAnchor(
      RoslynCpgNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: 8,
      SpanEnd: 12,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 3);
    var lastByStableOrder = new StableNodeAnchor(
      RoslynCpgNodeKind.SyntaxNode,
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
    var graph = new RoslynCpgGraph(allocation);

    var second = graph.AddNode(CreateSyntaxNode(secondAnchor, "second"));
    var first = graph.AddNode(CreateSyntaxNode(firstAnchor, "first"));
    graph.AddEdge(first, second, RoslynCpgEdgeKind.SyntaxChild);
    graph.FreezeQueryIndex();

    Assert.Equal(allocation.GetRequiredId(firstAnchor), Assert.Single(graph.Nodes, node => node.Name == "first").NodeId);
    Assert.Equal(allocation.GetRequiredId(secondAnchor), Assert.Single(graph.Nodes, node => node.Name == "second").NodeId);
    Assert.Equal(
      allocation.GetRequiredId(firstAnchor),
      Assert.Single(graph.Edges).SourceNodeId);
  }

  [Fact]
  public void AddNode_WithPreallocatedIds_RejectsUnknownAnchorBeforeGraphMutation()
  {
    var allocatedAnchor = CreateSyntaxAnchor(spanStart: 0, spanEnd: 4);
    var graph = new RoslynCpgGraph(DeterministicNodeIdTable.Create(new[] { allocatedAnchor }));
    var unallocatedNode = CreateSyntaxNode(CreateSyntaxAnchor(spanStart: 5, spanEnd: 9), "unallocated");

    Assert.Throws<InvalidOperationException>(() => graph.AddNode(unallocatedNode));
    Assert.Empty(graph.Nodes);
  }

  [Fact]
  public void BuildFromSource_WithCompatibilityPreallocatedIds_MatchesLegacyAcrossDegreesOfParallelism()
  {
    var baseline = BuildNodeAndEdgeSnapshot(CreateBuilder(maxDegreeOfParallelism: 1));

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new RoslynCpgBuilder(CreateBuilderOptions(maxDegreeOfParallelism) with
      {
        UsePreallocatedNodeIds = true,
      });
      var candidate = BuildNodeAndEdgeSnapshot(builder);

      Assert.Equal(baseline, candidate);
      var telemetry = Assert.IsType<RoslynCpgPreallocationTelemetry>(builder.LastBuildTelemetry.Preallocation);
      Assert.False(telemetry.UsedCompatibilityPreflight);
      Assert.True(telemetry.UsedAnchorDiscovery);
      Assert.True(telemetry.StableAnchorCount > 0);
    }
  }

  [Fact]
  public void StreamingDescriptorContracts_ContainOnlyStableGraphData()
  {
    var assembly = typeof(RoslynCpgBuilder).Assembly;
    var nodeDescriptor = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgNodeDescriptor");
    var edgeCandidate = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.CpgEdgeCandidate");
    Assert.NotNull(nodeDescriptor);
    Assert.NotNull(edgeCandidate);

    Assert.Contains(nodeDescriptor!.GetProperties(), property => property.Name == "Anchor");
    Assert.Contains(nodeDescriptor.GetProperties(), property => property.Name == "DispatchKind");
    Assert.Contains(nodeDescriptor.GetProperties(), property => property.Name == "TypeFullName");
    Assert.Contains(edgeCandidate!.GetProperties(), property => property.Name == "SourceAnchor");
    Assert.Contains(edgeCandidate.GetProperties(), property => property.Name == "TargetAnchor");
    Assert.All(
      nodeDescriptor.GetProperties().Concat(edgeCandidate.GetProperties()),
      property => Assert.False(
        property.PropertyType.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true ||
        property.PropertyType == typeof(RoslynCpgGraph)));
  }

  [Fact]
  public void OperationFragmentFacts_ExposeOnlyImmutableDescriptorCandidates()
  {
    var assembly = typeof(RoslynCpgBuilder).Assembly;
    var factsType = assembly.GetType("MinimalRoslynCpg.Builder.Streaming.OperationFragmentFacts");
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
  public void RoslynCpgBuilder_DoesNotRetainGlobalOperationToNodeCaches()
  {
    var fields = typeof(RoslynCpgBuilder).GetFields(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.NonPublic);

    Assert.DoesNotContain(fields, field => field.Name == "_operationNodes");
    Assert.DoesNotContain(fields, field => field.Name == "_operationOwningMethods");
  }

  [Fact]
  public void LocalFlowCandidateSet_ContainsOnlyStableEdgeCandidates()
  {
    var localFlowCandidateSet = typeof(RoslynCpgBuilder).Assembly.GetType(
      "MinimalRoslynCpg.Builder.Streaming.LocalFlowCandidateSet");
    Assert.NotNull(localFlowCandidateSet);

    var properties = localFlowCandidateSet!.GetProperties(
      System.Reflection.BindingFlags.Instance |
      System.Reflection.BindingFlags.Public |
      System.Reflection.BindingFlags.NonPublic);
    Assert.Contains(properties, property => property.Name == "EdgeCandidates");
    Assert.All(properties, property => Assert.False(ContainsRoslynOrGraphReference(property.PropertyType)));
  }

  [Fact]
  public void StableNodeIdentityFactory_ReusesStableAnchorAcrossGraphLifetimes()
  {
    var identityFactory = new StableNodeIdentityFactory();
    var first = identityFactory.GetStableAnchor(new RoslynCpgNode(
      RoslynCpgNodeKind.Method,
      "Method",
      Name: "Run",
      FilePath: "input.cs",
      SpanStart: 0,
      SpanEnd: 10));
    var second = identityFactory.GetStableAnchor(new RoslynCpgNode(
      RoslynCpgNodeKind.Method,
      "Method",
      Name: "Run",
      FilePath: "input.cs",
      SpanStart: 0,
      SpanEnd: 10));

    Assert.Equal(first, second);
  }

  [Fact]
  public void AddNodeAndEdge_BackfillsNodeIdsWithoutChangingDisplayFields()
  {
    var graph = new RoslynCpgGraph();
    var source = new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "source");
    var sink = new RoslynCpgNode(RoslynCpgNodeKind.Operation, "Operation", Name: "sink");

    var materializedSource = graph.AddNode(source);
    var materializedSink = graph.AddNode(sink);
    graph.AddEdge(
      materializedSource,
      materializedSink,
      RoslynCpgEdgeKind.DataFlow,
      contextId: new RoslynCpgContextId("flow"));
    graph.FreezeQueryIndex();

    var edge = Assert.Single(graph.Edges);
    materializedSource = Assert.Single(graph.Nodes, node => node.Name == "source");
    materializedSink = Assert.Single(graph.Nodes, node => node.Name == "sink");
    Assert.NotNull(materializedSource);
    Assert.NotNull(materializedSink);
    Assert.Equal("source", materializedSource.Name);
    Assert.Equal("sink", materializedSink.Name);
    Assert.Equal(new NodeId(1), materializedSource.NodeId);
    Assert.Equal(new NodeId(2), materializedSink.NodeId);
    Assert.NotNull(materializedSource.StableAnchor);
    Assert.NotNull(materializedSink.StableAnchor);
    Assert.Equal(materializedSource.NodeId!.Value, edge.SourceNodeId);
    Assert.Equal(materializedSink.NodeId!.Value, edge.TargetNodeId);
    Assert.Equal(materializedSource, graph.GetNode(materializedSource.NodeId!.Value));
    Assert.Equal("source", graph.GetNode(materializedSource.NodeId.Value)!.Name);
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

  private static Dictionary<string, uint> BuildStableNodeKeyToNodeIdMap(RoslynCpgBuilder builder)
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
      .OrderBy(BuildNodeContractKey, StringComparer.Ordinal)
      .ToDictionary(
        BuildNodeContractKey,
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
        type == typeof(RoslynCpgGraph))
    {
      return true;
    }

    return type.IsArray
      ? ContainsRoslynOrGraphReference(type.GetElementType()!)
      : type.IsGenericType && type.GenericTypeArguments.Any(ContainsRoslynOrGraphReference);
  }

  private static string BuildNodeContractKey(RoslynCpgNode node)
  {
    return string.Join(
      "|",
      node.Kind,
      node.DisplayKind,
      node.Name,
      node.FullName,
      node.Signature,
      node.FilePath,
      node.SpanStart,
      node.SpanEnd,
      node.IsImplicit);
  }

  private static RoslynCpgBuilder CreateBuilder(int maxDegreeOfParallelism)
  {
    return new RoslynCpgBuilder(CreateBuilderOptions(maxDegreeOfParallelism));
  }

  private static RoslynCpgBuilderOptions CreateBuilderOptions(int maxDegreeOfParallelism)
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1,
      SyntaxPassMode: RoslynCpgSyntaxPassMode.Partitioned,
      SyntaxLargeFileLineThreshold: 1);
  }

  private static string BuildNodeAndEdgeSnapshot(bool usePreallocatedNodeIds, int maxDegreeOfParallelism)
  {
    return BuildNodeAndEdgeSnapshot(new RoslynCpgBuilder(CreateBuilderOptions(maxDegreeOfParallelism) with
    {
      UsePreallocatedNodeIds = usePreallocatedNodeIds,
    }));
  }

  private static string BuildNodeAndEdgeSnapshot(RoslynCpgBuilder owner)
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
      RoslynCpgNodeKind.SyntaxNode,
      FilePathId: 1,
      SpanStart: spanStart,
      SpanEnd: spanEnd,
      StableNodeRole.SyntaxNode,
      Ordinal: 0,
      ExtraKeyId: 1);
  }

  private static RoslynCpgNode CreateSyntaxNode(StableNodeAnchor anchor, string name)
  {
    return new RoslynCpgNode(
      RoslynCpgNodeKind.SyntaxNode,
      "SyntaxNode",
      Name: name,
      StableAnchor: anchor);
  }
}
