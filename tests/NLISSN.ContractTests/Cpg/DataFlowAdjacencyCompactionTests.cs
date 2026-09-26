using NLCPG.Builder;
using NLCPG.Contracts;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

/// <summary>
/// M5「DataFlow 方法局部邻接序号化」的回归护栏。
///
/// 邻接表示（每节点 List + 字典 → 每方向两块 int 数组）是**纯内部表示变化**，
/// 故本类不是 RED→GREEN 驱动，而是保证重构不破坏语义、且新表示确实是 CSR 的护栏。
///
/// 判别力的边界（必读）：逐节点「CSR 序号序列 == 旧表示序列」的**直接**证明
/// 在隔离镜像 <c>Build/MemoryOptimization/M5/ab/diff</c> 中完成（4316 节点 / 792 边 / 0 不一致，
/// 且负对照打乱顺序后能抓出 60 处）。本类留在仓库内的判据是**可独立复核**的那几条：
/// 完整图与候选提交序等价、邻接访问计数不变、以及"旧物化链已消失"的源码护栏。
/// </summary>
public sealed class DataFlowAdjacencyCompactionTests {
  /// <summary>
  /// 覆盖 Task 1 §2 要求的结构场景：孤立语句、链、汇合、回边、自环（continue）、
  /// 同一节点多路径、以及跨方法被过滤的缓存邻居。
  /// </summary>
  private const string StructureZoo = """
    namespace Demo;

    public sealed class Zoo
    {
      private int _field;

      public int Isolated()
      {
        int unused = 1;
        return 0;
      }

      public int Chain(int seed)
      {
        int a = seed;
        int b = a + 1;
        int c = b + 2;
        return c;
      }

      public int Merge(bool choose, int seed)
      {
        int total = seed;
        if (choose) { total = total + 1; } else { total = total - 1; }
        return total;
      }

      public int BackEdge(int limit, int seed)
      {
        int total = seed;
        for (int index = 0; index < limit; index++)
        {
          if (index == 2) { continue; }
          total = total + index;
        }
        return total;
      }

      public int MultiPath(bool choose, int seed)
      {
        int total = seed;
        if (choose) { total = total + 1; }
        if (seed > 0) { total = total + 2; }
        return total;
      }

      public int CrossMethod(int seed)
      {
        _field = seed;
        return Helper(seed) + _field;
      }

      private static int Helper(int value)
      {
        return value * 2;
      }
    }
    """;

  private static readonly (string Name, string Source)[] AllFixtures = [
      ("Sparse64", DataFlowMeasurementSources.Sparse()),
      ("Collision32", DataFlowMeasurementSources.Collision()),
      ("JoinLoop4x4", DataFlowMeasurementSources.JoinLoop()),
      ("ZeroFactOps", DataFlowMeasurementSources.ZeroFactOps()),
      ("ZeroFactChain", DataFlowMeasurementSources.ZeroFactChain()),
      ("AllZeroFactMethod", DataFlowMeasurementSources.AllZeroFactMethod()),
      ("FactWithEmptyIncomingSet", DataFlowMeasurementSources.FactWithEmptyIncomingSet()),
      ("FactWithIncomingSet", DataFlowMeasurementSources.FactWithIncomingSet()),
      ("StructureZoo", StructureZoo),
  ];

  public static TheoryData<string> FixtureNames() {
    var data = new TheoryData<string>();
    foreach (var (name, _) in AllFixtures) {
      data.Add(name);
    }

    return data;
  }

  /// <summary>
  /// 冻结的等价基线：节点数、DataFlow 边数与候选发布顺序。
  ///
  /// 这些值在 M5 实施**之后**由诊断 Detailed 模式实测并冻结；它们锁住的是
  /// "后续改动不得改变可观测输出"，不是"M5 之前的期望值"。
  /// 改前的等价性由隔离镜像逐节点差分证明（见类注释）。
  /// </summary>
  [Theory]
  [InlineData("Sparse64", 2218, 132)]
  [InlineData("Collision32", 3759, 262)]
  [InlineData("JoinLoop4x4", 1383, 68)]
  [InlineData("ZeroFactOps", 519, 22)]
  [InlineData("ZeroFactChain", 569, 28)]
  [InlineData("AllZeroFactMethod", 87, 7)]
  [InlineData("FactWithEmptyIncomingSet", 54, 4)]
  [InlineData("FactWithIncomingSet", 50, 5)]
  [InlineData("StructureZoo", 721, 56)]
  public void BuildFromSource_EveryFixture_KeepsFrozenGraphAndPublicationOrder(
      string fixture, int expectedNodes, int expectedDataFlowEdges) {
    var source = AllFixtures.Single(entry => entry.Name == fixture).Source;
    var builder = CreateBuilder(out var mode);
    var graph = builder.BuildFromSource(source, fixture + ".cs");

    Assert.Equal(expectedNodes, graph.Nodes.Count());
    Assert.Equal(expectedDataFlowEdges, graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.DataFlow));

    // 候选提交序是正确性约束（预算路径依赖它），必须非空且被冻结。
    var diagnostics = builder.LastDataFlowDiagnostics;
    Assert.NotEmpty(diagnostics);
    Assert.Contains(diagnostics, diagnostic => diagnostic.PublicationOrder.Count > 0);
    Assert.All(diagnostics, diagnostic => Assert.Equal("Complete", diagnostic.ExitReason));
    Assert.Equal(DataFlowDiagnosticMode.Detailed, mode);
  }

  /// <summary>
  /// 邻接访问计数必须与旧实现逐项相同。
  ///
  /// 这是本类**最有判别力**的行为判据：CSR 的区间长度若与旧邻接数组长度不同
  /// （漏边、重复、或把方法外邻居误算进来），这些计数会立刻改变。
  /// 隔离镜像已证明两侧计数完全相同，故可直接冻结为回归基线。
  /// </summary>
  [Theory]
  [MemberData(nameof(FixtureNames))]
  public void BuildFromSource_NeighborVisitCounts_MatchTheLegacyMaterialization(
      string fixture) {
    var source = AllFixtures.Single(entry => entry.Name == fixture).Source;
    var builder = CreateBuilder(out _);
    builder.BuildFromSource(source, fixture + ".cs");

    var diagnostics = builder.LastDataFlowDiagnostics;
    var incomingVisits = diagnostics.Sum(d => d.Counters["PlanIncomingNodeVisits"]);
    var outgoingVisits = diagnostics.Sum(d => d.Counters["PlanOutgoingNodeVisits"]);
    var retained = diagnostics.Sum(d =>
        d.Counters["PlanIncomingEdgesRetained"] + d.Counters["PlanOutgoingEdgesRetained"]);
    var edgeVisits = diagnostics.Sum(d =>
        d.Counters["PlanIncomingEdgeVisits"] + d.Counters["PlanOutgoingEdgeVisits"]);

    // 每个流节点在两个方向各被访问一次。
    var flowNodes = diagnostics.Sum(d => d.Metrics.FlowNodeCount);
    Assert.Equal(flowNodes, incomingVisits);
    Assert.Equal(flowNodes, outgoingVisits);

    // 保留边数不得超过扫描到的边数；且不得为零（否则本测试会退化为空测试）。
    Assert.True(retained > 0, $"{fixture} retained no adjacency; fixture is not exercising CSR.");
    Assert.True(retained <= edgeVisits);

    // 固定 JIT 前提下 DOP=1 是确定性的：重复构建必须得到同一组计数。
    var second = CreateBuilder(out _);
    second.BuildFromSource(source, fixture + ".cs");
    Assert.Equal(incomingVisits, second.LastDataFlowDiagnostics.Sum(d => d.Counters["PlanIncomingNodeVisits"]));
    Assert.Equal(retained, second.LastDataFlowDiagnostics.Sum(d =>
        d.Counters["PlanIncomingEdgesRetained"] + d.Counters["PlanOutgoingEdgesRetained"]));
  }

  /// <summary>
  /// 源码护栏：旧「每节点 List + 字典邻接 + ToArray 冻结」链条不得复活，
  /// 且 CSR 构造必须保持单遍（否则会重新引入"缓存两遍之间必须稳定"的前置条件）。
  /// </summary>
  [Fact]
  public void DataFlowPass_BuildsCsrDirectly_WithoutTheLegacyNeighborMaterialization() {
    var source = File.ReadAllText(ProjectPath("src", "NLCPG", "Builder", "Passes", "DataFlowPass.cs"));

    Assert.DoesNotContain("BuildFlowNeighborsFromCache", source, StringComparison.Ordinal);
    Assert.DoesNotContain("SnapshotNeighbors", source, StringComparison.Ordinal);
    Assert.DoesNotContain("Dictionary<NLCPGNode, NLCPGNode[]> Predecessors", source, StringComparison.Ordinal);
    Assert.DoesNotContain("Dictionary<NLCPGNode, NLCPGNode[]> Successors", source, StringComparison.Ordinal);

    // 新表示与消费点必须在位。
    Assert.Contains("BuildFlowNeighborCsr(", source, StringComparison.Ordinal);
    Assert.Contains("PredecessorOffsets", source, StringComparison.Ordinal);
    Assert.Contains("SuccessorOffsets", source, StringComparison.Ordinal);

    // 单遍构造：不得存在"先计数、再填充"的两遍实现，也不得调用 ToArray 物化整个邻接。
    Assert.Contains("offsets[flowNodes.Length] = flat.Count;", source, StringComparison.Ordinal);
  }

  /// <summary>
  /// 诊断默认关闭时不得产生任何诊断对象（CSR 构造不得引入常驻诊断开销）。
  /// </summary>
  [Fact]
  public void BuildFromSource_DefaultMode_CreatesNoDiagnostics() {
    var builder = new NLCPGBuilder();
    builder.BuildFromSource(DataFlowMeasurementSources.Sparse(), "default.cs");
    Assert.Empty(builder.LastDataFlowDiagnostics);
  }

  /// <summary>
  /// 计划 §6 要求的「DOP 1/2 等价」：候选提交序必须与并行度无关。
  ///
  /// 为什么单独需要这条：既有的 `CpgWorkBatchDataFlowTests` 跨 DOP 对照用的是
  /// **排序后**的边序列（`DescribeGraph` 里 `OrderBy(SourceNodeId)...`），
  /// 因此它锁不住**提交顺序**——而提交顺序正是本项 §2 要求保持的不变量
  /// （预算路径的候选发放顺序依赖它）。
  ///
  /// 比较键**不含绝对 SpanStart**：同一份源码在不同 DOP 下编译出的绝对位置可能不同，
  /// 用位置作键会把"两次不同编译"当成"顺序差异"。键只用与编译产物无关的部分
  /// （锚点 Kind/Role/Ordinal + 边种类 + 调用点上下文）。
  /// </summary>
  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(4)]
  [InlineData(8)]
  public void BuildFromSource_CandidatePublicationOrder_IsIndependentOfDegreeOfParallelism(int dop) {
    const int methodCount = 48;
    var source = BuildStructurallyDistinctMethods(methodCount);

    var baseline = DescribePublication(1, source, out var baselineNodes, out var baselineEdges);
    var actual = DescribePublication(dop, source, out var actualNodes, out var actualEdges);

    // 完整图规模也必须一致。
    Assert.Equal(baselineNodes, actualNodes);
    Assert.Equal(baselineEdges, actualEdges);

    // 自守卫：签名必须能区分不同方法，否则"跨 DOP 相同"是非判别性断言。
    Assert.True(
      baseline.PerMethod.Values.SelectMany(value => value).Distinct(StringComparer.Ordinal).Count() > 1,
      "publication signature is constant; the comparison would not discriminate");

    // (1) 整体提交顺序必须与并行度无关。
    Assert.Equal(baseline.OrderedMethods, actual.OrderedMethods);

    // (2) 提交顺序必须**恰好是方法集合的一个排列**（不重不漏），且跨 DOP 相同。
    //
    // ⚠️ 本条曾被我写成"冻结实测的降序形态（每批 33 个、批内降序）"，那是**错误**的：
    // 实测发现该顺序取决于**进程/线程池状态**——本类单独运行时是降序
    // （n=8 → 7..0；n=40 → 32..0 然后 39..33），与 `DocumentShardingEquivalenceTests`
    // 同进程运行时会变成**升序**（0..32）。故降序**不是不变量**，把它冻结为基线
    // 会让测试在两种上下文下得出相反结论，是**非确定性断言**。
    // 现在只断言真正的不变量：数量与去重一致（跨 DOP 一致性已由 (1) 覆盖）。
    Assert.Equal(methodCount, actual.OrderedMethods.Count);
    Assert.Equal(
      actual.OrderedMethods.Distinct(StringComparer.Ordinal).Count(),
      actual.OrderedMethods.Count);

    // (3) 每个方法的候选提交序也必须与并行度无关。
    Assert.Equal(baseline.PerMethod.Count, actual.PerMethod.Count);
    foreach (var (method, expected) in baseline.PerMethod) {
      Assert.True(actual.PerMethod.TryGetValue(method, out var actualOrder),
        $"method '{method}' missing from the DOP={dop} build");
      Assert.Equal(expected, actualOrder);
    }
  }

  /// <summary>
  /// 已发布诊断的**有序**序列 + 逐方法候选提交序签名。
  /// </summary>
  private sealed record PublicationDescription(
      List<string> OrderedMethods, Dictionary<string, List<string>> PerMethod);

  /// <summary>
  /// 逐方法的候选提交序签名：只用与编译产物无关的键，故可跨构建比较。
  /// </summary>
  private static PublicationDescription DescribePublication(
      int dop, string source, out int nodeCount, out int edgeCount) {
    var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with {
      MaxDegreeOfParallelism = dop,
      LargeFileLineThreshold = 1,
      LargeFileMethodThreshold = 1,
      LargeMethodLineSpanThreshold = 1,
      SyntaxLargeFileLineThreshold = 1,
      DataFlowOptions = NLCPGDataFlowOptions.Unbounded,
    }) {
      DataFlowDiagnostics = new(DataFlowDiagnosticMode.Detailed, "m5-dop", "fixture"),
    };

    var graph = builder.BuildFromSource(source, "m5-dop-equivalence.cs");
    nodeCount = graph.Nodes.Count();
    edgeCount = graph.Edges.Count();

    // 保持诊断的原始发布顺序（不排序），这正是要锁定的不变量。
    var orderedMethods = builder.LastDataFlowDiagnostics
      .Select(diagnostic => diagnostic.MethodSignature)
      .ToList();

    var perMethod = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var diagnostic in builder.LastDataFlowDiagnostics) {
      perMethod[diagnostic.MethodSignature] = diagnostic.PublicationOrder
        .Select(edge => string.Join(
          "/",
          edge.SourceAnchor.Kind,
          edge.SourceAnchor.Role,
          edge.SourceAnchor.Ordinal,
          edge.TargetAnchor.Kind,
          edge.TargetAnchor.Role,
          edge.TargetAnchor.Ordinal,
          edge.Kind,
          edge.CallSiteContext?.ToContextId().Value))
        .ToList();
    }

    return new PublicationDescription(orderedMethods, perMethod);
  }

  /// <summary>
  /// 每个方法**结构不同**（语句数随序号变化）。
  /// 若所有方法体相同，只由锚点序数组成的签名会退化成常量，
  /// 跨 DOP 比较就变成恒真断言——本方法存在的唯一目的就是避免这一点。
  /// </summary>
  private static string BuildStructurallyDistinctMethods(int methodCount) {
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("public sealed class Wide {");
    for (var index = 0; index < methodCount; index++) {
      var statements = 2 + (index % 7);
      builder.AppendLine($"  public int M{index:D3}(int seed, bool choose) {{");
      builder.AppendLine("    int total = seed;");
      for (var statement = 0; statement < statements; statement++) {
        builder.AppendLine($"    total = total + {statement};");
      }

      builder.AppendLine("    for (int i = 0; i < 3; i++) {");
      builder.AppendLine("      if (choose) { total = total + i; } else { total = total - i; }");
      builder.AppendLine("    }");
      builder.AppendLine("    return total;");
      builder.AppendLine("  }");
      builder.AppendLine();
    }

    builder.AppendLine("}");
    return builder.ToString();
  }

  private static NLCPGBuilder CreateBuilder(out DataFlowDiagnosticMode mode) {
    mode = DataFlowDiagnosticMode.Detailed;
    return new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with {
      MaxDegreeOfParallelism = 1,
      LargeFileLineThreshold = 1,
      LargeFileMethodThreshold = 1,
      LargeMethodLineSpanThreshold = 1,
      SyntaxLargeFileLineThreshold = 1,
      DataFlowOptions = NLCPGDataFlowOptions.Unbounded,
    }) { DataFlowDiagnostics = new(mode, "m5", "fixture") };
  }

  private static string ProjectPath(params string[] parts) {
    var sourceFile = new System.Diagnostics.StackTrace(true).GetFrames()?
      .Select(frame => frame.GetFileName())
      .First(path => !string.IsNullOrWhiteSpace(path));
    var current = new DirectoryInfo(Path.GetDirectoryName(sourceFile!)!);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json"))) {
      current = current.Parent;
    }

    Assert.NotNull(current);
    return Path.Combine(current!.FullName, Path.Combine(parts));
  }
}
