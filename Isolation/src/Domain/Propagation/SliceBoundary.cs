namespace Domain.Propagation;

/// <summary>
/// 表示切片传播边界。
/// </summary>
public sealed class SliceBoundary
{
    public SliceBoundary(
        string boundaryName,
        SliceDirection direction,
        int maxDepth,
        bool includeExternalReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryName);
        if (direction == SliceDirection.Unknown)
        {
            throw new InvalidOperationException("切片边界必须声明明确传播方向。");
        }

        if (maxDepth <= 0)
        {
            throw new InvalidOperationException("切片边界的最大深度必须大于 0。");
        }

        BoundaryName = boundaryName.Trim();
        Direction = direction;
        MaxDepth = maxDepth;
        IncludeExternalReferences = includeExternalReferences;
    }

    public string BoundaryName { get; }

    public SliceDirection Direction { get; }

    public int MaxDepth { get; }

    public bool IncludeExternalReferences { get; }
}
