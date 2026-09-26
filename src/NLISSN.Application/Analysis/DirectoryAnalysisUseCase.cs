using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Analysis;
using NLISSN.Core.Analysis.MethodLinkage;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Performance;

namespace NLISSN.Application;

/// 目录分析中的一个源码输入；<see cref="Index" /> 定义稳定的发布顺序。
public sealed record DirectorySourceFile(
  int Index,
  string FilePath,
  string Source,
  bool IsGenerated = false,
  bool CanWrite = true);

/// A project-owned source file whose syntax tree already belongs to an external compilation.
public sealed record CompiledDirectorySourceFile(
  int Index,
  string FilePath,
  string Source,
  SyntaxTree SyntaxTree,
  bool IsGenerated = false,
  bool CanWrite = true);

/// 一个目录输入文件及其独立分析结果。
public sealed record DirectoryFileAnalysisResult(
  int Index,
  string FilePath,
  PrototypeAnalysisResult Result,
  bool CanWrite = true,
  bool IsGenerated = false);

/// 聚合目录级结果和逐文件结果。
public sealed record DirectoryAnalysisOutcome(
  PrototypeAnalysisResult Result,
  IReadOnlyList<DirectoryFileAnalysisResult> FileResults,
  DirectoryPerformanceFacts? Performance = null);

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
    public DirectoryAnalysisOutcome Analyze(
      IReadOnlyList<DirectorySourceFile> sourceFiles,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtime);

        var orderedSources = sourceFiles.OrderBy(file => file.Index).ToArray();
        if (orderedSources.Length == 0)
        {
            return new DirectoryAnalysisOutcome(
              CreateEmptyResult(),
              Array.Empty<DirectoryFileAnalysisResult>(),
              DirectoryPerformanceFactAggregator.Aggregate(
                "empty-directory",
                Array.Empty<DirectoryFileAnalysisResult>(),
                wallElapsedMs: 0));
        }

        var trees = orderedSources.ToDictionary(
          source => source.FilePath,
          source => CSharpSyntaxTree.ParseText(source.Source, path: source.FilePath),
          StringComparer.Ordinal);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        return AnalyzeCore(orderedSources, trees, compilation, settings, runtime);
    }

    // An MSBuild/Workspace caller supplies the real compilation and its project-owned trees.
    public DirectoryAnalysisOutcome AnalyzeCompiled(
      CSharpCompilation compilation,
      IReadOnlyList<CompiledDirectorySourceFile> sourceFiles,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      string performanceItemId = "directory")
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtime);

        var orderedSources = sourceFiles
          .OrderBy(file => file.Index)
          .Select(file => new DirectorySourceFile(
            file.Index,
            file.FilePath,
            file.Source,
            file.IsGenerated,
            file.CanWrite))
          .ToArray();
        if (orderedSources.Length == 0)
        {
            return new DirectoryAnalysisOutcome(
              CreateEmptyResult(),
              Array.Empty<DirectoryFileAnalysisResult>(),
              DirectoryPerformanceFactAggregator.Aggregate(
                performanceItemId,
                Array.Empty<DirectoryFileAnalysisResult>(),
                wallElapsedMs: 0));
        }

        var trees = sourceFiles.ToDictionary(
          source => source.FilePath,
          source => source.SyntaxTree,
          StringComparer.Ordinal);
        return AnalyzeCore(
          orderedSources,
          trees,
          compilation,
          settings,
          runtime,
          performanceItemId);
    }

    private DirectoryAnalysisOutcome AnalyzeCore(
      IReadOnlyList<DirectorySourceFile> orderedSources,
      IReadOnlyDictionary<string, SyntaxTree> trees,
      CSharpCompilation compilation,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      string performanceItemId = "directory")
    {
        var stopwatch = Stopwatch.StartNew();
        var sourcesByPath = orderedSources.ToDictionary(
          source => source.FilePath,
          source => source.Source,
          StringComparer.Ordinal);
        var analysisSources = ResolveAnalysisSources(orderedSources, settings);
        var methodLinkageAnalysis = settings.DeleteUnreferencedMethods
          ? runtime.GetOrCreateCompilationCache(
            compilation,
            static cachedCompilation => MethodLinkageAnalysis.Create(cachedCompilation))
          : null;
        var fileResults = AnalyzeFiles(
          analysisSources,
          trees,
          compilation,
          settings,
          runtime);

        if (ShouldUseDeclarationCleanup(settings))
        {
            ApplyDeclarationCleanup(orderedSources, fileResults);
        }

        var result = BuildResult(
          orderedSources.Count,
          analysisSources.Length,
          fileResults,
          methodLinkageAnalysis);
        var rewrittenSources = fileResults
          .Where(file => file.Result.Edits.Count > 0 && file.Result.RewrittenSource is not null)
          .ToDictionary(file => file.FilePath, file => file.Result.RewrittenSource!, StringComparer.Ordinal);
        var diagnostics = PostRewriteDiagnostics.ShouldSkipDeclarationDiagnostics(settings)
          ? Array.Empty<AnalysisDiagnostic>()
          : PostRewriteDiagnostics.GetRewriteDiagnostics(sourcesByPath, rewrittenSources);
        stopwatch.Stop();
        var performance = DirectoryPerformanceFactAggregator.Aggregate(
          performanceItemId,
          fileResults,
          stopwatch.ElapsedMilliseconds);

        return new DirectoryAnalysisOutcome(
          result with { Diagnostics = diagnostics },
          fileResults,
          performance);
    }

    public static PrototypeAnalysisResult CombineResults(
      IReadOnlyList<PrototypeAnalysisResult> results,
      string resultLabel)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultLabel);

        var rewrittenCount = results.Count(result =>
          result.Edits.Count > 0 && result.RewrittenSource is not null);
        var stats = results
          .Select(result => result.Stats)
          .Where(value => value is not null)
          .Cast<AnalysisStats>()
          .ToArray();
        int? analyzedFileCount = stats.Any(value => value.AnalyzedFileCount.HasValue)
          ? stats.Sum(value => value.AnalyzedFileCount ?? 0)
          : null;
        var evidence = results
          .Select(result => result.Evidence)
          .Where(value => value is not null)
          .Cast<AnalysisEvidenceGraph>()
          .ToArray();
        return new PrototypeAnalysisResult(
          results.SelectMany(result => result.SeedMarks).ToArray(),
          results.SelectMany(result => result.PropagatedMarks).ToArray(),
          results.SelectMany(result => result.LiftedMarks).ToArray(),
          results.SelectMany(result => result.Decisions).ToArray(),
          results.SelectMany(result => result.Edits).ToArray(),
          $"<{resultLabel}:{rewrittenCount}>",
          new DiffBuilder().Combine(results.Select(result => result.Diff)),
          null,
          new AnalysisStats(
            stats.Sum(value => value.ScannedFileCount),
            analyzedFileCount,
            stats.Sum(value => value.CandidateMethodCount),
            stats.Sum(value => value.DeletedMethodCount)),
          results.SelectMany(result => result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>()).ToArray(),
          results
            .SelectMany(result => result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>())
            .OrderBy(plan => plan.FilePath, StringComparer.Ordinal)
            .ToArray(),
          Evidence: evidence.Length == 0 ? null : AnalysisEvidenceGraph.Combine(evidence));
    }

    private List<DirectoryFileAnalysisResult> AnalyzeFiles(
      IReadOnlyList<DirectorySourceFile> sources,
      IReadOnlyDictionary<string, SyntaxTree> trees,
      CSharpCompilation compilation,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        // ① 先**整批**构建 CPG，使工作批次跨文件装箱（D1）。
        //    这一步必须在并行规则分析之前、且只做一次：跨文件装箱的意义就是让不同文件的方法
        //    落进同一个批次，逐文件各自构建则永远做不到。
        var batchFiles = sources
          .Select(source =>
          {
              var tree = trees[source.FilePath];
              return new BatchAnalysisFile(
                source.FilePath,
                source.Source,
                compilation.GetSemanticModel(tree),
                tree.GetRoot());
          })
          .ToArray();
        var graphBatch = _application.BuildGraphBatch(batchFiles, runtime);
        var batchFilesByPath = batchFiles.ToDictionary(
          file => file.FilePath,
          StringComparer.Ordinal);

        PrototypeAnalysisResult AnalyzeFile(DirectorySourceFile source)
        {
            var result = _application.AnalyzeWithGraph(
              batchFilesByPath[source.FilePath],
              graphBatch.Graphs[source.FilePath],
              graphBatch.Metrics,
              settings,
              runtime);
            return result.Edits.Count == 0 ? result with { RewrittenSource = null } : result;
        }

        if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
            runtime.ExecutionOptions.EffectiveDirectoryMaxDegreeOfParallelism == 1 ||
            sources.Count <= 1)
        {
            return sources
              .Select(source => new DirectoryFileAnalysisResult(
                source.Index,
                source.FilePath,
                AnalyzeFile(source),
                source.CanWrite,
                source.IsGenerated))
              .ToList();
        }

        return runtime.ConcurrencyPool.SelectOrderedAsync(
          sources.Count,
          runtime.ExecutionOptions.EffectiveDirectoryMaxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              var source = sources[index];
              return new DirectoryFileAnalysisResult(
                source.Index,
                source.FilePath,
                AnalyzeFile(source),
                source.CanWrite,
                source.IsGenerated);
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
            var cleaned = _cleanupService.ApplyEmptyNamespaceCleanup(
              fileResult.FilePath,
              original,
              fileResult.Result,
              cleanupState);
            fileResults[index] = fileResult with { Result = cleaned };
        }
    }

    private static DirectorySourceFile[] ResolveAnalysisSources(
      IReadOnlyList<DirectorySourceFile> sources,
      AnalysisRequestSettings settings)
    {
        if (!ShouldFilterDeclarationFilesByTargetName(settings))
        {
            return sources.ToArray();
        }

        var filtered = sources
          .Where(source => settings.DeleteClassNames.Any(name =>
            source.Source.Contains(name, StringComparison.Ordinal)))
          .ToArray();
        return filtered.Length == 0 ? sources.ToArray() : filtered;
    }

    private static PrototypeAnalysisResult BuildResult(
      int fileCount,
      int analyzedFileCount,
      IReadOnlyList<DirectoryFileAnalysisResult> fileResults,
      MethodLinkageResult? methodLinkageAnalysis)
    {
        var results = fileResults.Select(file => file.Result).ToArray();
        var rewrittenCount = results.Count(result => result.Edits.Count > 0 && result.RewrittenSource is not null);
        var writablePaths = fileResults
          .Where(file => file.CanWrite)
          .Select(file => file.FilePath)
          .ToHashSet(StringComparer.Ordinal);
        var rewritePlans = results
          .SelectMany(result => result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>())
          .Where(plan => writablePaths.Contains(plan.FilePath))
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
            methodLinkageAnalysis?.CandidateMethodCount ?? 0,
            methodLinkageAnalysis?.UnreferencedPrivateMethods.Count ?? 0),
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

    private static bool ShouldUseDeclarationCleanup(AnalysisRequestSettings settings)
    {
        return settings.HasDeleteClass && !settings.FastDeleteClassDirectory;
    }

    private static bool ShouldFilterDeclarationFilesByTargetName(AnalysisRequestSettings settings)
    {
        return settings.HasDeleteClass &&
          settings.FastDeleteClassDirectory &&
          settings.FilterDeleteClassFilesByTargetName;
    }
}
