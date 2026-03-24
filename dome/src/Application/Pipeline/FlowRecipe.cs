namespace TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// Declares the ordered slot names that compose a flow.
/// </summary>
/// <typeparam name="TContext">The pipeline context type used by the flow.</typeparam>
public sealed class FlowRecipe<TContext>
    where TContext : class, IPipelineContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FlowRecipe{TContext}"/> class.
    /// </summary>
    /// <param name="requiredSlots">The ordered slot names required by the flow.</param>
    public FlowRecipe(IReadOnlyList<string> requiredSlots)
    {
        ArgumentNullException.ThrowIfNull(requiredSlots);
        if (requiredSlots.Count == 0)
        {
            throw new ArgumentException("A flow recipe must declare at least one slot.", nameof(requiredSlots));
        }

        RequiredSlots = requiredSlots
            .Select(NormalizeSlotName)
            .ToArray();

        var duplicateSlot = RequiredSlots
            .GroupBy(static slot => slot, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)?
            .Key;
        if (duplicateSlot != null)
        {
            throw new ArgumentException($"Flow recipe contains duplicate slot '{duplicateSlot}'.", nameof(requiredSlots));
        }
    }

    /// <summary>
    /// Gets the ordered slot names required by the flow.
    /// </summary>
    public IReadOnlyList<string> RequiredSlots { get; }

    internal static string NormalizeSlotName(string slotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        return slotName.Trim();
    }
}
