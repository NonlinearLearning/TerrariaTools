using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder;

internal sealed class NLCPGBuildContext
{
  private readonly List<OperationInventoryEntry> _operationInventory = new();
  private NLCPGBuildContext(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, NLCPGGraph graph, NLCPGNode syntaxTreeNode)
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

  internal NLCPGGraph Graph { get; }

  internal NLCPGNode SyntaxTreeNode { get; }

  internal IReadOnlyList<OperationInventoryEntry> OperationInventory => _operationInventory;

  internal void AddOperationInventoryEntry(IOperation operation, IMethodSymbol? owningMethod, bool isRoot)
  {
    ArgumentNullException.ThrowIfNull(operation);
    _operationInventory.Add(new OperationInventoryEntry(operation, owningMethod, isRoot));
  }

  internal static NLCPGBuildContext Create(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, DeterministicNodeIdTable? preallocatedNodeIds = null, StableNodeIdentityFactory? identityFactory = null)
  {
    var graph = new NLCPGGraph(preallocatedNodeIds, identityFactory);
    graph.RegisterSource(filePath, source);
    var fullPath = Path.GetFullPath(filePath);
    var syntaxTreeNode = graph.AddNode(new NLCPGNode(
      Kind: NLCPGNodeKind.SyntaxTree,
      DisplayKind: nameof(NLCPGNodeKind.SyntaxTree),
      Name: Path.GetFileName(filePath),
      FullName: fullPath,
      FilePath: filePath,
      SpanStart: 0,
      SpanEnd: source.Length));
    return new NLCPGBuildContext(
      semanticModel,
      root,
      source,
      filePath,
      graph,
      syntaxTreeNode);
  }

  internal static NLCPGBuildContext CreateAnchorDiscovery(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
  {
    var graph = NLCPGGraph.CreateAnchorDiscovery(identityFactory, observeAnchor);
    graph.RegisterSource(filePath, source);
    var syntaxTreeNode = graph.AddNode(new NLCPGNode(
      Kind: NLCPGNodeKind.SyntaxTree,
      DisplayKind: nameof(NLCPGNodeKind.SyntaxTree),
      Name: Path.GetFileName(filePath),
      FullName: Path.GetFullPath(filePath),
      FilePath: filePath,
      SpanStart: 0,
      SpanEnd: source.Length));
    return new NLCPGBuildContext(semanticModel, root, source, filePath, graph, syntaxTreeNode);
  }

  internal static NLCPGBuildContext CreateFromSource(string source, string filePath, DeterministicNodeIdTable? preallocatedNodeIds = null, StableNodeIdentityFactory? identityFactory = null)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var compilation = CSharpCompilation.Create(
      assemblyName: Path.GetFileNameWithoutExtension(filePath),
      syntaxTrees: new[] { syntaxTree },
      references: NLCPGBuilder.CreateMetadataReferences());
    var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
    return Create(semanticModel, syntaxTree.GetRoot(), source, filePath, preallocatedNodeIds, identityFactory);
  }

  internal static NLCPGBuildContext CreateFromSourceAnchorDiscovery(string source, string filePath, StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var compilation = CSharpCompilation.Create(
      assemblyName: Path.GetFileNameWithoutExtension(filePath),
      syntaxTrees: new[] { syntaxTree },
      references: NLCPGBuilder.CreateMetadataReferences());
    var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
    return CreateAnchorDiscovery(semanticModel, syntaxTree.GetRoot(), source, filePath, identityFactory, observeAnchor);
  }
}

internal sealed record OperationInventoryEntry(
  IOperation Operation,
  IMethodSymbol? OwningMethod,
  bool IsRoot);
