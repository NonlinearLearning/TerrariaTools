using TerrariaTools.Dome.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelPlanning = TerrariaTools.Dome.Model.Planning;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class MarkingRuleEngineBuildDecisionsTests
{
    [Fact]
    public async Task BuildDecisions_MarksStatementWithDeleteDirective()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    // dome:delete
                    int count = 1;
                    int next = count;
                }
            }

            public static class Runner
            {
                public static void Run()
                {
                    new Player().Update();
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.True(decisions.Count >= 2);
        Assert.Contains(decisions, decision => decision.Reason.RuleId == "dome:delete");
        Assert.Contains(decisions, decision => decision.Reason.RuleId == "dataflow-propagation");
    }

    [Fact]
    public async Task BuildDecisions_BlocksHighRiskTargets()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public interface IPlayer
            {
                void Update();
            }

            public class Player : IPlayer
            {
                public void Update()
                {
                    // dome:delete
                    Run();
                }

                private void Run() { }
            }

            public static class Runner
            {
                public static void Run(IPlayer player)
                {
                    player.Update();
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.DoesNotContain(decisions, decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Statement);
        Assert.DoesNotContain(decisions, decision => decision.Target.MemberId.Value == "Sample.Player.Run()");
        Assert.DoesNotContain(decisions, decision => decision.Target.MemberId.Value == "Sample.Player.Update()");
    }

    [Fact]
    public async Task BuildDecisions_PromotesBoundaryDeletes()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    // dome:delete
                    fun2(value);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(
            decisions,
            decision => decision.Reason.RuleId == "boundary-promotion" &&
                        decision.Target.TargetKind == ModelPrimitives.TargetKind.Method &&
                        decision.Target.MemberId.Value == "Sample.Player.fun2(int)");
    }

    [Fact]
    public async Task BuildDecisions_DoesNotPromotePropagatedDelete()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    int count = value;
                    fun2(count);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.DoesNotContain(
            decisions,
            decision => decision.Reason.RuleId == "boundary-promotion" &&
                        decision.Locator.DisplayText == "fun2(count);" &&
                        decision.Target.TargetKind == ModelPrimitives.TargetKind.Method);
    }

    [Fact]
    public async Task BuildDecisions_StopsPropagationAfterCleanRedefinition()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    // dome:delete
                    int count = 1;
                    count = 2;
                    int next = count;
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.DoesNotContain(decisions, decision => decision.Locator.DisplayText == "int next = count;");
    }

    [Fact]
    public async Task BuildDecisions_DoesNotPropagateAcrossParentBlockByDefault()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int seed)
                {
                    // dome:delete
                    int parent = seed;
                    {
                        int child = parent;
                    }
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(decisions, decision => decision.Locator.DisplayText == "int parent = seed;");
        Assert.DoesNotContain(decisions, decision => decision.Locator.DisplayText == "int child = parent;" && decision.Reason.RuleId == "dataflow-propagation");
    }

    [Fact]
    public async Task BuildDecisions_EmitsDeleteForUnreferencedTopLevelInternalClass()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            class CacheEntry
            {
                public int Value { get; set; }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == ModelPrimitives.TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.CacheEntry" &&
            decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete &&
            decision.Reason.RuleId == "class-mark");
    }

    [Fact]
    public async Task BuildDecisions_DeletesUnusedPrivateFieldAndProperty()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                private int _unusedField = 1;
                private int UnusedProperty { get; } = 2;

                public void Update()
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == ModelPrimitives.TargetKind.Field &&
            decision.Target.MemberId.Value == "Sample.Player._unusedField" &&
            decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete &&
            decision.Reason.RuleId == "unused-member");
        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == ModelPrimitives.TargetKind.Property &&
            decision.Target.MemberId.Value == "Sample.Player.UnusedProperty" &&
            decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete &&
            decision.Reason.RuleId == "unused-member");
    }

    [Fact]
    public async Task BuildDecisions_UsesParentBlockPiercingForSeedThatReadsParentScopeSymbol()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int seed)
                {
                    int parent = seed;
                    {
                        // dome:delete
                        int child = parent;
                        int next = child;
                    }
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(
            decisions,
            decision => decision.Locator.DisplayText == "int child = parent;" &&
                        decision.Reason.RuleId == "expression-mark");
        Assert.Contains(
            decisions,
            decision => decision.Locator.DisplayText == "int next = child;" &&
                        decision.Reason.RuleId == "dataflow-propagation");
    }

    [Fact]
    public async Task BuildDecisions_DeletesUnreferencedNestedClass()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                private class CacheEntry
                {
                    public int Value { get; set; }
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(
            decisions,
            decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Class &&
                        decision.Target.MemberId.Value == "Sample.Player.CacheEntry" &&
                        decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete &&
                        decision.Reason.RuleId == "class-mark");
    }

    [Fact]
    public async Task BuildDecisions_DeletesUnreferencedPrivateMethod()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                private void Helper()
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(
            decisions,
            decision => decision.Target.MemberId.Value == "Sample.Player.Helper()" &&
                        decision.Action.Kind == ModelPrimitives.PlanActionKind.Delete &&
                        decision.Reason is ModelRules.PlanReason reason &&
                        (reason.RuleId == "function-mark" || reason.RuleId == "unused-method"));
    }

    [Fact]
    public async Task BuildDecisions_AddsReturnForNonVoidEmptyBody()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public int Use()
                {
                    return Compute();
                }

                private int Compute()
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.Contains(
            decisions,
            decision => decision.Target.MemberId.Value == "Sample.Player.Compute()" &&
                        decision.Action.Kind == ModelPrimitives.PlanActionKind.AddReturn &&
                        decision.Reason is ModelRules.PlanReason reason &&
                        reason.ReasonText.Contains("empty body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildDecisions_PrivatizesPublicMethodWithOnlyInternalReferences()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Zebra()
                {
                    Helper();
                }

                public void Helper()
                {
                }

                public void Alpha()
                {
                    Helper();
                }
            }

            public class Runner
            {
                public void Run(Player player)
                {
                    player.Zebra();
                    player.Alpha();
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        var decision = Assert.Single(decisions.Where(d => d.Target.MemberId.Value == "Sample.Player.Helper()" && d.Action.Kind == ModelPrimitives.PlanActionKind.ChangeVisibilityToPrivate));
        Assert.Equal("method-privatization", Assert.IsType<ModelRules.PlanReason>(decision.Reason).RuleId);
    }

    [Fact]
    public async Task BuildDecisions_ReordersPublicMethodsForType()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Zebra()
                {
                }

                public void Alpha()
                {
                }
            }
            """);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
            .BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        var decision = Assert.Single(decisions.Where(d =>
            d.Target.TargetKind == ModelPrimitives.TargetKind.Class &&
            d.Target.MemberId.Value == "Sample.Player" &&
            d.Action.Kind == ModelPrimitives.PlanActionKind.ReorderPublicMethods));
        Assert.Equal("public-method-order", Assert.IsType<ModelRules.PlanReason>(decision.Reason).RuleId);
    }

    [Fact]
    public async Task BuildDecisions_BoundaryPromotion_DoesNotDuplicateExistingMethodDelete()
    {
        var analysis = await AnalyzeAsync(
            """
            namespace Sample;

            public class Player
            {
                public void Update(int value)
                {
                    // dome:delete
                    fun2(value);
                }

                private void fun2(int i)
                {
                }
            }
            """);

        var context = analysis.CreateContext();
        var statementDecision = Assert.Single(
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
                .BuildDecisions(context, CancellationToken.None)
                .Where(decision => decision.Target.TargetKind == ModelPrimitives.TargetKind.Statement));
        var methodTarget = context.FunctionIndex.NodesByMemberId.Values.Single(function => function.MemberId.Value == "Sample.Player.fun2(int)");
        var existingMethodDelete = new ModelRules.MarkDecision(
            new ModelPrimitives.TargetIdentity(
                methodTarget.DocumentPath,
                methodTarget.MemberId,
                methodTarget.MemberKind,
                ModelPrimitives.TargetKind.Method),
            new ModelPrimitives.TargetLocator(
                methodTarget.SpanStart,
                methodTarget.SpanLength,
                methodTarget.DisplayName,
                new ModelPrimitives.TargetResolutionKey(methodTarget.SpanStart, methodTarget.SpanLength)),
            new ModelPlanning.PlanAction(ModelPrimitives.PlanActionKind.Delete, null),
            new ModelRules.PlanReason("existing-delete", "already deleted"));

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            new[] { statementDecision, existingMethodDelete },
            context.View.Targets.ToDictionary(target => $"{target.Target.IdentityKey}|{target.Locator.EffectiveResolutionKey.SpanStart}|{target.Locator.EffectiveResolutionKey.SpanLength}", StringComparer.Ordinal));

        Assert.Empty(promoted);
    }

    private static async Task<ApplicationAbstractions.AnalysisEngineResult> AnalyzeAsync(string sourceText) =>
        await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ApplicationAbstractions.SourceDocument("Sample.cs", "Sample.cs", sourceText)
                ]),
            CancellationToken.None);
}
