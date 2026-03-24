namespace TerrariaTools.Dome.Application.Composition;

using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.UseCases.Runtime;

/// <summary>
/// Describes one assembled runtime-family flow recipe.
/// </summary>
internal sealed class TerrariaRuntimeFlowRecipe
{
    internal TerrariaRuntimeFlowRecipe(
        FlowRecipe<TerrariaRuntimePipelineContext> stageRecipe,
        TerrariaRuntimeFlowSlots slots)
    {
        StageRecipe = stageRecipe;
        Prepare = slots.Prepare;
        ExecuteDome = slots.ExecuteDome;
        LoadReport = slots.LoadReport;
        BuildWorkspace = slots.BuildWorkspace;
        Persist = slots.Persist;
    }

    public IReadOnlyList<string> RequiredSlots => StageRecipe.RequiredSlots;

    public ITerrariaRuntimePrepareSlot Prepare { get; }

    public ITerrariaRuntimeExecuteDomeSlot ExecuteDome { get; }

    public ITerrariaRuntimeLoadReportSlot LoadReport { get; }

    public ITerrariaRuntimeBuildWorkspaceSlot BuildWorkspace { get; }

    public ITerrariaRuntimePersistSlot Persist { get; }

    internal FlowRecipe<TerrariaRuntimePipelineContext> StageRecipe { get; }
}

/// <summary>
/// Provides fixed-slot runtime-family recipes for the runtime host.
/// </summary>
internal static class TerrariaRuntimeFlowRecipes
{
    public static TerrariaRuntimeFlowRecipe Standard(TerrariaRuntimeFlowSlots slots) =>
        new(
            new FlowRecipe<TerrariaRuntimePipelineContext>(
                ["prepare", "execute-dome", "load-report", "build-workspace", "persist"]),
            slots);

    internal static IReadOnlyList<IPipelineStage<TerrariaRuntimePipelineContext>> BuildStages(
        TerrariaRuntimeFlowRecipe recipe)
    {
        var builder = new FlowBuilder<TerrariaRuntimePipelineContext>(recipe.StageRecipe);
        builder.Use("prepare", () => new PrepareRuntimeSlotStage(recipe.Prepare));
        builder.Use("execute-dome", () => new ExecuteDomeRuntimeSlotStage(recipe.ExecuteDome));
        builder.Use("load-report", () => new LoadReportRuntimeSlotStage(recipe.LoadReport));
        builder.Use("build-workspace", () => new BuildWorkspaceRuntimeSlotStage(recipe.BuildWorkspace));
        builder.Use("persist", () => new PersistRuntimeSlotStage(recipe.Persist));
        return builder.Build();
    }
}
