using Domain.Propagation;

namespace Logic.Propagation;

/// <summary>
/// 表示传播构造结果。
/// </summary>
public sealed class PropagationResolution
{
    /// <summary>
    /// 获取或初始化候选对象。
    /// </summary>
    public ChangeCandidate Candidate { get; init; } = null!;

    /// <summary>
    /// 获取或初始化切片边界。
    /// </summary>
    public SliceBoundary SliceBoundary { get; init; } = null!;

    /// <summary>
    /// 获取传播轨迹集合。
    /// </summary>
    public IReadOnlyCollection<PropagationTrace> PropagationTraces => Candidate.PropagationTraces;

    /// <summary>
    /// 获取或初始化传播事实引用。
    /// </summary>
    public IReadOnlyCollection<PropagationFactReference> FactReferences { get; init; } = Array.Empty<PropagationFactReference>();
}
