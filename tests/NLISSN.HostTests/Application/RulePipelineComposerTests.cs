using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class RulePipelineComposerTests
{
    [Fact]
    public void Compose_DefaultSelectionIncludesOnlyCoreRules()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection());

        Assert.Empty(result.Warnings);
        Assert.Equal((19, 13, 5, 32), (
          result.Pipeline.Markers.Count,
          result.Pipeline.Propagators.Count,
          result.Pipeline.Lifters.Count,
          result.Pipeline.Proposers.Count));
        Assert.DoesNotContain(
          result.Pipeline.Markers,
          rule => rule.RuleId is "mark.unreachable-method" or
            "mark.unreferenced-method" or
            "mark.clear-unused-interface-implementation" or
            "mark.privatize-internal-only-public-method");
        Assert.DoesNotContain(
          result.Pipeline.Propagators,
          rule => rule.RuleId is "propagate.unreachable-method" or
            "propagate.unreferenced-method" or
            "propagate.clear-unused-interface-implementation" or
            "propagate.privatize-internal-only-public-method");
        Assert.DoesNotContain(
          result.Pipeline.Lifters,
          rule => rule.RuleId is "lift.unreachable-method" or
            "lift.unreferenced-method" or
            "lift.clear-unused-interface-implementation" or
            "lift.privatize-internal-only-public-method");
        Assert.DoesNotContain(
          result.Pipeline.Proposers,
          rule => rule.RuleId is "propose.unreachable-method" or
            "propose.unreferenced-method" or
            "propose.clear-unused-interface-implementation" or
            "propose.privatize-internal-only-public-method");
    }

    [Theory]
    [InlineData(
      RuleFeature.UnreachableMethodDeletion,
      "mark.unreachable-method",
      "propagate.unreachable-method",
      "lift.unreachable-method",
      "propose.unreachable-method")]
    [InlineData(
      RuleFeature.UnreferencedMethodDeletion,
      "mark.unreferenced-method",
      "propagate.unreferenced-method",
      "lift.unreferenced-method",
      "propose.unreferenced-method")]
    [InlineData(
      RuleFeature.UnusedInterfaceImplementationCleanup,
      "mark.clear-unused-interface-implementation",
      "propagate.clear-unused-interface-implementation",
      "lift.clear-unused-interface-implementation",
      "propose.clear-unused-interface-implementation")]
    [InlineData(
      RuleFeature.InternalOnlyPublicMethodPrivatization,
      "mark.privatize-internal-only-public-method",
      "propagate.privatize-internal-only-public-method",
      "lift.privatize-internal-only-public-method",
      "propose.privatize-internal-only-public-method")]
    public void Compose_ExplicitFeatureAddsAllFourStages(
      RuleFeature feature,
      string markRuleId,
      string propagateRuleId,
      string liftRuleId,
      string proposeRuleId)
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(new[] { feature }));

        Assert.Empty(result.Warnings);
        Assert.Contains(result.Pipeline.Markers, rule => rule.RuleId == markRuleId);
        Assert.Contains(result.Pipeline.Propagators, rule => rule.RuleId == propagateRuleId);
        Assert.Contains(result.Pipeline.Lifters, rule => rule.RuleId == liftRuleId);
        Assert.Contains(result.Pipeline.Proposers, rule => rule.RuleId == proposeRuleId);
    }

    [Fact]
    public void Compose_DisabledRuleRemainsInPipelineAndGraphAsDisabled()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(
          disabledRuleIds: new[] { "mark.target.identifier-name" }));

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
          disabledRuleIds: new[] { "missing.z", "MISSING.Z", "missing.a", "missing.rule" }));

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
