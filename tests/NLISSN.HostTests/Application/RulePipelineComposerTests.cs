using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.HostTests.Application;

public sealed class RulePipelineComposerTests
{
    [Fact]
    public void Compose_AutomaticallyIncludesCoreAndPreservesCompleteFeatureStages()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(
          new[] { RuleFeature.UnreachableMethodDeletion }));

        Assert.Empty(result.Warnings);
        Assert.Equal(20, result.Pipeline.Markers.Count);
        Assert.Equal(13, result.Pipeline.Propagators.Count);
        Assert.Equal(6, result.Pipeline.Lifters.Count);
        Assert.Equal(33, result.Pipeline.Proposers.Count);
        Assert.Contains(result.Pipeline.Markers, rule => rule.RuleId == "mark.unreachable-method");
        Assert.Contains(result.Pipeline.Proposers, rule => rule.RuleId == "propose.unreachable-method");
        Assert.Contains(result.Pipeline.Propagators, rule => rule.RuleId == "propagate.unreachable-method");
        Assert.Contains(result.Pipeline.Lifters, rule => rule.RuleId == "lift.unreachable-method");
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
        var node = Assert.Single(graph.Nodes, candidate =>
          candidate.NodeId == RuleNodeId.For(RuleKind.Mark, "mark.target.identifier-name"));
        Assert.Equal(RuleKind.Mark, node.Kind);
    }

    [Fact]
    public void Compose_UnknownDisabledIdsAreCaseInsensitiveDeduplicatedAndStable()
    {
        var result = RulePipelineComposer.Compose(new RuleSelection(
          disabledRuleIds: new[] { "missing.z", "MISSING.Z", "missing.a", "mark.unreachable-method" }));

        Assert.Equal(new[] { "missing.a", "missing.z" }, result.Warnings.Select(warning => warning.RuleId));
        Assert.DoesNotContain(result.Warnings, warning =>
          warning.RuleId.Equals("mark.unreachable-method", StringComparison.OrdinalIgnoreCase));
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
    public void Compose_MethodDeletionFeaturesContainAllFourStages(
      RuleFeature feature,
      string markRuleId,
      string propagateRuleId,
      string liftRuleId,
      string proposeRuleId)
    {
        var pipeline = RulePipelineComposer.Compose(new RuleSelection(new[] { feature })).Pipeline;
        var graph = pipeline.CompileRuleGraph();

        var markNodeId = RuleNodeId.For(RuleKind.Mark, markRuleId);
        var propagateNodeId = RuleNodeId.For(RuleKind.Propagate, propagateRuleId);
        var liftNodeId = RuleNodeId.For(RuleKind.Lift, liftRuleId);
        var proposeNodeId = RuleNodeId.For(RuleKind.Propose, proposeRuleId);

        Assert.Contains(pipeline.Markers, rule => rule.RuleId == markRuleId);
        Assert.Contains(pipeline.Propagators, rule => rule.RuleId == propagateRuleId);
        Assert.Contains(pipeline.Lifters, rule => rule.RuleId == liftRuleId);
        Assert.Contains(pipeline.Proposers, rule => rule.RuleId == proposeRuleId);
        Assert.Contains(graph.Nodes, node => node.NodeId == markNodeId);
        var propagateNode = Assert.Single(graph.Nodes, node => node.NodeId == propagateNodeId);
        var liftNode = Assert.Single(graph.Nodes, node => node.NodeId == liftNodeId);
        var proposeNode = Assert.Single(graph.Nodes, node => node.NodeId == proposeNodeId);
        Assert.Contains(propagateNode.Dependencies, dependency => dependency.Producer == markNodeId);
        Assert.Contains(liftNode.Dependencies, dependency => dependency.Producer == propagateNodeId);
        Assert.Contains(proposeNode.Dependencies, dependency => dependency.Producer == liftNodeId);
    }
}
