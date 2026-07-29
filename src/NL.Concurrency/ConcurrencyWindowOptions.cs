namespace NL.Concurrency;

public sealed record ConcurrencyWindowOptions(
    int MaxDegreeOfParallelism,
    int? ReorderAllowance = null,
    int MaxCompletedRecordCount = int.MaxValue,
    ConcurrencyWorkClass WorkClass = ConcurrencyWorkClass.Throughput,
    long EstimatedRetainedBytesPerItem = 0)
{
    public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

    public int EffectiveReorderAllowance =>
        Math.Max(1, ReorderAllowance ?? EffectiveMaxDegreeOfParallelism);

    public int EffectiveMaxCompletedRecordCount => Math.Max(1, MaxCompletedRecordCount);

    public ConcurrencyAdmissionRequest CreateAdmissionRequest(int sourceCount)
    {
        Validate();

        var reservedItemCount = Math.Min(Math.Max(1, sourceCount), EffectiveReorderAllowance);
        var reservedByteCount = checked(EstimatedRetainedBytesPerItem * reservedItemCount);
        return new ConcurrencyAdmissionRequest(WorkClass, reservedItemCount, reservedByteCount);
    }

    internal void Validate()
    {
        if (ReorderAllowance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReorderAllowance));
        }

        if (EstimatedRetainedBytesPerItem < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EstimatedRetainedBytesPerItem));
        }
    }
}
