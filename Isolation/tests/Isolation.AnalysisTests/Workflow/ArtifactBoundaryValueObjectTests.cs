using Domain.Propagation;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class ArtifactBoundaryValueObjectTests
{
    [Fact]
    public void ShadowBoundary_deduplicates_reference_mappings()
    {
        ShadowBoundary boundary = ShadowBoundary.Create();

        boundary.AddReferenceMapping(new ReferenceMapping("PlayerTools.Entry", "PlayerTools.Helper"));
        boundary.AddReferenceMapping(new ReferenceMapping(" PlayerTools.Entry ", " PlayerTools.Helper "));

        ReferenceMapping mapping = Assert.Single(boundary.ReferenceMappings);
        Assert.Equal("PlayerTools.Entry", mapping.SourceReference);
        Assert.Equal("PlayerTools.Helper", mapping.TargetReference);
    }

    [Fact]
    public void RuntimeClosureBoundary_tracks_root_integrity_and_reference_mappings()
    {
        RuntimeClosureBoundary boundary = RuntimeClosureBoundary.Create(
            new ClosureRoot(" PlayerTools ", " Entry "));

        boundary.AddReferenceMapping(new ReferenceMapping("PlayerTools.Entry", "PlayerTools.Helper"));
        boundary.AddReferenceMapping(new ReferenceMapping(" PlayerTools.Entry ", " PlayerTools.Helper "));
        boundary.MarkIntegrity(ClosureIntegrityStatus.Verified);

        Assert.Equal("PlayerTools", boundary.Root.ClassName);
        Assert.Equal("Entry", boundary.Root.MemberName);
        Assert.Equal(ClosureIntegrityStatus.Verified, boundary.IntegrityStatus);
        Assert.Single(boundary.ReferenceMappings);
    }
}
