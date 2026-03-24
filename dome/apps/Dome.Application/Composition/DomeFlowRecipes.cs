namespace TerrariaTools.Dome.Application.Composition;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// Describes one assembled Dome-family flow recipe.
/// </summary>
internal sealed class DomeFlowRecipe
{
    private readonly IReadOnlyList<Func<ApplicationAbstractions.IResultSlot, ApplicationAbstractions.IResultSlot>> _resultDecorators;

    internal DomeFlowRecipe(
        FlowRecipe<DomePipelineContext> stageRecipe,
        DomeFlowSlots slots,
        IReadOnlyList<Func<ApplicationAbstractions.IResultSlot, ApplicationAbstractions.IResultSlot>>? resultDecorators = null)
    {
        StageRecipe = stageRecipe;
        Load = slots.Load;
        Analyze = slots.Analyze;
        Rule = slots.Rule;
        Decision = slots.Decision;
        Result = slots.Result;
        _resultDecorators = resultDecorators ?? Array.Empty<Func<ApplicationAbstractions.IResultSlot, ApplicationAbstractions.IResultSlot>>();
    }

    public IReadOnlyList<string> RequiredSlots => StageRecipe.RequiredSlots;

    public ApplicationAbstractions.ILoadSlot Load { get; }

    public ApplicationAbstractions.IAnalyzeSlot Analyze { get; }

    public ApplicationAbstractions.IRuleSlot Rule { get; }

    public ApplicationAbstractions.IDecisionSlot Decision { get; }

    public ApplicationAbstractions.IResultSlot Result { get; }

    internal FlowRecipe<DomePipelineContext> StageRecipe { get; }

    public DomeFlowRecipe ReplaceAnalyze(ApplicationAbstractions.IAnalyzeSlot analyzeSlot)
    {
        ArgumentNullException.ThrowIfNull(analyzeSlot);
        return new DomeFlowRecipe(
            StageRecipe,
            new DomeFlowSlots(Load, analyzeSlot, Rule, Decision, Result),
            _resultDecorators);
    }

    public DomeFlowRecipe DecorateResult(
        Func<ApplicationAbstractions.IResultSlot, ApplicationAbstractions.IResultSlot> decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);
        return new DomeFlowRecipe(
            StageRecipe,
            new DomeFlowSlots(Load, Analyze, Rule, Decision, Result),
            _resultDecorators.Concat([decorator]).ToArray());
    }

    internal ApplicationAbstractions.IResultSlot BuildResultSlot()
    {
        var resultSlot = Result;
        for (var index = _resultDecorators.Count - 1; index >= 0; index--)
        {
            resultSlot = _resultDecorators[index](resultSlot);
        }

        return resultSlot;
    }
}

/// <summary>
/// Provides fixed-slot Dome-family recipes for the standard host.
/// </summary>
internal static class DomeFlowRecipes
{
    public static DomeFlowRecipe AnalyzeOnly(DomeFlowSlots slots) =>
        new(
            new FlowRecipe<DomePipelineContext>(["load", "analyze", "result"]),
            slots);

    public static DomeFlowRecipe PlanOnly(DomeFlowSlots slots) =>
        new(
            new FlowRecipe<DomePipelineContext>(["load", "analyze", "rule", "decision", "result"]),
            slots);

    public static DomeFlowRecipe Standard(DomeFlowSlots slots) =>
        new(
            new FlowRecipe<DomePipelineContext>(["load", "analyze", "rule", "decision", "result"]),
            slots);

    public static DomeFlowRecipe ForMode(ModelPrimitives.RunMode mode, DomeFlowSlots slots) =>
        mode switch
        {
            ModelPrimitives.RunMode.AnalyzeOnly => AnalyzeOnly(slots),
            ModelPrimitives.RunMode.PlanOnly => PlanOnly(slots),
            _ => Standard(slots)
        };

    internal static IReadOnlyList<IPipelineStage<DomePipelineContext>> BuildStages(
        DomeFlowRecipe recipe,
        RunReportBuilder runReportBuilder,
        ArtifactPlanBuilder artifactPlanBuilder,
        IArtifactEmissionService artifactEmissionService,
        IDomeProgressReporter progressReporter)
    {
        var builder = new FlowBuilder<DomePipelineContext>(recipe.StageRecipe);
        if (recipe.RequiredSlots.Contains("load", StringComparer.OrdinalIgnoreCase))
        {
            builder.Use("load", () => new LoadSlotStage(recipe.Load, runReportBuilder, artifactPlanBuilder, artifactEmissionService, progressReporter));
        }

        if (recipe.RequiredSlots.Contains("analyze", StringComparer.OrdinalIgnoreCase))
        {
            builder.Use("analyze", () => new AnalyzeSlotStage(recipe.Analyze, runReportBuilder, artifactPlanBuilder, artifactEmissionService, progressReporter));
        }

        if (recipe.RequiredSlots.Contains("rule", StringComparer.OrdinalIgnoreCase))
        {
            builder.Use("rule", () => new RuleSlotStage(recipe.Rule, progressReporter));
        }

        if (recipe.RequiredSlots.Contains("decision", StringComparer.OrdinalIgnoreCase))
        {
            builder.Use("decision", () => new DecisionSlotStage(recipe.Decision, artifactPlanBuilder, runReportBuilder, artifactEmissionService, progressReporter));
        }

        if (recipe.RequiredSlots.Contains("result", StringComparer.OrdinalIgnoreCase))
        {
            builder.Use("result", () => new ResultSlotStage(recipe.BuildResultSlot()));
        }

        return builder.Build();
    }
}
