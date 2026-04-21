using Infrastructure.Analysis.Engine.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Isolation.AnalysisTests.Frontend;

public sealed class RoslynProjectLoaderTests : IDisposable
{
    private readonly string tempDirectory;

    public RoslynProjectLoaderTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public async Task LoadAsync_createsSemanticModelForSourceDirectory()
    {
        string sourcePath = Path.Combine(tempDirectory, "Sample.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Demo;

            public sealed class Sample
            {
                public int Add(int left, int right) => left + right;
            }
            """);

        RoslynCompilationContext context = await new RoslynProjectLoader().LoadAsync(tempDirectory);

        SyntaxTree tree = Assert.Single(context.SyntaxTrees);
        SemanticModel semanticModel = context.GetSemanticModel(tree);
        MethodDeclarationSyntax method = tree
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        Assert.Equal("Add", context.GetDeclaredSymbol(method)?.Name);
        Assert.Same(semanticModel, context.GetSemanticModel(tree));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
