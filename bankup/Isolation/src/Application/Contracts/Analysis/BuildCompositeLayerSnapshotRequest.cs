namespace Application.Contracts.Analysis;

/// <summary>
/// 构建组成层快照请求。
/// </summary>
public sealed class BuildCompositeLayerSnapshotRequest
{
    public Guid RunCorrelationId { get; init; }

    /// <summary>
    /// 获取或设置工作区标识。
    /// </summary>
    public Guid WorkspaceContextId { get; init; }

    /// <summary>
    /// 获取或设置组成名称。
    /// </summary>
    public string CompositionName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置分析深度。
    /// </summary>
    public int Depth { get; init; } = 2;

    /// <summary>
    /// 获取或设置组成层名称。
    /// </summary>
    public IReadOnlyCollection<string> LayerNames { get; init; } = Array.Empty<string>();
}
