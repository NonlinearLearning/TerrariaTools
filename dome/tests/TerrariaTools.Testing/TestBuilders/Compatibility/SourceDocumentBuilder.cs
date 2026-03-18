using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

namespace TerrariaTools.Testing.TestBuilders;

/// <summary>
/// Compatibility-only builder for native source documents.
/// </summary>
public sealed class SourceDocumentCompatibilityBuilder
{
    private string _relativePath = "Sample.cs";
    private string _sourcePath = "Sample.cs";
    private string _text = "class C { }";

    public SourceDocumentCompatibilityBuilder WithRelativePath(string relativePath)
    {
        _relativePath = relativePath;
        return this;
    }

    public SourceDocumentCompatibilityBuilder WithSourcePath(string sourcePath)
    {
        _sourcePath = sourcePath;
        return this;
    }

    public SourceDocumentCompatibilityBuilder WithText(string text)
    {
        _text = text;
        return this;
    }

    public ApplicationAbstractions.SourceDocument Build() => new(_sourcePath, _relativePath, _text);
}
