using NLISSN.Composition;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class RuleSelectionAdapterTests
{
    [Fact]
    public void FromLegacySettings_MapsSimpleAndFullyQualifiedTypeNamesToCanonicalRuleIds()
    {
        var selection = RuleSelectionAdapter.FromLegacySettings(
          new[]
          {
            "AtomicIdentifierNameMarkRule",
            "NLISSN.Rules.AtomicMemberAccessMarkRule",
            "propose.type.parameter",
            "unknown.rule"
          });

        Assert.Equal(
          new[]
          {
            "mark.target.identifier-name",
            "mark.target.member-access",
            "propose.type.parameter",
            "unknown.rule"
          },
          selection.DisabledRuleIds);
    }

    [Fact]
    public void FromLegacySettings_PreservesUnknownValuesForComposerWarnings()
    {
        var selection = RuleSelectionAdapter.FromLegacySettings(
          new[] { "not-a-rule", "NOT-A-RULE" });

        var result = RulePipelineComposer.Compose(selection);

        Assert.Single(result.Warnings);
        Assert.Equal("not-a-rule", result.Warnings[0].RuleId);
    }
}
