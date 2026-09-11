using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.HostTests.Application;

public sealed class MethodDeletionFourStageTests
{
    [Fact]
    public void UnreachableMethodDeletion_UsesAllFourStagesBeforeDeleteDecision()
    {
        const string source = """
          namespace Demo;

          public static class Sample
          {
            public static void Main() { Live(); }
            public static void Live() { }
            public static void Dead() { }
          }
          """;

        var result = new ApplicationService(
          RulePipelineComposer.Compose(new RuleSelection(
            new[] { RuleFeature.UnreachableMethodDeletion })).Pipeline)
          .Analyze(
            source,
            "unreachable-method-four-stages.cs",
            new Dictionary<string, string>
            {
              ["skip-rewrite"] = "true"
            });

        Assert.Single(result.SeedMarks, mark => mark.RuleId == "mark.unreachable-method");
        Assert.Single(result.PropagatedMarks, mark => mark.RuleId == "propagate.unreachable-method");
        Assert.Single(result.LiftedMarks, mark => mark.RuleId == "lift.unreachable-method");
        var decision = Assert.Single(result.Decisions, candidate => candidate.RuleId == "propose.unreachable-method");
        Assert.Equal("Dead", ((MethodDeclarationSyntax)decision.FinalNode).Identifier.Text);
    }

    [Fact]
    public void UnreferencedMethodDeletion_UsesAllFourStagesBeforeDeleteDecision()
    {
        const string source = """
          namespace Demo;

          public static class Sample
          {
            private static void Dead() { }
            public static void Main() { }
          }
          """;

        var result = new ApplicationService(
          RulePipelineComposer.Compose(new RuleSelection(
            new[] { RuleFeature.UnreferencedMethodDeletion })).Pipeline)
          .Analyze(
            source,
            "unreferenced-method-four-stages.cs",
            new Dictionary<string, string>
            {
              ["delete-unreferenced-methods"] = "true",
              ["skip-rewrite"] = "true"
            });

        Assert.Single(result.SeedMarks, mark => mark.RuleId == "mark.unreferenced-method");
        Assert.Single(result.PropagatedMarks, mark => mark.RuleId == "propagate.unreferenced-method");
        Assert.Single(result.LiftedMarks, mark => mark.RuleId == "lift.unreferenced-method");
        var decision = Assert.Single(result.Decisions, candidate => candidate.RuleId == "propose.unreferenced-method");
        Assert.Equal("Dead", ((MethodDeclarationSyntax)decision.FinalNode).Identifier.Text);
    }
}
