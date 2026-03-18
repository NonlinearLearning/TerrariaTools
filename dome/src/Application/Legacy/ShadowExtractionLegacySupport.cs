namespace TerrariaTools.Dome.Application;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Legacy;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

// Runtime shadow extraction remains the explicit legacy compatibility boundary.
// Standard DomeApplication path must not depend on this helper.
internal static class ShadowExtractionLegacySupport
{
    private static readonly SymbolDisplayFormat TypeIdFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    internal static bool HasValidLoadResult(ApplicationAbstractions.WorkspaceLoadResult loadResult) =>
        loadResult.IsSuccess && loadResult.SourceSet != null && loadResult.Documents.Count > 0;

    internal static async Task<ShadowExtractionAnalysis> AnalyzeAsync(
        ApplicationAbstractions.IAnalysisEngine analysisEngine,
        ShadowExtractionInputResolution input,
        CancellationToken cancellationToken)
    {
        var sourceSet = input.LoadResult.SourceSet
            ?? throw new InvalidOperationException("Workspace load result did not contain any source documents.");
        var analysisResult = await analysisEngine.AnalyzeAsync(sourceSet, cancellationToken);
        var analysisContext = analysisResult.CreateContext();
        var seedNode = ResolveSeedNode(analysisContext, input.Request.SeedMemberName)
            ?? throw new InvalidOperationException($"Seed method '{input.Request.SeedMemberName}' was not found.");
        var documents = BuildAnalysisDocuments(sourceSet);
        return new ShadowExtractionAnalysis(input, analysisResult, analysisContext, seedNode, documents);
    }

    internal static StageResult<ShadowClosurePlan> BuildClosurePlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reachableMethods = analysis.AnalysisContext.MethodCalls.GetReachableMethods([analysis.SeedNode.MemberId]);
        var reachableProjectNodes = reachableMethods
            .Select(memberId => analysis.AnalysisContext.FunctionIndex.NodesByMemberId.TryGetValue(memberId.Value, out var node) ? node : null)
            .Where(static node => node != null)
            .Cast<ModelAnalysis.FunctionNodeRef>()
            .ToArray();
        var declarationDocumentMap = BuildDeclarationDocumentMap(analysis.Documents);
        var namespaceDocumentMap = BuildNamespaceDocumentMap(analysis.Documents);
        var symbolClosure = analysis.AnalysisContext.SymbolDependencies.GetForwardSlice(
            reachableProjectNodes.Select(static node => node.MemberId.Value).ToArray(),
            new ModelAnalysis.SymbolDependencyQueryOptions(
                AllowedEdgeKinds:
                [
                    ModelAnalysis.SymbolDependencyEdgeKind.ContainsType,
                    ModelAnalysis.SymbolDependencyEdgeKind.BaseType,
                    ModelAnalysis.SymbolDependencyEdgeKind.InterfaceImplementation,
                    ModelAnalysis.SymbolDependencyEdgeKind.ExplicitInterfaceImplementation,
                    ModelAnalysis.SymbolDependencyEdgeKind.Override,
                    ModelAnalysis.SymbolDependencyEdgeKind.ReturnType,
                    ModelAnalysis.SymbolDependencyEdgeKind.ParameterType,
                    ModelAnalysis.SymbolDependencyEdgeKind.FieldType,
                    ModelAnalysis.SymbolDependencyEdgeKind.PropertyType,
                    ModelAnalysis.SymbolDependencyEdgeKind.EventType,
                    ModelAnalysis.SymbolDependencyEdgeKind.ObjectCreation,
                    ModelAnalysis.SymbolDependencyEdgeKind.Conversion
                ],
                AllowedNodeKinds:
                [
                    ModelAnalysis.SymbolDependencyNodeKind.Type,
                    ModelAnalysis.SymbolDependencyNodeKind.Method,
                    ModelAnalysis.SymbolDependencyNodeKind.Property,
                    ModelAnalysis.SymbolDependencyNodeKind.Field,
                    ModelAnalysis.SymbolDependencyNodeKind.Event
                ]));
        var closureDocuments = symbolClosure.Nodes
            .Select(node => declarationDocumentMap.GetValueOrDefault(node.SymbolId))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>();
        var includedDocuments = reachableProjectNodes
            .Select(static node => node.DocumentPath)
            .Concat(closureDocuments)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var symbolClosureDocumentCount = includedDocuments.Length;
        includedDocuments = ExpandIncludedDocumentsByNamespaces(includedDocuments, analysis.Documents, namespaceDocumentMap);
        progressReporter.Report($"[tr-shadow] Reachable methods: {reachableMethods.Count}, included documents: {includedDocuments.Length}");
        progressReporter.Report($"[tr-shadow] Symbol closure documents: {symbolClosureDocumentCount}");
        progressReporter.Report($"[tr-shadow] Namespace closure added: {includedDocuments.Length - symbolClosureDocumentCount}");

        var memberIdsByDocument = reachableProjectNodes
            .GroupBy(static node => node.DocumentPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlySet<string>)group.Select(static node => node.MemberId.Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        return StageResult<ShadowClosurePlan>.Success(new ShadowClosurePlan(
            includedDocuments,
            reachableMethods,
            memberIdsByDocument,
            symbolClosureDocumentCount));
    }

    internal static async Task<StageResult<ShadowWorkspaceWriteResult>> WriteWorkspaceAsync(
        TerrariaRuntimeShadowProjectBuilder shadowProjectBuilder,
        TerrariaRuntimeShadowSourceRewriter shadowSourceRewriter,
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var rewrittenDocuments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preservedMemberCount = 0;
        var defaultedMemberCount = 0;
        var emptiedMemberCount = 0;
        var preservedMembers = new HashSet<string>(StringComparer.Ordinal);
        var defaultedMembers = new HashSet<string>(StringComparer.Ordinal);
        var emptiedMembers = new HashSet<string>(StringComparer.Ordinal);

        var rewrittenCount = 0;
        foreach (var document in analysis.Documents.Where(document => closurePlan.IncludedDocuments.Contains(document.Document.RelativePath, StringComparer.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preservedMemberIds = closurePlan.MemberIdsByDocument.GetValueOrDefault(document.Document.RelativePath)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var rewriteResult = shadowSourceRewriter.Rewrite(
                document.Document.SourceText,
                document.SemanticModel,
                preservedMemberIds);
            rewrittenDocuments[document.Document.RelativePath] = rewriteResult.RewrittenSource;
            preservedMemberCount += rewriteResult.Summary.PreservedMembers;
            defaultedMemberCount += rewriteResult.Summary.DefaultedMembers;
            emptiedMemberCount += rewriteResult.Summary.EmptiedMembers;
            preservedMembers.UnionWith(rewriteResult.Summary.SamplePreservedMembers);
            defaultedMembers.UnionWith(rewriteResult.Summary.SampleDefaultedMembers);
            emptiedMembers.UnionWith(rewriteResult.Summary.SampleEmptiedMembers);
            rewrittenCount++;

            if (rewrittenCount % 100 == 0 || rewrittenCount == closurePlan.IncludedDocuments.Count)
            {
                progressReporter.Report($"[tr-shadow] Shadow rewrite progress {rewrittenCount}/{closurePlan.IncludedDocuments.Count}");
            }
        }

        await shadowProjectBuilder.BuildAsync(input.Layout, rewrittenDocuments, progressReporter, cancellationToken);

        return StageResult<ShadowWorkspaceWriteResult>.Success(new ShadowWorkspaceWriteResult(
            rewrittenDocuments,
            new ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary(
                preservedMemberCount,
                defaultedMemberCount,
                emptiedMemberCount,
                preservedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                defaultedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                emptiedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray())));
    }

    private static ModelAnalysis.FunctionNodeRef? ResolveSeedNode(ModelAnalysis.AnalysisContext context, string seedMemberName)
    {
        if (context.FunctionIndex.NodesByMemberId.TryGetValue(seedMemberName, out var exactMatch))
        {
            return exactMatch;
        }

        return context.FunctionIndex.NodesByMemberId.Values
            .OrderBy(static node => node.MemberId.Value, StringComparer.Ordinal)
            .FirstOrDefault(node =>
                string.Equals(node.DisplayName, seedMemberName, StringComparison.Ordinal) ||
                node.MemberId.Value.StartsWith(seedMemberName + "(", StringComparison.Ordinal) ||
                string.Equals(node.MemberId.Value, seedMemberName, StringComparison.Ordinal));
    }

    private static IReadOnlyList<ShadowExtractionAnalysisDocument> BuildAnalysisDocuments(ApplicationAbstractions.SourceDocumentSet sourceSet)
    {
        var trees = sourceSet.Documents
            .Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath))
            .ToArray();

        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        ];

        var compilation = CSharpCompilation.Create(
            "Dome.ShadowExtractionAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return sourceSet.Documents
            .Select(document =>
            {
                var tree = compilation.SyntaxTrees.Single(candidate => string.Equals(
                    Path.GetFullPath(candidate.FilePath ?? string.Empty),
                    Path.GetFullPath(document.SourcePath),
                    StringComparison.OrdinalIgnoreCase));
                return new ShadowExtractionAnalysisDocument(
                    document,
                    (CompilationUnitSyntax)tree.GetRoot(),
                    compilation.GetSemanticModel(tree));
            })
            .ToArray();
    }

    private static Dictionary<string, string> BuildDeclarationDocumentMap(IReadOnlyList<ShadowExtractionAnalysisDocument> documents)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            IndexDeclaredSymbols(document.Root, document.SemanticModel, document.Document.RelativePath, map);
        }

        return map;
    }

    private static Dictionary<string, HashSet<string>> BuildNamespaceDocumentMap(IReadOnlyList<ShadowExtractionAnalysisDocument> documents)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var namespaceName in GetDeclaredNamespaces(document.Root, document.SemanticModel))
            {
                if (!map.TryGetValue(namespaceName, out var documentPaths))
                {
                    documentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[namespaceName] = documentPaths;
                }

                documentPaths.Add(document.Document.RelativePath);
            }
        }

        return map;
    }

    private static void IndexDeclaredSymbols(SyntaxNode root, SemanticModel semanticModel, string relativePath, Dictionary<string, string> map)
    {
        foreach (var typeDeclaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
            {
                map[BuildTypeId(typeSymbol)] = relativePath;
            }
        }

        foreach (var delegateDeclaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(delegateDeclaration) is INamedTypeSymbol delegateSymbol)
            {
                map[BuildTypeId(delegateSymbol)] = relativePath;
            }
        }

        foreach (var methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(methodDeclaration), relativePath, map);
        }

        foreach (var constructorDeclaration in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(constructorDeclaration), relativePath, map);
        }

        foreach (var propertyDeclaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(propertyDeclaration), relativePath, map);
        }

        foreach (var fieldDeclaration in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(fieldDeclaration), relativePath, map);
        }

        foreach (var eventDeclaration in root.DescendantNodes().OfType<EventDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(eventDeclaration), relativePath, map);
        }

        foreach (var eventFieldDeclaration in root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
        {
            foreach (var declarator in eventFieldDeclaration.Declaration.Variables)
            {
                AddMemberSymbolId(semanticModel.GetDeclaredSymbol(declarator), relativePath, map);
            }
        }

        foreach (var operatorDeclaration in root.DescendantNodes().OfType<OperatorDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(operatorDeclaration), relativePath, map);
        }

        foreach (var conversionDeclaration in root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>())
        {
            AddMemberSymbolId(semanticModel.GetDeclaredSymbol(conversionDeclaration), relativePath, map);
        }
    }

    private static void AddMemberSymbolId(ISymbol? symbol, string relativePath, Dictionary<string, string> map)
    {
        if (symbol == null)
        {
            return;
        }

        map[MetadataMemberIdBuilder.Build(symbol).Value] = relativePath;
    }

    private static string BuildTypeId(ITypeSymbol typeSymbol) => typeSymbol.ToDisplayString(TypeIdFormat);

    private static string[] ExpandIncludedDocumentsByNamespaces(
        IReadOnlyList<string> includedDocuments,
        IReadOnlyList<ShadowExtractionAnalysisDocument> documents,
        IReadOnlyDictionary<string, HashSet<string>> namespaceDocumentMap)
    {
        var included = new HashSet<string>(includedDocuments, StringComparer.OrdinalIgnoreCase);
        var documentsByPath = documents.ToDictionary(static document => document.Document.RelativePath, StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(includedDocuments);

        while (pending.Count > 0)
        {
            var documentPath = pending.Dequeue();
            if (!documentsByPath.TryGetValue(documentPath, out var document))
            {
                continue;
            }

            foreach (var namespaceName in GetImportedNamespaces(document.Root))
            {
                if (!namespaceDocumentMap.TryGetValue(namespaceName, out var namespaceDocuments))
                {
                    continue;
                }

                foreach (var namespaceDocument in namespaceDocuments)
                {
                    if (included.Add(namespaceDocument))
                    {
                        pending.Enqueue(namespaceDocument);
                    }
                }
            }
        }

        return included.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> GetDeclaredNamespaces(CompilationUnitSyntax root, SemanticModel semanticModel)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeDeclaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                namespaces.Add(namespaceName);
            }
        }

        foreach (var delegateDeclaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(delegateDeclaration) is not INamedTypeSymbol delegateSymbol)
            {
                continue;
            }

            var namespaceName = delegateSymbol.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                namespaces.Add(namespaceName);
            }
        }

        return namespaces;
    }

    private static IEnumerable<string> GetImportedNamespaces(CompilationUnitSyntax root)
    {
        foreach (var usingDirective in root.Usings)
        {
            var namespaceName = usingDirective.Name?.ToString();
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                yield return namespaceName;
            }
        }
    }
}
