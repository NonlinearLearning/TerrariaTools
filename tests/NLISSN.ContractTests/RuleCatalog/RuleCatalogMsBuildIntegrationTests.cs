using System.Reflection;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleCatalogMsBuildIntegrationTests
{
    [Fact]
    public void RealBuildCatalog_ExposesOnlyFlatDescriptors()
    {
        var descriptors = GetAllDescriptors().ToArray();

        Assert.Equal(68, descriptors.Length);
    }

    [Fact]
    public void RealBuildCatalog_ContainsSixtyEightFactories()
    {
        var descriptors = GetAllDescriptors().ToArray();

        Assert.Equal(68, descriptors.Length);
        Assert.Equal(
          descriptors.Length,
          descriptors.Select(descriptor => descriptor.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Count());

        foreach (var descriptor in descriptors)
        {
            var instance = descriptor.Create();
            Assert.Equal(descriptor.TypeName, instance.GetType().Name);
            Assert.Equal(descriptor.RuleId, instance.RuleId);
        }

    }

    [Fact]
    public void RulesAssemblyReferencesGeneratorOnlyAsAnAnalyzer()
    {
        var rulesAssembly = typeof(GeneratedRuleCatalog).Assembly;
        Assert.DoesNotContain(
          rulesAssembly.GetReferencedAssemblies(),
          reference => string.Equals(
            reference.Name,
            typeof(NLISSN.Rule.Generator.RuleCatalogGenerator).Assembly.GetName().Name,
            StringComparison.Ordinal));

        var projectPath = RepositoryPath("src", "NLISSN.Rules", "NLISSN.Rules.csproj");
        var projectText = File.ReadAllText(projectPath);
        Assert.Contains("OutputItemType=\"Analyzer\"", projectText, StringComparison.Ordinal);
        Assert.Contains("ReferenceOutputAssembly=\"false\"", projectText, StringComparison.Ordinal);

        var generatedFiles = Directory.EnumerateFiles(
            RepositoryPath("Build"),
            "NLISSN.Rules.GeneratedRuleCatalog.g.cs",
            SearchOption.AllDirectories)
          .ToArray();
        Assert.NotEmpty(generatedFiles);
        Assert.All(generatedFiles, path =>
          Assert.StartsWith(
            Path.GetFullPath(RepositoryPath("Build")) + Path.DirectorySeparatorChar,
            Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Descriptor> GetAllDescriptors()
    {
        return GeneratedRuleCatalog.Markers.Select(descriptor => new Descriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.Factory))
          .Concat(GeneratedRuleCatalog.Propagators.Select(descriptor => new Descriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.Factory)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(descriptor => new Descriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.Factory)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(descriptor => new Descriptor(
            descriptor.RuleId,
            descriptor.TypeName,
            descriptor.Factory)));
    }

    private static string RepositoryPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(new[] { current!.FullName }.Concat(parts).ToArray());
    }

    private sealed record Descriptor(
      string RuleId,
      string TypeName,
      Func<IRuleDefinition> Create);
}
