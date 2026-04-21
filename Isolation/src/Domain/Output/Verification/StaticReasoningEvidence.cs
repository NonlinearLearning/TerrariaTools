using Domain.Common;

namespace Domain.Output.Verification;

/// <summary>
/// 表示静态推理证据。
/// </summary>
public sealed class StaticReasoningEvidence
{
    public StaticReasoningEvidence(string subjectName, string summary)
        : this(TargetName.Create(subjectName), summary)
    {
    }

    public StaticReasoningEvidence(TargetName subjectName, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        SubjectNameValue = subjectName;
        Summary = summary.Trim();
    }

    public string SubjectName => SubjectNameValue.Value;

    public TargetName SubjectNameValue { get; }

    public string Summary { get; }
}
