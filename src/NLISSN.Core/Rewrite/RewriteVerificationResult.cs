namespace NLISSN.Core.Rewrite;

/// <summary>
/// Captures report-mode rewrite verification evidence without mutating rewrite output.
/// </summary>
public sealed record RewriteVerificationResult(
    IReadOnlyList<RewriteVerificationFailure> Failures,
    IReadOnlyList<AnalysisDiagnostic> UnexpectedDiagnostics)
{
    public static RewriteVerificationResult Success { get; } = new(
        Array.Empty<RewriteVerificationFailure>(),
        Array.Empty<AnalysisDiagnostic>());

    public bool IsSuccess => Failures.Count == 0;
}

/// <summary>
/// Identifies a verification failure and the decision context that produced it.
/// </summary>
public sealed record RewriteVerificationFailure(
    RewriteVerificationFailureCode Code,
    string Message,
    string? RuleId,
    string? FilePath,
    int? AnchorStart);

public enum RewriteVerificationFailureCode
{
    MissingReplacementWitness,
    UnauthorizedPlanOperation,
    UnexpectedDiagnostic,
    ControlFlowEffectViolation,
}
