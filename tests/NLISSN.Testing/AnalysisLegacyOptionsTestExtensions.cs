using Microsoft.CodeAnalysis;
using NLISSN.Application;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Rewrite;

namespace RoslynPrototype.Tests;

/// <summary>
/// Keeps historical rule fixtures readable while production code accepts typed settings only.
/// </summary>
public static class AnalysisLegacyOptionsTestExtensions
{
  public static PrototypeAnalysisResult Analyze(
    this ApplicationService application,
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options)
  {
    return application.Analyze(source, filePath, CreateSettings(options), CreateRuntime(options));
  }

  public static PrototypeAnalysisResult Analyze(
    this ApplicationService application,
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    AnalysisRuntime runtime)
  {
    return application.Analyze(source, filePath, CreateSettings(options), runtime);
  }

  public static PrototypeAnalysisResult Analyze(
    this ApplicationService application,
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    return application.Analyze(source, filePath, CreateSettings(options), semanticModel, root);
  }

  public static PrototypeAnalysisResult Analyze(
    this ApplicationService application,
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    AnalysisRuntime runtime,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    return application.Analyze(source, filePath, CreateSettings(options), runtime, semanticModel, root);
  }

  public static DirectoryAnalysisOutcome Analyze(
    this DirectoryAnalysisUseCase useCase,
    IReadOnlyList<DirectorySourceFile> sources,
    IReadOnlyDictionary<string, string> options,
    AnalysisRuntime runtime)
  {
    return useCase.Analyze(sources, CreateSettings(options), runtime);
  }

  public static AnalysisRuntime CreateRuntime(IReadOnlyDictionary<string, string> options)
  {
    return AnalysisRuntimeFactory.Create(new RoslynPrototypeExecutionOptions(
      GetPositiveInt(options, "max-degree-of-parallelism", Math.Max(1, Environment.ProcessorCount)),
      EnableDirectoryParallelism: !IsTrue(options, "disable-directory-parallelism"),
      EnableGroupParallelism: IsTrue(options, "enable-group-parallelism"),
      EnableHelperParallelism: !IsTrue(options, "disable-helper-parallelism"),
      CpgMaxDegreeOfParallelism: GetOptionalPositiveInt(options, "cpg-max-degree-of-parallelism")));
  }

  public static AnalysisRequestSettings CreateSettings(IReadOnlyDictionary<string, string> options)
  {
    return new AnalysisRequestSettings(
      GetNames(options, "target-name"),
      GetNames(options, "delete-class"),
      IsTrue(options, "skip-rewrite"),
      IsTrue(options, "validate-bindings"),
      IsTrue(options, "delete-unreferenced-methods"),
      IsTrue(options, "clear-unused-interface-implementations"),
      IsTrue(options, "privatize-internal-only-public-methods"),
      IsTrue(options, "fast-delete-class-directory"),
      IsTrue(options, "filter-delete-class-files-by-target-name"));
  }

  private static IReadOnlyList<string> GetNames(
    IReadOnlyDictionary<string, string> options,
    string key)
  {
    return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
      ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray()
      : Array.Empty<string>();
  }

  private static bool IsTrue(IReadOnlyDictionary<string, string> options, string key)
  {
    return options.TryGetValue(key, out var value) &&
      string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
  }

  private static int GetPositiveInt(
    IReadOnlyDictionary<string, string> options,
    string key,
    int fallback)
  {
    return options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
      ? Math.Max(1, parsed)
      : fallback;
  }

  private static int? GetOptionalPositiveInt(
    IReadOnlyDictionary<string, string> options,
    string key)
  {
    if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (!int.TryParse(value, out var parsed) || parsed <= 0)
    {
      throw new ArgumentException(
        $"Invalid --cpg-max-degree-of-parallelism value '{value}'.",
        nameof(options));
    }

    return parsed;
  }
}
