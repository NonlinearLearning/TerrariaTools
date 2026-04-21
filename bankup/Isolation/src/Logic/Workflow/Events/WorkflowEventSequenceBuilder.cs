using Domain.Analysis.Events;
using Domain.Common.Events;
using Domain.Decision.Events;
using Domain.Execution;
using Domain.Execution.Events;
using Domain.Marking.Events;
using Domain.Output.Audit;
using Domain.Output.Audit.Events;
using Domain.Output.Verification;
using Domain.Output.Verification.Events;
using Domain.Propagation.Events;
using Domain.Workspaces.Events;

namespace Logic.Workflow.Events;

/// <summary>
/// 负责组装工作流主链领域事件序列。
/// </summary>
public sealed class WorkflowEventSequenceBuilder
{
    private readonly IDomainEventRecorder? domainEventRecorder;

    public WorkflowEventSequenceBuilder(IDomainEventRecorder? domainEventRecorder = null)
    {
        this.domainEventRecorder = domainEventRecorder;
    }

    public IReadOnlyCollection<IDomainEvent> BuildEvents(
        RewriteWorkflowEventStageInput input,
        RewriteWorkflowArtifacts artifacts)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(artifacts);
        RewritePlan plan = artifacts.Plan;
        RewriteResult result = artifacts.Result;
        VerificationEvidence evidence = artifacts.Evidence;
        RunReport report = artifacts.Report;

        Guid correlationId = input.RunCorrelationId != Guid.Empty
            ? input.RunCorrelationId
            : input.WorkspaceContextId != Guid.Empty
            ? input.WorkspaceContextId
            : input.WorkspaceContext.Id;
        List<IDomainEvent> events = domainEventRecorder?.GetRecordedEvents(correlationId)
            .Select(item => item.DomainEvent)
            .ToList() ?? [];

        AddAggregateEvent(events, input.WorkspaceContext.DomainEvents, correlationId, "WorkspacePrepared");
        AddIfMissing(events, new WorkspacePreparedDomainEvent(
            input.WorkspaceContext.Id,
            correlationId,
            input.WorkspaceContext.SolutionPath,
            input.WorkspaceContext.Projects.Count,
            input.WorkspaceContext.Documents.Count));

        if (input.AnalysisSnapshotId.HasValue)
        {
            AddIfMissing(events, new AnalysisSnapshotBuiltDomainEvent(
                input.AnalysisSnapshotId.Value,
                correlationId,
                input.TargetName,
                input.MaxDepth));
            AddIfMissing(events, new ProgramFactPublishedDomainEvent(
                input.AnalysisSnapshotId.Value,
                correlationId,
                input.TargetName,
                Math.Max(input.PropagationTraceCount, input.PropagationTargets.Count)));
        }

        if (input.RuleTargetId != Guid.Empty)
        {
            AddIfMissing(events, new RuleTargetIdentifiedDomainEvent(
                input.RuleTargetId,
                correlationId,
                input.RuleCode,
                input.TargetName));
        }

        AddIfMissing(events, new ChangeCandidateGeneratedDomainEvent(
            input.CandidateId,
            correlationId,
            input.TargetName,
            input.ReasonCount));

        if (input.PropagationTraceCount > 0 || input.PropagationTargets.Count > 0)
        {
            AddIfMissing(events, new ImpactRangeDetectedDomainEvent(
                input.CandidateId,
                correlationId,
                input.TargetName,
                Math.Max(input.PropagationTraceCount, input.PropagationTargets.Count)));
        }

        if (input.PlanAction == PlanAction.ExtractRuntimeClosure)
        {
            AddIfMissing(events, new RuntimeClosureBoundaryDetectedDomainEvent(
                input.CandidateId,
                correlationId,
                input.TargetName));
        }

        if (input.PlanAction == PlanAction.GenerateShadowClass)
        {
            AddIfMissing(events, new ShadowBoundaryDetectedDomainEvent(
                input.CandidateId,
                correlationId,
                input.TargetName));
        }

        AddAggregateEvent(events, input.Decision?.DomainEvents, correlationId, "DecisionCompleted");
        AddIfMissing(events, new DecisionCompletedDomainEvent(
            input.Decision.Id,
            correlationId,
            input.Approved,
            input.ApprovalCount,
            input.RejectionCount,
            input.ProtectionCount));

        AddAggregateEvent(events, plan.DomainEvents, correlationId, "RewritePlanCompiled");
        AddIfMissing(events, new RewritePlanCompiledDomainEvent(
            plan.Id,
            correlationId,
            plan.Metadata.PlanName,
            plan.ChangeItems.Count));

        AddAggregateEvent(events, plan.DomainEvents, correlationId, "PlanConflictDetected");
        if (plan.Conflicts.Count > 0 || input.ConflictTargetCount > 0)
        {
            AddIfMissing(events, new PlanConflictDetectedDomainEvent(
                plan.Id,
                correlationId,
                plan.Conflicts.Count > 0
                    ? plan.Conflicts.ToArray()
                    : [PlanConflict.MutuallyExclusiveAction]));
        }

        AddAggregateEvent(events, result.DomainEvents, correlationId, "ExecutionCompleted");
        AddIfMissing(events, new ExecutionCompletedDomainEvent(
            result.Id,
            correlationId,
            result.FileChanges.Count,
            result.ExecutionFailures.Count));

        AddAggregateEvent(events, evidence.DomainEvents, correlationId, "VerificationEvidenceCollected");
        AddIfMissing(events, new VerificationEvidenceCollectedDomainEvent(
            evidence.Id,
            correlationId,
            evidence.RiskSummary.LevelName,
            evidence.RiskSummary.RequiresManualReview));

        AddAggregateEvent(events, report.DomainEvents, correlationId, "RunReportGenerated");
        AddIfMissing(events, new RunReportGeneratedDomainEvent(
            report.Id,
            correlationId,
            report.AuditConclusion,
            report.ReportSummary.Highlights));

        return events;
    }

    private static void AddIfMissing(ICollection<IDomainEvent> events, IDomainEvent domainEvent)
    {
        if (events.Any(item =>
                item.AggregateId == domainEvent.AggregateId &&
                string.Equals(item.EventName, domainEvent.EventName, StringComparison.Ordinal)))
        {
            return;
        }

        events.Add(domainEvent);
    }

    private static void AddAggregateEvent(
        ICollection<IDomainEvent> events,
        IReadOnlyCollection<IDomainEvent>? aggregateEvents,
        Guid correlationId,
        string eventName)
    {
        if (aggregateEvents is null)
        {
            return;
        }

        foreach (IDomainEvent domainEvent in aggregateEvents.Where(item =>
                     item.CorrelationId == correlationId &&
                     string.Equals(item.EventName, eventName, StringComparison.Ordinal)))
        {
            AddIfMissing(events, domainEvent);
        }
    }
}
