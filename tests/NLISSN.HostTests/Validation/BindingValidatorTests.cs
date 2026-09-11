using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Core.Validation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests.Validation;

public sealed class BindingValidatorTests
{
  [Fact]
  public void Analyze_WhenValidationIsNotEnabled_DoesNotChangeDefaultAnalysisSurface()
  {
    var result = CreateApplication().Analyze(
      "class Demo { void Main() { } }",
      "validation.cs",
      new Dictionary<string, string> { ["target-name"] = "s" });

    Assert.Null(result.ValidationReport);
  }

  [Fact]
  public void Analyze_WhenValidationIsEnabled_IsStableAcrossDopOneAndSixteen()
  {
    var serial = AnalyzeWithValidation(1);
    var parallel = AnalyzeWithValidation(16);

    Assert.NotNull(serial.ValidationReport);
    Assert.NotNull(parallel.ValidationReport);
    Assert.True(serial.ValidationReport!.IsValid);
    Assert.True(parallel.ValidationReport!.IsValid);
    Assert.Equal(
      serial.ValidationReport.Issues.Select(issue => issue.StableKey),
      parallel.ValidationReport.Issues.Select(issue => issue.StableKey));
  }

  [Fact]
  public void Validate_WhenReplaceHasNoReplacementOrEvidence_ReportsStableCodes()
  {
    var root = CSharpSyntaxTree.ParseText("class Demo { }").GetRoot();
    var anchor = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
    var fragment = DecisionCpgFactory.CreateFragment("anchor", anchor, "anchor", DecisionActionKind.Replace);
    var unitNode = DecisionCpgFactory.CreateUnit("test", DecisionActionKind.Replace, fragment, "test");
    var unit = new DecisionUnit(
      "test",
      DecisionActionKind.Replace,
      unitNode,
      new[] { fragment },
      Array.Empty<NLCPG.Model.NLCPGEdge>(),
      DecisionCpgFactory.CreateSyntaxBindings((fragment, anchor)));
    var decision = new RuleDecision(anchor, anchor, DecisionActionKind.Replace, "test");

    var report = new DecisionBindingValidator().Validate(
      root,
      new[] { unit },
      new[] { decision },
      AnalysisEvidenceGraph.Empty);

    Assert.Equal(
      new[] { "DEC005", "DEC009", "DEC011" },
      report.Issues.Select(issue => issue.Code));
  }

  [Fact]
  public void Analyze_WhenRelationPayloadUsesTargetExpressionPort_ReportsBind014()
  {
    var application = new ApplicationService(
      new RuleDefinitionMark[] { new InvalidRelationPayloadMarkRule() },
      new RuleDefinitionPropagate[] { new InvalidRelationPayloadPropagationRule() },
      Array.Empty<NLISSN.Core.Lifting.RuleDefinitionLift>(),
      Array.Empty<NLISSN.Core.Decision.RuleDefinitionPropose>());

    var result = application.Analyze(
      "class Demo { }",
      "invalid-relation-port.cs",
      new Dictionary<string, string>
      {
        ["validate-bindings"] = "true",
        ["skip-rewrite"] = "true",
      });

    Assert.NotNull(result.ValidationReport);
    Assert.Contains(result.ValidationReport!.Issues, issue => issue.Code == "BIND014");
  }

  private static ApplicationService CreateApplication()
  {
    return new ApplicationService(RulePipelineComposer.Compose(new RuleSelection()).Pipeline);
  }

  private static NLISSN.Core.Rewrite.PrototypeAnalysisResult AnalyzeWithValidation(
    int maxDegreeOfParallelism)
  {
    return CreateApplication().Analyze(
      "class Demo { void Main() { } }",
      "validation.cs",
      new Dictionary<string, string>
      {
        ["target-name"] = "s",
        ["validate-bindings"] = "true",
        ["max-degree-of-parallelism"] = maxDegreeOfParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["enable-group-parallelism"] = "true",
      });
  }

  private sealed class InvalidRelationPayloadMarkRule : RuleDefinitionMark
  {
    public override string RuleId => "TEST-MARK-INVALID-RELATION-PORT-001";

    public override string Name => "Seed a class declaration for relation port validation";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { SyntaxKind.ClassDeclaration };

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.ClassDeclaration }, RuleFactKind.TargetExpression)
    });

    public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
    {
      var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
      yield return new MarkRecord(
        RuleId,
        declaration,
        null,
        null,
        "Test seed.",
        FactKind: RuleFactKind.TargetExpression,
        Origins: RuleEvidenceOrigin.DeclarationType);
    }
  }

  private sealed class InvalidRelationPayloadPropagationRule : RuleDefinitionPropagate
  {
    public override string RuleId => "TEST-PROP-INVALID-RELATION-PORT-001";

    public override string Name => "Attach a relation payload to a non-relation port";

    public override RuleConsumesContract Consumes => new(new[]
    {
      new RuleConsumedSyntax(new[] { SyntaxKind.ClassDeclaration }, RuleFactKind.TargetExpression)
    });

    public override RuleProducesContract Produces => new(new[]
    {
      new RuleProducedSyntax(new[] { SyntaxKind.ClassDeclaration }, RuleFactKind.FlowAssignmentTarget)
    });

    public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds => new[] { SyntaxKind.ClassDeclaration };

    public override IEnumerable<PropagatedMarkRecord> Propagate(
      IPropagationRuleContext context,
      IReadOnlyList<MarkRecord> seedMarks)
    {
      var source = Assert.Single(seedMarks);
      var declaration = context.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
      yield return new PropagatedMarkRecord(
        RuleId,
        source with
        {
          RuleId = RuleId,
          FactKind = RuleFactKind.FlowAssignmentTarget
        },
        source,
        1,
        new DeclarationHostPayload(declaration, DeclarationHostKind.FieldDeclaration));
    }
  }
}
