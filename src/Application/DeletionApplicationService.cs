using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using MinimalRoslynCpg.Builder;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

public sealed class DeletionApplicationService
{
    private readonly IReadOnlyList<RuleDefinitionMark> _markers;
    private readonly IReadOnlyList<RuleDefinitionPropagate> _propagators;
    private readonly IReadOnlyList<RuleDefinitionLift> _lifters;
    private readonly IReadOnlyList<RuleDefinitionPropose> _proposers;
    private readonly MarkingEngine _markingEngine;
    private readonly PropagationEngine _propagationEngine;
    private readonly MarkLiftingEngine _markLiftingEngine;
    private readonly RuleDecisionEngine _decisionEngine;
    private readonly PrototypeRewriter _rewriter;

    public DeletionApplicationService(RuleRegistrySet rules)
      : this(rules.Markers, rules.Propagators, rules.Lifters, rules.Proposers)
    {
    }

    public DeletionApplicationService(IReadOnlyList<RuleDefinitionMark> markers, IReadOnlyList<RuleDefinitionPropagate> propagators, IReadOnlyList<RuleDefinitionLift> lifters, IReadOnlyList<RuleDefinitionPropose> proposers)
    {
        _markers = markers;
        _propagators = propagators;
        _lifters = lifters;
        _proposers = proposers;
        _markingEngine = new MarkingEngine();
        _propagationEngine = new PropagationEngine();
        _markLiftingEngine = new MarkLiftingEngine();
        _decisionEngine = new RuleDecisionEngine();
        _rewriter = new PrototypeRewriter();
    }

    public PrototypeAnalysisResult Analyze(string source, string filePath, IReadOnlyDictionary<string, string> options)
    {
        return RunAnalysis(source, filePath, options);
    }

    public PrototypeAnalysisResult AnalyzeFromArgs(string[] args)
    {
        var inputPath = args.FirstOrDefault(path => !path.StartsWith("--", StringComparison.Ordinal));
        var options = ParseOptions(args);
        if (TryParseDisabledRuleTypes(options, out var disabledRuleTypes))
        {
            return new DeletionApplicationService(RuleRegistry.CreateDefaultRules(disabledRuleTypes))
              .AnalyzeFromArgsWithoutRegistryRebuild(args);
        }

        return AnalyzeFromArgsWithoutRegistryRebuild(args);
    }

    private PrototypeAnalysisResult AnalyzeFromArgsWithoutRegistryRebuild(string[] args)
    {
        var inputPath = args.FirstOrDefault(path => !path.StartsWith("--", StringComparison.Ordinal));
        var options = ParseOptions(args);
        if (inputPath is not null && Directory.Exists(inputPath))
        {
            return AnalyzeDirectory(inputPath, options);
        }

        var source = inputPath is not null && File.Exists(inputPath)
          ? File.ReadAllText(inputPath)
          : GetDefaultSource();
        var filePath = inputPath ?? "demo.cs";
        var result = Analyze(source, filePath, options);

        if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
        {
            return result;
        }

        if (ShouldWriteBack(options))
        {
            File.WriteAllText(inputPath, result.RewrittenSource, Encoding.UTF8);
        }

        if (!ShouldWriteDiff(options))
        {
            return result;
        }

        var diffPath = ResolveDiffPath(inputPath, options);
        File.WriteAllText(diffPath, result.DiffText, Encoding.UTF8);
        return result with { DiffFilePath = diffPath };
    }

    private PrototypeAnalysisResult RunAnalysis(string source, string filePath, IReadOnlyDictionary<string, string> options)
    {
        var analysisContext = BuildAnalysisContext(source, filePath, options);
        return RunAnalysis(analysisContext);
    }

    private PrototypeAnalysisResult RunAnalysis(string source, string filePath, IReadOnlyDictionary<string, string> options, SemanticModel semanticModel, SyntaxNode root)
    {
        var analysisContext = BuildAnalysisContext(source, filePath, options, semanticModel, root);
        return RunAnalysis(analysisContext);
    }

    public IReadOnlyList<string> FormatResult(PrototypeAnalysisResult result)
    {
        var effectiveMarks = result.SeedMarks.Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode))
          .ToList();
        var lines = new List<string>
    {
      $"SeedMarks: {result.SeedMarks.Count}",
      $"PropagatedMarks: {result.PropagatedMarks.Count}",
      $"LiftedMarks: {result.LiftedMarks.Count}",
      $"EffectiveMarks: {effectiveMarks.Count}"
    };

        foreach (var mark in result.SeedMarks)
        {
            lines.Add(
              $"SEED [{GetNodeKindText(mark.SyntaxNode)}] {mark.SyntaxNode.Span}: {mark.Reason}");
        }

        foreach (var mark in result.PropagatedMarks)
        {
            lines.Add(
              $"PROPAGATED [{GetNodeKindText(mark.Mark.SyntaxNode)}] {mark.Mark.SyntaxNode.Span} from {mark.SourceMark.SyntaxNode.Span} depth={mark.Depth}");
        }

        foreach (var mark in result.LiftedMarks)
        {
            lines.Add(
              $"LIFTED [{GetNodeKindText(mark.Mark.SyntaxNode)}] {mark.Mark.SyntaxNode.Span} from {mark.SourceMark.SyntaxNode.Span} depth={mark.Depth}");
        }

        lines.Add($"Decisions: {result.Decisions.Count}");
        foreach (var decision in result.Decisions)
        {
            lines.Add(
              $"DECISION {decision.Action} [{GetNodeKindText(decision.FinalNode)}] {decision.FinalNode.Span}: {decision.Reason}");
        }

        lines.Add($"Edits: {result.Edits.Count}");
        if (!string.IsNullOrEmpty(result.DiffFilePath))
        {
            lines.Add($"DiffFile: {result.DiffFilePath}");
        }

        if (result.Stats is not null)
        {
            lines.Add($"ScannedFiles: {result.Stats.ScannedFileCount}");
            lines.Add($"CandidateMethods: {result.Stats.CandidateMethodCount}");
            lines.Add($"DeletedMethods: {result.Stats.DeletedMethodCount}");
            lines.Add($"ElapsedMs: {result.Stats.ElapsedMilliseconds}");
        }

        var diagnostics = result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>();
        lines.Add($"Diagnostics: {diagnostics.Count}");
        foreach (var diagnostic in diagnostics)
        {
            lines.Add(
              $"DIAGNOSTIC {diagnostic.Severity} {diagnostic.Id} {diagnostic.FilePath}:{diagnostic.Start}..{diagnostic.End}: {diagnostic.Message}");
        }

        lines.Add("--- Rewritten Source ---");
        lines.Add(result.RewrittenSource);
        return lines;
    }

    private static string ResolveDiffPath(string inputPath, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("diff-out", out var explicitPath) &&
            !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var directory = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(directory, $"{fileName}.rewrite.diff");
    }

    private static string ResolveDirectoryDiffRoot(string inputPath, IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("diff-out", out var explicitPath) &&
            !string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        return inputPath;
    }

    private static string ResolveFileDiffPath(string inputRootPath, string filePath, string diffRootPath)
    {
        var relativePath = Path.GetRelativePath(inputRootPath, filePath);
        var relativeDirectory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var targetDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
          ? diffRootPath
          : Path.Combine(diffRootPath, relativeDirectory);
        return Path.Combine(targetDirectory, $"{fileName}.rewrite.diff");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[arg[2..]] = "true";
                continue;
            }

            options[arg[2..]] = args[index + 1];
            index++;
        }

        return options;
    }

    private static bool TryParseDisabledRuleTypes(IReadOnlyDictionary<string, string> options, out IReadOnlyList<string> disabledRuleTypes)
    {
        disabledRuleTypes = Array.Empty<string>();
        if (!options.TryGetValue("disabled-rule-types", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        disabledRuleTypes = rawValue
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToList();
        return disabledRuleTypes.Count > 0;
    }

    private static string GetDefaultSource()
    {
        return """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var value = s.Seed + offset;
          if (s.IsReady)
          {
            return value;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }

        public bool IsReady { get; set; }
      }
      """;
    }

    private static string GetNodeKindText(SyntaxNode node)
    {
        return node is CSharpSyntaxNode csharpNode
          ? csharpNode.Kind().ToString()
          : node.RawKind.ToString();
    }

    private PrototypeAnalysisResult AnalyzeDirectory(string directoryPath, IReadOnlyDictionary<string, string> options)
    {
        if (ShouldUseUnreferencedMethodFastPath(options))
        {
            return AnalyzeDirectoryForUnreferencedMethods(directoryPath, options);
        }

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
              null);
        }

        var sourcesByPath = filePaths.ToDictionary(
          path => path,
          path => File.ReadAllText(path),
          StringComparer.Ordinal);
        var trees = filePaths.ToDictionary(
          path => path,
          path => CSharpSyntaxTree.ParseText(sourcesByPath[path], path: path),
          StringComparer.Ordinal);
        var compilation = CreateCompilation(trees.Values);
        var fileResults = AnalyzeFilesInParallel(
          filePaths,
          trees,
          compilation,
          options,
          (filePath, _, semanticModel, root) => RunAnalysis(
            sourcesByPath[filePath],
            filePath,
            options,
            semanticModel,
            root));

        if (ShouldUseDeleteClassUsingCleanup(options))
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

            for (var index = 0; index < filePaths.Count; index++)
            {
                var filePath = filePaths[index];
                var result = fileResults[index];
                if (result.Edits.Count == 0)
                {
                    continue;
                }

                fileResults[index] = ApplyDeleteClassUsingCleanup(
                  filePath,
                  result,
                  projectSourcesByPath);
                fileResults[index] = ApplyDeleteClassEmptyNamespaceCleanup(
                  filePath,
                  fileResults[index],
                  projectSourcesByPath);
            }
        }

        var seedMarks = new List<MarkRecord>();
        var propagatedMarks = new List<PropagatedMarkRecord>();
        var liftedMarks = new List<LiftedMarkRecord>();
        var decisions = new List<RuleDecision>();
        var edits = new List<RewriteEdit>();
        var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var diffSections = new List<string>();
        var diffRootPath = ResolveDirectoryDiffRoot(directoryPath, options);
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
            if (ShouldWriteDiff(options))
            {
                var fileDiffPath = ResolveFileDiffPath(directoryPath, filePath, diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fileDiffPath)!);
                File.WriteAllText(fileDiffPath, result.DiffText, Encoding.UTF8);
                diffFilePaths.Add(fileDiffPath);
            }
        }

        if (ShouldWriteBack(options))
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
        if (diffFilePaths.Count > 0 && ShouldWriteDiff(options))
        {
            diffPath = diffRootPath;
        }

        var diagnostics = ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options)
          ? Array.Empty<AnalysisDiagnostic>()
          : GetRewriteDiagnostics(sourcesByPath, rewrittenSources);

        return new PrototypeAnalysisResult(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          decisions,
          edits,
          $"<multi-file:{rewrittenSources.Count}>",
          diffText,
          diffPath,
          Diagnostics: diagnostics);
    }

    private PrototypeAnalysisResult AnalyzeDirectoryForUnreferencedMethods(string directoryPath, IReadOnlyDictionary<string, string> options)
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

        var sourcesByPath = filePaths.ToDictionary(
          path => path,
          path => File.ReadAllText(path),
          StringComparer.Ordinal);
        var trees = filePaths.ToDictionary(
          path => path,
          path => CSharpSyntaxTree.ParseText(sourcesByPath[path], path: path),
          StringComparer.Ordinal);
        var compilation = CreateCompilation(trees.Values);
        var unreferencedMethodsByPath = FindUnreferencedMethodDeclarationsByPath(compilation);
        var candidateMethodCount = CountUnreferencedMethodCandidates(compilation);
        var deletedMethodCount = unreferencedMethodsByPath.Values.Sum(methods => methods.Count);
        var fileResults = AnalyzeFilesInParallel(
          filePaths,
          trees,
          compilation,
          options,
          (filePath, _, semanticModel, root) =>
          {
              var methodDeclarations = unreferencedMethodsByPath.TryGetValue(filePath, out var matches)
                ? matches
                : Array.Empty<MethodDeclarationSyntax>();
              return AnalyzeSingleFileForUnreferencedMethods(
                semanticModel,
                root,
                methodDeclarations);
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

    private PrototypeAnalysisResult AnalyzeSingleFileForUnreferencedMethods(SemanticModel semanticModel, SyntaxNode root, IReadOnlyList<MethodDeclarationSyntax> methodsToDelete)
    {
        var seedMarks = methodsToDelete
          .Select(method => new MarkRecord(
            DeleteUnreferencedMethodRuleIds.MarkRuleId,
            method,
            null,
            null,
            "Method has no references from methods that remain in the project.",
            DeleteUnreferencedMethodRuleIds.GroupKey))
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

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> BuildUnreferencedMethodCandidateMap(Compilation compilation)
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

    private static MethodReferenceIndex BuildUnreferencedMethodReferenceIndex(Compilation compilation, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
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

    private static Dictionary<IMethodSymbol, MethodDeclarationSyntax> FindUnreferencedMethodsByDeletionIteration(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
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

    private static bool HasRemainingReferences(IMethodSymbol method, IReadOnlySet<IMethodSymbol> deletedMethods, MethodReferenceIndex references)
    {
        if (references.ExternallyReferencedMethods.Contains(method))
        {
            return true;
        }

        return references.IncomingCandidateCallers[method]
          .Any(caller => !deletedMethods.Contains(caller));
    }

    private static HashSet<IMethodSymbol> FindExternallyReferencedClosure(IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates, MethodReferenceIndex references)
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

    private static Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> CreateMethodReferenceSetMap(IEnumerable<IMethodSymbol> candidates)
    {
        var map = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(
          SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            map[candidate] = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        return map;
    }

    private static IMethodSymbol? GetContainingCandidateMethod(SemanticModel model, SyntaxNode node, IReadOnlyDictionary<IMethodSymbol, MethodDeclarationSyntax> candidates)
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

    private static PrototypeAnalysisResult[] AnalyzeFilesInParallel(IReadOnlyList<string> filePaths, IReadOnlyDictionary<string, SyntaxTree> trees, CSharpCompilation compilation, IReadOnlyDictionary<string, string> options, Func<string, SyntaxTree, SemanticModel, SyntaxNode, PrototypeAnalysisResult> analyzeFile)
    {
        var fileResults = new PrototypeAnalysisResult[filePaths.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = ResolveMaxDegreeOfParallelism(options)
        };

        Parallel.For(
          0,
          filePaths.Count,
          parallelOptions,
          index =>
          {
              var filePath = filePaths[index];
              var tree = trees[filePath];
              var semanticModel = compilation.GetSemanticModel(tree);
              var root = tree.GetRoot();
              fileResults[index] = analyzeFile(filePath, tree, semanticModel, root);
          });

        return fileResults;
    }

    private static int ResolveMaxDegreeOfParallelism(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("max-degree-of-parallelism", out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return Math.Max(1, Environment.ProcessorCount);
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            return Math.Max(1, Environment.ProcessorCount);
        }

        return Math.Max(1, parsedValue);
    }

    private PrototypeAnalysisResult FinalizeDirectoryResults(string directoryPath, IReadOnlyDictionary<string, string> options, IReadOnlyList<string> filePaths, IReadOnlyList<PrototypeAnalysisResult> fileResults, AnalysisStats? stats = null)
    {
        var seedMarks = new List<MarkRecord>();
        var propagatedMarks = new List<PropagatedMarkRecord>();
        var liftedMarks = new List<LiftedMarkRecord>();
        var decisions = new List<RuleDecision>();
        var edits = new List<RewriteEdit>();
        var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var diffSections = new List<string>();
        var diffRootPath = ResolveDirectoryDiffRoot(directoryPath, options);
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
            if (ShouldWriteDiff(options))
            {
                var fileDiffPath = ResolveFileDiffPath(directoryPath, filePath, diffRootPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fileDiffPath)!);
                File.WriteAllText(fileDiffPath, result.DiffText, Encoding.UTF8);
                diffFilePaths.Add(fileDiffPath);
            }
        }

        if (ShouldWriteBack(options))
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
        if (diffFilePaths.Count > 0 && ShouldWriteDiff(options))
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

    private PrototypeAnalysisResult RunAnalysis(DeletionAnalysisContext analysisContext)
    {
        var seedMarks = _markingEngine.Run(analysisContext.RuleContext, analysisContext.Root, _markers);
        var propagatedMarks = _propagationEngine.Run(analysisContext.RuleContext, seedMarks, _propagators);
        var liftedMarks = _markLiftingEngine.Run(
          analysisContext.RuleContext,
          seedMarks,
          propagatedMarks,
          _lifters);
        var decisions = _decisionEngine.Decide(
          analysisContext.RuleContext,
          seedMarks,
          propagatedMarks,
          liftedMarks,
          _proposers);
        var filteredDecisions = FilterNestedDeleteDecisions(decisions);
        var rewriteResult = _rewriter.Rewrite(
          analysisContext.Root,
          analysisContext.SemanticModel,
          filteredDecisions);
        var result = new PrototypeAnalysisResult(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          filteredDecisions,
          rewriteResult.Edits,
          rewriteResult.RewrittenSource,
          rewriteResult.DiffText,
          null);

        if (ShouldUseDeleteClassUsingCleanup(analysisContext.RuleContext.Options) &&
            rewriteResult.Edits.Count > 0)
        {
            var projectSourcesByPath = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [analysisContext.SemanticModel.SyntaxTree.FilePath] = rewriteResult.RewrittenSource
            };
            result = ApplyDeleteClassUsingCleanup(
              analysisContext.SemanticModel.SyntaxTree.FilePath,
              result,
              projectSourcesByPath);
            result = ApplyDeleteClassEmptyNamespaceCleanup(
              analysisContext.SemanticModel.SyntaxTree.FilePath,
              result,
              projectSourcesByPath);
        }

        var diagnostics = ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(
          analysisContext.RuleContext.Options)
          ? Array.Empty<AnalysisDiagnostic>()
          : GetRewriteDiagnostics(
            analysisContext.SemanticModel.SyntaxTree.FilePath,
            result.RewrittenSource);

        return result with { Diagnostics = diagnostics };
    }

    private DeletionAnalysisContext BuildAnalysisContext(string source, string filePath, IReadOnlyDictionary<string, string> options)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        return BuildAnalysisContext(source, filePath, options, semanticModel, root);
    }

    private DeletionAnalysisContext BuildAnalysisContext(string source, string filePath, IReadOnlyDictionary<string, string> options, SemanticModel semanticModel, SyntaxNode root)
    {
        var graph = ShouldUseUnreferencedMethodFastPath(options)
          ? new MinimalRoslynCpg.Model.RoslynCpgGraph()
          : new RoslynCpgBuilder().BuildFromSemanticModel(
            semanticModel,
            root,
            source,
            filePath);
        var cpgAnalysisContext = new CpgAnalysisContext(graph, semanticModel, root);
        var ruleContext = new RuleContext(cpgAnalysisContext, options);

        return new DeletionAnalysisContext(root, semanticModel, ruleContext);
    }

    private static IReadOnlyList<RuleDecision> FilterNestedDeleteDecisions(IReadOnlyList<RuleDecision> decisions)
    {
        var ordered = decisions
          .OrderByDescending(decision => decision.FinalNode.Span.Length)
          .ToList();

        var filtered = new List<RuleDecision>();
        foreach (var decision in ordered)
        {
            if (decision.Action == DecisionActionKind.Delete &&
                IsCoveredByReplaceDecision(decision, ordered))
            {
                continue;
            }

            if (decision.Action != DecisionActionKind.Delete)
            {
                filtered.Add(decision);
                continue;
            }

            if (filtered.Any(existing =>
                  existing.Action == DecisionActionKind.Delete &&
                  existing.FinalNode.Span.Contains(decision.FinalNode.Span)))
            {
                continue;
            }

            filtered.Add(decision);
        }

        return filtered;
    }

    private static bool IsCoveredByReplaceDecision(RuleDecision deleteDecision, IReadOnlyList<RuleDecision> decisions)
    {
        return decisions.Any(decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.Span.Contains(deleteDecision.FinalNode.Span));
    }

    private static bool ShouldWriteBack(IReadOnlyDictionary<string, string> options)
    {
        return options.TryGetValue("write-back", out var rawValue) &&
          string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldWriteDiff(IReadOnlyDictionary<string, string> options)
    {
        return !IsTrueOption(options, "no-diff") &&
          !IsTrueOption(options, "skip-diff");
    }

    private static bool IsTrueOption(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var rawValue) &&
          string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseUnreferencedMethodFastPath(IReadOnlyDictionary<string, string> options)
    {
        return IsTrueOption(options, "delete-unreferenced-methods") &&
          !options.ContainsKey("target-name") &&
          !options.ContainsKey("delete-class") &&
          !options.ContainsKey("unreachable-methods") &&
          !IsTrueOption(options, "clear-unused-interface-implementations") &&
          !IsTrueOption(options, "privatize-internal-only-public-methods");
    }

    private static bool ShouldUseDeleteClassUsingCleanup(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          !IsTrueOption(options, "fast-delete-class-directory");
    }

    private static bool ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(IReadOnlyDictionary<string, string> options)
    {
        return options.ContainsKey("delete-class") &&
          IsTrueOption(options, "fast-delete-class-directory");
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

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CreateCompilation(new[] { tree });
    }

    private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        var references = new[]
        {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
    };

        return CSharpCompilation.Create(
          assemblyName: "RoslynPrototype",
          syntaxTrees: trees,
          references: references);
    }

    private PrototypeAnalysisResult ApplyDeleteClassUsingCleanup(string filePath, PrototypeAnalysisResult result, Dictionary<string, string> projectSourcesByPath)
    {
        var currentSource = result.RewrittenSource;
        var cleanupEdits = new List<RewriteEdit>();
        var baselineDiagnostics = GetStableErrorDiagnosticKeys(projectSourcesByPath);
        var changed = true;

        while (changed)
        {
            changed = false;
            var tree = CSharpSyntaxTree.ParseText(currentSource, path: filePath);
            if (tree.GetRoot() is not CompilationUnitSyntax root)
            {
                break;
            }

            foreach (var usingDirective in root.Usings)
            {
                if (usingDirective.Alias is not null ||
                    usingDirective.StaticKeyword != default ||
                    usingDirective.GlobalKeyword != default)
                {
                    continue;
                }

                var candidateRoot = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepNoTrivia);
                if (candidateRoot is null)
                {
                    continue;
                }

                var candidateSource = candidateRoot.ToFullString();
                var candidateProjectSources = new Dictionary<string, string>(
                  projectSourcesByPath,
                  StringComparer.Ordinal)
                {
                    [filePath] = candidateSource
                };
                var candidateDiagnostics = GetStableErrorDiagnosticKeys(candidateProjectSources);
                if (!baselineDiagnostics.SetEquals(candidateDiagnostics))
                {
                    continue;
                }

                cleanupEdits.Add(new RewriteEdit(
                  filePath,
                  usingDirective.Span,
                  usingDirective.WithoutTrivia().ToFullString(),
                  string.Empty));
                currentSource = candidateSource;
                projectSourcesByPath[filePath] = candidateSource;
                changed = true;
                break;
            }
        }

        if (cleanupEdits.Count == 0)
        {
            return result;
        }

        var edits = result.Edits.Concat(cleanupEdits).ToList();
        return result with
        {
            Edits = edits,
            RewrittenSource = currentSource,
            DiffText = _rewriter.BuildDiffText(edits)
        };
    }

    private PrototypeAnalysisResult ApplyDeleteClassEmptyNamespaceCleanup(string filePath, PrototypeAnalysisResult result, Dictionary<string, string> projectSourcesByPath)
    {
        var currentSource = result.RewrittenSource;
        var cleanupEdits = new List<RewriteEdit>();
        var changed = true;

        while (changed)
        {
            changed = false;
            var tree = CSharpSyntaxTree.ParseText(currentSource, path: filePath);
            if (tree.GetRoot() is not CompilationUnitSyntax root)
            {
                break;
            }

            var emptyNamespace = root.DescendantNodes()
              .OfType<NamespaceDeclarationSyntax>()
              .OrderByDescending(node => node.Span.Length)
              .FirstOrDefault(namespaceNode =>
                namespaceNode.Members.Count == 0 &&
                namespaceNode.Usings.Count == 0 &&
                namespaceNode.Externs.Count == 0);
            if (emptyNamespace is null)
            {
                break;
            }

            var candidateRoot = root.RemoveNode(emptyNamespace, SyntaxRemoveOptions.KeepNoTrivia);
            if (candidateRoot is null)
            {
                break;
            }

            var candidateSource = candidateRoot.ToFullString();
            var candidateProjectSources = new Dictionary<string, string>(
              projectSourcesByPath,
              StringComparer.Ordinal)
            {
                [filePath] = candidateSource
            };
            var baselineDiagnostics = GetStableErrorDiagnosticKeys(projectSourcesByPath);
            var candidateDiagnostics = GetStableErrorDiagnosticKeys(candidateProjectSources);
            if (!baselineDiagnostics.SetEquals(candidateDiagnostics))
            {
                break;
            }

            cleanupEdits.Add(new RewriteEdit(
              filePath,
              emptyNamespace.Span,
              emptyNamespace.WithoutTrivia().ToFullString(),
              string.Empty));
            currentSource = candidateSource;
            projectSourcesByPath[filePath] = candidateSource;
            changed = true;
        }

        if (cleanupEdits.Count == 0)
        {
            return result;
        }

        var edits = result.Edits.Concat(cleanupEdits).ToList();
        return result with
        {
            Edits = edits,
            RewrittenSource = currentSource,
            DiffText = _rewriter.BuildDiffText(edits)
        };
    }

    private static IReadOnlyList<AnalysisDiagnostic> GetRewriteDiagnostics(string filePath, string rewrittenSource)
    {
        var sourcesByPath = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [filePath] = rewrittenSource
        };
        return GetRewriteDiagnostics(sourcesByPath, sourcesByPath);
    }

    private static IReadOnlyList<AnalysisDiagnostic> GetRewriteDiagnostics(IReadOnlyDictionary<string, string> originalSourcesByPath, IReadOnlyDictionary<string, string> rewrittenSourcesByPath)
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

        return CreateCompilation(trees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .Where(diagnostic => !IsIgnoredPostRewriteDiagnostic(diagnostic))
          .Select(CreateAnalysisDiagnostic)
          .ToList();
    }

    private static HashSet<string> GetStableErrorDiagnosticKeys(IReadOnlyDictionary<string, string> sourcesByPath)
    {
        var trees = sourcesByPath
          .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
          .ToList();
        return CreateCompilation(trees)
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

    private sealed record MethodReferenceIndex(
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> IncomingCandidateCallers,
      IReadOnlyDictionary<IMethodSymbol, HashSet<IMethodSymbol>> CandidateCallees,
      IReadOnlySet<IMethodSymbol> ExternallyReferencedMethods);

    private sealed record DeletionAnalysisContext(
      SyntaxNode Root,
      SemanticModel SemanticModel,
      RuleContext RuleContext);
}
