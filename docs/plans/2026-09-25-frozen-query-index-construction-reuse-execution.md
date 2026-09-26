# P03：冻结查询索引计数、偏移与排列复用执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在 `NLCPGGraphIndex.Create` 中复用已经得到的直方图、前缀偏移和稳定排列，减少全边重复扫描与临时数组，同时逐字节保持边枚举和快照版本不变。

**Architecture:** 只改冻结索引内部的计数/排列载体。把“直方图 + 前缀 offsets + scatter”封装为一次计数结果，scatter 使用独立 cursor，避免把已完成的只读 offsets 当作写游标。让双键稳定排序返回可复用的 kind-only 中间排列；不更换 counting sort，不并行 `Create`，不改 `CreateSnapshotVersion` 字节序。

**Tech Stack:** C#/.NET 10、`NLCPGGraphIndex`、`CanonicalEdgeStore`、现有 FrozenEdge/Storage ContractTests。

日期：2026-09-25。范围是用户提出的第三优先项；本轮只写文档。执行切片 `frozen-query-index-construction-reuse` 尚未加入 [feature_list.json](../../Context/feature_list.json)；已有 `frozen-node-storage-compaction` 是不同的节点载体议题，不能作为本项完成证据。实施前先登记本项状态和完成条件。

## 1. 当前重复工作

目标文件：[NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs)。当前 `Create` 的相关调用约在 436–473：

- outgoing：`BuildKeyOffsets(sourceOrdinals, ...)`。
- incoming：`BuildOrdinals(orderedTargetOrdinals, ...)`，再对 `targetOrdinals` 调 `BuildKeyOffsets`。
- incomingByKind：`BuildOrdinalsByTwoKeys(orderedTargetOrdinals, orderedKindKeys, ...)`，复用 incoming offsets。
- edgesByKind：`BuildOrdinals(orderedKindKeys, ...)`，再对 `kindKeys` 调 `BuildKeyOffsets`。

`BuildOrdinals`（约 757）自己创建 `positions`、计数、前缀和 scatter；`BuildKeyOffsets`（约 800）再次创建数组、计数、前缀。双键排序的第一趟把序数按 kind 排好，第二趟把它按 target/kind 排好，但当前丢弃第一趟结果。

当前已经存在且不能重复认领的收益：CanonicalEdgeStore 复用 metadata value class、SnapshotVersion 的标签实例记忆化、incoming/incomingByKind 共享 offsets、边 SoA 存储。

## 2. 方案与不采用项

| 方案 | 代价 | 决定 |
| --- | --- | --- |
| `BuildOrdinalsWithOffsets`：一次直方图，独立 cursor scatter | 小；保持稳定性 | 采用 |
| 双键 helper 返回最终排列和第一趟 kind 排列 | 小到中；调用方保存额外 int[] | 采用，先量峰值 |
| 用 key 数组置换证明 histogram 相同，复用 offsets | 小；须写契约测试 | 采用 |
| 改用 `Array.Sort`/radix 新实现 | 中到高且已有 counting sort | 不采用 |
| 并行 `Create` 或重做 SnapshotHasher | 高，破坏顺序/指纹风险 | 不采用 |

关键成本判断：返回双键第一趟排列会暂时同时存两个 `int[edgeCount]`；若它的节省小于该数组峰值，保留原双键 helper，只执行 offsets 复用。

## 3. 语义不变量

1. 所有 counting pass 仍稳定；相同 key 的相对输入顺序不变。
2. `orderedTargetOrdinals` 与 `targetOrdinals` 是同一 multiset 的置换，kind 数组同理，因此 histogram/offsets 相同。实现应由同一数组一次计算，不依赖运行时“碰巧相同”。
3. `incomingOffsets` 仍由 target 直方图生成，并由 incoming 与 incomingByKind 共享同一数组实例。
4. `edgesByKindOrdinals` 必须是按 kind 升序、桶内保持 canonical 相对顺序的排列；不能把最终 target/kind 排列误当成 kind-only 排列。
5. `InsertionOrder`、`CanonicalEdgeStore` 的 permutation、`GraphSnapshotVersion` 字节写入顺序均不变。
6. positions 是 scatter 的可变 cursor；对外保存的 offsets 在构造完成后只读。禁止为省一次数组而就地递增 offsets。

## 4. 执行步骤

### T1：冻结索引基线与内存账本

文件：新增 `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexConstructionReuseTests.cs`，复用 `FrozenEdgeProjectionEquivalenceTests` 和 `NLCPGGraphIndexStorageContractTests`。

- [ ] 保存小图及代表性元数据图的 canonical/insertion/outgoing/incoming/incomingByKind/edgesByKind 顺序。
- [ ] 通过反射或内部诊断记录 edge count、各 ordinal/offset 数组长度、构建时同时存活数组数；不把 `Marshal.SizeOf` 当作托管数组峰值。
- [ ] 记录 `GraphSnapshotVersion`，至少两个不同边顺序/元数据输入证明指纹仍能区分。
- [ ] 用 counting sort 的重复 key、空图、单边、最大 enum kind 边界锁住 offsets。

### T2：合并直方图与 offsets

在 `NLCPGGraphIndex.cs` 增加私有结果载体，例如 `OrdinalBuildResult(int[] Ordinals, int[] Offsets)`：

~~~csharp
// 统计 offsets，再复制一份 cursor 用于 scatter；Offsets 本身保持只读。
var counts = new int[keyWidth + 1];
foreach (var key in keys) counts[key + 1] += 1;
PrefixSum(counts);
var offsets = counts.ToArray();
var cursor = counts;
var ordinals = new int[keys.Length];
for (var input = 0; input < keys.Length; input += 1)
{
    var key = keys[input];
    ordinals[cursor[key]++] = input;
}
return new OrdinalBuildResult(ordinals, offsets);
~~~

实际实现按仓库风格写完整 braces，不照搬伪代码。outgoing、incoming、edgesByKind 分别接入，先不改双键 helper。确认 `targetOrdinals` 作为 offsets 输入和 `orderedTargetOrdinals` 作为 ordinal 输入的 multiset 前置条件由测试覆盖。

### T3：复用双键 kind 中间排列

将 `BuildOrdinalsByTwoKeys` 改为内部结果同时返回：

- `PrimaryThenSecondary`：现有 incomingByKind 使用的 target → kind 稳定排列；
- `SecondaryOnly`：第一趟 kind 稳定排列，供 edgesByKind 直接使用。

调用方不得重新执行 `BuildOrdinals(orderedKindKeys, ...)`。edgesByKind offsets 使用 T2 返回的 kind offsets，并确认它与 `kindKeys` 的 histogram 相同。所有数组生命周期写入账本；如果保存第一趟结果造成峰值上升超过共同门槛，回退 T3，保留 T2。

### T4：图语义和冻结构建回归

- [x] `FrozenEdgeProjectionEquivalenceTests` 全部通过，字段级比较 metadata、ContextId、CallSiteContext。
- [x] `NLCPGGraphIndexStorageContractTests` 继续断言 ordinal buckets，不接受回退到完整边数组。
- [x] 查询每个 node/kind 的 incoming/outgoing 及 edgesByKind，比较原始顺序，不排序后比较。
- [x] BuildFromSource 两次、DOP=1/2、含空 metadata 与含 metadata 图，指纹逐字节相同。
- [x] 确认 CanonicalEdgeStore 的 metadata value class 复用没有被重新引入。

## 5. 测量与采纳门槛

先用同一 Release 二进制和固定输入交替运行基线/候选各三次；构建时间另计。若无法保留双实现，使用两套 DLL 并把环境限制写进报告。至少记录 Create 分段墙钟、`GC.GetAllocatedBytesForCurrentThread`（只作串行辅助）、进程峰值/数组账本和快照指纹。

T2/T3 只有在目标构建阶段中位下降至少 10% 或构建分配下降至少 10%，且总 Create 中位无超过 5% 回退时才采用。小图抖动不构成收益；用真实边规模的冻结夹具确认。快照等价是硬门槛，失败即回退。

命令：

~~~powershell
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 build ./src/NLCPG/NLCPG.csproj -c Release -p:UseSharedCompilation=false
pwsh -File ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~FrozenEdgeProjection|FullyQualifiedName~GraphIndexStorage|FullyQualifiedName~GraphIndexConstructionReuse"
~~~

不做全量真实 NPC 冻结，除非 T2/T3 在可控的大边输入先达到门槛；不把历史 FreezeQueryIndex 墙钟直接归因给本补丁。

## 6. 实施记录（2026-09-25）

### 6.1 实现要点（对齐 §2、§3）

改动只落在 [NLCPGGraphIndex.cs](../../src/NLCPG/Model/NLCPGGraphIndex.cs)，新增契约测试
`tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexConstructionReuseTests.cs`（7 个 `[Fact]`）。

- 唯一直方图入口 `BuildCountsCursor`：在 `CountingScratch` 上计数 + 前缀和，返回 scatter 可用的 cursor。
- `CopyOffsets` 把 cursor 的**活动区间**另存为常驻只读 offsets。复制点必须在 scatter 之前。
- `BuildOffsets` 只取桶边界（outgoing 方向）；`BuildOrdinalsWithOffsets` 由同一次直方图同时给出
  offsets 与稳定排列（incoming 方向）。二者共用同一份计数，不再对同一 multiset 统计两次。
- `BuildOrdinalsByTwoKeys` 返回 `TwoKeyOrdinalBuildResult(PrimaryThenSecondary, SecondaryOnly, SecondaryOffsets)`：
  第一趟 `identity --secondary--> buffer` 即 kind-only 排列，第二趟写回 `current`。仅两个 `int[n]` 缓冲，
  没有第三块数组；`edgesByKind` 直接复用 `SecondaryOnly`，不再对 `orderedKindKeys` 重新分桶。
- 调用方接入：outgoing 用 `BuildOffsets`；incoming 用 `BuildOrdinalsWithOffsets` 并把同一 `Offsets`
  实例交给 incoming 与 incomingByKind；`edgesByKindOrdinals/Offsets` 取自双键结果。
- 删除已无调用者的 `BuildOrdinals(int[], int)` 与 `BuildKeyOffsets(int[], int)`。
- 不变量 6（offsets 不得别名 cursor）由类型/数据流保证：`BuildCountsCursor` 返回的数组只被 scatter 消耗，
  对外 offsets 一律经 `CopyOffsets` 独占分配。旧 `CountingSortPass` 保留 5 参重载并转发 `offsetsOut: null`。

### 6.2 定向测试证据

`Build/P03/p03-t4.trx`：15/15 通过，0 失败，覆盖计划的 T4 过滤器
（`FrozenEdgeProjection` + `GraphIndexStorage` + `GraphIndexConstructionReuse`）。

~~~text
已通过! - 失败: 0，通过: 15，已跳过: 0，总计: 15
Total: 15  Passed: 15  Failed: 0
~~~

7 个新测试分别是：`EdgesByKind_KeepsKindBucketsInCanonicalRelativeOrder`、
`IncomingAndIncomingByKind_ShareBucketBoundariesForEveryNode`、
`ResidentOrdinalAndOffsetArrays_HaveExactExpectedLengths`、
`SnapshotVersion_IsDeterministicAndStillDiscriminatesDifferentEdges`、
`CountingSortBoundaries_EmptySingleEdgeAndMaximumKind_StayInRange`、
`DuplicateEndpointAndKind_OrdersByMetadataRankNotInsertionOrder`、
`PermutedInsertionOrder_YieldsSameCanonicalOrderAndSnapshotVersion`。
其中 incoming/incomingByKind 共享 offsets 用 `Assert.Same` 断言同一实例；节点级桶边界逐节点比对；
`ResidentOrdinalAndOffsetArrays_*` 断言 offset 长度为 `nodeCount+1` / `kindWidth+1` 且 `Outgoing._ordinals` 为 null。

### 6.3 区分力（变异检查）

把 `edgesByKindOrdinals` 故意改成 `twoKeyBuild.PrimaryThenSecondary`（即把 target→kind 最终排列
误当成 kind-only 排列），在隔离副本上重跑同一测试文件：

~~~text
失败 RoslynPrototype.Tests.NLCPGGraphIndexConstructionReuseTests.EdgesByKind_KeepsKindBucketsInCanonicalRelativeOrder
  Assert.Equal() Failure: Collections differ
失败! - 失败: 1，通过: 6，已跳过: 0，总计: 7
~~~

只有针对不变量 4 的那一个测试失败，其余 6 个仍通过 ⇒ 该断言非空洞，确实锚定 §3 第 4 条。

### 6.4 端到端语义等价（快照硬门槛）

同一份只走反射的探针二进制 `p03-probe`，分别加载基线 DLL 与候选 DLL（唯一变量是 NLCPG.dll，
探针 DLL 哈希两侧相同 `4C4F00A0…E6A8F17`），夹具 N=215,315、E=2,590,475、metaPct=60。
9 次交替运行的 oracle **逐字节相同**：

~~~text
snapshot       = 94B55C051DBD29D38DF8727119D5ACB501F0895E12FF6DFC78B162616ECB0A72
canonical_hash = 7195100F935D8629
by_kind_hash   = 2B8AE38B744AAE7D   (by_kind_total = 2,590,475)
adjacency_hash = 94991F3F657EA6B8   (逐节点 outgoing/incoming/incomingByKind 全枚举)
nodes_by_kind_hash = DA6E27AEA78A8080  nodes_by_file_path_hash = 9EEF02E2132A18C7
failures_after_checks = 0
~~~

另在 metaPct=0 与 metaPct=60 两种夹具上复核，快照与 canonical 哈希同样两侧一致。
账本两侧一致：`by_kind_offsets_len=37`、`outgoing_offsets_len=215316`、
`incoming_offsets_len=215316`、`incoming_offsets_shared_with_by_kind=True`、
`outgoing_ordinals_null=True`、`by_kind_offsets_total=2590475`。
保留数组总量与原实现相同（原先也常驻 `incomingOrdinals` + `incomingByKindOrdinals` + `edgesByKindOrdinals`
三份 `int[n]`，现在同样三份，只是其中两份就是双键的两块缓冲），故**无净常驻增长**。

### 6.5 门槛判定（§5）

基线用同一台机器、同一夹具、交替运行取得。原始日志：`Build/P03/measure/ab-stage.log`
（真实双 DLL 交替）与 `Build/P03/measure/ab-stage-instrumented.log`（在隔离副本上于目标分桶阶段
首尾各插一次计数器，用于把"目标构建阶段"单独量出来；两侧插桩位置逐行对应）。

| 指标 | 基线 | 候选 | 变化 |
| --- | --- | --- | --- |
| **目标分桶阶段分配** | 43,170,672 B | 32,808,744 B | **−24.0%** |
| 目标分桶阶段墙钟（最小值口径） | 170.816 ms | 117.157 ms | −31.4% |
| 总 Create 分配（全 `Create`） | 469,270,376 B | 458,908,448 B | −2.2% |
| 总 Create 墙钟（中位数口径） | 6,667.391 ms | 6,356.838 ms | −4.7% |
| 总 Create 墙钟（最小值口径） | 3,607.363 ms | 3,618.941 ms | +0.3% |

- **目标阶段分配 −24.0%**：远超"构建分配下降至少 10%"，且是确定性量（基线 12/12 次均为
  43,170,672，候选 12/12 次均为 32,808,744，零抖动）。差值 10,361,928 B 与
  "消掉一份 `int[E]`"的预测 4×2,590,475+24 = 10,361,924 B 吻合到 8 字节对齐误差内。
- **总 Create 无 >5% 回退**：中位数口径为改善；最小值口径 +0.3%，远在 5% 以内。
- 因此 T2+T3 **满足采纳门槛**，无需回退。

基线/候选哈希：

~~~text
src/NLCPG/Model/NLCPGGraphIndex.cs   A328B2DDC5E5E98C1E9C82F7A3D6736207F1EB55D4CDAEC6EC9739E67AFCCEF3 (1277 行)
tests/.../NLCPGGraphIndexConstructionReuseTests.cs  47AAD2EFA570241FEB7434A19E9C0D4BC9E47508A19457770C300B8EB9C63C18 (379 行)
基线 NLCPG.dll 0F06130B9DF05C8C037007F3D45B94F308566DF5CE49487D77764AACB46F6F08
候选 NLCPG.dll C28719F9E0046F37A455E327F5ABADF7ACBCD15A6B1E3DA79C0485C7303AB9F2
~~~

> ⚠️ **身份说明（本节全部数字对应上面的产品源码快照，不是当前工作树文件）**：
> 上面 `A328B2DD…` 是**量测与全部证据所对应的**源码身份，精确副本保留在
> `Build/P03/src-snapshots/candidate-NLCPGGraphIndex.cs`（1277 行）。
> 该文件随后被**无关的 M4**（冻结节点单份持有）改过一处：`Create` 改为
> `nodes as NLCPGNode[] ?? nodes.ToArray()` 以接管调用方数组，并改写所有权注释，
> 故**当前工作树文件已不再等于** `A328B2DD…`。与量测快照的差异**只有那 13 行 M4 改动**，
> P03 的全部标记（`BuildCountsCursor`/`CopyOffsets`/`BuildOffsets`/`BuildOrdinalsWithOffsets`/
> `BuildOrdinalsByTwoKeys`/`OrdinalBuildResult`/`TwoKeyOrdinalBuildResult`/`CountingScratch`）**仍在**。
> **复验**：在 M4 改动之后（测试程序集内 `NLCPG.dll` 构建于 15:21:38，晚于 M4 的 08:28 编辑），
> T4 过滤器仍 **15 通过 / 0 失败**（TRX `Build/P03/p03-after-m4.trx`）。
> 上述测试文件哈希 `47AAD2EF…` 经复查**未变**，仍与工作树一致。

### 6.6 环境诚实边界

- 量测期间机器上有 40 个左右 `dotnet`/testhost 进程并发（`Win32_Processor.LoadPercentage = 100`），
  且共享工作树被并行工作流反复改成不可编译态。因此**墙钟只有最小值/中位数口径可参考，抖动极大**，
  结论一律以确定性分配量为准；未做全量真实 NPC 冻结，历史 `FreezeQueryIndex` 墙钟未归因给本补丁。
- 上表"目标分桶阶段"的分配量来自隔离副本插桩（两侧插桩位置逐行对应、同夹具同二进制其余部分），
  不是生产构建产物；生产产物只用于 6.4 的等价性与总 Create 分配。
- 隔离副本的基线已用 oracle 交叉核对：其 `snapshot` 与 `canonical_hash` 与真实基线 DLL
  逐字节一致，故该基线是忠实的 P03 前状态。
- 期间 `check-harness-consistency.ps1` 首次失败仅因其 `finally` 删除系统 TEMP 子目录被拒
  （`Remove-Item ... 拒绝访问`），与本次改动无关；把 TEMP 指向工作区内目录后输出
  `[check-harness-consistency] OK`。
- `Miscellaneous/tmp/p03-*` 下的探针/变体工程与 `Build/P03/` 均为本轮验证脚手架（`Build/` 已被
  `.gitignore` 忽略），不作为产品交付物。

### 6.7 整仓 Contract 套件的既有抖动（与本切片无关）

整仓 Contract 套件（700 条）在本轮出现不稳定的失败集合：连续三次运行分别是 14、11、13 条失败，
而 **P03 范围内的测试每一次都是 0 失败**（`GraphIndexConstructionReuse` / `FrozenEdgeProjection` /
`GraphIndexStorage`）。已用同二进制对照定位原因，确认**不是本切片引入**：

| 对照 | 结果 |
| --- | --- |
| `--no-build` 下单独跑 `FullyQualifiedName~BuildFromSource` | **167/167 通过** |
| 单独跑报错涉及的四个类（`DataFlowDiagnosticsTests`/`NLCPGNodeIdContractTests`/`NLCPGPartitionedBuilderTests`/`DocumentShardingEquivalenceTests`） | **75/75 通过** |
| 同一二进制、同一 TEMP、连续两次整仓运行 | 失败数 11 → 13，且**失败名单不同** |
| 失败消息 | 多为 `Expected: "D:\\ProjectItem\\...\\"` vs `Actual: "C:\\WINDOWS\\TEMP\\..."` 的**路径前缀**差异，节点/边内容本身一致 |
| 历史 TRX（**本切片实现之前**） | 同名的 `BuildFromSource_*` 测试在 2026-09-22 ~ 09-24 已在失败（最多一次命中 5 条） |

故整仓失败归因于环境 TEMP 泄漏与跨测试相互干扰；**不**把整仓绿灯当作本切片判据，
也**不**为了让套件变绿去修改这些不属于 P03 的文件。已知既有失败
`NLISSN.Tests.Architecture.LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
（工程引用不匹配）保持原样，未作豁免或"修复"。
