using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.Structure;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Application;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RuleStructureContractTests
{
  [Fact]
  public void Resolve_IfStructureAndDirectMembers_UsesTypedRoslynSlots()
  {
    var ifStatement = ParseIfStatement("if (ready) Run();");

    var structure = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Whole);
    var condition = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition);
    var thenBranch = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.ThenBranch);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, structure.Status);
    Assert.Same(ifStatement, structure.Structure!.SyntaxNode);
    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, condition.Status);
    Assert.Same(ifStatement.Condition, condition.Structure!.SyntaxNode);
    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, thenBranch.Status);
    Assert.Same(ifStatement.Statement, thenBranch.Structure!.SyntaxNode);
  }

  [Fact]
  public void Resolve_ElseBranch_ReturnsAbsentWhenIfHasNoElse()
  {
    var ifStatement = ParseIfStatement("if (ready) Run();");

    var result = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.ElseBranch);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.AbsentOptionalMember, result.Status);
    Assert.Null(result.Structure);
  }

  [Fact]
  public void Resolve_ElseBranch_UsesElseClauseWhenItExists()
  {
    var ifStatement = ParseIfStatement("if (ready) Run(); else Stop();");

    var result = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.ElseBranch);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, result.Status);
    Assert.Same(ifStatement.Else, result.Structure!.SyntaxNode);
  }

  [Fact]
  public void Resolve_ElseIfStructure_OnlyUsesElseClauseStatement()
  {
    var ifStatement = ParseIfStatement("if (first) Run(); else if (second) Stop(); else Wait();");
    var elseIf = Assert.IsType<IfStatementSyntax>(ifStatement.Else!.Statement);

    var result = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.ElseIf);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, result.Status);
    Assert.Same(elseIf, result.Structure!.SyntaxNode);
  }

  [Fact]
  public void Validate_DescendantOfIfCondition_IsNotTheDirectConditionSlot()
  {
    var ifStatement = ParseIfStatement("if (left && right) Run();");
    var descendant = ifStatement.Condition.DescendantNodes().OfType<IdentifierNameSyntax>().First();

    var result = RuleSyntaxStructureCatalog.Validate(
      descendant,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.InvalidMember, result.Status);
    Assert.Null(result.Structure);
  }

  [Fact]
  public void Resolve_IncompleteIfThenBranch_ReturnsIncompleteSyntaxWithoutThrowing()
  {
    var ifStatement = ParseIfStatement("if (ready)");

    var result = RuleSyntaxStructureCatalog.Resolve(
      ifStatement,
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.ThenBranch);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.IncompleteSyntax, result.Status);
    Assert.Null(result.Structure);
  }

  [Fact]
  public void Resolve_VariableDeclaratorWhole_UsesTheExistingTypedRoslynNode()
  {
    var tree = CSharpSyntaxTree.ParseText("class Demo { void Run() { var value = 1; } }");
    var variableDeclarator = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();

    var result = RuleSyntaxStructureCatalog.Resolve(
      variableDeclarator,
      RuleSyntaxStructureKind.VariableDeclarator,
      RuleSyntaxStructureRole.Whole);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, result.Status);
    Assert.Same(variableDeclarator, result.Structure!.SyntaxNode);
  }

  [Fact]
  public void Resolve_LogicalBinaryWhole_AcceptsLogicalAndAndRejectsArithmeticBinary()
  {
    var tree = CSharpSyntaxTree.ParseText("class Demo { bool Run(bool left, bool right) => left && right; int Add() => 1 + 2; }");
    var logicalAnd = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>()
      .Single(node => node.IsKind(SyntaxKind.LogicalAndExpression));
    var addition = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>()
      .Single(node => node.IsKind(SyntaxKind.AddExpression));

    var logicalResult = RuleSyntaxStructureCatalog.Resolve(
      logicalAnd,
      RuleSyntaxStructureKind.LogicalBinary,
      RuleSyntaxStructureRole.Whole);
    var additionResult = RuleSyntaxStructureCatalog.Validate(
      addition,
      RuleSyntaxStructureKind.LogicalBinary,
      RuleSyntaxStructureRole.Whole);

    Assert.Equal(RuleSyntaxStructureResolutionStatus.Resolved, logicalResult.Status);
    Assert.Same(logicalAnd, logicalResult.Structure!.SyntaxNode);
    Assert.Equal(RuleSyntaxStructureResolutionStatus.InvalidMember, additionResult.Status);
  }

  [Fact]
  public void Validate_ParameterAndDelegateUsagePorts_AcceptsOnlyTheirDeclaredRoslynNodes()
  {
    var tree = CSharpSyntaxTree.ParseText("""
      class Demo
      {
        delegate void Handler(Demo value);
        static void Extension(this string receiver, Demo value) { }
        int this[Demo value] => 0;
        void Run(Demo value)
        {
          void Local(Demo parameter) { }
          Local(value);
          _ = this[value];
          Handler handler = Local;
          handler(value);
          "".Length.Extension(value);
        }
      }
      """);
    var root = tree.GetRoot();
    var localFunction = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
    var localInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
      .Single(node => node.Expression.ToString() == "Local");
    var indexer = root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Single();
    var elementAccess = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().Single();
    var delegateDeclaration = root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single();
    var extensionMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
      .Single(node => node.Identifier.ValueText == "Extension");
    var extensionInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
      .Single(node => node.Expression.ToString().Contains("Extension", StringComparison.Ordinal));

    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        localFunction,
        RuleSyntaxStructureKind.LocalFunctionParameterUsage,
        RuleSyntaxStructureRole.Declaration).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        localInvocation,
        RuleSyntaxStructureKind.LocalFunctionParameterUsage,
        RuleSyntaxStructureRole.Callsite).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        indexer,
        RuleSyntaxStructureKind.IndexerParameterUsage,
        RuleSyntaxStructureRole.Declaration).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        elementAccess,
        RuleSyntaxStructureKind.IndexerParameterUsage,
        RuleSyntaxStructureRole.Callsite).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        delegateDeclaration,
        RuleSyntaxStructureKind.DelegateUsage,
        RuleSyntaxStructureRole.Declaration).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        localFunction,
        RuleSyntaxStructureKind.DelegateUsage,
        RuleSyntaxStructureRole.Binding).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        extensionMethod,
        RuleSyntaxStructureKind.ExtensionMethodParameterUsage,
        RuleSyntaxStructureRole.Declaration).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.Resolved,
      RuleSyntaxStructureCatalog.Validate(
        extensionInvocation,
        RuleSyntaxStructureKind.ExtensionMethodParameterUsage,
        RuleSyntaxStructureRole.Callsite).Status);
    Assert.Equal(
      RuleSyntaxStructureResolutionStatus.InvalidMember,
      RuleSyntaxStructureCatalog.Validate(
        extensionMethod,
        RuleSyntaxStructureKind.LocalFunctionParameterUsage,
        RuleSyntaxStructureRole.Declaration).Status);
  }

  [Fact]
  public void Match_ConsumesAndProduces_WithSameStructureAndSemanticTag_AreCompatible()
  {
    var selector = new MarkedStructureSelector(
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition,
      new RuleSemanticTag("IfCompletion"));
    var produces = new RuleProducesContract(new[] { selector });
    var consumes = new RuleConsumesContract(new[]
    {
      new RuleConsumedStructure(selector, RuleInputCardinality.All)
    });

    Assert.True(RuleStructureContractMatcher.IsCompatible(
      Assert.Single(produces.Structures),
      Assert.Single(consumes.Structures).Selector));
  }

  [Fact]
  public void Match_ConsumesAndProduces_WithDifferentSemanticTags_AreIncompatible()
  {
    var produces = new MarkedStructureSelector(
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition,
      new RuleSemanticTag("IfCompletion"));
    var consumes = new MarkedStructureSelector(
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition,
      new RuleSemanticTag("IfStructure"));

    Assert.False(RuleStructureContractMatcher.IsCompatible(produces, consumes));
  }

  [Fact]
  public void RequireProducedMark_WhenNodeIsDeclaredDirectIfCondition_AcceptsMark()
  {
    var ifStatement = ParseIfStatement("if (ready) Run();");
    var produces = new RuleProducesContract(new[]
    {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.Condition,
        new RuleSemanticTag("IfCompletion"))
    });
    var mark = new MarkRecord(
      "TEST-CONTRACT-PRODUCER",
      ifStatement.Condition,
      null,
      null,
      "test",
      SemanticTag: new RuleSemanticTag("IfCompletion"));

    var selector = RuleStructureContractValidator.RequireProducedMark(produces, mark);

    Assert.Equal(RuleSyntaxStructureRole.Condition, selector.Role);
  }

  [Fact]
  public void RequireProducedMark_WhenNodeIsOnlyConditionDescendant_RejectsMark()
  {
    var ifStatement = ParseIfStatement("if (left && right) Run();");
    var produces = new RuleProducesContract(new[]
    {
      new MarkedStructureSelector(
        RuleSyntaxStructureKind.If,
        RuleSyntaxStructureRole.Condition,
        new RuleSemanticTag("IfCompletion"))
    });
    var descendant = ifStatement.Condition.DescendantNodes().OfType<IdentifierNameSyntax>().First();
    var mark = new MarkRecord(
      "TEST-CONTRACT-PRODUCER",
      descendant,
      null,
      null,
      "test",
      SemanticTag: new RuleSemanticTag("IfCompletion"));

    var exception = Assert.Throws<InvalidOperationException>(
      () => RuleStructureContractValidator.RequireProducedMark(produces, mark));

    Assert.Contains("does not match any declared produced structure", exception.Message);
  }

  [Fact]
  public void Compile_WhenExactlyOneConsumerHasNoProducer_ReportsMissingSelector()
  {
    var selector = IfCondition("IfCompletion");
    var consumer = Consumer("consumer", selector, RuleInputCardinality.ExactlyOne);

    var exception = Assert.Throws<InvalidOperationException>(
      () => new RuleStructureContractGraphCompiler().Compile(new[] { consumer }));

    Assert.Equal(
      "Rule node 'Propagate:consumer' requires exactly one producer for '(If, Condition, IfCompletion)', but found none.",
      exception.Message);
  }

  [Fact]
  public void Compile_WhenExactlyOneConsumerHasTwoProducers_ReportsStableProducerIds()
  {
    var selector = IfCondition("IfCompletion");
    var firstProducer = Producer("producer-a", selector);
    var secondProducer = Producer("producer-b", selector);
    var consumer = Consumer("consumer", selector, RuleInputCardinality.ExactlyOne);

    var exception = Assert.Throws<InvalidOperationException>(
      () => new RuleStructureContractGraphCompiler().Compile(
        new[] { firstProducer, secondProducer, consumer }));

    Assert.Equal(
      "Rule node 'Propagate:consumer' requires exactly one producer for '(If, Condition, IfCompletion)', but found: Mark:producer-a, Mark:producer-b.",
      exception.Message);
  }

  [Fact]
  public void Compile_WhenAllConsumerHasTwoCompatibleProducers_ConnectsBothInDeclarationOrder()
  {
    var selector = IfCondition("IfCompletion");
    var firstProducer = Producer("producer-a", selector);
    var secondProducer = Producer("producer-b", selector);
    var consumer = Consumer("consumer", selector, RuleInputCardinality.All);

    var graph = new RuleStructureContractGraphCompiler().Compile(
      new[] { firstProducer, secondProducer, consumer });

    Assert.Equal(
      new[] { firstProducer.NodeId, secondProducer.NodeId },
      graph.GetProducers(consumer.NodeId, selector));
  }

  [Fact]
  public void Compile_WhenCompatibleProducerIsDisabled_RetainsItsContractEdge()
  {
    var selector = IfCondition("IfCompletion");
    var disabledProducer = Producer("producer", selector, isEnabled: false);
    var consumer = Consumer("consumer", selector, RuleInputCardinality.All);

    var graph = new RuleStructureContractGraphCompiler().Compile(
      new[] { disabledProducer, consumer });

    Assert.False(graph.Nodes.Single(node => node.NodeId == disabledProducer.NodeId).IsEnabled);
    Assert.Equal(new[] { disabledProducer.NodeId }, graph.GetProducers(consumer.NodeId, selector));
  }

  [Fact]
  public void CompileRuleGraph_WhenConsumerDeclaresStructureContract_UsesContractProducerEdge()
  {
    var selector = IfCondition("IfCompletion");
    var producer = new ContractProducerRule(selector);
    var consumer = new ContractConsumerRule(selector);
    var pipeline = new RulePipeline(
      new[] { producer },
      new[] { consumer },
      Array.Empty<RuleDefinitionLift>(),
      Array.Empty<RuleDefinitionPropose>());

    var graph = pipeline.CompileRuleGraph();
    var consumerNode = Assert.Single(graph.Nodes, node => node.NodeId == consumer.NodeId);
    var dependency = Assert.Single(consumerNode.Dependencies);

    Assert.Equal(producer.NodeId, dependency.Producer);
    Assert.Equal(selector, dependency.RequiredStructure);
  }

  [Fact]
  public async Task ExecuteAsync_WhenConsumerDeclaresStructureContract_ProvidesOnlyThatPort()
  {
    var selector = IfCondition("IfCompletion");
    var producer = new RuleGraphNode(
      RuleNodeId.For(RuleKind.Mark, "producer"),
      RuleKind.Mark,
      Array.Empty<RuleDependency>())
    {
      ProducedStructures = new[] { selector }
    };
    var consumer = new RuleGraphNode(
      RuleNodeId.For(RuleKind.Propagate, "consumer"),
      RuleKind.Propagate,
      new RuleDependency(producer.NodeId, selector))
    {
      ConsumedStructures = new[]
      {
        new RuleConsumedStructure(selector, RuleInputCardinality.ExactlyOne)
      }
    };
    var graph = new RuleGraphCompiler().Compile(new[] { producer, consumer });
    var producerResult = new RuleNodeResult(new object[] { "legacy-stage-value" })
    {
      StructureOutputs = new Dictionary<MarkedStructureSelector, IReadOnlyList<object>>
      {
        [selector] = new object[] { "contract-value" }
      }
    };

    await new RuleGraphExecutor().ExecuteAsync(
      graph,
      new[]
      {
        new RuleGraphExecutionNode(
          producer,
          (_, _) => Task.FromResult(producerResult)),
        new RuleGraphExecutionNode(
          consumer,
          (inputs, _) =>
          {
            Assert.Equal(new object[] { "contract-value" }, inputs.GetOutputs(producer.NodeId, selector));
            Assert.Equal(new object[] { "legacy-stage-value" }, inputs.GetValues(producer.NodeId));
            return Task.FromResult(RuleNodeResult.Empty);
          })
      },
      maxDegreeOfParallelism: 1);
  }

  [Fact]
  public void FromValues_WhenMarkMatchesDeclaredStructure_IndexesStructurePort()
  {
    var selector = IfCondition("IfCompletion");
    var ifStatement = ParseIfStatement("if (ready) Run();");
    var mark = new MarkRecord(
      "producer",
      ifStatement.Condition,
      null,
      null,
      "test",
      SemanticTag: selector.SemanticTag);

    var result = RuleNodeResult.FromValues(
      new object[] { mark },
      new RuleProducesContract(new[] { selector }));

    Assert.Equal(new object[] { mark }, result.GetOutputs(selector));
  }

  [Fact]
  public void FromValues_WhenLegacyMarkHasNoSemanticTag_ExcludesItFromStructurePort()
  {
    var selector = IfCondition("IfCompletion");
    var ifStatement = ParseIfStatement("if (ready) Run();");
    var legacyMark = new MarkRecord("producer", ifStatement.Condition, null, null, "legacy");
    var contractedMark = legacyMark with { SemanticTag = selector.SemanticTag };

    var result = RuleNodeResult.FromValues(
      new object[] { legacyMark, contractedMark },
      new RuleProducesContract(new[] { selector }));

    Assert.Equal(new object[] { contractedMark }, result.GetOutputs(selector));
  }

  [Fact]
  public void CompileRuleGraph_DefaultIfProposal_ConsumesOnlySObjectIfCompletionPorts()
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(
      graph.Nodes,
      node => node.NodeId.Value == "Propose:DEL-SOBJ-PROPOSE-IF-001");

    Assert.Equal(2, proposal.Dependencies.Count);
    Assert.All(proposal.Dependencies, dependency =>
      Assert.Equal("Propagate:DEL-SOBJ-PROP-IF-COMPLETE-001", dependency.Producer.Value));
    Assert.Equal(
      new[] { RuleSyntaxStructureRole.Whole, RuleSyntaxStructureRole.ElseBranch },
      proposal.Dependencies
        .Select(dependency => dependency.RequiredStructure!.Role)
        .OrderBy(role => role));
    Assert.All(proposal.Dependencies, dependency =>
      Assert.Equal("SObject.IfCompletion", dependency.RequiredStructure!.SemanticTag.Value));
  }

  [Fact]
  public void CompileRuleGraph_DefaultClassIfProposal_ConsumesOnlyClassIfCompletionPorts()
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(
      graph.Nodes,
      node => node.NodeId.Value == "Propose:DEL-CLASS-PROP-IF-001");

    Assert.Equal(2, proposal.Dependencies.Count);
    Assert.All(proposal.Dependencies, dependency =>
      Assert.Equal("Propagate:DEL-CLASS-PROP-IF-COMPLETE-001", dependency.Producer.Value));
    Assert.Equal(
      new[] { RuleSyntaxStructureRole.Whole, RuleSyntaxStructureRole.ElseBranch },
      proposal.Dependencies
        .Select(dependency => dependency.RequiredStructure!.Role)
        .OrderBy(role => role));
    Assert.All(proposal.Dependencies, dependency =>
      Assert.Equal("Class.IfCompletion", dependency.RequiredStructure!.SemanticTag.Value));
  }

  [Theory]
  [InlineData(
    "Lift:DEL-SOBJ-LIFT-SWITCH-001",
    "Lift:DEL-SOBJ-LIFT-IF-001",
    "SObject.IfStructure")]
  [InlineData(
    "Lift:DEL-CLASS-LIFT-SWITCH-001",
    "Lift:DEL-CLASS-LIFT-IF-001",
    "Class.IfStructure")]
  public void CompileRuleGraph_DefaultSwitchLift_ConsumesDeclaredIfStructurePorts(
    string switchNodeId,
    string ifProducerNodeId,
    string semanticTag)
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var switchNode = Assert.Single(graph.Nodes, node => node.NodeId.Value == switchNodeId);
    var ifDependencies = switchNode.Dependencies
      .Where(dependency => dependency.Producer.Value == ifProducerNodeId)
      .ToList();

    Assert.Equal(3, ifDependencies.Count);
    Assert.Equal(
      new[]
      {
        RuleSyntaxStructureRole.Whole,
        RuleSyntaxStructureRole.ElseBranch,
        RuleSyntaxStructureRole.ElseIf
      },
      ifDependencies
        .Select(dependency => dependency.RequiredStructure!.Role)
        .OrderBy(role => role));
    Assert.All(ifDependencies, dependency =>
      Assert.Equal(semanticTag, dependency.RequiredStructure!.SemanticTag.Value));
  }

  [Theory]
  [InlineData(
    "Propose:DEL-CLASS-PROP-LOCALFUNC-PARAM-SHRINK-001",
    "Propagate:DEL-CLASS-PROP-LOCALFUNC-PARAM-USAGE-001",
    RuleSyntaxStructureKind.LocalFunctionParameterUsage,
    "Class.LocalFunctionParameterUsage")]
  [InlineData(
    "Propose:DEL-CLASS-PROP-INDEXER-PARAM-SHRINK-001",
    "Propagate:DEL-CLASS-PROP-INDEXER-PARAM-USAGE-001",
    RuleSyntaxStructureKind.IndexerParameterUsage,
    "Class.IndexerParameterUsage")]
  public void CompileRuleGraph_DefaultParameterUsageProposal_ConsumesOnlyDeclaredUsagePorts(
    string proposalNodeId,
    string producerNodeId,
    RuleSyntaxStructureKind structureKind,
    string semanticTag)
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(graph.Nodes, node => node.NodeId.Value == proposalNodeId);

    Assert.Equal(2, proposal.Dependencies.Count);
    Assert.All(proposal.Dependencies, dependency =>
    {
      Assert.Equal(producerNodeId, dependency.Producer.Value);
      Assert.Equal(structureKind, dependency.RequiredStructure!.StructureKind);
      Assert.Equal(semanticTag, dependency.RequiredStructure.SemanticTag.Value);
    });
    Assert.Equal(
      new[] { RuleSyntaxStructureRole.Declaration, RuleSyntaxStructureRole.Callsite },
      proposal.Dependencies
        .Select(dependency => dependency.RequiredStructure!.Role)
        .OrderBy(role => role));
  }

  [Theory]
  [InlineData(
    "Propose:DEL-CLASS-PROP-DELEGATE-PARAM-SHRINK-001",
    "Propagate:DEL-CLASS-PROP-DELEGATE-USAGE-001",
    RuleSyntaxStructureKind.DelegateUsage,
    "Class.DelegateUsage",
    3)]
  [InlineData(
    "Propose:DEL-CLASS-PROP-EXT-NONRECV-PARAM-SHRINK-001",
    "Propagate:DEL-CLASS-PROP-EXT-MAPPED-001",
    RuleSyntaxStructureKind.ExtensionMethodParameterUsage,
    "Class.ExtensionMethodParameterUsage",
    2)]
  public void CompileRuleGraph_DefaultSemanticUsageProposal_ConsumesOnlyDeclaredUsagePorts(
    string proposalNodeId,
    string producerNodeId,
    RuleSyntaxStructureKind structureKind,
    string semanticTag,
    int expectedDependencyCount)
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(graph.Nodes, node => node.NodeId.Value == proposalNodeId);

    Assert.Equal(expectedDependencyCount, proposal.Dependencies.Count);
    Assert.All(proposal.Dependencies, dependency =>
    {
      Assert.Equal(producerNodeId, dependency.Producer.Value);
      Assert.Equal(structureKind, dependency.RequiredStructure!.StructureKind);
      Assert.Equal(semanticTag, dependency.RequiredStructure.SemanticTag.Value);
    });
  }

  [Theory]
  [InlineData("Propose:DEL-DEAD-001", "Mark:DEL-DEAD-001", "UnreachableMethod")]
  [InlineData("Propose:DEL-UNREF-METHOD-PROP-001", "Mark:DEL-UNREF-METHOD-MARK-001", "UnreferencedMethod")]
  [InlineData("Propose:CLR-UNUSED-IFACE-IMPL-PROP-001", "Mark:CLR-UNUSED-IFACE-IMPL-MARK-001", "UnusedInterfaceImplementation")]
  [InlineData("Propose:PRIV-INTERNAL-PUBLIC-PROP-001", "Mark:mark.privatize-internal-only-public-method", "InternalOnlyPublicMethod")]
  public void CompileRuleGraph_StandaloneMethodProposal_ConsumesItsDeclaredMethodFact(
    string proposalNodeId,
    string markerNodeId,
    string semanticTag)
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(graph.Nodes, node => node.NodeId.Value == proposalNodeId);
    var dependency = Assert.Single(proposal.Dependencies);

    Assert.Equal(markerNodeId, dependency.Producer.Value);
    Assert.Equal(RuleSyntaxStructureKind.DeclarationHost, dependency.RequiredStructure!.StructureKind);
    Assert.Equal(RuleSyntaxStructureRole.Whole, dependency.RequiredStructure.Role);
    Assert.Equal(semanticTag, dependency.RequiredStructure.SemanticTag.Value);
  }

  [Theory]
  [InlineData("Propose:DEL-SOBJ-PROPOSE-DEFAULT-001", RuleFactDomain.SObject, 25)]
  [InlineData("Propose:DEL-SOBJ-PROPOSE-CTRL-001", RuleFactDomain.SObject, 25)]
  [InlineData("Propose:DEL-CLASS-PROP-DEFAULT-001", RuleFactDomain.Class, 15)]
  [InlineData("Propose:DEL-CLASS-PROP-CTRL-001", RuleFactDomain.Class, 15)]
  public void CompileRuleGraph_DefaultOrControlProposal_UsesDeclaredTerminalFactDomain(
    string proposalNodeId,
    RuleFactDomain domain,
    int expectedDependencyCount)
  {
    var graph = RuleRegistry.CreateDefaultRules().CompileRuleGraph();
    var proposal = Assert.Single(graph.Nodes, node => node.NodeId.Value == proposalNodeId);

    Assert.Equal(expectedDependencyCount, proposal.Dependencies.Count);
    Assert.All(proposal.Dependencies, dependency =>
    {
      Assert.Null(dependency.RequiredStructure);
      Assert.Equal(domain, dependency.RequiredTerminalFact!.Domain);
    });
  }

  [Fact]
  public void CompileRuleGraph_DefaultRules_UseOnlyDeclaredContractInputs()
  {
    // Arrange
    var pipeline = RuleRegistry.CreateDefaultRules();
    var allRules = pipeline.Markers.Cast<IRuleDefinition>()
      .Concat(pipeline.Propagators)
      .Concat(pipeline.Lifters)
      .Concat(pipeline.Proposers)
      .ToList();

    // Act
    var graph = pipeline.CompileRuleGraph();

    // Assert
    Assert.DoesNotContain(
      typeof(IRuleDefinition).GetProperties(),
      property => property.Name is "Dependencies" or "ProducedOutputs");
    Assert.All(graph.Nodes, node => Assert.All(
      node.Dependencies,
      dependency => Assert.True(
        dependency.RequiredStructure is not null || dependency.RequiredTerminalFact is not null,
        $"Rule node '{node.NodeId}' must declare a structure or terminal fact input.")));
  }

  private static MarkedStructureSelector IfCondition(string semanticTag)
  {
    return new MarkedStructureSelector(
      RuleSyntaxStructureKind.If,
      RuleSyntaxStructureRole.Condition,
      new RuleSemanticTag(semanticTag));
  }

  private static RuleStructureContractGraphNode Producer(
    string ruleId,
    MarkedStructureSelector selector,
    bool isEnabled = true)
  {
    return new RuleStructureContractGraphNode(
      RuleNodeId.For(RuleKind.Mark, ruleId),
      RuleKind.Mark,
      RuleConsumesContract.Empty,
      new RuleProducesContract(new[] { selector }),
      isEnabled);
  }

  private static RuleStructureContractGraphNode Consumer(
    string ruleId,
    MarkedStructureSelector selector,
    RuleInputCardinality cardinality)
  {
    return new RuleStructureContractGraphNode(
      RuleNodeId.For(RuleKind.Propagate, ruleId),
      RuleKind.Propagate,
      new RuleConsumesContract(new[] { new RuleConsumedStructure(selector, cardinality) }),
      RuleProducesContract.Empty);
  }

  private sealed class ContractProducerRule : RuleDefinitionMark
  {
    private readonly MarkedStructureSelector _selector;

    public ContractProducerRule(MarkedStructureSelector selector)
    {
      _selector = selector;
    }

    public override string RuleId => "TEST-CONTRACT-PRODUCER";

    public override string Name => "Produce a declared if-condition structure.";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
      new[] { SyntaxKind.IdentifierName };

    public override RuleProducesContract Produces => new(new[] { _selector });

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
      _ = context;
      _ = root;
      return Array.Empty<MarkRecord>();
    }
  }

  private sealed class ContractConsumerRule : RuleDefinitionPropagate
  {
    private readonly MarkedStructureSelector _selector;

    public ContractConsumerRule(MarkedStructureSelector selector)
    {
      _selector = selector;
    }

    public override string RuleId => "TEST-CONTRACT-CONSUMER";

    public override string Name => "Consume a declared if-condition structure.";

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
      new[] { SyntaxKind.IdentifierName };

    public override RuleConsumesContract Consumes => new(new[]
    {
      new RuleConsumedStructure(_selector, RuleInputCardinality.ExactlyOne)
    });

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
      _ = context;
      _ = seedMarks;
      return Array.Empty<PropagatedMarkRecord>();
    }
  }

  private static IfStatementSyntax ParseIfStatement(string statement)
  {
    var tree = CSharpSyntaxTree.ParseText($"class Demo {{ void Run() {{ {statement} }} }}");
    return tree.GetRoot().DescendantNodes()
      .OfType<IfStatementSyntax>()
      .Single(ifStatement => ifStatement.Parent is not ElseClauseSyntax);
  }
}
