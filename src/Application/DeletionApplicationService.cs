using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Builder;
using System.Text;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

public sealed class DeletionApplicationService
{
    private readonly RoslynCpgBuilder _cpgBuilder;
    private readonly IReadOnlyList<RuleDefinition> _rules;
    private readonly MarkingEngine _markingEngine;
    private readonly PropagationEngine _propagationEngine;
    private readonly RuleDecisionEngine _decisionEngine;
    private readonly PrototypeRewriter _rewriter;

    public DeletionApplicationService(IReadOnlyList<RuleDefinition> rules)
    {
        _cpgBuilder = new RoslynCpgBuilder();
        _rules = rules;
        _markingEngine = new MarkingEngine();
        _propagationEngine = new PropagationEngine();
        _decisionEngine = new RuleDecisionEngine();
        _rewriter = new PrototypeRewriter();
    }

    public PrototypeAnalysisResult Analyze(
      string source,
      string filePath,
      IReadOnlyDictionary<string, string> options)
    {
        return RunAnalysis(source, filePath, options);
    }

    public PrototypeAnalysisResult AnalyzeFromArgs(string[] args)
    {
        var inputPath = args.FirstOrDefault(path => !path.StartsWith("--", StringComparison.Ordinal));
        var options = ParseOptions(args);
        var source = inputPath is not null && File.Exists(inputPath)
          ? File.ReadAllText(inputPath)
          : GetDefaultSource();
        var filePath = inputPath ?? "demo.cs";
        var result = Analyze(source, filePath, options);

        if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
        {
            return result;
        }

        var diffPath = ResolveDiffPath(inputPath, options);
        File.WriteAllText(diffPath, result.DiffText, Encoding.UTF8);
        return result with { DiffFilePath = diffPath };
    }

    private PrototypeAnalysisResult RunAnalysis(
      string source,
      string filePath,
      IReadOnlyDictionary<string, string> options)
    {
        var analysisContext = BuildAnalysisContext(source, filePath, options);
        var seedMarks = _markingEngine.Run(analysisContext.RuleContext, analysisContext.Root, _rules);
        var propagatedMarks = _propagationEngine.Run(analysisContext.RuleContext, seedMarks, _rules);
        var decisions = _decisionEngine.Decide(analysisContext.RuleContext, seedMarks, propagatedMarks, _rules);
        var filteredDecisions = FilterNestedDeleteDecisions(decisions);
        var rewriteResult = _rewriter.Rewrite(
          analysisContext.Root,
          analysisContext.SemanticModel,
          filteredDecisions);

        return new PrototypeAnalysisResult(
          seedMarks,
          propagatedMarks,
          filteredDecisions,
          rewriteResult.Edits,
          rewriteResult.RewrittenSource,
          rewriteResult.DiffText,
          null);
    }

    public IReadOnlyList<string> FormatResult(PrototypeAnalysisResult result)
    {
        var effectiveMarks = result.SeedMarks.Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .ToList();
        var lines = new List<string>
    {
      $"SeedMarks: {result.SeedMarks.Count}",
      $"PropagatedMarks: {result.PropagatedMarks.Count}",
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

        lines.Add("--- Rewritten Source ---");
        lines.Add(result.RewrittenSource);
        return lines;
    }

    private static string ResolveDiffPath(
      string inputPath,
      IReadOnlyDictionary<string, string> options)
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

    private DeletionAnalysisContext BuildAnalysisContext(
      string source,
      string filePath,
      IReadOnlyDictionary<string, string> options)
    {
        var graph = _cpgBuilder.BuildFromSource(source, filePath);
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var cpgAnalysisContext = new CpgAnalysisContext(graph, semanticModel, root);
        var ruleContext = new RuleContext(cpgAnalysisContext, options);

        return new DeletionAnalysisContext(root, semanticModel, ruleContext);
    }

    private static IReadOnlyList<RuleDecision> FilterNestedDeleteDecisions(
      IReadOnlyList<RuleDecision> decisions)
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

    private static bool IsCoveredByReplaceDecision(
      RuleDecision deleteDecision,
      IReadOnlyList<RuleDecision> decisions)
    {
        return decisions.Any(decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.Span.Contains(deleteDecision.FinalNode.Span));
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        var references = new[]
        {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
    };

        return CSharpCompilation.Create(
          assemblyName: "RoslynPrototype",
          syntaxTrees: new[] { tree },
          references: references);
    }

    private sealed record DeletionAnalysisContext(
      SyntaxNode Root,
      SemanticModel SemanticModel,
      RuleContext RuleContext);
}
