namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// Exposes execution-scoped metadata needed by assembled application flow slots.
/// </summary>
public interface IFlowExecutionContext
{
    /// <summary>
    /// Gets a caller-supplied correlation identifier for the current execution.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Gets an execution-scoped metadata bag shared across slots.
    /// </summary>
    IDictionary<string, object?> Items { get; }
}

/// <summary>
/// Defines the standard execution contract for one fixed application flow slot.
/// </summary>
/// <typeparam name="TInput">The immutable input contract for the slot.</typeparam>
/// <typeparam name="TOutput">The immutable output contract for the slot.</typeparam>
public interface IFlowSlot<in TInput, TOutput>
{
    /// <summary>
    /// Executes the slot using the supplied business input and execution metadata.
    /// </summary>
    /// <param name="input">The immutable business input for the slot.</param>
    /// <param name="executionContext">The execution-scoped metadata for the flow.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The immutable slot output.</returns>
    Task<TOutput> ExecuteAsync(
        TInput input,
        IFlowExecutionContext executionContext,
        CancellationToken cancellationToken);
}
