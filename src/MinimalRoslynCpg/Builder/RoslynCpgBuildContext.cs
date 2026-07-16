using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder;

internal sealed class RoslynCpgBuildContext
{
  private RoslynCpgBuildContext(
    SemanticModel semanticModel,
    SyntaxNode root,
    string source,
    string filePath,
    RoslynCpgGraph graph,
    RoslynCpgNode syntaxTreeNode)
  {
    SemanticModel = semanticModel;
    Root = root;
    Source = source;
    FilePath = filePath;
    Graph = graph;
    SyntaxTreeNode = syntaxTreeNode;
  }

  internal SemanticModel SemanticModel { get; }

  internal SyntaxNode Root { get; }

  internal string Source { get; }

  internal string FilePath { get; }

  internal RoslynCpgGraph Graph { get; }

  internal RoslynCpgNode SyntaxTreeNode { get; }

  internal static RoslynCpgBuildContext Create(
    SemanticModel semanticModel,
    SyntaxNode root,
    string source,
    string filePath)
  {
    var graph = new RoslynCpgGraph();
    graph.RegisterSource(filePath, source);
    var fullPath = Path.GetFullPath(filePath);
    var syntaxTreeNode = graph.AddNode(new RoslynCpgNode(
      Kind: RoslynCpgNodeKind.SyntaxTree,
      DisplayKind: nameof(RoslynCpgNodeKind.SyntaxTree),
      Name: Path.GetFileName(filePath),
      FullName: fullPath,
      FilePath: filePath,
      SpanStart: 0,
      SpanEnd: source.Length));
    return new RoslynCpgBuildContext(
      semanticModel,
      root,
      source,
      filePath,
      graph,
      syntaxTreeNode);
  }

  internal static RoslynCpgBuildContext CreateFromSource(string source, string filePath)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var compilation = CSharpCompilation.Create(
      assemblyName: Path.GetFileNameWithoutExtension(filePath),
      syntaxTrees: new[] { syntaxTree },
      references: RoslynCpgBuilder.CreateMetadataReferences());
    var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
    return Create(semanticModel, syntaxTree.GetRoot(), source, filePath);
  }
}
