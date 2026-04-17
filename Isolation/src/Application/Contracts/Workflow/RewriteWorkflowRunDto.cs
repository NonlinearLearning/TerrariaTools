using Application.Contracts.Decision;
using Application.Contracts.Execution;
using Application.Contracts.Marking;
using Application.Contracts.Output;
using Application.Contracts.Propagation;

namespace Application.Contracts.Workflow;

/// <summary>
/// 改写工作流运行结果 DTO。
/// </summary>
public sealed class RewriteWorkflowRunDto
{
    public PropagationResultDto Propagation { get; set; } = new();

    public ChangeCandidateDto Candidate { get; set; } = new();

    public DecisionResultDto DecisionResult { get; set; } = new();

    public RewriteDecisionDto Decision { get; set; } = new();

    public RewritePlanDto Plan { get; set; } = new();

    public RewriteResultDto Result { get; set; } = new();

    public VerificationEvidenceDto Evidence { get; set; } = new();

    public RunReportDto Report { get; set; } = new();
}
