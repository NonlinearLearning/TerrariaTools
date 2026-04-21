using Domain.Common.Events;
using Domain.Common;

namespace Domain.Analysis.Events;

/// <summary>
/// 表示程序事实已发布。
/// </summary>
public sealed class ProgramFactPublishedDomainEvent : DomainEventBase
{
    public ProgramFactPublishedDomainEvent(
        Guid snapshotId,
        Guid correlationId,
        string subjectName,
        int factCount)
        : this(snapshotId, correlationId, Domain.Common.TargetName.Create(subjectName), factCount)
    {
    }

    public ProgramFactPublishedDomainEvent(
        Guid snapshotId,
        Guid correlationId,
        TargetName subjectName,
        int factCount)
        : base(
            "ProgramFactPublished",
            "ProgramFact",
            snapshotId,
            correlationId,
            null,
            $"程序事实已发布：主体 {subjectName.Value}，事实数 {factCount}。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factCount);
        SubjectNameValue = subjectName;
        FactCount = factCount;
    }

    public string SubjectName => SubjectNameValue.Value;

    public TargetName SubjectNameValue { get; }

    public int FactCount { get; }
}
