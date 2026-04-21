using Application.Contracts.Analysis;
using Application.Contracts;
using Application.Contracts.Decision;
using Application.Contracts.Execution;
using Application.Contracts.Marking;
using Application.Contracts.Output.Audit;
using Application.Contracts.Output.Verification;
using Application.Contracts.Propagation;
using Application.Contracts.Rewrite;
using Application.Contracts.Rewrite.Artifacts;
using Application.Contracts.Workspaces;
using Application.Contracts.Workflow;
using Application.DependencyInjection;
using Application.Mappers;
using Application.Services;
using Domain.Analysis;
using Domain.Analysis.Engine.Core;
using Domain.Common.Events;
using Domain.Decision;
using Domain.Execution;
using Domain.Output.Audit;
using Domain.Output.Verification;
using Domain.Propagation;
using Domain.Rewrite.Artifacts;
using Domain.Marking;
using Domain.Rules;
using Domain.Workspaces;
using Infrastructure.Analysis;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;
using Logic.Analysis;
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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Logic.Workspaces;
using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using CandidateReason = Domain.Rules.CandidateReason;
using RuleSet = Domain.Rules.RuleSet;

try
{
    string repositoryRoot = Directory.GetCurrentDirectory();
    RegisterAnalysisDependencyResolver(repositoryRoot);
    CSharpCompilation applicationCompilation = BuildApplicationCompilation(repositoryRoot);
    AssertDocsAsCodeEvidenceAndAnchors(repositoryRoot);
    AssertServiceRegistrations(repositoryRoot);

    InMemoryWorkspaceContextRepository workspaceRepository = new();
RuleCatalog ruleCatalog = new();
WorkspaceContextAppService workspaceAppService = new(
    new WorkspaceContextBuilder(
        new WorkspaceDefaultRulePreset(),
        new WorkspaceRuleDefaultsBuilder(new EnabledRuleFactory(ruleCatalog))),
    workspaceRepository);
    WorkspaceContextDto workspace = await workspaceAppService.CreateAsync(new CreateWorkspaceContextRequest
    {
        SolutionPath = repositoryRoot,
        LanguageVersion = "latest",
        RunMode = ContractRunMode.FullWorkflow,
        RuleSet = new RuleSetDto
        {
            Name = "default",
            EnabledRules = new[]
            {
                new EnabledRuleDto
                {
                    RuleCode = "workflow.rule",
                    DisplayName = "Workflow Rule",
                },
            },
        },
        Documents = new[]
        {
            "docs\\DDD\\事件风暴.md",
            "docs/DDD/事件风暴.md",
            "src/Domain/Analysis/Engine/Core/CpgGraph.cs",
        },
        Projects = new[]
        {
            new ProjectItemDto
            {
                Name = "Infrastructure",
                Path = "src/Infrastructure/Infrastructure.csproj",
            },
        },
    });

Assert(workspace.Documents.Count == 2, "WorkspaceContext 应该对文档路径做标准化去重。");

InMemoryAnalysisSnapshotRepository analysisSnapshotRepository = new();
InMemoryRuleTargetRepository ruleTargetRepository = new();

AnalysisAppService analysisAppService = new(
    workspaceRepository,
    analysisSnapshotRepository,
    new AnalysisSnapshotComposer(new DefaultAnalysisSnapshotBuilder()));

AnalysisCpgSnapshotDto cpgSnapshot = await analysisAppService.BuildCpgSnapshotAsync(new BuildAnalysisCpgSnapshotRequest
{
    WorkspaceContextId = workspace.Id,
    EntrySymbol = "Domain.Analysis.Engine.Core.CpgGraphBuilder.Build",
    MinimumTarget = ContractMinimumAnalysisTarget.Method,
    Depth = 2,
});

Assert(cpgSnapshot.Nodes.Count >= 2, "CPG 快照至少应生成入口节点和一个文档节点。");

AnalysisCpgAppService analysisCpgAppService = new(
    workspaceRepository,
    analysisSnapshotRepository,
    new AnalysisBackedCpgGateway(new AnalysisCpgSnapshotAssembler()),
    new AnalysisInputDescriptorBuilder());

AnalysisCpgSnapshotDto analysisBackedSnapshot = await analysisCpgAppService.BuildAnalysisBackedCpgSnapshotAsync(
    new BuildAnalysisBackedCpgSnapshotRequest
    {
        WorkspaceContextId = workspace.Id,
        SourcePath = "src/Domain/Workspaces/WorkspaceContext.cs",
        SourceKind = ContractAnalysisSourceKind.SourceFile,
        EntrySymbol = "WorkspaceContext",
        MinimumTarget = ContractMinimumAnalysisTarget.Type,
        Depth = 3,
    });
Assert(analysisBackedSnapshot.Nodes.Count > cpgSnapshot.Nodes.Count, "Analysis 适配器应产出真实 CPG 节点。");
Assert(
    analysisBackedSnapshot.Nodes.Any(item => item.DisplayName.Contains("WorkspaceContext", StringComparison.Ordinal)),
    "Analysis 适配器应把旧 Analysis 的类型节点映射到领域快照。");

RuleTargetAppService ruleTargetAppService = new(
    new RuleTargetBuilder(),
    new MarkingRulePreset(),
    ruleTargetRepository,
    new RuleTargetMarkingPreparer(CreateRuleTargetCandidateBuilder()),
    analysisSnapshotRepository,
    new MarkingDomainEventPublisher(new InMemoryDomainEventRecorder(), new MarkingEventSequenceBuilder()));
RuleTargetDto ruleTarget = await ruleTargetAppService.CreateAsync(new CreateRuleTargetRequest
{
    SnapshotId = cpgSnapshot.Id,
    RuleCode = "workflow.rule",
    CandidateReason = ContractCandidateReason.ManualReviewRequired,
    Node = new MinimumNodeDto
    {
        NodeId = "entry",
        DisplayName = "WorkspaceContext",
        DocumentPath = "docs/DDD/事件风暴.md",
        NodeType = ContractCpgNodeType.TypeDecl,
        StartLine = 1,
        StartColumn = 1,
        EndLine = 1,
        EndColumn = 16,
    },
    Note = "首批落地规则命中。",
});

Assert(ruleTarget.RuleCode == "workflow.rule", "规则命中目标应返回正确规则编号。");

CodeIsolationAppService codeIsolationAppService = new(new RoslynCodeIsolationFacade(new RoslynCodeIsolationGateway()));
const string SampleSource = """
using System;

namespace Demo;

public class PlayerTools
{
    private int seed = 7;

    public int Entry(int offset)
    {
        return Helper(offset) + seed;
    }

    public int Helper(int valse)
    {
        return valse + seed;
    }

    public string Format()
    {
        return seed.ToString();
    }
}
""";

CodeRewriteResultDto privatizedMethod = await codeIsolationAppService.PrivatizeMethodAsync(new PrivatizeMethodRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Helper",
    ParameterCount = 1,
});
Assert(privatizedMethod.SourceCode.Contains("private int Helper"), "方法私有化应将访问级别改为 private。");

CodeRewriteResultDto clearedMethod = await codeIsolationAppService.ClearMethodBodyAsync(new ClearMethodBodyRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Format",
    ParameterCount = 0,
});
Assert(clearedMethod.SourceCode.Contains("return null;"), "非 void 方法体清空后应保留可编译返回。");

CodeRewriteResultDto deletedMethod = await codeIsolationAppService.DeleteMethodAsync(new DeleteMethodRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Format",
    ParameterCount = 0,
});
Assert(!deletedMethod.SourceCode.Contains("Format()"), "方法删除后不应再保留目标方法。");

CodeRewriteResultDto deletedClass = await codeIsolationAppService.DeleteClassAsync(new DeleteClassRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
});
Assert(!deletedClass.SourceCode.Contains("class PlayerTools"), "类型删除后不应再保留目标类型。");

MemberSliceDto memberSlice = await codeIsolationAppService.BuildMemberSliceAsync(new BuildMemberSliceRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Entry",
    ParameterCount = 1,
});
Assert(memberSlice.MemberNames.Contains("Entry"), "成员切片应包含入口方法。");
Assert(memberSlice.MemberNames.Contains("Helper"), "成员切片应包含被入口方法调用的方法。");

ShadowClassDto shadowClass = await codeIsolationAppService.GenerateShadowClassAsync(new GenerateShadowClassRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Entry",
    ParameterCount = 1,
});
Assert(shadowClass.ShadowClassName == "PlayerToolsShadow", "影子类应生成约定名称。");
Assert(shadowClass.SourceCode.Contains("class PlayerToolsShadow"), "影子类源码应生成影子类型。");
Assert(shadowClass.Boundary.ReferenceMappings.Count == 0, "影子类应显式暴露传播边界载体。");

RuntimeClosureDto runtimeClosure = await codeIsolationAppService.ExtractMinimalRuntimeClosureAsync(new ExtractMinimalRuntimeClosureRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Entry",
    ParameterCount = 1,
});
Assert(runtimeClosure.ClosureClassName == "PlayerToolsRuntimeClosure", "最小运行闭包应生成闭包类型名称。");
Assert(runtimeClosure.MemberNames.Contains("Helper"), "最小运行闭包应保留入口依赖成员。");
Assert(runtimeClosure.Boundary.Root.ClassName == "PlayerTools", "最小运行闭包边界应显式暴露闭包根类型。");
Assert(runtimeClosure.Boundary.Root.MemberName == "Entry", "最小运行闭包边界应显式暴露闭包根成员。");

ChangeCandidate changeCandidate = ChangeCandidate.Create(
    ruleTarget.Id,
    ruleTarget.RuleCode,
    "PlayerTools.Entry",
    CandidateKind.Method,
    CandidateReason.CallChainMatched,
    ScenarioTag.MethodDeletion);
changeCandidate.AddReason(CandidateReason.DataFlowReachable);
changeCandidate.AddScenarioTag(ScenarioTag.MinimalRuntimeClosure);
changeCandidate.SetSliceBoundary(new SliceBoundary("EntryClosure", SliceDirection.Bidirectional, 3, false));
changeCandidate.AddPropagationTrace(new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "调用传播", 1));
Assert(changeCandidate.Reasons.Count == 2, "变更候选应能累计候选原因。");
Assert(changeCandidate.PropagationTraces.Count == 1, "变更候选应能附加传播轨迹。");

RewriteDecision rewriteDecision = RewriteDecision.Create("entry-flow", ConfidenceLevel.Medium);
rewriteDecision.Approve(changeCandidate.Id, ApprovalReason.PropagationBounded);
rewriteDecision.AddProtection(new DecisionProtection(changeCandidate.Id, "contract.protect", "外部契约需要人工确认。"));
rewriteDecision.AddConflict(new DecisionConflict(changeCandidate.Id, Guid.NewGuid(), "父子动作存在覆盖冲突。"));
Assert(rewriteDecision.Approvals.ContainsKey(changeCandidate.Id), "决策应记录批准项。");
Assert(rewriteDecision.Protections.Count == 1, "决策应记录保护项。");
Assert(rewriteDecision.Conflicts.Count == 1, "决策应记录冲突项。");

PlanMetadata planMetadata = new("entry-plan", "1.0.0", DateTimeOffset.UtcNow, "首批执行骨架");
RewritePlan rewritePlan = RewritePlan.Create(planMetadata);
PlanChangeItem planChangeItem = PlanChangeItem.Create(
    changeCandidate.Id,
    new PlanTarget(DocumentPath.Create("src/Domain/Rewrite/Artifacts/RuntimeClosure.cs"), "PlayerTools.Entry", "Entry(int)", "Entry"),
    PlanAction.DeleteMethod,
    PlanReason.CandidateApproved);
planChangeItem.AddReason(PlanReason.ClosureBoundaryRequired);
rewritePlan.AddChangeItem(planChangeItem);
rewritePlan.OrderChangeItem(planChangeItem.Id, 1);
rewritePlan.AddConflict(PlanConflict.ParentCoverage);
Assert(rewritePlan.ChangeItems.Count == 1, "改写计划应能聚合计划项。");
Assert(rewritePlan.ChangeItems.Single().Order == 1, "改写计划应支持排序。");
Assert(rewritePlan.Conflicts.Contains(PlanConflict.ParentCoverage), "改写计划应记录计划冲突。");

RewriteResult rewriteResult = RewriteResult.Create(rewritePlan.Id);
rewriteResult.StartExecution(Guid.NewGuid());
rewriteResult.AddFileChange(new FileChange(
    DocumentPath.Create("src/Domain/Rewrite/Artifacts/RuntimeClosure.cs"),
    "删除入口方法并保留闭包依赖。",
    new[] { "PlayerTools.Entry", "PlayerTools.Helper" }));
rewriteResult.AddExecutionTrace(new ExecutionTrace(planChangeItem.Id, "定位目标代码", "已定位到 Entry(int)。", DateTimeOffset.UtcNow));
rewriteResult.AddExecutionFailure(new ExecutionFailure(planChangeItem.Id, "Conflict", "存在待人工处理冲突。", true));
rewriteResult.CompleteExecution(Guid.NewGuid());
Assert(rewriteResult.FileChanges.Count == 1, "执行结果应记录文件变更。");
Assert(rewriteResult.ExecutionTraces.Count == 1, "执行结果应记录执行轨迹。");
Assert(rewriteResult.ExecutionFailures.Count == 1, "执行结果应记录执行失败。");

VerificationEvidence verificationEvidence = VerificationEvidence.Create(rewriteResult.Id);
verificationEvidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过。"));
verificationEvidence.AddStaticReasoningEvidence(new StaticReasoningEvidence("PlayerTools.Entry", "传播轨迹覆盖 Helper 调用链。"));
verificationEvidence.AddBehaviorEvidence(new BehaviorEvidence("RuntimeClosure", true, "闭包成员可完整导出。"));
verificationEvidence.UpdateRiskSummary(new RiskSummary("Medium", true, new[] { "仍需人工确认外部契约。" }));
Assert(verificationEvidence.CompilationEvidence.Count == 1, "证据应记录编译证据。");
Assert(verificationEvidence.BehaviorEvidence.Count == 1, "证据应记录行为证据。");
Assert(verificationEvidence.RiskSummary.RequiresManualReview, "风险摘要应能表达人工复核需求。");

RunReport runReport = RunReport.Create(
    workspace.Id,
    rewriteDecision.Id,
    rewritePlan.Id,
    rewriteResult.Id,
    new ReportSummary(1, 0, 1, "首批 DDD 传播/决策/执行/输出模型已落地。"),
    AuditConclusion.RequiresManualReview);
runReport.AttachVerificationEvidence(verificationEvidence.Id);
Assert(runReport.VerificationEvidenceId == verificationEvidence.Id, "运行报告应能挂接证据。");
Assert(runReport.AuditConclusion == AuditConclusion.RequiresManualReview, "运行报告应保留审计结论。");

ChangeCandidateDto changeCandidateDto = ContractMapper.Map(changeCandidate);
RewriteDecisionDto rewriteDecisionDto = ContractMapper.Map(rewriteDecision);
RewritePlanDto rewritePlanDto = ContractMapper.Map(rewritePlan);
RewriteResultDto rewriteResultDto = ContractMapper.Map(rewriteResult);
VerificationEvidenceDto verificationEvidenceDto = ContractMapper.Map(verificationEvidence);
RunReportDto runReportDto = ContractMapper.Map(runReport);
Assert(changeCandidateDto.ScenarioTags.Contains(ContractScenarioTag.MinimalRuntimeClosure), "候选 DTO 应映射场景标签。");
Assert(rewriteDecisionDto.Protections.Count == 1, "决策 DTO 应映射保护项。");
Assert(rewritePlanDto.ChangeItems.Count == 1, "计划 DTO 应映射计划项。");
Assert(rewriteResultDto.ExecutionFailures.Count == 1, "结果 DTO 应映射执行失败。");
Assert(verificationEvidenceDto.RiskSummary.RequiresManualReview, "证据 DTO 应映射风险摘要。");
Assert(runReportDto.VerificationEvidenceId == verificationEvidence.Id, "运行报告 DTO 应映射证据标识。");
Assert(runReportDto.AuditConclusion == ContractAuditConclusion.RequiresManualReview, "运行报告 DTO 应映射审计结论。");

PropagationAppService propagationAppService = new(new ImpactPropagator(), new PropagationRulePreset(), analysisSnapshotRepository);
PropagationResultDto propagationResult = await propagationAppService.PropagateAsync(new BuildPropagationRequest
{
    RuleTargetId = ruleTarget.Id,
    RuleCode = "propagation.rule",
    TargetName = "PlayerTools.Entry",
    CandidateKind = ContractCandidateKind.Method,
    PrimaryReason = ContractCandidateReason.CallChainMatched,
    AdditionalReasons = new[] { ContractCandidateReason.DataFlowReachable },
    ScenarioTags = new[] { ContractScenarioTag.MethodDeletion, ContractScenarioTag.MemberSlice },
    BoundaryName = "DirectPropagationBoundary",
    SliceDirection = ContractSliceDirection.Bidirectional,
    MaxDepth = 2,
    PropagationTargets = new[] { "PlayerTools.Helper" },
});
Assert(propagationResult.Candidate.Reasons.Count == 2, "传播服务应生成候选原因。");
Assert(propagationResult.PropagationTraces.Count == 1, "传播服务应生成传播轨迹。");
Assert(propagationResult.SliceBoundary?.BoundaryName == "DirectPropagationBoundary", "传播服务应生成切片边界。");

DecisionAppService decisionAppService = new(new RewriteDecisionMaker());
DecisionResultDto directDecisionResult = await decisionAppService.DecideAsync(new BuildRewriteDecisionRequest
{
    Candidate = propagationResult.Candidate,
    ProtectionRules = Array.Empty<string>(),
    ConflictTargets = Array.Empty<string>(),
    ConfidenceLevel = ContractConfidenceLevel.High,
    ForceReject = false,
});
Assert(directDecisionResult.Approved, "决策服务应批准无保护候选。");
Assert(directDecisionResult.Decision.Approvals.Count == 1, "决策服务应输出批准项。");

DecisionResultDto protectedDecisionResult = await decisionAppService.DecideAsync(new BuildRewriteDecisionRequest
{
    Candidate = propagationResult.Candidate,
    ProtectionRules = new[] { "public-contract" },
    ConflictTargets = new[] { "PlayerToolsShadow.Entry" },
    ConfidenceLevel = ContractConfidenceLevel.Medium,
    ForceReject = false,
});
Assert(!protectedDecisionResult.Approved, "决策服务应拒绝受保护候选。");
Assert(protectedDecisionResult.Protections.Count == 1, "决策服务应输出保护项。");
Assert(protectedDecisionResult.Conflicts.Count == 1, "决策服务应输出冲突项。");

InMemoryDomainEventRecorder workflowRecorder = new();
RewriteWorkflowAppService rewriteWorkflowAppService = new(
    workspaceRepository,
    analysisSnapshotRepository,
    ruleTargetRepository,
    new RewriteWorkflowMarkingPreparer(new RewriteWorkflowRulePreset(), CreateRuleTargetCandidateBuilder()),
    new RewriteWorkflowRulePreset(),
    new AnalysisDomainEventPublisher(workflowRecorder, new AnalysisEventSequenceBuilder()),
    new MarkingDomainEventPublisher(workflowRecorder, new MarkingEventSequenceBuilder()),
    new PropagationDomainEventPublisher(workflowRecorder, new PropagationEventSequenceBuilder()),
    new RewriteWorkflowPropagationStage(new ImpactPropagator()),
    new RewriteWorkflowDecisionStage(new RewriteDecisionAssessmentBuilder(), new RewriteDecisionMaker()),
    new RewriteWorkflowArtifactAssembler(
        new RewriteWorkflowPlanStage(new RewritePlanCompiler()),
        new RewriteWorkflowExecutionStage(new RewritePlanExecutor(new RoslynCodeIsolationFacade(new RoslynCodeIsolationGateway()))),
        new RewriteWorkflowEvidenceStage(
            new CompilationEvidenceCollector(),
            new StaticReasoningEvidenceCollector(),
            new BehaviorEvidenceCollector()),
        new RewriteWorkflowReportStage(new RunReportAssembler()),
        new RewriteWorkflowEventStage(
            workflowRecorder,
            new WorkflowEventSequenceBuilder(workflowRecorder))),
    workflowRecorder);
RewriteWorkflowRunDto workflowRun = await rewriteWorkflowAppService.RunAsync(new RunRewriteWorkflowRequest
{
    RunCorrelationId = Guid.NewGuid(),
    WorkspaceContextId = workspace.Id,
    AnalysisSnapshotId = cpgSnapshot.Id,
    RuleTargetId = ruleTarget.Id,
    RuleCode = "workflow.rule",
    TargetName = "PlayerTools.Entry",
    CandidateKind = ContractCandidateKind.Method,
    PrimaryReason = ContractCandidateReason.CallChainMatched,
    AdditionalReasons = new[] { ContractCandidateReason.DataFlowReachable },
    ScenarioTags = new[] { ContractScenarioTag.MethodDeletion, ContractScenarioTag.MinimalRuntimeClosure },
    BoundaryName = "WorkflowBoundary",
    SliceDirection = ContractSliceDirection.Bidirectional,
    MaxDepth = 2,
    IncludeExternalReferences = false,
    PropagationTargets = new[] { "PlayerTools.Helper", "PlayerTools.seed" },
    ProtectionRules = Array.Empty<string>(),
    ConflictTargets = Array.Empty<string>(),
    ConfidenceLevel = ContractConfidenceLevel.High,
    DocumentPath = "src/Infrastructure/Roslyn/RoslynCodeIsolationGateway.cs",
    MemberSignature = "Entry(int)",
    AnchorText = "Entry",
    PlanAction = ContractPlanAction.DeleteMethod,
    SimulateFailure = false,
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Format",
    ParameterCount = 0,
});
Assert(workflowRun.Candidate.Reasons.Count == 2, "工作流服务应生成候选原因集合。");
Assert(workflowRun.Decision.Approvals.Count == 1, "工作流服务应生成批准决策。");
Assert(workflowRun.Plan.ChangeItems.Count == 1, "工作流服务应编译计划项。");
Assert(workflowRun.Result.ExecutionFailures.Count == 0, "无冲突工作流不应生成执行失败。");
Assert(workflowRun.Report.AuditConclusion == ContractAuditConclusion.ApprovedForExecution, "无失败时应允许执行。");
Assert(workflowRun.DomainEvents.Count >= 8, "工作流服务应返回主链领域事件流。");
Assert(workflowRun.DomainEvents.All(item => item.CorrelationId == workflowRun.RunCorrelationId), "工作流事件流应统一使用 RunCorrelationId。");
Assert(workflowRun.DomainEvents.Any(item => item.EventName == "WorkspacePrepared"), "工作流事件流应包含工作区就绪事件。");

RewriteWorkflowRunDto guardedWorkflowRun = await rewriteWorkflowAppService.RunAsync(new RunRewriteWorkflowRequest
{
    WorkspaceContextId = workspace.Id,
    AnalysisSnapshotId = cpgSnapshot.Id,
    RuleTargetId = ruleTarget.Id,
    RuleCode = "workflow.guard",
    TargetName = "PlayerTools.Format",
    CandidateKind = ContractCandidateKind.Method,
    PrimaryReason = ContractCandidateReason.ManualReviewRequired,
    AdditionalReasons = Array.Empty<ContractCandidateReason>(),
    ScenarioTags = new[] { ContractScenarioTag.MethodBodyClearing },
    BoundaryName = "GuardedBoundary",
    SliceDirection = ContractSliceDirection.Forward,
    MaxDepth = 1,
    IncludeExternalReferences = false,
    PropagationTargets = Array.Empty<string>(),
    ProtectionRules = new[] { "public-contract" },
    ConflictTargets = new[] { "PlayerToolsShadow.Format" },
    ConfidenceLevel = ContractConfidenceLevel.Medium,
    DocumentPath = "src/Infrastructure/Roslyn/RoslynCodeIsolationGateway.cs",
    MemberSignature = "Format()",
    AnchorText = "Format",
    PlanAction = ContractPlanAction.ClearMethodBody,
    SimulateFailure = true,
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Format",
    ParameterCount = 0,
});
Assert(guardedWorkflowRun.Decision.Rejections.Count == 1, "受保护工作流应产生拒绝项。");
Assert(guardedWorkflowRun.Result.ExecutionFailures.Count == 1, "受保护工作流应保留执行失败。");
Assert(guardedWorkflowRun.Evidence.RiskSummary.RequiresManualReview, "受保护工作流应要求人工复核。");
Assert(guardedWorkflowRun.Report.AuditConclusion == ContractAuditConclusion.RequiresManualReview, "受保护工作流应输出人工复核结论。");
Assert(guardedWorkflowRun.DomainEvents.Any(item => item.EventName == "PlanConflictDetected"), "存在冲突时应发布计划冲突事件。");

AssertNamespaceMatrix(new (Type Type, string ExpectedNamespace, string Message)[]
{
    (typeof(WorkspaceContext), "Domain.Workspaces", "工作区上下文应归 Input / Workspace Context。"),
    (typeof(InputDescriptor), "Domain.Workspaces", "输入描述应归 Input / Workspace Context。"),
    (typeof(RunMode), "Domain.Workspaces", "运行模式应归 Input / Workspace Context。"),
    (typeof(WorkspaceContextDto), "Application.Contracts.Workspaces", "工作区 DTO 应归 Application 边界契约层。"),
    (typeof(AnalysisCpgSnapshot), "Domain.Analysis", "分析快照应归 Program Fact Context 的对外契约层。"),
    (typeof(AnalysisInputDescriptor), "Domain.Analysis", "分析输入描述应归 Program Fact Context 的对外契约层。"),
    (typeof(AnalysisCpgSnapshotDto), "Application.Contracts.Analysis", "分析 DTO 应归 Application 边界契约层。"),
    (typeof(RuleTarget), "Domain.Marking", "规则命中对象应归 Rule Screening Context。"),
    (typeof(RuleTargetDto), "Application.Contracts.Marking", "规则命中 DTO 应归 Application 边界契约层。"),
    (typeof(IRuleTargetMarkingPreparer), "Logic.Marking", "规则目标候选准备接口应归 Logic.Marking。"),
    (typeof(RuleTargetMarkingPreparer), "Logic.Marking", "规则目标候选准备实现应归 Logic.Marking。"),
    (typeof(ChangeCandidate), "Domain.Propagation", "变更候选应归 Impact Propagation Context。"),
    (typeof(PropagationFactReference), "Domain.Propagation", "传播事实引用应归 Impact Propagation Context。"),
    (typeof(ChangeCandidateDto), "Application.Contracts.Propagation", "变更候选 DTO 应归传播边界契约层。"),
    (typeof(CandidateReason), "Domain.Rules", "候选理由应归共享规则语义，而不是某个阶段私有对象。"),
    (typeof(RuleSet), "Domain.Rules", "规则集应归共享规则语义。"),
    (typeof(EnabledRule), "Domain.Rules", "启用规则应归共享规则语义。"),
    (typeof(RuleDescriptor), "Logic.Rules", "规则目录描述应归 Logic.Rules。"),
    (typeof(RuleCatalog), "Logic.Rules", "规则目录实现应归 Logic.Rules。"),
    (typeof(EnabledRuleFactory), "Logic.Rules", "启用规则工厂应归 Logic.Rules。"),
    (typeof(WorkspaceDefaultRulePreset), "Logic.Rules", "工作区默认规则预设应归 Logic.Rules。"),
    (typeof(RewriteWorkflowRulePreset), "Logic.Rules", "RewriteWorkflow 规则预设应归 Logic.Rules。"),
    (typeof(RewriteDecision), "Domain.Decision", "改写裁决应归 Rewrite Decision Context。"),
    (typeof(DecisionRiskScore), "Domain.Decision", "决策风险评分应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionAssessment), "Domain.Decision", "决策评估结果应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionAssessmentPolicy), "Domain.Decision", "决策评估策略应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionResolutionInput), "Domain.Decision", "决策解释输入应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionOutcome), "Domain.Decision", "决策解释结果应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionResolutionPolicy), "Domain.Decision", "决策解释策略应归 Rewrite Decision Context。"),
    (typeof(RewriteDecisionDto), "Application.Contracts.Decision", "裁决 DTO 应归 Application 边界契约层。"),
    (typeof(CodeRewriteResult), "Domain.Rewrite.Artifacts", "改写产物结果应归 Rewrite.Artifacts 子边界。"),
    (typeof(MemberSlice), "Domain.Rewrite.Artifacts", "成员切片应归 Rewrite.Artifacts 子边界。"),
    (typeof(ShadowClass), "Domain.Rewrite.Artifacts", "影子类应归 Rewrite.Artifacts 子边界。"),
    (typeof(RuntimeClosure), "Domain.Rewrite.Artifacts", "运行闭包应归 Rewrite.Artifacts 子边界。"),
    (typeof(CodeRewriteResultDto), "Application.Contracts.Rewrite.Artifacts", "改写产物 DTO 应归 Rewrite.Artifacts 契约边界。"),
    (typeof(RewritePlan), "Domain.Execution", "改写计划应归 Rewrite Execution Context。"),
    (typeof(RewriteResult), "Domain.Execution", "改写结果应归 Rewrite Execution Context。"),
    (typeof(RewritePlanDto), "Application.Contracts.Execution", "执行计划 DTO 应归 Application 边界契约层。"),
    (typeof(VerificationEvidence), "Domain.Output.Verification", "验证证据应归 Verification Context。"),
    (typeof(RunReport), "Domain.Output.Audit", "运行报告应归 Audit / Reporting Context。"),
    (typeof(VerificationEvidenceDto), "Application.Contracts.Output.Verification", "验证证据 DTO 应归输出契约边界。"),
    (typeof(RunReportDto), "Application.Contracts.Output.Audit", "运行报告 DTO 应归输出契约边界。"),
    (typeof(IRewriteWorkflowArtifactAssembler), "Logic.Workflow", "工作流装配器应归 Logic 工作流支撑层，而不是业务上下文本身。"),
    (typeof(IDomainEvent), "Domain.Common.Events", "领域事件契约应归 Domain.Common.Events。"),
    (typeof(WorkflowEventSequenceBuilder), "Logic.Workflow.Events", "工作流事件序列组装器应归 Logic.Workflow.Events。"),
    (typeof(WorkflowDomainEventDto), "Application.Contracts.Workflow", "工作流事件 DTO 应归 Application 契约层。"),
    (typeof(IRewritePlanCompiler), "Logic.Workflow", "计划编译器应归 Logic 工作流支撑层。"),
    (typeof(IRewritePlanExecutor), "Logic.Workflow", "计划执行器应归 Logic 工作流支撑层。"),
    (typeof(IRewriteWorkflowPropagationStage), "Logic.Workflow", "工作流传播阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowDecisionStage), "Logic.Workflow", "工作流决策阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowPlanStage), "Logic.Workflow", "工作流计划阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowExecutionStage), "Logic.Workflow", "工作流执行阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowEvidenceStage), "Logic.Workflow", "工作流证据阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowReportStage), "Logic.Workflow", "工作流报告阶段应归 Logic.Workflow。"),
    (typeof(IRewriteWorkflowEventStage), "Logic.Workflow", "工作流事件阶段应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowPropagationStage), "Logic.Workflow", "工作流传播阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowDecisionStage), "Logic.Workflow", "工作流决策阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowPlanStage), "Logic.Workflow", "工作流计划阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowExecutionStage), "Logic.Workflow", "工作流执行阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowEvidenceStage), "Logic.Workflow", "工作流证据阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowReportStage), "Logic.Workflow", "工作流报告阶段实现应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowEventStage), "Logic.Workflow", "工作流事件阶段实现应归 Logic.Workflow。"),
    (typeof(CompilationEvidenceCollector), "Logic.Workflow", "编译证据收集器应归 Logic.Workflow。"),
    (typeof(StaticReasoningEvidenceCollector), "Logic.Workflow", "静态推理证据收集器应归 Logic.Workflow。"),
    (typeof(BehaviorEvidenceCollector), "Logic.Workflow", "行为证据收集器应归 Logic.Workflow。"),
    (typeof(RewriteWorkflowAppService), "Application.Services", "工作流应用服务应归 Application 编排层。"),
    (typeof(AnalysisCpgAppService), "Application.Services", "Analysis CPG 应用服务应归 Application 编排层，而不是业务上下文本身。"),
    (typeof(ContractMapper), "Application.Mappers", "ContractMapper 应归 Application.Mappers。"),
    (typeof(RoslynCodeIsolationGateway), "Infrastructure.Roslyn", "Roslyn 网关应归技术支撑层，而不是业务上下文。"),
    (typeof(CpgGraph), "Domain.Analysis.Engine.Core", "CPG 图应归 Program Fact Context 的内部事实模型层。"),
    (typeof(AnalysisBackedCpgGateway), "Infrastructure.Analysis", "Analysis 网关适配器应归 Program Fact Context 的技术适配层。")
});

AssertContractsDirectoryDoesNotReferenceDomainNamespaces(
    applicationCompilation,
    Path.Combine("src", "Application", "Contracts"));

AssertApplicationServicesDoNotCreateForbiddenTypes(
    applicationCompilation,
    Path.Combine("src", "Application", "Services"),
    "Logic.Marking.RuleTargetCandidateBuildInput",
    "Domain.Rules.EnabledRule",
    "Domain.Rules.RuleScope",
    "Domain.Rules.RuleExecutionPolicy");
AssertApplicationServicesDoNotReferenceForbiddenTypes(
    applicationCompilation,
    Path.Combine("src", "Application", "Services"),
    "Application.Abstractions.IAnalysisAppService",
    "Application.Abstractions.IAnalysisCpgAppService",
    "Application.Abstractions.ICodeIsolationAppService",
    "Application.Abstractions.IDecisionAppService",
    "Application.Abstractions.IPropagationAppService",
    "Application.Abstractions.IRewriteWorkflowAppService",
    "Application.Abstractions.IRuleTargetAppService",
    "Application.Abstractions.IWorkspaceContextAppService",
    "Application.Services.AnalysisAppService",
    "Application.Services.AnalysisCpgAppService",
    "Application.Services.CodeIsolationAppService",
    "Application.Services.DecisionAppService",
    "Application.Services.PropagationAppService",
    "Application.Services.RewriteWorkflowAppService",
    "Application.Services.RuleTargetAppService",
    "Application.Services.WorkspaceContextAppService",
    "Logic.Workspaces.IWorkspaceRuleDefaultsBuilder",
    "Domain.Decision.RewriteDecisionResolutionInput",
    "Domain.Decision.RewriteDecisionOutcome",
    "Domain.Decision.RewriteDecisionResolutionPolicy",
    "Logic.Workflow.RewriteWorkflowPlanStageInput",
    "Logic.Workflow.RewriteWorkflowExecutionStageInput",
    "Logic.Workflow.RewriteWorkflowEvidenceStageInput",
    "Logic.Workflow.RewriteWorkflowReportStageInput",
    "Logic.Workflow.RewriteWorkflowEventStageInput");
AssertFileDoesNotReferenceForbiddenTypes(
    applicationCompilation,
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "Logic.Propagation.IImpactPropagator",
    "Logic.Decision.IRewriteDecisionAssessmentBuilder",
    "Logic.Decision.IRewriteDecisionMaker");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workspaces", "WorkspaceRuleDefaultsBuilder.cs"),
    "RulePriority.Normal",
    "WorkspaceRuleDefaultsBuilder 不应继续硬编码规则优先级。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workspaces", "WorkspaceRuleDefaultsBuilder.cs"),
    "new RuleScope(",
    "WorkspaceRuleDefaultsBuilder 不应继续硬编码 RuleScope。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workspaces", "WorkspaceRuleDefaultsBuilder.cs"),
    "new RuleExecutionPolicy(",
    "WorkspaceRuleDefaultsBuilder 不应继续硬编码 RuleExecutionPolicy。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workspaces", "WorkspaceContextBuilder.cs"),
    "workflow.rule",
    "WorkspaceContextBuilder 不应继续硬编码 workspace 默认规则码。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Marking", "RuleTargetCandidateBuilder.cs"),
    "new RuleExecutionPolicy(",
    "RuleTargetCandidateBuilder 不应继续局部 new 默认 RuleExecutionPolicy。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Marking", "RuleTargetCandidateBuilder.cs"),
    "new EnabledRule(",
    "RuleTargetCandidateBuilder 不应继续局部 new EnabledRule。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Marking", "RuleTargetCandidateBuilder.cs"),
    "enabledRuleFactory.Create(",
    "RuleTargetCandidateBuilder 应通过统一规则工厂构造启用规则。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowMarkingPreparer.cs"),
    "\"workflow-marking\"",
    "RewriteWorkflowMarkingPreparer 不应继续硬编码 workflow 规则集名称。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowMarkingPreparer.cs"),
    "rewriteWorkflowRulePreset.GetMarkingRuleSetName()",
    "RewriteWorkflowMarkingPreparer 应通过 RewriteWorkflowRulePreset 获取规则集名称。");
AssertFileSourceContains(
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "rewriteWorkflowRulePreset.ResolveMarkingRuleCode(",
    "RewriteWorkflowAppService 应通过 RewriteWorkflowRulePreset 解析 workflow 规则码。");
AssertFileSourceContains(
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "rewriteWorkflowRulePreset.NormalizeProtectionRules(",
    "RewriteWorkflowAppService 应通过 RewriteWorkflowRulePreset 规范化保护规则。");
AssertFileSourceContains(
    Path.Combine("src", "Application", "Services", "PropagationAppService.cs"),
    "propagationRulePreset.ResolveRuleCode(",
    "PropagationAppService 应通过 PropagationRulePreset 解析传播规则码。");
AssertFileSourceContains(
    Path.Combine("src", "Application", "Services", "RuleTargetAppService.cs"),
    "markingRulePreset.ResolveRuleCode(",
    "RuleTargetAppService 应通过 MarkingRulePreset 解析规则码。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "RuleCode.Create(",
    "RewriteWorkflowAppService 不应继续直接调用 RuleCode.Create，稳定规则码解析应走 preset。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "PropagationAppService.cs"),
    "RuleCode.Create(request.RuleCode)",
    "PropagationAppService 不应继续局部解析请求传播规则码。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "RuleTargetAppService.cs"),
    "RuleCode.Create(request.RuleCode)",
    "RuleTargetAppService 不应继续局部解析请求规则码。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "WorkspaceContextAppService.cs"),
    "RuleCode.Create(",
    "WorkspaceContextAppService 不应继续直接解析稳定规则码，默认装配应停留在 Logic.Workspaces。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Domain", "Workspaces", "WorkspaceContext.cs"),
    "RuleBoundary.CurrentWorkspace",
    "WorkspaceContext.Create 不应重新吸收策略型规则边界默认值。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Domain", "Workspaces", "WorkspaceContext.cs"),
    "RuleTargetKind.",
    "WorkspaceContext.Create 不应重新吸收策略型规则目标默认值。");
AssertApplicationServicesDoNotInvokeForbiddenMethods(
    applicationCompilation,
    Path.Combine("src", "Application", "Services"),
    ("Application.Services.RewriteWorkflowAppService", "BuildMarkingCandidates"));
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "new RewriteWorkflowPropagationStageInput",
    "RewriteWorkflowAppService 应通过阶段输入构造函数封装编排，不应直接手写 new RewriteWorkflowPropagationStageInput。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Application", "Services", "RewriteWorkflowAppService.cs"),
    "new RewriteWorkflowDecisionStageInput",
    "RewriteWorkflowAppService 应通过阶段输入构造函数封装编排，不应直接手写 new RewriteWorkflowDecisionStageInput。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "rewriteWorkflowPlanStage.BuildPlan(",
    "RewriteWorkflowArtifactAssembler 应委派计划阶段。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "rewriteWorkflowExecutionStage.ExecutePlan(",
    "RewriteWorkflowArtifactAssembler 应委派执行阶段。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "rewriteWorkflowEvidenceStage.BuildEvidence(",
    "RewriteWorkflowArtifactAssembler 应委派证据阶段。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "rewriteWorkflowReportStage.BuildReport(",
    "RewriteWorkflowArtifactAssembler 应委派报告阶段。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "rewriteWorkflowEventStage.RecordEvents(",
    "RewriteWorkflowArtifactAssembler 应委派事件阶段。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "new RewritePlanCompilationInput",
    "RewriteWorkflowArtifactAssembler 应保持阶段组装职责，计划输入细节留在 PlanStage。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "new RewritePlanExecutionInput",
    "RewriteWorkflowArtifactAssembler 应保持阶段组装职责，执行输入细节留在 ExecutionStage。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "VerificationEvidence.Create(",
    "RewriteWorkflowArtifactAssembler 应保持阶段组装职责，证据构造留在 EvidenceStage。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowArtifactAssembler.cs"),
    "new RunReportAssemblyInput",
    "RewriteWorkflowArtifactAssembler 应保持阶段组装职责，报告装配输入留在 ReportStage。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPlanStage.cs"),
    "rewritePlanCompiler.Compile(",
    "RewriteWorkflowPlanStage 应委派 IRewritePlanCompiler。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPlanStage.cs"),
    "runReportAssembler",
    "RewriteWorkflowPlanStage 应保持计划职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPlanStage.cs"),
    "EvidenceCollector",
    "RewriteWorkflowPlanStage 应保持计划职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowExecutionStage.cs"),
    "rewritePlanExecutor.Execute(",
    "RewriteWorkflowExecutionStage 应委派 IRewritePlanExecutor。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowExecutionStage.cs"),
    "VerificationEvidence.Create(",
    "RewriteWorkflowExecutionStage 应保持执行职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowExecutionStage.cs"),
    "runReportAssembler",
    "RewriteWorkflowExecutionStage 应保持执行职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEvidenceStage.cs"),
    "compilationEvidenceCollector.Collect(",
    "RewriteWorkflowEvidenceStage 应收集编译证据。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEvidenceStage.cs"),
    "staticReasoningEvidenceCollector.Collect(",
    "RewriteWorkflowEvidenceStage 应收集静态推理证据。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEvidenceStage.cs"),
    "behaviorEvidenceCollector.Collect(",
    "RewriteWorkflowEvidenceStage 应收集行为证据。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEvidenceStage.cs"),
    "runReportAssembler.Assemble(",
    "RewriteWorkflowEvidenceStage 应保持证据职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStage.cs"),
    "runReportAssembler.Assemble(",
    "RewriteWorkflowReportStage 应委派 IRunReportAssembler。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStage.cs"),
    "compilationEvidenceCollector",
    "RewriteWorkflowReportStage 应保持报告职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStage.cs"),
    "staticReasoningEvidenceCollector",
    "RewriteWorkflowReportStage 应保持报告职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStage.cs"),
    "behaviorEvidenceCollector",
    "RewriteWorkflowReportStage 应保持报告职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStage.cs"),
    "rewritePlanExecutor.Execute(",
    "RewriteWorkflowReportStage 应保持报告职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPropagationStage.cs"),
    "impactPropagator.Propagate(",
    "RewriteWorkflowPropagationStage 应委派 IImpactPropagator。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPropagationStage.cs"),
    "rewriteDecisionMaker",
    "RewriteWorkflowPropagationStage 应保持传播职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPropagationStage.cs"),
    "runReportAssembler",
    "RewriteWorkflowPropagationStage 应保持传播职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowDecisionStage.cs"),
    "rewriteDecisionAssessmentBuilder.Build(",
    "RewriteWorkflowDecisionStage 应委派 IRewriteDecisionAssessmentBuilder。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowDecisionStage.cs"),
    "rewriteDecisionMaker.Make(",
    "RewriteWorkflowDecisionStage 应委派 IRewriteDecisionMaker。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowDecisionStage.cs"),
    "impactPropagator",
    "RewriteWorkflowDecisionStage 应保持决策职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowDecisionStage.cs"),
    "runReportAssembler",
    "RewriteWorkflowDecisionStage 应保持决策职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewritePlanCompiler.cs"),
    "RewritePlan.Create(",
    "RewritePlanCompiler 应负责创建 RewritePlan。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewritePlanCompiler.cs"),
    "plan.ApplyDecisionOutcome(",
    "RewritePlanCompiler 应负责把决策结果编译进计划。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewritePlanCompiler.cs"),
    "rewritePlanExecutor",
    "RewritePlanCompiler 应保持计划编译职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewritePlanCompiler.cs"),
    "VerificationEvidence",
    "RewritePlanCompiler 应保持计划编译职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RunReportAssembler.cs"),
    "RunReport.CreateFromExecutionOutcome(",
    "RunReportAssembler 应通过统一运行报告工厂装配报告。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RunReportAssembler.cs"),
    "CompilationEvidenceCollector",
    "RunReportAssembler 应保持报告装配职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RunReportAssembler.cs"),
    "rewritePlanExecutor",
    "RunReportAssembler 应保持报告装配职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "public RewriteWorkflowPlanStageInput ToPlanStageInput()",
    "RewriteWorkflowAssemblyInput 应显式提供 PlanStage 输入映射。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "public RewriteWorkflowExecutionStageInput ToExecutionStageInput()",
    "RewriteWorkflowAssemblyInput 应显式提供 ExecutionStage 输入映射。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "public RewriteWorkflowEvidenceStageInput ToEvidenceStageInput()",
    "RewriteWorkflowAssemblyInput 应显式提供 EvidenceStage 输入映射。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "public RewriteWorkflowReportStageInput ToReportStageInput()",
    "RewriteWorkflowAssemblyInput 应显式提供 ReportStage 输入映射。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "public RewriteWorkflowEventStageInput ToEventStageInput()",
    "RewriteWorkflowAssemblyInput 应显式提供 EventStage 输入映射。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "domainEventRecorder",
    "RewriteWorkflowAssemblyInput 应保持输入映射职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowAssemblyInput.cs"),
    "BuildEvents(",
    "RewriteWorkflowAssemblyInput 应保持输入映射职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowPlanStageInput.cs"),
    "BuildEvents(",
    "RewriteWorkflowPlanStageInput 应保持纯输入模型职责。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowExecutionStageInput.cs"),
    "Propagate(",
    "RewriteWorkflowExecutionStageInput 应保持纯输入模型职责。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStageInput.cs"),
    "Make(",
    "RewriteWorkflowEventStageInput 应保持纯输入模型职责。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStage.cs"),
    "workflowEventSequenceBuilder.BuildEvents(",
    "RewriteWorkflowEventStage 应委派 WorkflowEventSequenceBuilder 生成事件序列。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStage.cs"),
    "domainEventRecorder.RecordRange(",
    "RewriteWorkflowEventStage 应通过 DomainEventRecorder 记录缺失事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStage.cs"),
    "domainEventRecorder.GetRecordedEvents(",
    "RewriteWorkflowEventStage 应从 DomainEventRecorder 返回已记录事件。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStage.cs"),
    "runReportAssembler",
    "RewriteWorkflowEventStage 应保持事件职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowEventStage.cs"),
    "rewritePlanExecutor",
    "RewriteWorkflowEventStage 应保持事件职责边界。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStageResult.cs"),
    "public RewriteWorkflowArtifacts ToArtifacts(",
    "RewriteWorkflowReportStageResult 应提供统一 artifacts 回传入口。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "public IReadOnlyCollection<IDomainEvent> BuildEvents(",
    "WorkflowEventSequenceBuilder 应显式提供事件序列构造入口。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "AddIfMissing(events, new DecisionCompletedDomainEvent(",
    "WorkflowEventSequenceBuilder 应负责补齐决策完成事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "AddIfMissing(events, new RewritePlanCompiledDomainEvent(",
    "WorkflowEventSequenceBuilder 应负责补齐计划编译事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "AddIfMissing(events, new ExecutionCompletedDomainEvent(",
    "WorkflowEventSequenceBuilder 应负责补齐执行完成事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "AddIfMissing(events, new VerificationEvidenceCollectedDomainEvent(",
    "WorkflowEventSequenceBuilder 应负责补齐证据收集事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "AddIfMissing(events, new RunReportGeneratedDomainEvent(",
    "WorkflowEventSequenceBuilder 应负责补齐运行报告事件。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "runReportAssembler",
    "WorkflowEventSequenceBuilder 应保持事件序列职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "rewritePlanExecutor",
    "WorkflowEventSequenceBuilder 应保持事件序列职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "Events", "WorkflowEventSequenceBuilder.cs"),
    "impactPropagator",
    "WorkflowEventSequenceBuilder 应保持事件序列职责边界。");
Assert(
    typeof(IDomainEventRecorder).GetMethods().Length == 7,
    "IDomainEventRecorder 应保持最小记录器接口面。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "InMemoryDomainEventRecorder.cs"),
    "recordedEvents.RemoveAll(",
    "InMemoryDomainEventRecorder 应支持按关联标识清理事件。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "InMemoryDomainEventRecorder.cs"),
    "return recordedEvents.AsReadOnly();",
    "InMemoryDomainEventRecorder 应提供全量只读视图。");
AssertFileSourceContains(
    Path.Combine("src", "Logic", "Workflow", "Events", "InMemoryDomainEventRecorder.cs"),
    ".OrderBy(item => item.Sequence)",
    "InMemoryDomainEventRecorder 应按序号返回关联事件。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "Events", "InMemoryDomainEventRecorder.cs"),
    "BuildEvents(",
    "InMemoryDomainEventRecorder 应保持记录器职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStageResult.cs"),
    "BuildEvents(",
    "RewriteWorkflowReportStageResult 应保持阶段结果职责边界。");
AssertFileSourceDoesNotContain(
    Path.Combine("src", "Logic", "Workflow", "RewriteWorkflowReportStageResult.cs"),
    "domainEventRecorder",
    "RewriteWorkflowReportStageResult 应保持阶段结果职责边界。");
Assert(
    typeof(IRewriteWorkflowArtifactAssembler)
        .GetMethods()
        .Count(static method => string.Equals(method.Name, nameof(IRewriteWorkflowArtifactAssembler.Assemble), StringComparison.Ordinal)) == 1,
    "IRewriteWorkflowArtifactAssembler 应只暴露单一工作流装配入口，阶段拆分细节应留在 Logic.Workflow 内部。");

AssertSharedKernelMatrix(
    typeof(RuleCode),
    typeof(RuleSet),
    typeof(EnabledRule),
    typeof(RulePriority),
    typeof(RuleScope),
    typeof(RuleExecutionPolicy));

AssertMapperSplit(repositoryRoot,
    "ContractMapper.cs",
    "ContractMapper.Workspaces.cs",
    "ContractMapper.Analysis.cs",
    "ContractMapper.MarkingPropagation.cs",
    "ContractMapper.RewriteArtifacts.cs",
    "ContractMapper.DecisionExecution.cs",
    "ContractMapper.Output.cs",
    "ContractMapper.Workflow.cs");

    Console.WriteLine("ArchitectureTests: PASS");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ArchitectureTests: FAIL {exception.GetType().FullName}");
    Console.Error.WriteLine(exception.Message);

    if (exception.InnerException is not null)
    {
        Console.Error.WriteLine($"Inner: {exception.InnerException.GetType().FullName}");
        Console.Error.WriteLine(exception.InnerException.Message);
    }

    Environment.ExitCode = 1;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNamespace(Type type, string expectedNamespace, string message)
{
    if (!string.Equals(type.Namespace, expectedNamespace, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} 实际命名空间: {type.Namespace ?? "<null>"}，期望命名空间: {expectedNamespace}。");
    }
}

static void AssertNamespaceMatrix(IEnumerable<(Type Type, string ExpectedNamespace, string Message)> entries)
{
    foreach ((Type type, string expectedNamespace, string message) in entries)
    {
        AssertNamespace(type, expectedNamespace, message);
    }
}

static void AssertSharedKernelMatrix(params Type[] sharedKernelTypes)
{
    foreach (Type sharedKernelType in sharedKernelTypes)
    {
        Assert(
            typeof(Domain.Common.ISharedKernelType).IsAssignableFrom(sharedKernelType),
            $"{sharedKernelType.FullName} 应显式标记为共享内核类型。");
    }
}

static void AssertMapperSplit(string repositoryRoot, params string[] mapperFileNames)
{
    string mapperDirectory = Path.Combine(repositoryRoot, "src", "Application", "Mappers");

    foreach (string mapperFileName in mapperFileNames)
    {
        string mapperPath = Path.Combine(mapperDirectory, mapperFileName);
        Assert(File.Exists(mapperPath), $"Mapper 拆分文件缺失：{mapperFileName}");
    }
}

static void AssertServiceRegistrations(string repositoryRoot)
{
    string applicationExtensionFile = Path.Combine(
        repositoryRoot,
        "src",
        "Application",
        "DependencyInjection",
        "ServiceCollectionExtensions.cs");
    string applicationSource = File.ReadAllText(applicationExtensionFile);

    string[] requiredApplicationRegistrations =
    [
        "AddSingleton<IWorkspaceContextAppService, WorkspaceContextAppService>()",
        "AddSingleton<IAnalysisAppService, AnalysisAppService>()",
        "AddSingleton<IAnalysisCpgAppService, AnalysisCpgAppService>()",
        "AddSingleton<IRuleTargetAppService, RuleTargetAppService>()",
        "AddSingleton<IPropagationAppService, PropagationAppService>()",
        "AddSingleton<IDecisionAppService, DecisionAppService>()",
        "AddSingleton<IRewriteWorkflowAppService, RewriteWorkflowAppService>()",
        "AddSingleton<ICodeIsolationAppService, CodeIsolationAppService>()",
    ];

    foreach (string registration in requiredApplicationRegistrations)
    {
        Assert(
            applicationSource.Contains(registration, StringComparison.Ordinal),
            $"AddIsolationApplication 应注册应用服务：{registration}");
    }

    string infrastructureExtensionFile = Path.Combine(
        repositoryRoot,
        "src",
        "Infrastructure",
        "DependencyInjection",
        "ServiceCollectionExtensions.cs");
    string infrastructureSource = File.ReadAllText(infrastructureExtensionFile);
    string[] requiredInfrastructureRegistrations =
    [
        "AddSingleton<IRuleCatalog, RuleCatalog>()",
        "AddSingleton<IEnabledRuleFactory, EnabledRuleFactory>()",
        "AddSingleton<IMarkingRulePreset, MarkingRulePreset>()",
        "AddSingleton<IRulePresetProvider, WorkspaceDefaultRulePreset>()",
        "AddSingleton<IPropagationRulePreset, PropagationRulePreset>()",
        "AddSingleton<IRewriteWorkflowRulePreset, RewriteWorkflowRulePreset>()",
    ];
    foreach (string registration in requiredInfrastructureRegistrations)
    {
        Assert(
            infrastructureSource.Contains(registration, StringComparison.Ordinal),
            $"AddIsolationAnalysis 应注册规则目录能力：{registration}");
    }

    Assert(
        !infrastructureSource.Contains("Application.Abstractions", StringComparison.Ordinal) &&
        !infrastructureSource.Contains("Application.Services", StringComparison.Ordinal),
        "Infrastructure DI 扩展不应反向依赖 Application。");

    RuleCatalog ruleCatalog = new();
    WorkspaceDefaultRulePreset workspacePreset = new();
    foreach (WorkspaceEnabledRuleInput input in workspacePreset.GetWorkspaceDefaults())
    {
        Assert(
            ruleCatalog.Contains(RuleCode.Create(input.RuleCode)),
            $"Workspace 默认规则必须先在 RuleCatalog 注册：{input.RuleCode}");
    }

    RewriteWorkflowRulePreset rewriteWorkflowPreset = new();
    foreach (RuleCode ruleCode in rewriteWorkflowPreset.GetDefaultRuleCodes())
    {
        Assert(
            ruleCatalog.Contains(ruleCode),
            $"RewriteWorkflow 默认规则必须先在 RuleCatalog 注册：{ruleCode.Value}");
    }

    PropagationRulePreset propagationRulePreset = new();
    foreach (RuleCode ruleCode in propagationRulePreset.GetRuleCodes())
    {
        Assert(
            ruleCatalog.Contains(ruleCode),
            $"Propagation 默认规则必须先在 RuleCatalog 注册：{ruleCode.Value}");
    }

    MarkingRulePreset markingRulePreset = new();
    foreach (RuleCode ruleCode in markingRulePreset.GetRuleCodes())
    {
        Assert(
            ruleCatalog.Contains(ruleCode),
            $"Marking 默认规则必须先在 RuleCatalog 注册：{ruleCode.Value}");
    }
}

static void AssertDocsAsCodeEvidenceAndAnchors(string repositoryRoot)
{
    string evidencePlanRelativePath = Path.Combine("docs", "plans", "2026-04-21-DDD证据清单-热点盘点-批次计划.md");
    string evidencePlanPath = Path.Combine(repositoryRoot, evidencePlanRelativePath);
    Assert(File.Exists(evidencePlanPath), "当前批次必须先落地 DDD 证据 / 热点 / 批次计划文档。");

    string evidencePlan = File.ReadAllText(evidencePlanPath);
    string normalizedEvidencePlan = evidencePlan.Replace("\\", "/", StringComparison.Ordinal);
    string[] requiredSections =
    [
        "## 2. 证据清单",
        "## 3. 热点盘点",
        "## 4. 批次计划"
    ];

    foreach (string requiredSection in requiredSections)
    {
        Assert(
            evidencePlan.Contains(requiredSection, StringComparison.Ordinal),
            $"{evidencePlanRelativePath} 必须保留章节：{requiredSection}");
    }

    string[] requiredReferences =
    [
        "https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice",
        "https://abp.io/docs/latest/framework/architecture/best-practices/application-services",
        "https://www.writethedocs.org/guide/docs-as-code/",
        "https://docs.gitlab.com/development/documentation/workflow/",
        "https://learn.microsoft.com/en-us/contribute/content/code-in-docs"
    ];

    foreach (string requiredReference in requiredReferences)
    {
        Assert(
            evidencePlan.Contains(requiredReference, StringComparison.Ordinal),
            $"{evidencePlanRelativePath} 必须保留外部权威资料引用：{requiredReference}");
    }

    string[] requiredAnchors =
    [
        "src/Application/Services/RewriteWorkflowAppService.cs",
        "src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs",
        "src/Logic/Workflow/Events/WorkflowEventSequenceBuilder.cs",
        "tests/ArchitectureTests/Program.cs",
        "tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs"
    ];

    foreach (string requiredAnchor in requiredAnchors)
    {
        Assert(
            normalizedEvidencePlan.Contains(requiredAnchor, StringComparison.Ordinal),
            $"{evidencePlanRelativePath} 必须绑定真实代码或测试锚点：{requiredAnchor}");

        string absoluteAnchorPath = Path.Combine(repositoryRoot, requiredAnchor.Replace('/', Path.DirectorySeparatorChar));
        Assert(
            File.Exists(absoluteAnchorPath),
            $"关键 docs-as-code 锚点路径必须存在：{requiredAnchor}");
    }

    string codeAlignConstraint = Path.Combine(repositoryRoot, "docs", "约束", "代码对齐文档约束.md");
    Assert(File.Exists(codeAlignConstraint), "代码对齐文档约束必须存在。");
    string codeAlignSource = File.ReadAllText(codeAlignConstraint);
    Assert(
        codeAlignSource.Contains("2026-04-21-DDD证据清单-热点盘点-批次计划.md", StringComparison.Ordinal),
        "代码对齐文档约束应写回当前 docs-as-code 计划文档锚点。");

    string docAlignConstraint = Path.Combine(repositoryRoot, "docs", "约束", "文档对齐代码约束.md");
    Assert(File.Exists(docAlignConstraint), "文档对齐代码约束必须存在。");
    string docAlignSource = File.ReadAllText(docAlignConstraint);
    Assert(
        docAlignSource.Contains("2026-04-21-DDD证据清单-热点盘点-批次计划.md", StringComparison.Ordinal),
        "文档对齐代码约束应写回当前 docs-as-code 计划文档锚点。");
}

static void AssertFileSourceDoesNotContain(string relativeFilePath, string forbiddenText, string message)
{
    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), relativeFilePath);
    string source = File.ReadAllText(fullPath);
    Assert(!source.Contains(forbiddenText, StringComparison.Ordinal), message);
}

static void AssertFileSourceContains(string relativeFilePath, string requiredText, string message)
{
    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), relativeFilePath);
    string source = File.ReadAllText(fullPath);
    Assert(source.Contains(requiredText, StringComparison.Ordinal), message);
}

static RuleTargetCandidateBuilder CreateRuleTargetCandidateBuilder()
{
    RuleCatalog ruleCatalog = new();
    EnabledRuleFactory enabledRuleFactory = new(ruleCatalog);
    return new RuleTargetCandidateBuilder(new ChangeCandidateMarker(), enabledRuleFactory, ruleCatalog);
}

static void AssertContractsDirectoryDoesNotReferenceDomainNamespaces(CSharpCompilation compilation, string relativeDirectory)
{
    string[] forbiddenNamespaces =
    [
        "Domain.Analysis",
        "Domain.Decision",
        "Domain.Execution",
        "Domain.Output.Audit",
        "Domain.Output.Verification",
        "Domain.Propagation",
        "Domain.Rewrite.Artifacts",
        "Domain.Workspaces",
        "Domain.Rules"
    ];

    string contractsDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory));
    foreach (SyntaxTree tree in GetSyntaxTreesUnderDirectory(compilation, contractsDirectory))
    {
        SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), tree.FilePath);

        foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            ISymbol? symbol = model.GetSymbolInfo(usingDirective.Name).Symbol;
            string? namespaceName = NormalizeNamespaceName(symbol as INamespaceSymbol);
            if (namespaceName is null)
            {
                continue;
            }

            foreach (string forbiddenNamespace in forbiddenNamespaces)
            {
                Assert(!namespaceName.StartsWith(forbiddenNamespace, StringComparison.Ordinal),
                    $"{relativePath} 不应直接引用 {forbiddenNamespace}");
            }
        }

        foreach (TypeSyntax typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            ITypeSymbol? typeSymbol = model.GetTypeInfo(typeSyntax).Type;
            AssertTypeDoesNotReferenceForbiddenNamespaces(typeSymbol, forbiddenNamespaces, relativePath);
        }

        foreach (MemberDeclarationSyntax memberDeclaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            ISymbol? memberSymbol = model.GetDeclaredSymbol(memberDeclaration);
            switch (memberSymbol)
            {
                case IMethodSymbol methodSymbol:
                    AssertTypeDoesNotReferenceForbiddenNamespaces(methodSymbol.ReturnType, forbiddenNamespaces, relativePath);
                    foreach (IParameterSymbol parameterSymbol in methodSymbol.Parameters)
                    {
                        AssertTypeDoesNotReferenceForbiddenNamespaces(parameterSymbol.Type, forbiddenNamespaces, relativePath);
                    }
                    break;
                case IPropertySymbol propertySymbol:
                    AssertTypeDoesNotReferenceForbiddenNamespaces(propertySymbol.Type, forbiddenNamespaces, relativePath);
                    break;
                case IFieldSymbol fieldSymbol:
                    AssertTypeDoesNotReferenceForbiddenNamespaces(fieldSymbol.Type, forbiddenNamespaces, relativePath);
                    break;
                case IEventSymbol eventSymbol:
                    AssertTypeDoesNotReferenceForbiddenNamespaces(eventSymbol.Type, forbiddenNamespaces, relativePath);
                    break;
                case INamedTypeSymbol namedTypeSymbol:
                    AssertTypeDoesNotReferenceForbiddenNamespaces(namedTypeSymbol.BaseType, forbiddenNamespaces, relativePath);
                    foreach (INamedTypeSymbol interfaceSymbol in namedTypeSymbol.Interfaces)
                    {
                        AssertTypeDoesNotReferenceForbiddenNamespaces(interfaceSymbol, forbiddenNamespaces, relativePath);
                    }
                    break;
            }
        }
    }
}

static void AssertTypeDoesNotReferenceForbiddenNamespaces(ITypeSymbol? typeSymbol, IEnumerable<string> forbiddenNamespaces, string relativePath)
{
    if (typeSymbol is null)
    {
        return;
    }

    string? namespaceName = NormalizeNamespaceName(typeSymbol.ContainingNamespace);
    if (namespaceName is not null)
    {
        foreach (string forbiddenNamespace in forbiddenNamespaces)
        {
            Assert(!namespaceName.StartsWith(forbiddenNamespace, StringComparison.Ordinal),
                $"{relativePath} 不应直接引用 {forbiddenNamespace}");
        }
    }

    if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
    {
        foreach (ITypeSymbol typeArgument in namedTypeSymbol.TypeArguments)
        {
            AssertTypeDoesNotReferenceForbiddenNamespaces(typeArgument, forbiddenNamespaces, relativePath);
        }
    }

    if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
    {
        AssertTypeDoesNotReferenceForbiddenNamespaces(arrayTypeSymbol.ElementType, forbiddenNamespaces, relativePath);
    }
}

static void AssertApplicationServicesDoNotCreateForbiddenTypes(CSharpCompilation compilation, string relativeDirectory, params string[] forbiddenTypeNames)
{
    string directory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory));
    foreach (SyntaxTree tree in GetSyntaxTreesUnderDirectory(compilation, directory))
    {
        SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        string relativeFile = Path.GetRelativePath(Directory.GetCurrentDirectory(), tree.FilePath);

        foreach (ObjectCreationExpressionSyntax node in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            string? createdType = NormalizeSymbolName(model.GetTypeInfo(node).Type);
            if (createdType is null)
            {
                continue;
            }

            foreach (string forbiddenTypeName in forbiddenTypeNames)
            {
                Assert(!string.Equals(createdType, forbiddenTypeName, StringComparison.Ordinal),
                    $"{relativeFile} 不应直接 new {forbiddenTypeName}");
            }
        }
    }
}

static void AssertApplicationServicesDoNotInvokeForbiddenMethods(CSharpCompilation compilation, string relativeDirectory, params (string ContainingType, string MethodName)[] forbiddenMethods)
{
    string directory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory));
    foreach (SyntaxTree tree in GetSyntaxTreesUnderDirectory(compilation, directory))
    {
        SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        string relativeFile = Path.GetRelativePath(Directory.GetCurrentDirectory(), tree.FilePath);

        foreach (InvocationExpressionSyntax node in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            IMethodSymbol? methodSymbol = model.GetSymbolInfo(node).Symbol as IMethodSymbol
                ?? model.GetSymbolInfo(node).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (methodSymbol is null)
            {
                continue;
            }

            string? containingType = NormalizeSymbolName(methodSymbol.ContainingType);
            foreach ((string forbiddenContainingType, string forbiddenMethodName) in forbiddenMethods)
            {
                Assert(
                    !(string.Equals(containingType, forbiddenContainingType, StringComparison.Ordinal) &&
                      string.Equals(methodSymbol.Name, forbiddenMethodName, StringComparison.Ordinal)),
                    $"{relativeFile} 不应直接调用 {forbiddenContainingType}.{forbiddenMethodName}");
            }
        }
    }
}

static void AssertApplicationServicesDoNotReferenceForbiddenTypes(CSharpCompilation compilation, string relativeDirectory, params string[] forbiddenTypeNames)
{
    string directory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory));
    foreach (SyntaxTree tree in GetSyntaxTreesUnderDirectory(compilation, directory))
    {
        SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        string relativeFile = Path.GetRelativePath(Directory.GetCurrentDirectory(), tree.FilePath);

        foreach (TypeSyntax typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            if (typeSyntax.Parent is SimpleBaseTypeSyntax)
            {
                continue;
            }

            string? referencedType = NormalizeSymbolName(model.GetTypeInfo(typeSyntax).Type);
            if (referencedType is null)
            {
                continue;
            }

            foreach (string forbiddenTypeName in forbiddenTypeNames)
            {
                Assert(!string.Equals(referencedType, forbiddenTypeName, StringComparison.Ordinal),
                    $"{relativeFile} 不应直接依赖 {forbiddenTypeName}");
            }
        }
    }
}

static void AssertFileDoesNotReferenceForbiddenTypes(CSharpCompilation compilation, string relativeFilePath, params string[] forbiddenTypeNames)
{
    string targetPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativeFilePath));
    SyntaxTree tree = compilation.SyntaxTrees.Single(current =>
        string.Equals(Path.GetFullPath(current.FilePath), targetPath, StringComparison.OrdinalIgnoreCase));
    SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
    CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
    string relativeFile = Path.GetRelativePath(Directory.GetCurrentDirectory(), tree.FilePath);

    foreach (TypeSyntax typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
    {
        if (typeSyntax.Parent is SimpleBaseTypeSyntax)
        {
            continue;
        }

        string? referencedType = NormalizeSymbolName(model.GetTypeInfo(typeSyntax).Type);
        if (referencedType is null)
        {
            continue;
        }

        foreach (string forbiddenTypeName in forbiddenTypeNames)
        {
            Assert(!string.Equals(referencedType, forbiddenTypeName, StringComparison.Ordinal),
                $"{relativeFile} 不应直接依赖 {forbiddenTypeName}");
        }
    }
}

static CSharpCompilation BuildApplicationCompilation(string repositoryRoot)
{
    string applicationDirectory = Path.Combine(repositoryRoot, "src", "Application");
    CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
    List<SyntaxTree> syntaxTrees = Directory
        .EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
        .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path, Encoding.UTF8))
        .ToList();

    HashSet<string> referencePaths = new(StringComparer.OrdinalIgnoreCase);
    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
    {
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            referencePaths.Add(path);
        }
    }

    string buildRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "Build", "bin"));
    referencePaths.Add(Path.Combine(buildRoot, "Domain", "Debug", "net10.0", "Domain.dll"));
    referencePaths.Add(Path.Combine(buildRoot, "Logic", "Debug", "net10.0", "Logic.dll"));

    List<MetadataReference> references = referencePaths
        .Where(File.Exists)
        .Select(static path => MetadataReference.CreateFromFile(path))
        .Cast<MetadataReference>()
        .ToList();

    return CSharpCompilation.Create(
        assemblyName: "Application.ArchitectureInspection",
        syntaxTrees: syntaxTrees,
        references: references,
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}

static IEnumerable<SyntaxTree> GetSyntaxTreesUnderDirectory(CSharpCompilation compilation, string directory)
{
    return compilation.SyntaxTrees.Where(tree =>
        tree.FilePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
}

static string? NormalizeSymbolName(ISymbol? symbol)
{
    string? displayName = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return displayName?.Replace("global::", string.Empty, StringComparison.Ordinal);
}

static string? NormalizeNamespaceName(INamespaceSymbol? symbol)
{
    return symbol?.ToDisplayString().Trim();
}

static void RegisterAnalysisDependencyResolver(string repositoryRoot)
{
    string buildRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "Build", "bin"));
    string packagesRoot = Path.Combine(repositoryRoot, ".nuget", "packages");
    string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;
    string[] probeDirectories =
    [
        runtimeDirectory,
        Path.Combine(buildRoot, "Infrastructure", "Debug", "net10.0"),
        Path.Combine(buildRoot, "Application", "Debug", "net10.0"),
        Path.Combine(buildRoot, "Logic", "Debug", "net10.0"),
        Path.Combine(buildRoot, "Domain", "Debug", "net10.0"),
    ];
    HashSet<string> probedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
    {
        string assemblySimpleName = assemblyName.Name ?? string.Empty;
        if (!probedAssemblies.Add(assemblySimpleName))
        {
            return null;
        }

        string fileName = $"{assemblySimpleName}.dll";

        foreach (string probeDirectory in probeDirectories)
        {
            string directPath = Path.Combine(probeDirectory, fileName);
            if (File.Exists(directPath))
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(directPath);
            }
        }

        string? sdkAssemblyPath = FindInstalledSdkAssembly(fileName);
        if (sdkAssemblyPath is not null)
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(sdkAssemblyPath);
        }

        if (!Directory.Exists(packagesRoot))
        {
            return null;
        }

        string? packageAssemblyPath = Directory
            .EnumerateFiles(packagesRoot, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        if (packageAssemblyPath is null)
        {
            return null;
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(packageAssemblyPath);
    };
}

static string? FindInstalledSdkAssembly(string fileName)
{
    string expectedMajorPrefix = $"{Environment.Version.Major}.";

    foreach (string dotnetRoot in GetDotnetRoots())
    {
        string sdkRoot = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            continue;
        }

        foreach (string sdkDirectory in Directory
            .EnumerateDirectories(sdkRoot)
            .Where(path => Path.GetFileName(path).StartsWith(expectedMajorPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(sdkDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

static IEnumerable<string> GetDotnetRoots()
{
    HashSet<string> uniqueRoots = new(StringComparer.OrdinalIgnoreCase);

    void AddRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        string fullPath = Path.GetFullPath(candidate);
        if (Directory.Exists(fullPath) && uniqueRoots.Add(fullPath))
        {
            return;
        }
    }

    AddRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
    AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
    AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"));
    AddRoot(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty, "..", "..", "..")));

    return uniqueRoots;
}
