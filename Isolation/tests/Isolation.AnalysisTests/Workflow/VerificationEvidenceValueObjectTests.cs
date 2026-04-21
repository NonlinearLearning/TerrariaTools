using Domain.Common;
using Domain.Output.Verification;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class VerificationEvidenceValueObjectTests
{
    [Fact]
    public void VerificationEvidence_supporting_types_normalize_strings_and_keep_value_object_projection()
    {
        CompilationEvidence compilation = new(true, 0, "  编译通过  ");
        StaticReasoningEvidence staticReasoning = new(TargetName.Create("PlayerTools.Entry"), "  fact-chain  ");
        BehaviorEvidence behavior = new("  DeleteMethod  ", false, "  行为验证失败  ");

        Assert.Equal("编译通过", compilation.Summary);
        Assert.Equal("PlayerTools.Entry", staticReasoning.SubjectName);
        Assert.Equal("PlayerTools.Entry", staticReasoning.SubjectNameValue.Value);
        Assert.Equal("fact-chain", staticReasoning.Summary);
        Assert.Equal("DeleteMethod", behavior.ScenarioName);
        Assert.Equal("行为验证失败", behavior.Summary);
    }

    [Fact]
    public void RiskSummary_parses_level_name_and_derives_manual_review_flags_from_evidence()
    {
        VerificationEvidence evidence = VerificationEvidence.Create(Guid.NewGuid());
        evidence.AddCompilationEvidence(new CompilationEvidence(true, 0, "编译通过"));
        evidence.AddBehaviorEvidence(new BehaviorEvidence("DeleteMethod", false, "行为验证失败"));

        RiskSummary parsed = new(" High ", true, ["行为验证失败。"]);
        RiskSummary derived = RiskSummary.FromEvidence(evidence);

        Assert.Equal(RiskLevel.High, parsed.Level);
        Assert.Equal("High", parsed.LevelName);
        Assert.True(parsed.RequiresManualReview);
        Assert.Equal(RiskLevel.High, derived.Level);
        Assert.True(derived.RequiresManualReview);
        Assert.Contains("行为验证失败。", derived.Items);
    }
}
