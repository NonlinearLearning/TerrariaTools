namespace TerrariaTools.Dome.Adapters.Reporting.Json;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;
using TerrariaTools.Dome.Adapters.Reporting.Json;

public sealed class JsonShadowExtractionReportStore(JsonArtifactWriter artifactWriter) : IShadowExtractionReportStore
{
    public Task SaveAsync(string path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken) =>
        artifactWriter.WriteJsonAsync(path, report, cancellationToken);
}




