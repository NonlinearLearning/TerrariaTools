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

        Assert.NotEmpty(view.StatementGraph.Nodes);
        Assert.Contains(view.StatementGraph.Edges, edge => edge.Kind == StatementDependencyKind.Defines);
        Assert.Contains(view.StatementGraph.Edges, edge => edge.Kind == StatementDependencyKind.Uses);
        Assert.Contains(view.StatementGraph.Edges, edge => edge.Kind == StatementDependencyKind.Precedes);

        Assert.Contains(view.FunctionGraph.Nodes, node => node.MemberId.Value == "Sample.Worker.Run(int)");
        Assert.Contains(view.FunctionGraph.Edges, edge => edge.Kind == FunctionDependencyKind.Calls);
        Assert.Contains(view.FunctionGraph.Edges, edge => edge.Kind == FunctionDependencyKind.WritesMember);
        Assert.Contains(view.FunctionGraph.Edges, edge => edge.Kind == FunctionDependencyKind.UsesPropertyAccessor);

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

        var compute = Assert.Single(result.View.FunctionGraph.Nodes.Where(node => node.MemberId.Value == "Sample.Player.Compute()"));
        var tick = Assert.Single(result.View.FunctionGraph.Nodes.Where(node => node.MemberId.Value == "Sample.Player.Tick()"));
        var normalize = Assert.Single(result.View.FunctionGraph.Nodes.Where(node => node.MemberId.Value == "Sample.Player.Normalize(int)"));

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
}
