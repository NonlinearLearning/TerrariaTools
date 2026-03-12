namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

/// <summary>
/// Pass 2 执行摘要。
/// </summary>
public sealed record ExecutionSummary(
    int ExecutedRuleCount,
    int ReplacedNodeCount,
    int DeletedNodeCount);
