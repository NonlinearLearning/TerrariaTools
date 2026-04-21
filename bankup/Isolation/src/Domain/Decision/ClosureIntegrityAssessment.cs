namespace Domain.Decision;

/// <summary>
/// 表示闭包完整性评估。
/// </summary>
public sealed class ClosureIntegrityAssessment
{
    private ClosureIntegrityAssessment(bool isBroken, string summary)
    {
        IsBroken = isBroken;
        Summary = summary;
    }

    public bool IsBroken { get; }

    public string Summary { get; }

    public static ClosureIntegrityAssessment Verified(string summary) => new(false, summary);

    public static ClosureIntegrityAssessment Broken(string summary) => new(true, summary);
}
