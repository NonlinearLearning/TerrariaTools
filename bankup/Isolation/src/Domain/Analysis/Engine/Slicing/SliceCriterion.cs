namespace Domain.Analysis.Engine.Slicing;

/// <summary>
/// 表示切片起点条件。
/// </summary>
/// <param name="NodeId">切片基准节点编号。</param>
public sealed record SliceCriterion(long NodeId);
