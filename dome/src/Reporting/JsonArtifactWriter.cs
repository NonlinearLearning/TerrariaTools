using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerrariaTools.Dome.Reporting;

using TerrariaTools.Dome.Core;

/// <summary>
/// JSON 构件写入器。
/// </summary>
public sealed class JsonArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 异步写入审计计划。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="plan">审计计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task WritePlanAsync(string path, AuditPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
    }

    /// <summary>
    /// 异步写入分析视图。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="view">分析视图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task WriteAnalysisAsync(string path, AnalysisResultModel view, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(view, JsonOptions), cancellationToken);
    }

    /// <summary>
    /// 异步写入运行报告。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
    }
}
