namespace Domain.Analysis.Engine.Query;

/// <summary>
/// 表示一次最小数据流查询任务。
/// </summary>
/// <param name="SinkNodeId">当前任务的 sink 节点。</param>
public sealed record QueryTask(long SinkNodeId);
