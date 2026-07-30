using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Application;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RuleStructureContractTests
{
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
      dependency => Assert.NotNull(dependency.RequiredInput)));
  }

  private static RuleStructureContractGraphNode SyntaxProducer(
    string ruleId,
    IReadOnlyList<SyntaxKind> syntaxKinds,
    RuleSemanticTag semanticTag)
  {
    return new RuleStructureContractGraphNode(
      RuleNodeId.For(RuleKind.Mark, ruleId),
      RuleKind.Mark,
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
      RuleKind.Propagate,
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
}
