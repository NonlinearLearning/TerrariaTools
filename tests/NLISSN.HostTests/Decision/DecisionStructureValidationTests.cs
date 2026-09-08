using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis;
using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Rules;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionStructureValidationTests
{
    [Fact]
    public void RuleDecisionEngine_UsesProposalModelDirectly()
    {
        var source = MinimalSources.EmptyMainWithDeadMethodSource;

        var (context, root, rules) = CreateContextAndRules(source);
        var markRule = rules.Markers.OfType<UnreachableMethodMarkRule>().Single();
        var proposalRule = rules.Proposers.OfType<UnreachableMethodProposalRule>().Single();
        var seedMarks = markRule.Mark(context.CreateMarkContext(), root).ToList();
        var proposals = proposalRule.Propose(
          context.CreateProposeContext(),
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>()).ToList();
        var engine = new RuleDecisionEngine();
        var engineDecisions = engine.Decide(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          rules.Proposers).ToList();

        Assert.NotEmpty(seedMarks);
        Assert.All(proposals, proposal => Assert.Equal(DecisionActionKind.Delete, proposal.Action));
        Assert.Equal(seedMarks.Count, engineDecisions.Count);
    }

    [Fact]
    public void RuleDecisionEngine_CollapsesSeedAndStructuralHostInsideSameConflictDomain()
    {
        var source = AtomicControlFlowSources.IfHostConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var seedMarks = RunAtomicMarks(context, root, rules);
        var propagatedMarks = RunAtomicPropagations(context, seedMarks, rules);
        var liftedMarks = Lift(context, seedMarks, propagatedMarks, rules);
        var engine = new RuleDecisionEngine();

        var engineDecisions = engine.Decide(context, seedMarks, propagatedMarks, liftedMarks, rules.Proposers).ToList();

        Assert.Single(engineDecisions);
        Assert.Equal(DecisionActionKind.Delete, engineDecisions[0].Action);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)engineDecisions[0].FinalNode.RawKind);
    }

    [Fact]
    public void RuleDecisionEngine_LogicalAndRightTarget_DeletesIfAfterLeftPropagation()
    {
        var source = AtomicLogicalSources.LogicalAndConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var seedMarks = RunAtomicMarks(context, root, rules);
        var propagatedMarks = RunAtomicPropagations(context, seedMarks, rules);
        var liftedMarks = Lift(context, seedMarks, propagatedMarks, rules);
        var engine = new RuleDecisionEngine();

        var engineDecisions = engine.Decide(context, seedMarks, propagatedMarks, liftedMarks, rules.Proposers).ToList();

        Assert.Single(engineDecisions);
        Assert.Equal(DecisionActionKind.Delete, engineDecisions[0].Action);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)engineDecisions[0].FinalNode.RawKind);
    }

    [Fact]
    public void Analyze_ClassDerivedLogicalIf_StopsAtUnaryExpressionBoundary()
    {
        const string source = """
          namespace Demo;

          public static class PlayerInput
          {
            public static bool UsingGamepad { get; set; }
          }

          public sealed class Checker
          {
            public int Check(bool smartCursorIsUsed)
            {
              if (!smartCursorIsUsed && !PlayerInput.UsingGamepad)
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          ["delete-class"] = "PlayerInput"
        };
        var service = new ApplicationService(RuleRegistry.CreateDefaultRules());

        var result = service.Analyze(source, "class-derived-logical-if.cs", options);

        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.IsKind(SyntaxKind.LogicalAndExpression));
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          string.Equals(
            decision.FinalNode.ToString(),
            "PlayerInput.UsingGamepad",
            StringComparison.Ordinal));
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.IfStatement));
    }

    [Fact]
    public void Analyze_ClassDerivedUnaryInitializer_DoesNotEscapeItsTerminalBoundary()
    {
        const string source = """
          namespace Demo;

          public static class PlayerInput
          {
            public static bool UsingGamepad { get; set; }
          }

          public sealed class Checker
          {
            public int Check(bool smartCursorIsUsed)
            {
              bool flag4 = !smartCursorIsUsed && !PlayerInput.UsingGamepad;
              if (!flag4)
              {
                return 1;
              }

              return 0;
            }
          }
          """;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
          ["delete-class"] = "PlayerInput"
        };
        var service = new ApplicationService(RuleRegistry.CreateDefaultRules());

        var result = service.Analyze(source, "class-derived-logical-initializer.cs", options);

        Assert.Contains(result.PropagatedMarks, mark =>
          mark.Mark.FactKind == RuleFactKind.FlowUnaryExpression &&
          string.Equals(mark.Mark.SyntaxNode.ToString(), "!PlayerInput.UsingGamepad", StringComparison.Ordinal));
        Assert.DoesNotContain(result.PropagatedMarks, mark =>
          mark.Mark.FactKind == RuleFactKind.FlowLocalDefinition ||
          mark.Mark.FactKind == RuleFactKind.FlowSymbolReference);
        Assert.DoesNotContain(result.LiftedMarks, mark =>
          mark.Mark.FactKind == RuleFactKind.LiftIfStructure);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          (decision.FinalNode.IsKind(SyntaxKind.LocalDeclarationStatement) ||
           decision.FinalNode.IsKind(SyntaxKind.IfStatement)));
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.IsKind(SyntaxKind.LogicalAndExpression));
        TextDiffAssert.Contains("bool flag4", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("if (!flag4)", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void LogicalExpressionProposalRule_WhenLogicalAndIsFullyCovered_DoesNotProposeReplacement()
    {
        var source = AtomicLogicalSources.LogicalAndConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var proposalRules = rules.Proposers
          .OfType<LogicalExpressionProposalRule>()
          .ToList();
        var seedMarks = RunAtomicMarks(context, root, rules);
        var propagatedMarks = RunAtomicPropagations(context, seedMarks, rules);
        var liftedMarks = Lift(context, seedMarks, propagatedMarks, rules);
        var proposals = proposalRules
          .SelectMany(rule => rule.Propose(context.CreateProposeContext(), seedMarks, propagatedMarks, liftedMarks))
          .ToList();
        Assert.Empty(proposals);
    }

    [Fact]
    public void DecisionCpgFactory_CreateFragment_UsesStructuredDecisionActionDispatchKind()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }", path: "decision-fragment.cs");
        var methodNode = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var fragment = DecisionCpgFactory.CreateFragment(
          "fragment:decision-action",
          methodNode,
          "anchor",
          DecisionActionKind.Delete);

        var dispatchKind = Assert.IsType<NLCPGDispatchKind>(fragment.DispatchKind);
        Assert.Equal(NLCPGDispatchCategory.DecisionAction, dispatchKind.Category);
        Assert.Equal(NLCPGDispatchFlags.None, dispatchKind.Flags);
        Assert.Equal(NLCPGDecisionActionKind.Delete, dispatchKind.Action);
        Assert.Equal("Delete", dispatchKind.ToString());
    }

    private static (AnalysisSession Context, SyntaxNode Root,  RulePipeline Rules) CreateContextAndRules(string source, string? targetName = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "test.cs");
        var root = tree.GetRoot();
        var compilation = CSharpCompilation.Create(
          "DecisionTests",
          new[] { tree },
          new[]
          {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
          });
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(source, "test.cs");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(options));
        var rules = RuleRegistry.CreateDefaultRules(enableUnreachableMethodDeletion: true);
        return (context, root, rules);
    }

    private static DecisionUnit ResolveMergedUnit(DefaultDecisionPolicy policy, params DecisionUnit[] units)
    {
        var method = typeof(DefaultDecisionPolicy).GetMethod(
          "ResolveToUnitForTesting",
          BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var merged = method!.Invoke(policy, new object[] { units });
        return Assert.IsType<DecisionUnit>(merged);
    }

    private static IReadOnlyList<LiftedMarkRecord> Lift(AnalysisSession context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks,  RulePipeline rules)
    {
        return new MarkLiftingEngine().Run(context, seedMarks, propagatedMarks, rules.Lifters);
    }

    private static List<MarkRecord> RunAtomicMarks(AnalysisSession context, SyntaxNode root,  RulePipeline rules)
    {
        return new MarkingEngine()
          .Run(context, root, rules.Markers)
          .Where(mark =>
            mark.FactKind == RuleFactKind.TargetExpression &&
            mark.Origins == RuleEvidenceOrigin.AtomicExpression)
          .ToList();
    }

    private static List<PropagatedMarkRecord> RunAtomicPropagations(AnalysisSession context, IReadOnlyList<MarkRecord> seedMarks,  RulePipeline rules)
    {
        return new PropagationEngine()
          .Run(
            context,
            seedMarks,
            rules.Propagators
              .OfType<ExpressionFlowPropagationRuleBase>()
              .ToList())
          .ToList();
    }
}
