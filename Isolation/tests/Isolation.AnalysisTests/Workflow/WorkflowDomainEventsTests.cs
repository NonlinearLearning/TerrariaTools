using Domain.Analysis;
using Domain.Decision;
using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Domain.Workspaces;
using Infrastructure.Roslyn;
using Logic.Analysis.Events;
using Logic.Rewrite;
using Logic.Workflow;
using Logic.Workflow.Events;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class WorkflowDomainEventsTests
{
    [Fact]
    public void WorkflowEventSequenceBuilder_buildsOrderedSuccessPath()
    {
        RewriteWorkflowArtifacts artifacts = BuildArtifacts(PlanAction.DeleteMethod);
        string[] names = artifacts.DomainEvents.Select(item => item.DomainEvent.EventName).ToArray();

        Assert.Contains("WorkspacePrepared", names);
        Assert.Contains("RuleTargetIdentified", names);
        Assert.Contains("ChangeCandidateGenerated", names);
        Assert.Contains("DecisionCompleted", names);
        Assert.Contains("RewritePlanCompiled", names);
        Assert.Contains("ExecutionCompleted", names);
        Assert.Contains("VerificationEvidenceCollected", names);
        Assert.Contains("RunReportGenerated", names);

        Assert.True(IndexOf(names, "RewritePlanCompiled") < IndexOf(names, "ExecutionCompleted"));
        Assert.True(IndexOf(names, "ExecutionCompleted") < IndexOf(names, "VerificationEvidenceCollected"));
        Assert.True(IndexOf(names, "VerificationEvidenceCollected") < IndexOf(names, "RunReportGenerated"));
    }

    [Fact]
    public void WorkflowEventSequenceBuilder_prefers_run_correlation_and_reuses_only_current_run_events()
    {
        InMemoryDomainEventRecorder recorder = new();
        WorkflowEventSequenceBuilder builder = new(recorder);
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        Guid runCorrelationId = Guid.NewGuid();
        Guid otherRunCorrelationId = Guid.NewGuid();
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new Domain.Analysis.MinimumNode("entry", "PlayerTools.Entry", Domain.Analysis.CpgType.Method, new Domain.Analysis.LocationRange("demo.cs", 1, 1, 1, 10)));

        AnalysisDomainEventPublisher analysisPublisher = new(recorder, new AnalysisEventSequenceBuilder());
        analysisPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
        });
        analysisPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = otherRunCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
        });

        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, ApprovalReason.PropagationBounded);
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));
        plan.AddChangeItem(PlanChangeItem.Create(
            candidateId,
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry()", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved));
        RewriteResult result = RewriteResult.Create(plan.Id);
        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.UpdateRiskSummary(new RiskSummary("Low", false, Array.Empty<string>()));
        RunReport report = RunReport.Create(workspaceContext.Id, decision.Id, plan.Id, result.Id, new ReportSummary(1, 0, 0, "demo"), AuditConclusion.ApprovedForExecution);

        IReadOnlyCollection<Domain.Common.Events.IDomainEvent> events = builder.BuildEvents(
            new RewriteWorkflowAssemblyInput
            {
                RunCorrelationId = runCorrelationId,
                WorkspaceContext = workspaceContext,
                WorkspaceContextId = workspaceContext.Id,
                AnalysisSnapshotId = snapshot.Id,
                RuleTargetId = Guid.NewGuid(),
                RuleCode = "workflow.rule",
                CandidateId = candidateId,
                DecisionId = decision.Id,
                Decision = decision,
                Approved = true,
                ApprovalCount = 1,
                TargetName = "PlayerTools.Entry",
                DocumentPath = "demo.cs",
                PlanAction = PlanAction.DeleteMethod,
            }.ToEventStageInput(),
            new RewriteWorkflowArtifacts
            {
                Plan = plan,
                Result = result,
                Evidence = evidence,
                Report = report,
            });

        Assert.All(events, item => Assert.Equal(runCorrelationId, item.CorrelationId));
        Assert.Equal(1, events.Count(item => item.EventName == "AnalysisSnapshotBuilt"));
        Assert.Equal(1, events.Count(item => item.EventName == "ProgramFactPublished"));
    }

    [Fact]
    public void WorkflowEventSequenceBuilder_emitsPlanConflictWhenDecisionHasConflicts()
    {
        RewriteDecision decision = RewriteDecision.Create("conflicted", ConfidenceLevel.Medium);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, ApprovalReason.PropagationBounded);
        decision.AddConflict(new DecisionConflict(candidateId, Guid.NewGuid(), "overlap"));

        RewriteWorkflowArtifactAssembler assembler = CreateAssembler();
        RewriteWorkflowArtifacts artifacts = assembler.Assemble(new RewriteWorkflowAssemblyInput
        {
            WorkspaceContext = WorkspaceContext.Create("demo.sln", "latest"),
            WorkspaceContextId = Guid.NewGuid(),
            AnalysisSnapshotId = Guid.NewGuid(),
            RuleTargetId = Guid.NewGuid(),
            RuleCode = "workflow.rule",
            CandidateId = candidateId,
            DecisionId = decision.Id,
            Decision = decision,
            Approved = true,
            ApprovalCount = 1,
            ConflictTargetCount = 1,
            PropagationTraceCount = 1,
            ReasonCount = 1,
            TargetName = "PlayerTools.Helper",
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = PlanAction.DeleteMethod,
            PropagationTargets = ["PlayerTools.Helper"],
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        });

        Assert.Contains(artifacts.DomainEvents, item => item.DomainEvent.EventName == "PlanConflictDetected");
        Assert.Single(artifacts.Result.ExecutionFailures);
        Assert.Equal(AuditConclusion.RequiresManualReview, artifacts.Report.AuditConclusion);
    }

    [Fact]
    public void DomainEventRecorder_assignsStableSequenceNumbers()
    {
        RewriteWorkflowArtifacts artifacts = BuildArtifacts(PlanAction.DeleteMethod);

        Assert.Equal(artifacts.DomainEvents.Count, artifacts.DomainEvents.Select(item => item.Sequence).Distinct().Count());
        Assert.All(artifacts.DomainEvents, envelope =>
        {
            Assert.True(envelope.Sequence > 0);
            Assert.NotEqual(Guid.Empty, envelope.DomainEvent.EventId);
            Assert.False(string.IsNullOrWhiteSpace(envelope.DomainEvent.EventName));
            Assert.False(string.IsNullOrWhiteSpace(envelope.DomainEvent.ContextName));
            Assert.False(string.IsNullOrWhiteSpace(envelope.DomainEvent.Summary));
        });
    }

    [Fact]
    public void DomainEvents_doNotLeakRoslynOrApplicationContracts()
    {
        Type[] eventTypes =
        [
            typeof(Domain.Common.Events.IDomainEvent),
            typeof(Domain.Workspaces.Events.WorkspacePreparedDomainEvent),
            typeof(Domain.Analysis.Events.AnalysisSnapshotBuiltDomainEvent),
            typeof(Domain.Marking.Events.RuleTargetIdentifiedDomainEvent),
            typeof(Domain.Propagation.Events.ChangeCandidateGeneratedDomainEvent),
            typeof(Domain.Decision.Events.DecisionCompletedDomainEvent),
            typeof(Domain.Execution.Events.RewritePlanCompiledDomainEvent),
            typeof(Domain.Output.Verification.Events.VerificationEvidenceCollectedDomainEvent),
            typeof(Domain.Output.Audit.Events.RunReportGeneratedDomainEvent),
        ];

        Assert.All(eventTypes, type =>
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.PropertyType.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.PropertyType.Namespace?.StartsWith("Application.Contracts", StringComparison.Ordinal) == true);
        });
    }

    private static RewriteWorkflowArtifacts BuildArtifacts(PlanAction planAction)
    {
        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, ApprovalReason.PropagationBounded);

        return CreateAssembler().Assemble(new RewriteWorkflowAssemblyInput
        {
            WorkspaceContext = WorkspaceContext.Create("demo.sln", "latest"),
            WorkspaceContextId = Guid.NewGuid(),
            AnalysisSnapshotId = Guid.NewGuid(),
            RuleTargetId = Guid.NewGuid(),
            RuleCode = "workflow.rule",
            CandidateId = candidateId,
            DecisionId = decision.Id,
            Decision = decision,
            Approved = true,
            ApprovalCount = 1,
            PropagationTraceCount = 1,
            ReasonCount = 2,
            TargetName = "PlayerTools.Helper",
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = planAction,
            PropagationTargets = ["PlayerTools.Helper"],
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        });
    }

    private static RewriteWorkflowArtifactAssembler CreateAssembler()
    {
        InMemoryDomainEventRecorder recorder = new();
        return new RewriteWorkflowArtifactAssembler(
            new RewriteWorkflowPlanStage(new RewritePlanCompiler()),
            new RewriteWorkflowExecutionStage(new RewritePlanExecutor(new RoslynCodeIsolationFacade(new RoslynCodeIsolationGateway()))),
            new RewriteWorkflowEvidenceStage(
                new CompilationEvidenceCollector(),
                new StaticReasoningEvidenceCollector(),
                new BehaviorEvidenceCollector()),
            new RewriteWorkflowReportStage(new RunReportAssembler()),
            new RewriteWorkflowEventStage(
                recorder,
                new WorkflowEventSequenceBuilder(recorder)));
    }

    private static int IndexOf(IReadOnlyList<string> names, string expected)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], expected, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private const string DemoSource = """
using System;

public class PlayerTools
{
    public int Entry(int offset)
    {
        return Helper(offset);
    }

    public int Helper(int value)
    {
        return value + 1;
    }
}
""";
}
