using Domain.Common.Events;

namespace Domain.Output.Verification.Events;

/// <summary>
/// 表示验证证据已采集。
/// </summary>
public sealed class VerificationEvidenceCollectedDomainEvent : DomainEventBase
{
    public VerificationEvidenceCollectedDomainEvent(
        Guid verificationEvidenceId,
        Guid correlationId,
        string riskLevel,
        bool requiresManualReview)
        : base(
            "VerificationEvidenceCollected",
            "Verification",
            verificationEvidenceId,
            correlationId,
            null,
            $"验证证据已采集：风险级别 {riskLevel}，人工复核 {requiresManualReview}。")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(riskLevel);
        RiskLevel = riskLevel.Trim();
        RequiresManualReview = requiresManualReview;
    }

    public string RiskLevel { get; }

    public bool RequiresManualReview { get; }
}
