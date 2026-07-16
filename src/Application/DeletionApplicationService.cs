using System.Diagnostics;
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
    return Analyze(
      source,
      filePath,
      options,
      DeletionAnalysisRuntime.CreateFromOptions(options));
  }

  public PrototypeAnalysisResult Analyze(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    DeletionAnalysisRuntime runtime)
  {
    var analysisContext = BuildAnalysisContext(source, filePath, options, runtime);
    return RunAnalysis(analysisContext);
  }

  public PrototypeAnalysisResult Analyze(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    return Analyze(
      source,
      filePath,
      options,
      DeletionAnalysisRuntime.CreateFromOptions(options),
      semanticModel,
      root);
  }

  public PrototypeAnalysisResult Analyze(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    DeletionAnalysisRuntime runtime,
    SemanticModel semanticModel,
    SyntaxNode root)
  {
    var analysisContext = BuildAnalysisContext(
      source,
      filePath,
      options,
      runtime,
      semanticModel,
      root);
    return RunAnalysis(analysisContext);
  }

  private PrototypeAnalysisResult RunAnalysis(DeletionAnalysisContext analysisContext)
  {
    var totalStopwatch = Stopwatch.StartNew();
    var markStopwatch = Stopwatch.StartNew();
    var seedMarks = _markingEngine.Run(analysisContext.RuleContext, analysisContext.Root, _markers);
    markStopwatch.Stop();

    var propagateStopwatch = Stopwatch.StartNew();
    var propagatedMarks = _propagationEngine.Run(
      analysisContext.RuleContext,
      seedMarks,
      _propagators);
    propagateStopwatch.Stop();

    var liftStopwatch = Stopwatch.StartNew();
    var liftedMarks = _markLiftingEngine.Run(
      analysisContext.RuleContext,
      seedMarks,
      propagatedMarks,
      _lifters);
    liftStopwatch.Stop();

    var decideStopwatch = Stopwatch.StartNew();
    var decisions = _decisionEngine.Decide(
      analysisContext.RuleContext,
      seedMarks,
      propagatedMarks,
      liftedMarks,
      _proposers);
    decideStopwatch.Stop();

    var filteredDecisions = FilterNestedDeleteDecisions(decisions);
    var rewriteStopwatch = Stopwatch.StartNew();
    var rewriteResult = ShouldSkipRewrite(analysisContext.RuleContext)
      ? new PrototypeRewriteResult(
        null,
        Array.Empty<RewriteEdit>(),
        string.Empty)
      : _rewriter.Rewrite(
        analysisContext.Root,
        analysisContext.SemanticModel,
        filteredDecisions);
    rewriteStopwatch.Stop();
    totalStopwatch.Stop();

    return new PrototypeAnalysisResult(
      seedMarks,
      propagatedMarks,
      liftedMarks,
      filteredDecisions,
      rewriteResult.Edits,
      rewriteResult.RewrittenSource,
      rewriteResult.DiffText,
      null,
      Timings: new AnalysisPhaseTimings(
        analysisContext.PreparationMilliseconds,
        analysisContext.CpgBuildMilliseconds,
        markStopwatch.ElapsedMilliseconds,
        propagateStopwatch.ElapsedMilliseconds,
        liftStopwatch.ElapsedMilliseconds,
        decideStopwatch.ElapsedMilliseconds,
        rewriteStopwatch.ElapsedMilliseconds,
        totalStopwatch.ElapsedMilliseconds),
      CpgBuildTelemetry: analysisContext.CpgBuildTelemetry,
      MarkAnalysisTelemetry: analysisContext.RuleContext.MarkAnalysisTelemetry,
      StructureViewCacheTelemetry: analysisContext.RuleContext.StructureViewCacheTelemetry);
  }

  private DeletionAnalysisContext BuildAnalysisContext(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    DeletionAnalysisRuntime runtime)
  {
    var preparationStopwatch = Stopwatch.StartNew();
    var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
    var root = tree.GetRoot();
    var compilation = RoslynCompilationFactory.CreateCompilation(tree);
    var semanticModel = compilation.GetSemanticModel(tree);
    preparationStopwatch.Stop();
    return BuildAnalysisContext(
      source,
      filePath,
      options,
      runtime,
      semanticModel,
      root,
      preparationStopwatch.ElapsedMilliseconds);
  }

  private DeletionAnalysisContext BuildAnalysisContext(
    string source,
    string filePath,
    IReadOnlyDictionary<string, string> options,
    DeletionAnalysisRuntime runtime,
    SemanticModel semanticModel,
    SyntaxNode root,
    long preparationMilliseconds = 0)
  {
    var cpgBuildStopwatch = Stopwatch.StartNew();
    var builderOptions = RoslynCpgBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
      RequestedCapabilities = new DeletionRulePipeline(_markers, _propagators, _lifters, _proposers)
        .GetRequiredCapabilities()
    };
    var builder = new RoslynCpgBuilder(builderOptions);
    var graph = builder.BuildFromSemanticModel(
      semanticModel,
      root,
      source,
      filePath);
    cpgBuildStopwatch.Stop();
    var cpgAnalysisContext = new CpgAnalysisContext(graph, semanticModel, root);
    var ruleContext = new RuleContext(cpgAnalysisContext, options, runtime: runtime);

    return new DeletionAnalysisContext(
      root,
      semanticModel,
      ruleContext,
      preparationMilliseconds,
      cpgBuildStopwatch.ElapsedMilliseconds,
      builder.LastBuildTelemetry);
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

  private static bool ShouldSkipRewrite(RuleContext ruleContext)
  {
    return ruleContext.TryGetOption("skip-rewrite", out var rawValue) &&
      string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
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
    RuleContext RuleContext,
    long PreparationMilliseconds,
    long CpgBuildMilliseconds,
    RoslynCpgBuildTelemetry CpgBuildTelemetry);
}
