using System.Reflection;
using System.Text.Json;
using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using Xunit;

namespace RoslynPrototype.ContractTests.Identity;

public sealed class RuleIdentityContractTests
{
    [Fact]
    public void IRuleDefinition_ExposesOnlyRuleIdAsRuleIdentity()
    {
        var propertyNames = typeof(IRuleDefinition)
          .GetProperties()
          .Select(property => property.Name)
          .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(IRuleDefinition.RuleId), propertyNames);
        Assert.DoesNotContain("Capability" + "Id", propertyNames);
    }

    [Fact]
    public void DefaultRuleCatalog_ExposesNonBlankGloballyUniqueRuleIds()
    {
        var rules = GetAllRules();
        var ruleIds = rules.Select(rule => rule.Rule.RuleId).ToArray();

        Assert.NotEmpty(ruleIds);
        Assert.All(ruleIds, ruleId => Assert.False(string.IsNullOrWhiteSpace(ruleId)));
        Assert.Equal(
          ruleIds.Length,
          ruleIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DefaultRuleCatalog_MatchesRuleIdentitySnapshot()
    {
        var snapshot = JsonSerializer.Deserialize<RuleIdentityBaseline[]>(
          global::RoslynPrototype.ContractTests.RuleCatalog.RuleIdentitySnapshotResource.Read(),
          new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(snapshot);

        var expected = snapshot!
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();
        var actual = GetAllRules()
          .Select(rule => $"{rule.Stage}|{rule.Rule.GetType().FullName}|{rule.Rule.RuleId}")
          .OrderBy(value => value, StringComparer.Ordinal)
          .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConcreteRuleDefinitions_DoNotExposeRemovedIdentityProperty()
    {
        foreach (var rule in GetAllRules().Select(entry => entry.Rule))
        {
            Assert.DoesNotContain(
              rule.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
              property => property.Name == "Capability" + "Id");
        }
    }

    private static IReadOnlyList<(RuleKind Stage, IRuleDefinition Rule)> GetAllRules()
    {
        var pipeline = RulePipelineComposer.Compose(new RuleSelection(new[]
        {
          RuleFeature.UnreachableMethodDeletion,
          RuleFeature.UnreferencedMethodDeletion,
          RuleFeature.UnusedInterfaceImplementationCleanup,
          RuleFeature.InternalOnlyPublicMethodPrivatization
        })).Pipeline;

        return pipeline.Markers
          .Cast<IRuleDefinition>()
          .Select(rule => (RuleKind.Mark, (IRuleDefinition)rule))
          .Concat(pipeline.Propagators.Select(rule => (RuleKind.Propagate, (IRuleDefinition)rule)))
          .Concat(pipeline.Lifters.Select(rule => (RuleKind.Lift, (IRuleDefinition)rule)))
          .Concat(pipeline.Proposers.Select(rule => (RuleKind.Propose, (IRuleDefinition)rule)))
          .ToArray();
    }

    private sealed record RuleIdentityBaseline(
      string Stage,
      string Type,
      string SourceCapability,
      string ObservedRuleId,
      string TargetRuleId);
}
