namespace TerrariaTools.Dome.Application;

/// <summary>
/// 产物写入计划。
/// </summary>
public sealed record ArtifactPlan(
    bool WriteAnalysis,
    bool WritePlan,
    bool WriteReport,
    IReadOnlyList<string> GeneratedArtifacts,
    IReadOnlyList<string> RewrittenDocuments);

/// <summary>
/// 产物写入计划构建器。
/// </summary>
public sealed class ArtifactPlanBuilder
{
    /// <summary>
    /// 构建工作区加载失败场景的产物计划。
    /// </summary>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildWorkspaceLoadFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建分析失败场景的产物计划。
    /// </summary>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildAnalysisFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建仅分析成功场景的产物计划。
    /// </summary>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildAnalyzeOnlySuccess() =>
        new(true, false, true, new[] { "analysis.json", "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建计划编译失败场景的产物计划。
    /// </summary>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildPlanCompileFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建仅计划成功场景的产物计划。
    /// </summary>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildPlanOnlySuccess() =>
        new(false, true, true, new[] { "audit-plan.json", "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建改写失败场景的产物计划。
    /// </summary>
    /// <param name="rewrittenDocuments">已改写文档列表。</param>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildRewriteFailure(IReadOnlyList<string> rewrittenDocuments) =>
        new(
            false,
            true,
            true,
            new[] { "audit-plan.json" }
                .Concat(rewrittenDocuments)
                .Append("report.json")
                .ToArray(),
            rewrittenDocuments.ToArray());

    /// <summary>
    /// 构建标准成功场景的产物计划。
    /// </summary>
    /// <param name="rewrittenDocuments">已改写文档列表。</param>
    /// <returns>产物计划。</returns>
    public ArtifactPlan BuildStandardSuccess(IReadOnlyList<string> rewrittenDocuments) =>
        new(
            false,
            true,
            true,
            new[] { "audit-plan.json" }
                .Concat(rewrittenDocuments)
                .Append("report.json")
                .ToArray(),
            rewrittenDocuments.ToArray());
}
