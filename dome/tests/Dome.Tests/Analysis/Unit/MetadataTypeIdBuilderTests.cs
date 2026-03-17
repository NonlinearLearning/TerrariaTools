using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class MetadataTypeIdBuilderTests
{
    [Fact]
    public void Build_Null_ReturnsUnknown()
    {
        Assert.Equal("Unknown", MetadataTypeIdBuilder.Build(null));
    }

    [Fact]
    public void Build_SimpleType_ReturnsExpectedDisplayString()
    {
        var symbol = GetDeclaredTypeSymbol("namespace Sample; public class Player { }", "Player");

        Assert.Equal("Sample.Player", MetadataTypeIdBuilder.Build(symbol));
    }

    [Fact]
    public void Build_NestedType_ReturnsFullyQualifiedNestedName()
    {
        var symbol = GetDeclaredTypeSymbol("namespace Sample; public class Outer { public class Inner { } }", "Inner");

        Assert.Equal("Sample.Outer.Inner", MetadataTypeIdBuilder.Build(symbol));
    }

    [Fact]
    public void Build_GenericType_UsesRoslynDisplayFormat()
    {
        const string source = "using System.Collections.Generic; namespace Sample; public class Holder { public List<int> Items = new(); }";
        var compilation = CreateCompilation(source);
        var root = compilation.SyntaxTrees.Single().GetCompilationUnitRoot();
        var field = root.DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
        var typeSymbol = compilation.GetSemanticModel(root.SyntaxTree).GetTypeInfo(field.Declaration.Type).Type!;

        Assert.Equal("System.Collections.Generic.List<int>", MetadataTypeIdBuilder.Build(typeSymbol));
    }

    private static ITypeSymbol GetDeclaredTypeSymbol(string source, string identifier)
    {
        var compilation = CreateCompilation(source);
        var root = compilation.SyntaxTrees.Single().GetCompilationUnitRoot();
        var node = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(type => type.Identifier.Text == identifier);
        return (ITypeSymbol)compilation.GetSemanticModel(root.SyntaxTree).GetDeclaredSymbol(node)!;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "Types",
            [tree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location)
            ]);
    }
}
