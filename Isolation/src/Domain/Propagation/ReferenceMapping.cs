namespace Domain.Propagation;

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
        if (string.Equals(
                normalizedSourceReference,
                normalizedTargetReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("传播边界引用映射不能指向完全相同的源与目标。");
        }

        SourceReference = normalizedSourceReference;
        TargetReference = normalizedTargetReference;
    }

    public string SourceReference { get; }

    public string TargetReference { get; }
}
