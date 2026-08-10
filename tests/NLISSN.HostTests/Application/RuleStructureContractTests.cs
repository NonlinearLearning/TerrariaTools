using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RuleStructureContractTests
{
  [Fact]
  public void StructuralKind_ContainsOnlyApprovedStructureConclusions()
  {
    Assert.Equal(
      new[]
      {
        StructuralKind.Assignment,
        StructuralKind.LocalDefinition,
        StructuralKind.If,
        StructuralKind.Loop,
        StructuralKind.Switch,
        StructuralKind.ConditionalExpression,
        StructuralKind.Return
      },
      Enum.GetValues<StructuralKind>());
  }

  [Fact]
  public void LiftPayloads_DeclareTheLiftStageContract()
  {
    var root = CSharpSyntaxTree.ParseText(
        "class Demo { void Run(bool left, bool right) { if (left && right) { } } }")
      .GetRoot();
    var logicalHost = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
    var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
    object[] payloads =
    {
      new LogicalExpressionReductionPayload(
        logicalHost,
        new[] { logicalHost.Left },
        new[] { logicalHost.Right }),
      new IfStructureLiftPayload(ifStatement, null, null, IfStructureLiftKind.DeleteWholeIf)
    };

    Assert.All(payloads, payload => Assert.IsAssignableFrom<ILiftPayload>(payload));
  }

  [Fact]
  public void MarkCoverage_WhenOnlyOneConditionSiblingIsMarked_ReturnsFalse()
  {
    var condition = CSharpSyntaxTree.ParseText(
        "class Demo { void Run() { if (left && right) { Run(); } } }")
      .GetRoot()
      .DescendantNodes()
      .OfType<BinaryExpressionSyntax>()
      .Single();
    var left = condition.Left;
    var mark = new MarkRecord("test", left, null, null, "test");

    Assert.False(MarkCoverage.IsCovered(condition, new[] { mark }));
    Assert.True(MarkCoverage.IsCovered(condition, new[]
    {
      mark,
      new MarkRecord("test", condition.Right, null, null, "test")
    }));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PropagationEngine_WhenRuleEmitsLiftOwnedPayload_RejectsOutput(bool emitsIfPayload)
  {
    var application = new ApplicationService(
      new RuleDefinitionMark[] { new TestIdentifierMarkRule() },
      new RuleDefinitionPropagate[] { new LiftOwnedPayloadPropagationRule(emitsIfPayload) },
      Array.Empty<RuleDefinitionLift>(),
      Array.Empty<RuleDefinitionPropose>());

    var exception = Assert.Throws<InvalidOperationException>(() => application.Analyze(
      "class Demo { void Run(bool left, bool right) { if (left && right) { } } }",
      "lift-payload-propagation.cs",
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

    Assert.Contains("Lift-owned structural payload", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Compile_WhenTagAndSyntaxKindsMatch_CreatesOneEdge()
  {
    var tag = new RuleSemanticTag("Test.If");
    var producer = SyntaxProducer("producer", new[] { SyntaxKind.IfStatement }, tag);
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
      tag,
      RuleInputCardinality.All);

    var graph = new RuleStructureContractGraphCompiler().Compile(new[] { producer, consumer });

    Assert.Equal(
      new[] { producer.NodeId },
      graph.GetProducers(consumer.NodeId, consumer.Consumes.Inputs.Single()));
  }

  [Fact]
  public void Compile_WhenTagsDiffer_DoesNotCreateAnEdge()
  {
    var producer = SyntaxProducer(
      "producer",
      new[] { SyntaxKind.IfStatement },
      new RuleSemanticTag("Test.Left"));
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      new RuleSemanticTag("Test.Right"),
      RuleInputCardinality.All);

    var graph = new RuleStructureContractGraphCompiler().Compile(new[] { producer, consumer });

    Assert.Empty(graph.GetProducers(consumer.NodeId, consumer.Consumes.Inputs.Single()));
  }

  [Fact]
  public void Compile_WhenProducerKindsAreOutsideConsumerContract_RejectsPartialOverlap()
  {
    var tag = new RuleSemanticTag("Test.If");
    var producer = SyntaxProducer(
      "producer",
      new[] { SyntaxKind.IfStatement, SyntaxKind.ElseClause },
      tag);
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      tag,
      RuleInputCardinality.All);

    var exception = Assert.Throws<InvalidOperationException>(
      () => new RuleStructureContractGraphCompiler().Compile(new[] { producer, consumer }));

    Assert.Contains("Test.If", exception.Message, StringComparison.Ordinal);
    Assert.Contains("ElseClause", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Compile_WhenAllHasMultipleProducers_ConnectsEveryProducerInDeclarationOrder()
  {
    var tag = new RuleSemanticTag("Test.If");
    var first = SyntaxProducer("producer-a", new[] { SyntaxKind.IfStatement }, tag);
    var second = SyntaxProducer("producer-b", new[] { SyntaxKind.IfStatement }, tag);
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      tag,
      RuleInputCardinality.All);

    var graph = new RuleStructureContractGraphCompiler().Compile(new[] { first, second, consumer });

    Assert.Equal(
      new[] { first.NodeId, second.NodeId },
      graph.GetProducers(consumer.NodeId, consumer.Consumes.Inputs.Single()));
  }

  [Fact]
  public void Compile_WhenExactlyOneHasNoProducer_RejectsConsumer()
  {
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      new RuleSemanticTag("Test.If"),
      RuleInputCardinality.ExactlyOne);

    var exception = Assert.Throws<InvalidOperationException>(
      () => new RuleStructureContractGraphCompiler().Compile(new[] { consumer }));

    Assert.Contains("exactly one producer", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Compile_WhenExactlyOneHasMultipleProducers_RejectsConsumer()
  {
    var tag = new RuleSemanticTag("Test.If");
    var first = SyntaxProducer("producer-a", new[] { SyntaxKind.IfStatement }, tag);
    var second = SyntaxProducer("producer-b", new[] { SyntaxKind.IfStatement }, tag);
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      tag,
      RuleInputCardinality.ExactlyOne);

    var exception = Assert.Throws<InvalidOperationException>(
      () => new RuleStructureContractGraphCompiler().Compile(new[] { first, second, consumer }));

    Assert.Contains("Mark:producer-a, Mark:producer-b", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Compile_WhenOptionalHasNoProducer_AcceptsConsumer()
  {
    var consumer = SyntaxConsumer(
      "consumer",
      new[] { SyntaxKind.IfStatement },
      new RuleSemanticTag("Test.If"),
      RuleInputCardinality.Optional);

    var graph = new RuleStructureContractGraphCompiler().Compile(new[] { consumer });

    Assert.Empty(graph.Edges);
  }

  [Fact]
  public void FromValues_WhenMarkMatchesOutput_IndexesOnlyMatchingSyntaxOutput()
  {
    var tag = new RuleSemanticTag("Test.If");
    var produces = new RuleProducesContract(
      new[] { new RuleProducedSyntax(new[] { SyntaxKind.IfStatement }, tag) });
    var mark = new MarkRecord(
      "producer",
      ParseIfStatement(),
      null,
      null,
      "test",
      SemanticTag: tag);

    var result = RuleNodeResult.FromValues(new object[] { mark }, produces);

    var input = new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement }, tag);
    Assert.Equal(new object[] { mark }, result.GetOutputs(input));
  }

  [Fact]
  public void EvidenceOrigins_WhenFactIsPropagatedAndLifted_PreserveTheSeedOrigin()
  {
    var source = new MarkRecord(
      "seed",
      ParseIfStatement(),
      null,
      null,
      "test",
      Origins: RuleEvidenceOrigin.AtomicExpression);
    var propagated = new PropagatedMarkRecord(
      "propagate",
      source with { RuleId = "propagate" },
      source,
      1);
    var lifted = new LiftedMarkRecord(
      "lift",
      propagated.Mark with { RuleId = "lift" },
      source,
      1);

    Assert.Equal(RuleEvidenceOrigin.AtomicExpression, source.Origins);
    Assert.Equal(RuleEvidenceOrigin.AtomicExpression, propagated.Origins);
    Assert.Equal(RuleEvidenceOrigin.AtomicExpression, lifted.Origins);
  }

  [Fact]
  public void Analyze_WhenLiftCombinesFacts_ExposesTheOriginUnion()
  {
    var application = new ApplicationService(
      new RuleDefinitionMark[] { new MultiOriginMarkRule() },
      Array.Empty<RuleDefinitionPropagate>(),
      new RuleDefinitionLift[] { new OriginUnionLiftRule() },
      Array.Empty<RuleDefinitionPropose>());

    var result = application.Analyze(
      "class Demo { void Run(bool left, bool right) { if (left && right) { } } }",
      "origin-union.cs",
      new Dictionary<string, string>());

    var lifted = Assert.Single(result.LiftedMarks);

    Assert.Equal(
      RuleEvidenceOrigin.AtomicExpression | RuleEvidenceOrigin.DeclarationExpression,
      lifted.Origins);
  }

  [Fact]
  public void GetOutputs_WhenOriginsDiffer_RoutesAllFactsThroughTheSameSyntaxPort()
  {
    var port = RuleFactPorts.TargetExpression;
    var produces = new RuleProducesContract(
      new[] { new RuleProducedSyntax(new[] { SyntaxKind.IfStatement }, port) });
    var atomic = new MarkRecord(
      "atomic",
      ParseIfStatement(),
      null,
      null,
      "test",
      SemanticTag: port,
      Origins: RuleEvidenceOrigin.AtomicExpression);
    var declaration = atomic with
    {
      RuleId = "declaration",
      Origins = RuleEvidenceOrigin.DeclarationExpression
    };
    var result = RuleNodeResult.FromValues(new object[] { atomic, declaration }, produces);

    Assert.Equal(
      new object[] { atomic, declaration },
      result.GetOutputs(new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement }, port)));
  }

  [Fact]
  public async Task ExecuteAsync_WhenEdgeDeclaresSyntaxInput_ProvidesOnlyItsContractValues()
  {
    var tag = new RuleSemanticTag("Test.If");
    var input = new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement }, tag);
    var output = new RuleProducedSyntax(new[] { SyntaxKind.IfStatement }, tag);
    var producer = new RuleGraphNode(
      RuleNodeId.For(RuleKind.Mark, "producer"),
      RuleKind.Mark,
      Array.Empty<RuleDependency>())
    {
      ProducedSyntax = new[] { output }
    };
    var consumer = new RuleGraphNode(
      RuleNodeId.For(RuleKind.Propagate, "consumer"),
      RuleKind.Propagate,
      new RuleDependency(producer.NodeId, input));
    var graph = new RuleGraphCompiler().Compile(new[] { producer, consumer });
    var mark = new MarkRecord(
      "producer",
      ParseIfStatement(),
      null,
      null,
      "test",
      SemanticTag: tag);

    await new RuleGraphExecutor().ExecuteAsync(
      graph,
      new[]
      {
        new RuleGraphExecutionNode(
          producer,
          (_, _) => Task.FromResult(RuleNodeResult.FromValues(new object[] { mark }, new RuleProducesContract(new[] { output })))),
        new RuleGraphExecutionNode(
          consumer,
          (inputs, _) =>
          {
            Assert.Equal(new object[] { mark }, inputs.GetOutputs(producer.NodeId, input));
            return Task.FromResult(RuleNodeResult.Empty);
          })
      },
      maxDegreeOfParallelism: 1);
  }

  [Fact]
  public void DefaultPipeline_UsesOnlySyntaxTagDependencies()
  {
    var pipeline = RuleRegistry.CreateDefaultRules();
    var graph = pipeline.CompileRuleGraph();

    Assert.DoesNotContain(
      typeof(IRuleDefinition).GetProperties(),
      property => property.Name is "Dependencies" or "ProducedOutputs");
    Assert.DoesNotContain(
      typeof(RuleDependency).GetProperties(),
      property => property.Name.Contains("Structure", StringComparison.Ordinal));
    Assert.All(graph.Nodes, node => Assert.All(
      node.Dependencies,
      dependency =>
      {
        if (node.Kind == RuleKind.Propagate && dependency.Producer.Value.StartsWith("Mark:", StringComparison.Ordinal))
        {
          Assert.Null(dependency.RequiredInput);
          return;
        }

        Assert.NotNull(dependency.RequiredInput);
      }));
  }

  private static RuleStructureContractGraphNode SyntaxProducer(
    string ruleId,
    IReadOnlyList<SyntaxKind> syntaxKinds,
    RuleSemanticTag semanticTag)
  {
    return new RuleStructureContractGraphNode(
      RuleNodeId.For(RuleKind.Mark, ruleId),
      RuleConsumesContract.Empty,
      new RuleProducesContract(new[] { new RuleProducedSyntax(syntaxKinds, semanticTag) }));
  }

  private static RuleStructureContractGraphNode SyntaxConsumer(
    string ruleId,
    IReadOnlyList<SyntaxKind> syntaxKinds,
    RuleSemanticTag semanticTag,
    RuleInputCardinality inputCardinality)
  {
    return new RuleStructureContractGraphNode(
      RuleNodeId.For(RuleKind.Propagate, ruleId),
      new RuleConsumesContract(new[] { new RuleConsumedSyntax(syntaxKinds, semanticTag) }),
      RuleProducesContract.Empty,
      InputCardinality: inputCardinality);
  }

  private static SyntaxNode ParseIfStatement()
  {
    return CSharpSyntaxTree.ParseText("class Demo { void Run() { if (ready) { Run(); } } }")
      .GetRoot()
      .DescendantNodes()
      .Single(node => node.IsKind(SyntaxKind.IfStatement));
  }

  private sealed class TestIdentifierMarkRule : RuleDefinitionMark
  {
    private static readonly RuleSemanticTag SemanticTag = new("Test.Mark");

    public override string RuleId => "TEST-MARK-LIFT-PAYLOAD-001";

    public override string Name => "Seed an identifier for propagation payload validation";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { SyntaxKind.IdentifierName };

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, SemanticTag)
    });

    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
      var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>().First();
      yield return new MarkRecord(RuleId, identifier, null, null, "Test seed.", SemanticTag: SemanticTag);
    }
  }

  private sealed class LiftOwnedPayloadPropagationRule : RuleDefinitionPropagate
  {
    private static readonly RuleSemanticTag InputTag = new("Test.Mark");
    private static readonly RuleSemanticTag OutputTag = new("Test.Propagated");
    private readonly bool _emitsIfPayload;

    public LiftOwnedPayloadPropagationRule(bool emitsIfPayload)
    {
      _emitsIfPayload = emitsIfPayload;
    }

    public override string RuleId => "TEST-PROP-LIFT-PAYLOAD-001";

    public override string Name => "Attempt to emit a Lift-owned payload from Propagate";

    public override RuleConsumesContract Consumes => new(new[]
    {
      new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, InputTag)
    });

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, OutputTag)
    });

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds => new[] { SyntaxKind.IdentifierName };

    public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
      var source = seedMarks.Single();
      var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
      object payload = _emitsIfPayload
        ? new IfStructureLiftPayload(ifStatement, null, null, IfStructureLiftKind.DeleteWholeIf)
        : new LogicalExpressionReductionPayload(
          ifStatement.Condition as BinaryExpressionSyntax ?? throw new InvalidOperationException(),
          new[] { ifStatement.Condition },
          Array.Empty<ExpressionSyntax>());

      yield return new PropagatedMarkRecord(RuleId, source with { RuleId = RuleId }, source, 1, payload);
    }
  }

  private sealed class MultiOriginMarkRule : RuleDefinitionMark
  {
    public override string RuleId => "TEST-MARK-ORIGIN-UNION-001";

    public override string Name => "Seed two expression facts with distinct evidence origins";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { SyntaxKind.IdentifierName };

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.TargetExpression)
    });

    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
      var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>()
        .Where(identifier => identifier.Identifier.ValueText is "left" or "right")
        .OrderBy(identifier => identifier.SpanStart)
        .ToList();

      yield return new MarkRecord(
        RuleId,
        identifiers[0],
        null,
        null,
        "Atomic expression seed.",
        SemanticTag: RuleFactPorts.TargetExpression,
        Origins: RuleEvidenceOrigin.AtomicExpression);
      yield return new MarkRecord(
        RuleId,
        identifiers[1],
        null,
        null,
        "Declaration expression seed.",
        SemanticTag: RuleFactPorts.TargetExpression,
        Origins: RuleEvidenceOrigin.DeclarationExpression);
    }
  }

  private sealed class OriginUnionLiftRule : RuleDefinitionLift
  {
    public override string RuleId => "TEST-LIFT-ORIGIN-UNION-001";

    public override string Name => "Lift compatible facts into one if structure";

    public override RuleConsumesContract Consumes => new(new[]
    {
      new RuleConsumedSyntax(new[] { SyntaxKind.IdentifierName }, RuleFactPorts.TargetExpression)
    });

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.IfStatement }, RuleFactPorts.LiftIfStructure)
    });

    public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds => new[] { SyntaxKind.IfStatement };

    public override IEnumerable<LiftedMarkRecord> Lift(
      ILiftRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
      var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
      yield return new LiftedMarkRecord(
        RuleId,
        new MarkRecord(
          RuleId,
          ifStatement,
          null,
          null,
          "Combined origin test lift.",
          SemanticTag: RuleFactPorts.LiftIfStructure),
        seedMarks[0],
        1,
        StructureKind: StructuralKind.If);
    }
  }
}
