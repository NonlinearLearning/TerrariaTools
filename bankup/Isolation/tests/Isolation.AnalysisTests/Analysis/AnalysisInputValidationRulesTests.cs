using Domain.Analysis;
using Logic.Analysis;
using Xunit;

namespace Isolation.AnalysisTests.Analysis;

public sealed class AnalysisInputValidationRulesTests
{
    [Fact]
    public void IsSourceKindMatched_matchesExpectedKinds()
    {
        Assert.True(AnalysisInputValidationRules.IsSourceKindMatched(AnalysisSourceKind.Solution, "demo.sln"));
        Assert.True(AnalysisInputValidationRules.IsSourceKindMatched(AnalysisSourceKind.Project, "demo.csproj"));
        Assert.True(AnalysisInputValidationRules.IsSourceKindMatched(AnalysisSourceKind.SourceFile, "demo.cs"));
        Assert.True(AnalysisInputValidationRules.IsSourceKindMatched(AnalysisSourceKind.Directory, "src"));
        Assert.False(AnalysisInputValidationRules.IsSourceKindMatched(AnalysisSourceKind.Project, "demo.sln"));
    }

    [Fact]
    public void BuildSourceKindMismatchMessage_isStable()
    {
        string message = AnalysisInputValidationRules.BuildSourceKindMismatchMessage(
            AnalysisSourceKind.Project,
            "demo.sln");

        Assert.Contains("Project", message);
        Assert.Contains("demo.sln", message);
    }
}
