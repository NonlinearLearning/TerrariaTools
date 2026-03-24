using System.Text.Json;
using System.Text.Json.Serialization;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Reporting.Json;

namespace TerrariaTools.Dome.Adapters.Reporting.Json;

public sealed class JsonRunReportStore(JsonArtifactWriter artifactWriter) : IRunReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<StageResult<ModelExecution.RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return StageResult<ModelExecution.RunReport>.Failure(ModelPrimitives.FailureCode.ReportFailed, $"Run report '{path}' was not found.");
        }

        try
        {
            var reportJson = await File.ReadAllTextAsync(path, cancellationToken);
            var report = JsonSerializer.Deserialize<ModelExecution.RunReport>(reportJson, JsonOptions);
            return report == null
                ? StageResult<ModelExecution.RunReport>.Failure(ModelPrimitives.FailureCode.ReportFailed, "report.json could not be deserialized.")
                : StageResult<ModelExecution.RunReport>.Success(report);
        }
        catch (Exception ex)
        {
            return StageResult<ModelExecution.RunReport>.Failure(ModelPrimitives.FailureCode.ReportFailed, ex.Message);
        }
    }

    public async Task SaveAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
    }
}







