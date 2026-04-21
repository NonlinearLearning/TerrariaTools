using Application.Contracts;
using Application.Contracts.Decision;
using Application.Contracts.Propagation;
using Application.Contracts.Workflow;
using Application.Mappers;
using Logic.Decision;
using Logic.Propagation;

namespace Application.Services.RewriteWorkflow;

internal static class RewriteWorkflowRunDtoAssembler
{
    public static RewriteWorkflowRunDto Map(RewriteWorkflowRunState state)
    {
        PropagationResultDto propagation = MapPropagation(state.RunCorrelationId, state.Propagation);
        DecisionResultDto decisionResult = MapDecision(state.Decision);

        return new RewriteWorkflowRunDto
        {
            RunCorrelationId = state.RunCorrelationId,
            Propagation = propagation,
            Candidate = propagation.Candidate,
            DecisionResult = decisionResult,
            Decision = decisionResult.Decision,
            Plan = ContractMapper.Map(state.Artifacts.Plan),
            Result = ContractMapper.Map(state.Artifacts.Result),
            Evidence = ContractMapper.Map(state.Artifacts.Evidence),
            Report = ContractMapper.Map(state.Artifacts.Report),
            DomainEvents = ContractMapper.Map(state.Artifacts.DomainEvents),
        };
    }

    private static PropagationResultDto MapPropagation(Guid runCorrelationId, PropagationResolution propagation)
    {
        return new PropagationResultDto
        {
            RunCorrelationId = runCorrelationId,
            CandidateId = propagation.Candidate.Id,
            Candidate = ContractMapper.Map(propagation.Candidate),
            SliceBoundary = ContractMapper.Map(propagation.SliceBoundary),
            PropagationTraces = propagation.PropagationTraces.Select(ContractMapper.Map).ToArray(),
            FactReferences = propagation.FactReferences.Select(ContractMapper.Map).ToArray(),
        };
    }

    private static DecisionResultDto MapDecision(RewriteDecisionResolution decision)
    {
        RewriteDecisionDto decisionDto = ContractMapper.Map(decision.Decision);
        return new DecisionResultDto
        {
            CandidateId = decision.CandidateId,
            Decision = decisionDto,
            Approved = decision.Approved,
            Protections = decisionDto.Protections,
            Conflicts = decisionDto.Conflicts,
        };
    }
}
