namespace Domain.Propagation;

/// <summary>
/// 表示切片方向。
/// </summary>
public enum SliceDirection
{
    Unknown = 0,
    Forward = 1,
    Backward = 2,
    Bidirectional = 3,
}

/// <summary>
/// 表示切片传播边界。
/// </summary>
public sealed class SliceBoundary
{
    public SliceBoundary(string boundaryName, SliceDirection direction, int maxDepth, bool includeExternalReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryName);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
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

/// <summary>
/// 表示传播轨迹。
/// </summary>
public sealed class PropagationTrace
{
    public PropagationTrace(string sourceName, string targetName, string reason, int stepOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegative(stepOrder);
        SourceName = sourceName.Trim();
        TargetName = targetName.Trim();
        Reason = reason.Trim();
        StepOrder = stepOrder;
    }

    public string SourceName { get; }

    public string TargetName { get; }

    public string Reason { get; }

    public int StepOrder { get; }
}

/// <summary>
/// 表示引用映射。
/// </summary>
public sealed class ReferenceMapping
{
    public ReferenceMapping(string sourceReference, string targetReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetReference);
        SourceReference = sourceReference.Trim();
        TargetReference = targetReference.Trim();
    }

    public string SourceReference { get; }

    public string TargetReference { get; }
}

/// <summary>
/// 表示闭包根。
/// </summary>
public sealed class ClosureRoot
{
    public ClosureRoot(string className, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ClassName = className.Trim();
        MemberName = memberName.Trim();
    }

    public string ClassName { get; }

    public string MemberName { get; }
}

/// <summary>
/// 表示闭包完整性状态。
/// </summary>
public enum ClosureIntegrityStatus
{
    Unknown = 0,
    Verified = 1,
    Risky = 2,
    Broken = 3,
}
