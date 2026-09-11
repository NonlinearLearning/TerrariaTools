using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Rewrite;

namespace NLISSN.Application;

/// 比较改写前后的 Roslyn 错误诊断，将新增错误作为分析结果的一部分返回。
public static class  PostRewriteDiagnostics
{
    public static bool ShouldSkipDeclarationDiagnostics(AnalysisRequestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HasDeleteClass && settings.FastDeleteClassDirectory;
    }

    // 为单文件分析结果补充改写后诊断，必要时可按选项跳过这一步。
    public static PrototypeAnalysisResult AddSingleFileDiagnostics(
      PrototypeAnalysisResult result,
      string originalSource,
      string filePath,
      bool skipDiagnostics)
  {
        if (skipDiagnostics)
        {
            return result with { Diagnostics = Array.Empty<AnalysisDiagnostic>() };
        }

        if (string.IsNullOrEmpty(result.RewrittenSource))
        {
            return result with { Diagnostics = Array.Empty<AnalysisDiagnostic>() };
        }

        var originalSourcesByPath = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [filePath] = originalSource
        };
        var rewrittenSourcesByPath = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [filePath] = result.RewrittenSource
        };
        return result with
        {
            Diagnostics = GetRewriteDiagnostics(originalSourcesByPath, rewrittenSourcesByPath)
        };
    }

    // 比较原始文件集与改写结果，返回新增 Roslyn 错误诊断的稳定快照。
    public static IReadOnlyList<AnalysisDiagnostic> GetRewriteDiagnostics(IReadOnlyDictionary<string, string> originalSourcesByPath, IReadOnlyDictionary<string, string> rewrittenSourcesByPath)
    {
        var baselineDiagnosticKeys = GetErrorDiagnostics(BuildTrees(originalSourcesByPath, originalSourcesByPath))
          .Select(BuildStableDiagnosticKey)
          .ToHashSet(StringComparer.Ordinal);

        return GetErrorDiagnostics(BuildTrees(originalSourcesByPath, rewrittenSourcesByPath))
          .Where(diagnostic => !baselineDiagnosticKeys.Contains(BuildStableDiagnosticKey(diagnostic)))
          .Select(CreateAnalysisDiagnostic)
          .ToList();
    }

    // 为一组源码生成稳定错误键集合，便于后续做前后诊断差集比较。
    public static HashSet<string> GetStableErrorDiagnosticKeys(IReadOnlyDictionary<string, string> sourcesByPath)
    {
        return GetStableErrorDiagnosticKeys(
          sourcesByPath,
          overriddenFilePath: null,
          overriddenSource: null);
    }

    // 在单文件覆盖场景下生成稳定错误键集合，用于判断候选清理是否引入新错误。
    public static HashSet<string> GetStableErrorDiagnosticKeys(IReadOnlyDictionary<string, string> sourcesByPath, string? overriddenFilePath, string? overriddenSource)
    {
        return GetErrorDiagnostics(
          BuildTreesWithOverride(sourcesByPath, overriddenFilePath, overriddenSource))
          .Select(BuildStableDiagnosticKey)
          .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsIgnoredPostRewriteDiagnostic(Diagnostic diagnostic)
    {
        return string.Equals(diagnostic.Id, "CS5001", StringComparison.Ordinal);
    }

    private static IReadOnlyList<SyntaxTree> BuildTrees(IReadOnlyDictionary<string, string> originalSourcesByPath, IReadOnlyDictionary<string, string> rewrittenSourcesByPath)
    {
        return originalSourcesByPath
          .Select(pair =>
          {
              var source = rewrittenSourcesByPath.TryGetValue(pair.Key, out var rewrittenSource)
                ? rewrittenSource
                : pair.Value;
              return CSharpSyntaxTree.ParseText(source, path: pair.Key);
          })
          .ToList();
    }

    private static IReadOnlyList<SyntaxTree> BuildTreesWithOverride(IReadOnlyDictionary<string, string> sourcesByPath, string? overriddenFilePath, string? overriddenSource)
    {
        return sourcesByPath
          .Select(pair =>
          {
              var source = overriddenFilePath is not null &&
                  overriddenSource is not null &&
                  string.Equals(pair.Key, overriddenFilePath, StringComparison.Ordinal)
                ? overriddenSource
                : pair.Value;
              return CSharpSyntaxTree.ParseText(source, path: pair.Key);
          })
          .ToList();
    }

    private static IReadOnlyList<Diagnostic> GetErrorDiagnostics(IReadOnlyList<SyntaxTree> trees)
    {
        return RoslynCompilationFactory.CreateCompilation(trees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .Where(diagnostic => !IsIgnoredPostRewriteDiagnostic(diagnostic))
          .ToList();
    }

    private static string BuildStableDiagnosticKey(Diagnostic diagnostic)
    {
        var path = diagnostic.Location.GetLineSpan().Path;
        return $"{path}|{diagnostic.Id}|{diagnostic.GetMessage()}";
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
