using Application.Contracts.Analysis;
using Domain.Marking;

namespace Application.Contracts.Marking;

/// <summary>
/// 规则命中目标 DTO。
/// </summary>
public sealed class RuleTargetDto
{
    public Guid Id { get; init; }

    public Guid SnapshotId { get; init; }

    public string RuleCode { get; init; } = string.Empty;

    public CandidateReason CandidateReason { get; init; }

    public MinimumNodeDto Node { get; init; } = new();

    public string? Note { get; init; }
}
