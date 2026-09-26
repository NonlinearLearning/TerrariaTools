# NLCPG 字符串表与序号索引小批量测量

> 状态：已补充固定源码的 NLCPG 端到端采样；不是 BenchmarkDotNet 结果，也不是
> retained-heap 证明。
>
> 测量日期：2026-09-21。

## 目的

验证“把高密度节点从 class 改为含字符串引用的 struct，再改为字符串表 ID”在本仓库
当前实现中的可观测边界。测量分成两部分：固定源码的 NLCPG 构图、查询和
export/restore；以及隔离 carrier 的构造和顺序扫描对照。仓库当前没有
BenchmarkDotNet 包，因此没有为了单次实验引入新的基准依赖。

carrier 对照的每个样本构造并扫描 50,000 个元素：

- `class`：每个元素是一个 class 实例，含五个字符串引用和一个 `int` span。
- `struct-string`：每个元素是一个 struct，字段与 class carrier 相同，字符串引用共享
  同一组输入字符串。
- `struct-id`：每个元素是六个 scalar（五个 `uint` ID 和一个 `int` span），扫描时通过
  一张六项字符串表解析两个 ID。

三组使用相同的五个输入文本：`Add`、`Demo.Sample.Add`、
`Demo.Sample.Add:int(int,int)`、`Demo.Sample`、`Fixture.cs`。列表预先以准确容量创建，
避免把扩容差异混入对照。NLCPG 端到端样本使用同一份固定源码，DOP=1，构出
`1424` 个节点和 `4481` 条边。

## 环境与命令

- OS：Windows，x64 进程环境未单独强制覆盖。
- Runtime：`.NET 10.0.11`。
- SDK：`10.0.400`。
- 执行命令：

  ```powershell
  dotnet test .\tests\NLISSN.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj `
    --no-restore -p:UseSharedCompilation=false `
    --filter "FullyQualifiedName~NLCPGNodeStoragePerformanceTests" `
    --logger "console;verbosity=detailed"
  ```

- 配置：Debug `dotnet test`、DOP=1；每项先预热 2 次，再测量 5 次；每个样本开始前
  执行完整 GC。该命令不是 Release 配置，也不是 BenchmarkDotNet。
- graph 端到端测量依次记录构图、查询/文本投影、export/restore，并记录分配量、
  GC 次数、GC heap size 和测量阶段采样到的峰值 working set 增量。
- carrier 测量每组构造并扫描 50,000 个元素；耗时是构造和顺序扫描合计，分配量来自
  `GC.GetTotalAllocatedBytes(true)` 的前后差。

## 原始结果

### NLCPG 端到端

固定源码、`1424` 节点、`4481` 边、DOP=1 的 5 个 measurement 原始样本如下：

| metric | raw samples | median |
| --- | --- | ---: |
| build | 186073, 165559, 176132, 172587, 181204 us | 176132 us |
| query/text projection | 5117, 2473, 2331, 2746, 2372 us | 2473 us |
| export/restore | 58283, 56976, 63528, 58866, 53776 us | 58283 us |
| allocated bytes | 45277624, 45162144, 45166816, 45167288, 45187744 B | 45167288 B |
| post-sample GC heap size | 30843360, 32175696, 30987752, 31540424, 31165936 B | 31165936 B |
| peak working-set delta | 117981184, 116498432, 116285440, 115634176, 116514816 B | 116498432 B |
| Gen0 collections | 4, 4, 4, 4, 4 | 4 |
| Gen1 collections | 3, 2, 3, 2, 3 | 3 |
| Gen2 collections | 2, 2, 2, 2, 2 | 2 |

固定结果还包括：serialized bytes=`293280`、local nodes=`4`、path query nodes=`1421`。
每次恢复后的节点数、边数、snapshot version 和文本投影 checksum 均与当次构图结果
一致；本轮进程内 checksum 为 `160180154`。该 checksum 使用运行时字符串 hash，
只用于同一次测试进程中的一致性断言，不是跨进程稳定标识。

这里没有同一源码、同一 pipeline 的旧 class 节点基线，因此这些 graph 数值不能换算
为“class 到 struct 的下降百分比”。`allocated bytes` 是测试过程的总分配增量；
`post-sample GC heap size` 是采样时的 runtime heap size；两者都不是对象 retained
size。`peak working-set delta` 是从每次样本开始时的 working set 基线到构图、查询和
export/restore 检查点中的最大差值。

### Carrier 对照

| carrier | raw time samples (us) | median time | raw allocated samples (B) | median allocated |
| --- | --- | ---: | --- | ---: |
| `class` | 1054, 1052, 1364, 1063, 1229 | 1063 | 3632360, 3652592, 3633208, 3625912, 3659960 | 3633208 |
| `struct-string` | 1108, 1695, 1124, 1096, 1114 | 1114 | 2465856, 2406400, 2406400, 2406400, 2406400 | 2406400 |
| `struct-id` | 1226, 1341, 1163, 1142, 1160 | 1163 | 1212824, 1206464, 1206464, 1206464, 1206464 | 1206464 |

本轮 carrier 样本相对 `class` 中位数：`struct-string` 分配量低约 `33.8%`，耗时高
约 `4.8%`；`struct-id` 分配量低约 `66.8%`，耗时高约 `9.4%`。三组 carrier 的
5 个样本均未触发额外 Gen0/Gen1/Gen2 collection。`struct-id` 的 checksum 与另外两组
不同，因为它扫描的是 ID 与解析表长度，而不是直接对全部字符串实例取哈希；测试仍
验证了三组元素数量一致，但 checksum 不是跨 carrier 的语义等价证明。

## 解读边界

这组数据只证明一个小型、固定字符串输入的 carrier 模型，以及当前 NLCPG graph
实现，在当前 Debug 进程中的分配和时间行为。它没有测量：

- class 版 NLCPG 完整 Roslyn 构图基线，因此不能声称 graph 端到端内存下降；
- Release 发布配置、不同架构、不同 GC 模式或不同 SDK/runtime；
- retained heap 的对象类型归因、对象存活图或 GC pause；
- 字符串不重复、字符串表增长、哈希冲突、跨 graph ID 误用和装箱边界。

因此本结果不能表述为“内存下降一半”或“访问速度必然提升”。它支持当前设计的
取舍：含字符串引用的 struct 只减少外层 carrier 分配，不消除字符串对象；graph-owned
字符串表 ID 才同时减少每个元素的引用槽位和重复文本载荷，但查询必须经过 resolver，
并可能增加解析、索引或缓存成本。当前 fresh sample 中，ID carrier 的分配优势仍然
存在，但访问速度优势未被证实；NLCPG 端到端速度和峰值内存仍需要可切换的 class
基线与更稳定的 Release 测量。

## 后续验证

需要最终端到端结论时，应在同一提交点建立可切换的 class carrier 或 class node 基线，
与当前 `NLCPGNode`/string-table/ordinal-index 实现使用相同源码、DOP、容量、预热和
样本数，并在 Release 下至少记录构图、冻结、kind/path 查询、文本查询、local view、
export/restore、GC、working set 和对象级 retained heap。若引入 BenchmarkDotNet，应
先把它作为仓库认可的性能测试依赖，再使用 `MemoryDiagnoser`；本页的 xUnit 小批量
测量不能替代那个实验。
