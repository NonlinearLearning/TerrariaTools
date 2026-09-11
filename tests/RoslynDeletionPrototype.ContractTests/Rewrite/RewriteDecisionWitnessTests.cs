using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Rewrite;

public sealed class RewriteDecisionWitnessTests
{
  [Fact]
  public void ValidateWitness_WhenActionIsNotAllowed_ThrowsInvalidOperationException()
  {
    var contract = CreateContract(allowedActions: new HashSet<DecisionActionKind> { DecisionActionKind.Delete });
    var witness = CreateWitness(DecisionActionKind.Replace, new[] { new TextSpan(0, 4) });

    var exception = Assert.Throws<InvalidOperationException>(() => contract.ValidateWitness(witness));

    Assert.Contains("does not allow action", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ValidatePlanOperations_WhenOperationIsOutsideAuthorizedSpans_ThrowsInvalidOperationException()
  {
    var contract = CreateContract(allowedActions: new HashSet<DecisionActionKind> { DecisionActionKind.Replace });
    var witness = CreateWitness(DecisionActionKind.Replace, new[] { new TextSpan(0, 4) });
    var operations = new[] { new RewritePlanEdit(6, 2, "old", "new") };

    var exception = Assert.Throws<InvalidOperationException>(() =>
      contract.ValidatePlanOperations(witness, operations));

    Assert.Contains("not covered", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ValidateWitness_WhenStructuralPreservationIsRequiredButEffectIsAbsent_ThrowsInvalidOperationException()
  {
    var contract = new RuleTransformationContract(
      "if-structure",
      new HashSet<DecisionActionKind> { DecisionActionKind.Delete },
      new HashSet<RewriteControlFlowEffect> { RewriteControlFlowEffect.RemoveThenBranch },
      true,
      true);
    var witness = CreateWitness(DecisionActionKind.Delete, new[] { new TextSpan(0, 4) }) with
    {
      ContractId = "if-structure"
    };

    var exception = Assert.Throws<InvalidOperationException>(() => contract.ValidateWitness(witness));

    Assert.Contains("structural effect", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DefaultDecisionPolicy_ReplaceDecisionCarriesFinalRuleContractAndWitness()
  {
    var tree = CSharpSyntaxTree.ParseText("class Sample { int M(int value) => value; }", path: "sample.cs");
    var root = tree.GetRoot();
    var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>().ToArray();
    var original = identifiers.Single(identifier => identifier.Identifier.ValueText == "value");
    var replacement = SyntaxFactory.LiteralExpression(
      Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression,
      SyntaxFactory.Literal(0));
    var decision = new RuleDecision(original, original, DecisionActionKind.Replace, "replace")
    {
      RuleId = "rule.replace",
      ContractId = "logical",
      VerificationWitness = new RewriteDecisionWitness(
        "rule.replace",
        "logical",
        DecisionActionKind.Replace,
        "sample.cs",
        original.Span,
        new[] { original.Span },
        new[] { RewriteControlFlowEffect.SimplifyLogicalCondition })
    };

    Assert.Equal("rule.replace", decision.RuleId);
    Assert.Equal("logical", decision.ContractId);
    Assert.Equal(original.Span, decision.VerificationWitness.Anchor);
    Assert.Equal(new[] { original.Span }, decision.VerificationWitness.AuthorizedSpans);
  }

  private static RuleTransformationContract CreateContract(IReadOnlySet<DecisionActionKind> allowedActions)
  {
    return new RuleTransformationContract(
      "logical",
      allowedActions,
      new HashSet<RewriteControlFlowEffect> { RewriteControlFlowEffect.SimplifyLogicalCondition },
      true,
      false);
  }

  private static RewriteDecisionWitness CreateWitness(
    DecisionActionKind action,
    IReadOnlyList<TextSpan> authorizedSpans)
  {
    return new RewriteDecisionWitness(
      "rule.replace",
      "logical",
      action,
      "sample.cs",
      authorizedSpans[0],
      authorizedSpans,
      Array.Empty<RewriteControlFlowEffect>());
  }
}
