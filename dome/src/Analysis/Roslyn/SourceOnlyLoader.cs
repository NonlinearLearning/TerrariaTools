namespace TerrariaTools.Dome.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;

public sealed partial class SourceOnlyLoader : ApplicationAbstractions.IWorkspaceLoader
{
    async Task<ApplicationAbstractions.WorkspaceLoadResult> ApplicationAbstractions.IWorkspaceLoader.LoadAsync(
        string inputPath,
        ApplicationAbstractions.WorkspaceLoadOptions options,
        CancellationToken cancellationToken) =>
        await LoadApplicationAsync(inputPath, cancellationToken);

    internal static async Task<ApplicationAbstractions.WorkspaceLoadResult> LoadApplicationAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(rootPath) && string.Equals(Path.GetExtension(rootPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            var sourceText = await File.ReadAllTextAsync(rootPath, cancellationToken);
            return ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ApplicationAbstractions.SourceDocumentSet(
                    Path.GetFullPath(rootPath),
                    Path.GetDirectoryName(Path.GetFullPath(rootPath)) ?? Path.GetFullPath(rootPath),
                    [
                        new ApplicationAbstractions.SourceDocument(
                            Path.GetFullPath(rootPath),
                            Path.GetFileName(rootPath),
                            sourceText)
                    ]),
                ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        if (Directory.Exists(rootPath))
        {
            var files = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var documents = new List<ApplicationAbstractions.SourceDocument>(files.Length);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
                documents.Add(new ApplicationAbstractions.SourceDocument(
                    Path.GetFullPath(file),
                    Path.GetRelativePath(rootPath, file),
                    sourceText));
            }

            return ApplicationAbstractions.WorkspaceLoadResult.Success(
                new ApplicationAbstractions.SourceDocumentSet(
                    rootPath,
                    rootPath,
                    documents),
                ModelPrimitives.WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        return ApplicationAbstractions.WorkspaceLoadResult.Failure(
            ModelPrimitives.WorkspaceLoadMode.SourceOnly,
            "SourceOnly",
            [
                new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                    "SourceOnlyLoad",
                    ModelPrimitives.WorkspaceLoadDiagnosticSeverity.Error,
                    $"Input path '{rootPath}' was not found.")
            ]);
    }
}
