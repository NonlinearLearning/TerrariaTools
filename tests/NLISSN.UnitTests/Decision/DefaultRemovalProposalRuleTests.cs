using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DefaultRemovalProposalRuleTests
{
  [Fact]
  public void Propose_OrdinarySeedMark_EmitsDeleteDecision()
  {
    var mark = CreateMark(SyntaxFactory.IdentifierName("s"), "seed");

    var actual = new DefaultRemovalProposalRule().Propose(
      null!,
      new[] { mark },
      Array.Empty<PropagatedMarkRecord>(),
      Array.Empty<LiftedMarkRecord>()).ToArray();

    var decision = Assert.Single(actual);
    Assert.Equal("DEL-SOBJ-PROPOSE-DEFAULT-001", decision.RuleId);
    Assert.Equal(DecisionActionKind.Delete, decision.Action);
    Assert.Equal(mark.SyntaxNode, Assert.Single(decision.SyntaxBindings.Values));
  }

  [Fact]
  public void Propose_SpecializedIfMark_EmitsNoDefaultDecision()
  {
    var mark = CreateMark(SyntaxFactory.ParseStatement("if (flag) { }")!, "if");

    var actual = new DefaultRemovalProposalRule().Propose(
      null!,
      new[] { mark },
      Array.Empty<PropagatedMarkRecord>(),
      Array.Empty<LiftedMarkRecord>());

    Assert.Empty(actual);
  }

  [Fact]
  public void Propose_OrdinaryDerivedMark_PreservesSourceBinding()
  {
    var source = CreateMark(SyntaxFactory.IdentifierName("s"), "seed");
    var derived = CreateMark(SyntaxFactory.ParseExpression("s.Member"), "derived");
    var propagation = new PropagatedMarkRecord("propagation", derived, source, Depth: 1);

    var actual = new DefaultRemovalProposalRule().Propose(
      null!,
      Array.Empty<MarkRecord>(),
      new[] { propagation },
      Array.Empty<LiftedMarkRecord>()).ToArray();

    var decision = Assert.Single(actual);
    Assert.Equal(DecisionActionKind.Delete, decision.Action);
    Assert.Contains(derived.SyntaxNode, decision.SyntaxBindings.Values);
    Assert.Contains(source.SyntaxNode, decision.SyntaxBindings.Values);
  }

  [Fact]
  public void Propose_SpecializedDerivedMark_EmitsNoDefaultDecision()
  {
    var source = CreateMark(SyntaxFactory.IdentifierName("s"), "seed");
    var derived = CreateMark(SyntaxFactory.ParseStatement("if (flag) { }")!, "derived-if");
    var propagation = new PropagatedMarkRecord("propagation", derived, source, Depth: 1);

    var actual = new DefaultRemovalProposalRule().Propose(
      null!,
      Array.Empty<MarkRecord>(),
      new[] { propagation },
      Array.Empty<LiftedMarkRecord>());

    Assert.Empty(actual);
  }

  private static MarkRecord CreateMark(SyntaxNode syntaxNode, string reason)
  {
    return new MarkRecord("test-mark", syntaxNode, null, null, reason);
  }
}
