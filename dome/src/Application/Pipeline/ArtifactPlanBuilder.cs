namespace TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 描述单次运行在当前终态下应输出的产物集合。
/// </summary>
/// <param name="WriteAnalysis">指示是否写出分析结果。</param>
/// <param name="WritePlan">指示是否写出审计计划。</param>
/// <param name="WriteReport">指示是否写出运行报告。</param>
/// <param name="GeneratedArtifacts">应生成的产物路径集合。</param>
/// <param name="RewrittenDocuments">应视为已重写的文档路径集合。</param>
public sealed record ArtifactPlan(
    bool WriteAnalysis,
    bool WritePlan,
    bool WriteReport,
    IReadOnlyList<string> GeneratedArtifacts,
    IReadOnlyList<string> RewrittenDocuments);

/// <summary>
/// 统一构建不同终态下的产物计划。
/// </summary>
public sealed class ArtifactPlanBuilder
{
    /// <summary>
    /// 构建工作区加载失败时的产物计划。
    /// </summary>
    /// <returns>工作区加载失败对应的产物计划。</returns>
    public ArtifactPlan BuildWorkspaceLoadFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建分析失败时的产物计划。
    /// </summary>
    /// <returns>分析失败对应的产物计划。</returns>
    public ArtifactPlan BuildAnalysisFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建仅分析模式成功时的产物计划。
    /// </summary>
    /// <returns>仅分析成功对应的产物计划。</returns>
    public ArtifactPlan BuildAnalyzeOnlySuccess() =>
        new(true, false, true, new[] { "analysis.json", "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建计划编译失败时的产物计划。
    /// </summary>
    /// <returns>计划编译失败对应的产物计划。</returns>
    public ArtifactPlan BuildPlanCompileFailure() =>
        new(false, false, true, new[] { "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建仅计划模式成功时的产物计划。
    /// </summary>
    /// <returns>仅计划成功对应的产物计划。</returns>
    public ArtifactPlan BuildPlanOnlySuccess() =>
        new(false, true, true, new[] { "audit-plan.json", "report.json" }, Array.Empty<string>());

    /// <summary>
    /// 构建重写失败时的产物计划。
    /// </summary>
    /// <param name="rewrittenDocuments">已成功写出的重写文档路径。</param>
    /// <returns>重写失败对应的产物计划。</returns>
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
    /// 构建标准模式成功时的产物计划。
    /// </summary>
    /// <param name="rewrittenDocuments">已成功写出的重写文档路径。</param>
    /// <returns>标准模式成功对应的产物计划。</returns>
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
