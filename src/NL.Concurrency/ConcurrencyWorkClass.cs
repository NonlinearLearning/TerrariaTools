namespace NL.Concurrency;

/// <summary>
/// Identifies the service class used when independent operations compete for
/// one runtime-local admission budget.
/// </summary>
public enum ConcurrencyWorkClass
{
    LatencySensitive,
    Throughput,
}

/// <summary>
/// Explains why an operation received an admission lease.
/// </summary>
public enum ConcurrencyAdmissionReason
{
    WeightedTurn,
    AgingPromotion,
}
