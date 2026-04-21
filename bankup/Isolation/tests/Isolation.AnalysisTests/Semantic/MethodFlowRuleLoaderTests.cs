using Domain.Analysis.Engine.Semantic.Flows;
using Xunit;

namespace Isolation.AnalysisTests.Semantic;

public sealed class MethodFlowRuleLoaderTests : IDisposable
{
    private readonly string tempDirectory;

    public MethodFlowRuleLoaderTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"analysis-semantics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void LoadFromFile_readsRulesAndAllowsLookupByMethodFullName()
    {
        string filePath = Path.Combine(tempDirectory, "semantics.txt");
        File.WriteAllText(
            filePath,
            """
            Demo.Flow.Clean(string) ARG[1] RET
            Demo.Flow.Copy(string,string) ARG[1] RET
            Demo.Flow.Copy(string,string) ARG[2] RET
            """);

        MethodFlowRuleSet ruleSet = MethodFlowRuleLoader.LoadFromFile(filePath);

        IReadOnlyList<MethodFlowRule> cleanRules = ruleSet.GetRules("Demo.Flow.Clean(string)");
        IReadOnlyList<MethodFlowRule> copyRules = ruleSet.GetRules("Demo.Flow.Copy(string,string)");

        MethodFlowRule cleanRule = Assert.Single(cleanRules);
        Assert.Equal(FlowEndpointKind.Argument, cleanRule.Source.Kind);
        Assert.Equal(1, cleanRule.Source.ArgumentIndex);
        Assert.Equal(FlowEndpointKind.Return, cleanRule.Target.Kind);

        Assert.Equal(2, copyRules.Count);
        Assert.All(copyRules, rule => Assert.Equal(FlowEndpointKind.Return, rule.Target.Kind));
    }

    [Fact]
    public void FullNameSemantics_matchesExactAndRegexMethodNames()
    {
        FullNameSemanticsParser parser = new();
        IReadOnlyList<MethodFlowRule> rules = parser.Parse(
            """
            Demo.Flow.Clean(string) ARG[1] RET
            regex:^Demo\.Generated\..*$ ARG[1] RET
            """);
        FullNameSemantics semantics = FullNameSemantics.FromRules(rules);

        Domain.Analysis.Engine.Core.CpgNode exactMethod = new(1, Domain.Analysis.Engine.Core.CpgNodeKind.Method);
        exactMethod.SetProperty("FullName", "Demo.Flow.Clean(string)");
        Domain.Analysis.Engine.Core.CpgNode regexMethod = new(2, Domain.Analysis.Engine.Core.CpgNodeKind.Method);
        regexMethod.SetProperty("FullName", "Demo.Generated.Copy(string)");

        Assert.Single(semantics.ForMethod(exactMethod));
        Assert.Single(semantics.ForMethod(regexMethod));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
