using TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Core.Planning;

/// <summary>
/// 表示审计计划的基础元数据。
/// </summary>
public sealed record PlanMetadata(
    string ToolName,
    string PlanVersion,
    string InputPath,
    string OutputPath,
    RunMode RunMode)
{
    /// <summary>
    /// 获取计划元数据生成的 UTC 时间。
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 表示规划层使用的计划动作。
/// </summary>
public sealed record PlanAction(PlanActionKind Kind, string? Payload = null)
{
    /// <summary>
    /// 将公共层动作隐式转换为规划层动作。
    /// </summary>
    public static implicit operator PlanAction(TerrariaTools.Dome.Core.Common.PlanAction action) =>
        new(action.Kind, action.Payload);

    /// <summary>
    /// 将规划层动作隐式转换为公共层动作。
    /// </summary>
    public static implicit operator TerrariaTools.Dome.Core.Common.PlanAction(PlanAction action) =>
        new(action.Kind, action.Payload);
}

/// <summary>
/// 表示计划变更的原因说明。
/// </summary>
public sealed record PlanReason(
    string RuleId,
    string ReasonText,
    string? SourceTargetKey = null,
    string? SourceTargetDisplayText = null,
    IReadOnlyList<string>? RelatedSymbolKeys = null,
    IReadOnlyList<string>? RelatedSymbolNames = null,
    string? Severity = null,
    string? SourceMemberId = null,
    BoundaryKind? BoundaryKind = null,
    IReadOnlyList<string>? TriggeredSymbolKeys = null,
    DecisionOrigin Origin = DecisionOrigin.Rule,
    DecisionCategory Category = DecisionCategory.Delete);

/// <summary>
/// 表示传播过程中关联的证据信息。
/// </summary>
public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

/// <summary>
/// 表示传播链中的单次跳转。
/// </summary>
public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

/// <summary>
/// 表示某个计划变更对应的完整传播链。
/// </summary>
public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

/// <summary>
/// 表示计划中的单条执行变更。
/// </summary>
public sealed record PlannedChange(
    int ExecutionOrder,
    TargetIdentity Target,
    TargetLocator Locator,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null);

/// <summary>
/// 表示计划编译过程中发现的冲突。
/// </summary>
public sealed record PlanConflict(
    string ConflictCode,
    TargetIdentity Target,
    TargetLocator Locator,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

/// <summary>
/// 表示完整的审计计划。
/// </summary>
public sealed record AuditPlan(
    PlanMetadata Metadata,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<PlanConflict> Conflicts);

/// <summary>
/// 表示计划编译结果。
/// </summary>
public sealed record PlanCompilationResult(
    bool IsSuccess,
    AuditPlan? Plan,
    FailureCode FailureCode,
    IReadOnlyList<PlanConflict> Conflicts,
    string? Message)
{
    /// <summary>
    /// 创建成功的计划编译结果。
    /// </summary>
    public static PlanCompilationResult Success(AuditPlan plan) =>
        new(true, plan, FailureCode.None, Array.Empty<PlanConflict>(), null);

    /// <summary>
    /// 创建失败的计划编译结果。
    /// </summary>
    public static PlanCompilationResult Failure(string? message, IReadOnlyList<PlanConflict> conflicts) =>
        new(false, null, FailureCode.PlanCompileFailed, conflicts, message);
}

/// <summary>
/// 表示函数级影响集合。
/// </summary>
public sealed record FunctionImpactSet(
    IReadOnlyList<string> DeletedFunctionIds,
    IReadOnlyList<string> AffectedFunctionIds,
    IReadOnlyList<string> AffectedDocumentPaths,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds);

/// <summary>
/// 表示函数级影响摘要。
/// </summary>
public sealed record FunctionImpactSummary(
    int DeletedFunctionCount,
    int AffectedFunctionCount,
    int AffectedDocumentCount,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    IReadOnlyList<string> SampleAffectedFunctionIds,
    IReadOnlyList<string> SampleAffectedDocumentPaths);

/// <summary>
/// 表示规划阶段的输出结果。
/// </summary>
public sealed record PlanningOutput(
    PlanCompilationResult Compilation,
    FunctionImpactSet? FunctionImpactSet)
{
    /// <summary>
    /// 获取编译成功后生成的计划对象。
    /// </summary>
    public AuditPlan? Plan => Compilation.Plan;

    /// <summary>
    /// 获取规划阶段是否成功完成。
    /// </summary>
    public bool IsSuccess => Compilation.IsSuccess;
}
