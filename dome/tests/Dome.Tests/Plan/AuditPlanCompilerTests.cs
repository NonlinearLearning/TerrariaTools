using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Plan;
using Xunit;
using System.Text.Json;

namespace TerrariaTools.Dome.Tests.Plan;

/// <summary>
/// 审计计划编译器测试类。
/// </summary>
public class AuditPlanCompilerTests
{
    /// <summary>
    /// 测试编译方法在同一目标具有未解决的冲突操作时失败。
    /// </summary>
    [Fact]
    public void Compile_FailsWhenSameTargetHasUnresolvedConflictingActions()
    {
        var target = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            10,
            12,
            "player.Run();");

        var decisions = new[]
        {
            MarkDecision.ForTarget(target, PlanActionKind.Delete, "delete-rule", "delete reason"),
            MarkDecision.ForTarget(target, PlanActionKind.CommentOut, "comment-rule", "comment reason")
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.PlanCompileFailed, result.FailureCode);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("MultipleActionsForTarget", conflict.ConflictCode);
        Assert.Contains("no resolver is configured", conflict.Reason);
    }

    /// <summary>
    /// 测试编译方法为编译后的更改保留传播链。
    /// </summary>
    [Fact]
    public void Compile_PreservesPropagationChainForCompiledChanges()
    {
        var rootTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            10,
            14,
            "int count = 1;");
        var target = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            25,
            17,
            "int next = count;");
        var chain = new PropagationChain(
            rootTarget.TargetKey,
            rootTarget.DisplayText,
            new[]
            {
                new PropagationHop(
                    rootTarget.TargetKey,
                    rootTarget.DisplayText,
                    target.TargetKey,
                    target.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });

        var decisions = new[]
        {
            MarkDecision.ForTarget(rootTarget, PlanActionKind.Delete, "dome:delete", "delete reason"),
            MarkDecision.ForTarget(
                target,
                PlanActionKind.Delete,
                "dataflow-propagation",
                "propagation reason",
                sourceTargetKey: rootTarget.TargetKey,
                sourceTargetDisplayText: rootTarget.DisplayText,
                relatedSymbolKeys: new[] { "count-key" },
                relatedSymbolNames: new[] { "count" },
                chain: chain)
        };

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            decisions);

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var direct = Assert.Single(plan.Changes.Where(change => change.Reason.RuleId == "dome:delete"));
        var propagated = Assert.Single(plan.Changes.Where(change => change.Reason.RuleId == "dataflow-propagation"));

        Assert.Null(direct.Chain);
        Assert.NotNull(propagated.Chain);
        Assert.Equal(rootTarget.DisplayText, propagated.Chain!.RootTargetDisplayText);
        Assert.Single(propagated.Chain.Hops);
        Assert.Equal(rootTarget.TargetKey, propagated.Reason.SourceTargetKey);
        Assert.Contains("count", propagated.Reason.RelatedSymbolNames!);
    }

    /// <summary>
    /// 测试编译方法为直接和传播更改生成一致的 JSON 形状。
    /// </summary>
    [Fact]
    public void Compile_ProducesConsistentJsonShapeForDirectAndPropagationChanges()
    {
        var rootTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            10,
            14,
            "int count = 1;");
        var singleHopTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            25,
            17,
            "int next = count;");
        var multiHopTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Update()"),
            MemberKind.Method,
            TargetKind.Statement,
            45,
            17,
            "int final = next;");
        var singleHopChain = new PropagationChain(
            rootTarget.TargetKey,
            rootTarget.DisplayText,
            new[]
            {
                new PropagationHop(
                    rootTarget.TargetKey,
                    rootTarget.DisplayText,
                    singleHopTarget.TargetKey,
                    singleHopTarget.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "count-key" }, new[] { "count" }))
            });
        var multiHopChain = new PropagationChain(
            rootTarget.TargetKey,
            rootTarget.DisplayText,
            new[]
            {
                singleHopChain.Hops[0],
                new PropagationHop(
                    singleHopTarget.TargetKey,
                    singleHopTarget.DisplayText,
                    multiHopTarget.TargetKey,
                    multiHopTarget.DisplayText,
                    "dataflow-propagation",
                    PlanActionKind.Delete,
                    new PropagationEvidence(new[] { "next-key" }, new[] { "next" }))
            });

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                MarkDecision.ForTarget(rootTarget, PlanActionKind.Delete, "dome:delete", "delete reason"),
                MarkDecision.ForTarget(
                    singleHopTarget,
                    PlanActionKind.Delete,
                    "dataflow-propagation",
                    "single hop",
                    sourceTargetKey: rootTarget.TargetKey,
                    sourceTargetDisplayText: rootTarget.DisplayText,
                    relatedSymbolKeys: new[] { "count-key" },
                    relatedSymbolNames: new[] { "count" },
                    chain: singleHopChain),
                MarkDecision.ForTarget(
                    multiHopTarget,
                    PlanActionKind.Delete,
                    "dataflow-propagation",
                    "multi hop",
                    sourceTargetKey: singleHopTarget.TargetKey,
                    sourceTargetDisplayText: singleHopTarget.DisplayText,
                    relatedSymbolKeys: new[] { "next-key" },
                    relatedSymbolNames: new[] { "next" },
                    chain: multiHopChain)
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

    /// <summary>
    /// 测试编译方法在存在方法删除时丢弃语句更改。
    /// </summary>
    [Fact]
    public void Compile_DropsStatementChangesWhenMethodDeleteExists()
    {
        var methodTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Run()"),
            MemberKind.Method,
            TargetKind.Method,
            100,
            40,
            "private void Run() { }");
        var statementTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.Run()"),
            MemberKind.Method,
            TargetKind.Statement,
            120,
            14,
            "int count = 1;");

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                MarkDecision.ForTarget(methodTarget, PlanActionKind.Delete, "function-mark", "method delete"),
                MarkDecision.ForTarget(statementTarget, PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(TargetKind.Method, change.Target.TargetKind);
        Assert.Equal(PlanActionKind.Delete, change.Action.Kind);
    }

    /// <summary>
    /// 测试编译方法在存在类删除时丢弃方法和语句更改。
    /// </summary>
    [Fact]
    public void Compile_DropsMethodAndStatementChangesWhenClassDeleteExists()
    {
        var classTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.CacheEntry"),
            MemberKind.Class,
            TargetKind.Class,
            20,
            60,
            "private class CacheEntry { }");
        var methodTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.CacheEntry.Run()"),
            MemberKind.Method,
            TargetKind.Method,
            40,
            20,
            "private void Run() { }");
        var statementTarget = new PlanTarget(
            "Sample.cs",
            new MemberId("Sample.Player.CacheEntry.Run()"),
            MemberKind.Method,
            TargetKind.Statement,
            50,
            14,
            "int count = 1;");

        var result = AuditPlanCompiler.Compile(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                MarkDecision.ForTarget(classTarget, PlanActionKind.Delete, "class-mark", "class delete"),
                MarkDecision.ForTarget(methodTarget, PlanActionKind.Delete, "function-mark", "method delete"),
                MarkDecision.ForTarget(statementTarget, PlanActionKind.CommentOut, "dome:comment", "statement comment")
            });

        var plan = Assert.IsType<AuditPlan>(result.Plan);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(TargetKind.Class, change.Target.TargetKind);
        Assert.Equal(PlanActionKind.Delete, change.Action.Kind);
    }
}
