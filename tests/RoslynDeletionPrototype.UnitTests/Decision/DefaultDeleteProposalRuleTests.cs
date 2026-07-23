using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DefaultDeleteProposalRuleTests
{
  [Fact]
  public void Propose_OrdinarySeedMark_EmitsDeleteDecision()
  {
    var mark = CreateMark(SyntaxFactory.IdentifierName("s"), "seed");

    var actual = new DefaultDeleteProposalRule().Propose(
      null!,
      new[] { mark },
      Array.Empty<PropagatedMarkRecord>(),
      Array.Empty<LiftedMarkRecord>()).ToArray();

    var decision = Assert.Single(actual);
    Assert.Equal(DeleteSObjectRuleIds.DefaultDeleteProposalRuleId, decision.RuleId);
    Assert.Equal(DecisionActionKind.Delete, decision.Action);
    Assert.Equal(mark.SyntaxNode, Assert.Single(decision.SyntaxBindings.Values));
  }

  [Fact]
  public void Propose_SpecializedIfMark_EmitsNoDefaultDecision()
  {
    var mark = CreateMark(SyntaxFactory.ParseStatement("if (flag) { }")!, "if");

    var actual = new DefaultDeleteProposalRule().Propose(
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

    var actual = new DefaultDeleteProposalRule().Propose(
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

    var actual = new DefaultDeleteProposalRule().Propose(
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
