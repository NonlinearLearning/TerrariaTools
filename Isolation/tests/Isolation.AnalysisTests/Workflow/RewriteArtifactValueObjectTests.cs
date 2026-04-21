using Domain.Rewrite.Artifacts;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewriteArtifactValueObjectTests
{
    [Fact]
    public void Rewrite_artifact_types_normalize_inputs_and_keep_string_projection()
    {
        RewriteArtifactSource source = new("class Demo {}");
        MemberSliceArtifact memberSlice = new("  PlayerTools  ", "  Entry  ", source);
        ShadowClassArtifact shadowClass = new("  PlayerTools  ", "  PlayerToolsShadow  ", source);
        RuntimeClosureArtifact runtimeClosure = new(
            "  PlayerTools  ",
            "  Entry  ",
            "  PlayerToolsRuntimeClosure  ",
            source);
        CodeRewriteArtifact rewrite = new(
            CodeRewriteKind.DeleteMethod,
            "  PlayerTools.Entry  ",
            source,
            true);

        Assert.Equal("class Demo {}", source.SourceCode);
        Assert.Equal("PlayerTools", memberSlice.ClassName);
        Assert.Equal("Entry", memberSlice.RootMemberName);
        Assert.Equal("PlayerTools", shadowClass.ClassName);
        Assert.Equal("PlayerToolsShadow", shadowClass.ShadowClassName);
        Assert.Equal("PlayerTools", runtimeClosure.ClassName);
        Assert.Equal("Entry", runtimeClosure.RootMethodName);
        Assert.Equal("PlayerToolsRuntimeClosure", runtimeClosure.ClosureClassName);
        Assert.Equal("PlayerTools.Entry", rewrite.TargetName);
        Assert.Equal("PlayerTools.Entry", rewrite.TargetNameValue.Value);
        Assert.True(rewrite.Changed);
    }
}
