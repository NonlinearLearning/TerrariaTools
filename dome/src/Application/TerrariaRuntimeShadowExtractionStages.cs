namespace TerrariaTools.Dome.Application;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;

public sealed class ShadowExtractionInputResolver(IWorkspaceLoader workspaceLoader) : IShadowExtractionInputResolver
{
    public async Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var layout = TerrariaRuntimeShadowLayout.Create(request);
        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);

        progressReporter.Report($"[tr-shadow] å¯®â‚¬æ¿®å¬ªå§žæžè—‰ä¼æµ£æ»ƒå°¯é”›æ­¿{request.SolutionPath}");
        var loadResult = await workspaceLoader.LoadAsync(request.SolutionPath, WorkspaceLoadOptions.Default, cancellationToken);
        if (!loadResult.IsSuccess || loadResult.AnalysisInput == null || loadResult.Documents.Count == 0)
        {
            var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found.";
            return StageResult<ShadowExtractionInputResolution>.Failure(FailureCode.WorkspaceLoadFailed, message);
        }

        progressReporter.Report($"[tr-shadow] å®¸ãƒ¤ç¶”é–å“„å§žæžè—‰ç•¬éŽ´æ„¶ç´°é?{loadResult.Documents.Count} æ¶“?C# é‚å›¨ã€‚éŠ†?");
        return StageResult<ShadowExtractionInputResolution>.Success(new ShadowExtractionInputResolution(request, layout, loadResult));
    }
}

public sealed class ShadowExtractionAnalysisStage(IAnalysisEngine analysisEngine) : IShadowExtractionAnalysisStage
{
    public async Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-shadow] å¯®â‚¬æ¿®å¬«å¢½ç›?Roslyn é’å—˜ç€½...");
        var analysisResult = await analysisEngine.AnalyzeAsync(input.LoadResult.AnalysisInput!, cancellationToken);
        var analysisContext = analysisResult.CreateContext();

        var seedNode = ResolveSeedNode(analysisContext, input.Request.SeedMemberName);
        if (seedNode == null)
        {
            return StageResult<ShadowExtractionAnalysis>.Failure(
                FailureCode.AnalysisFailed,
                $"Seed method '{input.Request.SeedMemberName}' was not found.");
        }

        progressReporter.Report($"[tr-shadow] Seed å®¸æ’ç•¾æµ£å¶ç´°{seedNode.MemberId.Value}");
        return StageResult<ShadowExtractionAnalysis>.Success(new ShadowExtractionAnalysis(input, analysisResult, analysisContext, seedNode));
    }

    private static FunctionNodeRef? ResolveSeedNode(AnalysisContext context, string seedMemberName)
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
}

public sealed class ShadowClosurePlanner : IShadowClosurePlanner
{
    private static readonly SymbolDisplayFormat TypeIdFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    public StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reachableMethods = analysis.AnalysisContext.MethodCalls.GetReachableMethods([analysis.SeedNode.MemberId]);
        var reachableProjectNodes = reachableMethods
            .Select(memberId => analysis.AnalysisContext.FunctionIndex.NodesByMemberId.TryGetValue(memberId.Value, out var node) ? node : null)
            .Where(static node => node != null)
            .Cast<FunctionNodeRef>()
            .ToArray();
        var declarationDocumentMap = BuildDeclarationDocumentMap(analysis.AnalysisResult.Documents);
        var namespaceDocumentMap = BuildNamespaceDocumentMap(analysis.AnalysisResult.Documents);
        var symbolClosure = analysis.AnalysisContext.SymbolDependencies.GetForwardSlice(
            reachableProjectNodes.Select(static node => node.MemberId.Value).ToArray(),
            new SymbolDependencyQueryOptions(
                AllowedEdgeKinds:
                [
                    SymbolDependencyEdgeKind.ContainsType,
                    SymbolDependencyEdgeKind.BaseType,
                    SymbolDependencyEdgeKind.InterfaceImplementation,
                    SymbolDependencyEdgeKind.ExplicitInterfaceImplementation,
                    SymbolDependencyEdgeKind.Override,
                    SymbolDependencyEdgeKind.ReturnType,
                    SymbolDependencyEdgeKind.ParameterType,
                    SymbolDependencyEdgeKind.FieldType,
                    SymbolDependencyEdgeKind.PropertyType,
                    SymbolDependencyEdgeKind.EventType,
                    SymbolDependencyEdgeKind.ObjectCreation,
                    SymbolDependencyEdgeKind.Conversion
                ],
                AllowedNodeKinds:
                [
                    SymbolDependencyNodeKind.Type,
                    SymbolDependencyNodeKind.Method,
                    SymbolDependencyNodeKind.Property,
                    SymbolDependencyNodeKind.Field,
                    SymbolDependencyNodeKind.Event
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
        includedDocuments = ExpandIncludedDocumentsByNamespaces(includedDocuments, analysis.AnalysisResult.Documents, namespaceDocumentMap);
        progressReporter.Report($"[tr-shadow] é™îˆæªé‚è§„ç¡¶ç¼ç†»î…¸ç€¹å±¾åžšé”›æ°¬å¡ {reachableMethods.Count} æ¶“î…æŸŸå¨‰æ›ªç´å¨‘å¤Šå¼· {includedDocuments.Length} æ¶“î…æžƒå¦—ï½ƒâ‚¬?");
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

    private static Dictionary<string, string> BuildDeclarationDocumentMap(IReadOnlyList<AnalysisDocumentContext> documents)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            IndexDeclaredSymbols(document.Root, document.SemanticModel, document.Document.RelativePath, map);
        }

        return map;
    }

    private static Dictionary<string, HashSet<string>> BuildNamespaceDocumentMap(IReadOnlyList<AnalysisDocumentContext> documents)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var namespaceName in GetDeclaredNamespaces((CompilationUnitSyntax)document.Root, document.SemanticModel))
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
        IReadOnlyList<AnalysisDocumentContext> documents,
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

            foreach (var namespaceName in GetImportedNamespaces((CompilationUnitSyntax)document.Root))
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

public sealed class ShadowWorkspaceWriter(
    TerrariaRuntimeShadowProjectBuilder shadowProjectBuilder,
    TerrariaRuntimeShadowSourceRewriter shadowSourceRewriter) : IShadowWorkspaceWriter
{
    public async Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
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

        progressReporter.Report("[tr-shadow] å¯®â‚¬æ¿®å¬¬æ•“éŽ´æ„­åžšé›æ¨¼éª‡ shadow å©§æ„®çˆœ...");
        var rewrittenCount = 0;
        foreach (var document in analysis.AnalysisResult.Documents.Where(document => closurePlan.IncludedDocuments.Contains(document.Document.RelativePath, StringComparer.OrdinalIgnoreCase)))
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
                progressReporter.Report($"[tr-shadow] Shadow å©§æ„®çˆœé¢ç†¸åžšæ©æ¶˜å®³é”›æ­¿{rewrittenCount}/{closurePlan.IncludedDocuments.Count}éŠ†?");
            }
        }

        progressReporter.Report("[tr-shadow] å¯®â‚¬æ¿®å¬ªå•“é?shadow å®¸ãƒ¤ç¶”é–?..");
        await shadowProjectBuilder.BuildAsync(input.Layout, rewrittenDocuments, progressReporter, cancellationToken);

        return StageResult<ShadowWorkspaceWriteResult>.Success(new ShadowWorkspaceWriteResult(
            rewrittenDocuments,
            new TerrariaRuntimeShadowRewriteSummary(
                preservedMemberCount,
                defaultedMemberCount,
                emptiedMemberCount,
                preservedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                defaultedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
                emptiedMembers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray())));
    }
}

public sealed class ShadowExtractionReportBuilder : IShadowExtractionReportBuilder
{
    public TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        return new TerrariaRuntimeShadowExtractionReport(
            input.Request.SeedMemberName,
            analysis.SeedNode.MemberId.Value,
            closurePlan.IncludedDocuments,
            closurePlan.ReachableMethods.Select(static memberId => memberId.Value).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            analysis.AnalysisContext.AdvancedAnalysis.BuildSummary(),
            workspaceWriteResult.RewrittenDocuments.Count,
            workspaceWriteResult.RewriteSummary);
    }
}
