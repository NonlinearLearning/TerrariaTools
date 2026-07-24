using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Application.Logging;
using RoslynPrototype.Analysis;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

internal sealed class DeletionDirectoryAnalysisService
{
    private const string DeleteUnreferencedMethodMarkRuleId = "DEL-UNREF-METHOD-MARK-001";
    private const string DeleteUnreferencedMethodGroupKey = "DEL-UNREF-METHOD";

    private readonly DeletionApplicationService _application;
    private readonly PrototypeRewriter _rewriter = new();
    private readonly DeleteClassPostRewriteCleanupService _cleanupService = new();

    internal DeletionDirectoryAnalysisService(DeletionRulePipeline pipeline)
    {
        _application = new DeletionApplicationService(pipeline);
    }

    internal PrototypeAnalysisResult AnalyzeDirectory(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime)
    {
        return AnalyzeDirectoryAsync(directoryPath, options, runtime).GetAwaiter().GetResult();
    }

    internal async Task<PrototypeAnalysisResult> AnalyzeDirectoryAsync(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      AnalysisTextLogWriter? analysisWriter = null)
    {
        if (DeletionApplicationOptions.ShouldUseUnreferencedMethodFastPath(options))
        {
            return AnalyzeDirectoryForUnreferencedMethods(directoryPath, options, runtime, analysisWriter);
        }

        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        if (filePaths.Count == 0)
        {
            return CreateEmptyDirectoryResult();
        }

        var directoryAnalysisStopwatch = Stopwatch.StartNew();
        var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
        var analysisFilePaths = ResolveAnalysisFilePaths(filePaths, sourcesByPath, options);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var aggregation = new DirectoryResultAggregator(directoryPath, options, analysisWriter);
        var editedFileResults = new List<IndexedFileAnalysisResult>();
        AnalyzeFilesInParallel(
          analysisFilePaths,
          trees,
          compilation,
          options,
          runtime,
          (filePath, _, semanticModel, root) => _application.Analyze(
            sourcesByPath[filePath],
            filePath,
            options,
            runtime,
            semanticModel,
            root),
          (index, filePath, result) =>
          {
              if (DeletionApplicationOptions.ShouldUseDeleteClassUsingCleanup(options) &&
                  result.Edits.Count > 0)
              {
                  aggregation.RecordDeferredDiff(filePath, result.Edits.Count);
                  editedFileResults.Add(new IndexedFileAnalysisResult(index, filePath, result));
                  return;
              }

              aggregation.AddFileResult(filePath, result);
          },
          analysisWriter);

        if (DeletionApplicationOptions.ShouldUseDeleteClassUsingCleanup(options))
        {
            var cleanupStopwatch = Stopwatch.StartNew();
            analysisWriter?.WriteDiffCleanupStarted(editedFileResults.Count);
            ApplyDeleteClassCleanup(analysisFilePaths, sourcesByPath, editedFileResults);
            cleanupStopwatch.Stop();
            analysisWriter?.WriteDiffCleanupCompleted(editedFileResults.Count, cleanupStopwatch.ElapsedMilliseconds);
            await aggregation.AddDeferredFileResultsAsync(editedFileResults, runtime);
        }

        var result = aggregation.BuildResult(new AnalysisStats(
            filePaths.Count,
            analysisFilePaths.Count,
            0,
            0,
            0));
        directoryAnalysisStopwatch.Stop();
        var diagnosticsStopwatch = Stopwatch.StartNew();
        var diagnostics = DeletionApplicationOptions.ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : DeletionPostRewriteDiagnostics.GetRewriteDiagnostics(
            sourcesByPath,
            aggregation.GetRewrittenSources());
        diagnosticsStopwatch.Stop();
        return result with
        {
          Diagnostics = diagnostics,
          Stats = result.Stats! with
          {
            DirectoryAnalysisMilliseconds = directoryAnalysisStopwatch.ElapsedMilliseconds,
            PostRewriteDiagnosticsMilliseconds = diagnosticsStopwatch.ElapsedMilliseconds,
          },
        };
    }

    private PrototypeAnalysisResult AnalyzeDirectoryForUnreferencedMethods(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      AnalysisTextLogWriter? analysisWriter = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        if (filePaths.Count == 0)
        {
            return new PrototypeAnalysisResult(
              Array.Empty<MarkRecord>(),
              Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          Array.Empty<RuleDecision>(),
          Array.Empty<RewriteEdit>(),
          string.Empty,
          DiffDocument.Empty,
          null,
          new AnalysisStats(0, 0, 0, 0, stopwatch.ElapsedMilliseconds));
        }

        var sourcesByPath = ReadSources(filePaths);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var unreferencedMethodsByPath = FindUnreferencedMethodDeclarationsByPath(compilation);
        var candidateMethodCount = CountUnreferencedMethodCandidates(compilation);
        var deletedMethodCount = unreferencedMethodsByPath.Values.Sum(methods => methods.Count);
        var aggregation = new DirectoryResultAggregator(directoryPath, options, analysisWriter);
        AnalyzeFilesInParallel(
          filePaths,
          trees,
          compilation,
          options,
          runtime,
          (filePath, _, semanticModel, root) =>
          {
              var methodDeclarations = unreferencedMethodsByPath.TryGetValue(filePath, out var matches)
            ? matches
            : Array.Empty<MethodDeclarationSyntax>();
              return AnalyzeSingleFileForUnreferencedMethods(semanticModel, root, methodDeclarations);
          },
          (_, filePath, result) => aggregation.AddFileResult(filePath, result),
          analysisWriter);

        stopwatch.Stop();
        return aggregation.BuildResult(new AnalysisStats(
            filePaths.Count,
            filePaths.Count,
            candidateMethodCount,
            deletedMethodCount,
            stopwatch.ElapsedMilliseconds));
    }

    private PrototypeAnalysisResult AnalyzeSingleFileForUnreferencedMethods(
      SemanticModel semanticModel,
      SyntaxNode root,
      IReadOnlyList<MethodDeclarationSyntax> methodsToDelete)
    {
        var seedMarks = methodsToDelete
          .Select(method => new MarkRecord(
            DeleteUnreferencedMethodMarkRuleId,
            method,
            null,
            null,
            "Method has no references from methods that remain in the project.",
            DeleteUnreferencedMethodGroupKey))
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
      FindUnreferencedMethodDeclarationsByPath(Compilation compilation)
    {
        var candidates = BuildUnreferencedMethodCandidateMap(compilation);
        var references = BuildUnreferencedMethodReferenceIndex(compilation, candidates);
        var unreferencedMethods = FindUnreferencedMethodsByDeletionIteration(candidates, references);

        return unreferencedMethods
          .GroupBy(pair => pair.Value.SyntaxTree.FilePath ?? string.Empty, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<MethodDeclarationSyntax>)group
              .Select(pair => pair.Value)
              .OrderBy(method => method.SpanStart)
              .ToList(),
            StringComparer.Ordinal);
    }

    private static int CountUnreferencedMethodCandidates(Compilation compilation)
    {
        return BuildUnreferencedMethodCandidateMap(compilation).Count;
    }

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax>
      BuildUnreferencedMethodCandidateMap(Compilation compilation)
    {
        var candidates = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(
          SymbolEqualityComparer.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(method, CancellationToken.None) is not IMethodSymbol symbol ||
                    !IsUnreferencedMethodDeletionCandidate(symbol))
                {
                    continue;
                }

                candidates[CanonicalizeMethodSymbol(symbol)] = method;
            }
        }

        return candidates;
    }

    private static MethodReferenceIndex BuildUnreferencedMethodReferenceIndex(
      Compilation compilation,
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var incomingCandidateCallers = CreateMethodReferenceSetMap(candidates.Keys);
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

                incomingCandidateCallers[referencedMethod].Add(caller);
                candidateCallees[caller].Add(referencedMethod);
            }
        }

        return new MethodReferenceIndex(
          incomingCandidateCallers,
          candidateCallees,
          externallyReferencedMethods);
    }

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax>
      FindUnreferencedMethodsByDeletionIteration(
        IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates,
        MethodReferenceIndex references)
    {
        var deletedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pendingScan = new HashSet<IMethodSymbol>(candidates.Keys, SymbolEqualityComparer.Default);

        while (pendingScan.Count > 0)
        {
            var methodsDeletedThisRound = new List<IMethodSymbol>();
            foreach (var candidate in pendingScan)
            {
                if (deletedMethods.Contains(candidate) ||
                    HasRemainingReferences(candidate, deletedMethods, references))
                {
                    continue;
                }

                deletedMethods.Add(candidate);
                methodsDeletedThisRound.Add(candidate);
            }

            pendingScan.Clear();
            foreach (var deletedMethod in methodsDeletedThisRound)
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
        var unreferencedMethods = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(
          SymbolEqualityComparer.Default);
        foreach (var pair in candidates)
        {
            if (!retainedMethods.Contains(pair.Key))
            {
                unreferencedMethods[pair.Key] = pair.Value;
            }
        }

        return unreferencedMethods;
    }

    private static bool HasRemainingReferences(
      IMethodSymbol method,
      IReadOnlySet<IMethodSymbol> deletedMethods,
      MethodReferenceIndex references)
    {
        if (references.ExternallyReferencedMethods.Contains(method))
        {
            return true;
        }

        return references.IncomingCandidateCallers[method]
          .Any(caller => !deletedMethods.Contains(caller));
    }

    private static HashSet<IMethodSymbol> FindExternallyReferencedClosure(
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates,
      MethodReferenceIndex references)
    {
        var retained = new HashSet<IMethodSymbol>(
          references.ExternallyReferencedMethods,
          SymbolEqualityComparer.Default);
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

    private static Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> CreateMethodReferenceSetMap(
      IEnumerable<IMethodSymbol> candidates)
    {
        var map = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(
          SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            map[candidate] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        return map;
    }

    private static IMethodSymbol? GetContainingCandidateMethod(
      SemanticModel model,
      SyntaxNode node,
      IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
    {
        var containingMethodSyntax = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethodSyntax is null ||
            model.GetDeclaredSymbol(containingMethodSyntax, CancellationToken.None)
              is not IMethodSymbol containingMethod)
        {
            return null;
        }

        var canonicalContainingMethod = CanonicalizeMethodSymbol(containingMethod);
        return candidates.ContainsKey(canonicalContainingMethod)
          ? canonicalContainingMethod
          : null;
    }

    private static IMethodSymbol? GetReferencedCandidateMethod(SemanticModel model, SyntaxNode node)
    {
        var symbol = model.GetSymbolInfo(node, CancellationToken.None).Symbol;
        return symbol switch
        {
            IMethodSymbol methodSymbol => CanonicalizeMethodSymbol(methodSymbol),
            _ => null
        };
    }

    private static bool IsUnreferencedMethodDeletionCandidate(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
          method.DeclaredAccessibility == Accessibility.Private &&
          !method.IsOverride &&
          method.ExplicitInterfaceImplementations.Length == 0 &&
          !IsEntryPointShape(method);
    }

    private static bool IsEntryPointShape(IMethodSymbol method)
    {
        return string.Equals(method.Name, "Main", StringComparison.Ordinal) &&
          method.IsStatic;
    }

    private static IMethodSymbol CanonicalizeMethodSymbol(IMethodSymbol method)
    {
        return method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
    }

    private static void AnalyzeFilesInParallel(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, SyntaxTree> trees,
      CSharpCompilation compilation,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      Func<string, SyntaxTree, SemanticModel, SyntaxNode, PrototypeAnalysisResult> analyzeFile,
      Action<int, string, PrototypeAnalysisResult> onCompleted,
      AnalysisTextLogWriter? analysisWriter = null)
    {
        PrototypeAnalysisResult AnalyzeAndRecord(
          string filePath,
          SyntaxTree tree)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var result = analyzeFile(filePath, tree, semanticModel, root);
            return result.Edits.Count == 0
              ? result with { RewrittenSource = null }
              : result;
        }

        if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism == 1 ||
            filePaths.Count <= 1)
        {
            for (var index = 0; index < filePaths.Count; index++)
            {
                var filePath = filePaths[index];
                var tree = trees[filePath];
                onCompleted(index, filePath, AnalyzeAndRecord(filePath, tree));
            }

            analysisWriter?.WriteDirectoryPublicationSummary(
              filePaths.Count,
              unpublishedCountPeak: 0,
              waitToPublishMilliseconds: 0,
              oldestUnpublishedIndex: -1);
            return;
        }

        var publishLock = new object();
        var nextPublishIndex = 0;
        var completedFlags = new bool[filePaths.Count];
        var completedResults = new PrototypeAnalysisResult?[filePaths.Count];
        var analysisCompletedTimestamps = new long[filePaths.Count];
        var unpublishedCountPeak = 0;
        var waitToPublishMilliseconds = 0L;
        var oldestUnpublishedIndex = -1;

        runtime.Scheduler.RunOrderedAsync(
          filePaths.Count,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          async (index, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              var filePath = filePaths[index];
              var tree = trees[filePath];
                  using var lease = await runtime.CpgBuildAdmissionBudget
                    .AcquireAsync(
                      runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism,
                      cancellationToken)
                    .ConfigureAwait(false);
                  using var scope = runtime.PushCpgBuildAdmissionLease(lease);
                  return await Task.Run(
                    () =>
                    {
                        var result = AnalyzeAndRecord(filePath, tree);
                        var completedTimestamp = Stopwatch.GetTimestamp();
                        lock (publishLock)
                        {
                            completedFlags[index] = true;
                            completedResults[index] = result;
                            analysisCompletedTimestamps[index] = completedTimestamp;
                            var unpublishedCount = 0;
                            var oldestUnpublished = -1;
                            for (var candidateIndex = nextPublishIndex;
                                 candidateIndex < filePaths.Count;
                                 candidateIndex++)
                            {
                                if (!completedFlags[candidateIndex])
                                {
                                    continue;
                                }

                                unpublishedCount += 1;
                                oldestUnpublished = oldestUnpublished < 0
                                  ? candidateIndex
                                  : oldestUnpublished;
                            }

                            if (unpublishedCount > unpublishedCountPeak)
                            {
                                unpublishedCountPeak = unpublishedCount;
                                oldestUnpublishedIndex = oldestUnpublished;
                            }

                            while (nextPublishIndex < filePaths.Count &&
                                   completedFlags[nextPublishIndex])
                            {
                                var readyIndex = nextPublishIndex;
                                var readyFilePath = filePaths[readyIndex];
                                var readyResult = completedResults[readyIndex]!;
                                var publicationTimestamp = Stopwatch.GetTimestamp();
                                waitToPublishMilliseconds += (long)Stopwatch
                                  .GetElapsedTime(analysisCompletedTimestamps[readyIndex], publicationTimestamp)
                                  .TotalMilliseconds;
                                completedResults[nextPublishIndex] = null;
                                nextPublishIndex += 1;
                                onCompleted(readyIndex, readyFilePath, readyResult);
                            }
                        }

                        return 0;
                    },
                    cancellationToken)
                    .ConfigureAwait(false);
          },
          runtime.ExecutionOptions.CancellationToken)
        .GetAwaiter()
        .GetResult();
        analysisWriter?.WriteDirectoryPublicationSummary(
          filePaths.Count,
          unpublishedCountPeak,
          waitToPublishMilliseconds,
          oldestUnpublishedIndex);
    }

    private static IReadOnlyList<string> ResolveAnalysisFilePaths(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, string> sourcesByPath,
      IReadOnlyDictionary<string, string> options)
    {
        if (!DeletionApplicationOptions.ShouldFilterDeleteClassFilesByTargetName(options) ||
            !options.TryGetValue("delete-class", out var targetClassName) ||
            string.IsNullOrWhiteSpace(targetClassName))
        {
            return filePaths;
        }

        var filteredFilePaths = filePaths
          .Where(path => SourceMentionsTargetName(sourcesByPath[path], targetClassName))
          .ToList();
        return filteredFilePaths.Count > 0 ? filteredFilePaths : filePaths;
    }

    private static bool SourceMentionsTargetName(string source, string targetClassName)
    {
        return source.Contains(targetClassName, StringComparison.Ordinal);
    }

    private void ApplyDeleteClassCleanup(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, string> sourcesByPath,
      List<IndexedFileAnalysisResult> fileResults)
    {
        var projectSourcesByPath = new Dictionary<string, string>(
          filePaths.Count,
          StringComparer.Ordinal);
        var editedResultsByPath = fileResults.ToDictionary(
          item => item.FilePath,
          item => item,
          StringComparer.Ordinal);
        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            projectSourcesByPath[filePath] = editedResultsByPath.TryGetValue(filePath, out var editedFileResult)
              ? editedFileResult.Result.RewrittenSource!
              : sourcesByPath[filePath];
        }

        var cleanupProjectState = new DeleteClassPostRewriteCleanupService.CleanupProjectState(
          projectSourcesByPath);

        for (var index = 0; index < fileResults.Count; index++)
        {
            var fileResult = fileResults[index];
            var filePath = fileResult.FilePath;
            var result = _cleanupService.ApplyUsingCleanup(
              filePath,
              sourcesByPath[filePath],
              fileResult.Result,
              cleanupProjectState);
            result = _cleanupService.ApplyEmptyNamespaceCleanup(
              filePath,
              sourcesByPath[filePath],
              result,
              cleanupProjectState);
            fileResults[index] = fileResult with { Result = result };
        }
    }

    private static Dictionary<string, string> ReadSources(IReadOnlyList<string> filePaths)
    {
        return filePaths.ToDictionary(
          path => path,
          path => File.ReadAllText(path),
          StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, string>> ReadSourcesAsync(
      IReadOnlyList<string> filePaths,
      CancellationToken cancellationToken)
    {
        var sources = new string[filePaths.Count];
        var nextIndex = -1;
        var workerCount = Math.Min(filePaths.Count, Math.Max(1, Environment.ProcessorCount));
        var workers = new Task[workerCount];
        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            workers[workerIndex] = ReadNextSourceAsync();
        }

        await Task.WhenAll(workers);
        return filePaths
          .Select((filePath, index) => new KeyValuePair<string, string>(filePath, sources[index]))
          .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        async Task ReadNextSourceAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= filePaths.Count)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                sources[index] = await File.ReadAllTextAsync(filePaths[index], cancellationToken);
            }
        }
    }

    private static Dictionary<string, SyntaxTree> ParseTrees(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, string> sourcesByPath)
    {
        return filePaths.ToDictionary(
          path => path,
          path => CSharpSyntaxTree.ParseText(sourcesByPath[path], path: path),
          StringComparer.Ordinal);
    }

    private static PrototypeAnalysisResult CreateEmptyDirectoryResult()
    {
        return new PrototypeAnalysisResult(
          Array.Empty<MarkRecord>(),
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          Array.Empty<RuleDecision>(),
          Array.Empty<RewriteEdit>(),
          string.Empty,
          DiffDocument.Empty,
          null);
    }

    private sealed record IndexedFileAnalysisResult(
      int Index,
      string FilePath,
      PrototypeAnalysisResult Result);

    private sealed class DirectoryResultAggregator
    {
        private readonly string _directoryPath;
        private readonly IReadOnlyDictionary<string, string> _options;
        private readonly AnalysisTextLogWriter? _analysisWriter;
        private readonly DiffBuilder _diffBuilder = new();
        private readonly TextDiffRenderer _textDiffRenderer = new();
        private readonly string _diffRootPath;
        private readonly string _diffView;
        private readonly List<MarkRecord> _seedMarks = new();
        private readonly List<PropagatedMarkRecord> _propagatedMarks = new();
        private readonly List<LiftedMarkRecord> _liftedMarks = new();
        private readonly List<RuleDecision> _decisions = new();
        private readonly List<RewriteEdit> _edits = new();
        private readonly List<DiffDocument> _diffDocuments = new();
        private readonly Dictionary<string, string> _rewrittenSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<RewritePlanEdit>> _rewritePlans = new(StringComparer.Ordinal);
        private readonly List<string> _diffFilePaths = new();
        private readonly Stopwatch _diffStopwatch = new();
        private RoslynCpgBuildTelemetry? _cpgBuildTelemetry;
        private MarkAnalysisTelemetry? _markAnalysisTelemetry;
        private long _preparationMilliseconds;
        private long _cpgBuildMilliseconds;
        private long _markMilliseconds;
        private long _propagateMilliseconds;
        private long _liftMilliseconds;
        private long _decideMilliseconds;
        private long _rewriteMilliseconds;
        private long _totalMilliseconds;
        private int _timedFileCount;
        private int _deferredDiffFileCount;
        private int _writtenDiffFileCount;
        private long _diffWriteMilliseconds;

        private sealed record DiffWriteWorkItem(
          int Index,
          string FilePath,
          string DiffFilePath,
          DiffDocument DiffDocument,
          int EditCount);

        private sealed record DiffWriteResult(
          int Index,
          string FilePath,
          string DiffFilePath,
          int EditCount);

        internal DirectoryResultAggregator(
          string directoryPath,
          IReadOnlyDictionary<string, string> options,
          AnalysisTextLogWriter? analysisWriter)
        {
            _directoryPath = directoryPath;
            _options = options;
            _analysisWriter = analysisWriter;
            _diffView = DeletionApplicationOptions.ResolveDiffView(options);
            _diffRootPath = DeletionDiffPathResolver.ResolveDirectoryDiffRoot(directoryPath, options);
        }

        internal void RecordDeferredDiff(string filePath, int editCount)
        {
            if (_deferredDiffFileCount == 0)
            {
                _diffStopwatch.Start();
            }

            _deferredDiffFileCount++;
            _analysisWriter?.WriteDiffPending(filePath, editCount);
        }

        internal void AddFileResult(string filePath, PrototypeAnalysisResult result)
        {
            AddFileResultCore(filePath, result);

            if (result.Edits.Count == 0 || !DeletionApplicationOptions.ShouldWriteDiff(_options))
            {
                return;
            }

            var writtenDiff = WriteDiff(new DiffWriteWorkItem(
              0,
              filePath,
              DeletionDiffPathResolver.ResolveFileDiffPath(_directoryPath, filePath, _diffRootPath),
              result.Diff,
              result.Edits.Count));
            CommitWrittenDiff(writtenDiff);
        }

        internal async Task AddDeferredFileResultsAsync(
          IReadOnlyList<IndexedFileAnalysisResult> fileResults,
          DeletionAnalysisRuntime runtime)
        {
            var orderedFileResults = fileResults.OrderBy(item => item.Index).ToArray();
            if (!DeletionApplicationOptions.ShouldWriteDiff(_options))
            {
                foreach (var fileResult in orderedFileResults)
                {
                    AddFileResultCore(fileResult.FilePath, fileResult.Result);
                }

                return;
            }

            var workItems = orderedFileResults
              .Select(fileResult => new DiffWriteWorkItem(
                fileResult.Index,
                fileResult.FilePath,
                DeletionDiffPathResolver.ResolveFileDiffPath(
                  _directoryPath,
                  fileResult.FilePath,
                  _diffRootPath),
                fileResult.Result.Diff,
                fileResult.Result.Edits.Count))
              .ToArray();
            var writeStopwatch = Stopwatch.StartNew();
            var writtenDiffs = await runtime.Scheduler.RunOrderedAsync(
              workItems.Length,
              runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
              (index, cancellationToken) =>
              {
                  cancellationToken.ThrowIfCancellationRequested();
                  return Task.FromResult(WriteDiff(workItems[index]));
              },
              runtime.ExecutionOptions.CancellationToken);
            writeStopwatch.Stop();
            _diffWriteMilliseconds = writeStopwatch.ElapsedMilliseconds;

            for (var index = 0; index < orderedFileResults.Length; index++)
            {
                var fileResult = orderedFileResults[index];
                AddFileResultCore(fileResult.FilePath, fileResult.Result);
                CommitWrittenDiff(writtenDiffs[index]);
            }
        }

        private void AddFileResultCore(string filePath, PrototypeAnalysisResult result)
        {
            _seedMarks.AddRange(result.SeedMarks);
            _propagatedMarks.AddRange(result.PropagatedMarks);
            _liftedMarks.AddRange(result.LiftedMarks);
            _decisions.AddRange(result.Decisions);
            _edits.AddRange(result.Edits);
            if (result.RewritePlans is not null)
            {
                foreach (var rewritePlan in result.RewritePlans)
                {
                    if (rewritePlan.Operations.Count > 0)
                    {
                        _rewritePlans[rewritePlan.FilePath] = rewritePlan.Operations;
                    }
                }
            }
            if (result.Diff.Files.Count > 0)
            {
                _diffDocuments.Add(result.Diff);
            }
            AddTimings(result.Timings);
            AddCpgBuildTelemetry(result.CpgBuildTelemetry);
            AddMarkAnalysisTelemetry(result.MarkAnalysisTelemetry);
            _analysisWriter?.WriteResult(filePath, result);

            if (result.Edits.Count == 0)
            {
                return;
            }

            if (result.RewrittenSource is not null)
            {
                _rewrittenSources[filePath] = result.RewrittenSource;
            }
        }

        private DiffWriteResult WriteDiff(DiffWriteWorkItem workItem)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(workItem.DiffFilePath)!);
            File.WriteAllText(
              workItem.DiffFilePath,
              _textDiffRenderer.Render(workItem.DiffDocument.Files.Single(), _diffView),
              Encoding.UTF8);
            return new DiffWriteResult(
              workItem.Index,
              workItem.FilePath,
              workItem.DiffFilePath,
              workItem.EditCount);
        }

        private void CommitWrittenDiff(DiffWriteResult writtenDiff)
        {
            _diffFilePaths.Add(writtenDiff.DiffFilePath);
            _writtenDiffFileCount++;
            _analysisWriter?.WriteDiffWritten(
              writtenDiff.FilePath,
              writtenDiff.DiffFilePath,
              writtenDiff.EditCount);
        }

        internal Dictionary<string, string> GetRewrittenSources()
        {
            return new Dictionary<string, string>(_rewrittenSources, StringComparer.Ordinal);
        }

        internal PrototypeAnalysisResult BuildResult(AnalysisStats? stats = null)
        {
            if (_deferredDiffFileCount > 0)
            {
                _diffStopwatch.Stop();
                _analysisWriter?.WriteDiffSummary(
                  _deferredDiffFileCount,
                  _writtenDiffFileCount,
                  _diffStopwatch.ElapsedMilliseconds,
                  _diffWriteMilliseconds);
            }

            if (DeletionApplicationOptions.ShouldWriteBack(_options))
            {
                foreach (var (filePath, rewrittenSource) in _rewrittenSources)
                {
                    File.WriteAllText(filePath, rewrittenSource, Encoding.UTF8);
                }
            }

            var diff = _diffBuilder.Combine(_diffDocuments);
            var diffPath = _diffFilePaths.Count > 0 && DeletionApplicationOptions.ShouldWriteDiff(_options)
              ? _diffRootPath
              : null;

            return new PrototypeAnalysisResult(
              _seedMarks,
              _propagatedMarks,
              _liftedMarks,
              _decisions,
              _edits,
              $"<multi-file:{_rewrittenSources.Count}>",
              diff,
              diffPath,
              stats,
              Timings: BuildTimings(),
              CpgBuildTelemetry: BuildCpgBuildTelemetry(),
              MarkAnalysisTelemetry: BuildMarkAnalysisTelemetry(),
              RewritePlans: _rewritePlans
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PrototypeFileRewritePlan(pair.Key, pair.Value))
                .ToArray());
        }

        private void AddTimings(AnalysisPhaseTimings? timings)
        {
            if (timings is null)
            {
                return;
            }

            _timedFileCount += 1;
            _preparationMilliseconds += timings.PreparationMilliseconds;
            _cpgBuildMilliseconds += timings.CpgBuildMilliseconds;
            _markMilliseconds += timings.MarkMilliseconds;
            _propagateMilliseconds += timings.PropagateMilliseconds;
            _liftMilliseconds += timings.LiftMilliseconds;
            _decideMilliseconds += timings.DecideMilliseconds;
            _rewriteMilliseconds += timings.RewriteMilliseconds;
            _totalMilliseconds += timings.TotalMilliseconds;
        }

        private AnalysisPhaseTimings? BuildTimings()
        {
            if (_timedFileCount == 0)
            {
                return null;
            }

            return new AnalysisPhaseTimings(
              _preparationMilliseconds,
              _cpgBuildMilliseconds,
              _markMilliseconds,
              _propagateMilliseconds,
              _liftMilliseconds,
              _decideMilliseconds,
              _rewriteMilliseconds,
              _totalMilliseconds);
        }

        private void AddCpgBuildTelemetry(RoslynCpgBuildTelemetry? telemetry)
        {
            if (telemetry is null)
            {
                return;
            }

            if (_cpgBuildTelemetry is null)
            {
                _cpgBuildTelemetry = telemetry;
                return;
            }

            _cpgBuildTelemetry = _cpgBuildTelemetry with
            {
                SourceLineCount = _cpgBuildTelemetry.SourceLineCount + telemetry.SourceLineCount,
                PartitionCount = _cpgBuildTelemetry.PartitionCount + telemetry.PartitionCount,
                MaxDegreeOfParallelism = Math.Max(
                  _cpgBuildTelemetry.MaxDegreeOfParallelism,
                  telemetry.MaxDegreeOfParallelism),
                OperationBuildElapsedMilliseconds =
                  _cpgBuildTelemetry.OperationBuildElapsedMilliseconds +
                  telemetry.OperationBuildElapsedMilliseconds,
                SyntaxBuildElapsedMilliseconds =
                  _cpgBuildTelemetry.SyntaxBuildElapsedMilliseconds +
                  telemetry.SyntaxBuildElapsedMilliseconds,
                DataFlowBuildElapsedMilliseconds =
                  _cpgBuildTelemetry.DataFlowBuildElapsedMilliseconds +
                  telemetry.DataFlowBuildElapsedMilliseconds,
                FreezeQueryIndexElapsedMilliseconds =
                  _cpgBuildTelemetry.FreezeQueryIndexElapsedMilliseconds +
                  telemetry.FreezeQueryIndexElapsedMilliseconds,
                FreezeTelemetry = MergeFreezeTelemetry(
                  _cpgBuildTelemetry.FreezeTelemetry,
                  telemetry.FreezeTelemetry),
                OperationOrderedWindow = MergeOrderedWindowTelemetry(
                  _cpgBuildTelemetry.OperationOrderedWindow,
                  telemetry.OperationOrderedWindow),
                CfgSensitiveOrderedWindow = MergeOrderedWindowTelemetry(
                  _cpgBuildTelemetry.CfgSensitiveOrderedWindow,
                  telemetry.CfgSensitiveOrderedWindow),
                AdmissionTelemetry = MergeAdmissionTelemetry(
                  _cpgBuildTelemetry.AdmissionTelemetry,
                  telemetry.AdmissionTelemetry),
                GraphNodeCount = _cpgBuildTelemetry.GraphNodeCount + telemetry.GraphNodeCount,
                GraphEdgeCount = _cpgBuildTelemetry.GraphEdgeCount + telemetry.GraphEdgeCount
            };
        }

        private static RoslynCpgFreezeTelemetry MergeFreezeTelemetry(
          RoslynCpgFreezeTelemetry left,
          RoslynCpgFreezeTelemetry right)
        {
            return new RoslynCpgFreezeTelemetry(
              left.TotalElapsedMilliseconds + right.TotalElapsedMilliseconds,
              left.AssignDeterministicNodeIdsElapsedMilliseconds + right.AssignDeterministicNodeIdsElapsedMilliseconds,
              left.CreateAnchorsElapsedMilliseconds + right.CreateAnchorsElapsedMilliseconds,
              left.CreateNodeIdTableElapsedMilliseconds + right.CreateNodeIdTableElapsedMilliseconds,
              left.RemapNodesElapsedMilliseconds + right.RemapNodesElapsedMilliseconds,
              left.RemapEdgesElapsedMilliseconds + right.RemapEdgesElapsedMilliseconds,
              left.BuildQueryIndexElapsedMilliseconds + right.BuildQueryIndexElapsedMilliseconds,
              left.PopulateEdgeIndexBucketsElapsedMilliseconds + right.PopulateEdgeIndexBucketsElapsedMilliseconds,
              left.OrderEdgesElapsedMilliseconds + right.OrderEdgesElapsedMilliseconds,
              left.OrderNodesElapsedMilliseconds + right.OrderNodesElapsedMilliseconds,
              left.SnapshotHashElapsedMilliseconds + right.SnapshotHashElapsedMilliseconds,
              left.BuildAdjacencyElapsedMilliseconds + right.BuildAdjacencyElapsedMilliseconds,
              left.BuildKindAdjacencyElapsedMilliseconds + right.BuildKindAdjacencyElapsedMilliseconds,
              left.BuildEdgeKindIndexElapsedMilliseconds + right.BuildEdgeKindIndexElapsedMilliseconds,
              left.BuildNodeKindIndexElapsedMilliseconds + right.BuildNodeKindIndexElapsedMilliseconds,
              left.BuildFilePathIndexElapsedMilliseconds + right.BuildFilePathIndexElapsedMilliseconds,
              left.NodeCount + right.NodeCount,
              left.EdgeCount + right.EdgeCount,
              left.DistinctAnchorCount + right.DistinctAnchorCount);
        }

        private static RoslynCpgOrderedWorkWindowTelemetry MergeOrderedWindowTelemetry(
          RoslynCpgOrderedWorkWindowTelemetry? left,
          RoslynCpgOrderedWorkWindowTelemetry? right)
        {
            var first = left ?? RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
            var second = right ?? RoslynCpgOrderedWorkWindowTelemetry.CreateDefault();
            return new RoslynCpgOrderedWorkWindowTelemetry(
              Math.Max(first.ActiveWorkerPeak, second.ActiveWorkerPeak),
              Math.Max(first.CompletedButUncommittedPeak, second.CompletedButUncommittedPeak),
              Math.Max(first.CompletedRecordCountPeak, second.CompletedRecordCountPeak),
              first.CommitWaitMilliseconds + second.CommitWaitMilliseconds,
                first.WindowBlockedMilliseconds + second.WindowBlockedMilliseconds);
        }

        private static CpgBuildAdmissionTelemetry? MergeAdmissionTelemetry(
          CpgBuildAdmissionTelemetry? left,
          CpgBuildAdmissionTelemetry? right)
        {
            if (left is null)
            {
                return right;
            }

            if (right is null)
            {
                return left;
            }

            return new CpgBuildAdmissionTelemetry(
              Math.Max(left.RequestedDegree, right.RequestedDegree),
              Math.Max(left.GrantedDegree, right.GrantedDegree),
              left.WaitMilliseconds + right.WaitMilliseconds,
              Math.Max(left.ActiveLeaseCountAtGrant, right.ActiveLeaseCountAtGrant),
              Math.Max(left.GrantedDegreeHighWaterMark, right.GrantedDegreeHighWaterMark),
              left.Policy,
              Math.Max(left.MaxDegreePerLease, right.MaxDegreePerLease));
        }

        private void AddMarkAnalysisTelemetry(MarkAnalysisTelemetry? telemetry)
        {
            if (telemetry is null)
            {
                return;
            }

            if (_markAnalysisTelemetry is null)
            {
                _markAnalysisTelemetry = telemetry;
                return;
            }

            _markAnalysisTelemetry = new MarkAnalysisTelemetry(
              _markAnalysisTelemetry.AtomicCandidateIndexHitCount +
                telemetry.AtomicCandidateIndexHitCount,
              _markAnalysisTelemetry.AtomicCandidateIndexMissCount +
                telemetry.AtomicCandidateIndexMissCount,
              _markAnalysisTelemetry.OperationLookupCacheHitCount +
                telemetry.OperationLookupCacheHitCount,
              _markAnalysisTelemetry.OperationLookupCacheMissCount +
                telemetry.OperationLookupCacheMissCount,
              _markAnalysisTelemetry.GraphBindingIndexHitCount +
                telemetry.GraphBindingIndexHitCount,
              _markAnalysisTelemetry.GraphBindingIndexMissCount +
                telemetry.GraphBindingIndexMissCount,
              _markAnalysisTelemetry.RegionCacheHitCount +
                telemetry.RegionCacheHitCount,
              _markAnalysisTelemetry.RegionCacheMissCount +
                telemetry.RegionCacheMissCount,
              _markAnalysisTelemetry.TargetMatchCacheHitCount +
                telemetry.TargetMatchCacheHitCount,
              _markAnalysisTelemetry.TargetMatchCacheMissCount +
                telemetry.TargetMatchCacheMissCount,
              _markAnalysisTelemetry.SliceQueryCacheHitCount +
                telemetry.SliceQueryCacheHitCount,
              _markAnalysisTelemetry.SliceQueryCacheMissCount +
                telemetry.SliceQueryCacheMissCount,
              _markAnalysisTelemetry.AtomicCandidateCount +
                telemetry.AtomicCandidateCount,
              _markAnalysisTelemetry.AtomicCandidatesReturnedCount +
                telemetry.AtomicCandidatesReturnedCount,
              _markAnalysisTelemetry.RegionFactsCreatedCount +
                telemetry.RegionFactsCreatedCount,
              _markAnalysisTelemetry.RegionFactsReusedCount +
                telemetry.RegionFactsReusedCount,
              _markAnalysisTelemetry.TargetMatchQueryCount +
                telemetry.TargetMatchQueryCount,
              _markAnalysisTelemetry.TargetMatchKeyCreatedCount +
                telemetry.TargetMatchKeyCreatedCount,
              MergeRuleTelemetry(
                _markAnalysisTelemetry.RuleTelemetry,
                telemetry.RuleTelemetry),
              MergeAtomicCandidatesReturnedByKind(
                _markAnalysisTelemetry.AtomicCandidatesReturnedByKind,
                telemetry.AtomicCandidatesReturnedByKind));
        }

        private RoslynCpgBuildTelemetry? BuildCpgBuildTelemetry()
        {
            return _cpgBuildTelemetry;
        }

        private MarkAnalysisTelemetry? BuildMarkAnalysisTelemetry()
        {
            return _markAnalysisTelemetry;
        }

        private static IReadOnlyList<MarkRuleTelemetry> MergeRuleTelemetry(
          IReadOnlyList<MarkRuleTelemetry> left,
          IReadOnlyList<MarkRuleTelemetry> right)
        {
            var merged = new Dictionary<(int RuleOrder, string RuleId, string? GroupKey), MarkRuleTelemetry>();

            foreach (var telemetry in left.Concat(right))
            {
                var key = (telemetry.RuleOrder, telemetry.RuleId, telemetry.GroupKey);
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = telemetry;
                    continue;
                }

                merged[key] = existing with
                {
                    ElapsedMilliseconds =
                      existing.ElapsedMilliseconds + telemetry.ElapsedMilliseconds,
                    CandidateMarkCount =
                      existing.CandidateMarkCount + telemetry.CandidateMarkCount,
                    AcceptedMarkCount =
                      existing.AcceptedMarkCount + telemetry.AcceptedMarkCount,
                    GraphBindingFallbackCount =
                      existing.GraphBindingFallbackCount +
                      telemetry.GraphBindingFallbackCount,
                    AtomicCandidateIndexHitCount =
                      existing.AtomicCandidateIndexHitCount +
                      telemetry.AtomicCandidateIndexHitCount,
                    AtomicCandidateIndexMissCount =
                      existing.AtomicCandidateIndexMissCount +
                      telemetry.AtomicCandidateIndexMissCount,
                    OperationLookupCacheHitCount =
                      existing.OperationLookupCacheHitCount +
                      telemetry.OperationLookupCacheHitCount,
                    OperationLookupCacheMissCount =
                      existing.OperationLookupCacheMissCount +
                      telemetry.OperationLookupCacheMissCount,
                    GraphBindingIndexHitCount =
                      existing.GraphBindingIndexHitCount +
                      telemetry.GraphBindingIndexHitCount,
                    GraphBindingIndexMissCount =
                      existing.GraphBindingIndexMissCount +
                      telemetry.GraphBindingIndexMissCount,
                    RegionCacheHitCount =
                      existing.RegionCacheHitCount +
                      telemetry.RegionCacheHitCount,
                    RegionCacheMissCount =
                      existing.RegionCacheMissCount +
                      telemetry.RegionCacheMissCount,
                    TargetMatchCacheHitCount =
                      existing.TargetMatchCacheHitCount +
                      telemetry.TargetMatchCacheHitCount,
                    TargetMatchCacheMissCount =
                      existing.TargetMatchCacheMissCount +
                      telemetry.TargetMatchCacheMissCount
                };
            }

            return merged.Values
              .OrderBy(item => item.RuleOrder)
              .ThenBy(item => item.RuleId, StringComparer.Ordinal)
              .ToList();
        }

        private static IReadOnlyDictionary<string, long> MergeAtomicCandidatesReturnedByKind(
          IReadOnlyDictionary<string, long> left,
          IReadOnlyDictionary<string, long> right)
        {
            return left.Concat(right)
              .GroupBy(entry => entry.Key, StringComparer.Ordinal)
              .ToDictionary(
                group => group.Key,
                group => group.Sum(entry => entry.Value),
                StringComparer.Ordinal);
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
          .Where(path => !IsIgnoredDirectoryPath(path))
          .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsIgnoredDirectoryPath(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
          segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private sealed record MethodReferenceIndex(
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> IncomingCandidateCallers,
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> CandidateCallees,
      IReadOnlySet<IMethodSymbol> ExternallyReferencedMethods);
}
