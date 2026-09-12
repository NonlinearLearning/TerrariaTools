using System.Reflection;
using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleRegistrationContractTests
{
    [Fact]
    public void RuleCatalogContract_IsDeclaredInTheLinkedRuleContractAssembly()
    {
        var assembly = typeof(IRuleDefinition).Assembly;

        Assert.NotNull(assembly.GetType("NLISSN.Core.Pipeline.RuleFeature"));
        Assert.NotNull(assembly.GetType("NLISSN.Core.Pipeline.RuleRegistrationAttribute"));
        Assert.NotNull(assembly.GetType("NLISSN.Core.Pipeline.RuleCatalogIgnoreAttribute"));
        Assert.NotNull(assembly.GetType("NLISSN.Core.Pipeline.RuleRegistration`1"));
    }

    [Fact]
    public void RuleRegistrationAttribute_ExposesFeatureAndCannotRepeat()
    {
        var attributeType = typeof(IRuleDefinition).Assembly.GetType(
          "NLISSN.Core.Pipeline.RuleRegistrationAttribute");

        Assert.NotNull(attributeType);

        var usage = attributeType!.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage!.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);

        var constructors = attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(2, constructors.Length);
        Assert.Contains(constructors, constructor => constructor.GetParameters().Length == 0);
        var featureType = typeof(IRuleDefinition).Assembly.GetType(
          "NLISSN.Core.Pipeline.RuleFeature");
        Assert.NotNull(featureType);
        Assert.Contains(constructors, constructor =>
          constructor.GetParameters() is [{ ParameterType: var parameterType }] &&
          parameterType == featureType);
        Assert.DoesNotContain(
          attributeType.GetProperties(BindingFlags.Public | BindingFlags.Instance),
          property => property.Name is "DefaultEnabled" or "RuleId");
    }

    [Fact]
    public void RuleCatalogIgnoreAttribute_IsAClassMarkerAndCannotRepeat()
    {
        var attributeType = typeof(IRuleDefinition).Assembly.GetType(
          "NLISSN.Core.Pipeline.RuleCatalogIgnoreAttribute");

        Assert.NotNull(attributeType);

        var usage = attributeType!.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage!.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.Empty(attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
          .Single()
          .GetParameters());
    }

    [Fact]
    public void RuleRegistrationDescriptor_ExposesOnlyRuntimeDirectoryData()
    {
        var descriptorType = typeof(IRuleDefinition).Assembly.GetType(
          "NLISSN.Core.Pipeline.RuleRegistration`1");

        Assert.NotNull(descriptorType);
        Assert.True(descriptorType!.IsGenericTypeDefinition);

        var properties = descriptorType
          .GetProperties(BindingFlags.Public | BindingFlags.Instance)
          .Select(property => property.Name)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

        Assert.Equal(
          new[] { "Factory", "Feature", "FullyQualifiedName", "RuleId", "TypeName" },
          properties);
        Assert.DoesNotContain(
          descriptorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
          field => field.FieldType.FullName?.Contains("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true);
    }
}
