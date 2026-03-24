using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class TestWorkspaceCompatibilityBuilder
{
    private readonly List<CoreAnalysis.SourceDocument> _documents = [];
    private ModelPrimitives.WorkspaceLoadMode _loadMode = ModelPrimitives.WorkspaceLoadMode.SourceOnly;
    private string _requestedPrimaryLoader = "TestWorkspaceCompatibilityBuilder";
    private bool _fallbackUsed;

    public TestWorkspaceCompatibilityBuilder AddDocument(CoreAnalysis.SourceDocument document)
    {
        _documents.Add(document);
        return this;
    }

    public TestWorkspaceCompatibilityBuilder AddDocument(Action<SourceDocumentCompatibilityBuilder> configure)
    {
        var builder = new SourceDocumentCompatibilityBuilder();
        configure(builder);
        _documents.Add(builder.Build());
        return this;
    }

    public TestWorkspaceCompatibilityBuilder WithLoadMode(ModelPrimitives.WorkspaceLoadMode loadMode)
    {
        _loadMode = loadMode;
        return this;
    }

    public TestWorkspaceCompatibilityBuilder WithRequestedPrimaryLoader(string requestedPrimaryLoader)
    {
        _requestedPrimaryLoader = requestedPrimaryLoader;
        return this;
    }

    public TestWorkspaceCompatibilityBuilder WithFallbackUsed(bool fallbackUsed)
    {
        _fallbackUsed = fallbackUsed;
        return this;
    }

    public ApplicationAbstractions.WorkspaceLoadResult Build()
    {
        var entryPath = _documents.FirstOrDefault()?.SourcePath ?? "input";
        var rootPath = Path.GetDirectoryName(entryPath) ?? string.Empty;
        return ApplicationAbstractions.WorkspaceLoadResult.Success(
            new CoreAnalysis.AnalysisInput(
                new CoreAnalysis.SourceDocumentSet(entryPath, rootPath, _documents.ToArray()),
                CoreAnalysis.AnalysisInputMode.SourceOnly),
            _loadMode,
            _requestedPrimaryLoader,
            _fallbackUsed);
    }
}



