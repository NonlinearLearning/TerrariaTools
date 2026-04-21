using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Domain.Workspaces;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class AggregateRichModelTests
{
    [Fact]
    public void RewritePlan_registersSequentialOrders_and_blocks_execution_when_conflicts_exist()
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));

        PlanChangeItem first = plan.RegisterChange(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry(int)", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);
        PlanChangeItem second = plan.RegisterChange(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Helper", "Helper(int)", "Helper"),
            PlanAction.PrivatizeMethod,
            PlanReason.LinkedActionDetected);

        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);

        plan.AddConflict(PlanConflict.ParentCoverage);

        Assert.Throws<InvalidOperationException>(() => plan.ValidateReadyForExecution());
    }

    [Fact]
    public void RewritePlan_compile_requires_non_empty_plan_items()
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));

        Assert.Throws<InvalidOperationException>(() => plan.Compile(Guid.NewGuid()));
    }

    [Fact]
    public void RewritePlan_compile_rejects_non_continuous_or_duplicated_order()
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));

        PlanChangeItem first = plan.RegisterChange(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry(int)", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);
        PlanChangeItem second = plan.RegisterChange(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Helper", "Helper(int)", "Helper"),
            PlanAction.PrivatizeMethod,
            PlanReason.LinkedActionDetected);
        plan.OrderChangeItem(first.Id, 1);
        plan.OrderChangeItem(second.Id, 1);

        Assert.Throws<InvalidOperationException>(() => plan.Compile(Guid.NewGuid()));
    }

    [Fact]
    public void VerificationEvidence_refreshesRiskSummary_from_added_evidence()
    {
        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());

        Assert.Equal("未评估", evidence.RiskSummary.LevelName);

        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        Assert.Equal("Low", evidence.RiskSummary.LevelName);
        Assert.False(evidence.RiskSummary.RequiresManualReview);

        evidence.AddBehaviorEvidence(new BehaviorEvidence("DeleteMethod", false, "行为验证失败"));

        Assert.Equal("High", evidence.RiskSummary.LevelName);
        Assert.True(evidence.RiskSummary.RequiresManualReview);
        Assert.Contains("行为验证失败。", evidence.RiskSummary.Items);
    }

    [Fact]
    public void RewriteResult_requires_open_execution_before_recording_changes_and_emits_completion_event()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
            result.AddFileChange(new FileChange(DocumentPath.Create("demo.cs"), "changed", ["PlayerTools.Helper"])));

        result.StartExecution(correlationId);
        result.MarkFileChanged(DocumentPath.Create("demo.cs"), "changed", ["PlayerTools.Helper"]);
        result.AddExecutionTrace(new ExecutionTrace(Guid.NewGuid(), "PlanExecutor", "执行成功", DateTimeOffset.UtcNow));
        result.CompleteExecution(correlationId);

        Assert.Single(result.FileChanges);
        Assert.Single(result.ExecutionTraces);
        Assert.Contains(result.DomainEvents, item => item.EventName == "ExecutionCompleted" && item.CorrelationId == correlationId);
    }

    [Fact]
    public void RewriteResult_complete_requires_observed_execution_outcome()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();
        result.StartExecution(correlationId);

        Assert.Throws<InvalidOperationException>(() => result.CompleteExecution(correlationId));
    }

    [Fact]
    public void VerificationEvidence_collect_locks_follow_up_mutation()
    {
        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();

        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.Collect(correlationId);

        Assert.True(evidence.IsCollected);
        Assert.Throws<InvalidOperationException>(() =>
            evidence.AddBehaviorEvidence(new BehaviorEvidence("DeleteMethod", true, "不应再追加")));
    }

    [Fact]
    public void VerificationEvidence_collect_requires_at_least_one_recorded_evidence()
    {
        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => evidence.Collect(Guid.NewGuid()));
    }

    [Fact]
    public void RunReport_createFromExecutionOutcome_uses_evidence_risk_to_drive_conclusion()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        result.StartExecution(Guid.NewGuid());
        result.AddFileChange(new FileChange(DocumentPath.Create("demo.cs"), "changed", ["PlayerTools.Helper"]));
        result.CompleteExecution(Guid.NewGuid());

        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.AddBehaviorEvidence(new BehaviorEvidence("DeleteMethod", false, "行为验证失败"));

        RunReport report = RunReport.CreateFromExecutionOutcome(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            result,
            evidence);

        Assert.Equal(AuditConclusion.RequiresManualReview, report.AuditConclusion);
        Assert.Equal(evidence.Id, report.VerificationEvidenceId);
        Assert.Contains("行为验证失败。", report.ReportSummary.Highlights);
    }

    [Fact]
    public void RunReport_recalculate_and_finalize_records_native_event()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();
        result.StartExecution(correlationId);
        result.FailExecution(Guid.NewGuid(), "PlanConflict", "需要人工处理。", true);
        result.CompleteExecution(correlationId);

        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.AddBehaviorEvidence(new BehaviorEvidence("DeleteMethod", false, "行为验证失败"));

        RunReport report = RunReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            result.Id,
            new ReportSummary(0, 0, 0, "placeholder"),
            AuditConclusion.ReferenceOnly);

        report.RecalculateFromEvidence(result, evidence);
        report.Finalize(correlationId);

        Assert.Equal(AuditConclusion.RequiresManualReview, report.AuditConclusion);
        Assert.Equal(evidence.Id, report.VerificationEvidenceId);
        Assert.Contains(report.DomainEvents, item => item.EventName == "RunReportGenerated" && item.CorrelationId == correlationId);
    }

    [Fact]
    public void RunReport_finalize_requires_attached_verification_evidence()
    {
        RunReport report = RunReport.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ReportSummary(1, 0, 0, "demo"),
            AuditConclusion.ApprovedForExecution);

        Assert.Throws<InvalidOperationException>(() => report.Finalize(Guid.NewGuid()));
    }

    [Fact]
    public void RunReport_recalculate_blocks_mutation_after_finalize()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        Guid correlationId = Guid.NewGuid();
        result.StartExecution(correlationId);
        result.MarkFileChanged(DocumentPath.Create("demo.cs"), "changed", ["PlayerTools.Helper"]);
        result.CompleteExecution(correlationId);

        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));

        RunReport report = RunReport.CreateFromExecutionOutcome(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            result,
            evidence);
        report.Finalize(correlationId);

        Assert.Throws<InvalidOperationException>(() => report.RecalculateFromEvidence(result, evidence));
    }
}
