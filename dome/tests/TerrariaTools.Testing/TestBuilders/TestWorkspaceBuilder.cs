using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class TestWorkspaceBuilder
{
    private readonly List<SourceDocument> _documents = [];
    private WorkspaceLoadMode _loadMode = WorkspaceLoadMode.SourceOnly;
    private string _requestedPrimaryLoader = "TestWorkspaceBuilder";
    private bool _fallbackUsed;

    public TestWorkspaceBuilder AddDocument(SourceDocument document)
    {
        _documents.Add(document);
        return this;
    }

    public TestWorkspaceBuilder AddDocument(Action<SourceDocumentBuilder> configure)
    {
        var builder = new SourceDocumentBuilder();
        configure(builder);
        _documents.Add(builder.Build());
        return this;
    }

    public TestWorkspaceBuilder WithLoadMode(WorkspaceLoadMode loadMode)
    {
        _loadMode = loadMode;
        return this;
    }

    public TestWorkspaceBuilder WithRequestedPrimaryLoader(string requestedPrimaryLoader)
    {
        _requestedPrimaryLoader = requestedPrimaryLoader;
        return this;
    }

    public TestWorkspaceBuilder WithFallbackUsed(bool fallbackUsed)
    {
        _fallbackUsed = fallbackUsed;
        return this;
    }

    public WorkspaceLoadResult Build() =>
        WorkspaceLoadResult.Success(_documents, _loadMode, _requestedPrimaryLoader, _fallbackUsed);
}
