using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native workspace load contracts.
/// </summary>
public sealed class TestWorkspaceCompatibilityBuilder
{
    private readonly List<ApplicationAbstractions.SourceDocument> _documents = [];
    private ModelPrimitives.WorkspaceLoadMode _loadMode = ModelPrimitives.WorkspaceLoadMode.SourceOnly;
    private string _requestedPrimaryLoader = "TestWorkspaceCompatibilityBuilder";
    private bool _fallbackUsed;

    public TestWorkspaceCompatibilityBuilder AddDocument(ApplicationAbstractions.SourceDocument document)
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
            new ApplicationAbstractions.SourceDocumentSet(entryPath, rootPath, _documents.ToArray()),
            _loadMode,
            _requestedPrimaryLoader,
            _fallbackUsed);
    }
}
