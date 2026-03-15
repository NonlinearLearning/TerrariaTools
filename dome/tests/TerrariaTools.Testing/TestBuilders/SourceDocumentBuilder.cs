using TerrariaTools.Dome.Core;

namespace TerrariaTools.Testing.TestBuilders;

public sealed class SourceDocumentBuilder
{
    private string _relativePath = "Sample.cs";
    private string _sourcePath = "Sample.cs";
    private string _text = "class C { }";

    public SourceDocumentBuilder WithRelativePath(string relativePath)
    {
        _relativePath = relativePath;
        return this;
    }

    public SourceDocumentBuilder WithSourcePath(string sourcePath)
    {
        _sourcePath = sourcePath;
        return this;
    }

    public SourceDocumentBuilder WithText(string text)
    {
        _text = text;
        return this;
    }

    public SourceDocument Build() => new(_relativePath, _sourcePath, _text);
}
