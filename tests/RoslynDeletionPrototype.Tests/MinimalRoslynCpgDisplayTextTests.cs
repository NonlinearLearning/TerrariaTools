using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Cli;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MinimalRoslynCpgDisplayTextTests
{
    [Fact]
    public void GetDisplayText_WhenNodeTextIsMissing_RecoversSourceSlice()
    {
        const string source =
            """
            namespace Demo;

            public sealed class Sample
            {
                public int Run(int seed)
                {
                    if (seed > 0) return seed + 1;
                    return 0;
                }
            }
            """;

        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "display-text.cs");
        var ifNode = Assert.Single(graph.Nodes, node =>
            node.Kind == MinimalRoslynCpg.Contracts.RoslynCpgNodeKind.SyntaxNode &&
            node.DisplayKind == nameof(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IfStatement));

        Assert.Null(ifNode.Text);
        Assert.Equal("if (seed > 0) return seed + 1;", graph.GetDisplayText(ifNode));
    }

    [Fact]
    public void GetDisplayText_WhenNoSourceSpanExists_FallsBackToIdentityFields()
    {
        var graph = new RoslynCpgGraph();

        Assert.Equal(
            "demo.full",
            graph.GetDisplayText(new RoslynCpgNode(MinimalRoslynCpg.Contracts.RoslynCpgNodeKind.Operation, "Operation", FullName: "demo.full")));
        Assert.Equal(
            "demoName",
            graph.GetDisplayText(new RoslynCpgNode(MinimalRoslynCpg.Contracts.RoslynCpgNodeKind.Operation, "Operation", Name: "demoName")));
        Assert.Equal(
            "Operation",
            graph.GetDisplayText(new RoslynCpgNode(MinimalRoslynCpg.Contracts.RoslynCpgNodeKind.Operation, "Operation")));
    }

    [Fact]
    public void Cli_LocalView_WhenDisplayTextIsMissing_StillPrintsRecoveredSourceText()
    {
        const string source =
            """
            namespace Demo;

            public sealed class Sample
            {
                public int Run(int seed)
                {
                    if (seed > 0) return seed + 1;
                    return 0;
                }
            }
            """;

        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "display-text-cli.cs");
        File.WriteAllText(filePath, source);

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, filePath);
        var anchorNodeId = Assert.Single(graph.Nodes, node =>
            node.Kind == MinimalRoslynCpg.Contracts.RoslynCpgNodeKind.SyntaxNode &&
            node.DisplayKind == nameof(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IfStatement)).NodeId!.Value;

        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            var output = new StringWriter();
            Console.SetOut(output);
            Console.SetError(TextWriter.Null);

            var exitCode = new MinimalRoslynCpgCli().Run(new[]
            {
                filePath,
                "--view",
                "local",
                "--anchor-node-id",
                anchorNodeId.ToString(),
                "--hops",
                "0",
            });

            Assert.Equal(0, exitCode);
            Assert.Contains("if (seed > 0) return seed + 1;", output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
