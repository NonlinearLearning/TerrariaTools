namespace TerrariaTools.Dome.Adapters.Runtime.Process;

using System.Xml.Linq;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;

public sealed class TerrariaRuntimeEnvironmentBuilder : ITerrariaRuntimeWorkspacePreparer
{
    private static readonly string[] IgnoredDirectoryNames =
    [
        "obj",
        "bin",
        ".vs"
    ];

    public Task EnsureOutputDirectoriesAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);
        return Task.CompletedTask;
    }

    public Task RefreshDependencyEnvironmentAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
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

        progressReporter.Report($"[tr-run] 依赖环境目录复制完成：共复制 {copiedFileCount} 个非 .cs 文件。");
        return Task.CompletedTask;
    }

    public Task PrepareWorkspaceAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken)
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
        progressReporter.Report($"[tr-run] 运行时工作区准备完成：共准备 {workspaceFileCount} 个文件。");
        return Task.CompletedTask;
    }

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

    private static bool HasProperty(XElement projectElement, string propertyName)
    {
        return projectElement.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal))
            .Elements()
            .Any(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.Ordinal));
    }
}





