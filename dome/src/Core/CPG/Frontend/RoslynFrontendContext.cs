using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class RoslynFrontendContext(
    RoslynFrontendConfig config,
    SyntaxTree syntaxTree,
    Compilation compilation,
    SemanticModel semanticModel)
{
    public RoslynFrontendConfig Config { get; } = config;

    public SyntaxTree SyntaxTree { get; } = syntaxTree;

    public Compilation Compilation { get; } = compilation;

    public SemanticModel SemanticModel { get; } = semanticModel;
}
