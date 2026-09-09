using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using NLISSN.Hosting;
using Xunit;

namespace NLISSN.Tests.Application;

public sealed class CommandHostCompositionTests
{
    [Fact]
    public void DefaultConstructorUsesTheGeneratedCoreCatalog()
    {
        var result = new CommandHost().AnalyzeFromArgs(new[] { "--skip-rewrite" });

        Assert.Contains(
          result.RuleGraphNodeStatuses!.Keys,
          node => node == RuleNodeId.For(RuleKind.Mark, "mark.target.identifier-name"));
        Assert.DoesNotContain(
          result.RuleGraphNodeStatuses.Keys,
          node => node == RuleNodeId.For(RuleKind.Mark, "mark.unreachable-method"));
    }

    [Fact]
    public void ApplicationServiceWithoutResolverUsesCompilationBackedFrameworkSummary()
    {
        var result = new ApplicationService(CreateSummaryPipeline()).Analyze(
            "public sealed class Sample { public string Run(object value) => value.ToString(); }",
            "default-resolver.cs",
            CreateSettings());

        var summaryMark = Assert.Single(result.PropagatedMarks);
        var payload = Assert.IsType<ExternalSummaryFlowPayload>(summaryMark.Payload);
        Assert.True(payload.Flow.IsResolved);
        Assert.Equal(FlowSummaryResolution.Framework, payload.Flow.Resolution);
        Assert.Contains(
            result.Evidence!.Nodes,
            node => node.Kind == AnalysisEvidenceKind.UsesSummary);
    }

    [Fact]
    public void ApplicationServiceExplicitResolverOverridesFrameworkCatalog()
    {
        var result = new ApplicationService(
            CreateSummaryPipeline(),
            new ExplicitProjectFlowResolver()).Analyze(
            "public sealed class Sample { public string Run(object value) => value.ToString(); }",
            "custom-resolver.cs",
            CreateSettings());

        var payload = Assert.IsType<ExternalSummaryFlowPayload>(Assert.Single(result.PropagatedMarks).Payload);
        Assert.Equal(FlowSummaryResolution.Project, payload.Flow.Resolution);
    }

    private static RulePipeline CreateSummaryPipeline()
    {
        return new RulePipeline(
            new RuleDefinitionMark[] { new InvocationSeedRule() },
            new RuleDefinitionPropagate[] { new SummaryProbePropagationRule() },
            Array.Empty<NLISSN.Core.Lifting.RuleDefinitionLift>(),
            Array.Empty<NLISSN.Core.Decision.RuleDefinitionPropose>());
    }

    private static AnalysisRequestSettings CreateSettings()
    {
        return new AnalysisRequestSettings(
            Array.Empty<string>(),
            Array.Empty<string>(),
            SkipRewrite: true,
            ValidateBindings: false,
            DeleteUnreferencedMethods: false,
            ClearUnusedInterfaceImplementations: false,
            PrivatizeInternalOnlyPublicMethods: false,
            FastDeleteClassDirectory: false,
            FilterDeleteClassFilesByTargetName: false);
    }

    private sealed class InvocationSeedRule : RuleDefinitionMark
    {
        private static readonly RuleSemanticTag InvocationTag = new("Test.FlowSummary.Invocation");

        public override string RuleId => "test.flow-summary.invocation";

        public override string Name => "Mark invocation for default resolver test";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds =>
            new[] { SyntaxKind.InvocationExpression };

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(new[] { SyntaxKind.InvocationExpression }, InvocationTag),
        });

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            _ = context;
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            yield return new MarkRecord(
                RuleId,
                invocation,
                null,
                null,
                "Flow summary invocation seed.",
                SemanticTag: InvocationTag);
        }
    }

    private sealed class SummaryProbePropagationRule : RuleDefinitionPropagate
    {
        private static readonly RuleSemanticTag InvocationTag = new("Test.FlowSummary.Invocation");
        private static readonly RuleSemanticTag SummaryTag = new("Test.FlowSummary.Resolved");

        public override string RuleId => "test.flow-summary.probe";

        public override string Name => "Resolve a framework flow summary";

        public override IReadOnlyCollection<NLCPGCapability> RequiredCapabilities =>
            new[] { NLCPGCapability.InterproceduralDataFlow };

        public override IReadOnlyList<SyntaxKind> AllowedPropagateNodeKinds =>
            new[] { SyntaxKind.InvocationExpression };

        public override RuleConsumesContract Consumes => new(new[]
        {
            new RuleConsumedSyntax(new[] { SyntaxKind.InvocationExpression }, InvocationTag),
        });

        public override RuleProducesContract Produces => new(new[]
        {
            new RuleProducedSyntax(new[] { SyntaxKind.InvocationExpression }, SummaryTag),
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
                FlowSummaryEndpoint.Receiver,
                FlowSummaryEndpoint.Return);
            if (!flow.IsResolved)
            {
                yield break;
            }

            yield return new PropagatedMarkRecord(
                RuleId,
                new MarkRecord(
                    RuleId,
                    seed.SyntaxNode,
                    null,
                    null,
                    "Resolved framework flow summary.",
                    SemanticTag: SummaryTag),
                seed,
                1,
                new ExternalSummaryFlowPayload(flow));
        }
    }

    private sealed class ExplicitProjectFlowResolver : ICallFlowResolver
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
}
