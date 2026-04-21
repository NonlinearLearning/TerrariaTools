using Application.Contracts;
using Application.Contracts.Workflow;
using Application.Services;
using Domain.Analysis;
using Domain.Decision;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;
using Logic.Analysis.Events;
using Logic.Decision;
using Logic.Marking;
using Logic.Marking.Events;
using Logic.Propagation;
using Logic.Propagation.Events;
using Logic.Rules;
using Logic.Rewrite;
using Logic.Workflow;
using Logic.Workflow.Events;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewriteWorkflowAppServiceCorrelationTests
{
    [Fact]
    public async Task RewriteWorkflowAppService_uses_single_run_correlation_across_full_stage_chain()
    {
        WorkflowTestContext context = await WorkflowTestContext.CreateAsync();
        Guid runCorrelationId = Guid.NewGuid();

        var result = await context.Service.RunAsync(new RunRewriteWorkflowRequest
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = context.WorkspaceContext.Id,
            AnalysisSnapshotId = context.Snapshot.Id,
            RuleTargetId = context.RuleTarget.Id,
            RuleCode = context.RuleTarget.RuleCode.Value,
            TargetName = context.RuleTarget.Node.DisplayName,
            CandidateKind = ContractCandidateKind.Method,
            PrimaryReason = ContractCandidateReason.CallChainMatched,
            ScenarioTags = [ContractScenarioTag.MethodDeletion],
            BoundaryName = "WorkflowBoundary",
            SliceDirection = ContractSliceDirection.Bidirectional,
            MaxDepth = 2,
            PropagationTargets = ["PlayerTools.Helper"],
            ConfidenceLevel = ContractConfidenceLevel.High,
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = ContractPlanAction.DeleteMethod,
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        });

        Assert.Equal(runCorrelationId, result.RunCorrelationId);
        Assert.Equal(runCorrelationId, result.Propagation.RunCorrelationId);
        Assert.NotEmpty(result.DomainEvents);
        Assert.All(result.DomainEvents, item => Assert.Equal(runCorrelationId, item.CorrelationId));
        Assert.Contains(result.DomainEvents, item => item.EventName == "AnalysisSnapshotBuilt");
        Assert.Contains(result.DomainEvents, item => item.EventName == "ProgramFactPublished");
        Assert.Contains(result.DomainEvents, item => item.EventName == "RuleTargetIdentified");
        Assert.Contains(result.DomainEvents, item => item.EventName == "ChangeCandidateGenerated");
        Assert.Contains(result.DomainEvents, item => item.EventName == "ImpactRangeDetected");
        Assert.Contains(result.DomainEvents, item => item.EventName == "DecisionCompleted");
    }

    [Fact]
    public async Task RewriteWorkflowAppService_does_not_mix_events_between_two_runs_of_same_workspace()
    {
        WorkflowTestContext context = await WorkflowTestContext.CreateAsync();
        Guid firstRunCorrelationId = Guid.NewGuid();
        Guid secondRunCorrelationId = Guid.NewGuid();

        var first = await context.Service.RunAsync(BuildRequest(context, firstRunCorrelationId));
        var second = await context.Service.RunAsync(BuildRequest(context, secondRunCorrelationId));

        Assert.All(first.DomainEvents, item => Assert.Equal(firstRunCorrelationId, item.CorrelationId));
        Assert.All(second.DomainEvents, item => Assert.Equal(secondRunCorrelationId, item.CorrelationId));
        Assert.DoesNotContain(first.DomainEvents, item => item.CorrelationId == secondRunCorrelationId);
        Assert.DoesNotContain(second.DomainEvents, item => item.CorrelationId == firstRunCorrelationId);
        Assert.Equal(
            context.Recorder.GetRecordedEvents(firstRunCorrelationId).Select(item => item.DomainEvent.EventName),
            first.DomainEvents.Select(item => item.EventName));
        Assert.Equal(
            context.Recorder.GetRecordedEvents(secondRunCorrelationId).Select(item => item.DomainEvent.EventName),
            second.DomainEvents.Select(item => item.EventName));
    }

    [Fact]
    public async Task RewriteWorkflowAppService_uses_rule_target_code_when_request_rule_code_is_empty()
    {
        WorkflowTestContext context = await WorkflowTestContext.CreateAsync();
        RunRewriteWorkflowRequest request = BuildRequest(context, Guid.NewGuid());
        request.RuleCode = string.Empty;
        request.ProtectionRules = ["public-contract"];

        RewriteWorkflowRunDto result = await context.Service.RunAsync(request);

        Assert.Equal("workflow.rule", result.Propagation.Candidate.RuleCode);
        Assert.Single(result.DecisionResult.Protections);
    }

    private static RunRewriteWorkflowRequest BuildRequest(WorkflowTestContext context, Guid runCorrelationId)
    {
        return new RunRewriteWorkflowRequest
        {
            RunCorrelationId = runCorrelationId,
            WorkspaceContextId = context.WorkspaceContext.Id,
            AnalysisSnapshotId = context.Snapshot.Id,
            RuleTargetId = context.RuleTarget.Id,
            RuleCode = context.RuleTarget.RuleCode.Value,
            TargetName = context.RuleTarget.Node.DisplayName,
            CandidateKind = ContractCandidateKind.Method,
            PrimaryReason = ContractCandidateReason.CallChainMatched,
            ScenarioTags = [ContractScenarioTag.MethodDeletion],
            BoundaryName = "WorkflowBoundary",
            SliceDirection = ContractSliceDirection.Bidirectional,
            MaxDepth = 2,
            PropagationTargets = ["PlayerTools.Helper"],
            ConfidenceLevel = ContractConfidenceLevel.High,
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = ContractPlanAction.DeleteMethod,
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        };
    }

    private sealed class WorkflowTestContext
    {
        public required WorkspaceContext WorkspaceContext { get; init; }

        public required AnalysisCpgSnapshot Snapshot { get; init; }

        public required RuleTarget RuleTarget { get; init; }

        public required InMemoryDomainEventRecorder Recorder { get; init; }

        public required RewriteWorkflowAppService Service { get; init; }

        public static async Task<WorkflowTestContext> CreateAsync()
        {
            InMemoryWorkspaceContextRepository workspaceRepository = new();
            InMemoryAnalysisSnapshotRepository snapshotRepository = new();
            InMemoryRuleTargetRepository ruleTargetRepository = new();
            InMemoryDomainEventRecorder recorder = new();

            WorkspaceContext workspaceContext = WorkspaceContext.Create("demo.sln", "latest");
            await workspaceRepository.AddAsync(workspaceContext);

            AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(workspaceContext.Id, MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
            snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
            snapshot.AddNode(new MinimumNode("helper", "PlayerTools.Helper", CpgType.Method, new LocationRange("demo.cs", 2, 1, 2, 10)));
            snapshot.AddCall(new CpgCall("entry", "helper", CpgCallKind.Static, "PlayerTools.Helper"));
            await snapshotRepository.AddCpgSnapshotAsync(snapshot);

            RuleTarget ruleTarget = RuleTarget.Create(
                snapshot.Id,
                RuleCode.Create("workflow.rule"),
                snapshot.Nodes.First(),
                CandidateReason.CallChainMatched,
                "demo");
            await ruleTargetRepository.AddAsync(ruleTarget);

            AnalysisDomainEventPublisher analysisPublisher = new(recorder, new AnalysisEventSequenceBuilder());
            MarkingDomainEventPublisher markingPublisher = new(recorder, new MarkingEventSequenceBuilder());
            PropagationDomainEventPublisher propagationPublisher = new(recorder, new PropagationEventSequenceBuilder());
            RuleCatalog ruleCatalog = new();
            EnabledRuleFactory enabledRuleFactory = new(ruleCatalog);
            RuleTargetCandidateBuilder ruleTargetCandidateBuilder = new(
                new ChangeCandidateMarker(),
                enabledRuleFactory,
                ruleCatalog);
            RewriteWorkflowMarkingPreparer rewriteWorkflowMarkingPreparer = new(
                new RewriteWorkflowRulePreset(),
                ruleTargetCandidateBuilder);
            RewriteWorkflowArtifactAssembler assembler = new(
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

            RewriteWorkflowAppService service = new(
                workspaceRepository,
                snapshotRepository,
                ruleTargetRepository,
                rewriteWorkflowMarkingPreparer,
                new RewriteWorkflowRulePreset(),
                analysisPublisher,
                markingPublisher,
                propagationPublisher,
                new RewriteWorkflowPropagationStage(new ImpactPropagator()),
                new RewriteWorkflowDecisionStage(new RewriteDecisionAssessmentBuilder(), new RewriteDecisionMaker()),
                assembler,
                recorder);

            return new WorkflowTestContext
            {
                WorkspaceContext = workspaceContext,
                Snapshot = snapshot,
                RuleTarget = ruleTarget,
                Recorder = recorder,
                Service = service,
            };
        }
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
