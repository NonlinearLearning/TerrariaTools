using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Analysis.FlowSummaries;
using MinimalRoslynCpg.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using System.Reflection;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MinimalRoslynCpgPartitionedBuilderTests
{
  [Fact]
  public async Task OrderedPartitionWindow_ReportsHeadBlockingWithoutChangingCommitOrder()
  {
    var windowType = typeof(RoslynCpgBuilder).Assembly.GetType(
      "MinimalRoslynCpg.Builder.BoundedPartitionWorkWindow");
    Assert.NotNull(windowType);
    var runOrdered = windowType.GetMethod(
      "RunOrdered",
      BindingFlags.Public | BindingFlags.Static);
    Assert.NotNull(runOrdered);

    using var firstWorkStarted = new ManualResetEventSlim();
    using var releaseFirstWork = new ManualResetEventSlim();
    using var lookAheadWorkStarted = new ManualResetEventSlim();
    var committedOrders = new List<int>();
    var commitLock = new object();
    Func<int, int, int> work = (input, order) =>
    {
      if (order == 0)
      {
        firstWorkStarted.Set();
        releaseFirstWork.Wait(TimeSpan.FromSeconds(5));
      }

      if (order == 2)
      {
        lookAheadWorkStarted.Set();
      }

      return input;
    };
    Action<int, int> commit = (_, order) =>
    {
      lock (commitLock)
      {
        committedOrders.Add(order);
      }
    };

    var telemetryTask = Task.Run(() => runOrdered
      .MakeGenericMethod(typeof(int), typeof(int))
      .Invoke(null, new object?[]
      {
        new[] { 10, 20, 30, 40 },
        2,
        work,
        commit,
        CancellationToken.None,
        null,
        2,
        100,
      }));

    Assert.True(firstWorkStarted.Wait(TimeSpan.FromSeconds(5)));
    Assert.True(lookAheadWorkStarted.Wait(TimeSpan.FromSeconds(5)));
    await Task.Delay(75);
    releaseFirstWork.Set();
    var telemetry = await telemetryTask;
    Assert.NotNull(telemetry);

    Assert.Equal(new[] { 0, 1, 2, 3 }, committedOrders);
    Assert.True(ReadTelemetryValue<int>(telemetry, "ActiveWorkerPeak") >= 2);
    Assert.True(ReadTelemetryValue<int>(telemetry, "CompletedButUncommittedPeak") > 0);
    Assert.True(ReadTelemetryValue<int>(telemetry, "CompletedRecordCountPeak") > 0);
    Assert.True(ReadTelemetryValue<long>(telemetry, "CommitWaitMilliseconds") > 0);
    Assert.True(ReadTelemetryValue<long>(telemetry, "WindowBlockedMilliseconds") > 0);
  }

  [Fact]
  public void FlowSummary_UsesRoslynParameterOrdinalsAndStableReturnEndpoint()
  {
    var summary = new RoslynCpgFlowSummary(
      "project",
      "Demo.Sample",
      "Map",
      0,
      new[] { RoslynCpgFlowSummaryEndpoint.Parameter(2) },
      RoslynCpgFlowSummaryEndpoint.Return);

    Assert.Equal(2, Assert.Single(summary.Sources).ParameterOrdinal);
    Assert.Equal(-1, summary.Target.ParameterOrdinal);
    Assert.False(RoslynCpgDefaultFlowSummaries.TryGet(summary.StableKey, out _));
  }
  [Fact]
  public void BuildFromSource_InterproceduralCapability_IsOptInAndResolvesDependencies()
  {
    var defaultGraph = new RoslynCpgBuilder().BuildFromSource(
      "namespace Demo; public sealed class Sample { private int AddOne(int value) => value + 1; public int Run(int value) => AddOne(value); }",
      "interprocedural-default.cs");
    var options = RoslynCpgBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { RoslynCpgCapability.InterproceduralDataFlow },
    };
    var builder = new RoslynCpgBuilder(options);

    var graph = builder.BuildFromSource(
      "namespace Demo; public sealed class Sample { private int AddOne(int value) => value + 1; public int Run(int value) => AddOne(value); }",
      "interprocedural-enabled.cs");

    Assert.DoesNotContain(defaultGraph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.InterproceduralDataFlow);
    var bridgeEdges = graph.Edges
      .Where(edge => edge.Kind == RoslynCpgEdgeKind.InterproceduralDataFlow)
      .ToArray();
    Assert.NotEmpty(bridgeEdges);
    Assert.All(bridgeEdges, edge => Assert.Contains(
      Assert.IsType<RoslynCpgInterproceduralBridgeKind>(
        edge.StructuredLabel!.InterproceduralBridgeKind),
      new[]
      {
        RoslynCpgInterproceduralBridgeKind.ArgumentToParameter,
        RoslynCpgInterproceduralBridgeKind.ReturnToMethodReturn,
        RoslynCpgInterproceduralBridgeKind.MethodReturnToCallResult,
      }));
    Assert.All(
      bridgeEdges,
      edge => Assert.StartsWith(
        "callsite:",
        Assert.IsType<RoslynCpgContextId>(edge.ContextId).Value,
        StringComparison.Ordinal));
    Assert.All(bridgeEdges, edge =>
    {
      var callSiteContext = Assert.IsType<RoslynCpgCallSiteContext>(edge.CallSiteContext);
      Assert.Equal(callSiteContext.ToContextId(), edge.ContextId);
      Assert.False(string.IsNullOrWhiteSpace(callSiteContext.FilePath));
      Assert.True(callSiteContext.SpanStart >= 0);
      Assert.True(callSiteContext.SpanEnd >= callSiteContext.SpanStart);
      Assert.False(string.IsNullOrWhiteSpace(callSiteContext.DisplayName));
    });
    Assert.Equal(bridgeEdges.Length, builder.LastBuildTelemetry.InterproceduralDataFlowTelemetry!.BridgeEdgeCount);
    Assert.Contains(RoslynCpgCapability.InterproceduralDataFlow, builder.LastBuildTelemetry.ResolvedCapabilities!);
    Assert.Contains(RoslynCpgCapability.QueryIndex, builder.LastBuildTelemetry.ResolvedCapabilities!);
  }

  [Fact]
  public void BuildFromSource_InterproceduralCapability_RecordsExternalTargetCut()
  {
    var options = RoslynCpgBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { RoslynCpgCapability.InterproceduralDataFlow },
    };

    var builder = new RoslynCpgBuilder(options);
    var graph = builder.BuildFromSource(
      "public sealed class Sample { public string Run(int value) => value.ToString(); }",
      "interprocedural-external.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.InterproceduralDataFlow);
    Assert.True(builder.LastBuildTelemetry.InterproceduralDataFlowTelemetry!.CutCountByReason.TryGetValue(
      "ExternalTarget",
      out var externalTargetCutCount));
    Assert.True(externalTargetCutCount > 0);
  }

  [Fact]
  public void RoslynCpgEdgeKind_ContainsControlDependenceOverlayKinds()
  {
    var names = Enum.GetNames<RoslynCpgEdgeKind>();

    Assert.Contains("Dominates", names);
    Assert.Contains("PostDominates", names);
    Assert.Contains("ControlDependence", names);
  }

  [Fact]
  public void BuildFromSource_ControlDependenceCapability_ProjectsStableConditionalOverlays()
  {
    const string source = CpgBuilderSources.ControlDependenceOverlay;
    var options = RoslynCpgBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { RoslynCpgCapability.ControlDependence },
    };

    var graph = new RoslynCpgBuilder(options).BuildFromSource(source, "control-dependence.cs");

    var condition = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.OpBinary && graph.GetDisplayText(node) == "value > 0");
    var trueBranchAssignment = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.OpAssignment && graph.GetDisplayText(node) == "value += 1");
    var falseBranchAssignment = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.OpAssignment && graph.GetDisplayText(node) == "value -= 1");
    var entry = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.MethodEntry && node.Name == "Adjust:entry");
    var exit = Assert.Single(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.MethodExit && node.Name == "Adjust:exit");

    Assert.Contains(graph.Controls(RequireNodeId(condition)), edge => edge.TargetNodeId == RequireNodeId(trueBranchAssignment));
    Assert.Contains(graph.Controls(RequireNodeId(condition)), edge => edge.TargetNodeId == RequireNodeId(falseBranchAssignment));
    Assert.Contains(graph.Dominates(RequireNodeId(entry)), edge => edge.TargetNodeId == RequireNodeId(condition));
    Assert.Contains(graph.GetEdges(RoslynCpgEdgeKind.PostDominates), edge => edge.TargetNodeId == RequireNodeId(exit));
  }

  [Fact]
  public void BuildFromSource_DefaultOptions_AlwaysUsePartitionedPasses()
  {
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(CreateLargeSource(methodCount: 2, statementsPerMethod: 1), "default-partitioned.cs");

    Assert.True(builder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.True(builder.LastBuildTelemetry.UsedPartitionedSyntaxPass);
    Assert.Contains("DominancePass", builder.LastBuildTelemetry.SkippedPassNames!);
    Assert.Contains("ControlDependencePass", builder.LastBuildTelemetry.SkippedPassNames!);
  }

  [Fact]
  public void BuildFromSource_DefaultCapabilities_DoesNotMaterializeOverlayEdges()
  {
    var graph = new RoslynCpgBuilder().BuildFromSource(
      "namespace Demo; public sealed class Sample { public int Run(int value) { if (value > 0) return value; return 0; } }",
      "default-no-overlay.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.Dominates);
    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.PostDominates);
    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.ControlDependence);
  }

  [Fact]
  public void BuildFromSource_SyntaxSemanticCapability_SkipsExpensiveMethodOverlays()
  {
    var options = RoslynCpgBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { RoslynCpgCapability.SyntaxSemantic }
    };
    var builder = new RoslynCpgBuilder(options);

    var graph = builder.BuildFromSource(
      "namespace Demo; public sealed class Sample { public int Run(int value) => value + 1; }",
      "syntax-only.cs");

    Assert.Equal("capability-v1", builder.LastBuildTelemetry.GraphSnapshotVersion);
    Assert.Contains("SyntaxPass", builder.LastBuildTelemetry.ExecutedPassNames!);
    Assert.Contains("DataFlowPass", builder.LastBuildTelemetry.SkippedPassNames!);
    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.DataFlow);
  }

  [Fact]
  public void BuildFromSource_ControlDependenceCapability_ResolvesOverlayDependencies()
  {
    var options = RoslynCpgBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { RoslynCpgCapability.ControlDependence }
    };
    var builder = new RoslynCpgBuilder(options);

    _ = builder.BuildFromSource(
      "namespace Demo; public sealed class Sample { public int Run(int value) { if (value > 0) return value; return 0; } }",
      "control-dependence.cs");

    Assert.Equal(
      new[]
      {
        RoslynCpgCapability.SyntaxSemantic,
        RoslynCpgCapability.MethodModel,
        RoslynCpgCapability.Cfg,
        RoslynCpgCapability.Dominance,
        RoslynCpgCapability.ControlDependence,
      },
      builder.LastBuildTelemetry.ResolvedCapabilities);
    Assert.Contains("DominancePass", builder.LastBuildTelemetry.ExecutedPassNames!);
    Assert.Contains("ControlDependencePass", builder.LastBuildTelemetry.ExecutedPassNames!);
  }

  [Fact]
  public void BuildFromSource_PartitionedOperationPass_RentsChildOperationBuffer()
  {
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1));

    _ = builder.BuildFromSource(CreateLargeSource(methodCount: 2, statementsPerMethod: 4), "pooled-operation-buffer.cs");

    var rentCountProperty = typeof(RoslynCpgBuildTelemetry).GetProperty("OperationChildBufferRentCount");
    Assert.NotNull(rentCountProperty);
    Assert.True((int)rentCountProperty.GetValue(builder.LastBuildTelemetry)! > 0);
  }

  [Fact]
  public void BuildFromSource_PartitionedOperationPass_ReleasesCommittedFragmentFactsWithinWorkWindow()
  {
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 1,
      LargeFileMethodThreshold: 1,
      LargeMethodLineSpanThreshold: 1));

    _ = builder.BuildFromSource(CreateLargeSource(methodCount: 12, statementsPerMethod: 4), "released-operation-fragments.cs");

    var telemetry = Assert.IsType<RoslynCpgOperationFragmentTelemetry>(
      builder.LastBuildTelemetry.OperationFragments);
    Assert.Equal(12, telemetry.CommittedFragmentCount);
    Assert.Equal(12, telemetry.ReleasedFragmentCount);
    Assert.InRange(telemetry.PeakBufferedFragmentCount, 1, 4);
    Assert.True(telemetry.ReleasedBuilderOperationState);
  }

  [Fact]
  public void BuildFromSource_PartitionedMode_ProducesSameGraphAsLegacy()
  {
    const string filePath = "partitioned-ab.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var legacyBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));
    var partitionedBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
    Assert.True(legacyBuilder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.True(partitionedBuilder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(12, partitionedBuilder.LastBuildTelemetry.PartitionCount);
  }

  [Fact]
  public void BuildFromSource_AutoMode_UsesPartitionedBuildForLargeFile()
  {
    const string filePath = "partitioned-auto-large.cs";
    var source = CreateLargeSource(methodCount: 10, statementsPerMethod: 12);
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 3,
      LargeFileLineThreshold: 80,
      LargeFileMethodThreshold: 6,
      LargeMethodLineSpanThreshold: 8));

    _ = builder.BuildFromSource(source, filePath);

    Assert.True(builder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(RoslynCpgBuilderMode.Partitioned, builder.LastBuildTelemetry.ExecutedMode);
    Assert.Equal(10, builder.LastBuildTelemetry.PartitionCount);
    Assert.Equal(3, builder.LastBuildTelemetry.MaxDegreeOfParallelism);
  }

  [Fact]
  public void BuildFromSource_AutoMode_KeepsLegacyBuildForSmallFile()
  {
    const string filePath = "partitioned-auto-small.cs";
    const string source = CpgBuilderSources.SmallPartitionedSource;
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 80,
      LargeFileMethodThreshold: 6,
      LargeMethodLineSpanThreshold: 8));

    _ = builder.BuildFromSource(source, filePath);

    Assert.True(builder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(RoslynCpgBuilderMode.Partitioned, builder.LastBuildTelemetry.ExecutedMode);
    Assert.Equal(1, builder.LastBuildTelemetry.PartitionCount);
  }

  [Fact]
  public void BuildFromSource_PartitionedSyntaxPass_PreservesGraphsAcrossDegreesOfParallelism()
  {
    const string filePath = "partitioned-syntax-pass.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var legacyGraph = new RoslynCpgBuilder(CreateSyntaxPassOptions(RoslynCpgSyntaxPassMode.Partitioned, 1))
      .BuildFromSource(source, filePath);

    foreach (var degreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      for (var iteration = 0; iteration < 10; iteration += 1)
      {
        var builder = new RoslynCpgBuilder(CreateSyntaxPassOptions(
          RoslynCpgSyntaxPassMode.Partitioned,
          degreeOfParallelism));
        var graph = builder.BuildFromSource(source, filePath);

        AssertGraphsEqual(legacyGraph, graph);
        Assert.True(builder.LastBuildTelemetry.UsedPartitionedSyntaxPass);
        Assert.Equal(12, builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxPartitionCount);
        Assert.Equal(degreeOfParallelism, builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxPartitionMaxDegreeOfParallelism);
      }
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlowUsedFacts_PreservesGraphsAcrossDegreesOfParallelism()
  {
    const string filePath = "partitioned-data-flow-used-facts.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var baselineGraph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, filePath);

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism));

      var graph = builder.BuildFromSource(source, filePath);

      AssertGraphsEqual(baselineGraph, graph);
      Assert.Equal(12, builder.LastBuildTelemetry.DataFlowPassTelemetry.UsedFactPartitionCount);
      Assert.Equal(
        maxDegreeOfParallelism,
        builder.LastBuildTelemetry.DataFlowPassTelemetry.UsedFactPartitionMaxDegreeOfParallelism);
      Assert.Equal(12, builder.LastBuildTelemetry.DataFlowPassTelemetry.CfgSensitivePartitionCount);
      Assert.Equal(
        maxDegreeOfParallelism,
        builder.LastBuildTelemetry.DataFlowPassTelemetry.CfgSensitivePartitionMaxDegreeOfParallelism);
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_RecordsOrderedCandidateCommitTelemetry()
  {
    var builder = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 4));

    _ = builder.BuildFromSource(
      CreateLargeSource(methodCount: 12, statementsPerMethod: 10),
      "ordered-data-flow-candidates.cs");

    var telemetry = builder.LastBuildTelemetry.DataFlowPassTelemetry;
    var telemetryType = telemetry.GetType();
    var candidateGenerationProperty = telemetryType.GetProperty("CfgSensitiveCandidateGenerationElapsedMilliseconds");
    var candidateCommitProperty = telemetryType.GetProperty("CfgSensitiveCandidateCommitElapsedMilliseconds");
    var peakBufferedBatchProperty = telemetryType.GetProperty("PeakBufferedCandidateBatchCount");
    var candidateEdgeCountProperty = telemetryType.GetProperty("CandidateEdgeCount");

    Assert.NotNull(candidateGenerationProperty);
    Assert.NotNull(candidateCommitProperty);
    Assert.NotNull(peakBufferedBatchProperty);
    Assert.NotNull(candidateEdgeCountProperty);
    Assert.True((long)candidateGenerationProperty.GetValue(telemetry)! >= 0);
    Assert.True((long)candidateCommitProperty.GetValue(telemetry)! >= 0);
    Assert.InRange((int)peakBufferedBatchProperty.GetValue(telemetry)!, 1, 4);
    Assert.True((int)candidateEdgeCountProperty.GetValue(telemetry)! > 0);
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_ReleasesCommittedMethodPlans()
  {
    var builder = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 4));

    _ = builder.BuildFromSource(
      CreateLargeSource(methodCount: 12, statementsPerMethod: 10),
      "released-data-flow-plans.cs");

    Assert.Equal(12, builder.LastBuildTelemetry.DataFlowPassTelemetry.ReleasedCfgSensitivePlanCount);
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_PreservesComplexMethodLocalFlowShape()
  {
    const string source = CpgBuilderSources.ComplexMethodLocalFlow;
    var graph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(4))
      .BuildFromSource(source, "dataflow-complex-shape.cs");

    Assert.Equal(
      new[]
      {
        "MethodParameter:seed->OpConditional:if (seed > 0)\n    {\n      current = seed + 1;\n    }",
        "MethodParameter:seed->Operation:var current = seed;",
        "MethodParameter:value->OpReturn:return value;",
        "MethodReturn:Echo:return->CallSite:Echo",
        "MethodReturn:Echo:return->MethodExit:Echo:exit",
        "MethodReturn:Run:return->MethodExit:Run:exit",
        "OpBinary:current + 1->OpAssignment:current = current + 1",
        "OpBinary:seed + 1->OpAssignment:current = seed + 1",
        "OpInvocation:Echo(current)->MethodReturn:Run:return",
        "OpInvocation:Echo(current)->OpReturn:return Echo(current);",
        "OpLocalReference:current->MethodParameter:value",
        "OpLocalReference:current->OpInvocation:Echo(current)",
        "OpParameterReference:seed->Operation:current = seed",
        "OpParameterReference:value->MethodReturn:Echo:return",
        "OpParameterReference:value->OpReturn:return value;",
        "OpReturn:return Echo(current);->MethodExit:Run:exit",
        "OpReturn:return value;->MethodExit:Echo:exit",
      },
      DescribeDataFlowEdges(graph));
  }

  [Fact]
  public void BuildFromSource_LocalDataFlowSample_EmitsExpectedSyntaxSymbolTypeAndOperationRelations()
  {
    const string source = CpgBuilderSources.LocalDataFlow;
    var graph = new RoslynCpgBuilder().BuildFromSource(source, "graph-correctness.cs");

    var methodNode = Assert.Single(graph.Nodes, node =>
      node.Kind == RoslynCpgNodeKind.SyntaxNode && node.Name == "Increment");
    var parameterNode = Assert.Single(graph.Nodes, node =>
      node.Kind == RoslynCpgNodeKind.SyntaxNode && node.Name == "seed");
    var localNode = Assert.Single(graph.Nodes, node =>
      node.Kind == RoslynCpgNodeKind.SyntaxNode && node.Name == "value");
    var seedReferences = graph.Nodes.Where(node =>
      node.Kind == RoslynCpgNodeKind.SyntaxNode &&
      node.DisplayKind == "IdentifierName" &&
      graph.GetDisplayText(node) == "seed").ToArray();
    var valueReferences = graph.Nodes.Where(node =>
      node.Kind == RoslynCpgNodeKind.SyntaxNode &&
      node.DisplayKind == "IdentifierName" &&
      graph.GetDisplayText(node) == "value").ToArray();

    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(methodNode) && edge.Kind == RoslynCpgEdgeKind.DeclaresSymbol);
    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(parameterNode) && edge.Kind == RoslynCpgEdgeKind.DeclaresSymbol);
    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(localNode) && edge.Kind == RoslynCpgEdgeKind.DeclaresSymbol);
    Assert.All(seedReferences, node => Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == RoslynCpgEdgeKind.ReferencesSymbol));
    Assert.All(valueReferences, node => Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == RoslynCpgEdgeKind.ReferencesSymbol));
    Assert.Contains(valueReferences, node => graph.Edges.Any(edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == RoslynCpgEdgeKind.HasType));
    Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.SyntaxHasOperation);
    Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.OpHasSyntax);
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_RepeatedReferencesKeepUniqueDeterministicEdges()
  {
    const string source = CpgBuilderSources.RepeatedReferences;
    var firstGraph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "duplicate-flow-a.cs");
    var secondGraph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(16))
      .BuildFromSource(source, "duplicate-flow-b.cs");

    var firstEdges = DescribeDataFlowEdges(firstGraph);
    var secondEdges = DescribeDataFlowEdges(secondGraph);

    Assert.Equal(firstEdges, secondEdges);
    Assert.Equal(firstEdges.Length, firstEdges.Distinct(StringComparer.Ordinal).Count());
    Assert.Equal(
      new[]
      {
        "MethodParameter:seed->Operation:var value = seed + 1;",
        "MethodReturn:Run:return->MethodExit:Run:exit",
        "OpBinary:seed + 1->Operation:value = seed + 1",
        "OpBinary:value + value + value->MethodReturn:Run:return",
        "OpBinary:value + value + value->OpReturn:return value + value + value;",
        "OpReturn:return value + value + value;->MethodExit:Run:exit",
      },
      firstEdges);
  }

  [Fact]
  public void BuildFromSource_DeclarationShapes_PreserveDeclaredSymbolEdges()
  {
    const string source = CpgBuilderSources.DeclarationShapes;
    var graph = new RoslynCpgBuilder().BuildFromSource(source, "declaration-shapes.cs");

    var declarationKinds = new[]
    {
      "FileScopedNamespaceDeclaration",
      "DelegateDeclaration",
      "EnumDeclaration",
      "EnumMemberDeclaration",
      "ClassDeclaration",
      "TypeParameter",
      "VariableDeclarator",
      "EventDeclaration",
      "PropertyDeclaration",
      "IndexerDeclaration",
      "GetAccessorDeclaration",
      "SetAccessorDeclaration",
      "AddAccessorDeclaration",
      "RemoveAccessorDeclaration",
      "ConstructorDeclaration",
      "MethodDeclaration",
      "OperatorDeclaration",
      "ConversionOperatorDeclaration",
      "Parameter",
      "LocalFunctionStatement",
      "SingleVariableDesignation",
      "LabeledStatement",
    };

    foreach (var declarationKind in declarationKinds)
    {
      var declarationNodes = graph.Nodes.Where(node =>
        node.Kind == RoslynCpgNodeKind.SyntaxNode && node.DisplayKind == declarationKind).ToArray();
      Assert.True(declarationNodes.Length > 0, $"Missing {declarationKind} syntax node.");
      Assert.All(declarationNodes, node => Assert.Contains(graph.Edges, edge =>
        edge.SourceNodeId == RequireNodeId(node) && edge.Kind == RoslynCpgEdgeKind.DeclaresSymbol));
    }

    var methodLikeDeclarationKinds = new[]
    {
      "ConstructorDeclaration",
      "MethodDeclaration",
      "OperatorDeclaration",
      "ConversionOperatorDeclaration",
      "LocalFunctionStatement",
      "GetAccessorDeclaration",
      "SetAccessorDeclaration",
      "AddAccessorDeclaration",
      "RemoveAccessorDeclaration",
    };
    foreach (var declarationKind in methodLikeDeclarationKinds)
    {
      foreach (var declarationNode in graph.Nodes.Where(node =>
        node.Kind == RoslynCpgNodeKind.SyntaxNode && node.DisplayKind == declarationKind))
      {
        Assert.Contains(graph.Edges, edge =>
          edge.SourceNodeId == RequireNodeId(declarationNode) &&
          edge.Kind == RoslynCpgEdgeKind.SyntaxChild &&
          graph.Nodes.Any(node => node.NodeId == edge.TargetNodeId && node.Kind == RoslynCpgNodeKind.Method));
      }
    }
  }

  [Fact]
  public void BuildFromSource_DeclaredSymbolQueryTelemetry_IsExposedAndReduced()
  {
    const string source = CpgBuilderSources.DeclaredSymbolQueryTelemetry;
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(source, "declared-symbol-query-telemetry.cs");

    var telemetry = builder.LastBuildTelemetry.SyntaxPassTelemetry;
    var queryCountProperty = telemetry.GetType().GetProperty("DeclaredSymbolQueryCount");
    Assert.NotNull(queryCountProperty);
    var queryCount = Assert.IsType<int>(queryCountProperty.GetValue(telemetry));
    Assert.True(queryCount < telemetry.SyntaxNodeCount);
    Assert.Equal(
      builder.LastBuildTelemetry.MethodDecorationTelemetry.SyntaxNodeCount,
      builder.LastBuildTelemetry.MethodDecorationTelemetry.DeclaredSymbolQueryCount);
    Assert.True(
      builder.LastBuildTelemetry.MethodDecorationTelemetry.SyntaxNodeCount < telemetry.SyntaxNodeCount);
  }

  [Fact]
  public void SharedSemanticModel_ConcurrentSyntaxQueries_MatchSerialBaseline()
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(CreateLargeSource(methodCount: 12, statementsPerMethod: 10));
    var compilation = CSharpCompilation.Create(
      "semantic-model-probe",
      new[] { syntaxTree },
      Array.Empty<MetadataReference>(),
      new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
    var semanticModel = compilation.GetSemanticModel(syntaxTree);
    var syntaxNodes = syntaxTree.GetRoot().DescendantNodes().ToArray();
    var expected = syntaxNodes.Select(node => FormatSemanticFacts(node, semanticModel)).ToArray();

    for (var iteration = 0; iteration < 10; iteration += 1)
    {
      var actual = new string[syntaxNodes.Length];
      Parallel.For(0, syntaxNodes.Length, index =>
      {
        actual[index] = FormatSemanticFacts(syntaxNodes[index], semanticModel);
      });

      Assert.Equal(expected, actual);
    }
  }

  [Fact]
  public void BuildFromSource_RecordsFineGrainedSyntaxAndDataFlowTelemetry()
  {
    const string filePath = "fine-grained-telemetry.cs";
    var source = CreateLargeSource(methodCount: 8, statementsPerMethod: 9);
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));

    _ = builder.BuildFromSource(source, filePath);

    Assert.True(builder.LastBuildTelemetry.OperationBuildElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxBuildElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowBuildElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeQueryIndexElapsedMilliseconds >= 0);
    var operationWindow = Assert.IsType<RoslynCpgOrderedWorkWindowTelemetry>(
      builder.LastBuildTelemetry.OperationOrderedWindow);
    var cfgSensitiveWindow = Assert.IsType<RoslynCpgOrderedWorkWindowTelemetry>(
      builder.LastBuildTelemetry.CfgSensitiveOrderedWindow);
    Assert.InRange(operationWindow.ActiveWorkerPeak, 1, 4);
    Assert.True(operationWindow.CompletedButUncommittedPeak >= 0);
    Assert.True(operationWindow.CompletedRecordCountPeak >= 0);
    Assert.True(operationWindow.CommitWaitMilliseconds >= 0);
    Assert.True(operationWindow.WindowBlockedMilliseconds >= 0);
    Assert.True(cfgSensitiveWindow.ActiveWorkerPeak >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.AssignDeterministicNodeIdsElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.CreateAnchorsElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.CreateNodeIdTableElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.RemapNodesElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.RemapEdgesElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildQueryIndexElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.PopulateEdgeIndexBucketsElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.OrderEdgesElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.OrderNodesElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.SnapshotHashElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildAdjacencyElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildKindAdjacencyElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildEdgeKindIndexElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildNodeKindIndexElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.BuildFilePathIndexElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.NodeCount > 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.EdgeCount > 0);
    Assert.True(builder.LastBuildTelemetry.FreezeTelemetry.DistinctAnchorCount > 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.TotalElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxNodeCount > 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxTokenCount > 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.TotalElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.MethodBlockCount > 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.OrderedOperationCount > 0);
    Assert.True(
      builder.LastBuildTelemetry.SyntaxBuildElapsedMilliseconds >=
      builder.LastBuildTelemetry.SyntaxPassTelemetry.TotalElapsedMilliseconds);
    Assert.True(
      builder.LastBuildTelemetry.DataFlowBuildElapsedMilliseconds >=
      builder.LastBuildTelemetry.DataFlowPassTelemetry.TotalElapsedMilliseconds);
  }

  [Fact]
  public void BuildFromSource_RecordsSeparatedTypeInfoTelemetry()
  {
    const string source = CpgBuilderSources.SeparatedTypeInfoTelemetry;
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(source, "separated-type-info-telemetry.cs");

    var telemetry = builder.LastBuildTelemetry.SyntaxPassTelemetry;
    Assert.True(telemetry.ResolveTypeInfoElapsedMilliseconds >= 0);
    Assert.True(telemetry.AddSyntaxTypeEdgesElapsedMilliseconds >= 0);
    Assert.True(telemetry.TypeInfoQueryCount > 0);
    Assert.True(telemetry.TypeInfoResolvedCount > 0);
  }

  [Fact]
  public void BuildFromSource_RecordsTypeInfoSourceAndDataFlowPreparationTelemetry()
  {
    const string source = CpgBuilderSources.TypeInfoSourceAndDataFlowPreparationTelemetry;
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(source, "type-info-source-and-data-flow-preparation-telemetry.cs");

    var syntaxTelemetry = builder.LastBuildTelemetry.SyntaxPassTelemetry;
    var dataFlowTelemetry = builder.LastBuildTelemetry.DataFlowPassTelemetry;
    Assert.True(syntaxTelemetry.OperationBackedTypeInfoDeferredCount > 0);
    Assert.True(syntaxTelemetry.OperationBackedTypeInfoResolvedCount >= 0);
    Assert.True(syntaxTelemetry.OperationBackedTypeInfoFallbackCount >= 0);
    Assert.True(syntaxTelemetry.OperationBackedTypeInfoFallbackElapsedMilliseconds >= 0);
    Assert.True(dataFlowTelemetry.PrepareFlowNodesElapsedMilliseconds >= 0);
    Assert.True(dataFlowTelemetry.CollectUsedFactsElapsedMilliseconds >= 0);
    Assert.True(dataFlowTelemetry.CreateDefinitionFactsElapsedMilliseconds >= 0);
    Assert.True(dataFlowTelemetry.InitializeCfgSensitiveStateElapsedMilliseconds >= 0);
    Assert.True(dataFlowTelemetry.FlowNodeCount > 0);
    Assert.True(dataFlowTelemetry.UsedFactCount > 0);
    Assert.True(dataFlowTelemetry.DefinitionFactCount > 0);
  }

  [Fact]
  public void BuildFromSource_DataFlowFactCollection_TraversesEachOperationOnceAndProjectsMethodNodes()
  {
    const string source = CpgBuilderSources.DataFlowFactCollection;
    var baseline = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1))
      .BuildFromSource(source, "data-flow-fact-dedup.cs");

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism));
      var graph = builder.BuildFromSource(source, "data-flow-fact-dedup.cs");

      AssertGraphsEqual(baseline, graph);
      var telemetry = builder.LastBuildTelemetry.DataFlowPassTelemetry;
      Assert.Equal(telemetry.OrderedOperationCount, telemetry.UsedFactRecordCount);
      Assert.Equal(telemetry.OrderedOperationCount, telemetry.MethodOperationNodeProjectionCount);
      Assert.True(telemetry.FrozenOperationNodeCount >= telemetry.OrderedOperationCount);
      Assert.True(telemetry.PrepareFlowNodesElapsedTicks > 0);
      Assert.True(telemetry.CollectUsedFactsElapsedTicks > 0);
    }
  }

  [Fact]
  public void BuildFromSource_DataFlowDefinitionBudget_SkipsOnlyOverBudgetMethod()
  {
    const string source = CpgBuilderSources.DataFlowDefinitionBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new RoslynCpgDataFlowOptions(MaxDefinitionsPerMethod: 0)
    };
    var builder = new RoslynCpgBuilder(options);

    var graph = builder.BuildFromSource(source, "definition-budget.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.DataFlow);
    Assert.Equal(1, builder.LastBuildTelemetry.DataFlowPassTelemetry.SkippedMethodCount);
  }

  [Fact]
  public void BuildFromSource_DataFlowNodeBudget_SkipsOverBudgetMethod()
  {
    const string source = CpgBuilderSources.DataFlowNodeBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new RoslynCpgDataFlowOptions(
        MaxDefinitionsPerMethod: int.MaxValue,
        MaxFlowNodesPerMethod: 0)
    };
    var builder = new RoslynCpgBuilder(options);

    var graph = builder.BuildFromSource(source, "flow-node-budget.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.DataFlow);
    Assert.Equal(1, builder.LastBuildTelemetry.DataFlowPassTelemetry.SkippedMethodCount);
  }

  [Fact]
  public void BuildFromSource_DataFlowCandidateBudget_FailBuildReportsStableMethodName()
  {
    const string source = CpgBuilderSources.DataFlowCandidateBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new RoslynCpgDataFlowOptions(
        MaxDefinitionsPerMethod: int.MaxValue,
        MaxFlowNodesPerMethod: int.MaxValue,
        MaxCandidateEdgesPerMethod: 0,
        OverflowBehavior: RoslynCpgDataFlowOverflowBehavior.FailBuild),
    };

    var exception = Assert.Throws<InvalidOperationException>(() =>
      new RoslynCpgBuilder(options).BuildFromSource(source, "candidate-budget.cs"));

    Assert.Contains("Run", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildFromSource_DataFlowBudgetSkip_ReportsStablePerMethodOverflowReason()
  {
    const string source = CpgBuilderSources.DataFlowBudgetSkip;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new RoslynCpgDataFlowOptions(MaxDefinitionsPerMethod: 2),
    };

    var builder = new RoslynCpgBuilder(options);

    _ = builder.BuildFromSource(source, "per-method-budget.cs");

    var telemetry = builder.LastBuildTelemetry.DataFlowPassTelemetry;
    var skippedMethod = Assert.Single(telemetry.MethodTelemetry!.Where(method =>
      method.OverflowReason != RoslynCpgDataFlowOverflowReason.None));

    Assert.Equal(
      "Demo.BudgetSample.OverBudget(int)",
      skippedMethod.MethodFullName);
    Assert.Equal(
      RoslynCpgDataFlowOverflowReason.DefinitionLimitExceeded,
      skippedMethod.OverflowReason);
    Assert.Equal(3, skippedMethod.DefinitionCount);
    Assert.Equal(0, skippedMethod.GeneratedCandidateCount);
  }

  [Fact]
  public void BuildFromSource_ClassifiesOperationBackedTypeInfoFallbacks()
  {
    const string source = CpgBuilderSources.OperationBackedTypeInfoFallback;
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(source, "type-info-fallback-classification.cs");

    var telemetry = builder.LastBuildTelemetry.SyntaxPassTelemetry;
    Assert.True(telemetry.OperationBackedTypeInfoFallbackCount > 0);
    Assert.True(telemetry.OperationBackedTypeInfoFallbackElapsedTicks > 0);
    Assert.True(telemetry.OperationBackedTypeInfoMissingOperationCount > 0);
    Assert.True(telemetry.OperationBackedTypeInfoNullOperationTypeCount >= 0);
    Assert.True(telemetry.OperationBackedTypeInfoFallbackCountBySyntaxKind.ContainsKey("AddExpression"));
    Assert.True(telemetry.OperationBackedTypeInfoFallbackCountBySyntaxKind.ContainsKey("IdentifierName"));
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_PreservesCompleteGraph()
  {
    const string filePath = "symbol-type-reuse.cs";
    const string source = CpgBuilderSources.SymbolTypeReuse;
    var withoutReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, filePath);
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
    Assert.True(withReuseBuilder.LastBuildTelemetry.SyntaxPassTelemetry.TypeInfoSymbolReuseCount > 0);
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_FallsBackForMethodGroupsAndDynamicSyntax()
  {
    const string source = CpgBuilderSources.SymbolTypeReuseFallback;
    var withoutReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
    Assert.True(withReuseBuilder.LastBuildTelemetry.SyntaxPassTelemetry.TypeInfoQueryCount > 0);
  }

  [Fact]
  public void BuildFromSource_OperationBackedSyntaxTypes_PreservesTypeEdges()
  {
    const string source = CpgBuilderSources.OperationBackedSyntaxTypes;
    var syntaxOnlyBuilder = new RoslynCpgBuilder(CreateBuilderOptions(
      enableReferencedSymbolTypeReuse: true,
      enableOperationBackedSyntaxTypes: false));
    var operationBackedBuilder = new RoslynCpgBuilder(CreateBuilderOptions(
      enableReferencedSymbolTypeReuse: true,
      enableOperationBackedSyntaxTypes: true));

    var syntaxOnlyGraph = syntaxOnlyBuilder.BuildFromSource(source, "operation-backed-types.cs");
    var operationBackedGraph = operationBackedBuilder.BuildFromSource(source, "operation-backed-types.cs");

    AssertGraphsEqual(syntaxOnlyGraph, operationBackedGraph);
    AssertTypeGraphEqual(syntaxOnlyGraph, operationBackedGraph);
  }

  [Fact]
  public void BuildFromSource_PartitionedMode_PreservesControlFlowAndDataFlowHeavyGraph()
  {
    const string filePath = "partitioned-controlflow-dataflow.cs";
    const string source = CpgBuilderSources.ControlFlowAndDataFlowHeavy;
    var legacyBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));
    var partitionedBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
    Assert.True(partitionedBuilder.LastBuildTelemetry.DataFlowPassTelemetry.BuildFlowNeighborsElapsedMilliseconds >= 0);
  }

  [Fact]
  public void BuildFromSource_PartitionedSyntaxPass_PreservesSemanticEdgesForGenericAccessorAndLocalFunction()
  {
    const string source = CpgBuilderSources.SyntaxSemanticShapes;
    var baseline = new RoslynCpgBuilder(CreateSyntaxPassOptions(RoslynCpgSyntaxPassMode.Partitioned, 1))
      .BuildFromSource(source, "syntax-semantic-shapes.cs");

    foreach (var degreeOfParallelism in new[] { 8, 16 })
    {
      var graph = new RoslynCpgBuilder(CreateSyntaxPassOptions(
        RoslynCpgSyntaxPassMode.Partitioned,
        degreeOfParallelism)).BuildFromSource(source, "syntax-semantic-shapes.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.HasType);
      Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.RefersToType);
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_PreservesCallReturnAndPropertyFlowsAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.DataFlowCallReturnAndProperty;
    var baseline = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "dataflow-call-property.cs");

    foreach (var degreeOfParallelism in new[] { 8, 16 })
    {
      var graph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(degreeOfParallelism))
        .BuildFromSource(source, "dataflow-call-property.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.DataFlow);
      Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.ParameterLink);
    }
  }

  [Fact]
  public void BuildFromSource_OperationConsumers_PreserveCallMemberAndDataFlowAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.DataFlowCallReturnAndProperty;
    var baseline = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "operation-consumer-inventory.cs");

    foreach (var degreeOfParallelism in new[] { 8, 12, 14, 16 })
    {
      var graph = new RoslynCpgBuilder(CreateDataFlowPartitionOptions(degreeOfParallelism))
        .BuildFromSource(source, "operation-consumer-inventory.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.CallSite);
      Assert.Contains(graph.Nodes, node => node.Kind == RoslynCpgNodeKind.MemberAccess);
      Assert.Contains(graph.Edges, edge => edge.Kind == RoslynCpgEdgeKind.DataFlow);
    }
  }

  [Fact]
  public void BuildFromSource_DispatchKinds_AreStructuredForMethodsAndCallSites()
  {
    const string source = CpgBuilderSources.DispatchKinds;
    var graph = new RoslynCpgBuilder().BuildFromSource(source, "dispatch-structured.cs");

    var methodNode = Assert.Single(graph.Nodes, node =>
      node.Kind == RoslynCpgNodeKind.Method &&
      node.Name == "Helper");
    var callSiteNode = Assert.Single(graph.Nodes, node =>
      node.Kind == RoslynCpgNodeKind.CallSite &&
      node.Name == "Helper");
    var methodDispatch = Assert.IsType<RoslynCpgDispatchKind>(methodNode.DispatchKind);
    var callSiteDispatch = Assert.IsType<RoslynCpgDispatchKind>(callSiteNode.DispatchKind);

    Assert.Equal(RoslynCpgDispatchCategory.Method, methodDispatch.Category);
    Assert.Equal(
      RoslynCpgDispatchFlags.Internal |
      RoslynCpgDispatchFlags.Static |
      RoslynCpgDispatchFlags.Definition,
      methodDispatch.Flags);
    Assert.Null(methodDispatch.Action);
    Assert.Equal("internal-static-definition", methodDispatch.ToString());

    Assert.Equal(RoslynCpgDispatchCategory.Method, callSiteDispatch.Category);
    Assert.Equal(
      RoslynCpgDispatchFlags.Internal |
      RoslynCpgDispatchFlags.Static |
      RoslynCpgDispatchFlags.Dispatch |
      RoslynCpgDispatchFlags.Exact,
      callSiteDispatch.Flags);
    Assert.Null(callSiteDispatch.Action);
    Assert.Equal("internal-static-exact", callSiteDispatch.ToString());
  }

  private static void AssertGraphsEqual(RoslynCpgGraph expected, RoslynCpgGraph actual)
  {
    Assert.Equal(
      expected.Nodes
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray(),
      actual.Nodes
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray());
    Assert.Equal(
      expected.Edges
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray(),
      actual.Edges
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray());
  }

  private static void AssertTypeGraphEqual(RoslynCpgGraph expected, RoslynCpgGraph actual)
  {
    Assert.Equal(
      expected.Nodes
        .Where(node => node.Kind == RoslynCpgNodeKind.TypeRef)
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray(),
      actual.Nodes
        .Where(node => node.Kind == RoslynCpgNodeKind.TypeRef)
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray());
    Assert.Equal(
      expected.Edges
        .Where(edge => edge.Kind is RoslynCpgEdgeKind.HasType
          or RoslynCpgEdgeKind.RefersToType
          or RoslynCpgEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray(),
      actual.Edges
        .Where(edge => edge.Kind is RoslynCpgEdgeKind.HasType
          or RoslynCpgEdgeKind.RefersToType
          or RoslynCpgEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray());
  }

  private static RoslynCpgBuilderOptions CreateBuilderOptions(
    bool enableReferencedSymbolTypeReuse,
    bool enableOperationBackedSyntaxTypes = true)
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: enableReferencedSymbolTypeReuse,
      EnableOperationBackedSyntaxTypes: enableOperationBackedSyntaxTypes);
  }

  private static RoslynCpgBuilderOptions CreateSyntaxPassOptions(
    RoslynCpgSyntaxPassMode syntaxPassMode,
    int maxDegreeOfParallelism)
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6,
      SyntaxPassMode: syntaxPassMode,
      SyntaxLargeFileLineThreshold: 40);
  }

  private static RoslynCpgBuilderOptions CreateDataFlowPartitionOptions(int maxDegreeOfParallelism)
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6,
      SyntaxLargeFileLineThreshold: 40);
  }

  private static string FormatNode(RoslynCpgNode node)
  {
    return string.Join(
      "|",
      node.NodeId,
      node.Kind,
      node.DisplayKind,
      node.Name,
      node.FullName,
      node.Signature,
      node.DispatchKind?.ToString(),
      node.TypeFullName,
      node.FilePath,
      node.SpanStart,
      node.SpanEnd,
      node.IsImplicit,
      node.Text);
  }

  private static string[] DescribeDataFlowEdges(RoslynCpgGraph graph)
  {
    return graph.Edges
      .Where(edge => edge.Kind == RoslynCpgEdgeKind.DataFlow)
      .Select(edge => $"{DescribeNode(graph, FindNode(graph, edge.SourceNodeId))}->{DescribeNode(graph, FindNode(graph, edge.TargetNodeId))}")
      .OrderBy(text => text, StringComparer.Ordinal)
      .ToArray();
  }

  private static RoslynCpgNode FindNode(RoslynCpgGraph graph, NodeId nodeId)
  {
    return graph.GetNode(nodeId);
  }

  private static string DescribeNode(RoslynCpgGraph graph, RoslynCpgNode node)
  {
    var displayText = graph.GetDisplayText(node).Replace("\r\n", "\n", StringComparison.Ordinal);
    return node.Kind switch
    {
      RoslynCpgNodeKind.MethodParameter => $"MethodParameter:{node.Name}",
      RoslynCpgNodeKind.MethodReturn => $"MethodReturn:{node.Name}",
      RoslynCpgNodeKind.MethodExit => $"MethodExit:{node.Name}",
      RoslynCpgNodeKind.CallSite => $"CallSite:{node.Name}",
      _ => $"{node.Kind}:{displayText}",
    };
  }

  private static string FormatSemanticFacts(SyntaxNode syntax, Microsoft.CodeAnalysis.SemanticModel semanticModel)
  {
    var declaredSymbol = semanticModel.GetDeclaredSymbol(syntax)?.ToDisplayString() ?? "<null>";
    var referencedSymbol = semanticModel.GetSymbolInfo(syntax).Symbol?.ToDisplayString() ?? "<null>";
    var typeSymbol = semanticModel.GetTypeInfo(syntax).Type?.ToDisplayString() ?? "<null>";
    return $"{declaredSymbol}|{referencedSymbol}|{typeSymbol}";
  }

  private static NodeId RequireNodeId(RoslynCpgNode node)
  {
    return Assert.NotNull(node.NodeId);
  }

  private static T ReadTelemetryValue<T>(object telemetry, string propertyName)
  {
    var property = telemetry.GetType().GetProperty(propertyName);
    Assert.NotNull(property);
    return Assert.IsType<T>(property.GetValue(telemetry));
  }

  private static string CreateLargeSource(int methodCount, int statementsPerMethod)
  {
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("namespace Demo;");
    builder.AppendLine();
    builder.AppendLine("public sealed class LargeSample");
    builder.AppendLine("{");
    for (var methodIndex = 0; methodIndex < methodCount; methodIndex += 1)
    {
      builder.AppendLine($"  public int Run{methodIndex}(int seed)");
      builder.AppendLine("  {");
      builder.AppendLine("    var total = seed;");
      for (var statementIndex = 0; statementIndex < statementsPerMethod; statementIndex += 1)
      {
        builder.AppendLine($"    total += {statementIndex + 1};");
      }

      builder.AppendLine($"    return total > {methodIndex + statementsPerMethod} ? total : total + 1;");
      builder.AppendLine("  }");
      builder.AppendLine();
    }

    builder.AppendLine("}");
    return builder.ToString();
  }

}
