namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Core;
using System.Xml.Linq;

/// <summary>
/// Terraria Runtime 环境构建器。
/// </summary>
public sealed class TerrariaRuntimeEnvironmentBuilder : ITerrariaRuntimeWorkspacePreparer
{
    private static readonly string[] IgnoredDirectoryNames =
    [
        "obj",
        "bin",
        ".vs"
    ];

    /// <summary>
    /// 刷新依赖环境目录，仅保留非 C# 依赖文件。
    /// </summary>
    /// <param name="layout">运行时目录布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务对象。</returns>
    public Task EnsureOutputDirectoriesAsync(TerrariaRuntimeLayout layout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);
        return Task.CompletedTask;
    }

    public Task RefreshDependencyEnvironmentAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-run] 开始刷新依赖环境目录...");
        Directory.CreateDirectory(layout.DependencyEnvironmentPath);
        var copiedFileCount = 0;

        foreach (var directory in Directory.EnumerateDirectories(layout.SourceRootPath, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(layout.SourceRootPath, directory))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(layout.SourceRootPath, directory);
            Directory.CreateDirectory(Path.Combine(layout.DependencyEnvironmentPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(layout.SourceRootPath, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(layout.SourceRootPath, filePath))
            {
                continue;
            }

            if (string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(layout.SourceRootPath, filePath);
            var destinationPath = Path.Combine(layout.DependencyEnvironmentPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
            copiedFileCount++;
        }

        progressReporter.Report($"[tr-run] 依赖环境目录已就绪：共复制 {copiedFileCount} 个非 .cs 文件。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 准备运行时工作区并覆盖改写结果。
    /// </summary>
    /// <param name="layout">运行时目录布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务对象。</returns>
    public Task PrepareWorkspaceAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-run] 开始准备运行时工作区...");
        if (Directory.Exists(layout.WorkspacePath))
        {
            Directory.Delete(layout.WorkspacePath, recursive: true);
        }

        CopyDirectory(layout.SourceRootPath, layout.WorkspacePath, includeCs: true);
        CopyDirectory(layout.DependencyEnvironmentPath, layout.WorkspacePath, includeCs: false);

        var rewrittenRoot = Path.Combine(layout.ArtifactsPath, "rewritten");
        if (Directory.Exists(rewrittenRoot))
        {
            CopyDirectory(rewrittenRoot, layout.WorkspacePath, includeCs: true, onlyCs: true);
        }

        NormalizeWorkspaceProjectFiles(layout.WorkspacePath);

        var workspaceFileCount = Directory.EnumerateFiles(layout.WorkspacePath, "*", SearchOption.AllDirectories).Count();
        progressReporter.Report($"[tr-run] 运行时工作区已就绪：共准备 {workspaceFileCount} 个文件。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 复制目录内容并按规则筛选 C# 文件。
    /// </summary>
    /// <param name="sourceRoot">源目录。</param>
    /// <param name="destinationRoot">目标目录。</param>
    /// <param name="includeCs">是否包含 C# 文件。</param>
    /// <param name="onlyCs">是否仅复制 C# 文件。</param>
    private static void CopyDirectory(string sourceRoot, string destinationRoot, bool includeCs, bool onlyCs = false)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(sourceRoot, directory))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(sourceRoot, filePath))
            {
                continue;
            }

            var isCs = string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);
            if (onlyCs && !isCs)
            {
                continue;
            }

            if (!onlyCs && !includeCs && isCs)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, filePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// 判断路径是否应被忽略。
    /// </summary>
    /// <param name="rootPath">根目录。</param>
    /// <param name="path">待判断路径。</param>
    /// <returns>是否忽略。</returns>
    private static bool ShouldSkipPath(string rootPath, string path)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        if (string.IsNullOrEmpty(relativePath) || string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relativePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment));

        return segments.Any(segment => IgnoredDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 规范化工作区内项目文件配置。
    /// </summary>
    /// <param name="workspacePath">工作区目录。</param>
    private static void NormalizeWorkspaceProjectFiles(string workspacePath)
    {
        foreach (var projectPath in Directory.EnumerateFiles(workspacePath, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
            var projectElement = document.Root;
            if (projectElement == null || !string.Equals(projectElement.Name.LocalName, "Project", StringComparison.Ordinal))
            {
                continue;
            }

            if (HasProperty(projectElement, "ImplicitUsings"))
            {
                continue;
            }

            var firstPropertyGroup = projectElement.Elements().FirstOrDefault(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal));
            if (firstPropertyGroup == null)
            {
                firstPropertyGroup = new XElement(projectElement.Name.Namespace + "PropertyGroup");
                projectElement.AddFirst(firstPropertyGroup);
            }

            firstPropertyGroup.Add(new XElement(projectElement.Name.Namespace + "ImplicitUsings", "disable"));
            document.Save(projectPath, SaveOptions.DisableFormatting);
        }
    }

    /// <summary>
    /// 判断项目文件是否包含指定属性。
    /// </summary>
    /// <param name="projectElement">项目根元素。</param>
    /// <param name="propertyName">属性名。</param>
    /// <returns>是否存在属性。</returns>
    private static bool HasProperty(XElement projectElement, string propertyName)
    {
        return projectElement.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal))
            .Elements()
            .Any(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.Ordinal));
    }
}
