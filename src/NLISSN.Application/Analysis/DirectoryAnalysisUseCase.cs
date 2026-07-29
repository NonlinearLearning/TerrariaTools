using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Builder;
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

/// 记录并发分析完成后等待按输入顺序发布的积压情况。
public sealed record DirectoryPublicationStats(
  int FileCount,
  int UnpublishedCountPeak,
  long WaitToPublishMilliseconds,
  int OldestUnpublishedIndex);

/// 聚合目录级结果、逐文件结果和有序发布遥测。
public sealed record DirectoryAnalysisOutcome(
  PrototypeAnalysisResult Result,
  IReadOnlyList<DirectoryFileAnalysisResult> FileResults,
  DirectoryPublicationStats PublicationStats);

/// 编排目录级删除分析，并在并行计算时维持与输入一致的确定性结果顺序。
public sealed class DirectoryAnalysisUseCase
{
    private const string DeleteUnreferencedMethodMarkRuleId = "DEL-UNREF-METHOD-MARK-001";
    private readonly ApplicationService _application;
    private readonly DeleteClassPostRewriteCleanupService _cleanupService = new();
    private readonly PrototypeRewriter _rewriter = new();

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
              Array.Empty<DirectoryFileAnalysisResult>(),
              new DirectoryPublicationStats(0, 0, 0, -1));
        }

        if (ShouldUseUnreferencedMethodFastPath(options))
        {
            return AnalyzeUnreferencedMethods(orderedSources, runtime);
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
        var (fileResults, publicationStats) = AnalyzeFiles(
          analysisSources,
          trees,
          compilation,
          options,
          runtime);

        if (ShouldUseDeleteClassCleanup(options))
        {
            ApplyDeleteClassCleanup(orderedSources, fileResults);
        }

        var result = BuildResult(
          orderedSources.Length,
          analysisSources.Length,
          fileResults);
        var rewrittenSources = fileResults
          .Where(file => file.Result.Edits.Count > 0 && file.Result.RewrittenSource is not null)
          .ToDictionary(file => file.FilePath, file => file.Result.RewrittenSource!, StringComparer.Ordinal);
        var diagnostics = ShouldSkipPostRewriteDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : PostRewriteDiagnostics.GetRewriteDiagnostics(sourcesByPath, rewrittenSources);

        return new DirectoryAnalysisOutcome(
          result with { Diagnostics = diagnostics },
          fileResults,
          publicationStats);
    }

    // 文件计算可以并行完成，发布列表仍按 source.Index 递增，保证后续改写和输出稳定。
    private (List<DirectoryFileAnalysisResult> FileResults, DirectoryPublicationStats PublicationStats) AnalyzeFiles(IReadOnlyList<DirectorySourceFile> sources, IReadOnlyDictionary<string, SyntaxTree> trees, CSharpCompilation compilation, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
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
            return (
              sources.Select(source => new DirectoryFileAnalysisResult(source.Index, source.FilePath, AnalyzeFile(source))).ToList(),
              new DirectoryPublicationStats(sources.Count, 0, 0, -1));
        }

        var publicationLock = new object();
        var completed = new bool[sources.Count];
        var completedResults = new DirectoryFileAnalysisResult?[sources.Count];
        var completedTimestamps = new long[sources.Count];
        var published = new List<DirectoryFileAnalysisResult>(sources.Count);
        var nextPublishIndex = 0;
        var unpublishedCountPeak = 0;
        var oldestUnpublishedIndex = -1;
        var waitToPublishMilliseconds = 0L;

        runtime.ConcurrencyPool.SelectOrderedAsync(
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
              var result = new DirectoryFileAnalysisResult(source.Index, source.FilePath, AnalyzeFile(source));
              var completedTimestamp = Stopwatch.GetTimestamp();
              lock (publicationLock)
              {
                  completed[index] = true;
                  completedResults[index] = result;
                  completedTimestamps[index] = completedTimestamp;
                  var unpublishedCount = 0;
                  var oldestUnpublished = -1;
                  for (var candidate = nextPublishIndex; candidate < sources.Count; candidate++)
                  {
                      if (!completed[candidate])
                      {
                          continue;
                      }

                      unpublishedCount++;
                      oldestUnpublished = oldestUnpublished < 0 ? candidate : oldestUnpublished;
                  }

                  if (unpublishedCount > unpublishedCountPeak)
                  {
                      unpublishedCountPeak = unpublishedCount;
                      oldestUnpublishedIndex = oldestUnpublished;
                  }

                  while (nextPublishIndex < sources.Count && completed[nextPublishIndex])
                  {
                      var publishTimestamp = Stopwatch.GetTimestamp();
                      waitToPublishMilliseconds += (long)Stopwatch
                        .GetElapsedTime(completedTimestamps[nextPublishIndex], publishTimestamp)
                        .TotalMilliseconds;
                      published.Add(completedResults[nextPublishIndex]!);
                      completedResults[nextPublishIndex] = null;
                      nextPublishIndex++;
                  }
              }

              return 0;
          },
          runtime.ExecutionOptions.CancellationToken).GetAwaiter().GetResult();

        return (
          published,
          new DirectoryPublicationStats(
            sources.Count,
            unpublishedCountPeak,
            waitToPublishMilliseconds,
            oldestUnpublishedIndex));
    }

    private DirectoryAnalysisOutcome AnalyzeUnreferencedMethods(IReadOnlyList<DirectorySourceFile> sources, AnalysisRuntime runtime)
    {
        var trees = sources.ToDictionary(
          source => source.FilePath,
          source => CSharpSyntaxTree.ParseText(source.Source, path: source.FilePath),
          StringComparer.Ordinal);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var candidates = BuildUnreferencedMethodCandidateMap(compilation);
        var methodsByPath = FindUnreferencedMethodDeclarationsByPath(compilation, candidates);
        var deletedMethodCount = methodsByPath.Values.Sum(methods => methods.Count);
        var results = AnalyzeUnreferencedFiles(sources, trees, compilation, methodsByPath, runtime);
        var aggregate = BuildResult(sources.Count, sources.Count, results) with
        {
            Stats = new AnalysisStats(
            sources.Count,
            sources.Count,
            candidates.Count,
            deletedMethodCount),
        };
        return new DirectoryAnalysisOutcome(
          aggregate,
          results,
          new DirectoryPublicationStats(sources.Count, 0, 0, -1));
    }

    private List<DirectoryFileAnalysisResult> AnalyzeUnreferencedFiles(IReadOnlyList<DirectorySourceFile> sources, IReadOnlyDictionary<string, SyntaxTree> trees, Compilation compilation, IReadOnlyDictionary<string, IReadOnlyList<MethodDeclarationSyntax>> methodsByPath, AnalysisRuntime runtime)
    {
        DirectoryFileAnalysisResult AnalyzeFile(DirectorySourceFile source)
        {
            var tree = trees[source.FilePath];
            var methods = methodsByPath.TryGetValue(source.FilePath, out var matchedMethods)
              ? matchedMethods
              : Array.Empty<MethodDeclarationSyntax>();
            var result = AnalyzeUnreferencedFile(
              compilation.GetSemanticModel(tree),
              tree.GetRoot(),
              methods);
            return new DirectoryFileAnalysisResult(
              source.Index,
              source.FilePath,
              result.Edits.Count == 0 ? result with { RewrittenSource = null } : result);
        }

        if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism == 1 ||
            sources.Count <= 1)
        {
            return sources.Select(AnalyzeFile).ToList();
        }

        return runtime.ConcurrencyPool.SelectOrderedAsync(
          sources.Count,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          (index, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              return Task.FromResult(AnalyzeFile(sources[index]));
          },
          runtime.ExecutionOptions.CancellationToken).GetAwaiter().GetResult().ToList();
    }

    private PrototypeAnalysisResult AnalyzeUnreferencedFile(SemanticModel semanticModel, SyntaxNode root, IReadOnlyList<MethodDeclarationSyntax> methodsToDelete)
    {
        var seedMarks = methodsToDelete
          .Select(method => new MarkRecord(
            DeleteUnreferencedMethodMarkRuleId,
            method,
            null,
            null,
            "Method has no references from methods that remain in the project."))
          .ToList();
        var decisions = methodsToDelete
          .Select(method => new RuleDecision(
            method,
            method,
            DecisionActionKind.Delete,
            "Method has no references from methods that remain in the project."))
          .ToList();
        var rewriteResult = _rewriter.Rewrite(root, semanticModel, decisions);
        return new PrototypeAnalysisResult(
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          decisions,
          rewriteResult.Edits,
          rewriteResult.RewrittenSource,
          rewriteResult.Diff,
          null,
          RewritePlans: rewriteResult.Operations is { Count: > 0 }
            ? new[] { new PrototypeFileRewritePlan(root.SyntaxTree.FilePath, rewriteResult.Operations) }
            : Array.Empty<PrototypeFileRewritePlan>());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<MethodDeclarationSyntax>>
      FindUnreferencedMethodDeclarationsByPath(Compilation compilation, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var references = BuildUnreferencedMethodReferenceIndex(compilation, candidates);
        var methods = FindUnreferencedMethodsByIteration(candidates, references);
        return methods
          .GroupBy(pair => pair.Value.SyntaxTree.FilePath ?? string.Empty, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<MethodDeclarationSyntax>)group
              .Select(pair => pair.Value)
              .OrderBy(method => method.SpanStart)
              .ToList(),
            StringComparer.Ordinal);
    }

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> BuildUnreferencedMethodCandidateMap(Compilation compilation)
    {
        var candidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
                    !IsUnreferencedMethodCandidate(symbol))
                {
                    continue;
                }

                candidates[CanonicalizeMethodSymbol(symbol)] = method;
            }
        }

        return candidates;
    }

    private static MethodReferenceIndex BuildUnreferencedMethodReferenceIndex(Compilation compilation, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var incomingCallers = CreateMethodReferenceSetMap(candidates.Keys);
        var candidateCallees = CreateMethodReferenceSetMap(candidates.Keys);
        var externallyReferencedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var referencedMethod = GetReferencedCandidateMethod(model, node);
                if (referencedMethod is null || !candidates.ContainsKey(referencedMethod))
                {
                    continue;
                }

                var caller = GetContainingCandidateMethod(model, node, candidates);
                if (caller is null)
                {
                    externallyReferencedMethods.Add(referencedMethod);
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(caller, referencedMethod))
                {
                    continue;
                }

                incomingCallers[referencedMethod].Add(caller);
                candidateCallees[caller].Add(referencedMethod);
            }
        }

        return new MethodReferenceIndex(incomingCallers, candidateCallees, externallyReferencedMethods);
    }

    // 每轮删除新暴露的候选方法，直至剩余候选仍被外部或未删除候选引用。
    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> FindUnreferencedMethodsByIteration(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
    {
        var deletedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pendingScan = new HashSet<IMethodSymbol>(candidates.Keys, SymbolEqualityComparer.Default);
        while (pendingScan.Count > 0)
        {
            var deletedThisRound = new List<IMethodSymbol>();
            foreach (var candidate in pendingScan)
            {
                if (deletedMethods.Contains(candidate) || HasRemainingReferences(candidate, deletedMethods, references))
                {
                    continue;
                }

                deletedMethods.Add(candidate);
                deletedThisRound.Add(candidate);
            }

            pendingScan.Clear();
            foreach (var deletedMethod in deletedThisRound)
            {
                foreach (var callee in references.CandidateCallees[deletedMethod])
                {
                    if (!deletedMethods.Contains(callee))
                    {
                        pendingScan.Add(callee);
                    }
                }
            }
        }

        var retainedMethods = FindExternallyReferencedClosure(candidates, references);
        var unreferencedMethods = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
        foreach (var pair in candidates)
        {
            if (!retainedMethods.Contains(pair.Key))
            {
                unreferencedMethods[pair.Key] = pair.Value;
            }
        }

        return unreferencedMethods;
    }

    private static bool HasRemainingReferences(IMethodSymbol method, IReadOnlySet<IMethodSymbol> deletedMethods, MethodReferenceIndex references)
    {
        return references.ExternallyReferencedMethods.Contains(method) ||
          references.IncomingCandidateCallers[method].Any(caller => !deletedMethods.Contains(caller));
    }

    private static HashSet<IMethodSymbol> FindExternallyReferencedClosure(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
    {
        var retained = new HashSet<IMethodSymbol>(references.ExternallyReferencedMethods, SymbolEqualityComparer.Default);
        var worklist = new Queue<IMethodSymbol>(retained);
        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            if (!candidates.ContainsKey(current))
            {
                continue;
            }

            foreach (var callee in references.CandidateCallees[current])
            {
                if (retained.Add(callee))
                {
                    worklist.Enqueue(callee);
                }
            }
        }

        return retained;
    }

    private static Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> CreateMethodReferenceSetMap(IEnumerable<IMethodSymbol> candidates)
    {
        var map = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            map[candidate] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        return map;
    }

    private static IMethodSymbol? GetContainingCandidateMethod(SemanticModel model, SyntaxNode node, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var syntax = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (syntax is null || model.GetDeclaredSymbol(syntax, CancellationToken.None) is not IMethodSymbol method)
        {
            return null;
        }

        var canonical = CanonicalizeMethodSymbol(method);
        return candidates.ContainsKey(canonical) ? canonical : null;
    }

    private static IMethodSymbol? GetReferencedCandidateMethod(SemanticModel model, SyntaxNode node)
    {
        return model.GetSymbolInfo(node, CancellationToken.None).Symbol is IMethodSymbol method
          ? CanonicalizeMethodSymbol(method)
          : null;
    }

    private static bool IsUnreferencedMethodCandidate(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
          method.DeclaredAccessibility == Accessibility.Private &&
          !method.IsOverride &&
          method.ExplicitInterfaceImplementations.Length == 0 &&
          !(string.Equals(method.Name, "Main", StringComparison.Ordinal) && method.IsStatic);
    }

    private static IMethodSymbol CanonicalizeMethodSymbol(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }

    private void ApplyDeleteClassCleanup(IReadOnlyList<DirectorySourceFile> sources, List<DirectoryFileAnalysisResult> fileResults)
    {
        var resultsByPath = fileResults.ToDictionary(file => file.FilePath, StringComparer.Ordinal);
        var projectSources = sources.ToDictionary(
          source => source.FilePath,
          source => resultsByPath.TryGetValue(source.FilePath, out var result) && result.Result.RewrittenSource is not null
            ? result.Result.RewrittenSource
            : source.Source,
          StringComparer.Ordinal);
        var cleanupState = new DeleteClassPostRewriteCleanupService.CleanupProjectState(projectSources);

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
        if (!ShouldFilterDeleteClassFilesByTargetName(options) ||
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

    private static PrototypeAnalysisResult BuildResult(int fileCount, int analyzedFileCount, IReadOnlyList<DirectoryFileAnalysisResult> fileResults)
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
          new AnalysisStats(fileCount, analyzedFileCount, 0, 0),
          RewritePlans: rewritePlans);
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

    private static bool IsTrue(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseUnreferencedMethodFastPath(IReadOnlyDictionary<string, string> options)
    {
        return IsTrue(options, "delete-unreferenced-methods") &&
          !options.ContainsKey("target-name") &&
          !options.ContainsKey("delete-class") &&
          !options.ContainsKey("unreachable-methods") &&
          !IsTrue(options, "clear-unused-interface-implementations") &&
          !IsTrue(options, "privatize-internal-only-public-methods");
    }

    private static bool ShouldUseDeleteClassCleanup(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") && !IsTrue(options, "fast-delete-class-directory");
    }

    private static bool ShouldSkipPostRewriteDiagnostics(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") && IsTrue(options, "fast-delete-class-directory");
    }

    private static bool ShouldFilterDeleteClassFilesByTargetName(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          IsTrue(options, "fast-delete-class-directory") &&
          IsTrue(options, "filter-delete-class-files-by-target-name");
    }

    private sealed record MethodReferenceIndex(
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> IncomingCandidateCallers,
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> CandidateCallees,
      IReadOnlySet<IMethodSymbol> ExternallyReferencedMethods);
}
