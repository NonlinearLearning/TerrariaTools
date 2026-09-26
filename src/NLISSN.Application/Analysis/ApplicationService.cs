using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
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
        // G0-L：本重载在内部自建 runtime，故由本方法负责释放其内核（Task.Run 式的
        // 固定功能约定：内部创建的资源在返回前释放）。异常路径同样经 finally 释放。
        var runtime = AnalysisRuntimeFactory.CreateDefault();
        try
        {
            return Analyze(source, filePath, settings, runtime);
        }
        finally
        {
            runtime.DisposeSchedulerAsync().AsTask().GetAwaiter().GetResult();
        }
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
        // G0-L：同上，本重载内部自建 runtime，故负责释放其内核。
        var runtime = AnalysisRuntimeFactory.CreateDefault();
        try
        {
            return Analyze(
              source,
              filePath,
              settings,
              runtime,
              semanticModel,
              root);
        }
        finally
        {
            runtime.DisposeSchedulerAsync().AsTask().GetAwaiter().GetResult();
        }
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

    /// <summary>
    /// 为一批**共享同一个 compilation** 的源文件批量构图，使 CPG 工作批次**跨文件**装箱（D1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是 D1 的生产入口：与逐个 <see cref="NLCPGBuilder.BuildFromSemanticModel"/> 的差别是
    /// 整批**一次**构建，因而工作批次可以跨文件凑成标准大小；但每文件仍得到**自己独立**的一张图。
    /// </para>
    /// <para>
    /// 本方法**只构图**，不做标记/传播/决策/改写——调用方随后用
    /// <see cref="AnalyzeWithGraph"/> 逐文件跑规则。这样拆分是为了让目录分析既能拿到跨文件装箱，
    /// 又能保留它原有的**逐文件并行**规则分析结构。
    /// </para>
    /// <para>
    /// <b>为什么必须共享 compilation：</b>见 <see cref="NLCPGBuildDocument"/> 的说明——
    /// 每文件各持一份 compilation 会让跨文件调用解析不出来。
    /// </para>
    /// </remarks>
    public NLCPGMultiFileBuildResult BuildGraphBatch(
      IReadOnlyList<BatchAnalysisFile> files,
      AnalysisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(runtime);
        if (files.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(files));
        }

        if (runtime.CurrentCpgBuildAdmissionLease is not null)
        {
            return BuildGraphBatchCore(files, runtime);
        }

        // 整批只占**一次** CPG 构建配额：一次 BuildManyDocuments 就是一次构建。
        using var lease = runtime.CpgBuildAdmissionBudget
          .AcquireAsync(
            runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism,
            runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        using var scope = runtime.PushCpgBuildAdmissionLease(lease);
        return BuildGraphBatchCore(files, runtime);
    }

    private NLCPGMultiFileBuildResult BuildGraphBatchCore(
      IReadOnlyList<BatchAnalysisFile> files,
      AnalysisRuntime runtime)
    {
        var configuration = CreateBuilderConfiguration(files[0].SemanticModel.Compilation, runtime);
        var documents = new NLCPGBuildDocument[files.Count];
        for (var index = 0; index < files.Count; index += 1)
        {
            documents[index] = new NLCPGBuildDocument(
              files[index].FilePath,
              files[index].Source,
              files[index].SemanticModel,
              files[index].Root);
        }

        // S5-2：把应用层的分片规划器接到构图器上。
        //
        // 只需挂一个端口：计划在构图器**内部**现算（用正在构图的那个实例自己的枚举），
        // 故不存在「两个 builder 配置必须一致」这种只能靠约定维持的约束，
        // 也不会为算计划而把每个语法树多枚举一遍。
        var builder = new NLCPGBuilder(
          configuration.Options with { WorkShardPlanner = new DocumentShardPlanAdapter() });

        return builder.BuildManyDocuments(documents);
    }

    /// <summary>
    /// 用**已构建**的图跑单文件的规则分析（标记/传播/提升/决策/改写）。
    /// </summary>
    /// <remarks>
    /// <paramref name="batchMetrics"/> 是整批共享构建的指标；本方法只取其中的
    /// 阶段耗时与缓存计数，并把节点/边规模**换成该文件自己那张图**的规模
    /// （整批口径的 NodeCount/EdgeCount 是合计值，直接透传会让每个文件都报出整批总量）。
    /// </remarks>
    public PrototypeAnalysisResult AnalyzeWithGraph(
      BatchAnalysisFile file,
      NLCPGGraph graph,
      NLCPGBuildMetrics batchMetrics,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(batchMetrics);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtime);

        // 构图那一批的配额租约在 BuildGraphBatch 返回时已经释放，故这里要**自己**取一次：
        // 规则分析同样受 CPG 并发配额管辖（与单文件路径一致）。
        // 若调用方已持有租约（例如本方法被内核工作项调用），则直接复用，不重复获取。
        if (runtime.CurrentCpgBuildAdmissionLease is not null)
        {
            return AnalyzeWithGraphCore(file, graph, batchMetrics, settings, runtime);
        }

        using var lease = runtime.CpgBuildAdmissionBudget
          .AcquireAsync(
            runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism,
            runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();
        using var scope = runtime.PushCpgBuildAdmissionLease(lease);
        return AnalyzeWithGraphCore(file, graph, batchMetrics, settings, runtime);
    }

    private PrototypeAnalysisResult AnalyzeWithGraphCore(
      BatchAnalysisFile file,
      NLCPGGraph graph,
      NLCPGBuildMetrics batchMetrics,
      AnalysisRequestSettings settings,
      AnalysisRuntime runtime)
    {
        var configuration = CreateBuilderConfiguration(file.SemanticModel.Compilation, runtime);

        // ⚠ 节点/边必须是**本文件**的规模，而非整批合计。
        var perFileMetrics = batchMetrics with
        {
            NodeCount = graph.Nodes.Count,
            EdgeCount = graph.Edges.Count,
        };
        var cpgPerformance = CpgPerformanceFactMapper.Map(
          file.FilePath,
          CpgPerformanceFactMapper.ComputeSourceIdentity(file.Source),
          perFileMetrics);
        RecordCpgBuildStages(runtime, file.FilePath, cpgPerformance);
        var cpgAnalysisContext = new CpgAnalysisContext(
          graph,
          file.SemanticModel,
          file.Root,
          configuration.AvailableCapabilities,
          CallFlowResolver: configuration.CallFlowResolver);
        var session = new AnalysisSession(cpgAnalysisContext, settings, runtime: runtime);
        return RunAnalysis(new AnalysisContext(
          file.Root,
          file.SemanticModel,
          session,
          cpgAnalysisContext,
          cpgPerformance));
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
        RecordRuleGraphStages(analysisContext, ruleGraphTelemetry);

        var filteredDecisions = FilterUnsafeLocalDeclarationDeletes(
          decisions,
          analysisContext.SemanticModel);
        var executablePlan = graphResult.ExecutablePlan is { } validatedPlan &&
          filteredDecisions.Count == validatedPlan.Decisions.Count
            ? validatedPlan
            : graphResult.ExecutablePlan is { } plan
              ? new ExecutablePlan(plan.DecisionPlan, filteredDecisions)
              : null;
        var rewriteScope = analysisContext.Session.Runtime.PerformanceStageCollector is not null
          ? PerformanceStageScope.Start(
            PerformanceStageId.ArtifactRewrite,
            PerformanceStageId.Run,
            analysisContext.CpgPerformance.ItemId,
            PerformanceAttributionLevel.Stage)
          : null;
        var rewriteSkipped = ShouldSkipRewrite(analysisContext.Session) ||
          executablePlan is null ||
          validationReport is { IsValid: false };
        PrototypeRewriteResult rewriteResult;
        try
        {
            rewriteResult = rewriteSkipped
              ? new PrototypeRewriteResult(
                null,
                Array.Empty<RewriteEdit>(),
                DiffDocument.Empty)
              : _rewriter.Rewrite(
                analysisContext.Root,
                analysisContext.SemanticModel,
                executablePlan!);
        }
        catch (Exception exception)
        {
            if (rewriteScope is not null)
            {
                analysisContext.Session.Runtime.PerformanceStageCollector!.Record(
                  rewriteScope.Fail(exception));
            }

            throw;
        }
        if (rewriteScope is not null)
        {
            analysisContext.Session.Runtime.PerformanceStageCollector!.Record(
              rewriteScope.Complete(
                rewriteSkipped ? PerformanceStatus.Skipped : PerformanceStatus.Completed));
        }
        var originalSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [analysisContext.Root.SyntaxTree.FilePath] = analysisContext.Root.ToFullString()
        };
        var rewrittenSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [analysisContext.Root.SyntaxTree.FilePath] = rewriteResult.RewrittenSource ?? analysisContext.Root.ToFullString()
        };
        var operationsByFile = new Dictionary<string, IReadOnlyList<RewritePlanEdit>>(StringComparer.Ordinal)
        {
            [analysisContext.Root.SyntaxTree.FilePath] = rewriteResult.Operations ?? Array.Empty<RewritePlanEdit>()
        };
        var verification = new RewriteVerifier().Verify(
            originalSources,
            rewrittenSources,
            filteredDecisions,
            operationsByFile);

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
            PerformanceStatus.Completed),
          Verification: verification);
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
            runtime.ExecutionOptions.CancellationToken)
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
        var configuration = CreateBuilderConfiguration(semanticModel.Compilation, runtime);
        var builder = new NLCPGBuilder(configuration.Options);
        var graph = builder.BuildFromSemanticModel(
          semanticModel,
          root,
          source,
          filePath);
        var cpgPerformance = CpgPerformanceFactMapper.Map(
          filePath,
          CpgPerformanceFactMapper.ComputeSourceIdentity(source),
          builder.LastBuildMetrics);
        RecordCpgBuildStages(runtime, filePath, cpgPerformance);
        var cpgAnalysisContext = new CpgAnalysisContext(
          graph,
          semanticModel,
          root,
          configuration.AvailableCapabilities,
          CallFlowResolver: configuration.CallFlowResolver);
        var session = new AnalysisSession(cpgAnalysisContext, settings, runtime: runtime);

        return new AnalysisContext(
          root,
          semanticModel,
          session,
          cpgAnalysisContext,
          cpgPerformance);
    }

    /// <summary>
    /// 组装构图配置。单文件与批量两条路径**共用**本方法，以免二者漂移。
    /// </summary>
    private BuilderConfiguration CreateBuilderConfiguration(
      Compilation compilation,
      AnalysisRuntime runtime)
    {
        var callFlowResolver = _callFlowResolver ??
          _defaultCallFlowResolvers.GetOrAdd(
            compilation,
            FlowSummaryResolverFactory.Create);
        var builderOptions = NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = runtime.CurrentCpgBuildAdmissionLease!.GrantedDegree,
            RequestedCapabilities = _pipeline.GetRequiredCapabilities(),
            CallFlowResolver = callFlowResolver,
            PerformanceDiagnostics = runtime.PartitionPerformanceEventSink is null
              ? NLCPGPerformanceDiagnosticsMode.Disabled
              : NLCPGPerformanceDiagnosticsMode.Diagnostic,
            PartitionPerformanceEventSink = runtime.PartitionPerformanceEventSink,
            PerformanceRunId = runtime.PerformanceRunId,
        };
        var requestedCapabilities = builderOptions.RequestedCapabilities ?? new[] { NLCPGCapability.Default };
        var availableCapabilities = requestedCapabilities.Aggregate(
          NLCPGCapability.None,
          static (current, capability) => current | capability);
        return new BuilderConfiguration(builderOptions, availableCapabilities, callFlowResolver);
    }

    /// <summary>记录一次 CPG 构建的阶段样本（单文件与批量两条路径共用）。</summary>
    private static void RecordCpgBuildStages(
      AnalysisRuntime runtime,
      string filePath,
      CpgPerformanceFacts cpgPerformance)
    {
        runtime.PerformanceStageCollector?.Record(new PerformanceStageSample(
          PerformanceStageId.CpgBuild,
          PerformanceStageId.Run,
          filePath,
          cpgPerformance.BuildElapsedMs,
          cpgPerformance.BuildElapsedMs,
          cpgPerformance.Status,
          cpgPerformance.ErrorKind,
          nodeDelta: cpgPerformance.NodeCount,
          edgeDelta: cpgPerformance.EdgeCount,
          attribution: PerformanceAttributionLevel.Stage));
        foreach (var pass in cpgPerformance.PassSamples)
        {
            runtime.PerformanceStageCollector?.Record(new PerformanceStageSample(
              pass.StageId,
              PerformanceStageId.CpgBuild,
              filePath,
              pass.WallElapsedMs,
              pass.WallElapsedMs,
              cpgPerformance.Status,
              cpgPerformance.ErrorKind,
              attribution: PerformanceAttributionLevel.Stage));
        }
    }

    private static bool ShouldSkipRewrite(AnalysisSession session)
    {
        return session.Settings.SkipRewrite;
    }

    private static void RecordRuleGraphStages(
      AnalysisContext analysisContext,
      IReadOnlyList<RuleGraphNodeTelemetry> telemetry)
    {
        var collector = analysisContext.Session.Runtime.PerformanceStageCollector;
        if (collector is null)
        {
            return;
        }

        foreach (var group in telemetry.GroupBy(node => node.NodeId.Kind).OrderBy(group => group.Key))
        {
            var samples = group.ToArray();
            collector.Record(new PerformanceStageSample(
              PerformanceStageId.ForRule(group.Key),
              PerformanceStageId.Run,
              analysisContext.CpgPerformance.ItemId,
              samples.Length == 0 ? null : samples.Max(sample => sample.ElapsedMilliseconds),
              samples.Length == 0 ? null : samples.Sum(sample => sample.ElapsedMilliseconds),
              samples.All(sample => sample.Status == RuleGraphNodeStatus.Disabled)
                ? PerformanceStatus.Skipped
                : PerformanceStatus.Completed,
              attribution: PerformanceAttributionLevel.Stage,
              inputCount: samples.Sum(sample => sample.InputCount),
              outputCount: samples.Sum(sample => sample.OutputCount)));
        }
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

    /// <summary>构图配置（单文件与批量两条路径共用），避免二者漂移。</summary>
    private sealed record BuilderConfiguration(
      NLCPGBuilderOptions Options,
      NLCPGCapability AvailableCapabilities,
      ICallFlowResolver CallFlowResolver);
}

/// <summary>
/// 批量分析的一个输入文件：源码 + 它在**共享 compilation** 中的语义模型与语法根。
/// </summary>
/// <remarks>
/// 语义模型必须来自同一个 <see cref="Compilation"/>——这是跨文件调用能解析出来的前提。
/// 调用方（目录分析）已为整个目录建好一份 compilation，故直接传入其语义模型，
/// 而不是让 <c>ApplicationService</c> 再解析一遍（那会得到两个不同的实例）。
/// </remarks>
public sealed record BatchAnalysisFile(
  string FilePath,
  string Source,
  SemanticModel SemanticModel,
  SyntaxNode Root);
