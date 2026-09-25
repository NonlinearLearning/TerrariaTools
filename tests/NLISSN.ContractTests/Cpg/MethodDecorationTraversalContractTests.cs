using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.ContractTests.Cpg;

// DoD #2（minimal-roslyn-cpg-declared-symbol-query-optimization）的定向契约：
//   "MethodDecorationPass only enumerates method-like declarations and reports dedicated
//    traversal/query telemetry."
//
// 该阶段原先对整棵语法树做了【三遍】DescendantNodes（方法声明、局部函数、访问器各一遍），
// 现改为【一遍】遍历 + 三个桶。本文件护住两件必须同时成立的事：
//
//   ① 三桶的【桶序】不变（全部方法声明 -> 全部局部函数 -> 全部访问器）。
//      桶序决定 AddMethodAbstractions 建节点/边的先后，进而决定 Method 节点的入图序。
//      它不是"实现细节"，而是既有的节点序契约：若退化成按文档序交错，节点序会改变。
//   ② 新遥测 MethodDecorationTelemetry 报出的数字确实落在被测对象上——
//      遍历被删、某个桶被丢、查询门被去掉，都必须让本文件失败。
//
// 判据来源（非本文件推导）：
//   · 桶序：改动前 MethodDecorationPass.cs 的三次 Concat 顺序（源码事实）。
//   · 方法/访问器符号名：Roslyn 语义（Alpha / Beta / Local / get_Property / set_Property）。
// 语言：Go 的显式错误处理不适用；此处沿用仓库既有 xUnit + ITestOutputHelper 约定。
public sealed class MethodDecorationTraversalContractTests
{
    private readonly ITestOutputHelper _output;

    public MethodDecorationTraversalContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 刻意把【属性】放在【方法】前面，使"文档序"与"桶序"必然不同：
    //   文档序（一次 DescendantNodes）：get_Property, set_Property, Alpha, Local, Beta
    //   桶序  （既有实现）              ：Alpha, Beta, Local, get_Property, set_Property
    // 这正是本文件能区分两种遍历方式的根据。
    private const string Source = """
      namespace Demo;

      public sealed class TraversalSample
      {
        private int _field;

        public int Property
        {
          get { return _field; }
          set { _field = value; }
        }

        public int Alpha(int value)
        {
          int Local(int input) => input + value;
          return Local(value);
        }

        public int Beta(int value)
        {
          return value;
        }
      }
      """;

    // 冻结基线：桶序下 Method 节点的入图序（按 Name 解析）。
    private static readonly string[] ExpectedBucketOrder =
    {
        "Alpha",
        "Beta",
        "Local",
        "get_Property",
        "set_Property",
    };

    // 同一 fixture 的文档序。仅用于证明上面的期望值【不是】文档序——
    // 否则本测试对"三遍改一遍"这一改动将是盲的。
    private static readonly string[] DocumentOrder =
    {
        "get_Property",
        "set_Property",
        "Alpha",
        "Local",
        "Beta",
    };

    [Fact]
    public void BuildFromSource_MethodLikeDeclarationBuckets_PreserveBucketOrderRatherThanDocumentOrder()
    {
        var (builder, graph) = Build(CreateOptions(dop: 1));
        var actual = MethodNodeNames(graph);

        _output.WriteLine($"actual order: {string.Join(", ", actual)}");
        _output.WriteLine(
          $"telemetry: methods={builder.LastMethodDecorationTelemetry.MethodDeclarationCount} " +
          $"localfns={builder.LastMethodDecorationTelemetry.LocalFunctionCount} " +
          $"accessors={builder.LastMethodDecorationTelemetry.AccessorDeclarationCount} " +
          $"queried={builder.LastMethodDecorationTelemetry.DeclaredSymbolQueryCount} " +
          $"resolved={builder.LastMethodDecorationTelemetry.DeclaredSymbolResolvedCount}");

        Assert.Equal(ExpectedBucketOrder, actual);
        // 关键的反向断言：若遍历被合并成按文档序的单遍处理，actual 会等于 DocumentOrder。
        Assert.NotEqual(DocumentOrder, actual);
    }

    [Fact]
    public void BuildFromSource_MethodDecoration_ReportsDedicatedTraversalAndQueryTelemetry()
    {
        var (builder, graph) = Build(CreateOptions(dop: 1));
        var telemetry = builder.LastMethodDecorationTelemetry;

        // 遍历侧：三个桶各自的实际拜访量。
        Assert.Equal(2, telemetry.MethodDeclarationCount);
        Assert.Equal(1, telemetry.LocalFunctionCount);
        Assert.Equal(2, telemetry.AccessorDeclarationCount);
        Assert.Equal(5, telemetry.MethodLikeDeclarationCount);

        // 查询侧：每个桶都进入了查询循环，且全部解析出方法符号。
        Assert.Equal(5, telemetry.DeclaredSymbolQueryCount);
        Assert.Equal(5, telemetry.DeclaredSymbolResolvedCount);
        Assert.Equal(0, telemetry.UnresolvedDeclarationCount);
        Assert.Equal(5, telemetry.AbstractionCount);

        // 遥测必须与图的真实产出对上，而不是自己和自己自洽。
        Assert.Equal(telemetry.AbstractionCount, MethodNodeNames(graph).Count);

        // 耗时是实测值，不冻结具体毫秒；只要求分段的遍历耗时非负。
        Assert.True(telemetry.TraversalElapsedMilliseconds >= 0);
        Assert.True(telemetry.DecorationElapsedMilliseconds >= 0);
    }

    [Fact]
    public void BuildFromSource_MethodDecoration_EnumeratesExactlyTheMethodLikeDeclarations()
    {
        // 用 Roslyn 独立解析同一份源码，数出"方法类声明"的真值，
        // 而不是把生产代码的桶计数抄一遍来和自己比对（那种比对改了代码仍会自洽通过）。
        var expected = CountMethodLikeDeclarationsWithRoslyn(Source);
        var (builder, _) = Build(CreateOptions(dop: 1));
        var telemetry = builder.LastMethodDecorationTelemetry;

        _output.WriteLine(
          $"roslyn: methods={expected.Methods} localfns={expected.LocalFunctions} " +
          $"accessors={expected.Accessors}; telemetry: methods={telemetry.MethodDeclarationCount} " +
          $"localfns={telemetry.LocalFunctionCount} accessors={telemetry.AccessorDeclarationCount}");

        // "只枚举方法类声明"：既不漏（三桶全到），也不多（没把类/字段/属性声明等算进来）。
        Assert.Equal(expected.Methods, telemetry.MethodDeclarationCount);
        Assert.Equal(expected.LocalFunctions, telemetry.LocalFunctionCount);
        Assert.Equal(expected.Accessors, telemetry.AccessorDeclarationCount);

        // 树里确实存在【非】方法类声明，否则上面的"不多"是空洞的。
        var totalDescendantCount = CountAllDescendantsWithRoslyn(Source);
        Assert.True(
          totalDescendantCount > telemetry.MethodLikeDeclarationCount,
          $"fixture 必须含非方法类语法节点：total={totalDescendantCount} " +
          $"methodLike={telemetry.MethodLikeDeclarationCount}");

        // 只有方法类声明会进入查询循环——查询数不会随整棵树的规模膨胀。
        Assert.Equal(telemetry.MethodLikeDeclarationCount, telemetry.DeclaredSymbolQueryCount);
    }

    [Fact]
    public void BuildFromSource_WithoutMethodModel_ReportsDefaultTelemetryAndNoMethodNodes()
    {
        // QueryIndex 不蕴含 MethodModel，故方法装饰阶段根本不应执行。
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { NLCPGCapability.QueryIndex },
        });
        var graph = builder.BuildFromSource(Source, "method-decoration-skipped.cs");

        // 默认值即"未运行"，而不是"运行了但什么都没找到"。
        Assert.Equal(default, builder.LastMethodDecorationTelemetry);
        Assert.Empty(MethodNodeNames(graph));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    public void BuildFromSource_MethodDecoration_PreservesTelemetryAndGraphAcrossDegreesOfParallelism(int dop)
    {
        var (baselineBuilder, baselineGraph) = Build(CreateOptions(dop: 1));
        var (actualBuilder, actualGraph) = Build(CreateOptions(dop: dop));

        Assert.Equal(
          DescribeMethodDecorationTelemetry(baselineBuilder.LastMethodDecorationTelemetry),
          DescribeMethodDecorationTelemetry(actualBuilder.LastMethodDecorationTelemetry));
        Assert.Equal(DescribeGraph(baselineGraph), DescribeGraph(actualGraph));
    }

    private static (NLCPGBuilder Builder, NLCPGGraph Graph) Build(NLCPGBuilderOptions options)
    {
        var builder = new NLCPGBuilder(options);
        var graph = builder.BuildFromSource(Source, "method-decoration-traversal.cs");
        return (builder, graph);
    }

    // 独立真值：直接用 Roslyn 解析同一份源码数方法类声明，不经过生产代码的任何类型。
    private static (int Methods, int LocalFunctions, int Accessors) CountMethodLikeDeclarationsWithRoslyn(
      string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var descendants = root.DescendantNodes().ToArray();
        return (
          descendants.OfType<BaseMethodDeclarationSyntax>().Count(),
          descendants.OfType<LocalFunctionStatementSyntax>().Count(),
          descendants.OfType<AccessorDeclarationSyntax>().Count());
    }

    private static int CountAllDescendantsWithRoslyn(string source)
    {
        return CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().Count();
    }

    private static List<string> MethodNodeNames(NLCPGGraph graph)
    {
        return graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.Method)
          .Select(node => graph.ResolveName(node) ?? string.Empty)
          .ToList();
    }

    // 耗时字段刻意排除在跨 DOP 比较之外：那是环境量，不是确定性契约。
    private static string DescribeMethodDecorationTelemetry(MethodDecorationTelemetry telemetry)
    {
        return string.Join(
          "|",
          telemetry.MethodDeclarationCount,
          telemetry.LocalFunctionCount,
          telemetry.AccessorDeclarationCount,
          telemetry.DeclaredSymbolQueryCount,
          telemetry.DeclaredSymbolResolvedCount,
          telemetry.AbstractionCount);
    }

    private static string[] DescribeGraph(NLCPGGraph graph)
    {
        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId)
          .Select(node => string.Join(
            "|",
            node.NodeId,
            node.Kind,
            graph.ResolveFullName(node),
            node.SpanStart,
            node.SpanEnd))
          .ToArray();
        var edges = graph.Edges
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind)
          .ThenBy(edge => edge.TargetNodeId)
          .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}")
          .ToArray();
        return nodes.Concat(edges).ToArray();
    }

    private static NLCPGBuilderOptions CreateOptions(int dop)
    {
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            // 只请求 MethodModel：隔离本阶段，避免 DataFlow/CallGraph 等其他 pass 也建 Method 节点
            // 而让"Method 节点序"不再只反映本阶段的枚举序。
            RequestedCapabilities = new[] { NLCPGCapability.MethodModel },
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        };
    }
}
