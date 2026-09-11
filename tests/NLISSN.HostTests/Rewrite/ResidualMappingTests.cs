using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Rewrite;

public sealed class ResidualMappingTests
{
  [Fact]
  public void ParentReplacement_RetainedChild_IsAppliedInReplacementTree()
  {
    const string source = "value + target";
    var tree = CSharpSyntaxTree.ParseText(source, path: "residual.cs");
    var root = tree.GetRoot();
    var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
    var target = binary.Right;
    var replacement = CSharpSyntaxTree.ParseText("value - target")
      .GetRoot()
      .DescendantNodes()
      .OfType<BinaryExpressionSyntax>()
      .Single();
    var childReplacement = SyntaxFactory.LiteralExpression(
      SyntaxKind.NumericLiteralExpression,
      SyntaxFactory.Literal(42));
    var parentIntent = EditIntent.Create(
      "parent",
      binary,
      DecisionActionKind.Replace,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(binary) },
      writeNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(replacement) },
      proofReferences: new[] { "proof:ExpressionReplacement" },
      composition: DecisionComposition.Composable,
      residualMapping: ResidualMapping.Retains(
        DecisionCpgFactory.BuildNodeKey(target),
        DecisionCpgFactory.BuildNodeKey(replacement.Right)));
    var childIntent = EditIntent.Create(
      "child",
      target,
      DecisionActionKind.Replace,
      proofReferences: new[] { "proof:ExpressionReplacement" },
      composition: DecisionComposition.Composable);
    var parent = CreateDecision(binary, replacement, parentIntent);
    var child = CreateDecision(target, childReplacement, childIntent);
    var plan = new DecisionPlanner().Plan(new[] { parent, child });

    Assert.Equal(DecisionCandidateStatus.Composed, plan.Get("parent").Status);
    Assert.Equal(DecisionCandidateStatus.Composed, plan.Get("child").Status);

    var decisions = new DecisionPlanner().ResolveDecisions(plan);
    var rewriter = new PrototypeRewriter();
    var result = rewriter.BuildPlan(root, CreateSemanticModel(tree), decisions);

    Assert.Contains(result.Operations, operation => operation.ReplacementText.Contains("42", StringComparison.Ordinal));
  }

  [Fact]
  public void ResidualMapping_Unknown_IsNotTreatedAsRetained()
  {
    var mapping = ResidualMapping.Unknown("child", "replacement did not preserve the child anchor");

    Assert.True(mapping.TryMap("child", out var entry));
    Assert.Equal(ResidualMappingKind.Unknown, entry.Kind);
    Assert.DoesNotContain(entry.Kind, new[] { ResidualMappingKind.Retained, ResidualMappingKind.Replaced });
  }

  private static DecisionUnit CreateDecision(
    SyntaxNode anchor,
    SyntaxNode replacement,
    EditIntent intent)
  {
    var anchorFragment = DecisionCpgFactory.CreateFragment(
      $"fragment:{intent.CandidateId}",
      anchor,
      "anchor",
      DecisionActionKind.Replace);
    var replacementFragment = DecisionCpgFactory.CreateFragment(
      $"replacement:{intent.CandidateId}",
      replacement,
      "replacement",
      DecisionActionKind.Replace);
    var unitNode = DecisionCpgFactory.CreateUnit(
      intent.CandidateId,
      DecisionActionKind.Replace,
      anchorFragment,
      intent.CandidateId);
    return new DecisionUnit(
      intent.CandidateId,
      DecisionActionKind.Replace,
      unitNode,
      new[] { anchorFragment, replacementFragment },
      new[]
      {
        DecisionCpgFactory.CreateContainment(unitNode, anchorFragment),
        DecisionCpgFactory.CreateContainment(unitNode, replacementFragment),
      },
      DecisionCpgFactory.CreateSyntaxBindings((anchorFragment, anchor), (replacementFragment, replacement)),
      reason: intent.CandidateId,
      intent: intent);
  }

  private static SemanticModel CreateSemanticModel(SyntaxTree tree)
  {
    var compilation = CSharpCompilation.Create(
      "ResidualMappingTests",
      new[] { tree },
      new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
    return compilation.GetSemanticModel(tree);
  }
}
