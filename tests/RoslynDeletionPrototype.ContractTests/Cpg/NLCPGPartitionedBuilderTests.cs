using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using System.Reflection;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class NLCPGPartitionedBuilderTests
{
  [Fact]
  public async Task OrderedPartitionWindow_CommitsInOrderEvenWhenHeadWorkBlocks()
  {
    var windowType = typeof(NLCPGBuilder).Assembly.GetType(
      "NLCPG.Builder.BoundedPartitionWorkWindow");
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

    var completionTask = Task.Run(() =>
      runOrdered.MakeGenericMethod(typeof(int), typeof(int)).Invoke(
        null,
        new object?[]
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
    await completionTask;

    Assert.Equal(new[] { 0, 1, 2, 3 }, committedOrders);
  }

  [Fact]
  public async Task TwoStagePartitionWindow_BoundsLookAheadAndCommitsInOrderWhenHeadCollectionBlocks()
  {
    var windowType = typeof(NLCPGBuilder).Assembly.GetType(
      "NLCPG.Builder.BoundedPartitionWorkWindow");
    Assert.NotNull(windowType);
    var runTwoStageOrdered = windowType.GetMethod(
      "RunTwoStageOrdered",
      BindingFlags.Public | BindingFlags.Static);
    Assert.NotNull(runTwoStageOrdered);

    using var firstCollectionStarted = new ManualResetEventSlim();
    using var releaseFirstCollection = new ManualResetEventSlim();
    using var secondLookAheadStarted = new ManualResetEventSlim();
    using var beyondAllowanceStarted = new ManualResetEventSlim();
    var committedOrders = new List<int>();
    var commitLock = new object();
    Func<int, int, int> collect = (input, order) =>
    {
      if (order == 0)
      {
        firstCollectionStarted.Set();
        releaseFirstCollection.Wait(TimeSpan.FromSeconds(5));
      }

      if (order == 2)
      {
        secondLookAheadStarted.Set();
      }

      if (order == 3)
      {
        beyondAllowanceStarted.Set();
      }

      return input;
    };
    Func<int, int, int> prepare = (input, _) => input;
    Func<int, int, int> solve = (input, _) => input;
    Action<int, int> commit = (_, order) =>
    {
      lock (commitLock)
      {
        committedOrders.Add(order);
      }
    };

    var completionTask = Task.Run(() =>
      runTwoStageOrdered.MakeGenericMethod(typeof(int), typeof(int), typeof(int), typeof(int)).Invoke(
        null,
        new object?[]
        {
          new[] { 10, 20, 30, 40 },
          2,
          collect,
          prepare,
          solve,
          commit,
          CancellationToken.None,
          null,
          null,
          2,
          100,
        }));

    Assert.True(firstCollectionStarted.Wait(TimeSpan.FromSeconds(5)));
    Assert.True(secondLookAheadStarted.Wait(TimeSpan.FromSeconds(5)));
    Assert.False(beyondAllowanceStarted.Wait(TimeSpan.FromMilliseconds(150)));
    releaseFirstCollection.Set();
    await completionTask;

    Assert.Equal(new[] { 0, 1, 2, 3 }, committedOrders);
  }

  [Fact]
  public void FlowSummary_UsesRoslynParameterOrdinalsAndStableReturnEndpoint()
  {
    var summary = new NLCPGFlowSummary(
      "project",
      "Demo.Sample",
      "Map",
      0,
      new[] { NLCPGFlowSummaryEndpoint.Parameter(2) },
      NLCPGFlowSummaryEndpoint.Return);

    Assert.Equal(2, Assert.Single(summary.Sources).ParameterOrdinal);
    Assert.Equal(-1, summary.Target.ParameterOrdinal);
    Assert.False(NLCPGDefaultFlowSummaries.TryGet(summary.StableKey, out _));
  }
  [Fact]
  public void BuildFromSource_InterproceduralCapability_DoesNotProjectExternalTargetEdges()
  {
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow },
    };

    var builder = new NLCPGBuilder(options);
    var graph = builder.BuildFromSource(
      "public sealed class Sample { public string Run(int value) => value.ToString(); }",
      "interprocedural-external.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.InterproceduralDataFlow);
  }

  [Fact]
  public void NLCPGEdgeKind_ContainsControlDependenceOverlayKinds()
  {
    var names = Enum.GetNames<NLCPGEdgeKind>();

    Assert.Contains("Dominates", names);
    Assert.Contains("PostDominates", names);
    Assert.Contains("ControlDependence", names);
  }

  [Fact]
  public void BuildFromSource_ControlDependenceCapability_ProjectsStableConditionalOverlays()
  {
    const string source = CpgBuilderSources.ControlDependenceOverlay;
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { NLCPGCapability.ControlDependence },
    };

    var graph = new NLCPGBuilder(options).BuildFromSource(source, "control-dependence.cs");

    var condition = Assert.Single(graph.Nodes, node => node.Kind == NLCPGNodeKind.OpBinary && graph.GetDisplayText(node) == "value > 0");
    var trueBranchAssignment = Assert.Single(graph.Nodes, node => node.Kind == NLCPGNodeKind.OpAssignment && graph.GetDisplayText(node) == "value += 1");
    var falseBranchAssignment = Assert.Single(graph.Nodes, node => node.Kind == NLCPGNodeKind.OpAssignment && graph.GetDisplayText(node) == "value -= 1");
    var entry = Assert.Single(graph.Nodes, node => node.Kind == NLCPGNodeKind.MethodEntry && node.Name == "Adjust:entry");
    var exit = Assert.Single(graph.Nodes, node => node.Kind == NLCPGNodeKind.MethodExit && node.Name == "Adjust:exit");

    Assert.Contains(graph.Controls(RequireNodeId(condition)), edge => edge.TargetNodeId == RequireNodeId(trueBranchAssignment));
    Assert.Contains(graph.Controls(RequireNodeId(condition)), edge => edge.TargetNodeId == RequireNodeId(falseBranchAssignment));
    Assert.Contains(graph.Dominates(RequireNodeId(entry)), edge => edge.TargetNodeId == RequireNodeId(condition));
    Assert.Contains(graph.GetEdges(NLCPGEdgeKind.PostDominates), edge => edge.TargetNodeId == RequireNodeId(exit));
  }

  [Fact]
  public void BuildFromSource_DefaultCapabilities_DoesNotMaterializeOverlayEdges()
  {
    var graph = new NLCPGBuilder().BuildFromSource(
      "namespace Demo; public sealed class Sample { public int Run(int value) { if (value > 0) return value; return 0; } }",
      "default-no-overlay.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.Dominates);
    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.PostDominates);
    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.ControlDependence);
  }

  [Fact]
  public void BuildFromSource_SyntaxSemanticCapability_SkipsExpensiveMethodOverlays()
  {
    var options = NLCPGBuilderOptions.CreateDefault() with
    {
      RequestedCapabilities = new[] { NLCPGCapability.SyntaxSemantic }
    };
    var builder = new NLCPGBuilder(options);

    var graph = builder.BuildFromSource(
      "namespace Demo; public sealed class Sample { public int Run(int value) => value + 1; }",
      "syntax-only.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
  }

  [Fact]
  public void BuildFromSource_PartitionedMode_ProducesSameGraphAsLegacy()
  {
    const string filePath = "partitioned-ab.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var legacyBuilder = new NLCPGBuilder(new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));
    var partitionedBuilder = new NLCPGBuilder(new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
  }

  [Fact]
  public void BuildFromSource_PartitionedSyntaxPass_PreservesGraphsAcrossDegreesOfParallelism()
  {
    const string filePath = "partitioned-syntax-pass.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var legacyGraph = new NLCPGBuilder(CreateSyntaxPassOptions(NLCPGSyntaxPassMode.Partitioned, 1))
      .BuildFromSource(source, filePath);

    foreach (var degreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      for (var iteration = 0; iteration < 10; iteration += 1)
      {
        var builder = new NLCPGBuilder(CreateSyntaxPassOptions(
          NLCPGSyntaxPassMode.Partitioned,
          degreeOfParallelism));
        var graph = builder.BuildFromSource(source, filePath);

        AssertGraphsEqual(legacyGraph, graph);
      }
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlowUsedFacts_PreservesGraphsAcrossDegreesOfParallelism()
  {
    const string filePath = "partitioned-data-flow-used-facts.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var baselineGraph = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, filePath);

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new NLCPGBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism));

      var graph = builder.BuildFromSource(source, filePath);

      AssertGraphsEqual(baselineGraph, graph);
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_PreservesComplexMethodLocalFlowShape()
  {
    const string source = CpgBuilderSources.ComplexMethodLocalFlow;
    var graph = new NLCPGBuilder(CreateDataFlowPartitionOptions(4))
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
    var graph = new NLCPGBuilder().BuildFromSource(source, "graph-correctness.cs");

    var methodNode = Assert.Single(graph.Nodes, node =>
      node.Kind == NLCPGNodeKind.SyntaxNode && node.Name == "Increment");
    var parameterNode = Assert.Single(graph.Nodes, node =>
      node.Kind == NLCPGNodeKind.SyntaxNode && node.Name == "seed");
    var localNode = Assert.Single(graph.Nodes, node =>
      node.Kind == NLCPGNodeKind.SyntaxNode && node.Name == "value");
    var seedReferences = graph.Nodes.Where(node =>
      node.Kind == NLCPGNodeKind.SyntaxNode &&
      node.DisplayKind == "IdentifierName" &&
      graph.GetDisplayText(node) == "seed").ToArray();
    var valueReferences = graph.Nodes.Where(node =>
      node.Kind == NLCPGNodeKind.SyntaxNode &&
      node.DisplayKind == "IdentifierName" &&
      graph.GetDisplayText(node) == "value").ToArray();

    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(methodNode) && edge.Kind == NLCPGEdgeKind.DeclaresSymbol);
    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(parameterNode) && edge.Kind == NLCPGEdgeKind.DeclaresSymbol);
    Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(localNode) && edge.Kind == NLCPGEdgeKind.DeclaresSymbol);
    Assert.All(seedReferences, node => Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == NLCPGEdgeKind.ReferencesSymbol));
    Assert.All(valueReferences, node => Assert.Contains(graph.Edges, edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == NLCPGEdgeKind.ReferencesSymbol));
    Assert.Contains(valueReferences, node => graph.Edges.Any(edge =>
      edge.SourceNodeId == RequireNodeId(node) && edge.Kind == NLCPGEdgeKind.HasType));
    Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.SyntaxHasOperation);
    Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.OpHasSyntax);
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_RepeatedReferencesKeepUniqueDeterministicEdges()
  {
    const string source = CpgBuilderSources.RepeatedReferences;
    var firstGraph = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "duplicate-flow-a.cs");
    var secondGraph = new NLCPGBuilder(CreateDataFlowPartitionOptions(16))
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
    var graph = new NLCPGBuilder().BuildFromSource(source, "declaration-shapes.cs");

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
        node.Kind == NLCPGNodeKind.SyntaxNode && node.DisplayKind == declarationKind).ToArray();
      Assert.True(declarationNodes.Length > 0, $"Missing {declarationKind} syntax node.");
      Assert.All(declarationNodes, node => Assert.Contains(graph.Edges, edge =>
        edge.SourceNodeId == RequireNodeId(node) && edge.Kind == NLCPGEdgeKind.DeclaresSymbol));
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
        node.Kind == NLCPGNodeKind.SyntaxNode && node.DisplayKind == declarationKind))
      {
        Assert.Contains(graph.Edges, edge =>
          edge.SourceNodeId == RequireNodeId(declarationNode) &&
          edge.Kind == NLCPGEdgeKind.SyntaxChild &&
          graph.Nodes.Any(node => node.NodeId == edge.TargetNodeId && node.Kind == NLCPGNodeKind.Method));
      }
    }
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
  public void BuildFromSource_DataFlowFactCollection_PreservesGraphAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.DataFlowFactCollection;
    var baseline = new NLCPGBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1))
      .BuildFromSource(source, "data-flow-fact-dedup.cs");

    foreach (var maxDegreeOfParallelism in new[] { 1, 8, 12, 14, 16 })
    {
      var builder = new NLCPGBuilder(CreateDataFlowPartitionOptions(maxDegreeOfParallelism));
      var graph = builder.BuildFromSource(source, "data-flow-fact-dedup.cs");

      AssertGraphsEqual(baseline, graph);
    }
  }

  [Fact]
  public void BuildFromSource_DataFlowDefinitionBudget_SkipsOnlyOverBudgetMethod()
  {
    const string source = CpgBuilderSources.DataFlowDefinitionBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new NLCPGDataFlowOptions(MaxDefinitionsPerMethod: 0)
    };
    var builder = new NLCPGBuilder(options);

    var graph = builder.BuildFromSource(source, "definition-budget.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
  }

  [Fact]
  public void BuildFromSource_DataFlowNodeBudget_SkipsOverBudgetMethod()
  {
    const string source = CpgBuilderSources.DataFlowNodeBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new NLCPGDataFlowOptions(
        MaxDefinitionsPerMethod: int.MaxValue,
        MaxFlowNodesPerMethod: 0)
    };
    var builder = new NLCPGBuilder(options);

    var graph = builder.BuildFromSource(source, "flow-node-budget.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
  }

  [Fact]
  public void BuildFromSource_DataFlowCandidateBudget_FailBuildReportsStableMethodName()
  {
    const string source = CpgBuilderSources.DataFlowCandidateBudget;
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new NLCPGDataFlowOptions(
        MaxDefinitionsPerMethod: int.MaxValue,
        MaxFlowNodesPerMethod: int.MaxValue,
        MaxCandidateEdgesPerMethod: 0,
        OverflowBehavior: NLCPGDataFlowOverflowBehavior.FailBuild),
    };

    var exception = Assert.Throws<InvalidOperationException>(() =>
      new NLCPGBuilder(options).BuildFromSource(source, "candidate-budget.cs"));

    Assert.Contains("Run", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildFromSource_DataFlowCandidateBudget_SkipMethodPreservesNonFlowGraph()
  {
    var options = CreateDataFlowPartitionOptions(maxDegreeOfParallelism: 1) with
    {
      DataFlowOptions = new NLCPGDataFlowOptions(
        MaxDefinitionsPerMethod: int.MaxValue,
        MaxFlowNodesPerMethod: int.MaxValue,
        MaxCandidateEdgesPerMethod: 0,
        OverflowBehavior: NLCPGDataFlowOverflowBehavior.SkipMethod),
    };

    var graph = new NLCPGBuilder(options)
      .BuildFromSource(CpgBuilderSources.DataFlowCandidateBudget, "candidate-budget-skip.cs");

    Assert.DoesNotContain(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
    Assert.Contains(graph.Nodes, node => node.Kind == NLCPGNodeKind.Method && node.Name == "Run");
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_PreservesCompleteGraph()
  {
    const string filePath = "symbol-type-reuse.cs";
    const string source = CpgBuilderSources.SymbolTypeReuse;
    var withoutReuseBuilder = new NLCPGBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new NLCPGBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, filePath);
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_FallsBackForMethodGroupsAndDynamicSyntax()
  {
    const string source = CpgBuilderSources.SymbolTypeReuseFallback;
    var withoutReuseBuilder = new NLCPGBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new NLCPGBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
  }

  [Fact]
  public void BuildFromSource_OperationBackedSyntaxTypes_PreservesTypeEdges()
  {
    const string source = CpgBuilderSources.OperationBackedSyntaxTypes;
    var syntaxOnlyBuilder = new NLCPGBuilder(CreateBuilderOptions(
      enableReferencedSymbolTypeReuse: true,
      enableOperationBackedSyntaxTypes: false));
    var operationBackedBuilder = new NLCPGBuilder(CreateBuilderOptions(
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
    var legacyBuilder = new NLCPGBuilder(new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));
    var partitionedBuilder = new NLCPGBuilder(new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
  }

  [Fact]
  public void BuildFromSource_PartitionedSyntaxPass_PreservesSemanticEdgesForGenericAccessorAndLocalFunction()
  {
    const string source = CpgBuilderSources.SyntaxSemanticShapes;
    var baseline = new NLCPGBuilder(CreateSyntaxPassOptions(NLCPGSyntaxPassMode.Partitioned, 1))
      .BuildFromSource(source, "syntax-semantic-shapes.cs");

    foreach (var degreeOfParallelism in new[] { 8, 16 })
    {
      var graph = new NLCPGBuilder(CreateSyntaxPassOptions(
        NLCPGSyntaxPassMode.Partitioned,
        degreeOfParallelism)).BuildFromSource(source, "syntax-semantic-shapes.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.HasType);
      Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.RefersToType);
    }
  }

  [Fact]
  public void BuildFromSource_PartitionedDataFlow_PreservesCallReturnAndPropertyFlowsAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.DataFlowCallReturnAndProperty;
    var baseline = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "dataflow-call-property.cs");

    foreach (var degreeOfParallelism in new[] { 8, 16 })
    {
      var graph = new NLCPGBuilder(CreateDataFlowPartitionOptions(degreeOfParallelism))
        .BuildFromSource(source, "dataflow-call-property.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
      Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.ParameterLink);
    }
  }

  [Fact]
  public void BuildFromSource_OperationConsumers_PreserveCallMemberAndDataFlowAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.DataFlowCallReturnAndProperty;
    var baseline = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "operation-consumer-inventory.cs");

    foreach (var degreeOfParallelism in new[] { 8, 12, 14, 16 })
    {
      var graph = new NLCPGBuilder(CreateDataFlowPartitionOptions(degreeOfParallelism))
        .BuildFromSource(source, "operation-consumer-inventory.cs");

      AssertGraphsEqual(baseline, graph);
      Assert.Contains(graph.Nodes, node => node.Kind == NLCPGNodeKind.CallSite);
      Assert.Contains(graph.Nodes, node => node.Kind == NLCPGNodeKind.MemberAccess);
      Assert.Contains(graph.Edges, edge => edge.Kind == NLCPGEdgeKind.DataFlow);
    }
  }

  [Fact]
  public void BuildFromSource_DispatchKinds_AreStructuredForMethodsAndCallSites()
  {
    const string source = CpgBuilderSources.DispatchKinds;
    var graph = new NLCPGBuilder().BuildFromSource(source, "dispatch-structured.cs");

    var methodNode = Assert.Single(graph.Nodes, node =>
      node.Kind == NLCPGNodeKind.Method &&
      node.Name == "Helper");
    var callSiteNode = Assert.Single(graph.Nodes, node =>
      node.Kind == NLCPGNodeKind.CallSite &&
      node.Name == "Helper");
    var methodDispatch = Assert.IsType<NLCPGDispatchKind>(methodNode.DispatchKind);
    var callSiteDispatch = Assert.IsType<NLCPGDispatchKind>(callSiteNode.DispatchKind);

    Assert.Equal(NLCPGDispatchCategory.Method, methodDispatch.Category);
    Assert.Equal(
      NLCPGDispatchFlags.Internal |
      NLCPGDispatchFlags.Static |
      NLCPGDispatchFlags.Definition,
      methodDispatch.Flags);
    Assert.Null(methodDispatch.Action);
    Assert.Equal("internal-static-definition", methodDispatch.ToString());

    Assert.Equal(NLCPGDispatchCategory.Method, callSiteDispatch.Category);
    Assert.Equal(
      NLCPGDispatchFlags.Internal |
      NLCPGDispatchFlags.Static |
      NLCPGDispatchFlags.Dispatch |
      NLCPGDispatchFlags.Exact,
      callSiteDispatch.Flags);
    Assert.Null(callSiteDispatch.Action);
    Assert.Equal("internal-static-exact", callSiteDispatch.ToString());
  }

  [Fact]
  public void BuildFromSource_CallTargetResolution_PreservesEndpointOrderAndDispatch()
  {
    var graph = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(CpgBuilderSources.CallTargetResolution, "call-target-resolution.cs");

    Assert.Equal(
      new[]
      {
        "baseValue.Virtual(value)|internal-virtual-dispatch-exact|Virtual",
        "contract.Apply(value)|internal-interface-dispatch-exact|Apply",
        "System.Math.Abs(value)|external-static-external-fallback|Abs",
        "value.Extend()|internal-extension-static-exact|Extend",
      },
      DescribeCallTargets(graph));
  }

  [Fact]
  public void BuildFromSource_CallTargetResolution_PreservesGraphAcrossDegreesOfParallelism()
  {
    const string source = CpgBuilderSources.CallTargetResolution;
    var baseline = new NLCPGBuilder(CreateDataFlowPartitionOptions(1))
      .BuildFromSource(source, "call-target-resolution.cs");

    foreach (var degreeOfParallelism in new[] { 8, 12, 14, 16 })
    {
      var graph = new NLCPGBuilder(CreateDataFlowPartitionOptions(degreeOfParallelism))
        .BuildFromSource(source, "call-target-resolution.cs");

      AssertGraphsEqual(baseline, graph);
    }
  }

  private static void AssertGraphsEqual(NLCPGGraph expected, NLCPGGraph actual)
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

  private static void AssertTypeGraphEqual(NLCPGGraph expected, NLCPGGraph actual)
  {
    Assert.Equal(
      expected.Nodes
        .Where(node => node.Kind == NLCPGNodeKind.TypeRef)
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray(),
      actual.Nodes
        .Where(node => node.Kind == NLCPGNodeKind.TypeRef)
        .OrderBy(node => node.NodeId)
        .ThenBy(node => node.FullName, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray());
    Assert.Equal(
      expected.Edges
        .Where(edge => edge.Kind is NLCPGEdgeKind.HasType
          or NLCPGEdgeKind.RefersToType
          or NLCPGEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray(),
      actual.Edges
        .Where(edge => edge.Kind is NLCPGEdgeKind.HasType
          or NLCPGEdgeKind.RefersToType
          or NLCPGEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceNodeId)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetNodeId)
        .ThenBy(edge => edge.StructuredLabel?.StableKey, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
        .ToArray());
  }

  private static NLCPGBuilderOptions CreateBuilderOptions(bool enableReferencedSymbolTypeReuse, bool enableOperationBackedSyntaxTypes = true)
  {
    return new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: enableReferencedSymbolTypeReuse,
      EnableOperationBackedSyntaxTypes: enableOperationBackedSyntaxTypes);
  }

  private static NLCPGBuilderOptions CreateSyntaxPassOptions(NLCPGSyntaxPassMode syntaxPassMode, int maxDegreeOfParallelism)
  {
    return new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6,
      SyntaxPassMode: syntaxPassMode,
      SyntaxLargeFileLineThreshold: 40);
  }

  private static NLCPGBuilderOptions CreateDataFlowPartitionOptions(int maxDegreeOfParallelism)
  {
    return new NLCPGBuilderOptions(
      NLCPGBuilderMode.Partitioned,
      MaxDegreeOfParallelism: maxDegreeOfParallelism,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6,
      SyntaxLargeFileLineThreshold: 40);
  }

  private static string FormatNode(NLCPGNode node)
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

  private static string[] DescribeDataFlowEdges(NLCPGGraph graph)
  {
    return graph.Edges
      .Where(edge => edge.Kind == NLCPGEdgeKind.DataFlow)
      .Select(edge => $"{DescribeNode(graph, FindNode(graph, edge.SourceNodeId))}->{DescribeNode(graph, FindNode(graph, edge.TargetNodeId))}")
      .OrderBy(text => text, StringComparer.Ordinal)
      .ToArray();
  }

  private static string[] DescribeCallTargets(NLCPGGraph graph)
  {
    return graph.Nodes
      .Where(node => node.Kind == NLCPGNodeKind.CallSite)
      .OrderBy(node => node.SpanStart)
      .Select(callSite =>
      {
        var targets = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.CallTargets && edge.SourceNodeId == RequireNodeId(callSite))
          .Select(edge => FindNode(graph, edge.TargetNodeId).FullName)
          .ToArray();
        return $"{graph.GetDisplayText(callSite)}|{callSite.DispatchKind}|{string.Join(",", targets)}";
      })
      .ToArray();
  }

  private static NLCPGNode FindNode(NLCPGGraph graph, NodeId nodeId)
  {
    return graph.GetNode(nodeId);
  }

  private static string DescribeNode(NLCPGGraph graph, NLCPGNode node)
  {
    var displayText = graph.GetDisplayText(node).Replace("\r\n", "\n", StringComparison.Ordinal);
    return node.Kind switch
    {
      NLCPGNodeKind.MethodParameter => $"MethodParameter:{node.Name}",
      NLCPGNodeKind.MethodReturn => $"MethodReturn:{node.Name}",
      NLCPGNodeKind.MethodExit => $"MethodExit:{node.Name}",
      NLCPGNodeKind.CallSite => $"CallSite:{node.Name}",
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

  private static NodeId RequireNodeId(NLCPGNode node)
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
