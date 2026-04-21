using Domain.Execution;

namespace Logic.Workflow;

public sealed class RewriteWorkflowEvidenceStageInput
{
    public Guid RunCorrelationId { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public PlanAction PlanAction { get; init; }
    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();
}
