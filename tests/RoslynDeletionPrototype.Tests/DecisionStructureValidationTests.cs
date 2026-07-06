using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Contracts;
using RoslynPrototype.Application;
using Rules;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class DecisionStructureValidationTests
{
    private const string DeleteSObjectGroupKey = DeleteSObjectRuleIds.GroupKey;

    [Fact]
    public void RuleDecisionEngine_UsesProposalModelDirectly()
    {
        var source = MinimalSources.EmptyMainWithDeadMethodSource;

        var (context, root, rules) = CreateContextAndRules(source);
        var markRule = rules.Markers.OfType<DeleteUnreachableMethodRule>().Single();
        var proposalRule = rules.Proposers.OfType<DeleteUnreachableMethodProposalRule>().Single();
        var seedMarks = markRule.Mark(context, root).ToList();
        var proposals = proposalRule.Propose(
          context,
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
        var source = SObjectControlFlowSources.IfHostConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var seedMarks = RunDeleteSObjectMarks(context, root, rules);
        var propagatedMarks = RunDeleteSObjectPropagations(context, seedMarks, rules);
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
        var source = SObjectLogicalSources.LogicalAndConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var seedMarks = RunDeleteSObjectMarks(context, root, rules);
        var propagatedMarks = RunDeleteSObjectPropagations(context, seedMarks, rules);
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
        var source = SObjectLogicalSources.LogicalAndConflictSource;

        var (context, root, rules) = CreateContextAndRules(source, "s");
        var proposalRules = rules.Proposers
          .Where(rule => string.Equals(rule.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
          .ToList();
        var seedMarks = RunDeleteSObjectMarks(context, root, rules);
        var propagatedMarks = RunDeleteSObjectPropagations(context, seedMarks, rules);
        var liftedMarks = Lift(context, seedMarks, propagatedMarks, rules);
        var proposals = proposalRules
          .SelectMany(rule => rule.Propose(context, seedMarks, propagatedMarks, liftedMarks))
          .ToList();
        var policy = new DefaultDecisionPolicy();

        var resolved = policy.Resolve(context, proposals);

        Assert.Equal(DecisionActionKind.Replace, resolved.Action);
        Assert.Equal(SyntaxKind.LogicalAndExpression, (SyntaxKind)resolved.FinalNode.RawKind);

        var logicalProposal = proposals.Single(unit =>
            unit.SyntaxBindings.TryGetValue(unit.Fragments[0].Id, out var node) &&
            node.IsKind(SyntaxKind.LogicalAndExpression));

        var merged = ResolveMergedUnit(policy, context, logicalProposal);

        Assert.Contains(merged.Fragments, fragment =>
            merged.SyntaxBindings.TryGetValue(fragment.Id, out var node) &&
                node.IsKind(SyntaxKind.LogicalAndExpression));
    }

    private static (Rules.RuleContext Context, SyntaxNode Root, RuleRegistrySet Rules) CreateContextAndRules(
        string source,
        string? targetName = null)
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
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "test.cs");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        var context = new Rules.RuleContext(new CpgAnalysisContext(graph, semanticModel, root), options);
        var rules = RuleRegistry.CreateDefaultRules();
        return (context, root, rules);
    }

    private static DecisionUnit ResolveMergedUnit(
        DefaultDecisionPolicy policy,
        Rules.RuleContext context,
        params DecisionUnit[] units)
    {
        var method = typeof(DefaultDecisionPolicy).GetMethod(
          "ResolveToUnitForTesting",
          BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var merged = method!.Invoke(policy, new object[] { context, units });
        return Assert.IsType<DecisionUnit>(merged);
    }

    private static IReadOnlyList<LiftedMarkRecord> Lift(
      Rules.RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      RuleRegistrySet rules)
    {
        return new MarkLiftingEngine().Run(context, seedMarks, propagatedMarks, rules.Lifters);
    }

    private static List<MarkRecord> RunDeleteSObjectMarks(
      Rules.RuleContext context,
      SyntaxNode root,
      RuleRegistrySet rules)
    {
        return new MarkingEngine()
          .Run(context, root, rules.Markers)
          .Where(mark => string.Equals(mark.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
          .ToList();
    }

    private static List<PropagatedMarkRecord> RunDeleteSObjectPropagations(
      Rules.RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      RuleRegistrySet rules)
    {
        return new PropagationEngine()
          .Run(
            context,
            seedMarks,
            rules.Propagators
              .Where(rule => string.Equals(rule.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
              .ToList())
          .ToList();
    }
}
