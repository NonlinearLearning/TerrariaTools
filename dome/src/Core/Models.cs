namespace TerrariaTools.Dome.Core;

public enum RunMode
{
    Standard,
    AnalyzeOnly,
    PlanOnly
}

public enum FailureCode
{
    None,
    WorkspaceLoadFailed,
    AnalysisFailed,
    PlanCompileFailed,
    RewriteFailed,
    ReportFailed
}

public enum MemberKind
{
    Unknown,
    Field,
    Method,
    Constructor,
    Property,
    Accessor
}

public enum TargetKind
{
    Statement,
    Method
}

public enum PlanActionKind
{
    Delete,
    CommentOut,
    ReplaceWithDefault,
    AddReturn
}

public readonly record struct MemberId(string Value)
{
    public override string ToString() => Value;
}

public sealed record RunRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> RuleSet,
    RunMode Mode);

public sealed record RunResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string OutputPath,
    string? ReportPath,
    string? Message)
{
    public static RunResult Success(string outputPath, string? reportPath) =>
        new(true, FailureCode.None, outputPath, reportPath, null);

    public static RunResult Failure(FailureCode code, string outputPath, string? message) =>
        new(false, code, outputPath, null, message);
}

public sealed record PlanMetadata(
    string ToolName,
    string PlanVersion,
    string InputPath,
    string OutputPath,
    RunMode RunMode)
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PlanTarget(
    string DocumentPath,
    MemberId MemberId,
    MemberKind MemberKind,
    TargetKind TargetKind,
    int SpanStart,
    int SpanLength,
    string DisplayText)
{
    public string TargetKey => $"{DocumentPath}|{MemberId.Value}|{TargetKind}|{SpanStart}|{SpanLength}";
}

public sealed record PlanAction(
    PlanActionKind Kind,
    string? Payload = null);

public sealed record PlanReason(
    string RuleId,
    string ReasonText,
    string? SourceTargetKey = null,
    string? SourceTargetDisplayText = null,
    IReadOnlyList<string>? RelatedSymbolKeys = null,
    IReadOnlyList<string>? RelatedSymbolNames = null,
    string? Severity = null);

public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

public sealed record MarkDecision(
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null)
{
    public static MarkDecision ForTarget(
        PlanTarget target,
        PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        string? payload = null,
        string? sourceTargetKey = null,
        string? sourceTargetDisplayText = null,
        IReadOnlyList<string>? relatedSymbolKeys = null,
        IReadOnlyList<string>? relatedSymbolNames = null,
        string? severity = null,
        PropagationChain? chain = null) =>
        new(
            target,
            new PlanAction(actionKind, payload),
            new PlanReason(
                ruleId,
                reasonText,
                sourceTargetKey,
                sourceTargetDisplayText,
                relatedSymbolKeys ?? Array.Empty<string>(),
                relatedSymbolNames ?? Array.Empty<string>(),
                severity),
            chain);
}

public sealed record PlannedChange(
    int ExecutionOrder,
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null);

public sealed record PlanConflict(
    string ConflictCode,
    PlanTarget Target,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

public sealed record AuditPlan(
    PlanMetadata Metadata,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<PlanConflict> Conflicts);

public sealed record AnalysisView(
    IReadOnlyList<AnalysisTarget> Targets,
    IReadOnlyList<AnalysisEdge> Edges);

public sealed record AnalysisTarget(
    PlanTarget Target,
    bool IsHighRisk,
    IReadOnlyList<DirectiveAction> Directives,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols);

public enum AnalysisEdgeKind
{
    Defines,
    Uses,
    Precedes
}

public enum SymbolKindRef
{
    Unknown,
    Local,
    Parameter,
    Field,
    Property
}

public sealed record SymbolRef(
    string SymbolKey,
    string DisplayName,
    SymbolKindRef SymbolKind,
    MemberId DeclaringMemberId,
    int DeclarationSpanStart,
    int DeclarationSpanLength);

public sealed record AnalysisEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    AnalysisEdgeKind Kind,
    string? SymbolKey = null);

public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

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

public sealed record RewriteExecutionResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string? RewrittenSource,
    string? Message)
{
    public static RewriteExecutionResult Success(string rewrittenSource) =>
        new(true, FailureCode.None, rewrittenSource, null);

    public static RewriteExecutionResult Failure(string? message) =>
        new(false, FailureCode.RewriteFailed, null, message);
}

public sealed record FailureSummary(
    FailureCode FailureCode,
    string Message);

public sealed record ConflictSummary(
    string ConflictCode,
    string TargetKey,
    string TargetDisplayText,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

public sealed record RiskSummary(
    int SkippedHighRiskTargetCount,
    IReadOnlyList<string> SampleTargetDisplayTexts);

public sealed record RunReport(
    bool IsSuccess,
    FailureCode FailureCode,
    int AnalysisTargets,
    int PlannedChanges,
    int Conflicts,
    int RewrittenDocuments,
    IReadOnlyList<string> GeneratedArtifacts,
    FailureSummary? FailureSummary,
    IReadOnlyList<ConflictSummary> ConflictSummaries,
    RiskSummary RiskSummary,
    string? Message);
