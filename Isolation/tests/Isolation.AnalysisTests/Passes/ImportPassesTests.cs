using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Logic.Analysis.Engine.Passes;
using Xunit;

namespace Isolation.AnalysisTests.Passes;

public sealed class ImportPassesTests
{
    [Fact]
    public void BuildImportsPass_createsImportNodeUnderFile()
    {
        CpgGraph graph = new();
        CpgNode fileNode = graph.CreateNode(CpgNodeKind.File);
        fileNode.SetProperty("FileName", "Demo.cs");

        BuildImportsPass pass = new(
        [
            new ImportDirectiveInfo(
                "Demo.cs",
                "System.Collections.Generic",
                "Generic",
                "using Generic = System.Collections.Generic;",
                1,
                1,
                1,
                false,
                false),
        ]);

        pass.Run(graph);
        new LinkAstPass().Run(graph);

        CpgNode importNode = Assert.Single(graph.GetNodes(CpgNodeKind.Import));
        Assert.True(importNode.TryGetProperty<string>("ImportedEntity", out string? importedEntity));
        Assert.Equal("System.Collections.Generic", importedEntity);
        Assert.Contains(graph.GetOutgoingEdges(fileNode.Id, CpgEdgeKind.Ast), edge => edge.TargetId == importNode.Id);
    }

    [Fact]
    public void BuildImportResolverPass_tagsResolvedNamespaceImport()
    {
        CpgGraph graph = new();
        CpgNode typeNode = graph.CreateNode(CpgNodeKind.Type);
        typeNode.SetProperty("FullName", "System.Collections.Generic.List");

        CpgNode importNode = graph.CreateNode(CpgNodeKind.Import);
        importNode.SetProperty("ImportedEntity", "System.Collections.Generic");

        new BuildImportResolverPass().Run(graph);

        Assert.True(importNode.TryGetProperty<string>("ResolvedImportKind", out string? resolution));
        Assert.Equal("NAMESPACE", resolution);
        CpgEdge tagEdge = Assert.Single(graph.GetOutgoingEdges(importNode.Id, CpgEdgeKind.TaggedBy));
        CpgNode tagNode = graph.GetNode(tagEdge.TargetId);
        Assert.True(tagNode.TryGetProperty<string>("Value", out string? value));
        Assert.Equal("NAMESPACE:System.Collections.Generic", value);
    }

    [Fact]
    public void BuildTypeRecoveryPass_recoversStaticImportCallHints()
    {
        CpgGraph graph = new();

        CpgNode methodNode = graph.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("Name", "Make");
        methodNode.SetProperty("FullName", "Demo.Helpers.Make()");
        methodNode.SetProperty("ContainingTypeFullName", "Demo.Helpers");

        CpgNode methodReturnNode = graph.CreateNode(CpgNodeKind.MethodReturn);
        methodReturnNode.SetProperty("AstParentId", methodNode.Id);
        methodReturnNode.SetProperty("TypeFullName", "int");
        methodReturnNode.SetProperty("PossibleTypes", new[] { "int" });

        CpgNode importNode = graph.CreateNode(CpgNodeKind.Import);
        importNode.SetProperty("ImportedEntity", "Demo.Helpers");
        importNode.SetProperty("ImportedAs", "Helpers");
        importNode.SetProperty("ResolvedImportKind", "TYPE");
        importNode.SetProperty("IsStatic", true);

        CpgNode callNode = graph.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", "Make");
        callNode.SetProperty("MethodFullName", "<unknown>");

        new BuildTypeRecoveryPass().Run(graph);
        new BuildTypeHintCallLinkerPass().Run(graph);

        Assert.True(callNode.TryGetProperty<string[]>("DynamicTypeHintFullNames", out string[]? hintedMethodFullNames));
        Assert.Contains("Demo.Helpers.Make()", hintedMethodFullNames ?? Array.Empty<string>());
        Assert.True(callNode.TryGetProperty<string[]>("PossibleTypes", out string[]? possibleTypes));
        Assert.Contains("int", possibleTypes ?? Array.Empty<string>());
        Assert.Equal("Demo.Helpers.Make()", callNode.TryGetProperty<string>("MethodFullName", out string? methodFullName) ? methodFullName : null);
        Assert.Contains(graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Call), edge => edge.TargetId == methodNode.Id);
    }
}
