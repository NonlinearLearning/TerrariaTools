using Domain.Execution;
using Domain.Workspaces;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewritePlanValueObjectTests
{
    [Fact]
    public void PlanMetadata_and_planTarget_normalize_trimmed_projection()
    {
        PlanMetadata metadata = new("  demo-plan  ", "  1.0.0  ", DateTimeOffset.UtcNow, "  note  ");
        PlanTarget target = new(
            DocumentPath.Create("demo.cs"),
            "  PlayerTools.Entry  ",
            "  Entry(int)  ",
            "  Entry  ");

        Assert.Equal("demo-plan", metadata.PlanName);
        Assert.Equal("1.0.0", metadata.CompilerVersion);
        Assert.Equal("note", metadata.Note);
        Assert.Equal("PlayerTools.Entry", target.TargetName);
        Assert.Equal("Entry(int)", target.MemberSignature);
        Assert.Equal("Entry", target.AnchorText);
    }

    [Fact]
    public void PlanChangeItem_deduplicates_reason_and_rejects_negative_order()
    {
        PlanChangeItem item = PlanChangeItem.Create(
            Guid.NewGuid(),
            new PlanTarget(DocumentPath.Create("demo.cs"), "PlayerTools.Entry", "Entry()", "Entry"),
            PlanAction.DeleteMethod,
            PlanReason.CandidateApproved);

        item.AddReason(PlanReason.CandidateApproved);
        item.AddReason(PlanReason.LinkedActionDetected);

        Assert.Equal(2, item.Reasons.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetOrder(-1));
    }
}
