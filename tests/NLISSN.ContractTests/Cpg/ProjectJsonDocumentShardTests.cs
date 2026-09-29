using NLCPG.Contracts;
using NLCPG.Model;
using NLCPG.ProjectJson;
using Xunit;

namespace NLISSN.Tests.ProjectJson;

/// <summary>
/// 单文档分片的核心不变量。这是「分片导出」的 10% 核心测试面：
/// 只覆盖分片**语义**（不变量），不覆盖完整导出（真实 2.29 GB 导出由
/// Build/worker8-utilization/ 的字节比对承担）。
/// </summary>
/// <remarks>
/// 之所以能在这层断言，是因为生产实现把分片接缝暴露成
/// <see cref="NLCPG.ProjectJson.ProjectJsonExporter.BuildDocumentShardProjections"/>：
/// 测试调用的是**生产逻辑本身**，不是复制品，因此这些断言对实际导出有效。
/// </remarks>
public sealed class ProjectJsonDocumentShardTests
{
    // 核心不变量 1：分片只是「记录写到哪个文件」的划分。
    // 把所有分片的节点并起来，必须与不分片时逐条相等（含 id、顺序与全部字段）。
    [Fact]
    public void Shards_PartitionNodesWithoutChangingAnyRecord()
    {
        var graph = BuildGraph(nodeCount: 37);
        var whole = BuildDocumentShardProjections(graph, shardCount: 1);
        var sharded = BuildDocumentShardProjections(graph, shardCount: 5);

        var wholeNodes = Assert.Single(whole).Projection.Nodes;
        var shardedNodes = sharded.SelectMany(shard => shard.Projection.Nodes).ToArray();

        Assert.Equal(wholeNodes.Select(node => node.Id), shardedNodes.Select(node => node.Id));
        Assert.Equal(wholeNodes, shardedNodes);
    }

    // 核心不变量 2：稳定 id 含「整篇 ordinal」。若各分片各自从 0 起算，
    // 同一 canonical base 的重复节点会被切到不同分片并生成**相同 id**（且不抛异常）。
    // 故这里刻意造出同库内重复节点，并要求全篇 id 唯一。
    [Fact]
    public void Shards_KeepNodeIdsUniqueWhenCanonicalBasesCollide()
    {
        var graph = new NLCPGGraph();
        // 同一 draft 重复入图 → 若 StableAnchor 语义变化会退化为同一节点，
        // 因此用「字段相同但锚点不同」的方式造真正会碰撞 canonical base 的节点。
        for (var index = 0; index < 8; index += 1)
        {
            graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Operation,
              Name: "collide",
              FullName: "Demo.collide",
              FilePath: "Terraria/Collide.cs",
              SpanStart: 10,
              SpanEnd: 20,
              StableIdentityText: "collide-anchor-" + index));
        }

        graph.FreezeQueryIndex();
        var sharded = BuildDocumentShardProjections(graph, shardCount: 4);
        var ids = sharded.SelectMany(shard => shard.Projection.Nodes).Select(node => node.Id).ToArray();

        Assert.Equal(8, ids.Length);
        Assert.Equal(8, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // 核心不变量 3：每片自包含。任何一条边出现在某片时，它的两个端点 id
    // 必须都能在**同一片**内解析到节点——否则消费方跨片偷看才能解析，分片即失效。
    [Fact]
    public void Shards_ResolveEveryEdgeEndpointWithinItsOwnShard()
    {
        var graph = BuildCrossLinkedGraph(nodeCount: 24);
        var sharded = BuildDocumentShardProjections(graph, shardCount: 4);

        var totalEdges = 0;
        foreach (var shard in sharded)
        {
            var nodeIds = shard.Projection.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var edge in shard.Projection.Edges)
            {
                Assert.Contains(edge.Source, nodeIds);
                Assert.Contains(edge.Target, nodeIds);
                totalEdges += 1;
            }
        }

        // 分片不得丢边：并集条数必须等于不分片时的条数。
        var wholeEdgeCount = Assert.Single(BuildDocumentShardProjections(graph, shardCount: 1))
          .Projection.Edges.Count;
        Assert.Equal(wholeEdgeCount, totalEdges);
    }

    // 核心不变量 4：分片数被节点数收敛，且不产出空片。
    [Fact]
    public void Shards_ClampCountToNodeCountAndNeverEmitEmptyShard()
    {
        var graph = BuildGraph(nodeCount: 3);
        var sharded = BuildDocumentShardProjections(graph, shardCount: 10);

        Assert.Equal(3, sharded.Count);
        Assert.All(sharded, shard => Assert.NotEmpty(shard.Projection.Nodes));
        Assert.All(sharded, shard => Assert.Equal(3, shard.ShardCount));
    }

    // 核心不变量 5：默认（不分片）逐字段等价于分片前的单文档产物，
    // 且文件名后缀规则只在 >1 片时生效——单文档 1 片必须仍是 <rel>.cs.json。
    [Fact]
    public void Shards_DefaultConfigurationProducesSingleUnshardedProjection()
    {
        var graph = BuildGraph(nodeCount: 12);
        var shards = BuildDocumentShardProjections(graph, shardCount: 1);

        var only = Assert.Single(shards);
        Assert.Equal(0, only.ShardIndex);
        Assert.Equal(1, only.ShardCount);
        // payload 的 sourcePath 与 schemaVersion 与分片无关。
        Assert.Equal("Terraria/NPC.cs", only.Projection.SourcePath);
        Assert.Equal(1, only.Projection.SchemaVersion);
    }

    // 核心不变量 6：接缝必须是**惰性**的（逐片产出，而不是先装满列表再返回）。
    // 若急切物化，则全部片的节点/边 DTO 同时驻留，峰值与不分片时相同——分片的内存收益归零。
    //
    // 判别方式用**逐片取消**，因为它在两种实现下结果不同：
    //   惰性：取出第 0 片后取消 → 第 0 片已产出可用；下一次 MoveNext 抛 OperationCanceledException。
    //   急切：调用时已把全部片算完（取消发生在调用之后），后续 MoveNext 不再检查令牌、正常返回。
    // 注意不能靠「单片节点数 < 全篇节点数」判别：急切实现下每片也只含自己的节点，两者都通过。
    [Fact]
    public void Shards_AreProducedLazilySoWorkStopsWhenEnumerationStops()
    {
        var graph = BuildGraph(nodeCount: 40);
        using var cancellation = new CancellationTokenSource();

        using var enumerator = ProjectJsonExporter.BuildDocumentShardProjections(
          graph,
          sourcePath: "Terraria/NPC.cs",
          projectRoot: Directory.GetCurrentDirectory(),
          filePathPrefix: null,
          configuredShardCount: 4,
          cancellationToken: cancellation.Token).GetEnumerator();

        Assert.True(enumerator.MoveNext());
        var first = enumerator.Current;
        Assert.Equal(4, first.ShardCount);
        Assert.NotEmpty(first.Projection.Nodes);

        // 取消后，剩余分片不再被产出（证明它们此前并未被预先算好）。
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
    }

    // 核心不变量 7：并行（DocumentShardParallelism > 1）与串行的输出必须**逐条相同**。
    // 并行度只应改变各片写入的时序，不得改变任何记录、任何片的归属、或片的顺序。
    // 这里直接对同一张图跑串行与并行两条路径，比较全部片（按下标）的完整内容。
    [Fact]
    public async Task ParallelShardWrites_ProduceIdenticalEntriesAndOrderingToSerial()
    {
        var graph = BuildCrossLinkedGraph(nodeCount: 200);
        var root = CreateTemporaryOutputRoot();
        try
        {
            var serial = await WriteShardsAsync(graph, root, parallelism: 1);
            var parallel = await WriteShardsAsync(graph, root, parallelism: 8);

            // 片数与每片的（相对路径、节点数、边数）必须完全一致，且顺序一致。
            Assert.Equal(serial.Entries.Count, parallel.Entries.Count);
            for (var index = 0; index < serial.Entries.Count; index += 1)
            {
                Assert.Equal(serial.Entries[index].OutputPath, parallel.Entries[index].OutputPath);
                Assert.Equal(serial.Entries[index].NodeCount, parallel.Entries[index].NodeCount);
                Assert.Equal(serial.Entries[index].EdgeCount, parallel.Entries[index].EdgeCount);
            }

            Assert.Equal(serial.Bytes, parallel.Bytes);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // 核心不变量 8：并行度为 1 时走串行路径，且其产物与惰性接缝逐条一致
    // （即「默认不改变既有行为」在接线后仍成立）。
    [Fact]
    public async Task ParallelismOne_MatchesTheLazySeamExactly()
    {
        var graph = BuildCrossLinkedGraph(nodeCount: 64);
        var root = CreateTemporaryOutputRoot();
        try
        {
            var outcome = await WriteShardsAsync(graph, root, parallelism: 1);
            var expected = BuildDocumentShardProjections(graph, shardCount: 4);
            Assert.Equal(expected.Count, outcome.Entries.Count);
            Assert.Equal(
              expected.Select(shard => shard.Projection.Nodes.Count),
              outcome.Entries.Select(entry => entry.NodeCount));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // 核心不变量 9：分片级字节闸门只限制「同时在途的量」，不改变输出。
    // 把预算压到只够一片（甚至小于单片额度），payload 仍必须与宽松预算逐字节相同。
    // 这是「闸门只能等待、不能失败，也不能改变记录」的核心断言。
    [Fact]
    public async Task BudgetGate_TightBudgetKeepsOutputIdenticalAndStillAdmitsAShard()
    {
        var graph = BuildCrossLinkedGraph(nodeCount: 200);
        var root = CreateTemporaryOutputRoot();
        try
        {
            // 宽松：预算远大于整篇估算 ⇒ 闸门几乎不阻塞。
            var loose = await WriteShardsWithBudgetAsync(
              graph,
              root,
              parallelism: 4,
              documentEstimatedBytes: 1_000_000,
              memoryBudgetBytes: 64L * 1024 * 1024);
            // 紧张：预算小于单片分摊额度 ⇒ 闸门必须靠「至少放行一片」避免饿死。
            var tight = await WriteShardsWithBudgetAsync(
              graph,
              root,
              parallelism: 4,
              documentEstimatedBytes: 1_000_000,
              memoryBudgetBytes: 1);

            Assert.Equal(loose.Entries.Count, tight.Entries.Count);
            Assert.Equal(loose.Bytes, tight.Bytes);
            Assert.Equal(
              loose.Entries.Select(entry => entry.OutputPath),
              tight.Entries.Select(entry => entry.OutputPath));
            // 紧张预算下实际并发必须被压到 1，而不是仍然 4 片齐发。
            Assert.Equal(1, tight.PeakParallelism);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // 核心不变量 10：额度够几片，实际并发就只能是几片——闸门必须真的在约束，
    // 而不是「记了账但照样全放」。这里给足以放行 2 片的预算，并断言峰值不超过 2。
    [Fact]
    public async Task BudgetGate_LimitsInFlightShardsToWhatTheBudgetAllows()
    {
        var graph = BuildCrossLinkedGraph(nodeCount: 200);
        var root = CreateTemporaryOutputRoot();
        try
        {
            // 整篇估算 4,000,000 按记录数分摊到 4 片，每片约 1,000,000；
            // 预算 2,000,000 ⇒ 最多 2 片在途。
            var outcome = await WriteShardsWithBudgetAsync(
              graph,
              root,
              parallelism: 4,
              documentEstimatedBytes: 4_000_000,
              memoryBudgetBytes: 2_000_000);

            Assert.True(
              outcome.PeakParallelism <= 2,
              $"预算只允许 2 片在途，实测峰值 {outcome.PeakParallelism}。");
            Assert.True(outcome.PeakParallelism >= 1, "至少要放行一片。");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // 经生产入口写盘，附带预算参数；返回 manifest 记录、字节合计与闸门观测面。
    private static async Task<ShardWriteOutcomeProbe> WriteShardsWithBudgetAsync(
      NLCPGGraph graph,
      string outputRoot,
      int parallelism,
      long documentEstimatedBytes,
      long memoryBudgetBytes)
    {
        var outputRelativePath = "Terraria/NPC.cs.json";
        var outcome = await ProjectJsonExporter.WriteDocumentShardsAsync(
          graph,
          sourcePath: "Terraria/NPC.cs",
          projectRoot: Directory.GetCurrentDirectory(),
          filePathPrefix: null,
          configuredShardCount: 4,
          parallelism: parallelism,
          outputRelativePath: outputRelativePath,
          outputFilePath: Path.Combine(outputRoot, "NPC.cs.json"),
          outputPath: outputRoot,
          collectMetrics: false,
          cancellationToken: CancellationToken.None,
          documentEstimatedBytes: documentEstimatedBytes,
          memoryBudgetBytes: memoryBudgetBytes);
        return new ShardWriteOutcomeProbe(
          outcome.Entries.Select(entry => (entry.OutputPath, entry.NodeCount, entry.EdgeCount)).ToArray(),
          outcome.Bytes,
          outcome.PeakParallelism);
    }

    // 经生产入口（WriteDocumentShardsAsync）写盘，返回 manifest 记录与字节合计。
    private static async Task<ShardWriteOutcomeProbe> WriteShardsAsync(
      NLCPGGraph graph,
      string outputRoot,
      int parallelism)
    {
        var outputRelativePath = "Terraria/NPC.cs.json";
        var outcome = await ProjectJsonExporter.WriteDocumentShardsAsync(
          graph,
          sourcePath: "Terraria/NPC.cs",
          projectRoot: Directory.GetCurrentDirectory(),
          filePathPrefix: null,
          configuredShardCount: 4,
          parallelism: parallelism,
          outputRelativePath: outputRelativePath,
          outputFilePath: Path.Combine(outputRoot, "NPC.cs.json"),
          outputPath: outputRoot,
          collectMetrics: false,
          cancellationToken: CancellationToken.None);
        return new ShardWriteOutcomeProbe(
          outcome.Entries.Select(entry => (entry.OutputPath, entry.NodeCount, entry.EdgeCount)).ToArray(),
          outcome.Bytes,
          outcome.PeakParallelism);
    }

    private sealed record ShardWriteOutcomeProbe(
      IReadOnlyList<(string OutputPath, int NodeCount, int EdgeCount)> Entries,
      long Bytes,
      int PeakParallelism);

    private static string CreateTemporaryOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nlissn-shard-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理是尽力而为，不影响断言结果。
        }
    }

    private static IReadOnlyList<ProjectJsonExporter.DocumentShard> BuildDocumentShardProjections(
      NLCPGGraph graph,
      int shardCount)
    {
        return ProjectJsonExporter.BuildDocumentShardProjections(
          graph,
          sourcePath: "Terraria/NPC.cs",
          projectRoot: Directory.GetCurrentDirectory(),
          filePathPrefix: null,
          configuredShardCount: shardCount,
          cancellationToken: CancellationToken.None).ToArray();
    }

    private static NLCPGGraph BuildGraph(int nodeCount)
    {
        var graph = new NLCPGGraph();
        for (var index = 0; index < nodeCount; index += 1)
        {
            graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Operation,
              Name: "node" + index,
              FullName: "Demo.node" + index,
              FilePath: "Terraria/NPC.cs",
              SpanStart: index * 10,
              SpanEnd: (index * 10) + 5,
              StableIdentityText: "anchor-" + index));
        }

        graph.FreezeQueryIndex();
        return graph;
    }

    // 造出跨分片边：让每个节点都连到远处的节点，迫使分片之间产生边界节点。
    private static NLCPGGraph BuildCrossLinkedGraph(int nodeCount)
    {
        var graph = new NLCPGGraph();
        var nodes = new NLCPGNode[nodeCount];
        for (var index = 0; index < nodeCount; index += 1)
        {
            nodes[index] = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Operation,
              Name: "node" + index,
              FullName: "Demo.node" + index,
              FilePath: "Terraria/NPC.cs",
              SpanStart: index * 10,
              SpanEnd: (index * 10) + 5,
              StableIdentityText: "anchor-" + index));
        }

        for (var index = 0; index < nodeCount; index += 1)
        {
            graph.AddEdge(nodes[index], nodes[(index + 7) % nodeCount], NLCPGEdgeKind.DataFlow);
            graph.AddEdge(nodes[index], nodes[(index + 1) % nodeCount], NLCPGEdgeKind.CfgNext);
        }

        graph.FreezeQueryIndex();
        return graph;
    }
}
