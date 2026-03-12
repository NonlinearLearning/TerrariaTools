namespace TerrariaTools.Dome.Application;

using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Reporting;
using TerrariaTools.Dome.Rewrite.Roslyn;
using TerrariaTools.Dome.Rules;

public static class DomeApplicationFactory
{
    public static DomeApplication CreateDefault()
    {
        return new DomeApplication(
            new SourceWorkspaceLoader(),
            new RoslynAnalysisEngine(),
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()),
            new RoslynRewriteExecutor(),
            new JsonArtifactWriter());
    }
}
