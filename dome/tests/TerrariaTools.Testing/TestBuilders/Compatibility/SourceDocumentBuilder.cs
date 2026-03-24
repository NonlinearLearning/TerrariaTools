using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;

namespace TerrariaTools.Testing.TestBuilders;

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

    public ModelAnalysis.SourceDocument Build() => new(_sourcePath, _relativePath, _text);
}

