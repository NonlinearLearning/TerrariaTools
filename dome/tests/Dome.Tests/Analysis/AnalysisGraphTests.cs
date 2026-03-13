using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 分析图测试类。
/// </summary>
public class AnalysisGraphTests
{
    /// <summary>
    /// 测试分析异步方法构建语句、函数和类型图。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_BuildsStatementFunctionAndTypeGraphs()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public interface IRunner
            {
                int Run(int seed);
            }

            public class Helper
            {
                public static int Normalize(int value) => value;
            }

            public class Worker : IRunner
            {
                private readonly Helper _helper = new();
                public int LastValue { get; private set; }

                public int Run(int seed)
                {
                    int count = seed;
                    LastValue = Helper.Normalize(count);
                    return LastValue;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var view = result.View;

        Assert.Equal(StatementGraphMaterialization.SnapshotOnly, view.StatementGraphMaterialization);
        Assert.Equal(FunctionGraphMaterialization.None, view.FunctionGraphMaterialization);
        Assert.Empty(view.StatementGraph.Nodes);
        Assert.Empty(view.StatementGraph.Edges);
        Assert.Empty(view.FunctionGraph.Nodes);
        Assert.Empty(view.FunctionGraph.Edges);
        Assert.Contains(result.FunctionIndex.NodesByMemberId.Keys, memberId => memberId == "Sample.Worker.Run(int)");

        var context = engine.CreateContext(result);
        var wholeProjectSnapshot = context.FunctionGraphs.GetWholeProjectSnapshot();
        Assert.Equal(FunctionGraphScope.WholeProject, wholeProjectSnapshot.Scope);
        Assert.Contains(wholeProjectSnapshot.Graph.Nodes, node => node.MemberId.Value == "Sample.Worker.Run(int)");
        Assert.NotEmpty(wholeProjectSnapshot.Graph.Edges);
        Assert.All(wholeProjectSnapshot.Graph.Edges, edge => Assert.Equal(FunctionDependencyKind.Calls, edge.Kind));
        Assert.Equal(new[] { "Sample.cs" }, wholeProjectSnapshot.IncludedDocumentPaths);

        Assert.Contains(view.TypeGraph.Nodes, node => node.TypeId == "Sample.Worker");
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.Implements);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.FieldType);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.PropertyType);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.ParameterType);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.ReturnType);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.ObjectCreation);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.StaticMemberAccess);
        Assert.Contains(view.TypeGraph.Edges, edge => edge.Kind == TypeDependencyKind.MemberBodyReference);
    }

    /// <summary>
    /// 测试分析异步方法为方法级规则投影方法事实。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsMethodFactsForMethodLevelRules()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                        public int Compute()
                        {
                        }

                        public void Tick()
                        {
                        }

                        private int Normalize(int value)
                        {
                            return value;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var compute = Assert.Single(result.FunctionIndex.NodesByMemberId.Values.Where(node => node.MemberId.Value == "Sample.Player.Compute()"));
        var tick = Assert.Single(result.FunctionIndex.NodesByMemberId.Values.Where(node => node.MemberId.Value == "Sample.Player.Tick()"));
        var normalize = Assert.Single(result.FunctionIndex.NodesByMemberId.Values.Where(node => node.MemberId.Value == "Sample.Player.Normalize(int)"));

        Assert.False(compute.ReturnsVoid);
        Assert.True(compute.HasBody);
        Assert.False(compute.HasStatements);
        Assert.Equal("int", compute.ReturnTypeDisplay);

        Assert.True(tick.ReturnsVoid);
        Assert.True(tick.HasBody);
        Assert.False(tick.HasStatements);
        Assert.Equal("void", tick.ReturnTypeDisplay);

        Assert.True(normalize.HasStatements);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesWorkspaceCompilationForConditionalSymbols()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            parseOptions: new CSharpParseOptions(preprocessorSymbols: new[] { "DEBUG" }),
            metadataReferences: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });
        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(
                documentId,
                "Sample.cs",
                SourceText.From(
                    """
                    #if DEBUG
                    namespace Sample;

                    public class Feature
                    {
                        public void Run()
                        {
                        }
                    }
                    #endif
                    """),
                filePath: "Sample.cs");

        workspace.TryApplyChanges(solution);
        var project = workspace.CurrentSolution.GetProject(projectId)!;
        var document = project.GetDocument(documentId)!;
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.NotNull(compilation);
        var root = await document.GetSyntaxRootAsync(CancellationToken.None);
        Assert.NotNull(root);
        var sourceText = await document.GetTextAsync(CancellationToken.None);
        var engine = new RoslynAnalysisEngine();

        var result = await engine.AnalyzeAsync(
            new WorkspaceAnalysisInput(
                workspace.CurrentSolution,
                project,
                Path.GetTempPath(),
                new[]
                {
                    new WorkspaceDocumentContext(
                        document,
                        new SourceDocument(
                            "Sample.cs",
                            "Sample.cs",
                            sourceText.ToString()),
                        compilation!,
                        compilation!.GetSemanticModel(root!.SyntaxTree),
                        (CompilationUnitSyntax)root!)
                }),
            CancellationToken.None);

        Assert.Contains(result.View.TypeGraph.Nodes, node => node.TypeId == "Sample.Feature");
        Assert.Contains(result.FunctionIndex.NodesByMemberId.Keys, memberId => memberId == "Sample.Feature.Run()");
    }

    [Fact]
    public async Task AnalyzeAsync_UsesFrozenWorkspaceRootInsteadOfLiveDocumentState()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            metadataReferences: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            });
        var originalText =
            """
            namespace Sample;

            public class Feature
            {
                public void Run()
                {
                }
            }
            """;
        var updatedText =
            """
            namespace Sample;

            public class Changed
            {
            }
            """;
        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(documentId, "Sample.cs", SourceText.From(originalText), filePath: "Sample.cs");

        workspace.TryApplyChanges(solution);
        var originalProject = workspace.CurrentSolution.GetProject(projectId)!;
        var originalDocument = originalProject.GetDocument(documentId)!;
        var compilation = await originalProject.GetCompilationAsync(CancellationToken.None);
        Assert.NotNull(compilation);
        var originalRoot = (CompilationUnitSyntax?)await originalDocument.GetSyntaxRootAsync(CancellationToken.None);
        Assert.NotNull(originalRoot);
        var semanticModel = compilation!.GetSemanticModel(originalRoot!.SyntaxTree);

        var changedSolution = workspace.CurrentSolution.WithDocumentText(documentId, SourceText.From(updatedText));
        workspace.TryApplyChanges(changedSolution);
        var changedProject = workspace.CurrentSolution.GetProject(projectId)!;
        var changedDocument = changedProject.GetDocument(documentId)!;

        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new WorkspaceAnalysisInput(
                workspace.CurrentSolution,
                changedProject,
                Path.GetTempPath(),
                new[]
                {
                    new WorkspaceDocumentContext(
                        changedDocument,
                        new SourceDocument("Sample.cs", "Sample.cs", originalText),
                        compilation,
                        semanticModel,
                        originalRoot)
                }),
            CancellationToken.None);

        Assert.Contains(result.View.TypeGraph.Nodes, node => node.TypeId == "Sample.Feature");
        Assert.DoesNotContain(result.View.TypeGraph.Nodes, node => node.TypeId == "Sample.Changed");
        Assert.Contains(result.FunctionIndex.NodesByMemberId.Keys, memberId => memberId == "Sample.Feature.Run()");
    }

    /// <summary>
    /// 测试分析异步方法投影类目标和表达式事实。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsClassTargetsAndExpressionFacts()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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

        var classTarget = Assert.Single(result.View.Targets.Where(target =>
            target.Target.TargetKind == TargetKind.Class &&
            target.Target.MemberId.Value == "Sample.Player.CacheEntry"));
        Assert.Equal("Sample.Player.CacheEntry", classTarget.Target.MemberId.Value);

        var expressionTarget = Assert.Single(result.View.Targets.Where(target => target.Target.DisplayText == "bool allowed = Run(value) && (value > 0);"));
        Assert.True(expressionTarget.HasMarkedExpressionSeed);
        Assert.Contains("InvocationExpression", expressionTarget.MarkedExpressionKinds);
        Assert.Contains("LogicalAndExpression", expressionTarget.MarkedExpressionKinds);
        Assert.Contains("ParenthesizedExpression", expressionTarget.MarkedExpressionKinds);
    }

    [Fact]
    public async Task AnalyzeAsync_ProjectsInvokedMemberIdsForDirectInvocationStatements()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            fun2(value);
                        }

                        private void fun2(int i)
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var invocationTarget = Assert.Single(result.View.Targets.Where(target => target.Target.DisplayText == "fun2(value);"));
        Assert.Equal(new[] { new MemberId("Sample.Player.fun2(int)") }, invocationTarget.InvokedMemberIds);
    }

    [Fact]
    public async Task AnalyzeAsync_TracksMinimalBlockAndParentBlockPiercingWithinMethodBoundary()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                                int child = parent;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var targets = result.View.Targets.ToArray();
        var parentTarget = Assert.Single(targets.Where(target => target.Target.DisplayText == "int parent = seed;"));
        var childTarget = Assert.Single(targets.Where(target => target.Target.DisplayText == "int child = parent;"));

        Assert.Equal(StatementScopeMode.MinimalBlock, parentTarget.ScopeMode);
        Assert.Equal(StatementScopeMode.MinimalBlock, childTarget.ScopeMode);
        Assert.NotNull(childTarget.ScopeId);
        Assert.NotNull(childTarget.ParentScopeId);
        Assert.NotEqual(childTarget.ScopeId, childTarget.ParentScopeId);
        Assert.Contains(childTarget.UsesSymbols, symbol => symbol.DisplayName == "parent");
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotPierceAcrossFunctionBoundary()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                        public int Compute()
                        {
                            int parent = 1;
                            return parent;
                        }

                        public int Update()
                        {
                            return parent;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var updateReturn = Assert.Single(result.View.Targets.Where(target =>
            target.Target.DisplayText == "return parent;" &&
            target.Target.MemberId.Value == "Sample.Player.Update()"));
        Assert.DoesNotContain(updateReturn.UsesSymbols, symbol => symbol.DisplayName == "parent");
        Assert.Equal(StatementScopeMode.MinimalBlock, updateReturn.ScopeMode);
    }

    [Fact]
    public async Task CreateContext_BuildsStatementFactsIndexByMember()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                                int child = parent;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.True(context.StatementFacts.FactsByMemberId.TryGetValue("Sample.Player.Update(int)", out var bucket));
        Assert.Equal(2, bucket.Count);
        Assert.All(bucket, fact => Assert.Equal(new MemberId("Sample.Player.Update(int)"), fact.MemberId));
    }

    [Fact]
    public async Task StatementAnalysisService_MinimalBlock_OnlyReturnsCurrentBlockStatements()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                                int child = parent;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);
        var childTarget = Assert.Single(result.View.Targets.Where(target => target.Target.DisplayText == "int child = parent;"));

        var snapshot = context.Statements.Analyze(childTarget.Target, StatementScopeMode.MinimalBlock);

        Assert.Equal(childTarget.Target.TargetKey, snapshot.SeedTargetKey);
        Assert.Equal(StatementScopeMode.MinimalBlock, snapshot.ScopeMode);
        Assert.Equal(new[] { childTarget.Target.TargetKey }, snapshot.Nodes);
        Assert.DoesNotContain(snapshot.Edges, edge => edge.Kind == StatementDependencyKind.Precedes);
        Assert.Contains(snapshot.Edges, edge => edge.Kind == StatementDependencyKind.Defines);
        Assert.Contains(snapshot.Edges, edge => edge.Kind == StatementDependencyKind.Uses);
    }

    [Fact]
    public async Task StatementAnalysisService_ParentBlockPiercing_IntroducesNearestParentDefinition()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                                int child = parent;
                            }
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);
        var parentTarget = Assert.Single(result.View.Targets.Where(target => target.Target.DisplayText == "int parent = seed;"));
        var childTarget = Assert.Single(result.View.Targets.Where(target => target.Target.DisplayText == "int child = parent;"));

        var snapshot = context.Statements.Analyze(childTarget.Target, StatementScopeMode.ParentBlockPiercing);

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Contains(parentTarget.Target.TargetKey, snapshot.Nodes);
        Assert.Contains(childTarget.Target.TargetKey, snapshot.Nodes);
        Assert.Contains(snapshot.Edges, edge =>
            edge.Kind == StatementDependencyKind.Precedes &&
            edge.SourceTargetKey == parentTarget.Target.TargetKey &&
            edge.TargetTargetKey == childTarget.Target.TargetKey);
    }

    [Fact]
    public async Task StatementAnalysisService_ParentBlockPiercing_DoesNotCrossFunctionBoundary()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
            new[]
            {
                new SourceDocument(
                    "Sample.cs",
                    "Sample.cs",
                    """
                    namespace Sample;

                    public class Player
                    {
                        public int Compute()
                        {
                            int parent = 1;
                            return parent;
                        }

                        public int Update()
                        {
                            return parent;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);
        var updateReturn = Assert.Single(result.View.Targets.Where(target =>
            target.Target.DisplayText == "return parent;" &&
            target.Target.MemberId.Value == "Sample.Player.Update()"));

        var snapshot = context.Statements.Analyze(updateReturn.Target, StatementScopeMode.ParentBlockPiercing);

        Assert.Equal(new[] { updateReturn.Target.TargetKey }, snapshot.Nodes);
        Assert.DoesNotContain(snapshot.Edges, edge => edge.Kind == StatementDependencyKind.Precedes);
    }

    [Fact]
    public async Task CreateContext_ProvidesExpandedMembersFunctionGraphSnapshot()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            Run();
                        }

                        private void Run()
                        {
                            Ping();
                        }

                        private void Ping()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);
        var snapshot = context.FunctionGraphs.GetExpandedMembersSnapshot(new[] { new MemberId("Sample.Player.Run()") });

        Assert.Equal(FunctionGraphScope.ExpandedMembers, snapshot.Scope);
        Assert.Equal(new[] { "Sample.Player.Run()" }, snapshot.RootMemberIds.Select(id => id.Value));
        Assert.Equal(new[] { "Sample.cs" }, snapshot.IncludedDocumentPaths);
        Assert.Contains("Sample.Player.Update()", snapshot.Graph.Nodes.Select(node => node.MemberId.Value));
        Assert.Contains("Sample.Player.Run()", snapshot.Graph.Nodes.Select(node => node.MemberId.Value));
        Assert.Contains("Sample.Player.Ping()", snapshot.Graph.Nodes.Select(node => node.MemberId.Value));
        Assert.All(snapshot.Graph.Edges, edge => Assert.Equal(FunctionDependencyKind.Calls, edge.Kind));
    }

    [Fact]
    public async Task CreateSnapshotAndServices_SeparatesFactsFromServices()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            Run();
                        }

                        private void Run()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);
        var snapshot = context.Snapshot;
        var services = context.Services;

        Assert.Same(result.View, snapshot.View);
        Assert.Same(result.FunctionIndex, snapshot.FunctionIndex);
        Assert.Same(result.FunctionFacts, snapshot.FunctionFacts);
        Assert.NotNull(services.Inheritance);
        Assert.NotNull(services.References);
        Assert.NotNull(services.Statements);
        Assert.NotNull(services.FunctionGraphs);
        Assert.Equal(
            context.Statements.Analyze(
                snapshot.View.Targets.First(target => target.Target.TargetKind == TargetKind.Statement).Target,
                StatementScopeMode.MinimalBlock).Nodes,
            services.Statements.Analyze(
                snapshot.View.Targets.First(target => target.Target.TargetKind == TargetKind.Statement).Target,
                StatementScopeMode.MinimalBlock).Nodes);
    }

    [Fact]
    public async Task CreateContext_ExposesSnapshotAndServicesAsSingleSourcesOfTruth()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            Run();
                        }

                        private void Run()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var context = engine.CreateContext(result);

        Assert.Same(context.Snapshot.View, context.View);
        Assert.Same(context.Snapshot.FunctionIndex, context.FunctionIndex);
        Assert.Same(context.Snapshot.FunctionFacts, context.FunctionFacts);
        Assert.Same(context.Snapshot.StatementFacts, context.StatementFacts);
        Assert.Same(context.Services.Statements, context.Statements);
        Assert.Same(context.Services.FunctionGraphs, context.FunctionGraphs);
        Assert.Same(context.Services.Inheritance, context.Inheritance);
        Assert.Same(context.Services.References, context.References);
    }

    [Fact]
    public async Task FunctionGraphProvider_GetSnapshot_UsesExplicitRequest()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            Run();
                        }

                        private void Run()
                        {
                            Ping();
                        }

                        private void Ping()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var services = engine.CreateContext(result).Services;
        var snapshot = services.FunctionGraphs.GetSnapshot(
            FunctionGraphRequests.ExpandedMembersCalls(
                new[] { new MemberId("Sample.Player.Run()") },
                "AnalysisGraphTests",
                "verify explicit request"));

        Assert.Equal(FunctionGraphScope.ExpandedMembers, snapshot.Scope);
        Assert.Equal(new[] { "Sample.Player.Run()" }, snapshot.RootMemberIds.Select(id => id.Value));
        Assert.Equal(new[] { "Sample.cs" }, snapshot.IncludedDocumentPaths);
        Assert.All(snapshot.Graph.Edges, edge => Assert.Equal(FunctionDependencyKind.Calls, edge.Kind));
    }

    [Fact]
    public void FunctionGraphRequests_CreateSupportedRequests()
    {
        var wholeProject = FunctionGraphRequests.WholeProjectCalls("AnalysisGraphTests", "whole project");
        var expandedMembers = FunctionGraphRequests.ExpandedMembersCalls(
            new[] { new MemberId("Sample.Player.Run()") },
            "AnalysisGraphTests",
            "expanded");

        Assert.Equal(FunctionGraphScope.WholeProject, wholeProject.Scope);
        Assert.Empty(wholeProject.RootMemberIds);
        Assert.Equal(new[] { FunctionDependencyKind.Calls }, wholeProject.EdgeKinds);

        Assert.Equal(FunctionGraphScope.ExpandedMembers, expandedMembers.Scope);
        Assert.Equal(1, expandedMembers.Depth);
        Assert.Equal(new[] { "Sample.Player.Run()" }, expandedMembers.RootMemberIds.Select(id => id.Value));
        Assert.Equal(new[] { FunctionDependencyKind.Calls }, expandedMembers.EdgeKinds);
    }

    [Fact]
    public async Task FunctionGraphProvider_GetSnapshot_RejectsUnsupportedRequests()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            Run();
                        }

                        private void Run()
                        {
                        }
                    }
                    """)
            },
            CancellationToken.None);

        var provider = engine.CreateContext(result).FunctionGraphs;

        Assert.Throws<NotSupportedException>(() => provider.GetSnapshot(
            new FunctionGraphRequest(
                FunctionGraphScope.WholeProject,
                Array.Empty<MemberId>(),
                0,
                new[] { FunctionDependencyKind.ReadsMember },
                "AnalysisGraphTests",
                "invalid edge kind")));

        Assert.Throws<NotSupportedException>(() => provider.GetSnapshot(
            new FunctionGraphRequest(
                FunctionGraphScope.ExpandedMembers,
                new[] { new MemberId("Sample.Player.Run()") },
                2,
                new[] { FunctionDependencyKind.Calls },
                "AnalysisGraphTests",
                "invalid depth")));

        Assert.Throws<NotSupportedException>(() =>
            provider.GetExpandedMembersSnapshot(new[] { new MemberId("Sample.Player.Run()") }, depth: 2));
    }
}
