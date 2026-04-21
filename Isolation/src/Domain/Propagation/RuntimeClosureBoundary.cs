namespace Domain.Propagation;

/// <summary>
/// 表示最小运行闭包携带的传播边界。
/// </summary>
public sealed class RuntimeClosureBoundary
{
    private readonly List<ReferenceMapping> referenceMappings = new();

    private RuntimeClosureBoundary(ClosureRoot root, ClosureIntegrityStatus integrityStatus)
    {
        Root = root;
        IntegrityStatus = integrityStatus;
    }

    public ClosureRoot Root { get; }

    public ClosureIntegrityStatus IntegrityStatus { get; private set; }

    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => referenceMappings.AsReadOnly();

    public static RuntimeClosureBoundary Create(ClosureRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new RuntimeClosureBoundary(root, ClosureIntegrityStatus.Unknown);
    }

    public void AddReferenceMapping(ReferenceMapping referenceMapping)
    {
        ArgumentNullException.ThrowIfNull(referenceMapping);

        if (referenceMappings.Any(item =>
                string.Equals(item.SourceReference, referenceMapping.SourceReference, StringComparison.Ordinal) &&
                string.Equals(item.TargetReference, referenceMapping.TargetReference, StringComparison.Ordinal)))
        {
            return;
        }

        referenceMappings.Add(referenceMapping);
    }

    public void MarkIntegrity(ClosureIntegrityStatus integrityStatus)
    {
        if (integrityStatus == ClosureIntegrityStatus.Unknown)
        {
            throw new InvalidOperationException("运行闭包边界必须写入明确完整性状态。");
        }

        IntegrityStatus = integrityStatus;
    }
}
