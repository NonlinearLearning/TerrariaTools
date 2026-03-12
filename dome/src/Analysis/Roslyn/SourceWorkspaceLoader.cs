namespace TerrariaTools.Dome.Analysis.Roslyn;

public sealed record SourceDocument(string SourcePath, string RelativePath, string SourceText);

public sealed class SourceWorkspaceLoader
{
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
