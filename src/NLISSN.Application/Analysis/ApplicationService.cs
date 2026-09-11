using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Performance;
using NLISSN.Application.Performance;

namespace NLISSN.Application;

/// 编排单个源码文件的删除规则分析：构图、标记、传播、提升、决策和改写。
public sealed class ApplicationService
{
    private readonly RulePipeline _pipeline;
    private readonly CompiledRuleGraph _compiledRuleGraph;
    private readonly PrototypeRewriter _rewriter;
    private readonly ICallFlowResolver? _callFlowResolver;
    private readonly IPerformanceEventSink? _performanceEventSink;
    private readonly ConcurrentDictionary<Compilation, ICallFlowResolver> _defaultCallFlowResolvers =
        new(ReferenceEqualityComparer.Instance);

    // 用完整规则管道初始化单文件分析服务，并准备四个阶段的执行器和改写器。
    public ApplicationService(
      RulePipeline pipeline,
      ICallFlowResolver? callFlowResolver = null,
      IPerformanceEventSink? performanceEventSink = null)
    {
        _pipeline = pipeline;
        _compiledRuleGraph = pipeline.CompileRuleGraph();
        _rewriter = new PrototypeRewriter();
        _callFlowResolver = callFlowResolver;
        _performanceEventSink = performanceEventSink;
    }

    // 允许调用方直接注入四个阶段的规则列表，内部仍组装成统一规则管道。
    public ApplicationService(IReadOnlyList<RuleDefinitionMark> markers, IReadOnlyList<RuleDefinitionPropagate> propagators, IReadOnlyList<RuleDefinitionLift> lifters, IReadOnlyList<RuleDefinitionPropose> proposers)
      : this(new RulePipeline(markers, propagators, lifters, proposers))
    {
    }

    public PrototypeAnalysisResult Analyze(string source, string filePath, AnalysisRequestSettings settings)
    {
        return Analyze(
          source,
          filePath,
          settings,
          AnalysisRuntimeFactory.CreateDefault());
    }

    public PrototypeAnalysisResult Analyze(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        var analysisContext = BuildAnalysisContext(source, filePath, settings, runtime);
        return RunAnalysis(analysisContext);
    }

    public PrototypeAnalysisResult Analyze(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      SemanticModel semanticModel,
      SyntaxNode root)
    {
        return Analyze(
          source,
          filePath,
          settings,
          AnalysisRuntimeFactory.CreateDefault(),
          semanticModel,
          root);
    }

    public PrototypeAnalysisResult Analyze(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      SemanticModel semanticModel,
      SyntaxNode root)
    {
        var analysisContext = BuildAnalysisContext(
          source,
          filePath,
          settings,
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
        IReadOnlyDictionary<RuleNodeId, RuleGraphNodeStatus>? ruleGraphNodeStatuses;
        RuleGraphExecutionMetrics? ruleGraphMetrics;
        var graphResult = new RuleGraphAnalysisExecutor().Run(
          analysisContext.Session,
          analysisContext.Root,
          _pipeline,
          _compiledRuleGraph,
          _performanceEventSink ?? analysisContext.Session.Runtime.PerformanceEventSink,
          analysisContext.Session.Runtime.PerformanceRunId,
          analysisContext.CpgPerformance.ItemId);
        seedMarks = graphResult.SeedMarks;
        propagatedMarks = graphResult.PropagatedMarks;
        liftedMarks = graphResult.LiftedMarks;
        decisions = graphResult.Decisions;
        ruleGraphTelemetry = graphResult.Telemetry;
        ruleGraphNodeStatuses = graphResult.NodeStatuses;
        ruleGraphMetrics = graphResult.Metrics;
        var validationReport = graphResult.ValidationReport;
        var ruleGraphPerformance = graphResult.Performance;

        var filteredDecisions = FilterUnsafeLocalDeclarationDeletes(
          FilterNestedDeleteDecisions(decisions),
          analysisContext.SemanticModel);
        var rewriteResult = ShouldSkipRewrite(analysisContext.Session) || validationReport is { IsValid: false }
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
          RuleGraphNodeStatuses: ruleGraphNodeStatuses,
          RuleGraphMetrics: ruleGraphMetrics,
          GraphMetrics: new CpgGraphMetrics(
            analysisContext.CpgAnalysisContext.Graph.Nodes.Count,
            analysisContext.CpgAnalysisContext.Graph.Edges.Count),
          Evidence: graphResult.Evidence,
          ValidationReport: validationReport,
          Performance: new ApplicationPerformanceFacts(
            analysisContext.CpgPerformance.ItemId,
            analysisContext.CpgPerformance,
            ruleGraphPerformance,
            new RewritePerformanceFacts(
              null,
              rewriteResult.Edits.Count,
              rewriteResult.Diff.Files.Count),
            PerformanceStatus.Completed));
    }

    private AnalysisContext BuildAnalysisContext(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var compilation = RoslynCompilationFactory.CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        return BuildAnalysisContext(
          source,
          filePath,
          settings,
          runtime,
          semanticModel,
          root);
    }

    private AnalysisContext BuildAnalysisContext(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      SemanticModel semanticModel,
      SyntaxNode root)
    {
        if (runtime.CurrentCpgBuildAdmissionLease is not null)
        {
            return BuildAnalysisContextCore(
              source,
              filePath,
              settings,
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
          settings,
          runtime,
          semanticModel,
          root);
    }

    private AnalysisContext BuildAnalysisContextCore(
      string source,
      string filePath,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime,
      SemanticModel semanticModel,
      SyntaxNode root)
    {
        var callFlowResolver = _callFlowResolver ??
          _defaultCallFlowResolvers.GetOrAdd(
            semanticModel.Compilation,
            FlowSummaryResolverFactory.Create);
        var builderOptions = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = runtime.CurrentCpgBuildAdmissionLease!.GrantedDegree,
            RequestedCapabilities = _pipeline.GetRequiredCapabilities(),
            CallFlowResolver = callFlowResolver,
        };
        var builder = new NLCPGBuilder(builderOptions);
        var graph = builder.BuildFromSemanticModel(
          semanticModel,
          root,
          source,
          filePath);
        var cpgPerformance = CpgPerformanceFactMapper.Map(
          filePath,
          CpgPerformanceFactMapper.ComputeSourceIdentity(source),
          builder.LastBuildMetrics);
        var requestedCapabilities = builderOptions.RequestedCapabilities ?? new[] { NLCPGCapability.Default };
        var availableCapabilities = requestedCapabilities.Aggregate(
          NLCPGCapability.None,
          static (current, capability) => current | capability);
        var cpgAnalysisContext = new CpgAnalysisContext(
          graph,
          semanticModel,
          root,
          availableCapabilities,
          CallFlowResolver: callFlowResolver);
        var session = new AnalysisSession(cpgAnalysisContext, settings, runtime: runtime);

        return new AnalysisContext(
          root,
          semanticModel,
          session,
          cpgAnalysisContext,
          cpgPerformance);
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

    private static bool ShouldSkipRewrite(AnalysisSession session)
    {
        return session.Settings.SkipRewrite;
    }

    private static bool IsCoveredByReplaceDecision(RuleDecision deleteDecision, IReadOnlyList<RuleDecision> decisions)
    {
        return decisions.Any(decision =>
          decision.Action == DecisionActionKind.Replace &&
          decision.FinalNode.Span.Contains(deleteDecision.FinalNode.Span));
    }

    private static IReadOnlyList<RuleDecision> FilterUnsafeLocalDeclarationDeletes(
      IReadOnlyList<RuleDecision> decisions,
      SemanticModel semanticModel)
    {
        return decisions.Where(decision =>
          decision.Action != DecisionActionKind.Delete ||
          decision.FinalNode is not LocalDeclarationStatementSyntax declaration ||
          !HasSurvivingLocalReference(declaration, decisions, semanticModel)).ToList();
    }

    private static bool HasSurvivingLocalReference(
      LocalDeclarationStatementSyntax declaration,
      IReadOnlyList<RuleDecision> decisions,
      SemanticModel semanticModel)
    {
        var root = declaration.SyntaxTree.GetRoot();
        foreach (var declarator in declaration.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol localSymbol)
            {
                continue;
            }

            foreach (var reference in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!SymbolEqualityComparer.Default.Equals(
                      semanticModel.GetSymbolInfo(reference).Symbol,
                      localSymbol))
                {
                    continue;
                }

                var isDeleted = decisions.Any(decision =>
                  decision.Action == DecisionActionKind.Delete &&
                  decision.FinalNode != declaration &&
                  decision.FinalNode.Span.Contains(reference.Span));
                if (!isDeleted)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed record AnalysisContext(
      SyntaxNode Root,
      SemanticModel SemanticModel,
      AnalysisSession Session,
      CpgAnalysisContext CpgAnalysisContext,
      CpgPerformanceFacts CpgPerformance);
}
