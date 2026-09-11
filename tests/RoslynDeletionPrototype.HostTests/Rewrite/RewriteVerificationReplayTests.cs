using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RewriteVerificationReplayTests
{
  [Fact]
  public void DirectInMemoryAndPersistedReplay_ProduceEquivalentVerificationEvidence()
  {
    const string source = "class Sample { int M(int value) => value; }";
    var tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
    var root = tree.GetRoot();
    var compilation = CSharpCompilation.Create("sample", new[] { tree });
    var semanticModel = compilation.GetSemanticModel(tree);
    var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>()
      .Single(node => node.Identifier.ValueText == "value");
    var witness = new RewriteDecisionWitness(
      "rule.replace",
      "logical",
      DecisionActionKind.Replace,
      "sample.cs",
      identifier.Span,
      new[] { identifier.Span },
      new[] { RewriteControlFlowEffect.SimplifyLogicalCondition });
    var decision = new RuleDecision(identifier, identifier, DecisionActionKind.Replace, "replace", SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))
    {
      RuleId = "rule.replace",
      ContractId = "logical",
      VerificationWitness = witness
    };
    var rewriter = new PrototypeRewriter();
    var direct = rewriter.Rewrite(root, semanticModel, new[] { decision });
    var plan = rewriter.BuildPlan(root, semanticModel, new[] { decision });
    var inMemory = rewriter.ExecutePlan(source, "sample.cs", plan);
    var persisted = rewriter.ExecutePlan(source, "sample.cs", new RewritePlanFile("sample.cs", "unused", plan.Operations));
    var verifier = new RewriteVerifier();

    var directVerification = Verify(verifier, source, direct, decision);
    var inMemoryVerification = Verify(verifier, source, inMemory, decision);
    var persistedVerification = Verify(verifier, source, persisted, decision);

    Assert.Equal(direct.RewrittenSource, inMemory.RewrittenSource);
    Assert.Equal(direct.RewrittenSource, persisted.RewrittenSource);
    Assert.Equal(directVerification.UnexpectedDiagnostics, inMemoryVerification.UnexpectedDiagnostics);
    Assert.Equal(directVerification.UnexpectedDiagnostics, persistedVerification.UnexpectedDiagnostics);
    Assert.Equal(directVerification.Failures, inMemoryVerification.Failures);
    Assert.Equal(directVerification.Failures, persistedVerification.Failures);
  }

  private static RewriteVerificationResult Verify(RewriteVerifier verifier, string source, PrototypeRewriteResult result, RuleDecision decision)
  {
    return verifier.Verify(
      new Dictionary<string, string>(StringComparer.Ordinal) { ["sample.cs"] = source },
      new Dictionary<string, string>(StringComparer.Ordinal) { ["sample.cs"] = Assert.IsType<string>(result.RewrittenSource) },
      new[] { decision },
      new Dictionary<string, IReadOnlyList<RewritePlanEdit>>(StringComparer.Ordinal)
      {
        ["sample.cs"] = result.Operations ?? Array.Empty<RewritePlanEdit>()
      });
  }
}
