using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.HostTests.Application;

public sealed class RuleSelectionAdapterTests
{
    [Fact]
    public void FromLegacySettings_MapsTypeNamesAndRuleIdsToCanonicalRuleIds()
    {
        var selection = RuleSelectionAdapter.FromLegacySettings(
          new[]
          {
            "AtomicIdentifierNameMarkRule",
            "NLISSN.Rules.AtomicMemberAccessMarkRule",
            "propose.type.parameter",
            "unknown.rule"
          },
          deleteUnreachableMethods: true,
          deleteUnreferencedMethods: false,
          clearUnusedInterfaceImplementations: false,
          privatizeInternalOnlyPublicMethods: false);

        Assert.Equal(
          new[]
          {
            "mark.target.identifier-name",
            "mark.target.member-access",
            "propose.type.parameter",
            "unknown.rule"
          },
          selection.DisabledRuleIds);
        Assert.Contains(RuleFeature.UnreachableMethodDeletion, selection.RequestedFeatures);
    }

    [Fact]
    public void FromLegacySettings_PreservesUnknownValuesForComposerWarnings()
    {
        var selection = RuleSelectionAdapter.FromLegacySettings(
          new[] { "not-a-rule", "NOT-A-RULE" },
          deleteUnreachableMethods: false,
          deleteUnreferencedMethods: false,
          clearUnusedInterfaceImplementations: false,
          privatizeInternalOnlyPublicMethods: false);

        var result = RulePipelineComposer.Compose(selection);

        Assert.Single(result.Warnings);
        Assert.Equal("not-a-rule", result.Warnings[0].RuleId);
    }
}
