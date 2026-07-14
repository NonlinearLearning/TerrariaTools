using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Builder.Passes;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder;

/// <summary>
/// 从单个源码文件构建最小 Roslyn 风格代码属性图。
/// </summary>
public sealed partial class RoslynCpgBuilder
{
    private static readonly IReadOnlyList<IRoslynCpgPass> LegacyPipeline = new IRoslynCpgPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
        OperationPass.Instance,
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };
    private static readonly IReadOnlyList<IRoslynCpgPass> PartitionedPreOperationPipeline = new IRoslynCpgPass[]
    {
        SyntaxPass.Instance,
        MethodDecorationPass.Instance,
    };
    private static readonly IReadOnlyList<IRoslynCpgPass> PartitionedPostOperationPipeline = new IRoslynCpgPass[]
    {
        CallGraphPass.Instance,
        MemberAccessPass.Instance,
        ControlFlowPass.Instance,
        DataFlowPass.Instance,
    };

    private readonly Dictionary<SyntaxNode, RoslynCpgNode> _syntaxNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RoslynCpgNode> _symbolNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<IOperation, RoslynCpgNode> _operationNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IOperation, IMethodSymbol> _operationOwningMethods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RoslynCpgNode> _typeDeclNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodParameterNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodReturnNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodEntryNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoslynCpgNode> _methodExitNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IMethodSymbol>> _methodSymbolsByNameAndSignature = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<INamedTypeSymbol>> _baseTypeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _cfgPredecessorsByNodeId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _cfgSuccessorsByNodeId = new(StringComparer.Ordinal);
    private readonly Dictionary<IInvocationOperation, RoslynCpgNode> _callSiteNodesByInvocation =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RoslynCpgNode> _propertyAccessorCallSiteNodesByKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyntaxNode> _pendingOperationSyntaxTypeNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private readonly RoslynCpgBuilderOptions _options;
    private int _operationSequence;
    private int _referenceSequence;
    private int _typeReferenceSequence;
    private int _callSiteSequence;
    private int _memberAccessSequence;
    private RoslynCpgSyntaxPassTelemetry _syntaxPassTelemetry = RoslynCpgSyntaxPassTelemetry.CreateDefault();
    private RoslynCpgDataFlowPassTelemetry _dataFlowPassTelemetry = RoslynCpgDataFlowPassTelemetry.CreateDefault();

    private sealed record LoopControlTargets(IOperation? ContinueTarget, IOperation? BreakTarget);
    private sealed record DefinitionFact(string LocationKey, string? BaseKey, string Category, string? PathKey = null);

    public RoslynCpgBuilder(RoslynCpgBuilderOptions? options = null)
    {
        _options = options ?? RoslynCpgBuilderOptions.CreateDefault();
    }

    public RoslynCpgBuildTelemetry LastBuildTelemetry { get; private set; } = RoslynCpgBuildTelemetry.CreateDefault();

    /// <summary>
    /// 解析源码、收集语法和操作，再补 CFG 与 DataFlow 叠加层。
    /// </summary>
    public RoslynCpgGraph BuildFromSource(string source, string filePath = "input.cs")
    {
        return Build(RoslynCpgBuildContext.CreateFromSource(source, filePath));
    }

    public RoslynCpgGraph BuildFromSemanticModel(SemanticModel semanticModel, SyntaxNode root, string source, string filePath)
    {
        return Build(RoslynCpgBuildContext.Create(semanticModel, root, source, filePath));
    }

    private RoslynCpgGraph Build(RoslynCpgBuildContext context)
    {
        _syntaxNodes.Clear();
        _symbolNodes.Clear();
        _operationNodes.Clear();
        _operationOwningMethods.Clear();
        _typeDeclNodes.Clear();
        _methodNodes.Clear();
        _methodParameterNodes.Clear();
        _methodReturnNodes.Clear();
        _methodEntryNodes.Clear();
        _methodExitNodes.Clear();
        _methodSymbolsByFullName.Clear();
        _methodSymbolsByNameAndSignature.Clear();
        _baseTypeCache.Clear();
        _cfgPredecessorsByNodeId.Clear();
        _cfgSuccessorsByNodeId.Clear();
        _callSiteNodesByInvocation.Clear();
        _propertyAccessorCallSiteNodesByKey.Clear();
        _pendingOperationSyntaxTypeNodes.Clear();
        _declaredTypes.Clear();
        _operationSequence = 0;
        _referenceSequence = 0;
        _typeReferenceSequence = 0;
        _callSiteSequence = 0;
        _memberAccessSequence = 0;
        _syntaxPassTelemetry = RoslynCpgSyntaxPassTelemetry.CreateDefault();
        _dataFlowPassTelemetry = RoslynCpgDataFlowPassTelemetry.CreateDefault();
        LastBuildTelemetry = RoslynCpgBuildTelemetry.CreateDefault();
        var operationBuildStrategy = CreateOperationBuildStrategy(context);
        if (!operationBuildStrategy.UsePartitionedOperationBuild)
        {
            RunPipeline(LegacyPipeline, context);
        }
        else
        {
            RunPipeline(PartitionedPreOperationPipeline, context);

            RunPartitionedOperationPass(context, operationBuildStrategy.OperationRoots);

            CompleteOperationBackedSyntaxTypes(context);

            RunPipeline(PartitionedPostOperationPipeline, context);
        }

        LastBuildTelemetry = new RoslynCpgBuildTelemetry(
          _options.BuildMode,
          operationBuildStrategy.ExecutedMode,
          operationBuildStrategy.UsePartitionedOperationBuild,
          operationBuildStrategy.SourceLineCount,
          operationBuildStrategy.UsePartitionedOperationBuild ? operationBuildStrategy.OperationRoots.Count : 0,
          _options.EffectiveMaxDegreeOfParallelism,
          _syntaxPassTelemetry,
          _dataFlowPassTelemetry);
        return context.Graph;
    }

    private void RunPipeline(IReadOnlyList<IRoslynCpgPass> pipeline, RoslynCpgBuildContext context)
    {
        foreach (var pass in pipeline)
        {
            pass.Run(this, context);
        }
    }

    private void CompleteOperationBackedSyntaxTypes(RoslynCpgBuildContext context)
    {
        foreach (var syntax in _pendingOperationSyntaxTypeNodes.ToArray())
        {
            if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode))
            {
                continue;
            }

            var typeSymbol = context.SemanticModel.GetTypeInfo(syntax).Type;
            AddTypeEdges(syntaxNode, typeSymbol, context.Graph);
        }

        _pendingOperationSyntaxTypeNodes.Clear();
    }

    private void AddOperationBackedSyntaxTypeEdge(IOperation operation, RoslynCpgGraph graph)
    {
        if (!_pendingOperationSyntaxTypeNodes.Remove(operation.Syntax) ||
            !_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
        {
            return;
        }

        if (operation.Type is null)
        {
            _pendingOperationSyntaxTypeNodes.Add(operation.Syntax);
            return;
        }

        AddTypeEdges(syntaxNode, operation.Type, graph);
    }

    private void AddControlFlowEdge(
      RoslynCpgNode sourceNode,
      RoslynCpgNode targetNode,
      RoslynCpgEdgeKind edgeKind,
      RoslynCpgGraph graph)
    {
        graph.AddEdge(sourceNode, targetNode, edgeKind);
        if (edgeKind is not (RoslynCpgEdgeKind.CfgNext or RoslynCpgEdgeKind.CfgTrue or RoslynCpgEdgeKind.CfgFalse))
        {
            return;
        }

        AddCfgNeighbor(_cfgSuccessorsByNodeId, sourceNode.Id, targetNode.Id);
        AddCfgNeighbor(_cfgPredecessorsByNodeId, targetNode.Id, sourceNode.Id);
    }

    private IReadOnlyCollection<string> GetCachedCfgPredecessors(string nodeId)
    {
        return _cfgPredecessorsByNodeId.TryGetValue(nodeId, out var predecessors)
          ? predecessors
          : Array.Empty<string>();
    }

    private IReadOnlyCollection<string> GetCachedCfgSuccessors(string nodeId)
    {
        return _cfgSuccessorsByNodeId.TryGetValue(nodeId, out var successors)
          ? successors
          : Array.Empty<string>();
    }

    private static void AddCfgNeighbor(
      Dictionary<string, HashSet<string>> neighborsByNodeId,
      string nodeId,
      string neighborNodeId)
    {
        if (!neighborsByNodeId.TryGetValue(nodeId, out var neighbors))
        {
            neighbors = new HashSet<string>(StringComparer.Ordinal);
            neighborsByNodeId[nodeId] = neighbors;
        }

        neighbors.Add(neighborNodeId);
    }

    private static string PropertyAccessorCallSiteKey(
      IPropertyReferenceOperation propertyReference,
      IMethodSymbol accessorMethod)
    {
        return $"{propertyReference.Syntax.SpanStart}:{propertyReference.Syntax.Span.End}:{accessorMethod.Name}";
    }


    private RoslynCpgNode GetOrCreateOperationNode(IOperation operation, RoslynCpgGraph graph)
    {
        if (_operationNodes.TryGetValue(operation, out var existing))
        {
            return existing;
        }

        _operationSequence += 1;
        var kind = MapOperationKind(operation);
        var node = graph.AddNode(new RoslynCpgNode(
          Id: $"op:{_operationSequence}:{operation.Kind}:{operation.Syntax.SpanStart}:{operation.Syntax.Span.End}",
          Kind: kind,
          DisplayKind: operation.Kind.ToString(),
          Name: ResolveOperationName(operation),
          FullName: ResolveOperationFullName(operation),
          Signature: ResolveOperationSignature(operation),
          TypeFullName: ComposeTypeFullName(operation.Type),
          FilePath: operation.Syntax.SyntaxTree.FilePath,
          SpanStart: operation.Syntax.SpanStart,
          SpanEnd: operation.Syntax.Span.End,
          Text: Shorten(operation.Syntax.ToString()),
          IsImplicit: operation.IsImplicit));
        _operationNodes[operation] = node;
        return node;
    }

    private void AddDeclaredSymbolEdges(SyntaxNode syntax, RoslynCpgNode syntaxNode, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(syntax);
        AddDeclaredSymbolEdges(syntaxNode, symbol, graph);
    }

    private void AddDeclaredSymbolEdges(RoslynCpgNode syntaxNode, ISymbol? symbol, RoslynCpgGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, RoslynCpgEdgeKind.DeclaresSymbol);

        if (symbol is INamedTypeSymbol declaredTypeSymbol)
        {
            _declaredTypes.Add(declaredTypeSymbol);
            var typeDeclNode = GetOrCreateTypeDeclNode(declaredTypeSymbol, graph);
            graph.AddEdge(syntaxNode, typeDeclNode, RoslynCpgEdgeKind.SyntaxChild);
            graph.AddEdge(typeDeclNode, symbolNode, RoslynCpgEdgeKind.DeclaresSymbol);
            graph.AddEdge(typeDeclNode, symbolNode, RoslynCpgEdgeKind.RefersToType);
        }

        if (symbol.ContainingSymbol is not null && symbol.ContainingSymbol.Kind != SymbolKind.NetModule)
        {
            var containerNode = GetOrCreateSymbolNode(symbol.ContainingSymbol, graph);
            graph.AddEdge(containerNode, symbolNode, RoslynCpgEdgeKind.ContainsSymbol);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            foreach (var baseType in namedType.Interfaces.Cast<ITypeSymbol>().Append(namedType.BaseType).Where(x => x is not null))
            {
                var baseTypeNode = GetOrCreateSymbolNode(baseType!, graph);
                graph.AddEdge(symbolNode, baseTypeNode, RoslynCpgEdgeKind.BaseType);
                var typeDeclNode = GetOrCreateTypeDeclNode(namedType, graph);
                graph.AddEdge(typeDeclNode, baseTypeNode, RoslynCpgEdgeKind.InheritsFrom);
            }
        }

        if (symbol is IMethodSymbol declaredMethodSymbol && declaredMethodSymbol.ReturnType is not null)
        {
            var returnTypeNode = GetOrCreateSymbolNode(declaredMethodSymbol.ReturnType, graph);
            graph.AddEdge(symbolNode, returnTypeNode, RoslynCpgEdgeKind.ReturnsType);
        }

        AddTypeEdges(symbolNode, SymbolTypeOf(symbol), graph);
    }

    private void AddReferencedSymbolEdges(SyntaxNode syntax, RoslynCpgNode syntaxNode, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
        if (!CanReferenceSymbol(syntax))
        {
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(syntax).Symbol;
        AddReferencedSymbolEdges(syntax, syntaxNode, symbol, graph);
    }

    private void AddReferencedSymbolEdges(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      ISymbol? symbol,
      RoslynCpgGraph graph)
    {
        if (symbol is null)
        {
            return;
        }

        var symbolNode = GetOrCreateSymbolNode(symbol, graph);
        graph.AddEdge(syntaxNode, symbolNode, RoslynCpgEdgeKind.ReferencesSymbol);

        _referenceSequence += 1;
        var referenceNode = graph.AddNode(new RoslynCpgNode(
          Id: $"ref:{_referenceSequence}:{syntax.SpanStart}:{syntax.Span.End}",
          Kind: RoslynCpgNodeKind.Reference,
          DisplayKind: nameof(RoslynCpgNodeKind.Reference),
          Name: syntaxNode.Name,
          FullName: symbolNode.FullName,
          TypeFullName: symbolNode.TypeFullName,
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd,
          Text: syntaxNode.Text));
        graph.AddEdge(syntaxNode, referenceNode, RoslynCpgEdgeKind.SyntaxChild);
        graph.AddEdge(referenceNode, symbolNode, RoslynCpgEdgeKind.Ref);
        AddEvalTypeEdge(referenceNode, SymbolTypeOf(symbol), graph);
    }

    private void AddTypeEdges(RoslynCpgNode sourceNode, ITypeSymbol? typeSymbol, RoslynCpgGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, RoslynCpgEdgeKind.HasType);
    }

    private void AddEvalTypeEdge(RoslynCpgNode sourceNode, ITypeSymbol? typeSymbol, RoslynCpgGraph graph)
    {
        if (typeSymbol is null)
        {
            return;
        }

        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(sourceNode, typeNode, RoslynCpgEdgeKind.EvalType);
    }

    private void AddTypeReferenceEdges(
      SyntaxNode syntax,
      RoslynCpgNode syntaxNode,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      ITypeSymbol? resolvedTypeSymbol = null)
    {
        if (syntax is not TypeSyntax and not ObjectCreationExpressionSyntax and not BaseTypeSyntax)
        {
            return;
        }

        var typeSymbol = resolvedTypeSymbol ?? syntax switch
        {
            TypeSyntax typeSyntax => semanticModel.GetTypeInfo(typeSyntax).Type,
            ObjectCreationExpressionSyntax creation => semanticModel.GetTypeInfo(creation).Type,
            BaseTypeSyntax baseType => semanticModel.GetTypeInfo(baseType.Type).Type,
            _ => null,
        };
        if (typeSymbol is null)
        {
            return;
        }

        _typeReferenceSequence += 1;
        var typeRefNode = graph.AddNode(new RoslynCpgNode(
          Id: $"typeref:{_typeReferenceSequence}:{syntax.SpanStart}:{syntax.Span.End}",
          Kind: RoslynCpgNodeKind.TypeRef,
          DisplayKind: nameof(RoslynCpgNodeKind.TypeRef),
          Name: typeSymbol.Name,
          FullName: ComposeTypeFullName(typeSymbol),
          TypeFullName: ComposeTypeFullName(typeSymbol),
          FilePath: syntaxNode.FilePath,
          SpanStart: syntaxNode.SpanStart,
          SpanEnd: syntaxNode.SpanEnd,
          Text: syntaxNode.Text));
        graph.AddEdge(syntaxNode, typeRefNode, RoslynCpgEdgeKind.SyntaxChild);
        var typeNode = GetOrCreateSymbolNode(typeSymbol, graph);
        graph.AddEdge(typeRefNode, typeNode, RoslynCpgEdgeKind.RefersToType);
    }

    private RoslynCpgNode GetOrCreateSymbolNode(ISymbol symbol, RoslynCpgGraph graph)
    {
        var symbolKey = SymbolId(symbol);
        if (_symbolNodes.TryGetValue(symbolKey, out var existing))
        {
            return existing;
        }

        var symbolNode = graph.AddNode(new RoslynCpgNode(
          Id: symbolKey,
          Kind: MapSymbolKind(symbol),
          DisplayKind: symbol.Kind.ToString(),
          Name: symbol.Name,
          FullName: ComposeFullName(symbol),
          Signature: ComposeSignature(symbol),
          DispatchKind: symbol is IMethodSymbol methodDispatchSymbol ? ComposeMethodDispatchKind(methodDispatchSymbol) : null,
          TypeFullName: ComposeTypeFullName(SymbolTypeOf(symbol)),
          FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        _symbolNodes[symbolKey] = symbolNode;
        if (symbol is IMethodSymbol registeredMethodSymbol)
        {
            RegisterMethodSymbol(registeredMethodSymbol);
        }

        return symbolNode;
    }

    private RoslynCpgNode GetOrCreateTypeDeclNode(INamedTypeSymbol symbol, RoslynCpgGraph graph)
    {
        var key = $"typedecl:{ComposeFullName(symbol)}";
        if (_typeDeclNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var typeDeclNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.TypeDecl,
          DisplayKind: nameof(RoslynCpgNodeKind.TypeDecl),
          Name: symbol.Name,
          FullName: ComposeFullName(symbol),
          Signature: ComposeTypeParameterSignature(symbol),
          TypeFullName: ComposeTypeFullName(symbol),
          FilePath: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        _typeDeclNodes[key] = typeDeclNode;
        return typeDeclNode;
    }


    private static RoslynCpgEdgeKind SelectOperationEdge(IOperation parent, IOperation child)
    {
        return parent switch
        {
            IInvocationOperation when child is IArgumentOperation => RoslynCpgEdgeKind.OpArgument,
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IFieldReferenceOperation fieldReference when ReferenceEquals(fieldReference.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IPropertyReferenceOperation when child is IArgumentOperation => RoslynCpgEdgeKind.OpArgument,
            IPropertyReferenceOperation propertyReference when ReferenceEquals(propertyReference.Instance, child) => RoslynCpgEdgeKind.OpInstance,
            IReturnOperation when ReferenceEquals(parent.ChildOperations.FirstOrDefault(), child) => RoslynCpgEdgeKind.OpTarget,
            IConditionalOperation conditional when ReferenceEquals(conditional.Condition, child) => RoslynCpgEdgeKind.OpCondition,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenTrue, child) => RoslynCpgEdgeKind.OpWhenTrue,
            IConditionalOperation conditional when ReferenceEquals(conditional.WhenFalse, child) => RoslynCpgEdgeKind.OpWhenFalse,
            ILoopOperation loop when ReferenceEquals(loop.Body, child) => RoslynCpgEdgeKind.OpBody,
            _ => RoslynCpgEdgeKind.OpChild,
        };
    }

    private static RoslynCpgNodeKind MapSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.Namespace => RoslynCpgNodeKind.SymbolNamespace,
            SymbolKind.NamedType => RoslynCpgNodeKind.SymbolType,
            SymbolKind.Method => RoslynCpgNodeKind.SymbolMethod,
            SymbolKind.Property => RoslynCpgNodeKind.SymbolProperty,
            SymbolKind.Field => RoslynCpgNodeKind.SymbolField,
            SymbolKind.Local => RoslynCpgNodeKind.SymbolLocal,
            SymbolKind.Parameter => RoslynCpgNodeKind.SymbolParameter,
            _ => RoslynCpgNodeKind.SymbolUnknown,
        };
    }

    private static RoslynCpgNodeKind MapOperationKind(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation => RoslynCpgNodeKind.OpBlock,
            IInvocationOperation => RoslynCpgNodeKind.OpInvocation,
            IArgumentOperation => RoslynCpgNodeKind.OpArgument,
            IBinaryOperation => RoslynCpgNodeKind.OpBinary,
            IAssignmentOperation => RoslynCpgNodeKind.OpAssignment,
            ILocalReferenceOperation => RoslynCpgNodeKind.OpLocalReference,
            IParameterReferenceOperation => RoslynCpgNodeKind.OpParameterReference,
            IFieldReferenceOperation => RoslynCpgNodeKind.OpFieldReference,
            IPropertyReferenceOperation => RoslynCpgNodeKind.OpPropertyReference,
            ILiteralOperation => RoslynCpgNodeKind.OpLiteral,
            IReturnOperation => RoslynCpgNodeKind.OpReturn,
            IBranchOperation branch when branch.BranchKind == BranchKind.Break => RoslynCpgNodeKind.OpBreak,
            IBranchOperation branch when branch.BranchKind == BranchKind.Continue => RoslynCpgNodeKind.OpContinue,
            ISwitchOperation => RoslynCpgNodeKind.OpSwitch,
            ITryOperation => RoslynCpgNodeKind.OpTry,
            ICatchClauseOperation => RoslynCpgNodeKind.OpCatch,
            IConditionalOperation => RoslynCpgNodeKind.OpConditional,
            ILoopOperation => RoslynCpgNodeKind.OpLoop,
            _ => RoslynCpgNodeKind.Operation,
        };
    }

    private static ISymbol? ResolveOperationSymbol(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null,
        };
    }

    private static ITypeSymbol? SymbolTypeOf(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            ITypeSymbol type => type,
            _ => null,
        };
    }

    private static bool CanReferenceSymbol(SyntaxNode syntax)
    {
        return syntax is IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax;
    }

    private static string SymbolId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
          ?? $"symbol:{symbol.Kind}:{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}:{symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? -1}";
    }

    private static string ComposeFullName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string ComposeTypeFullName(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
          .Replace("global::", string.Empty, StringComparison.Ordinal)
          ?? string.Empty;
    }

    private static string SyntaxId(SyntaxNode syntax, string filePath)
    {
        return $"syntax:{Path.GetFullPath(filePath)}:{syntax.RawKind}:{syntax.SpanStart}:{syntax.Span.End}";
    }

    private static string TokenId(SyntaxToken token, string filePath)
    {
        return $"token:{Path.GetFullPath(filePath)}:{token.RawKind}:{token.SpanStart}:{token.Span.End}";
    }

    private static string ResolveOperationName(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.Name,
            ILocalReferenceOperation localReference => localReference.Local.Name,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.Name,
            IFieldReferenceOperation fieldReference => fieldReference.Field.Name,
            IPropertyReferenceOperation propertyReference => propertyReference.Property.Name,
            _ => operation.Kind.ToString(),
        };
    }

    private static string? ResolveOperationFullName(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeFullName(symbol) : null;
    }

    private static string? ResolveOperationSignature(IOperation operation)
    {
        return ResolveOperationSymbol(operation) is { } symbol ? ComposeSignature(symbol) : null;
    }


    private static IMethodSymbol CanonicalMethodSymbol(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is null)
        {
            return methodSymbol;
        }

        return methodSymbol.ReducedFrom;
    }

    private static string ComposeMethodFullName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeInvocationMethodFullName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(methodSymbol.ContainingType) + ".";
        return $"{containingType}{ComposeInvocationMethodName(methodSymbol)}:{ComposeInvocationSignature(methodSymbol)}";
    }

    private static string ComposeMethodSignature(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeInvocationSignature(IMethodSymbol methodSymbol)
    {
        var parameterTypes = string.Join(",", methodSymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        var returnType = ComposeTypeFullName(methodSymbol.ReturnType);
        var genericSuffix = ComposeMethodInstantiationKey(methodSymbol);
        return $"{returnType}{genericSuffix}({parameterTypes})";
    }

    private static string ComposeMethodLookupKey(IMethodSymbol methodSymbol)
    {
        return $"{ComposeMethodName(methodSymbol)}:{ComposeMethodSignature(methodSymbol)}";
    }

    private static string ComposeMethodName(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation &&
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            var implementedMethod = methodSymbol.ExplicitInterfaceImplementations[0];
            return $"{ComposeTypeFullName(implementedMethod.ContainingType)}.{implementedMethod.Name}";
        }

        return methodSymbol.Name;
    }

    private static string ComposeInvocationMethodName(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return ".ctor";
        }

        if (methodSymbol.MethodKind == MethodKind.StaticConstructor)
        {
            return ".cctor";
        }

        return methodSymbol.Name;
    }

    private static string ComposeMethodInstantiationKey(IMethodSymbol methodSymbol)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        if (methodSymbol.TypeArguments.Length == 0 && methodSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = methodSymbol.TypeArguments.Length > 0
          ? methodSymbol.TypeArguments.Select(ComposeGenericTypeIdentity)
          : methodSymbol.TypeParameters.Select(parameter => $"{parameter.Ordinal}:{parameter.Name}");
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposeGenericTypeIdentity(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            ITypeParameterSymbol typeParameter => $"{typeParameter.Ordinal}:{typeParameter.Name}",
            _ => ComposeTypeFullName(typeSymbol),
        };
    }

    private static string ComposeSignature(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => ComposeMethodSignature(methodSymbol),
            INamedTypeSymbol typeSymbol => ComposeTypeParameterSignature(typeSymbol),
            IPropertySymbol propertySymbol => ComposePropertySignature(propertySymbol),
            IFieldSymbol fieldSymbol => ComposeTypeFullName(fieldSymbol.Type),
            ILocalSymbol localSymbol => ComposeTypeFullName(localSymbol.Type),
            IParameterSymbol parameterSymbol => ComposeTypeFullName(parameterSymbol.Type),
            _ => string.Empty,
        };
    }

    private static string ComposeCallDispatchKind(IMethodSymbol methodSymbol, bool hasInstance = true)
    {
        var prefix = IsInternalMethod(methodSymbol) ? "internal-" : "external-";
        if (methodSymbol.IsExtensionMethod)
        {
            return prefix + (hasInstance ? "extension-instance" : "extension-static");
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return prefix + "interface-implementation";
        }

        if (!hasInstance || methodSymbol.IsStatic)
        {
            return prefix + "static";
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return prefix + "interface-dispatch";
        }

        if (methodSymbol.IsOverride)
        {
            return prefix + "override-dispatch";
        }

        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual)
        {
            return prefix + "virtual-dispatch";
        }

        return prefix + "static";
    }

    private static string ComposePropertyAccessorDispatchKind(IMethodSymbol methodSymbol, bool hasInstance)
    {
        var baseDispatch = ComposeCallDispatchKind(methodSymbol, hasInstance);
        var isIndexer = methodSymbol.AssociatedSymbol is IPropertySymbol { Parameters.Length: > 0 };
        if (methodSymbol.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            return baseDispatch + (isIndexer ? "-indexer-get" : "-property-get");
        }

        if (methodSymbol.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return baseDispatch + (isIndexer ? "-indexer-set" : "-property-set");
        }

        return methodSymbol.AssociatedSymbol is IPropertySymbol
          ? baseDispatch + (isIndexer ? "-indexer-accessor" : "-property-accessor")
          : baseDispatch;
    }

    private static string ComposeResolvedDispatchKind(IMethodSymbol resolvedMethod, IMethodSymbol requestedMethod, ITypeSymbol? receiverType, string baseDispatchKind)
    {
        if (!IsInternalMethod(resolvedMethod))
        {
            return baseDispatchKind + "-external-fallback";
        }

        if (string.Equals(ComposeMethodFullName(resolvedMethod), ComposeMethodFullName(requestedMethod), StringComparison.Ordinal))
        {
            return baseDispatchKind + "-exact";
        }

        if (receiverType is INamedTypeSymbol namedReceiverType && resolvedMethod.ContainingType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(resolvedMethod.ContainingType, namedReceiverType))
            {
                return baseDispatchKind + "-receiver-exact";
            }

            if (InheritsFrom(namedReceiverType, resolvedMethod.ContainingType))
            {
                return baseDispatchKind + "-hierarchy";
            }
        }

        return baseDispatchKind + "-fallback";
    }

    private static string ComposeMethodDispatchKind(IMethodSymbol methodSymbol)
    {
        var prefix = IsInternalMethod(methodSymbol) ? "internal-" : "external-";
        if (methodSymbol.IsExtensionMethod || methodSymbol.ReducedFrom is not null)
        {
            return prefix + "extension-definition";
        }

        if (methodSymbol.MethodKind == MethodKind.ExplicitInterfaceImplementation ||
            methodSymbol.ExplicitInterfaceImplementations.Length > 0)
        {
            return prefix + "interface-implementation";
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return prefix + "interface-definition";
        }

        if (methodSymbol.IsOverride)
        {
            return prefix + "override-definition";
        }

        if (methodSymbol.IsAbstract)
        {
            return prefix + "abstract-definition";
        }

        if (methodSymbol.IsVirtual)
        {
            return prefix + "virtual-definition";
        }

        return prefix + (methodSymbol.IsStatic ? "static-definition" : "instance-definition");
    }

    private static bool IsInternalMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Locations.Any(location => location.IsInSource);
    }

    private static string ComposeTypeParameterSignature(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeArguments.Length == 0 && typeSymbol.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        var typeParameters = typeSymbol.TypeArguments.Length > 0
          ? typeSymbol.TypeArguments.Select(ComposeTypeFullName)
          : typeSymbol.TypeParameters.Select(parameter => parameter.Name);
        return $"<{string.Join(",", typeParameters)}>";
    }

    private static string ComposePropertySignature(IPropertySymbol propertySymbol)
    {
        if (propertySymbol.Parameters.Length == 0)
        {
            return ComposeTypeFullName(propertySymbol.Type);
        }

        var parameterTypes = string.Join(",", propertySymbol.Parameters.Select(parameter => ComposeTypeFullName(parameter.Type)));
        return $"{ComposeTypeFullName(propertySymbol.Type)}[{parameterTypes}]";
    }


    private static bool InheritsFrom(INamedTypeSymbol candidateType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateType, targetType))
        {
            return true;
        }

        foreach (var baseType in candidateType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, targetType))
            {
                return true;
            }
        }

        for (var current = candidateType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetType))
            {
                return true;
            }
        }

        return false;
    }

    private static string NameOfMethod(BaseMethodDeclarationSyntax declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => declaration.Kind().ToString(),
        };
    }

    private static string Shorten(string text, int maxLength = 120)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    internal static IReadOnlyList<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return Array.Empty<MetadataReference>();
        }

        return trustedPlatformAssemblies
          .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
          .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
          .Select(path => MetadataReference.CreateFromFile(path))
          .ToList();
    }
}
