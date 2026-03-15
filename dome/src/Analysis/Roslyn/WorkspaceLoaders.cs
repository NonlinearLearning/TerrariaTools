namespace TerrariaTools.Dome.Analysis.Roslyn;

using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using TerrariaTools.Dome.Core;

/// <summary>
/// 工作区加载器接口。
/// </summary>
public interface IWorkspaceLoader
{
    /// <summary>
    /// 异步加载工作区。
    /// </summary>
    /// <param name="inputPath">输入路径（.sln 或 .csproj）。</param>
    /// <param name="options">加载选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载结果。</returns>
    Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// 基于 Roslyn CodeAnalysis 的工作区加载器。
/// </summary>
public sealed class CodeAnalysisWorkspaceLoader : IWorkspaceLoader
{
    private static int _locatorInitialized;

    /// <summary>
    /// 创建 MSBuild 工作区实例。
    /// </summary>
    /// <returns>MSBuild 工作区。</returns>
    internal static MSBuildWorkspace CreateWorkspace()
    {
        var hostServices = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        return MSBuildWorkspace.Create(hostServices);
    }

    /// <inheritdoc />
    public async Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
    {
        try
        {
            EnsureMsBuildRegistered();

            var diagnostics = new List<WorkspaceLoadDiagnostic>();
            using var workspace = CreateWorkspace();
            workspace.WorkspaceFailed += (_, args) =>
            {
                diagnostics.Add(new WorkspaceLoadDiagnostic(
                    "CodeAnalysisWorkspace",
                    args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                        ? WorkspaceLoadDiagnosticSeverity.Error
                        : WorkspaceLoadDiagnosticSeverity.Warning,
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

            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                new[]
                {
                    new WorkspaceLoadDiagnostic(
                        "CodeAnalysisLoad",
                        WorkspaceLoadDiagnosticSeverity.Error,
                        $"Input path '{inputPath}' is not a .sln or .csproj file.")
                });
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                new[]
                {
                    new WorkspaceLoadDiagnostic(
                        "CodeAnalysisLoad",
                        WorkspaceLoadDiagnosticSeverity.Error,
                        ex.Message)
                });
        }
    }

    /// <summary>
    /// 确保 MSBuildLocator 已完成注册。
    /// </summary>
    private static void EnsureMsBuildRegistered()
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

    /// <summary>
    /// 基于解决方案构建工作区加载结果。
    /// </summary>
    private static async Task<WorkspaceLoadResult> BuildResultFromSolutionAsync(
        Solution solution,
        string rootPath,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var documents = new List<WorkspaceAnalysisDocumentContext>();
        var solutionDiagnostics = new List<WorkspaceLoadDiagnostic>(diagnostics);
        foreach (var project in solution.Projects)
        {
            var projectResult = await BuildProjectDocumentsAsync(project, rootPath, cancellationToken);
            documents.AddRange(projectResult.Documents);
            solutionDiagnostics.AddRange(projectResult.Diagnostics);
        }

        return WorkspaceLoadResult.Success(
            new WorkspaceAnalysisContextInput(solution, null, rootPath, documents),
            WorkspaceLoadMode.CodeAnalysis,
            "CodeAnalysis",
            diagnostics: BuildDiagnostics(solutionDiagnostics, solution.FilePath, documents.Count));
    }

    /// <summary>
    /// 基于项目构建工作区加载结果。
    /// </summary>
    private static async Task<WorkspaceLoadResult> BuildResultFromProjectAsync(
        Project project,
        string rootPath,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var projectResult = await BuildProjectDocumentsAsync(project, rootPath, cancellationToken);
        return WorkspaceLoadResult.Success(
            new WorkspaceAnalysisContextInput(project.Solution, project, rootPath, projectResult.Documents),
            WorkspaceLoadMode.CodeAnalysis,
            "CodeAnalysis",
            diagnostics: BuildDiagnostics(diagnostics.Concat(projectResult.Diagnostics).ToArray(), project.FilePath, projectResult.Documents.Count));
    }

    /// <summary>
    /// 构建项目内文档上下文集合。
    /// </summary>
    private static async Task<ProjectDocumentLoadResult> BuildProjectDocumentsAsync(
        Project project,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<WorkspaceLoadDiagnostic>();
        if (!string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            diagnostics.Add(new WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                WorkspaceLoadDiagnosticSeverity.Info,
                $"Project '{project.Name}' was skipped because its language is '{project.Language}', not C#."));
            return new ProjectDocumentLoadResult(Array.Empty<WorkspaceAnalysisDocumentContext>(), diagnostics);
        }

        var projectPath = project.FilePath ?? project.Name;
        var candidateDocuments = project.Documents
            .Where(static d => string.Equals(Path.GetExtension(d.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var totalDocumentCount = project.Documents.Count();

        if (candidateDocuments.Length == 0)
        {
            diagnostics.Add(new WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') did not expose any .cs documents to CodeAnalysis. Total Roslyn documents: {totalDocumentCount}."));
            return new ProjectDocumentLoadResult(Array.Empty<WorkspaceAnalysisDocumentContext>(), diagnostics);
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
        {
            diagnostics.Add(new WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') returned null compilation with {candidateDocuments.Length} candidate .cs documents and {totalDocumentCount} total Roslyn documents."));
            return new ProjectDocumentLoadResult(Array.Empty<WorkspaceAnalysisDocumentContext>(), diagnostics);
        }

        var tasks = candidateDocuments
            .Select(document => BuildDocumentContextAsync(document, compilation, rootPath, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var documents = results.Where(static context => context != null).Cast<WorkspaceAnalysisDocumentContext>().ToArray();
        if (documents.Length != candidateDocuments.Length)
        {
            diagnostics.Add(new WorkspaceLoadDiagnostic(
                "CodeAnalysisProject",
                WorkspaceLoadDiagnosticSeverity.Warning,
                $"Project '{project.Name}' ('{projectPath}') produced {documents.Length} workspace documents from {candidateDocuments.Length} candidate .cs documents. Some documents were skipped because file path, syntax root, or semantic model prerequisites were unavailable."));
        }

        return new ProjectDocumentLoadResult(documents, diagnostics);
    }

    /// <summary>
    /// 构建工作区加载诊断列表。
    /// </summary>
    private static IReadOnlyList<WorkspaceLoadDiagnostic> BuildDiagnostics(
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics,
        string? inputPath,
        int documentCount)
    {
        if (documentCount > 0 || diagnostics.Count > 0)
        {
            return diagnostics;
        }

        return new[]
        {
            new WorkspaceLoadDiagnostic(
                "CodeAnalysisLoad",
                WorkspaceLoadDiagnosticSeverity.Warning,
                $"CodeAnalysis opened '{inputPath ?? "<unknown>"}' but produced zero C# documents.")
        };
    }

    /// <summary>
    /// 构建单文档工作区上下文。
    /// </summary>
    private static async Task<WorkspaceAnalysisDocumentContext?> BuildDocumentContextAsync(
        Document document,
        Compilation compilation,
        string rootPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (document.FilePath == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root?.SyntaxTree == null)
        {
            return null;
        }

        var sourceText = await document.GetTextAsync(cancellationToken);
        var sourceDocument = new SourceDocument(
            Path.GetFullPath(document.FilePath),
            Path.GetRelativePath(rootPath, document.FilePath),
            sourceText.ToString());
        var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);

        return new WorkspaceAnalysisDocumentContext(
            document,
            sourceDocument,
            compilation,
            semanticModel,
            (CompilationUnitSyntax)root);
    }

    /// <summary>
    /// 项目文档加载结果。
    /// </summary>
    private sealed record ProjectDocumentLoadResult(
        IReadOnlyList<WorkspaceAnalysisDocumentContext> Documents,
        IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics);
}

/// <summary>
/// 工作区加载协调器。
/// 负责协调 CodeAnalysis 加载器和 SourceOnly 加载器，支持回退策略。
/// </summary>
public sealed class WorkspaceLoadCoordinator : IWorkspaceLoader
{
    private readonly IWorkspaceLoader _codeAnalysisLoader;
    private readonly IWorkspaceLoader _sourceOnlyLoader;

    /// <summary>
    /// 初始化工作区加载协调器。
    /// </summary>
    /// <param name="codeAnalysisLoader">CodeAnalysis 加载器。</param>
    /// <param name="sourceOnlyLoader">SourceOnly 加载器。</param>
    public WorkspaceLoadCoordinator(IWorkspaceLoader codeAnalysisLoader, IWorkspaceLoader sourceOnlyLoader)
    {
        _codeAnalysisLoader = codeAnalysisLoader;
        _sourceOnlyLoader = sourceOnlyLoader;
    }

    /// <inheritdoc />
    public async Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(inputPath);
        var extension = Path.GetExtension(fullPath);

        if (options.PreferredLoader == WorkspaceLoaderPreference.SourceOnly)
        {
            return await _sourceOnlyLoader.LoadAsync(
                GetSourceOnlyRoot(fullPath),
                new WorkspaceLoadOptions(WorkspaceLoaderPreference.SourceOnly, options.AllowFallbackToSourceOnly),
                cancellationToken);
        }

        if (Directory.Exists(fullPath) || string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return await _sourceOnlyLoader.LoadAsync(fullPath, options, cancellationToken);
        }

        if (!File.Exists(fullPath))
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.SourceOnly,
                "WorkspaceLoadCoordinator",
                new[]
                {
                    new WorkspaceLoadDiagnostic(
                        "WorkspaceLoad",
                        WorkspaceLoadDiagnosticSeverity.Error,
                        $"Input path '{inputPath}' was not found.")
                });
        }

        if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.SourceOnly,
                "WorkspaceLoadCoordinator",
                new[]
                {
                    new WorkspaceLoadDiagnostic(
                        "WorkspaceLoad",
                        WorkspaceLoadDiagnosticSeverity.Error,
                        $"Unsupported input path '{inputPath}'.")
                });
        }

        var codeAnalysisResult = await _codeAnalysisLoader.LoadAsync(
            fullPath,
            new WorkspaceLoadOptions(WorkspaceLoaderPreference.CodeAnalysisFirst, options.AllowFallbackToSourceOnly),
            cancellationToken);

        if (codeAnalysisResult.IsSuccess && codeAnalysisResult.Documents.Count > 0)
        {
            return codeAnalysisResult;
        }

        if (codeAnalysisResult.IsSuccess && codeAnalysisResult.Documents.Count == 0 && !options.AllowFallbackToSourceOnly)
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysis,
                "CodeAnalysis",
                codeAnalysisResult.Diagnostics.Count > 0
                    ? codeAnalysisResult.Diagnostics
                    : new[]
                    {
                        new WorkspaceLoadDiagnostic(
                            "CodeAnalysisLoad",
                            WorkspaceLoadDiagnosticSeverity.Error,
                            $"CodeAnalysis loaded '{fullPath}' but did not produce any C# documents.")
                    });
        }

        if (!options.AllowFallbackToSourceOnly)
        {
            return codeAnalysisResult;
        }

        var sourceRoot = GetSourceOnlyRoot(fullPath);
        var sourceResult = await _sourceOnlyLoader.LoadAsync(
            sourceRoot,
            new WorkspaceLoadOptions(WorkspaceLoaderPreference.SourceOnly, true),
            cancellationToken);

        if (!sourceResult.IsSuccess)
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
                "CodeAnalysis",
                codeAnalysisResult.Diagnostics.Concat(sourceResult.Diagnostics).ToArray());
        }

        return WorkspaceLoadResult.Success(
            sourceResult.AnalysisInput ?? new SourceOnlyAnalysisInput(sourceRoot, sourceResult.Documents),
            WorkspaceLoadMode.CodeAnalysisFallbackToSourceOnly,
            "CodeAnalysis",
            true,
            codeAnalysisResult.Diagnostics.Concat(sourceResult.Diagnostics).ToArray());
    }

    /// <summary>
    /// 获取 SourceOnly 模式的根路径。
    /// </summary>
    private static string GetSourceOnlyRoot(string inputPath) =>
        Directory.Exists(inputPath) || string.Equals(Path.GetExtension(inputPath), ".cs", StringComparison.OrdinalIgnoreCase)
            ? inputPath
            : Path.GetDirectoryName(inputPath) ?? inputPath;
}
