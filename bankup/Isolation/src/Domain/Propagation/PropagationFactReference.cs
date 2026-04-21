namespace Domain.Propagation;

/// <summary>
/// 表示传播所引用的事实来源。
/// </summary>
public sealed class PropagationFactReference
{
    public PropagationFactReference(string sourceNodeId, string targetNodeId, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        SourceNodeId = sourceNodeId.Trim();
        TargetNodeId = targetNodeId.Trim();
        Kind = kind.Trim();
    }

    public string SourceNodeId { get; }

    public string TargetNodeId { get; }

    public string Kind { get; }
}
