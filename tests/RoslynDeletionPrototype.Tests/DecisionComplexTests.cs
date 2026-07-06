using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Tests.TestCodeSet.Decision;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionComplexTests
{
    [Fact]
    public void Analyze_NestedLogicalAndCondition_ReducesOnlyTargetOperand()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          DecisionComplexSources.NestedLogicalAndReductionSource,
          "nested-logical-and.cs",
          CreateOptions("s"));

        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Replace &&
          IsNodeKind(decision.FinalNode, SyntaxKind.LogicalAndExpression));
        TextDiffAssert.Contains("if (ready && enabled)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("if (enabled)", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_NestedLogicalOrCondition_ReducesOnlyTargetOperand()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          DecisionComplexSources.NestedLogicalOrReductionSource,
          "nested-logical-or.cs",
          CreateOptions("s"));

        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Replace &&
          IsNodeKind(decision.FinalNode, SyntaxKind.LogicalOrExpression));
        TextDiffAssert.Contains("if (ready || fallback)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("if (fallback)", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_WhenParentControlHostDeletes_NestedBodyDecisionsCollapseIntoParent()
    {
        var application = CreateApplication();

        var result = application.Analyze(
          DecisionComplexSources.ParentHostWinsOverNestedBodySource,
          "parent-host-wins.cs",
          CreateOptions("s"));

        Assert.Single(result.Decisions);
        var decision = result.Decisions[0];
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.True(IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("s.Touch();", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 0;", result.RewrittenSource, result.DiffText);
    }

    private static DeletionApplicationService CreateApplication()
    {
        return new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
    }

    private static Dictionary<string, string> CreateOptions(string targetName)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          ["target-name"] = targetName
        };
    }

    private static bool IsNodeKind(Microsoft.CodeAnalysis.SyntaxNode node, SyntaxKind kind)
    {
        return node.RawKind == (int)kind;
    }
}
