using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

// 通过 Roslyn 加载解决方案和项目，
// 并将结果整理为应用流水线可消费的工作区契约。
public sealed class CodeAnalysisWorkspaceLoader : ApplicationAbstractions.IWorkspaceLoader
{
    private static int _locatorInitialized;

    internal static MSBuildWorkspace CreateWorkspace()
    {
        var hostServices = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        return MSBuildWorkspace.Create(hostServices);
    }

    public async Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureMsBuildRegistered();

            var diagnostics = new List<ApplicationAbstractions.WorkspaceLoadDiagnostic>();
            using var workspace = CreateWorkspace();
            workspace.WorkspaceFailed += (_, args) =>
            {
                diagnostics.Add(new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                    "CodeAnalysisWorkspace",
                    args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                        ? ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error
                        : ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Warning,
                    args.Diagnostic.Message));
            };

            if (string.Equals(Path.GetExtension(inputPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await workspace.OpenSolutionAsync(inputPath, cancellationToken: cancellationToken);
                return await BuildResultFromSolutionAsync(
                    solution,
                    Path.GetDirectoryName(Path.GetFullPath(inputPath))!,
                    diagnostics,
                    cancellationToken);
            }

            if (string.Equals(Path.GetExtension(inputPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(inputPath, cancellationToken: cancellationToken);
                return await BuildResultFromProjectAsync(
                    project,
                    Path.GetDirectoryName(Path.GetFullPath(inputPath))!,
                    diagnostics,
                    cancellationToken);
            }

            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                [
                    new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                        "CodeAnalysisLoad",
                        ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                        $"Input path '{inputPath}' is not a .sln or .csproj file.")
                ]);
        }
        catch (Exception ex)
        {
            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                [
                    new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                        "CodeAnalysisLoad",
                        ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                        ex.Message)
                ]);
        }
    }

    internal static void EnsureMsBuildRegistered()
    {
        if (Interlocked.Exchange(ref _locatorInitialized, 1) == 1)
        {
            return;
        }

        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
        if (instances.Length == 0)
        {
            throw new InvalidOperationException("No MSBuild instance was found for CodeAnalysis workspace loading.");
        }

        MSBuildLocator.RegisterInstance(instances.OrderByDescending(instance => instance.Version).First());
    }

    private static async Task<ApplicationAbstractions.WorkspaceLoadResult> BuildResultFromSolutionAsync(
        Solution solution,
        string rootPath,
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var documents = new List<ModelAnalysis.SourceDocument>();
        var solutionDiagnostics = new List<ApplicationAbstractions.WorkspaceLoadDiagnostic>(diagnostics);
        foreach (var project in solution.Projects)
        {
            var projectResult = await BuildProjectDocumentsAsync(project, rootPath, cancellationToken);
            documents.AddRange(projectResult.Documents);
            solutionDiagnostics.AddRange(projectResult.Diagnostics);
        }

        return BuildSuccessResult(inputPath: solution.FilePath ?? rootPath, rootPath, documents, diagnostics: BuildDiagnostics(solutionDiagnostics, solution.FilePath, documents.Count));
    }

    private static async Task<ApplicationAbstractions.WorkspaceLoadResult> BuildResultFromProjectAsync(
        Project project,
        string rootPath,
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var projectResult = await BuildProjectDocumentsAsync(project, rootPath, cancellationToken);
        return BuildSuccessResult(
            inputPath: project.FilePath ?? rootPath,
            rootPath,
            projectResult.Documents,
            diagnostics: BuildDiagnostics(diagnostics.Concat(projectResult.Diagnostics).ToArray(), project.FilePath, projectResult.Documents.Count));
    }

    private static async Task<ProjectDocumentLoadResult> BuildProjectDocumentsAsync(
        Project project,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ApplicationAbstractions.WorkspaceLoadDiagnostic>();
        if (!string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            diagnostics.Add(new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Info,
                $"Project '{project.Name}' was skipped because its language is '{project.Language}', not C#."));
            return new ProjectDocumentLoadResult(Array.Empty<ModelAnalysis.SourceDocument>(), diagnostics);
        }

        var projectPath = project.FilePath ?? project.Name;
        var candidateDocuments = project.Documents
            .Where(static d => string.Equals(Path.GetExtension(d.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var totalDocumentCount = project.Documents.Count();

        if (candidateDocuments.Length == 0)
        {
            diagnostics.Add(new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') did not expose any .cs documents to CodeAnalysis. Total Roslyn documents: {totalDocumentCount}."));
            return new ProjectDocumentLoadResult(Array.Empty<ModelAnalysis.SourceDocument>(), diagnostics);
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
        {
            diagnostics.Add(new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') returned null compilation with {candidateDocuments.Length} candidate .cs documents and {totalDocumentCount} total Roslyn documents."));
            return new ProjectDocumentLoadResult(Array.Empty<ModelAnalysis.SourceDocument>(), diagnostics);
        }

        var tasks = candidateDocuments
            .Select(document => BuildDocumentAsync(document, rootPath, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var documents = results.Where(static context => context != null).Cast<ModelAnalysis.SourceDocument>().ToArray();
        if (documents.Length != candidateDocuments.Length)
        {
            diagnostics.Add(new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') produced {documents.Length} workspace documents from {candidateDocuments.Length} candidate .cs documents. Some documents were skipped because file path or syntax prerequisites were unavailable."));
        }

        return new ProjectDocumentLoadResult(documents, diagnostics);
    }

    private static IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> BuildDiagnostics(
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> diagnostics,
        string? inputPath,
        int documentCount)
    {
        if (documentCount > 0 || diagnostics.Count > 0)
        {
            return diagnostics;
        }

        return
        [
            new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                "CodeAnalysisLoad",
                ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Warning,
                $"CodeAnalysis opened '{inputPath ?? "<unknown>"}' but produced zero C# documents.")
        ];
    }

    private static async Task<ModelAnalysis.SourceDocument?> BuildDocumentAsync(
        Document document,
        string rootPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (document.FilePath == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root is not CompilationUnitSyntax)
        {
            return null;
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        return new ModelAnalysis.SourceDocument(
            Path.GetFullPath(document.FilePath),
            Path.GetRelativePath(rootPath, document.FilePath),
            sourceText.ToString());
    }

    private static ApplicationAbstractions.WorkspaceLoadResult BuildSuccessResult(
        string inputPath,
        string rootPath,
        IReadOnlyList<ModelAnalysis.SourceDocument> documents,
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> diagnostics)
    {
        return ApplicationAbstractions.WorkspaceLoadResult.Success(
            new ModelAnalysis.AnalysisInput(
                new ModelAnalysis.SourceDocumentSet(inputPath, rootPath, documents),
                ModelAnalysis.AnalysisInputMode.Workspace,
                new ModelAnalysis.AnalysisEnvironmentInfo("CodeAnalysis", ProjectPath: inputPath, SolutionPath: inputPath, RequestedPrimaryLoader: "CodeAnalysis")),
            ModelPrimitives.WorkspaceLoadMode.CodeAnalysis,
            "CodeAnalysis",
            diagnostics: diagnostics);
    }

    private sealed record ProjectDocumentLoadResult(
        IReadOnlyList<ModelAnalysis.SourceDocument> Documents,
        IReadOnlyList<ApplicationAbstractions.WorkspaceLoadDiagnostic> Diagnostics);
}

public static class WorkspaceSemanticModelProvider
{
    // Shadow extraction prefers semantic models from the real workspace so
    // rewrite decisions track project references and compilation settings.
    public static async Task<IReadOnlyDictionary<string, SemanticModel>> TryLoadAsync(
        ModelAnalysis.AnalysisInput input,
        CancellationToken cancellationToken)
    {
        if (input.InputMode != ModelAnalysis.AnalysisInputMode.Workspace)
        {
            return new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);
        }

        var workspacePath = ResolveWorkspacePath(input);
        if (string.IsNullOrWhiteSpace(workspacePath) || !File.Exists(workspacePath))
        {
            return new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            CodeAnalysisWorkspaceLoader.EnsureMsBuildRegistered();

            using var workspace = CodeAnalysisWorkspaceLoader.CreateWorkspace();
            if (string.Equals(Path.GetExtension(workspacePath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await workspace.OpenSolutionAsync(workspacePath, cancellationToken: cancellationToken);
                return await BuildSemanticModelsAsync(solution.Projects, cancellationToken);
            }

            if (string.Equals(Path.GetExtension(workspacePath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(workspacePath, cancellationToken: cancellationToken);
                return await BuildSemanticModelsAsync([project], cancellationToken);
            }
        }
        catch
        {
            return new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveWorkspacePath(ModelAnalysis.AnalysisInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Environment?.ProjectPath))
        {
            return Path.GetFullPath(input.Environment.ProjectPath);
        }

        if (!string.IsNullOrWhiteSpace(input.Environment?.SolutionPath))
        {
            return Path.GetFullPath(input.Environment.SolutionPath);
        }

        if (!string.IsNullOrWhiteSpace(input.SourceSet.EntryPath) &&
            (string.Equals(Path.GetExtension(input.SourceSet.EntryPath), ".sln", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetExtension(input.SourceSet.EntryPath), ".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            return Path.GetFullPath(input.SourceSet.EntryPath);
        }

        return null;
    }

    private static async Task<IReadOnlyDictionary<string, SemanticModel>> BuildSemanticModelsAsync(
        IEnumerable<Project> projects,
        CancellationToken cancellationToken)
    {
        var semanticModels = new Dictionary<string, SemanticModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects.Where(static project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null)
            {
                continue;
            }

            foreach (var document in project.Documents.Where(static document =>
                         string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (document.FilePath == null)
                {
                    continue;
                }

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                if (syntaxTree == null)
                {
                    continue;
                }

                semanticModels[Path.GetFullPath(document.FilePath)] = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            }
        }

        return semanticModels;
    }
}

public sealed class WorkspaceLoadCoordinator : ApplicationAbstractions.IWorkspaceLoader
{
    private readonly ApplicationAbstractions.IWorkspaceLoader _codeAnalysisLoader;
    private readonly ApplicationAbstractions.IWorkspaceLoader _sourceOnlyLoader;

    public WorkspaceLoadCoordinator(
        ApplicationAbstractions.IWorkspaceLoader codeAnalysisLoader,
        ApplicationAbstractions.IWorkspaceLoader sourceOnlyLoader)
    {
        _codeAnalysisLoader = codeAnalysisLoader;
        _sourceOnlyLoader = sourceOnlyLoader;
    }

    public async Task<ApplicationAbstractions.WorkspaceLoadResult> LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(inputPath);
        var extension = Path.GetExtension(fullPath);

        if (options.PreferredLoader == ApplicationAbstractions.WorkspaceLoaderPreference.SourceOnly)
        {
            return await _sourceOnlyLoader.LoadAsync(GetSourceOnlyRoot(fullPath), options, cancellationToken);
        }

        if (Directory.Exists(fullPath) || string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return await _sourceOnlyLoader.LoadAsync(fullPath, options, cancellationToken);
        }

        if (!File.Exists(fullPath))
        {
            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                "WorkspaceLoadCoordinator",
                [
                    new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                        "WorkspaceLoad",
                        ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                        $"Input path '{inputPath}' was not found.")
                ]);
        }

        if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                "WorkspaceLoadCoordinator",
                [
                    new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                        "WorkspaceLoad",
                        ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                        $"Unsupported input path '{inputPath}'.")
                ]);
        }

        var codeAnalysisResult = await _codeAnalysisLoader.LoadAsync(
            fullPath,
            new ApplicationAbstractions.WorkspaceLoadOptions(ApplicationAbstractions.WorkspaceLoaderPreference.CodeAnalysisFirst, options.AllowFallbackToSourceOnly),
            cancellationToken);

        if (codeAnalysisResult.IsSuccess && codeAnalysisResult.Documents.Count > 0)
        {
            return codeAnalysisResult;
        }

        if (codeAnalysisResult.IsSuccess && codeAnalysisResult.Documents.Count == 0 && !options.AllowFallbackToSourceOnly)
        {
            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                codeAnalysisResult.Diagnostics.Count > 0
                    ? codeAnalysisResult.Diagnostics
                    :
                    [
                        new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                            "CodeAnalysisLoad",
                            ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                            $"CodeAnalysis loaded '{fullPath}' but did not produce any C# documents.")
                    ]);
        }

        if (!options.AllowFallbackToSourceOnly)
        {
            return codeAnalysisResult;
        }

        var sourceRoot = GetSourceOnlyRoot(fullPath);
        var sourceResult = await _sourceOnlyLoader.LoadAsync(
            sourceRoot,
            new ApplicationAbstractions.WorkspaceLoadOptions(ApplicationAbstractions.WorkspaceLoaderPreference.SourceOnly, true),
            cancellationToken);

        if (!sourceResult.IsSuccess)
        {
            return ApplicationAbstractions.WorkspaceLoadResult.Failure(
                ModelPrimitives.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
                "CodeAnalysis",
                codeAnalysisResult.Diagnostics.Concat(sourceResult.Diagnostics).ToArray());
        }

        return ApplicationAbstractions.WorkspaceLoadResult.Success(
            sourceResult.Input!,
            ModelPrimitives.WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
            "CodeAnalysis",
            true,
            codeAnalysisResult.Diagnostics.Concat(sourceResult.Diagnostics).ToArray());
    }

    private static string GetSourceOnlyRoot(string inputPath) =>
        Directory.Exists(inputPath) || string.Equals(Path.GetExtension(inputPath), ".cs", StringComparison.OrdinalIgnoreCase)
            ? inputPath
            : Path.GetDirectoryName(inputPath) ?? inputPath;
}




