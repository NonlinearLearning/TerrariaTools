using TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// Carries execution-scoped metadata for assembled flows without becoming a
/// mutable business-state container.
/// </summary>
/// <param name="CorrelationId">A caller-supplied identifier for tracing.</param>
/// <param name="StageTraces">The collected stage execution traces.</param>
/// <param name="Items">Arbitrary execution-scoped metadata.</param>
public sealed record FlowExecutionContext(
    string CorrelationId,
    IList<PipelineStageTrace> StageTraces,
    IDictionary<string, object?> Items) : IFlowExecutionContext;
