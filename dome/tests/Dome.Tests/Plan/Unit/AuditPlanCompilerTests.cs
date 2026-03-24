using System.Text.Json;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;
using Xunit;

namespace TerrariaTools.Dome.Tests.Plan;

public class AuditPlanCompilerTests
{
    [Fact]
    public void Compile_FailsWhenSameTargetHasUnresolvedConflictingActions()
    {
        var target = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 10, 12, "player.Run();");

        var decisions = new[]
        {
            CreateDecision(target, CoreCommon.PlanActionKind.Delete, "delete-rule", "delete reason"),
            CreateDecision(target, CoreCommon.PlanActionKind.CommentOut, "comment-rule", "comment reason")
        };

        var result = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", "input.cs", "out", CoreCommon.RunMode.Standard),
            decisions);

        Assert.False(result.IsSuccess);
        Assert.Equal(CoreCommon.FailureCode.PlanCompileFailed, result.FailureCode);
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
        var rootTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 10, 14, "int count = 1;");
        var target = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 25, 17, "int next = count;");
        var chain = new CoreRules.PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                new CoreRules.PropagationHop(
                    CreateTargetKey(rootTarget),
                    rootTarget.locator.DisplayText,
                    CreateTargetKey(target),
                    target.locator.DisplayText,
                    "dataflow-propagation",
                    CoreCommon.PlanActionKind.Delete,
                    new CoreRules.PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });

        var decisions = new[]
        {
            CreateDecision(rootTarget, CoreCommon.PlanActionKind.Delete, "dome:delete", "delete reason"),
            CreateDecision(
                target,
                CoreCommon.PlanActionKind.Delete,
                "dataflow-propagation",
                "propagation reason",
                new CoreRules.PlanReason(
                    "dataflow-propagation",
                    "propagation reason",
                    SourceTargetKey: CreateTargetKey(rootTarget),
                    SourceTargetDisplayText: rootTarget.locator.DisplayText,
                    RelatedSymbolKeys: new[] { "count-key" },
                    RelatedSymbolNames: new[] { "count" }),
                chain)
        };

        var result = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", "input.cs", "out", CoreCommon.RunMode.Standard),
            decisions);

        var plan = Assert.IsType<CorePlanning.AuditPlan>(result.Plan);
        var direct = Assert.Single(plan.Changes.Where(change => Assert.IsType<CorePlanning.PlanReason>(change.Reason).RuleId == "dome:delete"));
        var propagated = Assert.Single(plan.Changes.Where(change => Assert.IsType<CorePlanning.PlanReason>(change.Reason).RuleId == "dataflow-propagation"));

        Assert.Null(direct.Chain);
        var propagatedChain = Assert.IsType<CorePlanning.PropagationChain>(propagated.Chain);
        Assert.Equal(rootTarget.locator.DisplayText, propagatedChain.RootTargetDisplayText);
        Assert.Single(propagatedChain.Hops);
        var propagatedReason = Assert.IsType<CorePlanning.PlanReason>(propagated.Reason);
        Assert.Equal(CreateTargetKey(rootTarget), propagatedReason.SourceTargetKey);
        Assert.Contains("count", propagatedReason.RelatedSymbolNames!);
    }

    [Fact]
    public void Compile_ProducesConsistentJsonShapeForDirectAndPropagationChanges()
    {
        var rootTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 10, 14, "int count = 1;");
        var singleHopTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 25, 17, "int next = count;");
        var multiHopTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Update()", CoreCommon.MemberKind.Method, 45, 17, "int final = next;");
        var singleHopChain = new CoreRules.PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                new CoreRules.PropagationHop(
                    CreateTargetKey(rootTarget),
                    rootTarget.locator.DisplayText,
                    CreateTargetKey(singleHopTarget),
                    singleHopTarget.locator.DisplayText,
                    "dataflow-propagation",
                    CoreCommon.PlanActionKind.Delete,
                    new CoreRules.PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });
        var multiHopChain = new CoreRules.PropagationChain(
            CreateTargetKey(rootTarget),
            rootTarget.locator.DisplayText,
            new[]
            {
                singleHopChain.Hops[0],
                new CoreRules.PropagationHop(
                    CreateTargetKey(singleHopTarget),
                    singleHopTarget.locator.DisplayText,
                    CreateTargetKey(multiHopTarget),
                    multiHopTarget.locator.DisplayText,
                    "dataflow-propagation",
                    CoreCommon.PlanActionKind.Delete,
                    new CoreRules.PropagationEvidence(new[] { "next-key" }, new[] { "next" }))
            });

        var result = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", "input.cs", "out", CoreCommon.RunMode.Standard),
            new[]
            {
                CreateDecision(rootTarget, CoreCommon.PlanActionKind.Delete, "dome:delete", "delete reason"),
                CreateDecision(
                    singleHopTarget,
                    CoreCommon.PlanActionKind.Delete,
                    "dataflow-propagation",
                    "single hop",
                    new CoreRules.PlanReason(
                        "dataflow-propagation",
                        "single hop",
                        SourceTargetKey: CreateTargetKey(rootTarget),
                        SourceTargetDisplayText: rootTarget.locator.DisplayText,
                        RelatedSymbolKeys: new[] { "count-key" },
                        RelatedSymbolNames: new[] { "count" }),
                    singleHopChain),
                CreateDecision(
                    multiHopTarget,
                    CoreCommon.PlanActionKind.Delete,
                    "dataflow-propagation",
                    "multi hop",
                    new CoreRules.PlanReason(
                        "dataflow-propagation",
                        "multi hop",
                        SourceTargetKey: CreateTargetKey(singleHopTarget),
                        SourceTargetDisplayText: singleHopTarget.locator.DisplayText,
                        RelatedSymbolKeys: new[] { "next-key" },
                        RelatedSymbolNames: new[] { "next" }),
                    multiHopChain)
            });

        var plan = Assert.IsType<CorePlanning.AuditPlan>(result.Plan);
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
        var methodTarget = CreateTarget(CoreCommon.TargetKind.Method, "Sample.Player.Run()", CoreCommon.MemberKind.Method, 100, 40, "private void Run() { }");
        var statementTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.Run()", CoreCommon.MemberKind.Method, 120, 14, "int count = 1;");

        var result = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", "input.cs", "out", CoreCommon.RunMode.Standard),
            new[]
            {
                CreateDecision(methodTarget, CoreCommon.PlanActionKind.Delete, "function-mark", "method delete"),
                CreateDecision(statementTarget, CoreCommon.PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<CorePlanning.AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(CoreCommon.TargetKind.Method, change.Target.TargetKind);
        Assert.Equal("Sample.Player.Run()", change.Target.MemberId.Value);
        Assert.Equal(CoreCommon.PlanActionKind.Delete, change.Action.Kind);
    }

    [Fact]
    public void Compile_DropsMethodAndStatementChangesWhenClassDeleteExists()
    {
        var classTarget = CreateTarget(CoreCommon.TargetKind.Class, "Sample.Player.CacheEntry", CoreCommon.MemberKind.Class, 20, 60, "private class CacheEntry { }");
        var methodTarget = CreateTarget(CoreCommon.TargetKind.Method, "Sample.Player.CacheEntry.Run()", CoreCommon.MemberKind.Method, 40, 20, "private void Run() { }");
        var statementTarget = CreateTarget(CoreCommon.TargetKind.Statement, "Sample.Player.CacheEntry.Run()", CoreCommon.MemberKind.Method, 50, 14, "int count = 1;");

        var result = CorePlanning.AuditPlanCompiler.Compile(
            new CorePlanning.PlanMetadata("dome", "1", "input.cs", "out", CoreCommon.RunMode.Standard),
            new[]
            {
                CreateDecision(classTarget, CoreCommon.PlanActionKind.Delete, "class-mark", "class delete"),
                CreateDecision(methodTarget, CoreCommon.PlanActionKind.Delete, "function-mark", "method delete"),
                CreateDecision(statementTarget, CoreCommon.PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<CorePlanning.AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(CoreCommon.TargetKind.Class, change.Target.TargetKind);
        Assert.Equal("Sample.Player.CacheEntry", change.Target.MemberId.Value);
        Assert.Equal(20, change.Locator.SpanStart);
        Assert.Equal("private class CacheEntry { }", change.Locator.DisplayText);
        Assert.Equal(CoreCommon.PlanActionKind.Delete, change.Action.Kind);
    }

    private static (CoreCommon.TargetIdentity target, CoreCommon.TargetLocator locator) CreateTarget(
        CoreCommon.TargetKind targetKind,
        string memberId,
        CoreCommon.MemberKind memberKind,
        int spanStart,
        int spanLength,
        string displayText) =>
        (
            new CoreCommon.TargetIdentity("Sample.cs", new CoreCommon.MemberId(memberId), memberKind, targetKind),
            new CoreCommon.TargetLocator(spanStart, spanLength, displayText)
        );

    private static string CreateTargetKey((CoreCommon.TargetIdentity target, CoreCommon.TargetLocator locator) target) =>
        $"{target.target.IdentityKey}|{target.locator.EffectiveResolutionKey.SpanStart}|{target.locator.EffectiveResolutionKey.SpanLength}";

    private static CoreRules.MarkDecision CreateDecision(
        (CoreCommon.TargetIdentity target, CoreCommon.TargetLocator locator) target,
        CoreCommon.PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        CoreRules.PlanReason? reason = null,
        CoreRules.PropagationChain? chain = null) =>
        new(
            target.target,
            target.locator,
            new CoreCommon.PlanAction(actionKind),
            reason ?? new CoreRules.PlanReason(ruleId, reasonText),
            chain);
}
