using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class RoslynAnalysisBundleMergerTests
{
    [Fact]
    public void Merge_SingleBundle_PreservesAllCollections()
    {
        var bundle = CreateBundle(
            "Sample.cs",
            targets: [new AnalysisTarget(new PlanTarget("Sample.cs", new MemberId("Sample.Player.Run()"), MemberKind.Method, TargetKind.Method, 0, 3, "Run"), false, [], [], [], [], StatementKindRef.Expression, false, false, false, [], StatementScopeMode.MinimalBlock, null, null)],
            edges: [new AnalysisEdge("a", "b", AnalysisEdgeKind.Uses, "symbol")],
            typeNodes: [new TypeNodeRef("Sample.Player", "Player", "Sample.cs")],
            typeEdges: [new TypeDependencyEdge("Sample.Player", "Sample.Base", TypeDependencyKind.BaseType)],
            functionNodes: [new FunctionNodeRef(new MemberId("Sample.Player.Run()"), MemberKind.Method, "Sample.Player", "Run", "Sample.cs", 0, 3, false, true, true, true, "void")]);

        var merged = RoslynAnalysisBundleMerger.Merge([bundle]);

        Assert.Single(merged.Documents);
        Assert.Single(merged.Targets);
        Assert.Single(merged.Edges);
        Assert.Single(merged.TypeNodes);
        Assert.Single(merged.TypeEdges);
        Assert.Single(merged.FunctionNodes);
    }

    [Fact]
    public void Merge_DuplicateTypeNodes_KeepLastBundleValue()
    {
        var first = CreateBundle("A.cs", typeNodes: [new TypeNodeRef("Sample.Player", "Player-v1", "A.cs")]);
        var second = CreateBundle("B.cs", typeNodes: [new TypeNodeRef("Sample.Player", "Player-v2", "B.cs")]);

        var merged = RoslynAnalysisBundleMerger.Merge([first, second]);

        var node = Assert.Single(merged.TypeNodes);
        Assert.Equal("Player-v2", node.DisplayName);
        Assert.Equal("B.cs", node.DocumentPath);
    }

    [Fact]
    public void Merge_DuplicateFunctionNodes_KeepLastBundleValue()
    {
        var first = CreateBundle("A.cs", functionNodes: [new FunctionNodeRef(new MemberId("Sample.Player.Run()"), MemberKind.Method, "Sample.Player", "Run-v1", "A.cs", 0, 3, false, true, true, true, "void")]);
        var second = CreateBundle("B.cs", functionNodes: [new FunctionNodeRef(new MemberId("Sample.Player.Run()"), MemberKind.Method, "Sample.Player", "Run-v2", "B.cs", 10, 4, false, false, true, true, "int")]);

        var merged = RoslynAnalysisBundleMerger.Merge([first, second]);

        var node = Assert.Single(merged.FunctionNodes);
        Assert.Equal("Run-v2", node.DisplayName);
        Assert.Equal("B.cs", node.DocumentPath);
        Assert.Equal("int", node.ReturnTypeDisplay);
    }

    [Fact]
    public void Merge_TypeEdges_AreDeduplicated()
    {
        var edge = new TypeDependencyEdge("Sample.Player", "Sample.Base", TypeDependencyKind.BaseType);
        var first = CreateBundle("A.cs", typeEdges: [edge]);
        var second = CreateBundle("B.cs", typeEdges: [edge]);

        var merged = RoslynAnalysisBundleMerger.Merge([first, second]);

        Assert.Single(merged.TypeEdges);
    }

    [Fact]
    public void Merge_TypeAndFunctionNodes_AreReturnedInDeterministicSortedOrder()
    {
        var bundle = CreateBundle(
            "Sample.cs",
            typeNodes:
            [
                new TypeNodeRef("Z.Type", "Z", "Sample.cs"),
                new TypeNodeRef("A.Type", "A", "Sample.cs")
            ],
            functionNodes:
            [
                new FunctionNodeRef(new MemberId("Z.Run()"), MemberKind.Method, "Z.Type", "ZRun", "Sample.cs", 0, 1, false, true, true, true, "void"),
                new FunctionNodeRef(new MemberId("A.Run()"), MemberKind.Method, "A.Type", "ARun", "Sample.cs", 0, 1, false, true, true, true, "void")
            ]);

        var merged = RoslynAnalysisBundleMerger.Merge([bundle]);

        Assert.Equal(["A.Type", "Z.Type"], merged.TypeNodes.Select(x => x.TypeId).ToArray());
        Assert.Equal(["A.Run()", "Z.Run()"], merged.FunctionNodes.Select(x => x.MemberId.Value).ToArray());
    }

    private static DocumentAnalysisBundle CreateBundle(
        string path,
        IReadOnlyList<AnalysisTarget>? targets = null,
        IReadOnlyList<AnalysisEdge>? edges = null,
        IReadOnlyList<TypeNodeRef>? typeNodes = null,
        IReadOnlyList<TypeDependencyEdge>? typeEdges = null,
        IReadOnlyList<FunctionNodeRef>? functionNodes = null)
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Sample; public class Placeholder { }", path: path);
        var compilation = CSharpCompilation.Create(
            "Bundle",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        return new DocumentAnalysisBundle(
            new SourceDocument(path, path, "namespace Sample; public class Placeholder { }"),
            (CompilationUnitSyntax)tree.GetRoot(),
            compilation.GetSemanticModel(tree),
            targets ?? [],
            edges ?? [],
            typeNodes ?? [],
            typeEdges ?? [],
            functionNodes ?? [],
            [],
            new DocumentAnalysisTimings(0, 0, 0, 0, 0, 0));
    }
}
