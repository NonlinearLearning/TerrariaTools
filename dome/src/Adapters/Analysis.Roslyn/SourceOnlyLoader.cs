namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

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
                new CoreAnalysis.AnalysisInput(
                    new CoreAnalysis.SourceDocumentSet(
                        Path.GetFullPath(rootPath),
                        Path.GetDirectoryName(Path.GetFullPath(rootPath)) ?? Path.GetFullPath(rootPath),
                        [
                            new CoreAnalysis.SourceDocument(
                                Path.GetFullPath(rootPath),
                                Path.GetFileName(rootPath),
                                sourceText)
                        ]),
                    CoreAnalysis.AnalysisInputMode.SourceOnly,
                    new CoreAnalysis.AnalysisEnvironmentInfo("SourceOnly", ProjectPath: rootPath, RequestedPrimaryLoader: "SourceOnly")),
                ApplicationAbstractions.WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        if (Directory.Exists(rootPath))
        {
            var files = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var documents = new List<CoreAnalysis.SourceDocument>(files.Length);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
                documents.Add(new CoreAnalysis.SourceDocument(
                    Path.GetFullPath(file),
                    Path.GetRelativePath(rootPath, file),
                    sourceText));
            }

            return ApplicationAbstractions.WorkspaceLoadResult.Success(
                new CoreAnalysis.AnalysisInput(
                    new CoreAnalysis.SourceDocumentSet(
                        rootPath,
                        rootPath,
                        documents),
                    CoreAnalysis.AnalysisInputMode.SourceOnly,
                    new CoreAnalysis.AnalysisEnvironmentInfo("SourceOnly", ProjectPath: rootPath, RequestedPrimaryLoader: "SourceOnly")),
                ApplicationAbstractions.WorkspaceLoadMode.SourceOnly,
                "SourceOnly");
        }

        return ApplicationAbstractions.WorkspaceLoadResult.Failure(
            ApplicationAbstractions.WorkspaceLoadMode.SourceOnly,
            "SourceOnly",
            [
                new ApplicationAbstractions.WorkspaceLoadDiagnostic(
                    "SourceOnlyLoad",
                    ApplicationAbstractions.WorkspaceLoadDiagnosticSeverity.Error,
                    $"Input path '{rootPath}' was not found.")
            ]);
    }
}




