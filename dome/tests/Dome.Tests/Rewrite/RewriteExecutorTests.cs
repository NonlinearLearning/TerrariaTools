using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rewrite.Roslyn;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

public class RewriteExecutorTests
{
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
}
