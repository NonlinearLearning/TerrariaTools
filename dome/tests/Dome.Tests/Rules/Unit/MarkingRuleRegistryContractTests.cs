using System.Reflection;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class MarkingRuleRegistryContractTests
{
    [Fact]
    public void CreateDefault_ExposesNewRuleContractCollections()
    {
        var registry = MarkingRuleRegistry.CreateDefault();

        Assert.NotEmpty(registry.SeedRules);
        Assert.NotEmpty(registry.ExpressionProjectionRules);
        Assert.NotEmpty(registry.PropagationRules);
        Assert.NotEmpty(registry.ProtectionRules);
        Assert.NotEmpty(registry.MethodRules);
        Assert.NotEmpty(registry.MemberTargetRules);
        Assert.NotEmpty(registry.ClassRules);
        Assert.NotEmpty(registry.BoundaryPromotionRules);
        Assert.NotEmpty(registry.StatementScopeRules);
    }

    [Fact]
    public void LegacyRuntime_IsNotPubliclyExposed()
    {
        Assert.Null(typeof(MarkingRuleRegistry).GetProperty("LegacyRuntime", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void LegacyCompatibilityFactory_IsNotPubliclyExposed()
    {
        Assert.Null(typeof(MarkingRuleRegistry).GetMethod("CreateLegacyCompatibility", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void CreateDefault_DoesNotIncludeLegacyRuleImplementations()
    {
        var registry = MarkingRuleRegistry.CreateDefault();
        var allRules = registry.SeedRules.Cast<object>()
            .Concat(registry.ExpressionProjectionRules)
            .Concat(registry.PropagationRules)
            .Concat(registry.ProtectionRules)
            .Concat(registry.MethodRules)
            .Concat(registry.MemberTargetRules)
            .Concat(registry.ClassRules)
            .Concat(registry.BoundaryPromotionRules)
            .Concat(registry.StatementScopeRules)
            .ToArray();

        var offenders = allRules
            .Select(rule => rule.GetType().Name)
            .Where(typeName => typeName.Contains("Legacy", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Default registry must not include legacy runtime rules.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void PublicRuleCollections_MethodMemberClass_ExposeCoreRuleContracts()
    {
        var type = typeof(MarkingRuleRegistry);
        Assert.Equal(typeof(IReadOnlyList<IMethodRule>), type.GetProperty(nameof(MarkingRuleRegistry.MethodRules))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<IMemberTargetRule>), type.GetProperty(nameof(MarkingRuleRegistry.MemberTargetRules))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<IClassRule>), type.GetProperty(nameof(MarkingRuleRegistry.ClassRules))!.PropertyType);
    }
}
