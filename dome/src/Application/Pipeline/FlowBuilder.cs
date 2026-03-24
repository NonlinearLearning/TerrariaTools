namespace TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// Builds a concrete ordered pipeline from a slot recipe plus per-slot
/// implementations and decorators.
/// </summary>
/// <typeparam name="TContext">The pipeline context type used by the flow.</typeparam>
public sealed class FlowBuilder<TContext>
    where TContext : class, IPipelineContext
{
    private readonly HashSet<string> _requiredSlots;
    private readonly Dictionary<string, Func<IPipelineStage<TContext>>> _slotFactories =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Func<IPipelineStage<TContext>, IPipelineStage<TContext>>>> _slotDecorators =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowBuilder{TContext}"/> class.
    /// </summary>
    /// <param name="recipe">The ordered recipe to satisfy.</param>
    public FlowBuilder(FlowRecipe<TContext> recipe)
    {
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        _requiredSlots = [.. Recipe.RequiredSlots];
    }

    /// <summary>
    /// Gets the ordered recipe to satisfy.
    /// </summary>
    public FlowRecipe<TContext> Recipe { get; }

    /// <summary>
    /// Registers the core implementation for a required slot.
    /// </summary>
    /// <param name="slotName">The slot being configured.</param>
    /// <param name="factory">The factory that produces the stage instance.</param>
    /// <returns>The current builder.</returns>
    public FlowBuilder<TContext> Use(string slotName, Func<IPipelineStage<TContext>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var normalizedSlotName = FlowRecipe<TContext>.NormalizeSlotName(slotName);
        EnsureRecipeContainsSlot(normalizedSlotName);
        if (!_slotFactories.TryAdd(normalizedSlotName, factory))
        {
            throw new InvalidOperationException($"Slot '{normalizedSlotName}' is already configured.");
        }

        return this;
    }

    /// <summary>
    /// Registers a decorator around the configured slot implementation.
    /// </summary>
    /// <param name="slotName">The slot being decorated.</param>
    /// <param name="decorator">The decorator factory.</param>
    /// <returns>The current builder.</returns>
    public FlowBuilder<TContext> Decorate(
        string slotName,
        Func<IPipelineStage<TContext>, IPipelineStage<TContext>> decorator)
    {
        ArgumentNullException.ThrowIfNull(decorator);

        var normalizedSlotName = FlowRecipe<TContext>.NormalizeSlotName(slotName);
        EnsureRecipeContainsSlot(normalizedSlotName);
        if (!_slotDecorators.TryGetValue(normalizedSlotName, out var decorators))
        {
            decorators = [];
            _slotDecorators.Add(normalizedSlotName, decorators);
        }

        decorators.Add(decorator);
        return this;
    }

    /// <summary>
    /// Materializes the configured slots into an ordered pipeline.
    /// </summary>
    /// <returns>The assembled pipeline stages.</returns>
    public IReadOnlyList<IPipelineStage<TContext>> Build()
    {
        var stages = new List<IPipelineStage<TContext>>(Recipe.RequiredSlots.Count);
        foreach (var slotName in Recipe.RequiredSlots)
        {
            if (!_slotFactories.TryGetValue(slotName, out var factory))
            {
                throw new InvalidOperationException($"Required slot '{slotName}' is not configured.");
            }

            var stage = factory();
            ArgumentNullException.ThrowIfNull(stage);

            if (_slotDecorators.TryGetValue(slotName, out var decorators))
            {
                for (var index = decorators.Count - 1; index >= 0; index--)
                {
                    stage = decorators[index](stage) ??
                        throw new InvalidOperationException($"Decorator for slot '{slotName}' returned null.");
                }
            }

            stages.Add(stage);
        }

        return stages;
    }

    private void EnsureRecipeContainsSlot(string slotName)
    {
        if (!_requiredSlots.Contains(slotName))
        {
            throw new InvalidOperationException($"Slot '{slotName}' is not declared by this flow recipe.");
        }
    }
}
