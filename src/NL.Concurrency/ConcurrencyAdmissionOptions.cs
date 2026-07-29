namespace NL.Concurrency;

/// <summary>
/// Bounds one runtime-local admission coordinator. Values are deliberately
/// explicit so resource policy cannot be hidden in a scheduler implementation.
/// </summary>
public sealed record ConcurrencyAdmissionOptions(
    int MaxConcurrentOperations,
    int MaxReservedItemCount,
    long MaxReservedByteCount,
    int LatencySensitiveWeight = 3,
    int ThroughputWeight = 1,
    TimeSpan? MaximumThroughputWait = null)
{
    public TimeSpan EffectiveMaximumThroughputWait =>
        MaximumThroughputWait ?? TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (MaxConcurrentOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentOperations));
        }

        if (MaxReservedItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReservedItemCount));
        }

        if (MaxReservedByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReservedByteCount));
        }

        if (LatencySensitiveWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LatencySensitiveWeight));
        }

        if (ThroughputWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThroughputWeight));
        }

        if (EffectiveMaximumThroughputWait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumThroughputWait));
        }
    }
}

/// <summary>
/// Declares the resources an independent operation retains while admitted.
/// </summary>
public sealed record ConcurrencyAdmissionRequest(
    ConcurrencyWorkClass WorkClass,
    int ReservedItemCount = 1,
    long ReservedByteCount = 0)
{
    internal void Validate(ConcurrencyAdmissionOptions options)
    {
        if (ReservedItemCount <= 0 || ReservedItemCount > options.MaxReservedItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ReservedItemCount));
        }

        if (ReservedByteCount < 0 || ReservedByteCount > options.MaxReservedByteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ReservedByteCount));
        }
    }
}
