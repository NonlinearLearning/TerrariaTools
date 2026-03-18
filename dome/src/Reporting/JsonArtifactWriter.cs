using System.Text.Json;
using System.Text.Json.Serialization;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;

namespace TerrariaTools.Dome.Reporting;

/// <summary>
/// JSON 构件写入器。
/// </summary>
public sealed class JsonArtifactWriter : ApplicationAbstractions.IArtifactWriter
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
    public async Task WritePlanAsync(string path, ModelPlanning.AuditPlan plan, CancellationToken cancellationToken)
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
    public async Task WriteAnalysisAsync(string path, ModelAnalysis.AnalysisResultModel view, CancellationToken cancellationToken)
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
    public async Task WriteReportAsync(string path, ApplicationAbstractions.RunReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
    }

    public async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
    }
}
