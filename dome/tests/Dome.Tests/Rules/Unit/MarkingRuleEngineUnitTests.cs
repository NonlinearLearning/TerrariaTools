using TerrariaTools.Dome.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelRules = TerrariaTools.Dome.Model.Rules;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class MarkingRuleEngineUnitTests
{
    // Main-entry coverage: new behavior tests should prefer BuildDecisions(Model.Analysis.AnalysisContext, ...).
    [Fact]
    public async Task BuildDecisions_UsesModelContextForDirectiveAndPropagation()
    {
        var analysis = await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ApplicationAbstractions.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ApplicationAbstractions.SourceDocument(
                        "Sample.cs",
                        "Sample.cs",
                        """
                        namespace Sample;

                        public class Player
                        {
                            public void Update()
                            {
                                // dome:delete
                                int count = 1;
                                int next = count;
                            }
                        }

                        public static class Runner
                        {
                            public static void Run()
                            {
                                new Player().Update();
                            }
                        }
                        """)
                ]),
            CancellationToken.None);

        var result = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).BuildDecisions(analysis.CreateContext(), CancellationToken.None);

        Assert.True(result.Count >= 2);
        Assert.Contains(result, decision => decision.Reason.RuleId == "dome:delete");
        Assert.Contains(result, decision => decision.Reason.RuleId == "dataflow-propagation");
    }
}
