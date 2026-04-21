using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Domain.Analysis.Engine.Semantic.Validation;
using Xunit;

namespace Isolation.AnalysisTests.Semantic.Validation;

public sealed class PostFrontendValidatorTests : IDisposable
{
    private readonly string tempDirectory;

    public PostFrontendValidatorTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void Validate_reportsCurrentFrontendViolationsAndWritesCountToMetadata()
    {
        File.WriteAllText(
            Path.Combine(tempDirectory, "Sample.cs"),
            """
            namespace Demo;

            public sealed class Worker
            {
                public int Read(int input)
                {
                    int current = input + 1;
                    return current;
                }
            }
            """);

        RoslynCpgFrontend frontend = new(new DefaultRoslynCpgBuilder());
        CpgGraph graph = frontend.CreateGraph(new CpgFrontendOptions { InputPath = tempDirectory });

        IReadOnlyList<ValidationViolation> violations = PostFrontendValidator.Validate(graph);

        Assert.NotEmpty(violations);
        Assert.All(violations, violation => Assert.Equal("MULTI_REF", violation.Code));
        CpgNode metaDataNode = Assert.Single(graph.GetNodes(CpgNodeKind.MetaData));
        Assert.True(metaDataNode.TryGetProperty<int>("ValidationViolationCount", out int count));
        Assert.Equal(violations.Count, count);
    }

    [Fact]
    public void Validate_returnsNoViolationsForWellFormedManualGraph()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("FullName", "Demo.Sample.Run(int)");

        CpgNode parameterNode = graph.CreateNode(CpgNodeKind.MethodParameterIn);
        parameterNode.SetProperty("DeclaredSymbolId", "parameter:Demo.Sample.Run(int):input");
        parameterNode.SetProperty("AstParentId", methodNode.Id);
        graph.AddEdge(methodNode.Id, parameterNode.Id, CpgEdgeKind.Ast);

        CpgNode identifierNode = graph.CreateNode(CpgNodeKind.Identifier);
        identifierNode.SetProperty("ReferencedSymbolId", "parameter:Demo.Sample.Run(int):input");
        identifierNode.SetProperty("AstParentId", methodNode.Id);
        graph.AddEdge(methodNode.Id, identifierNode.Id, CpgEdgeKind.Ast);
        graph.AddEdge(identifierNode.Id, parameterNode.Id, CpgEdgeKind.Ref);

        IReadOnlyList<ValidationViolation> violations = PostFrontendValidator.Validate(graph);

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateOrThrow_throwsForDuplicateMethodFullName()
    {
        CpgGraph graph = new();
        CpgNode firstMethod = graph.CreateNode(CpgNodeKind.Method);
        firstMethod.SetProperty("FullName", "Demo.Sample.Run()");

        CpgNode secondMethod = graph.CreateNode(CpgNodeKind.Method);
        secondMethod.SetProperty("FullName", "Demo.Sample.Run()");

        ValidationError error = Assert.Throws<ValidationError>(
            () => PostFrontendValidator.ValidateOrThrow(graph, ValidationLevel.V1));

        Assert.Contains("FULLNAME_UNIQUE_METHOD", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOrThrow_throwsForIdentifierWithMultipleRefTargets()
    {
        CpgGraph graph = new();
        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("FullName", "Demo.Sample.Run()");

        CpgNode firstLocal = graph.CreateNode(CpgNodeKind.Local);
        firstLocal.SetProperty("AstParentId", methodNode.Id);
        graph.AddEdge(methodNode.Id, firstLocal.Id, CpgEdgeKind.Ast);

        CpgNode secondLocal = graph.CreateNode(CpgNodeKind.Local);
        secondLocal.SetProperty("AstParentId", methodNode.Id);
        graph.AddEdge(methodNode.Id, secondLocal.Id, CpgEdgeKind.Ast);

        CpgNode identifier = graph.CreateNode(CpgNodeKind.Identifier);
        identifier.SetProperty("AstParentId", methodNode.Id);
        graph.AddEdge(methodNode.Id, identifier.Id, CpgEdgeKind.Ast);
        graph.AddEdge(identifier.Id, firstLocal.Id, CpgEdgeKind.Ref);
        graph.AddEdge(identifier.Id, secondLocal.Id, CpgEdgeKind.Ref);

        ValidationError error = Assert.Throws<ValidationError>(
            () => PostFrontendValidator.ValidateOrThrow(graph, ValidationLevel.V1));

        Assert.Contains("MULTI_REF", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
