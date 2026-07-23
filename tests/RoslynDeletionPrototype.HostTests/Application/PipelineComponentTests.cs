using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Analysis.FlowSummaries;
using System.Text;
using RoslynPrototype.Analysis;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Lifting;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using Rules;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.DeleteClass;
using RoslynPrototype.Tests.TestCodeSet.DeleteClassDirectory;
using RoslynPrototype.Tests.TestCodeSet.Pipeline;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PipelineComponentTests : IDisposable
{
    private const string DeleteSObjectGroupKey = DeleteSObjectRuleIds.GroupKey;
    private readonly string _tempDirectory;

    public PipelineComponentTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void FlowSummaryRegistry_ProjectOverride_TakesPrecedenceOverFrameworkAndUnknown()
    {
        var framework = new RoslynCpgFlowSummary(
          "framework",
          "Demo.Helpers",
          "Map",
          0,
          new[] { RoslynCpgFlowSummaryEndpoint.Parameter(0) },
          RoslynCpgFlowSummaryEndpoint.Return);
        var project = framework with { Sources = new[] { RoslynCpgFlowSummaryEndpoint.Receiver } };
        var registry = new RoslynCpgFlowSummaryRegistry(new[] { project }, new[] { framework });

        var resolved = registry.Resolve(project.StableKey);

        Assert.Equal(RoslynCpgFlowSummaryResolution.Project, resolved.Resolution);
        Assert.Same(project, resolved.Summary);
        Assert.Equal(RoslynCpgFlowSummaryResolution.Unknown, registry.Resolve("missing").Resolution);
    }

    [Fact]
    public void DeletionRulePipeline_HelperReturnSlicePilot_IsOptInAndDoesNotChangeRuleResults()
    {
        var pipeline = new DeletionRulePipeline(
          Array.Empty<RuleDefinitionMark>(),
          new RuleDefinitionPropagate[] { new DeleteClassSymbolReferencePropagationRule() },
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());

        var disabled = pipeline.GetRequiredCapabilities();
        var enabled = (pipeline with { EnableHelperReturnSlicePilot = true }).GetRequiredCapabilities();

        Assert.DoesNotContain(RoslynCpgCapability.InterproceduralDataFlow, disabled);
        Assert.Contains(RoslynCpgCapability.InterproceduralDataFlow, enabled);
    }

    [Fact]
    public void Analyze_SyntaxSemanticRule_SkipsDataFlowOverlay()
    {
        var application = new DeletionApplicationService(
          new RuleDefinitionMark[] { new SyntaxSemanticOnlyMarkRule() },
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());

        var result = application.Analyze(
          "namespace Demo; public sealed class Sample { public int Run(int value) => value + 1; }",
          "syntax-semantic-rule.cs",
          new Dictionary<string, string>());

        Assert.Contains(RoslynCpgCapability.SyntaxSemantic, result.CpgBuildTelemetry!.ResolvedCapabilities!);
        Assert.Contains("DataFlowPass", result.CpgBuildTelemetry.SkippedPassNames!);
    }

    [Fact]
    public void AnalyzeFromArgs_WithSkipRewrite_DoesNotRetainRewrittenSource()
    {
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          "--target-name",
          "s",
          "--skip-rewrite",
          "--no-diff"
        });

        Assert.NotEmpty(result.Decisions);
        Assert.Empty(result.Edits);
        Assert.Null(result.RewrittenSource);
        Assert.Empty(result.Diff);
    }

    [Fact]
    public void Analyze_RuntimeConfiguredDop_ReportsConfiguredCpgDop()
    {
        var source = PipelineSources.RuntimeConfiguredDopSource;
        var runtime = new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(MaxDegreeOfParallelism: 3),
          new DeletionAnalysisEpoch(0, 0, 0));
        var application = new DeletionApplicationService(
          Array.Empty<RuleDefinitionMark>(),
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());

        var result = application.Analyze(
          source,
          "runtime-cpg-dop.cs",
          new Dictionary<string, string>(),
          runtime);

        Assert.NotNull(result.CpgBuildTelemetry);
        Assert.Equal(3, result.CpgBuildTelemetry!.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Analyze_RuntimeConfiguredCpgDop_ReportsCpgOverride()
    {
        var source = PipelineSources.RuntimeConfiguredDopSource;
        var runtime = new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 12,
            CpgMaxDegreeOfParallelism: 1),
          new DeletionAnalysisEpoch(0, 0, 0));
        var application = new DeletionApplicationService(
          Array.Empty<RuleDefinitionMark>(),
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());

        var result = application.Analyze(
          source,
          "runtime-cpg-override.cs",
          new Dictionary<string, string>(),
          runtime);

        Assert.Equal(12, runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
        Assert.Equal(1, result.CpgBuildTelemetry!.MaxDegreeOfParallelism);
    }

    [Fact]
    public void CreateFromOptions_WithCpgDopOverride_UsesExplicitCpgValue()
    {
        var runtime = DeletionAnalysisRuntime.CreateFromOptions(
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["max-degree-of-parallelism"] = "12",
            ["cpg-max-degree-of-parallelism"] = "1"
          });

        Assert.Equal(12, runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
        Assert.Equal(1, runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism);
    }

    [Fact]
    public void CreateFromOptions_WithoutCpgDopOverride_InheritsGlobalValue()
    {
        var runtime = DeletionAnalysisRuntime.CreateFromOptions(
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["max-degree-of-parallelism"] = "12"
          });

        Assert.Equal(12, runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("true")]
    public void CreateFromOptions_WithInvalidCpgDopOverride_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
          DeletionAnalysisRuntime.CreateFromOptions(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["cpg-max-degree-of-parallelism"] = value
            }));

        Assert.Contains("--cpg-max-degree-of-parallelism", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkingEngine_Run_WithGroupParallelism_RunsIndependentRulesConcurrently()
    {
        var source = PipelineSources.ConcurrentMarkingSource;
        var runtime = new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 2,
            EnableGroupParallelism: true),
          new DeletionAnalysisEpoch(0, 0, 0));
        var (context, root) = CreateContext(source, runtime: runtime);
        var probe = new ConcurrentRuleProbe(expectedConcurrentRules: 2);
        var rules = new RuleDefinitionMark[]
        {
          new ConcurrentClassMarkRule("TEST-CONCURRENT-MARK-001", "test-concurrent-first", "First", probe),
          new ConcurrentClassMarkRule("TEST-CONCURRENT-MARK-002", "test-concurrent-second", "Second", probe)
        };

        var marks = new MarkingEngine().Run(context, root, rules);

        Assert.Equal(2, marks.Count);
        Assert.Equal(2, probe.PeakActiveRuleCount);
    }

    [Fact]
    public void MarkingEngine_Run_DeduplicatesSameRuleAndSyntaxSpan()
    {
        var source = SObjectExpressionSources.MarkingDedupSource;

        var (context, root) = CreateContext(source, "s");
        var engine = new MarkingEngine();
        var rules = new RuleDefinitionMark[] { new DuplicateSeedRule() };

        var marks = engine.Run(context, root, rules);

        var mark = Assert.Single(marks);
        Assert.NotNull(mark.Annotation);
        Assert.NotNull(mark.PrimaryGraphNode);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)mark.SyntaxNode.RawKind);
    }

    [Fact]
    public void MarkingEngine_Run_RecordsPerRuleLedgerBeforeFinalDeduplication()
    {
        var source = SObjectExpressionSources.MarkingDedupSource;
        var (context, root) = CreateContext(source, "s");
        var engine = new MarkingEngine();

        var marks = engine.Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new DuplicateSeedRule(),
            new EmptyMarkRule()
          });

        var ledger = context.MarkAnalysisTelemetry.RuleTelemetry;
        var duplicateRule = Assert.Single(ledger, item => item.RuleId == "TEST-DUP-SEED");
        var emptyRule = Assert.Single(ledger, item => item.RuleId == "TEST-EMPTY-MARK");

        Assert.Single(marks);
        Assert.Equal(2, duplicateRule.CandidateMarkCount);
        Assert.Equal(2, duplicateRule.AcceptedMarkCount);
        Assert.Equal(2, duplicateRule.GraphBindingFallbackCount);
        Assert.Equal(2, duplicateRule.GraphBindingIndexHitCount);
        Assert.Equal(0, emptyRule.CandidateMarkCount);
        Assert.Equal(0, emptyRule.AcceptedMarkCount);
    }

    [Fact]
    public void MarkingEngine_Run_SObjectRules_PreservesSeedMarksAcrossGroupParallelismAndUsesSnapshotCaches()
    {
        var source = PipelineSources.SnapshotCacheSource;
        var (serialContext, serialRoot) = CreateContext(source, "s");
        var parallelRuntime = new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 4,
            EnableGroupParallelism: true),
          new DeletionAnalysisEpoch(0, 0, 0));
        var (parallelContext, parallelRoot) = CreateContext(source, "s", parallelRuntime);
        var engine = new MarkingEngine();

        var serialMarks = engine.Run(serialContext, serialRoot, GetDeleteSObjectMarkRules());
        var parallelMarks = engine.Run(parallelContext, parallelRoot, GetDeleteSObjectMarkRules());
        var targetIdentifier = parallelRoot.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .First(identifier => identifier.Identifier.ValueText == "s");

        _ = parallelContext.GetCachedOperation(targetIdentifier);
        _ = parallelContext.GetCachedOperation(targetIdentifier);

        Assert.Equal(BuildMarkKeys(serialMarks), BuildMarkKeys(parallelMarks));
        Assert.All(serialMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        Assert.All(parallelMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        Assert.True(parallelContext.MarkAnalysisTelemetry.AtomicCandidateIndexMissCount > 0);
        Assert.True(parallelContext.MarkAnalysisTelemetry.OperationLookupCacheHitCount > 0);
        Assert.True(parallelContext.MarkAnalysisTelemetry.GraphBindingIndexHitCount > 0);
        Assert.Equal(
          GetDeleteSObjectMarkRules().Select(rule => rule.RuleId),
          parallelContext.MarkAnalysisTelemetry.RuleTelemetry.Select(item => item.RuleId));
        Assert.True(parallelContext.MarkAnalysisTelemetry.RuleTelemetry.Sum(
          item => item.AtomicCandidateIndexHitCount + item.AtomicCandidateIndexMissCount) > 0);
    }

    [Fact]
    public void PropagationEngine_Run_DeduplicatesSamePropagatedSpan()
    {
        var source = SObjectControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var engine = new PropagationEngine();
        var rules = new RuleDefinitionPropagate[] { new DuplicatePropagationRule() };

        var propagatedMarks = engine.Run(context, seedMarks, rules);

        var propagated = Assert.Single(propagatedMarks);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)propagated.Mark.SyntaxNode.RawKind);
        Assert.NotNull(propagated.Mark.PrimaryGraphNode);
    }

    [Fact]
    public void PropagationEngine_Run_ChainsIndependentRulesWithinSameGroupKey()
    {
        var source = PipelineSources.ChainedPropagationSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var engine = new PropagationEngine();
        var rules = new RuleDefinitionPropagate[]
        {
            new DefinitionLeftValuePropagationRule(),
            new LocalReferenceFromDeclaratorPropagationRule()
        };

        var propagatedMarks = engine.Run(context, seedMarks, rules);

        Assert.Contains(
          propagatedMarks,
          mark => mark.RuleId == "TEST-CHAIN-DECL-001" &&
            mark.Mark.SyntaxNode is VariableDeclaratorSyntax declarator &&
            string.Equals(declarator.Identifier.ValueText, "value", StringComparison.Ordinal));
        Assert.Contains(
          propagatedMarks,
          mark => mark.RuleId == "TEST-CHAIN-REF-001" &&
            mark.Mark.SyntaxNode is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "value", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_BuildsRuleScopedStructureViewForEachRule()
    {
        var source = SObjectControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new ViewAwarePropagationRule() });

        var propagatedMark = Assert.Single(propagatedMarks);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)propagatedMark.Mark.SyntaxNode.RawKind);
    }

    [Fact]
    public void Analyze_DirectCall_UsesRuntimeDerivedFromOptions()
    {
        var source = PipelineSources.RuntimeAwareSource;
        var application = new DeletionApplicationService(
          new RuleDefinitionMark[] { new RuntimeAwareMarkRule() },
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());

        var result = application.Analyze(
          source,
          "runtime-aware.cs",
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["max-degree-of-parallelism"] = "3",
            ["disable-helper-parallelism"] = "true",
            ["enable-group-parallelism"] = "true"
          });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Contains("mdop=3", seedMark.Reason, StringComparison.Ordinal);
        Assert.Contains("group=True", seedMark.Reason, StringComparison.Ordinal);
        Assert.Contains("helper=False", seedMark.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletionAnalysisRuntime_GetOrCreateCompilationCache_AllowsMultipleCacheTypesPerCompilation()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Demo; public sealed class Sample { }", path: "runtime-cache.cs");
        var compilation = CreateCompilation(tree);
        var runtime = DeletionAnalysisRuntime.CreateDefault();

        var firstCache = GetCompilationCache(
          runtime,
          compilation,
          static currentCompilation => new TestCompilationCacheA(currentCompilation.AssemblyName ?? "unknown"));
        var secondCache = GetCompilationCache(
          runtime,
          compilation,
          static currentCompilation => new TestCompilationCacheB((currentCompilation.SyntaxTrees.Count(), 7)));
        var firstCacheAgain = GetCompilationCache(
          runtime,
          compilation,
          static _ => new TestCompilationCacheA("should-not-recreate"));

        Assert.Equal(compilation.AssemblyName, firstCache.Value);
        Assert.Equal((1, 7), secondCache.Value);
        Assert.Same(firstCache, firstCacheAgain);
    }

    [Fact]
    public void MarkingEngine_Run_EnableGroupParallelism_UsesScheduler()
    {
        var source = PipelineSources.ParallelMarkingSource;
        var scheduler = new RecordingScheduler();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var engine = new MarkingEngine();

        var marks = engine.Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelClassMarkRule("TEST-PARALLEL-MARK-A", "TEST-GROUP-A", "Alpha"),
            new ParallelClassMarkRule("TEST-PARALLEL-MARK-B", "TEST-GROUP-B", "Beta")
          });

        Assert.Equal(1, scheduler.InvocationCount);
        Assert.Equal(new[] { 2 }, scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-PARALLEL-MARK-A", "TEST-PARALLEL-MARK-B" },
          marks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void PropagationEngine_Run_EnableGroupParallelism_UsesScheduler()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingScheduler();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelClassMarkRule("TEST-PROP-SEED-A", "TEST-GROUP-A", "Alpha"),
            new ParallelClassMarkRule("TEST-PROP-SEED-B", "TEST-GROUP-B", "Beta")
          });
        scheduler.Reset();
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[]
          {
            new GroupMethodPropagationRule("TEST-PROP-A", "TEST-GROUP-A"),
            new GroupMethodPropagationRule("TEST-PROP-B", "TEST-GROUP-B")
          });

        Assert.Equal(1, scheduler.InvocationCount);
        Assert.Equal(new[] { 2 }, scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-PROP-A", "TEST-PROP-B" },
          propagatedMarks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void MarkLiftingEngine_Run_EnableGroupParallelism_UsesScheduler()
    {
        var source = PipelineSources.ParallelMarkingSource;
        var scheduler = new RecordingScheduler();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelClassMarkRule("TEST-LIFT-SEED-A", "TEST-GROUP-A", "Alpha"),
            new ParallelClassMarkRule("TEST-LIFT-SEED-B", "TEST-GROUP-B", "Beta")
          });
        scheduler.Reset();
        var engine = new MarkLiftingEngine();

        var liftedMarks = engine.Run(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          new RuleDefinitionLift[]
          {
            new NamespaceLiftRule("TEST-LIFT-A1", "TEST-GROUP-A"),
            new NamespaceLiftRule("TEST-LIFT-A2", "TEST-GROUP-A"),
            new NamespaceLiftRule("TEST-LIFT-B", "TEST-GROUP-B")
          });

        Assert.Equal(1, scheduler.InvocationCount);
        Assert.Equal(new[] { 2 }, scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-LIFT-A1", "TEST-LIFT-B" },
          liftedMarks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void RuleDecisionEngine_Decide_EnableGroupParallelism_UsesScheduler()
    {
        var source = PipelineSources.ParallelMarkingSource;
        var scheduler = new RecordingScheduler();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelClassMarkRule("TEST-DECIDE-SEED-A", "TEST-GROUP-A", "Alpha"),
            new ParallelClassMarkRule("TEST-DECIDE-SEED-B", "TEST-GROUP-B", "Beta")
          });
        scheduler.Reset();
        var engine = new RuleDecisionEngine();

        var decisions = engine.Decide(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          Array.Empty<LiftedMarkRecord>(),
          new RuleDefinitionPropose[]
          {
            new DeleteClassDecisionRule("TEST-DECIDE-A1", "TEST-GROUP-A"),
            new DeleteClassDecisionRule("TEST-DECIDE-A2", "TEST-GROUP-A"),
            new DeleteClassDecisionRule("TEST-DECIDE-B", "TEST-GROUP-B")
          });

        Assert.Equal(1, scheduler.InvocationCount);
        Assert.Equal(new[] { 2 }, scheduler.ItemCounts);
        Assert.Equal(
          new[] { "Delete TEST-DECIDE-A1", "Delete TEST-DECIDE-B" },
          decisions.Select(decision => decision.Reason).ToArray());
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassMethodParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassMethodParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-method-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-method-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassMethodParameterUsagePropagationRule() });

        var methodPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyPrivate", StringComparison.Ordinal));
        var payload = Assert.IsType<MethodParameterUsagePayload>(methodPropagation.Payload);
        Assert.Equal(MethodParameterUsageMode.PrivatePositional, payload.Mode);
        Assert.Equal(0, payload.ParameterIndex);
        Assert.Single(payload.InvocationCallsites);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is InvocationExpressionSyntax invocation &&
            string.Equals(invocation.ToString(), "ApplyPrivate(null, frame)", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassLocalFunctionParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassLocalFunctionParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-local-function-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-local-function-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassLocalFunctionParameterUsagePropagationRule() });

        var functionPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is LocalFunctionStatementSyntax localFunction &&
            string.Equals(localFunction.Identifier.ValueText, "ApplyLocal", StringComparison.Ordinal));
        var payload = Assert.IsType<LocalFunctionParameterUsagePayload>(functionPropagation.Payload);
        Assert.Equal(LocalFunctionParameterUsageMode.Positional, payload.Mode);
        Assert.Equal(0, payload.ParameterIndex);
        Assert.Single(payload.InvocationCallsites);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is InvocationExpressionSyntax invocation &&
            string.Equals(invocation.ToString(), "ApplyLocal(null, frame)", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassIndexerParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassIndexerParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-indexer-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-indexer-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassIndexerParameterUsagePropagationRule() });

        var indexerPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is IndexerDeclarationSyntax);
        var payload = Assert.IsType<IndexerParameterUsagePayload>(indexerPropagation.Payload);
        Assert.Equal(IndexerParameterUsageMode.Positional, payload.Mode);
        Assert.Equal(0, payload.ParameterIndex);
        Assert.Single(payload.AccessCallsites);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is ElementAccessExpressionSyntax access &&
            string.Equals(access.ToString(), "board[null, slot]", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassDelegateUsageClassificationRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassDelegateUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-delegate-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-delegate-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassDelegateUsageClassificationPropagationRule() });

        var delegatePropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Handler", StringComparison.Ordinal));
        var payload = Assert.IsType<DelegateUsagePayload>(delegatePropagation.Payload);
        Assert.Equal(DelegateUsageMode.MethodGroup, payload.Mode);
        Assert.Equal(0, payload.ParameterIndex);
        Assert.Single(payload.MethodTargets);
        Assert.Empty(payload.LocalFunctionTargets);
        Assert.Empty(payload.LambdaTargets);
        Assert.Equal(2, payload.InvocationCallsites.Count);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Apply", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassExtensionMethodMappedCallsiteRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassExtensionMethodSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-extension-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-extension-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassExtensionMethodMappedCallsitePropagationRule() });

        var methodPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Use", StringComparison.Ordinal));
        var payload = Assert.IsType<ExtensionMethodMappedCallsitePayload>(methodPropagation.Payload);
        Assert.Equal(1, payload.ParameterIndex);
        Assert.Single(payload.InvocationCallsites);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is InvocationExpressionSyntax invocation &&
            string.Equals(invocation.ToString(), "text.Use(null, frame)", StringComparison.Ordinal));
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassDeclarationHostRule_ProducesStructuredPayloads()
    {
        var source = PipelineSources.DeleteClassDeclarationHostSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-declaration-host-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-declaration-host-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassDeclarationHostPropagationRule() });

        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is FieldDeclarationSyntax &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.FieldDeclaration);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is PropertyDeclarationSyntax property &&
            string.Equals(property.Identifier.ValueText, "Property", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.PropertyDeclaration);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Create", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.MethodReturnType);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is PropertyDeclarationSyntax property &&
            string.Equals(property.Identifier.ValueText, "Current", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.InterfaceProperty);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Apply", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.InterfaceMethod);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is IndexerDeclarationSyntax &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.InterfaceIndexer);
        Assert.Contains(
          propagatedMarks,
          mark => (mark.Mark.SyntaxNode is EventDeclarationSyntax || mark.Mark.SyntaxNode is EventFieldDeclarationSyntax) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.InterfaceEvent);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Build", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.DelegateReturnType);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Score", StringComparison.Ordinal) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.ExtensionReceiverMethod);
        Assert.Contains(
          propagatedMarks,
          mark => (mark.Mark.SyntaxNode is BaseListSyntax || mark.Mark.SyntaxNode is SimpleBaseTypeSyntax) &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.BaseType);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is LocalDeclarationStatementSyntax &&
            mark.Payload is DeclarationHostPayload payload &&
            payload.Kind == DeclarationHostKind.LocalGenericTypeArgument);
    }

    [Fact]
    public void PropagationEngine_Run_DeleteSObjectLogicalOperandGroupRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.LogicalOperandGroupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteSObjectLogicalOperandGroupPropagationRule() });

        var logicalPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is BinaryExpressionSyntax binaryExpression &&
            string.Equals(binaryExpression.ToString(), "s.IsReady && ready && fallback", StringComparison.Ordinal));
        var payload = Assert.IsType<LogicalHostPayload>(logicalPropagation.Payload);
        Assert.Single(payload.RemovableOperands);
        Assert.Equal("s.IsReady", payload.RemovableOperands[0].ToString());
        Assert.Equal(2, payload.SurvivorOperands.Count);
        Assert.Equal("ready", payload.SurvivorOperands[0].ToString());
        Assert.Equal("fallback", payload.SurvivorOperands[1].ToString());
    }

    [Fact]
    public void PropagationEngine_Run_DeleteSObjectIfStructureCompletionRule_ProducesStructuredPayloads()
    {
        var source = PipelineSources.SObjectIfStructureCompletionSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteSObjectIfStructureCompletionPropagationRule() });

        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is IfStatementSyntax ifStatement &&
            string.Equals(ifStatement.Condition.ToString(), "s.IsReady", StringComparison.Ordinal) &&
            mark.Payload is IfStructureCompletionPayload payload &&
            payload.Kind == IfStructureCompletionKind.ReplaceIfWithElseIfTail &&
            payload.TailNode is IfStatementSyntax);
        Assert.Contains(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is ElseClauseSyntax &&
            mark.Payload is IfStructureCompletionPayload payload &&
            payload.Kind == IfStructureCompletionKind.DeleteOwningElseClause &&
            payload.ParentElseClause is not null);
    }

    [Fact]
    public void PropagationEngine_Run_DeleteClassIfStructureCompletionRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DeleteClassIfStructureCompletionSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-if-structure-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(
          source,
          "delete-class-if-structure-propagation.cs");
        var context = new RuleContext(
          new CpgAnalysisContext(graph, semanticModel, root),
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["delete-class"] = "PlayerInput"
          });
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteClassMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeleteClassIfStructureCompletionPropagationRule() });

        var ifPropagation = Assert.Single(
          propagatedMarks,
          mark => mark.Mark.SyntaxNode is IfStatementSyntax ifStatement &&
            string.Equals(ifStatement.Condition.ToString(), "input.IsReady", StringComparison.Ordinal));
        var payload = Assert.IsType<IfStructureCompletionPayload>(ifPropagation.Payload);
        Assert.Equal(IfStructureCompletionKind.ReplaceIfWithElseIfTail, payload.Kind);
        Assert.IsType<IfStatementSyntax>(payload.TailNode);
    }

    [Fact]
    public void MarkLiftingEngine_Run_BuildsRuleScopedStructureViewForEachRule()
    {
        var source = SObjectControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetDeleteSObjectMarkRules());
        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DuplicatePropagationRule() });
        var engine = new MarkLiftingEngine();

        var liftedMarks = engine.Run(
          context,
          seedMarks,
          propagatedMarks,
          new RuleDefinitionLift[] { new ViewAwareLiftRule() });

        var liftedMark = Assert.Single(liftedMarks);
        Assert.Equal(SyntaxKind.ReturnStatement, (SyntaxKind)liftedMark.Mark.SyntaxNode.RawKind);
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_ReplacesExpressionsAndDeletesStatements()
    {
        var source = RewriteSources.ReplaceAndDeleteSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "rewrite-test.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var declaration = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
        var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>().First(node => node.Identifier.ValueText == "temp");
        var decisions = new[]
        {
            new RuleDecision(identifier, identifier, DecisionActionKind.Delete, "Replace identifier with default."),
            new RuleDecision(declaration, declaration, DecisionActionKind.Delete, "Delete declaration.")
        };
        var rewriter = new PrototypeRewriter();

        var result = rewriter.Rewrite(root, semanticModel, decisions);

        Assert.Equal(2, result.Edits.Count);
        TextDiffAssert.Contains("default(int)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("var temp = value + 1;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("<deleted>", result.Diff, result.Diff);
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_DeepElseIfChain_DoesNotOverflowRewriteTraversal()
    {
        const int elseIfDepth = 400;
        var source = CreateDeepElseIfChainSource(elseIfDepth);

        var tree = CSharpSyntaxTree.ParseText(source, path: "deep-elseif-rewrite.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var targetElseClause = root.DescendantNodes().OfType<ElseClauseSyntax>().First();
        var replacementElseClause = SyntaxFactory.ElseClause(
            SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(-1)))));
        var decisions = new[]
        {
            new RuleDecision(
                targetElseClause,
                targetElseClause,
                DecisionActionKind.Replace,
                "Collapse deep else-if chain tail.",
                replacementElseClause)
        };
        var rewriter = new PrototypeRewriter();

        var exception = Record.Exception(() => rewriter.Rewrite(root, semanticModel, decisions));

        Assert.Null(exception);
        var result = rewriter.Rewrite(root, semanticModel, decisions);
        TextDiffAssert.Contains("return -1;", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("else if (flag1)", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_WhenParentDeleteOverlapsChildDelete_KeepsOuterRewrite()
    {
        var source = PipelineSources.OverlappingDeleteSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "overlap-delete.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        var readyIdentifier = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Single(node => node.Identifier.ValueText == "ready");
        var returnStatement = ifStatement.Statement.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var decisions = new[]
        {
            new RuleDecision(ifStatement, ifStatement, DecisionActionKind.Delete, "Delete enclosing if."),
            new RuleDecision(readyIdentifier, readyIdentifier, DecisionActionKind.Delete, "Delete nested condition symbol."),
            new RuleDecision(returnStatement, returnStatement, DecisionActionKind.Delete, "Delete nested return.")
        };
        var rewriter = new PrototypeRewriter();

        var exception = Record.Exception(() => rewriter.Rewrite(root, semanticModel, decisions));

        Assert.Null(exception);
        var result = rewriter.Rewrite(root, semanticModel, decisions);
        TextDiffAssert.DoesNotContain("if (ready)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 2;", result.RewrittenSource, result.Diff);
        Assert.Single(result.Edits);
    }

    [Fact]
    public void AnalyzeFromArgs_WritesDiffFileWhenInputProducesEdits()
    {
        var filePath = Path.Combine(_tempDirectory, "delete-s-object-sample.cs");
        var rawDiffPath = Path.Combine(_tempDirectory, "delete-s-object-sample.raw.diff");
        var aggregateDiffPath = BuildDiffArtifactWriter.GetDiffFilePath(
            "PipelineComponentTests.cs",
            "Cli");
        BuildDiffArtifactWriter.InitializeDiffFile(aggregateDiffPath);
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        Assert.NotNull(result.DiffFilePath);
        Assert.True(File.Exists(result.DiffFilePath));
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        BuildDiffArtifactWriter.AppendDiffFragment(
            aggregateDiffPath,
            nameof(AnalyzeFromArgs_WritesDiffFileWhenInputProducesEdits),
            File.ReadAllText(rawDiffPath));
        var aggregateDiffText = File.ReadAllText(aggregateDiffPath);
        TextDiffAssert.Contains(
          "UnitTest: AnalyzeFromArgs_WritesDiffFileWhenInputProducesEdits",
          aggregateDiffText,
          aggregateDiffText);
        TextDiffAssert.Contains("+++ rewritten #1", aggregateDiffText, aggregateDiffText);
    }

    [Fact]
    public void AnalyzeFromArgs_WithReadableDiffView_WritesReadableDiffFile()
    {
        var filePath = Path.Combine(_tempDirectory, "delete-s-object-readable.cs");
        var rawDiffPath = Path.Combine(_tempDirectory, "delete-s-object-readable.diff");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath,
            "--diff-view",
            "readable"
        });

        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.Equal(result.Diff.Summary, result.DiffSummary);

        var diffText = File.ReadAllText(rawDiffPath);
        Assert.Contains("diff-summary files=1 edits=", diffText, StringComparison.Ordinal);
        Assert.Contains("=== file", diffText, StringComparison.Ordinal);
        Assert.Contains("edit #1 kind=", diffText, StringComparison.Ordinal);
        Assert.Contains("--- before", diffText, StringComparison.Ordinal);
        Assert.Contains("+++ after", diffText, StringComparison.Ordinal);
        Assert.Equal(diffText, new TextDiffRenderer().Render(result.Diff, "readable"));
    }

    [Fact]
    public void AnalyzeFromArgs_UsesDefaultSourceWhenInputPathIsMissing()
    {
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[] { "--target-name", "s" });

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.NotEmpty(result.PropagatedMarks);
        Assert.Equal(2, result.Edits.Count);
        Assert.Null(result.DiffFilePath);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_DoesNotWriteDiffFileWhenAnalysisProducesNoEdits()
    {
        var filePath = Path.Combine(_tempDirectory, "no-edits-sample.cs");
        File.WriteAllText(filePath, MinimalSources.EmptyMainSource);
        var explicitDiffPath = Path.Combine(_tempDirectory, "no-edits.diff");
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "missing",
            "--diff-out",
            explicitDiffPath
        });

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(explicitDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_WithNoDiff_WritesBackSingleFileWithoutCreatingDiffFile()
    {
        var filePath = Path.Combine(_tempDirectory, "single-file-no-diff.cs");
        var expectedDiffPath = Path.Combine(_tempDirectory, "single-file-no-diff.rewrite.diff");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--write-back",
            "--no-diff"
        });

        Assert.NotEmpty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(expectedDiffPath));
        var rewrittenSource = File.ReadAllText(filePath);
        TextDiffAssert.Contains("return value;", rewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("var value = s.Seed + offset;", rewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_RewritesMultipleFilesAndKeepsCompilationValid()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-project");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var consumerFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo;

          public static class PlayerInput
          {
            public static bool Enabled => true;

            public static void Ping()
            {
            }
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          using System;

          namespace Demo;

          public sealed class Game
          {
            public void Run()
            {
              if (PlayerInput.Enabled)
              {
                Console.WriteLine(1);
              }

              PlayerInput.Ping();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back"
        });

        Assert.NotEmpty(result.SeedMarks);
        Assert.NotEmpty(result.Decisions);
        Assert.NotEmpty(result.Edits);
        Assert.NotNull(result.DiffFilePath);
        Assert.True(Directory.Exists(result.DiffFilePath));

        var rewrittenClassSource = File.ReadAllText(classFilePath);
        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        Assert.DoesNotContain("class PlayerInput", rewrittenClassSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("if (PlayerInput.Enabled)", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.Ping();", rewrittenConsumerSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenClassSource, path: classFilePath),
            CSharpSyntaxTree.ParseText(rewrittenConsumerSource, path: consumerFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_MaxDegreeOfParallelism_KeepsStableResults()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-parallelism-project");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var consumerFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo;

          public static class PlayerInput
          {
            public static bool Enabled => true;

            public static int Value()
            {
              return 1;
            }
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          using System;

          namespace Demo;

          public sealed class Game
          {
            public int Run()
            {
              if (PlayerInput.Enabled)
              {
                Console.WriteLine(PlayerInput.Value());
              }

              return 0;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var serialResult = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          "1",
          "--no-diff"
        });
        Assert.NotEmpty(serialResult.Edits);
        foreach (var maxDegreeOfParallelism in new[] { 16, 64 })
        {
            var parallelResult = application.AnalyzeFromArgs(new[]
            {
              projectDirectory,
              "--delete-class",
              "PlayerInput",
              "--max-degree-of-parallelism",
              maxDegreeOfParallelism.ToString(),
              "--no-diff"
            });

            Assert.NotEmpty(parallelResult.Edits);
            AssertEquivalentAnalysisResults(serialResult, parallelResult);
        }
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DisableHelperParallelism_KeepsStableResults()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-helper-parallelism-project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "PlayerInput.cs"),
          """
          namespace Demo;

          public sealed class PlayerInput
          {
          }
          """);
        File.WriteAllText(
          Path.Combine(projectDirectory, "Game.cs"),
          """
          namespace Demo;

          public sealed class Game
          {
            private int Apply(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return Apply(null, frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var defaultResult = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          "8",
          "--no-diff"
        });
        var helperSerialResult = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          "8",
          "--disable-helper-parallelism",
          "--no-diff"
        });

        Assert.NotEmpty(defaultResult.Edits);
        Assert.NotEmpty(helperSerialResult.Edits);
        AssertEquivalentAnalysisResults(defaultResult, helperSerialResult);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods_WithNoDiff_WritesBackWithoutCreatingDiffArtifacts()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-unreferenced-no-diff-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        var callerFilePath = Path.Combine(projectDirectory, "Caller.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Worker
          {
            public int Run()
            {
              return KeepAlive();
            }

            private int KeepAlive()
            {
              return 1;
            }

            private int DeadPrivate()
            {
              return 2;
            }
          }
          """);
        File.WriteAllText(
          callerFilePath,
          """
          namespace Demo;

          public sealed class Caller
          {
            public int Run()
            {
              return new Worker().Run();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-unreferenced-methods",
          "--write-back",
          "--no-diff"
        });

        Assert.NotEmpty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.Empty(Directory.EnumerateFiles(
          projectDirectory,
          "*.rewrite.diff",
          SearchOption.AllDirectories));
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("private int KeepAlive()", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("private int DeadPrivate()", rewrittenServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_WritesPerFileDiffsUnderConfiguredRoot()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-project-diff");
        var nestedDirectory = Path.Combine(projectDirectory, "Gameplay");
        Directory.CreateDirectory(nestedDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var consumerFilePath = Path.Combine(nestedDirectory, "Game.cs");
        var diffRootPath = Path.Combine(_tempDirectory, "diff-output-root");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo;

          public static class PlayerInput
          {
            public static bool Enabled => true;
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          namespace Demo;

          public sealed class Game
          {
            public bool Run()
            {
              return PlayerInput.Enabled;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--diff-out",
          diffRootPath
        });

        Assert.NotEmpty(result.Edits);
        Assert.Equal(Path.GetFullPath(diffRootPath), result.DiffFilePath);

        var declarationDiffPath = Path.Combine(diffRootPath, "PlayerInput.rewrite.diff");
        var consumerDiffPath = Path.Combine(diffRootPath, "Gameplay", "Game.rewrite.diff");
        Assert.True(File.Exists(declarationDiffPath));
        Assert.True(File.Exists(consumerDiffPath));
        Assert.Contains("class PlayerInput", File.ReadAllText(declarationDiffPath), StringComparison.Ordinal);
        Assert.Contains("PlayerInput.Enabled", File.ReadAllText(consumerDiffPath), StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ConcurrentDiffWrites_PreserveResultsAndDiffBytes()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-concurrent-diff-project");
        var gameplayDirectory = Path.Combine(projectDirectory, "Gameplay");
        var systemsDirectory = Path.Combine(projectDirectory, "Systems");
        Directory.CreateDirectory(gameplayDirectory);
        Directory.CreateDirectory(systemsDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "PlayerInput.cs"),
          DirectoryDeleteClassSources.PlayerInputEnabledSource);
        File.WriteAllText(
          Path.Combine(gameplayDirectory, "Game.cs"),
          DirectoryDeleteClassSources.GameUsingPlayerInputSource);
        File.WriteAllText(
          Path.Combine(systemsDirectory, "Renderer.cs"),
          DirectoryDeleteClassSources.RendererWithBlockBodyUsingPlayerInputSource);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
        var diffRootPath = Path.Combine(_tempDirectory, "concurrent-diff-output");
        var resultsByDegree = new Dictionary<int, PrototypeAnalysisResult>();
        var diffBytesByDegree = new Dictionary<int, IReadOnlyDictionary<string, byte[]>>();

        foreach (var maxDegreeOfParallelism in new[] { 1, 2, 16 })
        {
            var result = application.AnalyzeFromArgs(new[]
            {
              projectDirectory,
              "--delete-class",
              "PlayerInput",
              "--max-degree-of-parallelism",
              maxDegreeOfParallelism.ToString(),
              "--diff-out",
              diffRootPath
            });

            resultsByDegree.Add(maxDegreeOfParallelism, result);
            diffBytesByDegree.Add(
              maxDegreeOfParallelism,
              Directory.EnumerateFiles(diffRootPath, "*.rewrite.diff", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(diffRootPath, path), StringComparer.Ordinal)
                .ToDictionary(
                  path => Path.GetRelativePath(diffRootPath, path),
                  File.ReadAllBytes,
                  StringComparer.Ordinal));
        }

        var serialResult = resultsByDegree[1];
        var serialDiffBytes = diffBytesByDegree[1];
        foreach (var maxDegreeOfParallelism in new[] { 2, 16 })
        {
            AssertEquivalentAnalysisResults(serialResult, resultsByDegree[maxDegreeOfParallelism]);
            Assert.Equal(serialResult.DiffFilePath, resultsByDegree[maxDegreeOfParallelism].DiffFilePath);
            Assert.Equal(serialDiffBytes.Keys, diffBytesByDegree[maxDegreeOfParallelism].Keys);
            foreach (var relativePath in serialDiffBytes.Keys)
            {
                Assert.Equal(serialDiffBytes[relativePath], diffBytesByDegree[maxDegreeOfParallelism][relativePath]);
            }
        }
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_RewritesLargeAssetProjectAndKeepsCompilationValid()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-large-asset-project");
        DeleteClassLargeSources.WriteLargeProject(projectDirectory);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        Assert.NotEmpty(result.SeedMarks);
        Assert.NotEmpty(result.Decisions);
        Assert.NotEmpty(result.Edits);

        var rewrittenGameplay = File.ReadAllText(Path.Combine(projectDirectory, "Gameplay", "GameFlow.cs"));
        var rewrittenBindings = File.ReadAllText(Path.Combine(projectDirectory, "Ui", "HudBindings.cs"));
        var rewrittenContracts = File.ReadAllText(Path.Combine(projectDirectory, "Contracts", "InputContracts.cs"));

        Assert.DoesNotContain("PlayerInput", rewrittenGameplay, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput", rewrittenBindings, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput", rewrittenContracts, StringComparison.Ordinal);

        var rewrittenTrees = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
          .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
          .ToArray();
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_MarksTypeSyntaxReferencesAsAtomicComponents()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-type-syntax-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public class PlayerInputList : List<PlayerInput>
          {
          }

          public sealed class Game
          {
            private PlayerInput _field;

            public int Create(PlayerInput input)
            {
              return input is null ? 0 : 1;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenSource = File.ReadAllText(sourceFilePath);

        var typeSyntaxMarks = result.SeedMarks
          .Where(mark => string.Equals(
            mark.RuleId,
            DeleteClassRuleIds.TypeSyntaxMarkRuleId,
            StringComparison.Ordinal))
          .Select(mark => mark.SyntaxNode.ToString())
          .ToList();

        Assert.Contains("PlayerInput", typeSyntaxMarks);
        Assert.True(
          typeSyntaxMarks.Count == 3,
          $"Expected 3 type syntax marks, got {typeSyntaxMarks.Count}: {string.Join(", ", typeSyntaxMarks)}");
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesFieldAndPropertyDeclarationsWithTargetType()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-type-declaration-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private PlayerInput _field;

            public PlayerInput Current { get; set; }

            public PlayerInput Create(PlayerInput input)
            {
              return input;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is FieldDeclarationSyntax &&
            decision.Action == DecisionActionKind.Delete);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is PropertyDeclarationSyntax &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain("private PlayerInput _field;", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("public PlayerInput Current { get; set; }", result.RewrittenSource, result.Diff);
        Assert.DoesNotContain("public int Create(PlayerInput input)", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_PropagatesObjectCreationToLocalDeclaration()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-object-creation-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public void Run()
            {
              var input = new PlayerInput();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.PropagatedMarks,
          mark => string.Equals(
              mark.RuleId,
              "DEL-CLASS-PROP-NEW-DECL-001",
              StringComparison.Ordinal) &&
            mark.Mark.SyntaxNode is VariableDeclaratorSyntax declarator &&
            string.Equals(declarator.Identifier.ValueText, "input", StringComparison.Ordinal));
        TextDiffAssert.DoesNotContain("var input = new PlayerInput();", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_PropagatesLocalDeclarationToSameScopeReferences()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-local-reference-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public PlayerInput Run()
            {
              var input = new PlayerInput();
              return input;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--no-diff"
        });

        Assert.Contains(
          result.PropagatedMarks,
          mark => string.Equals(
              mark.RuleId,
              "DEL-CLASS-PROP-LOCAL-REF-001",
              StringComparison.Ordinal) &&
            mark.Mark.SyntaxNode is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "input", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesPrivateMethodsReturningTargetType()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-method-return-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private PlayerInput CreatePrivate()
            {
              return new PlayerInput();
            }

            public int Keep()
            {
              return 1;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "CreatePrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain(
          "private PlayerInput CreatePrivate()",
          result.RewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public int Keep()", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesPublicMethodsReturningTargetType()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-public-method-return-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public PlayerInput CreatePublic()
            {
              return new PlayerInput();
            }

            public int Keep()
            {
              return 1;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "CreatePublic", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain(
          "public PlayerInput CreatePublic()",
          result.RewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public int Keep()", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksPrivateMethodsWithTargetTypeParameterAndSyncsCallsites()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-method-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private int ApplyPrivate(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return ApplyPrivate(null, frame);
            }

            public void Keep(int frame)
            {
              _ = frame;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyPrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is InvocationExpressionSyntax invocation &&
            invocation.ToString().Contains("ApplyPrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.Contains(
          "private int ApplyPrivate(int frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyPrivate(frame);",
          rewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public void Keep(int frame)", result.Diff, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksPrivateMethodParameter_ForNamedArguments()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-method-parameter-named-argument-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private int ApplyPrivate(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return ApplyPrivate(input: null, frame: frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyPrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is InvocationExpressionSyntax invocation &&
            invocation.ToString().Contains("ApplyPrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "private int ApplyPrivate(int frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyPrivate(frame: frame);",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DoesNotShrinkPrivateMethodParameter_WhenNamedAndPositionalCallsitesAreMixed()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-method-parameter-mixed-callsite-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private int ApplyPrivate(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return ApplyPrivate(null, frame) + ApplyPrivate(input: null, frame: frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyPrivate", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "private int ApplyPrivate(PlayerInput input, int frame)",
          rewrittenSource,
          result.Diff);
        var diagnostics = result.Diagnostics ?? throw new InvalidOperationException("Expected diagnostics.");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksOptionalMethodParameter_AndKeepsOmittedCallsites()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-optional-method-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private int ApplyOptional(int frame, PlayerInput input = null, int scale = 1)
            {
              return frame * scale;
            }

            public int Run(int frame)
            {
              return ApplyOptional(frame)
                + ApplyOptional(frame, input: null, scale: 2)
                + ApplyOptional(frame, scale: 3);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyOptional", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "private int ApplyOptional(int frame, int scale = 1)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyOptional(frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "ApplyOptional(frame, scale: 2)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "ApplyOptional(frame, scale: 3)",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksParamsMethodParameter_WhenAllCallsitesOmitParamsSlot()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-params-method-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            private int ApplyParams(int frame, params PlayerInput[] inputs)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return ApplyParams(frame) + ApplyParams(1);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyParams", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "private int ApplyParams(int frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyParams(frame) + ApplyParams(1);",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksPublicMethodsWithTargetTypeParameterAndSyncsCallsites()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-public-method-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public int ApplyPublic(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              return ApplyPublic(null, frame);
            }

            public void Keep(int frame)
            {
              _ = frame;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "ApplyPublic", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is InvocationExpressionSyntax invocation &&
            invocation.ToString().Contains("ApplyPublic", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.Contains(
          "public int ApplyPublic(int frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyPublic(frame);",
          rewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public void Keep(int frame)", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksLocalFunctionParameterAndSyncsCallsites()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-local-function-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public int Run(int frame)
            {
              return ApplyLocal(null, frame);

              int ApplyLocal(PlayerInput input, int localFrame)
              {
                return localFrame;
              }
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is LocalFunctionStatementSyntax localFunction &&
            string.Equals(localFunction.Identifier.ValueText, "ApplyLocal", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is InvocationExpressionSyntax invocation &&
            invocation.ToString().Contains("ApplyLocal", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "int ApplyLocal(int localFrame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyLocal(frame);",
          rewrittenSource,
          result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksNamedArgumentLocalFunctionParameter()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-local-function-named-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public int Run(int frame)
            {
              return ApplyLocal(input: null, localFrame: frame);

              int ApplyLocal(PlayerInput input, int localFrame)
              {
                return localFrame;
              }
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is LocalFunctionStatementSyntax localFunction &&
            string.Equals(localFunction.Identifier.ValueText, "ApplyLocal", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is InvocationExpressionSyntax invocation &&
            invocation.ToString().Contains("ApplyLocal", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "int ApplyLocal(int localFrame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyLocal(localFrame: frame);",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksOptionalLocalFunctionParameter_AndKeepsOmittedCallsites()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-local-function-optional-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public int Run(int frame)
            {
              return ApplyLocal(frame)
                + ApplyLocal(frame, input: null, scale: 2)
                + ApplyLocal(frame, scale: 3);

              int ApplyLocal(int localFrame, PlayerInput input = null, int scale = 1)
              {
                return localFrame * scale;
              }
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is LocalFunctionStatementSyntax localFunction &&
            string.Equals(localFunction.Identifier.ValueText, "ApplyLocal", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "int ApplyLocal(int localFrame, int scale = 1)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return ApplyLocal(frame)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "ApplyLocal(frame, scale: 2)",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "ApplyLocal(frame, scale: 3)",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksIndexerParameterAndSyncsElementAccesses()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-indexer-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Buffer
          {
            public int this[int index, PlayerInput input] => index;
          }

          public sealed class Game
          {
            public int Run(Buffer buffer, int frame)
            {
              return buffer[frame, null];
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is IndexerDeclarationSyntax &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is ElementAccessExpressionSyntax elementAccess &&
            elementAccess.ToString().Contains("buffer[", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "public int this[int index] => index;",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return buffer[frame];",
          rewrittenSource,
          result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksNamedArgumentIndexerParameter()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-indexer-named-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Buffer
          {
            public int this[int index, PlayerInput input] => index;
          }

          public sealed class Game
          {
            public int Run(Buffer buffer, int frame)
            {
              return buffer[index: frame, input: null];
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is IndexerDeclarationSyntax &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is ElementAccessExpressionSyntax elementAccess &&
            elementAccess.ToString().Contains("buffer[", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains(
          "public int this[int index] => index;",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.Contains(
          "return buffer[index: frame];",
          rewrittenSource,
          result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksDelegateMethodGroupBindingsAndInvocations()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-delegate-method-group-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public delegate int Handler(PlayerInput input, int frame);

          public sealed class Game
          {
            private int Apply(PlayerInput input, int frame)
            {
              return frame;
            }

            public int Run(int frame)
            {
              Handler handler = Apply;
              return handler(null, frame) + handler.Invoke(null, frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Handler", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Apply", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains("public delegate int Handler(int frame);", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("private int Apply(int frame)", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("Handler handler = Apply;", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("return handler(frame) + handler.Invoke(frame);", rewrittenSource, result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksDelegateLambdaBindingsAndInvocations()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-delegate-lambda-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public delegate int Handler(PlayerInput input, int frame);

          public sealed class Game
          {
            public int Run(int frame)
            {
              Handler handler = (input, currentFrame) => currentFrame;
              return handler(null, frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Handler", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is ParenthesizedLambdaExpressionSyntax &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains("public delegate int Handler(int frame);", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("Handler handler = (currentFrame) => currentFrame;", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("return handler(frame);", rewrittenSource, result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksDelegateInvocationChainWithoutBindingRewrite()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-delegate-invocation-chain-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public delegate int Handler(PlayerInput input, int frame);

          public sealed class Game
          {
            public int Run(Handler handler, int frame)
            {
              var alias = handler;
              return handler(null, frame) + alias.Invoke(null, frame);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Handler", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains("public delegate int Handler(int frame);", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("return handler(frame) + alias.Invoke(frame);", rewrittenSource, result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksExtensionMethodNonReceiverParameter()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-extension-nonreceiver-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public static class InputExtensions
          {
            public static int Score(this int value, PlayerInput input, int frame)
            {
              return value + frame;
            }
          }

          public sealed class Game
          {
            public int Run(int frame)
            {
              return frame.Score(null, 1) + InputExtensions.Score(frame, null, 2);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Score", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        TextDiffAssert.Contains("public static int Score(this int value, int frame)", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("return frame.Score(1) + InputExtensions.Score(frame, 2);", rewrittenSource, result.Diff);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ShrinksUnusedDelegateParameter()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-delegate-parameter-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public delegate void Apply(PlayerInput input, int frame);

          public delegate int Keep(int frame);
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Apply", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Replace);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax delegateDeclaration &&
            string.Equals(delegateDeclaration.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.Contains(
          "public delegate void Apply(int frame);",
          rewrittenSource,
          result.Diff);
        Assert.Contains("public delegate int Keep(int frame);", rewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesInterfaceMethodsWithTargetTypeSignature()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-interface-method-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Contract.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public interface IGameContract
          {
            PlayerInput Create();

            void Apply(PlayerInput input);

            int Keep();
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Create", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Apply", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain(
          "PlayerInput Create();",
          rewrittenSource,
          result.Diff);
        TextDiffAssert.DoesNotContain(
          "void Apply(PlayerInput input);",
          rewrittenSource,
          result.Diff);
        Assert.Contains("int Keep();", rewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesInterfacePropertiesWithTargetTypeSignature()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-interface-property-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Contract.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public interface IGameContract
          {
            PlayerInput Current { get; }

            int Keep { get; }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is PropertyDeclarationSyntax property &&
            string.Equals(property.Identifier.ValueText, "Current", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is PropertyDeclarationSyntax property &&
            string.Equals(property.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain(
          "PlayerInput Current { get; }",
          rewrittenSource,
          result.Diff);
        Assert.Contains("int Keep { get; }", rewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesInterfaceEventsWithTargetTypeSignature()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-interface-event-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Contract.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public delegate void PlayerInputHandler(PlayerInput input);

          public class PlayerInput
          {
          }

          public interface IGameContract
          {
            event PlayerInputHandler Changed;

            event System.Action KeepAlive;
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput,PlayerInputHandler",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains(
          result.Decisions,
          decision => (decision.FinalNode is EventFieldDeclarationSyntax ||
                       decision.FinalNode is EventDeclarationSyntax) &&
            decision.Action == DecisionActionKind.Delete &&
            decision.Reason.Contains("Interface event signature", StringComparison.Ordinal));
        Assert.DoesNotContain(
          result.Decisions,
          decision => (decision.FinalNode is EventFieldDeclarationSyntax eventField &&
                       eventField.Declaration.Variables.Any(variable =>
                         string.Equals(variable.Identifier.ValueText, "KeepAlive", StringComparison.Ordinal))) ||
                      (decision.FinalNode is EventDeclarationSyntax eventDeclaration &&
                       string.Equals(eventDeclaration.Identifier.ValueText, "KeepAlive", StringComparison.Ordinal)));
        TextDiffAssert.DoesNotContain(
          "event PlayerInputHandler Changed;",
          rewrittenSource,
          result.Diff);
        Assert.Contains("event System.Action KeepAlive;", rewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesInterfaceIndexersWithTargetTypeSignature()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-interface-indexer-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Contract.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public interface IGameContract
          {
            PlayerInput this[int index] { get; }

            int this[PlayerInput input] { get; }

            int this[string key] { get; }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });
        var rewrittenSource = File.ReadAllText(sourceFilePath);

        Assert.Contains("int this[string key] { get; }", rewrittenSource, StringComparison.Ordinal);
        TextDiffAssert.DoesNotContain("PlayerInput this[int index] { get; }", rewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("int this[PlayerInput input] { get; }", rewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesExtensionMethodsWithTargetReceiverType()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-extension-method-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "InputExtensions.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public sealed class PlayerInput
          {
          }

          public static class InputExtensions
          {
            public static int Score(this PlayerInput input)
            {
              return 1;
            }

            public static int Keep(this int value)
            {
              return value;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--no-diff"
        });

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Score", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        Assert.DoesNotContain(
          result.Decisions,
          decision => decision.FinalNode is MethodDeclarationSyntax method &&
            string.Equals(method.Identifier.ValueText, "Keep", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        TextDiffAssert.DoesNotContain(
          "public static int Score(this PlayerInput input)",
          result.RewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public static int Keep(this int value)", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_RemovesTargetBaseTypes()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-base-type-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public interface IPlayerInputConsumer
          {
          }

          public interface IOther
          {
          }

          public sealed class Single : PlayerInput
          {
          }

          public sealed class Multi : IPlayerInputConsumer, IOther
          {
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput,IPlayerInputConsumer",
          "--write-back",
          "--no-diff"
        });

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is BaseListSyntax &&
            decision.Action == DecisionActionKind.Delete);
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is SimpleBaseTypeSyntax simpleBaseType &&
            string.Equals(simpleBaseType.Type.ToString(), "IPlayerInputConsumer", StringComparison.Ordinal) &&
            decision.Action == DecisionActionKind.Delete);
        var rewrittenSource = File.ReadAllText(sourceFilePath);
        TextDiffAssert.Contains("public sealed class Single", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("public sealed class Multi : IOther", rewrittenSource, result.Diff);
        Assert.DoesNotContain("Single : PlayerInput", rewrittenSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IPlayerInputConsumer, IOther", rewrittenSource, StringComparison.Ordinal);

        var rewrittenTree = CSharpSyntaxTree.ParseText(rewrittenSource, path: sourceFilePath);
        var errors = CreateCompilation(rewrittenTree)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesLocalDeclarationsWithTargetGenericTypeArgument()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-generic-type-argument-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          using System.Collections.Generic;

          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class Game
          {
            public int GetItems()
            {
              List<PlayerInput> items;
              return 0;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--no-diff"
        });

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is LocalDeclarationStatementSyntax &&
            decision.Action == DecisionActionKind.Delete &&
            string.Equals(
              decision.Reason,
              "Local declaration type argument references the delete-class target.",
              StringComparison.Ordinal));
        TextDiffAssert.DoesNotContain(
          "List<PlayerInput> items;",
          result.RewrittenSource,
          result.Diff);
        Assert.DoesNotContain("public int GetItems()", result.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_ReportsDiagnosticsForResidualPublicSignatures()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-diagnostic-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public sealed class GenericHost
          {
            public void Keep<T>()
              where T : PlayerInput
            {
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var diagnostics = result.Diagnostics ?? throw new InvalidOperationException("Expected diagnostics.");
        Assert.NotEmpty(diagnostics);
        Assert.Contains(
          diagnostics,
          diagnostic => string.Equals(diagnostic.Severity, "Error", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("PlayerInput", StringComparison.Ordinal));

    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_DeletesDelegatesWithTargetTypeSignature()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-delegate-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo;

          public class PlayerInput
          {
          }

          public delegate void Apply(PlayerInput input);

          internal delegate PlayerInput Build();

          public delegate void Keep(int frame);
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax &&
            decision.Action == DecisionActionKind.Replace &&
            decision.Reason.Contains(
              "Delegate parameter type references the delete-class target",
              StringComparison.Ordinal));
        Assert.Contains(
          result.Decisions,
          decision => decision.FinalNode is DelegateDeclarationSyntax &&
            decision.Action == DecisionActionKind.Delete &&
            decision.Reason.Contains(
              "Delegate return type references the delete-class target",
              StringComparison.Ordinal));

        var rewrittenSource = File.ReadAllText(sourceFilePath);
        TextDiffAssert.DoesNotContain("public delegate void Apply(PlayerInput input);", rewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("internal delegate PlayerInput Build();", rewrittenSource, result.Diff);
        TextDiffAssert.Contains("public delegate void Keep(int frame);", rewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_RemovesUnusedUsingsAfterRewrite()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-using-cleanup-project");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var consumerFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo.Input;

          public static class PlayerInput
          {
            public static bool Enabled => true;
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          using Demo.Input;
          using System;

          namespace Demo;

          public sealed class Game
          {
            public void Run()
            {
              Console.WriteLine(PlayerInput.Enabled);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        Assert.DoesNotContain("using Demo.Input;", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.Enabled", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using System;", rewrittenConsumerSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(File.ReadAllText(classFilePath), path: classFilePath),
            CSharpSyntaxTree.ParseText(rewrittenConsumerSource, path: consumerFilePath)
        };
        var errors = CreateCompilation(rewrittenTrees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_KeepsExtensionMethodUsings()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-using-keep-extension-project");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var extensionFilePath = Path.Combine(projectDirectory, "NumberExtensions.cs");
        var consumerFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo.Input;

          public static class PlayerInput
          {
            public static bool Enabled => true;
          }
          """);
        File.WriteAllText(
          extensionFilePath,
          """
          namespace Demo.Extensions;

          public static class NumberExtensions
          {
            public static int Twice(this int value)
            {
              return value * 2;
            }
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          using Demo.Extensions;
          using Demo.Input;

          namespace Demo;

          public sealed class Game
          {
            public int Run()
            {
              if (PlayerInput.Enabled)
              {
                return 0;
              }

              return 1.Twice();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        TextDiffAssert.Contains("using Demo.Extensions;", rewrittenConsumerSource, result.Diff);
        Assert.DoesNotContain("using Demo.Input;", rewrittenConsumerSource, StringComparison.Ordinal);
        TextDiffAssert.Contains("1.Twice()", rewrittenConsumerSource, result.Diff);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(File.ReadAllText(classFilePath), path: classFilePath),
            CSharpSyntaxTree.ParseText(File.ReadAllText(extensionFilePath), path: extensionFilePath),
            CSharpSyntaxTree.ParseText(rewrittenConsumerSource, path: consumerFilePath)
        };
        var errors = CreateCompilation(rewrittenTrees)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_RemovesEmptyNamespaceBlocks()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-empty-namespace-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo
          {
            public sealed class PlayerInput
            {
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenSource = File.ReadAllText(sourceFilePath);
        Assert.DoesNotContain("namespace Demo", rewrittenSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class PlayerInput", rewrittenSource, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(rewrittenSource));
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_KeepsNamespaceWhenEmptyPublicClassRemains()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-keep-empty-public-class-project");
        Directory.CreateDirectory(projectDirectory);
        var sourceFilePath = Path.Combine(projectDirectory, "Types.cs");
        File.WriteAllText(
          sourceFilePath,
          """
          namespace Demo
          {
            public sealed class PlayerInput
            {
            }

            public sealed class Placeholder
            {
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenSource = File.ReadAllText(sourceFilePath);
        TextDiffAssert.Contains("namespace Demo", rewrittenSource, result.Diff);
        Assert.DoesNotContain("class PlayerInput", rewrittenSource, StringComparison.Ordinal);
        TextDiffAssert.Contains("public sealed class Placeholder", rewrittenSource, result.Diff);

        var rewrittenTree = CSharpSyntaxTree.ParseText(rewrittenSource, path: sourceFilePath);
        var errors = CreateCompilation(rewrittenTree)
          .GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
        Assert.Empty(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_FastDirectoryMode_SkipsUsingAndNamespaceCleanup()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-fast-directory-project");
        Directory.CreateDirectory(projectDirectory);
        var classFilePath = Path.Combine(projectDirectory, "PlayerInput.cs");
        var consumerFilePath = Path.Combine(projectDirectory, "Game.cs");
        File.WriteAllText(
          classFilePath,
          """
          namespace Demo.Input;

          public static class PlayerInput
          {
            public static bool Enabled => true;
          }
          """);
        File.WriteAllText(
          consumerFilePath,
          """
          using Demo.Input;
          using System;

          namespace Demo;

          public sealed class Game
          {
            public void Run()
            {
              Console.WriteLine(PlayerInput.Enabled);
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--fast-delete-class-directory",
          "--write-back",
          "--no-diff"
        });

        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        TextDiffAssert.Contains("using Demo.Input;", rewrittenConsumerSource, result.Diff);
        TextDiffAssert.Contains("using System;", rewrittenConsumerSource, result.Diff);
        TextDiffAssert.Contains("namespace Demo;", rewrittenConsumerSource, result.Diff);
        Assert.DoesNotContain("PlayerInput.Enabled", rewrittenConsumerSource, StringComparison.Ordinal);

        var rewrittenClassSource = File.ReadAllText(classFilePath);
        TextDiffAssert.Contains("namespace Demo.Input;", rewrittenClassSource, result.Diff);
        Assert.DoesNotContain("class PlayerInput", rewrittenClassSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_FastDirectoryMode_TargetNameFilter_ReducesAnalyzedFiles()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-fast-directory-target-filter-project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "PlayerInput.cs"),
          """
          namespace Demo.Input;

          public sealed class PlayerInput
          {
          }
          """);
        File.WriteAllText(
          Path.Combine(projectDirectory, "Consumer.cs"),
          """
          namespace Demo;

          using Demo.Input;

          public sealed class Consumer
          {
            public PlayerInput Current { get; } = new PlayerInput();
          }
          """);
        File.WriteAllText(
          Path.Combine(projectDirectory, "Unrelated.cs"),
          """
          namespace Demo;

          public sealed class Unrelated
          {
            public int Count() => 42;
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--fast-delete-class-directory",
          "--filter-delete-class-files-by-target-name",
          "--no-diff"
        });

        var stats = Assert.IsType<AnalysisStats>(result.Stats);
        Assert.Equal(3, stats.ScannedFileCount);
        Assert.Equal(2, stats.AnalyzedFileCount);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteClass_FastDirectoryMode_WithoutTargetNameFilter_KeepsAllFilesAnalyzed()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-fast-directory-target-filter-disabled-project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "PlayerInput.cs"),
          """
          namespace Demo.Input;

          public sealed class PlayerInput
          {
          }
          """);
        File.WriteAllText(
          Path.Combine(projectDirectory, "Consumer.cs"),
          """
          namespace Demo;

          using Demo.Input;

          public sealed class Consumer
          {
            public PlayerInput Current { get; } = new PlayerInput();
          }
          """);
        File.WriteAllText(
          Path.Combine(projectDirectory, "Unrelated.cs"),
          """
          namespace Demo;

          public sealed class Unrelated
          {
            public int Count() => 42;
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--fast-delete-class-directory",
          "--no-diff"
        });

        var stats = Assert.IsType<AnalysisStats>(result.Stats);
        Assert.Equal(3, stats.ScannedFileCount);
        Assert.Equal(3, stats.AnalyzedFileCount);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods_RemovesOnlyDeadPrivateMethods()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-unreferenced-method-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        var callerFilePath = Path.Combine(projectDirectory, "Caller.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Worker
          {
            public int Run()
            {
              return Used() + 1;
            }

            private int Used()
            {
              return Helper();
            }

            private int Helper()
            {
              return 1;
            }

            private int Unused()
            {
              return 2;
            }

            private int DeadCaller()
            {
              return DeadCallee();
            }

            private int DeadCallee()
            {
              return 3;
            }

            private int SelfRecursive()
            {
              return SelfRecursive();
            }

            private int DeadCycleA()
            {
              return DeadCycleB();
            }

            private int DeadCycleB()
            {
              return DeadCycleA();
            }
          }
          """);
        File.WriteAllText(
          callerFilePath,
          """
          namespace Demo;

          public sealed class Caller
          {
            public int Run()
            {
              return new Worker().Run();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-unreferenced-methods",
          "--write-back"
        });

        Assert.NotEmpty(result.SeedMarks);
        Assert.NotEmpty(result.Edits);
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("private int Used()", rewrittenServiceSource, result.Diff);
        TextDiffAssert.Contains("private int Helper()", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("private int Unused()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int DeadCaller()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int DeadCallee()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int SelfRecursive()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int DeadCycleA()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private int DeadCycleB()", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.NotNull(result.Stats);
        Assert.Equal(2, result.Stats!.ScannedFileCount);
        Assert.Equal(8, result.Stats.CandidateMethodCount);
        Assert.Equal(6, result.Stats.DeletedMethodCount);
        Assert.True(result.Stats.ElapsedMilliseconds >= 0);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenServiceSource, path: serviceFilePath),
            CSharpSyntaxTree.ParseText(File.ReadAllText(callerFilePath), path: callerFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods_KeepsPublicMethods()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-unreferenced-public-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Worker
          {
            public int PublicApi()
            {
              return 1;
            }

            private int PrivateDead()
            {
              return 2;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-unreferenced-methods",
          "--write-back"
        });

        Assert.NotEmpty(result.Edits);
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("public int PublicApi()", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("private int PrivateDead()", rewrittenServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods_KeepsPrivateMethodsUsedByLocalFunctionAndDelegate()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-unreferenced-local-function-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          using System;

          namespace Demo;

          public sealed class Worker
          {
            public int Run()
            {
              Func<int> thunk = DelegateTarget;
              return thunk() + UseLocal();

              int UseLocal()
              {
                return LocalTarget();
              }
            }

            private int DelegateTarget()
            {
              return 1;
            }

            private int LocalTarget()
            {
              return 2;
            }

            private int DeadPrivate()
            {
              return 3;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-unreferenced-methods",
          "--write-back"
        });

        Assert.NotEmpty(result.Edits);
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("private int DelegateTarget()", rewrittenServiceSource, result.Diff);
        TextDiffAssert.Contains("private int LocalTarget()", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("private int DeadPrivate()", rewrittenServiceSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenServiceSource, path: serviceFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeleteUnreferencedMethods_KeepsPrivateMethodsReferencedAcrossPartialFilesAndConstructor()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-unreferenced-partial-project");
        Directory.CreateDirectory(projectDirectory);
        var firstPartialPath = Path.Combine(projectDirectory, "Worker.Part1.cs");
        var secondPartialPath = Path.Combine(projectDirectory, "Worker.Part2.cs");
        File.WriteAllText(
          firstPartialPath,
          """
          namespace Demo;

          public sealed partial class Worker
          {
            public Worker()
            {
              Initialize();
            }

            public int Run()
            {
              return UseShared();
            }
          }
          """);
        File.WriteAllText(
          secondPartialPath,
          """
          namespace Demo;

          public sealed partial class Worker
          {
            private int UseShared()
            {
              return SharedHelper();
            }

            private int SharedHelper()
            {
              return 1;
            }

            private void Initialize()
            {
              SharedHelper();
            }

            private int DeadPartial()
            {
              return 2;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-unreferenced-methods",
          "--write-back"
        });

        Assert.NotEmpty(result.Edits);
        var rewrittenSecondPartialSource = File.ReadAllText(secondPartialPath);
        TextDiffAssert.Contains("private int UseShared()", rewrittenSecondPartialSource, result.Diff);
        TextDiffAssert.Contains("private int SharedHelper()", rewrittenSecondPartialSource, result.Diff);
        TextDiffAssert.Contains("private void Initialize()", rewrittenSecondPartialSource, result.Diff);
        Assert.DoesNotContain("private int DeadPartial()", rewrittenSecondPartialSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(File.ReadAllText(firstPartialPath), path: firstPartialPath),
            CSharpSyntaxTree.ParseText(rewrittenSecondPartialSource, path: secondPartialPath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryClearUnusedInterfaceImplementations_ReplacesBodiesWithCompileSafeStubs()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "clear-unused-interface-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Payload
          {
          }

          public interface IWorker
          {
            Payload Create(out Payload value);

            void Ping();
          }

          public sealed class Worker : IWorker
          {
            public Payload Create(out Payload value)
            {
              value = new Payload();
              return value;
            }

            public void Ping()
            {
              var payload = new Payload();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--clear-unused-interface-implementations",
          "--write-back"
        });

        Assert.NotEmpty(result.SeedMarks);
        Assert.NotEmpty(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace);
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("value = new global::Demo.Payload();", rewrittenServiceSource, result.Diff);
        TextDiffAssert.Contains("return new global::Demo.Payload();", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("return value;", rewrittenServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var payload = new Payload();", rewrittenServiceSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenServiceSource, path: serviceFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryClearUnusedInterfaceImplementations_KeepsCalledInterfaceMembers()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "clear-called-interface-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        var callerFilePath = Path.Combine(projectDirectory, "Caller.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public interface IWorker
          {
            void Ping();
          }

          public sealed class Worker : IWorker
          {
            public void Ping()
            {
              System.Console.WriteLine(1);
            }
          }
          """);
        File.WriteAllText(
          callerFilePath,
          """
          namespace Demo;

          public sealed class Caller
          {
            public void Run(IWorker worker)
            {
              worker.Ping();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--clear-unused-interface-implementations",
          "--write-back"
        });

        Assert.Empty(result.Edits);
        TextDiffAssert.Contains("System.Console.WriteLine(1);", File.ReadAllText(serviceFilePath), result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryPrivatizeInternalOnlyPublicMethods_RewritesInternalPublicMethodToPrivate()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "privatize-internal-public-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Worker
          {
            public int Run()
            {
              return Helper();
            }

            public int Helper()
            {
              return 1;
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--privatize-internal-only-public-methods",
          "--write-back"
        });

        Assert.NotEmpty(result.SeedMarks);
        Assert.NotEmpty(result.Edits);
        var rewrittenServiceSource = File.ReadAllText(serviceFilePath);
        TextDiffAssert.Contains("public int Run()", rewrittenServiceSource, result.Diff);
        TextDiffAssert.Contains("private int Helper()", rewrittenServiceSource, result.Diff);
        Assert.DoesNotContain("public int Helper()", rewrittenServiceSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenServiceSource, path: serviceFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryPrivatizeInternalOnlyPublicMethods_KeepsExternallyCalledPublicMethods()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "privatize-external-public-project");
        Directory.CreateDirectory(projectDirectory);
        var serviceFilePath = Path.Combine(projectDirectory, "Worker.cs");
        var callerFilePath = Path.Combine(projectDirectory, "Caller.cs");
        File.WriteAllText(
          serviceFilePath,
          """
          namespace Demo;

          public sealed class Worker
          {
            public int Run()
            {
              return Helper();
            }

            public int Helper()
            {
              return 1;
            }
          }
          """);
        File.WriteAllText(
          callerFilePath,
          """
          namespace Demo;

          public sealed class Caller
          {
            public int Run(Worker worker)
            {
              return worker.Helper();
            }
          }
          """);
        var application = new DeletionApplicationService(RuleRegistry.CreateDefaultRules());

        var result = application.AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--privatize-internal-only-public-methods",
          "--write-back"
        });

        Assert.Empty(result.Edits);
        TextDiffAssert.Contains("public int Helper()", File.ReadAllText(serviceFilePath), result.Diff);
    }

    [Fact]
    public void PrototypeRewriter_Rewrite_WhenNoDecisionsKeepsSourceAndEmptyDiff()
    {
        var source = RewriteSources.NoDecisionSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "no-decisions.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var rewriter = new PrototypeRewriter();

        var result = rewriter.Rewrite(root, semanticModel, Array.Empty<RuleDecision>());

        Assert.Empty(result.Edits);
        TextDiffAssert.Contains("public sealed class Sample", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return value + 1;", result.RewrittenSource, result.Diff);
        Assert.Empty(result.Diff);
    }

    [Fact]
    public void RuleRegistry_CreateDefaultRules_ReturnsStableRuleSet()
    {
        var rules = RuleRegistry.CreateDefaultRules();
        var contractAssembly = typeof(RuleDefinitionMark).Assembly;
        var implementationAssembly = typeof(DeleteSObjectIdentifierNameMarkRule).Assembly;
        var markRuleType = contractAssembly.GetType("Rules.RuleDefinitionMark");
        var propagateRuleType = contractAssembly.GetType("Rules.RuleDefinitionPropagate");
        var liftRuleType = contractAssembly.GetType("Rules.RuleDefinitionLift");
        var proposeRuleType = contractAssembly.GetType("Rules.RuleDefinitionPropose");

        Assert.NotNull(markRuleType);
        Assert.NotNull(propagateRuleType);
        Assert.NotNull(liftRuleType);
        Assert.NotNull(proposeRuleType);
        Assert.True(markRuleType!.IsClass);
        Assert.True(propagateRuleType!.IsClass);
        Assert.True(liftRuleType!.IsClass);
        Assert.True(proposeRuleType!.IsClass);
        Assert.NotSame(typeof(RuleRegistry).Assembly, implementationAssembly);
        Assert.NotSame(contractAssembly, implementationAssembly);

        Assert.True(rules.Markers.Count >= 10);
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteSObjectIdentifierNameMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteSObjectMemberAccessMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteSObjectInvocationMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteUnreachableMethodRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteUnreferencedMethodRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "ClearUnusedInterfaceImplementationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "PrivatizeInternalOnlyPublicMethodRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "DeleteClassTypeSyntaxMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteClassObjectCreationDeclarationPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteClassSymbolReferencePropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectAssignmentLeftValuePropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectDefinitionInitializerPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectLogicalConditionPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectLogicalOperandGroupPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectSymbolReferencePropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteSObjectIfStructureCompletionPropagationRule", StringComparison.Ordinal));
        Assert.True(rules.Lifters.Count >= 3);
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "DeleteSObjectExpressionHostLiftingRule", StringComparison.Ordinal));
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "DeleteSObjectIfStructureLiftingRule", StringComparison.Ordinal));
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "DeleteSObjectSwitchStructureLiftingRule", StringComparison.Ordinal));
        Assert.True(rules.Proposers.Count >= 5);
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "LogicalExpressionProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "IfStructureProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ControlStructureDeleteProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DefaultDeleteProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteUnreachableMethodProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteUnreferencedMethodProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ClearUnusedInterfaceImplementationProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "PrivatizeInternalOnlyPublicMethodProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassTypeSyntaxDeclarationProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeleteClassIfStructureCompletionPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassMethodReturnTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassPublicMethodReturnTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassParameterProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassPrivateMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassNamedArgumentMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassOptionalParameterDefaultedMethodShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassParamsMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassPublicMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassLocalFunctionParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassNamedArgumentLocalFunctionParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassOptionalParameterDefaultedLocalFunctionShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassIndexerParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassNamedArgumentIndexerParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassDelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassMethodGroupDelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassLambdaDelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassDelegateInvocationChainParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassExtensionReceiverNonFirstParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassPublicParameterProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassInterfaceMethodProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassInterfacePropertyProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassInterfaceEventProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassInterfaceIndexerProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassDelegateProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassExtensionReceiverProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassBaseTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DeleteClassGenericTypeArgumentProposalRule", StringComparison.Ordinal));

        Assert.Contains(rules.Markers, rule => markRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Propagators, rule => propagateRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Lifters, rule => liftRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Proposers, rule => proposeRuleType.IsAssignableFrom(rule.GetType()));
    }

    [Fact]
    public void RuleRegistry_Assembly_DoesNotExposeLegacyDeleteSObjectPropagationHelpers()
    {
        var assembly = typeof(RuleRegistry).Assembly;

        Assert.Null(assembly.GetType("Rules.DeleteSObjectPropagationState"));
        Assert.Null(assembly.GetType("Rules.LogicalConditionPropagationStep"));
        Assert.Null(assembly.GetType("Rules.SymbolReferencePropagationStep"));
    }

    [Fact]
    public void RuleRegistry_CreateDefaultRules_WhenDisabledRuleTypeProvided_FiltersMatchingClassOnly()
    {
        var rules = RuleRegistry.CreateDefaultRules(new[] { "DeleteSObjectMemberAccessMarkRule" });

        Assert.DoesNotContain(
          rules.Markers,
          rule => string.Equals(rule.GetType().Name, "DeleteSObjectMemberAccessMarkRule", StringComparison.Ordinal));
        Assert.Contains(
          rules.Markers,
          rule => string.Equals(rule.GetType().Name, "DeleteUnreachableMethodRule", StringComparison.Ordinal));
        Assert.Contains(
          rules.Propagators,
          rule => string.Equals(rule.GetType().Name, "DeleteSObjectAssignmentLeftValuePropagationRule", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeFromArgs_WhenDisabledRuleTypeProvided_DisablesOnlyMatchingClass()
    {
        var host = new DeletionCommandHost(
          RuleRegistry.CreateDefaultRules(new[] { "DeleteSObjectMemberAccessMarkRule" }));

        var result = host.AnalyzeFromArgs(new[]
        {
          "--target-name",
          "s"
        });

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.PropagatedMarks);
        Assert.Empty(result.LiftedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void RuleRegistry_CreateDefaultRules_DeleteSObjectMarkRulesShareGroupKeyAndUniqueRuleIds()
    {
        var rules = RuleRegistry.CreateDefaultRules();
        var deleteSObjectMarkRules = GetDeleteSObjectMarkRules(rules);

        Assert.True(deleteSObjectMarkRules.Count >= 10);
        Assert.All(
          deleteSObjectMarkRules,
          rule => Assert.Equal(DeleteSObjectGroupKey, rule.GroupKey));
        Assert.Equal(
          deleteSObjectMarkRules.Count,
          deleteSObjectMarkRules
            .Select(rule => rule.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static (RuleContext Context, SyntaxNode Root) CreateContext(
      string source,
      string? targetName = null,
      DeletionAnalysisRuntime? runtime = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "component-test.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new MinimalRoslynCpg.Builder.RoslynCpgBuilder().BuildFromSource(source, "component-test.cs");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        return (new RuleContext(new CpgAnalysisContext(graph, semanticModel, root), options, runtime: runtime), root);
    }

    private static DeletionAnalysisRuntime CreateParallelRuntime(RecordingScheduler scheduler)
    {
        return new DeletionAnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            4,
            EnableGroupParallelism: true),
          new DeletionAnalysisEpoch(0, 0, 0),
          scheduler);
    }

    private static TCache GetCompilationCache<TCache>(
      DeletionAnalysisRuntime runtime,
      Compilation compilation,
      Func<Compilation, TCache> factory)
      where TCache : class
    {
        var method = typeof(DeletionAnalysisRuntime)
          .GetMethod("GetOrCreateCompilationCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var genericMethod = method!.MakeGenericMethod(typeof(TCache));
        var cache = genericMethod.Invoke(runtime, new object[] { compilation, factory });
        return Assert.IsType<TCache>(cache);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CreateCompilation(new[] { tree });
    }

    private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        return CSharpCompilation.Create(
          "PipelineComponentTests",
          trees,
          new[]
          {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
          },
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string CreateDeepElseIfChainSource(int elseIfDepth)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace Demo;");
        builder.AppendLine();
        builder.AppendLine("internal static class DeepElseIfRewrite");
        builder.AppendLine("{");
        builder.AppendLine("    public static int Run(bool[] flags)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (flags.Length > 0 && flags[0])");
        builder.AppendLine("        {");
        builder.AppendLine("            return 0;");
        builder.AppendLine("        }");

        for (var index = 1; index <= elseIfDepth; index += 1)
        {
            builder.AppendLine($"        else if (flags.Length > {index} && flags[{index}])");
            builder.AppendLine("        {");
            builder.AppendLine($"            return {index};");
            builder.AppendLine("        }");
        }

        builder.AppendLine("        else");
        builder.AppendLine("        {");
        builder.AppendLine("            return -2;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static IReadOnlyList<RuleDefinitionMark> GetDeleteSObjectMarkRules(DeletionRulePipeline? rules = null)
    {
        var markerRules = rules?.Markers ?? RuleRegistry.CreateDefaultRules().Markers;
        return markerRules
          .Where(rule => string.Equals(rule.GroupKey, DeleteSObjectGroupKey, StringComparison.Ordinal))
          .ToList();
    }

    private static IReadOnlyList<RuleDefinitionMark> GetDeleteClassMarkRules(DeletionRulePipeline? rules = null)
    {
        var markerRules = rules?.Markers ?? RuleRegistry.CreateDefaultRules().Markers;
        return markerRules
          .Where(rule => string.Equals(rule.GroupKey, DeleteClassRuleIds.GroupKey, StringComparison.Ordinal))
          .ToList();
    }

    private sealed class DuplicateSeedRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-DUP-SEED";

        public override string Name { get; } = "Emit duplicated seed marks";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single();
            yield return new MarkRecord(RuleId, memberAccess, null, null, "first");
            yield return new MarkRecord(RuleId, memberAccess, null, null, "second");
        }
    }

    private sealed class EmptyMarkRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-EMPTY-MARK";

        public override string Name { get; } = "Emit no seed marks";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            _ = root;
            yield break;
        }
    }

    private sealed class SyntaxSemanticOnlyMarkRule : RuleDefinitionMark
    {
        public override string RuleId => "syntax-semantic-only";

        public override string Name => "Syntax semantic only";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => Array.Empty<SyntaxKind>();

        public override IReadOnlyCollection<RoslynCpgCapability> RequiredCapabilities =>
          new[] { RoslynCpgCapability.SyntaxSemantic };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            return Array.Empty<MarkRecord>();
        }
    }

    private sealed record TestCompilationCacheA(string Value);

    private sealed record TestCompilationCacheB((int TreeCount, int StableId) Value);

    private sealed class RecordingScheduler : IRuleStageScheduler
    {
        public int InvocationCount { get; private set; }

        public List<int> ItemCounts { get; } = new();

        public void Reset()
        {
            InvocationCount = 0;
            ItemCounts.Clear();
        }

        public async Task<IReadOnlyList<TResult>> RunOrderedAsync<TResult>(
          int itemCount,
          int maxDegreeOfParallelism,
          Func<int, CancellationToken, Task<TResult>> workItem,
          CancellationToken cancellationToken)
        {
            _ = maxDegreeOfParallelism;
            InvocationCount++;
            ItemCounts.Add(itemCount);

            var results = new TResult[itemCount];
            for (var index = 0; index < itemCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = await workItem(index, cancellationToken);
            }

            return results;
        }
    }

    private sealed class RuntimeAwareMarkRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-RUNTIME-MARK";

        public override string Name { get; } = "Emit a mark only when runtime options flow into the direct Analyze overload.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            var executionOptions = context.Runtime.ExecutionOptions;
            if (executionOptions.EffectiveMaxDegreeOfParallelism != 3 ||
                executionOptions.EnableHelperParallelism ||
                !executionOptions.EnableGroupParallelism)
            {
                yield break;
            }

            var classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            yield return new MarkRecord(
              RuleId,
              classDeclaration,
              null,
              null,
              $"mdop={executionOptions.EffectiveMaxDegreeOfParallelism};group={executionOptions.EnableGroupParallelism};helper={executionOptions.EnableHelperParallelism}");
        }
    }

    private sealed class ParallelClassMarkRule : RuleDefinitionMark
    {
        private readonly string _className;
        private readonly string _groupKey;

        public ParallelClassMarkRule(string ruleId, string groupKey, string className)
        {
            RuleId = ruleId;
            _groupKey = groupKey;
            _className = className;
        }

        public override string RuleId { get; }

        public override string GroupKey => _groupKey;

        public override string Name { get; } = "Mark a named class for parallel scheduler tests.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            var classDeclaration = root.DescendantNodes()
              .OfType<ClassDeclarationSyntax>()
              .Single(candidate => string.Equals(candidate.Identifier.ValueText, _className, StringComparison.Ordinal));
            yield return new MarkRecord(RuleId, classDeclaration, null, null, $"Seed {_className}");
        }
    }

    private sealed class ConcurrentClassMarkRule : RuleDefinitionMark
    {
        private readonly string _className;
        private readonly string _groupKey;
        private readonly ConcurrentRuleProbe _probe;

        public ConcurrentClassMarkRule(
          string ruleId,
          string groupKey,
          string className,
          ConcurrentRuleProbe probe)
        {
            RuleId = ruleId;
            _groupKey = groupKey;
            _className = className;
            _probe = probe;
        }

        public override string RuleId { get; }

        public override string GroupKey => _groupKey;

        public override string Name { get; } = "Mark a named class while measuring rule concurrency.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
        {
            _ = context;
            _probe.Enter();
            try
            {
                var classDeclaration = root.DescendantNodes()
                  .OfType<ClassDeclarationSyntax>()
                  .Single(candidate => string.Equals(candidate.Identifier.ValueText, _className, StringComparison.Ordinal));
                yield return new MarkRecord(RuleId, classDeclaration, null, null, $"Seed {_className}");
            }
            finally
            {
                _probe.Leave();
            }
        }
    }

    private sealed class ConcurrentRuleProbe
    {
        private readonly int _expectedConcurrentRules;
        private readonly ManualResetEventSlim _release = new();
        private int _activeRuleCount;
        private int _peakActiveRuleCount;

        public ConcurrentRuleProbe(int expectedConcurrentRules)
        {
            _expectedConcurrentRules = expectedConcurrentRules;
        }

        public int PeakActiveRuleCount => Volatile.Read(ref _peakActiveRuleCount);

        public void Enter()
        {
            var activeRuleCount = Interlocked.Increment(ref _activeRuleCount);
            UpdatePeakActiveRuleCount(activeRuleCount);
            if (activeRuleCount == _expectedConcurrentRules)
            {
                _release.Set();
            }

            _release.Wait(TimeSpan.FromSeconds(2));
        }

        public void Leave()
        {
            Interlocked.Decrement(ref _activeRuleCount);
        }

        private void UpdatePeakActiveRuleCount(int activeRuleCount)
        {
            var currentPeak = Volatile.Read(ref _peakActiveRuleCount);
            while (activeRuleCount > currentPeak)
            {
                var observedPeak = Interlocked.CompareExchange(
                  ref _peakActiveRuleCount,
                  activeRuleCount,
                  currentPeak);
                if (observedPeak == currentPeak)
                {
                    return;
                }

                currentPeak = observedPeak;
            }
        }
    }

    private sealed class DuplicatePropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "DEL-SOBJ-TEST-PROP-001";

        public override string GroupKey { get; } = DeleteSObjectGroupKey;

        public override string Name { get; } = "Emit duplicated propagated marks";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            var source = Assert.Single(seedMarks);
            var propagatedMark = new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, ifStatement, null, null, "lift to if"),
              source,
              1);
            yield return propagatedMark;
            yield return propagatedMark;
        }

    }

    private sealed class DefinitionLeftValuePropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-CHAIN-DECL-001";

        public override string GroupKey { get; } = DeleteSObjectGroupKey;

        public override string Name { get; } = "Propagate initializer marks to declarators";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.VariableDeclarator };

        public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            foreach (var seedMark in seedMarks)
            {
                if (seedMark.SyntaxNode is not ExpressionSyntax expression)
                {
                    continue;
                }

                var declarator = expression.Ancestors()
                  .OfType<VariableDeclaratorSyntax>()
                  .FirstOrDefault(candidate => candidate.Initializer?.Value.Span.Contains(expression.Span) == true);
                if (declarator is null)
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  new MarkRecord(
                    RuleId,
                    declarator,
                    null,
                    null,
                    "Initializer seed is propagated to its declarator."),
                  seedMark,
                  1);
            }
        }
    }

    private sealed class LocalReferenceFromDeclaratorPropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-CHAIN-REF-001";

        public override string GroupKey { get; } = DeleteSObjectGroupKey;

        public override string Name { get; } = "Propagate declarator marks to later local references";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IdentifierName };

        public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var declaratorMark = seedMarks.FirstOrDefault(mark =>
              mark.SyntaxNode is VariableDeclaratorSyntax variableDeclarator &&
              string.Equals(variableDeclarator.Identifier.ValueText, "value", StringComparison.Ordinal));
            if (declaratorMark?.SyntaxNode is not VariableDeclaratorSyntax declarator)
            {
                yield break;
            }

            var symbol = context.SemanticModel.GetDeclaredSymbol(declarator) as ILocalSymbol;
            if (symbol is null)
            {
                yield break;
            }

            foreach (var identifier in context.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText != "value" ||
                    identifier.SpanStart <= declarator.SpanStart)
                {
                    continue;
                }

                var referencedSymbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
                if (!SymbolEqualityComparer.Default.Equals(symbol, referencedSymbol))
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  new MarkRecord(
                    RuleId,
                    identifier,
                    null,
                    null,
                    "Declarator-propagated mark is propagated to a later local reference."),
                  declaratorMark,
                  1);
            }
        }
    }

    private sealed class GroupMethodPropagationRule : RuleDefinitionPropagate
    {
        private readonly string _groupKey;

        public GroupMethodPropagationRule(string ruleId, string groupKey)
        {
            RuleId = ruleId;
            _groupKey = groupKey;
        }

        public override string RuleId { get; }

        public override string GroupKey => _groupKey;

        public override string Name { get; } = "Propagate a class seed to its method for scheduler tests.";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.MethodDeclaration };

        public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            var seedMark = Assert.Single(seedMarks);
            var classDeclaration = Assert.IsType<ClassDeclarationSyntax>(seedMark.SyntaxNode);
            var method = classDeclaration.Members.OfType<MethodDeclarationSyntax>().Single();
            yield return new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, method, null, null, $"Propagate {classDeclaration.Identifier.ValueText}"),
              seedMark,
              1);
        }
    }

    private sealed class ViewAwarePropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-VIEW-PROP-001";

        public override string GroupKey { get; } = DeleteSObjectGroupKey;

        public override string Name { get; } = "Require a rule-scoped structure view during propagation";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public override IEnumerable<PropagatedMarkRecord> Propagate(RuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var structureView = context.StructureView;
            Assert.NotNull(structureView);
            var viewNodeIds = structureView!.Nodes.Select(node => node.NodeId).ToHashSet();
            Assert.NotEmpty(seedMarks);
            Assert.All(
              seedMarks,
              mark => Assert.True(
                mark.PrimaryGraphNode?.NodeId is not null && viewNodeIds.Contains(mark.PrimaryGraphNode.NodeId),
                $"Rule view does not include primary graph node for seed span {mark.SyntaxNode.Span}."));

            var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            yield return new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, ifStatement, null, null, "rule-scoped view is available during propagation"),
              seedMarks[0],
              1);
        }
    }

    private sealed class ViewAwareLiftRule : RuleDefinitionLift
    {
        public override string RuleId { get; } = "TEST-VIEW-LIFT-001";

        public override string GroupKey { get; } = DeleteSObjectGroupKey;

        public override string Name { get; } = "Require a rule-scoped structure view during lifting";

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
            new[] { SyntaxKind.ReturnStatement };

        public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            var structureView = context.StructureView;
            Assert.NotNull(structureView);
            var viewNodeIds = structureView!.Nodes.Select(node => node.NodeId).ToHashSet();
            Assert.NotEmpty(seedMarks);
            Assert.NotEmpty(propagatedMarks);
            Assert.All(
              seedMarks,
              mark => Assert.True(
                mark.PrimaryGraphNode?.NodeId is not null && viewNodeIds.Contains(mark.PrimaryGraphNode.NodeId),
                $"Rule view does not include primary graph node for seed span {mark.SyntaxNode.Span}."));
            Assert.All(
              propagatedMarks,
              mark => Assert.True(
                mark.Mark.PrimaryGraphNode?.NodeId is not null && viewNodeIds.Contains(mark.Mark.PrimaryGraphNode.NodeId),
                $"Rule view does not include primary graph node for propagated span {mark.Mark.SyntaxNode.Span}."));

            var returnStatement = context.Root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            yield return new LiftedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, returnStatement, null, null, "rule-scoped view is available during lifting"),
              propagatedMarks[0].Mark,
              propagatedMarks[0].Depth + 1);
        }
    }

    private sealed class NamespaceLiftRule : RuleDefinitionLift
    {
        private readonly string _groupKey;

        public NamespaceLiftRule(string ruleId, string groupKey)
        {
            RuleId = ruleId;
            _groupKey = groupKey;
        }

        public override string RuleId { get; }

        public override string GroupKey => _groupKey;

        public override string Name { get; } = "Lift a class seed to its namespace for scheduler tests.";

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
            new[] { SyntaxKind.FileScopedNamespaceDeclaration };

        public override IEnumerable<LiftedMarkRecord> Lift(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = propagatedMarks;
            var seedMark = Assert.Single(seedMarks);
            var namespaceDeclaration = seedMark.SyntaxNode.Ancestors()
              .OfType<FileScopedNamespaceDeclarationSyntax>()
              .Single();
            yield return new LiftedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, namespaceDeclaration, null, null, "Lift to namespace"),
              seedMark,
              1);
        }
    }

    private sealed class DeleteClassDecisionRule : RuleDefinitionPropose
    {
        private readonly string _groupKey;

        public DeleteClassDecisionRule(string ruleId, string groupKey)
        {
            RuleId = ruleId;
            _groupKey = groupKey;
        }

        public override string RuleId { get; }

        public override string GroupKey => _groupKey;

        public override string Name { get; } = "Create a delete decision for scheduler tests.";

        public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<DecisionUnit> Propose(
          RuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
          IReadOnlyList<LiftedMarkRecord> liftedMarks)
        {
            _ = context;
            _ = propagatedMarks;
            _ = liftedMarks;
            var seedMark = Assert.Single(seedMarks);
            var fragment = DecisionCpgFactory.CreateFragment(
              $"fragment:{RuleId}",
              seedMark.SyntaxNode,
              "anchor",
              DecisionActionKind.Delete);
            var unitNode = DecisionCpgFactory.CreateUnit(
              RuleId,
              DecisionActionKind.Delete,
              fragment,
              $"Delete {RuleId}");
            yield return new DecisionUnit(
              RuleId,
              DecisionActionKind.Delete,
              unitNode,
              new[] { fragment },
              new[] { DecisionCpgFactory.CreateContainment(unitNode, fragment) },
              DecisionCpgFactory.CreateSyntaxBindings((fragment, seedMark.SyntaxNode)),
              reason: $"Delete {RuleId}",
              groupKey: GroupKey);
        }
    }

    private static void AssertEquivalentAnalysisResults(PrototypeAnalysisResult expected, PrototypeAnalysisResult actual)
    {
        Assert.Equal(expected.SeedMarks.Count, actual.SeedMarks.Count);
        Assert.Equal(expected.PropagatedMarks.Count, actual.PropagatedMarks.Count);
        Assert.Equal(expected.LiftedMarks.Count, actual.LiftedMarks.Count);
        Assert.Equal(expected.Decisions.Count, actual.Decisions.Count);
        Assert.Equal(expected.Edits.Count, actual.Edits.Count);
        Assert.Equal(expected.Diff.ToString(), actual.Diff.ToString());

        var expectedDecisionKeys = expected.Decisions
          .Select(BuildDecisionKey)
          .ToList();
        var actualDecisionKeys = actual.Decisions
          .Select(BuildDecisionKey)
          .ToList();
        Assert.Equal(expectedDecisionKeys, actualDecisionKeys);

        var expectedDiagnosticKeys = (expected.Diagnostics ?? Array.Empty<AnalysisDiagnostic>())
          .Select(BuildDiagnosticKey)
          .ToList();
        var actualDiagnosticKeys = (actual.Diagnostics ?? Array.Empty<AnalysisDiagnostic>())
          .Select(BuildDiagnosticKey)
          .ToList();
        Assert.Equal(expectedDiagnosticKeys, actualDiagnosticKeys);
    }

    private static IReadOnlyList<string> BuildMarkKeys(IReadOnlyList<MarkRecord> marks)
    {
        return marks
          .Select(mark => string.Join(
            "|",
            mark.RuleId,
            mark.SyntaxNode.SpanStart,
            mark.SyntaxNode.Span.Length,
            mark.Reason,
            mark.GroupKey,
            mark.PrimaryGraphNode!.NodeId))
          .ToList();
    }

    private static string BuildDecisionKey(RuleDecision decision)
    {
        return string.Join(
          "|",
          decision.Action,
          decision.FinalNode.RawKind,
          decision.FinalNode.SpanStart,
          decision.FinalNode.Span.Length,
          decision.Reason);
    }

    private static string BuildDiagnosticKey(AnalysisDiagnostic diagnostic)
    {
        return string.Join(
          "|",
          diagnostic.Id,
          diagnostic.Severity,
          diagnostic.FilePath,
          diagnostic.Start,
          diagnostic.End,
          diagnostic.Message);
    }
}

