using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;
using TerrariaTools.Dome.Core.Rules.Services;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public sealed class MarkingRuleEngineUnitTests
{
    // Main-entry coverage: new behavior tests should prefer BuildDecisions(Model.Analysis.AnalysisContext, ...).
    [Fact]
    public async Task BuildDecisions_UsesModelContextForDirectiveAndPropagation()
    {
        var analysis = await ((ApplicationAbstractions.IAnalysisEngine)new RoslynAnalysisEngine()).AnalyzeAsync(
            new ModelAnalysis.SourceDocumentSet(
                "Sample.cs",
                "Sample.cs",
                [
                    new ModelAnalysis.SourceDocument(
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
