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
    // Rule family: ISeedRule
    // Direct behavior: a statement with a dome:delete directive emits a direct statement delete.
    // Propagation: the direct decision remains a propagation source for downstream use/def statements.
    // Blocking: propagation may later stop because of sanitization, redefinition, or protection rules.
    // Boundary promotion: the direct statement delete remains eligible for boundary promotion.
    [Fact]
    public async Task DirectiveSeedRule_MarksStatementWithDeleteDirective()
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

    // Rule family: ISeedRule
    // Direct behavior: statements without a directive do not emit direct mark decisions.
    // Propagation: without a direct decision there is no propagation source.
    // Blocking: not applicable because the rule never matches.
    // Boundary promotion: not applicable because no statement delete is emitted.
    [Fact]
    public async Task DirectiveSeedRule_DoesNotMarkStatementWithoutDirective()
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
                            int count = value;
                            int next = count;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Empty(decisions);
    }

    /// <summary>
    /// 测试执行方法不为高风险成员发出决策。
    /// </summary>
    // Rule family: IProtectionRule
    // Direct behavior: high-risk targets do not emit direct decisions even if a directive is present.
    // Propagation: protected high-risk targets do not become propagation sources or propagation targets.
    // Blocking: protection takes precedence over directive matching.
    // Boundary promotion: protected high-risk targets do not produce promotable statement deletes.
    [Fact]
    public async Task HighRiskProtectionRule_BlocksPropagationIntoProtectedTarget()
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
    // Rule family: propagation semantics
    // Direct behavior: the seed statement still emits a direct delete.
    // Propagation: propagation stops once a clean redefinition replaces the tainted symbol.
    // Blocking: clean redefinition clears taint for later uses in the same snapshot.
    // Boundary promotion: no later statement delete should remain for promotion after the reset point.
    [Fact]
    public async Task Propagation_StopsAfterCleanRedefinition()
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
    // Rule family: IPropagationRule
    // Direct behavior: the seed statement emits a direct delete and the first dependent statement is marked.
    // Propagation: sanitizing assignment becomes the terminal point for taint propagation.
    // Blocking: the sanitizing assignment prevents later uses from inheriting the delete.
    // Boundary promotion: statements after sanitization should not produce promotable delete decisions.
    [Fact]
    public async Task SanitizationPropagationRule_StopsPropagationAfterSanitizingAssignment()
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
    // Rule family: IProtectionRule
    // Direct behavior: object initializer assignments do not produce direct mark decisions.
    // Propagation: initializer targets are excluded from the propagation graph.
    // Blocking: the protection rule blocks both direct marking and downstream propagation through the initializer.
    // Boundary promotion: no protected initializer statement can participate in promotion.
    [Fact]
    public async Task ObjectInitializerProtectionRule_DoesNotMarkInitializerAssignment()
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

    [Fact]
    public async Task Execute_UsesExplicitStatementScopeModeFromExecutionContext()
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
        var patchedContext = AnalysisContext.Create(context.Snapshot, context.Services with { Statements = recordingStatements });
        var seedTarget = Assert.Single(analysis.View.Targets.Where(target => target.Target.DisplayText == "int child = parent;"));

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(
            patchedContext.Snapshot,
            patchedContext.Services,
            new RuleExecutionContext("MarkingRuleEngineTests", seedTarget.Target, StatementScopeMode.ParentBlockPiercing, CancellationToken.None, "explicit scope"));

        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int next = child;");
        Assert.Contains(
            recordingStatements.Calls,
            call => call.TargetKey == seedTarget.Target.TargetKey && call.ScopeMode == StatementScopeMode.ParentBlockPiercing);
    }

    [Fact]
    public async Task Execute_CompatibilityContextMatchesExplicitExecution()
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
        var rules = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault());
        var compatibilityDecisions = rules.Execute(context);
        var explicitDecisions = rules.Execute(
            context.Snapshot,
            context.Services,
            new RuleExecutionContext("MarkingRuleEngineTests", null, StatementScopeMode.MinimalBlock, CancellationToken.None, "equivalence"));

        Assert.Equal(
            compatibilityDecisions.Select(ToDecisionSignature).OrderBy(value => value, StringComparer.Ordinal),
            explicitDecisions.Select(ToDecisionSignature).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Execute_PreservesOtherDirectDecisionsWithinSameSnapshot()
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
                            // dome:delete
                            int next = count;
                            int final = next;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);

        Assert.Equal(3, decisions.Count);
        Assert.Equal(1, decisions.Count(decision => decision.Reason.RuleId == "dome:delete"));
        Assert.Equal(1, decisions.Count(decision => decision.Reason.RuleId == "expression-mark"));
        Assert.Contains(decisions, decision => decision.Target.DisplayText == "int final = next;" && decision.Reason.RuleId == "dataflow-propagation");
    }

    [Fact]
    public async Task BoundaryPromotionEngine_DoesNotDuplicateExistingMethodDelete()
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
        var statementDecision = Assert.Single(
            new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault())
                .Execute(analysis.View)
                .Where(decision => decision.Target.TargetKind == TargetKind.Statement));
        var methodTarget = context.FunctionIndex.NodesByMemberId.Values.Single(function => function.MemberId.Value == "Sample.Player.fun2(int)");
        var existingMethodDelete = MarkDecision.ForTarget(
            new PlanTarget(
                methodTarget.DocumentPath,
                methodTarget.MemberId,
                methodTarget.MemberKind,
                TargetKind.Method,
                methodTarget.SpanStart,
                methodTarget.SpanLength,
                methodTarget.DisplayName),
            PlanActionKind.Delete,
            "existing-delete",
            "already deleted");

        var promoted = new BoundaryPromotionEngine(MarkingRuleRegistry.CreateDefault()).Promote(
            context,
            new[] { statementDecision, existingMethodDelete },
            context.View.Targets.ToDictionary(target => target.Target.TargetKey, StringComparer.Ordinal));

        Assert.Empty(promoted);
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
    public async Task Execute_DoesNotDeleteNestedClassReferencedOnlyFromMethodBody()
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
                            var beam = new DrillBeam();
                            beam.Fire();
                        }

                        private class DrillBeam
                        {
                            public void Fire()
                            {
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.Player.DrillBeam");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteNestedClassReferencedThroughGenericFieldType()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    public class Film
                    {
                        private readonly List<Sequence> _sequences = new();

                        private class Sequence
                        {
                            public int Duration { get; set; }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.Film.Sequence");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteGenericHelperMethodCalledFromSameType()
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
                        public bool Update(string text)
                        {
                            return Parse(text, out var value);
                        }

                        private static bool Parse<T>(string text, out T value)
                        {
                            value = default!;
                            return !string.IsNullOrEmpty(text);
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.Parse(string, T)");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteHelperMethodCalledFromStaticFieldInitializer()
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

                    public class Profiles
                    {
                        private static readonly object[] Entries =
                        {
                            Build("Guide"),
                        };

                        private static object Build(string key)
                        {
                            return key;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Profiles.Build(string)");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteHelperMethodReferencedAsMethodGroupInStaticFieldInitializer()
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

                    public sealed class ParticlePool<T>
                    {
                        public ParticlePool(int count, System.Func<T> factory)
                        {
                        }
                    }

                    public sealed class Particle
                    {
                    }

                    public class Profiles
                    {
                        private static readonly ParticlePool<Particle> Pool = new(16, GetNewParticle);

                        private static Particle GetNewParticle()
                        {
                            return new Particle();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Profiles.GetNewParticle()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteHelperMethodCalledFromImplicitOperator()
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

                    public sealed class WeightedRandom<T>
                    {
                        public static implicit operator T(WeightedRandom<T> random)
                        {
                            return random.Get();
                        }

                        private T Get()
                        {
                            return default!;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.WeightedRandom<T>.Get()");
    }

    [Fact]
    public async Task Execute_DoesNotDeletePrivateMainEntrypoint()
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

                    public static class Program
                    {
                        private static void Main(string[] args)
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
            decision.Target.MemberId.Value == "Sample.Program.Main(string[])");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteHelperMethodCalledFromConstructorInitializer()
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

                    public class Base
                    {
                        protected Base(int value)
                        {
                        }
                    }

                    public sealed class Demo : Base
                    {
                        public Demo(string name) : base(GetWeight(name))
                        {
                        }

                        private static int GetWeight(string name)
                        {
                            return name.Length;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Demo.GetWeight(string)");
    }

    [Fact]
    public async Task Execute_DoesNotDeletePrivateExtensionMethodCalledWithReducedSyntax()
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

                    public sealed class Tile
                    {
                    }

                    public static class Minecart
                    {
                        public static int Read(Tile tile)
                        {
                            return tile.FrontTrack();
                        }

                        private static int FrontTrack(this Tile tile)
                        {
                            return 1;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Minecart.FrontTrack(Sample.Tile)");
    }

    // Rule family: IExpressionProjectionRule
    // Direct behavior: expression-bearing statements with a directive project the expression match to the containing statement.
    // Propagation: the projected statement decision acts as a direct decision and may propagate to downstream use/def targets.
    // Blocking: high-risk and object-initializer statement targets are excluded from projection.
    // Boundary promotion: projected statement deletes remain eligible for boundary promotion.
    [Fact]
    public async Task ExpressionProjectionRule_ProjectsDeleteToContainingStatement()
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
        Assert.DoesNotContain(
            decisions,
            item => item.Target.DisplayText == "bool allowed = Run(value) && (value > 0);" && item.Reason.RuleId == "dome:delete");
        Assert.Contains("InvocationExpression", decision.Reason.RelatedSymbolNames!);
    }

    // Rule family: IExpressionProjectionRule
    // Direct behavior: projection only applies to the statement that actually carries the marked expression seed.
    // Propagation: only the correctly projected statement may become a propagation source.
    // Blocking: similar expression-bearing statements without the matching directive must not be projected.
    // Boundary promotion: only the correctly projected statement may later participate in promotion.
    [Fact]
    public async Task ExpressionProjectionRule_DoesNotProjectAcrossDifferentStatement()
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
                            bool fallback = Check(value) && (value < 10);
                            return allowed || fallback;
                        }

                        private bool Run(int value) => value > 0;
                        private bool Check(int value) => value < 10;
                    }
                    """)
            },
            CancellationToken.None);

        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(analysis.View);
        var expressionMarks = decisions.Where(decision => decision.Reason.RuleId == "expression-mark").ToArray();

        var projected = Assert.Single(expressionMarks);
        Assert.Equal("bool allowed = Run(value) && (value > 0);", projected.Target.DisplayText);
        Assert.DoesNotContain(expressionMarks, decision => decision.Target.DisplayText == "bool fallback = Check(value) && (value < 10);");
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

    [Fact]
    public async Task Execute_DoesNotDeleteEventSubscribedPrivateMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System;

                    namespace Sample;

                    public sealed class Player
                    {
                        private event Action? Changed;

                        public Player()
                        {
                            Changed += HandleChanged;
                        }

                        private void HandleChanged()
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
            decision.Target.MemberId.Value == "Sample.Player.HandleChanged()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteDelegateAssignedPrivateMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System;

                    namespace Sample;

                    public sealed class Player
                    {
                        private readonly Action _handler;

                        public Player()
                        {
                            _handler = HandleChanged;
                        }

                        private void HandleChanged()
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
            decision.Target.MemberId.Value == "Sample.Player.HandleChanged()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteKnownFrameworkEntrypointMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Terraria.WorldBuilding
                    {
                        public abstract class GenPass
                        {
                        }
                    }

                    namespace Sample;

                    public sealed class DemoPass : Terraria.WorldBuilding.GenPass
                    {
                        private void Apply()
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
            decision.Target.MemberId.Value == "Sample.DemoPass.Apply()");
    }

    [Fact]
    public async Task Execute_DeletesSameNamedMethodOutsideKnownFrameworkType()
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

                    public sealed class DemoPass
                    {
                        private void Apply()
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
            decision.Target.MemberId.Value == "Sample.DemoPass.Apply()" &&
            decision.Reason.RuleId == "function-mark");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteClassRegisteredInStaticManager()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    internal sealed class NodeRule
                    {
                        public void Apply()
                        {
                        }
                    }

                    public static class RuleManager
                    {
                        private static readonly List<NodeRule> Rules = new() { new NodeRule() };
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.NodeRule");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteClassRegisteredViaGenericRegister()
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

                    public sealed class NetManager
                    {
                        public static NetManager Instance { get; } = new();

                        public void Register<T>()
                        {
                        }
                    }

                    internal sealed class NetLiquidModule
                    {
                    }

                    public static class Bootstrap
                    {
                        public static void Load()
                        {
                            NetManager.Instance.Register<NetLiquidModule>();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.NetLiquidModule");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteClassRegisteredViaManagerIndexer()
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

                    public sealed class SkyManager
                    {
                        public static SkyManager Instance { get; } = new();

                        public object this[string key]
                        {
                            set
                            {
                            }
                        }
                    }

                    internal sealed class PartySky
                    {
                        private void Draw()
                        {
                        }
                    }

                    public static class Bootstrap
                    {
                        public static void Load()
                        {
                            SkyManager.Instance["Party"] = new PartySky();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.PartySky");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteRuleNodeAddedToComposerChain()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    public interface IItemDropRule
                    {
                        bool CanDrop();
                    }

                    internal sealed class DropRule : IItemDropRule
                    {
                        public bool CanDrop()
                        {
                            return true;
                        }
                    }

                    public sealed class LeadingConditionRule
                    {
                        private readonly List<IItemDropRule> ChainedRules = new();

                        public void Add(IItemDropRule rule)
                        {
                            ChainedRules.Add(rule);
                        }
                    }

                    public static class Bootstrap
                    {
                        public static LeadingConditionRule Create()
                        {
                            var chain = new LeadingConditionRule();
                            chain.Add(new DropRule());
                            return chain;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.DropRule");
    }

    [Fact]
    public async Task Execute_DeletesTypeAddedOnlyToLocalTemporaryList()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    internal sealed class TempRule
                    {
                    }

                    public static class Bootstrap
                    {
                        public static void Build()
                        {
                            var local = new List<TempRule>();
                            local.Add(new TempRule());
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.TempRule" &&
            decision.Reason.RuleId == "class-mark");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteKnownFrameworkShutdownMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Terraria.Social.Base
                    {
                        public abstract class NetSocialModule
                        {
                        }
                    }

                    namespace Sample;

                    public sealed class DemoModule : Terraria.Social.Base.NetSocialModule
                    {
                        private void Shutdown()
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
            decision.Target.MemberId.Value == "Sample.DemoModule.Shutdown()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteKnownFrameworkDrawMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Terraria.WorldBuilding
                    {
                        public abstract class GenShape
                        {
                        }
                    }

                    namespace Sample;

                    public sealed class DemoShape : Terraria.WorldBuilding.GenShape
                    {
                        private void Draw()
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
            decision.Target.MemberId.Value == "Sample.DemoShape.Draw()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteKnownFrameworkApplyPassMethod()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Terraria.WorldBuilding
                    {
                        public abstract class GenPass
                        {
                        }
                    }

                    namespace Sample;

                    public sealed class DemoPass : Terraria.WorldBuilding.GenPass
                    {
                        private void ApplyPass()
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
            decision.Target.MemberId.Value == "Sample.DemoPass.ApplyPass()");
    }

    [Fact]
    public async Task Execute_DoesNotDeleteMethodCachedInDelegateDictionary()
    {
        var engine = new RoslynAnalysisEngine();
        var analysis = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    using System;
                    using System.Collections.Generic;

                    namespace Sample;

                    public sealed class Player
                    {
                        private readonly Dictionary<int, Func<bool>> _spawnConditions = new();

                        public Player()
                        {
                            _spawnConditions[1] = CanSpawn;
                        }

                        private bool CanSpawn()
                        {
                            return true;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.Player.CanSpawn()");
    }

    [Fact]
    public async Task Execute_DeletesUnreferencedPrivateHelperInsideRegisteredType()
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

                    public sealed class NetManager
                    {
                        public static NetManager Instance { get; } = new();

                        public void Register<T>()
                        {
                        }
                    }

                    internal sealed class NetLiquidModule
                    {
                        private void Helper()
                        {
                        }
                    }

                    public static class Bootstrap
                    {
                        public static void Load()
                        {
                            NetManager.Instance.Register<NetLiquidModule>();
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Class &&
            decision.Target.MemberId.Value == "Sample.NetLiquidModule");
        Assert.Contains(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.NetLiquidModule.Helper()" &&
            decision.Reason.RuleId == "function-mark");
    }

    [Fact]
    public async Task Execute_DeletesSameNamedFrameworkMethodOutsideKnownFrameworkType()
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

                    public sealed class DemoWidget
                    {
                        private void Shutdown()
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
            decision.Target.MemberId.Value == "Sample.DemoWidget.Shutdown()" &&
            decision.Reason.RuleId == "function-mark");
    }

    [Fact]
    public async Task Execute_DoesNotDeletePrivateMethodReferencedByExpressionBodiedProperty()
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

                    public sealed class WorldGenRange
                    {
                        public int Minimum { get; set; }

                        public int ScaledMinimum => ScaleValue(Minimum);

                        private int ScaleValue(int value)
                        {
                            return value * 2;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(analysis);
        var decisions = new MarkingRuleEngine(MarkingRuleRegistry.CreateDefault()).Execute(context);

        Assert.DoesNotContain(decisions, decision =>
            decision.Target.TargetKind == TargetKind.Method &&
            decision.Target.MemberId.Value == "Sample.WorldGenRange.ScaleValue(int)" &&
            decision.Reason.RuleId == "function-mark");
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

    private static string ToDecisionSignature(MarkDecision decision) =>
        string.Join(
            "|",
            decision.Target.TargetKey,
            decision.Action.Kind,
            decision.Reason.RuleId,
            decision.Reason.SourceTargetKey ?? string.Empty,
            decision.Action.Payload ?? string.Empty);
}
