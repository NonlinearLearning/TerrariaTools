using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

public class MarkingRuleEngineTests
{
    [Fact]
    public async Task Execute_PropagatesMarkedDecisionAcrossUseDefChain()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
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
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Equal(2, decisions.Count);
        var direct = Assert.Single(decisions.Where(decision => decision.Reason.RuleId == "dome:delete"));
        Assert.Null(direct.Chain);
        var propagated = Assert.Single(decisions.Where(decision => decision.Reason.RuleId == "dataflow-propagation"));
        Assert.NotNull(propagated.Chain);
        Assert.Equal("int count = 1;", propagated.Chain!.RootTargetDisplayText);
        Assert.Single(propagated.Chain.Hops);
        Assert.NotNull(propagated.Reason.SourceTargetKey);
        Assert.Equal("int count = 1;", propagated.Reason.SourceTargetDisplayText);
        Assert.Contains("count", propagated.Reason.RelatedSymbolNames);
        Assert.NotEmpty(propagated.Reason.RelatedSymbolKeys);
    }

    [Fact]
    public async Task Execute_DoesNotEmitDecisionsForHighRiskMembers()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public interface IPlayer
                    {
                        void Update();
                    }

                    public class Player : IPlayer
                    {
                        public void Update()
                        {
                            // dome:delete
                            Run();
                        }

                        private void Run() { }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task Execute_StopsPropagationAfterCleanRedefinition()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
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
                            count = 2;
                            int next = count;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.DoesNotContain(decisions, decision => decision.Target.DisplayText == "int next = count;");
    }

    [Fact]
    public async Task Execute_PropagatesFromParameterButDoesNotCrossIntoUnrelatedLocal()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                    public int Update(int value)
                    {
                        // dome:delete
                        int next = value;
                        int localValue = 10;
                        return localValue;
                    }
                }
                """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int next = value;");
        Assert.DoesNotContain(decisions, decision => decision.Target.DisplayText == "return localValue;");
    }

    [Fact]
    public async Task Execute_BuildsMultiHopPropagationChain()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
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
                            int final = next;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);
        var finalDecision = Assert.Single(decisions.Where(decision => decision.Target.DisplayText == "int final = next;"));

        Assert.Equal("dataflow-propagation", finalDecision.Reason.RuleId);
        Assert.NotNull(finalDecision.Chain);
        Assert.Equal("int count = 1;", finalDecision.Chain!.RootTargetDisplayText);
        Assert.Equal(2, finalDecision.Chain.Hops.Count);
        Assert.Equal("int count = 1;", finalDecision.Chain.Hops[0].FromTargetDisplayText);
        Assert.Equal("int next = count;", finalDecision.Chain.Hops[0].ToTargetDisplayText);
        Assert.Equal("int next = count;", finalDecision.Chain.Hops[1].FromTargetDisplayText);
        Assert.Equal("int final = next;", finalDecision.Chain.Hops[1].ToTargetDisplayText);
        Assert.Contains("next", finalDecision.Chain.Hops[1].Evidence.RelatedSymbolNames);
    }

    [Fact]
    public async Task Execute_DoesNotEmitDecisionsForInterfacePropertyAccessors()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public interface IPlayer
                    {
                        int Value { get; set; }
                    }

                    public class Player : IPlayer
                    {
                        private int _value;

                        public int Value
                        {
                            get => _value;
                            set
                            {
                                // dome:delete
                                _value = value;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task Execute_DoesNotEmitDecisionsForOverridePropertyAccessors()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public abstract class PlayerBase
                    {
                        public abstract int Value { get; set; }
                    }

                    public class Player : PlayerBase
                    {
                        private int _value;

                        public override int Value
                        {
                            get => _value;
                            set
                            {
                                // dome:delete
                                _value = value;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task Execute_DoesNotPropagateAcrossDocuments()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Root.cs",
                    "Root.cs",
                    """
                    namespace Sample;

                    public class RootPlayer
                    {
                        public void Update()
                        {
                            // dome:delete
                            int count = 1;
                            int next = count;
                        }
                    }
                    """),
                new SourceDocument(
                    Path.Combine("Features", "Nested.cs"),
                    Path.Combine("Features", "Nested.cs"),
                    """
                    namespace Sample.Features;

                    public class NestedPlayer
                    {
                        public void Update()
                        {
                            int count = 2;
                            int next = count;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Contains(decisions, decision => decision.Target.DocumentPath == "Root.cs" && decision.Target.DisplayText == "int count = 1;");
        Assert.Contains(decisions, decision => decision.Target.DocumentPath == "Root.cs" && decision.Target.DisplayText == "int next = count;");
        Assert.DoesNotContain(decisions, decision => decision.Target.DocumentPath == Path.Combine("Features", "Nested.cs"));
    }
}
