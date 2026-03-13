namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 基于源码文件的工作区加载器。
/// 仅加载 .cs 文件内容，不进行编译或语义分析。
/// </summary>
public sealed class SourceWorkspaceLoader : IWorkspaceLoader
{
    /// <inheritdoc />
    public Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken)
    {
        return LoadInternalAsync(inputPath, cancellationToken);
    }

    /// <summary>
    /// 从根路径加载源码文档。
    /// </summary>
    /// <param name="rootPath">根路径（文件或目录）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载结果。</returns>
    internal static async Task<WorkspaceLoadResult> LoadFromRootAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (File.Exists(rootPath) && string.Equals(Path.GetExtension(rootPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            var sourceText = await File.ReadAllTextAsync(rootPath, cancellationToken);
            return WorkspaceLoadResult.Success(
                new[]
                {
                    new SourceDocument(
                        Path.GetFullPath(rootPath),
                        Path.GetFileName(rootPath),
                        sourceText)
                },
                WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        if (Directory.Exists(rootPath))
        {
            var files = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var documents = new List<SourceDocument>(files.Length);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
                documents.Add(new SourceDocument(
                    Path.GetFullPath(file),
                    Path.GetRelativePath(rootPath, file),
                    sourceText));
            }

            return WorkspaceLoadResult.Success(
                documents,
                WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        return WorkspaceLoadResult.Failure(
            WorkspaceLoadMode.SourceOnly,
            "SourceOnly",
            new[]
            {
                new WorkspaceLoadDiagnostic(
                    "SourceOnlyLoad",
                    WorkspaceLoadDiagnosticSeverity.Error,
                    $"Input path '{rootPath}' was not found.")
            });
    }

    /// <summary>
    /// 内部加载逻辑。
    /// </summary>
    private static async Task<WorkspaceLoadResult> LoadInternalAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (File.Exists(inputPath) && !string.Equals(Path.GetExtension(inputPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceLoadResult.Failure(
                WorkspaceLoadMode.SourceOnly,
                "SourceOnly",
                new[]
                {
                    new WorkspaceLoadDiagnostic(
                        "SourceOnlyLoad",
                        WorkspaceLoadDiagnosticSeverity.Error,
                        $"Input file '{inputPath}' is not a C# source file.")
                });
        }

        return await LoadFromRootAsync(inputPath, cancellationToken);
    }
}
