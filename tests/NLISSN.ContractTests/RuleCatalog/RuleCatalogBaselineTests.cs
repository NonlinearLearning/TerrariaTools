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
    public void ManualCatalog_AllFeatures_ContainsEveryConcreteRuleExactlyOnce()
    {
        var pipeline = CreatePipeline(
          unreachableMethodDeletion: true,
          unreferencedMethodDeletion: true,
          unusedInterfaceImplementationCleanup: true,
          internalOnlyPublicMethodPrivatization: true);
        var registeredTypes = GetAllRules(pipeline)
          .Select(entry => entry.Rule.GetType().FullName!)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();
        var concreteRuleTypes = typeof(AtomicIdentifierNameMarkRule).Assembly
          .GetTypes()
          .Where(type =>
            type.IsClass &&
            !type.IsAbstract &&
            typeof(IRuleDefinition).IsAssignableFrom(type))
          .Select(type => type.FullName!)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

        Assert.Equal(concreteRuleTypes, registeredTypes);
        Assert.Equal(
          registeredTypes.Length,
          registeredTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ManualCatalog_DefaultAndAllFeatureStageCounts_AreFrozen()
    {
        var defaultPipeline = CreatePipeline();
        var allFeaturePipeline = CreatePipeline(
          unreachableMethodDeletion: true,
          unreferencedMethodDeletion: true,
          unusedInterfaceImplementationCleanup: true,
          internalOnlyPublicMethodPrivatization: true);

        Assert.Equal((19, 12, 5, 32), GetStageCounts(defaultPipeline));
        Assert.Equal((23, 16, 9, 36), GetStageCounts(allFeaturePipeline));

        var snapshot = LoadIdentitySnapshot();
        var expectedDefault = snapshot
          .Where(entry => !OptionalRuleTypes.Contains(entry.Type))
          .OrderBy(entry => StageOrder(entry.Stage))
          .ThenBy(entry => entry.Type, StringComparer.Ordinal)
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .ToArray();
        var actualDefault = GetAllRules(defaultPipeline)
          .Select(entry => $"{entry.Stage}|{entry.Rule.GetType().FullName}|{entry.Rule.RuleId}")
          .ToArray();

        Assert.Equal(expectedDefault, actualDefault);

        var expected = snapshot
          .OrderBy(entry => StageOrder(entry.Stage))
          .ThenBy(entry => entry.Type, StringComparer.Ordinal)
          .Select(entry => $"{entry.Stage}|{entry.Type}|{entry.TargetRuleId}")
          .ToArray();
        var actual = GetAllRules(allFeaturePipeline)
          .Select(entry => $"{entry.Stage}|{entry.Rule.GetType().FullName}|{entry.Rule.RuleId}")
          .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(
          actual.Length,
          actual.Select(entry => entry[(entry.LastIndexOf('|') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void ManualCatalog_FeatureStageDeltas_AreFrozen()
    {
        var defaultPipeline = CreatePipeline();

        AssertFeatureDelta(
          defaultPipeline,
          CreatePipeline(unreachableMethodDeletion: true),
          new[] { typeof(UnreachableMethodMarkRule) },
          new[] { typeof(UnreachableMethodPropagationRule) },
          new[] { typeof(UnreachableMethodLiftingRule) },
          new[] { typeof(UnreachableMethodProposalRule) });
        AssertFeatureDelta(
          defaultPipeline,
          CreatePipeline(unreferencedMethodDeletion: true),
          new[] { typeof(UnreferencedMethodMarkRule) },
          new[] { typeof(UnreferencedMethodPropagationRule) },
          new[] { typeof(UnreferencedMethodLiftingRule) },
          new[] { typeof(UnreferencedMethodProposalRule) });
        AssertFeatureDelta(
          defaultPipeline,
          CreatePipeline(unusedInterfaceImplementationCleanup: true),
          new[] { typeof(ClearUnusedInterfaceImplementationRule) },
          new[] { typeof(ClearUnusedInterfaceImplementationPropagationRule) },
          new[] { typeof(ClearUnusedInterfaceImplementationLiftingRule) },
          new[] { typeof(ClearUnusedInterfaceImplementationProposalRule) });
        AssertFeatureDelta(
          defaultPipeline,
          CreatePipeline(internalOnlyPublicMethodPrivatization: true),
          new[] { typeof(PrivatizeInternalOnlyPublicMethodRule) },
          new[] { typeof(PrivatizeInternalOnlyPublicMethodPropagationRule) },
          new[] { typeof(PrivatizeInternalOnlyPublicMethodLiftingRule) },
          new[] { typeof(PrivatizeInternalOnlyPublicMethodProposalRule) });
    }

    private static void AssertFeatureDelta(
      RulePipeline defaultPipeline,
      RulePipeline featurePipeline,
      IReadOnlyList<Type> expectedMarkers,
      IReadOnlyList<Type> expectedPropagators,
      IReadOnlyList<Type> expectedLifters,
      IReadOnlyList<Type> expectedProposers)
    {
        Assert.Equal(expectedMarkers, Difference(defaultPipeline.Markers, featurePipeline.Markers));
        Assert.Equal(expectedPropagators, Difference(defaultPipeline.Propagators, featurePipeline.Propagators));
        Assert.Equal(expectedLifters, Difference(defaultPipeline.Lifters, featurePipeline.Lifters));
        Assert.Equal(expectedProposers, Difference(defaultPipeline.Proposers, featurePipeline.Proposers));
    }

    private static IReadOnlyList<Type> Difference<TStage>(
      IReadOnlyList<TStage> baseline,
      IReadOnlyList<TStage> candidate)
      where TStage : class
    {
        var baselineTypes = baseline.Select(rule => rule.GetType()).ToHashSet();
        return candidate
          .Select(rule => rule.GetType())
          .Where(type => !baselineTypes.Contains(type))
          .ToArray();
    }

    private static RulePipeline CreatePipeline(
      bool unreachableMethodDeletion = false,
      bool unreferencedMethodDeletion = false,
      bool unusedInterfaceImplementationCleanup = false,
      bool internalOnlyPublicMethodPrivatization = false)
    {
        return RulePipelineComposer.Compose(RuleSelectionAdapter.FromLegacySettings(
          disabledRuleTypes: null,
          deleteUnreachableMethods: unreachableMethodDeletion,
          deleteUnreferencedMethods: unreferencedMethodDeletion,
          clearUnusedInterfaceImplementations: unusedInterfaceImplementationCleanup,
          privatizeInternalOnlyPublicMethods: internalOnlyPublicMethodPrivatization)).Pipeline;
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

    private static readonly IReadOnlySet<string> OptionalRuleTypes = new HashSet<string>(
      new[]
      {
        typeof(UnreachableMethodMarkRule).FullName!,
        typeof(UnreachableMethodPropagationRule).FullName!,
        typeof(UnreachableMethodLiftingRule).FullName!,
        typeof(UnreachableMethodProposalRule).FullName!,
        typeof(UnreferencedMethodMarkRule).FullName!,
        typeof(UnreferencedMethodPropagationRule).FullName!,
        typeof(UnreferencedMethodLiftingRule).FullName!,
        typeof(UnreferencedMethodProposalRule).FullName!,
        typeof(ClearUnusedInterfaceImplementationRule).FullName!,
        typeof(ClearUnusedInterfaceImplementationPropagationRule).FullName!,
        typeof(ClearUnusedInterfaceImplementationLiftingRule).FullName!,
        typeof(ClearUnusedInterfaceImplementationProposalRule).FullName!,
        typeof(PrivatizeInternalOnlyPublicMethodRule).FullName!,
        typeof(PrivatizeInternalOnlyPublicMethodPropagationRule).FullName!,
        typeof(PrivatizeInternalOnlyPublicMethodLiftingRule).FullName!,
        typeof(PrivatizeInternalOnlyPublicMethodProposalRule).FullName!,
      },
      StringComparer.Ordinal);

    private sealed record RuleIdentityBaseline(
      string Stage,
      string Type,
      string SourceCapability,
      string ObservedRuleId,
      string TargetRuleId);
}
