using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rewrite.Roslyn;
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    /// 测试异步执行方法在成员 ID 使用元数据参数类型时仍能解析并删除泛型方法。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeletesGenericMethodWhenMemberIdUsesMetadataParameterTypes()
    {
        var source = """
            using System.Collections.Generic;

            namespace Sample;

            public sealed class LocalizedText
            {
            }

            public class ChatCommandProcessor
            {
                private static bool ParseCommandPrefix<T>(string text, Dictionary<LocalizedText, T> commands, out string remainder, out T value)
                {
                    remainder = text;
                    value = default!;
                    return false;
                }
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var methodDeclaration = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<MethodDeclarationSyntax>());
        var methodText = methodDeclaration.ToString().Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.ChatCommandProcessor.ParseCommandPrefix(string, System.Collections.Generic.Dictionary<Sample.LocalizedText, T>, string, T)"),
                        MemberKind.Method,
                        TargetKind.Method,
                        methodDeclaration.SpanStart,
                        methodDeclaration.Span.Length,
                        methodText,
                        new TargetResolutionKey(
                            methodDeclaration.SpanStart,
                            methodDeclaration.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("function-mark", "generic method delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("ParseCommandPrefix<T>", result.RewrittenSource);
    }

    /// <summary>
    /// 测试异步执行方法在外部类型无法语义解析时仍能按规范化参数签名删除泛型方法。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeletesGenericMethodWhenExternalParameterTypesAreUnresolved()
    {
        var source = """
            using System.Collections.Generic;
            using Sample.Localization;

            namespace Sample;

            public class ChatCommandProcessor
            {
                private static bool ParseCommandPrefix<T>(string text, Dictionary<LocalizedText, T> commands, out string remainder, out T value)
                {
                    remainder = text;
                    value = default!;
                    return false;
                }
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var methodDeclaration = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<MethodDeclarationSyntax>());
        var methodText = methodDeclaration.ToString().Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.ChatCommandProcessor.ParseCommandPrefix(string, System.Collections.Generic.Dictionary<Sample.Localization.LocalizedText, T>, string, T)"),
                        MemberKind.Method,
                        TargetKind.Method,
                        methodDeclaration.SpanStart,
                        methodDeclaration.Span.Length,
                        methodText,
                        new TargetResolutionKey(
                            methodDeclaration.SpanStart,
                            methodDeclaration.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("function-mark", "generic unresolved method delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("ParseCommandPrefix<T>", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesExpressionBodiedPropertyWhenTargetKindIsMethod()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                private int Value => 42;
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var property = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<PropertyDeclarationSyntax>());
        var propertyText = property.ToString().Trim();
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Value.get"),
                        MemberKind.Accessor,
                        TargetKind.Method,
                        property.SpanStart,
                        property.Span.Length,
                        propertyText,
                        new TargetResolutionKey(property.SpanStart, property.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("function-mark", "property getter delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("private int Value => 42;", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesAccessorDeclarationWhenTargetKindIsMethod()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                private int _value;

                public int Value
                {
                    get
                    {
                        return _value;
                    }
                    set
                    {
                        _value = value;
                    }
                }
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var accessor = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<AccessorDeclarationSyntax>().Where(accessor => accessor.Keyword.Text == "get"));
        var accessorText = accessor.ToString().Trim();
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Value.get"),
                        MemberKind.Accessor,
                        TargetKind.Method,
                        accessor.SpanStart,
                        accessor.Span.Length,
                        accessorText,
                        new TargetResolutionKey(accessor.SpanStart, accessor.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("function-mark", "accessor delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("get", result.RewrittenSource);
        Assert.Contains("set", result.RewrittenSource);
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

    [Fact]
    public async Task ExecuteAsync_DeletesWholeClassWhenClassTargetUsesResolutionKey()
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

        var context = CreateRewriteContext("Sample.cs", source);
        var classDeclaration = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<ClassDeclarationSyntax>().Where(@class => @class.Identifier.Text == "CacheEntry"));
        var classText = classDeclaration.ToString().Trim();

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.CacheEntry"),
                        MemberKind.Class,
                        TargetKind.Class,
                        classDeclaration.SpanStart,
                        classDeclaration.Span.Length,
                        classText,
                        new TargetResolutionKey(classDeclaration.SpanStart, classDeclaration.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("class-mark", "class delete"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("private class CacheEntry", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesLaterClassAfterEarlierStatementDeletionUsingTrackedNodes()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Alpha()
                {
                    int count = 1;
                }

                private class CacheEntry
                {
                    public int Value { get; set; }
                }
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var root = (CompilationUnitSyntax)context.Root;
        var classDeclaration = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(@class => @class.Identifier.Text == "CacheEntry"));
        var statement = Assert.Single(root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>());

        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Alpha()"),
                        MemberKind.Method,
                        TargetKind.Statement,
                        statement.SpanStart,
                        statement.Span.Length,
                        statement.ToString().Trim(),
                        new TargetResolutionKey(statement.SpanStart, statement.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("dome:delete", "delete inner statement")),
                new PlannedChange(
                    1,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.CacheEntry"),
                        MemberKind.Class,
                        TargetKind.Class,
                        classDeclaration.SpanStart,
                        classDeclaration.Span.Length,
                        classDeclaration.ToString().Trim(),
                        new TargetResolutionKey(classDeclaration.SpanStart, classDeclaration.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("class-mark", "delete outer class"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        Assert.DoesNotContain("int count = 1;", result.RewrittenSource);
        Assert.DoesNotContain("private class CacheEntry", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_ChangesMethodVisibilityToPrivate()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                public void Helper()
                {
                }
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var method = Assert.Single(((CompilationUnitSyntax)context.Root).DescendantNodes().OfType<MethodDeclarationSyntax>());
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.Helper()"),
                        MemberKind.Method,
                        TargetKind.Method,
                        method.SpanStart,
                        method.Span.Length,
                        method.ToString().Trim(),
                        new TargetResolutionKey(method.SpanStart, method.Span.Length)),
                    new PlanAction(PlanActionKind.ChangeVisibilityToPrivate),
                    new PlanReason("method-privatization", "privatize helper"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("private void Helper()", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesFieldAndPropertyTargets()
    {
        var source = """
            namespace Sample;

            public class Player
            {
                private int _unusedField = 1;
                private int UnusedProperty { get; } = 2;
            }
            """;

        var context = CreateRewriteContext("Sample.cs", source);
        var root = (CompilationUnitSyntax)context.Root;
        var field = Assert.Single(root.DescendantNodes().OfType<VariableDeclaratorSyntax>());
        var property = Assert.Single(root.DescendantNodes().OfType<PropertyDeclarationSyntax>());
        var plan = new AuditPlan(
            new PlanMetadata("dome", "1", "input.cs", "out", RunMode.Standard),
            new[]
            {
                new PlannedChange(
                    0,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player._unusedField"),
                        MemberKind.Field,
                        TargetKind.Field,
                        field.SpanStart,
                        field.Span.Length,
                        field.ToString().Trim(),
                        new TargetResolutionKey(field.SpanStart, field.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("unused-member", "delete field")),
                new PlannedChange(
                    1,
                    new PlanTarget(
                        "Sample.cs",
                        new MemberId("Sample.Player.UnusedProperty"),
                        MemberKind.Property,
                        TargetKind.Property,
                        property.SpanStart,
                        property.Span.Length,
                        property.ToString().Trim(),
                        new TargetResolutionKey(property.SpanStart, property.Span.Length)),
                    new PlanAction(PlanActionKind.Delete),
                    new PlanReason("unused-member", "delete property"))
            },
            Array.Empty<PlanConflict>());

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(context, plan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("_unusedField", result.RewrittenSource);
        Assert.DoesNotContain("UnusedProperty", result.RewrittenSource);
    }

    private static RewriteExecutionDocumentContext CreateRewriteContext(string relativePath, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: relativePath);
        var root = tree.GetCompilationUnitRoot();
        var compilation = CSharpCompilation.Create(
            "RewriteExecutorTests",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        return new RewriteExecutionDocumentContext(
            new SourceDocument(relativePath, relativePath, source),
            root,
            compilation.GetSemanticModel(tree));
    }
}
