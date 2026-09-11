using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Application;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class RewriteControlFlowVerifierTests
{
  [Fact]
  public void Verify_WhenMappedFollowingStatementRemainsReachable_AcceptsLoopRemoval()
  {
    const string before = "class Sample { int M() { while (true) { return 1; } int result = 2; return result; } }";
    const string after = "class Sample { int M() { int result = 2; return result; } }";
    var loopStart = before.IndexOf("while", StringComparison.Ordinal);
    var resultStart = before.IndexOf("int result", StringComparison.Ordinal);
    var loopLength = resultStart - loopStart;
    var witness = new RewriteDecisionWitness(
      "rule.loop",
      "loop",
      NLISSN.Core.Decision.DecisionActionKind.Delete,
      "sample.cs",
      new TextSpan(loopStart, loopLength),
      new[] { new TextSpan(loopStart, loopLength) },
      new[] { RewriteControlFlowEffect.RemoveLoop });

    var result = new RoslynControlFlowRewriteVerifier().Verify(
      before,
      after,
      "sample.cs",
      witness,
      new[] { new TextSpan(resultStart, "int result = 2;".Length) },
      new[] { new RewritePlanEdit(loopStart, loopLength, before.Substring(loopStart, loopLength), string.Empty) });

    Assert.True(result.IsSuccess, string.Join("; ", result.Failures.Select(failure => failure.Message)));
  }

  [Fact]
  public void Verify_WhenPreservedSpanIsDeleted_ReportsFailure()
  {
    const string before = "class Sample { int M() { while (true) { return 1; } int result = 2; return result; } }";
    const string after = "class Sample { int M() { } }";
    var loopStart = before.IndexOf("while", StringComparison.Ordinal);
    var returnStart = before.IndexOf("int result", StringComparison.Ordinal);
    var witness = new RewriteDecisionWitness(
      "rule.loop",
      "loop",
      NLISSN.Core.Decision.DecisionActionKind.Delete,
      "sample.cs",
      new TextSpan(loopStart, 5),
      new[] { new TextSpan(loopStart, 5) },
      new[] { RewriteControlFlowEffect.RemoveLoop });

    var result = new RoslynControlFlowRewriteVerifier().Verify(
      before,
      after,
      "sample.cs",
      witness,
      new[] { new TextSpan(returnStart, "int result = 2;".Length) },
      new[] { new RewritePlanEdit(returnStart, "int result = 2;".Length, "int result = 2;", string.Empty) });

    var failure = Assert.Single(result.Failures);
    Assert.Equal(ControlFlowVerificationFailureCode.PreservedSpanModified, failure.Code);
  }

  [Fact]
  public void Verify_WhenPreserveFinallyIsDeclaredButNoFinallyExists_ReportsFailure()
  {
    const string source = "class Sample { int M() { int result = 2; return result; } }";
    var span = new TextSpan(source.IndexOf("int result", StringComparison.Ordinal), "int result = 2;".Length);
    var witness = new RewriteDecisionWitness(
      "rule.finally", "finally", NLISSN.Core.Decision.DecisionActionKind.Delete, "sample.cs",
      span, new[] { span }, new[] { RewriteControlFlowEffect.PreserveFinally });

    var result = new RoslynControlFlowRewriteVerifier().Verify(
      source, source, "sample.cs", witness, new[] { span }, Array.Empty<RewritePlanEdit>());

    Assert.Contains(result.Failures, failure => failure.Code == ControlFlowVerificationFailureCode.FinallyRouteMissing);
  }
}
