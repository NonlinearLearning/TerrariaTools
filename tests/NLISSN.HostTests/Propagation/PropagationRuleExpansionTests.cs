using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Application;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PropagationRuleExpansionTests
{
    [Fact]
    public void Analyze_ConditionalAccessInvoke_PropagatesToConditionalAccess()
    {
        var result = Analyze(PropagationSources.ConditionalAccessInvokeSource, "conditional-access-invoke.cs", "Invoke");

        AssertContainsEffective(result, SyntaxKind.ConditionalAccessExpression, "handler?.Invoke()");
    }

    [Fact]
    public void Analyze_ObjectCreationWithInitializer_PropagatesThroughInitializerAndCreation()
    {
        var result = Analyze(PropagationSources.ObjectCreationWithInitializerSource, "object-creation-initializer.cs", "Seed");

        AssertContainsEffectiveText(result, SyntaxKind.ObjectCreationExpression, "new Holder");
    }

    [Fact]
    public void Analyze_ConditionalExpression_PropagatesToTernaryHost()
    {
        var result = Analyze(PropagationSources.ConditionalExpressionSource, "conditional-expression.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ConditionalExpression, "ready ? s.Seed : 0");
    }

    [Fact]
    public void Analyze_TransparentWrappers_PropagateOneWrapperAtATime()
    {
        var result = Analyze(PropagationSources.TransparentWrappersSource, "transparent-wrappers.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ParenthesizedExpression, "(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CastExpression, "(int)(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CheckedExpression, "checked((int)(s.Seed))");
    }

    [Fact]
    public void Analyze_InterpolatedString_PropagatesThroughInterpolation()
    {
        var result = Analyze(PropagationSources.InterpolatedStringSource, "interpolated-string.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.Interpolation, "{s.Seed}");
        AssertContainsPropagated(result, SyntaxKind.InterpolatedStringExpression, "$\"value {s.Seed}\"");
    }

    [Fact]
    public void Analyze_YieldThrowAndArrow_PropagateToStatementOrClauseHosts()
    {
        var result = Analyze(PropagationSources.YieldThrowAndArrowSource, "yield-throw-arrow.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.YieldReturnStatement, "yield return s.Seed;");
        AssertContainsPropagatedText(result, SyntaxKind.ThrowStatement, "throw new InvalidOperationException(s.Text);");
        AssertContainsPropagated(result, SyntaxKind.ArrowExpressionClause, "=> s.Seed");
    }

    [Fact]
    public void Analyze_ResourceAndLoopHeaders_PropagateToOwningStatements()
    {
        var result = AnalyzeWithSeeds(
          PropagationSources.ResourceAndLoopHeadersSource,
          "resource-loop-headers.cs",
          (SyntaxKind.SimpleMemberAccessExpression, "s.Sync"),
          (SyntaxKind.InvocationExpression, "s.Open()"),
          (SyntaxKind.SimpleMemberAccessExpression, "s.Seed"),
          (SyntaxKind.SimpleMemberAccessExpression, "s.Values"));

        AssertContainsPropagatedText(result, SyntaxKind.LockStatement, "lock (s.Sync)");
        AssertContainsPropagatedText(result, SyntaxKind.UsingStatement, "using (s.Open())");
        AssertContainsPropagatedText(result, SyntaxKind.FixedStatement, "fixed (int* value = &s.Seed)");
        AssertContainsPropagatedText(result, SyntaxKind.ForEachStatement, "foreach (var value in s.Values)");
    }

    [Fact]
    public void Analyze_SwitchExpression_PropagatesThroughArmAndExpression()
    {
        var result = Analyze(PropagationSources.SwitchExpressionSource, "switch-expression.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpressionArm, "0 => s.Seed");
        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpression, "value switch");
    }

    [Fact]
    public void Analyze_ArgumentShell_PropagatesThroughArgumentListAndInvocation()
    {
        var result = AnalyzeWithSeed(
          PropagationSources.ArgumentShellSource,
          "argument-shell.cs",
          SyntaxKind.SimpleMemberAccessExpression,
          "s.Seed");

        AssertContainsPropagated(result, SyntaxKind.Argument, "s.Seed");
        AssertContainsPropagated(result, SyntaxKind.ArgumentList, "(s.Seed, 1)");
        AssertContainsPropagated(result, SyntaxKind.InvocationExpression, "Combine(s.Seed, 1)");
    }

    [Fact]
    public void Analyze_ChainedMemberAccess_PropagatesToOutermostAccessAndInvocation()
    {
        var result = Analyze(PropagationSources.ChainedMemberAccessSource, "chained-member-access.cs", "s");

        AssertContainsEffective(result, SyntaxKind.InvocationExpression, "s.Inner.Next.Value()");
        AssertContainsPropagatedText(result, SyntaxKind.ReturnStatement, "return s.Inner.Next.Value();");
    }

    [Fact]
    public void Analyze_SymbolReference_PropagatesFromMarkedDefinitionToLaterReferences()
    {
        var result = Analyze(PropagationSources.SymbolReferenceSource, "symbol-reference.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.VariableDeclarator, "value = s.Seed");
        AssertContainsPropagated(result, SyntaxKind.IdentifierName, "value");
    }

    [Fact]
    public void Analyze_LogicalAndOperand_PropagatesToLogicalAndHost()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool survivor)
                {
                    if (!removable && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-and.cs",
          SyntaxKind.IdentifierName,
          "removable");

        var propagated = Assert.Single(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001");
        Assert.Equal(SyntaxKind.LogicalAndExpression, propagated.Mark.SyntaxNode.Kind());
        Assert.Equal("!removable && survivor", propagated.Mark.SyntaxNode.ToString());
        Assert.Equal(RuleFactPorts.FlowLogicalExpression, propagated.Mark.SemanticTag);
        Assert.Equal("removable", propagated.SourceMark.SyntaxNode.ToString());
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.SyntaxNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_LogicalOrOperand_PropagatesToLogicalOrHost()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool survivor)
                {
                    if (removable || survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "logical-or.cs",
          SyntaxKind.IdentifierName,
          "removable");

        var propagated = Assert.Single(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001");
        Assert.Equal(SyntaxKind.LogicalOrExpression, propagated.Mark.SyntaxNode.Kind());
        Assert.Equal("removable || survivor", propagated.Mark.SyntaxNode.ToString());
    }

    [Fact]
    public void Analyze_MultipleLogicalOperands_DeduplicatesHostAndRetainsSource()
    {
        const string source = """
            class Sample
            {
                void Check(bool first, bool second, bool survivor)
                {
                    if (first && second && survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeeds(
          source,
          "logical-and-deduplication.cs",
          (SyntaxKind.IdentifierName, "first"),
          (SyntaxKind.IdentifierName, "second"));

        var propagated = Assert.Single(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001");
        Assert.Equal("first && second && survivor", propagated.Mark.SyntaxNode.ToString());
        Assert.Equal("first", propagated.SourceMark.SyntaxNode.ToString());
    }

    [Fact]
    public void Analyze_MixedLogicalChain_DoesNotPropagateAcrossOperatorKinds()
    {
        const string source = """
            class Sample
            {
                void Check(bool removable, bool second, bool survivor)
                {
                    if (removable && second || survivor)
                    {
                    }
                }
            }
            """;

        var result = AnalyzeWithSeed(
          source,
          "mixed-logical-chain.cs",
          SyntaxKind.IdentifierName,
          "removable");

        var propagated = Assert.Single(result.PropagatedMarks, mark =>
          mark.RuleId == "DEL-SOBJ-PROP-LOGICAL-OPERAND-001");
        Assert.Equal(SyntaxKind.LogicalAndExpression, propagated.Mark.SyntaxNode.Kind());
        Assert.Equal("removable && second", propagated.Mark.SyntaxNode.ToString());
    }

    private static PrototypeAnalysisResult Analyze(string source, string filePath, string targetName)
    {
        var application = new  ApplicationService(RuleRegistry.CreateDefaultRules());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["target-name"] = targetName
          });
    }

    private static PrototypeAnalysisResult AnalyzeWithSeed(string source, string filePath, SyntaxKind seedKind, string seedText)
    {
        return AnalyzeWithSeeds(source, filePath, (seedKind, seedText));
    }

    private static PrototypeAnalysisResult AnalyzeWithSeeds(string source, string filePath, params (SyntaxKind Kind, string Text)[] seeds)
    {
        var application = new  ApplicationService(
          new RuleDefinitionMark[] { new ExactSyntaxSeedRule(seeds) },
          RuleRegistry.CreateDefaultRules().Propagators
            .OfType<ExpressionFlowPropagationRuleBase>()
            .ToList(),
          new RuleDefinitionLift[]
          {
            new ExpressionHostLiftingRule(),
            new IfStructureLiftingRule(),
            new SwitchStructureLiftingRule()
          },
          Array.Empty<RuleDefinitionPropose>());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertContainsPropagated(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsPropagatedText(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          node.ToString().Contains(expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffective(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffectiveText(PrototypeAnalysisResult result, SyntaxKind kind, string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          node.ToString().Contains(expectedText, StringComparison.Ordinal));
    }

    private static IEnumerable<SyntaxNode> EnumerateEffectiveNodes(PrototypeAnalysisResult result)
    {
        return result.SeedMarks
          .Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode));
    }

    private sealed class ExactSyntaxSeedRule : RuleDefinitionMark
    {
        private static readonly RuleSemanticTag AtomicTargetSemanticTag = RuleFactPorts.TargetExpression;
        private readonly IReadOnlyList<(SyntaxKind Kind, string Text)> _seeds;

        public ExactSyntaxSeedRule(IReadOnlyList<(SyntaxKind Kind, string Text)> seeds)
        {
            _seeds = seeds;
        }

    public override string RuleId { get; } = "DEL-SOBJ-MARK-MEMBER-001";


        public override string Name { get; } = "Exact syntax seed for propagation tests";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
          _seeds.Select(seed => seed.Kind).Distinct().ToList();

        public override RuleProducesContract Produces { get; } = new(new[]
        {
          new RuleProducedSyntax(ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds, AtomicTargetSemanticTag)
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            foreach (var seed in _seeds)
            {
                foreach (var node in root.DescendantNodes()
                           .Where(node =>
                             node.RawKind == (int)seed.Kind &&
                             string.Equals(node.ToString(), seed.Text, StringComparison.Ordinal)))
                {
                    yield return new MarkRecord(
                      RuleId,
                      node,
                      null,
                      null,
                      $"Test seed '{seed.Text}'.",
                      SemanticTag: AtomicTargetSemanticTag,
                      Origins: RuleEvidenceOrigin.AtomicExpression);
                }
            }
        }
    }
}
