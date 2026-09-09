using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleCatalogProductionBoundaryTests
{
    [Fact]
    public void ProductionCompositionHasNoManualRuleRegistry()
    {
        var repositoryRoot = RepositoryRoot();
        Assert.False(File.Exists(Path.Combine(
          repositoryRoot,
          "src",
          "NLISSN",
          "Composition",
          "RuleRegistry.cs")));

        var productionSources = Directory.EnumerateFiles(
          Path.Combine(repositoryRoot, "src"),
          "*.cs",
          SearchOption.AllDirectories);
        foreach (var sourcePath in productionSources)
        {
            Assert.DoesNotContain(
              "RuleRegistry.CreateDefaultRules",
              File.ReadAllText(sourcePath),
              StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current!.FullName;
    }
}
