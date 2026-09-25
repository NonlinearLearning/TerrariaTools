using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.ContractTests.Cpg;

// DoD #1 / #3（minimal-roslyn-cpg-declared-symbol-query-optimization）的定向契约：
//   DoD#1 "SyntaxPass and PartitionedSyntaxPass query declared symbols only for
//          declaration-capable syntax nodes while preserving declaration edges."
//   DoD#3 "Declaration-shape, complete-graph partition parity, and DOP determinism regressions pass."
//
// 为什么需要本文件：产品侧 SyntaxPassMetrics 是【私有嵌套类】且【只被局部变量持有】，
// 构建结束后完全不可达；NLCPGBuildMetrics 也没有任何声明符号计数字段。
// 于是"只对可声明节点发起查询"这一语义此前【没有任何可观测面】——
// 既无法在测试中断言，也无法做前后计数对比（而这正是 DoD#4 需要的量）。
// 本文件针对新提升的 SyntaxPassTelemetry 建立判据。
//
// 断言分层（刻意避免"拿产品逻辑抄一遍再和自己比"）：
//   · 上界：QueryCount < SyntaxNodeCount —— 证明过滤真的发生了（非空洞）。
//   · 下界：QueryCount >= Roslyn 独立数出的"确实解析出符号的节点数" ——
//     证明过滤没有【过度】而漏掉真实声明（漏掉就丢 DeclaresSymbol 边）。
//   · 等价：legacy 与 partitioned 两条路径的 QueryCount 必须一致（DoD#3 分区等价）。
public sealed class SyntaxPassTelemetryContractTests
{
    private readonly ITestOutputHelper _output;

    public SyntaxPassTelemetryContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 含大量【非】声明节点（表达式、语句、字面量），使"跳过数 > 0"必然成立，
    // 同时含各种声明形状（类/方法/属性/局部函数/参数/字段）。
    private const string Source = """
      namespace Demo;

      public sealed class SyntaxTelemetrySample
      {
        private int _field;

        public int Property { get; set; }

        public int Compute(int parameter)
        {
          var local = parameter + 1;
          var text = "literal";
          if (local > 3 && text.Length == 7)
          {
            int LocalFunction(int input) => input * 2;
            return LocalFunction(local);
          }

          return _field + local;
        }
      }
      """;

    [Fact]
    public void BuildFromSource_SyntaxPass_ReportsDedicatedTelemetryRatherThanLeavingItUnobservable()
    {
        var builder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        builder.BuildFromSource(Source, "syntax-telemetry.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        _output.WriteLine(
          $"nodes={telemetry.SyntaxNodeCount} tokens={telemetry.SyntaxTokenCount} " +
          $"queried={telemetry.DeclaredSymbolQueryCount} resolved={telemetry.DeclaredSymbolResolvedCount} " +
          $"skipped={telemetry.SkippedDeclaredSymbolQueryCount} partitioned={telemetry.Partitioned}");

        // 遥测被真正写入：默认值(=未运行)会让下面每条断言都失去意义。
        Assert.NotEqual(default, telemetry);
        Assert.False(telemetry.Partitioned);
        Assert.True(telemetry.SyntaxNodeCount > 0);
    }

    [Fact]
    public void BuildFromSource_SyntaxPass_QueriesStrictlyFewerNodesThanItMaterialises()
    {
        var builder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        builder.BuildFromSource(Source, "syntax-telemetry-filtered.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        // 上界 + 非空洞：源码含大量非声明节点，故必须严格小于；
        // 若把 CanDeclareSymbol 门去掉（对每个节点都查询），二者会相等。
        Assert.True(
          telemetry.DeclaredSymbolQueryCount < telemetry.SyntaxNodeCount,
          $"必须严格少查：queried={telemetry.DeclaredSymbolQueryCount} nodes={telemetry.SyntaxNodeCount}");
        Assert.True(
          telemetry.SkippedDeclaredSymbolQueryCount > 0,
          "夹具必须含非声明节点，否则本断言空洞");
    }

    [Fact]
    public void BuildFromSource_SyntaxPass_QueriesAtLeastEveryNodeThatRoslynResolvesToASymbol()
    {
        // 独立下界：直接用 Roslyn 遍历同一份源码，数出【确实能解析出声明符号】的节点。
        // 这些节点每一个都必须被产品查询过，否则 DeclaresSymbol 边就丢了。
        var expectedResolvable = CountNodesWithResolvableDeclaredSymbol(Source);
        Assert.True(expectedResolvable > 0, "夹具必须至少含一个可解析声明的节点");

        var builder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        builder.BuildFromSource(Source, "syntax-telemetry-lower-bound.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        _output.WriteLine(
          $"roslyn-resolvable={expectedResolvable} queried={telemetry.DeclaredSymbolQueryCount}");

        // 这是【下界】：产品可以查询多于"能解析出符号"的节点
        // （可声明但解析为 null 的节点也计数），但绝不能少于它们。
        Assert.True(
          telemetry.DeclaredSymbolQueryCount >= expectedResolvable,
          $"过滤过度：queried={telemetry.DeclaredSymbolQueryCount} < roslyn-resolvable={expectedResolvable}");
    }

    // DoD#1 的后半句"while preserving declaration edges"此前**只在 legacy 路径上**被证明
    // （上面的等价/下界用例都用 partitioned:false），但 DoD#1 原文同时点名
    // SyntaxPass 与 PartitionedSyntaxPass。这条把同一条"不过滤过度"的判据补到分区路径上。
    //
    // 为什么分区路径需要独立证明：分区路径的声明符号来自
    // PartitionedSyntaxPass.AnalyzeSyntaxFacts:183 的【另一道】CanDeclareSymbol 门
    // （经缓存回填，SyntaxPass.cs:324 只是转发计数）。两道门各自实现，
    // 一道过度过滤不会让另一道失败。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildFromSource_DeclarationEdges_ArePreservedOnBothSyntaxPaths(bool partitioned)
    {
        var source = CreatePartitionableSource(methodCount: 12, statementsPerMethod: 10);

        // 独立真值：问 Roslyn 本体，而不是复述产品的白名单。
        var expectedResolvable = CountNodesWithResolvableDeclaredSymbol(source);
        Assert.True(expectedResolvable > 0, "夹具必须至少含一个可解析声明的节点");

        var builder = partitioned
          ? new NLCPGBuilder(CreateOptions(dop: 4, partitioned: true, largeSource: true))
          : new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        var graph = builder.BuildFromSource(source, "syntax-edges-both-paths.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        // ⚠️ 必须只数【以语法节点为源】的声明边。
        // 第十一轮用 syntax-parity-probe 实测：DeclaresSymbol 边有【两个独立来源】——
        // 语法 pass（源是 SyntaxNode）与 MethodDecorationPass（源是 Method 节点，
        // 走自己的 MethodDecorationPass.cs:151，完全不经过 CanDeclareSymbol）。
        // 在同夹具上：legacy 总边=40（语法源 39 + 装饰 1）、
        // partitioned 总边=52（语法源 39 + 装饰 13）。
        // ⇒ 若用【总边数】做上界，分区侧有 13 条余量，足以掩盖 13 条语法声明边的丢失，
        //   这条断言就会对 DoD#1 的"preserving declaration edges"失效。
        // 故这里按源节点种类过滤，只让语法 pass 的产出参与断言。
        var syntaxNodeIds = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode)
          .Select(node => node.NodeId)
          .ToHashSet();

        var declaresSymbolEdgeCount = graph.Edges.Count(
          edge => edge.Kind == NLCPGEdgeKind.DeclaresSymbol);

        var syntaxSourcedEdgeCount = graph.Edges.Count(
          edge => edge.Kind == NLCPGEdgeKind.DeclaresSymbol &&
                  syntaxNodeIds.Contains(edge.SourceNodeId));

        _output.WriteLine(
          $"partitioned={partitioned} (actual={telemetry.Partitioned}) " +
          $"DeclaresSymbolEdges={declaresSymbolEdgeCount} " +
          $"(syntax-sourced={syntaxSourcedEdgeCount}, " +
          $"method-decoration={declaresSymbolEdgeCount - syntaxSourcedEdgeCount}) " +
          $"roslyn-resolvable={expectedResolvable}");

        // 防"跑错路径"：分区参数必须真的对应分区行为，否则这条退化成把 legacy 测两遍。
        Assert.Equal(partitioned, telemetry.Partitioned);

        // 不得把 MethodDecorationPass 的边算进来充数：那会让断言对语法边丢失失明。
        Assert.True(
          syntaxSourcedEdgeCount <= declaresSymbolEdgeCount,
          "语法源边数不可能超过总边数");

        // 不过滤过度 ⇒ 每个 Roslyn 真能解析出符号的声明都留有【语法 pass 产生的】声明边。
        Assert.True(
          syntaxSourcedEdgeCount >= expectedResolvable,
          $"[partitioned={partitioned}] 语法声明边疑似丢失：syntax-sourced={syntaxSourcedEdgeCount} " +
          $"< roslyn-resolvable={expectedResolvable} " +
          $"(总边={declaresSymbolEdgeCount}，其中 {declaresSymbolEdgeCount - syntaxSourcedEdgeCount} 条来自 MethodDecorationPass，不参与本断言)");
    }

    // DeclaresSymbol 边有【三个】发射点，这是本文件多条断言的正确性前提：
    //   ① NLCPGBuilder.cs:2848  syntaxNode -> symbolNode   源是语法节点，经 CanDeclareSymbol 门（DoD#1 的对象）
    //   ② NLCPGBuilder.cs:2861  typeDeclNode -> symbolNode  源是 TypeDecl，命名类型声明时【派生】的别名边
    //   ③ MethodDecorationPass.cs:151  methodNode -> symbolNode 源是 Method，完全不过 CanDeclareSymbol（DoD#2 的对象）
    // 后两者【都不经过】CanDeclareSymbol，是"总数"里凭空多出来的余量。
    // 第十一轮实测（12 方法夹具）：legacy 40 = 语法源 39 + TypeDecl 1；
    // partitioned 52 = 语法源 39 + TypeDecl 1 + Method 12。
    // 故 DoD#1 的"preserving declaration edges"只能拿【①】来判；用总数判会在分区侧
    // 被 13 条非语法边垫高而对丢边失明。
    // 本测试把"非语法源边只来自 ②③ 这两个已知发射点"钉成可失败的事实。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildFromSource_MethodDecorationContributesDeclaresSymbolEdgesFromNonSyntaxSources(bool partitioned)
    {
        var source = CreatePartitionableSource(methodCount: 12, statementsPerMethod: 10);

        var builder = partitioned
          ? new NLCPGBuilder(CreateOptions(dop: 4, partitioned: true, largeSource: true))
          : new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        var graph = builder.BuildFromSource(source, "declares-three-sites.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        Assert.Equal(partitioned, telemetry.Partitioned);

        var syntaxNodeIds = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode)
          .Select(node => node.NodeId)
          .ToHashSet();

        var declaresEdges = graph.Edges
          .Where(edge => edge.Kind == NLCPGEdgeKind.DeclaresSymbol)
          .ToArray();

        var nodeById = graph.Nodes.ToDictionary(node => node.NodeId, node => node);

        var syntaxSourced = declaresEdges.Count(edge => syntaxNodeIds.Contains(edge.SourceNodeId));
        var methodSourced = declaresEdges.Count(
          edge => nodeById.TryGetValue(edge.SourceNodeId, out var n) && n.Kind == NLCPGNodeKind.Method);
        var typeDeclSourced = declaresEdges.Count(
          edge => nodeById.TryGetValue(edge.SourceNodeId, out var n) && n.Kind == NLCPGNodeKind.TypeDecl);
        var unexplained = declaresEdges.Length - syntaxSourced - methodSourced - typeDeclSourced;

        _output.WriteLine(
          $"partitioned={partitioned} total={declaresEdges.Length} syntax-sourced={syntaxSourced} " +
          $"method-sourced={methodSourced} typeDecl-sourced={typeDeclSourced} unexplained={unexplained}");

        // ① 必须真实存在，否则 DoD#1 的判据没有对象。
        Assert.True(syntaxSourced > 0, "必须有语法 pass 产生的声明边");

        // 非语法源的边【必须被 ②③ 完全解释】——出现第四种来源即说明有未登记的发射点。
        Assert.True(
          unexplained == 0,
          $"存在未登记的 DeclaresSymbol 发射点：unexplained={unexplained} " +
          $"(总={declaresEdges.Length} 语法源={syntaxSourced} Method={methodSourced} TypeDecl={typeDeclSourced})");

        // ③ DoD#2 的对象：分区侧请求了 MethodModel，装饰 pass 才会产出 Method 源边。
        if (partitioned)
        {
            Assert.True(methodSourced > 0, "分区侧（MethodModel）必须产出 MethodDecorationPass 的声明边");
        }

        // Roslyn 独立真值必须完全由语法源边覆盖（这条才是 DoD#1 的判据）。
        var expectedResolvable = CountNodesWithResolvableDeclaredSymbol(source);
        Assert.True(
          syntaxSourced >= expectedResolvable,
          $"语法源声明边={syntaxSourced} 必须 >= roslyn-resolvable={expectedResolvable}");
    }

    [Fact]
    public void BuildFromSource_DeclarationEdges_ArePreservedForEveryRoslynResolvableDeclaration()
    {
        // "while preserving declaration edges" —— 可观测地核对 DeclaresSymbol 边确实建了。
        var expectedResolvable = CountNodesWithResolvableDeclaredSymbol(Source);
        var builder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        var graph = builder.BuildFromSource(Source, "syntax-telemetry-edges.cs");

        var declaresSymbolEdgeCount = graph.Edges.Count(
          edge => edge.Kind == NLCPGEdgeKind.DeclaresSymbol);

        _output.WriteLine(
          $"DeclaresSymbol edges={declaresSymbolEdgeCount} roslyn-resolvable={expectedResolvable}");

        Assert.True(declaresSymbolEdgeCount > 0, "必须真的建了 DeclaresSymbol 边");
        // 产品还会为"可声明但解析为 null"的节点建边吗？不会——AddDeclaredSymbolEdges(null) 直接返回。
        // 故边数不应少于独立数出的可解析声明数（允许产品解析到更多别名/合成符号）。
        Assert.True(
          declaresSymbolEdgeCount >= expectedResolvable,
          $"声明边疑似丢失：edges={declaresSymbolEdgeCount} < roslyn-resolvable={expectedResolvable}");
    }

    [Fact]
    public void BuildFromSource_PartitionedAndLegacySyntaxPaths_QueryTheSameNodeCount()
    {
        // DoD#3 的"分区等价"在【语法查询】这一轴上的判据：
        // 两条路径各自实现 CanDeclareSymbol 门，查询数必须一致，否则分区模式会
        // 悄悄多查或少查（少查即丢边）。
        var source = CreatePartitionableSource(methodCount: 12, statementsPerMethod: 10);

        var legacyBuilder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        legacyBuilder.BuildFromSource(source, "syntax-parity-legacy.cs");
        var legacy = legacyBuilder.LastSyntaxPassTelemetry;

        var partitionedBuilder = new NLCPGBuilder(CreateOptions(
          dop: 4,
          partitioned: true,
          largeSource: true));
        partitionedBuilder.BuildFromSource(source, "syntax-parity-partitioned.cs");
        var partitioned = partitionedBuilder.LastSyntaxPassTelemetry;

        _output.WriteLine(
          $"legacy: nodes={legacy.SyntaxNodeCount} queried={legacy.DeclaredSymbolQueryCount} " +
          $"partitioned={legacy.Partitioned}; partitioned: nodes={partitioned.SyntaxNodeCount} " +
          $"queried={partitioned.DeclaredSymbolQueryCount} partitioned={partitioned.Partitioned}");

        // 先证明两条路径都真的跑到了（否则相等是空洞的）。
        Assert.False(legacy.Partitioned, "legacy 侧必须走非分区路径");
        Assert.True(partitioned.Partitioned, "分区侧必须走分区语法路径");
        Assert.True(legacy.SyntaxNodeCount > 0 && partitioned.SyntaxNodeCount > 0);

        Assert.Equal(legacy.SyntaxNodeCount, partitioned.SyntaxNodeCount);
        Assert.Equal(legacy.DeclaredSymbolQueryCount, partitioned.DeclaredSymbolQueryCount);
        Assert.Equal(legacy.DeclaredSymbolResolvedCount, partitioned.DeclaredSymbolResolvedCount);
    }

    // DoD#3 "complete-graph partition parity" 的【前提】判据：先钉死"哪条语法路径被选中"。
    //
    // 为什么需要：本文件早先真踩过坑——请求的能力集到不了分区路径，于是"分区侧"
    // 静默跑的是 legacy 路径（见 CreateOptions 里的记录）。第十轮用
    // path-selection-probe 实测 12 组（能力集 × 源码规模 × 大文件阈值）确认：
    // 决定路径的【只有】能力集——只要蕴含 MethodModel，OperationRoots 才被装配，
    // 而阈值 20 与 800 的结果【完全一致】。本测试把这条选择规则变成可失败的事实。
    [Theory]
    [InlineData(NLCPGCapability.SyntaxSemantic, false)]
    [InlineData(NLCPGCapability.MethodModel, true)]
    public void BuildFromSource_SyntaxPathSelection_IsGovernedByMethodModelCapability(
      NLCPGCapability capability,
      bool expectPartitioned)
    {
        // 同一份源码、同一 DOP，唯一变量是能力集。
        var source = CreatePartitionableSource(methodCount: 12, statementsPerMethod: 10);

        var builder = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = 1,
            RequestedCapabilities = new[] { capability },
        });
        builder.BuildFromSource(source, "syntax-path-selection.cs");
        var telemetry = builder.LastSyntaxPassTelemetry;

        _output.WriteLine(
          $"capability={capability} -> partitioned={telemetry.Partitioned} " +
          $"(expected {expectPartitioned}), nodes={telemetry.SyntaxNodeCount} " +
          $"queried={telemetry.DeclaredSymbolQueryCount}");

        Assert.Equal(expectPartitioned, telemetry.Partitioned);

        // 防"读到默认值"：Partitioned 的默认是 false，故必须证明 SyntaxPass 真跑过。
        Assert.True(telemetry.SyntaxNodeCount > 0, "SyntaxPass 必须真的运行过");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    public void BuildFromSource_SyntaxPassTelemetry_IsStableAcrossDegreesOfParallelism(int dop)
    {
        var source = CreatePartitionableSource(methodCount: 12, statementsPerMethod: 10);
        var baseline = BuildPartitioned(source, dop: 1);
        var actual = BuildPartitioned(source, dop: dop);

        // 耗时字段属环境量，排除在跨 DOP 比较外。
        Assert.Equal(Describe(baseline.Builder.LastSyntaxPassTelemetry),
          Describe(actual.Builder.LastSyntaxPassTelemetry));
        Assert.Equal(DescribeGraph(baseline.Graph), DescribeGraph(actual.Graph));
    }

    // 记录一处【已知边界】而非缺陷：switch 标签（case/default）会物化为 SyntaxNode，
    // 但【没有】DeclaresSymbol 出边，因为 CanDeclareSymbol 白名单收的是
    // LabeledStatementSyntax（goto 标签），没有收 CaseSwitchLabelSyntax /
    // DefaultSwitchLabelSyntax。相对"去门基线"，这会少建等同数量的声明边。
    //
    // 为什么值得固化：该边界此前【没有任何测试覆盖】——夹具里虽有 switch
    // （CpgBuilderSources.ControlFlowAndDataFlowHeavy），却无人断言过标签的声明边，
    // 这正是它长期未被发现的原因。本测试把缺口变成套件内可观测事实；
    // 将来若把这两个 Kind 加入白名单，本测试会【失败】并强制更新这条记录。
    //
    // 严重度已单独界定：这不是图不一致。Roslyn 的 GetSymbolInfo 对
    // goto case / goto default 一律返回 null，故不存在"引用了一个从未被声明"的
    // 悬空符号；被漏掉的标签是【引用不可达】的，只是信息缺失。
    [Fact]
    public void BuildFromSource_SwitchLabels_AreMaterialisedButCarryNoDeclarationEdge()
    {
        const string source = """
          namespace Demo;

          public sealed class SwitchLabelSample
          {
            public int Run(int total)
            {
              switch (total)
              {
                case 0:
                  total += 10;
                  break;
                case 1:
                case 2:
                  total += 20;
                  break;
                default:
                  total += 30;
                  break;
              }

              goto done;
            done:
              return total;
            }
          }
          """;

        var graph = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false))
          .BuildFromSource(source, "syntax-switch-labels.cs");

        static int CountWithDeclarationEdge(NLCPGGraph graph, string displayKind)
        {
            return graph.Nodes
              .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode &&
                             graph.ResolveDisplayKind(node) == displayKind)
              .Count(node => graph.Edges.Any(edge =>
                edge.SourceNodeId == node.NodeId && edge.Kind == NLCPGEdgeKind.DeclaresSymbol));
        }

        static int CountNodes(NLCPGGraph graph, string displayKind)
        {
            return graph.Nodes.Count(node =>
              node.Kind == NLCPGNodeKind.SyntaxNode &&
              graph.ResolveDisplayKind(node) == displayKind);
        }

        var caseLabels = CountNodes(graph, "CaseSwitchLabel");
        var defaultLabels = CountNodes(graph, "DefaultSwitchLabel");
        var gotoLabels = CountNodes(graph, "LabeledStatement");

        _output.WriteLine(
          $"CaseSwitchLabel={caseLabels} DefaultSwitchLabel={defaultLabels} LabeledStatement={gotoLabels}; " +
          $"declarationEdges: case={CountWithDeclarationEdge(graph, "CaseSwitchLabel")} " +
          $"default={CountWithDeclarationEdge(graph, "DefaultSwitchLabel")} " +
          $"goto={CountWithDeclarationEdge(graph, "LabeledStatement")}");

        // 防空洞：夹具必须真的含这些标签，否则下面的 0 断言毫无意义。
        Assert.True(caseLabels > 0, "夹具必须含 CaseSwitchLabel 语法节点");
        Assert.True(defaultLabels > 0, "夹具必须含 DefaultSwitchLabel 语法节点");
        Assert.True(gotoLabels > 0, "夹具必须含 LabeledStatement 语法节点（对照组）");

        // 对照组：白名单【内】的 goto 标签必须有声明边。
        // 这一条同时证明上面的 0 不是"整张图都没建声明边"的假象。
        Assert.True(
          CountWithDeclarationEdge(graph, "LabeledStatement") > 0,
          "对照组失败：白名单内的 LabeledStatement 应当有 DeclaresSymbol 边");

        // 记录的边界：白名单【外】的 switch 标签没有声明边。
        Assert.Equal(0, CountWithDeclarationEdge(graph, "CaseSwitchLabel"));
        Assert.Equal(0, CountWithDeclarationEdge(graph, "DefaultSwitchLabel"));
    }

    // DoD#1 的定量判据，与上面那条互补（那条看"边"，这条看"查询计数"）：
    // 把 switch（含 case/default 标签）加进同一份源码，只允许【语法节点数】增加，
    // 而【声明符号查询数】必须一字不变——因为 switch 标签不在 CanDeclareSymbol
    // 白名单内，门必须把它们【全部跳过】。DoD#1 同时点名 SyntaxPass 与
    // PartitionedSyntaxPass，故两条路径都必须成立，用 partitioned 参数覆盖。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildFromSource_SwitchLabels_AddSyntaxNodesButNoDeclaredSymbolQueries(bool partitioned)
    {
        const int methodCount = 12;
        const int statementsPerMethod = 10;
        var withoutSwitch = CreateSwitchComparableSource(methodCount, statementsPerMethod, includeSwitch: false);
        var withSwitch = CreateSwitchComparableSource(methodCount, statementsPerMethod, includeSwitch: true);

        var baseline = BuildForSwitchComparison(withoutSwitch, partitioned, "switch-query-baseline.cs");
        var actual = BuildForSwitchComparison(withSwitch, partitioned, "switch-query-actual.cs");

        _output.WriteLine(
          $"partitioned={partitioned} baseline: nodes={baseline.SyntaxNodeCount} " +
          $"queried={baseline.DeclaredSymbolQueryCount}; actual: nodes={actual.SyntaxNodeCount} " +
          $"queried={actual.DeclaredSymbolQueryCount}");

        // 先证明两条都真的走到了预期路径，否则"分区侧"可能悄悄跑的是 legacy，
        // 于是这条测试就退化成把同一条路径测了两遍（本文件早先踩过这个坑）。
        Assert.Equal(partitioned, baseline.Partitioned);
        Assert.Equal(partitioned, actual.Partitioned);

        // 夹具确实加了东西：节点数必须变多。
        Assert.True(
          actual.SyntaxNodeCount > baseline.SyntaxNodeCount,
          $"加入 switch 后语法节点数应增加：{baseline.SyntaxNodeCount} -> {actual.SyntaxNodeCount}");

        // 防"0 == 0"空洞：分区路径的查询数是经 cachedFacts.QueriedDeclaredSymbol 传来的
        // （SyntaxPass.cs:324），其赋值发生在 PartitionedSyntaxPass.AnalyzeSyntaxFacts:183
        // 的【另一道】CanDeclareSymbol 门上。万一那道门的计数失效恒为 0，
        // 下面的相等断言会在 0 == 0 上【静默通过】而毫无判别力。
        // 故必须先证明计数器是活的：夹具含方法/类/参数等可声明节点，查询数必 > 0。
        Assert.True(
          baseline.DeclaredSymbolQueryCount > 0,
          $"计数器疑似失效（恒 0），相等断言将失去判别力：queried={baseline.DeclaredSymbolQueryCount}");
        Assert.True(actual.DeclaredSymbolQueryCount > 0,
          $"计数器疑似失效（恒 0）：queried={actual.DeclaredSymbolQueryCount}");

        // 核心断言：多出来的 switch 标签【一个都没有】被查询。
        Assert.Equal(baseline.DeclaredSymbolQueryCount, actual.DeclaredSymbolQueryCount);
    }

    // DoD#3 "complete-graph partition parity" 在【语法子图】这一轴上的判据。
    //
    // 为什么必须单独做这一条：真正的 "legacy vs 分区" 无法在【相同能力集】下构造
    // （路径由能力集唯一决定，见 SyntaxPathSelection 那条），而两条路径的【整图】
    // 必然不同——分区侧请求了 MethodModel，会多出 Method/Operation 等节点。
    // 但本优化的作用域是【语法层】，故可比较二者的公共子集：
    //   ① 语法节点（Kind == SyntaxNode）的 (DisplayKind, FullName, Span) 集合
    //   ② 两端皆为语法节点的 SyntaxChild 边集合
    //   ③ 【以语法节点为源】的 DeclaresSymbol 边集合
    // 第十一轮用只读探针（Build/M1-methoddecoration/syntax-parity-probe）实测三者逐项相同；
    // 探针在 gitignore 的 Build/ 下、不是持久证据，故第十三轮把它固化为可失败契约。
    [Fact]
    public void BuildFromSource_SyntaxSubgraph_IsIdenticalOnBothSyntaxPaths()
    {
        var source = Source;

        // 两侧必须用【同一】filePath：节点 FullName 含路径，
        // 路径不同会让描述符出现与被测行为无关的差异（本文件此前踩过这个坑）。
        const string path = "syntax-subgraph-parity.cs";

        var legacyBuilder = new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        var legacyGraph = legacyBuilder.BuildFromSource(source, path);
        var legacyTelemetry = legacyBuilder.LastSyntaxPassTelemetry;

        var partitionedBuilder = new NLCPGBuilder(
          CreateOptions(dop: 4, partitioned: true, largeSource: true));
        var partitionedGraph = partitionedBuilder.BuildFromSource(source, path);
        var partitionedTelemetry = partitionedBuilder.LastSyntaxPassTelemetry;

        _output.WriteLine(
          $"legacy: partitioned={legacyTelemetry.Partitioned} " +
          $"syntaxNodes={legacyTelemetry.SyntaxNodeCount} nodes={legacyGraph.Nodes.Count} " +
          $"edges={legacyGraph.Edges.Count}; partitioned: " +
          $"partitioned={partitionedTelemetry.Partitioned} " +
          $"syntaxNodes={partitionedTelemetry.SyntaxNodeCount} nodes={partitionedGraph.Nodes.Count} " +
          $"edges={partitionedGraph.Edges.Count}");

        // 前提：两条路径必须真的分别命中，否则"等价"退化成把同一条路径测两遍。
        Assert.False(legacyTelemetry.Partitioned, "legacy 侧必须走非分区语法路径");
        Assert.True(partitionedTelemetry.Partitioned, "分区侧必须走分区语法路径");

        // 也要证明两侧确实【不是同一张图】：分区侧多出 MethodModel 相关节点。
        // 否则下面的"子图相同"可能来自整图本来就相同这种平凡情形。
        Assert.True(
          partitionedGraph.Nodes.Count > legacyGraph.Nodes.Count,
          $"分区侧应因 MethodModel 而含更多节点：legacy={legacyGraph.Nodes.Count} " +
          $"partitioned={partitionedGraph.Nodes.Count}");

        var legacySubgraph = DescribeSyntaxSubgraph(legacyGraph);
        var partitionedSubgraph = DescribeSyntaxSubgraph(partitionedGraph);

        _output.WriteLine(
          $"syntax-subgraph descriptors: legacy={legacySubgraph.Length} " +
          $"partitioned={partitionedSubgraph.Length}");

        // 防空洞：子图必须非空且含边，否则"空 == 空"会静默通过。
        Assert.True(legacySubgraph.Length > 0, "语法子图描述符不得为空");
        Assert.True(
          legacySubgraph.Any(line => line.StartsWith("E|", StringComparison.Ordinal)),
          "语法子图必须含边（否则只比较了孤立节点集合）");

        Assert.Equal(legacySubgraph, partitionedSubgraph);
    }

    // 语法子图描述符：只取语法节点、语法节点之间的 SyntaxChild 边、
    // 以及【以语法节点为源】的 DeclaresSymbol 边。
    // 刻意排除以 Method/TypeDecl 为源的声明边——那两个来源不走 CanDeclareSymbol，
    // 混进来会让本判据对语法边丢失失明（第十一轮已量化该掩蔽余量）。
    private static string[] DescribeSyntaxSubgraph(NLCPGGraph graph)
    {
        var syntaxNodeIds = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode)
          .Select(node => node.NodeId)
          .ToHashSet();

        var nodeById = graph.Nodes.ToDictionary(node => node.NodeId, node => node);

        var nodes = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.SyntaxNode)
          .OrderBy(node => node.SpanStart)
          .ThenBy(node => node.SpanEnd)
          .ThenBy(node => graph.ResolveDisplayKind(node), StringComparer.Ordinal)
          .Select(node => string.Join(
            "|",
            "N",
            graph.ResolveDisplayKind(node),
            graph.ResolveFullName(node),
            node.SpanStart,
            node.SpanEnd))
          .ToArray();

        var edges = graph.Edges
          .Where(edge =>
            (edge.Kind == NLCPGEdgeKind.SyntaxChild &&
             syntaxNodeIds.Contains(edge.SourceNodeId) &&
             syntaxNodeIds.Contains(edge.TargetNodeId)) ||
            (edge.Kind == NLCPGEdgeKind.DeclaresSymbol &&
             syntaxNodeIds.Contains(edge.SourceNodeId)))
          .Select(edge => string.Join(
            "|",
            "E",
            edge.Kind,
            graph.ResolveDisplayKind(nodeById[edge.SourceNodeId]),
            graph.ResolveFullName(nodeById[edge.SourceNodeId]),
            graph.ResolveFullName(nodeById[edge.TargetNodeId])))
          .OrderBy(line => line, StringComparer.Ordinal)
          .ToArray();

        return nodes.Concat(edges).ToArray();
    }

    private static NLCPGBuilder.SyntaxPassTelemetry BuildForSwitchComparison(
      string source,
      bool partitioned,
      string path)
    {
        var builder = partitioned
          ? new NLCPGBuilder(CreateOptions(dop: 4, partitioned: true, largeSource: true))
          : new NLCPGBuilder(CreateOptions(dop: 1, partitioned: false));
        builder.BuildFromSource(source, path);
        return builder.LastSyntaxPassTelemetry;
    }

    // 与 CreatePartitionableSource 同形，仅多/少一个含 case+default 的 switch，
    // 使"节点数变化"与"查询数不变"能被归因到 switch 标签本身。
    private static string CreateSwitchComparableSource(
      int methodCount,
      int statementsPerMethod,
      bool includeSwitch)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("namespace Demo;");
        builder.AppendLine("public sealed class SwitchComparableSample");
        builder.AppendLine("{");
        builder.AppendLine("  private int _field;");
        for (var methodIndex = 0; methodIndex < methodCount; methodIndex++)
        {
            builder.AppendLine($"  public int Method{methodIndex}(int parameter)");
            builder.AppendLine("  {");
            builder.AppendLine("    var local = parameter;");
            for (var statementIndex = 0; statementIndex < statementsPerMethod; statementIndex++)
            {
                builder.AppendLine($"    local = local + {statementIndex};");
            }

            if (includeSwitch)
            {
                builder.AppendLine("    switch (local)");
                builder.AppendLine("    {");
                builder.AppendLine("      case 0:");
                builder.AppendLine("        local += 10;");
                builder.AppendLine("        break;");
                builder.AppendLine("      case 1:");
                builder.AppendLine("        local += 20;");
                builder.AppendLine("        break;");
                builder.AppendLine("      default:");
                builder.AppendLine("        local += 30;");
                builder.AppendLine("        break;");
                builder.AppendLine("    }");
            }

            builder.AppendLine("    return local;");
            builder.AppendLine("  }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static (NLCPGBuilder Builder, NLCPGGraph Graph) BuildPartitioned(string source, int dop)
    {
        var builder = new NLCPGBuilder(CreateOptions(dop, partitioned: true, largeSource: true));
        // 路径必须逐次相同：节点 FullName 含文件路径，换了文件名会让图描述符出现
        // 与被测行为无关的差异（本测试第一版就踩了这个坑）。
        return (builder, builder.BuildFromSource(source, "syntax-partitioned.cs"));
    }

    // 独立真值：不做任何"可声明节点种类"判断（那会把产品逻辑抄一遍），
    // 而是直接问 Roslyn：这个节点到底能不能解析出声明符号。
    private static int CountNodesWithResolvableDeclaredSymbol(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
          "SyntaxTelemetryOracle",
          new[] { tree },
          options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot()
          .DescendantNodes()
          .Count(node => model.GetDeclaredSymbol(node) is not null);
    }

    private static string Describe(NLCPGBuilder.SyntaxPassTelemetry telemetry)
    {
        return string.Join(
          "|",
          telemetry.DeclaredSymbolQueryCount,
          telemetry.DeclaredSymbolResolvedCount,
          telemetry.SyntaxNodeCount,
          telemetry.Partitioned);
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

    private static string CreatePartitionableSource(int methodCount, int statementsPerMethod)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("namespace Demo;");
        builder.AppendLine("public sealed class PartitionableSample");
        builder.AppendLine("{");
        builder.AppendLine("  private int _field;");
        for (var methodIndex = 0; methodIndex < methodCount; methodIndex++)
        {
            builder.AppendLine($"  public int Method{methodIndex}(int parameter)");
            builder.AppendLine("  {");
            builder.AppendLine("    var local = parameter;");
            for (var statementIndex = 0; statementIndex < statementsPerMethod; statementIndex++)
            {
                builder.AppendLine($"    local = local + {statementIndex};");
            }

            builder.AppendLine("    return local;");
            builder.AppendLine("  }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static NLCPGBuilderOptions CreateOptions(
      int dop,
      bool partitioned,
      bool largeSource = false)
    {
        if (partitioned && largeSource)
        {
            // 分区语法路径的门是 operationBuildStrategy.OperationRoots.Count > 0，
            // 而 OperationRoots 只在 buildPlan.RequiresMethodModel 为真时才装配
            // （NLCPGBuilder.cs:371-377）⇒ 必须请求一个蕴含 MethodModel 的能力，
            // 否则 ShouldUsePartitionedSyntaxPass 恒为 false——本测试第一版
            // 只请求 SyntaxSemantic，于是"分区侧"实际跑的是 legacy 路径。
            //
            // ⚠️ 下面那几个阈值【不是】触发条件：第十轮用 path-selection-probe
            // 实测（12 组 能力集×源码规模×阈值 全排列）证明，阈值 20 与 800 的结果
            // 完全相同，唯一决定路径的是能力集。保留它们只为贴近历史配置，
            // 不得据此以为"把阈值调低就会走分区"。
            return NLCPGBuilderOptions.CreateDefault() with
            {
                MaxDegreeOfParallelism = dop,
                RequestedCapabilities = new[] { NLCPGCapability.MethodModel },
                LargeFileLineThreshold = 20,
                LargeFileMethodThreshold = 2,
                LargeMethodLineSpanThreshold = 6,
                SyntaxLargeFileLineThreshold = 20,
            };
        }

        // 非分区侧只请求语法相关能力，避免其他 pass 干扰对语法查询轴的解释。
        return NLCPGBuilderOptions.CreateDefault() with
        {
            MaxDegreeOfParallelism = dop,
            RequestedCapabilities = new[] { NLCPGCapability.SyntaxSemantic },
        };
    }
}
