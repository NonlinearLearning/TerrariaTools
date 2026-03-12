using TerrariaTools.Dome.Analysis.Roslyn;
using TerrariaTools.Dome.Core;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis;

/// <summary>
/// 数据流分析测试类。
/// </summary>
public class DataFlowAnalysisTests
{
    /// <summary>
    /// 测试分析异步方法为简单语句投影定义和使用。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsDefinesAndUsesForSimpleStatements()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    int count = 1;
                    int next = count;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var targets = result.View.Targets
            .Where(target => target.Target.TargetKind == TargetKind.Statement)
            .ToArray();

        Assert.Equal(2, targets.Length);
        Assert.Contains(targets[0].DefinesSymbols, symbol => symbol.DisplayName == "count");
        Assert.Contains(targets[1].DefinesSymbols, symbol => symbol.DisplayName == "next");
        Assert.Contains(targets[1].UsesSymbols, symbol => symbol.DisplayName == "count");
        Assert.Contains(result.View.Edges, edge => edge.Kind == AnalysisEdgeKind.Precedes);
    }

    /// <summary>
    /// 测试分析异步方法为简单赋值语句投影定义和使用。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsDefinesAndUsesForSimpleAssignmentStatements()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public void Update()
                {
                    int count = 1;
                    int total = 0;
                    total = count;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var assignmentTarget = result.View.Targets.Single(target => target.Target.DisplayText == "total = count;");

        Assert.Contains(assignmentTarget.DefinesSymbols, symbol => symbol.DisplayName == "total");
        Assert.Contains(assignmentTarget.UsesSymbols, symbol => symbol.DisplayName == "count");
    }

    /// <summary>
    /// 测试分析异步方法区分参数和本地定义。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_DistinguishesParameterFromLocalDefinitions()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public int Update(int value)
                {
                    int next = value;
                    int localValue = 10;
                    return localValue;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var targets = result.View.Targets.ToArray();
        var assignmentFromParameter = targets.Single(target => target.Target.DisplayText == "int next = value;");
        var localDefinition = targets.Single(target => target.Target.DisplayText == "int localValue = 10;");
        var returnTarget = targets.Single(target => target.Target.DisplayText == "return localValue;");

        var parameterUse = Assert.Single(assignmentFromParameter.UsesSymbols.Where(symbol => symbol.DisplayName == "value"));
        var localSymbol = Assert.Single(localDefinition.DefinesSymbols.Where(symbol => symbol.DisplayName == "localValue"));
        var returnUse = Assert.Single(returnTarget.UsesSymbols.Where(symbol => symbol.DisplayName == "localValue"));

        Assert.Equal(SymbolKindRef.Parameter, parameterUse.SymbolKind);
        Assert.Equal(SymbolKindRef.Local, localSymbol.SymbolKind);
        Assert.Equal(localSymbol.SymbolKey, returnUse.SymbolKey);
        Assert.NotEqual(parameterUse.SymbolKey, localSymbol.SymbolKey);
    }

    /// <summary>
    /// 测试分析异步方法返回语句产生使用但不产生定义。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ReturnStatementProducesUsesWithoutDefines()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public int Update(int value)
                {
                    return value;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var returnTarget = Assert.Single(result.View.Targets.Where(target => target.Target.TargetKind == TargetKind.Statement));

        Assert.Empty(returnTarget.DefinesSymbols);
        var returnUse = Assert.Single(returnTarget.UsesSymbols);
        Assert.Equal("value", returnUse.DisplayName);
        Assert.Equal(SymbolKindRef.Parameter, returnUse.SymbolKind);
    }

    /// <summary>
    /// 测试分析异步方法投影字段和属性初始化器。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsFieldAndPropertyInitializers()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                private int _seed = 1;
                public int Value { get; set; } = _seed;
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var targets = result.View.Targets.ToArray();
        var fieldInitializer = targets.Single(target => target.Target.DisplayText == "private int _seed = 1;");
        var propertyInitializer = targets.Single(target => target.Target.DisplayText == "public int Value { get; set; } = _seed;");

        Assert.Contains(fieldInitializer.DefinesSymbols, symbol => symbol.DisplayName == "_seed");
        Assert.Contains(propertyInitializer.DefinesSymbols, symbol => symbol.DisplayName == "Value");
        Assert.Contains(propertyInitializer.UsesSymbols, symbol => symbol.DisplayName == "_seed");
    }

    /// <summary>
    /// 测试分析异步方法投影构造函数成员赋值。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsConstructorMemberAssignments()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public int Value { get; private set; }

                public Player(int seed)
                {
                    Value = seed;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var assignmentTarget = result.View.Targets.Single(target => target.Target.DisplayText == "Value = seed;");

        Assert.Contains(assignmentTarget.DefinesSymbols, symbol => symbol.DisplayName == "Value");
        Assert.Contains(assignmentTarget.UsesSymbols, symbol => symbol.DisplayName == "seed");
    }

    /// <summary>
    /// 测试分析异步方法投影访问器赋值和返回。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsAccessorAssignmentsAndReturns()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                private int _value;

                public int Value
                {
                    get
                    {
                        return _value;
                    }
                    set
                    {
                        _value = value;
                    }
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var returnTarget = result.View.Targets.Single(target => target.Target.DisplayText == "return _value;");
        var assignmentTarget = result.View.Targets.Single(target => target.Target.DisplayText == "_value = value;");

        Assert.Empty(returnTarget.DefinesSymbols);
        Assert.Contains(returnTarget.UsesSymbols, symbol => symbol.DisplayName == "_value");
        Assert.Contains(assignmentTarget.DefinesSymbols, symbol => symbol.DisplayName == "_value");
        Assert.Contains(assignmentTarget.UsesSymbols, symbol => symbol.DisplayName == "value");
    }

    /// <summary>
    /// 测试分析异步方法跨多个文件保留相对路径和成员ID。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_PreservesRelativePathsAndMemberIdsAcrossMultipleFiles()
    {
        var engine = new RoslynAnalysisEngine();
        var result = await engine.AnalyzeAsync(
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
                            int count = 1;
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
                            int total = 2;
                        }
                    }
                    """)
            },
            CancellationToken.None);

        Assert.Contains(result.View.Targets, target =>
            target.Target.DocumentPath == "Root.cs" &&
            target.Target.MemberId.Value == "Sample.RootPlayer.Update()");
        Assert.Contains(result.View.Targets, target =>
            target.Target.DocumentPath == Path.Combine("Features", "Nested.cs") &&
            target.Target.MemberId.Value == "Sample.Features.NestedPlayer.Update()");
    }

    /// <summary>
    /// 测试分析异步方法投影控制流和规则事实。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ProjectsControlFlowAndRuleFacts()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public class Player
            {
                public int Update(int value)
                {
                    if (value > 0)
                    {
                        return value;
                    }

                    while (value > 1)
                    {
                        value = 0;
                    }

                    for (int i = 0; i < 1; i++)
                    {
                        value = i;
                    }

                    return 0;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);

        var ifTarget = result.View.Targets.Single(target => target.Target.DisplayText.StartsWith("if (value > 0)", StringComparison.Ordinal));
        var whileTarget = result.View.Targets.Single(target => target.Target.DisplayText.StartsWith("while (value > 1)", StringComparison.Ordinal));
        var forTarget = result.View.Targets.Single(target => target.Target.DisplayText.StartsWith("for (int i = 0;", StringComparison.Ordinal));
        var sanitizeTarget = result.View.Targets.Single(target => target.Target.DisplayText == "value = 0;");
        var returnTarget = result.View.Targets.Single(target => target.Target.DisplayText == "return value;");

        Assert.Equal(StatementKindRef.If, ifTarget.StatementKind);
        Assert.Equal(StatementKindRef.While, whileTarget.StatementKind);
        Assert.Equal(StatementKindRef.For, forTarget.StatementKind);
        Assert.Equal(StatementKindRef.Assignment, sanitizeTarget.StatementKind);
        Assert.Equal(StatementKindRef.Return, returnTarget.StatementKind);
        Assert.True(sanitizeTarget.IsSanitizingAssignment);
    }

    /// <summary>
    /// 测试分析异步方法将对象初始化器赋值识别为受保护事实。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_RecognizesObjectInitializerAssignmentsAsProtectedFacts()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
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
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var objectInitializerTarget = result.View.Targets.Single(target => target.Target.DisplayText == "var item = new Item { Value = seed };");

        Assert.True(objectInitializerTarget.IsObjectInitializerAssignment);
        Assert.Equal(StatementKindRef.ObjectInitializerAssignment, objectInitializerTarget.StatementKind);
        Assert.Contains(objectInitializerTarget.UsesSymbols, symbol => symbol.DisplayName == "seed");
    }

    /// <summary>
    /// 测试分析异步方法将无跟踪符号的静态值赋值视为清理。
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_TreatsStaticValueAssignmentsWithoutTrackedSymbolsAsSanitizing()
    {
        var engine = new RoslynAnalysisEngine();
        var document = new SourceDocument(
            "Sample.cs",
            "Sample.cs",
            """
            namespace Sample;

            public static class Defaults
            {
                public static int Value => 42;
            }

            public class Player
            {
                public void Update()
                {
                    int count = 1;
                    count = Defaults.Value;
                    int next = count;
                }
            }
            """);

        var result = await engine.AnalyzeAsync(new[] { document }, CancellationToken.None);
        var assignmentTarget = result.View.Targets.Single(target => target.Target.DisplayText == "count = Defaults.Value;");

        Assert.True(assignmentTarget.IsSanitizingAssignment);
        Assert.Empty(assignmentTarget.UsesSymbols);
    }
}
