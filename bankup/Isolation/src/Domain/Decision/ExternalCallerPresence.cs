namespace Domain.Decision;

/// <summary>
/// 表示外部调用者存在性。
/// </summary>
public sealed class ExternalCallerPresence
{
    private ExternalCallerPresence(IReadOnlyCollection<string> callers)
    {
        Callers = callers;
    }

    public IReadOnlyCollection<string> Callers { get; }

    public bool Exists => Callers.Count > 0;

    public static ExternalCallerPresence None() => new(Array.Empty<string>());

    public static ExternalCallerPresence Detected(IReadOnlyCollection<string> callers) => new(callers);
}
