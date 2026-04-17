using Application.Contracts.Analysis;
using Domain.Marking;

namespace Application.Contracts.Marking;

/// <summary>
/// 创建规则命中目标请求。
/// </summary>
public sealed class CreateRuleTargetRequest
{
    public Guid SnapshotId { get; init; }

    public string RuleCode { get; init; } = string.Empty;

    public CandidateReason CandidateReason { get; init; } = CandidateReason.Unknown;

    public MinimumNodeDto Node { get; init; } = new();

    public string? Note { get; init; }
}
