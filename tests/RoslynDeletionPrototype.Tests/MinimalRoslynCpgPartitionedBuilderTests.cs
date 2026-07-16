using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Analysis.FlowSummaries;
using MinimalRoslynCpg.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MinimalRoslynCpgPartitionedBuilderTests
{
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
    const string source =
      """
      public sealed class Sample
      {
        public int Adjust(int value)
        {
          if (value > 0)
          {
            value += 1;
          }
          else
          {
            value -= 1;
          }

          return value;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class SmallSample
      {
        public int Run(int value)
        {
          return value + 1;
        }
      }
      """;
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
  public void BuildFromSource_PartitionedDataFlow_PreservesComplexMethodLocalFlowShape()
  {
    const string source = """
      namespace Demo;

      public sealed class FlowShapeSample
      {
        public int Run(int seed)
        {
          var current = seed;
          if (seed > 0)
          {
            current = seed + 1;
          }

          while (current < 3)
          {
            current = current + 1;
          }

          return Echo(current);
        }

        private static int Echo(int value)
        {
          return value;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class GraphSample
      {
        public int Increment(int seed)
        {
          var value = seed + 1;
          return value;
        }
      }
      """;
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
    const string source = """
      namespace Demo;

      public sealed class DuplicateFlowSample
      {
        public int Run(int seed)
        {
          var value = seed + 1;
          return value + value + value;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public delegate int Transformer<T>(T value);

      public enum State
      {
        Ready,
      }

      public sealed class DeclarationShapes<T>
      {
        private int _field;
        public event System.Action? Changed;
        public event System.Action? CustomChanged
        {
          add { }
          remove { }
        }
        public int Property { get; set; }
        public int this[int index] { get => index; set => _field = value; }

        public DeclarationShapes(int parameter)
        {
          _field = parameter;
        }

        public static DeclarationShapes<T> operator +(
          DeclarationShapes<T> left,
          DeclarationShapes<T> right) => left;

        public static implicit operator int(DeclarationShapes<T> value) => value._field;

        public int Run<TMethod>(int parameter)
        {
          var local = parameter;
          if (local is int pattern)
          {
          label:
            int LocalFunction(int localParameter) => localParameter + pattern;
            return LocalFunction(local);
          }

          return 0;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class QueryTelemetrySample
      {
        public int Run(int value)
        {
          var total = value + 1;
          total = total * 2;
          return total > 3 ? total : total + 4;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class TypeInfoSample
      {
        private int _field;
        public int Property { get; set; }

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          return new System.Collections.Generic.List<int> { local }.Count;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class TelemetrySample
      {
        private int _field;
        public int Property { get; set; }

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          return local;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class DataFlowDedupSample
      {
        public int Run(int input)
        {
          var first = input + 1;
          var second = Transform(first * (input + 2));
          return second + first;
        }

        private static int Transform(int value) => value;
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class DefinitionBudgetSample
      {
        public int Run(int input)
        {
          var first = input + 1;
          var second = first + 1;
          return second;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class FlowNodeBudgetSample
      {
        public int Run(int input)
        {
          return input + 1;
        }
      }
      """;
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
    const string source =
      "namespace Demo; public sealed class Sample { public int Run(int value) { return value + 1; } }";
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
    const string source =
      """
      namespace Demo;

      public sealed class BudgetSample
      {
        public int WithinBudget(int input)
        {
          var result = input + 1;
          return result;
        }

        public int OverBudget(int input)
        {
          var first = input + 1;
          var second = first + 1;
          return second;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class TypeInfoFallbackSample
      {
        private int _field = 1 + 2;

        public void Run(int parameter)
        {
          System.Action action = Log;
          action();
        }

        private void Log(int value)
        {
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class SymbolTypeSample
      {
        private int _field;
        public int Property { get; set; }
        public event System.Action? Changed;

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          Changed?.Invoke();
          return local;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class FallbackSample
      {
        private static int Transform(int value) => value + 1;

        public int Run(dynamic dynamicValue)
        {
          System.Func<int, int> methodGroup = Transform;
          var conditional = dynamicValue?.ToString();
          return methodGroup(conditional is null ? 0 : 1);
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class OperationTypeSample
      {
        private int _value;

        public int Run(int parameter)
        {
          var local = parameter + _value;
          return local > 0 ? local : local + 1;
        }
      }
      """;
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
    const string source =
      """
      namespace Demo;

      public sealed class ComplexSample
      {
        public int Run(int input)
        {
          var total = input;
          while (total < 5)
          {
            total += 1;
            if (total == 3)
            {
              continue;
            }
          }

          for (var index = 0; index < 2; index += 1)
          {
            total += index;
          }

          switch (total)
          {
            case 0:
              total += 10;
              break;
            case 1:
            case 2:
              total += 20;
              break;
            default:
              total += 30;
              break;
          }

          try
          {
            total += Helper(total);
          }
          catch (System.InvalidOperationException)
          {
            total -= 1;
          }
          finally
          {
            total += 100;
          }

          return total;
        }

        private static int Helper(int value)
        {
          return value > 10 ? value : value + 1;
        }
      }
      """;
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
    const string source = """
      namespace Demo;
      public sealed class Box<T> { public T Value { get; set; } = default!; }
      public sealed class Sample
      {
        public int Run(Box<int> box)
        {
          int Convert() => box.Value + 1;
          return Convert();
        }
      }
      """;
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
    const string source = """
      namespace Demo;
      public sealed class Counter { public int Value { get; set; } }
      public sealed class Sample
      {
        public int Run(Counter counter, int seed)
        {
          counter.Value = seed;
          var next = Increment(counter.Value);
          return next;
        }
        private static int Increment(int value) => value + 1;
      }
      """;
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
  public void BuildFromSource_DispatchKinds_AreStructuredForMethodsAndCallSites()
  {
    const string source = """
      namespace Demo;

      public sealed class DispatchSample
      {
        private static int Helper(int value) => value + 1;

        public int Run(int value)
        {
          return Helper(value);
        }
      }
      """;
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
