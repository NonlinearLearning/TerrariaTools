using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DeletionApplicationServiceFlowTests
{
    [Fact]
    public void Analyze_DefaultRules_RunTargetRuleAndReachabilityRuleInOnePipeline()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          ReachabilitySources.MixedRulePipelineSource,
          "mixed-rule-pipeline.cs",
          CreateOptions(targetName: "s"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Contains(result.SeedMarks, mark =>
          IsNodeKind(mark.SyntaxNode, SyntaxKind.AddExpression));
        Assert.Contains(result.SeedMarks, mark =>
          IsNodeKind(mark.SyntaxNode, SyntaxKind.MethodDeclaration));
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          IsNodeKind(decision.FinalNode, SyntaxKind.LocalDeclarationStatement));
        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          IsNodeKind(decision.FinalNode, SyntaxKind.MethodDeclaration));
        Assert.Equal(2, result.Edits.Count);
        Assert.DoesNotContain("var value = s.Seed + 1;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void Dead()", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("public static void Main()", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("public static int Live(Box s)", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_ReachabilityRule_IgnoresLegacyConfiguredMethodNamesOption()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          ReachabilitySources.ReachabilityIgnoresConfiguredMethodNamesSource,
          "reachability-ignores-config.cs",
          CreateOptions(unreachableMethods: "Live,Helper"));

        Assert.Single(result.SeedMarks);
        Assert.All(result.SeedMarks, mark =>
          Assert.Equal(SyntaxKind.MethodDeclaration, (SyntaxKind)mark.SyntaxNode.RawKind));
        Assert.Single(result.Decisions);
        Assert.DoesNotContain("public static void Dead()", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("public static void Live()", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("public static void Helper()", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_WhenTargetNameMissing_StillRunsReachabilityRule()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          ReachabilitySources.MixedRulePipelineSource,
          "target-option-missing.cs",
          CreateOptions());

        Assert.Single(result.SeedMarks);
        Assert.Contains(result.SeedMarks, mark =>
          IsNodeKind(mark.SyntaxNode, SyntaxKind.MethodDeclaration));
        Assert.Single(result.Decisions);
        Assert.DoesNotContain("public static void Dead()", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("var value = s.Seed + 1;", result.RewrittenSource, StringComparison.Ordinal);
    }

    private static DeletionApplicationService CreateApplication()
    {
        return new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
    }

    private static Dictionary<string, string> CreateOptions(
      string? targetName = null,
      string? unreachableMethods = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        if (!string.IsNullOrWhiteSpace(unreachableMethods))
        {
            options["unreachable-methods"] = unreachableMethods;
        }

        return options;
    }

    private static bool IsNodeKind(Microsoft.CodeAnalysis.SyntaxNode node, SyntaxKind kind)
    {
        return node.RawKind == (int)kind;
    }
}
