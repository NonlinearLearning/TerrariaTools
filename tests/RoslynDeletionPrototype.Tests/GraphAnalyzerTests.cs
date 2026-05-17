using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class GraphAnalyzerTests
{
  [Fact]
  public void Analyze_TargetNameSample_DeletesSeededNodesAndPropagatesMarks()
  {
    var application = CreateApplication();
    var source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var value = s.Seed + offset;
          if (s.IsReady)
          {
            return value;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }

        public bool IsReady { get; set; }
      }
      """;

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
    var source = """
      namespace Demo;

      public static class Sample
      {
        public static void Main()
        {
          Live();
        }

        public static void Live()
        {
        }

        public static void DeadA()
        {
          DeadB();
        }

        public static void DeadB()
        {
        }
      }
      """;

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
    var source = """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, bool ready, int offset)
        {
          if (ready && s.IsReady)
          {
            return offset;
          }

          return 0;
        }
      }

      public sealed class Box
      {
        public bool IsReady { get; set; }
      }
      """;

    var result = application.Analyze(source, "logical-and-sample.cs", CreateOptions("s"));

    Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalAndExpression));
    Assert.Contains("ready", result.DiffText);
    Assert.Contains("s.IsReady", result.DiffText);
    Assert.DoesNotContain(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
    Assert.Contains("if (ready)", result.RewrittenSource);
    Assert.DoesNotContain("s.IsReady", result.RewrittenSource);
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

  private sealed class InvalidNodeKindRule : IDeletionRule
  {
    public RuleMetadata Metadata { get; } = new(
      "TEST-INVALID-001",
      "Emit unsupported node kind",
      true);

    public IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; } =
      new[] { SyntaxKind.MethodDeclaration };

    public IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
      var node = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
      yield return new MarkRecord(
        Metadata.RuleId,
        node,
        null,
        null,
        "Emit invalid node kind for validation test.");
    }

    public IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
    {
      return Enumerable.Empty<PropagatedMarkRecord>();
    }
  }
}
