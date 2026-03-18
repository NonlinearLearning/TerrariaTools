using System.Text.Json;
using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Model.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public class AuditPlanCompilerTests
{
    [Fact]
    public void Compile_FailsWhenSameTargetHasUnresolvedConflictingActions()
    {
        var target = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 10, 12, "player.Run();");

        var decisions = new[]
        {
            CreateDecision(target, PlanActionKind.Delete, "delete-rule", "delete reason"),
            CreateDecision(target, PlanActionKind.CommentOut, "comment-rule", "comment reason")
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.PlanCompileFailed, result.FailureCode);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("MultipleActionsForTarget", conflict.ConflictCode);
        Assert.Equal("Sample.Player.Update()", conflict.Target.MemberId.Value);
        Assert.Equal(10, conflict.Locator.SpanStart);
        Assert.Equal("player.Run();", conflict.Locator.DisplayText);
        Assert.Contains("no resolver is configured", conflict.Reason);
    }

    [Fact]
    public void Compile_PreservesPropagationChainForCompiledChanges()
    {
        var rootTarget = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 10, 14, "int count = 1;");
        var target = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 25, 17, "int next = count;");
        var chain = new PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                new PropagationHop(
                    CreateTargetKey(rootTarget),
                    rootTarget.locator.DisplayText,
                    CreateTargetKey(target),
                    target.locator.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });

        var decisions = new[]
        {
            CreateDecision(rootTarget, PlanActionKind.Delete, "dome:delete", "delete reason"),
            CreateDecision(
                target,
                PlanActionKind.Delete,
                "dataflow-propagation",
                "propagation reason",
                new PlanReason(
                    "dataflow-propagation",
                    "propagation reason",
                    SourceTargetKey: CreateTargetKey(rootTarget),
                    SourceTargetDisplayText: rootTarget.locator.DisplayText,
                    RelatedSymbolKeys: new[] { "count-key" },
                    RelatedSymbolNames: new[] { "count" }),
                chain)
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var direct = Assert.Single(plan.Changes.Where(change => Assert.IsType<PlanReason>(change.Reason).RuleId == "dome:delete"));
        var propagated = Assert.Single(plan.Changes.Where(change => Assert.IsType<PlanReason>(change.Reason).RuleId == "dataflow-propagation"));

        Assert.Null(direct.Chain);
        var propagatedChain = Assert.IsType<PropagationChain>(propagated.Chain);
        Assert.Equal(rootTarget.locator.DisplayText, propagatedChain.RootTargetDisplayText);
        Assert.Single(propagatedChain.Hops);
        var propagatedReason = Assert.IsType<PlanReason>(propagated.Reason);
        Assert.Equal(CreateTargetKey(rootTarget), propagatedReason.SourceTargetKey);
        Assert.Contains("count", propagatedReason.RelatedSymbolNames!);
    }

    [Fact]
    public void Compile_ProducesConsistentJsonShapeForDirectAndPropagationChanges()
    {
        var rootTarget = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 10, 14, "int count = 1;");
        var singleHopTarget = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 25, 17, "int next = count;");
        var multiHopTarget = CreateTarget(TargetKind.Statement, "Sample.Player.Update()", MemberKind.Method, 45, 17, "int final = next;");
        var singleHopChain = new PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                new PropagationHop(
                    CreateTargetKey(rootTarget),
                    rootTarget.locator.DisplayText,
                    CreateTargetKey(singleHopTarget),
                    singleHopTarget.locator.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });
        var multiHopChain = new PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                singleHopChain.Hops[0],
                new PropagationHop(
                    CreateTargetKey(singleHopTarget),
                    singleHopTarget.locator.DisplayText,
                    CreateTargetKey(multiHopTarget),
                    multiHopTarget.locator.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "next-key" }, new[] { "next" }))
            });

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                CreateDecision(rootTarget, PlanActionKind.Delete, "dome:delete", "delete reason"),
                CreateDecision(
                    singleHopTarget,
                    PlanActionKind.Delete,
                    "dataflow-propagation",
                    "single hop",
                    new PlanReason(
                        "dataflow-propagation",
                        "single hop",
                        SourceTargetKey: CreateTargetKey(rootTarget),
                        SourceTargetDisplayText: rootTarget.locator.DisplayText,
                        RelatedSymbolKeys: new[] { "count-key" },
                        RelatedSymbolNames: new[] { "count" }),
                    singleHopChain),
                CreateDecision(
                    multiHopTarget,
                    PlanActionKind.Delete,
                    "dataflow-propagation",
                    "multi hop",
                    new PlanReason(
                        "dataflow-propagation",
                        "multi hop",
                        SourceTargetKey: CreateTargetKey(singleHopTarget),
                        SourceTargetDisplayText: singleHopTarget.locator.DisplayText,
                        RelatedSymbolKeys: new[] { "next-key" },
                        RelatedSymbolNames: new[] { "next" }),
                    multiHopChain)
            });

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var json = JsonSerializer.Serialize(plan);
        using var document = JsonDocument.Parse(json);
        var changes = document.RootElement.GetProperty("Changes");

        Assert.Equal(3, changes.GetArrayLength());
        Assert.True(changes[0].TryGetProperty("Chain", out var directChain));
        Assert.Equal(JsonValueKind.Null, directChain.ValueKind);
        Assert.True(changes[1].TryGetProperty("Chain", out var singleChain));
        Assert.Equal(1, singleChain.GetProperty("Hops").GetArrayLength());
        Assert.True(changes[2].TryGetProperty("Chain", out var multiChain));
        Assert.Equal(2, multiChain.GetProperty("Hops").GetArrayLength());
    }

    [Fact]
    public void Compile_DropsStatementChangesWhenMethodDeleteExists()
    {
        var methodTarget = CreateTarget(TargetKind.Method, "Sample.Player.Run()", MemberKind.Method, 100, 40, "private void Run() { }");
        var statementTarget = CreateTarget(TargetKind.Statement, "Sample.Player.Run()", MemberKind.Method, 120, 14, "int count = 1;");

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                CreateDecision(methodTarget, PlanActionKind.Delete, "function-mark", "method delete"),
                CreateDecision(statementTarget, PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(TargetKind.Method, change.Target.TargetKind);
        Assert.Equal("Sample.Player.Run()", change.Target.MemberId.Value);
        Assert.Equal(PlanActionKind.Delete, change.Action.Kind);
    }

    [Fact]
    public void Compile_DropsMethodAndStatementChangesWhenClassDeleteExists()
    {
        var classTarget = CreateTarget(TargetKind.Class, "Sample.Player.CacheEntry", MemberKind.Class, 20, 60, "private class CacheEntry { }");
        var methodTarget = CreateTarget(TargetKind.Method, "Sample.Player.CacheEntry.Run()", MemberKind.Method, 40, 20, "private void Run() { }");
        var statementTarget = CreateTarget(TargetKind.Statement, "Sample.Player.CacheEntry.Run()", MemberKind.Method, 50, 14, "int count = 1;");

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                CreateDecision(classTarget, PlanActionKind.Delete, "class-mark", "class delete"),
                CreateDecision(methodTarget, PlanActionKind.Delete, "function-mark", "method delete"),
                CreateDecision(statementTarget, PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(TargetKind.Class, change.Target.TargetKind);
        Assert.Equal("Sample.Player.CacheEntry", change.Target.MemberId.Value);
        Assert.Equal(20, change.Locator.SpanStart);
        Assert.Equal("private class CacheEntry { }", change.Locator.DisplayText);
        Assert.Equal(PlanActionKind.Delete, change.Action.Kind);
    }

    private static (TargetIdentity target, TargetLocator locator) CreateTarget(
        TargetKind targetKind,
        string memberId,
        MemberKind memberKind,
        int spanStart,
        int spanLength,
        string displayText) =>
        (
            new TargetIdentity("Sample.cs", new MemberId(memberId), memberKind, targetKind),
            new TargetLocator(spanStart, spanLength, displayText)
        );

    private static string CreateTargetKey((TargetIdentity target, TargetLocator locator) target) =>
        $"{target.target.IdentityKey}|{target.locator.EffectiveResolutionKey.SpanStart}|{target.locator.EffectiveResolutionKey.SpanLength}";

    private static MarkDecision CreateDecision(
        (TargetIdentity target, TargetLocator locator) target,
        PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        PlanReason? reason = null,
        PropagationChain? chain = null) =>
        new(
            target.target,
            target.locator,
            new PlanAction(actionKind),
            reason ?? new PlanReason(ruleId, reasonText),
            chain);
}
