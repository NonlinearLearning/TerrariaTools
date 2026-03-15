using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

public class InternalAnalysisServicesTests
{
    [Fact]
    public async Task CreateContext_BuildsSymbolDependencyGraphForInitializersAndConstructors()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Helper
            {
                public static string Normalize(string value) => value.Trim();
            }

            public class Base
            {
                protected Base(string value)
                {
                }
            }

            public class Worker : Base
            {
                private readonly string _field = Helper.Normalize(" a ");

                public string Value { get; } = Helper.Normalize(" b ");

                public Worker() : base(Helper.Normalize(" c "))
                {
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var graph = context.SymbolDependencies.GetWholeGraph();

        var normalizeNode = Assert.Single(graph.Nodes.Where(node => node.DisplayName.Contains("Helper.Normalize", StringComparison.Ordinal)));
        Assert.Contains(graph.Edges, edge => edge.TargetSymbolId == normalizeNode.SymbolId && edge.Kind == SymbolDependencyEdgeKind.InitializerReference);
        Assert.Contains(graph.Edges, edge => edge.TargetSymbolId == normalizeNode.SymbolId && edge.Kind == SymbolDependencyEdgeKind.ConstructorInitializer);
        var slice = context.SymbolDependencies.GetForwardSlice(new[] { "Sample.Worker" });
        Assert.Contains(slice.Nodes, node => node.SymbolId == "Sample.Helper");
        Assert.Contains(slice.Nodes, node => node.DisplayName.Contains("Helper.Normalize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateContext_BuildsMethodCallQueriesForCallersCalleesAndReachability()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public static class Extensions
            {
                public static int Bump(this int value) => value + 1;
            }

            public class Worker
            {
                public int Run()
                {
                    return Local(1).Bump();
                }

                private int Local(int value)
                {
                    return Normalize(value);
                }

                private static int Normalize(int value)
                {
                    return value;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var runId = new MemberId("Sample.Worker.Run()");
        var localId = new MemberId("Sample.Worker.Local(int)");
        var normalizeId = new MemberId("Sample.Worker.Normalize(int)");

        Assert.Contains(context.MethodCalls.GetCallees(runId), item => item.Value == localId.Value);
        Assert.Contains(context.MethodCalls.GetCallers(normalizeId), item => item.Value == localId.Value);
        Assert.Contains(context.MethodCalls.GetReachableMethods(new[] { runId }), item => item.Value == normalizeId.Value);
        Assert.Contains(context.MethodCalls.GetWholeGraph().Nodes, node => node.MemberId.Value == "Sample.Extensions.Bump(int)");
    }

    [Fact]
    public async Task CreateContext_BuildsDataFlowSummaryForMethodBodies()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public int Run(int seed)
                {
                    int count = seed;
                    int result = count + 1;
                    return result;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var summary = context.DataFlow.Analyze(new MemberId("Sample.Worker.Run(int)"));

        Assert.Contains(summary.DefinedSymbols, item => item == "count");
        Assert.Contains(summary.DefinedSymbols, item => item == "result");
        Assert.Contains(summary.UsedSymbols, item => item == "seed");
        Assert.Contains(summary.UsedSymbols, item => item == "count");
    }

    [Fact]
    public async Task CreateContext_BuildsSwitchFlowSummaryForSwitchStatements()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public void Run(int mode)
                {
                    switch (mode)
                    {
                        case 1:
                            First();
                            break;
                        default:
                            Second();
                            break;
                    }
                }

                private void First()
                {
                }

                private void Second()
                {
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var summaries = context.SwitchFlows.Analyze(new MemberId("Sample.Worker.Run(int)"));

        var summary = Assert.Single(summaries);
        Assert.Equal(2, summary.Cases.Count);
        Assert.Contains(summary.Cases, item => item.Label == "case 1");
        Assert.Contains(summary.Cases.SelectMany(item => item.InvokedMemberIds), item => item.Value == "Sample.Worker.First()");
        Assert.Contains(summary.Cases.SelectMany(item => item.InvokedMemberIds), item => item.Value == "Sample.Worker.Second()");
    }

    [Fact]
    public async Task CreateContext_MapsCallChainLogsAgainstStaticMethods()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public void Run()
                {
                    First();
                }

                private void First()
                {
                    Second();
                }

                private void Second()
                {
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var report = context.CallChains.Analyze(
            """
            [12:00:00] [ENTER] Sample.Worker.Run
            [12:00:01] [ENTER] Sample.Worker.First
            """);

        Assert.Equal(2, report.TotalCalls);
        Assert.Contains(report.MappedMemberIds, item => item.Value == "Sample.Worker.Run()");
        Assert.Contains(report.MappedMemberIds, item => item.Value == "Sample.Worker.First()");
        Assert.Contains(report.PotentialStaticOnlyMemberIds, item => item.Value == "Sample.Worker.Second()");
    }

    [Fact]
    public async Task CreateContext_BuildsAdvancedAnalysisSummaryForCyclesAndRoots()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public void Run()
                {
                    First();
                }

                private void First()
                {
                    Second();
                }

                private void Second()
                {
                    First();
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var summary = context.AdvancedAnalysis.BuildSummary();

        Assert.True(summary.MethodNodeCount >= 3);
        Assert.True(summary.MethodEdgeCount >= 3);
        Assert.Contains(summary.RootMethods, item => item.Value == "Sample.Worker.Run()");
        Assert.Contains(summary.CyclicMethodComponents, component => component.Contains("Sample.Worker.First()", StringComparer.Ordinal));
        Assert.Contains(summary.CyclicMethodComponents, component => component.Contains("Sample.Worker.Second()", StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreateContext_BuildsSymbolDependenciesForEventsGenericTypesAndConversions()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            using System;

            namespace Sample;

            public interface IWorker
            {
                void Run();
            }

            public readonly struct Token
            {
                public static implicit operator string(Token token) => string.Empty;
            }

            public sealed class Payload<T> where T : IDisposable
            {
                public T? Value { get; }

                public Payload(T value)
                {
                    Value = value;
                }
            }

            public sealed class Resource : IDisposable
            {
                public void Dispose()
                {
                }
            }

            public sealed class Worker : IWorker
            {
                private readonly Payload<Resource[]> _payload = new(new[] { new Resource() });
                public event EventHandler? Updated;

                public (Token, Resource?) Make()
                {
                    Updated?.Invoke(this, EventArgs.Empty);
                    return (new Token(), new Resource());
                }

                void IWorker.Run()
                {
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var graph = context.SymbolDependencies.GetWholeGraph();

        Assert.Contains(graph.Nodes, node => node.SymbolId == "Sample.Worker.Updated" && node.Kind == SymbolDependencyNodeKind.Event);
        Assert.Contains(graph.Edges, edge => edge.Kind == SymbolDependencyEdgeKind.InterfaceImplementation);
        Assert.Contains(graph.Edges, edge => edge.Kind == SymbolDependencyEdgeKind.Conversion && edge.TargetSymbolId == "string");
        Assert.Contains(graph.Edges, edge => edge.Kind == SymbolDependencyEdgeKind.ParameterType && edge.TargetSymbolId.Contains("System.IDisposable", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Kind == SymbolDependencyEdgeKind.FieldType && edge.TargetSymbolId.Contains("Sample.Resource[]", StringComparison.Ordinal));

        var filteredSlice = context.SymbolDependencies.GetForwardSlice(
            new[] { "Sample.Worker" },
            new SymbolDependencyQueryOptions(
                MaxDepth: 2,
                AllowedEdgeKinds: new[] { SymbolDependencyEdgeKind.InterfaceImplementation, SymbolDependencyEdgeKind.InitializerReference },
                AllowedNodeKinds: new[] { SymbolDependencyNodeKind.Type, SymbolDependencyNodeKind.Method },
                IncludeRoots: true));

        Assert.Contains(filteredSlice.Nodes, node => node.SymbolId == "Sample.Worker");
        Assert.DoesNotContain(filteredSlice.Nodes, node => node.Kind == SymbolDependencyNodeKind.Event);
    }

    [Fact]
    public async Task CreateContext_ExplainsMethodReachabilityWithShortestPath()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Worker
            {
                public void Run()
                {
                    First();
                }

                private void First()
                {
                    Second();
                }

                private void Second()
                {
                    Third();
                }

                private void Third()
                {
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var explanation = context.MethodCalls.ExplainReachability(
            new MemberId("Sample.Worker.Run()"),
            new MemberId("Sample.Worker.Third()"));

        Assert.True(explanation.IsReachable);
        Assert.Equal(4, explanation.Path.Count);
        Assert.Equal("Sample.Worker.Run()", explanation.Path[0].Value);
        Assert.Equal("Sample.Worker.Third()", explanation.Path[^1].Value);
        Assert.Equal(explanation.Path.Select(item => item.Value), context.MethodCalls.GetShortestPath(
            new[] { new MemberId("Sample.Worker.Run()") },
            new MemberId("Sample.Worker.Third()")).Select(item => item.Value));
    }

    [Fact]
    public async Task CreateContext_BuildsExtendedAdvancedAnalysisSummary()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public interface IRunner
            {
                void Run();
            }

            public class Worker : IRunner
            {
                public void Run()
                {
                    First();
                }

                private void First()
                {
                    Second();
                }

                private void Second()
                {
                    First();
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var context = engine.CreateContext(result);
        var summary = context.AdvancedAnalysis.BuildSummary();

        Assert.True(summary.MethodRootCount >= 1);
        Assert.True(summary.SymbolRootCount >= 1);
        Assert.True(summary.MethodSccCount >= 1);
        Assert.True(summary.SymbolSccCount >= 1);
        Assert.NotEmpty(summary.MethodRoots);
        Assert.NotEmpty(summary.SymbolRoots);
        Assert.NotEmpty(summary.LargestMethodComponents);
        Assert.NotEmpty(summary.HighlyConnectedMethods);
        Assert.True(summary.InterfaceBridgeCount >= 1);
    }
}
