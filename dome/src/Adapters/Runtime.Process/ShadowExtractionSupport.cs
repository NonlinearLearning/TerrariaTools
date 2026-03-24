namespace TerrariaTools.Dome.Adapters.Runtime.Process;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Application.UseCases.ShadowExtraction;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

internal static class ShadowExtractionSupport
{
    private sealed record ShadowRewriteDocument(
        CoreAnalysis.SourceDocument Document,
        SemanticModel SemanticModel);

    internal static bool HasValidLoadResult(ApplicationAbstractions.WorkspaceLoadResult loadResult) =>
        loadResult.IsSuccess && loadResult.Input != null && loadResult.Documents.Count > 0;

    internal static async Task<ShadowExtractionAnalysis> AnalyzeAsync(
        ApplicationAbstractions.IAnalysisEngine analysisEngine,
        ShadowExtractionInputResolution input,
        CancellationToken cancellationToken)
    {
        var analysisInput = input.LoadResult.Input
            ?? throw new InvalidOperationException("Workspace load result did not contain analysis input.");
        var analysisResult = await analysisEngine.AnalyzeAsync(analysisInput, cancellationToken);
        return new ShadowExtractionAnalysis(input, analysisResult);
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
        var documents = await BuildAnalysisDocumentsAsync(
            input.LoadResult.Input ?? throw new InvalidOperationException("Workspace load result did not contain analysis input."),
            cancellationToken);

        foreach (var document in documents.Where(document => closurePlan.IncludedDocuments.Contains(document.Document.RelativePath, StringComparer.OrdinalIgnoreCase)))
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

    private static async Task<IReadOnlyList<ShadowRewriteDocument>> BuildAnalysisDocumentsAsync(
        CoreAnalysis.AnalysisInput input,
        CancellationToken cancellationToken)
    {
        var sourceSet = input.SourceSet;
        var workspaceSemanticModels = await WorkspaceSemanticModelProvider.TryLoadAsync(input, cancellationToken);
        var fallbackSemanticModels = workspaceSemanticModels.Count == sourceSet.Documents.Count
            ? null
            : BuildSyntheticSemanticModels(sourceSet);

        return sourceSet.Documents
            .Select(document =>
            {
                var documentPath = Path.GetFullPath(document.SourcePath);
                if (workspaceSemanticModels.TryGetValue(documentPath, out var semanticModel))
                {
                    return new ShadowRewriteDocument(document, semanticModel);
                }

                if (fallbackSemanticModels != null && fallbackSemanticModels.TryGetValue(documentPath, out semanticModel))
                {
                    return new ShadowRewriteDocument(document, semanticModel);
                }

                throw new InvalidOperationException($"No semantic model was available for shadow rewrite document '{document.SourcePath}'.");
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, SemanticModel> BuildSyntheticSemanticModels(CoreAnalysis.SourceDocumentSet sourceSet)
    {
        var trees = sourceSet.Documents
            .Select(document => CSharpSyntaxTree.ParseText(document.SourceText, path: document.SourcePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "Dome.ShadowExtractionAnalysis",
            trees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return sourceSet.Documents
            .Select(document =>
            {
                var tree = compilation.SyntaxTrees.Single(candidate => string.Equals(
                    Path.GetFullPath(candidate.FilePath ?? string.Empty),
                    Path.GetFullPath(document.SourcePath),
                    StringComparison.OrdinalIgnoreCase));
                return new KeyValuePair<string, SemanticModel>(
                    Path.GetFullPath(document.SourcePath),
                    compilation.GetSemanticModel(tree));
            })
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            var references = trustedPlatformAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .ToArray();
            if (references.Length > 0)
            {
                return references;
            }
        }

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
        ];
    }
}




