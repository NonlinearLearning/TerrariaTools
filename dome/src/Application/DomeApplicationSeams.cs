namespace TerrariaTools.Dome.Application;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;

/// <summary>
/// 重写输出存储接口。
/// </summary>
public interface IRewriteOutputStore
{
    /// <summary>
    /// 保存重写后的源码内容。
    /// </summary>
    /// <param name="outputRootPath">输出根目录。</param>
    /// <param name="relativePath">相对路径。</param>
    /// <param name="rewrittenSource">重写后的源码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken);
}

/// <summary>
/// 基于文件系统的重写输出存储实现。
/// </summary>
public sealed class FileSystemRewriteOutputStore : IRewriteOutputStore
{
    /// <summary>
    /// 将重写源码写入磁盘。
    /// </summary>
    /// <param name="outputRootPath">输出根目录。</param>
    /// <param name="relativePath">相对路径。</param>
    /// <param name="rewrittenSource">重写后的源码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(outputRootPath, "rewritten", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, rewrittenSource, cancellationToken);
    }
}

/// <summary>
/// 产物发射服务接口。
/// </summary>
public interface IArtifactEmissionService
{
    /// <summary>
    /// 按计划输出分析、计划与报告产物。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="artifactPlan">产物输出计划。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="analysisView">分析视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        ModelPlanning.AuditPlan? plan,
        ApplicationAbstractions.RunReport report,
        ModelAnalysis.AnalysisResultModel? analysisView,
        CancellationToken cancellationToken);
}

/// <summary>
/// 默认产物发射服务实现。
/// </summary>
public sealed class ArtifactEmissionService(ApplicationAbstractions.IArtifactWriter artifactWriter) : IArtifactEmissionService
{
    /// <summary>
    /// 根据产物计划执行输出写入。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="artifactPlan">产物输出计划。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="analysisView">分析视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        ModelPlanning.AuditPlan? plan,
        ApplicationAbstractions.RunReport report,
        ModelAnalysis.AnalysisResultModel? analysisView,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputPath);

        if (artifactPlan.WriteAnalysis && analysisView != null)
        {
            await artifactWriter.WriteAnalysisAsync(Path.Combine(outputPath, "analysis.json"), analysisView, cancellationToken);
        }

        if (artifactPlan.WritePlan && plan != null)
        {
            await artifactWriter.WritePlanAsync(Path.Combine(outputPath, "audit-plan.json"), plan, cancellationToken);
        }

        if (artifactPlan.WriteReport)
        {
            await artifactWriter.WriteReportAsync(Path.Combine(outputPath, "report.json"), report, cancellationToken);
        }
    }
}
