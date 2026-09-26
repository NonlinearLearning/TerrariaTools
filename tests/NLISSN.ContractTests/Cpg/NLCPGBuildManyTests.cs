using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// <c>NLCPGBuilder.BuildMany</c>（D1 多文件入口）的契约。
/// </summary>
/// <remarks>
/// <para>
/// 本类存在的理由：<c>BuildMany</c> 是 D1「跨文件凑标准大小」唯一的可达入口，
/// 而 T4 的按项路由（每文件一张图 + 共享字符串表）在它出现之前**不可达**。
/// </para>
/// <para>
/// <b>核心用例是 <see cref="BuildMany_TwoFiles_ResolvesCallTargetsThatSingleFileBuildCannot"/>：</b>
/// 它用「同一份调用方源码，单独构建时解析不出调用目标 / 与兄弟文件一起构建时解析得出」
/// 这一**正反对照**，锁死「全部文件放进同一个 compilation」这一正确性前提。
/// 若退回「每文件各持一个只含自身的 compilation」，对照的后半句会失败。
/// </para>
/// </remarks>
public sealed class NLCPGBuildManyTests
{
    private const string CallerSource = """
      namespace Demo;

      public sealed class Caller
      {
        public int Invoke(Callee callee)
        {
          return callee.Compute(41);
        }
      }
      """;

    private const string CalleeSource = """
      namespace Demo;

      public sealed class Callee
      {
        public int Compute(int value)
        {
          return value + 1;
        }
      }
      """;

    private const string CallerPath = "many-caller.cs";
    private const string CalleePath = "many-callee.cs";

    [Fact]
    public void BuildMany_TwoFiles_ResolvesCallTargetsThatSingleFileBuildCannot()
    {
        // 观测点：调用方图里出现一条**指向被调用方文件**的边。
        // `callee.Compute(41)` 要连到 `Callee.Compute` 的参数/返回节点，就必须先解析出
        // 那个方法符号；而 `Callee` 的声明只在另一个文件里。
        //
        // 反面对照：只编译调用方时 `Callee` 无定义，符号解析不出来，
        // 因而不可能产出指向被调用方文件的链接。
        var aloneGraph = CreateBuilder().BuildFromSource(CallerSource, CallerPath);
        Assert.DoesNotContain(aloneGraph.Edges, edge => LinksIntoFile(aloneGraph, edge.TargetNodeId, CalleePath));

        // 正面对照：把被调用方一起放进**同一个 compilation**，同一份源码必须连得过去。
        // 若退回「每文件各持一个只含自身的 compilation」，这里会退化成反面对照的样子。
        var graphs = CreateBuilder().BuildMany(new[]
        {
            (CallerPath, CallerSource),
            (CalleePath, CalleeSource),
        });
        var callerGraph = graphs[CallerPath];
        Assert.Contains(callerGraph.Edges, edge => LinksIntoFile(callerGraph, edge.TargetNodeId, CalleePath));
    }

    [Fact]
    public void BuildMany_TwoFiles_ReturnsOneGraphPerFile()
    {
        var graphs = CreateBuilder().BuildMany(new[]
        {
            (CallerPath, CallerSource),
            (CalleePath, CalleeSource),
        });

        Assert.Equal(2, graphs.Count);
        Assert.True(graphs.ContainsKey(CallerPath));
        Assert.True(graphs.ContainsKey(CalleePath));

        // 每个文件自己的图里都必须有内容。
        Assert.NotEmpty(graphs[CallerPath].Nodes);
        Assert.NotEmpty(graphs[CalleePath].Nodes);

        // ⚠ 不能用「另一个文件的方法名是否出现在本图」来判定——跨文件解析成功后，
        //   被调用方的方法**符号节点**本就会（且应当）出现在调用方图里。
        //   真正要锁的是「每个文件**自己声明**的方法落在自己的图里」：
        //   `Demo.Callee.Compute` 的**声明节点**只能来自被调用方文件。
        Assert.Contains(
          graphs[CalleePath].Nodes,
          node => NodeText(graphs[CalleePath], node).Contains("Demo.Callee.Compute", StringComparison.Ordinal));
        Assert.Contains(
          graphs[CallerPath].Nodes,
          node => NodeText(graphs[CallerPath], node).Contains("Demo.Caller.Invoke", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildMany_SingleFile_MatchesBuildFromSource()
    {
        // 单元素输入必须与单文件入口产出同一张图——否则 BuildMany 会成为
        // 一条「语义相近但不同」的旁路。
        var manyGraph = CreateBuilder()
          .BuildMany(new[] { (CallerPath, CallerSource) })[CallerPath];
        var singleGraph = CreateBuilder().BuildFromSource(CallerSource, CallerPath);

        Assert.Equal(DescribeGraph(singleGraph), DescribeGraph(manyGraph));
    }

    [Fact]
    public void BuildMany_EmptyInput_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() =>
          builder.BuildMany(Array.Empty<(string FilePath, string Source)>()));
    }

    [Fact]
    public void BuildMany_DuplicateFilePath_Throws()
    {
        // 重复路径会让「按文件路径解析图」失去唯一答案，必须 fail-closed。
        var builder = CreateBuilder();

        var exception = Assert.Throws<ArgumentException>(() => builder.BuildMany(new[]
        {
            (CallerPath, CallerSource),
            (CallerPath, CalleeSource),
        }));
        Assert.Contains("Duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMany_NullInput_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() =>
          builder.BuildMany(null!));
    }

    [Fact]
    public void BuildMany_BlankFilePath_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() =>
          builder.BuildMany(new[] { ("  ", CallerSource) }));
    }

    [Fact]
    public void BuildMany_PreallocatedNodeIds_ThrowsNotSupported()
    {
        // 每张兄弟图各自持有预分配表，无法共用一份；必须显式拒绝，
        // 而不是塞一张空表进去产出「看似正常但 NodeId 不稳定」的图。
        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            UsePreallocatedNodeIds = true,
        });

        Assert.Throws<NotSupportedException>(() => builder.BuildMany(new[]
        {
            (CallerPath, CallerSource),
            (CalleePath, CalleeSource),
        }));
    }

    /// <summary>
    /// 某条边的目标节点是否位于 <paramref name="filePath"/> 这个文件。
    /// </summary>
    /// <remarks>
    /// 这是「跨文件符号解析成功」的直接证据：目标节点带着**被调用方文件**的
    /// <c>FilePathId</c>，而该节点只能来自对被调用方方法的符号解析。
    /// </remarks>
    private static bool LinksIntoFile(NLCPGGraph graph, NodeId targetNodeId, string filePath)
    {
        var node = graph.GetNode(targetNodeId);
        if (node is not { } target)
        {
            return false;
        }

        return string.Equals(
          NormalizePath(graph.ResolveFilePath(target)),
          NormalizePath(filePath),
          StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? filePath)
    {
        return filePath?.Replace('\\', '/') ?? string.Empty;
    }

    private static string NodeText(NLCPGGraph graph, NLCPGNode node)
    {
        return string.Join(
          "|",
          graph.ResolveName(node),
          graph.ResolveFullName(node),
          graph.ResolveSignature(node));
    }

    private static NLCPGBuilder CreateBuilder()
    {
        return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            // 强制走「大文件/多方法」分区路径，使批次真正产生（默认阈值会让小样例不装箱）。
            LargeFileLineThreshold = 1,
            LargeFileMethodThreshold = 1,
            LargeMethodLineSpanThreshold = 1,
            SyntaxLargeFileLineThreshold = 1,
        });
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
          .Select(edge => $"{edge.SourceNodeId}|{edge.Kind}|{edge.TargetNodeId}|{edge.StructuredLabel?.StableKey}")
          .ToArray();
        return nodes.Concat(edges).ToArray();
    }
}
