using TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Dome.Model.Planning;

public sealed record PlanMetadata(
    string ToolName,
    string PlanVersion,
    string InputPath,
    string OutputPath,
    RunMode RunMode)
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PlanAction(
    PlanActionKind Kind,
    string? Payload = null);

public sealed record PlannedChange(
    int ExecutionOrder,
    TargetIdentity Target,
    TargetLocator Locator,
    PlanAction Action,
    object? Reason = null,
    object? Chain = null);

public sealed record PlanConflict(
    string ConflictCode,
    TargetIdentity Target,
    TargetLocator Locator,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

public sealed record AuditPlan(
    PlanMetadata Metadata,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<PlanConflict> Conflicts);

public sealed record PlanCompilationResult(
    bool IsSuccess,
    AuditPlan? Plan,
    FailureCode FailureCode,
    IReadOnlyList<PlanConflict> Conflicts,
    string? Message)
{
    public static PlanCompilationResult Success(AuditPlan plan) =>
        new(true, plan, FailureCode.None, Array.Empty<PlanConflict>(), null);

    public static PlanCompilationResult Failure(string? message, IReadOnlyList<PlanConflict> conflicts) =>
        new(false, null, FailureCode.PlanCompileFailed, conflicts, message);
}

public sealed record FunctionImpactSet(
    IReadOnlyList<string> DeletedFunctionIds,
    IReadOnlyList<string> AffectedFunctionIds,
    IReadOnlyList<string> AffectedDocumentPaths,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds);

public sealed record FunctionImpactSummary(
    int DeletedFunctionCount,
    int AffectedFunctionCount,
    int AffectedDocumentCount,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    IReadOnlyList<string> SampleAffectedFunctionIds,
    IReadOnlyList<string> SampleAffectedDocumentPaths);
