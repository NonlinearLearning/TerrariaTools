using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Rules;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules;

/// <summary>
/// 标记规则引擎测试类。
/// </summary>
public class MarkingRuleEngineTests
{
    /// <summary>
    /// 测试执行方法在 Use-Def 链中传播标记决策。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法不为高风险成员发出决策。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法在干净的重新定义后停止传播。
    /// </summary>
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
    public async Task Execute_DoesNotPropagateAcrossParentBlockByDefault()
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
                        public void Update(int seed)
                        {
                            // dome:delete
                            int parent = seed;
                            {
                                int child = parent;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int parent = seed;");
        Assert.DoesNotContain(decisions, decision => decision.Target.DisplayText == "int child = parent;" && decision.Reason.RuleId == "dataflow-propagation");
    }

    /// <summary>
    /// 测试执行方法从参数传播但不跨越到不相关的局部变量。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法构建多跳传播链。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法不为接口属性访问器发出决策。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法不为重写属性访问器发出决策。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法不跨文档传播。
    /// </summary>
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

    /// <summary>
    /// 测试执行方法对 if 语句使用控制流规则。
    /// </summary>
    [Fact]
    public async Task Execute_UsesControlFlowRuleForIfStatements()
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
                            if (value > 0)
                            {
                                return value;
                            }

                            return 0;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);
        var ifDecision = Assert.Single(decisions.Where(decision => decision.Target.DisplayText.StartsWith("if (value > 0)", StringComparison.Ordinal)));

        Assert.Equal("controlflow-mark", ifDecision.Reason.RuleId);
        Assert.Equal(PlanActionKind.Delete, ifDecision.Action.Kind);
    }

    /// <summary>
    /// 测试执行方法不传播超过消毒赋值。
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotPropagatePastSanitizingAssignments()
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
                            next = 0;
                            int final = next;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int next = count;");
        Assert.DoesNotContain(decisions, decision => decision.Target.DisplayText == "int final = next;");
    }

    /// <summary>
    /// 测试执行方法阻止对象初始化器目标。
    /// </summary>
    [Fact]
    public async Task Execute_BlocksObjectInitializerTargets()
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

                    public class Item
                    {
                        public int Value { get; set; }
                    }

                    public class Player
                    {
                        public void Update(int seed)
                        {
                            // dome:delete
                            var item = new Item { Value = seed };
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Empty(decisions);
    }

    /// <summary>
    /// 测试执行方法为未引用的私有方法发出删除。
    /// </summary>
    [Fact]
    public async Task Execute_EmitsDeleteForUnreferencedPrivateMethod()
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
                        }

                        private void Run()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.Run()" &&
            decision.Action.Kind == PlanActionKind.Delete &&
            decision.Reason.RuleId == "function-mark");
    }

    /// <summary>
    /// 测试执行方法为引用的空非 void 方法发出添加返回。
    /// </summary>
    [Fact]
    public async Task Execute_EmitsAddReturnForReferencedEmptyNonVoidMethod()
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
                        public int Update()
                        {
                            return Compute();
                        }

                        private int Compute()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.Compute()" &&
            decision.Action.Kind == PlanActionKind.AddReturn &&
            decision.Action.Payload == "0");
    }

    [Fact]
    public async Task Execute_PromotesDeletedInvocationToMethodDeleteWhenItIsTheOnlyReference()
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
                        public void Update(int value)
                        {
                            // dome:delete
                            fun2(value);
                        }

                        private void fun2(int i)
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        var promoted = Assert.Single(decisions.Where(decision =>
            decision.Reason.RuleId == "boundary-promotion" &&
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.fun2(int)"));
        Assert.NotNull(promoted.Reason.SourceTargetKey);
        Assert.Equal("fun2(value);", promoted.Reason.SourceTargetDisplayText);
        Assert.Equal("Sample.Player.Update(int)", promoted.Reason.SourceMemberId);
        Assert.Equal(BoundaryKind.Invocation, promoted.Reason.BoundaryKind);
        Assert.Contains("Sample.Player.fun2(int)", promoted.Reason.TriggeredSymbolKeys!);
    }

    [Fact]
    public async Task Execute_UsesParentBlockPiercingForSeedThatReadsParentScopeSymbol()
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
                        public void Update(int seed)
                        {
                            int parent = seed;
                            {
                                // dome:delete
                                int child = parent;
                                int next = child;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var recordingStatements = new RecordingStatementAnalysisService(context.Statements);
        var patchedServices = context.Services with { Statements = recordingStatements };
        var patchedContext = AnalysisContext.Create(context.Snapshot, patchedServices);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(patchedContext);

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int child = parent;");

        var childTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int child = parent;"));
        Assert.Contains(
            recordingStatements.Calls,
            call => call.TargetKey == childTarget.Target.TargetKey && call.ScopeMode == StatementScopeMode.ParentBlockPiercing);
    }

    [Fact]
    public async Task Execute_AcceptsCatalogServicesAndExecutionContext()
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

        var context = engine.CreateContext(analysis);
        var seedTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int count = 1;"));

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(
            context.Snapshot,
            context.Services,
            new RuleExecutionContext("MarkingRuleEngineTests", seedTarget.Target, StatementScopeMode.MinimalBlock, CancellationToken.None, "verify explicit context"));

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int count = 1;");
        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int next = count;");
    }

    /// <summary>
    /// 测试执行方法不删除重写方法。
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotDeleteOverrideMethod()
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
                        public abstract int Compute();
                    }

                    public class Player : PlayerBase
                    {
                        public override int Compute()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.Compute()");
    }

    /// <summary>
    /// 测试执行方法为未引用的嵌套类发出删除。
    /// </summary>
    [Fact]
    public async Task Execute_EmitsDeleteForUnreferencedNestedClass()
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
                        private class CacheEntry
                        {
                            public int Value { get; set; }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.Player.CacheEntry" &&
            decision.Action.Kind == PlanActionKind.Delete &&
            decision.Reason.RuleId == "class-mark");
    }

    /// <summary>
    /// 测试执行方法不删除引用的类。
    /// </summary>
    [Fact]
    public async Task Execute_DoesNotDeleteReferencedClass()
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
                        private class CacheEntry
                        {
                            public int Value { get; set; }
                        }

                        private readonly CacheEntry _entry = new();
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.Player.CacheEntry");
    }

    [Fact]
    public async Task Execute_ProjectsExpressionSeedsToStatementDecisions()
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
                        public bool Update(int value)
                        {
                            // dome:delete
                            bool allowed = Run(value) && (value > 0);
                            return allowed;
                        }

                        private bool Run(int value) => value > 0;
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);
        var decision = Assert.Single(decisions.Where(item => item.Target.DisplayText == "bool allowed = Run(value) && (value > 0);"));

        Assert.Equal("expression-mark", decision.Reason.RuleId);
        Assert.Contains("InvocationExpression", decision.Reason.RelatedSymbolNames!);
    }

    [Fact]
    public async Task Execute_EmitsDeleteForUnreferencedTopLevelInternalClass()
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

                    class CacheEntry
                    {
                        public int Value { get; set; }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.CacheEntry" &&
            decision.Action.Kind == PlanActionKind.Delete &&
            decision.Reason.RuleId == "class-mark");
    }

    [Fact]
    public async Task Execute_DoesNotDeletePublicTopLevelClass()
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

                    public class CacheEntry
                    {
                        public int Value { get; set; }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.CacheEntry");
    }

    private sealed class RecordingStatementAnalysisService(IStatementAnalysisService inner) : IStatementAnalysisService
    {
        public List<(string TargetKey, StatementScopeMode ScopeMode)> Calls { get; } = new();

        public StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode)
        {
            Calls.Add((seedTarget.TargetKey, scopeMode));
            return inner.Analyze(seedTarget, scopeMode);
        }
    }
}
