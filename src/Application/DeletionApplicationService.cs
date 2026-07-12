using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Analysis;
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

  public DeletionApplicationService(DeletionRulePipeline pipeline)
  {
    _markers = pipeline.Markers;
    _propagators = pipeline.Propagators;
    _lifters = pipeline.Lifters;
    _proposers = pipeline.Proposers;
    _markingEngine = new MarkingEngine();
    _propagationEngine = new PropagationEngine();
    _markLiftingEngine = new MarkLiftingEngine();
    _decisionEngine = new RuleDecisionEngine();
    _rewriter = new PrototypeRewriter();
  }

  public DeletionApplicationService(
    IReadOnlyList<RuleDefinitionMark> markers,
    IReadOnlyList<RuleDefinitionPropagate> propagators,
    IReadOnlyList<RuleDefinitionLift> lifters,
    IReadOnlyList<RuleDefinitionPropose> proposers)
    : this(new DeletionRulePipeline(markers, propagators, lifters, proposers))
  {
  }

  public PrototypeAnalysisResult Analyze(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options)
  {
    var analysisContext = BuildAnalysisContext(source, filePath, options);
    return RunAnalysis(analysisContext);
  }

  public PrototypeAnalysisResult Analyze(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    var analysisContext = BuildAnalysisContext(source, filePath, options, semanticModel, root);
    return RunAnalysis(analysisContext);
  }

  private PrototypeAnalysisResult RunAnalysis(DeletionAnalysisContext analysisContext)
  {
    var seedMarks = _markingEngine.Run(analysisContext.RuleContext, analysisContext.Root, _markers);
    var propagatedMarks = _propagationEngine.Run(
      analysisContext.RuleContext,
      seedMarks,
      _propagators);
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

    return new PrototypeAnalysisResult(
      seedMarks,
      propagatedMarks,
      liftedMarks,
      filteredDecisions,
      rewriteResult.Edits,
      rewriteResult.RewrittenSource,
      rewriteResult.DiffText,
      null);
  }

  private DeletionAnalysisContext BuildAnalysisContext(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options)
  {
    var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var root = tree.GetRoot();
    var compilation = RoslynCompilationFactory.CreateCompilation(tree);
    var semanticModel = compilation.GetSemanticModel(tree);
    return BuildAnalysisContext(source, filePath, options, semanticModel, root);
  }

  private DeletionAnalysisContext BuildAnalysisContext(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    var graph = new RoslynCpgBuilder().BuildFromSemanticModel(
      semanticModel,
      root,
      source,
      filePath);
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

  private sealed record DeletionAnalysisContext(
    SyntaxNode Root,
    SemanticModel SemanticModel,
    RuleContext RuleContext);
}
