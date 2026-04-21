using Domain.Common;

namespace Domain.Propagation;

/// <summary>
/// 表示传播轨迹。
/// </summary>
public sealed class PropagationTrace
{
    public PropagationTrace(string sourceName, string targetName, string reason, int stepOrder)
        : this(
            Domain.Common.TargetName.Create(sourceName),
            Domain.Common.TargetName.Create(targetName),
            reason,
            stepOrder)
    {
    }

    public PropagationTrace(
        TargetName sourceName,
        TargetName targetName,
        string reason,
        int stepOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (stepOrder <= 0)
        {
            throw new InvalidOperationException("传播轨迹的步骤序号必须从 1 开始。");
        }

        SourceNameValue = sourceName;
        TargetNameValue = targetName;
        Reason = reason.Trim();
        StepOrder = stepOrder;
    }

    public string SourceName => SourceNameValue.Value;

    public TargetName SourceNameValue { get; }

    public string TargetName => TargetNameValue.Value;

    public TargetName TargetNameValue { get; }

    public string Reason { get; }

    public int StepOrder { get; }
}
