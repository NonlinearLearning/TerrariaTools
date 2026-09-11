using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Builder;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Application;
using NLISSN.Core.Analysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests.Decision;

public sealed class FlowSummaryDeletionSafetyTests
{
    private static readonly RuleSemanticTag InvocationSemanticTag = new("Test.FlowSummary.Invocation");
    private static readonly RuleSemanticTag SummarySemanticTag = new("Test.FlowSummary.Resolved");

    public static IEnumerable<object[]> SummaryResolutionCases()
    {
        yield return new object[] { "resolved", ResolvedCallFlowStatus.Resolved, true, string.Empty };
        yield return new object[] { "unknown", ResolvedCallFlowStatus.Unknown, false, "No summary is configured." };
        yield return new object[] { "blocked", ResolvedCallFlowStatus.Blocked, false, "The summary blocks this endpoint pair." };
        yield return new object[] { "truncated", ResolvedCallFlowStatus.Blocked, false, "Summary traversal was truncated." };
    }

    [Fact]
    public void PropagationContext_ResolveCallFlow_RecordsSummaryEvidence()
    {
        const string source = "class C { string M(object value) => value.ToString(); }";
        var tree = CSharpSyntaxTree.ParseText(source, path: "flow-summary-context.cs");
        var compilation = CSharpCompilation.Create(
            "FlowSummaryContextTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var root = tree.GetRoot();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = Assert.IsAssignableFrom<IInvocationOperation>(
            compilation.GetSemanticModel(tree).GetOperation(invocation));
        var session = new AnalysisSession(
            new CpgAnalysisContext(
                new NLCPGBuilder().BuildFromSource(source, "flow-summary-context.cs"),
                compilation.GetSemanticModel(tree),
                root,
                CallFlowResolver: new ResolvedFlowResolver()),
            AnalysisLegacyOptionsTestExtensions.CreateSettings(new Dictionary<string, string>()));

        var result = session.CreatePropagationContext(Array.Empty<MarkRecord>()).ResolveCallFlow(
            operation,
            FlowSummaryEndpoint.Receiver,
            FlowSummaryEndpoint.Return);
        var evidence = session.Evidence.Complete(Array.Empty<RuleDecision>());

        Assert.Equal(ResolvedCallFlowStatus.Resolved, result.Status);
        Assert.Contains(evidence.Graph.Nodes, node => node.Kind == AnalysisEvidenceKind.UsesSummary);
    }

    [Fact]
    public void Propose_ParameterToReturnSummaryFact_DoesNotCreateDeleteDecision()
    {
        var invocation = CSharpSyntaxTree.ParseText("class C { void M() { External(value); } }")
            .GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var seed = MarkRecordFactory.Create("TEST-SEED", invocation, "Seed invocation.");
        var mapping = new FlowSummaryMapping(
            FlowSummaryEndpoint.Parameter(0),
            FlowSummaryEndpoint.Return,
            FlowSummaryMappingKind.Explicit);
        var resolved = new ResolvedCallFlow(
            ResolvedCallFlowStatus.Resolved,
            FlowSummaryResolution.Project,
            new FlowSummaryMethodKey(
                "Demo",
                "Demo.External",
                "External",
                0,
                1,
                new[] { "value" },
                new[] { "System.Object" }),
            mapping,
            null);
        var propagated = new PropagatedMarkRecord(
            "TEST-SUMMARY",
            MarkRecordFactory.Create("TEST-SUMMARY", invocation, "External summary fact."),
            seed,
            1,
            new ExternalSummaryFlowPayload(resolved));

        var decisions = new DefaultRemovalProposalRule().Propose(
            null!,
            new[] { seed },
            new[] { propagated },
            Array.Empty<NLISSN.Core.Lifting.LiftedMarkRecord>()).ToList();

        Assert.Empty(decisions);
    }

    [Fact]
    public void ProductionSummaryRule_EmitsReceiverFactWithProvenanceWithoutDeleteDecision()
    {
        const string source = "class C { string M(object value) => value.ToString(); }";
        var application = new ApplicationService(
            CreateProductionSummaryPipeline(),
            new ResolvedFlowResolver());

        var result = application.Analyze(
            source,
            "production-summary-receiver.cs",
            new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        var propagated = Assert.Single(result.PropagatedMarks);
        var payload = Assert.IsType<ExternalSummaryFlowPayload>(propagated.Payload);
        Assert.True(payload.Flow.IsResolved);
        Assert.True(payload.IsInputToReturn);
        Assert.Equal(FlowSummaryEndpoint.Receiver, payload.SourceEndpoint);
        Assert.Equal("value", payload.SourceSyntax!.ToString());
        Assert.Equal("value", propagated.Mark.SyntaxNode.ToString());
        Assert.Empty(result.Decisions);
        Assert.Contains(
            result.Evidence!.Nodes,
            node => node.Kind == AnalysisEvidenceKind.UsesSummary);
    }

    [Fact]
    public void ProductionSummaryRule_UnknownFlowProtectsInputFromDefaultDeletion()
    {
        const string source = "class C { string M(object value) => value.ToString(); }";
        var application = new ApplicationService(
            CreateProductionSummaryPipeline(),
            new StatusFlowResolver(ResolvedCallFlowStatus.Unknown, "No summary is configured."));

        var result = application.Analyze(
            source,
            "production-summary-unknown.cs",
            new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        var propagated = Assert.Single(result.PropagatedMarks);
        var payload = Assert.IsType<ExternalSummaryFlowPayload>(propagated.Payload);
        Assert.Equal(ResolvedCallFlowStatus.Unknown, payload.Flow.Status);
        Assert.True(payload.ProtectsInput);
        Assert.Empty(result.Decisions);
    }

    [Theory]
    [MemberData(nameof(SummaryResolutionCases))]
    public void Analyze_WhenSummaryFlowResolutionVaries_OnlyResolvedFlowCreatesDeletionCandidate(
        string caseName,
        ResolvedCallFlowStatus status,
        bool expectedCandidate,
        string? rejectionReason)
    {
        const string source = "class C { void External(object value) { } void M(object value) { External(value); } }";
        var pipeline = new RulePipeline(
            new RuleDefinitionMark[] { new InvocationMarkRule() },
            new RuleDefinitionPropagate[] { new SummaryPayloadPropagationRule() },
            Array.Empty<NLISSN.Core.Lifting.RuleDefinitionLift>(),
            new RuleDefinitionPropose[] { new SummaryPayloadProposalRule() });
        var application = new ApplicationService(
            pipeline,
            new StatusFlowResolver(status, rejectionReason));

        var result = application.Analyze(
            source,
            $"summary-{caseName}.cs",
            new Dictionary<string, string> { ["skip-rewrite"] = "true" });

        Assert.Equal(expectedCandidate, result.PropagatedMarks.Count == 1);
        Assert.Equal(expectedCandidate, result.Decisions.Count == 1);
        Assert.All(
            result.PropagatedMarks,
            mark => Assert.IsType<ExternalSummaryFlowPayload>(mark.Payload));
    }

    private sealed class ResolvedFlowResolver : ICallFlowResolver
    {
        public ResolvedCallFlow Resolve(
            IInvocationOperation invocation,
            FlowSummaryEndpoint source,
            FlowSummaryEndpoint target)
        {
            return new ResolvedCallFlow(
                ResolvedCallFlowStatus.Resolved,
                FlowSummaryResolution.Project,
                FlowSummaryMethodKey.From(invocation.TargetMethod),
                new FlowSummaryMapping(source, target, FlowSummaryMappingKind.Explicit),
                null);
        }

        public IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation) =>
            new[] { Resolve(invocation, FlowSummaryEndpoint.Receiver, FlowSummaryEndpoint.Return) };
    }

    private sealed class StatusFlowResolver : ICallFlowResolver
    {
        private readonly ResolvedCallFlowStatus _status;
        private readonly string? _rejectionReason;

        public StatusFlowResolver(ResolvedCallFlowStatus status, string? rejectionReason)
        {
            _status = status;
            _rejectionReason = rejectionReason;
        }

        public ResolvedCallFlow Resolve(
            IInvocationOperation invocation,
            FlowSummaryEndpoint source,
            FlowSummaryEndpoint target)
        {
            var isResolved = _status == ResolvedCallFlowStatus.Resolved;
            return new ResolvedCallFlow(
                _status,
                isResolved ? FlowSummaryResolution.Project : FlowSummaryResolution.Unknown,
                FlowSummaryMethodKey.From(invocation.TargetMethod),
                isResolved ? new FlowSummaryMapping(source, target, FlowSummaryMappingKind.Explicit) : null,
                _rejectionReason);
        }

        public IReadOnlyList<ResolvedCallFlow> ResolveAll(IInvocationOperation invocation) =>
            new[] { Resolve(invocation, FlowSummaryEndpoint.Parameter(0), FlowSummaryEndpoint.Return) };
    }

    private sealed class InvocationMarkRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-SUMMARY-MARK-001";

        public override string Name { get; } = "Mark the summary invocation.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
            new[] { SyntaxKind.InvocationExpression };

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(new[] { SyntaxKind.InvocationExpression }, InvocationSemanticTag),
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            yield return new MarkRecord(RuleId, invocation, null, null, "Summary invocation seed.");
        }
    }

    private sealed class SummaryPayloadPropagationRule : RuleDefinitionPropagate
    {
        public override string RuleId { get; } = "TEST-SUMMARY-PROP-001";

        public override string Name { get; } = "Emit only resolved external summary payloads.";

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds { get; } =
            new[] { SyntaxKind.InvocationExpression };

        public override RuleConsumesContract Consumes => new(new[]
        {
            new RuleConsumedSyntax(new[] { SyntaxKind.InvocationExpression }, InvocationSemanticTag),
        });

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(new[] { SyntaxKind.InvocationExpression }, SummarySemanticTag),
        });

        public override IEnumerable<PropagatedMarkRecord> Propagate(
            IPropagationRuleContext context,
            IReadOnlyList<MarkRecord> seedMarks)
        {
            var seed = Assert.Single(seedMarks);
            var operation = Assert.IsAssignableFrom<IInvocationOperation>(
                context.SemanticModel.GetOperation(seed.SyntaxNode));
            var flow = context.ResolveCallFlow(
                operation,
                FlowSummaryEndpoint.Parameter(0),
                FlowSummaryEndpoint.Return);
            if (!flow.IsResolved)
            {
                yield break;
            }

            yield return new PropagatedMarkRecord(
                RuleId,
                new MarkRecord(RuleId, seed.SyntaxNode, null, null, "Resolved summary payload."),
                seed,
                1,
                new ExternalSummaryFlowPayload(flow));
        }
    }

    private sealed class SummaryPayloadProposalRule : RuleDefinitionPropose
    {
        public override string RuleId { get; } = "TEST-SUMMARY-PROPOSE-001";

        public override string Name { get; } = "Propose only from resolved summary payloads.";

        public override IReadOnlyList<SyntaxKind> DecisionConflictNodeKinds { get; } =
            new[] { SyntaxKind.InvocationExpression };

        public override IReadOnlyList<SyntaxKind> MergeableNodeKinds { get; } =
            new[] { SyntaxKind.InvocationExpression };

        public override RuleConsumesContract Consumes => new(new[]
        {
            new RuleConsumedSyntax(new[] { SyntaxKind.InvocationExpression }, SummarySemanticTag),
        });

        public override IEnumerable<DecisionUnit> Propose(
            IProposeRuleContext context,
            IReadOnlyList<MarkRecord> seedMarks,
            IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
            IReadOnlyList<NLISSN.Core.Lifting.LiftedMarkRecord> liftedMarks)
        {
            _ = context;
            _ = seedMarks;
            _ = liftedMarks;

            foreach (var propagated in propagatedMarks)
            {
                if (propagated.Payload is not ExternalSummaryFlowPayload { Flow.IsResolved: true })
                {
                    continue;
                }

                yield return DeleteDecisionFactory.CreateDeleteDecision(
                    RuleId,
                    propagated.Mark.SyntaxNode,
                    "Resolved summary payload candidate.");
            }
        }
    }

    private static RulePipeline CreateProductionSummaryPipeline()
    {
        return new RulePipeline(
            new RuleDefinitionMark[] { new TargetExpressionInvocationMarkRule() },
            new RuleDefinitionPropagate[] { new ExternalSummaryFlowPropagationRule() },
            Array.Empty<NLISSN.Core.Lifting.RuleDefinitionLift>(),
            new RuleDefinitionPropose[] { new DefaultRemovalProposalRule() });
    }

    private sealed class TargetExpressionInvocationMarkRule : RuleDefinitionMark
    {
        public override string RuleId => "TEST-SUMMARY-TARGET-MARK-001";

        public override string Name => "Mark an invocation as a target expression.";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
            new[] { SyntaxKind.InvocationExpression };

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(new[] { SyntaxKind.InvocationExpression }, RuleFactKind.TargetExpression),
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            yield return MarkRecordFactory.Create(
                RuleId,
                invocation,
                "Target expression invocation seed.",
                factKind: RuleFactKind.TargetExpression);
        }
    }
}
