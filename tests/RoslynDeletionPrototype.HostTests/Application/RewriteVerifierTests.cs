using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RewriteVerifierTests
{
  [Fact]
  public void Verify_WhenPlanOperationIsOutsideFinalDecisionAnchor_ReportsUnauthorizedEdit()
  {
    const string source = "class Sample { int M() => 1; }";
    var decision = CreateDeleteDecision(source, new[] { new Microsoft.CodeAnalysis.Text.TextSpan(0, 5) });
    var result = new RewriteVerifier().Verify(
      Sources(source),
      Sources(source),
      new[] { decision },
      Plans("sample.cs", new RewritePlanEdit(6, 5, "Sample", "Other")));

    var failure = Assert.Single(result.Failures);
    Assert.Equal(RewriteVerificationFailureCode.UnauthorizedPlanOperation, failure.Code);
  }

  [Fact]
  public void Verify_WhenReplacementDecisionHasNoWitness_ReportsMissingWitness()
  {
    const string source = "class Sample { int M() => 1; }";
    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var literal = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
    var decision = new RuleDecision(literal, literal, DecisionActionKind.Replace, "replace")
    {
      RuleId = "rule.replace"
    };
    var result = new RewriteVerifier().Verify(
      Sources(source),
      Sources("class Sample { int M() => 2; }"),
      new[] { decision },
      Plans("sample.cs", new RewritePlanEdit(literal.SpanStart, literal.Span.Length, "1", "2")));

    var failure = Assert.Single(result.Failures);
    Assert.Equal(RewriteVerificationFailureCode.MissingReplacementWitness, failure.Code);
  }

  [Fact]
  public void Verify_WhenExistingDiagnosticIsUnchanged_DoesNotReportUnexpectedDiagnostic()
  {
    const string source = "public sealed class Sample { MissingType Value; }";

    var result = new RewriteVerifier().Verify(
      Sources(source),
      Sources(source),
      Array.Empty<RuleDecision>(),
      new Dictionary<string, IReadOnlyList<RewritePlanEdit>>(StringComparer.Ordinal));

    Assert.Empty(result.UnexpectedDiagnostics);
    Assert.Empty(result.Failures);
  }

  [Fact]
  public void Verify_WhenReplacementPlanIsOutsideWitnessSpan_ReportsUnauthorizedEdit()
  {
    const string source = "class Sample { int M() => 1; }";
    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var literal = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
    var witness = new RewriteDecisionWitness(
      "rule.replace",
      "logical",
      DecisionActionKind.Replace,
      "sample.cs",
      literal.Span,
      new[] { literal.Span },
      new[] { RewriteControlFlowEffect.SimplifyLogicalCondition });
    var decision = new RuleDecision(literal, literal, DecisionActionKind.Replace, "replace")
    {
      RuleId = "rule.replace",
      ContractId = "logical",
      VerificationWitness = witness
    };
    var result = new RewriteVerifier().Verify(
      Sources(source),
      Sources(source),
      new[] { decision },
      Plans("sample.cs", new RewritePlanEdit(6, 5, "Sample", "Other")));

    var failure = Assert.Single(result.Failures);
    Assert.Equal(RewriteVerificationFailureCode.UnauthorizedPlanOperation, failure.Code);
  }

  private static RuleDecision CreateDeleteDecision(
    string source,
    IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> authorizedSpans)
  {
    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var classDeclaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
    return new RuleDecision(classDeclaration, classDeclaration, DecisionActionKind.Delete, "delete")
    {
      RuleId = "rule.delete",
      ContractId = "delete",
      VerificationWitness = new RewriteDecisionWitness(
        "rule.delete",
        "delete",
        DecisionActionKind.Delete,
        "sample.cs",
        classDeclaration.Span,
        authorizedSpans,
        Array.Empty<RewriteControlFlowEffect>())
    };
  }

  private static IReadOnlyDictionary<string, string> Sources(string source)
  {
    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["sample.cs"] = source
    };
  }

  private static IReadOnlyDictionary<string, IReadOnlyList<RewritePlanEdit>> Plans(
    string filePath,
    params RewritePlanEdit[] operations)
  {
    return new Dictionary<string, IReadOnlyList<RewritePlanEdit>>(StringComparer.Ordinal)
    {
      [filePath] = operations
    };
  }
}
