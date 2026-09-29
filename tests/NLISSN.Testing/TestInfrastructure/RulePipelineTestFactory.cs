using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Pipeline;

namespace RoslynPrototype.Testing.TestInfrastructure;

public static class RulePipelineTestFactory
{
    public static RulePipeline Create(
      IEnumerable<string>? disabledRuleTypes = null)
    {
        return RulePipelineComposer.Compose(
          RuleSelectionAdapter.FromLegacySettings(
            disabledRuleTypes)).Pipeline;
    }
}
