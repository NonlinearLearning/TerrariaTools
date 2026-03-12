using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rewrite.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

/// <summary>
/// 重写执行器测试类。
/// </summary>
public class RewriteExecutorTests
{
    /// <summary>
    /// 测试异步执行方法在无法解析目标时失败。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FailsWhenTargetCannotBeResolved()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    Run();
                }

                private void Run() { }
            }
            """;

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, 999, 3, "Run();"),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("delete-rule", "delete reason"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
    }

    /// <summary>
    /// 测试异步执行方法在计划使用添加返回时将语句替换为返回。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReplacesStatementWithReturnWhenPlanUsesAddReturn()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public int Update()
                {
                    Run();
                    return 0;
                }

                private void Run() { }
            }
            """;

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, source.IndexOf("Run();", StringComparison.Ordinal), "Run();".Length, "Run();"),
                    new PlanAction(PlanActionKind.AddReturn, "1"),
                    new PlanReason("return-rule", "replace with return"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        Assert.Contains("return 1;", result.RewrittenSource);
    }

    /// <summary>
    /// 测试异步执行方法在跨度匹配但文本不匹配时失败。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FailsWhenSpanMatchesButTextDoesNotMatch()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    Run();
                }

                private void Run() { }
            }
            """;

        var spanStart = source.IndexOf("Run();", StringComparison.Ordinal);
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, spanStart, "Run();".Length, "Stop();"),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("delete-rule", "delete reason"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
        Assert.Contains("text", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 测试异步执行方法在使用默认替换针对非赋值语句时失败。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FailsWhenReplaceWithDefaultTargetsNonAssignmentStatement()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    Run();
                }

                private void Run() { }
            }
            """;

        var spanStart = source.IndexOf("Run();", StringComparison.Ordinal);
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, spanStart, "Run();".Length, "Run();"),
                    new PlanAction(PlanActionKind.ReplaceWithDefault, "default"),
                    new PlanReason("default-rule", "default reason"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCode.RewriteFailed, result.FailureCode);
        Assert.Contains("unsupported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 测试异步执行方法在同一文档中以稳定顺序注释和删除。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CommentsOutAndDeletesInStableOrderWithinSameDocument()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    int count = 1;
                    int next = count;
                }
            }
            """;

        var deleteSpan = source.IndexOf("int count = 1;", StringComparison.Ordinal);
        var commentSpan = source.IndexOf("int next = count;", StringComparison.Ordinal);
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, deleteSpan, "int count = 1;".Length, "int count = 1;"),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("delete-rule", "delete reason")),
                new PlannedChange(
                    1,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Update()"), MemberKind.Method, TargetKind.Statement, commentSpan, "int next = count;".Length, "int next = count;"),
                    new PlanAction(PlanActionKind.CommentOut),
                    new PlanReason("comment-rule", "comment reason"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        Assert.DoesNotContain("int count = 1;", result.RewrittenSource);
        Assert.Contains("// comment-rule: int next = count;", result.RewrittenSource);
    }

    /// <summary>
    /// 测试异步执行方法在目标类型为方法时删除整个方法。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeletesWholeMethodWhenTargetKindIsMethod()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                }

                private void Run()
                {
                    int count = 1;
                }
            }
            """;

        var methodText = """
            private void Run()
            {
                int count = 1;
            }
            """.Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Run()"), MemberKind.Method, TargetKind.Method, source.IndexOf("private void Run()", StringComparison.Ordinal), methodText.Length, methodText),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("function-mark", "method delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("private void Run()", result.RewrittenSource);
    }

    /// <summary>
    /// 测试异步执行方法在目标类型为方法时向空方法体添加返回。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AddsReturnIntoEmptyMethodBodyWhenTargetKindIsMethod()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                private int Compute()
                {
                }
            }
            """;

        var methodText = """
            private int Compute()
            {
            }
            """.Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.Compute()"), MemberKind.Method, TargetKind.Method, source.IndexOf("private int Compute()", StringComparison.Ordinal), methodText.Length, methodText),
                    new PlanAction(PlanActionKind.AddReturn, "0"),
                    new PlanReason("function-mark", "method add return"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("return 0;", result.RewrittenSource);
    }

    /// <summary>
    /// 测试异步执行方法在目标类型为类时删除整个类。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeletesWholeClassWhenTargetKindIsClass()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                private class CacheEntry
                {
                    public int Value { get; set; }
                }
            }
            """;

        var classText = """
            private class CacheEntry
            {
                public int Value { get; set; }
            }
            """.Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget("Sample.cs", new MemberId("Sample.Player.CacheEntry"), MemberKind.Class, TargetKind.Class, source.IndexOf("private class CacheEntry", StringComparison.Ordinal), classText.Length, classText),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("class-mark", "class delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("private class CacheEntry", result.RewrittenSource);
    }
}
