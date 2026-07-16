using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Builder;
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
      DeletionAnalysisRuntime runtime)
    {
        if (DeletionApplicationOptions.ShouldUseUnreferencedMethodFastPath(options))
        {
            return AnalyzeDirectoryForUnreferencedMethods(directoryPath, options, runtime);
        }

        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        if (filePaths.Count == 0)
        {
            return CreateEmptyDirectoryResult();
        }

        var sourcesByPath = await ReadSourcesAsync(filePaths, runtime.ExecutionOptions.CancellationToken);
        var analysisFilePaths = ResolveAnalysisFilePaths(filePaths, sourcesByPath, options);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var aggregation = new DirectoryResultAggregator(directoryPath, options);
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
                  editedFileResults.Add(new IndexedFileAnalysisResult(index, filePath, result));
                  return;
              }

              aggregation.AddFileResult(filePath, result);
          });

        if (DeletionApplicationOptions.ShouldUseDeleteClassUsingCleanup(options))
        {
            ApplyDeleteClassCleanup(analysisFilePaths, sourcesByPath, editedFileResults);
            foreach (var editedFileResult in editedFileResults.OrderBy(item => item.Index))
            {
                aggregation.AddFileResult(editedFileResult.FilePath, editedFileResult.Result);
            }
        }

        var result = aggregation.BuildResult(new AnalysisStats(
            filePaths.Count,
            analysisFilePaths.Count,
            0,
            0,
            0));
        var diagnostics = DeletionApplicationOptions.ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : DeletionPostRewriteDiagnostics.GetRewriteDiagnostics(
            sourcesByPath,
            aggregation.GetRewrittenSources());
        return result with { Diagnostics = diagnostics };
    }

    private PrototypeAnalysisResult AnalyzeDirectoryForUnreferencedMethods(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime)
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
              string.Empty,
              null,
              new AnalysisStats(0, 0, 0, 0, stopwatch.ElapsedMilliseconds));
        }

        var sourcesByPath = ReadSources(filePaths);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var unreferencedMethodsByPath = FindUnreferencedMethodDeclarationsByPath(compilation);
        var candidateMethodCount = CountUnreferencedMethodCandidates(compilation);
        var deletedMethodCount = unreferencedMethodsByPath.Values.Sum(methods => methods.Count);
        var aggregation = new DirectoryResultAggregator(directoryPath, options);
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
          (_, filePath, result) => aggregation.AddFileResult(filePath, result));

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
          rewriteResult.DiffText,
          null);
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
      Action<int, string, PrototypeAnalysisResult> onCompleted)
    {
        using var timingLog = PerFileTimingLog.Create(options);
        using var phaseTimingLogs = PerFilePhaseTimingLogs.Create(options);
        using var memoryDiagnosticsLog = PerFileMemoryDiagnosticsLog.Create(options);
        PrototypeAnalysisResult AnalyzeAndRecord(
          string filePath,
          SyntaxTree tree)
        {
            var initialAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            var totalStopwatch = Stopwatch.StartNew();
            var semanticModelStopwatch = Stopwatch.StartNew();
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            semanticModelStopwatch.Stop();
            var result = analyzeFile(filePath, tree, semanticModel, root);
            totalStopwatch.Stop();
            timingLog?.Write(filePath, totalStopwatch.ElapsedMilliseconds);
            phaseTimingLogs?.Write(
              filePath,
              semanticModelStopwatch.ElapsedMilliseconds,
              totalStopwatch.ElapsedMilliseconds,
              result.Timings);
            memoryDiagnosticsLog?.Write(
              filePath,
              totalStopwatch.ElapsedMilliseconds,
              GC.GetTotalAllocatedBytes(precise: false) - initialAllocatedBytes,
              result.CpgBuildTelemetry,
              result.StructureViewCacheTelemetry);
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

            return;
        }

        var publishLock = new object();
        var nextPublishIndex = 0;
        var completedFlags = new bool[filePaths.Count];
        var completedResults = new PrototypeAnalysisResult?[filePaths.Count];

        runtime.Scheduler.RunOrderedAsync(
          filePaths.Count,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          (index, cancellationToken) =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              var filePath = filePaths[index];
              var tree = trees[filePath];
              return Task.Run(
                () =>
                {
                    var result = AnalyzeAndRecord(filePath, tree);
                    lock (publishLock)
                    {
                        completedFlags[index] = true;
                        completedResults[index] = result;
                        while (nextPublishIndex < filePaths.Count &&
                               completedFlags[nextPublishIndex])
                        {
                            var readyIndex = nextPublishIndex;
                            var readyFilePath = filePaths[readyIndex];
                            var readyResult = completedResults[readyIndex]!;
                            completedResults[nextPublishIndex] = null;
                            nextPublishIndex += 1;
                            onCompleted(readyIndex, readyFilePath, readyResult);
                        }
                    }

                    return 0;
                },
                cancellationToken);
          },
          runtime.ExecutionOptions.CancellationToken)
        .GetAwaiter()
        .GetResult();
    }

    private sealed class PerFileTimingLog : IDisposable
    {
        private const int BufferCapacity = 256;
        private readonly Channel<PerFileTimingLogEntry> _entries;
        private readonly Task _writerTask;
        private readonly StreamWriter _writer;

        internal PerFileTimingLog(string filePath)
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            _writer = new StreamWriter(
              new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true));
            _entries = Channel.CreateBounded<PerFileTimingLogEntry>(new BoundedChannelOptions(BufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _writerTask = WriteEntriesAsync();
        }

        internal static PerFileTimingLog? Create(IReadOnlyDictionary<string, string> options)
        {
            var filePath = DeletionApplicationOptions.ResolvePerFileTimingLogPath(options);
            return filePath is null ? null : new PerFileTimingLog(filePath);
        }

        internal void Write(string filePath, long elapsedMilliseconds)
        {
            var entry = new PerFileTimingLogEntry(filePath, elapsedMilliseconds, DateTime.UtcNow);
            _entries.Writer.WriteAsync(entry).AsTask().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _entries.Writer.TryComplete();
            _writerTask.GetAwaiter().GetResult();
            _writer.Dispose();
        }

        private async Task WriteEntriesAsync()
        {
            await foreach (var entry in _entries.Reader.ReadAllAsync())
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(entry));
            }

            await _writer.FlushAsync();
        }
    }

    private sealed class PerFilePhaseTimingLogs : IDisposable
    {
        private static readonly string[] PhaseNames =
        {
          "semantic-model",
          "cpg-build",
          "mark",
          "propagate",
          "lift",
          "decide",
          "total"
        };

        private readonly IReadOnlyDictionary<string, PerFileTimingLog> _logs;

        private PerFilePhaseTimingLogs(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
            _logs = PhaseNames.ToDictionary(
              phaseName => phaseName,
              phaseName => new PerFileTimingLog(Path.Combine(directoryPath, $"{phaseName}.jsonl")),
              StringComparer.Ordinal);
        }

        internal static PerFilePhaseTimingLogs? Create(IReadOnlyDictionary<string, string> options)
        {
            var directoryPath = DeletionApplicationOptions.ResolvePerFilePhaseTimingLogDirectory(options);
            return directoryPath is null ? null : new PerFilePhaseTimingLogs(directoryPath);
        }

        internal void Write(
          string filePath,
          long semanticModelMilliseconds,
          long totalMilliseconds,
          AnalysisPhaseTimings? timings)
        {
            var phaseElapsedMilliseconds = new Dictionary<string, long>(StringComparer.Ordinal)
            {
              ["semantic-model"] = semanticModelMilliseconds,
              ["cpg-build"] = timings?.CpgBuildMilliseconds ?? 0,
              ["mark"] = timings?.MarkMilliseconds ?? 0,
              ["propagate"] = timings?.PropagateMilliseconds ?? 0,
              ["lift"] = timings?.LiftMilliseconds ?? 0,
              ["decide"] = timings?.DecideMilliseconds ?? 0,
              ["total"] = totalMilliseconds
            };

            foreach (var (phaseName, elapsedMilliseconds) in phaseElapsedMilliseconds)
            {
                _logs[phaseName].Write(filePath, elapsedMilliseconds);
            }
        }

        public void Dispose()
        {
            foreach (var log in _logs.Values)
            {
                log.Dispose();
            }
        }
    }

    private sealed class PerFileMemoryDiagnosticsLog : IDisposable
    {
        private const int BufferCapacity = 256;
        private readonly Channel<PerFileMemoryDiagnosticsEntry> _entries;
        private readonly Task _writerTask;
        private readonly StreamWriter _writer;

        private PerFileMemoryDiagnosticsLog(string filePath)
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            _writer = new StreamWriter(
              new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true));
            _entries = Channel.CreateBounded<PerFileMemoryDiagnosticsEntry>(new BoundedChannelOptions(BufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _writerTask = WriteEntriesAsync();
        }

        internal static PerFileMemoryDiagnosticsLog? Create(IReadOnlyDictionary<string, string> options)
        {
            var filePath = DeletionApplicationOptions.ResolvePerFileMemoryDiagnosticsLogPath(options);
            return filePath is null ? null : new PerFileMemoryDiagnosticsLog(filePath);
        }

        internal void Write(
          string filePath,
          long elapsedMilliseconds,
          long allocatedBytes,
          RoslynCpgBuildTelemetry? cpgBuildTelemetry,
          RoslynCpgStructureViewCacheTelemetry? structureViewCacheTelemetry)
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            using var process = Process.GetCurrentProcess();
            var dataFlow = cpgBuildTelemetry?.DataFlowPassTelemetry;
            var syntax = cpgBuildTelemetry?.SyntaxPassTelemetry;
            var entry = new PerFileMemoryDiagnosticsEntry(
              filePath,
              elapsedMilliseconds,
              allocatedBytes,
              memoryInfo.HeapSizeBytes,
              memoryInfo.TotalCommittedBytes,
              memoryInfo.FragmentedBytes,
              process.WorkingSet64,
              process.PrivateMemorySize64,
              GC.CollectionCount(2),
              ThreadPool.ThreadCount,
              ThreadPool.PendingWorkItemCount,
              cpgBuildTelemetry?.GraphNodeCount ?? 0,
              cpgBuildTelemetry?.GraphEdgeCount ?? 0,
              syntax?.SyntaxNodeCount ?? 0,
              syntax?.SyntaxTokenCount ?? 0,
              cpgBuildTelemetry?.PartitionCount ?? 0,
              cpgBuildTelemetry?.OperationChildBufferRentCount ?? 0,
              dataFlow?.FlowNodeCount ?? 0,
              dataFlow?.DefinitionFactCount ?? 0,
              dataFlow?.CandidateEdgeCount ?? 0,
              dataFlow?.PeakBufferedCandidateBatchCount ?? 0,
              structureViewCacheTelemetry?.RequestCount ?? 0,
              structureViewCacheTelemetry?.CacheHitCount ?? 0,
              structureViewCacheTelemetry?.CacheMissCount ?? 0,
              structureViewCacheTelemetry?.UniqueFragmentSetCount ?? 0,
              structureViewCacheTelemetry?.MaxCachedViewCount ?? 0,
              structureViewCacheTelemetry?.CacheHitRate ?? 0,
              DateTime.UtcNow);
            _entries.Writer.WriteAsync(entry).AsTask().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _entries.Writer.TryComplete();
            _writerTask.GetAwaiter().GetResult();
            _writer.Dispose();
        }

        private async Task WriteEntriesAsync()
        {
            await foreach (var entry in _entries.Reader.ReadAllAsync())
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(entry));
            }

            await _writer.FlushAsync();
        }
    }

    private sealed record PerFileTimingLogEntry(
      [property: JsonPropertyName("filePath")] string FilePath,
      [property: JsonPropertyName("elapsedMs")] long ElapsedMilliseconds,
      [property: JsonPropertyName("completedAtUtc")] DateTime CompletedAtUtc);

    private sealed record PerFileMemoryDiagnosticsEntry(
      [property: JsonPropertyName("filePath")] string FilePath,
      [property: JsonPropertyName("elapsedMs")] long ElapsedMilliseconds,
      [property: JsonPropertyName("allocatedBytes")] long AllocatedBytes,
      [property: JsonPropertyName("managedHeapBytes")] long ManagedHeapBytes,
      [property: JsonPropertyName("managedCommittedBytes")] long ManagedCommittedBytes,
      [property: JsonPropertyName("managedFragmentedBytes")] long ManagedFragmentedBytes,
      [property: JsonPropertyName("workingSetBytes")] long WorkingSetBytes,
      [property: JsonPropertyName("privateBytes")] long PrivateBytes,
      [property: JsonPropertyName("gen2CollectionCount")] int Gen2CollectionCount,
      [property: JsonPropertyName("threadPoolThreadCount")] int ThreadPoolThreadCount,
      [property: JsonPropertyName("threadPoolPendingWorkItemCount")] long ThreadPoolPendingWorkItemCount,
      [property: JsonPropertyName("cpgNodeCount")] int CpgNodeCount,
      [property: JsonPropertyName("cpgEdgeCount")] int CpgEdgeCount,
      [property: JsonPropertyName("syntaxNodeCount")] int SyntaxNodeCount,
      [property: JsonPropertyName("syntaxTokenCount")] int SyntaxTokenCount,
      [property: JsonPropertyName("cpgPartitionCount")] int CpgPartitionCount,
      [property: JsonPropertyName("operationChildBufferRentCount")] int OperationChildBufferRentCount,
      [property: JsonPropertyName("dataFlowNodeCount")] int DataFlowNodeCount,
      [property: JsonPropertyName("dataFlowDefinitionFactCount")] int DataFlowDefinitionFactCount,
      [property: JsonPropertyName("dataFlowCandidateEdgeCount")] int DataFlowCandidateEdgeCount,
      [property: JsonPropertyName("peakBufferedCandidateBatchCount")] int PeakBufferedCandidateBatchCount,
      [property: JsonPropertyName("structureViewCacheRequestCount")] long StructureViewCacheRequestCount,
      [property: JsonPropertyName("structureViewCacheHitCount")] long StructureViewCacheHitCount,
      [property: JsonPropertyName("structureViewCacheMissCount")] long StructureViewCacheMissCount,
      [property: JsonPropertyName("structureViewUniqueFragmentSetCount")] int StructureViewUniqueFragmentSetCount,
      [property: JsonPropertyName("structureViewMaxCachedViewCount")] int StructureViewMaxCachedViewCount,
      [property: JsonPropertyName("structureViewCacheHitRate")] double StructureViewCacheHitRate,
      [property: JsonPropertyName("completedAtUtc")] DateTime CompletedAtUtc);

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
              fileResult.Result,
              cleanupProjectState);
            result = _cleanupService.ApplyEmptyNamespaceCleanup(
              filePath,
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
          string.Empty,
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
        private readonly string _diffRootPath;
        private readonly List<MarkRecord> _seedMarks = new();
        private readonly List<PropagatedMarkRecord> _propagatedMarks = new();
        private readonly List<LiftedMarkRecord> _liftedMarks = new();
        private readonly List<RuleDecision> _decisions = new();
        private readonly List<RewriteEdit> _edits = new();
        private readonly Dictionary<string, string> _rewrittenSources = new(StringComparer.Ordinal);
        private readonly List<string> _diffSections = new();
        private readonly List<string> _diffFilePaths = new();
        private long _preparationMilliseconds;
        private long _cpgBuildMilliseconds;
        private long _markMilliseconds;
        private long _propagateMilliseconds;
        private long _liftMilliseconds;
        private long _decideMilliseconds;
        private long _rewriteMilliseconds;
        private long _totalMilliseconds;
        private int _timedFileCount;

        internal DirectoryResultAggregator(
          string directoryPath,
          IReadOnlyDictionary<string, string> options)
        {
            _directoryPath = directoryPath;
            _options = options;
            _diffRootPath = DeletionDiffPathResolver.ResolveDirectoryDiffRoot(directoryPath, options);
        }

        internal void AddFileResult(string filePath, PrototypeAnalysisResult result)
        {
            _seedMarks.AddRange(result.SeedMarks);
            _propagatedMarks.AddRange(result.PropagatedMarks);
            _liftedMarks.AddRange(result.LiftedMarks);
            _decisions.AddRange(result.Decisions);
            _edits.AddRange(result.Edits);
            AddTimings(result.Timings);

            if (result.Edits.Count == 0)
            {
                return;
            }

            if (result.RewrittenSource is not null)
            {
                _rewrittenSources[filePath] = result.RewrittenSource;
            }

            _diffSections.Add($"### {filePath}{Environment.NewLine}{result.DiffText}");
            if (DeletionApplicationOptions.ShouldWriteDiff(_options))
            {
                var fileDiffPath = DeletionDiffPathResolver.ResolveFileDiffPath(
                  _directoryPath,
                  filePath,
                  _diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fileDiffPath)!);
                File.WriteAllText(fileDiffPath, result.DiffText, Encoding.UTF8);
                _diffFilePaths.Add(fileDiffPath);
            }
        }

        internal Dictionary<string, string> GetRewrittenSources()
        {
            return new Dictionary<string, string>(_rewrittenSources, StringComparer.Ordinal);
        }

        internal PrototypeAnalysisResult BuildResult(AnalysisStats? stats = null)
        {
            if (DeletionApplicationOptions.ShouldWriteBack(_options))
            {
                foreach (var (filePath, rewrittenSource) in _rewrittenSources)
                {
                    File.WriteAllText(filePath, rewrittenSource, Encoding.UTF8);
                }
            }

            var diffText = string.Join(
              $"{Environment.NewLine}{Environment.NewLine}",
              _diffSections);
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
              diffText,
              diffPath,
              stats,
              Timings: BuildTimings());
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
