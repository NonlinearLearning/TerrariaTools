using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Pipeline;

namespace NLISSN.Application;

/// 目录分析中的一个源码输入；<see cref="Index" /> 定义稳定的发布顺序。
public sealed record DirectorySourceFile(int Index, string FilePath, string Source);

/// 一个目录输入文件及其独立分析结果。
public sealed record DirectoryFileAnalysisResult(
  int Index,
  string FilePath,
  PrototypeAnalysisResult Result);

/// 聚合目录级结果和逐文件结果。
public sealed record DirectoryAnalysisOutcome(
  PrototypeAnalysisResult Result,
  IReadOnlyList<DirectoryFileAnalysisResult> FileResults);

/// 编排目录级删除分析，并在并行计算时维持与输入一致的确定性结果顺序。
public sealed class DirectoryAnalysisUseCase
{
    private readonly ApplicationService _application;
    private readonly PostRewriteCleanupService _cleanupService = new();

    // 以同一规则管道初始化目录分析用例，并复用单文件分析与清理组件。
    public DirectoryAnalysisUseCase(RulePipeline pipeline)
    {
        _application = new ApplicationService(pipeline);
    }

    // 对目录输入执行排序、并行分析、可选清理和聚合诊断，返回目录级稳定结果。
    public DirectoryAnalysisOutcome Analyze(IReadOnlyList<DirectorySourceFile> sourceFiles, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        var orderedSources = sourceFiles.OrderBy(file => file.Index).ToArray();
        if (orderedSources.Length == 0)
        {
            return new DirectoryAnalysisOutcome(
              CreateEmptyResult(),
              Array.Empty<DirectoryFileAnalysisResult>());
        }

        var sourcesByPath = orderedSources.ToDictionary(
          source => source.FilePath,
          source => source.Source,
          StringComparer.Ordinal);
        var analysisSources = ResolveAnalysisSources(orderedSources, options);
        var trees = orderedSources.ToDictionary(
          source => source.FilePath,
          source => CSharpSyntaxTree.ParseText(source.Source, path: source.FilePath),
          StringComparer.Ordinal);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var unreferencedMethodAnalysis = IsTrue(options, "delete-unreferenced-methods")
          ? runtime.GetOrCreateCompilationCache(
            compilation,
            static cachedCompilation => UnreferencedMethodAnalysis.Create(cachedCompilation))
          : null;
        var fileResults = AnalyzeFiles(
          analysisSources,
          trees,
          compilation,
          options,
          runtime);

        if (ShouldUseDeclarationCleanup(options))
        {
            ApplyDeclarationCleanup(orderedSources, fileResults);
        }

        var result = BuildResult(
          orderedSources.Length,
          analysisSources.Length,
          fileResults,
          unreferencedMethodAnalysis);
        var rewrittenSources = fileResults
          .Where(file => file.Result.Edits.Count > 0 && file.Result.RewrittenSource is not null)
          .ToDictionary(file => file.FilePath, file => file.Result.RewrittenSource!, StringComparer.Ordinal);
        var diagnostics = PostRewriteDiagnostics.ShouldSkipDeclarationDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : PostRewriteDiagnostics.GetRewriteDiagnostics(sourcesByPath, rewrittenSources);

        return new DirectoryAnalysisOutcome(
          result with { Diagnostics = diagnostics },
          fileResults);
    }

    private List<DirectoryFileAnalysisResult> AnalyzeFiles(IReadOnlyList<DirectorySourceFile> sources, IReadOnlyDictionary<string, SyntaxTree> trees, CSharpCompilation compilation, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        PrototypeAnalysisResult AnalyzeFile(DirectorySourceFile source)
        {
            var tree = trees[source.FilePath];
            var result = _application.Analyze(
              source.Source,
              source.FilePath,
              options,
              runtime,
              compilation.GetSemanticModel(tree),
              tree.GetRoot());
            return result.Edits.Count == 0 ? result with { RewrittenSource = null } : result;
        }

        if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism == 1 ||
            sources.Count <= 1)
        {
            return sources
              .Select(source => new DirectoryFileAnalysisResult(source.Index, source.FilePath, AnalyzeFile(source)))
              .ToList();
        }

        return runtime.ConcurrencyPool.SelectOrderedAsync(
          sources.Count,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              using var lease = await runtime.CpgBuildAdmissionBudget
            .AcquireAsync(runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism, cancellationToken)
            .ConfigureAwait(false);
              using var scope = runtime.PushCpgBuildAdmissionLease(lease);
              var source = sources[index];
              return new DirectoryFileAnalysisResult(source.Index, source.FilePath, AnalyzeFile(source));
          },
          runtime.ExecutionOptions.CancellationToken).GetAwaiter().GetResult().ToList();
    }

    private void ApplyDeclarationCleanup(IReadOnlyList<DirectorySourceFile> sources, List<DirectoryFileAnalysisResult> fileResults)
    {
        var resultsByPath = fileResults.ToDictionary(file => file.FilePath, StringComparer.Ordinal);
        var projectSources = sources.ToDictionary(
          source => source.FilePath,
          source => resultsByPath.TryGetValue(source.FilePath, out var result) && result.Result.RewrittenSource is not null
            ? result.Result.RewrittenSource
            : source.Source,
          StringComparer.Ordinal);
        var cleanupState = new PostRewriteCleanupService.CleanupProjectState(projectSources);

        for (var index = 0; index < fileResults.Count; index++)
        {
            var fileResult = fileResults[index];
            var original = sources.Single(source => string.Equals(source.FilePath, fileResult.FilePath, StringComparison.Ordinal)).Source;
            var cleaned = _cleanupService.ApplyUsingCleanup(fileResult.FilePath, original, fileResult.Result, cleanupState);
            cleaned = _cleanupService.ApplyEmptyNamespaceCleanup(fileResult.FilePath, original, cleaned, cleanupState);
            fileResults[index] = fileResult with { Result = cleaned };
        }
    }

    private static DirectorySourceFile[] ResolveAnalysisSources(IReadOnlyList<DirectorySourceFile> sources, IReadOnlyDictionary<string, string> options)
    {
        if (!ShouldFilterDeclarationFilesByTargetName(options) ||
            !options.TryGetValue("delete-class", out var targetName) ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return sources.ToArray();
        }

        var filtered = sources
          .Where(source => source.Source.Contains(targetName, StringComparison.Ordinal))
          .ToArray();
        return filtered.Length == 0 ? sources.ToArray() : filtered;
    }

    private static PrototypeAnalysisResult BuildResult(
      int fileCount,
      int analyzedFileCount,
      IReadOnlyList<DirectoryFileAnalysisResult> fileResults,
      UnreferencedMethodAnalysis? unreferencedMethodAnalysis)
    {
        var results = fileResults.Select(file => file.Result).ToArray();
        var rewrittenCount = results.Count(result => result.Edits.Count > 0 && result.RewrittenSource is not null);
        var rewritePlans = results
          .SelectMany(result => result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>())
          .Where(plan => plan.Operations.Count > 0)
          .OrderBy(plan => plan.FilePath, StringComparer.Ordinal)
          .ToArray();
        return new PrototypeAnalysisResult(
          results.SelectMany(result => result.SeedMarks).ToList(),
          results.SelectMany(result => result.PropagatedMarks).ToList(),
          results.SelectMany(result => result.LiftedMarks).ToList(),
          results.SelectMany(result => result.Decisions).ToList(),
          results.SelectMany(result => result.Edits).ToList(),
          $"<multi-file:{rewrittenCount}>",
          new DiffBuilder().Combine(results.Where(result => result.Diff.Files.Count > 0).Select(result => result.Diff).ToList()),
          null,
          new AnalysisStats(
            fileCount,
            analyzedFileCount,
            unreferencedMethodAnalysis?.CandidateMethodCount ?? 0,
            unreferencedMethodAnalysis?.UnreferencedMethods.Count ?? 0),
          RewritePlans: rewritePlans,
          Evidence: CombineEvidence(results));
    }

    private static PrototypeAnalysisResult CreateEmptyResult()
    {
        return new PrototypeAnalysisResult(
          Array.Empty<MarkRecord>(),
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          Array.Empty<RuleDecision>(),
          Array.Empty<RewriteEdit>(),
          "<multi-file:0>",
          new DiffBuilder().Build(Array.Empty<RewriteEdit>()),
          null,
          new AnalysisStats(0, 0, 0, 0),
          Diagnostics: Array.Empty<AnalysisDiagnostic>(),
          RewritePlans: Array.Empty<PrototypeFileRewritePlan>());
    }

    private static AnalysisEvidenceGraph? CombineEvidence(IReadOnlyList<PrototypeAnalysisResult> results)
    {
        var graphs = results
          .Select(result => result.Evidence)
          .Where(graph => graph is not null)
          .Cast<AnalysisEvidenceGraph>()
          .ToList();
        if (graphs.Count == 0)
        {
            return null;
        }

        return AnalysisEvidenceGraph.Combine(graphs);
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseDeclarationCleanup(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") && !IsTrue(options, "fast-delete-class-directory");
    }

    private static bool ShouldFilterDeclarationFilesByTargetName(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          IsTrue(options, "fast-delete-class-directory") &&
          IsTrue(options, "filter-delete-class-files-by-target-name");
    }
}
