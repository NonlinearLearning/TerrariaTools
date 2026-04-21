using Domain.Analysis;
using Domain.Analysis.Events;
using Domain.Marking;
using Domain.Marking.Events;
using Domain.Propagation;
using Domain.Propagation.Events;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Analysis.Events;
using Logic.Marking;
using Logic.Marking.Events;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Workflow.Events;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class StageDomainEventsTests
{
    [Fact]
    public void AnalysisDomainEventPublisher_records_snapshot_events_with_workspace_correlation()
    {
        InMemoryDomainEventRecorder recorder = new();
        AnalysisDomainEventPublisher publisher = new(recorder, new AnalysisEventSequenceBuilder());
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
        snapshot.AddNode(new MinimumNode("helper", "PlayerTools.Helper", CpgType.Method, new LocationRange("demo.cs", 2, 1, 2, 10)));
        snapshot.AddCall(new CpgCall("entry", "helper", CpgCallKind.Static, "PlayerTools.Helper"));

        IReadOnlyCollection<DomainEventEnvelope> envelopes = publisher.Publish(new AnalysisDomainEventPublishInput
        {
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = "PlayerTools.Entry",
            Depth = 2,
        });

        Assert.Collection(envelopes,
            item => Assert.IsType<AnalysisSnapshotBuiltDomainEvent>(item.DomainEvent),
            item => Assert.IsType<ProgramFactPublishedDomainEvent>(item.DomainEvent));
        Assert.All(envelopes, item => Assert.Equal(workspaceContext.Id, item.DomainEvent.CorrelationId));
        Assert.Equal(2, recorder.GetRecordedEvents(workspaceContext.Id).Count);
    }

    [Fact]
    public void AnalysisEventSequenceBuilder_prefers_snapshot_native_events()
    {
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        Guid correlationId = Guid.NewGuid();
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
        snapshot.Complete(correlationId);
        snapshot.PublishFacts(correlationId);

        AnalysisEventSequenceBuilder builder = new();
        IReadOnlyCollection<Domain.Common.Events.IDomainEvent> events = builder.BuildEvents(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = correlationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
        });

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events.Count(item => item.EventName == "AnalysisSnapshotBuilt"));
        Assert.Equal(1, events.Count(item => item.EventName == "ProgramFactPublished"));
    }

    [Fact]
    public void StageDomainEventPublishers_use_explicit_run_correlation_without_cross_run_mix()
    {
        InMemoryDomainEventRecorder recorder = new();
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        Guid runCorrelationId = Guid.NewGuid();
        Guid otherRunCorrelationId = Guid.NewGuid();

        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
        snapshot.AddNode(new MinimumNode("helper", "PlayerTools.Helper", CpgType.Method, new LocationRange("demo.cs", 2, 1, 2, 10)));
        snapshot.AddCall(new CpgCall("entry", "helper", CpgCallKind.Static, "PlayerTools.Helper"));

        AnalysisDomainEventPublisher analysisPublisher = new(recorder, new AnalysisEventSequenceBuilder());
        analysisPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
        });

        RuleTarget ruleTarget = RuleTarget.Create(
            snapshot.Id,
            RuleCode.Create("workflow.rule"),
            snapshot.Nodes.First(),
            CandidateReason.CallChainMatched,
            "demo");
        ChangeCandidate candidate = ChangeCandidate.Create(
            ruleTarget.Id,
            ruleTarget.RuleCode,
            ruleTarget.Node.DisplayName,
            CandidateKind.Method,
            ruleTarget.CandidateReason,
            ScenarioTag.MethodDeletion);
        candidate.AddReason(CandidateReason.DataFlowReachable);

        MarkingDomainEventPublisher markingPublisher = new(recorder, new MarkingEventSequenceBuilder());
        markingPublisher.Publish(new MarkingDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = workspaceContext.Id,
            RuleTarget = ruleTarget,
            Candidates = [candidate],
        });

        PropagationResolution resolution = new()
        {
            Candidate = candidate,
            SliceBoundary = new SliceBoundary("ClosureBoundary", SliceDirection.Bidirectional, 2, false),
            FactReferences = [new PropagationFactReference("entry", "helper", "Call")],
        };
        candidate.AddPropagationTrace(new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "调用传播", 1));

        PropagationDomainEventPublisher propagationPublisher = new(recorder, new PropagationEventSequenceBuilder());
        propagationPublisher.Publish(new PropagationDomainEventPublishInput
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = workspaceContext.Id,
            Resolution = resolution,
            PlanAction = Domain.Execution.PlanAction.ExtractRuntimeClosure,
        });

        analysisPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            RunCorrelationId = otherRunCorrelationId,
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
        });

        Assert.All(recorder.GetRecordedEvents(runCorrelationId), item => Assert.Equal(runCorrelationId, item.DomainEvent.CorrelationId));
        Assert.All(recorder.GetRecordedEvents(otherRunCorrelationId), item => Assert.Equal(otherRunCorrelationId, item.DomainEvent.CorrelationId));
        Assert.Equal(6, recorder.GetRecordedEvents(runCorrelationId).Count);
        Assert.Equal(2, recorder.GetRecordedEvents(otherRunCorrelationId).Count);
    }

    [Fact]
    public void MarkingDomainEventPublisher_records_rule_target_and_candidate_events()
    {
        InMemoryDomainEventRecorder recorder = new();
        MarkingDomainEventPublisher publisher = new(recorder, new MarkingEventSequenceBuilder());
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        RuleTarget ruleTarget = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");
        ChangeCandidate candidate = ChangeCandidate.Create(
            ruleTarget.Id,
            ruleTarget.RuleCode,
            ruleTarget.Node.DisplayName,
            CandidateKind.Method,
            ruleTarget.CandidateReason,
            ScenarioTag.MethodDeletion);
        candidate.AddReason(CandidateReason.DataFlowReachable);

        IReadOnlyCollection<DomainEventEnvelope> envelopes = publisher.Publish(new MarkingDomainEventPublishInput
        {
            WorkspaceContextId = workspaceContext.Id,
            RuleTarget = ruleTarget,
            Candidates = [candidate],
        });

        Assert.Collection(envelopes,
            item => Assert.IsType<RuleTargetIdentifiedDomainEvent>(item.DomainEvent),
            item => Assert.IsType<ChangeCandidateGeneratedDomainEvent>(item.DomainEvent));
        Assert.All(envelopes, item => Assert.Equal(workspaceContext.Id, item.DomainEvent.CorrelationId));
    }

    [Fact]
    public void MarkingEventSequenceBuilder_prefers_aggregate_native_events()
    {
        Guid correlationId = Guid.NewGuid();
        RuleTarget ruleTarget = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");
        ruleTarget.Confirm(correlationId);

        ChangeCandidate candidate = ChangeCandidate.Create(
            ruleTarget.Id,
            ruleTarget.RuleCode,
            ruleTarget.Node.DisplayName,
            CandidateKind.Method,
            ruleTarget.CandidateReason,
            ScenarioTag.MethodDeletion);
        candidate.ConfirmGenerated(correlationId);

        MarkingEventSequenceBuilder builder = new();
        IReadOnlyCollection<Domain.Common.Events.IDomainEvent> events = builder.BuildEvents(new MarkingDomainEventPublishInput
        {
            RunCorrelationId = correlationId,
            RuleTarget = ruleTarget,
            Candidates = [candidate],
        });

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events.Count(item => item.EventName == "RuleTargetIdentified"));
        Assert.Equal(1, events.Count(item => item.EventName == "ChangeCandidateGenerated"));
    }

    [Fact]
    public void PropagationDomainEventPublisher_records_impact_and_boundary_events()
    {
        InMemoryDomainEventRecorder recorder = new();
        PropagationDomainEventPublisher publisher = new(recorder, new PropagationEventSequenceBuilder());
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MinimalRuntimeClosure);
        candidate.AddPropagationTrace(new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "调用传播", 1));

        PropagationResolution resolution = new()
        {
            Candidate = candidate,
            SliceBoundary = new SliceBoundary("ClosureBoundary", SliceDirection.Bidirectional, 2, false),
            FactReferences = Array.Empty<PropagationFactReference>(),
        };

        IReadOnlyCollection<DomainEventEnvelope> envelopes = publisher.Publish(new PropagationDomainEventPublishInput
        {
            WorkspaceContextId = workspaceContext.Id,
            Resolution = resolution,
            PlanAction = Domain.Execution.PlanAction.ExtractRuntimeClosure,
        });

        Assert.Collection(envelopes,
            item => Assert.IsType<ImpactRangeDetectedDomainEvent>(item.DomainEvent),
            item => Assert.IsType<RuntimeClosureBoundaryDetectedDomainEvent>(item.DomainEvent));
        Assert.All(envelopes, item => Assert.Equal(workspaceContext.Id, item.DomainEvent.CorrelationId));
    }

    [Fact]
    public void PropagationEventSequenceBuilder_prefers_candidate_native_events()
    {
        Guid correlationId = Guid.NewGuid();
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MinimalRuntimeClosure);
        candidate.AddPropagationTrace(new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "调用传播", 1));
        candidate.DetectImpactRange(correlationId);
        candidate.DetectRuntimeClosureBoundary(correlationId);

        PropagationEventSequenceBuilder builder = new();
        IReadOnlyCollection<Domain.Common.Events.IDomainEvent> events = builder.BuildEvents(new PropagationDomainEventPublishInput
        {
            RunCorrelationId = correlationId,
            Resolution = new PropagationResolution
            {
                Candidate = candidate,
                FactReferences = Array.Empty<PropagationFactReference>(),
                SliceBoundary = new SliceBoundary("ClosureBoundary", SliceDirection.Bidirectional, 2, false),
            },
            PlanAction = Domain.Execution.PlanAction.ExtractRuntimeClosure,
        });

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events.Count(item => item.EventName == "ImpactRangeDetected"));
        Assert.Equal(1, events.Count(item => item.EventName == "RuntimeClosureBoundaryDetected"));
    }

    [Fact]
    public void WorkflowEventSequenceBuilder_reuses_existing_upstream_events_without_duplicates()
    {
        InMemoryDomainEventRecorder recorder = new();
        WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));

        AnalysisDomainEventPublisher analysisPublisher = new(recorder, new AnalysisEventSequenceBuilder());
        analysisPublisher.Publish(new AnalysisDomainEventPublishInput
        {
            WorkspaceContext = workspaceContext,
            CpgSnapshot = snapshot,
            EntrySymbol = "PlayerTools.Entry",
            Depth = 2,
        });

        WorkflowEventSequenceBuilder builder = new(recorder);
        Domain.Decision.RewriteDecision decision = Domain.Decision.RewriteDecision.Create("demo", Domain.Decision.ConfidenceLevel.High);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, Domain.Decision.ApprovalReason.PropagationBounded);
        Domain.Execution.RewritePlan plan = Domain.Execution.RewritePlan.Create(new Domain.Execution.PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));
        plan.AddChangeItem(Domain.Execution.PlanChangeItem.Create(
            candidateId,
            new Domain.Execution.PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry()", "Entry"),
            Domain.Execution.PlanAction.DeleteMethod,
            Domain.Execution.PlanReason.CandidateApproved));
        Domain.Execution.RewriteResult result = Domain.Execution.RewriteResult.Create(plan.Id);
        Domain.Output.Verification.VerificationEvidence evidence = Domain.Output.Verification.VerificationEvidence.Create(result.Id);
        evidence.UpdateRiskSummary(new Domain.Output.Verification.RiskSummary("Low", false, Array.Empty<string>()));
        Domain.Output.Audit.RunReport report = Domain.Output.Audit.RunReport.Create(workspaceContext.Id, decision.Id, plan.Id, result.Id, new Domain.Output.Audit.ReportSummary(1, 0, 0, "demo"), Domain.Output.Audit.AuditConclusion.ApprovedForExecution);

        IReadOnlyCollection<Domain.Common.Events.IDomainEvent> events = builder.BuildEvents(
            new Logic.Workflow.RewriteWorkflowAssemblyInput
            {
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
                PlanAction = Domain.Execution.PlanAction.DeleteMethod,
            }.ToEventStageInput(),
            new Logic.Workflow.RewriteWorkflowArtifacts
            {
                Plan = plan,
                Result = result,
                Evidence = evidence,
                Report = report,
            });

        Assert.Equal(1, events.Count(item => item.EventName == "AnalysisSnapshotBuilt"));
        Assert.Equal(1, events.Count(item => item.EventName == "ProgramFactPublished"));
    }
}
