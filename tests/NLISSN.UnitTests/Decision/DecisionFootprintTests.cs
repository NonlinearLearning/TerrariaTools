using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionFootprintTests
{
  [Fact]
  public void Footprint_ConsumesOriginalNodesButNeverReplacementNode()
  {
    var root = CSharpSyntaxTree.ParseText("value + target").GetRoot();
    var anchor = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
    var replacement = anchor.Left;
    var intent = EditIntent.Create(
      "replace-value",
      anchor,
      DecisionActionKind.Replace,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(anchor), DecisionCpgFactory.BuildNodeKey(anchor.Right) },
      writeNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(replacement) },
      proofReferences: new[] { "proof:expression-replacement" },
      composition: DecisionComposition.Composable);

    Assert.Contains(DecisionCpgFactory.BuildNodeKey(anchor), intent.EraseSet);
    Assert.Contains(DecisionCpgFactory.BuildNodeKey(anchor.Right), intent.EraseSet);
    Assert.Contains(DecisionCpgFactory.BuildNodeKey(replacement), intent.WriteSet);
    Assert.DoesNotContain(DecisionCpgFactory.BuildNodeKey(replacement), intent.EraseSet);
    Assert.NotEqual(intent.AnchorNodeKey, intent.CandidateId);
  }

  [Fact]
  public void Footprint_CandidateIdIsStableAcrossInputOrder()
  {
    var root = CSharpSyntaxTree.ParseText("target").GetRoot();
    var target = root.DescendantNodes().OfType<IdentifierNameSyntax>().Single();
    var first = EditIntent.Create(
      "delete-target",
      target,
      DecisionActionKind.Delete,
      consumedNodeKeys: new[] { "b", "a" },
      proofReferences: new[] { "proof-b", "proof-a" });
    var second = EditIntent.Create(
      "delete-target",
      target,
      DecisionActionKind.Delete,
      consumedNodeKeys: new[] { "a", "b" },
      proofReferences: new[] { "proof-a", "proof-b" });

    Assert.Equal(first.CandidateId, second.CandidateId);
  }

  [Fact]
  public void BehaviorBudget_TracksPreservedEffectsAsTypedObligations()
  {
    var budget = BehaviorBudget.For(
      BehaviorObligationKind.ShortCircuit,
      BehaviorObligationKind.Binding,
      BehaviorObligationKind.ControlFlow);

    Assert.Contains(BehaviorObligationKind.ShortCircuit, budget.RequiredPreservations);
    Assert.Contains(BehaviorObligationKind.Binding, budget.RequiredPreservations);
    Assert.DoesNotContain(BehaviorObligationKind.ShortCircuit, budget.AllowedChanges);
  }
}
