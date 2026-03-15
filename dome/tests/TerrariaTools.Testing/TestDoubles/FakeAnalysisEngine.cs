using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestDoubles;

public sealed class FakeAnalysisEngine : IAnalysisEngine
{
    private readonly AnalysisEngineResult _result;

    public FakeAnalysisEngine(AnalysisEngineResult result)
    {
        _result = result;
    }

    public Task<AnalysisEngineResult> AnalyzeAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken) =>
        Task.FromResult(_result);

    public Task<AnalysisEngineResult> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken) =>
        Task.FromResult(_result);
}
