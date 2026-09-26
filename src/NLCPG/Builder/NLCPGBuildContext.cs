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
  private NLCPGBuildContext(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, NLCPGGraph graph, NLCPGNode syntaxTreeNode, NLCPGGraphRegistry? graphRegistry)
  {
    SemanticModel = semanticModel;
    Root = root;
    Source = source;
    FilePath = filePath;
    Graph = graph;
    SyntaxTreeNode = syntaxTreeNode;
    GraphRegistry = graphRegistry;
  }

  internal SemanticModel SemanticModel { get; }

  internal SyntaxNode Root { get; }

  internal string Source { get; }

  internal string FilePath { get; }

  internal NLCPGGraph Graph { get; }

  internal NLCPGNode SyntaxTreeNode { get; }

  /// <summary>
  /// 多文件构建的图注册表；单文件构建为 <c>null</c>。
  /// </summary>
  /// <remarks>
  /// <c>null</c> 即「本构建只有一张图」——这正是改造前**全部**构建的形态，
  /// 故所有消费方都必须在此为 <c>null</c> 时退回 <see cref="Graph"/>，
  /// 以保住单文件路径的行为逐字不变。
  /// </remarks>
  internal NLCPGGraphRegistry? GraphRegistry { get; }

  /// <summary>
  /// 多文件构建的文档集合；单文件构建为 <c>null</c>。
  /// </summary>
  /// <remarks>
  /// 与 <see cref="GraphRegistry"/> 同时存在：注册表回答「某文件的**图**是哪个」，
  /// 文档集回答「某文件的 **context/语法根/语义模型**是哪个」。
  /// 二者都为 <c>null</c> 即「本构建只有一个文件」——改造前**全部**构建的形态。
  /// </remarks>
  internal NLCPGDocumentSet? DocumentSet { get; private set; }

  /// <summary>
  /// 取本构建的全部文档；单文件时返回仅含自身的单项序列。
  /// </summary>
  /// <remarks>
  /// 供规划相位**聚合跨文件工作项**使用。顺序恒为**登记序**（确定性），
  /// 单文件时恒为「自身」这一个元素 ⇒ 单文件路径行为逐字不变。
  /// </remarks>
  internal IReadOnlyList<NLCPGBuildContext> Documents
  {
    get
    {
      if (DocumentSet is null)
      {
        return new[] { this };
      }

      var documents = new List<NLCPGBuildContext>(DocumentSet.Count);
      foreach (var filePath in DocumentSet.FilePaths)
      {
        documents.Add(DocumentSet.ResolveContext(filePath));
      }

      return documents;
    }
  }

  /// <summary>把一个多文件文档集挂到本 context（仅供多文件入口调用）。</summary>
  internal void AttachDocumentSet(NLCPGDocumentSet documentSet)
  {
    DocumentSet = documentSet ?? throw new ArgumentNullException(nameof(documentSet));
  }

  /// <summary>
  /// **按项路由的 context 解析点**：取某源文件的**该文件** context。
  /// </summary>
  /// <remarks>
  /// 单文件（无文档集）时恒返回 <c>this</c>——与 <see cref="ResolveGraph"/> 同理，
  /// 刻意不看路径，以免路径书写形式差异把正确的单文件构建变成失败构建。
  /// </remarks>
  internal NLCPGBuildContext ResolveDocument(string filePath)
  {
    return DocumentSet is null ? this : DocumentSet.ResolveContext(filePath);
  }

  /// <summary>
  /// **D1 按项路由的解析点**：取某源文件应写入的图。
  /// </summary>
  /// <remarks>
  /// <para>
  /// 无注册表（单文件）时**恒返回** <see cref="Graph"/>，不看 <paramref name="filePath"/>——
  /// 这是刻意的：单文件构建里 fragment 携带的路径可能与 <see cref="FilePath"/> 的书写形式
  /// 不同（绝对/相对、正反斜线），若在此做字符串比较，会把原本正确的单文件构建
  /// 变成抛异常的失败构建。多图语义只在**显式**登记了注册表时启用。
  /// </para>
  /// <para>
  /// 有注册表时按路径解析，未登记即抛出（fail-closed，理由见
  /// <see cref="NLCPGGraphRegistry"/> 的类型注释）。
  /// </para>
  /// </remarks>
  internal NLCPGGraph ResolveGraph(string filePath)
  {
    return GraphRegistry is null ? Graph : GraphRegistry.Resolve(filePath);
  }

  internal IReadOnlyList<OperationInventoryEntry> OperationInventory => _operationInventory;

  internal IReadOnlyList<IInvocationOperation> InvocationOperations => _invocationOperations;

  internal IReadOnlyList<IPropertyReferenceOperation> PropertyReferenceOperations => _propertyReferenceOperations;

  internal IReadOnlyList<IFieldReferenceOperation> FieldReferenceOperations => _fieldReferenceOperations;

  internal void AddOperationInventoryEntry(IOperation operation, IOperation methodRoot, IMethodSymbol? owningMethod, bool isRoot, NLCPGNode node)
  {
    ArgumentNullException.ThrowIfNull(operation);
    ArgumentNullException.ThrowIfNull(methodRoot);
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

  internal static NLCPGBuildContext Create(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, DeterministicNodeIdTable? preallocatedNodeIds = null, StableNodeIdentityFactory? identityFactory = null, NLCPGGraphRegistry? graphRegistry = null)
  {
    var graph = graphRegistry is null
      ? new NLCPGGraph(preallocatedNodeIds, identityFactory)
      : graphRegistry.GetOrAdd(filePath);
    graph.RegisterSource(filePath, source);
    var fullPath = Path.GetFullPath(filePath);
    var syntaxTreeNode = graph.AddNode(new NLCPGNodeDraft(
      Kind: NLCPGNodeKind.SyntaxTree,
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
      syntaxTreeNode,
      graphRegistry);
  }

  internal static NLCPGBuildContext CreateAnchorDiscovery(SemanticModel semanticModel, SyntaxNode root, string source, string filePath, StableNodeIdentityFactory identityFactory, Action<StableNodeAnchor> observeAnchor)
  {
    var graph = NLCPGGraph.CreateAnchorDiscovery(identityFactory, observeAnchor);
    graph.RegisterSource(filePath, source);
    var syntaxTreeNode = graph.AddNode(new NLCPGNodeDraft(
      Kind: NLCPGNodeKind.SyntaxTree,
      Name: Path.GetFileName(filePath),
      FullName: Path.GetFullPath(filePath),
      FilePath: filePath,
      SpanStart: 0,
      SpanEnd: source.Length));
    return new NLCPGBuildContext(semanticModel, root, source, filePath, graph, syntaxTreeNode, graphRegistry: null);
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

  /// <summary>
  /// **多文件**的语义输入：把全部源文件放进**同一个** <see cref="CSharpCompilation"/>，
  /// 再逐文件取 <see cref="SemanticModel"/>。
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ 这不是优化，是 D1 的**正确性前提**。若沿用电文件的
  /// <see cref="CreateSourceSemanticInput"/>（每文件一个只含自身的 compilation），
  /// 则 A 文件里对 B 文件类型的调用在该语义模型下**无法解析**：
  /// 调用目标退化为错误符号，调用图边、成员访问事实随之缺失或错误。
  /// 而把文件放进同一个装箱池的全部意义，正是让跨文件的完整方法能被一起处理。
  /// </para>
  /// <para>
  /// 共用 compilation 同时保证：同名类型的 <see cref="IMethodSymbol"/> 在各文件间
  /// **相互可比**，故 <c>SymbolEqualityComparer</c> 与符号缓存（按稳定键）
  /// 在跨文件时仍是一致的命名空间。
  /// </para>
  /// </remarks>
  internal static IReadOnlyList<NLCPGSourceSemanticInput> CreateSourceSemanticInputs(
    IReadOnlyList<(string FilePath, string Source)> files)
  {
    ArgumentNullException.ThrowIfNull(files);

    var syntaxTrees = new SyntaxTree[files.Count];
    for (var index = 0; index < files.Count; index += 1)
    {
      syntaxTrees[index] = CSharpSyntaxTree.ParseText(files[index].Source, path: files[index].FilePath);
    }

    var compilation = CSharpCompilation.Create(
      assemblyName: "NLCPG.MultiFileBuild",
      syntaxTrees: syntaxTrees,
      references: NLCPGBuilder.CreateMetadataReferences());

    var inputs = new List<NLCPGSourceSemanticInput>(syntaxTrees.Length);
    foreach (var syntaxTree in syntaxTrees)
    {
      var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
      inputs.Add(new NLCPGSourceSemanticInput(semanticModel, syntaxTree.GetRoot()));
    }

    return inputs;
  }
}

internal sealed record OperationInventoryEntry(
  IOperation Operation,
  IOperation MethodRoot,
  IMethodSymbol? OwningMethod,
  bool IsRoot,
  NLCPGNode Node);
