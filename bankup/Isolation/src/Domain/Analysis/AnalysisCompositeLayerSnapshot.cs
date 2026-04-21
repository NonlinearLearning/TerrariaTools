using Domain.Common;
using Domain.Analysis.Events;

namespace Domain.Analysis;

/// <summary>
/// 表示组成层分析快照。
/// </summary>
public sealed class AnalysisCompositeLayerSnapshot : AggregateRoot<Guid>
{
    private readonly List<string> layerNames = new();
    private readonly List<MinimumNode> nodes = new();
    private bool isCompleted;
    private bool factsPublished;

    private AnalysisCompositeLayerSnapshot(Guid id, Guid workspaceContextId, string compositionName, int depth)
        : base(id)
    {
        WorkspaceContextId = workspaceContextId;
        CompositionName = compositionName;
        Depth = depth;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 获取工作区标识。
    /// </summary>
    public Guid WorkspaceContextId { get; }

    /// <summary>
    /// 获取组成名称。
    /// </summary>
    public string CompositionName { get; }

    /// <summary>
    /// 获取分析深度。
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 获取层名称集合。
    /// </summary>
    public IReadOnlyCollection<string> LayerNames => layerNames.AsReadOnly();

    /// <summary>
    /// 获取节点集合。
    /// </summary>
    public IReadOnlyCollection<MinimumNode> Nodes => nodes.AsReadOnly();

    public bool IsCompleted => isCompleted;

    /// <summary>
    /// 创建组成层快照。
    /// </summary>
    public static AnalysisCompositeLayerSnapshot Create(Guid workspaceContextId, string compositionName, int depth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        return new AnalysisCompositeLayerSnapshot(Guid.NewGuid(), workspaceContextId, compositionName.Trim(), depth);
    }

    /// <summary>
    /// 增加层名称。
    /// </summary>
    public void AddLayer(string layerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        EnsureMutable();
        string normalizedLayerName = layerName.Trim();

        if (layerNames.Contains(normalizedLayerName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        layerNames.Add(normalizedLayerName);
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
            CompositionName,
            Depth));
    }

    public void PublishFacts(Guid correlationId)
    {
        if (!isCompleted)
        {
            throw new InvalidOperationException("组合层快照完成前不能发布事实。");
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
            CompositionName,
            nodes.Count + layerNames.Count));
    }

    public bool HasPublishedFacts()
    {
        return factsPublished;
    }

    public void EnsureMutable()
    {
        if (isCompleted)
        {
            throw new InvalidOperationException("组合层快照完成后不能继续写入。");
        }
    }

    public void ValidateReadyToComplete()
    {
        if (nodes.Count == 0 && layerNames.Count == 0)
        {
            throw new InvalidOperationException("组合层快照至少需要节点或层名称才能完成。");
        }
    }
}
