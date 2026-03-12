namespace TerrariaTools.Dome.Analysis.Roslyn;

/// <summary>
/// 源代码文档记录，包含源路径、相对路径和源代码文本。
/// </summary>
/// <param name="SourcePath">源文件的绝对路径。</param>
/// <param name="RelativePath">相对于工作区根目录的路径。</param>
/// <param name="SourceText">源代码文本内容。</param>
public sealed record SourceDocument(string SourcePath, string RelativePath, string SourceText);

/// <summary>
/// 源代码工作区加载器，负责从文件或目录加载源代码文档。
/// </summary>
public sealed class SourceWorkspaceLoader
{
    /// <summary>
    /// 异步加载指定路径下的源代码文档。
    /// </summary>
    /// <param name="inputPath">输入的文件或目录路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载的源代码文档列表。</returns>
    public async Task<IReadOnlyList<SourceDocument>> LoadAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (File.Exists(inputPath))
        {
            var sourceText = await File.ReadAllTextAsync(inputPath, cancellationToken);
            return new[]
            {
                new SourceDocument(
                    Path.GetFullPath(inputPath),
                    Path.GetFileName(inputPath),
                    sourceText)
            };
        }

        if (Directory.Exists(inputPath))
        {
            var files = Directory.GetFiles(inputPath, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var documents = new List<SourceDocument>(files.Length);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
                documents.Add(new SourceDocument(
                    Path.GetFullPath(file),
                    Path.GetRelativePath(inputPath, file),
                    sourceText));
            }

            return documents;
        }

        return Array.Empty<SourceDocument>();
    }
}
