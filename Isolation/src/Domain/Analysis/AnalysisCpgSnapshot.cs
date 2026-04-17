using Domain.Common;

namespace Domain.Analysis;

/// <summary>
/// 表示分析阶段的 CPG 快照。
/// </summary>
public sealed class AnalysisCpgSnapshot : AggregateRoot<Guid>
{
    private readonly List<MinimumNode> nodes = new();
    private readonly List<CpgFlow> flows = new();
    private readonly List<CpgCall> calls = new();

    private AnalysisCpgSnapshot(
        Guid id,
        Guid workspaceContextId,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
        : base(id)
    {
        WorkspaceContextId = workspaceContextId;
        MinimumTarget = minimumTarget;
        EntrySymbol = entrySymbol;
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
    public string EntrySymbol { get; }

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

    /// <summary>
    /// 创建 CPG 快照。
    /// </summary>
    public static AnalysisCpgSnapshot Create(
        Guid workspaceContextId,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySymbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        return new AnalysisCpgSnapshot(Guid.NewGuid(), workspaceContextId, minimumTarget, entrySymbol.Trim(), depth);
    }

    /// <summary>
    /// 增加节点。
    /// </summary>
    public void AddNode(MinimumNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

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
        flows.Add(flow);
    }

    /// <summary>
    /// 增加调用边。
    /// </summary>
    public void AddCall(CpgCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        calls.Add(call);
    }
}
