using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CoverageProofTests
{
  [Fact]
  public void StructureComplete_RejectsTopologyHostAlone()
  {
    var host = CSharpSyntaxTree.ParseText("ready && s.IsReady")
      .GetRoot()
      .DescendantNodes()
      .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>()
      .Single();
    var mark = new MarkRecord(
      "flow",
      host,
      null,
      null,
      "logical host",
      FactKind: RuleFactKind.FlowLogicalExpression,
      Capability: FactCapability.TopologyHost);

    var proof = CoverageProofEvaluator.Evaluate(
      CoverageGoal.StructureComplete,
      host,
      new[] { CoverageEvidence.FromMark(mark, sourceTreeVersion: "tree:v1") });

    Assert.Equal(CoverageProofStatus.Rejected, proof.Status);
    Assert.Contains(CoverageRequirement.StructureEvidence, proof.MissingRequirements);
    Assert.Contains(
      proof.RejectedEvidence,
      evidence => evidence.Capability == FactCapability.TopologyHost);
  }

  [Fact]
  public void AtomicTarget_RequiresAnExactAtomicFact()
  {
    var node = CSharpSyntaxTree.ParseText("s.IsReady")
      .GetRoot()
      .DescendantNodes()
      .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>()
      .Single();
    var mark = new MarkRecord(
      "target",
      node,
      null,
      null,
      "atomic",
      FactKind: RuleFactKind.TargetExpression,
      Capability: FactCapability.AtomicTarget);

    var proof = CoverageProofEvaluator.Evaluate(
      CoverageGoal.AtomicTarget,
      node,
      new[] { CoverageEvidence.FromMark(mark, sourceTreeVersion: "tree:v1") });

    Assert.Equal(CoverageProofStatus.Complete, proof.Status);
    Assert.Contains(proof.ConsumedNodeKeys, key => key.Contains(node.SpanStart.ToString(), StringComparison.Ordinal));
  }

  [Fact]
  public void UnknownCertainty_DoesNotBecomeNoMatch()
  {
    var node = CSharpSyntaxTree.ParseText("s").GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>().Single();
    var mark = new MarkRecord(
      "target",
      node,
      null,
      null,
      "truncated",
      FactKind: RuleFactKind.TargetExpression,
      Capability: FactCapability.AtomicTarget,
      Certainty: FactCertainty.Truncated);

    var proof = CoverageProofEvaluator.Evaluate(
      CoverageGoal.AtomicTarget,
      node,
      new[] { CoverageEvidence.FromMark(mark, sourceTreeVersion: "tree:v1") });

    Assert.Equal(CoverageProofStatus.Unknown, proof.Status);
    Assert.NotEmpty(proof.MissingRequirements);
  }

  [Fact]
  public void MarkCoverage_StructureComplete_RejectsTopologyHostWithoutCompleteOperands()
  {
    var root = CSharpSyntaxTree.ParseText("if (ready && s.IsReady) { Run(); }")
      .GetRoot();
    var condition = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>().Single();
    var host = new MarkRecord(
      "flow",
      condition,
      null,
      null,
      "logical host",
      FactKind: RuleFactKind.FlowLogicalExpression,
      Capability: FactCapability.TopologyHost);

    var proof = MarkCoverage.Evaluate(
      CoverageGoal.StructureComplete,
      condition,
      new[] { host });

    Assert.NotEqual(CoverageProofStatus.Complete, proof.Status);
    Assert.Contains(CoverageRequirement.StructureEvidence, proof.MissingRequirements);
  }

  [Fact]
  public void MarkCoverage_LogicalReduction_RequiresHostAndChildEvidence()
  {
    var root = CSharpSyntaxTree.ParseText("ready && s.IsReady")
      .GetRoot();
    var host = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax>().Single();
    var target = host.Right;
    var hostMark = new MarkRecord(
      "flow",
      host,
      null,
      null,
      "logical host",
      FactKind: RuleFactKind.FlowLogicalExpression,
      Capability: FactCapability.TopologyHost);
    var targetMark = new MarkRecord(
      "target",
      target,
      null,
      null,
      "atomic target",
      FactKind: RuleFactKind.TargetExpression,
      Capability: FactCapability.AtomicTarget);

    var proof = MarkCoverage.Evaluate(
      CoverageGoal.LogicalReduction,
      host,
      new[] { hostMark, targetMark });

    Assert.Equal(CoverageProofStatus.Complete, proof.Status);
    Assert.Contains(proof.AcceptedEvidence, evidence => evidence.Capability == FactCapability.TopologyHost);
    Assert.Contains(proof.AcceptedEvidence, evidence => evidence.Capability == FactCapability.AtomicTarget);
  }

  [Fact]
  public void MarkCoverage_StructureProof_IsCompleteOnlyForLiftOwnedEvidence()
  {
    var root = CSharpSyntaxTree.ParseText("if (s.IsReady) { Run(); }")
      .GetRoot();
    var ifStatement = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax>().Single();
    var condition = ifStatement.Condition;
    var target = new MarkRecord(
      "target",
      condition,
      null,
      null,
      "atomic target",
      FactKind: RuleFactKind.TargetExpression,
      Capability: FactCapability.AtomicTarget);

    var proof = MarkCoverage.EvaluateStructureComplete(
      ifStatement,
      new[] { CoverageEvidence.FromMark(target) },
      new[] { condition });

    Assert.Equal(CoverageProofStatus.Complete, proof.Status);
    Assert.Equal(CoverageGoal.StructureComplete, proof.Goal);
    Assert.Contains(FactIdentity.BuildNodeKey(ifStatement), proof.ConsumedNodeKeys);
  }
}
