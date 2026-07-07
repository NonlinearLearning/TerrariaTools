using System.Runtime.CompilerServices;
using System.Text;

namespace RoslynPrototype.Tests;

internal static class BuildDiffArtifactWriter
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> InitializedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    public static string GetDiffFilePath(string testCodeFileName, params string[] mirroredSegments)
    {
        var directoryPath = Path.Combine(
            ResolveRepositoryRoot(),
            "Build",
            "Diff",
            "TestCodeSet");
        foreach (var segment in mirroredSegments)
        {
            directoryPath = Path.Combine(directoryPath, segment);
        }

        Directory.CreateDirectory(directoryPath);
        var diffFileName = $"{Path.GetFileNameWithoutExtension(testCodeFileName)}.diff";
        return Path.Combine(directoryPath, diffFileName);
    }

    public static void InitializeDiffFile(string diffFilePath)
    {
        lock (Sync)
        {
            if (InitializedPaths.Contains(diffFilePath))
            {
                return;
            }

            File.WriteAllText(diffFilePath, string.Empty);
            InitializedPaths.Add(diffFilePath);
        }
    }

    public static void AppendDiffFragment(string diffFilePath, string unitTestName, string diffText)
    {
        lock (Sync)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"### UnitTest: {unitTestName}");
            builder.AppendLine(diffText.TrimEnd());
            builder.AppendLine();
            File.AppendAllText(diffFilePath, builder.ToString());
        }
    }

    private static string ResolveRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
