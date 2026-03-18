namespace TerrariaTools.Dome.Application;

using System.Xml.Linq;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;

public sealed class TerrariaRuntimeShadowProjectBuilder
{
    private static readonly string[] IgnoredDirectoryNames =
    [
        "obj",
        "bin",
        ".vs"
    ];

    public Task BuildAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowLayout layout,
        IReadOnlyDictionary<string, string> rewrittenDocuments,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        progressReporter.Report("[tr-shadow] 开始准备 shadow 项目工作区...");

        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);
        PrepareDependencyEnvironment(layout);

        if (Directory.Exists(layout.WorkspacePath))
        {
            Directory.Delete(layout.WorkspacePath, recursive: true);
        }

        CopyDirectory(layout.DependencyEnvironmentPath, layout.WorkspacePath, includeCs: false);

        foreach (var rewrittenDocument in rewrittenDocuments.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.Combine(layout.WorkspacePath, rewrittenDocument.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, rewrittenDocument.Value);
        }

        NormalizeWorkspaceProjectFiles(layout.WorkspacePath);
        progressReporter.Report($"[tr-shadow] Shadow 项目工作区已就绪：共生成 {rewrittenDocuments.Count} 个代码文档。");
        return Task.CompletedTask;
    }

    private static void PrepareDependencyEnvironment(ApplicationAbstractions.TerrariaRuntimeShadowLayout layout)
    {
        Directory.CreateDirectory(layout.DependencyEnvironmentPath);

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
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot, bool includeCs)
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
            if (!includeCs && isCs)
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
