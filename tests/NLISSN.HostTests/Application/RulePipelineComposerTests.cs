using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class RulePipelineComposerTests
{
    [Fact]
    public void Compose_UsesOnlyGeneratedRules()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection());

        Assert.Empty(result.Warnings);
        Assert.Equal(GeneratedRuleCatalog.Markers.Count, result.Pipeline.Markers.Count);
        Assert.Equal(GeneratedRuleCatalog.Propagators.Count, result.Pipeline.Propagators.Count);
        Assert.Equal(GeneratedRuleCatalog.Lifters.Count, result.Pipeline.Lifters.Count);
        Assert.Equal(GeneratedRuleCatalog.Proposers.Count, result.Pipeline.Proposers.Count);
    }

    [Fact]
    public void Compose_DefaultSelectionMatchesGeneratedCatalog()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection());

        Assert.Empty(result.Warnings);
        Assert.Equal(GeneratedRuleCatalog.Markers.Count, result.Pipeline.Markers.Count);
        Assert.Equal(GeneratedRuleCatalog.Propagators.Count, result.Pipeline.Propagators.Count);
        Assert.Equal(GeneratedRuleCatalog.Lifters.Count, result.Pipeline.Lifters.Count);
        Assert.Equal(GeneratedRuleCatalog.Proposers.Count, result.Pipeline.Proposers.Count);
        Assert.Equal(
          GeneratedRuleCatalog.Markers.Select(descriptor => descriptor.RuleId)
            .OrderBy(ruleId => ruleId, StringComparer.Ordinal),
          result.Pipeline.Markers.Select(rule => rule.RuleId)
            .OrderBy(ruleId => ruleId, StringComparer.Ordinal));
        Assert.Equal(
          GeneratedRuleCatalog.Propagators.Select(descriptor => descriptor.RuleId),
          result.Pipeline.Propagators.Select(rule => rule.RuleId));
        Assert.Equal(
          GeneratedRuleCatalog.Lifters.Select(descriptor => descriptor.RuleId),
          result.Pipeline.Lifters.Select(rule => rule.RuleId));
        Assert.Equal(
          GeneratedRuleCatalog.Proposers.Select(descriptor => descriptor.RuleId)
            .OrderBy(ruleId => ruleId, StringComparer.Ordinal),
          result.Pipeline.Proposers.Select(rule => rule.RuleId)
            .OrderBy(ruleId => ruleId, StringComparer.Ordinal));
    }

    [Fact]
    public void Compose_DisabledRuleRemainsInPipelineAndGraphAsDisabled()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(
          new[] { "mark.target.identifier-name" }));

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain(result.Pipeline.Markers, rule => rule.RuleId == "mark.target.identifier-name");
        Assert.Contains(result.Pipeline.DisabledMarkers!, rule => rule.RuleId == "mark.target.identifier-name");

        var graph = result.Pipeline.CompileRuleGraph();
        var disabledNode = graph.Nodes.Single(node =>
          node.NodeId == RuleNodeId.For(RuleKind.Mark, "mark.target.identifier-name"));
        Assert.Equal(RuleKind.Mark, disabledNode.Kind);
    }

    [Fact]
    public void Compose_UnknownDisabledIdsAreCaseInsensitiveDeduplicatedAndStable()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(
          new[]
          {
            "missing.z",
            "MISSING.Z",
            "missing.a",
            "missing.rule"
          }));

        Assert.Equal(
          new[] { "missing.a", "missing.rule", "missing.z" },
          result.Warnings.Select(warning => warning.RuleId));
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public void Compose_CreatesFreshInstancesForEachSelectedDescriptor()
    {
        var selection = new RuleSelection();
        var first = RulePipelineComposer.Compose(selection).Pipeline;
        var second = RulePipelineComposer.Compose(selection).Pipeline;

        Assert.NotSame(first.Markers[0], second.Markers[0]);
        Assert.NotSame(first.Proposers[0], second.Proposers[0]);
    }

}
