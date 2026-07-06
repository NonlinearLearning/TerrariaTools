using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder;

/// <summary>
/// 从单个源码文件构建最小 Roslyn 风格代码属性图。
/// </summary>
public sealed class RoslynCpgBuilder
{
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
    private readonly List<INamedTypeSymbol> _declaredTypes = new();
    private int _operationSequence;
    private int _referenceSequence;
    private int _typeReferenceSequence;
    private int _callSiteSequence;
    private int _memberAccessSequence;

    private sealed record LoopControlTargets(IOperation? ContinueTarget, IOperation? BreakTarget);
    private sealed record DefinitionFact(string LocationKey, string? BaseKey, string Category, string? PathKey = null);

    /// <summary>
    /// 解析源码、收集语法和操作，再补 CFG 与 DataFlow 叠加层。
    /// </summary>
    public RoslynCpgGraph BuildFromSource(string source, string filePath = "input.cs")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var compilation = CSharpCompilation.Create(
          assemblyName: Path.GetFileNameWithoutExtension(filePath),
          syntaxTrees: new[] { syntaxTree },
          references: CreateMetadataReferences());
        var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        return BuildFromSemanticModel(semanticModel, syntaxTree.GetRoot(), source, filePath);
    }

    public RoslynCpgGraph BuildFromSemanticModel(
      SemanticModel semanticModel,
      SyntaxNode root,
      string source,
      string filePath)
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
        _declaredTypes.Clear();
        _operationSequence = 0;
        _referenceSequence = 0;
        _typeReferenceSequence = 0;
        _callSiteSequence = 0;
        _memberAccessSequence = 0;
        var graph = new RoslynCpgGraph();

        var syntaxTreeNode = graph.AddNode(new RoslynCpgNode(
          Id: $"tree:{Path.GetFullPath(filePath)}",
          Kind: RoslynCpgNodeKind.SyntaxTree,
          DisplayKind: nameof(RoslynCpgNodeKind.SyntaxTree),
          Name: Path.GetFileName(filePath),
          FullName: Path.GetFullPath(filePath),
          FilePath: filePath,
          SpanStart: 0,
          SpanEnd: source.Length,
          Text: source));

        // 构图按三段执行：先建语法/符号层，再建操作层，最后补控制流和数据流。
        VisitSyntax(root, syntaxTreeNode, graph, semanticModel, filePath);
        VisitOperationRoots(root, graph, semanticModel);
        AddMethodLevelControlFlow(graph);
        AddReachingDefinitionDataFlow(graph);
        return graph;
    }

    /// <summary>
    /// 遍历语法树，建立语法节点、token、声明绑定和引用绑定。
    /// </summary>
    private void VisitSyntax(
      SyntaxNode syntax,
      RoslynCpgNode parent,
      RoslynCpgGraph graph,
      SemanticModel semanticModel,
      string filePath)
    {
        var syntaxNode = graph.AddNode(new RoslynCpgNode(
          Id: SyntaxId(syntax, filePath),
          Kind: RoslynCpgNodeKind.SyntaxNode,
          DisplayKind: syntax.Kind().ToString(),
          Name: syntax switch
          {
              BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
              BaseMethodDeclarationSyntax methodDeclaration => NameOfMethod(methodDeclaration),
              VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
              ParameterSyntax parameter => parameter.Identifier.ValueText,
              _ => null,
          },
          FilePath: filePath,
          SpanStart: syntax.SpanStart,
          SpanEnd: syntax.Span.End,
          Text: Shorten(syntax.ToString())));
        _syntaxNodes[syntax] = syntaxNode;
        graph.AddEdge(parent, syntaxNode, RoslynCpgEdgeKind.SyntaxChild);

        AddDeclaredSymbolEdges(syntax, syntaxNode, graph, semanticModel);
        AddReferencedSymbolEdges(syntax, syntaxNode, graph, semanticModel);
        AddTypeEdges(syntaxNode, semanticModel.GetTypeInfo(syntax).Type, graph);
        AddTypeReferenceEdges(syntax, syntaxNode, graph, semanticModel);

        foreach (var childNode in syntax.ChildNodes())
        {
            VisitSyntax(childNode, syntaxNode, graph, semanticModel, filePath);
        }

        foreach (var childToken in syntax.ChildTokens())
        {
            var tokenNode = graph.AddNode(new RoslynCpgNode(
              Id: TokenId(childToken, filePath),
              Kind: RoslynCpgNodeKind.SyntaxToken,
              DisplayKind: childToken.Kind().ToString(),
              Name: childToken.ValueText.Length > 0 ? childToken.ValueText : childToken.Text,
              FilePath: filePath,
              SpanStart: childToken.SpanStart,
              SpanEnd: childToken.Span.End,
              Text: childToken.Text));
            graph.AddEdge(syntaxNode, tokenNode, RoslynCpgEdgeKind.TokenChild);
        }
    }

    /// <summary>
    /// 以方法、accessor 和全局语句为根，启动 Roslyn operation 树采集。
    /// </summary>
    private void VisitOperationRoots(SyntaxNode root, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
        foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            SyntaxNode? bodySyntax = method switch
            {
                MethodDeclarationSyntax x when x.Body is not null => x.Body,
                MethodDeclarationSyntax x when x.ExpressionBody is not null => x.ExpressionBody.Expression,
                ConstructorDeclarationSyntax x when x.Body is not null => x.Body,
                ConstructorDeclarationSyntax x when x.ExpressionBody is not null => x.ExpressionBody.Expression,
                _ => null,
            };
            if (bodySyntax is not null)
            {
                AddOperationTree(semanticModel.GetOperation(bodySyntax), parentOperation: null, owningMethod: methodSymbol, graph);
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            foreach (var accessor in property.AccessorList?.Accessors ?? Enumerable.Empty<AccessorDeclarationSyntax>())
            {
                var accessorSymbol = semanticModel.GetDeclaredSymbol(accessor) as IMethodSymbol;
                SyntaxNode? bodySyntax = accessor switch
                {
                    { Body: not null } x => x.Body,
                    { ExpressionBody: not null } x => x.ExpressionBody.Expression,
                    _ => null,
                };
                if (bodySyntax is not null)
                {
                    AddOperationTree(semanticModel.GetOperation(bodySyntax), parentOperation: null, owningMethod: accessorSymbol, graph);
                }
            }
        }

        foreach (var statement in root.DescendantNodes().OfType<GlobalStatementSyntax>())
        {
            AddOperationTree(semanticModel.GetOperation(statement.Statement), parentOperation: null, owningMethod: null, graph);
        }
    }

    /// <summary>
    /// 递归挂入 Roslyn operation，并把它反绑到语法节点和符号节点。
    /// </summary>
    private void AddOperationTree(IOperation? operation, IOperation? parentOperation, IMethodSymbol? owningMethod, RoslynCpgGraph graph)
    {
        if (operation is null)
        {
            return;
        }

        var operationNode = GetOrCreateOperationNode(operation, graph);
        if (owningMethod is not null && !_operationOwningMethods.ContainsKey(operation))
        {
            _operationOwningMethods[operation] = owningMethod;
        }

        if (parentOperation is not null)
        {
            var parentNode = GetOrCreateOperationNode(parentOperation, graph);
            graph.AddEdge(parentNode, operationNode, SelectOperationEdge(parentOperation, operation));
        }

        if (_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
        {
            graph.AddEdge(syntaxNode, operationNode, RoslynCpgEdgeKind.SyntaxHasOperation);
            graph.AddEdge(operationNode, syntaxNode, RoslynCpgEdgeKind.OpHasSyntax);
        }

        AddTypeEdges(operationNode, operation.Type, graph);
        AddEvalTypeEdge(operationNode, operation.Type, graph);

        var resolvedSymbol = ResolveOperationSymbol(operation);
        if (resolvedSymbol is not null)
        {
            var symbolNode = GetOrCreateSymbolNode(resolvedSymbol, graph);
            graph.AddEdge(operationNode, symbolNode, RoslynCpgEdgeKind.OpResolvesToSymbol);
        }

        if (operation is IInvocationOperation invocationOperation)
        {
            AddCallSite(invocationOperation, operationNode, graph);
        }

        if (operation is IFieldReferenceOperation fieldReferenceOperation)
        {
            AddMemberAccess(fieldReferenceOperation, operationNode, fieldReferenceOperation.Field, fieldReferenceOperation.Instance?.Type, graph);
        }

        if (operation is IPropertyReferenceOperation propertyReferenceOperation)
        {
            AddMemberAccess(propertyReferenceOperation, operationNode, propertyReferenceOperation.Property, propertyReferenceOperation.Instance?.Type, graph);
            AddPropertyAccessorCallSite(propertyReferenceOperation, operationNode, graph);
        }

        foreach (var child in operation.ChildOperations)
        {
            AddOperationTree(child, operation, owningMethod, graph);
        }
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

        if (symbol is IMethodSymbol methodSymbol)
        {
            AddMethodAbstractions(syntaxNode, methodSymbol, graph);
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

    private void AddTypeReferenceEdges(SyntaxNode syntax, RoslynCpgNode syntaxNode, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
        if (syntax is not TypeSyntax and not ObjectCreationExpressionSyntax and not BaseTypeSyntax)
        {
            return;
        }

        var typeSymbol = syntax switch
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

    private void AddMethodAbstractions(RoslynCpgNode syntaxNode, IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
        var methodNode = GetOrCreateMethodNode(methodSymbol, graph);
        var methodSymbolNode = GetOrCreateSymbolNode(methodSymbol, graph);
        graph.AddEdge(syntaxNode, methodNode, RoslynCpgEdgeKind.SyntaxChild);
        graph.AddEdge(methodNode, methodSymbolNode, RoslynCpgEdgeKind.DeclaresSymbol);

        if (methodSymbol.ReturnType is not null)
        {
            var returnTypeNode = GetOrCreateSymbolNode(methodSymbol.ReturnType, graph);
            graph.AddEdge(methodNode, returnTypeNode, RoslynCpgEdgeKind.ReturnsType);
            AddEvalTypeEdge(methodNode, methodSymbol.ReturnType, graph);

            var methodReturnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
            graph.AddEdge(methodNode, methodReturnNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(methodReturnNode, returnTypeNode, RoslynCpgEdgeKind.EvalType);
        }

        var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
        var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
        graph.AddEdge(methodNode, entryNode, RoslynCpgEdgeKind.SyntaxChild);
        graph.AddEdge(methodNode, exitNode, RoslynCpgEdgeKind.SyntaxChild);

        foreach (var parameter in methodSymbol.Parameters)
        {
            var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
            graph.AddEdge(methodNode, parameterNode, RoslynCpgEdgeKind.ParameterLink);
        }
    }

    private RoslynCpgNode GetOrCreateMethodNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var key = $"method:{SymbolId(methodSymbol)}";
        if (_methodNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var methodNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.Method,
          DisplayKind: nameof(RoslynCpgNodeKind.Method),
          Name: ComposeMethodName(methodSymbol),
          FullName: ComposeMethodFullName(methodSymbol),
          Signature: ComposeMethodSignature(methodSymbol),
          DispatchKind: ComposeMethodDispatchKind(methodSymbol),
          TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
          FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: methodSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        _methodNodes[key] = methodNode;
        return methodNode;
    }

    private RoslynCpgNode GetOrCreateMethodParameterNode(IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, RoslynCpgGraph graph)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var key = $"methodparam:{SymbolId(methodSymbol)}:{parameterSymbol.Ordinal}";
        if (_methodParameterNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var parameterNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.MethodParameter,
          DisplayKind: nameof(RoslynCpgNodeKind.MethodParameter),
          Name: parameterSymbol.Name,
          FullName: $"{ComposeMethodFullName(methodSymbol)}#{parameterSymbol.Ordinal}:{parameterSymbol.Name}",
          Signature: ComposeTypeFullName(parameterSymbol.Type),
          TypeFullName: ComposeTypeFullName(parameterSymbol.Type),
          FilePath: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: parameterSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        _methodParameterNodes[key] = parameterNode;

        var parameterSymbolNode = GetOrCreateSymbolNode(parameterSymbol, graph);
        graph.AddEdge(parameterNode, parameterSymbolNode, RoslynCpgEdgeKind.Ref);
        AddEvalTypeEdge(parameterNode, parameterSymbol.Type, graph);
        return parameterNode;
    }

    private RoslynCpgNode GetOrCreateMethodReturnNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var key = $"methodreturn:{SymbolId(methodSymbol)}";
        if (_methodReturnNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var returnNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.MethodReturn,
          DisplayKind: nameof(RoslynCpgNodeKind.MethodReturn),
          Name: $"{ComposeMethodName(methodSymbol)}:return",
          FullName: $"{ComposeMethodFullName(methodSymbol)}:return",
          Signature: ComposeTypeFullName(methodSymbol.ReturnType),
          TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
          FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: methodSymbol.Name));
        _methodReturnNodes[key] = returnNode;
        return returnNode;
    }

    private RoslynCpgNode GetOrCreateMethodEntryNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var key = $"methodentry:{SymbolId(methodSymbol)}";
        if (_methodEntryNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var entryNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.MethodEntry,
          DisplayKind: nameof(RoslynCpgNodeKind.MethodEntry),
          Name: $"{ComposeMethodName(methodSymbol)}:entry",
          FullName: $"{ComposeMethodFullName(methodSymbol)}:entry",
          Signature: ComposeMethodSignature(methodSymbol),
          TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
          FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
          Text: methodSymbol.Name));
        _methodEntryNodes[key] = entryNode;
        return entryNode;
    }

    private RoslynCpgNode GetOrCreateMethodExitNode(IMethodSymbol methodSymbol, RoslynCpgGraph graph)
    {
        methodSymbol = CanonicalMethodSymbol(methodSymbol);
        var key = $"methodexit:{SymbolId(methodSymbol)}";
        if (_methodExitNodes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var exitNode = graph.AddNode(new RoslynCpgNode(
          Id: key,
          Kind: RoslynCpgNodeKind.MethodExit,
          DisplayKind: nameof(RoslynCpgNodeKind.MethodExit),
          Name: $"{ComposeMethodName(methodSymbol)}:exit",
          FullName: $"{ComposeMethodFullName(methodSymbol)}:exit",
          Signature: ComposeMethodSignature(methodSymbol),
          TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
          FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
          SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
          Text: methodSymbol.Name));
        _methodExitNodes[key] = exitNode;
        return exitNode;
    }

    private void AddMethodLevelControlFlow(RoslynCpgGraph graph)
    {
        var operations = _operationNodes.Keys.ToList();
        foreach (var methodBlock in operations.OfType<IBlockOperation>())
        {
            if (!IsMethodRootBlock(methodBlock))
            {
                continue;
            }

            if (_operationOwningMethods.TryGetValue(methodBlock, out var methodSymbol))
            {
                var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
                var parameterNodes = methodSymbol.Parameters
                  .Select(parameter => GetOrCreateMethodParameterNode(methodSymbol, parameter, graph))
                  .ToList();
                var returnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
                var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
                var firstOperation = FirstExecutableOperation(methodBlock);

                if (parameterNodes.Count > 0)
                {
                    graph.AddEdge(entryNode, parameterNodes[0], RoslynCpgEdgeKind.CfgNext);
                    for (var index = 0; index < parameterNodes.Count - 1; index += 1)
                    {
                        graph.AddEdge(parameterNodes[index], parameterNodes[index + 1], RoslynCpgEdgeKind.CfgNext);
                    }

                    if (firstOperation is not null)
                    {
                        graph.AddEdge(parameterNodes[^1], GetOrCreateOperationNode(firstOperation, graph), RoslynCpgEdgeKind.CfgNext);
                    }
                    else
                    {
                        graph.AddEdge(parameterNodes[^1], returnNode, RoslynCpgEdgeKind.CfgNext);
                    }
                }
                else if (firstOperation is not null)
                {
                    graph.AddEdge(entryNode, GetOrCreateOperationNode(firstOperation, graph), RoslynCpgEdgeKind.CfgNext);
                }
                else
                {
                    graph.AddEdge(entryNode, returnNode, RoslynCpgEdgeKind.CfgNext);
                }

                foreach (var returnOperation in methodBlock.DescendantsAndSelf().OfType<IReturnOperation>())
                {
                    graph.AddEdge(GetOrCreateOperationNode(returnOperation, graph), returnNode, RoslynCpgEdgeKind.CfgNext);
                }

                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null && !ContainsExplicitReturn(methodBlock) && !StopsSequentialFlow(terminalOperation))
                {
                    graph.AddEdge(GetOrCreateOperationNode(terminalOperation, graph), returnNode, RoslynCpgEdgeKind.CfgNext);
                }

                graph.AddEdge(returnNode, exitNode, RoslynCpgEdgeKind.CfgNext);
            }

            AddSequentialEdges(methodBlock.Operations, graph);
            foreach (var operation in methodBlock.Descendants())
            {
                switch (operation)
                {
                    case IConditionalOperation conditional:
                        AddConditionalEdges(conditional, graph);
                        break;
                    case IWhileLoopOperation whileLoop:
                        AddWhileLoopEdges(whileLoop, graph);
                        break;
                    case IForLoopOperation forLoop:
                        AddForLoopEdges(forLoop, graph);
                        break;
                    case ISwitchOperation switchOperation:
                        AddSwitchEdges(switchOperation, graph);
                        break;
                    case ITryOperation tryOperation:
                        AddTryEdges(tryOperation, graph);
                        break;
                    case IReturnOperation:
                        break;
                }
            }

            AddLoopJumpEdges(methodBlock, graph);
        }
    }

    private void AddReachingDefinitionDataFlow(RoslynCpgGraph graph)
    {
        foreach (var methodBlock in _operationNodes.Keys.OfType<IBlockOperation>().Where(IsMethodRootBlock))
        {
            var orderedOperations = methodBlock.DescendantsAndSelf().ToList();
            AddCfgSensitiveSymbolDataFlow(methodBlock, orderedOperations, graph);
            foreach (var operation in orderedOperations)
            {
                var operationNode = GetOrCreateOperationNode(operation, graph);

                foreach (var sourceOperation in ValueSourceOperations(operation))
                {
                    if (!ReferenceEquals(sourceOperation, operation))
                    {
                        graph.AddEdge(GetOrCreateOperationNode(sourceOperation, graph), operationNode, RoslynCpgEdgeKind.DataFlow);
                    }
                }

                if (operation is IReturnOperation returnOperation &&
                    returnOperation.ReturnedValue is not null &&
                    _operationOwningMethods.TryGetValue(operation, out var owningMethod))
                {
                    var exitNode = GetOrCreateMethodExitNode(owningMethod, graph);
                    var returnNode = GetOrCreateMethodReturnNode(owningMethod, graph);
                    graph.AddEdge(GetOrCreateOperationNode(returnOperation.ReturnedValue, graph), returnNode, RoslynCpgEdgeKind.DataFlow);
                    graph.AddEdge(returnNode, exitNode, RoslynCpgEdgeKind.DataFlow);
                    graph.AddEdge(operationNode, exitNode, RoslynCpgEdgeKind.DataFlow);
                }
            }

            if (_operationOwningMethods.TryGetValue(methodBlock, out var flowMethodSymbol))
            {
                var returnNode = GetOrCreateMethodReturnNode(flowMethodSymbol, graph);
                var terminalOperation = methodBlock.Operations.LastOrDefault();
                if (terminalOperation is not null &&
                    !ContainsExplicitReturn(methodBlock) &&
                    !StopsSequentialFlow(terminalOperation))
                {
                    graph.AddEdge(GetOrCreateOperationNode(terminalOperation, graph), returnNode, RoslynCpgEdgeKind.DataFlow);
                }
            }
        }

        AddCallArgumentAndReturnDataFlow(graph);
    }

    private void AddCfgSensitiveSymbolDataFlow(
      IBlockOperation methodBlock,
      List<IOperation> orderedOperations,
      RoslynCpgGraph graph)
    {
        var flowNodes = new List<RoslynCpgNode>();
        var definitionFactsByNodeId = new Dictionary<string, DefinitionFact>(StringComparer.Ordinal);
        var usedFactsByNodeId = new Dictionary<string, List<DefinitionFact>>(StringComparer.Ordinal);

        if (_operationOwningMethods.TryGetValue(methodBlock, out var methodSymbol))
        {
            foreach (var parameter in methodSymbol.Parameters)
            {
                var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
                flowNodes.Add(parameterNode);
                definitionFactsByNodeId[parameterNode.Id] = DefinitionFactForParameter(parameter);
            }
        }

        foreach (var operation in orderedOperations)
        {
            var operationNode = GetOrCreateOperationNode(operation, graph);
            flowNodes.Add(operationNode);
            usedFactsByNodeId[operationNode.Id] = UsedFacts(operation).ToList();

            var definedFact = DefinedFact(operation);
            if (definedFact is not null)
            {
                definitionFactsByNodeId[operationNode.Id] = definedFact;
            }
        }

        var flowNodeIds = flowNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var nodesById = flowNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var predecessors = BuildFlowNeighbors(graph, flowNodeIds, incoming: true);
        var successors = BuildFlowNeighbors(graph, flowNodeIds, incoming: false);
        var inSets = flowNodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var outSets = flowNodes.ToDictionary(node => node.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var worklist = new Queue<string>(flowNodes.Select(node => node.Id));
        var queued = new HashSet<string>(flowNodes.Select(node => node.Id), StringComparer.Ordinal);

        while (worklist.Count > 0)
        {
            var nodeId = worklist.Dequeue();
            queued.Remove(nodeId);

            var incomingDefinitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var predecessorId in predecessors[nodeId])
            {
                incomingDefinitions.UnionWith(outSets[predecessorId]);
            }

            inSets[nodeId] = incomingDefinitions;
            var updatedOut = ApplyDefinitionTransfer(nodeId, incomingDefinitions, definitionFactsByNodeId);
            if (updatedOut.SetEquals(outSets[nodeId]))
            {
                continue;
            }

            outSets[nodeId] = updatedOut;
            foreach (var successorId in successors[nodeId])
            {
                if (queued.Add(successorId))
                {
                    worklist.Enqueue(successorId);
                }
            }
        }

        foreach (var operation in orderedOperations)
        {
            var operationNode = GetOrCreateOperationNode(operation, graph);
            foreach (var usedFact in usedFactsByNodeId[operationNode.Id])
            {
                foreach (var reachingDefinitionId in inSets[operationNode.Id])
                {
                    if (!definitionFactsByNodeId.TryGetValue(reachingDefinitionId, out var reachingFact) ||
                        !FactsMatch(reachingFact, usedFact))
                    {
                        continue;
                    }

                    graph.AddEdge(nodesById[reachingDefinitionId], operationNode, RoslynCpgEdgeKind.DataFlow);
                }
            }
        }
    }

    private static Dictionary<string, List<string>> BuildFlowNeighbors(
      RoslynCpgGraph graph,
      HashSet<string> flowNodeIds,
      bool incoming)
    {
        var neighbors = flowNodeIds.ToDictionary(nodeId => nodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!IsControlFlowEdge(edge.Kind))
            {
                continue;
            }

            if (!flowNodeIds.Contains(edge.SourceId) || !flowNodeIds.Contains(edge.TargetId))
            {
                continue;
            }

            if (incoming)
            {
                neighbors[edge.TargetId].Add(edge.SourceId);
            }
            else
            {
                neighbors[edge.SourceId].Add(edge.TargetId);
            }
        }

        return neighbors;
    }

    private static HashSet<string> ApplyDefinitionTransfer(
      string nodeId,
      HashSet<string> incomingDefinitions,
      Dictionary<string, DefinitionFact> definitionFactsByNodeId)
    {
        var outgoingDefinitions = new HashSet<string>(incomingDefinitions, StringComparer.Ordinal);
        if (!definitionFactsByNodeId.TryGetValue(nodeId, out var definedFact))
        {
            return outgoingDefinitions;
        }

        outgoingDefinitions.RemoveWhere(definitionId =>
          definitionFactsByNodeId.TryGetValue(definitionId, out var priorFact) &&
          FactsConflict(priorFact, definedFact));
        outgoingDefinitions.Add(nodeId);
        return outgoingDefinitions;
    }

    private static bool IsControlFlowEdge(RoslynCpgEdgeKind edgeKind)
    {
        return edgeKind is RoslynCpgEdgeKind.CfgNext or RoslynCpgEdgeKind.CfgTrue or RoslynCpgEdgeKind.CfgFalse;
    }

    private static bool FactsMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        if (string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsContainerMatch(reachingFact, usedFact) || IsPartMatch(reachingFact, usedFact) || IsAliasMatch(reachingFact, usedFact))
        {
            return true;
        }

        if (string.IsNullOrEmpty(reachingFact.BaseKey) || string.IsNullOrEmpty(usedFact.BaseKey))
        {
            return false;
        }

        return string.Equals(reachingFact.BaseKey, usedFact.BaseKey, StringComparison.Ordinal) &&
               string.Equals(reachingFact.LocationKey, usedFact.LocationKey, StringComparison.Ordinal);
    }

    private static bool FactsConflict(DefinitionFact priorFact, DefinitionFact definedFact)
    {
        if (string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal))
        {
            return true;
        }

        var definedRootKey = FactRootKey(definedFact);
        if (!string.IsNullOrEmpty(definedRootKey) &&
            string.Equals(priorFact.BaseKey, definedRootKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (definedFact.Category is "field" or "property" &&
            string.Equals(priorFact.BaseKey, definedFact.BaseKey, StringComparison.Ordinal))
        {
            return IsAliasMatch(priorFact, definedFact) ||
                   string.Equals(priorFact.PathKey, definedFact.PathKey, StringComparison.Ordinal);
        }

        if (definedFact.Category is "call" && priorFact.Category == "call")
        {
            return string.Equals(priorFact.LocationKey, definedFact.LocationKey, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsContainerMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var reachingRootKey = FactRootKey(reachingFact);
        return !string.IsNullOrEmpty(usedFact.BaseKey) &&
               !string.IsNullOrEmpty(reachingRootKey) &&
               string.Equals(reachingRootKey, usedFact.BaseKey, StringComparison.Ordinal);
    }

    private static bool IsPartMatch(DefinitionFact reachingFact, DefinitionFact usedFact)
    {
        var usedRootKey = FactRootKey(usedFact);
        return !string.IsNullOrEmpty(reachingFact.BaseKey) &&
               !string.IsNullOrEmpty(usedRootKey) &&
               string.Equals(reachingFact.BaseKey, usedRootKey, StringComparison.Ordinal);
    }

    private static bool IsAliasMatch(DefinitionFact left, DefinitionFact right)
    {
        return !string.IsNullOrEmpty(left.BaseKey) &&
               !string.IsNullOrEmpty(right.BaseKey) &&
               string.Equals(left.BaseKey, right.BaseKey, StringComparison.Ordinal) &&
               string.Equals(left.PathKey, right.PathKey, StringComparison.Ordinal);
    }

    private static string? FactRootKey(DefinitionFact fact)
    {
        return fact.BaseKey ?? fact.LocationKey;
    }

    private void AddCallArgumentAndReturnDataFlow(RoslynCpgGraph graph)
    {
        foreach (var invocation in _operationNodes.Keys.OfType<IInvocationOperation>())
        {
            var targetMethod = invocation.TargetMethod;
            if (targetMethod is null)
            {
                continue;
            }

            var callSiteNode = FindCallSiteNode(invocation, graph);
            if (callSiteNode is null)
            {
                continue;
            }

            var candidateMethods = ResolveCallTargetCandidates(invocation, targetMethod).ToList();
            var preferredCandidates = PreferCallTargets(candidateMethods, targetMethod, invocation.Instance?.Type).ToList();
            var effectiveTargets = preferredCandidates.Count > 0 ? preferredCandidates : candidateMethods;
            if (effectiveTargets.Count == 0)
            {
                effectiveTargets.Add(targetMethod);
            }

            foreach (var candidateMethod in effectiveTargets.Where(IsInternalMethod).Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
            {
                AddArgumentToParameterFlows(invocation, candidateMethod, graph);

                var returnNode = GetOrCreateMethodReturnNode(candidateMethod, graph);
                graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }

        foreach (var propertyReference in _operationNodes.Keys.OfType<IPropertyReferenceOperation>())
        {
            AddPropertyAccessorSummaryDataFlow(propertyReference, graph);
        }
    }

    private void AddPropertyAccessorSummaryDataFlow(
      IPropertyReferenceOperation propertyReference,
      RoslynCpgGraph graph)
    {
        AddPropertyGetterSummaryDataFlow(propertyReference, graph);
        AddPropertySetterSummaryDataFlow(propertyReference, graph);
    }

    private void AddPropertyGetterSummaryDataFlow(
      IPropertyReferenceOperation propertyReference,
      RoslynCpgGraph graph)
    {
        var getterMethod = propertyReference.Property.GetMethod;
        if (getterMethod is null)
        {
            return;
        }

        var getter = CanonicalMethodSymbol(getterMethod);
        if (!IsInternalMethod(getter))
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, getter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, getter, callSiteNode, includesSetterValue: false, setterValue: null, graph);

        var returnNode = GetOrCreateMethodReturnNode(getter, graph);
        graph.AddEdge(returnNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(returnNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertySetterSummaryDataFlow(
      IPropertyReferenceOperation propertyReference,
      RoslynCpgGraph graph)
    {
        if (!IsPropertyWrite(propertyReference))
        {
            return;
        }

        var setterMethod = propertyReference.Property.SetMethod;
        if (setterMethod is null)
        {
            return;
        }

        var setter = CanonicalMethodSymbol(setterMethod);
        if (!IsInternalMethod(setter))
        {
            return;
        }

        if (propertyReference.Parent is not ISimpleAssignmentOperation assignment)
        {
            return;
        }

        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        var callSiteNode = FindPropertyAccessorCallSiteNode(propertyReference, setter, graph);
        AddPropertyAccessorParameterFlows(propertyReference, setter, callSiteNode, includesSetterValue: true, assignment.Value, graph);

        graph.AddEdge(GetOrCreateOperationNode(assignment.Value, graph), propertyNode, RoslynCpgEdgeKind.DataFlow);
        if (callSiteNode is not null)
        {
            graph.AddEdge(callSiteNode, propertyNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private void AddPropertyAccessorParameterFlows(
      IPropertyReferenceOperation propertyReference,
      IMethodSymbol accessorMethod,
      RoslynCpgNode? callSiteNode,
      bool includesSetterValue,
      IOperation? setterValue,
      RoslynCpgGraph graph)
    {
        var parameterIndex = 0;
        if (propertyReference.Instance is not null && accessorMethod.Parameters.Length > parameterIndex)
        {
            var receiverNode = GetOrCreateOperationNode(propertyReference.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        foreach (var argument in propertyReference.Arguments)
        {
            if (parameterIndex >= accessorMethod.Parameters.Length || argument.Value is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argument.Value, graph);
            var argumentParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, argumentParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex += 1;
        }

        if (includesSetterValue &&
            setterValue is not null &&
            parameterIndex < accessorMethod.Parameters.Length)
        {
            var valueNode = GetOrCreateOperationNode(setterValue, graph);
            var setterValueParameterNode = GetOrCreateMethodParameterNode(accessorMethod, accessorMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(valueNode, setterValueParameterNode, RoslynCpgEdgeKind.DataFlow);
            if (callSiteNode is not null)
            {
                graph.AddEdge(valueNode, callSiteNode, RoslynCpgEdgeKind.DataFlow);
            }
        }
    }

    private void AddArgumentToParameterFlows(
      IInvocationOperation invocation,
      IMethodSymbol candidateMethod,
      RoslynCpgGraph graph)
    {
        var parameterIndex = 0;

        if (candidateMethod.IsExtensionMethod && invocation.Instance is not null && candidateMethod.Parameters.Length > 0)
        {
            var receiverNode = GetOrCreateOperationNode(invocation.Instance, graph);
            var receiverParameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[0], graph);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(receiverNode, receiverParameterNode, RoslynCpgEdgeKind.DataFlow);
            parameterIndex = 1;
        }

        for (var argumentIndex = 0; argumentIndex < invocation.Arguments.Length && parameterIndex < candidateMethod.Parameters.Length; argumentIndex += 1, parameterIndex += 1)
        {
            var argumentValue = invocation.Arguments[argumentIndex].Value;
            if (argumentValue is null)
            {
                continue;
            }

            var argumentNode = GetOrCreateOperationNode(argumentValue, graph);
            var parameterNode = GetOrCreateMethodParameterNode(candidateMethod, candidateMethod.Parameters[parameterIndex], graph);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.ParameterLink);
            graph.AddEdge(argumentNode, parameterNode, RoslynCpgEdgeKind.DataFlow);
        }
    }

    private RoslynCpgNode? FindCallSiteNode(IInvocationOperation invocationOperation, RoslynCpgGraph graph)
    {
        var invocationNode = GetOrCreateOperationNode(invocationOperation, graph);
        return graph.Nodes.FirstOrDefault(node =>
          node.Kind == RoslynCpgNodeKind.CallSite &&
          node.FilePath == invocationNode.FilePath &&
          node.SpanStart == invocationNode.SpanStart &&
          node.SpanEnd == invocationNode.SpanEnd);
    }

    private RoslynCpgNode? FindPropertyAccessorCallSiteNode(
      IPropertyReferenceOperation propertyReference,
      IMethodSymbol accessorMethod,
      RoslynCpgGraph graph)
    {
        var propertyNode = GetOrCreateOperationNode(propertyReference, graph);
        return graph.Nodes.FirstOrDefault(node =>
          node.Kind == RoslynCpgNodeKind.CallSite &&
          node.Name == accessorMethod.Name &&
          node.FilePath == propertyNode.FilePath &&
          node.SpanStart == propertyNode.SpanStart &&
          node.SpanEnd == propertyNode.SpanEnd);
    }

    private void AddSequentialEdges(IEnumerable<IOperation> operations, RoslynCpgGraph graph)
    {
        var ordered = operations.ToList();
        for (var index = 0; index < ordered.Count - 1; index += 1)
        {
            if (StopsSequentialFlow(ordered[index]))
            {
                continue;
            }

            graph.AddEdge(GetOrCreateOperationNode(ordered[index], graph), GetOrCreateOperationNode(ordered[index + 1], graph), RoslynCpgEdgeKind.CfgNext);
        }

        foreach (var nestedBlock in ordered.OfType<IBlockOperation>())
        {
            AddSequentialEdges(nestedBlock.Operations, graph);
        }
    }

    private void AddConditionalEdges(IConditionalOperation conditional, RoslynCpgGraph graph)
    {
        var conditionNode = GetOrCreateOperationNode(conditional.Condition, graph);
        var trueNode = conditional.WhenTrue is null ? null : GetOrCreateOperationNode(conditional.WhenTrue, graph);
        var falseNode = conditional.WhenFalse is null ? null : GetOrCreateOperationNode(conditional.WhenFalse, graph);
        if (trueNode is not null)
        {
            graph.AddEdge(conditionNode, trueNode, RoslynCpgEdgeKind.CfgTrue);
        }

        if (falseNode is not null)
        {
            graph.AddEdge(conditionNode, falseNode, RoslynCpgEdgeKind.CfgFalse);
        }
    }

    private void AddWhileLoopEdges(IWhileLoopOperation whileLoop, RoslynCpgGraph graph)
    {
        if (whileLoop.Condition is null)
        {
            return;
        }

        var conditionNode = GetOrCreateOperationNode(whileLoop.Condition, graph);
        var bodyNode = whileLoop.Body is null ? null : GetOrCreateOperationNode(whileLoop.Body, graph);
        if (bodyNode is null)
        {
            return;
        }

        graph.AddEdge(conditionNode, bodyNode, RoslynCpgEdgeKind.CfgTrue);
        graph.AddEdge(bodyNode, conditionNode, RoslynCpgEdgeKind.CfgNext);

        var exitTarget = NextSiblingOperation(whileLoop);
        if (exitTarget is not null)
        {
            graph.AddEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgFalse);
        }
    }

    private void AddForLoopEdges(IForLoopOperation forLoop, RoslynCpgGraph graph)
    {
        var conditionOperation = forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null);
        var bodyNode = forLoop.Body is null ? null : GetOrCreateOperationNode(forLoop.Body, graph);
        if (conditionOperation is not null && bodyNode is not null)
        {
            var conditionNode = GetOrCreateOperationNode(conditionOperation, graph);
            graph.AddEdge(conditionNode, bodyNode, RoslynCpgEdgeKind.CfgTrue);
            graph.AddEdge(bodyNode, conditionNode, RoslynCpgEdgeKind.CfgNext);

            var exitTarget = NextSiblingOperation(forLoop);
            if (exitTarget is not null)
            {
                graph.AddEdge(conditionNode, GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgFalse);
            }
        }
    }

    private void AddSwitchEdges(ISwitchOperation switchOperation, RoslynCpgGraph graph)
    {
        var switchValueNode = GetOrCreateOperationNode(switchOperation.Value, graph);
        var exitTarget = NextSiblingOperation(switchOperation);
        var hasDefaultCase = false;
        var caseEntries = switchOperation.Cases
          .Select(@case => new
          {
              Case = @case,
              Entry = FirstExecutableOperation(@case.Body),
              Terminal = LastExecutableOperation(@case.Body),
          })
          .ToList();

        foreach (var item in caseEntries)
        {
            var @case = item.Case;
            var caseBodyEntry = ResolveSwitchCaseEntry(caseEntries, @case, exitTarget);
            if (caseBodyEntry is null)
            {
                continue;
            }

            graph.AddEdge(switchValueNode, GetOrCreateOperationNode(caseBodyEntry, graph), RoslynCpgEdgeKind.CfgTrue);

            if (@case.Clauses.Any(clause => clause.CaseKind == CaseKind.Default))
            {
                hasDefaultCase = true;
            }
        }

        if (!hasDefaultCase && exitTarget is not null)
        {
            graph.AddEdge(switchValueNode, GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgFalse);
        }

        for (var index = 0; index < caseEntries.Count; index += 1)
        {
            var @case = caseEntries[index].Case;
            var caseTerminal = caseEntries[index].Terminal;
            var nextCaseEntry = index + 1 < caseEntries.Count
              ? ResolveSwitchCaseEntry(caseEntries, caseEntries[index + 1].Case, exitTarget)
              : exitTarget;

            if (caseTerminal is not null &&
                caseTerminal is not IBranchOperation { BranchKind: BranchKind.Break } &&
                !StopsSequentialFlow(caseTerminal))
            {
                if (nextCaseEntry is not null)
                {
                    graph.AddEdge(
                      GetOrCreateOperationNode(caseTerminal, graph),
                      GetOrCreateOperationNode(nextCaseEntry, graph),
                      RoslynCpgEdgeKind.CfgNext);
                }
                else if (exitTarget is not null)
                {
                    graph.AddEdge(
                      GetOrCreateOperationNode(caseTerminal, graph),
                      GetOrCreateOperationNode(exitTarget, graph),
                      RoslynCpgEdgeKind.CfgNext);
                }
            }

            foreach (var operation in DescendantsAndSelf(@case.Body))
            {
                if (operation is IBranchOperation { BranchKind: BranchKind.Break })
                {
                    if (exitTarget is not null)
                    {
                        graph.AddEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(exitTarget, graph),
                          RoslynCpgEdgeKind.CfgNext);
                    }
                }
            }
        }
    }

    private static IOperation? ResolveSwitchCaseEntry(
      IEnumerable<dynamic> caseEntries,
      ISwitchCaseOperation @case,
      IOperation? exitTarget)
    {
        var entries = caseEntries.ToList();
        var startIndex = entries.FindIndex(item => ReferenceEquals(item.Case, @case));
        if (startIndex < 0)
        {
            return exitTarget;
        }

        for (var index = startIndex; index < entries.Count; index += 1)
        {
            if (entries[index].Entry is IOperation entry)
            {
                return entry;
            }
        }

        return exitTarget;
    }

    private void AddTryEdges(ITryOperation tryOperation, RoslynCpgGraph graph)
    {
        var tryBodyEntry = FirstExecutableOperation(tryOperation.Body);
        var finallyEntry = tryOperation.Finally is null ? null : FirstExecutableOperation(tryOperation.Finally);
        var exitTarget = NextSiblingOperation(tryOperation);
        var tryTerminal = LastExecutableOperation(tryOperation.Body);

        if (tryBodyEntry is not null)
        {
            graph.AddEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(tryBodyEntry, graph), RoslynCpgEdgeKind.CfgNext);
        }
        else if (finallyEntry is not null)
        {
            graph.AddEdge(GetOrCreateOperationNode(tryOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), RoslynCpgEdgeKind.CfgNext);
        }

        foreach (var catchClause in tryOperation.Catches)
        {
            var catchEntry = FirstExecutableOperation(catchClause.Handler);
            var catchTerminal = LastExecutableOperation(catchClause.Handler);
            if (tryBodyEntry is not null && catchEntry is not null)
            {
                graph.AddEdge(GetOrCreateOperationNode(tryBodyEntry, graph), GetOrCreateOperationNode(catchEntry, graph), RoslynCpgEdgeKind.CfgFalse);
            }

            if (catchEntry is not null && finallyEntry is not null)
            {
                graph.AddEdge(GetOrCreateOperationNode(catchEntry, graph), GetOrCreateOperationNode(finallyEntry, graph), RoslynCpgEdgeKind.CfgNext);
            }

            if (catchTerminal is not null &&
                !ReferenceEquals(catchTerminal, catchEntry) &&
                !StopsSequentialFlow(catchTerminal))
            {
                if (finallyEntry is not null)
                {
                    graph.AddEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), RoslynCpgEdgeKind.CfgNext);
                }
                else if (exitTarget is not null)
                {
                    graph.AddEdge(GetOrCreateOperationNode(catchTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgNext);
                }
            }
        }

        if (tryTerminal is not null && !StopsSequentialFlow(tryTerminal))
        {
            if (finallyEntry is not null)
            {
                graph.AddEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(finallyEntry, graph), RoslynCpgEdgeKind.CfgNext);
            }
            else if (exitTarget is not null)
            {
                graph.AddEdge(GetOrCreateOperationNode(tryTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgNext);
            }
        }

        var finallyTerminal = tryOperation.Finally is null ? null : LastExecutableOperation(tryOperation.Finally);
        if (finallyTerminal is not null && exitTarget is not null && !StopsSequentialFlow(finallyTerminal))
        {
            graph.AddEdge(GetOrCreateOperationNode(finallyTerminal, graph), GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgNext);
        }
        else if (finallyEntry is not null && exitTarget is not null)
        {
            graph.AddEdge(GetOrCreateOperationNode(finallyEntry, graph), GetOrCreateOperationNode(exitTarget, graph), RoslynCpgEdgeKind.CfgNext);
        }

        if (finallyEntry is not null)
        {
            foreach (var returnOperation in tryOperation.Descendants().OfType<IReturnOperation>())
            {
                if (tryOperation.Finally is not null && IsWithinOperation(returnOperation, tryOperation.Finally))
                {
                    continue;
                }

                graph.AddEdge(GetOrCreateOperationNode(returnOperation, graph), GetOrCreateOperationNode(finallyEntry, graph), RoslynCpgEdgeKind.CfgNext);
            }
        }
    }

    private void AddLoopJumpEdges(IBlockOperation methodBlock, RoslynCpgGraph graph)
    {
        foreach (var loop in methodBlock.Descendants().OfType<ILoopOperation>())
        {
            var targets = LoopTargets(loop);
            if (targets.ContinueTarget is null && targets.BreakTarget is null)
            {
                continue;
            }

            foreach (var operation in loop.Body?.DescendantsAndSelf() ?? Enumerable.Empty<IOperation>())
            {
                if (operation.Kind == OperationKind.Branch)
                {
                    var branch = (IBranchOperation)operation;
                    if (branch.BranchKind == BranchKind.Continue && targets.ContinueTarget is not null)
                    {
                        graph.AddEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(targets.ContinueTarget, graph),
                          RoslynCpgEdgeKind.CfgNext);
                    }

                    if (branch.BranchKind == BranchKind.Break && targets.BreakTarget is not null)
                    {
                        graph.AddEdge(
                          GetOrCreateOperationNode(operation, graph),
                          GetOrCreateOperationNode(targets.BreakTarget, graph),
                          RoslynCpgEdgeKind.CfgNext);
                    }
                }
            }
        }
    }

    private static LoopControlTargets LoopTargets(ILoopOperation loop)
    {
        return loop switch
        {
            IWhileLoopOperation whileLoop => new LoopControlTargets(whileLoop.Condition, NextSiblingOperation(whileLoop)),
            IForLoopOperation forLoop => new LoopControlTargets(
              forLoop.Condition ?? (forLoop.Before.Length > 0 ? forLoop.Before.LastOrDefault() : null),
              NextSiblingOperation(forLoop)),
            _ => new LoopControlTargets(null, NextSiblingOperation(loop)),
        };
    }

    private static IOperation? NextSiblingOperation(IOperation operation)
    {
        if (operation.Parent is not IBlockOperation parentBlock)
        {
            return null;
        }

        var siblings = parentBlock.Operations;
        for (var index = 0; index < siblings.Length - 1; index += 1)
        {
            if (ReferenceEquals(siblings[index], operation))
            {
                return siblings[index + 1];
            }
        }

        return null;
    }

    private static IOperation? FirstExecutableOperation(IOperation operation)
    {
        if (operation is IBlockOperation blockOperation)
        {
            return blockOperation.Operations.FirstOrDefault();
        }

        return operation;
    }

    private static IOperation? FirstExecutableOperation(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            var executable = FirstExecutableOperation(operation);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    private static IOperation? LastExecutableOperation(IOperation operation)
    {
        return operation switch
        {
            IBlockOperation blockOperation => LastExecutableOperation(blockOperation.Operations),
            _ => operation,
        };
    }

    private static IOperation? LastExecutableOperation(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations.Reverse())
        {
            var executable = LastExecutableOperation(operation);
            if (executable is not null)
            {
                return executable;
            }
        }

        return null;
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IEnumerable<IOperation> operations)
    {
        foreach (var operation in operations)
        {
            foreach (var descendant in operation.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    private bool IsMethodRootBlock(IBlockOperation blockOperation)
    {
        return blockOperation.Parent is IMethodBodyOperation or IConstructorBodyOperation;
    }

    private static ISymbol? DefinedSymbol(IOperation operation)
    {
        return operation switch
        {
            IVariableDeclaratorOperation declarator => declarator.Symbol,
            ISimpleAssignmentOperation assignment => assignment.Target switch
            {
                ILocalReferenceOperation localReference => localReference.Local,
                IParameterReferenceOperation parameterReference => parameterReference.Parameter,
                IFieldReferenceOperation fieldReference => fieldReference.Field,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null,
            },
            _ => null,
        };
    }

    private static DefinitionFact? DefinedFact(IOperation operation)
    {
        return operation switch
        {
            IVariableDeclaratorOperation declarator => DefinitionFactForSymbol(declarator.Symbol),
            ISimpleAssignmentOperation assignment => DefinitionFactForAssignmentTarget(assignment.Target),
            IInvocationOperation invocation => DefinitionFactForInvocation(invocation),
            IPropertyReferenceOperation propertyReference when IsPropertyRead(propertyReference) =>
              DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => null,
        };
    }

    private static IEnumerable<IOperation> ValueSourceOperations(IOperation operation)
    {
        switch (operation)
        {
            case IVariableDeclaratorOperation declarator when declarator.Initializer?.Value is { } initializerValue:
                yield return initializerValue;
                break;
            case ISimpleAssignmentOperation assignment:
                yield return assignment.Value;
                break;
            case IInvocationOperation invocation:
                if (invocation.Instance is not null)
                {
                    yield return invocation.Instance;
                }

                foreach (var argument in invocation.Arguments)
                {
                    if (argument.Value is not null)
                    {
                        yield return argument.Value;
                    }
                }

                break;
            case IReturnOperation returnOperation when returnOperation.ReturnedValue is not null:
                yield return returnOperation.ReturnedValue;
                break;
        }
    }

    private static IEnumerable<DefinitionFact> UsedFacts(IOperation operation)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            switch (descendant)
            {
                case ILocalReferenceOperation localReference:
                    yield return DefinitionFactForSymbol(localReference.Local);
                    break;
                case IParameterReferenceOperation parameterReference:
                    yield return DefinitionFactForParameter(parameterReference.Parameter);
                    break;
                case IFieldReferenceOperation fieldReference:
                    yield return DefinitionFactForField(fieldReference.Field, fieldReference.Instance);
                    break;
                case IPropertyReferenceOperation propertyReference:
                    yield return DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance);
                    break;
            }
        }
    }

    private static DefinitionFact DefinitionFactForAssignmentTarget(IOperation target)
    {
        return target switch
        {
            ILocalReferenceOperation localReference => DefinitionFactForSymbol(localReference.Local),
            IParameterReferenceOperation parameterReference => DefinitionFactForParameter(parameterReference.Parameter),
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance),
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance),
            _ => new DefinitionFact(target.Kind.ToString(), null, "unknown"),
        };
    }

    private static DefinitionFact DefinitionFactForInvocation(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var locationKey = targetMethod is null
          ? $"call:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}"
          : $"call:{ComposeMethodFullName(targetMethod)}";
        return new DefinitionFact(locationKey, ReceiverKey(invocation.Instance), "call", ComposeInvocationPathKey(invocation));
    }

    private static DefinitionFact DefinitionFactForSymbol(ISymbol symbol)
    {
        return new DefinitionFact(SymbolId(symbol), null, symbol.Kind.ToString(), SymbolId(symbol));
    }

    private static DefinitionFact DefinitionFactForParameter(IParameterSymbol parameter)
    {
        return new DefinitionFact(SymbolId(parameter), null, "parameter", SymbolId(parameter));
    }

    private static DefinitionFact DefinitionFactForField(IFieldSymbol field, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var locationKey = baseKey is null
          ? $"field:{SymbolId(field)}"
          : $"field:{baseKey}.{field.Name}";
        return new DefinitionFact(locationKey, baseKey, "field", field.Name);
    }

    private static DefinitionFact DefinitionFactForProperty(IPropertySymbol property, IOperation? instance)
    {
        var baseKey = ReceiverKey(instance);
        var propertyKey = property.Parameters.Length == 0
          ? property.Name
          : $"{property.Name}:{ComposePropertySignature(property)}";
        var locationKey = baseKey is null
          ? $"property:{SymbolId(property)}"
          : $"property:{baseKey}.{propertyKey}";
        return new DefinitionFact(locationKey, baseKey, "property", propertyKey);
    }

    private static string ComposeInvocationPathKey(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        if (targetMethod is not null)
        {
            return ComposeMethodLookupKey(targetMethod);
        }

        return $"invoke:{invocation.Syntax.SpanStart}:{invocation.Syntax.Span.End}";
    }

    private static bool IsPropertyRead(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is not ISimpleAssignmentOperation assignment ||
               !ReferenceEquals(assignment.Target, propertyReference);
    }

    private static bool IsPropertyWrite(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent is ISimpleAssignmentOperation assignment &&
               ReferenceEquals(assignment.Target, propertyReference);
    }

    private static IMethodSymbol? ResolvePropertyAccessorMethod(IPropertyReferenceOperation propertyReference)
    {
        if (IsPropertyWrite(propertyReference))
        {
            return propertyReference.Property.SetMethod;
        }

        if (IsPropertyRead(propertyReference))
        {
            return propertyReference.Property.GetMethod;
        }

        return null;
    }

    private static string? ReceiverKey(IOperation? instance)
    {
        if (instance is null)
        {
            return null;
        }

        return instance switch
        {
            IInstanceReferenceOperation instanceReference => ComposeTypeFullName(instanceReference.Type),
            ILocalReferenceOperation localReference => $"local:{SymbolId(localReference.Local)}",
            IParameterReferenceOperation parameterReference => $"param:{SymbolId(parameterReference.Parameter)}",
            IFieldReferenceOperation fieldReference => DefinitionFactForField(fieldReference.Field, fieldReference.Instance).LocationKey,
            IPropertyReferenceOperation propertyReference => DefinitionFactForProperty(propertyReference.Property, propertyReference.Instance).LocationKey,
            _ => $"op:{instance.Kind}:{instance.Syntax.SpanStart}:{instance.Syntax.Span.End}",
        };
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

    private static bool StopsSequentialFlow(IOperation operation)
    {
        return operation is IReturnOperation ||
               operation is IBranchOperation { BranchKind: BranchKind.Break or BranchKind.Continue };
    }

    private static bool ContainsExplicitReturn(IBlockOperation blockOperation)
    {
        return blockOperation.Descendants().OfType<IReturnOperation>().Any();
    }

    private static bool IsWithinOperation(IOperation candidate, IOperation container)
    {
        for (var current = candidate.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
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

    private void AddMemberAccess(
      IOperation operation,
      RoslynCpgNode operationNode,
      ISymbol memberSymbol,
      ITypeSymbol? instanceType,
      RoslynCpgGraph graph)
    {
        _memberAccessSequence += 1;
        var memberAccessNode = graph.AddNode(new RoslynCpgNode(
          Id: $"memberaccess:{_memberAccessSequence}:{operation.Syntax.SpanStart}:{operation.Syntax.Span.End}",
          Kind: RoslynCpgNodeKind.MemberAccess,
          DisplayKind: nameof(RoslynCpgNodeKind.MemberAccess),
          Name: memberSymbol.Name,
          FullName: ComposeMemberAccessFullName(memberSymbol, instanceType),
          Signature: ComposeSignature(memberSymbol),
          TypeFullName: ComposeTypeFullName(SymbolTypeOf(memberSymbol)),
          FilePath: operationNode.FilePath,
          SpanStart: operationNode.SpanStart,
          SpanEnd: operationNode.SpanEnd,
          Text: operationNode.Text));
        graph.AddEdge(operationNode, memberAccessNode, RoslynCpgEdgeKind.AccessesMember);

        var memberNode = GetOrCreateSymbolNode(memberSymbol, graph);
        graph.AddEdge(memberAccessNode, memberNode, RoslynCpgEdgeKind.Ref);
        AddEvalTypeEdge(memberAccessNode, SymbolTypeOf(memberSymbol), graph);
    }

    private void AddCallSite(IInvocationOperation invocationOperation, RoslynCpgNode operationNode, RoslynCpgGraph graph)
    {
        _callSiteSequence += 1;
        var targetMethod = invocationOperation.TargetMethod;
        var resolvedCandidates = targetMethod is null
          ? null
          : ResolvePreferredCallTargets(ResolveCallTargetCandidates(invocationOperation, targetMethod), targetMethod, invocationOperation.Instance?.Type);
        var callSiteNode = graph.AddNode(new RoslynCpgNode(
          Id: $"callsite:{_callSiteSequence}:{invocationOperation.Syntax.SpanStart}:{invocationOperation.Syntax.Span.End}",
          Kind: RoslynCpgNodeKind.CallSite,
          DisplayKind: nameof(RoslynCpgNodeKind.CallSite),
          Name: targetMethod?.Name ?? operationNode.Name,
          FullName: targetMethod is null ? operationNode.FullName : ComposeInvocationMethodFullName(targetMethod),
          Signature: targetMethod is null ? operationNode.Signature : ComposeInvocationSignature(targetMethod),
          DispatchKind: targetMethod is null
            ? null
            : ComposeResolvedDispatchKind(
              resolvedCandidates![0],
              targetMethod,
              invocationOperation.Instance?.Type,
              ComposeCallDispatchKind(resolvedCandidates[0], invocationOperation.Instance is not null)),
          TypeFullName: ComposeTypeFullName(invocationOperation.Type),
          FilePath: operationNode.FilePath,
          SpanStart: operationNode.SpanStart,
          SpanEnd: operationNode.SpanEnd,
          Text: operationNode.Text));
        graph.AddEdge(operationNode, callSiteNode, RoslynCpgEdgeKind.SyntaxChild);

        if (targetMethod is not null)
        {
            foreach (var candidateMethod in resolvedCandidates!)
            {
                var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
                graph.AddEdge(callSiteNode, methodNode, RoslynCpgEdgeKind.CallTargets);
            }

            AddEvalTypeEdge(callSiteNode, targetMethod.ReturnType, graph);
        }
    }

    private RoslynCpgNode? AddPropertyAccessorCallSite(
      IPropertyReferenceOperation propertyReference,
      RoslynCpgNode operationNode,
      RoslynCpgGraph graph)
    {
        var accessorMethod = ResolvePropertyAccessorMethod(propertyReference);
        if (accessorMethod is null)
        {
            return null;
        }

        _callSiteSequence += 1;
        var resolvedCandidates = ResolvePreferredCallTargets(
          ResolveAccessorTargetCandidates(accessorMethod, propertyReference.Instance?.Type),
          accessorMethod,
          propertyReference.Instance?.Type);
        var accessorEvalType = ResolvePropertyAccessorEvalType(propertyReference, accessorMethod);
        var callSiteNode = graph.AddNode(new RoslynCpgNode(
          Id: $"callsite:property:{_callSiteSequence}:{propertyReference.Syntax.SpanStart}:{propertyReference.Syntax.Span.End}",
          Kind: RoslynCpgNodeKind.CallSite,
          DisplayKind: nameof(RoslynCpgNodeKind.CallSite),
          Name: accessorMethod.Name,
          FullName: ComposeInvocationMethodFullName(accessorMethod),
          Signature: ComposeInvocationSignature(accessorMethod),
          DispatchKind: ComposeResolvedDispatchKind(
            resolvedCandidates[0],
            accessorMethod,
            propertyReference.Instance?.Type,
            ComposePropertyAccessorDispatchKind(resolvedCandidates[0], propertyReference.Instance is not null)),
          TypeFullName: ComposeTypeFullName(accessorEvalType),
          FilePath: operationNode.FilePath,
          SpanStart: operationNode.SpanStart,
          SpanEnd: operationNode.SpanEnd,
          Text: operationNode.Text));
        graph.AddEdge(operationNode, callSiteNode, RoslynCpgEdgeKind.SyntaxChild);

        foreach (var candidateMethod in resolvedCandidates)
        {
            var methodNode = GetOrCreateSymbolNode(candidateMethod, graph);
            graph.AddEdge(callSiteNode, methodNode, RoslynCpgEdgeKind.CallTargets);
        }

        AddEvalTypeEdge(callSiteNode, accessorEvalType, graph);
        return callSiteNode;
    }

    private static ITypeSymbol? ResolvePropertyAccessorEvalType(
      IPropertyReferenceOperation propertyReference,
      IMethodSymbol accessorMethod)
    {
        if (accessorMethod.ReturnType.SpecialType != SpecialType.System_Void)
        {
            return accessorMethod.ReturnType;
        }

        return propertyReference.Type ?? propertyReference.Property.Type;
    }

    private IEnumerable<IMethodSymbol> ResolveCallTargetCandidates(
      IInvocationOperation invocationOperation,
      IMethodSymbol targetMethod)
    {
        targetMethod = CanonicalMethodSymbol(targetMethod);
        var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
        {
            [SymbolId(targetMethod)] = targetMethod,
        };

        foreach (var exactMethod in ResolveExactMethodFallbackCandidates(targetMethod))
        {
            candidates[SymbolId(exactMethod)] = exactMethod;
        }

        var receiverType = invocationOperation.Instance?.Type;
        if (receiverType is null)
        {
            return candidates.Values;
        }

        var targetDeclaringType = targetMethod.ContainingType;
        if (targetDeclaringType is null)
        {
            return candidates.Values;
        }

        var baseDefinition = targetMethod.OriginalDefinition.OverriddenMethod ?? targetMethod.OriginalDefinition;

        foreach (var declaredType in _declaredTypes)
        {
            if (!InheritsFrom(declaredType, targetDeclaringType) ||
                !InheritsFrom(declaredType, receiverType))
            {
                continue;
            }

            foreach (var member in declaredType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
            {
                var canonicalMember = CanonicalMethodSymbol(member);
                if (!MethodSignatureMatches(member, targetMethod) ||
                    !CanDispatchToCandidate(canonicalMember, targetMethod, baseDefinition, declaredType, receiverType))
                {
                    continue;
                }

                candidates[SymbolId(canonicalMember)] = canonicalMember;
            }
        }

        foreach (var superType in EnumerateBaseTypes(receiverType))
        {
            foreach (var member in superType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
            {
                var canonicalMember = CanonicalMethodSymbol(member);
                if (!MethodSignatureMatches(member, targetMethod) ||
                    !CanDispatchToCandidate(canonicalMember, targetMethod, baseDefinition, superType, receiverType))
                {
                    continue;
                }

                candidates[SymbolId(canonicalMember)] = canonicalMember;
            }
        }

        foreach (var superMethod in ResolveSuperClassFallbackCandidates(targetMethod, receiverType))
        {
            candidates[SymbolId(superMethod)] = superMethod;
        }

        foreach (var extensionMethod in ResolveReceiverAwareExtensionCandidates(targetMethod, receiverType))
        {
            candidates[SymbolId(extensionMethod)] = extensionMethod;
        }

        return candidates.Values;
    }

    private IEnumerable<IMethodSymbol> ResolveExactMethodFallbackCandidates(IMethodSymbol targetMethod)
    {
        var fullName = ComposeMethodFullName(targetMethod);
        if (_methodSymbolsByFullName.TryGetValue(fullName, out var methodsByFullName))
        {
            foreach (var method in methodsByFullName)
            {
                yield return method;
            }
        }

        var nameAndSignatureKey = ComposeMethodLookupKey(targetMethod);
        if (_methodSymbolsByNameAndSignature.TryGetValue(nameAndSignatureKey, out var methodsByNameAndSignature))
        {
            foreach (var method in methodsByNameAndSignature)
            {
                yield return method;
            }
        }
    }

    private IEnumerable<IMethodSymbol> ResolveSuperClassFallbackCandidates(
      IMethodSymbol targetMethod,
      ITypeSymbol receiverType)
    {
        foreach (var superType in EnumerateBaseTypes(receiverType))
        {
            foreach (var member in superType.GetMembers(targetMethod.Name).OfType<IMethodSymbol>())
            {
                var canonicalMember = CanonicalMethodSymbol(member);
                if (!MethodSignatureMatches(canonicalMember, targetMethod))
                {
                    continue;
                }

                yield return canonicalMember;
            }
        }
    }

    private IEnumerable<IMethodSymbol> ResolveReceiverAwareExtensionCandidates(
      IMethodSymbol targetMethod,
      ITypeSymbol receiverType)
    {
        if (!targetMethod.IsExtensionMethod && targetMethod.ReducedFrom is null)
        {
            yield break;
        }

        foreach (var methodGroup in _methodSymbolsByNameAndSignature.Values)
        {
            foreach (var method in methodGroup)
            {
                if (!method.IsExtensionMethod)
                {
                    continue;
                }

                var canonicalMethod = CanonicalMethodSymbol(method);
                if (!MethodSignatureMatches(canonicalMethod, targetMethod) ||
                    !CanDispatchToExtensionReceiver(canonicalMethod, receiverType))
                {
                    continue;
                }

                yield return canonicalMethod;
            }
        }
    }

    private IEnumerable<IMethodSymbol> ResolveAccessorTargetCandidates(
      IMethodSymbol accessorMethod,
      ITypeSymbol? receiverType)
    {
        accessorMethod = CanonicalMethodSymbol(accessorMethod);
        var candidates = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal)
        {
            [SymbolId(accessorMethod)] = accessorMethod,
        };

        foreach (var exactMethod in ResolveExactMethodFallbackCandidates(accessorMethod))
        {
            candidates[SymbolId(exactMethod)] = exactMethod;
        }

        if (receiverType is null)
        {
            return candidates.Values;
        }

        var targetDeclaringType = accessorMethod.ContainingType;
        if (targetDeclaringType is not null)
        {
            var baseDefinition = accessorMethod.OriginalDefinition.OverriddenMethod ?? accessorMethod.OriginalDefinition;
            foreach (var declaredType in _declaredTypes)
            {
                if (!InheritsFrom(declaredType, targetDeclaringType) ||
                    !InheritsFrom(declaredType, receiverType))
                {
                    continue;
                }

                foreach (var member in declaredType.GetMembers(accessorMethod.Name).OfType<IMethodSymbol>())
                {
                    var canonicalMember = CanonicalMethodSymbol(member);
                    if (!MethodSignatureMatches(member, accessorMethod) ||
                        !CanDispatchToCandidate(canonicalMember, accessorMethod, baseDefinition, declaredType, receiverType))
                    {
                        continue;
                    }

                    candidates[SymbolId(canonicalMember)] = canonicalMember;
                }
            }

            foreach (var superType in EnumerateBaseTypes(receiverType))
            {
                foreach (var member in superType.GetMembers(accessorMethod.Name).OfType<IMethodSymbol>())
                {
                    var canonicalMember = CanonicalMethodSymbol(member);
                    if (!MethodSignatureMatches(member, accessorMethod) ||
                        !CanDispatchToCandidate(canonicalMember, accessorMethod, baseDefinition, superType, receiverType))
                    {
                        continue;
                    }

                    candidates[SymbolId(canonicalMember)] = canonicalMember;
                }
            }
        }

        foreach (var superMethod in ResolveSuperClassFallbackCandidates(accessorMethod, receiverType))
        {
            candidates[SymbolId(superMethod)] = superMethod;
        }

        return candidates.Values;
    }

    private static IEnumerable<IMethodSymbol> PreferCallTargets(
      IEnumerable<IMethodSymbol> methods,
      IMethodSymbol fallbackTarget,
      ITypeSymbol? receiverType)
    {
        var materialized = methods.ToList();
        var exactInternalMethods = materialized
          .Where(method => IsInternalMethod(method) &&
                           string.Equals(ComposeMethodFullName(method), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
          .ToList();
        if (exactInternalMethods.Count > 0)
        {
            return RankCallTargets(exactInternalMethods, fallbackTarget, receiverType);
        }

        var internalMethods = materialized.Where(IsInternalMethod).ToList();
        if (internalMethods.Count > 0)
        {
            return RankCallTargets(internalMethods, fallbackTarget, receiverType);
        }

        var exactExternalMethods = materialized
          .Where(method => !IsInternalMethod(method) &&
                           string.Equals(ComposeMethodFullName(method), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
          .ToList();
        if (exactExternalMethods.Count > 0)
        {
            return RankCallTargets(exactExternalMethods, fallbackTarget, receiverType);
        }

        var externalMethods = materialized.Where(method => !IsInternalMethod(method)).ToList();
        if (externalMethods.Count > 0)
        {
            return RankCallTargets(externalMethods, fallbackTarget, receiverType);
        }

        return new[] { fallbackTarget };
    }

    private static List<IMethodSymbol> ResolvePreferredCallTargets(
      IEnumerable<IMethodSymbol> methods,
      IMethodSymbol fallbackTarget,
      ITypeSymbol? receiverType)
    {
        var materialized = methods.ToList();
        if (materialized.Count == 0)
        {
            materialized.Add(fallbackTarget);
        }

        var preferredTargets = PreferCallTargets(materialized, fallbackTarget, receiverType).ToList();
        return preferredTargets.Count > 0 ? preferredTargets : materialized;
    }

    private static IEnumerable<IMethodSymbol> RankCallTargets(
      IEnumerable<IMethodSymbol> methods,
      IMethodSymbol fallbackTarget,
      ITypeSymbol? receiverType)
    {
        return methods
          .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
          .OrderByDescending(method => CallTargetScore(method, fallbackTarget, receiverType))
          .ThenBy(method => ComposeMethodFullName(method), StringComparer.Ordinal)
          .ToList();
    }

    private static int CallTargetScore(
      IMethodSymbol candidate,
      IMethodSymbol fallbackTarget,
      ITypeSymbol? receiverType)
    {
        var score = 0;
        if (IsInternalMethod(candidate))
        {
            score += 1000;
        }

        if (string.Equals(ComposeMethodFullName(candidate), ComposeMethodFullName(fallbackTarget), StringComparison.Ordinal))
        {
            score += 500;
        }

        if (string.Equals(ComposeMethodLookupKey(candidate), ComposeMethodLookupKey(fallbackTarget), StringComparison.Ordinal))
        {
            score += 250;
        }

        if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, fallbackTarget.OriginalDefinition))
        {
            score += 200;
        }

        if (candidate.OverriddenMethod is not null &&
            SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, fallbackTarget.OriginalDefinition))
        {
            score += 150;
        }

        if (candidate.AssociatedSymbol is IPropertySymbol candidateProperty &&
            fallbackTarget.AssociatedSymbol is IPropertySymbol fallbackProperty)
        {
            if (string.Equals(ComposePropertySignature(candidateProperty), ComposePropertySignature(fallbackProperty), StringComparison.Ordinal) &&
                string.Equals(candidateProperty.Name, fallbackProperty.Name, StringComparison.Ordinal))
            {
                score += 180;
            }

            if (receiverType is INamedTypeSymbol propertyReceiverType && candidateProperty.ContainingType is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(candidateProperty.ContainingType, propertyReceiverType))
                {
                    score += 90;
                }
                else if (InheritsFrom(propertyReceiverType, candidateProperty.ContainingType))
                {
                    score += 60;
                }
            }
        }

        if (receiverType is INamedTypeSymbol namedReceiverType && candidate.ContainingType is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.ContainingType, namedReceiverType))
            {
                score += 120;
            }
            else if (InheritsFrom(namedReceiverType, candidate.ContainingType))
            {
                score += 80;
            }
        }

        if (candidate.IsExtensionMethod)
        {
            score += 20;
        }

        return score;
    }

    private void RegisterMethodSymbol(IMethodSymbol methodSymbol)
    {
        var canonicalMethod = CanonicalMethodSymbol(methodSymbol);
        RegisterMethodLookup(_methodSymbolsByFullName, ComposeMethodFullName(canonicalMethod), canonicalMethod);
        RegisterMethodLookup(_methodSymbolsByNameAndSignature, ComposeMethodLookupKey(canonicalMethod), canonicalMethod);
    }

    private static void RegisterMethodLookup(
      Dictionary<string, List<IMethodSymbol>> methodLookup,
      string key,
      IMethodSymbol methodSymbol)
    {
        if (!methodLookup.TryGetValue(key, out var methods))
        {
            methods = new List<IMethodSymbol>();
            methodLookup[key] = methods;
        }

        if (!methods.Any(existing => SymbolEqualityComparer.Default.Equals(existing, methodSymbol)))
        {
            methods.Add(methodSymbol);
        }
    }

    private IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return Enumerable.Empty<INamedTypeSymbol>();
        }

        var cacheKey = ComposeTypeFullName(namedType);
        if (_baseTypeCache.TryGetValue(cacheKey, out var cachedTypes))
        {
            return cachedTypes;
        }

        var collectedTypes = new List<INamedTypeSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interfaceType in namedType.AllInterfaces)
        {
            AddBaseType(interfaceType, collectedTypes, seen);
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            AddBaseType(current, collectedTypes, seen);
        }

        _baseTypeCache[cacheKey] = collectedTypes;
        return collectedTypes;
    }

    private static void AddBaseType(
      INamedTypeSymbol baseType,
      List<INamedTypeSymbol> collectedTypes,
      HashSet<string> seen)
    {
        var key = ComposeTypeFullName(baseType);
        if (seen.Add(key))
        {
            collectedTypes.Add(baseType);
        }
    }

    private static bool MethodSignatureMatches(IMethodSymbol candidate, IMethodSymbol targetMethod)
    {
        if (!string.Equals(ComposeMethodName(candidate), ComposeMethodName(targetMethod), StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
          ComposeMethodSignature(candidate),
          ComposeMethodSignature(targetMethod),
          StringComparison.Ordinal);
    }

    private static bool CanDispatchToCandidate(
      IMethodSymbol candidate,
      IMethodSymbol targetMethod,
      IMethodSymbol baseDefinition,
      INamedTypeSymbol candidateType,
      ITypeSymbol receiverType)
    {
        if (!InheritsFrom(candidateType, receiverType))
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, targetMethod.OriginalDefinition) ||
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, baseDefinition))
        {
            return true;
        }

        if (candidate.OverriddenMethod is not null &&
            (SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
             SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod.OriginalDefinition, baseDefinition)))
        {
            return true;
        }

        foreach (var implementedMethod in candidate.ExplicitInterfaceImplementations)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, targetMethod.OriginalDefinition) ||
                SymbolEqualityComparer.Default.Equals(implementedMethod.OriginalDefinition, baseDefinition))
            {
                return true;
            }
        }

        var implementation = candidateType.FindImplementationForInterfaceMember(targetMethod);
        if (implementation is IMethodSymbol implementationMethod &&
            SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, candidate.OriginalDefinition))
        {
            return true;
        }

        return candidate.IsVirtual || candidate.IsOverride || candidate.IsAbstract || candidate.IsSealed;
    }

    private static bool CanDispatchToExtensionReceiver(IMethodSymbol methodSymbol, ITypeSymbol receiverType)
    {
        if (!methodSymbol.IsExtensionMethod || methodSymbol.Parameters.Length == 0)
        {
            return false;
        }

        var receiverParameterType = methodSymbol.Parameters[0].Type;
        return receiverParameterType switch
        {
            INamedTypeSymbol namedReceiverType => InheritsFrom(namedReceiverType, receiverType) || InheritsFrom((INamedTypeSymbol)receiverType, namedReceiverType),
            ITypeParameterSymbol => true,
            _ => string.Equals(ComposeTypeFullName(receiverParameterType), ComposeTypeFullName(receiverType), StringComparison.Ordinal),
        };
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

    private static string ComposeResolvedDispatchKind(
      IMethodSymbol resolvedMethod,
      IMethodSymbol requestedMethod,
      ITypeSymbol? receiverType,
      string baseDispatchKind)
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

    private string ComposeMemberAccessFullName(ISymbol memberSymbol, ITypeSymbol? instanceType)
    {
        var baseType = ResolveAccessBaseType(instanceType, memberSymbol);
        if (string.IsNullOrEmpty(baseType))
        {
            return ComposeFullName(memberSymbol);
        }

        return memberSymbol switch
        {
            IPropertySymbol propertySymbol when propertySymbol.Parameters.Length > 0 =>
              $"{baseType}.{propertySymbol.Name}:{ComposePropertySignature(propertySymbol)}",
            _ => $"{baseType}.{memberSymbol.Name}",
        };
    }

    private string ResolveAccessBaseType(ITypeSymbol? instanceType, ISymbol memberSymbol)
    {
        if (instanceType is INamedTypeSymbol namedInstanceType &&
            CanUseReceiverTypeForMemberAccessFullName(namedInstanceType, memberSymbol))
        {
            return ComposeTypeFullName(namedInstanceType);
        }

        return ResolveDeclaringType(instanceType, memberSymbol);
    }

    private string ResolveDeclaringType(ITypeSymbol? instanceType, ISymbol memberSymbol)
    {
        if (instanceType is not null)
        {
            var candidate = ResolveDeclaredType(instanceType, memberSymbol);
            if (candidate is not null)
            {
                return ComposeTypeFullName(candidate);
            }
        }

        return memberSymbol.ContainingType is null ? string.Empty : ComposeTypeFullName(memberSymbol.ContainingType);
    }

    private INamedTypeSymbol? ResolveDeclaredType(ITypeSymbol instanceType, ISymbol memberSymbol)
    {
        if (instanceType is not INamedTypeSymbol namedInstanceType)
        {
            return memberSymbol.ContainingType ?? instanceType as INamedTypeSymbol;
        }

        if (memberSymbol.ContainingType is not null &&
            SymbolEqualityComparer.Default.Equals(memberSymbol.ContainingType, namedInstanceType))
        {
            return namedInstanceType;
        }

        var memberName = memberSymbol.Name;
        var exactContainingTypeCandidate = _declaredTypes.FirstOrDefault(declaredType =>
          memberSymbol.ContainingType is not null &&
          SymbolEqualityComparer.Default.Equals(declaredType, memberSymbol.ContainingType));
        if (exactContainingTypeCandidate is not null)
        {
            return exactContainingTypeCandidate;
        }

        var exactReceiverCandidate = _declaredTypes.FirstOrDefault(declaredType =>
          SymbolEqualityComparer.Default.Equals(declaredType, namedInstanceType) &&
          DeclaresCompatibleMember(declaredType, memberSymbol, memberName));
        if (exactReceiverCandidate is not null)
        {
            return exactReceiverCandidate;
        }

        foreach (var declaredType in _declaredTypes)
        {
            if (!InheritsFrom(namedInstanceType, declaredType) ||
                !DeclaresCompatibleMember(declaredType, memberSymbol, memberName))
            {
                continue;
            }

            return declaredType;
        }

        return memberSymbol.ContainingType ?? namedInstanceType;
    }

    private static bool CanUseReceiverTypeForMemberAccessFullName(
      INamedTypeSymbol instanceType,
      ISymbol memberSymbol)
    {
        if (memberSymbol.ContainingType is null)
        {
            return true;
        }

        return InheritsFrom(instanceType, memberSymbol.ContainingType) ||
               InheritsFrom(memberSymbol.ContainingType, instanceType);
    }

    private static bool DeclaresCompatibleMember(
      INamedTypeSymbol declaredType,
      ISymbol memberSymbol,
      string memberName)
    {
        foreach (var candidateMember in declaredType.GetMembers(memberName))
        {
            if (candidateMember.Kind != memberSymbol.Kind)
            {
                continue;
            }

            if (MembersMatch(candidateMember, memberSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MembersMatch(ISymbol candidateMember, ISymbol memberSymbol)
    {
        if (SymbolEqualityComparer.Default.Equals(candidateMember, memberSymbol))
        {
            return true;
        }

        return (candidateMember, memberSymbol) switch
        {
            (IPropertySymbol candidateProperty, IPropertySymbol targetProperty) =>
              string.Equals(ComposePropertySignature(candidateProperty), ComposePropertySignature(targetProperty), StringComparison.Ordinal),
            (IFieldSymbol candidateField, IFieldSymbol targetField) =>
              string.Equals(ComposeTypeFullName(candidateField.Type), ComposeTypeFullName(targetField.Type), StringComparison.Ordinal),
            _ => false,
        };
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

    private static IReadOnlyList<MetadataReference> CreateMetadataReferences()
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
