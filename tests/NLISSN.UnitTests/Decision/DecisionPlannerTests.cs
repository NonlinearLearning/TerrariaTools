using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionPlannerTests
{
  [Fact]
  public void IndependentFootprints_AreBothSelected()
  {
    var root = CSharpSyntaxTree.ParseText("target; other;").GetRoot();
    var nodes = root.DescendantNodes().OfType<ExpressionStatementSyntax>().ToArray();
    var first = CreateUnit("first", nodes[0], DecisionActionKind.Delete, EditIntent.Create(
      "first", nodes[0], DecisionActionKind.Delete,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(nodes[0]) },
      proofReferences: new[] { "proof:first" }));
    var second = CreateUnit("second", nodes[1], DecisionActionKind.Delete, EditIntent.Create(
      "second", nodes[1], DecisionActionKind.Delete,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(nodes[1]) },
      proofReferences: new[] { "proof:second" }));

    var plan = new DecisionPlanner().Plan(new[] { first, second });

    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("first").Status);
    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("second").Status);
  }

  [Fact]
  public void ParentDelete_WithExplicitDominance_ConsumesChildReplace()
  {
    var (root, outer, inner) = NestedIf();
    var parentKey = DecisionCpgFactory.BuildNodeKey(outer);
    var childKey = DecisionCpgFactory.BuildNodeKey(inner);
    var parent = CreateUnit("parent", outer, DecisionActionKind.Delete, EditIntent.Create(
      "parent", outer, DecisionActionKind.Delete,
      consumedNodeKeys: new[] { parentKey },
      eraseNodeKeys: new[] { parentKey, childKey },
      proofReferences: new[] { "proof:StructureComplete" },
      composition: DecisionComposition.OpaqueDominates,
      dominatesChildren: true));
    var child = CreateUnit("child", inner, DecisionActionKind.Replace, EditIntent.Create(
      "child", inner, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { childKey },
      proofReferences: new[] { "proof:LogicalReduction" },
      composition: DecisionComposition.Composable));

    var plan = new DecisionPlanner().Plan(new[] { child, parent });

    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("parent").Status);
    Assert.Equal(DecisionCandidateStatus.Dominated, plan.Get("child").Status);
    Assert.Contains(plan.Relations, relation =>
      relation.Kind == DecisionRelationKind.Dominates &&
      relation.FromCandidateId == "parent" &&
      relation.ToCandidateId == "child");
  }

  [Fact]
  public void ParentDelete_WithoutDominanceProof_DoesNotSilentlyDropChild()
  {
    var (_, outer, inner) = NestedIf();
    var parent = CreateUnit("parent", outer, DecisionActionKind.Delete, EditIntent.Create(
      "parent", outer, DecisionActionKind.Delete,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(outer) }));
    var child = CreateUnit("child", inner, DecisionActionKind.Replace, EditIntent.Create(
      "child", inner, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(inner) },
      proofReferences: new[] { "proof:LogicalReduction" }));

    var plan = new DecisionPlanner().Plan(new[] { parent, child });

    Assert.Equal(DecisionCandidateStatus.Rejected, plan.Get("parent").Status);
    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("child").Status);
    Assert.Contains(plan.Relations, relation => relation.Kind == DecisionRelationKind.Conflicts);
  }

  [Fact]
  public void CompleteDeclarationDelete_DominatesBodyCandidate()
  {
    var root = CSharpSyntaxTree.ParseText(
      "class C { private Target Create() { return new Target(); } }").GetRoot();
    var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
    var body = method.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
    var methodKey = DecisionCpgFactory.BuildNodeKey(method);
    var bodyKeys = method.DescendantNodesAndSelf()
      .Select(DecisionCpgFactory.BuildNodeKey)
      .ToArray();
    var parent = CreateUnit("declaration", method, DecisionActionKind.Delete, EditIntent.Create(
      "declaration", method, DecisionActionKind.Delete,
      consumedNodeKeys: new[] { methodKey },
      eraseNodeKeys: bodyKeys,
      proofReferences: new[] { $"proof:DeclarationBoundary:{methodKey}" },
      composition: DecisionComposition.OpaqueDominates,
      dominatesChildren: true,
      declarationBoundary: true));
    var child = CreateUnit("body", body, DecisionActionKind.Delete, EditIntent.Create(
      "body", body, DecisionActionKind.Delete,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(body) },
      proofReferences: new[] { "proof:AtomicTarget" }));

    var plan = new DecisionPlanner().Plan(new[] { child, parent });

    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("declaration").Status);
    Assert.Equal(DecisionCandidateStatus.Dominated, plan.Get("body").Status);
    Assert.Contains(plan.Relations, relation =>
      relation.Kind == DecisionRelationKind.Dominates &&
      relation.FromCandidateId == "declaration" &&
      relation.ToCandidateId == "body");
  }

  [Fact]
  public void SameAnchorReplacements_AreConflictRegardlessOfInputOrder()
  {
    var root = CSharpSyntaxTree.ParseText("target").GetRoot();
    var target = root.DescendantNodes().OfType<IdentifierNameSyntax>().Single();
    var first = CreateUnit("first", target, DecisionActionKind.Replace, EditIntent.Create(
      "first", target, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(target) },
      proofReferences: new[] { "proof:first" }));
    var second = CreateUnit("second", target, DecisionActionKind.Replace, EditIntent.Create(
      "second", target, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(target) },
      proofReferences: new[] { "proof:second" }));

    var plan = new DecisionPlanner().Plan(new[] { second, first });

    Assert.Equal(DecisionCandidateStatus.Conflict, plan.Get("first").Status);
    Assert.Equal(DecisionCandidateStatus.Conflict, plan.Get("second").Status);
  }

  [Fact]
  public void PartialOverlap_IsConflictInsteadOfSpanWinner()
  {
    var root = CSharpSyntaxTree.ParseText("first + second").GetRoot();
    var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
    var first = CreateUnit("first", binary, DecisionActionKind.Replace, EditIntent.Create(
      "first", binary, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { "node:a", "node:shared" },
      proofReferences: new[] { "proof:first" }));
    var second = CreateUnit("second", binary, DecisionActionKind.Replace, EditIntent.Create(
      "second", binary, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { "node:shared", "node:b" },
      proofReferences: new[] { "proof:second" }));

    var plan = new DecisionPlanner().Plan(new[] { first, second });

    Assert.All(plan.Candidates, candidate => Assert.Equal(DecisionCandidateStatus.Conflict, candidate.Status));
  }

  [Fact]
  public void UnknownAtomicGroupMember_BlocksTheWholeGroup()
  {
    var root = CSharpSyntaxTree.ParseText("first; second;").GetRoot();
    var nodes = root.DescendantNodes().OfType<ExpressionStatementSyntax>().ToArray();
    var first = CreateUnit("first", nodes[0], DecisionActionKind.Replace, EditIntent.Create(
      "first", nodes[0], DecisionActionKind.Replace,
      consumedNodeKeys: new[] { "first" },
      proofReferences: new[] { "proof:first" },
      atomicGroup: "group-1"));
    var unknown = CreateUnit("second", nodes[1], DecisionActionKind.Replace, EditIntent.Create(
      "second", nodes[1], DecisionActionKind.Replace,
      consumedNodeKeys: new[] { "second" },
      proofReferences: new[] { "proof:second" },
      atomicGroup: "group-1",
      status: EditIntentStatus.Unknown));

    var plan = new DecisionPlanner().Plan(new[] { first, unknown });

    Assert.Equal(DecisionCandidateStatus.Blocked, plan.Get("first").Status);
    Assert.Equal(DecisionCandidateStatus.Blocked, plan.Get("second").Status);
  }

  [Fact]
  public void ParentReplace_RemovedChild_IsDominanceAndNotComposition()
  {
    var (_, outer, inner) = NestedIf();
    var outerKey = DecisionCpgFactory.BuildNodeKey(outer);
    var innerKey = DecisionCpgFactory.BuildNodeKey(inner);
    var parent = CreateUnit("parent", outer, DecisionActionKind.Replace, EditIntent.Create(
      "parent", outer, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { outerKey },
      eraseNodeKeys: new[] { outerKey, innerKey },
      writeNodeKeys: new[] { "replacement" },
      proofReferences: new[] { "proof:LogicalReduction" },
      composition: DecisionComposition.Composable,
      residualMapping: ResidualMapping.Removes(innerKey)));
    var child = CreateUnit("child", inner, DecisionActionKind.Replace, EditIntent.Create(
      "child", inner, DecisionActionKind.Replace,
      consumedNodeKeys: new[] { innerKey },
      proofReferences: new[] { "proof:LogicalReduction" },
      composition: DecisionComposition.Composable));

    var plan = new DecisionPlanner().Plan(new[] { parent, child });

    Assert.Equal(DecisionCandidateStatus.Selected, plan.Get("parent").Status);
    Assert.Equal(DecisionCandidateStatus.Dominated, plan.Get("child").Status);
    Assert.Contains(plan.Relations, relation =>
      relation.Kind == DecisionRelationKind.Dominates &&
      relation.FromCandidateId == "parent" &&
      relation.ToCandidateId == "child");
    Assert.DoesNotContain(plan.Relations, relation =>
      relation.Kind == DecisionRelationKind.Composes &&
      relation.FromCandidateId == "parent" &&
      relation.ToCandidateId == "child");
  }

  private static DecisionUnit CreateUnit(string ruleId, SyntaxNode anchor, DecisionActionKind action, EditIntent intent)
  {
    var fragment = DecisionCpgFactory.CreateFragment(
      $"frag:{ruleId}",
      anchor,
      "anchor",
      action);
    var unitNode = DecisionCpgFactory.CreateUnit(ruleId, action, fragment, ruleId);
    return new DecisionUnit(
      ruleId,
      action,
      unitNode,
      new[] { fragment },
      new[] { DecisionCpgFactory.CreateContainment(unitNode, fragment) },
      DecisionCpgFactory.CreateSyntaxBindings((fragment, anchor)),
      reason: ruleId,
      intent: intent);
  }

  private static (SyntaxNode Root, IfStatementSyntax Outer, IfStatementSyntax Inner) NestedIf()
  {
    var root = CSharpSyntaxTree.ParseText("if (outer) { if (inner) { Run(); } }").GetRoot();
    var ifs = root.DescendantNodes().OfType<IfStatementSyntax>().ToArray();
    return (root, ifs[0], ifs[1]);
  }
}
