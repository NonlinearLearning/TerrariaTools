using TerrariaTools.Dome.Application;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Application;

public sealed class DomeApplicationTests
{
    [Fact]
    public async Task RunAsync_WritesPlanAndReportToOutputDirectory()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Sample.cs")));
        });
    }

    [Fact]
    public async Task RunAsync_AnalyzeOnly_WritesAnalysisAndReportWithoutPlanOrRewrite()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.AnalyzeOnly),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "analysis.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "rewritten")));
        });
    }

    [Fact]
    public async Task RunAsync_PlanOnly_WritesPlanAndReportWithoutRewrite()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Sample.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.PlanOnly),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
            Assert.False(File.Exists(Path.Combine(outputDir, "analysis.json")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "rewritten")));
        });
    }

    [Fact]
    public async Task RunAsync_StandardMode_WritesRewrittenOutputsForMultipleFiles()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputDir = Path.Combine(tempRoot, "input");
            var outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(Path.Combine(inputDir, "Features"));

            await File.WriteAllTextAsync(
                Path.Combine(inputDir, "Root.cs"),
                """
                namespace Sample;

                public class RootPlayer
                {
                    public void Update()
                    {
                        // dome:delete
                        Run();
                    }

                    private void Run() { }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(inputDir, "Features", "Nested.cs"),
                """
                namespace Sample.Features;

                public class NestedPlayer
                {
                    public void Update()
                    {
                        // dome:comment
                        Run();
                    }

                    private void Run() { }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputDir, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Root.cs")));
            Assert.True(File.Exists(Path.Combine(outputDir, "rewritten", "Features", "Nested.cs")));
            Assert.True(File.Exists(Path.Combine(outputDir, "audit-plan.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
        });
    }

    [Fact]
    public async Task RunAsync_ClosedLoopDirectDelete_RewritesMarkedStatement()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Player.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public sealed class Player
                {
                    public void Update()
                    {
                        Prepare();

                        // dome:delete
                        int count = 1;

                        Keep();
                    }

                    private static void Prepare()
                    {
                    }

                    private static void Keep()
                    {
                    }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var rewritten = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.cs"));

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("int count = 1;", rewritten);
            Assert.Contains("Prepare();", rewritten);
            Assert.Contains("Keep();", rewritten);
        });
    }

    [Fact]
    public async Task RunAsync_PrivatizesInternalPublicHelper_ReordersPublicMethods_AndDeletesUnusedMethod()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Player.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
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

                    private void Unused()
                    {
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

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var rewritten = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.cs"));

            Assert.True(result.IsSuccess);
            Assert.Contains("private void Helper()", rewritten);
            Assert.DoesNotContain("private void Unused()", rewritten);
            Assert.True(rewritten.IndexOf("public void Alpha()", StringComparison.Ordinal) <
                        rewritten.IndexOf("public void Zebra()", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task RunAsync_DeletesUnusedFieldPropertyAndNestedClass()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var inputFile = Path.Combine(tempRoot, "Player.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                inputFile,
                """
                namespace Sample;

                public class Player
                {
                    private int _unusedField = 1;
                    private int UnusedProperty { get; } = 2;

                    private sealed class UnusedNested
                    {
                    }

                    public void Update()
                    {
                    }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(inputFile, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var rewritten = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.cs"));

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("_unusedField", rewritten);
            Assert.DoesNotContain("UnusedProperty", rewritten);
            Assert.DoesNotContain("UnusedNested", rewritten);
        });
    }

    [Fact]
    public async Task RunAsync_DoesNotMutateProtectedPartialInterfaceOrInheritanceMembers()
    {
        await WithTempRootAsync(async tempRoot =>
        {
            var rootFile = Path.Combine(tempRoot, "Root.cs");
            var partialFile = Path.Combine(tempRoot, "Player.Partial.cs");
            var outputDir = Path.Combine(tempRoot, "out");

            await File.WriteAllTextAsync(
                rootFile,
                """
                namespace Sample;

                public interface IRunner
                {
                    void Execute();
                }

                public abstract class BaseRunner
                {
                    public abstract void Tick();
                }

                public class MidRunner : BaseRunner
                {
                    public override void Tick()
                    {
                    }
                }

                public class LeafRunner : MidRunner
                {
                }

                public partial class Player : IRunner
                {
                    public void Execute()
                    {
                        Shared();
                    }
                }
                """);

            await File.WriteAllTextAsync(
                partialFile,
                """
                namespace Sample;

                public partial class Player
                {
                    public void Shared()
                    {
                    }
                }
                """);

            var result = await DomeApplicationFactory.CreateDefault().RunAsync(
                new RunRequest(tempRoot, outputDir, Array.Empty<string>(), RunMode.Standard),
                CancellationToken.None);

            var rewrittenRoot = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Root.cs"));
            var rewrittenPartial = await File.ReadAllTextAsync(Path.Combine(outputDir, "rewritten", "Player.Partial.cs"));

            Assert.True(result.IsSuccess);
            Assert.Contains("public override void Tick()", rewrittenRoot);
            Assert.Contains("class MidRunner : BaseRunner", rewrittenRoot);
            Assert.Contains("class LeafRunner : MidRunner", rewrittenRoot);
            Assert.Contains("public void Execute()", rewrittenRoot);
            Assert.Contains("public void Shared()", rewrittenPartial);
        });
    }

    private static async Task WithTempRootAsync(Func<string, Task> action)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            await action(tempRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
