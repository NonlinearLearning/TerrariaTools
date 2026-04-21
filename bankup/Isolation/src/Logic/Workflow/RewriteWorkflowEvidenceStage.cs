using Domain.Execution;
using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 工作流证据收集阶段。
/// </summary>
public sealed class RewriteWorkflowEvidenceStage : IRewriteWorkflowEvidenceStage
{
    private readonly ICompilationEvidenceCollector compilationEvidenceCollector;
    private readonly IStaticReasoningEvidenceCollector staticReasoningEvidenceCollector;
    private readonly IBehaviorEvidenceCollector behaviorEvidenceCollector;

    public RewriteWorkflowEvidenceStage(
        ICompilationEvidenceCollector compilationEvidenceCollector,
        IStaticReasoningEvidenceCollector staticReasoningEvidenceCollector,
        IBehaviorEvidenceCollector behaviorEvidenceCollector)
    {
        this.compilationEvidenceCollector = compilationEvidenceCollector;
        this.staticReasoningEvidenceCollector = staticReasoningEvidenceCollector;
        this.behaviorEvidenceCollector = behaviorEvidenceCollector;
    }

    public RewriteWorkflowEvidenceStageResult BuildEvidence(RewriteWorkflowEvidenceStageInput input, RewriteWorkflowExecutionStageResult previousStage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previousStage);

        RewriteResult result = previousStage.Result;
        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(compilationEvidenceCollector.Collect(
            new CompilationEvidenceCollectionInput(result, result.ExecutionFailures.Count == 0, result.ExecutionFailures.Count)));
        evidence.AddStaticReasoningEvidence(staticReasoningEvidenceCollector.Collect(
            new StaticReasoningEvidenceCollectionInput(input.TargetName, input.PropagationTargets.Prepend(input.TargetName).ToArray())));
        evidence.AddBehaviorEvidence(behaviorEvidenceCollector.Collect(
            new BehaviorEvidenceCollectionInput(input.PlanAction.ToString(), result)));
        evidence.Collect(RewriteWorkflowCorrelationResolver.Resolve(input));
        return new RewriteWorkflowEvidenceStageResult
        {
            Plan = previousStage.Plan,
            Result = result,
            Evidence = evidence,
        };
    }
}
