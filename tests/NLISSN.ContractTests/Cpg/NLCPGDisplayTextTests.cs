using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Cli;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.Tests;

// 这些用例调用 NLCPGCli，而 CLI 通过 Directory.GetCurrentDirectory() 解析 nlissn.yml，
// 故它们必须 Directory.SetCurrentDirectory()。CWD 是进程级状态，会与并行运行的其他集合
// 互相污染（其他用例把相对路径交给 Path.GetFullPath，会解析到本类的临时目录）。
[Collection("ProcessCurrentDirectory")]
public sealed class NLCPGDisplayTextTests
{
    [Fact]
    public void GetDisplayText_WhenNodeTextIsMissing_RecoversSourceSlice()
    {
        const string source = CpgDisplaySources.ConditionalSeedSource;

        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(source, "display-text.cs");
        var ifNode = Assert.Single(graph.Nodes, node =>
            node.Kind == NLCPG.Contracts.NLCPGNodeKind.SyntaxNode &&
            graph.ResolveDisplayKind(node) == nameof(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IfStatement));

    Assert.Equal("if (seed > 0) return seed + 1;", graph.GetDisplayText(ifNode));
    }

    [Fact]
    public void GetDisplayText_WhenNoSourceSpanExists_FallsBackToIdentityFields()
    {
        var graph = new NLCPGGraph();

        Assert.Equal(
            "demo.full",
            graph.GetDisplayText(graph.AddNode(new NLCPGNodeDraft(NLCPG.Contracts.NLCPGNodeKind.Operation, FullName: "demo.full"))));
        Assert.Equal(
            "demoName",
            graph.GetDisplayText(graph.AddNode(new NLCPGNodeDraft(NLCPG.Contracts.NLCPGNodeKind.Operation, Name: "demoName"))));
        Assert.Equal(
            "Operation",
            graph.GetDisplayText(graph.AddNode(new NLCPGNodeDraft(NLCPG.Contracts.NLCPGNodeKind.Operation))));
    }

    [Fact]
    public void Cli_LocalView_WhenDisplayTextIsMissing_StillPrintsRecoveredSourceText()
    {
        const string source = CpgDisplaySources.ConditionalSeedSource;

        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "display-text-cli.cs");
        File.WriteAllText(filePath, source);

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(source, filePath);
        var anchorNodeId = Assert.Single(graph.Nodes, node =>
            node.Kind == NLCPG.Contracts.NLCPGNodeKind.SyntaxNode &&
            graph.ResolveDisplayKind(node) == nameof(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IfStatement)).NodeId!.Value;
        File.WriteAllText(
          Path.Combine(tempDirectory, "nlissn.yml"),
          $"""
          schemaVersion: 3
          tool: nlcpg
          input:
            path: ./display-text-cli.cs
          nlcpg:
            view:
              mode: local
              anchor:
                nodeId: {anchorNodeId}
              hops: 0
              direction: both
              edgeKinds: []
          """);

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            var output = new StringWriter();
            Console.SetOut(output);
            Console.SetError(TextWriter.Null);
            Directory.SetCurrentDirectory(tempDirectory);

            var exitCode = new NLCPGCli().Run(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.Contains("if (seed > 0) return seed + 1;", output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Cli_LoadsUnifiedYamlConfiguration_AndWritesConfiguredLocalView()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        var sourcePath = Path.Combine(tempDirectory, "configured.cs");
        var jsonPath = Path.Combine(tempDirectory, "Build", "local-view.json");
        const string source = "namespace Demo; public sealed class Sample { public int Add() => 1; }";
        File.WriteAllText(sourcePath, source);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(source, sourcePath);
        var anchorNodeId = Assert.Single(graph.Nodes, node =>
            node.Kind == NLCPG.Contracts.NLCPGNodeKind.Method &&
            graph.ResolveName(node) == "Add").NodeId!.Value;
        File.WriteAllText(
          Path.Combine(tempDirectory, "nlissn.yml"),
          $"""
          schemaVersion: 3
          tool: nlcpg
          input:
            path: ./configured.cs
          nlcpg:
            view:
              mode: local
              anchor:
                nodeId: {anchorNodeId}
              hops: 0
              direction: both
              edgeKinds: []
            output:
              json: ./Build/local-view.json
          """);

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            var exitCode = new NLCPGCli().Run(Array.Empty<string>());

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(jsonPath));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Cli_RejectsCommandLineArguments_EvenHelp()
    {
        Assert.Throws<ArgumentException>(() => new NLCPGCli().Run(new[] { "--help" }));
    }

    [Fact]
    public void Cli_RejectsConfigurationForAnotherTool()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(
          Path.Combine(tempDirectory, "nlissn.yml"),
          """
          schemaVersion: 3
          tool: nlissn
          """);

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            Assert.Throws<ArgumentException>(() => new NLCPGCli().Run(Array.Empty<string>()));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
