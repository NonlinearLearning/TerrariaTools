using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Analysis.FlowSummaries;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests.Decision;

public sealed class FlowSummaryDeletionSafetyTests
{
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
}
