namespace TerrariaTools.Dome.Analysis.Roslyn;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Core;

internal sealed class MemberCleanupQueryService : IMemberCleanupQueryService
{
    private readonly FunctionIndex _functionIndex;
    private readonly IReferenceQueryService _references;
    private readonly Dictionary<string, MemberCleanupSymbolInfo> _symbolsById;
    private readonly Dictionary<string, MemberCleanupTypeInfo> _typesById;
    private readonly Dictionary<string, HashSet<string>> _incomingTypeIdsBySymbolId;
    private readonly Dictionary<string, IReadOnlyList<MemberId>> _publicMethodsByTypeId;

    public MemberCleanupQueryService(
        IReadOnlyList<AnalysisDocumentContext> documents,
        FunctionIndex functionIndex,
        IReferenceQueryService references,
        IInheritanceQueryService inheritance,
        ISymbolDependencyGraphProvider symbolDependencies)
    {
        _functionIndex = functionIndex;
        _references = references;
        _symbolsById = new Dictionary<string, MemberCleanupSymbolInfo>(StringComparer.Ordinal);
        _typesById = new Dictionary<string, MemberCleanupTypeInfo>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            BuildDocumentMetadata(document, inheritance);
        }

        _incomingTypeIdsBySymbolId = BuildIncomingTypeMap(symbolDependencies.GetWholeGraph());

        _publicMethodsByTypeId = _symbolsById.Values
            .Where(static info => info.MemberKind == MemberKind.Method && info.IsPublic && info.IsOrdinaryMethod && !info.IsStatic)
            .GroupBy(static info => info.DeclaringTypeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MemberId>)group
                    .OrderBy(static info => info.Name, StringComparer.Ordinal)
                    .ThenBy(static info => info.SymbolId, StringComparer.Ordinal)
                    .Select(static info => new MemberId(info.SymbolId))
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public MemberCleanupSymbolInfo? GetSymbolInfo(string symbolOrMemberId)
        => _symbolsById.TryGetValue(symbolOrMemberId, out var info) ? info : null;

    public MemberCleanupTypeInfo? GetTypeInfo(string typeId)
        => _typesById.TryGetValue(typeId, out var info) ? info : null;

    public bool HasAnyReferences(string symbolOrMemberId)
    {
        if (_references.HasReferences(symbolOrMemberId))
        {
            return true;
        }

        return _incomingTypeIdsBySymbolId.TryGetValue(symbolOrMemberId, out var sourceTypes) && sourceTypes.Count > 0;
    }

    public bool HasInternalMethodReferences(MemberId memberId)
    {
        if (!_symbolsById.TryGetValue(memberId.Value, out var info))
        {
            return false;
        }

        foreach (var caller in _references.GetReferencingFunctions(memberId.Value))
        {
            if (_functionIndex.NodesByMemberId.TryGetValue(caller.Value, out var callerNode) &&
                string.Equals(callerNode.DeclaringTypeId, info.DeclaringTypeId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasExternalMethodReferences(MemberId memberId)
    {
        if (!_symbolsById.TryGetValue(memberId.Value, out var info))
        {
            return false;
        }

        foreach (var caller in _references.GetReferencingFunctions(memberId.Value))
        {
            if (_functionIndex.NodesByMemberId.TryGetValue(caller.Value, out var callerNode) &&
                !string.Equals(callerNode.DeclaringTypeId, info.DeclaringTypeId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<MemberId> GetReorderablePublicMethods(string typeId)
        => _publicMethodsByTypeId.TryGetValue(typeId, out var methods) ? methods : Array.Empty<MemberId>();

    private void BuildDocumentMetadata(AnalysisDocumentContext document, IInheritanceQueryService inheritance)
    {
        foreach (var typeDeclaration in document.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            var typeId = MetadataTypeIdBuilder.Build(typeSymbol);
            _typesById[typeId] = new MemberCleanupTypeInfo(
                typeId,
                document.Document.RelativePath,
                typeSymbol.Name,
                typeSymbol.DeclaredAccessibility == Accessibility.Public,
                typeSymbol.IsAbstract,
                typeSymbol.IsStatic,
                typeDeclaration.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)) || typeSymbol.DeclaringSyntaxReferences.Length > 1,
                typeSymbol.ContainingType != null,
                typeSymbol.TypeKind == TypeKind.Interface,
                inheritance.IsInInheritanceChain(typeId));
        }

        foreach (var method in document.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            var memberId = MetadataMemberIdBuilder.Build(methodSymbol).Value;
            var typeInfo = GetTypeInfo(MetadataTypeIdBuilder.Build(methodSymbol.ContainingType));
            _symbolsById[memberId] = new MemberCleanupSymbolInfo(
                memberId,
                MemberKind.Method,
                MetadataTypeIdBuilder.Build(methodSymbol.ContainingType),
                document.Document.RelativePath,
                methodSymbol.Name,
                methodSymbol.DeclaredAccessibility == Accessibility.Public,
                methodSymbol.DeclaredAccessibility == Accessibility.Private,
                methodSymbol.IsStatic,
                methodSymbol.IsAbstract,
                methodSymbol.IsVirtual,
                methodSymbol.IsOverride,
                methodSymbol.IsExtern,
                true,
                typeInfo?.IsPartial ?? false,
                typeInfo?.IsNested ?? false,
                typeInfo?.IsInterface ?? false,
                methodSymbol.IsStatic && string.Equals(methodSymbol.Name, "Main", StringComparison.Ordinal));
        }

        foreach (var field in document.Root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (document.SemanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                {
                    continue;
                }

                var symbolId = MetadataMemberIdBuilder.Build(fieldSymbol).Value;
                var typeInfo = GetTypeInfo(MetadataTypeIdBuilder.Build(fieldSymbol.ContainingType));
                _symbolsById[symbolId] = new MemberCleanupSymbolInfo(
                    symbolId,
                    MemberKind.Field,
                    MetadataTypeIdBuilder.Build(fieldSymbol.ContainingType),
                    document.Document.RelativePath,
                    fieldSymbol.Name,
                    fieldSymbol.DeclaredAccessibility == Accessibility.Public,
                    fieldSymbol.DeclaredAccessibility == Accessibility.Private,
                    fieldSymbol.IsStatic,
                    false,
                    false,
                    false,
                    false,
                    false,
                    typeInfo?.IsPartial ?? false,
                    typeInfo?.IsNested ?? false,
                    typeInfo?.IsInterface ?? false,
                    false);
            }
        }

        foreach (var property in document.Root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (document.SemanticModel.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            var symbolId = MetadataMemberIdBuilder.Build(propertySymbol).Value;
            var typeInfo = GetTypeInfo(MetadataTypeIdBuilder.Build(propertySymbol.ContainingType));
            _symbolsById[symbolId] = new MemberCleanupSymbolInfo(
                symbolId,
                MemberKind.Property,
                MetadataTypeIdBuilder.Build(propertySymbol.ContainingType),
                document.Document.RelativePath,
                propertySymbol.Name,
                propertySymbol.DeclaredAccessibility == Accessibility.Public,
                propertySymbol.DeclaredAccessibility == Accessibility.Private,
                propertySymbol.IsStatic,
                propertySymbol.IsAbstract,
                propertySymbol.IsVirtual,
                propertySymbol.IsOverride,
                false,
                false,
                typeInfo?.IsPartial ?? false,
                typeInfo?.IsNested ?? false,
                typeInfo?.IsInterface ?? false,
                false);
        }
    }

    private Dictionary<string, HashSet<string>> BuildIncomingTypeMap(SymbolDependencyGraph graph)
    {
        var nodeLookup = graph.Nodes.ToDictionary(static node => node.SymbolId, StringComparer.Ordinal);
        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            if (edge.Kind == SymbolDependencyEdgeKind.ContainsType)
            {
                continue;
            }

            if (!nodeLookup.TryGetValue(edge.SourceSymbolId, out var sourceNode))
            {
                continue;
            }

            var sourceTypeId = ResolveDeclaringTypeId(sourceNode.SymbolId);
            if (string.IsNullOrWhiteSpace(sourceTypeId))
            {
                continue;
            }

            if (!incoming.TryGetValue(edge.TargetSymbolId, out var sourceTypes))
            {
                sourceTypes = new HashSet<string>(StringComparer.Ordinal);
                incoming[edge.TargetSymbolId] = sourceTypes;
            }

            sourceTypes.Add(sourceTypeId);
        }

        return incoming;
    }

    private string? ResolveDeclaringTypeId(string symbolId)
    {
        if (_symbolsById.TryGetValue(symbolId, out var symbolInfo))
        {
            return symbolInfo.DeclaringTypeId;
        }

        if (_typesById.ContainsKey(symbolId))
        {
            return symbolId;
        }

        var lastDot = symbolId.LastIndexOf('.');
        if (lastDot <= 0)
        {
            return null;
        }

        var candidate = symbolId[..lastDot];
        return _typesById.ContainsKey(candidate) ? candidate : null;
    }
}
