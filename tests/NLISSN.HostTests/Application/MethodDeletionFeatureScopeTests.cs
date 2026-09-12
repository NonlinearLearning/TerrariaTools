using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class MethodDeletionFeatureScopeTests
{
    [Fact]
    public void GeneratedCatalogContainsFeatureRulesButDefaultPipelineExcludesThem()
    {
        var descriptors = GeneratedRuleCatalog.Markers
          .Select(descriptor => descriptor.Factory())
          .Cast<IRuleDefinition>()
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => descriptor.Factory()))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => descriptor.Factory()))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => descriptor.Factory()))
          .ToArray();

        Assert.Contains(
          descriptors,
          rule => rule.RuleId == "mark.unreachable-method");
        Assert.Contains(
          descriptors,
          rule => rule.RuleId == "propose.unreferenced-method");
        Assert.All(
          descriptors.Where(rule => rule.RuleId.Contains("unreachable-method", StringComparison.Ordinal) ||
            rule.RuleId.Contains("unreferenced-method", StringComparison.Ordinal)),
          rule => Assert.NotEqual(RuleFeature.Core, GetFeature(rule.RuleId)));

        var pipeline = RulePipelineComposer.Compose(new RuleSelection()).Pipeline;

        Assert.DoesNotContain(pipeline.Markers, rule =>
          rule.RuleId is "mark.unreachable-method" or "mark.unreferenced-method");
        Assert.DoesNotContain(pipeline.Propagators, rule =>
          rule.RuleId is "propagate.unreachable-method" or "propagate.unreferenced-method");
        Assert.DoesNotContain(pipeline.Lifters, rule =>
          rule.RuleId is "lift.unreachable-method" or "lift.unreferenced-method");
        Assert.DoesNotContain(pipeline.Proposers, rule =>
          rule.RuleId is "propose.unreachable-method" or "propose.unreferenced-method");
    }

    [Fact]
    public void ExplicitMethodDeletionFeaturesAddAllFourStages()
    {
        var pipeline = RulePipelineComposer.Compose(new RuleSelection(new[]
        {
          RuleFeature.UnreachableMethodDeletion,
          RuleFeature.UnreferencedMethodDeletion
        })).Pipeline;

        Assert.Contains(pipeline.Markers, rule => rule.RuleId == "mark.unreachable-method");
        Assert.Contains(pipeline.Propagators, rule => rule.RuleId == "propagate.unreachable-method");
        Assert.Contains(pipeline.Lifters, rule => rule.RuleId == "lift.unreachable-method");
        Assert.Contains(pipeline.Proposers, rule => rule.RuleId == "propose.unreachable-method");
        Assert.Contains(pipeline.Markers, rule => rule.RuleId == "mark.unreferenced-method");
        Assert.Contains(pipeline.Propagators, rule => rule.RuleId == "propagate.unreferenced-method");
        Assert.Contains(pipeline.Lifters, rule => rule.RuleId == "lift.unreferenced-method");
        Assert.Contains(pipeline.Proposers, rule => rule.RuleId == "propose.unreferenced-method");
    }

    private static RuleFeature GetFeature(string ruleId)
    {
        return GeneratedRuleCatalog.Markers
          .Select(descriptor => new { descriptor.RuleId, descriptor.Feature })
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new { descriptor.RuleId, descriptor.Feature }))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new { descriptor.RuleId, descriptor.Feature }))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new { descriptor.RuleId, descriptor.Feature }))
          .Single(descriptor => descriptor.RuleId == ruleId)
          .Feature;
    }
}
