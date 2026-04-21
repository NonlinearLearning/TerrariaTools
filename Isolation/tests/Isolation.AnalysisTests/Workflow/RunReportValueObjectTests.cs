using Domain.Output.Audit;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RunReportValueObjectTests
{
    [Fact]
    public void ReportSummary_normalizes_highlights_and_preserves_counts()
    {
        ReportSummary summary = new(1, 2, 3, "  generated from evidence  ");

        Assert.Equal(1, summary.ApprovedCount);
        Assert.Equal(2, summary.RejectedCount);
        Assert.Equal(3, summary.FailureCount);
        Assert.Equal("generated from evidence", summary.Highlights);
    }
}
