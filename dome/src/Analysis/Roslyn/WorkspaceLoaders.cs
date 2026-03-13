namespace TerrariaTools.Dome.Analysis.Roslyn;

using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    /// <inheritdoc />
    public async Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
    {
        try
        {
            EnsureMsBuildRegistered();

            using var workspace = MSBuildWorkspace.Create();
            if (string.Equals(Path.GetExtension(inputPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                var solution = await workspace.OpenSolutionAsync(inputPath, cancellationToken: cancellationToken);
                return await BuildResultFromSolutionAsync(
                    solution,
                    Path.GetDirectoryName(Path.GetFullPath(inputPath))!,
                    cancellationToken);
            }

            if (string.Equals(Path.GetExtension(inputPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(inputPath, cancellationToken: cancellationToken);
                return await BuildResultFromProjectAsync(
                    project,
                    Path.GetDirectoryName(Path.GetFullPath(inputPath))!,
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

    private static async Task<WorkspaceLoadResult> BuildResultFromSolutionAsync(
        Solution solution,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var documents = new List<WorkspaceDocumentContext>();
        foreach (var project in solution.Projects)
        {
            documents.AddRange(await BuildDocumentContextsAsync(project, rootPath, cancellationToken));
        }

        return WorkspaceLoadResult.Success(
            new WorkspaceAnalysisInput(solution, null, rootPath, documents),
            WorkspaceLoadMode.CodeAnalysis,
            "CodeAnalysis");
    }

    private static async Task<WorkspaceLoadResult> BuildResultFromProjectAsync(
        Project project,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var documents = await BuildDocumentContextsAsync(project, rootPath, cancellationToken);
        return WorkspaceLoadResult.Success(
            new WorkspaceAnalysisInput(project.Solution, project, rootPath, documents),
            WorkspaceLoadMode.CodeAnalysis,
            "CodeAnalysis");
    }

    private static async Task<IReadOnlyList<WorkspaceDocumentContext>> BuildDocumentContextsAsync(
        Project project,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
        {
            return Array.Empty<WorkspaceDocumentContext>();
        }

        var documents = new List<WorkspaceDocumentContext>();
        foreach (var document in project.Documents.Where(static d => string.Equals(Path.GetExtension(d.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (document.FilePath == null)
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root?.SyntaxTree == null)
            {
                continue;
            }

            var sourceText = await document.GetTextAsync(cancellationToken);
            var sourceDocument = new SourceDocument(
                Path.GetFullPath(document.FilePath),
                Path.GetRelativePath(rootPath, document.FilePath),
                sourceText.ToString());
            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);

            documents.Add(new WorkspaceDocumentContext(
                document,
                sourceDocument,
                compilation,
                semanticModel,
                (CompilationUnitSyntax)root));
        }

        return documents;
    }
}

/// <summary>
/// 工作区加载协调器。
/// 负责协调 CodeAnalysis 加载器和 SourceOnly 加载器，支持回退策略。
/// </summary>
public sealed class WorkspaceLoadCoordinator : IWorkspaceLoader
{
    private readonly IWorkspaceLoader _codeAnalysisLoader;
    private readonly IWorkspaceLoader _sourceOnlyLoader;

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

        if (codeAnalysisResult.IsSuccess)
        {
            return codeAnalysisResult;
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
