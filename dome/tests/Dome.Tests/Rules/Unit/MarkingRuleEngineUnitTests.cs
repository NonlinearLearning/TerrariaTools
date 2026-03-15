using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rules;
using TerrariaTools.Dome.Tests.Testing.TestBuilders;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class MarkingRuleEngineUnitTests
{
    [Fact]
    public void Execute_UsesBuilderContextForDirectiveAndPropagationWithoutRoslyn()
    {
        var memberId = new MemberId("Sample.Player.Update()");
        var sharedSymbol = new SymbolRef("count", "count", SymbolKindRef.Local, memberId, 0, 5);
        var sourceTarget = new PlanTarget("Sample.cs", memberId, MemberKind.Method, TargetKind.Statement, 0, 14, "int count = 1;");
        var dependentTarget = new PlanTarget("Sample.cs", memberId, MemberKind.Method, TargetKind.Statement, 20, 18, "int next = count;");
        var engine = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault());
        var builder = new TestAnalysisContextBuilder()
            .AddTarget(new AnalysisTarget(
                sourceTarget,
                false,
                [new DirectiveAction(PlanActionKind.Delete, null, "dome:delete", "delete source")],
                [sharedSymbol],
                [],
                [],
                StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .AddTarget(new AnalysisTarget(
                dependentTarget,
                false,
                [],
                [],
                [sharedSymbol],
                [],
                StatementKindRef.Declaration,
                false,
                false,
                false,
                [],
                StatementScopeMode.MinimalBlock,
                "scope",
                null))
            .AddStatementSnapshot(sourceTarget, StatementScopeMode.MinimalBlock, sourceTarget.TargetKey, dependentTarget.TargetKey);

        var result = engine.Execute(builder.BuildContext());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, decision => decision.Target.TargetKey == sourceTarget.TargetKey && decision.Action.Kind == PlanActionKind.Delete);
        Assert.Contains(result, decision => decision.Target.TargetKey == dependentTarget.TargetKey && decision.Action.Kind == PlanActionKind.Delete);
    }
}
