using ModelPlanning = TerrariaTools.Dome.Core.Planning;
using ModelCommon = TerrariaTools.Dome.Core.Common;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using PortsCommon = TerrariaTools.Dome.Application.Ports;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rewrite;

/// <summary>
/// Compatibility coverage only. Standard rewrite behavior is validated elsewhere.
/// </summary>
public sealed class RewriteExecutorCompatibilityTests
{
    [Fact]
    public async Task ExecuteAsync_FailsWhenNativeTargetCannotBeResolved()
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

        var plan = new ModelPlanning.AuditPlan(
            new ModelPlanning.PlanMetadata("dome", "1", "Sample.cs", "out", ModelCommon.RunMode.Standard),
            [
                new ModelPlanning.PlannedChange(
                    0,
                    new ModelCommon.TargetIdentity("Sample.cs", new ModelCommon.MemberId("Sample.Player.Update()"), ModelCommon.MemberKind.Method, ModelCommon.TargetKind.Statement),
                    new ModelCommon.TargetLocator(999, 3, "Run();"),
                    new ModelPlanning.PlanAction(ModelCommon.PlanActionKind.Delete),
                    new ModelPlanning.PlanReason("delete-rule", "delete reason"))
            ],
            []);

        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(source, plan, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PortsCommon.FailureCode.RewriteFailed, result.FailureCode);
    }

    [Fact]
    public async Task ExecuteAsync_CompatibilityOverload_ProjectsLegacyPlanObject()
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

        var spanStart = source.IndexOf("Run();", StringComparison.Ordinal);
        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(
            source,
            CreateLegacyPlan(spanStart, "Run();".Length, "Run();", ModelCommon.PlanActionKind.AddReturn, "1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        Assert.Contains("return 1;", result.RewrittenSource);
    }

    [Fact]
    public async Task ExecuteAsync_CompatibilityDocumentContextOverload_ProjectsLegacyDocumentContext()
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
        var executor = new RoslynRewriteExecutor();
        var result = await executor.ExecuteAsync(
            CreateLegacyDocumentContext("Sample.cs", source),
            CreateLegacyPlan(spanStart, "Run();".Length, "Run();", ModelCommon.PlanActionKind.Delete),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RewrittenSource);
        Assert.DoesNotContain("Run();", result.RewrittenSource);
    }

    private static object CreateLegacyDocumentContext(string relativePath, string source) =>
        new
        {
            Document = new
            {
                RelativePath = relativePath,
                SourceText = source
            }
        };

    private static object CreateLegacyPlan(int spanStart, int spanLength, string displayText, ModelCommon.PlanActionKind actionKind, string? payload = null) =>
        new
        {
            Metadata = new
            {
                ToolName = "dome",
                PlanVersion = "1",
                InputPath = "Sample.cs",
                OutputPath = "out",
                RunMode = ModelCommon.RunMode.Standard
            },
            Changes = new object[]
            {
                new
                {
                    ExecutionOrder = 0,
                    Target = new
                    {
                        DocumentPath = "Sample.cs",
                        MemberId = new { Value = "Sample.Player.Update()" },
                        MemberKind = ModelCommon.MemberKind.Method,
                        TargetKind = ModelCommon.TargetKind.Statement,
                        SpanStart = spanStart,
                        SpanLength = spanLength,
                        DisplayText = displayText,
                        EffectiveResolutionKey = new { SpanStart = spanStart, SpanLength = spanLength }
                    },
                    Action = new
                    {
                        Kind = actionKind,
                        Payload = payload
                    }
                }
            },
            Conflicts = Array.Empty<object>()
        };
}
