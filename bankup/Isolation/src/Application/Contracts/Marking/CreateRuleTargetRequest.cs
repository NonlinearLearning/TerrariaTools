using Application.Contracts.Analysis;
using Application.Contracts;

namespace Application.Contracts.Marking;

/// <summary>
/// 创建规则命中目标请求。
/// </summary>
public sealed class CreateRuleTargetRequest
{
    public Guid RunCorrelationId { get; init; }

    public Guid SnapshotId { get; init; }

    public string RuleCode { get; init; } = string.Empty;

    public ContractCandidateReason CandidateReason { get; init; } = ContractCandidateReason.Unknown;

    public MinimumNodeDto Node { get; init; } = new();

    public string? Note { get; init; }
}
