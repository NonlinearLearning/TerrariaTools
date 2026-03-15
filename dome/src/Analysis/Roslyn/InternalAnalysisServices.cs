namespace TerrariaTools.Dome.Analysis.Roslyn;

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Core;

internal static class AnalysisSymbolIds
{
    public static string FromSymbol(ISymbol symbol) =>
        symbol switch
        {
            ITypeSymbol type => MetadataTypeIdBuilder.Build(type),
            _ => MetadataMemberIdBuilder.Build(symbol).Value
        };

    public static SymbolDependencyNodeKind GetNodeKind(ISymbol symbol) =>
        symbol switch
        {
            ITypeSymbol => SymbolDependencyNodeKind.Type,
            IMethodSymbol => SymbolDependencyNodeKind.Method,
            IPropertySymbol => SymbolDependencyNodeKind.Property,
            IFieldSymbol => SymbolDependencyNodeKind.Field,
            IEventSymbol => SymbolDependencyNodeKind.Event,
            _ => SymbolDependencyNodeKind.Unknown
        };
}

internal sealed class MethodCallQueryService(FunctionFactsIndex functionFacts) : IMethodCallQueryService
{
    public IReadOnlyList<MemberId> GetCallees(MemberId memberId)
    {
        return functionFacts.FactsByMemberId.TryGetValue(memberId.Value, out var fact)
            ? fact.CalledMemberIds.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray()
            : Array.Empty<MemberId>();
    }

    public IReadOnlyList<MemberId> GetCallers(MemberId memberId)
    {
        return functionFacts.IncomingCallersByMemberId.TryGetValue(memberId.Value, out var callers)
            ? callers.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray()
            : Array.Empty<MemberId>();
    }

    public IReadOnlyList<MemberId> GetReachableMethods(IReadOnlyList<MemberId> rootMemberIds)
    {
        var included = new HashSet<string>(rootMemberIds.Select(item => item.Value), StringComparer.Ordinal);
        var queue = new Queue<string>(included);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!functionFacts.FactsByMemberId.TryGetValue(current, out var fact))
            {
                continue;
            }

            foreach (var callee in fact.CalledMemberIds)
            {
                if (included.Add(callee.Value))
                {
                    queue.Enqueue(callee.Value);
                }
            }
        }

        return included.OrderBy(item => item, StringComparer.Ordinal).Select(static item => new MemberId(item)).ToArray();
    }

    public FunctionDependencyGraph GetWholeGraph()
    {
        var nodes = functionFacts.FactsByMemberId.Values
            .Select(static fact => fact.Node)
            .OrderBy(static node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var edges = functionFacts.FactsByMemberId.Values
            .SelectMany(static fact => fact.CalledMemberIds.Select(callee => new FunctionDependencyEdge(fact.Node.MemberId, callee, FunctionDependencyKind.Calls)))
            .OrderBy(static edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ToArray();
        return new FunctionDependencyGraph(nodes, edges);
    }

    public IReadOnlyList<MemberId> GetShortestPath(IReadOnlyList<MemberId> rootMemberIds, MemberId targetMemberId)
    {
        var rootIds = rootMemberIds.Select(static item => item.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (rootIds.Contains(targetMemberId.Value, StringComparer.Ordinal))
        {
            return new[] { targetMemberId };
        }

        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(rootIds, StringComparer.Ordinal);
        var queue = new Queue<string>(rootIds);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!functionFacts.FactsByMemberId.TryGetValue(current, out var fact))
            {
                continue;
            }

            foreach (var callee in fact.CalledMemberIds)
            {
                if (!visited.Add(callee.Value))
                {
                    continue;
                }

                parents[callee.Value] = current;
                if (string.Equals(callee.Value, targetMemberId.Value, StringComparison.Ordinal))
                {
                    return RebuildPath(rootIds, parents, targetMemberId.Value);
                }

                queue.Enqueue(callee.Value);
            }
        }

        return Array.Empty<MemberId>();
    }

    public MethodReachabilityExplanation ExplainReachability(MemberId rootMemberId, MemberId targetMemberId)
    {
        var path = GetShortestPath(new[] { rootMemberId }, targetMemberId);
        return new MethodReachabilityExplanation(rootMemberId, targetMemberId, path.Count > 0, path);
    }

    private static IReadOnlyList<MemberId> RebuildPath(
        IReadOnlyList<string> rootIds,
        IReadOnlyDictionary<string, string> parents,
        string targetId)
    {
        var path = new List<string> { targetId };
        var current = targetId;
        while (parents.TryGetValue(current, out var parent))
        {
            path.Add(parent);
            current = parent;
        }

        path.Reverse();
        return path.Select(static item => new MemberId(item)).ToArray();
    }
}

internal sealed class AdvancedAnalysisSummaryService(
    IMethodCallQueryService methodCalls,
    ISymbolDependencyGraphProvider symbolDependencies) : IAdvancedAnalysisSummaryService
{
    public AdvancedAnalysisSummary BuildSummary()
    {
        var methodGraph = methodCalls.GetWholeGraph();
        var symbolGraph = symbolDependencies.GetWholeGraph();
        var methodIncoming = new HashSet<string>(methodGraph.Edges.Select(static edge => edge.TargetMemberId.Value), StringComparer.Ordinal);
        var methodRoots = methodGraph.Nodes
            .Where(node => !methodIncoming.Contains(node.MemberId.Value))
            .OrderBy(static node => node.MemberId.Value, StringComparer.Ordinal)
            .Select(static node => node.MemberId)
            .ToArray();
        var symbolIncoming = new HashSet<string>(symbolGraph.Edges.Select(static edge => edge.TargetSymbolId), StringComparer.Ordinal);
        var symbolRoots = symbolGraph.Nodes
            .Where(node => !symbolIncoming.Contains(node.SymbolId))
            .OrderBy(static node => node.SymbolId, StringComparer.Ordinal)
            .Select(static node => node.SymbolId)
            .ToArray();
        var methodComponents = FindCyclicComponents(
            methodGraph.Nodes.Select(static item => item.MemberId.Value),
            methodGraph.Edges.Select(static edge => (edge.SourceMemberId.Value, edge.TargetMemberId.Value)));
        var symbolComponents = FindCyclicComponents(
            symbolGraph.Nodes.Select(static item => item.SymbolId),
            symbolGraph.Edges.Select(static edge => (edge.SourceSymbolId, edge.TargetSymbolId)));

        return new AdvancedAnalysisSummary(
            methodGraph.Nodes.Count,
            methodGraph.Edges.Count,
            methodRoots,
            symbolRoots,
            methodComponents.Count,
            symbolComponents.Count,
            methodComponents.OrderByDescending(static item => item.Count).ThenBy(static item => item[0], StringComparer.Ordinal).Take(5).ToArray(),
            symbolComponents.OrderByDescending(static item => item.Count).ThenBy(static item => item[0], StringComparer.Ordinal).Take(5).ToArray(),
            GetHighlyConnectedMethods(methodGraph),
            GetHighlyConnectedSymbols(symbolGraph),
            symbolGraph.Edges.Count(static edge => edge.Kind is SymbolDependencyEdgeKind.InterfaceImplementation or SymbolDependencyEdgeKind.ExplicitInterfaceImplementation),
            symbolGraph.Edges.Count(static edge => edge.Kind == SymbolDependencyEdgeKind.Override),
            symbolGraph.Nodes.Count,
            symbolGraph.Edges.Count);
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCyclicComponents(
        IEnumerable<string> nodes,
        IEnumerable<(string Source, string Target)> edges)
    {
        var adjacency = edges
            .GroupBy(static edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(static edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var indexByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinkByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<IReadOnlyList<string>>();
        var index = 0;

        foreach (var node in nodes)
        {
            if (!indexByNode.ContainsKey(node))
            {
                Visit(node);
            }
        }

        return components;

        void Visit(string node)
        {
            indexByNode[node] = index;
            lowLinkByNode[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            if (adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!indexByNode.ContainsKey(neighbor))
                    {
                        Visit(neighbor);
                        lowLinkByNode[node] = Math.Min(lowLinkByNode[node], lowLinkByNode[neighbor]);
                    }
                    else if (onStack.Contains(neighbor))
                    {
                        lowLinkByNode[node] = Math.Min(lowLinkByNode[node], indexByNode[neighbor]);
                    }
                }
            }

            if (lowLinkByNode[node] != indexByNode[node])
            {
                return;
            }

            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!string.Equals(current, node, StringComparison.Ordinal));

            var isCycle = component.Count > 1 ||
                          (adjacency.TryGetValue(node, out var selfTargets) && selfTargets.Contains(node, StringComparer.Ordinal));
            if (isCycle)
            {
                component.Sort(StringComparer.Ordinal);
                components.Add(component);
            }
        }
    }

    private static IReadOnlyList<string> GetHighlyConnectedMethods(FunctionDependencyGraph graph)
    {
        return graph.Edges
            .SelectMany(static edge => new[] { edge.SourceMemberId.Value, edge.TargetMemberId.Value })
            .GroupBy(static item => item, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(static group => group.Key)
            .ToArray();
    }

    private static IReadOnlyList<string> GetHighlyConnectedSymbols(SymbolDependencyGraph graph)
    {
        return graph.Edges
            .SelectMany(static edge => new[] { edge.SourceSymbolId, edge.TargetSymbolId })
            .GroupBy(static item => item, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(static group => group.Key)
            .ToArray();
    }
}

internal sealed class DataFlowSummaryService(
    IReadOnlyList<AnalysisDocumentContext> documents) : IDataFlowSummaryService
{
    private readonly Dictionary<string, (SyntaxNode Node, SemanticModel Model)> _declarations =
        AnalysisDocumentLookup.BuildDeclarationMap(documents);

    public DataFlowSummary Analyze(MemberId memberId)
    {
        if (!_declarations.TryGetValue(memberId.Value, out var entry))
        {
            throw new InvalidOperationException($"No declaration found for '{memberId.Value}'.");
        }

        var scope = AnalysisDocumentLookup.GetExecutableScope(entry.Node);
        if (scope == null)
        {
            return new DataFlowSummary(memberId, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<MemberId>());
        }

        var invokedMemberIds = scope.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => entry.Model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
            .Where(static symbol => symbol != null)
            .Select(static symbol => symbol!.ReducedFrom ?? symbol!)
            .Select(static symbol => MetadataMemberIdBuilder.Build(symbol))
            .DistinctBy(static item => item.Value)
            .OrderBy(static item => item.Value, StringComparer.Ordinal)
            .ToArray();

        var variables = scope.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => entry.Model.GetSymbolInfo(identifier).Symbol)
            .OfType<ILocalSymbol>()
            .ToArray();
        var parameters = scope.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => entry.Model.GetSymbolInfo(identifier).Symbol)
            .OfType<IParameterSymbol>()
            .ToArray();

        var defined = scope.DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => entry.Model.GetDeclaredSymbol(variable)?.Name)
            .OfType<string>()
            .Concat(
                scope.DescendantNodesAndSelf()
                    .OfType<AssignmentExpressionSyntax>()
                    .Select(assignment => entry.Model.GetSymbolInfo(assignment.Left).Symbol?.Name)
                    .OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var used = variables.Select(static symbol => symbol.Name)
            .Concat(parameters.Select(static symbol => symbol.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

        return new DataFlowSummary(memberId, defined, used, invokedMemberIds);
    }

}

internal sealed class SwitchFlowSummaryService(
    IReadOnlyList<AnalysisDocumentContext> documents) : ISwitchFlowSummaryService
{
    private readonly Dictionary<string, (SyntaxNode Node, SemanticModel Model)> _declarations =
        AnalysisDocumentLookup.BuildDeclarationMap(documents);

    public IReadOnlyList<SwitchFlowSummary> Analyze(MemberId memberId)
    {
        if (!_declarations.TryGetValue(memberId.Value, out var entry))
        {
            throw new InvalidOperationException($"No declaration found for '{memberId.Value}'.");
        }

        var scope = AnalysisDocumentLookup.GetExecutableScope(entry.Node);
        if (scope == null)
        {
            return Array.Empty<SwitchFlowSummary>();
        }

        return scope.DescendantNodes()
            .OfType<SwitchStatementSyntax>()
            .Select(statement => new SwitchFlowSummary(
                memberId,
                statement.Sections.Select(section => new SwitchCaseSummary(
                        string.Join(" | ", section.Labels.Select(static label => label.ToString().TrimEnd(':'))),
                        section.DescendantNodes()
                            .OfType<IdentifierNameSyntax>()
                            .Select(identifier => entry.Model.GetSymbolInfo(identifier).Symbol?.Name)
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(static item => item, StringComparer.Ordinal)
                            .ToArray(),
                        section.DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Select(invocation => entry.Model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
                            .Where(static symbol => symbol != null)
                            .Select(static symbol => MetadataMemberIdBuilder.Build(symbol!.ReducedFrom ?? symbol!))
                            .DistinctBy(static item => item.Value)
                            .OrderBy(static item => item.Value, StringComparer.Ordinal)
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }
}

internal sealed class SymbolDependencyGraphProvider : ISymbolDependencyGraphProvider
{
    private readonly Dictionary<string, SymbolDependencyNode> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<SymbolDependencyEdge> _edges = new();

    public SymbolDependencyGraphProvider(IReadOnlyList<AnalysisDocumentContext> documents)
    {
        foreach (var document in documents)
        {
            BuildDocument(document);
        }
    }

    public SymbolDependencyGraph GetWholeGraph() => new(
        _nodes.Values.OrderBy(static node => node.SymbolId, StringComparer.Ordinal).ToArray(),
        _edges.OrderBy(static edge => edge.SourceSymbolId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Kind)
            .ToArray());

    public SymbolDependencyGraph GetBackwardSlice(string symbolId)
        => GetBackwardSlice(symbolId, new SymbolDependencyQueryOptions());

    public SymbolDependencyGraph GetBackwardSlice(string symbolId, SymbolDependencyQueryOptions options)
    {
        var reverse = _edges.GroupBy(static edge => edge.TargetSymbolId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return TraverseSlice(new[] { symbolId }, reverse, static edge => edge.SourceSymbolId, options);
    }

    public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds)
        => GetForwardSlice(rootSymbolIds, new SymbolDependencyQueryOptions());

    public SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options)
    {
        var forward = _edges.GroupBy(static edge => edge.SourceSymbolId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return TraverseSlice(rootSymbolIds, forward, static edge => edge.TargetSymbolId, options);
    }

    private void BuildDocument(AnalysisDocumentContext document)
    {
        foreach (var type in document.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(type) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            AddNode(typeSymbol, document.Document.SourcePath);

            if (typeSymbol.BaseType != null)
            {
                AddDependency(typeSymbol, typeSymbol.BaseType, SymbolDependencyEdgeKind.BaseType, document.Document.SourcePath);
            }

            foreach (var iface in typeSymbol.Interfaces)
            {
                AddDependency(typeSymbol, iface, SymbolDependencyEdgeKind.InterfaceImplementation, document.Document.SourcePath);
            }
        }

        foreach (var field in document.Root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (document.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                {
                    continue;
                }

                AddNode(fieldSymbol, document.Document.SourcePath);
                AddDependency(fieldSymbol, fieldSymbol.ContainingType, SymbolDependencyEdgeKind.ContainsType, document.Document.SourcePath);
                AddDependency(fieldSymbol, fieldSymbol.Type, SymbolDependencyEdgeKind.FieldType, document.Document.SourcePath);

                if (variable.Initializer != null)
                {
                    AnalyzeExpression(fieldSymbol, variable.Initializer.Value, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.InitializerReference);
                    if (fieldSymbol.ContainingType != null)
                    {
                        AnalyzeExpression(fieldSymbol.ContainingType, variable.Initializer.Value, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.InitializerReference);
                    }
                }
            }
        }

        foreach (var property in document.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            AddNode(propertySymbol, document.Document.SourcePath);
            AddDependency(propertySymbol, propertySymbol.ContainingType, SymbolDependencyEdgeKind.ContainsType, document.Document.SourcePath);
            AddDependency(propertySymbol, propertySymbol.Type, SymbolDependencyEdgeKind.PropertyType, document.Document.SourcePath);
            if (propertySymbol.OverriddenProperty != null)
            {
                AddDependency(propertySymbol, propertySymbol.OverriddenProperty, SymbolDependencyEdgeKind.Override, document.Document.SourcePath);
            }

            foreach (var implementation in propertySymbol.ExplicitInterfaceImplementations)
            {
                AddDependency(propertySymbol, implementation, SymbolDependencyEdgeKind.ExplicitInterfaceImplementation, document.Document.SourcePath);
            }

            if (property.Initializer != null)
            {
                AnalyzeExpression(propertySymbol, property.Initializer.Value, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.InitializerReference);
                if (propertySymbol.ContainingType != null)
                {
                    AnalyzeExpression(propertySymbol.ContainingType, property.Initializer.Value, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.InitializerReference);
                }
            }
        }

        foreach (var eventField in document.Root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
        {
            foreach (var variable in eventField.Declaration.Variables)
            {
                if (document.SemanticModel.GetDeclaredSymbol(variable) is not IEventSymbol eventSymbol)
                {
                    continue;
                }

                AddNode(eventSymbol, document.Document.SourcePath);
                AddDependency(eventSymbol, eventSymbol.ContainingType, SymbolDependencyEdgeKind.ContainsType, document.Document.SourcePath);
                AddDependency(eventSymbol, eventSymbol.Type, SymbolDependencyEdgeKind.ParameterType, document.Document.SourcePath);
                AddInterfaceDependencies(eventSymbol, document.Document.SourcePath);
            }
        }

        foreach (var eventDeclaration in document.Root.DescendantNodes().OfType<EventDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(eventDeclaration) is not IEventSymbol eventSymbol)
            {
                continue;
            }

            AddNode(eventSymbol, document.Document.SourcePath);
            AddDependency(eventSymbol, eventSymbol.ContainingType, SymbolDependencyEdgeKind.ContainsType, document.Document.SourcePath);
            AddDependency(eventSymbol, eventSymbol.Type, SymbolDependencyEdgeKind.ParameterType, document.Document.SourcePath);
            AddInterfaceDependencies(eventSymbol, document.Document.SourcePath);

            foreach (var accessor in eventDeclaration.AccessorList?.Accessors ?? Enumerable.Empty<AccessorDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(accessor) is IMethodSymbol accessorSymbol)
                {
                    AnalyzeMethod(document, accessorSymbol, accessor);
                }
            }
        }

        foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(method) is IMethodSymbol methodSymbol)
            {
                AnalyzeMethod(document, methodSymbol, method);
            }
        }

        foreach (var ctor in document.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(ctor) is IMethodSymbol ctorSymbol)
            {
                AnalyzeMethod(document, ctorSymbol, ctor);
            }
        }

        foreach (var accessor in document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(accessor) is IMethodSymbol accessorSymbol)
            {
                AnalyzeMethod(document, accessorSymbol, accessor);
            }
        }

        foreach (var operatorDeclaration in document.Root.DescendantNodes().OfType<OperatorDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(operatorDeclaration) is IMethodSymbol operatorSymbol)
            {
                AnalyzeMethod(document, operatorSymbol, operatorDeclaration);
            }
        }

        foreach (var conversionDeclaration in document.Root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(conversionDeclaration) is IMethodSymbol conversionSymbol)
            {
                AnalyzeMethod(document, conversionSymbol, conversionDeclaration);
            }
        }
    }

    private void AnalyzeMethod(AnalysisDocumentContext document, IMethodSymbol methodSymbol, SyntaxNode declaration)
    {
        AddNode(methodSymbol, document.Document.SourcePath);
        AddDependency(methodSymbol, methodSymbol.ContainingType, SymbolDependencyEdgeKind.ContainsType, document.Document.SourcePath);
        AddDependency(methodSymbol, methodSymbol.ReturnType, SymbolDependencyEdgeKind.ReturnType, document.Document.SourcePath);
        foreach (var parameter in methodSymbol.Parameters)
        {
            AddDependency(methodSymbol, parameter.Type, SymbolDependencyEdgeKind.ParameterType, document.Document.SourcePath);
        }
        AddTypeStructureDependencies(methodSymbol, document.Document.SourcePath);

        if (methodSymbol.OverriddenMethod != null)
        {
            AddDependency(methodSymbol, methodSymbol.OverriddenMethod, SymbolDependencyEdgeKind.Override, document.Document.SourcePath);
        }

        foreach (var implementation in methodSymbol.ExplicitInterfaceImplementations)
        {
            AddDependency(methodSymbol, implementation, SymbolDependencyEdgeKind.ExplicitInterfaceImplementation, document.Document.SourcePath);
        }

        AddInterfaceDependencies(methodSymbol, document.Document.SourcePath);

        if (declaration is ConstructorDeclarationSyntax ctor && ctor.Initializer != null)
        {
            var targetCtor = document.SemanticModel.GetSymbolInfo(ctor.Initializer).Symbol;
            if (targetCtor != null)
            {
                AddDependency(methodSymbol, targetCtor, SymbolDependencyEdgeKind.ConstructorInitializer, document.Document.SourcePath);
            }

            AnalyzeExpression(methodSymbol, ctor.Initializer, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.ConstructorInitializer);
            if (methodSymbol.ContainingType != null)
            {
                AnalyzeExpression(methodSymbol.ContainingType, ctor.Initializer, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.ConstructorInitializer);
            }
        }

        var scope = AnalysisDocumentLookup.GetExecutableScope(declaration);
        if (scope != null)
        {
            AnalyzeExpression(methodSymbol, scope, document.SemanticModel, document.Document.SourcePath, SymbolDependencyEdgeKind.MemberReference);
        }
    }

    private void AnalyzeExpression(ISymbol from, SyntaxNode expression, SemanticModel model, string documentPath, SymbolDependencyEdgeKind defaultKind)
    {
        foreach (var descendant in expression.DescendantNodesAndSelf())
        {
            switch (descendant)
            {
                case InvocationExpressionSyntax invocation:
                {
                    if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
                    {
                        AddDependency(from, method.ReducedFrom ?? method, SymbolDependencyEdgeKind.Invocation, documentPath);
                    }

                    break;
                }
                case ObjectCreationExpressionSyntax objectCreation:
                {
                    if (model.GetSymbolInfo(objectCreation).Symbol is ISymbol symbol)
                    {
                        AddDependency(from, symbol, SymbolDependencyEdgeKind.ObjectCreation, documentPath);
                    }

                    break;
                }
                case InitializerExpressionSyntax initializer:
                {
                    foreach (var invocation in initializer.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (model.GetSymbolInfo(invocation).Symbol is ISymbol symbol)
                        {
                            AddDependency(from, symbol, SymbolDependencyEdgeKind.CollectionInitializer, documentPath);
                        }
                    }

                    break;
                }
                case ExpressionSyntax expr:
                {
                    var typeInfo = model.GetTypeInfo(expr);
                    if (typeInfo.ConvertedType != null &&
                        !SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType))
                    {
                        AddDependency(from, typeInfo.ConvertedType, SymbolDependencyEdgeKind.Conversion, documentPath);
                    }

                    break;
                }
            }

            if (descendant is IdentifierNameSyntax or MemberAccessExpressionSyntax or SimpleNameSyntax)
            {
                var symbol = model.GetSymbolInfo(descendant).Symbol;
                if (symbol != null)
                {
                    AddDependency(from, symbol, defaultKind, documentPath);
                }
            }
        }
    }

    private void AddNode(ISymbol symbol, string? documentPath)
    {
        var id = AnalysisSymbolIds.FromSymbol(symbol);
        _nodes.TryAdd(id, new SymbolDependencyNode(id, AnalysisSymbolIds.GetNodeKind(symbol), symbol.ToDisplayString(), documentPath));
    }

    private void AddDependency(ISymbol from, ISymbol? to, SymbolDependencyEdgeKind kind, string documentPath)
    {
        if (to == null)
        {
            return;
        }

        if (to is IMethodSymbol method && method.MethodKind == MethodKind.ReducedExtension && method.ReducedFrom != null)
        {
            to = method.ReducedFrom;
        }

        AddNode(from, documentPath);
        AddNode(to, documentPath);
        _edges.Add(new SymbolDependencyEdge(AnalysisSymbolIds.FromSymbol(from), AnalysisSymbolIds.FromSymbol(to), kind));

        if (to is IMethodSymbol targetMethod && targetMethod.ContainingType != null)
        {
            AddNode(targetMethod.ContainingType, documentPath);
            _edges.Add(new SymbolDependencyEdge(AnalysisSymbolIds.FromSymbol(targetMethod), AnalysisSymbolIds.FromSymbol(targetMethod.ContainingType), SymbolDependencyEdgeKind.ContainsType));
        }
    }

    private SymbolDependencyGraph TraverseSlice(
        IEnumerable<string> rootIds,
        IReadOnlyDictionary<string, SymbolDependencyEdge[]> adjacency,
        Func<SymbolDependencyEdge, string> getNext,
        SymbolDependencyQueryOptions options)
    {
        var included = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string NodeId, int Depth)>(
            rootIds.Where(static item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Select(static item => (item, 0)));
        var allowedEdgeKinds = options.AllowedEdgeKinds?.ToHashSet() ;
        var allowedNodeKinds = options.AllowedNodeKinds?.ToHashSet();

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (!included.Add(current))
            {
                continue;
            }

            if (options.MaxDepth.HasValue && depth >= options.MaxDepth.Value)
            {
                continue;
            }

            if (!adjacency.TryGetValue(current, out var nextEdges))
            {
                continue;
            }

            foreach (var edge in nextEdges)
            {
                if (allowedEdgeKinds != null && !allowedEdgeKinds.Contains(edge.Kind))
                {
                    continue;
                }

                var nextNodeId = getNext(edge);
                if (allowedNodeKinds != null &&
                    _nodes.TryGetValue(nextNodeId, out var nextNode) &&
                    !allowedNodeKinds.Contains(nextNode.Kind))
                {
                    continue;
                }

                queue.Enqueue((nextNodeId, depth + 1));
            }
        }

        if (!options.IncludeRoots)
        {
            foreach (var rootId in rootIds)
            {
                included.Remove(rootId);
            }
        }

        var nodes = _nodes.Values.Where(node => included.Contains(node.SymbolId))
            .Where(node => allowedNodeKinds == null || allowedNodeKinds.Contains(node.Kind))
            .OrderBy(static node => node.SymbolId, StringComparer.Ordinal)
            .ToArray();
        var nodeSet = nodes.Select(static node => node.SymbolId).ToHashSet(StringComparer.Ordinal);
        var edges = _edges.Where(edge => nodeSet.Contains(edge.SourceSymbolId) && nodeSet.Contains(edge.TargetSymbolId))
            .Where(edge => allowedEdgeKinds == null || allowedEdgeKinds.Contains(edge.Kind))
            .OrderBy(static edge => edge.SourceSymbolId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetSymbolId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Kind)
            .ToArray();
        return new SymbolDependencyGraph(nodes, edges);
    }

    private void AddInterfaceDependencies(ISymbol symbol, string documentPath)
    {
        switch (symbol)
        {
            case IMethodSymbol methodSymbol:
            {
                foreach (var implementation in methodSymbol.ContainingType.AllInterfaces
                             .SelectMany(iface => methodSymbol.ContainingType.FindImplementationForInterfaceMember(iface.GetMembers().OfType<IMethodSymbol>()
                                 .FirstOrDefault(candidate => candidate.Name == methodSymbol.Name)) is IMethodSymbol implementation &&
                                 SymbolEqualityComparer.Default.Equals(implementation, methodSymbol)
                                     ? new[] { iface.GetMembers().OfType<IMethodSymbol>().First(candidate => candidate.Name == methodSymbol.Name) }
                                     : Array.Empty<IMethodSymbol>()))
                {
                    AddDependency(methodSymbol, implementation, SymbolDependencyEdgeKind.InterfaceImplementation, documentPath);
                }

                break;
            }
            case IPropertySymbol propertySymbol:
            {
                foreach (var iface in propertySymbol.ContainingType.AllInterfaces)
                {
                    foreach (var candidate in iface.GetMembers().OfType<IPropertySymbol>().Where(member => member.Name == propertySymbol.Name))
                    {
                        if (propertySymbol.ContainingType.FindImplementationForInterfaceMember(candidate) is IPropertySymbol implementation &&
                            SymbolEqualityComparer.Default.Equals(implementation, propertySymbol))
                        {
                            AddDependency(propertySymbol, candidate, SymbolDependencyEdgeKind.InterfaceImplementation, documentPath);
                        }
                    }
                }

                break;
            }
            case IEventSymbol eventSymbol:
            {
                foreach (var iface in eventSymbol.ContainingType.AllInterfaces)
                {
                    foreach (var candidate in iface.GetMembers().OfType<IEventSymbol>().Where(member => member.Name == eventSymbol.Name))
                    {
                        if (eventSymbol.ContainingType.FindImplementationForInterfaceMember(candidate) is IEventSymbol implementation &&
                            SymbolEqualityComparer.Default.Equals(implementation, eventSymbol))
                        {
                            AddDependency(eventSymbol, candidate, SymbolDependencyEdgeKind.InterfaceImplementation, documentPath);
                        }
                    }
                }

                break;
            }
        }
    }

    private void AddTypeStructureDependencies(ISymbol symbol, string documentPath)
    {
        switch (symbol)
        {
            case IMethodSymbol methodSymbol:
                AddTypeDependency(methodSymbol, methodSymbol.ReturnType, SymbolDependencyEdgeKind.ReturnType, documentPath);
                foreach (var parameter in methodSymbol.Parameters)
                {
                    AddTypeDependency(methodSymbol, parameter.Type, SymbolDependencyEdgeKind.ParameterType, documentPath);
                }

                if (methodSymbol.MethodKind == MethodKind.Conversion)
                {
                    AddTypeDependency(methodSymbol, methodSymbol.ReturnType, SymbolDependencyEdgeKind.Conversion, documentPath);
                }

                break;
            case IPropertySymbol propertySymbol:
                AddTypeDependency(propertySymbol, propertySymbol.Type, SymbolDependencyEdgeKind.PropertyType, documentPath);
                break;
            case IFieldSymbol fieldSymbol:
                AddTypeDependency(fieldSymbol, fieldSymbol.Type, SymbolDependencyEdgeKind.FieldType, documentPath);
                break;
            case IEventSymbol eventSymbol:
                AddTypeDependency(eventSymbol, eventSymbol.Type, SymbolDependencyEdgeKind.ParameterType, documentPath);
                break;
        }
    }

    private void AddTypeDependency(ISymbol from, ITypeSymbol? typeSymbol, SymbolDependencyEdgeKind kind, string documentPath)
    {
        if (typeSymbol == null)
        {
            return;
        }

        AddDependency(from, typeSymbol, kind, documentPath);
        foreach (var nested in ExpandTypeDependencies(typeSymbol))
        {
            AddDependency(from, nested, kind, documentPath);
        }
    }

    private static IEnumerable<ITypeSymbol> ExpandTypeDependencies(ITypeSymbol typeSymbol)
    {
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<ITypeSymbol>();
        queue.Enqueue(typeSymbol);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(current, typeSymbol))
            {
                yield return current;
            }

            switch (current)
            {
                case IArrayTypeSymbol arrayType:
                    queue.Enqueue(arrayType.ElementType);
                    break;
                case ITypeParameterSymbol typeParameter:
                    foreach (var constraint in typeParameter.ConstraintTypes)
                    {
                        queue.Enqueue(constraint);
                    }
                    break;
                case INamedTypeSymbol namedType:
                    foreach (var argument in namedType.TypeArguments.OfType<ITypeSymbol>())
                    {
                        queue.Enqueue(argument);
                    }

                    foreach (var constraint in namedType.TypeParameters.SelectMany(static typeParameter => typeParameter.ConstraintTypes))
                    {
                        queue.Enqueue(constraint);
                    }

                    if (namedType.IsTupleType)
                    {
                        foreach (var element in namedType.TupleElements)
                        {
                            queue.Enqueue(element.Type);
                        }
                    }
                    break;
                case IPointerTypeSymbol pointerType:
                    queue.Enqueue(pointerType.PointedAtType);
                    break;
            }

            if (current.NullableAnnotation == NullableAnnotation.Annotated &&
                current.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                current is INamedTypeSymbol nullableType &&
                nullableType.TypeArguments.FirstOrDefault() is ITypeSymbol nullableArgument)
            {
                queue.Enqueue(nullableArgument);
            }
        }
    }
}

internal sealed class CallChainAnalysisService(
    FunctionIndex functionIndex,
    IMethodCallQueryService methodCalls) : ICallChainAnalysisService
{
    private static readonly Regex EntryRegex = new(@"\[(?<time>.*?)\]\s+\[ENTER\]\s+(?<method>.*)", RegexOptions.Compiled);

    public IReadOnlyList<CallChainEntry> Parse(string logText)
    {
        return logText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => EntryRegex.Match(line))
            .Where(static match => match.Success)
            .Select(match => new CallChainEntry(match.Groups["time"].Value, match.Groups["method"].Value.Trim()))
            .ToArray();
    }

    public CallChainAnalysisSummary Analyze(string logText)
    {
        var entries = Parse(logText);
        var mapped = entries
            .SelectMany(entry => MapMethodName(entry.MethodName))
            .DistinctBy(static item => item.Value)
            .OrderBy(static item => item.Value, StringComparer.Ordinal)
            .ToArray();
        var unmapped = entries.Select(static entry => entry.MethodName)
            .Distinct(StringComparer.Ordinal)
            .Where(methodName => !mapped.Any(mappedId => string.Equals(TrimSignature(mappedId.Value), methodName, StringComparison.Ordinal)))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var staticOnly = functionIndex.NodesByMemberId.Keys
            .Except(mapped.Select(static item => item.Value), StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .Select(static item => new MemberId(item))
            .ToArray();

        return new CallChainAnalysisSummary(entries.Count, mapped, unmapped, staticOnly);
    }

    private IEnumerable<MemberId> MapMethodName(string methodName)
    {
        foreach (var memberId in functionIndex.NodesByMemberId.Keys)
        {
            if (string.Equals(TrimSignature(memberId), methodName, StringComparison.Ordinal))
            {
                yield return new MemberId(memberId);
            }
        }
    }

    private static string TrimSignature(string memberId)
    {
        var parameterIndex = memberId.IndexOf('(');
        return parameterIndex > 0 ? memberId[..parameterIndex] : memberId;
    }
}

internal static class AnalysisDocumentLookup
{
    public static Dictionary<string, (SyntaxNode Node, SemanticModel Model)> BuildDeclarationMap(IReadOnlyList<AnalysisDocumentContext> documents)
    {
        var map = new Dictionary<string, (SyntaxNode Node, SemanticModel Model)>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(method) is ISymbol symbol)
                {
                    map[MetadataMemberIdBuilder.Build(symbol).Value] = (method, document.SemanticModel);
                }
            }

            foreach (var ctor in document.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(ctor) is ISymbol symbol)
                {
                    map[MetadataMemberIdBuilder.Build(symbol).Value] = (ctor, document.SemanticModel);
                }
            }

            foreach (var accessor in document.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                if (document.SemanticModel.GetDeclaredSymbol(accessor) is ISymbol symbol)
                {
                    map[MetadataMemberIdBuilder.Build(symbol).Value] = (accessor, document.SemanticModel);
                }
            }
        }

        return map;
    }

    public static SyntaxNode? GetExecutableScope(SyntaxNode declaration) =>
        declaration switch
        {
            MethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
            ConstructorDeclarationSyntax ctor => ctor.Body ?? (SyntaxNode?)ctor.Initializer,
            AccessorDeclarationSyntax accessor => accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression,
            _ => null
        };
}
