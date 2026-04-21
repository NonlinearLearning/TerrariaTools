using Domain.Common.Events;
using Domain.Common;

namespace Domain.Analysis.Events;

/// <summary>
/// 表示分析快照已构建完成。
/// </summary>
public sealed class AnalysisSnapshotBuiltDomainEvent : DomainEventBase
{
    public AnalysisSnapshotBuiltDomainEvent(
        Guid snapshotId,
        Guid correlationId,
        string entrySymbol,
        int depth)
        : this(snapshotId, correlationId, Domain.Common.TargetName.Create(entrySymbol), depth)
    {
    }

    public AnalysisSnapshotBuiltDomainEvent(
        Guid snapshotId,
        Guid correlationId,
        TargetName entrySymbol,
        int depth)
        : base(
            "AnalysisSnapshotBuilt",
            "ProgramFact",
            snapshotId,
            correlationId,
            null,
            $"分析快照已构建：入口 {entrySymbol.Value}，深度 {depth}。")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        EntrySymbolValue = entrySymbol;
        Depth = depth;
    }

    public string EntrySymbol => EntrySymbolValue.Value;

    public TargetName EntrySymbolValue { get; }

    public int Depth { get; }
}
