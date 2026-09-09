using System.Text.Json;
using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.ContractTests.RuleCatalog;

public sealed class RuleCatalogBaselineTests
{
    [Fact]
    public void GeneratedCatalog_ContainsEveryConcreteRuleExactlyOnce()
    {
        var pipeline = CreatePipeline();
        var registeredTypes = GetAllRules(pipeline)
          .Select(entry => entry.Rule.GetType().FullName!)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();
        var concreteRuleTypes = typeof(AtomicIdentifierNameMarkRule).Assembly
          .GetTypes()
          .Where(type =>
            type.IsClass &&
            !type.IsAbstract &&
            typeof(IRuleDefinition).IsAssignableFrom(type) &&
            !type.IsDefined(typeof(RuleCatalogIgnoreAttribute), inherit: false))
          .Select(type => type.FullName!)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

        Assert.Equal(concreteRuleTypes, registeredTypes);
        Assert.Equal(
          registeredTypes.Length,
          registeredTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GeneratedCatalog_StageCountsAndIdentitySnapshotAreFrozen()
    {
        var defaultPipeline = CreatePipeline();

        Assert.Equal((19, 12, 5, 32), GetStageCounts(defaultPipeline));

        var snapshot = LoadIdentitySnapshot();
        var expected = snapshot
          .OrderBy(entry => StageOrder(entry.Stage))
          .ThenBy(entry => entry.Type, StringComparer.Ordinal)
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .ToArray();
        var actualDefault = GetAllRules(defaultPipeline)
          .Select(entry => $"{entry.Stage}|{entry.Rule.GetType().FullName}|{entry.Rule.RuleId}")
          .ToArray();

        Assert.Equal(expected, actualDefault);
        Assert.Equal(
          actualDefault.Length,
          actualDefault.Select(entry => entry[(entry.LastIndexOf('|') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .Count());

    }

    private static RulePipeline CreatePipeline()
    {
        return RulePipelineTestFactory.Create();
    }

    private static (int Markers, int Propagators, int Lifters, int Proposers) GetStageCounts(
      RulePipeline pipeline)
    {
        return (
          pipeline.Markers.Count,
          pipeline.Propagators.Count,
          pipeline.Lifters.Count,
          pipeline.Proposers.Count);
    }

    private static IReadOnlyList<(RuleKind Stage, IRuleDefinition Rule)> GetAllRules(
      RulePipeline pipeline)
    {
        return pipeline.Markers
          .Cast<IRuleDefinition>()
          .Select(rule => (RuleKind.Mark, rule))
          .Concat(pipeline.Propagators.Select(rule => (RuleKind.Propagate, (IRuleDefinition)rule)))
          .Concat(pipeline.Lifters.Select(rule => (RuleKind.Lift, (IRuleDefinition)rule)))
          .Concat(pipeline.Proposers.Select(rule => (RuleKind.Propose, (IRuleDefinition)rule)))
          .ToArray();
    }

    private static IReadOnlyList<RuleIdentityBaseline> LoadIdentitySnapshot()
    {
        var snapshotPath = RepositoryPath(
          "tests",
          "NLISSN.ContractTests",
          "Identity",
          "RuleIdentitySnapshot.json");
        return JsonSerializer.Deserialize<RuleIdentityBaseline[]>(
          File.ReadAllText(snapshotPath),
          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
          ?? throw new InvalidOperationException($"Could not load rule identity snapshot '{snapshotPath}'.");
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

    private sealed record RuleIdentityBaseline(
      string Stage,
      string Type,
      string SourceCapability,
      string ObservedRuleId,
      string TargetRuleId);
}
