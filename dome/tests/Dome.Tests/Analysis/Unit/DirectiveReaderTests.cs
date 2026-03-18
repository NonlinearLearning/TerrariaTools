using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Analysis.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class DirectiveReaderLegacyTests
{
    [Fact]
    public void Read_NoLeadingComment_ReturnsEmptyList()
    {
        var statement = ParseStatement("value++;");

        var directives = DirectiveReader.Read(statement);

        Assert.Empty(directives);
    }

    [Fact]
    public void Read_DeleteDirective_ProducesDeleteAction()
    {
        var directives = DirectiveReader.Read(ParseStatement("// dome:delete\nvalue++;"));

        var directive = Assert.Single(directives);
        Assert.Equal(ModelPrimitives.PlanActionKind.Delete, directive.ActionKind);
        Assert.Null(directive.Payload);
    }

    [Fact]
    public void Read_CommentDirective_ProducesCommentOutAction()
    {
        var directives = DirectiveReader.Read(ParseStatement("// dome:comment\nvalue++;"));

        var directive = Assert.Single(directives);
        Assert.Equal(ModelPrimitives.PlanActionKind.CommentOut, directive.ActionKind);
    }

    [Fact]
    public void Read_DefaultDirective_ProducesReplaceWithDefaultAndPayload()
    {
        var directives = DirectiveReader.Read(ParseStatement("// dome:default\nvalue++;"));

        var directive = Assert.Single(directives);
        Assert.Equal(ModelPrimitives.PlanActionKind.ReplaceWithDefault, directive.ActionKind);
        Assert.Equal("default", directive.Payload);
    }

    [Fact]
    public void Read_Matching_IsCaseInsensitive()
    {
        var directives = DirectiveReader.Read(ParseStatement("// DOME:DELETE\nvalue++;"));

        Assert.Single(directives);
        Assert.Equal(ModelPrimitives.PlanActionKind.Delete, directives[0].ActionKind);
    }

    [Fact]
    public void Read_UnrelatedComments_DoNotProduceDirectives()
    {
        var directives = DirectiveReader.Read(ParseStatement("// not a dome directive\nvalue++;"));

        Assert.Empty(directives);
    }

    private static StatementSyntax ParseStatement(string source)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            class C
            {
                void M()
                {
                    {{source}}
                }
            }
            """);
        var method = tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        return method.Body!.Statements.Single();
    }
}
