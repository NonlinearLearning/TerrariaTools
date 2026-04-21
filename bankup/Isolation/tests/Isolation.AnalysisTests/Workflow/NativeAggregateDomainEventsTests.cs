using Domain.Common.Events;
using Domain.Decision;
using Domain.Decision.Events;
using Domain.Execution;
using Domain.Execution.Events;
using Domain.Output.Audit.Events;
using Domain.Output.Verification;
using Domain.Output.Verification.Events;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Workflow.Events;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class NativeAggregateDomainEventsTests
{
    [Fact]
    public void RewriteDecision_complete_rejects_public_surface_and_records_native_event()
    {
        RewriteDecision decision = RewriteDecision.Create("player-tools", ConfidenceLevel.Medium);
        Guid candidateId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();
        RewriteDecisionResolutionPolicy policy = new();

        RewriteDecisionOutcome outcome = policy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = candidateId,
            ContractExposure = ContractExposure.PublicSurface("api"),
            ExternalCallerPresence = ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Verified("ok"),
            RiskScore = DecisionRiskScore.Low("safe"),
        });
        bool approved = decision.ApplyOutcome(outcome, correlationId);

        Assert.False(approved);
        Assert.Equal(RejectionReason.ExternalContractDetected, decision.Rejections[candidateId]);
        DecisionCompletedDomainEvent domainEvent = Assert.IsType<DecisionCompletedDomainEvent>(Assert.Single(decision.DomainEvents));
        Assert.Equal(correlationId, domainEvent.CorrelationId);
        Assert.False(domainEvent.Approved);
        Assert.Equal(1, domainEvent.RejectionCount);
    }

    [Fact]
    public void RewriteDecision_complete_approves_candidate_with_native_event_and_confidence_revision()
    {
        RewriteDecision decision = RewriteDecision.Create("player-tools", ConfidenceLevel.Low);
        Guid candidateId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();
        RewriteDecisionResolutionPolicy policy = new();

        RewriteDecisionOutcome guardedOutcome = policy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = candidateId,
            ContractExposure = ContractExposure.InternalOnly("logic"),
            ExternalCallerPresence = ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Verified("ok"),
            RiskScore = DecisionRiskScore.Low("safe"),
            ProtectionRules = [RuleCode.Create("decision.protect")],
            ConflictTargets = ["PlayerTools.Helper"],
        });
        bool approved = decision.ApplyOutcome(guardedOutcome, correlationId);

        Assert.False(approved);
        Assert.Equal(RejectionReason.ManualReviewRequired, decision.Rejections[candidateId]);
        Assert.Single(decision.Protections);
        Assert.Single(decision.Conflicts);

        decision.ClearDomainEvents();
        RewriteDecisionOutcome approvedOutcome = policy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = candidateId,
            ContractExposure = ContractExposure.InternalOnly("logic"),
            ExternalCallerPresence = ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Verified("ok"),
            RiskScore = DecisionRiskScore.Low("safe"),
        });
        approved = decision.ApplyOutcome(approvedOutcome, correlationId);

        Assert.True(approved);
        Assert.Equal(ApprovalReason.PropagationBounded, decision.Approvals[candidateId]);
        DecisionCompletedDomainEvent domainEvent = Assert.IsType<DecisionCompletedDomainEvent>(Assert.Single(decision.DomainEvents));
        Assert.True(domainEvent.Approved);
        Assert.Equal(1, domainEvent.ApprovalCount);
    }

    [Fact]
    public void RewritePlan_and_verificationEvidence_record_native_domain_events()
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));
        plan.RegisterChange(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry()", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);

        Guid correlationId = Guid.NewGuid();
        plan.AddConflict(PlanConflict.ParentCoverage, correlationId);
        plan.RecordCompiled(correlationId);

        Assert.Contains(plan.DomainEvents, item => item is RewritePlanCompiledDomainEvent compiled && compiled.CorrelationId == correlationId);
        Assert.Contains(plan.DomainEvents, item => item is PlanConflictDetectedDomainEvent conflict && conflict.CorrelationId == correlationId);

        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.Collect(correlationId);

        VerificationEvidenceCollectedDomainEvent evidenceEvent = Assert.IsType<VerificationEvidenceCollectedDomainEvent>(Assert.Single(evidence.DomainEvents));
        Assert.Equal("Low", evidenceEvent.RiskLevel);
        Assert.False(evidenceEvent.RequiresManualReview);
    }

    [Fact]
    public void WorkflowEventSequenceBuilder_reuses_native_aggregate_events_before_fallback_generation()
    {
        WorkspaceContext workspace = WorkspaceContext.Create("demo.sln", "latest");
        Guid correlationId = Guid.NewGuid();
        Guid candidateId = Guid.NewGuid();
        RewriteDecisionResolutionPolicy policy = new();

        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        RewriteDecisionOutcome outcome = policy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = candidateId,
            ContractExposure = ContractExposure.InternalOnly("logic"),
            ExternalCallerPresence = ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Verified("ok"),
            RiskScore = DecisionRiskScore.Low("safe"),
        });
        Assert.True(decision.ApplyOutcome(outcome, correlationId));

        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));
        plan.RegisterChange(
            candidateId,
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry()", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);
        plan.RecordCompiled(correlationId);

        RewriteResult result = RewriteResult.Create(plan.Id);
        result.StartExecution(correlationId);
        result.AddExecutionTrace(new ExecutionTrace(candidateId, "Workflow", "执行完成", DateTimeOffset.UtcNow));
        result.CompleteExecution(correlationId);
        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.Collect(correlationId);
        Domain.Output.Audit.RunReport report = Domain.Output.Audit.RunReport.Create(
            workspace.Id,
            decision.Id,
            plan.Id,
            result.Id,
            new Domain.Output.Audit.ReportSummary(1, 0, 0, "demo"),
            Domain.Output.Audit.AuditConclusion.ApprovedForExecution);
        report.AttachVerificationEvidence(evidence.Id);
        report.Finalize(correlationId);

        WorkflowEventSequenceBuilder builder = new(new InMemoryDomainEventRecorder());
        IReadOnlyCollection<IDomainEvent> events = builder.BuildEvents(
            new Logic.Workflow.RewriteWorkflowAssemblyInput
            {
                RunCorrelationId = correlationId,
                WorkspaceContext = workspace,
                WorkspaceContextId = workspace.Id,
                CandidateId = candidateId,
                Decision = decision,
                DecisionId = decision.Id,
                Approved = true,
                ApprovalCount = 1,
                TargetName = "PlayerTools.Entry",
                DocumentPath = "demo.cs",
                RuleCode = "workflow.rule",
                RuleTargetId = Guid.NewGuid(),
                PlanAction = PlanAction.DeleteMethod,
            }.ToEventStageInput(),
            new Logic.Workflow.RewriteWorkflowArtifacts
            {
                Plan = plan,
                Result = result,
                Evidence = evidence,
                Report = report,
            });

        Assert.Equal(1, events.Count(item => item.EventName == "DecisionCompleted"));
        Assert.Equal(1, events.Count(item => item.EventName == "RewritePlanCompiled"));
        Assert.Equal(1, events.Count(item => item.EventName == "VerificationEvidenceCollected"));
        Assert.Equal(1, events.Count(item => item.EventName == "RunReportGenerated"));
    }

    [Fact]
    public void RunReport_finalize_records_native_domain_event()
    {
        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        Domain.Output.Audit.RunReport report = Domain.Output.Audit.RunReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Domain.Output.Audit.ReportSummary(1, 0, 0, "demo"),
            Domain.Output.Audit.AuditConclusion.ApprovedForExecution);
        report.AttachVerificationEvidence(evidence.Id);
        Guid correlationId = Guid.NewGuid();

        report.Finalize(correlationId);

        RunReportGeneratedDomainEvent domainEvent =
            Assert.IsType<RunReportGeneratedDomainEvent>(Assert.Single(report.DomainEvents));
        Assert.Equal(correlationId, domainEvent.CorrelationId);
    }
}
