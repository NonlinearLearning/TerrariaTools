using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class GraphAnalyzerTests
{
    [Fact]
    public void Analyze_TargetNameSample_DeletesSeededNodesAndPropagatesMarks()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.TargetNameSource;

        var result = application.Analyze(source, "delete-s-object-sample.cs", CreateOptions("s"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Equal(2, result.PropagatedMarks.Count);
        Assert.All(result.SeedMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.IfStatement) && mark.Depth == 1);
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.LocalDeclarationStatement) && mark.Depth == 1);

        Assert.Equal(2, result.Decisions.Count);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.LocalDeclarationStatement));

        Assert.Equal(2, result.Edits.Count);
        Assert.Contains("--- original #1 delete-s-object-sample.cs:", result.DiffText);
        Assert.Contains("var value = s.Seed + offset;", result.DiffText);
        Assert.Contains("+++ rewritten #1", result.DiffText);
        Assert.Contains("<deleted>", result.DiffText);
        Assert.DoesNotContain("var value =", result.RewrittenSource);
        Assert.DoesNotContain("if (s.IsReady)", result.RewrittenSource);
        Assert.Contains("return offset;", result.RewrittenSource);
    }

    [Fact]
    public void Analyze_UnreachableMethodsSample_DeletesConfiguredMethods()
    {
        var application = CreateApplication();
        var source = ReachabilitySources.UnreachableMethodsSource;

        var result = application.Analyze(source, "unreachable-method-sample.cs", CreateOptions(unreachableMethods: "DeadA,DeadB"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Empty(result.PropagatedMarks);
        Assert.All(result.SeedMarks, mark => Assert.Equal("Method", mark.PrimaryGraphNode!.DisplayKind));

        Assert.Equal(2, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
        {
            Assert.Equal(DecisionActionKind.Delete, decision.Action);
            Assert.Equal(SyntaxKind.MethodDeclaration, GetNodeKind(decision.FinalNode));
        });

        Assert.Equal(2, result.Edits.Count);
        Assert.Contains("Main", result.RewrittenSource);
        Assert.Contains("Live", result.RewrittenSource);
        Assert.DoesNotContain("DeadA", result.RewrittenSource);
        Assert.DoesNotContain("DeadB", result.RewrittenSource);
    }

    [Fact]
    public void Analyze_LogicalAndCondition_RewritesToRemainingOperand()
    {
        var application = CreateApplication();
        var source = SObjectLogicalSources.LogicalAndConditionSource;

        var result = application.Analyze(source, "logical-and-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalAndExpression));
        Assert.Contains("ready", result.DiffText);
        Assert.Contains("s.IsReady", result.DiffText);
        Assert.DoesNotContain(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        Assert.Contains("if (ready)", result.RewrittenSource);
        Assert.DoesNotContain("s.IsReady", result.RewrittenSource);
    }

    [Fact]
    public void Analyze_WhileCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.WhileConditionSource;

        var result = application.Analyze(source, "while-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        Assert.Single(result.PropagatedMarks);
        Assert.Single(result.Decisions);
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.WhileStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
        Assert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return offset;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DoCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.DoConditionSource;

        var result = application.Analyze(source, "do-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        Assert.Single(result.PropagatedMarks);
        Assert.Single(result.Decisions);
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.DoStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.DoStatement));
        Assert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return offset;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_ForCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ForConditionSource;

        var result = application.Analyze(source, "for-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        Assert.Single(result.PropagatedMarks);
        Assert.Single(result.Decisions);
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ForStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        Assert.DoesNotContain("for (; s.IsReady;", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("return offset;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_ReturnExpression_DeletesReturnStatementHost()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ReturnExpressionSource;

        var result = application.Analyze(source, "return-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        Assert.Single(result.PropagatedMarks);
        Assert.Single(result.Decisions);
        Assert.Contains(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ReturnStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ReturnStatement));
        Assert.DoesNotContain("return s.Seed;", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_LogicalOrCondition_RewritesToRemainingOperand()
    {
        var application = CreateApplication();
        var source = SObjectLogicalSources.LogicalOrConditionSource;

        var result = application.Analyze(source, "logical-or-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalOrExpression));
        Assert.Contains("ready", result.DiffText);
        Assert.Contains("s.IsReady", result.DiffText);
        Assert.Contains("if (ready)", result.RewrittenSource);
        Assert.DoesNotContain("s.IsReady", result.RewrittenSource);
    }

    [Fact]
    public void Analyze_UnreachableMethodsWithoutEntryPoint_ProducesNoMarks()
    {
        var application = CreateApplication();
        var source = ReachabilitySources.NoEntryPointSource;

        var result = application.Analyze(source, "no-entry-point.cs", CreateOptions(unreachableMethods: "Dead"));

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.PropagatedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
        Assert.Contains("MainEntry", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("Dead();", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("public static void Dead()", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_HonorsExplicitDiffOutPath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-explicit-diff-{Guid.NewGuid():N}.cs");
        var diffPath = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-explicit-diff-{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, CliInputSources.ExplicitDiffOutSource);

        try
        {
            var application = CreateApplication();
            var result = application.AnalyzeFromArgs(new[]
            {
                filePath,
                "--target-name",
                "s",
                "--diff-out",
                diffPath
            });

            Assert.NotNull(result.DiffFilePath);
            Assert.Equal(Path.GetFullPath(diffPath), result.DiffFilePath);
            Assert.True(File.Exists(diffPath));
            Assert.Contains("+++ rewritten #1", File.ReadAllText(diffPath), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (File.Exists(diffPath))
            {
                File.Delete(diffPath);
            }
        }
    }

    [Fact]
    public void Analyze_UnrelatedConflictDomains_KeepMultipleFinalDecisions()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.MultipleDomainsSource;

        var result = application.Analyze(source, "multiple-domains.cs", CreateOptions("s"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Equal(2, result.PropagatedMarks.Count);
        Assert.Equal(2, result.Decisions.Count);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
    }

    [Fact]
    public void Analyze_WhenRuleEmitsUnsupportedNodeKind_ThrowsInvalidOperationException()
    {
        var application = new DeletionApplicationService(new[] { new InvalidNodeKindRule() });
        var source = "class C { void M() { if (true) { } } }";

        var exception = Assert.Throws<InvalidOperationException>(() => application.Analyze(source, "invalid-node-kind.cs", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Contains("TEST-INVALID-001", exception.Message);
        Assert.Contains("IfStatement", exception.Message);
        Assert.Contains("MethodDeclaration", exception.Message);
    }

    private static DeletionApplicationService CreateApplication()
    {
        return new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
    }

    private static Dictionary<string, string> CreateOptions(string? targetName = null, string? unreachableMethods = null)
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

    private static bool IsNodeKind(SyntaxNode node, SyntaxKind kind)
    {
        return node.RawKind == (int)kind;
    }

    private static SyntaxKind GetNodeKind(SyntaxNode node)
    {
        return (SyntaxKind)node.RawKind;
    }

    private sealed class InvalidNodeKindRule : RuleDefinition
    {
        public string RuleId { get; } = "TEST-INVALID-001";

        public string Name { get; } = "Emit unsupported node kind";

        public IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
          new[] { SyntaxKind.MethodDeclaration };

        public IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
          new[] { SyntaxKind.MethodDeclaration };

        public IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
          new[] { SyntaxKind.MethodDeclaration };

        public IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
          Array.Empty<SyntaxKind>();

        public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            var node = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            yield return new MarkRecord(
              RuleId,
              node,
              null,
              null,
              "Emit invalid node kind for validation test.");
        }

        public IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            return Enumerable.Empty<PropagatedMarkRecord>();
        }

        public IEnumerable<DecisionUnit> Propose(
            RuleContext context,
            IReadOnlyList<MarkRecord> seedMarks,
            IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            return Enumerable.Empty<DecisionUnit>();
        }
    }
}
