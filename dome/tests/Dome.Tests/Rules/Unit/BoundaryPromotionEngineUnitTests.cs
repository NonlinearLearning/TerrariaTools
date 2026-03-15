using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rules;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class BoundaryPromotionEngineUnitTests
{
    [Fact]
    public void Promote_UsesBuilderContextWithoutRoslyn()
    {
        var sourceTarget = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, 0, 12, "Helper();");
        var boundaryTarget = new PlanTarget("Sample.cs", new MemberId("Sample.Player.Helper()"), MemberKind.Method, TargetKind.Method, 20, 20, "Helper");
        var invocationTarget = new AnalysisTarget(
            sourceTarget,
            false,
            [],
            [],
            [],
            [boundaryTarget.MemberId],
            StatementKindRef.Unknown,
            false,
            false,
            false,
            [],
            StatementScopeMode.MinimalBlock,
            "scope",
            null);
        var context = new TestAnalysisContextBuilder()
            .AddTarget(invocationTarget)
            .AddFunctionNode(new FunctionNodeRef(
                boundaryTarget.MemberId,
                MemberKind.Method,
                "Sample.Player",
                "Helper",
                "Sample.cs",
                boundaryTarget.SpanStart,
                boundaryTarget.SpanLength,
                true,
                true,
                true,
                false,
                "void"))
            .WithReferences(new SingleReferenceQueryService(boundaryTarget.MemberId, sourceTarget.MemberId))
            .BuildContext();
        var decisions = new[]
        {
            MarkDecision.ForTarget(
                sourceTarget,
                PlanActionKind.Delete,
                "seed",
                "seed delete")
        };
        var engine = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault());

        var result = engine.Promote(
            context,
            decisions,
            new Dictionary<string, AnalysisTarget>(StringComparer.Ordinal)
            {
                [sourceTarget.TargetKey] = invocationTarget
            });

        var promoted = Assert.Single(result);
        Assert.Equal(boundaryTarget.MemberId, promoted.Target.MemberId);
        Assert.Equal(DecisionOrigin.BoundaryPromotion, promoted.Reason.Origin);
    }

    private sealed class SingleReferenceQueryService(MemberId from, MemberId to) : IReferenceQueryService
    {
        public bool HasReferences(string symbolOrMemberId) => symbolOrMemberId == to.Value;

        public IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId) => symbolOrMemberId == to.Value ? [from] : Array.Empty<MemberId>();

        public IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId) => Array.Empty<string>();
    }
}
