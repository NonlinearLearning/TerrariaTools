using System.Text.Json;
using NLISSN.Application;
using NLISSN.Composition;
using NLISSN.Core.Pipeline;
using NLISSN.Rules;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class GeneratedRuleCatalogEquivalenceTests
{
    [Fact]
    public void Composer_DefaultMatchesFrozenIdentitySnapshot()
    {
        var snapshot = LoadIdentitySnapshot();

        Assert.Equal(
          ExpectedStageSnapshot(snapshot),
          StageSnapshot(RulePipelineComposer.Compose(new RuleSelection()).Pipeline));
        Assert.Equal(
          ExpectedStageSnapshot(snapshot),
          StageSnapshot(RulePipelineComposer.Compose(new RuleSelection()).Pipeline));
    }

    [Fact]
    public void Composer_DisabledRulePreservesActiveAndDisabledGraphDeclarations()
    {
        var generated = RulePipelineComposer.Compose(new RuleSelection(
          disabledRuleIds: new[] { "mark.target.member-access" })).Pipeline;

        Assert.Equal(
          new[] { "mark.target.member-access" },
          generated.DisabledMarkers!.Select(rule => rule.RuleId));
        Assert.DoesNotContain(
          generated.Markers,
          rule => rule.RuleId == "mark.target.member-access");

        var graph = generated.CompileRuleGraph();
        var disabledNode = Assert.Single(graph.Nodes, node =>
          node.NodeId == RuleNodeId.For(RuleKind.Mark, "mark.target.member-access"));
        Assert.Equal(RuleKind.Mark, disabledNode.Kind);
    }

    private static IReadOnlyList<string> ExpectedStageSnapshot(
      IReadOnlyList<RuleIdentityBaseline> snapshot)
    {
        return snapshot
          .OrderBy(entry => StageOrder(entry.Stage))
          .ThenBy(entry => entry.Type, StringComparer.Ordinal)
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .ToArray();
    }

    private static IReadOnlyList<string> StageSnapshot(RulePipeline pipeline)
    {
        return pipeline.Markers
          .Cast<IRuleDefinition>()
          .Select(rule => $"Mark|{rule.GetType().FullName}|{rule.RuleId}")
          .Concat(pipeline.Propagators.Select(rule => $"Propagate|{rule.GetType().FullName}|{rule.RuleId}"))
          .Concat(pipeline.Lifters.Select(rule => $"Lift|{rule.GetType().FullName}|{rule.RuleId}"))
          .Concat(pipeline.Proposers.Select(rule => $"Propose|{rule.GetType().FullName}|{rule.RuleId}"))
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
