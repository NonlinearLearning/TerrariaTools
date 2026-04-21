namespace Domain.Decision;

/// <summary>
/// 表示外部契约暴露情况。
/// </summary>
public sealed class ContractExposure
{
    private ContractExposure(bool isPublicSurface, string source)
    {
        IsPublicSurface = isPublicSurface;
        Source = source;
    }

    public bool IsPublicSurface { get; }

    public string Source { get; }

    public static ContractExposure InternalOnly(string source) => new(false, source);

    public static ContractExposure PublicSurface(string source) => new(true, source);
}
