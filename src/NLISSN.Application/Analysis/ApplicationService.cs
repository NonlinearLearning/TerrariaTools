using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Builder;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;

namespace NLISSN.Application;

/// 编排单个源码文件的删除规则分析：构图、标记、传播、提升、决策和改写。
public sealed class ApplicationService
{
    private readonly IReadOnlyList<RuleDefinitionMark> _markers;
    private readonly IReadOnlyList<RuleDefinitionPropagate> _propagators;
    private readonly IReadOnlyList<RuleDefinitionLift> _lifters;
    private readonly IReadOnlyList<RuleDefinitionPropose> _proposers;
    private readonly RulePipeline _pipeline;
    private readonly CompiledRuleGraph? _compiledRuleGraph;
    private readonly MarkingEngine _markingEngine;
    private readonly PropagationEngine _propagationEngine;
    private readonly MarkLiftingEngine _markLiftingEngine;
    private readonly RuleDecisionEngine _decisionEngine;
    private readonly PrototypeRewriter _rewriter;

    // 用完整规则管道初始化单文件分析服务，并准备四个阶段的执行器和改写器。
    public ApplicationService(RulePipeline pipeline)
    {
        _pipeline = pipeline;
        _compiledRuleGraph = pipeline.EnableRuleGraphExecution
          ? pipeline.CompileRuleGraph()
          : null;
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

    // 允许调用方直接注入四个阶段的规则列表，内部仍组装成统一规则管道。
    public ApplicationService(IReadOnlyList<RuleDefinitionMark> markers, IReadOnlyList<RuleDefinitionPropagate> propagators, IReadOnlyList<RuleDefinitionLift> lifters, IReadOnlyList<RuleDefinitionPropose> proposers)
      : this(new RulePipeline(markers, propagators, lifters, proposers))
    {
    }

    // 从源码、文件路径和 CLI 选项构建默认运行时后执行一次完整分析。
    public PrototypeAnalysisResult Analyze(string source, string filePath, IReadOnlyDictionary<string, string> options)
    {
        return Analyze(
          source,
          filePath,
          options,
           AnalysisRuntime.CreateFromOptions(options));
    }

    // 使用调用方提供的运行时执行完整分析，保留外部传入的并行和缓存设置。
    public PrototypeAnalysisResult Analyze(string source, string filePath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        var analysisContext = BuildAnalysisContext(source, filePath, options, runtime);
        return RunAnalysis(analysisContext);
    }

    // 复用现成语义模型和语法树执行分析，避免调用方重复创建运行时和编译。
    public PrototypeAnalysisResult Analyze(string source, string filePath, IReadOnlyDictionary<string, string> options, SemanticModel semanticModel, SyntaxNode root)
    {
        return Analyze(
          source,
          filePath,
          options,
           AnalysisRuntime.CreateFromOptions(options),
          semanticModel,
          root);
    }

    // 在复用现成语义模型的同时接收外部运行时，适合目录级批量分析共享上下文。
    public PrototypeAnalysisResult Analyze(string source, string filePath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime, SemanticModel semanticModel, SyntaxNode root)
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

    private PrototypeAnalysisResult RunAnalysis(AnalysisContext analysisContext)
    {
        IReadOnlyList<MarkRecord> seedMarks;
        IReadOnlyList<PropagatedMarkRecord> propagatedMarks;
        IReadOnlyList<LiftedMarkRecord> liftedMarks;
        IReadOnlyList<RuleDecision> decisions;
        IReadOnlyList<RuleGraphNodeTelemetry>? ruleGraphTelemetry;
        RuleGraphExecutionMetrics? ruleGraphMetrics;
        if (_pipeline.EnableRuleGraphExecution)
        {
            var graphResult = new RuleGraphAnalysisExecutor().Run(
              analysisContext.RuleContext,
              analysisContext.Root,
              _pipeline,
              _compiledRuleGraph!);
            seedMarks = graphResult.SeedMarks;
            propagatedMarks = graphResult.PropagatedMarks;
            liftedMarks = graphResult.LiftedMarks;
            decisions = graphResult.Decisions;
            ruleGraphTelemetry = graphResult.Telemetry;
            ruleGraphMetrics = graphResult.Metrics;
        }
        else
        {
            seedMarks = _markingEngine.Run(analysisContext.RuleContext, analysisContext.Root, _markers);
            propagatedMarks = _propagationEngine.Run(analysisContext.RuleContext, seedMarks, _propagators);
            liftedMarks = _markLiftingEngine.Run(
              analysisContext.RuleContext,
              seedMarks,
              propagatedMarks,
              _lifters);
            decisions = _decisionEngine.Decide(
              analysisContext.RuleContext,
              seedMarks,
              propagatedMarks,
              liftedMarks,
              _proposers);
            ruleGraphTelemetry = null;
            ruleGraphMetrics = null;
        }

        var filteredDecisions = FilterNestedDeleteDecisions(decisions);
        var rewriteResult = ShouldSkipRewrite(analysisContext.RuleContext)
          ? new PrototypeRewriteResult(
            null,
            Array.Empty<RewriteEdit>(),
            DiffDocument.Empty)
          : _rewriter.Rewrite(
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
          rewriteResult.Diff,
          null,
          RewritePlans: rewriteResult.Operations is { Count: > 0 }
            ? new[] { new PrototypeFileRewritePlan(analysisContext.Root.SyntaxTree.FilePath, rewriteResult.Operations) }
            : Array.Empty<PrototypeFileRewritePlan>(),
          RuleGraphTelemetry: ruleGraphTelemetry,
          RuleGraphMetrics: ruleGraphMetrics);
    }

    private AnalysisContext BuildAnalysisContext(string source, string filePath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = RoslynCompilationFactory.CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        return BuildAnalysisContext(
          source,
          filePath,
          options,
          runtime,
          semanticModel,
          root);
    }

    private AnalysisContext BuildAnalysisContext(string source, string filePath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime, SemanticModel semanticModel, SyntaxNode root)
    {
        if (runtime.CurrentCpgBuildAdmissionLease is not null)
        {
            return BuildAnalysisContextCore(
              source,
              filePath,
              options,
              runtime,
               semanticModel,
               root);
        }

        using var lease = runtime.CpgBuildAdmissionBudget
          .AcquireAsync(
            runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism,
            runtime.ExecutionOptions.CancellationToken,
            CpgBuildAdmissionPolicy.WholeBuild)
          .GetAwaiter()
          .GetResult();
        using var scope = runtime.PushCpgBuildAdmissionLease(lease);
        return BuildAnalysisContextCore(
          source,
          filePath,
          options,
          runtime,
          semanticModel,
          root);
    }

    private AnalysisContext BuildAnalysisContextCore(string source, string filePath, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime, SemanticModel semanticModel, SyntaxNode root)
    {
        var builderOptions = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = runtime.CurrentCpgBuildAdmissionLease!.GrantedDegree,
            RequestedCapabilities = new RulePipeline(_markers, _propagators, _lifters, _proposers)
            .GetRequiredCapabilities()
        };
        var builder = new NLCPGBuilder(builderOptions);
        var graph = builder.BuildFromSemanticModel(
          semanticModel,
          root,
          source,
          filePath);
        var cpgAnalysisContext = new CpgAnalysisContext(graph, semanticModel, root);
        var ruleContext = new RuleContext(cpgAnalysisContext, options, runtime: runtime);

        return new AnalysisContext(
          root,
          semanticModel,
          ruleContext);
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

    private static bool ShouldSkipRewrite(RuleContext ruleContext)
    {
        return ruleContext.TryGetOption("skip-rewrite", out var rawValue) &&
          string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoveredByReplaceDecision(RuleDecision deleteDecision, IReadOnlyList<RuleDecision> decisions)
    {
        return decisions.Any(decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.Span.Contains(deleteDecision.FinalNode.Span));
    }

    private sealed record AnalysisContext(
      SyntaxNode Root,
      SemanticModel SemanticModel,
      RuleContext RuleContext);
}
