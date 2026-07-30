using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Core.Decision;
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

  private static ApplicationService CreateApplication()
  {
    return new ApplicationService(RuleRegistry.CreateDefaultRules());
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
}
