using System.Text.Json;
using System.Reflection;
using System.Xml.Linq;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleCatalogBuildContractTests
{
    [Fact]
    public void GeneratedCatalog_MatchesTheFrozenAllFeatureIdentitySnapshot()
    {
        var expected = LoadIdentitySnapshot()
          .OrderBy(entry => StageOrder(entry.Stage))
          .ThenBy(entry => SimpleTypeName(entry.Type), StringComparer.Ordinal)
          .ThenBy(entry => entry.Type, StringComparer.Ordinal)
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .ToArray();
        var actual = GetCatalogEntries()
          .Select(entry => $"{entry.Stage}|{entry.FullyQualifiedName}|{entry.RuleId}")
          .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GeneratedCatalog_UsesTheFrozenFeatureMappingAndCompleteStages()
    {
        var entries = GetCatalogEntries();

        Assert.All(entries, entry => Assert.Equal(ExpectedFeature(entry.FullyQualifiedName), entry.Feature));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Mark" &&
            entry.Feature == RuleFeature.UnreachableMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Propagate" &&
            entry.Feature == RuleFeature.UnreachableMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Lift" &&
            entry.Feature == RuleFeature.UnreachableMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Propose" &&
            entry.Feature == RuleFeature.UnreachableMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Mark" &&
            entry.Feature == RuleFeature.UnreferencedMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Propagate" &&
            entry.Feature == RuleFeature.UnreferencedMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Lift" &&
            entry.Feature == RuleFeature.UnreferencedMethodDeletion));
        Assert.Equal(
          1,
          entries.Count(entry =>
            entry.Stage == "Propose" &&
            entry.Feature == RuleFeature.UnreferencedMethodDeletion));
    }

    [Fact]
    public void GeneratedCatalog_ExposesFourTypedReadOnlyLists()
    {
        AssertReadOnlyListProperty<RuleDefinitionMark>(nameof(GeneratedRuleCatalog.Markers));
        AssertReadOnlyListProperty<RuleDefinitionPropagate>(nameof(GeneratedRuleCatalog.Propagators));
        AssertReadOnlyListProperty<RuleDefinitionLift>(nameof(GeneratedRuleCatalog.Lifters));
        AssertReadOnlyListProperty<RuleDefinitionPropose>(nameof(GeneratedRuleCatalog.Proposers));
    }

    [Fact]
    public void GeneratedCatalog_BuildOutputIsUnderBuildAndHasNoGeneratorRuntimeReference()
    {
        var assembly = typeof(GeneratedRuleCatalog).Assembly;
        Assert.DoesNotContain(
          assembly.GetReferencedAssemblies(),
          reference => string.Equals(reference.Name, "NLISSN.Rule.Checker", StringComparison.Ordinal));

        var buildRoot = FindBuildRoot(assembly.Location);
        var generatedPath = Path.Combine(
          buildRoot,
          "src",
          "obj",
          "NLISSN.Rules",
          "generated",
          "NLISSN.Rule.Checker",
          "NLISSN.Rule.Checker.RuleCatalogGenerator",
          "NLISSN.Rules.GeneratedRuleCatalog.g.cs");

        Assert.True(File.Exists(generatedPath), $"Expected generated source at '{generatedPath}'.");
        Assert.StartsWith(
          Path.Combine(buildRoot, "src"),
          Path.GetFullPath(generatedPath),
          StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectReference_UsesAnalyzerOnlyGeneratorAndBuildsGeneratedFilesUnderBuild()
    {
        var generatorProject = XDocument.Load(ProjectPath(
          "src",
          "NLISSN.Rule",
          "Checker",
          "NLISSN.Rule.Checker.csproj"));
        var rulesProject = XDocument.Load(ProjectPath(
          "src",
          "NLISSN.Rules",
          "NLISSN.Rules.csproj"));

        Assert.Equal(
          "netstandard2.0",
          generatorProject.Descendants("TargetFramework").Single().Value);
        Assert.Empty(generatorProject.Descendants("ProjectReference"));

        var generatorPackage = generatorProject.Descendants("PackageReference")
          .Single(element => element.Attribute("Include")?.Value == "Microsoft.CodeAnalysis.CSharp");
        Assert.Equal("4.14.0", generatorPackage.Attribute("Version")?.Value);
        Assert.Equal("all", generatorPackage.Element("PrivateAssets")?.Value);

        var generatorReference = rulesProject.Descendants("ProjectReference")
          .Single(element => element.Attribute("Include")?.Value.Contains(
            "NLISSN.Rule.Checker",
            StringComparison.Ordinal) == true);
        Assert.Equal("Analyzer", generatorReference.Attribute("OutputItemType")?.Value);
        Assert.Equal("false", generatorReference.Attribute("ReferenceOutputAssembly")?.Value);
        Assert.Equal(
          "true",
          rulesProject.Descendants("EmitCompilerGeneratedFiles").Single().Value);
        Assert.Equal(
          "$(BaseIntermediateOutputPath)generated",
          rulesProject.Descendants("CompilerGeneratedFilesOutputPath").Single().Value);
    }

    [Fact]
    public void GeneratedCatalog_FactoriesCreateFreshRuleInstances()
    {
        var descriptor = Assert.Single(
          GeneratedRuleCatalog.Markers,
          candidate => candidate.RuleId == "mark.target.identifier-name");

        Assert.NotSame(descriptor.Factory(), descriptor.Factory());
    }

    private static void AssertReadOnlyListProperty<TStage>(string propertyName)
      where TStage : class, IRuleDefinition
    {
        var property = typeof(GeneratedRuleCatalog).GetProperty(
          propertyName,
          BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(property);
        Assert.Equal(typeof(IReadOnlyList<>), property!.PropertyType.GetGenericTypeDefinition());
        Assert.Equal(typeof(RuleRegistration<TStage>), property.PropertyType.GetGenericArguments()[0]);
    }

    private static IReadOnlyList<CatalogEntry> GetCatalogEntries()
    {
        return GeneratedRuleCatalog.Markers
          .Select(entry => new CatalogEntry(
            "Mark",
            entry.RuleId,
            entry.TypeName,
            entry.FullyQualifiedName,
            entry.Feature))
          .Concat(GeneratedRuleCatalog.Propagators.Select(entry => new CatalogEntry(
            "Propagate",
            entry.RuleId,
            entry.TypeName,
            entry.FullyQualifiedName,
            entry.Feature)))
          .Concat(GeneratedRuleCatalog.Lifters.Select(entry => new CatalogEntry(
            "Lift",
            entry.RuleId,
            entry.TypeName,
            entry.FullyQualifiedName,
            entry.Feature)))
          .Concat(GeneratedRuleCatalog.Proposers.Select(entry => new CatalogEntry(
            "Propose",
            entry.RuleId,
            entry.TypeName,
            entry.FullyQualifiedName,
            entry.Feature)))
          .ToArray();
    }

    private static RuleFeature ExpectedFeature(string fullyQualifiedName)
    {
        if (fullyQualifiedName.Contains("UnreachableMethod", StringComparison.Ordinal))
        {
            return RuleFeature.UnreachableMethodDeletion;
        }

        if (fullyQualifiedName.Contains("UnreferencedMethod", StringComparison.Ordinal))
        {
            return RuleFeature.UnreferencedMethodDeletion;
        }

        if (fullyQualifiedName.Contains("ClearUnusedInterfaceImplementation", StringComparison.Ordinal))
        {
            return RuleFeature.UnusedInterfaceImplementationCleanup;
        }

        if (fullyQualifiedName.Contains("PrivatizeInternalOnlyPublicMethod", StringComparison.Ordinal))
        {
            return RuleFeature.InternalOnlyPublicMethodPrivatization;
        }

        return RuleFeature.Core;
    }

    private static IReadOnlyList<RuleIdentityBaseline> LoadIdentitySnapshot()
    {
        return JsonSerializer.Deserialize<RuleIdentityBaseline[]>(
            RuleIdentitySnapshotResource.Read(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
          ?? throw new InvalidOperationException("Could not load the embedded rule identity snapshot.");
    }

    private static int StageOrder(string stage)
    {
        return stage switch
        {
            "Mark" => 0,
            "Propagate" => 1,
            "Lift" => 2,
            "Propose" => 3,
            _ => throw new InvalidOperationException($"Unknown rule stage '{stage}'.")
        };
    }

    private static string SimpleTypeName(string fullyQualifiedName)
    {
        var separator = fullyQualifiedName.LastIndexOf('.');
        return separator < 0 ? fullyQualifiedName : fullyQualifiedName[(separator + 1)..];
    }

    private static string FindBuildRoot(string assemblyLocation)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)!);
        while (current is not null && !string.Equals(current.Name, "Build", StringComparison.OrdinalIgnoreCase))
        {
            current = current.Parent;
        }

        return current?.FullName
          ?? throw new InvalidOperationException($"Could not find Build root for '{assemblyLocation}'.");
    }

    private static string ProjectPath(params string[] parts)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(GetSourceFilePath())!);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }

        return Path.Combine(new[]
        {
            current?.FullName ?? throw new InvalidOperationException("Could not find repository root."),
        }.Concat(parts).ToArray());
    }

    private static string GetSourceFilePath(
      [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") => sourceFile;

    private sealed record CatalogEntry(
      string Stage,
      string RuleId,
      string TypeName,
      string FullyQualifiedName,
      RuleFeature Feature);

    private sealed record RuleIdentityBaseline(
      string Stage,
      string Type,
      string SourceCapability,
      string ObservedRuleId,
      string TargetRuleId);
}

internal static class RuleIdentitySnapshotResource
{
    public static string Read()
    {
        using var stream = typeof(RuleIdentitySnapshotResource).Assembly.GetManifestResourceStream(
          "NLISSN.ContractTests.RuleIdentitySnapshot.json");
        if (stream is null)
        {
            throw new InvalidOperationException("The embedded rule identity snapshot was not found.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
