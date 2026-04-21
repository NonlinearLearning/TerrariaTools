namespace Domain.Propagation;

/// <summary>
/// 表示影子类携带的传播边界。
/// </summary>
public sealed class ShadowBoundary
{
    private readonly List<ReferenceMapping> referenceMappings = new();

    private ShadowBoundary()
    {
    }

    public IReadOnlyCollection<ReferenceMapping> ReferenceMappings => referenceMappings.AsReadOnly();

    public static ShadowBoundary Create()
    {
        return new ShadowBoundary();
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
}
