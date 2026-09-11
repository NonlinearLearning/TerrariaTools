using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Model;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Validation;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class AuthorityFunnelTests
{
  [Fact]
  public void RawMark_CannotAuthorizeStructuralDelete()
  {
    var root = CSharpSyntaxTree.ParseText("if (target) { Run(); }").GetRoot();
    var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
    var rawMark = new MarkRecord(
      "mark.raw",
      ifStatement,
      null,
      null,
      "Raw observation.",
      FactKind: RuleFactKind.TargetExpression,
      Capability: FactCapability.AtomicTarget);

    var authorization = new CandidateValidator().Authorize(
      rawMark,
      ifStatement,
      DecisionActionKind.Delete);

    Assert.Equal(EditIntentStatus.Rejected, authorization.Status);
    Assert.Contains("MarkRecord", authorization.Reason, StringComparison.Ordinal);
  }

  [Fact]
  public void ForgedLiftedStructureWithoutAcceptedProof_IsRejected()
  {
    var root = CSharpSyntaxTree.ParseText("if (target) { Run(); }").GetRoot();
    var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
    var source = MarkRecordFactory.Create(
      "fake.lift",
      ifStatement.Condition,
      "Source observation.",
      factKind: RuleFactKind.TargetExpression,
      capability: FactCapability.AtomicTarget);
    var liftedMark = new LiftedMarkRecord(
      "fake.lift",
      source with { FactKind = RuleFactKind.LiftIfStructure },
      source,
      1,
      StructuralKind.If,
      new IfStructureLiftPayload(
        ifStatement,
        null,
        null,
        IfStructureLiftKind.DeleteWholeIf,
        new CoverageProof(
          CoverageGoal.StructureComplete,
          CoverageProofStatus.Complete,
          acceptedEvidence: Array.Empty<CoverageEvidence>(),
          consumedNodeKeys: new[] { FactIdentity.BuildNodeKey(ifStatement) }.ToHashSet(StringComparer.Ordinal))));

    var validation = new CandidateValidator().Validate(liftedMark);

    Assert.Equal(EditIntentStatus.Rejected, validation.Status);
    Assert.Contains("proof", validation.Reason, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void RuleDecisionWithoutPlannerProof_CannotBecomeExecutablePlan()
  {
    var root = CSharpSyntaxTree.ParseText("target;").GetRoot();
    var target = root.DescendantNodes().OfType<ExpressionStatementSyntax>().Single();
    var fragment = DecisionCpgFactory.CreateFragment(
      "fragment:untrusted",
      target,
      "anchor",
      DecisionActionKind.Delete);
    var unitNode = DecisionCpgFactory.CreateUnit(
      "untrusted",
      DecisionActionKind.Delete,
      fragment,
      "untrusted");
    var unit = new DecisionUnit(
      "untrusted",
      DecisionActionKind.Delete,
      unitNode,
      new[] { fragment },
      new[] { DecisionCpgFactory.CreateContainment(unitNode, fragment) },
      DecisionCpgFactory.CreateSyntaxBindings((fragment, target)),
      reason: "untrusted");
    var decisionPlan = new DecisionPlanner().Plan(new[] { unit });

    var result = new PlanValidator().TryCreateExecutablePlan(
      decisionPlan,
      root,
      out var executablePlan,
      out var report);

    Assert.False(result);
    Assert.Null(executablePlan);
    Assert.Contains(report.Issues, issue => issue.Code == "PLAN001");
  }

  [Fact]
  public void PrototypeRewriter_ExposesExecutablePlanEntryPoint()
  {
    var hasFormalEntryPoint = typeof(PrototypeRewriter)
      .GetMethods()
      .Where(method => method.Name == nameof(PrototypeRewriter.Rewrite))
      .SelectMany(method => method.GetParameters())
      .Any(parameter => parameter.ParameterType == typeof(ExecutablePlan));

    Assert.True(hasFormalEntryPoint);
  }
}
