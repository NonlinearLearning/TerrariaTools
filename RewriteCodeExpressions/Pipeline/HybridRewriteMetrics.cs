namespace TerrariaTools.RewriteCodeExpressions.Pipeline;

/// <summary>
/// Hybrid 重写统计指标。
/// </summary>
/// <param name="PlanItemCount">重写计划中的项总数。</param>
/// <param name="ExecutedRuleCount">执行的规则总数。</param>
/// <param name="ReplacedNodeCount">被替换的节点总数。</param>
/// <param name="DeletedNodeCount">被删除的节点总数。</param>
public sealed record HybridRewriteMetrics(
    int PlanItemCount,
    int ExecutedRuleCount,
    int ReplacedNodeCount,
    int DeletedNodeCount);
