using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Logic.Analysis.Engine.Frontend;

namespace Infrastructure.Analysis.Engine.Frontend;

/// <summary>
/// 负责把输入路径加载成真实 Roslyn 编译上下文。
///
/// 当前支持三种输入：
/// - `sln`
/// - `csproj`
/// - 源码目录或单个 `.cs` 文件
///
/// 这样设计的原因很直接：
/// - 真实项目分析需要 `Project` 和 `Compilation`；
/// - 轻量试验场景又不能被 `MSBuildWorkspace` 绑定死；
/// - 两种入口都需要统一落成一个可复用的上下文对象。
/// </summary>
public sealed class RoslynProjectLoader
{
    /// <summary>
    /// 将输入路径加载成 Roslyn 编译上下文。
    /// </summary>
    /// <param name="inputPath">解决方案、项目、目录或单文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可供前端和 pass 复用的编译上下文。</returns>
    public async Task<RoslynCompilationContext> LoadAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string fullPath = Path.GetFullPath(inputPath);

        if (File.Exists(fullPath))
        {
            if (RoslynProjectLoadConventions.IsSolutionFile(fullPath))
            {
                return await LoadSolutionAsync(fullPath, cancellationToken);
            }

            if (RoslynProjectLoadConventions.IsProjectFile(fullPath))
            {
                return await LoadProjectAsync(fullPath, cancellationToken);
            }

            if (RoslynProjectLoadConventions.IsSourceFile(fullPath))
            {
                return await LoadSourceFilesAsync(new[] { fullPath }, cancellationToken);
            }

            throw new InvalidOperationException(RoslynProjectLoadConventions.BuildUnsupportedFileTypeMessage(fullPath));
        }

        if (Directory.Exists(fullPath))
        {
            return await LoadDirectoryAsync(fullPath, cancellationToken);
        }

        throw new DirectoryNotFoundException(RoslynProjectLoadConventions.BuildMissingInputPathMessage(fullPath));
    }

    private static async Task<RoslynCompilationContext> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        Project? project = solution.Projects.FirstOrDefault();

        if (project is null)
        {
            throw new InvalidOperationException(RoslynProjectLoadConventions.BuildNoAnalyzableProjectsMessage());
        }

        return await CreateContextAsync(project, cancellationToken);
    }

    private static async Task<RoslynCompilationContext> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Project project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        return await CreateContextAsync(project, cancellationToken);
    }

    private static async Task<RoslynCompilationContext> LoadDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        string[] sourceFiles = Directory
            .EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourceFiles.Length == 0)
        {
            throw new InvalidOperationException(RoslynProjectLoadConventions.BuildNoSourceFilesMessage(directoryPath));
        }

        return await LoadSourceFilesAsync(sourceFiles, cancellationToken);
    }

    private static async Task<RoslynCompilationContext> LoadSourceFilesAsync(
        IReadOnlyCollection<string> sourceFiles,
        CancellationToken cancellationToken)
    {
        AdhocWorkspace workspace = new();
        ProjectId projectId = ProjectId.CreateNewId("Analysis");
        ProjectInfo projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Analysis",
            "Analysis",
            LanguageNames.CSharp,
            metadataReferences: CreateDefaultMetadataReferences());

        Solution solution = workspace.CurrentSolution.AddProject(projectInfo);

        foreach (string sourceFile in sourceFiles)
        {
            SourceText sourceText = SourceText.From(
                await File.ReadAllTextAsync(sourceFile, cancellationToken),
                encoding: null);

            DocumentInfo documentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(sourceFile),
                loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Create(), sourceFile)),
                filePath: sourceFile);

            solution = solution.AddDocument(documentInfo);
        }

        Project project = solution.GetProject(projectId)
            ?? throw new InvalidOperationException(RoslynProjectLoadConventions.BuildInMemoryProjectCreationFailedMessage());

        return await CreateContextAsync(project, cancellationToken);
    }

    private static async Task<RoslynCompilationContext> CreateContextAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        Compilation compilation = await project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException(RoslynProjectLoadConventions.BuildCompilationCreationFailedMessage());

        Dictionary<SyntaxTree, SemanticModel> semanticModels = new();
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            semanticModels[syntaxTree] = compilation.GetSemanticModel(syntaxTree);
        }

        return new RoslynCompilationContext(project.Name, compilation, semanticModels);
    }

    private static IReadOnlyList<MetadataReference> CreateDefaultMetadataReferences()
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException(RoslynProjectLoadConventions.BuildMissingTrustedPlatformAssembliesMessage());
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
