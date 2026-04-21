using Application.Contracts;
using Application.Contracts.Workspaces;
using Application.Services;
using Domain.Analysis;
using Domain.Decision;
using Domain.Execution;
using Domain.Marking;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Decision;
using Logic.Marking;
using Logic.Propagation;
using Logic.Rules;
using Logic.Rewrite;
using Logic.Workflow;
using Logic.Workflow.Events;
using Logic.Workspaces;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;
using System.Reflection;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class DddP1WorkflowTests
{
    [Fact]
    public void WorkspaceContextBuilder_buildsInputSemantics()
    {
        WorkspaceContextBuilder builder = new(new WorkspaceDefaultRulePreset(), CreateWorkspaceRuleDefaultsBuilder());

        WorkspaceContext context = builder.Build(new WorkspaceContextBuildInput
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = RunMode.FullWorkflow,
            RuleInputs =
            [
                new WorkspaceEnabledRuleInput
                {
                    RuleCode = "workflow.rule",
                    DisplayName = "workflow.rule",
                },
            ],
            InputDescriptor = InputDescriptor.Create(
                InputOrigin.Solution,
                "demo.sln",
                RunMode.FullWorkflow,
                CreateRuleSet("workflow.rule")),
        });

        Assert.Equal(RunMode.FullWorkflow, context.RunMode);
        Assert.Equal(InputOrigin.Solution, context.InputDescriptor.Origin);
        Assert.Single(context.RuleSet.EnabledRules);
    }

    [Fact]
    public void WorkspaceRuleDefaultsBuilder_builds_default_rule_semantics()
    {
        WorkspaceRuleDefaultsBuilder builder = CreateWorkspaceRuleDefaultsBuilder();

        IReadOnlyCollection<EnabledRule> rules = builder.Build(
            [
                new WorkspaceEnabledRuleInput
                {
                    RuleCode = "workflow.rule",
                    DisplayName = "Workflow Rule",
                },
            ]);

        EnabledRule rule = Assert.Single(rules);
        Assert.Equal("workflow.rule", rule.RuleCode.Value);
        Assert.Equal("Workflow Rule", rule.DisplayName);
        Assert.Equal(RuleBoundary.CurrentWorkspace, rule.RuleScope.Boundary);
        Assert.Equal(RulePropagationAllowance.CallPropagation, rule.RuleScope.PropagationAllowance);
        Assert.Equal(RuleParticipationMode.Candidate, rule.RuleExecutionPolicy.ParticipationMode);
    }

    [Fact]
    public async Task WorkspaceContextAppService_creates_workspace_with_logic_built_default_rules()
    {
        WorkspaceContextAppService service = new(
            new WorkspaceContextBuilder(new WorkspaceDefaultRulePreset(), CreateWorkspaceRuleDefaultsBuilder()),
            new InMemoryWorkspaceContextRepository());

        WorkspaceContextDto workspace = await service.CreateAsync(new CreateWorkspaceContextRequest
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = ContractRunMode.FullWorkflow,
            RuleSet = new RuleSetDto
            {
                Name = "default",
                EnabledRules =
                [
                    new EnabledRuleDto
                    {
                        RuleCode = "workflow.rule",
                        DisplayName = "Workflow Rule",
                    },
                ],
            },
        });

        EnabledRuleDto rule = Assert.Single(workspace.RuleSet.EnabledRules);
        Assert.Equal("workflow.rule", rule.RuleCode);
        Assert.Equal("Workflow Rule", rule.DisplayName);
    }

    [Fact]
    public async Task WorkspaceContextAppService_creates_workspace_without_direct_default_builder_dependency()
    {
        WorkspaceContextAppService service = new(
            new WorkspaceContextBuilder(new WorkspaceDefaultRulePreset(), CreateWorkspaceRuleDefaultsBuilder()),
            new InMemoryWorkspaceContextRepository());

        WorkspaceContextDto workspace = await service.CreateAsync(new CreateWorkspaceContextRequest
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = ContractRunMode.FullWorkflow,
            RuleSet = new RuleSetDto
            {
                Name = "default",
                EnabledRules =
                [
                    new EnabledRuleDto
                    {
                        RuleCode = "workflow.rule",
                    },
                ],
            },
        });

        EnabledRuleDto rule = Assert.Single(workspace.RuleSet.EnabledRules);
        Assert.Equal("workflow.rule", rule.RuleCode);
        Assert.Equal("workflow.rule", rule.DisplayName);
    }

    [Fact]
    public void MarkingRulePreset_resolves_registered_rule_code()
    {
        MarkingRulePreset preset = new();

        RuleCode ruleCode = preset.ResolveRuleCode("workflow.rule");

        Assert.Equal("workflow.rule", ruleCode.Value);
        Assert.Contains(ruleCode, preset.GetRuleCodes());
    }

    [Fact]
    public void MarkingRulePreset_rejects_rule_outside_marking_preset()
    {
        MarkingRulePreset preset = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => preset.ResolveRuleCode("propagation.rule"));

        Assert.Contains("MarkingRulePreset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeCandidateMarker_createsCandidateFromRuleTarget()
    {
        RuleTarget target = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            new MinimumNode("n1", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");

        ChangeCandidateMarker marker = new();

        RuleExecutionResult result = marker.Execute(new RuleExecutionInput
        {
            RuleSet = CreateRuleSet(target.RuleCode.Value),
            RuleTarget = target,
            CandidateKind = CandidateKind.Method,
            ScenarioTags = [ScenarioTag.MethodDeletion],
        });

        Assert.Same(target, result.RuleTarget);
        Assert.Single(result.Candidates);
        Assert.Equal("PlayerTools.Entry", result.Candidates.Single().TargetName);
    }

    [Fact]
    public void RuleTargetCandidateBuilder_buildsCandidateWithoutApplicationLayerAssembly()
    {
        RuleTarget target = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            new MinimumNode("n1", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");

        RuleTargetCandidateBuilder builder = CreateRuleTargetCandidateBuilder();

        RuleExecutionResult result = builder.Build(new RuleTargetCandidateBuildInput
        {
            RuleSetName = "workflow-marking",
            RuleTarget = target,
            ScenarioTags = [ScenarioTag.MethodDeletion],
        });

        Assert.Same(target, result.RuleTarget);
        Assert.Single(result.Candidates);
        Assert.Equal(CandidateKind.Method, result.Candidates.Single().CandidateKind);
    }

    [Fact]
    public void ImpactPropagator_usesSnapshotFactsWhenTargetsNotProvided()
    {
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(Guid.NewGuid(), MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
        snapshot.AddNode(new MinimumNode("helper", "PlayerTools.Helper", CpgType.Method, new LocationRange("demo.cs", 2, 1, 2, 10)));
        snapshot.AddCall(new CpgCall("entry", "helper", CpgCallKind.Static, "PlayerTools.Helper"));

        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);

        ImpactPropagator propagator = new();

        PropagationResolution resolution = propagator.Propagate(new PropagationBuildInput
        {
            Candidate = candidate,
            Snapshot = snapshot,
            BoundaryName = "FactDriven",
            SliceDirection = SliceDirection.Bidirectional,
            MaxDepth = 2,
        });

        Assert.Single(resolution.PropagationTraces);
        Assert.Equal("PlayerTools.Helper", resolution.PropagationTraces.Single().TargetName);
        Assert.Equal("entry", resolution.FactReferences.Single().SourceNodeId);
    }

    [Fact]
    public void RewriteDecisionMaker_usesStructuredAssessments()
    {
        RewriteDecisionMaker maker = new();

        RewriteDecisionResolution resolution = maker.Make(new RewriteDecisionBuildInput
        {
            CandidateId = Guid.NewGuid(),
            TargetName = "PlayerTools.Entry",
            ConfidenceLevel = ConfidenceLevel.High,
            ContractExposure = ContractExposure.PublicSurface("public-api"),
            ExternalCallerPresence = ExternalCallerPresence.Detected(["GameLoop"]),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Broken("missing dependency"),
            RiskScore = DecisionRiskScore.High("unsafe"),
        });

        Assert.False(resolution.Approved);
        Assert.Single(resolution.Decision.Rejections);
        Assert.Equal(RejectionReason.ExternalContractDetected, resolution.Decision.Rejections.Values.Single());
    }

    [Fact]
    public void RewriteDecisionResolutionPolicy_builds_manual_review_outcome_from_protections_and_conflicts()
    {
        RewriteDecisionResolutionPolicy policy = new();
        Guid candidateId = Guid.NewGuid();

        RewriteDecisionOutcome outcome = policy.Resolve(new RewriteDecisionResolutionInput
        {
            CandidateId = candidateId,
            ProtectionRules = [RuleCode.Create("public-contract")],
            ConflictTargets = ["PlayerTools.Helper"],
            ContractExposure = ContractExposure.InternalOnly("logic"),
            ExternalCallerPresence = ExternalCallerPresence.None(),
            ClosureIntegrityAssessment = ClosureIntegrityAssessment.Verified("ok"),
            RiskScore = DecisionRiskScore.Low("safe"),
        });

        Assert.False(outcome.Approved);
        Assert.Equal(RejectionReason.ManualReviewRequired, outcome.RejectionReason);
        Assert.Single(outcome.Protections);
        Assert.Single(outcome.Conflicts);
    }

    [Fact]
    public void RewriteDecisionAssessmentBuilder_derivesWorkflowAssessments()
    {
        RewriteDecisionAssessmentBuilder builder = new();

        RewriteDecisionAssessment assessment = builder.Build(new RewriteDecisionAssessmentBuildInput
        {
            IncludeExternalReferences = true,
            FactReferenceCount = 2,
            ExternalCallers = ["GameLoop", "NpcAi"],
            SimulateFailure = true,
        });

        Assert.True(assessment.ContractExposure.IsPublicSurface);
        Assert.True(assessment.ExternalCallerPresence.Exists);
        Assert.True(assessment.ClosureIntegrityAssessment.IsBroken);
        Assert.True(assessment.RiskScore.IsHighRisk);
    }

    [Fact]
    public void RewriteDecisionAssessmentPolicy_evaluates_workflow_facts_in_domain()
    {
        RewriteDecisionAssessmentPolicy policy = new();

        RewriteDecisionAssessment assessment = policy.Evaluate(new RewriteDecisionWorkflowFacts
        {
            IncludeExternalReferences = true,
            FactReferenceCount = 2,
            ExternalCallers = ["GameLoop", "NpcAi"],
            SimulateFailure = true,
        });

        Assert.True(assessment.ContractExposure.IsPublicSurface);
        Assert.True(assessment.ExternalCallerPresence.Exists);
        Assert.True(assessment.ClosureIntegrityAssessment.IsBroken);
        Assert.True(assessment.RiskScore.IsHighRisk);
    }

    [Fact]
    public void RewriteWorkflowPropagationStage_builds_propagation_as_stable_workflow_phase()
    {
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(Guid.NewGuid(), MinimumAnalysisTarget.Method, "PlayerTools.Entry", 2);
        snapshot.AddNode(new MinimumNode("entry", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)));
        snapshot.AddNode(new MinimumNode("helper", "PlayerTools.Helper", CpgType.Method, new LocationRange("demo.cs", 2, 1, 2, 10)));
        snapshot.AddCall(new CpgCall("entry", "helper", CpgCallKind.Static, "PlayerTools.Helper"));

        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);
        RewriteWorkflowPropagationStage stage = new(new ImpactPropagator());

        RewriteWorkflowPropagationStageResult result = stage.Propagate(new RewriteWorkflowPropagationStageInput
        {
            RuleTargetId = Guid.NewGuid(),
            RuleCode = "workflow.rule",
            TargetName = "PlayerTools.Entry",
            CandidateKind = CandidateKind.Method,
            PrimaryReason = CandidateReason.CallChainMatched,
            AdditionalReasons = [CandidateReason.DataFlowReachable],
            ScenarioTags = [ScenarioTag.MethodDeletion],
            BoundaryName = "WorkflowBoundary",
            SliceDirection = SliceDirection.Bidirectional,
            MaxDepth = 2,
            Candidate = candidate,
            Snapshot = snapshot,
        });

        Assert.Equal("PlayerTools.Entry", result.Resolution.Candidate.TargetName);
        Assert.Single(result.Resolution.PropagationTraces);
    }

    [Fact]
    public void RewriteWorkflowDecisionStage_builds_assessment_and_resolution_as_stable_workflow_phase()
    {
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);
        candidate.AddPropagationTrace(new PropagationTrace("PlayerTools.Entry", "GameLoop", "调用传播", 1));
        PropagationResolution propagation = new()
        {
            Candidate = candidate,
            SliceBoundary = new SliceBoundary("WorkflowBoundary", SliceDirection.Bidirectional, 1, false),
            FactReferences =
            [
                new PropagationFactReference("entry", "helper", "PlayerTools.Helper"),
            ],
        };
        RewriteWorkflowDecisionStage stage = new(new RewriteDecisionAssessmentBuilder(), new RewriteDecisionMaker());

        RewriteWorkflowDecisionStageResult result = stage.Decide(new RewriteWorkflowDecisionStageInput
        {
            Propagation = propagation,
            IncludeExternalReferences = true,
            SimulateFailure = true,
            ConfidenceLevel = ConfidenceLevel.High,
        });

        Assert.True(result.Assessment.ContractExposure.IsPublicSurface);
        Assert.False(result.Resolution.Approved);
        Assert.Single(result.Resolution.Decision.Rejections);
    }

    [Fact]
    public void PlanCompilerAndExecutor_buildRealExecutionResult()
    {
        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, ApprovalReason.PropagationBounded);

        RewritePlanCompiler compiler = new();
        RewritePlan plan = compiler.Compile(new RewritePlanCompilationInput
        {
            CandidateId = candidateId,
            Decision = decision,
            TargetName = "PlayerTools.Helper",
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = PlanAction.DeleteMethod,
        });

        RewritePlanExecutor executor = new(new RoslynCodeIsolationFacade(new RoslynCodeIsolationGateway()));
        RewriteResult result = executor.Execute(new RewritePlanExecutionInput
        {
            WorkspaceContext = WorkspaceContext.Create("demo.sln", "latest"),
            Plan = plan,
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        });

        Assert.Single(result.FileChanges);
        Assert.Single(result.ExecutionTraces);
        Assert.Empty(result.ExecutionFailures);
    }

    [Fact]
    public void EvidenceCollectors_buildEvidenceAndReportFromRealInputs()
    {
        RewriteResult result = RewriteResult.Create(Guid.NewGuid());
        result.StartExecution(Guid.NewGuid());
        result.AddFileChange(new FileChange(DocumentPath.Create("demo.cs"), "changed", ["PlayerTools.Entry"]));
        result.CompleteExecution(Guid.NewGuid());

        CompilationEvidenceCollector compilationCollector = new();
        StaticReasoningEvidenceCollector staticCollector = new();
        BehaviorEvidenceCollector behaviorCollector = new();
        RunReportAssembler reportAssembler = new();

        VerificationEvidence evidence = VerificationEvidence.Create(result.Id);
        evidence.AddCompilationEvidence(compilationCollector.Collect(new CompilationEvidenceCollectionInput(result, true, 0)));
        evidence.AddStaticReasoningEvidence(staticCollector.Collect(new StaticReasoningEvidenceCollectionInput("PlayerTools.Entry", ["fact-chain"])));
        evidence.AddBehaviorEvidence(behaviorCollector.Collect(new BehaviorEvidenceCollectionInput("DeleteMethod", result)));
        evidence.Collect(Guid.NewGuid());

        RunReport report = reportAssembler.Assemble(new RunReportAssemblyInput
        {
            WorkspaceContextId = Guid.NewGuid(),
            DecisionId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            Result = result,
            Evidence = evidence,
        });

        Assert.True(evidence.CompilationEvidence.Single().Success);
        Assert.Equal(AuditConclusion.ApprovedForExecution, report.AuditConclusion);
        Assert.Equal(evidence.Id, report.VerificationEvidenceId);
        Assert.Equal("Low", evidence.RiskSummary.LevelName);
    }

    [Fact]
    public void RewritePlan_rejectsDuplicateTargetAction()
    {
        RewritePlan plan = RewritePlan.Create(new PlanMetadata("demo", "1.0", DateTimeOffset.UtcNow, null));
        PlanChangeItem first = PlanChangeItem.Create(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Helper", "Helper(int)", "Helper"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);
        PlanChangeItem duplicate = PlanChangeItem.Create(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Helper", "Helper(int)", "Helper"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);

        plan.AddChangeItem(first);
        Assert.Throws<InvalidOperationException>(() => plan.AddChangeItem(duplicate));
    }

    [Fact]
    public void RunReport_disallowsReplacingEvidenceAttachment()
    {
        RunReport report = RunReport.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ReportSummary(1, 0, 0, "demo"), AuditConclusion.ApprovedForExecution);
        Guid first = Guid.NewGuid();
        report.AttachVerificationEvidence(first);

        Assert.Throws<InvalidOperationException>(() => report.AttachVerificationEvidence(Guid.NewGuid()));
    }

    [Fact]
    public void RewriteWorkflowArtifactAssembler_delegatesToConcreteCollaborators()
    {
        RewriteWorkflowArtifactAssembler assembler = new(
            new RewriteWorkflowPlanStage(new RewritePlanCompiler()),
            new RewriteWorkflowExecutionStage(new RewritePlanExecutor(new RoslynCodeIsolationFacade(new RoslynCodeIsolationGateway()))),
            new RewriteWorkflowEvidenceStage(
                new CompilationEvidenceCollector(),
                new StaticReasoningEvidenceCollector(),
                new BehaviorEvidenceCollector()),
            new RewriteWorkflowReportStage(new RunReportAssembler()),
            new RewriteWorkflowEventStage(
                new InMemoryDomainEventRecorder(),
                new WorkflowEventSequenceBuilder()));

        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        Guid candidateId = Guid.NewGuid();
        decision.Approve(candidateId, ApprovalReason.PropagationBounded);

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
            TargetName = "PlayerTools.Helper",
            DocumentPath = "demo.cs",
            MemberSignature = "Helper(int)",
            AnchorText = "Helper",
            PlanAction = PlanAction.DeleteMethod,
            PropagationTraceCount = 1,
            ReasonCount = 1,
            PropagationTargets = ["PlayerTools.Helper"],
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Helper",
            ParameterCount = 1,
        });

        Assert.Single(artifacts.Plan.ChangeItems);
        Assert.Single(artifacts.Result.FileChanges);
        Assert.Single(artifacts.Evidence.CompilationEvidence);
        Assert.True(artifacts.DomainEvents.Count >= 8);
    }

    [Fact]
    public void RewriteWorkflowArtifactAssembler_exposes_single_public_assembly_entry()
    {
        MethodInfo[] methods = typeof(IRewriteWorkflowArtifactAssembler)
            .GetMethods()
            .Where(static method => string.Equals(method.Name, nameof(IRewriteWorkflowArtifactAssembler.Assemble), StringComparison.Ordinal))
            .ToArray();

        MethodInfo method = Assert.Single(methods);
        Assert.Equal(typeof(RewriteWorkflowAssemblyInput), method.GetParameters().Single().ParameterType);
    }

    [Fact]
    public void RewriteWorkflowMarkingPreparer_uses_rewrite_workflow_rule_preset()
    {
        RuleTarget target = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            new MinimumNode("n1", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");
        RewriteWorkflowMarkingPreparer preparer = new(
            new RewriteWorkflowRulePreset(),
            CreateRuleTargetCandidateBuilder());

        IReadOnlyCollection<ChangeCandidate> candidates = preparer.Prepare(target);

        ChangeCandidate candidate = Assert.Single(candidates);
        Assert.Equal("PlayerTools.Entry", candidate.TargetName);
    }

    [Fact]
    public void RewriteWorkflowMarkingPreparer_rejects_rule_outside_rewrite_workflow_preset()
    {
        RuleTarget target = RuleTarget.Create(
            Guid.NewGuid(),
            RuleCode.Create("marking.rule-target"),
            new MinimumNode("n1", "PlayerTools.Entry", CpgType.Method, new LocationRange("demo.cs", 1, 1, 1, 10)),
            CandidateReason.CallChainMatched,
            "demo");
        RewriteWorkflowMarkingPreparer preparer = new(
            new RewriteWorkflowRulePreset(),
            CreateRuleTargetCandidateBuilder());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => preparer.Prepare(target));

        Assert.Contains("RewriteWorkflowRulePreset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteWorkflowRulePreset_normalizes_protection_rules()
    {
        RewriteWorkflowRulePreset preset = new();

        IReadOnlyCollection<string> protectionRules = preset.NormalizeProtectionRules(["public-contract", "decision.protect"]);

        Assert.Equal(2, protectionRules.Count);
        Assert.Contains("public-contract", protectionRules);
        Assert.Contains("decision.protect", protectionRules);
    }

    [Fact]
    public void PropagationRulePreset_resolves_registered_rule_code()
    {
        PropagationRulePreset preset = new();

        RuleCode ruleCode = preset.ResolveRuleCode("propagation.rule");

        Assert.Equal("propagation.rule", ruleCode.Value);
        Assert.Contains(ruleCode, preset.GetRuleCodes());
    }

    [Fact]
    public void PropagationRulePreset_rejects_rule_outside_propagation_preset()
    {
        PropagationRulePreset preset = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => preset.ResolveRuleCode("workflow.rule"));

        Assert.Contains("PropagationRulePreset", exception.Message, StringComparison.Ordinal);
    }

    private static RuleSet CreateRuleSet(string ruleCode)
    {
        RuleSet ruleSet = RuleSet.Create(
            "default",
            new RuleExecutionPolicy(
                RuleParticipationMode.Candidate,
                RuleConflictMode.PreferHigherPriority,
                RuleFailureMode.Warn,
                RuleSafetyLevel.Balanced,
                RuleEvidenceMode.AttachReason));
        ruleSet.AddRule(CreateEnabledRule(ruleCode));
        return ruleSet;
    }

    private static EnabledRule CreateEnabledRule(string ruleCode)
    {
        return new EnabledRule(
            RuleCode.Create(ruleCode),
            ruleCode,
            RulePriority.Normal,
            new RuleScope(
                [RuleTargetKind.Method],
                [RuleStageScope.Marking],
                RuleBoundary.CurrentWorkspace,
                RulePropagationAllowance.CallPropagation),
            new RuleExecutionPolicy(
                RuleParticipationMode.Candidate,
                RuleConflictMode.PreferHigherPriority,
                RuleFailureMode.Warn,
                RuleSafetyLevel.Balanced,
                RuleEvidenceMode.AttachReason));
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

    private static WorkspaceRuleDefaultsBuilder CreateWorkspaceRuleDefaultsBuilder()
    {
        return new WorkspaceRuleDefaultsBuilder(new EnabledRuleFactory(new RuleCatalog()));
    }

    private static RuleTargetCandidateBuilder CreateRuleTargetCandidateBuilder()
    {
        RuleCatalog ruleCatalog = new();
        EnabledRuleFactory enabledRuleFactory = new(ruleCatalog);
        return new RuleTargetCandidateBuilder(new ChangeCandidateMarker(), enabledRuleFactory, ruleCatalog);
    }
}
