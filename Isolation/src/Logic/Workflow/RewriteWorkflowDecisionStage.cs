using Domain.Decision;
using Logic.Decision;

namespace Logic.Workflow;

/// <summary>
/// 工作流决策阶段。
/// </summary>
public sealed class RewriteWorkflowDecisionStage : IRewriteWorkflowDecisionStage
{
    private readonly IRewriteDecisionAssessmentBuilder rewriteDecisionAssessmentBuilder;
    private readonly IRewriteDecisionMaker rewriteDecisionMaker;

    public RewriteWorkflowDecisionStage(
        IRewriteDecisionAssessmentBuilder rewriteDecisionAssessmentBuilder,
        IRewriteDecisionMaker rewriteDecisionMaker)
    {
        this.rewriteDecisionAssessmentBuilder = rewriteDecisionAssessmentBuilder;
        this.rewriteDecisionMaker = rewriteDecisionMaker;
    }

    public RewriteWorkflowDecisionStageResult Decide(RewriteWorkflowDecisionStageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RewriteDecisionAssessment assessment = rewriteDecisionAssessmentBuilder.Build(new RewriteDecisionAssessmentBuildInput
        {
            IncludeExternalReferences = input.IncludeExternalReferences,
            FactReferenceCount = input.Propagation.FactReferences.Count,
            ExternalCallers = input.Propagation.PropagationTraces.Select(static item => item.TargetName).ToArray(),
            SimulateFailure = input.SimulateFailure,
        });
        RewriteDecisionResolution resolution = rewriteDecisionMaker.Make(new RewriteDecisionBuildInput
        {
            CandidateId = input.Propagation.Candidate.Id,
            TargetName = input.Propagation.Candidate.TargetName,
            ProtectionRules = input.ProtectionRules,
            ConflictTargets = input.ConflictTargets,
            ConfidenceLevel = input.ConfidenceLevel,
            ForceReject = input.ForceReject,
            ContractExposure = assessment.ContractExposure,
            ExternalCallerPresence = assessment.ExternalCallerPresence,
            ClosureIntegrityAssessment = assessment.ClosureIntegrityAssessment,
            RiskScore = assessment.RiskScore,
        });

        return new RewriteWorkflowDecisionStageResult
        {
            Assessment = assessment,
            Resolution = resolution,
        };
    }
}
