using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Application;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class LogicalConditionMarkAnalyzerTests
{
    private const string DeleteSObjectGroupKey = DeleteSObjectRuleIds.GroupKey;

    public static IEnumerable<object[]> LargeParenthesizedLogicalCases()
    {
        yield return CreateCase(
            nameof(TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase1Source),
            TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase1Source,
            "(a && b) || (c && d) || !b || e || (f && g) || h || i || j || k || l");
        yield return CreateCase(
            nameof(TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase2Source),
            TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase2Source,
            "((a || b) && (c || !b)) || d || e || (f && g) || h || i || j || k || l || m");
        yield return CreateCase(
            nameof(TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase3Source),
            TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase3Source,
            "(a && b) || c || d || (!b && e) || f || g || h || (i && j) || k || l");
        yield return CreateCase(
            nameof(TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase4Source),
            TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase4Source,
            "((a && b) || c) || d || e || ((f || !b) && g) || h || i || j || k || l");
        yield return CreateCase(
            nameof(TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase5Source),
            TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceLargeCase5Source,
            "(a && (b || c)) || d || e || !b || (f && g) || h || i || j || k || l");
    }

    [Fact]
    public void Analyze_CollectsDirectAndNegatedHitsForSameVariable()
    {
        var source = TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceSource;
        var (context, root) = CreateAnalysisContext(source, "logical-mixed-precedence.cs");
        var directIdentifier = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Single(node =>
                node.Identifier.ValueText == "b" &&
                node.Parent is BinaryExpressionSyntax);

        var analysis = new LogicalConditionMarkAnalyzer().Analyze(
            directIdentifier,
            "b",
            context);

        Assert.Equal("b", analysis.TargetSymbol.Name);
        Assert.Equal(2, analysis.Hits.Count);
        Assert.Contains(analysis.Hits, hit =>
            hit.HitKind == LogicalConditionHitKind.Direct &&
            hit.Node.ToString() == "b");
        Assert.Contains(analysis.Hits, hit =>
            hit.HitKind == LogicalConditionHitKind.UnaryWrapped &&
            hit.Node.ToString() == "!b");
        Assert.Equal(SyntaxKind.LogicalOrExpression, (SyntaxKind)analysis.PreferredMarkedNode.RawKind);
    }

    [Fact]
    public void Mark_EmitsAtomicSeedsAndPropagateLiftsLogicalOrForDirectAndNegatedSameVariableHits()
    {
        var source = TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceSource;
        var (context, root) = CreateRuleContext(source, "logical-mixed-precedence.cs", "b");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        Assert.Equal(2, marks.Count);
        Assert.All(marks, mark => Assert.Equal(SyntaxKind.IdentifierName, (SyntaxKind)mark.SyntaxNode.RawKind));
        AssertContainsLogicalOr(effectiveMarks, "a && b || c || !b");
    }

    [Fact]
    public void Mark_WithParenthesizedLogicalOperand_StillPropagatesLogicalOr()
    {
        var source = TestCodeSet.SObject.SObjectLogicalSources.LogicalMixedPrecedenceWithParenthesesSource;
        var (context, root) = CreateRuleContext(
            source,
            "logical-mixed-precedence-parenthesized.cs",
            "b");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        Assert.Equal(2, marks.Count);
        Assert.All(marks, mark => Assert.Equal(SyntaxKind.IdentifierName, (SyntaxKind)mark.SyntaxNode.RawKind));
        AssertContainsLogicalOr(effectiveMarks, "(a && b) || c || !b");
    }

    [Fact]
    public void Analyze_MultiTargetGroupWithFiveHits_CollectsAllTargetVariables()
    {
        var source = TestCodeSet.SObject.SObjectLogicalSources.LogicalMultiTargetGroupFiveHitsSource;
        var (context, root) = CreateAnalysisContext(source, "logical-multi-target-group.cs");
        var directIdentifier = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(node => node.Identifier.ValueText == "b");

        var analysis = new LogicalConditionMarkAnalyzer().Analyze(
            directIdentifier,
            "b,c,d,e,f",
            context);

        Assert.Equal(5, analysis.Hits.Count);
        Assert.Equal(
            new[] { "b", "c", "d", "e", "f" },
            analysis.Hits.Select(hit => hit.TargetSymbol.Name).Distinct().OrderBy(name => name).ToArray());
        Assert.All(analysis.Hits, hit => Assert.Equal(LogicalConditionHitKind.Direct, hit.HitKind));
        Assert.Equal(SyntaxKind.LogicalOrExpression, (SyntaxKind)analysis.PreferredMarkedNode.RawKind);
        Assert.Equal("a || b || c || d || e || f || g || h", analysis.PreferredMarkedNode.ToString());
    }

    [Fact]
    public void Mark_MultiTargetGroupWithFiveHits_EmitsAtomicSeedsAndPropagatesLogicalOr()
    {
        var source = TestCodeSet.SObject.SObjectLogicalSources.LogicalMultiTargetGroupFiveHitsSource;
        var (context, root) = CreateRuleContext(
            source,
            "logical-multi-target-group.cs",
            "b,c,d,e,f");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        Assert.Equal(5, marks.Count);
        Assert.All(marks, mark => Assert.Equal(SyntaxKind.IdentifierName, (SyntaxKind)mark.SyntaxNode.RawKind));
        AssertContainsLogicalOr(effectiveMarks, "a || b || c || d || e || f || g || h");
    }

    [Fact]
    public void Mark_MemberAccessOperand_EmitsMemberAccessSeedAndPropagatesLogicalHost()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box a, bool ready)
            {
              if (ready && a.c)
              {
                return 1;
              }

              return 0;
            }
          }

          public sealed class Box
          {
            public bool c { get; set; }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-member-access.cs", "c");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("a.c", seedMark.SyntaxNode.ToString());
        AssertContainsLogicalAnd(effectiveMarks, "ready && a.c");
    }

    [Fact]
    public void Mark_ThisMemberAccessOperand_EmitsMemberAccessSeedAndPropagatesLogicalHost()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public bool Ready { get; set; }

            public int Compute(bool ready)
            {
              if (ready && this.Ready)
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-this-member-access.cs", "this");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("this.Ready", seedMark.SyntaxNode.ToString());
        AssertContainsLogicalAnd(effectiveMarks, "ready && this.Ready");
    }

    [Fact]
    public void Mark_BaseMemberAccessOperand_EmitsMemberAccessSeedAndPropagatesLogicalHost()
    {
        const string source = """
          namespace Demo;

          public class BaseSample
          {
            protected bool Ready { get; set; }
          }

          public sealed class Sample : BaseSample
          {
            public int Compute(bool ready)
            {
              if (ready && base.Ready)
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-base-member-access.cs", "base");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("base.Ready", seedMark.SyntaxNode.ToString());
        AssertContainsLogicalAnd(effectiveMarks, "ready && base.Ready");
    }

    [Fact]
    public void Mark_InvocationOperand_EmitsInvocationSeedAndPropagatesLogicalHost()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(bool ready)
            {
              if (ready && fun())
              {
                return 1;
              }

              return 0;
            }

            private bool fun()
            {
              return true;
            }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-invocation.cs", "fun");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.InvocationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("fun()", seedMark.SyntaxNode.ToString());
        AssertContainsLogicalAnd(effectiveMarks, "ready && fun()");
    }

    [Fact]
    public void Mark_NestedLogicalOperand_EmitsOnlyLeafAtomicSeeds()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(bool a, bool x)
            {
              if (a && (x || 1 == 1))
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-nested-operands.cs", "x");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.IdentifierName, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("x", seedMark.SyntaxNode.ToString());
        Assert.DoesNotContain(marks, mark => mark.SyntaxNode is BinaryExpressionSyntax);
        AssertContainsLogicalOr(effectiveMarks, "x || 1 == 1");
    }

    [Fact]
    public void Mark_LiteralOperand_EmitsLiteralSeedsWithoutLogicalSymbolResolution()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(bool ready)
            {
              if (ready && 2 == 2)
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-literal.cs", "2");
        var marks = RunDeleteSObjectMarks(context, root);
        var exception = Record.Exception(() => RunDeleteSObjectPropagations(context, marks));

        Assert.Null(exception);
        Assert.Equal(2, marks.Count);
        Assert.All(marks, mark => Assert.Equal(SyntaxKind.NumericLiteralExpression, (SyntaxKind)mark.SyntaxNode.RawKind));
        Assert.All(marks, mark => Assert.Equal("2", mark.SyntaxNode.ToString()));
    }

    [Fact]
    public void Mark_ObjectCreationOperand_EmitsObjectCreationSeed()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute()
            {
              var box = new Box();
              return box.IsReady ? 1 : 0;
            }
          }

          public sealed class Box
          {
            public bool IsReady { get; set; }
          }
          """;
        var (context, root) = CreateRuleContext(source, "logical-object-creation.cs", "Box");

        var marks = RunDeleteSObjectMarks(context, root);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.ObjectCreationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("new Box()", seedMark.SyntaxNode.ToString());
    }

    [Fact]
    public void Mark_ConditionalAccessProperty_EmitsConditionalAccessSeed()
    {
        var source = TestCodeSet.SObject.SObjectExpressionSources.ConditionalAccessPropertySource;
        var (context, root) = CreateRuleContext(source, "conditional-access-property.cs", "s");

        var marks = RunDeleteSObjectMarks(context, root);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.ConditionalAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("s?.Seed", seedMark.SyntaxNode.ToString());
    }

    [Fact]
    public void Mark_ConditionalAccessInvoke_EmitsMemberBindingAndInvocationSeeds()
    {
        var source = TestCodeSet.SObject.SObjectExpressionSources.ConditionalAccessInvokeSource;
        var (context, root) = CreateRuleContext(source, "conditional-access-invoke.cs", "Invoke");

        var marks = RunDeleteSObjectMarks(context, root);

        Assert.Equal(2, marks.Count);
        Assert.Contains(marks, mark =>
            (SyntaxKind)mark.SyntaxNode.RawKind == SyntaxKind.MemberBindingExpression &&
            string.Equals(mark.SyntaxNode.ToString(), ".Invoke", StringComparison.Ordinal));
        Assert.Contains(marks, mark =>
            (SyntaxKind)mark.SyntaxNode.RawKind == SyntaxKind.ConditionalAccessExpression &&
            string.Equals(mark.SyntaxNode.ToString(), "handler?.Invoke()", StringComparison.Ordinal));
    }

    [Fact]
    public void Mark_ConditionalAccessChain_EmitsOutermostConditionalAccessSeed()
    {
        var source = TestCodeSet.SObject.SObjectExpressionSources.ConditionalAccessChainSource;
        var (context, root) = CreateRuleContext(source, "conditional-access-chain.cs", "s");

        var marks = RunDeleteSObjectMarks(context, root);

        var seedMark = Assert.Single(marks);
        Assert.Equal(SyntaxKind.ConditionalAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Equal("s?.Inner?.Seed", seedMark.SyntaxNode.ToString());
    }

    [Theory]
    [MemberData(nameof(LargeParenthesizedLogicalCases))]
    public void Analyze_LargeParenthesizedCases_CollectDirectAndNegatedHits(
        string caseName,
        string source,
        string expectedMarkedText)
    {
        var (context, root) = CreateAnalysisContext(source, "logical-large-parenthesized.cs");
        var directIdentifier = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .First(node =>
                node.Identifier.ValueText == "b" &&
                node.Parent is BinaryExpressionSyntax);

        var analysis = new LogicalConditionMarkAnalyzer().Analyze(
            directIdentifier,
            "b",
            context);

        Assert.Equal("b", analysis.TargetSymbol.Name);
        Assert.True(analysis.Hits.Count >= 2);
        Assert.Contains(analysis.Hits, hit => hit.HitKind == LogicalConditionHitKind.Direct);
        Assert.Contains(analysis.Hits, hit => hit.HitKind == LogicalConditionHitKind.UnaryWrapped);
        Assert.Equal(SyntaxKind.LogicalOrExpression, (SyntaxKind)analysis.PreferredMarkedNode.RawKind);
        Assert.Equal(expectedMarkedText, analysis.PreferredMarkedNode.ToString());
        Assert.False(string.IsNullOrWhiteSpace(caseName));
    }

    [Theory]
    [MemberData(nameof(LargeParenthesizedLogicalCases))]
    public void Mark_LargeParenthesizedCases_EmitAtomicSeedsAndPropagateLogicalOr(
        string caseName,
        string source,
        string expectedMarkedText)
    {
        var (context, root) = CreateRuleContext(
            source,
            "logical-large-parenthesized.cs",
            "b");
        var marks = RunDeleteSObjectMarks(context, root);
        var propagatedMarks = RunDeleteSObjectPropagations(context, marks);
        var effectiveMarks = BuildEffectiveMarks(context, marks, propagatedMarks);

        Assert.True(marks.Count >= 2);
        Assert.All(marks, mark => Assert.Equal(SyntaxKind.IdentifierName, (SyntaxKind)mark.SyntaxNode.RawKind));
        AssertContainsLogicalOr(effectiveMarks, expectedMarkedText);
        Assert.False(string.IsNullOrWhiteSpace(caseName));
    }

    private static IReadOnlyList<MarkRecord> BuildEffectiveMarks(
        RuleContext context,
        IReadOnlyList<MarkRecord> marks,
        IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var liftedMarks = new RuleDefinitionLift[]
            {
                new DeleteSObjectExpressionHostLiftingRule(),
                new DeleteSObjectIfStructureLiftingRule(),
                new DeleteSObjectSwitchStructureLiftingRule()
            }
            .SelectMany(rule => rule.Lift(context, marks, propagatedMarks))
            .Select(mark => mark.Mark);
        return marks
            .Concat(propagatedMarks.Select(mark => mark.Mark))
            .Concat(liftedMarks)
            .ToList();
    }

    private static void AssertContainsLogicalAnd(
        IReadOnlyList<MarkRecord> marks,
        string expectedMarkedText)
    {
        Assert.Contains(marks, mark =>
            mark.SyntaxNode is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
            string.Equals(mark.SyntaxNode.ToString(), expectedMarkedText, StringComparison.Ordinal));
    }

    private static void AssertContainsLogicalOr(
        IReadOnlyList<MarkRecord> marks,
        string expectedMarkedText)
    {
        Assert.Contains(marks, mark =>
            mark.SyntaxNode is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
            string.Equals(mark.SyntaxNode.ToString(), expectedMarkedText, StringComparison.Ordinal));
    }

    private static object[] CreateCase(string caseName, string source, string expectedMarkedText)
    {
        return new object[] { caseName, source, expectedMarkedText };
    }

    private static (CpgAnalysisContext Context, SyntaxNode Root) CreateAnalysisContext(
        string source,
        string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new RoslynCpgBuilder().BuildFromSource(source, filePath);
        return (new CpgAnalysisContext(graph, semanticModel, root), root);
    }

    private static (RuleContext Context, SyntaxNode Root) CreateRuleContext(
        string source,
        string filePath,
        string targetName)
    {
        var (analysisContext, root) = CreateAnalysisContext(source, filePath);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target-name"] = targetName
        };
        return (new RuleContext(analysisContext, options), root);
    }

    private static List<MarkRecord> RunDeleteSObjectMarks(RuleContext context, SyntaxNode root)
    {
        var rules = RuleRegistry.CreateDefaultRules();
        return new MarkingEngine()
            .Run(context, root, rules.Markers)
            .Where(mark => string.Equals(mark.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
            .ToList();
    }

    private static List<PropagatedMarkRecord> RunDeleteSObjectPropagations(
        RuleContext context,
        IReadOnlyList<MarkRecord> marks)
    {
        var rules = RuleRegistry.CreateDefaultRules();
        return new PropagationEngine()
            .Run(
                context,
                marks,
                rules.Propagators
                    .Where(rule => string.Equals(rule.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
                    .ToList())
            .ToList();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
            "LogicalConditionMarkAnalyzerTests",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            });
    }
}
