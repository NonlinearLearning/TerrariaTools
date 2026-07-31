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
        var seedMarks = markRule.Mark(context.CreateMarkRuleContext(), root).ToList();
        var proposals = proposalRule.Propose(
          context.CreateProposeRuleContext(),
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
    public void RuleDecisionEngine_PrefersReducibleLogicalHostInsideSameConflictDomain()
    {
        var source = AtomicLogicalSources.LogicalAndConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var seedMarks = RunAtomicMarks(context, root, rules);
        var propagatedMarks = RunAtomicPropagations(context, seedMarks, rules);
        var liftedMarks = Lift(context, seedMarks, propagatedMarks, rules);
        var engine = new RuleDecisionEngine();

        var engineDecisions = engine.Decide(context, seedMarks, propagatedMarks, liftedMarks, rules.Proposers).ToList();

        Assert.Single(engineDecisions);
        Assert.Equal(DecisionActionKind.Replace, engineDecisions[0].Action);
        Assert.Equal(SyntaxKind.LogicalAndExpression, (SyntaxKind)engineDecisions[0].FinalNode.RawKind);
    }

    [Fact]
    public void DefaultDecisionPolicy_WhenLogicalHostIsMarked_ResolvesReplaceDecision()
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
          .SelectMany(rule => rule.Propose(
            context.CreateProposeRuleContext(),
            seedMarks,
            propagatedMarks,
            liftedMarks))
          .ToList();
        var policy = new DefaultDecisionPolicy();

        var resolved = policy.Resolve(context, proposals);

        Assert.Equal(DecisionActionKind.Replace, resolved.Action);
        Assert.Equal(SyntaxKind.LogicalAndExpression, (SyntaxKind)resolved.FinalNode.RawKind);

        var logicalProposal = proposals.Single(unit =>
            unit.SyntaxBindings.TryGetValue(unit.Fragments[0].NodeId!.Value, out var node) &&
            node.IsKind(SyntaxKind.LogicalAndExpression));

        var merged = ResolveMergedUnit(policy, context, logicalProposal);

        Assert.Contains(merged.Fragments, fragment =>
            fragment.NodeId.HasValue &&
            merged.SyntaxBindings.TryGetValue(fragment.NodeId.Value, out var node) &&
                node.IsKind(SyntaxKind.LogicalAndExpression));
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

    private static (NLISSN.Core.Pipeline.RuleContext Context, SyntaxNode Root,  RulePipeline Rules) CreateContextAndRules(string source, string? targetName = null)
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

        var context = new NLISSN.Core.Pipeline.RuleContext(new CpgAnalysisContext(graph, semanticModel, root), options);
        var rules = RuleRegistry.CreateDefaultRules();
        return (context, root, rules);
    }

    private static DecisionUnit ResolveMergedUnit(DefaultDecisionPolicy policy, NLISSN.Core.Pipeline.RuleContext context, params DecisionUnit[] units)
    {
        var method = typeof(DefaultDecisionPolicy).GetMethod(
          "ResolveToUnitForTesting",
          BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var merged = method!.Invoke(policy, new object[] { context, units });
        return Assert.IsType<DecisionUnit>(merged);
    }

    private static IReadOnlyList<LiftedMarkRecord> Lift(NLISSN.Core.Pipeline.RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks,  RulePipeline rules)
    {
        return new MarkLiftingEngine().Run(context, seedMarks, propagatedMarks, rules.Lifters);
    }

    private static List<MarkRecord> RunAtomicMarks(NLISSN.Core.Pipeline.RuleContext context, SyntaxNode root,  RulePipeline rules)
    {
        return new MarkingEngine()
          .Run(context, root, rules.Markers)
          .Where(mark =>
            mark.SemanticTag == RuleFactPorts.TargetExpression &&
            mark.Origins == RuleEvidenceOrigin.AtomicExpression)
          .ToList();
    }

    private static List<PropagatedMarkRecord> RunAtomicPropagations(NLISSN.Core.Pipeline.RuleContext context, IReadOnlyList<MarkRecord> seedMarks,  RulePipeline rules)
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
