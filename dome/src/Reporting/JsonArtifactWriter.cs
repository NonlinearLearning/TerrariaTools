using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerrariaTools.Dome.Reporting;

using TerrariaTools.Dome.Core;

public sealed class JsonArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WritePlanAsync(string path, AuditPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
    }

    public async Task WriteAnalysisAsync(string path, AnalysisView view, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(view, JsonOptions), cancellationToken);
    }

    public async Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
    }
}
