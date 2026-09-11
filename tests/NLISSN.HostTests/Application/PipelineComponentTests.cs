using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NL.Concurrency;
using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;
using System.Text;
using NLISSN.Core.Analysis;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Common;
using RoslynPrototype.Tests.TestCodeSet.Large;
using RoslynPrototype.Tests.TestCodeSet.DirectoryFixtures;
using RoslynPrototype.Tests.TestCodeSet.Pipeline;
using RoslynPrototype.Tests.TestCodeSet.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Target;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class PipelineComponentTests : IDisposable
{
    private readonly string _tempDirectory;

    public PipelineComponentTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void FlowSummaryRegistry_ProjectOverride_TakesPrecedenceOverFrameworkAndUnknown()
    {
        var framework = new NLCPGFlowSummary(
          "framework",
          "Demo.Helpers",
          "Map",
          0,
          new[] { NLCPGFlowSummaryEndpoint.Parameter(0) },
          NLCPGFlowSummaryEndpoint.Return);
        var project = framework with { Sources = new[] { NLCPGFlowSummaryEndpoint.Receiver } };
        var registry = new NLCPGFlowSummaryRegistry(new[] { project }, new[] { framework });

        var resolved = registry.Resolve(project.StableKey);

        Assert.Equal(NLCPGFlowSummaryResolution.Project, resolved.Resolution);
        Assert.Same(project, resolved.Summary);
        Assert.Equal(NLCPGFlowSummaryResolution.Unknown, registry.Resolve("missing").Resolution);
    }

    [Fact]
    public void AnalyzeFromArgs_WithSkipRewrite_DoesNotRetainRewrittenSource()
    {
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_WithRuntimeLog_WritesProcessAndPoolMetrics()
    {
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime.log");

        CreateCommandHost().AnalyzeFromArgs(new[]
        {
          "--target-name",
          "s",
          "--skip-rewrite",
          "--no-diff",
          "--runtime-log",
          runtimeLogPath,
          "--log-profile",
          "benchmark"
        });

        var lines = File.ReadAllLines(runtimeLogPath);

        Assert.Contains(lines, line => line.Contains("cat=run evt=started", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("cat=run evt=sampled", StringComparison.Ordinal) &&
          line.Contains("allocBytes=", StringComparison.Ordinal) &&
          line.Contains("tpPending=", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("cat=run evt=completed", StringComparison.Ordinal) &&
          line.Contains("status=completed", StringComparison.Ordinal) &&
          line.Contains("poolOperations=", StringComparison.Ordinal) &&
          line.Contains("nodes=", StringComparison.Ordinal) &&
          line.Contains("edges=", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateFromOptions_WithCpgDopOverride_UsesExplicitCpgValue()
    {
        var runtime = AnalysisLegacyOptionsTestExtensions.CreateRuntime(
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
        var runtime = AnalysisLegacyOptionsTestExtensions.CreateRuntime(
          new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
          {
            ["max-degree-of-parallelism"] = "12"
          });

        Assert.Equal(12, runtime.ExecutionOptions.EffectiveCpgMaxDegreeOfParallelism);
    }

    [Fact]
    public void RuntimeLifecycle_PreservesCpgBuildAdmissionBudget()
    {
        var runtime = new  AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(MaxDegreeOfParallelism: 4, CpgMaxDegreeOfParallelism: 3),
          new  AnalysisEpoch(0, 0, 0));

        Assert.Same(runtime.CpgBuildAdmissionBudget, runtime.InvalidateCaches().CpgBuildAdmissionBudget);
        Assert.Same(runtime.CpgBuildAdmissionBudget, runtime.NextEpoch().CpgBuildAdmissionBudget);
    }

    [Fact]
    public async Task RuntimeConcurrencyPool_RecordsOperationsInRuntimeOwnedTelemetry()
    {
        var runtime = new AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(MaxDegreeOfParallelism: 2),
          new AnalysisEpoch(0, 0, 0));

        var results = await runtime.ConcurrencyPool.SelectCpuBoundOrdered(
          new[] { 1, 2 },
          maxDegreeOfParallelism: 2,
          (source, _, _) => source * 2);

        var telemetry = Assert.Single(runtime.ConcurrencyTelemetry.Operations);
        Assert.Equal(ConcurrencyOperationKind.CpuBoundOrderedSelection, telemetry.OperationKind);
        Assert.Equal(new[] { 2, 4 }, results);
        Assert.Same(runtime.ConcurrencyTelemetry, runtime.NextEpoch().ConcurrencyTelemetry);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("true")]
    public void CreateFromOptions_WithInvalidCpgDopOverride_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
           AnalysisLegacyOptionsTestExtensions.CreateRuntime(
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
        var runtime = new  AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 2,
            EnableGroupParallelism: true),
          new  AnalysisEpoch(0, 0, 0));
        var (context, root) = CreateContext(source, runtime: runtime);
        var probe = new ConcurrentRuleProbe(expectedConcurrentRules: 2);
        var rules = new RuleDefinitionMark[]
        {
          new ConcurrentTypeMarkRule("TEST-CONCURRENT-MARK-001", "First", probe),
          new ConcurrentTypeMarkRule("TEST-CONCURRENT-MARK-002", "Second", probe)
        };

        var marks = new MarkingEngine().Run(context, root, rules);

        Assert.Equal(2, marks.Count);
        Assert.Equal(2, probe.PeakActiveRuleCount);
    }

    [Fact]
    public void MarkingEngine_Run_WithGroupParallelismDisabled_RunsIndependentRulesSerially()
    {
        // Arrange
        var runtime = new AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 2,
            EnableGroupParallelism: false),
          new AnalysisEpoch(0, 0, 0));
        var (context, root) = CreateContext(PipelineSources.ConcurrentMarkingSource, runtime: runtime);
        var probe = new ConcurrentRuleProbe(
          expectedConcurrentRules: 2,
          releaseWait: TimeSpan.FromMilliseconds(250));
        var rules = new RuleDefinitionMark[]
        {
          new ConcurrentTypeMarkRule("TEST-CONCURRENT-MARK-001", "First", probe),
          new ConcurrentTypeMarkRule("TEST-CONCURRENT-MARK-002", "Second", probe)
        };

        // Act
        var marks = new MarkingEngine().Run(context, root, rules);

        // Assert
        Assert.Equal(2, marks.Count);
        Assert.Equal(1, probe.PeakActiveRuleCount);
    }

    [Fact]
    public void MarkingEngine_Run_DeduplicatesSameRuleAndSyntaxSpan()
    {
        var source = AtomicExpressionSources.MarkingDedupSource;

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
    public void MarkingEngine_Run_TargetRules_PreservesSeedMarksAcrossGroupParallelismAndReusesCachedOperation()
    {
        var source = PipelineSources.SnapshotCacheSource;
        var (serialContext, serialRoot) = CreateContext(source, "s");
        var parallelRuntime = new  AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            MaxDegreeOfParallelism: 4,
            EnableGroupParallelism: true),
          new  AnalysisEpoch(0, 0, 0));
        var (parallelContext, parallelRoot) = CreateContext(source, "s", parallelRuntime);
        var engine = new MarkingEngine();

        var serialMarks = engine.Run(serialContext, serialRoot, GetAtomicMarkRules());
        var parallelMarks = engine.Run(parallelContext, parallelRoot, GetAtomicMarkRules());
        var targetIdentifier = parallelRoot.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .First(identifier => identifier.Identifier.ValueText == "s");

        var firstOperation = parallelContext.GetCachedOperation(targetIdentifier);
        var secondOperation = parallelContext.GetCachedOperation(targetIdentifier);

        Assert.Equal(BuildMarkKeys(serialMarks), BuildMarkKeys(parallelMarks));
        Assert.All(serialMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        Assert.All(parallelMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        Assert.Same(firstOperation, secondOperation);
    }

    [Fact]
    public void MarkAnalysisSnapshot_ReusesRegionFactsForDistinctAnchorsInOneStatement()
    {
        var (context, root) = CreateContext("""
          public sealed class Sample
          {
            public void Run(Box s)
            {
              var value = s.Left + s.Right;
            }
          }

          public sealed class Box
          {
            public int Left { get; }
            public int Right { get; }
          }
          """, "s");
        var anchors = root.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .Where(identifier => identifier.Identifier.ValueText == "s")
          .ToArray();

        var first = context.AnalyzeMarkRegion(anchors[0]);
        var second = context.AnalyzeMarkRegion(anchors[1]);

        Assert.Same(anchors[0], first.AnchorNode);
        Assert.Same(anchors[1], second.AnchorNode);
        Assert.Same(first.RegionNode, second.RegionNode);
        Assert.Equal(first.Span, second.Span);
    }

    [Fact]
    public void EnumerateAllowedExpressions_AllAtomicKinds_ReturnsRequestedKindsInSourceOrder()
    {
        var (context, root) = CreateContext("""
          public sealed class Sample
          {
            public void Run(Box s, int[] values)
            {
              var literal = 1;
              var member = s.Value;
              var invocation = s.GetValue();
              var element = values[0];
              var conditional = s?.Value;
              var created = new Box();
            }
          }

          public sealed class Box
          {
            public int Value { get; }
            public int GetValue() => Value;
          }
          """, "s");
        var allowedKinds = new[]
        {
            SyntaxKind.IdentifierName,
            SyntaxKind.NumericLiteralExpression,
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.InvocationExpression,
            SyntaxKind.ElementAccessExpression,
            SyntaxKind.ConditionalAccessExpression,
            SyntaxKind.ObjectCreationExpression
        };

        var expressions = context.EnumerateAllowedExpressions(root, allowedKinds).ToArray();

        var expected = new AtomicExpressionAnalyzer().Analyze(root)
          .Where(expression => allowedKinds.Contains(expression.Kind()))
          .Select(expression => (expression.Kind(), expression.Span))
          .ToArray();
        Assert.Equal(expected, expressions.Select(expression => (expression.Kind(), expression.Span)));
        Assert.Equal(
          expressions.OrderBy(expression => expression.SpanStart).Select(expression => expression.Span),
          expressions.Select(expression => expression.Span));
    }

    [Fact]
    public void EnumerateMethodDeclarations_WithBlockAndExpressionBodies_ReturnsAllMethodsInSourceOrder()
    {
        var (context, root) = CreateContext("""
          public sealed class Sample
          {
            public void Block()
            {
            }

            public int Expression() => 1;
          }
          """);

        var methods = context.EnumerateMethodDeclarations(root).ToArray();

        Assert.Equal(new[] { "Block", "Expression" }, methods.Select(method => method.Identifier.ValueText));
    }

    [Fact]
    public void AnalyzeMarkRegion_ForDistinctAnchors_PreservesAnchorAndExactCounts()
    {
        var (context, root) = CreateContext("""
          public sealed class Sample
          {
            public void Run(Box s)
            {
              var value = s.Left + s.Right;
            }
          }

          public sealed class Box
          {
            public int Left { get; }
            public int Right { get; }
          }
          """, "s");
        var anchors = root.DescendantNodes()
          .OfType<IdentifierNameSyntax>()
          .Where(identifier => identifier.Identifier.ValueText == "s")
          .ToArray();

        var first = context.AnalyzeMarkRegion(anchors[0]);
        var second = context.AnalyzeMarkRegion(anchors[1]);

        Assert.Same(anchors[0], first.AnchorNode);
        Assert.Same(anchors[1], second.AnchorNode);
        Assert.Same(first.RegionNode, second.RegionNode);
        Assert.Equal(12, first.NodeCount);
        Assert.Equal(8, first.ExpressionCount);
        Assert.Equal(1, first.StatementCount);
        Assert.Equal(first.NodeCount, second.NodeCount);
        Assert.Equal(first.ExpressionCount, second.ExpressionCount);
        Assert.Equal(first.StatementCount, second.StatementCount);
    }

    [Fact]
    public void MarkAnalysisSnapshot_FiltersMemberAccessesAndReusesTargetDescriptorKey()
    {
        var (context, root) = CreateContext("""
          public sealed class Sample
          {
            public void Run(Box s, Box other)
            {
              var value = s.Left + other.Right;
            }
          }

          public sealed class Box
          {
            public int Left { get; }
            public int Right { get; }
          }
          """, "s, other, s");

        var memberAccesses = context.EnumerateAllowedExpressions(
          root,
          new[] { SyntaxKind.SimpleMemberAccessExpression }).ToArray();
        var repeatedMemberAccesses = context.EnumerateAllowedExpressions(
          root,
          new[] { SyntaxKind.SimpleMemberAccessExpression }).ToArray();
        var descriptor = context.GetTargetNameDescriptor();
        var repeatedDescriptor = context.GetTargetNameDescriptor();

        Assert.Equal(new[] { "s", "other" }, descriptor.DisplayNames);
        Assert.Same(descriptor, repeatedDescriptor);
        Assert.Equal(memberAccesses, repeatedMemberAccesses);
        Assert.All(
          memberAccesses,
          expression => Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, expression.Kind()));
    }

    [Fact]
    public void PropagationEngine_Run_DeduplicatesSamePropagatedSpan()
    {
        var source = AtomicControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
        var engine = new PropagationEngine();
        var rules = new RuleDefinitionPropagate[] { new DuplicatePropagationRule() };

        var propagatedMarks = engine.Run(context, seedMarks, rules);

        var propagated = Assert.Single(propagatedMarks);
        Assert.Equal(SyntaxKind.IfStatement, (SyntaxKind)propagated.Mark.SyntaxNode.RawKind);
        Assert.NotNull(propagated.Mark.PrimaryGraphNode);
    }

    [Fact]
    public void PropagationEngine_Run_ChainsRulesThroughExplicitDependencies()
    {
        var source = PipelineSources.ChainedPropagationSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
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
    public void PropagationEngine_Run_ExposesDeclaredProducerOutputToConsumer()
    {
        var source = AtomicControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[]
          {
            new DuplicatePropagationRule(),
            new IfStatementVisibilityPropagationRule()
          });

        Assert.Contains(
          propagatedMarks,
            mark => mark.RuleId == "TEST-PROP-VISIBILITY-001" &&
              mark.Mark.SyntaxNode is ReturnStatementSyntax);
    }

    [Fact]
    public void PropagationEngine_Run_SelfConsumingRule_ReachesFixedPoint()
    {
        const string source = "class C { object M(State state) { return state.Root.Next.Next; } } class State { public State Root => this; public State Next => this; }";
        var (context, root) = CreateContext(source, "state");
        var seed = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
          .Single(candidate => string.Equals(candidate.ToString(), "state.Root", StringComparison.Ordinal));
        var seedMarks = new[]
        {
            new MarkRecord(
              "TEST-FIXED-POINT-SEED-001",
              seed,
              null,
              null,
              "Seed the first member access.",
              FactKind: RuleFactKind.TargetExpression)
        };

        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new RepeatedMemberAccessPropagationRule() });

        Assert.Collection(
          propagatedMarks.OrderBy(mark => mark.Depth),
          first =>
          {
              Assert.Equal(1, first.Depth);
              Assert.Equal("state.Root.Next", first.Mark.SyntaxNode.ToString());
          },
          second =>
          {
              Assert.Equal(2, second.Depth);
              Assert.Equal("state.Root.Next.Next", second.Mark.SyntaxNode.ToString());
          });
    }

    [Fact]
    public void PropagationEngine_Run_TwoRuleFeedback_AdmitsEachAnchorOnce()
    {
        const string source = "class C { object M(State state) { return state.Root.Next.Next; } } class State { public State Root => this; public State Next => this; }";
        var (context, root) = CreateContext(source, "state");
        var seed = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
          .Single(candidate => string.Equals(candidate.ToString(), "state.Root", StringComparison.Ordinal));
        var seedMarks = new[]
        {
            new MarkRecord(
              "TEST-FIXED-POINT-SEED-002",
              seed,
              null,
              null,
              "Seed the first member access.",
              FactKind: RuleFactKind.TargetExpression)
        };

        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[]
          {
              new FirstFeedbackMemberAccessPropagationRule(),
              new SecondFeedbackMemberAccessPropagationRule()
          });

        Assert.Equal(2, propagatedMarks.Count);
        Assert.Equal(
          2,
          propagatedMarks.Select(mark => (
              mark.RuleId,
              mark.Mark.SyntaxNode.SpanStart,
              mark.Mark.SyntaxNode.Span.Length,
              mark.Mark.SyntaxNode.RawKind,
              mark.Mark.SemanticTag))
            .Distinct()
            .Count());
        Assert.Equal(new[] { 1, 2 }, propagatedMarks.Select(mark => mark.Depth).OrderBy(depth => depth));
    }

    [Fact]
    public void Analyze_FixedPointRegionFeedsConvergedFactsIntoLiftAndPropose()
    {
        const string source = "class C { object M(State state) { return state.Root.Next.Next; } } class State { public State Root => this; public State Next => this; }";
        var lifter = new FixedPointLiftRule();
        var proposer = new FixedPointProposalRule();
        var application = new ApplicationService(new RulePipeline(
          new RuleDefinitionMark[] { new FixedPointSeedMarkRule() },
          new RuleDefinitionPropagate[] { new RepeatedMemberAccessPropagationRule() },
          new RuleDefinitionLift[] { lifter },
          new RuleDefinitionPropose[] { proposer }));

        var result = application.Analyze(
          source,
          "fixed-point-e2e.cs",
          new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        Assert.Equal(
          new[] { "state.Root.Next", "state.Root.Next.Next" },
          result.PropagatedMarks
            .OrderBy(mark => mark.Depth)
            .Select(mark => mark.Mark.SyntaxNode.ToString()));
        Assert.True(lifter.SawConvergedFacts);
        Assert.True(proposer.SawLiftedFact);
        var secondFact = result.PropagatedMarks.Single(mark => mark.Depth == 2);
        var evidence = Assert.IsType<AnalysisEvidenceGraph>(result.Evidence);
        var secondEvidence = Assert.Single(
          evidence.Nodes,
          node => node.Kind == AnalysisEvidenceKind.Propagation &&
            node.Anchor is
            {
                Start: var start,
                Length: var length
            } &&
            start == secondFact.Mark.SyntaxNode.SpanStart &&
            length == secondFact.Mark.SyntaxNode.Span.Length);
        Assert.Contains("depth:2", secondEvidence.SummaryKey ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(
          evidence.Edges,
          edge => edge.Kind == AnalysisEvidenceEdgeKind.DerivedFrom &&
            edge.TargetId == secondEvidence.Id);
    }

    [Fact]
    public void DeclarationSymbolReferencePropagationRule_SameScopeReference_ExcludesNestedShadowedLocal()
    {
        var (context, root) = CreateContext("""
          namespace Demo;

          public sealed class PlayerInput
          {
          }

          public sealed class Game
          {
            public void Run()
            {
              var input = new PlayerInput();
              System.Console.Write(input);

              void Nested()
              {
                var input = new PlayerInput();
                System.Console.Write(input);
              }
            }
          }
          """, "PlayerInput");
        var outerDeclarator = root.DescendantNodes()
          .OfType<VariableDeclaratorSyntax>()
          .First(declarator => string.Equals(declarator.Identifier.ValueText, "input", StringComparison.Ordinal));
        var seedMark = new MarkRecord(
          "TEST-CLASS-LOCAL-001",
          outerDeclarator,
          null,
          null,
          "This diagnostic text is intentionally unrelated.",
          RuleOutputKind.LocalDefinitionFromObjectCreation,
          FactKind: RuleFactKind.FlowLocalDefinition);

        var propagatedMarks = new PropagationEngine().Run(
          context,
          new[] { seedMark },
          new RuleDefinitionPropagate[] { new DeclarationSymbolReferencePropagationRule() });

        var propagated = Assert.Single(propagatedMarks);
        Assert.Equal("input", Assert.IsType<IdentifierNameSyntax>(propagated.Mark.SyntaxNode).Identifier.ValueText);
        Assert.True(propagated.Mark.SyntaxNode.SpanStart > outerDeclarator.SpanStart);
    }

    [Fact]
    public void SymbolReferencePropagationRule_SameScopeReference_ExcludesNestedShadowedLocal()
    {
        var (context, root) = CreateContext("""
          namespace Demo;

          public sealed class Box
          {
            public int Value { get; set; }
          }

          public sealed class Game
          {
            public int Run(Box s)
            {
              var value = s.Value;

              void Nested()
              {
                var value = 1;
                System.Console.Write(value);
              }

              return value;
            }
          }
          """, "s");
        var outerDeclarator = root.DescendantNodes()
          .OfType<VariableDeclaratorSyntax>()
          .First(declarator => string.Equals(declarator.Identifier.ValueText, "value", StringComparison.Ordinal));
        var seedMark = new MarkRecord(
          "TEST-SOBJ-LOCAL-001",
          outerDeclarator,
          null,
          null,
          "This diagnostic text is intentionally unrelated.",
          RuleOutputKind.LocalDefinitionFromInitializer,
          FactKind: RuleFactKind.FlowLocalDefinition);

        var propagatedMarks = new PropagationEngine().Run(
          context,
          new[] { seedMark },
          new RuleDefinitionPropagate[] { new SymbolReferencePropagationRule() });

        var propagated = Assert.Single(propagatedMarks);
        Assert.Equal("value", Assert.IsType<IdentifierNameSyntax>(propagated.Mark.SyntaxNode).Identifier.ValueText);
        Assert.True(propagated.Mark.SyntaxNode.SpanStart > outerDeclarator.SpanStart);
    }

    [Fact]
    public void AnalysisSession_LocalSymbolReferences_MaterializesOncePerSession()
    {
        var (session, root) = CreateContext("""
          public sealed class Sample
          {
            public int Run(int value)
            {
              return value + 1;
            }
          }
          """);

        var first = session.LocalSymbolReferences;
        var identifierCount = root.DescendantNodes().OfType<IdentifierNameSyntax>().Count();
        var second = session.LocalSymbolReferences;

        Assert.Equal(identifierCount, first.IdentifierTraversalCount);
        Assert.Same(first, second);
        Assert.Equal(identifierCount, second.IdentifierTraversalCount);
    }

    [Fact]
    public void PropagationEngine_Run_BuildsRuleScopedStructureViewForEachRule()
    {
        var source = AtomicControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
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
        var application = new  ApplicationService(
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
    public void  AnalysisRuntime_GetOrCreateCompilationCache_AllowsMultipleCacheTypesPerCompilation()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Demo; public sealed class Sample { }", path: "runtime-cache.cs");
        var compilation = CreateCompilation(tree);
        var runtime =  AnalysisRuntime.CreateDefault();

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
    public void AnalysisRuntime_GetOrCreateCompilationCache_AcrossRuntimeEpochs_ReusesValue()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace Demo; public sealed class Sample { }", path: "runtime-cache-epoch.cs");
        var compilation = CreateCompilation(tree);
        var runtime = AnalysisRuntime.CreateDefault();

        var first = GetCompilationCache(
            runtime,
            compilation,
            static _ => new TestCompilationCacheA("first"));
        var invalidated = GetCompilationCache(
            runtime.InvalidateCaches(),
            compilation,
            static _ => new TestCompilationCacheA("invalidated"));
        var nextEpoch = GetCompilationCache(
            runtime.NextEpoch(),
            compilation,
            static _ => new TestCompilationCacheA("next"));

        Assert.Same(first, invalidated);
        Assert.Same(first, nextEpoch);
    }

    [Fact]
    public void MarkingEngine_Run_DoesNotUseGroupScheduler()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingConcurrencyPool();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var engine = new MarkingEngine();

        var marks = engine.Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelTypeMarkRule("TEST-PARALLEL-MARK-A", "Alpha"),
            new ParallelTypeMarkRule("TEST-PARALLEL-MARK-B", "Beta")
          });

        Assert.Equal(0, scheduler.InvocationCount);
        Assert.Empty(scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-PARALLEL-MARK-A", "TEST-PARALLEL-MARK-B" },
          marks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void PropagationEngine_Run_UsesRuleGraphDependencies()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingConcurrencyPool();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelTypeMarkRule("TEST-PROP-SEED-A", "Alpha"),
            new ParallelTypeMarkRule("TEST-PROP-SEED-B", "Beta")
          });
        scheduler.Reset();
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[]
          {
            new MethodPropagationRule("TEST-PROP-A", "TEST-PROP-SEED-A"),
            new MethodPropagationRule("TEST-PROP-B", "TEST-PROP-SEED-B")
          });

        Assert.Equal(0, scheduler.InvocationCount);
        Assert.Empty(scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-PROP-A", "TEST-PROP-B" },
          propagatedMarks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void MarkLiftingEngine_Run_UsesRuleGraphDependencies()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingConcurrencyPool();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelTypeMarkRule("TEST-LIFT-SEED-A", "Alpha"),
            new ParallelTypeMarkRule("TEST-LIFT-SEED-B", "Beta")
          });
        scheduler.Reset();
        var engine = new MarkLiftingEngine();

        var liftedMarks = engine.Run(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          new RuleDefinitionLift[]
          {
            new NamespaceLiftRule("TEST-LIFT-A1", "TEST-LIFT-SEED-A"),
            new NamespaceLiftRule("TEST-LIFT-A2", "TEST-LIFT-SEED-A"),
            new NamespaceLiftRule("TEST-LIFT-B", "TEST-LIFT-SEED-B")
          });

        Assert.Equal(0, scheduler.InvocationCount);
        Assert.Empty(scheduler.ItemCounts);
        Assert.Equal(
          new[] { "TEST-LIFT-A1", "TEST-LIFT-A2", "TEST-LIFT-B" },
          liftedMarks.Select(mark => mark.RuleId).ToArray());
    }

    [Fact]
    public void RuleDecisionEngine_Decide_SchedulesOnlyConflictResolution()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingConcurrencyPool();
        var runtime = CreateParallelRuntime(scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);
        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelTypeMarkRule("TEST-DECIDE-SEED-A", "Alpha"),
            new ParallelTypeMarkRule("TEST-DECIDE-SEED-B", "Beta")
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
            new DeclarationDecisionRule("TEST-DECIDE-A1", "TEST-DECIDE-SEED-A"),
            new DeclarationDecisionRule("TEST-DECIDE-A2", "TEST-DECIDE-SEED-A"),
            new DeclarationDecisionRule("TEST-DECIDE-B", "TEST-DECIDE-SEED-B")
          });

        Assert.Equal(1, scheduler.InvocationCount);
        Assert.Equal(new[] { 2 }, scheduler.ItemCounts);
        Assert.Equal(
          new[] { "Delete TEST-DECIDE-A1", "Delete TEST-DECIDE-B" },
          decisions.Select(decision => decision.Reason).ToArray());
    }

    [Fact]
    public void CompatibilityStageEngines_WhenGroupParallelismIsDisabled_RequestSingleDependencyGraphWorker()
    {
        var source = PipelineSources.ParallelPropagationSource;
        var scheduler = new RecordingConcurrencyPool();
        var runtime = new AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(4, EnableGroupParallelism: false),
          new AnalysisEpoch(0, 0, 0),
          scheduler);
        var (context, root) = CreateContext(source, runtime: runtime);

        var seedMarks = new MarkingEngine().Run(
          context,
          root,
          new RuleDefinitionMark[]
          {
            new ParallelTypeMarkRule("TEST-COMPAT-SEED-A", "Alpha"),
            new ParallelTypeMarkRule("TEST-COMPAT-SEED-B", "Beta")
          });
        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[]
          {
            new MethodPropagationRule("TEST-COMPAT-PROP-A", "TEST-COMPAT-SEED-A"),
            new MethodPropagationRule("TEST-COMPAT-PROP-B", "TEST-COMPAT-SEED-B")
          });
        var liftedMarks = new MarkLiftingEngine().Run(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          new RuleDefinitionLift[]
          {
            new NamespaceLiftRule("TEST-COMPAT-LIFT-A", "TEST-COMPAT-SEED-A"),
            new NamespaceLiftRule("TEST-COMPAT-LIFT-B", "TEST-COMPAT-SEED-B")
          });
        _ = new RuleDecisionEngine().Decide(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          liftedMarks,
          new RuleDefinitionPropose[]
          {
            new DeclarationDecisionRule("TEST-COMPAT-DECIDE-A", "TEST-COMPAT-SEED-A"),
            new DeclarationDecisionRule("TEST-COMPAT-DECIDE-B", "TEST-COMPAT-SEED-B")
          });

        Assert.Equal(new[] { 1, 1, 1 }, scheduler.DependencyGraphMaxDegrees);
        Assert.Equal(new[] { 1 }, scheduler.SelectOrderedMaxDegrees);
    }

    [Fact]
    public void PropagationEngine_Run_DeclarationMethodParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.MethodParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-method-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-method-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new MethodParameterUsagePropagationRule() });

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
    public void PropagationEngine_Run_DeclarationLocalFunctionParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.LocalFunctionParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-local-function-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-local-function-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new LocalFunctionParameterUsagePropagationRule() });

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
    public void PropagationEngine_Run_DeclarationIndexerParameterUsageRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.IndexerParameterUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-indexer-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-indexer-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new IndexerParameterUsagePropagationRule() });

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
    public void PropagationEngine_Run_DeclarationDelegateUsageTypeificationRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.DelegateUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-delegate-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-delegate-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DelegateUsageClassificationPropagationRule() });

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
    public void PropagationEngine_Run_DeclarationExtensionMethodMappedCallsiteRule_ProducesStructuredPayload()
    {
        var source = PipelineSources.ExtensionMethodUsageSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-extension-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-extension-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new ExtensionMethodMappedCallsitePropagationRule() });

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
    public void PropagationEngine_Run_DeclarationDeclarationHostRule_ProducesStructuredPayloads()
    {
        var source = PipelineSources.DeclarationHostSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-declaration-host-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-declaration-host-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var engine = new PropagationEngine();

        var propagatedMarks = engine.Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DeclarationHostPropagationRule() });

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
    public void MarkLiftingEngine_Run_AtomicLogicalExpressionRule_ProducesLiftPayload()
    {
        var source = PipelineSources.LogicalOperandGroupSource;

        var (context, root) = CreateContext(source, "s, unused");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new LogicalExpressionPropagationRule() });
        var liftedMarks = new MarkLiftingEngine().Run(
          context,
          seedMarks,
          propagatedMarks,
          new RuleDefinitionLift[] { new LogicalExpressionLiftingRule() });

        Assert.DoesNotContain(
          liftedMarks,
          mark => mark.Mark.SyntaxNode is BinaryExpressionSyntax binaryExpression &&
            string.Equals(binaryExpression.ToString(), "s.IsReady && ready && fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkLiftingEngine_Run_AtomicIfStructureRule_ProducesStructuralLiftPayloads()
    {
        var source = PipelineSources.AtomicIfStructureCompletionSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
        var liftedMarks = new MarkLiftingEngine().Run(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          new RuleDefinitionLift[] { new IfStructureLiftingRule() });

        Assert.Contains(
          liftedMarks,
          mark => mark.Mark.SyntaxNode is IfStatementSyntax ifStatement &&
            string.Equals(ifStatement.Condition.ToString(), "s.IsReady", StringComparison.Ordinal) &&
            mark.StructureKind == StructuralKind.If &&
            mark.Payload is IfStructureLiftPayload payload &&
            payload.Kind == IfStructureLiftKind.ReplaceIfWithElseIfTail &&
            payload.TailNode is IfStatementSyntax);
        Assert.Contains(
          liftedMarks,
          mark => mark.Mark.SyntaxNode is IfStatementSyntax &&
            mark.StructureKind == StructuralKind.If &&
            mark.Payload is IfStructureLiftPayload payload &&
            payload.Kind == IfStructureLiftKind.DeleteOwningElseClause &&
            payload.ParentElseClause is not null);
    }

    [Fact]
    public void MarkLiftingEngine_Run_SharedIfStructureRule_ProducesStructuralLiftPayload()
    {
        var source = PipelineSources.DeclarationIfStructureCompletionSource;

        var tree = CSharpSyntaxTree.ParseText(source, path: "delete-class-if-structure-propagation.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(
          source,
          "delete-class-if-structure-propagation.cs");
        var context = new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
              ["delete-class"] = "PlayerInput"
            }));
        var seedMarks = new MarkingEngine().Run(context, root, GetDeclarationMarkRules());
        var liftedMarks = new MarkLiftingEngine().Run(
          context,
          seedMarks,
          Array.Empty<PropagatedMarkRecord>(),
          new RuleDefinitionLift[] { new IfStructureLiftingRule() });

        var ifLift = Assert.Single(
          liftedMarks,
          mark => mark.Mark.SyntaxNode is IfStatementSyntax ifStatement &&
            string.Equals(ifStatement.Condition.ToString(), "input.IsReady", StringComparison.Ordinal));
        Assert.Equal(StructuralKind.If, ifLift.StructureKind);
        var payload = Assert.IsType<IfStructureLiftPayload>(ifLift.Payload);
        Assert.Equal(IfStructureLiftKind.ReplaceIfWithElseIfTail, payload.Kind);
        Assert.IsType<IfStatementSyntax>(payload.TailNode);
    }

    [Fact]
    public void MarkLiftingEngine_Run_BuildsRuleScopedStructureViewForEachRule()
    {
        var source = AtomicControlFlowSources.PropagationDedupSource;

        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
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
    public void MultiFragmentStructureView_RuleOutputDecisionRewriteAndDiff_RemainConnected()
    {
        // ViewAwareLiftRule verifies that both seed and propagated graph anchors are selected.
        var source = AtomicControlFlowSources.PropagationDedupSource;
        var (context, root) = CreateContext(source, "s");
        var seedMarks = new MarkingEngine().Run(context, root, GetAtomicMarkRules());
        var propagatedMarks = new PropagationEngine().Run(
          context,
          seedMarks,
          new RuleDefinitionPropagate[] { new DuplicatePropagationRule() });
        var liftedMark = Assert.Single(new MarkLiftingEngine().Run(
          context,
          seedMarks,
          propagatedMarks,
          new RuleDefinitionLift[] { new ViewAwareLiftRule() }));
        var decision = new RuleDecision(
          liftedMark.Mark.SyntaxNode,
          liftedMark.Mark.SyntaxNode,
          DecisionActionKind.Delete,
          "Delete the rule-selected return statement.");
        var rewriter = new PrototypeRewriter();

        var result = rewriter.Rewrite(root, context.SemanticModel, new[] { decision });

        Assert.Equal(SyntaxKind.ReturnStatement, (SyntaxKind)liftedMark.Mark.SyntaxNode.RawKind);
        Assert.Single(result.Edits);
        TextDiffAssert.Contains("return default(int);", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("return 1;", result.RewrittenSource, result.Diff);
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
    public void AnalyzeFromArgs_SuppressesUnsafeLocalDeclarationRewrite()
    {
        var filePath = Path.Combine(_tempDirectory, "delete-s-object-sample.cs");
        var rawDiffPath = Path.Combine(_tempDirectory, "delete-s-object-sample.raw.diff");
        var aggregateDiffPath = BuildDiffArtifactWriter.GetDiffFilePath(
            "PipelineComponentTests.cs",
            "Cli");
        BuildDiffArtifactWriter.InitializeDiffFile(aggregateDiffPath);
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void AnalyzeFromArgs_WithReadableDiffView_SuppressesUnsafeLocalDeclarationRewrite()
    {
        var filePath = Path.Combine(_tempDirectory, "delete-s-object-readable.cs");
        var rawDiffPath = Path.Combine(_tempDirectory, "delete-s-object-readable.diff");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath,
            "--diff-view",
            "readable"
        });

        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_UsesDefaultSourceWhenInputPathIsMissing()
    {
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[] { "--target-name", "s" });

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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_WithNoDiff_PreservesUnsafeLocalDeclaration()
    {
        var filePath = Path.Combine(_tempDirectory, "single-file-no-diff.cs");
        var expectedDiffPath = Path.Combine(_tempDirectory, "single-file-no-diff.rewrite.diff");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--write-back",
            "--no-diff"
        });

        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(expectedDiffPath));
        var rewrittenSource = File.ReadAllText(filePath);
        Assert.Contains("return value;", rewrittenSource, StringComparison.Ordinal);
        Assert.Contains("var value = s.Seed + offset;", rewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_RewritesMultipleFilesAndKeepsCompilationValid()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
        Assert.Equal(
          Path.Combine(
            Directory.GetCurrentDirectory(),
            "Build",
            "Result",
            "Diff"),
          result.DiffFilePath);
        Assert.True(Directory.Exists(result.DiffFilePath));

        var rewrittenTypeSource = File.ReadAllText(classFilePath);
        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        Assert.DoesNotContain("class PlayerInput", rewrittenTypeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("if (PlayerInput.Enabled)", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.Ping();", rewrittenConsumerSource, StringComparison.Ordinal);

        var rewrittenTrees = new[]
        {
            CSharpSyntaxTree.ParseText(rewrittenTypeSource, path: classFilePath),
            CSharpSyntaxTree.ParseText(rewrittenConsumerSource, path: consumerFilePath)
        };
        var compilation = CreateCompilation(rewrittenTrees);
        var errors = compilation.GetDiagnostics()
          .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
          .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_SeparateDirectoryAndCpgDop_KeepsStableResults()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var serialResult = CreateCommandHost().AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          "1",
          "--no-diff"
        });
        Assert.NotEmpty(serialResult.Edits);
        foreach (var configuration in new[]
        {
          (DirectoryDop: 12, CpgDop: 1, DisableDirectoryParallelism: false),
          (DirectoryDop: 12, CpgDop: 12, DisableDirectoryParallelism: false),
          (DirectoryDop: 12, CpgDop: 12, DisableDirectoryParallelism: true)
        })
        {
            var arguments = new List<string>
            {
                projectDirectory,
                "--delete-class",
                "PlayerInput",
                "--max-degree-of-parallelism",
                configuration.DirectoryDop.ToString(),
                "--cpg-max-degree-of-parallelism",
                configuration.CpgDop.ToString(),
                "--no-diff"
            };
            if (configuration.DisableDirectoryParallelism)
            {
                arguments.Add("--disable-directory-parallelism");
            }

            var parallelResult = CreateCommandHost().AnalyzeFromArgs(arguments.ToArray());

            Assert.NotEmpty(parallelResult.Edits);
            var expectedGrantedCpgDop = configuration.DisableDirectoryParallelism
              ? configuration.CpgDop
              : Math.Min(configuration.CpgDop, Math.Max(1, configuration.CpgDop / 2));
            AssertEquivalentAnalysisResults(serialResult, parallelResult);
        }
    }

    [Fact]
    public async Task AnalyzeDirectoryAsync_SourceOrderPublicationBacklog_PreservesFileAnalysis()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "directory-publication-backlog-project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "A.Slow.cs"),
          "namespace Demo; public sealed class SlowFile { public int Run() => 1; }",
          Encoding.UTF8);
        File.WriteAllText(
          Path.Combine(projectDirectory, "B.Fast.cs"),
          "namespace Demo; public sealed class FastFileOne { public int Run() => 2; }",
          Encoding.UTF8);
        var delayRule = new FileDelayMarkRule();
        var host = new  CommandHost(new  RulePipeline(
          new RuleDefinitionMark[] { delayRule },
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>()));

        await host.AnalyzeFromArgsAsync(new[]
        {
            projectDirectory,
            "--max-degree-of-parallelism",
            "2",
            "--cpg-max-degree-of-parallelism",
            "2",
            "--no-diff"
        });

        Assert.True(delayRule.FastFileEntered.IsCompletedSuccessfully);
    }

    [Fact]
    public void AnalyzeFromArgs_WithInvalidCpgDopOverride_ThrowsArgumentException()
    {
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var exception = Assert.Throws<ArgumentException>(() => CreateCommandHost().AnalyzeFromArgs(new[]
        {
          "--cpg-max-degree-of-parallelism",
          "0"
        }));

        Assert.Contains("--cpg-max-degree-of-parallelism", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DisableHelperParallelism_KeepsStableResults()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var defaultResult = CreateCommandHost().AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--max-degree-of-parallelism",
          "8",
          "--no-diff"
        });
        var helperSerialResult = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_WritesPerFileDiffsUnderConfiguredRoot()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--diff-out",
          diffRootPath
        });

        Assert.NotEmpty(result.Edits);
        Assert.Equal(Path.GetFullPath(diffRootPath), result.DiffFilePath);

        var declarationDiffPath = Path.Combine(
          diffRootPath,
          "ExpressionControlFlow",
          "PlayerInput.rewrite.diff");
        var consumerDiffPath = Path.Combine(
          diffRootPath,
          "ExpressionControlFlow",
          "Gameplay",
          "Game.rewrite.diff");
        Assert.True(File.Exists(declarationDiffPath));
        Assert.True(File.Exists(consumerDiffPath));
        Assert.Contains("class PlayerInput", File.ReadAllText(declarationDiffPath), StringComparison.Ordinal);
        Assert.Contains("PlayerInput.Enabled", File.ReadAllText(consumerDiffPath), StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ConcurrentDiffWrites_PreserveResultsAndDiffBytes()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-concurrent-diff-project");
        var gameplayDirectory = Path.Combine(projectDirectory, "Gameplay");
        var systemsDirectory = Path.Combine(projectDirectory, "Systems");
        Directory.CreateDirectory(gameplayDirectory);
        Directory.CreateDirectory(systemsDirectory);
        File.WriteAllText(
          Path.Combine(projectDirectory, "PlayerInput.cs"),
          DirectorySources.PlayerInputEnabledSource);
        File.WriteAllText(
          Path.Combine(gameplayDirectory, "Game.cs"),
          DirectorySources.GameUsingPlayerInputSource);
        File.WriteAllText(
          Path.Combine(systemsDirectory, "Renderer.cs"),
          DirectorySources.RendererWithBlockBodyUsingPlayerInputSource);
        var application = new  ApplicationService(RulePipelineTestFactory.Create());
        var diffRootPath = Path.Combine(_tempDirectory, "concurrent-diff-output");
        var resultsByDegree = new Dictionary<int, PrototypeAnalysisResult>();
        var diffBytesByDegree = new Dictionary<int, IReadOnlyDictionary<string, byte[]>>();

        foreach (var maxDegreeOfParallelism in new[] { 1, 2, 16 })
        {
            var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_RewritesLargeAssetProjectAndKeepsCompilationValid()
    {
        var projectDirectory = Path.Combine(_tempDirectory, "delete-class-large-asset-project");
        LargeSources.WriteLargeProject(projectDirectory);
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_MarksTypeSyntaxReferencesAsAtomicComponents()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
            "mark.type.type-syntax",
            StringComparison.Ordinal))
          .Select(mark => mark.SyntaxNode.ToString())
          .ToList();

        Assert.Contains("PlayerInput", typeSyntaxMarks);
        Assert.True(
          typeSyntaxMarks.Count == 3,
          $"Expected 3 type syntax marks, got {typeSyntaxMarks.Count}: {string.Join(", ", typeSyntaxMarks)}");
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesFieldAndPropertyDeclarationsWithTargetType()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_PropagatesObjectCreationToLocalDeclaration()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
              "propagate.type.object-creation-declaration",
              StringComparison.Ordinal) &&
            mark.Mark.SyntaxNode is VariableDeclaratorSyntax declarator &&
            string.Equals(declarator.Identifier.ValueText, "input", StringComparison.Ordinal));
        TextDiffAssert.DoesNotContain("var input = new PlayerInput();", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_PropagatesLocalDeclarationToSameScopeReferences()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
              "propagate.type.symbol-reference",
              StringComparison.Ordinal) &&
            mark.Mark.SyntaxNode is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "input", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesPrivateMethodsReturningTargetType()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesPublicMethodsReturningTargetType()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksPrivateMethodsWithTargetTypeParameterAndSyncsCallsites()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksPrivateMethodParameter_ForNamedArguments()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DoesNotShrinkPrivateMethodParameter_WhenNamedAndPositionalCallsitesAreMixed()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksOptionalMethodParameter_AndKeepsOmittedCallsites()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksParamsMethodParameter_WhenAllCallsitesOmitParamsSlot()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksPublicMethodsWithTargetTypeParameterAndSyncsCallsites()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksLocalFunctionParameterAndSyncsCallsites()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksNamedArgumentLocalFunctionParameter()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksOptionalLocalFunctionParameter_AndKeepsOmittedCallsites()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksIndexerParameterAndSyncsElementAccesses()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksNamedArgumentIndexerParameter()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksDelegateMethodGroupBindingsAndInvocations()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksDelegateLambdaBindingsAndInvocations()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksDelegateInvocationChainWithoutBindingRewrite()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksExtensionMethodNonReceiverParameter()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ShrinksUnusedDelegateParameter()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesInterfaceMethodsWithTargetTypeSignature()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesInterfacePropertiesWithTargetTypeSignature()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesInterfaceEventsWithTargetTypeSignature()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesInterfaceIndexersWithTargetTypeSignature()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesExtensionMethodsWithTargetReceiverType()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_RemovesTargetBaseTypes()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesLocalDeclarationsWithTargetGenericTypeArgument()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_ReportsDiagnosticsForResidualPublicSignatures()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_DeletesDelegatesWithTargetTypeSignature()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_KeepsUnusedUsingsAfterRewrite()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        Assert.Contains("using Demo.Input;", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerInput.Enabled", rewrittenConsumerSource, StringComparison.Ordinal);
        Assert.Contains("using System;", rewrittenConsumerSource, StringComparison.Ordinal);

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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_KeepsExtensionMethodUsings()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
          projectDirectory,
          "--delete-class",
          "PlayerInput",
          "--write-back",
          "--no-diff"
        });

        var rewrittenConsumerSource = File.ReadAllText(consumerFilePath);
        TextDiffAssert.Contains("using Demo.Extensions;", rewrittenConsumerSource, result.Diff);
        Assert.Contains("using Demo.Input;", rewrittenConsumerSource, StringComparison.Ordinal);
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_RemovesEmptyNamespaceBlocks()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_KeepsNamespaceWhenEmptyPublicTypeRemains()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_FastDirectoryMode_SkipsUsingAndNamespaceCleanup()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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

        var rewrittenTypeSource = File.ReadAllText(classFilePath);
        TextDiffAssert.Contains("namespace Demo.Input;", rewrittenTypeSource, result.Diff);
        Assert.DoesNotContain("class PlayerInput", rewrittenTypeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeFromArgs_ForDirectoryDeclaration_FastDirectoryMode_TargetNameFilter_ReducesAnalyzedFiles()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void AnalyzeFromArgs_ForDirectoryDeclaration_FastDirectoryMode_WithoutTargetNameFilter_KeepsAllFilesAnalyzed()
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
        var application = new  ApplicationService(RulePipelineTestFactory.Create());

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
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
    public void RulePipelineComposer_ReturnsStableRuleSet()
    {
        var rules = RulePipelineTestFactory.Create();
        var contractAssembly = typeof(RuleDefinitionMark).Assembly;
        var implementationAssembly = typeof(AtomicIdentifierNameMarkRule).Assembly;
        var markRuleType = contractAssembly.GetType("NLISSN.Core.Marking.RuleDefinitionMark");
        var propagateRuleType = contractAssembly.GetType("NLISSN.Core.Propagation.RuleDefinitionPropagate");
        var liftRuleType = contractAssembly.GetType("NLISSN.Core.Lifting.RuleDefinitionLift");
        var proposeRuleType = contractAssembly.GetType("NLISSN.Core.Decision.RuleDefinitionPropose");
        Assert.NotNull(markRuleType);
        Assert.NotNull(propagateRuleType);
        Assert.NotNull(liftRuleType);
        Assert.NotNull(proposeRuleType);
        Assert.Null(contractAssembly.GetType("NLISSN.Core.Pipeline.RuleDefinitionMark"));
        Assert.Null(contractAssembly.GetType("NLISSN.Core.Pipeline.RuleDefinitionPropagate"));
        Assert.Null(contractAssembly.GetType("NLISSN.Core.Pipeline.RuleDefinitionLift"));
        Assert.Null(contractAssembly.GetType("NLISSN.Core.Pipeline.RuleDefinitionPropose"));
        Assert.True(markRuleType!.IsClass);
        Assert.True(propagateRuleType!.IsClass);
        Assert.True(liftRuleType!.IsClass);
        Assert.True(proposeRuleType!.IsClass);
        Assert.NotSame(typeof(RulePipelineComposer).Assembly, implementationAssembly);
        Assert.NotSame(contractAssembly, implementationAssembly);

        Assert.True(rules.Markers.Count >= 10);
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "AtomicIdentifierNameMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "AtomicMemberAccessMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "AtomicInvocationMarkRule", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Markers, rule => rule is UnreachableMethodMarkRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is UnreferencedMethodMarkRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is ClearUnusedInterfaceImplementationRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is PrivatizeInternalOnlyPublicMethodRule);
        Assert.Contains(rules.Markers, rule => string.Equals(rule.GetType().Name, "TypeSyntaxMarkRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "ObjectCreationDeclarationPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DeclarationSymbolReferencePropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "AssignmentLeftValuePropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "DefinitionInitializerPropagationRule", StringComparison.Ordinal));
        Assert.Contains(rules.Propagators, rule => rule is LogicalExpressionPropagationRule);
        Assert.Contains(rules.Propagators, rule => string.Equals(rule.GetType().Name, "SymbolReferencePropagationRule", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Propagators, rule => rule.RuleId == "unknown.propagate.if-complete.sobj");
        Assert.True(rules.Lifters.Count >= 4);
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "ExpressionHostLiftingRule", StringComparison.Ordinal));
        Assert.Contains(rules.Lifters, rule => rule is LogicalExpressionLiftingRule);
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "IfStructureLiftingRule", StringComparison.Ordinal));
        Assert.Contains(rules.Lifters, rule => string.Equals(rule.GetType().Name, "SwitchStructureLiftingRule", StringComparison.Ordinal));
        Assert.True(rules.Proposers.Count >= 5);
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "LogicalExpressionProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "IfStructureProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ControlStructureRemovalProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DefaultRemovalProposalRule", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Proposers, rule => rule is UnreachableMethodProposalRule);
        Assert.DoesNotContain(rules.Proposers, rule => rule is UnreferencedMethodProposalRule);
        Assert.DoesNotContain(rules.Proposers, rule => rule is ClearUnusedInterfaceImplementationProposalRule);
        Assert.DoesNotContain(rules.Proposers, rule => rule is PrivatizeInternalOnlyPublicMethodProposalRule);
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "TypeSyntaxDeclarationProposalRule", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Propagators, rule => rule.RuleId == "unknown.propagate.if-complete.class");
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "MethodReturnTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "PublicMethodReturnTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ParameterProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "PrivateMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "NamedArgumentMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "OptionalParameterDefaultedMethodShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ParamsMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "PublicMethodParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "LocalFunctionParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "NamedArgumentLocalFunctionParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "OptionalParameterDefaultedLocalFunctionShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "IndexerParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "NamedArgumentIndexerParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "MethodGroupDelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "LambdaDelegateParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DelegateInvocationChainParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ExtensionReceiverNonFirstParameterShrinkProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "PublicParameterProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "InterfaceMethodProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "InterfacePropertyProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "InterfaceEventProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "InterfaceIndexerProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "DelegateProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "ExtensionReceiverProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "BaseTypeProposalRule", StringComparison.Ordinal));
        Assert.Contains(rules.Proposers, rule => string.Equals(rule.GetType().Name, "GenericTypeArgumentProposalRule", StringComparison.Ordinal));

        Assert.Contains(rules.Markers, rule => markRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Propagators, rule => propagateRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Lifters, rule => liftRuleType.IsAssignableFrom(rule.GetType()));
        Assert.Contains(rules.Proposers, rule => proposeRuleType.IsAssignableFrom(rule.GetType()));
    }

    [Fact]
    public void RulePipelineComposer_UsesFlatStageRegistrationWithoutRuleFamilyTypes()
    {
        var rules = RulePipelineTestFactory.Create();

        Assert.DoesNotContain(
          typeof(RulePipelineComposer).Assembly.GetTypes(),
          type => type.Name.EndsWith("RuleSet", StringComparison.Ordinal) &&
            (type.Name.StartsWith("Type", StringComparison.Ordinal) ||
             type.Name.StartsWith("Target", StringComparison.Ordinal)));
        Assert.Contains(rules.Markers, rule => rule is AtomicIdentifierNameMarkRule);
        Assert.Contains(rules.Markers, rule => rule is TypeSyntaxMarkRule);
        Assert.DoesNotContain(
          rules.Markers.Cast<object>()
            .Concat(rules.Propagators)
            .Concat(rules.Lifters)
            .Concat(rules.Proposers),
          rule => rule.GetType().Name.StartsWith("Delete", StringComparison.Ordinal) ||
            rule.GetType().Name.StartsWith("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void RulePipelineComposer_ExcludesOptionalMethodRulesByDefault()
    {
        var rules = RulePipelineTestFactory.Create();

        Assert.DoesNotContain(rules.Markers, rule => rule is UnreachableMethodMarkRule);
        Assert.DoesNotContain(rules.Proposers, rule => rule is UnreachableMethodProposalRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is UnreferencedMethodMarkRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is ClearUnusedInterfaceImplementationRule);
        Assert.DoesNotContain(rules.Markers, rule => rule is PrivatizeInternalOnlyPublicMethodRule);
        Assert.DoesNotContain(
          typeof(RulePipelineComposer).Assembly.GetTypes(),
          type => type.Name.Contains("RuleSet", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeFromArgs_WithoutDiffOut_SuppressesUnsafeLocalDeclarationRewrite()
    {
        var filePath = Path.Combine(_tempDirectory, "default-diff-output.cs");
        File.WriteAllText(filePath, CliInputSources.DiffWriteSource);
        var expectedPath = Path.Combine(
          Directory.GetCurrentDirectory(),
          "Build",
          "Result",
          "Diff",
          "default-diff-output.rewrite.diff");

        try
        {
            var result = CreateCommandHost().AnalyzeFromArgs(new[]
            {
              filePath,
              "--target-name",
              "s"
            });

            Assert.Empty(result.Edits);
            Assert.Null(result.DiffFilePath);
            Assert.False(File.Exists(expectedPath));
            Assert.False(File.Exists(Path.Combine(_tempDirectory, "default-diff-output.rewrite.diff")));
        }
        finally
        {
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
    }

    [Fact]
    public void Analyze_ConfigurationFile_SuppressesUnsafeLocalDeclarationRewrite()
    {
        var sourcePath = Path.Combine(_tempDirectory, "configured-input.cs");
        var configurationPath = Path.Combine(_tempDirectory, "nlissn.yml");
        File.WriteAllText(sourcePath, CliInputSources.DiffWriteSource);
        File.WriteAllText(
          configurationPath,
          """
          schemaVersion: 2
          runId: configured-input
          input:
            path: configured-input.cs
          analysis:
            targetName: s
          execution:
            maxDegreeOfParallelism: 1
          artifacts:
            root: artifacts
            diff:
              enabled: false
          """);

        var result = CreateCommandHost().Analyze(YamlConfigurationLoader.Load(configurationPath));

        Assert.Empty(result.Edits);
        Assert.Null(result.DiffFilePath);
    }

    [Fact]
    public void DefaultRulePipeline_ExposesUniqueRuleIds()
    {
        var pipeline = RulePipelineTestFactory.Create();
        var ruleIds = pipeline.Markers.Cast<IRuleDefinition>()
          .Concat(pipeline.Propagators)
          .Concat(pipeline.Lifters)
          .Concat(pipeline.Proposers)
          .Select(rule => rule.RuleId)
          .ToList();

        Assert.Equal(ruleIds.Count, ruleIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ruleIds, ruleId =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ruleId));
            Assert.DoesNotContain("-001", ruleId, StringComparison.Ordinal);
            Assert.DoesNotContain(".sobject.", ruleId, StringComparison.Ordinal);
            Assert.DoesNotContain(".class.", ruleId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RulePipelineComposer_Assembly_DoesNotExposeLegacyAtomicPropagationHelpers()
    {
        var assembly = typeof(RulePipelineComposer).Assembly;

        Assert.Null(assembly.GetType("NLISSN.Rules.PropagationState"));
        Assert.Null(assembly.GetType("NLISSN.Rules.LogicalConditionPropagationStep"));
        Assert.Null(assembly.GetType("NLISSN.Rules.SymbolReferencePropagationStep"));
    }

    [Fact]
    public void RulePipelineComposer_WhenDisabledRuleTypeProvided_FiltersMatchingTypeOnly()
    {
        var rules = RulePipelineTestFactory.Create(new[] { "AtomicMemberAccessMarkRule" });

        Assert.DoesNotContain(
          rules.Markers,
          rule => string.Equals(rule.GetType().Name, "AtomicMemberAccessMarkRule", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Markers, rule => rule is UnreachableMethodMarkRule);
        Assert.Contains(
          rules.Propagators,
          rule => string.Equals(rule.GetType().Name, "AssignmentLeftValuePropagationRule", StringComparison.Ordinal));
        Assert.NotEmpty(rules.CompileRuleGraph().Nodes);
    }

    [Fact]
    public void AnalyzeFromArgs_WhenDisabledRuleTypeProvided_DisablesOnlyMatchingType()
    {
        var host = new  CommandHost(
          RulePipelineTestFactory.Create(new[] { "AtomicMemberAccessMarkRule" }));

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
    public void RulePipelineComposer_AtomicMarkRulesHaveUniqueRuleIds()
    {
        var rules = RulePipelineTestFactory.Create();
        var deleteTargetMarkRules = GetAtomicMarkRules(rules);

        Assert.True(deleteTargetMarkRules.Count >= 10);
        Assert.Equal(
          deleteTargetMarkRules.Count,
          deleteTargetMarkRules
            .Select(rule => rule.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void PropagationContext_WhenRuleDoesNotReadStructureView_DoesNotRunStructureQuery()
    {
        var (session, _) = CreateContext("class C { void M() { } }");

        var results = PropagationEngine.ExecuteRule(
            session,
            new NoViewPropagationRule(),
            Array.Empty<MarkRecord>());

        Assert.Empty(results);
        Assert.Equal(0, session.StructureViewQueryCount);
    }

    [Fact]
    public void PropagationContext_WhenStructureViewIsRead_ReusesOneQueryResult()
    {
        var (session, root) = CreateContext("class C { void M() { } }");
        var context = session.CreatePropagationContext(
            new[] { new MarkRecord("TEST", root, null, null, "Test input.") });

        var first = context.StructureViewQuery;
        var second = context.StructureViewQuery;

        Assert.Same(first, second);
        Assert.Equal(1, session.StructureViewQueryCount);
    }

    [Fact]
    public void LiftContext_WhenRuleDoesNotReadStructureView_DoesNotRunStructureQuery()
    {
        var (session, _) = CreateContext("class C { void M() { } }");

        var results = MarkLiftingEngine.ExecuteRule(
            session,
            new NoViewLiftRule(),
            Array.Empty<MarkRecord>(),
            Array.Empty<PropagatedMarkRecord>(),
            Array.Empty<LiftedMarkRecord>());

        Assert.Empty(results);
        Assert.Equal(0, session.StructureViewQueryCount);
    }

    [Fact]
    public void LiftContext_WhenInputsAreAbsent_PreservesDisconnectedQueryStatus()
    {
        var (session, _) = CreateContext("class C { void M() { } }");
        var context = session.CreateLiftContext(
            Array.Empty<MarkRecord>(),
            Array.Empty<PropagatedMarkRecord>());

        var query = context.StructureViewQuery;

        Assert.Equal(NLCPG.Analysis.CpgQueryStatus.Disconnected, query.Status);
        Assert.Null(query.View);
        Assert.Equal(0, session.StructureViewQueryCount);
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

    private static  CommandHost CreateCommandHost()
    {
        return new  CommandHost(RulePipelineTestFactory.Create());
    }

    [Fact]
    public void AnalysisSession_ExpressionTopology_ReusesPathAndOperationCache()
    {
        var (session, root) = CreateContext("class C { bool M(bool a, bool b) => a && b; }");
        var seed = root.DescendantNodes().OfType<IdentifierNameSyntax>()
          .Single(node => node.Identifier.ValueText == "a");

        var first = session.ResolveExpressionTopology(seed);
        var second = session.ResolveExpressionTopology(seed);
        var metrics = session.GetExpressionTopologyMetrics();

        Assert.Same(first, second);
        Assert.Equal(1, metrics.AnalyzeCount);
        Assert.Equal(1, metrics.CachedPathCount);
        Assert.True(metrics.OperationCacheMisses >= 1);
    }

    [Fact]
    public void AnalysisSession_ExpressionTopology_UserDefinedBinaryOperator_StopsConservatively()
    {
        var (session, root) = CreateContext(
          """
          struct Flag
          {
            public static Flag operator &(Flag left, Flag right) => left;
            bool M(Flag left, Flag right) => (left & right) is not null;
          }
          """);
        var seed = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
          .Single(binary => binary.IsKind(SyntaxKind.BitwiseAndExpression)).Left;

        var path = session.ResolveExpressionTopology(seed);

        var step = Assert.Single(path.Steps);
        Assert.Equal(NLISSN.Core.Analysis.ExpressionPropagation.ExpressionPropagationMode.Stop, step.Mode);
        Assert.True(step.OperatorFacts.HasOperatorMethod);
    }

    private static (AnalysisSession Context, SyntaxNode Root) CreateContext(string source, string? targetName = null,  AnalysisRuntime? runtime = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "component-test.cs");
        var root = tree.GetRoot();
        var compilation = CreateCompilation(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var graph = new NLCPG.Builder.NLCPGBuilder().BuildFromSource(source, "component-test.cs");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        return (new AnalysisSession(
          new CpgAnalysisContext(graph, semanticModel, root),
          AnalysisLegacyOptionsTestExtensions.CreateSettings(options),
          runtime: runtime), root);
    }

    private static  AnalysisRuntime CreateParallelRuntime(RecordingConcurrencyPool scheduler)
    {
        return new  AnalysisRuntime(
          new RoslynPrototypeExecutionOptions(
            4,
            EnableGroupParallelism: true),
          new  AnalysisEpoch(0, 0, 0),
          scheduler);
    }

    private static TCache GetCompilationCache<TCache>( AnalysisRuntime runtime, Compilation compilation, Func<Compilation, TCache> factory)
      where TCache : class
    {
        var method = typeof( AnalysisRuntime)
          .GetMethod("GetOrCreateCompilationCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
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

    private static IReadOnlyList<RuleDefinitionMark> GetAtomicMarkRules( RulePipeline? rules = null)
    {
        var markerRules = rules?.Markers ?? RulePipelineTestFactory.Create().Markers;
        return markerRules
          .Where(rule => rule.RuleId.StartsWith("mark.target.", StringComparison.Ordinal))
          .ToList();
    }

    private static IReadOnlyList<RuleDefinitionMark> GetDeclarationMarkRules( RulePipeline? rules = null)
    {
        var markerRules = rules?.Markers ?? RulePipelineTestFactory.Create().Markers;
        return markerRules
          .Where(rule => rule.RuleId.StartsWith("mark.type.", StringComparison.Ordinal))
          .ToList();
    }

    private sealed class DuplicateSeedRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-DUP-SEED";

        public override string Name { get; } = "Emit duplicated seed marks";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
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

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            _ = root;
            yield break;
        }
    }

    private sealed record TestCompilationCacheA(string Value);

    private sealed record TestCompilationCacheB((int TreeCount, int StableId) Value);

    private sealed class RecordingConcurrencyPool : IConcurrencyPool
    {
        private readonly IConcurrencyPool _inner = new BoundedConcurrencyPool();

        public int InvocationCount { get; private set; }

        public List<int> ItemCounts { get; } = new();

        public List<int> DependencyGraphMaxDegrees { get; } = new();

        public List<int> SelectOrderedMaxDegrees { get; } = new();

        public void Reset()
        {
            InvocationCount = 0;
            ItemCounts.Clear();
            DependencyGraphMaxDegrees.Clear();
            SelectOrderedMaxDegrees.Clear();
        }

        public async Task<IReadOnlyList<TResult>> SelectOrderedAsync<TResult>(int itemCount, int maxDegreeOfParallelism, Func<int, CancellationToken, Task<TResult>> workItem, CancellationToken cancellationToken)
        {
            InvocationCount++;
            ItemCounts.Add(itemCount);
            SelectOrderedMaxDegrees.Add(maxDegreeOfParallelism);

            var results = new TResult[itemCount];
            for (var index = 0; index < itemCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = await workItem(index, cancellationToken);
            }

            return results;
        }

        public Task<IReadOnlyList<TResult>> SelectOrderedAsync<TSource, TResult>(IReadOnlyList<TSource> sources, int maxDegreeOfParallelism, Func<TSource, int, CancellationToken, Task<TResult>> workItem, CancellationToken cancellationToken = default)
        {
            return _inner.SelectOrderedAsync(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public Task<IReadOnlyList<TResult>> SelectCpuBoundOrdered<TSource, TResult>(IReadOnlyList<TSource> sources, int maxDegreeOfParallelism, Func<TSource, int, CancellationToken, TResult> workItem, CancellationToken cancellationToken = default)
        {
            return _inner.SelectCpuBoundOrdered(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public void CommitOrdered<TSource, TResult>(IReadOnlyList<TSource> sources, ConcurrencyWindowOptions options, Func<TSource, int, TResult> workItem, Action<TResult, int> commit, Func<TResult, int>? retainedRecordCount = null, CancellationToken cancellationToken = default)
        {
            _inner.CommitOrdered(sources, options, workItem, commit, retainedRecordCount, cancellationToken);
        }

        public void CommitTwoStageOrdered<TSource, TCollected, TPrepared, TResult>(IReadOnlyList<TSource> sources, ConcurrencyWindowOptions options, Func<TSource, int, TCollected> collect, Func<TCollected, int, TPrepared> prepare, Func<TPrepared, int, TResult> solve, Action<TResult, int> commit, Func<TCollected, int>? collectedRetainedRecordCount = null, Func<TResult, int>? resultRetainedRecordCount = null, CancellationToken cancellationToken = default)
        {
            _inner.CommitTwoStageOrdered(sources, options, collect, prepare, solve, commit, collectedRetainedRecordCount, resultRetainedRecordCount, cancellationToken);
        }

        public Task ForEachAsync<TSource>(IReadOnlyList<TSource> sources, int maxDegreeOfParallelism, Func<TSource, int, CancellationToken, Task> workItem, CancellationToken cancellationToken = default)
        {
            return _inner.ForEachAsync(sources, maxDegreeOfParallelism, workItem, cancellationToken);
        }

        public Task<DependencyExecutionResult<TNode, TResult>> RunDependencyGraphAsync<TNode, TResult>(IReadOnlyList<DependencyWorkItem<TNode, TResult>> workItems, int maxDegreeOfParallelism, IComparer<TNode> readyOrder, CancellationToken cancellationToken = default)
            where TNode : notnull
        {
            DependencyGraphMaxDegrees.Add(maxDegreeOfParallelism);
            return _inner.RunDependencyGraphAsync(workItems, maxDegreeOfParallelism, readyOrder, cancellationToken);
        }
    }

    private sealed class RuntimeAwareMarkRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-RUNTIME-MARK";

        public override string Name { get; } = "Emit a mark only when runtime options flow into the direct Analyze overload.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            var executionOptions = context.Runtime.ExecutionOptions;
            if (executionOptions.EffectiveMaxDegreeOfParallelism != 3 ||
                executionOptions.EnableHelperParallelism ||
                !executionOptions.EnableGroupParallelism)
            {
                yield break;
            }

            var typeDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            yield return new MarkRecord(
              RuleId,
              typeDeclaration,
              null,
              null,
              $"mdop={executionOptions.EffectiveMaxDegreeOfParallelism};group={executionOptions.EnableGroupParallelism};helper={executionOptions.EnableHelperParallelism}");
        }
    }

    private sealed class ParallelTypeMarkRule : RuleDefinitionMark
    {
        private readonly string _typeName;
        public ParallelTypeMarkRule(string ruleId, string typeName)
        {
            RuleId = ruleId;
            _typeName = typeName;
        }

        public override string RuleId { get; }


        public override string Name { get; } = "Mark a named class for parallel scheduler tests.";

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(new[] { SyntaxKind.MethodDeclaration }, CreateSemanticTag(RuleId));

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.MethodDeclaration };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            var typeDeclaration = root.DescendantNodes()
              .OfType<ClassDeclarationSyntax>()
              .Single(candidate => string.Equals(candidate.Identifier.ValueText, _typeName, StringComparison.Ordinal));
            var method = typeDeclaration.Members.OfType<MethodDeclarationSyntax>().Single();
            yield return new MarkRecord(
              RuleId,
              method,
              null,
              null,
              $"Seed {_typeName}",
              SemanticTag: CreateSemanticTag(RuleId));
        }
    }

    private sealed class ConcurrentTypeMarkRule : RuleDefinitionMark
    {
        private readonly string _typeName;
        private readonly ConcurrentRuleProbe _probe;

        public ConcurrentTypeMarkRule(string ruleId, string typeName, ConcurrentRuleProbe probe)
        {
            RuleId = ruleId;
            _typeName = typeName;
            _probe = probe;
        }

        public override string RuleId { get; }


        public override string Name { get; } = "Mark a named class while measuring rule concurrency.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            _probe.Enter();
            try
            {
                var typeDeclaration = root.DescendantNodes()
                  .OfType<ClassDeclarationSyntax>()
                  .Single(candidate => string.Equals(candidate.Identifier.ValueText, _typeName, StringComparison.Ordinal));
                yield return new MarkRecord(RuleId, typeDeclaration, null, null, $"Seed {_typeName}");
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
        private readonly TimeSpan _releaseWait;
        private int _activeRuleCount;
        private int _peakActiveRuleCount;

        public ConcurrentRuleProbe(int expectedConcurrentRules, TimeSpan? releaseWait = null)
        {
            _expectedConcurrentRules = expectedConcurrentRules;
            _releaseWait = releaseWait ?? TimeSpan.FromSeconds(2);
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

            _release.Wait(_releaseWait);
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
        private static readonly RuleSemanticTag IfSemanticTag = new("Test.Propagation.If");

        public override string RuleId { get; } = "test.duplicate-propagation";


        public override string Name { get; } = "Emit duplicated propagated marks";

        public override RuleConsumesContract Consumes => CreateTargetAtomicConsumes();

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(new[] { SyntaxKind.IfStatement }, IfSemanticTag);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var ifStatement = context.Root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            var source = Assert.Single(seedMarks);
            var propagatedMark = new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(
                RuleId,
                ifStatement,
                null,
                null,
                "lift to if",
                SemanticTag: IfSemanticTag),
              source,
              1);
            yield return propagatedMark;
            yield return propagatedMark;
        }

    }

    private sealed class RepeatedMemberAccessPropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-FIXED-POINT-SELF-001";

        public override string Name { get; } = "Propagate each member access to its direct member-access parent.";

        public override RuleConsumesContract Consumes => CreateTargetAtomicConsumes();

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(
            new[] { SyntaxKind.SimpleMemberAccessExpression },
            RuleFactKind.TargetExpression);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          IPropagationRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            foreach (var sourceMark in seedMarks)
            {
                var next = context.Root.DescendantNodes()
                  .OfType<MemberAccessExpressionSyntax>()
                  .SingleOrDefault(candidate => candidate.Expression.Span == sourceMark.SyntaxNode.Span);
                if (next is null)
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  new MarkRecord(
                    RuleId,
                    next,
                    null,
                    null,
                    "Propagate to the direct member-access parent.",
                    FactKind: RuleFactKind.TargetExpression),
                  sourceMark,
                  0);
            }
        }
    }

    private sealed class FirstFeedbackMemberAccessPropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag FeedbackTag = new("Test.FixedPoint.Feedback");

        public override string RuleId { get; } = "TEST-FIXED-POINT-FIRST-001";

        public override string Name { get; } = "Move a target member access into the feedback channel.";

        public override RuleConsumesContract Consumes => CreateTargetAtomicConsumes();

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(new[] { SyntaxKind.SimpleMemberAccessExpression }, FeedbackTag);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          IPropagationRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            foreach (var sourceMark in seedMarks)
            {
                var next = context.Root.DescendantNodes()
                  .OfType<MemberAccessExpressionSyntax>()
                  .SingleOrDefault(candidate => candidate.Expression.Span == sourceMark.SyntaxNode.Span);
                if (next is null)
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  new MarkRecord(
                    RuleId,
                    next,
                    null,
                    null,
                    "Emit the feedback fact.",
                    SemanticTag: FeedbackTag),
                  sourceMark,
                  0);
            }
        }
    }

    private sealed class SecondFeedbackMemberAccessPropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag FeedbackTag = new("Test.FixedPoint.Feedback");

        public override string RuleId { get; } = "TEST-FIXED-POINT-SECOND-001";

        public override string Name { get; } = "Return a feedback member access to the target channel.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.SimpleMemberAccessExpression }, FeedbackTag);

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(
            new[] { SyntaxKind.SimpleMemberAccessExpression },
            RuleFactKind.TargetExpression);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          IPropagationRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            foreach (var sourceMark in seedMarks)
            {
                var next = context.Root.DescendantNodes()
                  .OfType<MemberAccessExpressionSyntax>()
                  .SingleOrDefault(candidate => candidate.Expression.Span == sourceMark.SyntaxNode.Span);
                if (next is null)
                {
                    continue;
                }

                yield return new PropagatedMarkRecord(
                  RuleId,
                  new MarkRecord(
                    RuleId,
                    next,
                    null,
                    null,
                    "Return the fact to the target channel.",
                    FactKind: RuleFactKind.TargetExpression),
                  sourceMark,
                  0);
            }
        }
    }

    private sealed class FixedPointSeedMarkRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-FIXED-POINT-SEED-MARK-001";

        public override string Name { get; } = "Mark the first member access for fixed-point analysis.";

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(
            new[] { SyntaxKind.SimpleMemberAccessExpression },
            RuleFactKind.TargetExpression);

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            var seed = root.DescendantNodes()
              .OfType<MemberAccessExpressionSyntax>()
              .Single(candidate => string.Equals(candidate.ToString(), "state.Root", StringComparison.Ordinal));
            yield return new MarkRecord(
              RuleId,
              seed,
              null,
              null,
              "Seed the first member access.",
              FactKind: RuleFactKind.TargetExpression);
        }
    }

    private sealed class FixedPointLiftRule : RuleDefinitionLift
    {
        private static readonly RuleSemanticTag LiftTag = new("Test.FixedPoint.Lift");

        public bool SawConvergedFacts { get; private set; }

        public override string RuleId { get; } = "TEST-FIXED-POINT-LIFT-001";

        public override string Name { get; } = "Lift the final fixed-point fact.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(
            new[] { SyntaxKind.SimpleMemberAccessExpression },
            RuleFactKind.TargetExpression);

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(new[] { SyntaxKind.SimpleMemberAccessExpression }, LiftTag);

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<LiftedMarkRecord> Lift(
          ILiftRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            var finalFact = Assert.Single(propagatedMarks, mark => mark.Depth == 2);
            var seedMark = Assert.Single(seedMarks);
            SawConvergedFacts = propagatedMarks.Count == 2;
            yield return new LiftedMarkRecord(
              RuleId,
              new MarkRecord(
                RuleId,
                finalFact.Mark.SyntaxNode,
                null,
                null,
                "Lift the converged second-hop fact.",
                SemanticTag: LiftTag),
              seedMark,
              finalFact.Depth);
        }
    }

    private sealed class FixedPointProposalRule : RuleDefinitionPropose
    {
        private static readonly RuleSemanticTag LiftTag = new("Test.FixedPoint.Lift");

        public bool SawLiftedFact { get; private set; }

        public override string RuleId { get; } = "TEST-FIXED-POINT-PROPOSE-001";

        public override string Name { get; } = "Consume a fixed-point lift.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.SimpleMemberAccessExpression }, LiftTag);

        public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
          new[] { SyntaxKind.SimpleMemberAccessExpression };

        public override IEnumerable<DecisionUnit> Propose(
          IProposeRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks,
          IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
          IReadOnlyList<LiftedMarkRecord> liftedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            var lifted = Assert.Single(liftedMarks);
            SawLiftedFact = string.Equals(
              lifted.Mark.SyntaxNode.ToString(),
              "state.Root.Next.Next",
              StringComparison.Ordinal);
            yield break;
        }
    }

    private sealed class DefinitionLeftValuePropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag DeclaratorSemanticTag = new("Test.Chain.Declarator");

        public override string RuleId { get; } = "TEST-CHAIN-DECL-001";


        public override string Name { get; } = "Propagate initializer marks to declarators";

        public override RuleConsumesContract Consumes => CreateTargetAtomicConsumes();

        public override RuleProducesContract Produces =>
          CreateSyntaxProduces(new[] { SyntaxKind.VariableDeclarator }, DeclaratorSemanticTag);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.VariableDeclarator };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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
                    "Initializer seed is propagated to its declarator.",
                    SemanticTag: DeclaratorSemanticTag),
                  seedMark,
                  1);
            }
        }
    }

    private sealed class LocalReferenceFromDeclaratorPropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag DeclaratorSemanticTag = new("Test.Chain.Declarator");

        public override string RuleId { get; } = "TEST-CHAIN-REF-001";


        public override string Name { get; } = "Propagate declarator marks to later local references";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.VariableDeclarator }, DeclaratorSemanticTag);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IdentifierName };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
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

    private sealed class IfStatementVisibilityPropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag IfSemanticTag = new("Test.Propagation.If");

        public override string RuleId { get; } = "TEST-PROP-VISIBILITY-001";


        public override string Name { get; } = "Propagate only when one if statement is visible.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.IfStatement }, IfSemanticTag);

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.ReturnStatement };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var ifMarks = seedMarks.Where(mark => mark.SyntaxNode is IfStatementSyntax).ToArray();
            var ifMark = Assert.Single(ifMarks);

            var returnStatement = context.Root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            yield return new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, returnStatement, null, null, "A single propagated if statement is visible."),
              ifMark,
              1);
        }
    }

    private sealed class MethodPropagationRule : RuleDefinitionPropagate
    {
        private readonly string _seedRuleId;

        public MethodPropagationRule(string ruleId, string seedRuleId)
        {
            RuleId = ruleId;
            _seedRuleId = seedRuleId;
        }

        public override string RuleId { get; }


        public override string Name { get; } = "Propagate a class seed to its method for scheduler tests.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.MethodDeclaration }, CreateSemanticTag(_seedRuleId));

        public override RuleInputCardinality InputCardinality => RuleInputCardinality.ExactlyOne;

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.MethodDeclaration };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            var seedMark = Assert.Single(seedMarks);
            var method = Assert.IsType<MethodDeclarationSyntax>(seedMark.SyntaxNode);
            var typeDeclaration = Assert.IsType<ClassDeclarationSyntax>(method.Parent);
            yield return new PropagatedMarkRecord(
              RuleId,
              new MarkRecord(RuleId, method, null, null, $"Propagate {typeDeclaration.Identifier.ValueText}"),
              seedMark,
              1);
        }
    }

    private sealed class ViewAwarePropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-VIEW-PROP-001";


        public override string Name { get; } = "Require a rule-scoped structure view during propagation";

        public override RuleConsumesContract Consumes => CreateTargetAtomicConsumes();

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.IfStatement };

        public override IEnumerable<PropagatedMarkRecord> Propagate(IPropagationRuleContext context, IReadOnlyList<MarkRecord> seedMarks)
        {
            var query = context.StructureViewQuery;
            Assert.Equal(NLCPG.Analysis.CpgQueryStatus.Complete, query.Status);
            var structureView = Assert.IsType<NLCPGStructureView>(query.View);
            var viewNodeIds = structureView.Nodes.Select(node => node.NodeId).ToHashSet();
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
        private static readonly RuleSemanticTag IfSemanticTag = new("Test.Propagation.If");

        public override string RuleId { get; } = "TEST-VIEW-LIFT-001";


        public override string Name { get; } = "Require a rule-scoped structure view during lifting";

        public override RuleConsumesContract Consumes => new(new[]
        {
            new RuleConsumedSyntax(
                ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds,
                RuleFactKind.TargetExpression),
            new RuleConsumedSyntax(new[] { SyntaxKind.IfStatement }, IfSemanticTag)
        });

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
            new[] { SyntaxKind.ReturnStatement };

        public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            var query = context.StructureViewQuery;
            Assert.Equal(NLCPG.Analysis.CpgQueryStatus.Complete, query.Status);
            var structureView = Assert.IsType<NLCPGStructureView>(query.View);
            var viewNodeIds = structureView.Nodes.Select(node => node.NodeId).ToHashSet();
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

    private sealed class NoViewPropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-NO-VIEW-PROP-001";

        public override string Name { get; } = "Does not read a structure view.";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
          Array.Empty<SyntaxKind>();

        public override IEnumerable<PropagatedMarkRecord> Propagate(
          IPropagationRuleContext context,
          IReadOnlyList<MarkRecord> seedMarks)
        {
            _ = context;
            _ = seedMarks;
            yield break;
        }
    }

    private sealed class NoViewLiftRule : RuleDefinitionLift
    {
        public override string RuleId { get; } = "TEST-NO-VIEW-LIFT-001";

        public override string Name { get; } = "Does not read a structure view.";

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
            Array.Empty<SyntaxKind>();

        public override IEnumerable<LiftedMarkRecord> Lift(
            ILiftRuleContext context,
            IReadOnlyList<MarkRecord> seedMarks,
            IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = propagatedMarks;
            yield break;
        }
    }

    private sealed class NamespaceLiftRule : RuleDefinitionLift
    {
        private readonly string _seedRuleId;

        public NamespaceLiftRule(string ruleId, string seedRuleId)
        {
            RuleId = ruleId;
            _seedRuleId = seedRuleId;
        }

        public override string RuleId { get; }


        public override string Name { get; } = "Lift a class seed to its namespace for scheduler tests.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.MethodDeclaration }, CreateSemanticTag(_seedRuleId));

        public override RuleInputCardinality InputCardinality => RuleInputCardinality.ExactlyOne;

        public override IReadOnlyList<SyntaxKind> AllowedLiftNodeKinds { get; } =
            new[] { SyntaxKind.FileScopedNamespaceDeclaration };

        public override IEnumerable<LiftedMarkRecord> Lift(ILiftRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
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

    private sealed class DeclarationDecisionRule : RuleDefinitionPropose
    {
        private readonly string _seedRuleId;

        public DeclarationDecisionRule(string ruleId, string seedRuleId)
        {
            RuleId = ruleId;
            _seedRuleId = seedRuleId;
        }

        public override string RuleId { get; }


        public override string Name { get; } = "Create a delete decision for scheduler tests.";

        public override RuleConsumesContract Consumes =>
          CreateSyntaxConsumes(new[] { SyntaxKind.MethodDeclaration }, CreateSemanticTag(_seedRuleId));

        public override RuleInputCardinality InputCardinality => RuleInputCardinality.ExactlyOne;

        public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
            new[] { SyntaxKind.ClassDeclaration };

        public override IEnumerable<DecisionUnit> Propose(IProposeRuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<LiftedMarkRecord> liftedMarks)
        {
            _ = context;
            _ = propagatedMarks;
            _ = liftedMarks;
            var seedMark = Assert.Single(seedMarks);
            var typeDeclaration = seedMark.SyntaxNode.Ancestors().OfType<ClassDeclarationSyntax>().Single();
            var fragment = DecisionCpgFactory.CreateFragment(
              $"fragment:{RuleId}",
              typeDeclaration,
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
              DecisionCpgFactory.CreateSyntaxBindings((fragment, typeDeclaration)),
              reason: $"Delete {RuleId}");
        }
    }

    private static RuleSemanticTag CreateSemanticTag(string ruleId)
    {
        return new RuleSemanticTag($"Test.Parallel.{ruleId}");
    }

    private static RuleProducesContract CreateSyntaxProduces(
      IReadOnlyList<SyntaxKind> syntaxKinds,
      RuleSemanticTag semanticTag)
    {
        return new RuleProducesContract(new[]
        {
            new RuleProducedSyntax(syntaxKinds, semanticTag)
        });
    }

    private static RuleProducesContract CreateSyntaxProduces(
      IReadOnlyList<SyntaxKind> syntaxKinds,
      RuleFactKind factKind)
    {
        return new RuleProducesContract(new[]
        {
            new RuleProducedSyntax(syntaxKinds, factKind)
        });
    }

    private static RuleConsumesContract CreateSyntaxConsumes(
      IReadOnlyList<SyntaxKind> syntaxKinds,
      RuleSemanticTag semanticTag)
    {
        return new RuleConsumesContract(new[]
        {
            new RuleConsumedSyntax(syntaxKinds, semanticTag)
        });
    }

    private static RuleConsumesContract CreateSyntaxConsumes(
      IReadOnlyList<SyntaxKind> syntaxKinds,
      RuleFactKind factKind)
    {
        return new RuleConsumesContract(new[]
        {
            new RuleConsumedSyntax(syntaxKinds, factKind)
        });
    }

    private static RuleConsumesContract CreateTargetAtomicConsumes()
    {
        return CreateSyntaxConsumes(
          ExpressionFlowPropagationRuleBase.TargetExpressionInputNodeKinds,
          RuleFactKind.TargetExpression);
    }

    private static void AssertEquivalentAnalysisResults(PrototypeAnalysisResult expected, PrototypeAnalysisResult actual)
    {
        Assert.Equal(expected.SeedMarks.Count, actual.SeedMarks.Count);
        Assert.Equal(expected.PropagatedMarks.Count, actual.PropagatedMarks.Count);
        Assert.Equal(expected.LiftedMarks.Count, actual.LiftedMarks.Count);
        Assert.Equal(expected.Decisions.Count, actual.Decisions.Count);
        Assert.Equal(expected.Edits.Count, actual.Edits.Count);
        Assert.Equal(expected.RewrittenSource, actual.RewrittenSource);
        Assert.Equal(expected.Diff.ToString(), actual.Diff.ToString());
        Assert.Equal(BuildMarkKeys(expected.SeedMarks), BuildMarkKeys(actual.SeedMarks));
        Assert.Equal(
          BuildPropagatedMarkKeys(expected.PropagatedMarks),
          BuildPropagatedMarkKeys(actual.PropagatedMarks));
        Assert.Equal(
          BuildLiftedMarkKeys(expected.LiftedMarks),
          BuildLiftedMarkKeys(actual.LiftedMarks));
        Assert.Equal(
          System.Text.Json.JsonSerializer.Serialize(expected.Evidence),
          System.Text.Json.JsonSerializer.Serialize(actual.Evidence));

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
            mark.PrimaryGraphNode!.NodeId))
          .ToList();
    }

    private static IReadOnlyList<string> BuildPropagatedMarkKeys(
      IReadOnlyList<PropagatedMarkRecord> marks) =>
      marks.Select(mark => string.Join(
        "|",
        mark.RuleId,
        mark.Mark.SyntaxNode.SyntaxTree.FilePath,
        mark.Mark.SyntaxNode.SpanStart,
        mark.Mark.SyntaxNode.Span.Length,
        mark.Mark.SyntaxNode.RawKind,
        mark.Mark.FactKind is { } factKind
          ? factKind.ToString()
          : mark.Mark.SemanticTag?.Value,
        mark.Depth,
        mark.Payload is NLISSN.Core.Analysis.ExpressionPropagation.ExpressionTopologyPayload payload
          ? payload.StepIndex
          : -1)).ToList();

    private static IReadOnlyList<string> BuildLiftedMarkKeys(IReadOnlyList<LiftedMarkRecord> marks) =>
      marks.Select(mark => string.Join(
        "|",
        mark.RuleId,
        mark.Mark.SyntaxNode.SyntaxTree.FilePath,
        mark.Mark.SyntaxNode.SpanStart,
        mark.Mark.SyntaxNode.Span.Length,
        mark.Mark.SyntaxNode.RawKind,
        mark.Mark.FactKind is { } factKind
          ? factKind.ToString()
          : mark.Mark.SemanticTag?.Value,
        mark.Depth)).ToList();

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

    private static long ExtractNamedLongField(string line, string fieldName)
    {
        var prefix = $"{fieldName}=";
        var startIndex = line.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing field '{fieldName}' in log line: {line}");
        startIndex += prefix.Length;
        var endIndex = line.IndexOf(' ', startIndex);
        var rawValue = endIndex >= 0
          ? line[startIndex..endIndex]
          : line[startIndex..];
        return long.Parse(rawValue);
    }

    private sealed class FileDelayMarkRule : RuleDefinitionMark
    {
        private readonly TaskCompletionSource _fastFileEntered = new(
          TaskCreationOptions.RunContinuationsAsynchronously);

        public override string RuleId { get; } = "TEST-DIRECTORY-PUBLICATION-BACKLOG-001";


        public override string Name { get; } = "Delay one file to force directory publication backlog telemetry.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
          new[] { SyntaxKind.CompilationUnit };

        public Task FastFileEntered => _fastFileEntered.Task;

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            var fileName = Path.GetFileName(root.SyntaxTree.FilePath);
            if (string.Equals(fileName, "A.Slow.cs", StringComparison.Ordinal))
            {
                _fastFileEntered.Task.Wait(TimeSpan.FromSeconds(5));
                Thread.Sleep(100);
            }
            else
            {
                _fastFileEntered.TrySetResult();
            }

            yield break;
        }
    }
}

