using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        if (DeletionApplicationOptions.ShouldUseUnreferencedMethodFastPath(options))
        {
            return AnalyzeDirectoryForUnreferencedMethods(directoryPath, options, runtime);
        }

        var filePaths = EnumerateSourceFiles(directoryPath).ToList();
        if (filePaths.Count == 0)
        {
            return CreateEmptyDirectoryResult();
        }

        var sourcesByPath = ReadSources(filePaths);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var fileResults = AnalyzeFilesInParallel(
          filePaths,
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
            root));

        if (DeletionApplicationOptions.ShouldUseDeleteClassUsingCleanup(options))
        {
            ApplyDeleteClassCleanup(filePaths, sourcesByPath, fileResults);
        }

        var result = FinalizeDirectoryResults(directoryPath, options, filePaths, fileResults);
        var diagnostics = DeletionApplicationOptions.ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : DeletionPostRewriteDiagnostics.GetRewriteDiagnostics(
            sourcesByPath,
            BuildRewrittenSources(filePaths, fileResults));
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
              new AnalysisStats(0, 0, 0, stopwatch.ElapsedMilliseconds));
        }

        var sourcesByPath = ReadSources(filePaths);
        var trees = ParseTrees(filePaths, sourcesByPath);
        var compilation = RoslynCompilationFactory.CreateCompilation(trees.Values);
        var unreferencedMethodsByPath = FindUnreferencedMethodDeclarationsByPath(compilation);
        var candidateMethodCount = CountUnreferencedMethodCandidates(compilation);
        var deletedMethodCount = unreferencedMethodsByPath.Values.Sum(methods => methods.Count);
        var fileResults = AnalyzeFilesInParallel(
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
          });

        stopwatch.Stop();
        return FinalizeDirectoryResults(
          directoryPath,
          options,
          filePaths,
          fileResults,
          new AnalysisStats(
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

    private static PrototypeAnalysisResult[] AnalyzeFilesInParallel(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, SyntaxTree> trees,
      CSharpCompilation compilation,
      IReadOnlyDictionary<string, string> options,
      DeletionAnalysisRuntime runtime,
      Func<string, SyntaxTree, SemanticModel, SyntaxNode, PrototypeAnalysisResult> analyzeFile)
    {
        if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism == 1 ||
            filePaths.Count <= 1)
        {
            var serialResults = new PrototypeAnalysisResult[filePaths.Count];
            for (var index = 0; index < filePaths.Count; index++)
            {
                var filePath = filePaths[index];
                var tree = trees[filePath];
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                serialResults[index] = analyzeFile(filePath, tree, semanticModel, root);
            }

            return serialResults;
        }

        var orderedResults = runtime.Scheduler.RunOrderedAsync(
            filePaths.Count,
            runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = filePaths[index];
                var tree = trees[filePath];
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                return Task.FromResult(analyzeFile(filePath, tree, semanticModel, root));
            },
            runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedResults.ToArray();
    }

    private PrototypeAnalysisResult FinalizeDirectoryResults(
      string directoryPath,
      IReadOnlyDictionary<string, string> options,
      IReadOnlyList<string> filePaths,
      IReadOnlyList<PrototypeAnalysisResult> fileResults,
      AnalysisStats? stats = null)
    {
        var seedMarks = new List<MarkRecord>();
        var propagatedMarks = new List<PropagatedMarkRecord>();
        var liftedMarks = new List<LiftedMarkRecord>();
        var decisions = new List<RuleDecision>();
        var edits = new List<RewriteEdit>();
        var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var diffSections = new List<string>();
        var diffRootPath = DeletionDiffPathResolver.ResolveDirectoryDiffRoot(directoryPath, options);
        var diffFilePaths = new List<string>();

        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            var result = fileResults[index];
            seedMarks.AddRange(result.SeedMarks);
            propagatedMarks.AddRange(result.PropagatedMarks);
            liftedMarks.AddRange(result.LiftedMarks);
            decisions.AddRange(result.Decisions);
            edits.AddRange(result.Edits);

            if (result.Edits.Count == 0)
            {
                continue;
            }

            rewrittenSources[filePath] = result.RewrittenSource;
            diffSections.Add($"### {filePath}{Environment.NewLine}{result.DiffText}");
            if (DeletionApplicationOptions.ShouldWriteDiff(options))
            {
                var fileDiffPath = DeletionDiffPathResolver.ResolveFileDiffPath(
                  directoryPath,
                  filePath,
                  diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fileDiffPath)!);
                File.WriteAllText(fileDiffPath, result.DiffText, Encoding.UTF8);
                diffFilePaths.Add(fileDiffPath);
            }
        }

        if (DeletionApplicationOptions.ShouldWriteBack(options))
        {
            foreach (var (filePath, rewrittenSource) in rewrittenSources)
            {
                File.WriteAllText(filePath, rewrittenSource, Encoding.UTF8);
            }
        }

        var diffText = string.Join(
          $"{Environment.NewLine}{Environment.NewLine}",
          diffSections);
        string? diffPath = null;
        if (diffFilePaths.Count > 0 && DeletionApplicationOptions.ShouldWriteDiff(options))
        {
            diffPath = diffRootPath;
        }

        return new PrototypeAnalysisResult(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          decisions,
          edits,
          $"<multi-file:{rewrittenSources.Count}>",
          diffText,
          diffPath,
          stats);
    }

    private void ApplyDeleteClassCleanup(
      IReadOnlyList<string> filePaths,
      IReadOnlyDictionary<string, string> sourcesByPath,
      PrototypeAnalysisResult[] fileResults)
    {
        var projectSourcesByPath = new Dictionary<string, string>(
          filePaths.Count,
          StringComparer.Ordinal);
        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            projectSourcesByPath[filePath] = fileResults[index].Edits.Count > 0
              ? fileResults[index].RewrittenSource
              : sourcesByPath[filePath];
        }

        var cleanupProjectState = new DeleteClassPostRewriteCleanupService.CleanupProjectState(
          projectSourcesByPath);

        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            var result = fileResults[index];
            if (result.Edits.Count == 0)
            {
                continue;
            }

            fileResults[index] = _cleanupService.ApplyUsingCleanup(
              filePath,
              result,
              cleanupProjectState);
            fileResults[index] = _cleanupService.ApplyEmptyNamespaceCleanup(
              filePath,
              fileResults[index],
              cleanupProjectState);
        }
    }

    private static Dictionary<string, string> BuildRewrittenSources(
      IReadOnlyList<string> filePaths,
      IReadOnlyList<PrototypeAnalysisResult> fileResults)
    {
        var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < filePaths.Count; index++)
        {
            var result = fileResults[index];
            if (result.Edits.Count > 0)
            {
                rewrittenSources[filePaths[index]] = result.RewrittenSource;
            }
        }

        return rewrittenSources;
    }

    private static Dictionary<string, string> ReadSources(IReadOnlyList<string> filePaths)
    {
        return filePaths.ToDictionary(
          path => path,
          path => File.ReadAllText(path),
          StringComparer.Ordinal);
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
