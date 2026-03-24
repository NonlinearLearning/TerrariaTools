namespace TerrariaTools.Dome.Application.Host;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

/// <summary>
/// 定义重写输出的持久化能力。
/// </summary>
public interface IRewriteOutputStore
{
    /// <summary>
    /// 保存单个重写后文档。
    /// </summary>
    /// <param name="outputRootPath">输出根目录。</param>
    /// <param name="relativePath">文档相对路径。</param>
    /// <param name="rewrittenSource">重写后的源码文本。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken);
}

/// <summary>
/// 使用文件系统保存重写输出。
/// </summary>
public sealed class FileSystemRewriteOutputStore : IRewriteOutputStore
{
    /// <summary>
    /// 将重写结果保存到输出目录下的 <c>rewritten</c> 子目录。
    /// </summary>
    /// <param name="outputRootPath">输出根目录。</param>
    /// <param name="relativePath">文档相对路径。</param>
    /// <param name="rewrittenSource">重写后的源码文本。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public async Task SaveAsync(string outputRootPath, string relativePath, string rewrittenSource, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(outputRootPath, "rewritten", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, rewrittenSource, cancellationToken);
    }
}

/// <summary>
/// 定义运行产物的统一输出能力。
/// </summary>
public interface IArtifactEmissionService
{
    /// <summary>
    /// 按产物计划输出分析、计划和报告文件。
    /// </summary>
    /// <param name="outputPath">输出目录。</param>
    /// <param name="artifactPlan">产物计划。</param>
    /// <param name="plan">可选的审计计划。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="analysisView">可选的分析结果视图。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        CorePlanning.AuditPlan? plan,
        ModelExecution.RunReport report,
        CoreAnalysis.AnalysisResultModel? analysisView,
        CancellationToken cancellationToken);
}

/// <summary>
/// 使用产物写入器输出运行产物。
/// </summary>
/// <param name="artifactWriter">底层产物写入器。</param>
public sealed class ArtifactEmissionService(ApplicationAbstractions.IArtifactWriter artifactWriter) : IArtifactEmissionService
{
    /// <summary>
    /// 按产物计划输出分析、计划和报告文件。
    /// </summary>
    /// <param name="outputPath">输出目录。</param>
    /// <param name="artifactPlan">产物计划。</param>
    /// <param name="plan">可选的审计计划。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="analysisView">可选的分析结果视图。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public async Task EmitAsync(
        string outputPath,
        ArtifactPlan artifactPlan,
        CorePlanning.AuditPlan? plan,
        ModelExecution.RunReport report,
        CoreAnalysis.AnalysisResultModel? analysisView,
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
