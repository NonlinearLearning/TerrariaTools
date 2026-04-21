using Domain.Analysis.Engine.Core;

namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 表示可序列化的切片边。
/// </summary>
public sealed class SliceEdge
{
    /// <summary>
    /// 从 CPG 边创建切片边。
    /// </summary>
    /// <param name="edge">CPG 边。</param>
    public SliceEdge(CpgEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        SourceId = edge.SourceId;
        TargetId = edge.TargetId;
        Kind = edge.Kind.ToString();
        Label = edge.Label;
    }

    /// <summary>
    /// 获取起点编号。
    /// </summary>
    public long SourceId { get; }

    /// <summary>
    /// 获取终点编号。
    /// </summary>
    public long TargetId { get; }

    /// <summary>
    /// 获取边类型。
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// 获取边标签。
    /// </summary>
    public string Label { get; }
}
