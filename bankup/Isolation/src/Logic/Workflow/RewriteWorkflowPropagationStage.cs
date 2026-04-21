using Logic.Propagation;
using Domain.Rules;

namespace Logic.Workflow;

/// <summary>
/// 工作流传播阶段。
/// </summary>
public sealed class RewriteWorkflowPropagationStage : IRewriteWorkflowPropagationStage
{
    private readonly IImpactPropagator impactPropagator;

    public RewriteWorkflowPropagationStage(IImpactPropagator impactPropagator)
    {
        this.impactPropagator = impactPropagator;
    }

    public RewriteWorkflowPropagationStageResult Propagate(RewriteWorkflowPropagationStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RuleCode);

        return new RewriteWorkflowPropagationStageResult
        {
            Resolution = impactPropagator.Propagate(new PropagationBuildInput
            {
                RuleTargetId = input.RuleTargetId,
                RuleCode = RuleCode.Create(input.RuleCode),
                TargetName = input.TargetName,
                CandidateKind = input.CandidateKind,
                PrimaryReason = input.PrimaryReason,
                AdditionalReasons = input.AdditionalReasons,
                ScenarioTags = input.ScenarioTags,
                BoundaryName = input.BoundaryName,
                SliceDirection = input.SliceDirection,
                MaxDepth = input.MaxDepth,
                IncludeExternalReferences = input.IncludeExternalReferences,
                PropagationTargets = input.PropagationTargets,
                Candidate = input.Candidate,
                Snapshot = input.Snapshot,
            }),
        };
    }
}
