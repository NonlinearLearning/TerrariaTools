using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Marking;
using RoslynPrototype.Application;
using RoslynPrototype.Rewrite;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PropagationRuleExpansionTests
{
    [Fact]
    public void Analyze_ConditionalAccessInvoke_PropagatesToConditionalAccess()
    {
        const string source = """
          namespace Demo;

          using System;

          public sealed class Sample
          {
            public void Raise(Action? handler)
            {
              handler?.Invoke();
            }
          }
          """;

        var result = Analyze(source, "conditional-access-invoke.cs", "Invoke");

        AssertContainsEffective(result, SyntaxKind.ConditionalAccessExpression, "handler?.Invoke()");
    }

    [Fact]
    public void Analyze_ObjectCreationWithInitializer_PropagatesThroughInitializerAndCreation()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public Holder Create(Box s)
            {
              var holder = new Holder
              {
                Value = s.Seed
              };
              return holder;
            }
          }

          public sealed class Holder
          {
            public int Value { get; set; }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "object-creation-initializer.cs", "Seed");

        AssertContainsEffectiveText(result, SyntaxKind.ObjectCreationExpression, "new Holder");
    }

    [Fact]
    public void Analyze_ConditionalExpression_PropagatesToTernaryHost()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s, bool ready)
            {
              return ready ? s.Seed : 0;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "conditional-expression.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ConditionalExpression, "ready ? s.Seed : 0");
    }

    [Fact]
    public void Analyze_TransparentWrappers_PropagateOneWrapperAtATime()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s)
            {
              return checked((int)(s.Seed));
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "transparent-wrappers.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.ParenthesizedExpression, "(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CastExpression, "(int)(s.Seed)");
        AssertContainsPropagated(result, SyntaxKind.CheckedExpression, "checked((int)(s.Seed))");
    }

    [Fact]
    public void Analyze_InterpolatedString_PropagatesThroughInterpolation()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public string Format(Box s)
            {
              return $"value {s.Seed}";
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "interpolated-string.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.Interpolation, "{s.Seed}");
        AssertContainsPropagated(result, SyntaxKind.InterpolatedStringExpression, "$\"value {s.Seed}\"");
    }

    [Fact]
    public void Analyze_YieldThrowAndArrow_PropagateToStatementOrClauseHosts()
    {
        const string source = """
          namespace Demo;

          using System;
          using System.Collections.Generic;

          public sealed class Sample
          {
            public IEnumerable<int> Values(Box s)
            {
              yield return s.Seed;
            }

            public int ThrowIt(Box s)
            {
              throw new InvalidOperationException(s.Text);
            }

            public int Arrow(Box s) => s.Seed;
          }

          public sealed class Box
          {
            public int Seed { get; set; }

            public string Text { get; set; } = "";
          }
          """;

        var result = Analyze(source, "yield-throw-arrow.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.YieldReturnStatement, "yield return s.Seed;");
        AssertContainsPropagatedText(result, SyntaxKind.ThrowStatement, "throw new InvalidOperationException(s.Text);");
        AssertContainsPropagated(result, SyntaxKind.ArrowExpressionClause, "=> s.Seed");
    }

    [Fact]
    public void Analyze_ResourceAndLoopHeaders_PropagateToOwningStatements()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public void Run(Box s, int[] values)
            {
              lock (s.Sync)
              {
              }

              using (s.Open())
              {
              }

              fixed (int* value = &s.Seed)
              {
              }

              foreach (var value in s.Values)
              {
              }
            }
          }

          public sealed class Box
          {
            public object Sync { get; } = new object();

            public int Seed;

            public int[] Values { get; } = new int[0];

            public System.IDisposable Open() => null!;
          }
          """;

        var result = AnalyzeWithSeeds(
          source,
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
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s, int value)
            {
              return value switch
              {
                0 => s.Seed,
                _ => value
              };
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "switch-expression.cs", "s");

        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpressionArm, "0 => s.Seed");
        AssertContainsPropagatedText(result, SyntaxKind.SwitchExpression, "value switch");
    }

    [Fact]
    public void Analyze_ArgumentShell_PropagatesThroughArgumentListAndInvocation()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s)
            {
              return Combine(s.Seed, 1);
            }

            private static int Combine(int value, int other)
            {
              return value + other;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = AnalyzeWithSeed(
          source,
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
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s)
            {
              return s.Inner.Next.Value();
            }
          }

          public sealed class Box
          {
            public InnerBox Inner { get; } = new InnerBox();
          }

          public sealed class InnerBox
          {
            public NextBox Next { get; } = new NextBox();
          }

          public sealed class NextBox
          {
            public int Value()
            {
              return 1;
            }
          }
          """;

        var result = Analyze(source, "chained-member-access.cs", "s");

        AssertContainsEffective(result, SyntaxKind.InvocationExpression, "s.Inner.Next.Value()");
        AssertContainsPropagatedText(result, SyntaxKind.ReturnStatement, "return s.Inner.Next.Value();");
    }

    [Fact]
    public void Analyze_SymbolReference_PropagatesFromMarkedDefinitionToLaterReferences()
    {
        const string source = """
          namespace Demo;

          public sealed class Sample
          {
            public int Compute(Box s)
            {
              var value = s.Seed;
              return value + 1;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """;

        var result = Analyze(source, "symbol-reference.cs", "s");

        AssertContainsPropagated(result, SyntaxKind.VariableDeclarator, "value = s.Seed");
        AssertContainsPropagated(result, SyntaxKind.IdentifierName, "value");
    }

    private static PrototypeAnalysisResult Analyze(
      string source,
      string filePath,
      string targetName)
    {
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["target-name"] = targetName
          });
    }

    private static PrototypeAnalysisResult AnalyzeWithSeed(
      string source,
      string filePath,
      SyntaxKind seedKind,
      string seedText)
    {
        return AnalyzeWithSeeds(source, filePath, (seedKind, seedText));
    }

    private static PrototypeAnalysisResult AnalyzeWithSeeds(
      string source,
      string filePath,
      params (SyntaxKind Kind, string Text)[] seeds)
    {
        var application = new DeletionApplicationService(
          new RuleDefinitionMark[] { new ExactSyntaxSeedRule(seeds) },
          RuleRegistry.CreateDefaultRules().Propagators
            .Where(rule => string.Equals(rule.GroupKey, DeleteSObjectRuleIds.GroupKey, StringComparison.Ordinal))
            .ToList(),
          new RuleDefinitionLift[]
          {
            new DeleteSObjectExpressionHostLiftingRule(),
            new DeleteSObjectIfStructureLiftingRule(),
            new DeleteSObjectSwitchStructureLiftingRule()
          },
          Array.Empty<RuleDefinitionPropose>());
        return application.Analyze(
          source,
          filePath,
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static void AssertContainsPropagated(
      PrototypeAnalysisResult result,
      SyntaxKind kind,
      string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsPropagatedText(
      PrototypeAnalysisResult result,
      SyntaxKind kind,
      string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          node.ToString().Contains(expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffective(
      PrototypeAnalysisResult result,
      SyntaxKind kind,
      string expectedText)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
          node.RawKind == (int)kind &&
          string.Equals(node.ToString(), expectedText, StringComparison.Ordinal));
    }

    private static void AssertContainsEffectiveText(
      PrototypeAnalysisResult result,
      SyntaxKind kind,
      string expectedText)
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
        private readonly IReadOnlyList<(SyntaxKind Kind, string Text)> _seeds;

        public ExactSyntaxSeedRule(IReadOnlyList<(SyntaxKind Kind, string Text)> seeds)
        {
            _seeds = seeds;
        }

        public override string RuleId { get; } = "DEL-SOBJ-TEST-MARK-001";

        public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

        public override string Name { get; } = "Exact syntax seed for propagation tests";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
          _seeds.Select(seed => seed.Kind).Distinct().ToList();

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
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
                      $"Test seed '{seed.Text}'.");
                }
            }
        }
    }
}
