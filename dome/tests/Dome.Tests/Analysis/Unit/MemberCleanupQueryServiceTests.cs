using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public sealed class MemberCleanupQueryServiceTests
{
    [Fact]
    public void GetSymbolInfo_And_GetTypeInfo_ReturnStoredMetadata()
    {
        var fixture = CreateFixture();

        var methodInfo = fixture.Service.GetSymbolInfo("Sample.Player.Alpha()");
        var typeInfo = fixture.Service.GetTypeInfo("Sample.Player");

        Assert.NotNull(methodInfo);
        Assert.Equal(MemberKind.Method, methodInfo!.MemberKind);
        Assert.True(methodInfo.IsPublic);

        Assert.NotNull(typeInfo);
        Assert.Equal("Player", typeInfo!.Name);
        Assert.True(typeInfo.IsPublic);
    }

    [Fact]
    public void HasAnyReferences_ReturnsTrueForReferenceQueryHits()
    {
        var fixture = CreateFixture(references: new StubReferenceQueryService(hasReferenceIds: ["Sample.Player._value"]));

        Assert.True(fixture.Service.HasAnyReferences("Sample.Player._value"));
    }

    [Fact]
    public void HasAnyReferences_ReturnsTrueForIncomingTypeHits_WhenReferenceQueryReturnsFalse()
    {
        const string fieldId = "Sample.Player._value";
        var graph = new SymbolDependencyGraph(
            [
                new SymbolDependencyNode("Sample.Other", SymbolDependencyNodeKind.Type, "Other", "Sample.cs"),
                new SymbolDependencyNode(fieldId, SymbolDependencyNodeKind.Field, "_value", "Sample.cs")
            ],
            [
                new SymbolDependencyEdge("Sample.Other", fieldId, SymbolDependencyEdgeKind.FieldType)
            ]);
        var fixture = CreateFixture(symbolDependencies: new StubSymbolDependencyGraphProvider(graph));

        Assert.True(fixture.Service.HasAnyReferences(fieldId));
    }

    [Fact]
    public void HasInternalMethodReferences_DistinguishesSameTypeCallers()
    {
        var fixture = CreateFixture(
            references: new StubReferenceQueryService(functionsById: new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal)
            {
                ["Sample.Player.Alpha()"] = [new MemberId("Sample.Player.Beta()")]
            }));

        Assert.True(fixture.Service.HasInternalMethodReferences(new MemberId("Sample.Player.Alpha()")));
        Assert.False(fixture.Service.HasExternalMethodReferences(new MemberId("Sample.Player.Alpha()")));
    }

    [Fact]
    public void HasExternalMethodReferences_DistinguishesCrossTypeCallers()
    {
        var fixture = CreateFixture(
            references: new StubReferenceQueryService(functionsById: new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal)
            {
                ["Sample.Player.Alpha()"] = [new MemberId("Sample.Other.Gamma()")]
            }));

        Assert.False(fixture.Service.HasInternalMethodReferences(new MemberId("Sample.Player.Alpha()")));
        Assert.True(fixture.Service.HasExternalMethodReferences(new MemberId("Sample.Player.Alpha()")));
    }

    [Fact]
    public void GetReorderablePublicMethods_ReturnsOnlyPublicOrdinaryNonStaticMethods_InStableOrder()
    {
        var fixture = CreateFixture();

        var methods = fixture.Service.GetReorderablePublicMethods("Sample.Player");

        Assert.Equal(["Sample.Player.Alpha()", "Sample.Player.Zeta()"], methods.Select(x => x.Value).ToArray());
    }

    private static Fixture CreateFixture(
        IReferenceQueryService? references = null,
        ISymbolDependencyGraphProvider? symbolDependencies = null)
    {
        const string source = """
            namespace Sample;

            public class Player
            {
                private int _value;
                public int Value { get; set; }
                public int Alpha() => 1;
                public void Zeta() { }
                public static void StaticPublic() { }
                private void Hidden() { }
            }

            public class Other
            {
                public void Gamma() { }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source, path: "Sample.cs");
        var compilation = CSharpCompilation.Create(
            "Cleanup",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var document = new AnalysisDocumentContext(new SourceDocument("Sample.cs", "Sample.cs", source), root, model, []);

        var functionNodes = new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal)
        {
            ["Sample.Player.Alpha()"] = new(new MemberId("Sample.Player.Alpha()"), MemberKind.Method, "Sample.Player", "Alpha", "Sample.cs", 0, 1, false, false, true, true, "int"),
            ["Sample.Player.Beta()"] = new(new MemberId("Sample.Player.Beta()"), MemberKind.Method, "Sample.Player", "Beta", "Sample.cs", 0, 1, false, true, true, true, "void"),
            ["Sample.Other.Gamma()"] = new(new MemberId("Sample.Other.Gamma()"), MemberKind.Method, "Sample.Other", "Gamma", "Sample.cs", 0, 1, false, true, true, true, "void")
        };

        var functionIndex = new FunctionIndex(functionNodes, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Sample.cs"] = functionNodes.Keys.ToArray()
        });

        var service = new MemberCleanupQueryService(
            [document],
            functionIndex,
            references ?? new StubReferenceQueryService(),
            new StubInheritanceQueryService(),
            symbolDependencies ?? new StubSymbolDependencyGraphProvider(new SymbolDependencyGraph([], [])));

        return new Fixture(service);
    }

    private sealed record Fixture(MemberCleanupQueryService Service);

    private sealed class StubInheritanceQueryService : IInheritanceQueryService
    {
        public bool ImplementsInterfaceMember(string memberId) => false;
        public bool IsInInheritanceChain(string typeId) => false;
        public bool IsOverrideMember(string memberId) => false;
    }

    private sealed class StubReferenceQueryService(
        IReadOnlySet<string>? hasReferenceIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<MemberId>>? functionsById = null) : IReferenceQueryService
    {
        private readonly IReadOnlySet<string> _hasReferenceIds = hasReferenceIds ?? new HashSet<string>(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, IReadOnlyList<MemberId>> _functionsById =
            functionsById ?? new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal);

        public bool HasReferences(string symbolOrMemberId) => _hasReferenceIds.Contains(symbolOrMemberId);

        public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) =>
            _functionsById.TryGetValue(symbolOrMemberId, out var values) ? values : [];

        public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => [];
    }

    private sealed class StubSymbolDependencyGraphProvider(SymbolDependencyGraph graph) : ISymbolDependencyGraphProvider
    {
        public SymbolDependencyGraph GetBackwardSlice(string symbolId) => graph;
        public SymbolDependencyGraph GetBackwardSlice(string symbolId, SymbolDependencyQueryOptions options) => graph;
        public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds) => graph;
        public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options) => graph;
        public SymbolDependencyGraph GetWholeGraph() => graph;
    }
}
