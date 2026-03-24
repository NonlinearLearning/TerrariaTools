namespace TerrariaTools.Dome.Application.Composition;

using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

/// <summary>
/// Describes one assembled shadow-extraction flow recipe.
/// </summary>
internal sealed class TerrariaRuntimeShadowExtractionFlowRecipe
{
    internal TerrariaRuntimeShadowExtractionFlowRecipe(
        FlowRecipe<ShadowExtractionPipelineContext> stageRecipe,
        TerrariaRuntimeShadowExtractionFlowSlots slots)
    {
        StageRecipe = stageRecipe;
        ResolveInput = slots.ResolveInput;
        Analyze = slots.Analyze;
        BuildClosure = slots.BuildClosure;
        WriteWorkspace = slots.WriteWorkspace;
        Build = slots.Build;
        Persist = slots.Persist;
    }

    public IReadOnlyList<string> RequiredSlots => StageRecipe.RequiredSlots;

    public ITerrariaRuntimeShadowResolveInputSlot ResolveInput { get; }

    public ITerrariaRuntimeShadowAnalyzeSlot Analyze { get; }

    public ITerrariaRuntimeShadowBuildClosureSlot BuildClosure { get; }

    public ITerrariaRuntimeShadowWriteWorkspaceSlot WriteWorkspace { get; }

    public ITerrariaRuntimeShadowBuildSlot Build { get; }

    public ITerrariaRuntimeShadowPersistSlot Persist { get; }

    internal FlowRecipe<ShadowExtractionPipelineContext> StageRecipe { get; }
}

/// <summary>
/// Provides fixed-slot flow recipes for the shadow-extraction host.
/// </summary>
internal static class TerrariaRuntimeShadowExtractionFlowRecipes
{
    public static TerrariaRuntimeShadowExtractionFlowRecipe Standard(
        TerrariaRuntimeShadowExtractionFlowSlots slots) =>
        new(
            new FlowRecipe<ShadowExtractionPipelineContext>(
                ["resolve-input", "analyze", "build-closure", "write-workspace", "build", "persist"]),
            slots);

    internal static IReadOnlyList<IPipelineStage<ShadowExtractionPipelineContext>> BuildStages(
        TerrariaRuntimeShadowExtractionFlowRecipe recipe)
    {
        var builder = new FlowBuilder<ShadowExtractionPipelineContext>(recipe.StageRecipe);
        builder.Use("resolve-input", () => new ResolveInputShadowSlotStage(recipe.ResolveInput));
        builder.Use("analyze", () => new AnalyzeShadowSlotStage(recipe.Analyze));
        builder.Use("build-closure", () => new BuildClosureShadowSlotStage(recipe.BuildClosure));
        builder.Use("write-workspace", () => new WriteWorkspaceShadowSlotStage(recipe.WriteWorkspace));
        builder.Use("build", () => new BuildShadowSlotStage(recipe.Build));
        builder.Use("persist", () => new PersistShadowSlotStage(recipe.Persist));
        return builder.Build();
    }
}
