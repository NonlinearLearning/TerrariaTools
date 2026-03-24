using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 定义执行源码重写的能力。
/// </summary>
public interface IRewriteExecutor
{
    /// <summary>
    /// 按给定输入执行一次重写。
    /// </summary>
    /// <param name="input">重写输入。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>重写结果。</returns>
    Task<RewriteOutput> ExecuteAsync(RewriteInput input, CancellationToken cancellationToken);
}

/// <summary>
/// 定义写出计划、分析结果和报告的能力。
/// </summary>
public interface IArtifactWriter
{
    /// <summary>
    /// 将审计计划写入指定路径。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="plan">待写出的审计计划。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task WritePlanAsync(string path, CorePlanning.AuditPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// 将分析结果写入指定路径。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="view">待写出的分析视图。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task WriteAnalysisAsync(string path, CoreAnalysis.AnalysisResultModel view, CancellationToken cancellationToken);

    /// <summary>
    /// 将运行报告写入指定路径。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="report">待写出的报告。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken);
}
