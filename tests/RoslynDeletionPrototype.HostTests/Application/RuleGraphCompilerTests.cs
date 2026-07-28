using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using NLISSN.Application;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RuleGraphCompilerTests
{
    [Fact]
    public void Analyze_WithSObjectPipelineGraphExecution_PreservesLegacyStageResults()
    {
        const string source = """
          public sealed class Box { public int Value; }
          public sealed class Demo { int Run(Box s) { var value = s.Value; return value; } }
          """;
        var rules = new SObjectRuleSet();
        var legacy = new ApplicationService(new RulePipeline(
          rules.Markers, rules.Propagators, rules.Lifters, rules.Proposers));
        var graph = new ApplicationService(new RulePipeline(
          rules.Markers, rules.Propagators, rules.Lifters, rules.Proposers,
          EnableRuleGraphExecution: true));
        var options = new Dictionary<string, string> { ["target-name"] = "s", ["skip-rewrite"] = "true" };

        var legacyResult = legacy.Analyze(source, "graph-sobj.cs", options);
        var graphResult = graph.Analyze(source, "graph-sobj.cs", options);

        Assert.Equal(Project(legacyResult.SeedMarks), Project(graphResult.SeedMarks));
        Assert.Equal(Project(legacyResult.PropagatedMarks.Select(mark => mark.Mark)), Project(graphResult.PropagatedMarks.Select(mark => mark.Mark)));
        Assert.Equal(Project(legacyResult.LiftedMarks.Select(mark => mark.Mark)), Project(graphResult.LiftedMarks.Select(mark => mark.Mark)));
        Assert.Equal(
          legacyResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}"),
          graphResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}"));
    }

    [Fact]
    public void Analyze_WithClassPipelineGraphExecution_PreservesLegacyStageResults()
    {
        const string source = """
          public sealed class PlayerInput { }
          public sealed class Demo
          {
            PlayerInput field = new PlayerInput();
            void Run() { var input = new PlayerInput(); System.Console.Write(input); }
          }
          """;
        var rules = new ClassRuleSet();
        var legacy = new ApplicationService(new RulePipeline(
          rules.Markers, rules.Propagators, rules.Lifters, rules.Proposers));
        var graph = new ApplicationService(new RulePipeline(
          rules.Markers, rules.Propagators, rules.Lifters, rules.Proposers,
          EnableRuleGraphExecution: true));
        var options = new Dictionary<string, string> { ["delete-class"] = "PlayerInput", ["skip-rewrite"] = "true" };

        var legacyResult = legacy.Analyze(source, "graph-class.cs", options);
        var graphResult = graph.Analyze(source, "graph-class.cs", options);

        Assert.Equal(Project(legacyResult.SeedMarks), Project(graphResult.SeedMarks));
        Assert.Equal(Project(legacyResult.PropagatedMarks.Select(mark => mark.Mark)), Project(graphResult.PropagatedMarks.Select(mark => mark.Mark)));
        Assert.Equal(Project(legacyResult.LiftedMarks.Select(mark => mark.Mark)), Project(graphResult.LiftedMarks.Select(mark => mark.Mark)));
        Assert.Equal(
          legacyResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}"),
          graphResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}"));
    }

    [Fact]
    public void Analyze_WithGraphDependenciesAcrossDifferentGroupKeys_ExecutesLiftAndPropose()
    {
        const string source = "public sealed class Demo { }";
        var marker = new CrossGroupMarker();
        var lifter = new CrossGroupLifter();
        var proposer = new CrossGroupProposer();
        var service = new ApplicationService(new RulePipeline(
          new[] { marker },
          Array.Empty<RuleDefinitionPropagate>(),
          new[] { lifter },
          new[] { proposer },
          EnableRuleGraphExecution: true));

        var result = service.Analyze(source, "graph-cross-group.cs", new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        Assert.Single(result.SeedMarks);
        Assert.Single(result.LiftedMarks);
        Assert.Single(result.Decisions);
        Assert.Equal(CrossGroupProposer.Id, result.Decisions[0].Reason);
    }

    [Fact]
    public void Analyze_WhenTypedOutputIsAbsent_DoesNotRouteGenericOutputToTypedConsumer()
    {
        var service = new ApplicationService(new RulePipeline(
          new RuleDefinitionMark[] { new TypedPortMarker() },
          new RuleDefinitionPropagate[]
          {
              new GenericPropagatedOutputProducer(),
              new LocalReferenceOnlyConsumer()
          },
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>(),
          EnableRuleGraphExecution: true));

        var result = service.Analyze(
          "public sealed class Demo { }",
          "typed-port.cs",
          new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        Assert.DoesNotContain(
          result.PropagatedMarks,
          mark => mark.RuleId == LocalReferenceOnlyConsumer.Id);
    }

    [Fact]
    public void Analyze_WithDefaultRuleGraph_DopOneAndSixteenProduceEquivalentResults()
    {
        const string source = """
          public sealed class PlayerInput { public int Value; }
          public sealed class Demo
          {
              PlayerInput field = new PlayerInput();
              int Run(PlayerInput s) { var value = s.Value; return value + field.Value; }
          }
          """;
        var singleThread = new ApplicationService(RuleRegistry.CreateDefaultRules());
        var parallel = new ApplicationService(RuleRegistry.CreateDefaultRules());
        var baseOptions = new Dictionary<string, string>
        {
            ["target-name"] = "s",
            ["delete-class"] = "PlayerInput",
            ["cpg-max-degree-of-parallelism"] = "1"
        };
        var one = new Dictionary<string, string>(baseOptions) { ["max-degree-of-parallelism"] = "1" };
        var sixteen = new Dictionary<string, string>(baseOptions) { ["max-degree-of-parallelism"] = "16" };

        var singleThreadResult = singleThread.Analyze(source, "graph-dop.cs", one);
        var parallelResult = parallel.Analyze(source, "graph-dop.cs", sixteen);

        Assert.Equal(Project(singleThreadResult.SeedMarks), Project(parallelResult.SeedMarks));
        Assert.Equal(Project(singleThreadResult.PropagatedMarks.Select(mark => mark.Mark)), Project(parallelResult.PropagatedMarks.Select(mark => mark.Mark)));
        Assert.Equal(Project(singleThreadResult.LiftedMarks.Select(mark => mark.Mark)), Project(parallelResult.LiftedMarks.Select(mark => mark.Mark)));
        Assert.Equal(
          singleThreadResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}:{decision.Reason}"),
          parallelResult.Decisions.Select(decision => $"{decision.Action}:{decision.FinalNode.SpanStart}:{decision.FinalNode.Span.Length}:{decision.Reason}"));
        Assert.Equal(singleThreadResult.RewrittenSource, parallelResult.RewrittenSource);
        Assert.Equal(singleThreadResult.Diff.ToString(), parallelResult.Diff.ToString());
        Assert.NotNull(singleThreadResult.RuleGraphTelemetry);
        Assert.NotNull(parallelResult.RuleGraphTelemetry);
        Assert.Equal(
          RuleRegistry.CreateDefaultRules().CompileRuleGraph().Nodes.Count,
          singleThreadResult.RuleGraphTelemetry!.Count);
        Assert.Equal(
          singleThreadResult.RuleGraphTelemetry.Select(node => node.NodeId),
          parallelResult.RuleGraphTelemetry!.Select(node => node.NodeId));
        Assert.All(singleThreadResult.RuleGraphTelemetry, node => Assert.True(node.InputCount >= 0 && node.OutputCount >= 0 && node.ElapsedMilliseconds >= 0));
    }

    [Fact]
    public void CompileRuleGraph_WithDefaultPipeline_CreatesOneNodePerRule()
    {
        var pipeline = RuleRegistry.CreateDefaultRules();

        var graph = pipeline.CompileRuleGraph();

        var expectedCount = pipeline.Markers.Count +
          pipeline.Propagators.Count +
          pipeline.Lifters.Count +
          pipeline.Proposers.Count;
        Assert.Equal(expectedCount, graph.Nodes.Count);
        Assert.Equal(expectedCount, graph.Nodes.Select(node => node.NodeId).Distinct().Count());
        Assert.Contains(graph.Nodes, node =>
          node.NodeId.Value == "Mark:DEL-SOBJ-MARK-ID-001" && node.Kind == RuleKind.Mark);
        Assert.Contains(graph.Nodes, node =>
          node.NodeId.Value == "Propagate:DEL-SOBJ-PROP-SYMBOL-001" && node.Kind == RuleKind.Propagate);
    }

    [Fact]
    public void CompileRuleGraph_WithDefaultPipeline_UsesExplicitInputsForEveryConsumerNode()
    {
        var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
        var independentCompatibilityNodes = new[]
        {
            "Propose:DEL-CLASS-PROP-PARAM-001",
            "Propose:DEL-CLASS-PROP-PUBLIC-PARAM-001"
        };

        Assert.All(
          graph.Nodes.Where(node =>
            node.Kind != RuleKind.Mark &&
            !independentCompatibilityNodes.Contains(node.NodeId.Value)),
          node => Assert.NotEmpty(node.Dependencies));
        Assert.All(
          graph.Nodes.Where(node => node.Dependencies.Count == 0 && node.Kind != RuleKind.Mark),
          node => Assert.Contains(node.NodeId.Value, independentCompatibilityNodes));
    }

    [Fact]
    public void CompileRuleGraph_WhenDeclaredProducerIsMissing_ThrowsInsteadOfDroppingDependency()
    {
        var pipeline = new RulePipeline(
          Array.Empty<RuleDefinitionMark>(),
          Array.Empty<RuleDefinitionPropagate>(),
          new RuleDefinitionLift[] { new MissingProducerLifter() },
          Array.Empty<RuleDefinitionPropose>());

        var exception = Assert.Throws<InvalidOperationException>(pipeline.CompileRuleGraph);

        Assert.Equal(
          "Rule node 'Lift:TEST-GRAPH-MISSING-PRODUCER-001' depends on unknown producer 'Mark:TEST-GRAPH-MISSING-MARK-001'.",
          exception.Message);
    }

    [Fact]
    public void CompileRuleGraph_WithDefaultPipeline_DeclaresTypedSymbolReferenceDependencies()
    {
        var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();

        var sObjectReference = graph.Nodes.Single(node =>
          node.NodeId.Value == "Propagate:DEL-SOBJ-PROP-SYMBOL-001");
        var classReference = graph.Nodes.Single(node =>
          node.NodeId.Value == "Propagate:DEL-CLASS-PROP-LOCAL-REF-001");
        var sObjectSwitch = graph.Nodes.Single(node =>
          node.NodeId.Value == "Lift:DEL-SOBJ-LIFT-SWITCH-001");
        var classSwitch = graph.Nodes.Single(node =>
          node.NodeId.Value == "Lift:DEL-CLASS-LIFT-SWITCH-001");

        Assert.Contains(
          sObjectReference.Dependencies,
          dependency => dependency.Producer.Value == "Propagate:DEL-SOBJ-PROP-DECL-INIT-001" &&
            dependency.RequiredOutput == RuleOutputKind.LocalDefinitionFromInitializer);
        Assert.Contains(
          classReference.Dependencies,
          dependency => dependency.Producer.Value == "Propagate:DEL-CLASS-PROP-NEW-DECL-001" &&
            dependency.RequiredOutput == RuleOutputKind.LocalDefinitionFromObjectCreation);
        Assert.Equal(2, sObjectSwitch.Dependencies.Count);
        Assert.Equal(2, classSwitch.Dependencies.Count);
        Assert.Contains(sObjectSwitch.Dependencies, dependency =>
          dependency.Producer.Value == "Lift:DEL-SOBJ-LIFT-HOST-001" &&
          dependency.RequiredOutput == RuleOutputKind.ExpressionHost);
        Assert.Contains(sObjectSwitch.Dependencies, dependency =>
          dependency.Producer.Value == "Lift:DEL-SOBJ-LIFT-IF-001" &&
          dependency.RequiredOutput == RuleOutputKind.IfStructure);
    }

    [Fact]
    public void CompileRuleGraph_WithDefaultPipeline_UsesAtomicProposalDependencies()
    {
        var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();

        var logical = graph.Nodes.Single(node =>
          node.NodeId.Value == "Propose:DEL-SOBJ-PROPOSE-LOGIC-001");
        var classReturn = graph.Nodes.Single(node =>
          node.NodeId.Value == "Propose:DEL-CLASS-PROP-RETURN-001");
        var privateParameter = graph.Nodes.Single(node =>
          node.NodeId.Value == "Propose:DEL-CLASS-PROP-PRIVATE-PARAM-SHRINK-001");
        var sObjectSwitch = graph.Nodes.Single(node =>
          node.NodeId.Value == "Lift:DEL-SOBJ-LIFT-SWITCH-001");

        Assert.Equal(
          new[] { "Propagate:DEL-SOBJ-PROP-LOGIC-GROUP-001" },
          logical.Dependencies.Select(dependency => dependency.Producer.Value));
        Assert.Equal(RuleOutputKind.PropagatedMark, Assert.Single(logical.Dependencies).RequiredOutput);
        Assert.Equal(
          new[] { "Propagate:DEL-CLASS-PROP-DECL-HOST-001" },
          classReturn.Dependencies.Select(dependency => dependency.Producer.Value));
        Assert.Equal(
          new[] { "Propagate:DEL-CLASS-PROP-METHOD-PARAM-USAGE-001" },
          privateParameter.Dependencies.Select(dependency => dependency.Producer.Value));
        Assert.Equal(2, sObjectSwitch.Dependencies.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProducerReturnsEmptyOutput_RunsDependentNodeOnce()
    {
        var producer = Node("mark-a", RuleKind.Mark, RuleOutputKind.SeedMark);
        var consumer = Node(
          "propagate-a",
          RuleKind.Propagate,
          RuleOutputKind.LocalReference,
          new RuleDependency(new RuleNodeId("mark-a"), RuleOutputKind.SeedMark));
        var graph = new RuleGraphCompiler().Compile(new[] { producer, consumer });
        var consumerCalls = 0;

        var result = await new RuleGraphExecutor().ExecuteAsync(
          graph,
          new[]
          {
            new RuleGraphExecutionNode(producer, (_, _) => Task.FromResult(RuleNodeResult.Empty)),
            new RuleGraphExecutionNode(
              consumer,
              (inputs, _) =>
              {
                  consumerCalls++;
                  Assert.Empty(inputs.GetOutputs(new RuleNodeId("mark-a"), RuleOutputKind.SeedMark));
                  return Task.FromResult(RuleNodeResult.Empty);
              })
          },
          maxDegreeOfParallelism: 2);

        Assert.Equal(1, consumerCalls);
        Assert.Equal(new[] { "mark-a", "propagate-a" }, result.Nodes.Select(node => node.NodeId.Value));
    }

    [Fact]
    public async Task ExecuteAsync_WithIndependentNodes_UsesGraphOrderInsteadOfCompletionOrder()
    {
        var slow = Node("slow", RuleKind.Mark, RuleOutputKind.SeedMark);
        var fast = Node("fast", RuleKind.Mark, RuleOutputKind.SeedMark);
        var graph = new RuleGraphCompiler().Compile(new[] { slow, fast });

        var result = await new RuleGraphExecutor().ExecuteAsync(
          graph,
          new[]
          {
            new RuleGraphExecutionNode(
              slow,
              async (_, cancellationToken) =>
              {
                  await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                  return RuleNodeResult.From(RuleOutputKind.SeedMark, "slow");
              }),
            new RuleGraphExecutionNode(
              fast,
              (_, _) => Task.FromResult(RuleNodeResult.From(RuleOutputKind.SeedMark, "fast")))
          },
          maxDegreeOfParallelism: 2);

        Assert.Equal(new[] { "slow", "fast" }, result.Nodes.Select(node => node.NodeId.Value));
        Assert.Equal(new object[] { "slow" }, result.GetOutputs(new RuleNodeId("slow"), RuleOutputKind.SeedMark));
        Assert.Equal(new object[] { "fast" }, result.GetOutputs(new RuleNodeId("fast"), RuleOutputKind.SeedMark));
    }

    [Fact]
    public async Task ExecuteAsync_WithMoreReadyNodesThanDegree_ReportsQueueAndConcurrencyPeaks()
    {
        var first = Node("first", RuleKind.Mark, RuleOutputKind.SeedMark);
        var second = Node("second", RuleKind.Mark, RuleOutputKind.SeedMark);
        var third = Node("third", RuleKind.Mark, RuleOutputKind.SeedMark);
        var graph = new RuleGraphCompiler().Compile(new[] { first, second, third });

        var result = await new RuleGraphExecutor().ExecuteAsync(
          graph,
          new[]
          {
              new RuleGraphExecutionNode(first, (_, _) => Task.FromResult(RuleNodeResult.Empty)),
              new RuleGraphExecutionNode(second, (_, _) => Task.FromResult(RuleNodeResult.Empty)),
              new RuleGraphExecutionNode(third, (_, _) => Task.FromResult(RuleNodeResult.Empty))
          },
          maxDegreeOfParallelism: 2);

        Assert.NotNull(result.Metrics);
        Assert.Equal(3, result.Metrics!.PeakReadyNodeCount);
        Assert.Equal(2, result.Metrics.PeakConcurrentNodeCount);
    }

    [Fact]
    public void Compile_WithIndependentNodesAndDependency_UsesStableTopologicalOrder()
    {
        var nodes = new[]
        {
            Node("mark-b", RuleKind.Mark, RuleOutputKind.SeedMark),
            Node("mark-a", RuleKind.Mark, RuleOutputKind.SeedMark),
            Node(
              "propagate-a",
              RuleKind.Propagate,
              RuleOutputKind.LocalReference,
              new RuleDependency(new RuleNodeId("mark-a"), RuleOutputKind.SeedMark))
        };

        var compiled = new RuleGraphCompiler().Compile(nodes);

        Assert.Equal(
          new[] { "mark-b", "mark-a", "propagate-a" },
          compiled.Nodes.Select(node => node.NodeId.Value));
    }

    [Fact]
    public void Compile_WhenDependencyProducerIsMissing_ThrowsDeterministicException()
    {
        var node = Node(
          "propagate-a",
          RuleKind.Propagate,
          RuleOutputKind.LocalReference,
          new RuleDependency(new RuleNodeId("missing"), RuleOutputKind.SeedMark));

        var exception = Assert.Throws<InvalidOperationException>(
          () => new RuleGraphCompiler().Compile(new[] { node }));

        Assert.Equal("Rule node 'propagate-a' depends on unknown producer 'missing'.", exception.Message);
    }

    [Fact]
    public void Compile_WhenDependencyOutputIsNotProduced_ThrowsDeterministicException()
    {
        var producer = Node("mark-a", RuleKind.Mark, RuleOutputKind.SeedMark);
        var consumer = Node(
          "propagate-a",
          RuleKind.Propagate,
          RuleOutputKind.LocalReference,
          new RuleDependency(new RuleNodeId("mark-a"), RuleOutputKind.LocalReference));

        var exception = Assert.Throws<InvalidOperationException>(
          () => new RuleGraphCompiler().Compile(new[] { producer, consumer }));

        Assert.Equal(
          "Rule node 'propagate-a' requires output 'LocalReference' from producer 'mark-a', but it is not produced.",
          exception.Message);
    }

    [Fact]
    public void Compile_WhenGraphContainsCycle_ThrowsDeterministicException()
    {
        var first = Node(
          "first",
          RuleKind.Mark,
          RuleOutputKind.SeedMark,
          new RuleDependency(new RuleNodeId("second"), RuleOutputKind.LocalReference));
        var second = Node(
          "second",
          RuleKind.Propagate,
          RuleOutputKind.LocalReference,
          new RuleDependency(new RuleNodeId("first"), RuleOutputKind.SeedMark));

        var exception = Assert.Throws<InvalidOperationException>(
          () => new RuleGraphCompiler().Compile(new[] { first, second }));

        Assert.Equal("Rule graph contains a cycle: first, second.", exception.Message);
    }

    private static RuleGraphNode Node(
      string nodeId,
      RuleKind kind,
      RuleOutputKind output,
      params RuleDependency[] dependencies)
    {
        return new RuleGraphNode(
          new RuleNodeId(nodeId),
          kind,
          new[] { output },
          dependencies);
    }

    private static IReadOnlyList<string> Project(IEnumerable<NLISSN.Core.Marking.MarkRecord> marks)
    {
        return marks.Select(mark => $"{mark.RuleId}:{mark.SyntaxNode.SpanStart}:{mark.SyntaxNode.Span.Length}").ToList();
    }

    private sealed class CrossGroupMarker : RuleDefinitionMark
    {
        public const string Id = "TEST-GRAPH-MARK-001";

        public override string RuleId => Id;

        public override string GroupKey => "marker-group";

        public override string Name => "Create a graph test seed mark.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            yield return new MarkRecord(RuleId, root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Single(), null, null, RuleId);
        }
    }

    private sealed class TypedPortMarker : RuleDefinitionMark
    {
        public const string Id = "TEST-GRAPH-TYPED-PORT-MARK-001";

        public override string RuleId => Id;

        public override string Name => "Create a seed for typed output routing.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
          new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            var declaration = root.DescendantNodes()
              .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
              .Single();
            yield return new MarkRecord(Id, declaration, null, null, Id);
        }
    }

    private sealed class GenericPropagatedOutputProducer : RuleDefinitionPropagate
    {
        public const string Id = "TEST-GRAPH-GENERIC-PROPAGATE-001";

        public override string RuleId => Id;

        public override string Name => "Produce a generic propagated mark.";

        public override IReadOnlyList<RuleDependency> Dependencies => new[]
        {
            new RuleDependency(RuleNodeId.For(RuleKind.Mark, TypedPortMarker.Id), RuleOutputKind.SeedMark)
        };

        public override IReadOnlyList<RuleOutputKind> ProducedOutputs => new[]
        {
            RuleOutputKind.PropagatedMark,
            RuleOutputKind.LocalReference
        };

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
          new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            foreach (var seedMark in seedMarks)
            {
                yield return new PropagatedMarkRecord(
                  Id,
                  seedMark with { RuleId = Id },
                  seedMark,
                  1);
            }
        }
    }

    private sealed class LocalReferenceOnlyConsumer : RuleDefinitionPropagate
    {
        public const string Id = "TEST-GRAPH-LOCAL-REFERENCE-CONSUMER-001";

        public override string RuleId => Id;

        public override string Name => "Consume only local-reference output.";

        public override IReadOnlyList<RuleDependency> Dependencies => new[]
        {
            new RuleDependency(
              RuleNodeId.For(RuleKind.Propagate, GenericPropagatedOutputProducer.Id),
              RuleOutputKind.LocalReference)
        };

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
          new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            foreach (var seedMark in seedMarks)
            {
                yield return new PropagatedMarkRecord(
                  Id,
                  seedMark with { RuleId = Id },
                  seedMark,
                  1);
            }
        }
    }

    private sealed class CrossGroupLifter : RuleDefinitionLift
    {
        public const string Id = "TEST-GRAPH-LIFT-001";

        public override string RuleId => Id;

        public override string GroupKey => "lifter-group";

        public override string Name => "Lift the explicit graph dependency.";

        public override IReadOnlyList<RuleDependency> Dependencies => new[]
        {
            new RuleDependency(RuleNodeId.For(RuleKind.Mark, CrossGroupMarker.Id), RuleOutputKind.SeedMark)
        };

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds => new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<LiftedMarkRecord> Lift(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = propagatedMarks;
            var seed = Assert.Single(seedMarks);
            yield return new LiftedMarkRecord(RuleId, seed with { RuleId = RuleId }, seed, 1);
        }
    }

    private sealed class MissingProducerLifter : RuleDefinitionLift
    {
        public override string RuleId => "TEST-GRAPH-MISSING-PRODUCER-001";

        public override string Name => "Require an unavailable graph producer.";

        public override IReadOnlyList<RuleDependency> Dependencies => new[]
        {
            new RuleDependency(
              RuleNodeId.For(RuleKind.Mark, "TEST-GRAPH-MISSING-MARK-001"),
              RuleOutputKind.SeedMark)
        };

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds =>
          new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<LiftedMarkRecord> Lift(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            return Array.Empty<LiftedMarkRecord>();
        }
    }

    private sealed class CrossGroupProposer : RuleDefinitionPropose
    {
        public const string Id = "TEST-GRAPH-PROPOSE-001";

        public override string RuleId => Id;

        public override string GroupKey => "proposer-group";

        public override string Name => "Propose from the explicit graph dependency.";

        public override IReadOnlyList<RuleDependency> Dependencies => new[]
        {
            new RuleDependency(RuleNodeId.For(RuleKind.Lift, CrossGroupLifter.Id), RuleOutputKind.LiftedMark)
        };

        public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds => new[] { SyntaxKind.ClassDeclaration };

        public override IReadOnlyList<SyntaxKind> MergeableNodeKinds => new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<DecisionUnit> Propose(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
          IReadOnlyList<LiftedMarkRecord> liftedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            var lifted = Assert.Single(liftedMarks);
            yield return DeleteDecisionFactory.CreateDeleteDecision(RuleId, lifted.Mark.SyntaxNode, RuleId);
        }
    }
}
