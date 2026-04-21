using Domain.Analysis.Engine.Core;
using System.Text.RegularExpressions;
using Domain.Analysis.Engine.Semantic;
using Infrastructure.Analysis.Engine.X2Cpg;
using Logic.Analysis.Engine.X2Cpg;
using Logic.Analysis.Engine.X2Cpg.DataStructures;
using Logic.Analysis.Engine.X2Cpg.Utils;
using Xunit;

namespace Isolation.AnalysisTests.X2Cpg;

public sealed class X2CpgCoreTests
{
    [Fact]
    public void Defines_exposesJoernCoreConstants()
    {
        Assert.Equal("ANY", Defines.Any);
        Assert.Equal("<clinit>", Defines.StaticInitMethodName);
        Assert.Equal("<init>", Defines.ConstructorMethodName);
    }

    [Fact]
    public void Stack_pushesAndPopsFromHead()
    {
        X2CpgStack<int> stack = new();
        stack.Push(1);
        stack.Push(2);

        Assert.Equal(2, stack.Pop());
        Assert.Equal(1, stack.Pop());
    }

    [Fact]
    public void AstPropertiesUtil_readsRootProperties()
    {
        CpgGraph graph = new();
        CpgNode node = graph.CreateNode(CpgNodeKind.Identifier);
        node.SetProperty("Name", "value");
        node.SetProperty("Code", "value");
        node.SetProperty("TypeFullName", "int");
        Ast ast = Ast.FromRoot(node);

        Assert.Equal("value", AstPropertiesUtil.RootName(ast));
        Assert.Equal("value", AstPropertiesUtil.RootCode(ast));
        Assert.Equal("int", AstPropertiesUtil.RootType(ast));
    }

    [Fact]
    public void ListAndOffsetUtilities_matchJoernBehavior()
    {
        Assert.Equal(new[] { 1, 2, 3 }, ListUtils.TakeUntil(new[] { 1, 2, 3, 4 }, value => value >= 3));
        Assert.Null(ListUtils.SingleOrNone(new[] { "a", "b" }));

        int[] table = OffsetUtils.GetLineOffsetTable("ab\r\nc\n");
        Assert.Equal(new[] { 0, 4 }, table);
        Assert.Equal((4, 6), OffsetUtils.CoordinatesToOffset(table, 1, 0, 1, 1));
        Assert.True("ABC_1".IsAllUpperCase());
    }

    [Fact]
    public void Imports_createsImportNodeAndOptionalLink()
    {
        CpgGraph graph = new();
        CpgNode call = graph.CreateNode(CpgNodeKind.Call);

        CpgNode importNode = Imports.CreateImportNodeAndLink(graph, "System.Text", "Text", call);

        Assert.Equal(CpgNodeKind.Import, importNode.Kind);
        Assert.Equal("System.Text", importNode.Property<string>("ImportedEntity"));
        Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.IsCallForImport), edge => edge.TargetId == importNode.Id);
    }

    [Fact]
    public void ProgramSummary_mergesAndResolvesTypes()
    {
        ProgramSummary summary = new();
        TypeSummary first = new("Demo.User");
        first.Methods.Add(new MethodSummary("Save", "void(string)"));
        TypeSummary second = new("Demo.User");
        second.Fields.Add(new FieldSummary("Name", "string"));

        summary.AddType("Demo", first);
        summary.AddType("Demo", second);

        TypeSummary type = Assert.Single(summary.TypesUnderNamespace("Demo"));
        Assert.Single(type.Methods);
        Assert.Single(type.Fields);
        Assert.Equal(type, summary.TryResolveTypeReference("User"));
    }

    [Fact]
    public void TypeStubUtil_resolvesTypeStubDirectory()
    {
        TypeStubMetaData metaData = new(UseTypeStubs: true, new Uri("file:///tmp/project/target/classes"));

        string path = TypeStubUtil.TypeStubDir(metaData);

        Assert.EndsWith("project/type_stubs", path.Replace('\\', '/'));
    }

    [Fact]
    public void SourceFileFilter_appliesPureIgnoreRules()
    {
        string root = Path.GetFullPath("src");
        string includedFile = Path.Combine(root, "Demo.cs");
        string generatedFile = Path.Combine(root, "Demo.g.cs");
        string ignoredDirectory = Path.Combine(root, "obj");
        string ignoredFile = Path.Combine(ignoredDirectory, "Generated.cs");

        IReadOnlyList<string> files = SourceFileFilter.FilterByRules(
            new[] { includedFile, generatedFile, ignoredFile },
            root,
            ignoredFilesRegex: new Regex(".*\\.g\\.cs$", RegexOptions.Compiled),
            ignoredFilesPath: new[] { ignoredDirectory });

        Assert.Equal(new[] { includedFile }, files);
    }
}
