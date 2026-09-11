using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CoverageEvidenceTests
{
  [Fact]
  public void FlowLogicalExpression_IsTopologyHost_AndNeverStructureCompleteCapability()
  {
    Assert.Equal(
      FactCapability.TopologyHost,
      FactCapabilityRules.For(RuleFactKind.FlowLogicalExpression));
    Assert.DoesNotContain(
      "StructureComplete",
      Enum.GetNames<FactCapability>());
  }

  [Fact]
  public void FromPropagated_PreservesPayloadSourceDepthAndProvenance()
  {
    var source = new MarkRecord(
      "seed",
      SyntaxFactory.IdentifierName("s"),
      null,
      null,
      "seed",
      FactKind: RuleFactKind.TargetExpression);
    var current = new MarkRecord(
      "propagate",
      SyntaxFactory.ParseExpression("ready && s.IsReady"),
      null,
      null,
      "topology",
      FactKind: RuleFactKind.FlowLogicalExpression);
    var payload = new { Name = "logical-chain", Version = 2 };
    var provenance = new FactProvenance(
      "propagate",
      FactSourceStage.Propagate,
      new[] { "fact:seed" },
      new[] { "seed -> logical" },
      4);
    var propagated = new PropagatedMarkRecord(
      "propagate",
      current,
      source,
      4,
      payload,
      provenance);

    var evidence = CoverageEvidence.FromPropagated(propagated, "tree:v2");

    Assert.Same(payload, evidence.Payload);
    Assert.Same(source, evidence.SourceMark);
    Assert.Equal(4, evidence.Depth);
    Assert.Equal(provenance, evidence.Provenance);
    Assert.Equal("tree:v2", evidence.Identity.SourceTreeVersion);
    Assert.NotEqual(
      evidence.Identity,
      CoverageEvidence.FromPropagated(
        propagated with { Payload = new { Name = "different", Version = 2 } },
        "tree:v2").Identity);
  }

  [Fact]
  public void DifferentFactKindPayloadOrProvenance_HaveDifferentIdentities()
  {
    var node = SyntaxFactory.IdentifierName("s");
    var first = CoverageEvidence.FromMark(
      new MarkRecord("rule", node, null, null, "one", FactKind: RuleFactKind.TargetExpression),
      payload: "one",
      provenance: new FactProvenance("rule", FactSourceStage.Mark));
    var second = CoverageEvidence.FromMark(
      new MarkRecord("rule", node, null, null, "two", FactKind: RuleFactKind.FlowLogicalExpression),
      payload: "two",
      provenance: new FactProvenance("other-rule", FactSourceStage.Mark));

    Assert.NotEqual(first.Identity, second.Identity);
  }

  [Fact]
  public void PropagationFactKey_DoesNotMergePayloadOrProvenanceVariants()
  {
    var node = SyntaxFactory.IdentifierName("s");
    var mark = new MarkRecord(
      "rule",
      node,
      null,
      null,
      "fact",
      FactKind: RuleFactKind.TargetExpression,
      SourceTreeVersion: "tree:v1");
    var source = mark with
    {
      Provenance = new FactProvenance("source-a", FactSourceStage.Mark)
    };
    var first = new PropagatedMarkRecord(
      "rule",
      mark,
      source,
      1,
      Payload: "payload-a",
      Provenance: new FactProvenance("rule", FactSourceStage.Propagate, depth: 1));
    var second = first with
    {
      Payload = "payload-b",
      Provenance = new FactProvenance("rule", FactSourceStage.Propagate, new[] { "other-source" }, depth: 1)
    };

    Assert.NotEqual(PropagationFactKey.Create(first), PropagationFactKey.Create(second));
  }
}
