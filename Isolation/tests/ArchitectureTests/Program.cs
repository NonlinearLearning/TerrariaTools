using Application.Contracts.Analysis;
using Application.Contracts.Decision;
using Application.Contracts.Execution;
using Application.Contracts.Marking;
using Application.Contracts.Output;
using Application.Contracts.Propagation;
using Application.Contracts.Rewrite;
using Application.Contracts.Workspaces;
using Application.Contracts.Workflow;
using Application.Mappers;
using Application.Services;
using Domain.Analysis;
using Domain.Decision;
using Domain.Execution;
using Domain.Marking;
using Domain.Output;
using Domain.Propagation;
using Domain.Workspaces;
using Infrastructure.Analysis;
using Infrastructure.Persistence;
using Infrastructure.Roslyn;

InMemoryWorkspaceContextRepository workspaceRepository = new();
WorkspaceContextAppService workspaceAppService = new(workspaceRepository);
WorkspaceContextDto workspace = await workspaceAppService.CreateAsync(new CreateWorkspaceContextRequest
{
    SolutionPath = "D:/ProjectItem/SourceCode/Net/TerrariaTools/Isolation",
    LanguageVersion = "latest",
    Documents = new[]
    {
        "docs\\DDD\\事件风暴.md",
        "docs/DDD/事件风暴.md",
        "src/Analysis/Core/CpgGraph.cs",
    },
    Projects = new[]
    {
        new ProjectItemDto
        {
            Name = "Analysis",
            Path = "src/Analysis/Analysis.csproj",
        },
    },
});

Assert(workspace.Documents.Count == 2, "WorkspaceContext 应该对文档路径做标准化去重。");

AnalysisAppService analysisAppService = new(
    workspaceRepository,
    new InMemoryAnalysisSnapshotRepository(),
    new DefaultAnalysisSnapshotBuilder());

AnalysisCpgSnapshotDto cpgSnapshot = await analysisAppService.BuildCpgSnapshotAsync(new BuildAnalysisCpgSnapshotRequest
{
    WorkspaceContextId = workspace.Id,
    EntrySymbol = "Analysis.Core.CpgGraphBuilder.Build",
    MinimumTarget = MinimumAnalysisTarget.Method,
    Depth = 2,
});

Assert(cpgSnapshot.Nodes.Count >= 2, "CPG 快照至少应生成入口节点和一个文档节点。");

RuleTargetAppService ruleTargetAppService = new(new InMemoryRuleTargetRepository());
RuleTargetDto ruleTarget = await ruleTargetAppService.CreateAsync(new CreateRuleTargetRequest
{
    SnapshotId = cpgSnapshot.Id,
    RuleCode = "marking.rule-target",
    CandidateReason = CandidateReason.ManualReviewRequired,
    Node = new MinimumNodeDto
    {
        NodeId = "entry",
        DisplayName = "WorkspaceContext",
        DocumentPath = "docs/DDD/事件风暴.md",
        NodeType = CpgType.TypeDecl,
        StartLine = 1,
        StartColumn = 1,
        EndLine = 1,
        EndColumn = 16,
    },
    Note = "首批落地规则命中。",
});

Assert(ruleTarget.RuleCode == "marking.rule-target", "规则命中目标应返回正确规则编号。");

CodeIsolationAppService codeIsolationAppService = new(new RoslynCodeIsolationGateway());
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

    public int Helper(int value)
    {
        return value + seed;
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

RuntimeClosureDto runtimeClosure = await codeIsolationAppService.ExtractMinimalRuntimeClosureAsync(new ExtractMinimalRuntimeClosureRequest
{
    SourceCode = SampleSource,
    ClassName = "PlayerTools",
    MethodName = "Entry",
    ParameterCount = 1,
});
Assert(runtimeClosure.ClosureClassName == "PlayerToolsRuntimeClosure", "最小运行闭包应生成闭包类型名称。");
Assert(runtimeClosure.MemberNames.Contains("Helper"), "最小运行闭包应保留入口依赖成员。");

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
    new PlanTarget(DocumentPath.Create("src/Domain/Rewrite/RuntimeClosure.cs"), "PlayerTools.Entry", "Entry(int)", "Entry"),
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
rewriteResult.AddFileChange(new FileChange(
    DocumentPath.Create("src/Domain/Rewrite/RuntimeClosure.cs"),
    "删除入口方法并保留闭包依赖。",
    new[] { "PlayerTools.Entry", "PlayerTools.Helper" }));
rewriteResult.AddExecutionTrace(new ExecutionTrace(planChangeItem.Id, "定位目标代码", "已定位到 Entry(int)。", DateTimeOffset.UtcNow));
rewriteResult.AddExecutionFailure(new ExecutionFailure(planChangeItem.Id, "Conflict", "存在待人工处理冲突。", true));
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
Assert(changeCandidateDto.ScenarioTags.Contains(ScenarioTag.MinimalRuntimeClosure), "候选 DTO 应映射场景标签。");
Assert(rewriteDecisionDto.Protections.Count == 1, "决策 DTO 应映射保护项。");
Assert(rewritePlanDto.ChangeItems.Count == 1, "计划 DTO 应映射计划项。");
Assert(rewriteResultDto.ExecutionFailures.Count == 1, "结果 DTO 应映射执行失败。");
Assert(verificationEvidenceDto.RiskSummary.RequiresManualReview, "证据 DTO 应映射风险摘要。");
Assert(runReportDto.VerificationEvidenceId == verificationEvidence.Id, "运行报告 DTO 应映射证据标识。");

PropagationAppService propagationAppService = new();
PropagationResultDto propagationResult = await propagationAppService.PropagateAsync(new BuildPropagationRequest
{
    RuleTargetId = ruleTarget.Id,
    RuleCode = "propagation.rule",
    TargetName = "PlayerTools.Entry",
    CandidateKind = CandidateKind.Method,
    PrimaryReason = CandidateReason.CallChainMatched,
    AdditionalReasons = new[] { CandidateReason.DataFlowReachable },
    ScenarioTags = new[] { ScenarioTag.MethodDeletion, ScenarioTag.MemberSlice },
    BoundaryName = "DirectPropagationBoundary",
    SliceDirection = SliceDirection.Bidirectional,
    MaxDepth = 2,
    PropagationTargets = new[] { "PlayerTools.Helper" },
});
Assert(propagationResult.Candidate.Reasons.Count == 2, "传播服务应生成候选原因。");
Assert(propagationResult.PropagationTraces.Count == 1, "传播服务应生成传播轨迹。");
Assert(propagationResult.SliceBoundary?.BoundaryName == "DirectPropagationBoundary", "传播服务应生成切片边界。");

DecisionAppService decisionAppService = new();
DecisionResultDto directDecisionResult = await decisionAppService.DecideAsync(new BuildRewriteDecisionRequest
{
    Candidate = propagationResult.Candidate,
    ProtectionRules = Array.Empty<string>(),
    ConflictTargets = Array.Empty<string>(),
    ConfidenceLevel = ConfidenceLevel.High,
    ForceReject = false,
});
Assert(directDecisionResult.Approved, "决策服务应批准无保护候选。");
Assert(directDecisionResult.Decision.Approvals.Count == 1, "决策服务应输出批准项。");

DecisionResultDto protectedDecisionResult = await decisionAppService.DecideAsync(new BuildRewriteDecisionRequest
{
    Candidate = propagationResult.Candidate,
    ProtectionRules = new[] { "public-contract" },
    ConflictTargets = new[] { "PlayerToolsShadow.Entry" },
    ConfidenceLevel = ConfidenceLevel.Medium,
    ForceReject = false,
});
Assert(!protectedDecisionResult.Approved, "决策服务应拒绝受保护候选。");
Assert(protectedDecisionResult.Protections.Count == 1, "决策服务应输出保护项。");
Assert(protectedDecisionResult.Conflicts.Count == 1, "决策服务应输出冲突项。");

RewriteWorkflowAppService rewriteWorkflowAppService = new(propagationAppService, decisionAppService);
RewriteWorkflowRunDto workflowRun = await rewriteWorkflowAppService.RunAsync(new RunRewriteWorkflowRequest
{
    WorkspaceContextId = workspace.Id,
    RuleTargetId = ruleTarget.Id,
    RuleCode = "workflow.rule",
    TargetName = "PlayerTools.Entry",
    CandidateKind = CandidateKind.Method,
    PrimaryReason = CandidateReason.CallChainMatched,
    AdditionalReasons = new[] { CandidateReason.DataFlowReachable },
    ScenarioTags = new[] { ScenarioTag.MethodDeletion, ScenarioTag.MinimalRuntimeClosure },
    BoundaryName = "WorkflowBoundary",
    SliceDirection = SliceDirection.Bidirectional,
    MaxDepth = 2,
    PropagationTargets = new[] { "PlayerTools.Helper", "PlayerTools.seed" },
    ProtectionRules = Array.Empty<string>(),
    ConflictTargets = Array.Empty<string>(),
    ConfidenceLevel = ConfidenceLevel.High,
    DocumentPath = "src/Infrastructure/Roslyn/RoslynCodeIsolationGateway.cs",
    MemberSignature = "Entry(int)",
    AnchorText = "Entry",
    PlanAction = PlanAction.DeleteMethod,
    SimulateFailure = false,
});
Assert(workflowRun.Candidate.Reasons.Count == 2, "工作流服务应生成候选原因集合。");
Assert(workflowRun.Decision.Approvals.Count == 1, "工作流服务应生成批准决策。");
Assert(workflowRun.Plan.ChangeItems.Count == 1, "工作流服务应编译计划项。");
Assert(workflowRun.Result.ExecutionFailures.Count == 0, "无冲突工作流不应生成执行失败。");
Assert(workflowRun.Report.AuditConclusion == AuditConclusion.ApprovedForExecution, "无失败时应允许执行。");

RewriteWorkflowRunDto guardedWorkflowRun = await rewriteWorkflowAppService.RunAsync(new RunRewriteWorkflowRequest
{
    WorkspaceContextId = workspace.Id,
    RuleTargetId = ruleTarget.Id,
    RuleCode = "workflow.guard",
    TargetName = "PlayerTools.Format",
    CandidateKind = CandidateKind.Method,
    PrimaryReason = CandidateReason.ManualReviewRequired,
    AdditionalReasons = Array.Empty<CandidateReason>(),
    ScenarioTags = new[] { ScenarioTag.MethodBodyClearing },
    BoundaryName = "GuardedBoundary",
    SliceDirection = SliceDirection.Forward,
    MaxDepth = 1,
    PropagationTargets = Array.Empty<string>(),
    ProtectionRules = new[] { "public-contract" },
    ConflictTargets = new[] { "PlayerToolsShadow.Format" },
    ConfidenceLevel = ConfidenceLevel.Medium,
    DocumentPath = "src/Infrastructure/Roslyn/RoslynCodeIsolationGateway.cs",
    MemberSignature = "Format()",
    AnchorText = "Format",
    PlanAction = PlanAction.ClearMethodBody,
    SimulateFailure = true,
});
Assert(guardedWorkflowRun.Decision.Rejections.Count == 1, "受保护工作流应产生拒绝项。");
Assert(guardedWorkflowRun.Result.ExecutionFailures.Count == 1, "受保护工作流应保留执行失败。");
Assert(guardedWorkflowRun.Evidence.RiskSummary.RequiresManualReview, "受保护工作流应要求人工复核。");
Assert(guardedWorkflowRun.Report.AuditConclusion == AuditConclusion.RequiresManualReview, "受保护工作流应输出人工复核结论。");

Console.WriteLine("ArchitectureTests: PASS");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
