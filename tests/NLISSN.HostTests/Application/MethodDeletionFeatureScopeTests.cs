using NLISSN.Composition;
using NLISSN.Application;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class MethodDeletionFeatureScopeTests
{
    [Fact]
    public void DeferredMethodDeletionFeaturesAreAbsentFromGeneratedCatalog()
    {
        var rules = GeneratedRuleCatalog.Markers.Select(descriptor => descriptor.Factory())
          .Cast<IRuleDefinition>()
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => descriptor.Factory()))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => descriptor.Factory()))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => descriptor.Factory()))
          .ToArray();

        Assert.DoesNotContain(rules, rule =>
          rule.RuleId is "mark.unreachable-method" or "propose.unreachable-method" or
          "mark.unreferenced-method" or "propose.unreferenced-method");
    }

    [Fact]
    public void DeferredRuleFamiliesCannotAddRulesToTheCurrentPipeline()
    {
        var pipeline = RulePipelineComposer.Compose(new RuleSelection()).Pipeline;

        Assert.Equal(GeneratedRuleCatalog.Markers.Count, pipeline.Markers.Count);
        Assert.Equal(GeneratedRuleCatalog.Propagators.Count, pipeline.Propagators.Count);
        Assert.Equal(GeneratedRuleCatalog.Lifters.Count, pipeline.Lifters.Count);
        Assert.Equal(GeneratedRuleCatalog.Proposers.Count, pipeline.Proposers.Count);
        Assert.DoesNotContain(pipeline.Markers, rule =>
          rule.RuleId is "mark.unreachable-method" or "mark.unreferenced-method");
        Assert.DoesNotContain(pipeline.Proposers, rule =>
          rule.RuleId is "propose.unreachable-method" or "propose.unreferenced-method");
        Assert.DoesNotContain(pipeline.Markers, rule =>
          rule.RuleId is "mark.clear-unused-interface-implementation" or
          "mark.privatize-internal-only-public-method");
        Assert.DoesNotContain(pipeline.Propagators, rule =>
          rule.RuleId is "propagate.clear-unused-interface-implementation" or
          "propagate.privatize-internal-only-public-method");
        Assert.DoesNotContain(pipeline.Lifters, rule =>
          rule.RuleId is "lift.clear-unused-interface-implementation" or
          "lift.privatize-internal-only-public-method");
        Assert.DoesNotContain(pipeline.Proposers, rule =>
          rule.RuleId is "propose.clear-unused-interface-implementation" or
          "propose.privatize-internal-only-public-method");
    }
}
