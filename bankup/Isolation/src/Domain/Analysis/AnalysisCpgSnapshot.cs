using Domain.Common;
using Domain.Analysis.Events;

namespace Domain.Analysis;

/// <summary>
/// 表示分析阶段的 CPG 快照。
/// </summary>
public sealed class AnalysisCpgSnapshot : AggregateRoot<Guid>
{
    private readonly List<MinimumNode> nodes = new();
    private readonly List<CpgFlow> flows = new();
    private readonly List<CpgCall> calls = new();
    private bool isCompleted;
    private bool factsPublished;

    private AnalysisCpgSnapshot(
        Guid id,
        Guid workspaceContextId,
        MinimumAnalysisTarget minimumTarget,
        TargetName entrySymbol,
        int depth)
        : base(id)
    {
        WorkspaceContextId = workspaceContextId;
        MinimumTarget = minimumTarget;
        EntrySymbolValue = entrySymbol;
        Depth = depth;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 获取所属工作区标识。
    /// </summary>
    public Guid WorkspaceContextId { get; }

    /// <summary>
    /// 获取最小分析目标。
    /// </summary>
    public MinimumAnalysisTarget MinimumTarget { get; }

    /// <summary>
    /// 获取入口符号。
    /// </summary>
    public string EntrySymbol => EntrySymbolValue.Value;

    /// <summary>
    /// 获取入口符号值对象。
    /// </summary>
    public TargetName EntrySymbolValue { get; }

    /// <summary>
    /// 获取构建深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 获取节点集合。
    /// </summary>
    public IReadOnlyCollection<MinimumNode> Nodes => nodes.AsReadOnly();

    /// <summary>
    /// 获取控制流集合。
    /// </summary>
    public IReadOnlyCollection<CpgFlow> Flows => flows.AsReadOnly();

    /// <summary>
    /// 获取调用集合。
    /// </summary>
    public IReadOnlyCollection<CpgCall> Calls => calls.AsReadOnly();

    public bool IsCompleted => isCompleted;

    /// <summary>
    /// 创建 CPG 快照。
    /// </summary>
    public static AnalysisCpgSnapshot Create(
        Guid workspaceContextId,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
    {
        return Create(workspaceContextId, minimumTarget, TargetName.Create(entrySymbol), depth);
    }

    public static AnalysisCpgSnapshot Create(
        Guid workspaceContextId,
        MinimumAnalysisTarget minimumTarget,
        TargetName entrySymbol,
        int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        return new AnalysisCpgSnapshot(Guid.NewGuid(), workspaceContextId, minimumTarget, entrySymbol, depth);
    }

    /// <summary>
    /// 增加节点。
    /// </summary>
    public void AddNode(MinimumNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureMutable();

        if (nodes.Any(item => item.NodeId == node.NodeId))
        {
            return;
        }

        nodes.Add(node);
    }

    /// <summary>
    /// 增加控制流。
    /// </summary>
    public void AddFlow(CpgFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        EnsureMutable();
        bool exists = flows.Any(item =>
            string.Equals(item.FromNodeId, flow.FromNodeId, StringComparison.Ordinal) &&
            string.Equals(item.ToNodeId, flow.ToNodeId, StringComparison.Ordinal) &&
            item.FlowKind == flow.FlowKind);
        if (exists)
        {
            return;
        }

        flows.Add(flow);
    }

    /// <summary>
    /// 增加调用边。
    /// </summary>
    public void AddCall(CpgCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        EnsureMutable();
        bool exists = calls.Any(item =>
            string.Equals(item.FromNodeId, call.FromNodeId, StringComparison.Ordinal) &&
            string.Equals(item.ToNodeId, call.ToNodeId, StringComparison.Ordinal) &&
            item.CallKind == call.CallKind);
        if (exists)
        {
            return;
        }

        calls.Add(call);
    }

    public void Complete(Guid correlationId)
    {
        ValidateReadyToComplete();
        isCompleted = true;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("AnalysisSnapshotBuilt", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new AnalysisSnapshotBuiltDomainEvent(
            Id,
            resolvedCorrelationId,
            EntrySymbolValue,
            Depth));
    }

    public void PublishFacts(Guid correlationId)
    {
        if (!isCompleted)
        {
            throw new InvalidOperationException("分析快照完成前不能发布事实。");
        }

        factsPublished = true;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("ProgramFactPublished", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new ProgramFactPublishedDomainEvent(
            Id,
            resolvedCorrelationId,
            EntrySymbolValue,
            nodes.Count + flows.Count + calls.Count));
    }

    public bool HasPublishedFacts()
    {
        return factsPublished;
    }

    public void EnsureMutable()
    {
        if (isCompleted)
        {
            throw new InvalidOperationException("分析快照完成后不能继续写入。");
        }
    }

    public void ValidateReadyToComplete()
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("分析快照至少需要一个节点才能完成。");
        }
    }
}
