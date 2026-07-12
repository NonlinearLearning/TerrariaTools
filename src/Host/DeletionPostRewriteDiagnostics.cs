using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application;

internal static class DeletionPostRewriteDiagnostics
{
    internal static PrototypeAnalysisResult AddSingleFileDiagnostics(
      PrototypeAnalysisResult result,
      string filePath,
      IReadOnlyDictionary<string, string> options)
    {
        if (DeletionApplicationOptions.ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options))
        {
            return result with { Diagnostics = Array.Empty<AnalysisDiagnostic>() };
        }

        var sourcesByPath = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [filePath] = result.RewrittenSource
        };
        return result with
        {
            Diagnostics = GetRewriteDiagnostics(sourcesByPath, sourcesByPath)
        };
    }

    internal static IReadOnlyList<AnalysisDiagnostic> GetRewriteDiagnostics(
      IReadOnlyDictionary<string, string> originalSourcesByPath,
      IReadOnlyDictionary<string, string> rewrittenSourcesByPath)
    {
        var trees = originalSourcesByPath
          .Select(pair =>
          {
              var source = rewrittenSourcesByPath.TryGetValue(pair.Key, out var rewrittenSource)
            ? rewrittenSource
            : pair.Value;
              return CSharpSyntaxTree.ParseText(source, path: pair.Key);
          })
          .ToList();

        return RoslynCompilationFactory.CreateCompilation(trees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .Where(diagnostic => !IsIgnoredPostRewriteDiagnostic(diagnostic))
          .Select(CreateAnalysisDiagnostic)
          .ToList();
    }

    internal static HashSet<string> GetStableErrorDiagnosticKeys(
      IReadOnlyDictionary<string, string> sourcesByPath)
    {
        var trees = sourcesByPath
          .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
          .ToList();
        return RoslynCompilationFactory.CreateCompilation(trees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .Where(diagnostic => !IsIgnoredPostRewriteDiagnostic(diagnostic))
          .Select(diagnostic =>
          {
              var path = diagnostic.Location.GetLineSpan().Path;
              return $"{path}|{diagnostic.Id}|{diagnostic.GetMessage()}";
          })
          .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsIgnoredPostRewriteDiagnostic(Diagnostic diagnostic)
    {
        return string.Equals(diagnostic.Id, "CS5001", StringComparison.Ordinal);
    }

    private static AnalysisDiagnostic CreateAnalysisDiagnostic(Diagnostic diagnostic)
    {
        var location = diagnostic.Location;
        var lineSpan = location.GetLineSpan();
        var sourceSpan = location.SourceSpan;
        return new AnalysisDiagnostic(
          diagnostic.Id,
          diagnostic.Severity.ToString(),
          diagnostic.GetMessage(),
          lineSpan.Path,
          sourceSpan.Start,
          sourceSpan.End);
    }
}
