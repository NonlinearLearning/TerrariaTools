using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder;

internal sealed record NLCPGSourceSemanticInput(SemanticModel SemanticModel, SyntaxNode Root);

internal sealed class NLCPGBuildContext
{
  private readonly List<OperationInventoryEntry> _operationInventory = new();
  private readonly List<IInvocationOperation> _invocationOperations = new();
  private readonly List<IPropertyReferenceOperation> _propertyReferenceOperations = new();
  private readonly List<IFieldReferenceOperation> _fieldReferenceOperations = new();
  private readonly Dictionary<SyntaxNode, IOperation> _operationRootsBySyntax =
    new(ReferenceEqualityComparer.Instance);
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

  internal IReadOnlyList<IInvocationOperation> InvocationOperations => _invocationOperations;

  internal IReadOnlyList<IPropertyReferenceOperation> PropertyReferenceOperations => _propertyReferenceOperations;

  internal IReadOnlyList<IFieldReferenceOperation> FieldReferenceOperations => _fieldReferenceOperations;

  internal void AddOperationInventoryEntry(IOperation operation, IOperation methodRoot, IMethodSymbol? owningMethod, bool isRoot, NLCPGNode node)
  {
    ArgumentNullException.ThrowIfNull(operation);
    ArgumentNullException.ThrowIfNull(methodRoot);
    ArgumentNullException.ThrowIfNull(node);
    _operationInventory.Add(new OperationInventoryEntry(operation, methodRoot, owningMethod, isRoot, node));
    switch (operation)
    {
      case IInvocationOperation invocation:
        _invocationOperations.Add(invocation);
        break;
      case IPropertyReferenceOperation propertyReference:
        _propertyReferenceOperations.Add(propertyReference);
        break;
      case IFieldReferenceOperation fieldReference:
        _fieldReferenceOperations.Add(fieldReference);
        break;
    }

    if (isRoot)
    {
      RegisterOperationRoot(operation);
    }
  }

  internal bool TryGetOperationRoot(SyntaxNode bodySyntax, out IOperation operation)
  {
    return _operationRootsBySyntax.TryGetValue(bodySyntax, out operation!);
  }

  internal void RegisterOperationRoot(IOperation operation)
  {
    ArgumentNullException.ThrowIfNull(operation);
    _operationRootsBySyntax[operation.Syntax] = operation;
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
    var input = CreateSourceSemanticInput(source, filePath);
    return Create(input.SemanticModel, input.Root, source, filePath, preallocatedNodeIds, identityFactory);
  }

  internal static NLCPGBuildContext CreateFromSourceAnchorDiscovery(string source, string filePath, StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
  {
    var input = CreateSourceSemanticInput(source, filePath);
    return CreateAnchorDiscovery(input.SemanticModel, input.Root, source, filePath, identityFactory, observeAnchor);
  }

  internal static NLCPGSourceSemanticInput CreateSourceSemanticInput(string source, string filePath)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var compilation = CSharpCompilation.Create(
      assemblyName: Path.GetFileNameWithoutExtension(filePath),
      syntaxTrees: new[] { syntaxTree },
      references: NLCPGBuilder.CreateMetadataReferences());
    var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
    return new NLCPGSourceSemanticInput(semanticModel, syntaxTree.GetRoot());
  }
}

internal sealed record OperationInventoryEntry(
  IOperation Operation,
  IOperation MethodRoot,
  IMethodSymbol? OwningMethod,
  bool IsRoot,
  NLCPGNode Node);
