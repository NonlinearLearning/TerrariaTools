using Domain.Rewrite.Artifacts;
using Logic.Rewrite;
using Xunit;

namespace Isolation.AnalysisTests.Rewrite;

public sealed class RoslynCodeIsolationConventionsTests
{
    [Fact]
    public void NamingRules_buildStableNames()
    {
        Assert.Equal("Demo.Run", RoslynCodeIsolationConventions.BuildMemberTargetName("Demo", "Run"));
        Assert.Equal("DemoShadow", RoslynCodeIsolationConventions.BuildShadowClassName("Demo"));
        Assert.Equal("DemoRuntimeClosure", RoslynCodeIsolationConventions.BuildRuntimeClosureClassName("Demo"));
    }

    [Fact]
    public void ErrorMessages_includeTargetNames()
    {
        Assert.Contains("Demo", RoslynCodeIsolationConventions.BuildClassNotFoundMessage("Demo"));
        Assert.Contains("Demo.Run", RoslynCodeIsolationConventions.BuildMethodNotFoundMessage("Demo", "Run"));
        Assert.Contains("TRUSTED_PLATFORM_ASSEMBLIES", RoslynCodeIsolationConventions.BuildTrustedPlatformAssembliesMissingMessage());
    }

    [Fact]
    public void AddCompletedDiagnostic_appendsStandardDiagnostic()
    {
        CodeRewriteResult result = CodeRewriteResult.Create(
            CodeRewriteKind.DeleteMethod,
            "Demo.Run",
            "class Demo { }",
            true);

        CodeRewriteResult updated = RoslynCodeIsolationConventions.AddCompletedDiagnostic(result);

        Assert.Contains(RoslynCodeIsolationConventions.RewriteCompletedDiagnostic, updated.Diagnostics);
    }
}
