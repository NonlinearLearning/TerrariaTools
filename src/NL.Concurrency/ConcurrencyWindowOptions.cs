namespace NL.Concurrency;

public sealed record ConcurrencyWindowOptions(
    int MaxDegreeOfParallelism,
    int? ReorderAllowance = null,
    int MaxCompletedRecordCount = int.MaxValue)
{
    public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

    public int EffectiveReorderAllowance =>
        Math.Max(0, ReorderAllowance ?? EffectiveMaxDegreeOfParallelism);

    public int EffectiveMaxCompletedRecordCount => Math.Max(1, MaxCompletedRecordCount);
}
