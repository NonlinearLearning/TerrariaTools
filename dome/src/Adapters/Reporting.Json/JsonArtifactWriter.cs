using System.Text.Json;
using System.Text.Json.Serialization;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Dome.Adapters.Reporting.Json;

// 将主要运行结果以缩进后的 JSON 形式持久化，
// 方便 CLI 用户和测试直接查看分析、计划与报告内容。
public sealed class JsonArtifactWriter : ApplicationAbstractions.IArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WritePlanAsync(string path, CorePlanning.AuditPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
    }

    public async Task WriteAnalysisAsync(string path, CoreAnalysis.AnalysisResultModel view, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(view, JsonOptions), cancellationToken);
    }

    public async Task WriteReportAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken)
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





