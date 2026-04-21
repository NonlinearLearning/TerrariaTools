using Domain.Execution;
using Domain.Decision;
using Domain.Workspaces;

namespace Logic.Workflow;

/// <summary>
/// 表示改写工作流构造输入。
/// </summary>
public sealed class RewriteWorkflowAssemblyInput
{
    public Guid RunCorrelationId { get; init; }

    public WorkspaceContext WorkspaceContext { get; init; } = null!;

    /// <summary>
    /// 获取或初始化工作区上下文标识。
    /// </summary>
    public Guid WorkspaceContextId { get; init; }

    public Guid? AnalysisSnapshotId { get; init; }

    public Guid RuleTargetId { get; init; }

    public string RuleCode { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化候选标识。
    /// </summary>
    public Guid CandidateId { get; init; }

    /// <summary>
    /// 获取或初始化决策标识。
    /// </summary>
    public Guid DecisionId { get; init; }

    public RewriteDecision Decision { get; init; } = null!;

    /// <summary>
    /// 获取或初始化是否批准。
    /// </summary>
    public bool Approved { get; init; }

    /// <summary>
    /// 获取或初始化批准数量。
    /// </summary>
    public int ApprovalCount { get; init; }

    /// <summary>
    /// 获取或初始化拒绝数量。
    /// </summary>
    public int RejectionCount { get; init; }

    /// <summary>
    /// 获取或初始化保护数量。
    /// </summary>
    public int ProtectionCount { get; init; }

    /// <summary>
    /// 获取或初始化传播轨迹数量。
    /// </summary>
    public int PropagationTraceCount { get; init; }

    public int ReasonCount { get; init; }

    public int MaxDepth { get; init; }

    /// <summary>
    /// 获取或初始化冲突目标数量。
    /// </summary>
    public int ConflictTargetCount { get; init; }

    /// <summary>
    /// 获取或初始化目标名称。
    /// </summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化文档路径。
    /// </summary>
    public string DocumentPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或初始化成员签名。
    /// </summary>
    public string? MemberSignature { get; init; }

    /// <summary>
    /// 获取或初始化锚点文本。
    /// </summary>
    public string? AnchorText { get; init; }

    /// <summary>
    /// 获取或初始化计划动作。
    /// </summary>
    public PlanAction PlanAction { get; init; }

    /// <summary>
    /// 获取或初始化传播目标集合。
    /// </summary>
    public IReadOnlyCollection<string> PropagationTargets { get; init; } = Array.Empty<string>();

    public string SourceCode { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public string? MethodName { get; init; }

    public int? ParameterCount { get; init; }

    public RewriteWorkflowPlanStageInput ToPlanStageInput() => new()
    {
        RunCorrelationId = RunCorrelationId,
        WorkspaceContext = WorkspaceContext,
        CandidateId = CandidateId,
        Decision = Decision,
        TargetName = TargetName,
        DocumentPath = DocumentPath,
        MemberSignature = MemberSignature,
        AnchorText = AnchorText,
        PlanAction = PlanAction,
    };

    public RewriteWorkflowExecutionStageInput ToExecutionStageInput() => new()
    {
        RunCorrelationId = RunCorrelationId,
        WorkspaceContext = WorkspaceContext,
        SourceCode = SourceCode,
        ClassName = ClassName,
        MethodName = MethodName,
        ParameterCount = ParameterCount,
    };

    public RewriteWorkflowEvidenceStageInput ToEvidenceStageInput() => new()
    {
        RunCorrelationId = RunCorrelationId,
        TargetName = TargetName,
        PlanAction = PlanAction,
        PropagationTargets = PropagationTargets,
    };

    public RewriteWorkflowReportStageInput ToReportStageInput() => new()
    {
        RunCorrelationId = RunCorrelationId,
        WorkspaceContextId = WorkspaceContextId,
        DecisionId = DecisionId,
        Decision = Decision,
    };

    public RewriteWorkflowEventStageInput ToEventStageInput() => new()
    {
        RunCorrelationId = RunCorrelationId,
        WorkspaceContext = WorkspaceContext,
        WorkspaceContextId = WorkspaceContextId,
        AnalysisSnapshotId = AnalysisSnapshotId,
        RuleTargetId = RuleTargetId,
        RuleCode = RuleCode,
        CandidateId = CandidateId,
        DecisionId = DecisionId,
        Decision = Decision,
        Approved = Approved,
        ApprovalCount = ApprovalCount,
        RejectionCount = RejectionCount,
        ProtectionCount = ProtectionCount,
        PropagationTraceCount = PropagationTraceCount,
        ReasonCount = ReasonCount,
        MaxDepth = MaxDepth,
        ConflictTargetCount = ConflictTargetCount,
        TargetName = TargetName,
        DocumentPath = DocumentPath,
        PlanAction = PlanAction,
        PropagationTargets = PropagationTargets,
    };
}
