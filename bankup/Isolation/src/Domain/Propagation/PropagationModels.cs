using Domain.Common;

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

/// <summary>
/// 表示传播轨迹。
/// </summary>
public sealed class PropagationTrace
{
    public PropagationTrace(string sourceName, string targetName, string reason, int stepOrder)
        : this(Domain.Common.TargetName.Create(sourceName), Domain.Common.TargetName.Create(targetName), reason, stepOrder)
    {
    }

    public PropagationTrace(TargetName sourceName, TargetName targetName, string reason, int stepOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (stepOrder <= 0)
        {
            throw new InvalidOperationException("传播轨迹的步骤序号必须从 1 开始。");
        }

        SourceNameValue = sourceName;
        TargetNameValue = targetName;
        Reason = reason.Trim();
        StepOrder = stepOrder;
    }

    public string SourceName => SourceNameValue.Value;

    public TargetName SourceNameValue { get; }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

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
        string normalizedSourceReference = sourceReference.Trim();
        string normalizedTargetReference = targetReference.Trim();
        if (string.Equals(normalizedSourceReference, normalizedTargetReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("传播边界引用映射不能指向完全相同的源与目标。");
        }

        SourceReference = normalizedSourceReference;
        TargetReference = normalizedTargetReference;
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
