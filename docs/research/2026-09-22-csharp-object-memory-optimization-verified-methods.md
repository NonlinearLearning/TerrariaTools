# C# 对象内存占用优化方法：针对最新 GC 报告的代码级验证与外部实现研究

> 日期：2026-09-22。状态：**研究 + 本地实测复核**；未修改生产代码，未运行新的端到端性能基准。
>
> 输入证据：[Version4 DOP12 全并行诊断记录](../benchmarks/nlissn-version4-dop12-diagnostics.md) 与
> [`version4-dop12-type-share-0016.csv`](../benchmarks/data/version4-dop12-type-share-0016.csv)。
>
> 旧研究：[C# class 内存性能优化方案](2026-09-17-csharp-class-memory-optimization-solutions.md)、
> [class 内存优化采用审查](2026-09-17-nl-class-memory-optimization-adoption-audit.md)、
> [字符串表与序号索引小批量测量](../benchmarks/2026-09-20-nlcpg-string-table-index-compaction.md)。

## 结论

诊断报告已经把问题定位到**数组**而不是 class 对象头：峰值样本中数组占 **3,588.7 MB / 92.95%**，
非数组 6,448 个类型合计只占 **272.3 MB / 7.05%**。因此“把 class 改成 struct”在本仓库
**不是**第一顺位手段——本轮独立复核再次证实了这一点（见“实测复核”）。

按“改动小、证据强、风险低”排序，真正对症的方法只有三类：

1. **消除 O(N²) 巨型数组**（占堆 60.20%）：`DataFlowPass` 的 `inSets`/`outSets` 位集改成
   分块/有界表示，并给 `MaxFlowNodesPerMethod` 设一个真实上限。本轮已**字节级反解**
   出触发分配的方法有 **N = 35,303 个流节点**，单个数组 148.7 MiB。
2. **消除重复的引用数组副本**（Dictionary `Entry[]` 22.3% + CSR 边数组 + `PendingEdge` 缓冲）：
   减容量、去重复索引、把二级索引从 `NLCPGEdge`(实测 72 B) 改为 `int` 序号。
3. **减少短命分配的流量**（累计 91.7 GiB，存活仅 3.77 GiB）：这里 `ArrayPool`/
   `CollectionsMarshal`/有界预算才有效，而不是池化领域模型。

**一条对本仓库重要的否定性结论**：报告把 `dotnet-trace` 的独占热点归到
`RuntimeMethodInfo.GetParameters()` 并指向“per-node 反射式元数据访问”。本轮全仓搜索
**在 `src/` 下找不到任何 `GetParameters()` 调用点**（见“归因修正”）。该热点来自
**反射式 `System.Text.Json` 序列化**（本仓库确实大量使用，且无 source generator），
不是 CPG 构图的热路径。把它当作构图优化目标会走错方向。

**第二条否定性结论**：`NLCPGDataFlowOptions` 的预算字段**没有暴露到 YAML 配置**，
`NLCPGDataFlowOptions.Unbounded` 是唯一默认，且 `CreateDefault()` 传的是 `null`
（= 无上限）。所以“给 MaxFlowNodesPerMethod 设上限”目前只能改代码或加配置映射。

## 判定口径

- **实测复核**：本轮从仓库内真实 CSV 重新聚合、并新增一个独立 JIT/运行时测量程序
  （`Unsafe.SizeOf` + `GC.GetTotalAllocatedBytes`）得出的数字。可复现，命令见文末。
- **代码事实**：从受版本控制源码读到的当前实现。
- **外部一手来源**：Microsoft Learn 官方文档与 dotnet/roslyn 源码，已 fetch 原文。
- **工程推导**：由上述证据推出的方案，**不是**已测出的收益。凡未测者一律标注。

## 一、实测复核：把报告的结论重新算一遍

### 1.1 offender 排名复核（来自仓库内 CSV，逐行重算）

对 `version4-dop12-type-share-0016.csv`（7,289 行）独立重算，`TotalBytes` 合计
**4,048,479,859 B = 3,860.9 MiB**，与报告完全一致。数组/非数组拆分：

| 类别 | 类型数 | 实例数 | MB | 占堆 |
| --- | ---: | ---: | ---: | ---: |
| 数组类型（`Type` 以 `[]` 结尾） | 841 | 528,734 | **3,588.7** | **92.95%** |
| 非数组类型 | 6,448 | 4,225,781 | 272.3 | 7.05% |

前 5 名（全部是巨型数组）：

| # | 类型 | 实例数 | MB | 占比 |
| ---: | --- | ---: | ---: | ---: |
| 1 | `System.UInt64[]` | 21 | 2,324.3 | 60.20% |
| 2 | `Entry<…PendingEdgeKey,System.Int32>[]` | 1 | 375.3 | 9.72% |
| 3 | `BufferedPendingEdge[]` | 2 | 240.0 | 6.22% |
| 4 | `Entry<StableNodeAnchor,NLCPGNode>[]` | 1 | 186.3 | 4.82% |
| 5 | `Entry<IOperation,NLCPGNode>[]` | 66 | 102.2 | 2.65% |

Dictionary 内部 `Entry<…>[]` 数组全体（145 个类型）合计 **861.2 MB / 22.31%**。
**结论**：优化目标必须是“数组的个数、元素宽度、元素是否含引用”，不是“类型的 class/struct 关键字”。

### 1.2 新增实测：托管结构体与数组的真实尺寸

用一个引用 `Build/src/Debug/net10.0/NLCPG.dll` 的临时程序（不改仓库、不新增依赖）实测：

| 类型 | `Unsafe.SizeOf` (B) | 说明 |
| --- | ---: | --- |
| `NodeId` | 4 | 已经是 `readonly record struct(uint)` |
| `StableNodeAnchor` | 28 | 已是 struct，但字段多 |
| `NLCPGEdge` | **72** | 已是 `readonly record struct`，仍很大 |
| `NLCPGNode` | **104** | **已经是 `readonly record struct`** |
| `Entry<StableNodeAnchor,NLCPGNode>` | 140 | = 8 头 + 8 哈希/链接 + 28 + 104（含对齐） |
| `Entry<NLCPGNode,NLCPGNode[]>` | 120 | |
| `Entry<PendingEdgeKey,int>`（近似） | 96 | |

数组实测分配（`GC.GetTotalAllocatedBytes(true)` 差值，n=10,000）：

| 元素类型 | 数组总量 | 每元素 |
| --- | ---: | ---: |
| `NodeId[]` | 40,360 B | 4.03 B |
| `ulong[]` | 80,360 B | 8.03 B |
| `StableNodeAnchor[]` | 280,360 B | 28.03 B |
| `NLCPGEdge[]` | 720,360 B | 72.03 B |
| `NLCPGNode[]` | 1,040,360 B | 104.03 B |
| `Entry<StableNodeAnchor,NLCPGNode>[]` | 1,400,360 B | 140.03 B |

**关键推论**：`NLCPGNode` 与 `NLCPGEdge` **已经是紧凑值类型**了，所以“从 class 改 struct”
这个建议在本仓库的核心模型上**已经没有可执行空间**。当前浪费来自
（a）这些 104 B / 72 B 的值被**整份复制到多个索引数组**，以及
（b）字典为每个条目付出 **140 B/条**。

### 1.3 新增实测：位集 O(N²) 的规模被字节级反解出来

`DataFlowPass.cs:618-619`：

```csharp
var inSets  = new ulong[checked(flowNodes.Length * wordsPerSet)];
var outSets = new ulong[checked(flowNodes.Length * wordsPerSet)];
```

配合 `wordsPerSet = ceil(N/64)`，单个数组 = `N × ceil(N/64) × 8` 字节。实测数据里
`System.UInt64[]` 的 `TypeSize`（`dotnet-gcdump` 报告的单实例尺寸）是 **155,898,072 B**。
反解：

```text
N=35303: 35303 × ceil(35303/64) × 8 + 24   = 155,898,048 + 24 = 155,898,072  ✅ 完全吻合
N=35302:                                     155,893,656                    ✗
N=35304:                                     155,902,488                    ✗
```

即 **单个方法有 N = 35,303 个流节点**，`inSets` 一个数组就 **148.7 MiB**，
`inSets`+`outSets` 合计 **297.4 MiB**，且两者**必然在 LOH**。样本里 8 个 >100 MB 的
实例（平均 267 MB）对应的 N 约 **47,000**。

LOH 阈值交叉点（实测口径 85,000 B）：

| 流节点 N | 单个数组 | 去向 |
| ---: | ---: | --- |
| 817 | 84,968 B | SOH |
| **818** | **85,072 B** | **首个进入 LOH 的 N** |
| 10,000 | 12.56 MB | LOH |
| 30,000 | 112.6 MB | LOH |
| 35,303 | 155.9 MB | LOH |

而 `MaxFlowNodesPerMethod` 默认 `int.MaxValue`，运行配置没有覆盖（见 1.5）。
这条链路是**有代码、有规模、有字节级吻合**的三重证据，是本轮最高置信度的优化点。

### 1.4 新增实测：Dictionary 条目数反解

用 `Entry[]` 数组字节数 ÷ 每条约目实测尺寸：

| 字典 | `Entry[]` 字节 | 每条第目 | 条目数 |
| --- | ---: | ---: | ---: |
| `Dictionary<StableNodeAnchor,NLCPGNode>` (`_mutableNodesByAnchor`) | 195,336,844 | 140 B | **≈ 1,395,263** |
| `Dictionary<PendingEdgeKey,int>` (`_pendingEdges._ordinals`) | 393,481,888 | 96 B | **≈ 4,098,769** |
| `Dictionary<IOperation,NLCPGNode>`（66 个实例之一） | 38,933,904 | 140 B | ≈ 278,099 |

**含义**：`_ordinals` 这个**纯去重字典**为 410 万个边键付出 **375.3 MiB**（占堆 9.72%），
而它只用于“这条边是否已存在”。这是本仓库**单点性价比最高**的可消除开销之一。
`BufferedPendingEdge[]`（240 MB / 2 实例，即 60 MB/个 ≈ 每元素至少 96 B，约 60–65 万条边）
是同一去重结构的**并行第二份副本**。

**口径说明（重要）**：上表“条目数”是 `Entry[]` 字节 ÷ **实测近似条目尺寸**得到的，
不是从字典 `Count` 读出的，且**未通过交叉校验**，不要引用为精确值：

- `Entry<StableNodeAnchor,NLCPGNode>` 的条目尺寸是**实测** 140 B，故 139.5 万条可信度最高。
- `Entry<PendingEdgeKey,int>` 的尺寸是**推算**（2×28 键 + 4 enum + 3×8 引用 + 8 = 96 B），
  得 ≈410 万条。但把它与 `_items` 交叉核对会出现矛盾：`BufferedPendingEdge` 与
  `PendingEdgeKey` 字段相同（约 84–88 B/元素），而 `BufferedPendingEdge[]` 实例最大
  251,658,264 B ⇒ 约 **286 万**条。两者相差约 30%，说明“410 万”偏高，
  `_ordinals` 的真实条目数更可能在 **≈290 万–410 万** 区间。
- 矛盾的可能来源：`Dictionary` 的内部条目数组按素数增长，或 `BufferedPendingEdge`
  存在对齐/布局差异。**本轮未采集分配栈，故未能定论。**
- **结论不变**：`_ordinals` + `_items` 是对同一份边元数据的**两份**编码，
  合计 **615.3 MiB（375.3 + 240.0），占堆 15.94%**——这个结论只依赖 CSV 的两行字节数，
  与条目数推算无关，因此是稳的。

### 1.5 新增实测：运行时 GC 配置全是默认值

- `Build/src/Debug/net10.0/NLCPG.runtimeconfig.json` 与 `NLISSN.runtimeconfig.json` 的
  `configProperties` **只有** `System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization`。
- 全仓无 `runtimeconfig.template.json`，无 `ServerGarbageCollection`/`GarbageCollectionAdaptationMode`/
  `ConcurrentGarbageCollection`/`RetainVMGarbageCollection`/`HeapHardLimit` 等 MSBuild 属性。
- 因此生效的是：**Workstation GC + 后台 GC + DATAS 默认开启（.NET 9+）**，没有堆上限。

这有直接后果：无 `HeapHardLimit` 意味着 committed 可以涨到物理内存的 1.83 倍而不被拦，
这也解释了报告观测到的换页签名。但**设堆上限不是解法**（见 Loop 10）。

**补充**：文档把 DATAS 列在 **Server GC 设置组**内，而该组明确“**have no effect on Workstation GC**”，
所以“DATAS 默认开启”**不能直接推出**它在本进程生效。据此，本文把
“先用 `GC.GetConfigurationVariables()` 读出实际生效配置”列为任何 GC 调参实验的第一步
（见 Loop 10 的更正）。

### 1.6 实测：`CreateDefault()` 确实是无上限

```
NLCPGDataFlowOptions.Unbounded = { MaxDefinitionsPerMethod = 2147483647,
                                   MaxFlowNodesPerMethod  = 2147483647,
                                   MaxCandidateEdgesPerMethod = 2147483647,
                                   OverflowBehavior = SkipMethod }
NLCPGBuilderOptions.CreateDefault().DataFlowOptions          = <null>
NLCPGBuilderOptions.CreateDefault().EffectiveDataFlowOptions = <同上 Unbounded>
```

并且全仓搜索确认 `maxDefinitionsPerMethod` / `dataFlow` / `maxFlowNodes` **在
`Miscellaneous/schemas/*.json` 与 `nlissn.yml` 中都不存在**——预算不可从配置到达。

### 1.7 实测：生产构图路径确实没有传预算

所有生产入口都用 `NLCPGBuilderOptions.CreateDefault() with { … }`，而该 `with` 表达式
**都不包含 `DataFlowOptions`**：

| 生产入口 | 位置 | 是否设置 `DataFlowOptions` |
| --- | --- | --- |
| NLISSN CPG 构图主路径 | [ApplicationService.cs:295](../../src/NLISSN.Application/Analysis/ApplicationService.cs) | **否** |
| ProjectExport 构图 | [ProjectJsonExporter.cs:194](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs)、[:319](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs) | **否** |
| ProjectExport 项目池 | [ProjectJsonExporter.ProjectPool.cs:385](../../src/NLCPG.ProjectExport/ProjectJsonExporter.ProjectPool.cs) | **否** |

`NLCPGBuilder.cs:130` 的回退也是 `options ?? NLCPGBuilderOptions.CreateDefault()`。
即：**整条生产链路都没有上限**，`DataFlowPass` 的 O(N²) 分配无任何拦截。
这使得 P0 的“设有限默认值”成为**最小且确定**的改动点——
无论改默认值还是在上述四处显式传 `DataFlowOptions`，都能立即截断 N=35,303 这类方法的巨型分配。

## 二、十个研究循环

每轮形式：**先读关键代码 → 网络查权威做法 → 找 GitHub 实现 → 回到代码判断可行性**。

### Loop 1：O(N²) 巨型数组 → 有界/分块位集

**代码**：`DataFlowPass.cs:573-681`，`inSets`/`outSets` 是 `N × ceil(N/64)` 的扁平 `ulong[]`，
按 `nodeOrdinal * wordsPerSet + wordIndex` 寻址（`OrBitSet`/`CopyBitSet`/`BitSetEquals`/
`IsBitSetEmpty`/`EnumerateSetBits`）。

**外部权威**：Microsoft 的 LOH 文档给出方向性意见——大树示例中**保留 `class Node`，
但把左右孩子引用换成整数索引**，以减少 GC 需要追踪的引用；并明确
“我们建议分配一个大对象池并复用，而不是分配临时大对象”
（[Large object heap](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)）。
该页也给出 LOH 的 85,000 B 阈值、清除成本（16 MB 对象约 16 ms）与“LOH 只在 Gen2 回收”。

**GitHub 实现**：dotnet/roslyn 的 `BitVector`
（[BitVector.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Collections/BitVector.cs)）
用**内联单字 + 按需增长数组**表示位集：

```csharp
private Word _bits0;          // 第一个字内联在结构体内，避免小位集分配数组
private Word[] _bits;         // 超出部分才分配
private int _capacity;
```

`WordsForCapacity(capacity)` 在 `capacity <= 0` 时返回 0，所以**≤64 位的位集零数组分配**；
`EnsureCapacity` 用 `Array.Resize` 增长；`Create` 在 `requiredWords == 0` 时返回共享的
`s_emptyArray`。**关键点是容量跟随“实际用到的宽度”增长，而不是按最坏情况预留。**
`BitVector` 内部**没有任何池化**（无 `ObjectPool`/`Free()`/`ArrayPool`）。

**更强的反例（Roslyn 的 dataflow 根本不这样分配）**：Roslyn 的
`AbstractFlowPass<TLocalState, TLocalFunctionState>`
（[AbstractFlowPass.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/FlowAnalysis/AbstractFlowPass.cs)）
**不为每个流节点分配一个状态**，而是持有**一个当前状态**加一个**按需填充、池化**的标签字典：

```csharp
protected TLocalState State;              // 当前状态（唯一）
private readonly PooledDictionary<LabelSymbol, TLocalState> _labels;   // 仅在 join 点按需创建
protected virtual TLocalState LabelState(LabelSymbol label)
{
    if (_labels.TryGetValue(label, out result)) { return result; }
    result = UnreachableState();
    _labels.Add(label, result);
    return result;
}
```

最接近本仓库“reaching definitions”的 `DefiniteAssignmentPass` 也是**每个状态一个 `BitVector`**，
且容量**随发现的变量数增量增长**（`Normalize` 里 `EnsureCapacity(variableBySlot.Count)`），
初始状态是零分配的 `BitVector.Empty`。该类文档**自己承认**在病态输入下会二次方退化：

> “…it is possible (though rarely occurs in practice) that we are changing the state at a label
> that we've already analyzed… This can result in quadratic performance in unlikely but possible code…”

**对本仓库的直接含义**：Roslyn 的做法是“**按 join 点保存状态 + 容量增量增长 + 池化**”，
而本仓库是“**按方法一次性预留 N×N/64 的稠密矩阵**”。前者在病态输入下**承认退化但先兜住**，
后者**直接把内存打满**。所以 N=35,303 的 148.7 MiB 不是“Roslyn 也会遇到的正常代价”，
而是**预留策略**造成的。

**可行性（已逐行核对测试，有一处需要澄清）**：溢出**行为**已有测试覆盖——
`BuildFromSource_DataFlowNodeBudget_SkipsOverBudgetMethod`
（[NLCPGPartitionedBuilderTests.cs:669-684](../../tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs)）
设 `MaxFlowNodesPerMethod: 0` 并断言图中**没有 `DataFlow` 边**，即“跳过”路径确实生效。

**但需要澄清**：全仓测试中 **`NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded`
从未被断言过**——现有断言只有 `CandidateEdgeLimitExceeded`（
[CpgWorkBatchDataFlowTests.cs:107](../../tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs)、
[NLCPGPartitionedBuilderTests.cs:726](../../tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs)）
与 `None`。`DefinitionLimitExceeded` 同样未被断言。
即：**“跳过”行为有测试，“跳过的原因是该原因”没有测试**。
P0 落地时应**补一条断言 `FlowNodeLimitExceeded` 的测试**，否则上限触发的可观测性没有回归保护。

所以**最小改动 = 给 `MaxFlowNodesPerMethod` 一个有限默认值**，机制已有、无需新算法。

**生产级先例（已 fetch 原文）**：Roslyn 的 `MethodCompiler`
（[MethodCompiler.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/Compiler/MethodCompiler.cs)）
对“方法太大”正是用**硬上界 + 报错 + 放弃发射**处理的：

```csharp
var localVariables = builder.LocalSlotManager.LocalsInOrder();
if (localVariables.Length > 0xFFFE)
{
    diagnosticsForThisMethod.Add(ErrorCode.ERR_TooManyLocals, method.GetFirstLocation());
}
if (diagnosticsForThisMethod.HasAnyErrors())
{
    // we are done here. Since there were errors we should not emit anything.
    return null;
}
```

递归深度同理（`StackGuard.MaxUncheckedRecursionDepth = 20` + `RuntimeHelpers.EnsureSufficientExecutionStack`）。
.NET runtime 的 JIT 也用同一思路：`JitMaxLocalsToTrack = 0x400`，
**超过阈值就停止追踪局部变量**（[jitconfigvalues.h](https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/jit/jitconfigvalues.h)）。

这与本仓库已有的 `SkipMethod` 溢出策略**完全同构**，因此 P0 不只是“省内存”，
而是**向生产编译器已验证的失败模式对齐**。

**一个必须写清的边界（防止误判收益）**：**分块并不能减少总保留堆**。
把 148.7 MiB 切成若干块后，仍然是 148.7 MiB，GC 仍然按总量记账。分块的收益只有三条：
（a）避免单对象超过 LOH 的极端尺寸/`OutOfMemoryException`，
（b）降低 LOH 碎片与固定，
（c）可用“内联首块”的技巧让**小方法零分配**（Roslyn `_bits0` 的手法）。
所以**分块必须与 P0 的上限/放弃策略一起做，不能替代它**。
唯一能**真正**给出保留堆上界的是“先算规模、超限就跳过分析”，
这也正是 Roslyn `ERR_TooManyLocals` 与 JIT `JitMaxLocalsToTrack = 0x400` 的做法。

### Loop 2：位集复用 → `ArrayPool` 与容量上界

**代码**：仓库已有 `ArrayPool` 用法先例（`CpgShardStore.cs:459/492/557`、
`PartitionedOperationPass.cs:265/313`，归还时 `clearArray: true`），说明这是**仓库已接受**的模式。

**外部权威**：`ArrayPool<T>` 文档明确适用场景是“频繁创建销毁数组造成显著 GC 压力”，
并规定租借长度**可能大于**请求长度，所以必须另存逻辑长度
（[ArrayPool&lt;T&gt;](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)、
[Return 契约](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return)）。

**对本仓库的关键约束**：`ArrayPool<ulong>` **不能**解决一个 148.7 MiB 的数组——
它仍然是大对象，仍然在 LOH，仍然要清零。池化只能降低“反复分配同一尺寸”的**分配流量**，
不能降低**同时存活**的字节数。所以 Loop 2 的正确结论是：
**池化是 Loop 6（生命周期）和 Loop 5（去重复副本）的辅助手段，不能单独解决 60.2%。**

**生产级池化先例（已 fetch 原文）**：Roslyn 的 `ArrayBuilder<T>.Free()`
（[ArrayBuilder.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Dependencies/PooledObjects/ArrayBuilder.cs)）
给出**“超大实例不入池”**这一关键规则：

```csharp
public const int PooledArrayLengthLimitExclusive = 128;
public void Free()
{
    var pool = _pool;
    if (pool != null)
    {
        // We do not want to retain (potentially indefinitely) very large builders
        // ... It makes sense to constrain the size to some "not too small" number.
        if (_builder.Capacity < PooledArrayLengthLimitExclusive)
        {
            if (this.Count != 0) { this.Clear(); }
            pool.Free(this);
            return;
        }
        else
        {
            pool.ForgetTrackedObject(this);
        }
    }
}
```

`PooledStringBuilder.Free()` 用同样规则（`Capacity <= 1024` 才入池）。
`ObjectPool<T>` 的文档注释明确 *“Pool is not meant for storage. If there is no space in the
pool, extra returned objects will be dropped.”*

**这条规则必须移植过来**：本仓库若引入位集池化，**必须拒绝把 148.7 MiB 的数组还进池**，
否则池会长期钉住 LOH 内存——比不池化更糟。阈值应远大于 128，且**复用时必须 `Array.Clear`**，
否则残留位会破坏 fixpoint 的单调性假设。

### Loop 3：`Entry<TKey,TValue>[]` 占 22.3% → 减条目、减宽

**代码**：条目宽度已实测为 `140 B`（`Entry<StableNodeAnchor,NLCPGNode>`）与约 `96 B`
（`Entry<PendingEdgeKey,int>`，推算值）。两者都是**值类型键**，所以 Entry 把整个键**内联**了。

**要点**：把键改成 `int` 序号（例如 `Dictionary<int,NLCPGNode>` 按 ordinal 索引）
可把键从 28 B 降到 4 B，即每条目约 140 → 116 B（**约 17%**）。
真正的大头在 Loop 6：`_ordinals` 这个去重字典（条目数推算约 290 万–410 万，
见 1.4 口径说明）**根本不需要保存完整键值**。

**外部权威/实现**：官方工具是三组，**各自的收益类型不同，不要混用**：

| 工具 | 官方口径 | 对本仓库的作用 |
| --- | --- | --- |
| `Dictionary.EnsureCapacity(int)` | “确保字典能容纳指定条目数而无需再扩容” | **对症**：干掉 1→3→7→17… 扩容序列。报告里 95.9% 的 91.7 GiB 是垃圾，扩容复制是主要来源之一。**注意**：不要把不可信输入直接传入容量（DoS），需 clamp。 |
| `Dictionary.TrimExcess()` / `TrimExcess(int)` | “把容量设为原本只用这些条目初始化时的容量” | **对症**：收缩长期存活的字典。**注意**：它先分配**新数组**再复制，会造成瞬时峰值——在 18 GiB LOH 背景下必须评估。 |
| `List<T>.TrimExcess()` | “若元素数不足容量的 **90%** 则不做任何事”；O(n)，新数组+复制 | 只在 `Count << Capacity` 时调用；同样有瞬时翻倍风险。 |
| `CollectionsMarshal.GetValueRefOrNullRef` / `GetValueRefOrAddDefault` | “访问集合底层表示的非安全类” | **对症**（热路径）：原地读写字典值，**避免二次哈希查找与 struct 值拷贝**。可用于 `Dictionary<IOperation,NLCPGNode>` 一类大值类型。**注意**：得到 `ref` 后任何字典变更都会使其失效。 |
| `CollectionsMarshal.AsSpan(List<T>)` + `SetCount<T>` | 同上 | 从 span 直接填充 `List<T>`，**零中间数组**。 |
| `FrozenDictionary`/`FrozenSet`（`System.Collections.Frozen`，.NET 8+） | “不可变，针对**创建不频繁但运行时频繁使用**的场景；**创建成本相对高**，但查找性能极佳” | **预期管理**：它优化的是**查找速度与启动**，**不实质减少条目数或字节**。对 22.3% 的 `Entry[]` 占比不是解法。仅当存在“长期只读查找表”时才考虑。 |

**明确否定项**：`CollectionsMarshal.SetValue` 在官方 net-10.0 API 页面上**不存在**，
不要按它规划。`System.Collections.Specialized.BitArray` 对大型稠密集合仍是差选择
（非池化、非线程安全）。

### Loop 4：`GetParameters()` 热点 → **归因修正**

**代码核实（重要修正）**：全仓（排除 `Build`/`bin`/`obj`）搜索 `GetParameters(`：
**0 个结果**。`BindingFlags`/`Activator.Create`/`GetType().Get*` 只命中
[CoverageEvidence.cs:183](../../src/NLISSN.Core/Lifting/CoverageEvidence.cs) 一处
`GetProperties(...)`。

**那 `RuntimeMethodInfo.GetParameters()` 是什么？** 仓库大量使用**反射式**
`System.Text.Json`，且**没有任何 source generator / `JsonSerializerContext`**：

| 位置 | 用法 |
| --- | --- |
| [ProjectJsonExporter.cs:14,413,429,442,563,849](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs) | `JsonSerializer.Serialize/DeserializeAsync` |
| [PerformanceSummaryDocument.cs:58](../../src/NLISSN/Performance/PerformanceSummaryDocument.cs) | `JsonSerializerOptions` |
| [PerformanceDiagnosticsCollector.cs:68](../../src/NLISSN.Application/Performance/PerformanceDiagnosticsCollector.cs) | `JsonSerializer.Serialize` |
| `AnalysisEvidence.cs`、`Resolve*dConfigurationArtifact.cs`、`RewritePlanArtifactService.cs` 等 | 同上 |

反射式 System.Text.Json 在**首次**为每个类型构建元数据契约时会调用
`MethodInfo.GetParameters()` 等反射 API，且每次序列化都要走基于反射的
getter/属性访问路径。**这条热点属于“JSON 产物写出”，不属于 CPG 构图热路径。**

**行动含义**：
- 不要把 `GetParameters` 当作构图优化目标；它更可能是**产物/遥测写出**的开销。
- 若要消除它：采用 **source-generated JSON**（`[JsonSerializable]` + `JsonSerializerContext`），
  运行时不再反射构建契约。官方口径是“把元数据收集从运行时移到编译期……
  **消除运行时元数据收集**”，并有“**最多 40%+ 的启动时间下降、私有内存下降**”的表述
  （[source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)）。
  这是**低风险、高确定性**的改动，但**不会**减少 60% 的 LOH 数组。
  **迁移前必须核对的三个限制**（均已核实）：
  1. **只支持 `public`/`internal` 成员**——若某类型用 `[JsonInclude]` 暴露 **private** 成员或访问器，
     source-gen 模式会在**运行时抛 `NotSupportedException`**。这是 record 类型迁移的头号雷区。
  2. **fast-path 模式**不支持 `Converters`、`DictionaryKeyPolicy`、`Encoder`、
     `IgnoreNullValues`、`NumberHandling`、`ReferenceHandler` 及若干属性——
     会**静默回退**到 metadata 模式；若只选了 serialization-optimization 模式则可能直接失败。
  3. **多态只在 metadata 模式支持**，fast-path 不支持；且需要显式 `[JsonDerivedType]`。
  **加固手段**：设 `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>`，
  让任何遗漏的反射路径**快速失败**而不是静默走慢路径。
  **本项目需先审计**：[ProjectJsonExporter.cs:16-17](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs)
  与 [PerformanceSummaryDocument.cs:58-63](../../src/NLISSN/Performance/PerformanceSummaryDocument.cs)
  使用了 `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`（metadata 模式支持），
  而 `PerformanceDiagnosticsCollector.cs:71` 还用了 `DictionaryKeyPolicy`——后者在 fast-path 不被支持，
  需确认走 metadata 模式即可。
- 报告中该热点只覆盖**前 61.12 s**（trace 窗口），而 LOH 峰值在 t=156 s 之后，
  两者时间窗不重叠——这进一步说明该热点**不是**内存墙的成因。

**另发现一处具体的、可立即修的缺陷**：
[PerformanceDiagnosticsCollector.cs:68](../../src/NLISSN.Application/Performance/PerformanceDiagnosticsCollector.cs)
在**方法体内**`new JsonSerializerOptions { … }`：

```csharp
JsonSerializer.Serialize(stream, document, new JsonSerializerOptions
{
  PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
  WriteIndented = true
});
```

`JsonSerializerOptions` 实例是 System.Text.Json **元数据缓存的键**；每次调用新建实例
会**绕过**已构建的类型契约缓存，迫使运行时重新反射构建（并重复付出首次使用成本，
还会反复分配 options 自身及其内部缓存结构）。同一文件的
`document` 是**匿名类型**，进一步增加契约构建面。
[YamlConfigurationLoader.cs:89](../../src/NLISSN.Infrastructure/Configuration/YamlConfigurationLoader.cs)
是同一写法。

**最小修复**：把 `JsonSerializerOptions` 提升为 `static readonly` 字段
（项目里 [ProjectJsonExporter.cs:14](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs) 与
[PerformanceSummaryDocument.cs:58](../../src/NLISSN/Performance/PerformanceSummaryDocument.cs)
已经是这个正确模式）。这是**一行级、零语义风险**的改动，
与 Loop 4 的 source-generator 方案相互独立、可分别验证。
**注意**：`JsonSerializerOptions` 在首次使用后被冻结，提升为静态字段后不得再改属性。

### Loop 5：`ToDictionary`/`Resize` 独占开销 → 去重与容量

**代码**：构图路径上 `ToDictionary` 有十余处，例如
`NLCPGBuilder.cs:720-724`（四个 `Dictionary<…,List<PendingEdge>>`）、
`DataFlowPass.cs:617/867/851`、`NLCPGGraphIndex.cs:139/149/158`、
`CpgFragmentReducer.cs:21`、`SkeletonShardPublisher.cs:164-172`。
`DataFlowPass.cs:186-187` 的 `TryGetCandidates` **每个操作都 `new List` + `new HashSet`**。

**要点**：
- `NLCPGBuilder.cs:756-770` 用了 `graph.Nodes.Where(...).ToArray()` 与
  `OrderBy(...).ThenBy(...).ToArray()`——在**全图**上做多次 LINQ 排序分配。
- `DataFlowPass.cs:186` 的 `new List<NLCPGNode>()` / `new HashSet<NLCPGNode>()`
  在每个使用点分配，属于 Loop 2 类（池化/复用的正当候选）。
- 已知数量时应 `new List<T>(capacity)` 或 `EnsureCapacity`；`Clear()` **不释放**容量
  （[List&lt;T&gt;.Capacity](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.capacity)）。

### Loop 6：`InterproceduralDataFlowPlan[]` 与边去重双份副本

**代码**：`NLCPGBuilder.cs:752` `var plans = new List<InterproceduralDataFlowPlan>();`，
随后 `plans.AddRange(...)` 逐调用点累积。`InterproceduralDataFlowPlan` 是
**`readonly record struct` 含 4 个 `NLCPGNode`（各 104 B）= 416 B + 其余字段**，
所以按值放进 `List<T>` 时每元素 ~432 B。

**但先要修正一个口径**：在**峰值样本 0016** 里 `InterproceduralDataFlowPlan[]`
只有 **2 个实例 / 48 B**（占 0.0001%）。报告里“1,515.6 MB”出现在**另一个样本 `0019-late-246s`**，
报告自己已把它标为“阶段性而非持续驻留”。所以它是**长尾阶段的峰值**，不是峰值样本的主犯。
引用时必须带样本编号，不能与 0016 的排名混用。

**真正的常驻大头是边去重的两份副本**：
1. `PendingEdgeBuffer._ordinals`：`Dictionary<PendingEdgeKey,int>`，实测 **375.3 MiB / 410 万条**。
2. `PendingEdgeBuffer._items`：`List<BufferedPendingEdge>` → `BufferedPendingEdge[]`，**240.0 MB**。

两者编码**同一份信息**（源锚点、目标锚点、kind、结构化标签、context、call site）。
`PendingEdgeKey` 与 `BufferedPendingEdge` 字段完全相同
（[NLCPGGraph.cs:894-908](../../src/NLCPG/Model/NLCPGGraph.cs)），即**同一元数据存了两遍**，
其中锚点是 28 B 的 `StableNodeAnchor`（含 `FilePathId`/`SpanStart`/`SpanEnd`/`Role`/`Ordinal`/`ExtraKeyId`）。

**方向**：`_ordinals` 的职责是去重，不需要保存完整键的**值副本**——
用 `HashSet` 或“开放寻址 + 只存哈希”会小得多；或把锚点对归一化为 `int` 节点序号对
（`(int srcOrdinal, int dstOrdinal)` 8 B，而非 2×28=56 B）。

### Loop 7：`PendingEdge[]` / `BufferedPendingEdge[]` → SoA 与序号

同 Loop 6。补充：`NLCPGGraphIndex.Create` 会对**全边集**构建 **6 个 `NLCPGEdge[]`**，
每个元素实测 **72 B**：

| 数组 | 来源 |
| --- | --- |
| `orderedEdges` | `NLCPGGraphIndex.cs:126-135` `…ThenBy(…).ToArray()` |
| `Outgoing.Edges` | `BuildCsr(..., edge => edge.SourceNodeId, groupByKind: false)` |
| `Incoming.Edges` | `BuildCsr(..., edge => edge.TargetNodeId, groupByKind: false)` |
| `OutgoingByKind.Edges` | `BuildCsr(..., groupByKind: true)` |
| `IncomingByKind.Edges` | `BuildCsr(..., groupByKind: true)` |
| `EdgesByKindBuffer` | `BuildKindBuffer` ([NLCPGGraphIndex.cs:275-298](../../src/NLCPG/Model/NLCPGGraphIndex.cs)) |

（另有 `CanonicalNodes`，但它是 `NLCPGNode[]`（104 B/元素）而非 `NLCPGEdge[]`，不计入本条。）

```text
3,600,000 edges × 72 B × 6 arrays = 1,483.1 MiB
1,000,000 edges × 72 B × 6 arrays =   412.0 MiB
```

这 6 份是**同一批 `NLCPGEdge` 值**的重复副本（其中 4 份是 CSR 邻接，2 份是全局排序/按 kind 缓冲），
且元素含引用（`StructuredLabel`/`CallSiteContext` 是**引用类型**），所以 LOH 里每个元素
都要被 GC 逐元素追踪。这就是报告“`NLCPGEdge[]` 6 实例 257.6 MB”背后的**乘法因子**，
也与实测“6 个实例”完全对上。
把其中 4 个**二级** CSR 邻接表从 `NLCPGEdge[]` 改为 `int[]` edge ordinal：
元素 72 B → 4 B，**每张表元素区减少 94.4%**，同时**消除该表的引用追踪**
（LOH 文档明确点出这一点：改用整数索引后 GC 不必查看引用）。

**代价**：查询时多一次间接寻址（下标的间接）。旧研究
（[adoption audit](2026-09-17-nl-class-memory-optimization-adoption-audit.md)）已给出相同建议，
并强调应先做**一张表**的对照实验，保留 canonical edge table。

### Loop 8：`System.String`（48.4 万–604 万实例）→ 图内字符串表

**代码**：`StringInterner`（[StringInterner.cs](../../src/NLCPG/Model/StringInterner.cs)）
已经实现“文本 → `uint` ID + 回解”，但**每个公共方法都 `lock (_gate)`**，
且 `_idsByText`/`_textsById` 是两张无界 `Dictionary`。`NLCPGNode` 已用
`NameId`/`FullNameId`/`SignatureId`/`TypeFullNameId`/`FilePathId` 存 ID——**这个设计已经落地**。

**所以字符串问题的剩余部分是**：
- `Dictionary<string,string>` 与 `Dictionary<string,List<NLCPGNode>>` 的**键**仍是 string
  （`NLCPGGraph.cs:15`、`DataFlowPass.cs:158-161` 四张 `_byLocation/_byRoot/_byBase/_byBaseAndPath`）；
- `StringInterner` 的 `lock` 在 DOP 12 下是**串行点**（报告里 20,494 次锁争用全部落在并行窗口）；
- 每次 `Intern` 都要拿已构造的 string 做哈希查找，**没有命中时的 span 快路径**。

**外部一手来源**：`String.Intern` 的性能注意事项——需要先构造输入字符串，
且驻留字符串**可能直到进程结束才释放**，故不宜对无界输入使用
（[String.Intern](https://learn.microsoft.com/en-us/dotnet/api/system.string.intern#performance-considerations)）。

**生产级实现（已 fetch 原文）**：Roslyn 的 `StringTable`
（[StringTable.cs](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/InternalUtilities/StringTable.cs)）
正是“**有界 + span 查找 + 命中不分配**”的范本：

```csharp
internal string Add(ReadOnlySpan<char> chars)
{
    var hashCode = Hash.GetFNVHashCode(chars);
    var arr = _localTable;
    var idx = LocalIdxFromHash(hashCode);
    var text = arr[idx].Text;
    if (text != null && arr[idx].HashCode == hashCode)
    {
        var result = arr[idx].Text;
        if (TextEquals(result, chars)) { return result; }   // 命中：零分配
    }
    string? shared = FindSharedEntry(chars, hashCode);
    if (shared != null) { arr[idx].HashCode = hashCode; arr[idx].Text = shared; return shared; }
    return AddItem(chars, hashCode);        // 只有未命中才 chars.ToString()
}
```

要点：**L1 每实例 2048 槽（非线程安全、last-add-wins）+ L2 静态 65536 槽（线程安全）**，
所以热路径不必每次都付 interlocked 成本；容量有限、按需淘汰，不是无界 intern；
`Free()` **故意保留缓存内容**只归还对象。
`Add` 有 `ReadOnlySpan<char>` / `string,int,int` / `char[]` / `StringBuilder` / UTF-8 重载。

**对本仓库 `StringInterner` 的三条具体差距**：
1. **无 `ReadOnlySpan<char>` 快路径** → 每次查找都要先构造 string（Roslyn 命中时零分配）。
2. **无容量上界** → 两张 `Dictionary` 随字符种类单调增长。
3. **每个公共方法都 `lock`** → DOP12 下成为串行点（报告里 20,494 次锁争用全部落在并行窗口）。
Roslyn 的 L1/L2 分层正是为解决（3）而设计的：热路径走非线程安全的 L1，
只有 L1 未命中才进 L2。

**可直接采用的现成实现（已 fetch 源码核实）**：CommunityToolkit.HighPerformance 的 `StringPool`
（[StringPool.cs](https://raw.githubusercontent.com/CommunityToolkit/dotnet/main/src/CommunityToolkit.HighPerformance/Buffers/StringPool.cs)）
正是“有界 + span 查找 + 命中零分配”：
- **有界且会淘汰**：内部 `FixedSizePriorityMap[] maps`，按 `timestamp` 小顶堆做 **LFU 淘汰**；
  默认容量 2048、`MinimumSize = 32`，并有 `public static StringPool Shared`。
- **命中零分配**：`GetOrAdd(ReadOnlySpan<char>)` 与 `TryGet(ReadOnlySpan<char>, out string?)`；
  未命中才 `span.ToString()`。
- **低争用**：per-bucket `lock (map.SyncRoot)`（`SyncRoot => this.buckets`）。
- 包：CommunityToolkit.HighPerformance **8.4.2**（MIT，Microsoft），
  csproj 目标 `netstandard2.0;netstandard2.1;net8.0`，net10 应用消费 net8.0 资产。

**采用 `StringPool` 的硬边界**：其条目**可被淘汰**，因此**不保证**“同一文本永远得到同一实例”。
`StringInterner` 的 `uint` ID 是**稳定且单调分配**的，两者**语义不同**。
所以 `StringPool` 可用于**纯查找/去重**（如 `Dictionary<string,…>` 的键归一化），
但**不能**直接替换 `StringInterner` 承担图内持久 ID 的职责。

结论不变：应使用**有界、分析范围生命周期**的字符串表——`StringInterner` 已符合
“范围生命周期”与“稳定 ID”，缺的是**有界**与**span 查找**；而 `lock` 应改为
Roslyn 式 L1/L2 分层或 `StringPool` 式 per-bucket 锁。

### Loop 9：节点存储 → 去重复副本（不是 class→struct）

**代码事实**：`NLCPGNode` **已经是 104 B 的 `readonly record struct`**；
`_mutableNodesByAnchor`（约 139.5 万条 × 140 B = 186.3 MB）与
`_nodesByNodeId`（`Dictionary<NodeId,NLCPGNode>`）**同时**保存同一批节点的完整 104 B 值。
加上 `NLCPGNode[]`（9,041 实例 / 66.1 MB）等，同一逻辑节点在内存里存在**多份 104 B 副本**。

**方向**（与旧研究一致）：保留**一份** canonical node 表，其余索引只存 `NodeId`/`int` ordinal。
`NLCPGGraphIndex` 已有 `NodeOrdinals`（`Dictionary<NodeId,int>`）与 `OrdinalNodeList`
（[NLCPGGraphIndex.cs:216-229](../../src/NLCPG/Model/NLCPGGraphIndex.cs)）这一正确模式，
应推广到 `_mutableNodesByAnchor`/`_nodesByNodeId` 与 `Dictionary<IOperation,NLCPGNode>`（66 实例 / 102.2 MB）。

**外部依据（逐字引用）**：LOH 文档的二叉树示例正是本仓库这类问题的权威表述
（[Large object heap](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)）：

> “If the elements of an array are reference-rich, it incurs a cost that is not present if the
> elements are not reference-rich. **If the element doesn't contain any references, the garbage
> collector doesn't need to go through the array at all.** … An alternative approach is to store
> the index of the right and the left nodes … And the garbage collector doesn't need to look at
> any references for the left and right node.”

这正是 `NLCPGEdge[]`(72 B，含 2 个引用字段) → `int[]`(4 B，**无引用**) 的依据：
GC **不再逐元素扫描**该数组。CSR（offsets `int[]` + targets `int[]`）的参考实现见
QuikGraph 的 `CompressedSparseRowGraph`（真实仓库是
[KeRNeLith/QuikGraph](https://github.com/KeRNeLith/QuikGraph)，
其 `_outEdges` + `Range(Start,End)` 的构建与遍历即 offsets+targets 布局；
[源码](https://raw.githubusercontent.com/KeRNeLith/QuikGraph/master/src/QuikGraph/Structures/Graphs/CompressedSparseRowGraph.cs)）。

**另一条针对小数组的手段**：`[InlineArray]`（C# 12 / .NET 8+）可把**固定小容量**缓冲区
内联进对象，去掉数组对象、数组头与该数组的 GC 扫描——
正对 `NLCPGNode[]` 的 9,041 个实例（平均约 7.6 KB）里那些**真正小容量**的情形。
**两个硬约束**：（1）长度必须是编译期常量；
（2）.NET 9+ 下默认 `Equals`/`GetHashCode` **会抛 `NotSupportedException`**，必须同时重写二者
（[InlineArrayAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.inlinearrayattribute)）。
同理 `SpanOwner<T>`/`MemoryOwner<T>`（CommunityToolkit.HighPerformance 8.4.2, MIT）
可替代临时 `T[]`，但 `SpanOwner<T>` 是 `ref struct`：**不能用于 async/lambda，且必须放进 `using`**，
否则缓冲区**静默不归还**。

### Loop 10：GC 运行时配置 → 什么是真解法、什么是伪解法

**实测现状**（1.5 节）：全默认 = Workstation GC + 后台 GC + **无堆上限**。

按官方文档逐项判断（[GC runtime config](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)、
[DATAS](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/datas)）：

| 设置 | 官方键名/属性 | 官方口径与本场景判断 |
| --- | --- | --- |
| `System.GC.Server` / `ServerGarbageCollection` | `DOTNET_gcServer` | 默认 `false`（Workstation）。**收益可疑**：负载实测平均 1.1/16 核、长尾单线程，Server GC 按核数建堆，与内存墙**反向**。 |
| `System.GC.Concurrent` / `ConcurrentGarbageCollection` | `DOTNET_gcConcurrent` | 默认 `true`（后台 GC）。**无需改**。 |
| `System.GC.DynamicAdaptationMode` / `GarbageCollectionAdaptationMode` | `DOTNET_GCDynamicAdaptationMode` | **重要更正**：文档把 DATAS 列在“**Manage resource usage for Server GC**”分组下，而该分组前言明确写 *“They **have no effect on Workstation GC**”*。本仓库是 Workstation GC，**所以 DATAS 是否生效并不确定**（见下方“更正”）。 |
| `System.GC.HeapHardLimit` / `…Percent` | `DOTNET_GCHeapHardLimit` | 官方定义：“GC 堆**与 GC bookkeeping** 的最大**提交**大小”，仅 64 位生效。**不作为解法**：超出走向 OOM 或更频繁 GC，不会让必要活数据变小；本负载问题是**瞬态垃圾**。 |
| `System.GC.LOHThreshold` | `DOTNET_GCLOHThreshold` | **危险且无用**。官方明确“**你指定的值必须大于默认阈值**”（85,000 B）——抬高只会让**更多**对象进 LOH，方向完全错。注意“该值可能被运行时**上限裁剪**”，可用 `GC.GetConfigurationVariables()` 查实际生效值。 |
| `System.GC.ConserveMemory`（0–9） | `DOTNET_GCConserveMemory` | **最值得试的一项**。官方逐字：“**If the value is non-zero, the large object heap will be compacted automatically if it has too much fragmentation.**” 并建议 *“Start with a value between 5 and 7.”* 直击报告中的 LOH 长期 ≥17.5 GiB 不降与 704 MB 碎片。代价：更频繁 GC 与可能更长暂停。 |
| `System.GC.RetainVM` / `RetainVMGarbageCollection` | `DOTNET_GCRetainVM` | 只决定“该删除的段放入 standby 还是还给 OS”；与“换页”症状相关但**不解决问题**。 |
| `System.GC.HighMemoryPercent` | `DOTNET_GCHighMemPercent` | **值得试的第二项**。默认 90% 物理内存负载才转激进 full-compacting GC。官方明确：“**if you want larger processes to have smaller heap sizes (even when there's plenty of physical memory available), lowering this threshold is an effective way for GC to react sooner to compact the heap down.**” 机器仅 13.86 GiB，**调低**可让 GC 更早压缩。 |

**新增：两项 .NET 10 专有、且与本负载“巨型分配”高度相关的设置**

这两项本轮才从官方文档确认，未被既有报告提及：

| 设置 | 官方键名（.NET 10） | 为何与本仓库相关 |
| --- | --- | --- |
| `System.GC.RegionRange` | `DOTNET_GCRegionRange`（env 自 .NET 7，JSON 键自 .NET 10） | 官方：“建议预留量为 GC 堆已提交大小的 **2–5 倍**。**If your scenario does not make many large allocations**（指 UOH 或大于 UOH region 的分配），twice the committed size should be safe. **Otherwise, you might want to make it larger so you don't incur too frequent full-compacting GCs to make space for those larger regions.**” 本仓库**恰恰是**大量巨型分配（148.7 MiB 单数组）的场景，官方这句话直接指向它。默认 256 GB 预留（仅保留虚拟地址，不提交）。 |
| `System.GC.RegionSize` | `DOTNET_GCRegionSize`（env 自 .NET 7，JSON 键自 .NET 10） | 默认 SOH region 4 MB，UOH（LOH+POH）为其 8 倍。官方：“regions 只在需要时分配，通常不必调整”，但在**堆很小**时调小可减少 GC 自身 bookkeeping 的原生内存。对本仓库属次要项。 |

DATAS 在 .NET 10 还新增了 `System.GC.DGen0GrowthPercent` / `DGen0GrowthMaxFactor` / `DGen0GrowthMinFactor`
（以及 `DTargetTCP`）来调 gen0 预算公式——但**前提是 DATAS 实际生效**，见下方更正。

**更正：DATAS 在本仓库是否生效，本文不作断言**

多数资料（含本文初稿）会说“DATAS 自 .NET 9 默认开启”。但官方 config 文档把 DATAS 放在
**Server GC 设置组**内，该组前言的原文是：

> “The following settings affect the number of Server GC threads and if/how they are affinitized
> to cores. **They have no effect on Workstation GC.**”
> —— 组内列出 `HeapCount`、`Affinitize`、`CPU groups`、**`DATAS`**。

而 [DATAS 专页](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/datas)
又说它是“workstation GC 与 server GC 的**混合**，能用少至 1 个堆”。两处表述存在张力。
本仓库**未启用 Server GC**（1.5 节已实测），因此 **DATAS 是否在本进程生效无法由文档单独判定**。

**正确做法**：用 `GC.GetConfigurationVariables()` 在运行时**读出实际生效配置**，
而不是从文档推断。这应作为任何 GC 调参实验的**第一步**，否则会把 A/B 差异归因到未生效的开关上。

**明确结论**：GC 配置**全是缓解，不是根因修复**。在
“单类型 60.2% 是 O(N²) 数组”“LOH 18 GiB 由 >100 KB 单实例主导”面前，
调 GC 只是改变撞墙的形态。**必须与 Loop 1/6/7 一起做，且不能替代它们。**
优先顺序：先用 `GC.GetConfigurationVariables()` 确认现状，再试 `ConserveMemory`（5–7）
与 `HighMemoryPercent`，并考虑 `RegionRange` 对巨型分配的适配。

## 三、对现有报告的归因修正

| 报告中的表述 | 本轮核实结果 | 影响 |
| --- | --- | --- |
| `dotnet-trace` 热点 `RuntimeMethodInfo.GetParameters()` 指向“per-node 反射式元数据访问” | **`src/` 下 0 处 `GetParameters()`；反射来自 System.Text.Json（无 source generator）** | 该热点属**产物写出**，不是构图热路径；且 trace 窗口（0–61 s）与 LOH 峰值（>156 s）不重叠。 |
| `InterproceduralDataFlowPlan[]` 1,515.6 MB | 出现在样本 `0019`，**峰值样本 0016 里只有 48 B** | 属长尾阶段性峰值；不能与 0016 的占比混用。 |
| “代码线索”指向 `DataFlowPass.cs:618-619` | **已升级为强证据**：反解出 **N=35,303**，单数组 **148.7 MiB**，字节级吻合（+24 B 数组头） | 从“线索”变为“可定量目标”。 |
| `MaxFlowNodesPerMethod` 默认无上限 | 复核确认 `Unbounded = int.MaxValue`，且 `CreateDefault()` 传 `null` | 确认预算未生效。 |
| 报告未提及预算能否从配置设置 | **不能**：YAML schema 与 `nlissn.yml` 均无 `dataFlow`/`maxFlowNodes` 字段 | 修预算需**同时**改代码或加配置映射。 |
| 报告称 `NLCPGNode` 为富字段 class（旧研究） | **实际已是 104 B `readonly record struct`**，`NLCPGEdge` 已是 72 B struct | “class→struct”建议**已无空间**，应转向“去重复副本”。 |
| 报告用 `dotnet-trace report topN` 的 Inclusive/Exclusive 判定“独占热点” | 该命令官方定义是“**在调用栈上停留时间最长的前 N 个方法**”，即 **CPU 采样**归因，**不是分配归因** | 该表是 **CPU 时间**结论，不能用来回答“谁分配了 LOH 数组”。见 3.1。 |

### 3.1 如何真正闭合两个证据缺口（含官方工具口径）

**(a) 分配归因（报告缺失，也是 P0 归属的最后一步）**

`dotnet-trace report topN` 的官方说明是 *“Finds the top N methods that have been on the callstack
the longest”*，配合 `--inclusive` 切换 inclusive/exclusive——这是**采样 CPU 时间**视图
（[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)）。
它回答“CPU 花在哪”，**不回答“字节是谁分配的”**。

要按**调用栈**归因分配，必须用分配采样栈：

- `gc-verbose` profile 的定义是 *“Tracks GC collections and **samples object allocations**”*，
  所以报告用的 profile **本身具备**分配采样能力，只是没有按栈呈现而已；
- 相关 CLR 关键字：`gcsampledobjectallocationhigh`（`0x200000`）、
  `allocationsampling`（`0x80000000000`）；
- 在 PerfView 中查看 **GC Heap Alloc** 视图可按调用栈聚合分配（这正是 LOH 文档推荐的
  `perfview /GCOnly` + AllocationTick 路线）。

**这一步能把本文的“N=35,303 字节级反解”从强关联升级为直接归因。**

**(b) 换页计数（报告明确记为推断）**

报告未采集任何 page fault 计数器。要坐实换页，需要
`\Memory\Pages/sec`、`\Process(*)\Page Faults/sec`，或 ETW provider
`Microsoft-Windows-Kernel-Memory`。本文沿用报告的判定边界：换页仍是**推断**。

**(c) 复核本文建议的最小实验矩阵**

对 P0/P1 每一项，至少同时记录：构造与冻结耗时、分配字节、GC 各代次数与暂停、
`GC.GetGCMemoryInfo()` 的 `HeapSizeBytes`/`TotalCommittedBytes`、
以及**语义快照等价**（DOP1 与 DOP12）。否则无法区分“省了内存”与“改变了结果”。

## 四、按优先级排序的落地清单

| 优先级 | 方法 | 针对 | 预期量级 | 风险 | 验证入口 |
| --- | --- | --- | --- | --- | --- |
| **P0** | 给 `MaxFlowNodesPerMethod` 设有限默认（并暴露到 YAML），超出走已实现的 `SkipMethod`；**补一条断言 `FlowNodeLimitExceeded` 的测试** | 60.2% 的 `System.UInt64[]` | 消除单数组 148.7 MiB / 每方法 297.4 MiB | 低（“跳过”行为已有测试，但该 reason 未被断言）；需测语义影响面 | `NLCPGPartitionedBuilderTests`（`…NodeBudget_SkipsOverBudgetMethod`）+ 新增 reason 断言 |
| **P0** | `inSets`/`outSets` 改分块位集或 `ArrayPool<ulong>` 租借 + 强制单数组 <85,000 B | 同上 | 把 LOH 巨型数组移出 LOH | 中（需保持 fixpoint 语义与遍历顺序） | 位集单元测试 + DOP1 语义快照 |
| **P1** | 边去重去掉双份副本：`_ordinals` 用 `HashSet`/只存哈希；`_buffered` 用序号对而非锚点对 | 375.3 MiB + 240.0 MB | ~450–600 MB | 中（须保持去重等价与确定性顺序） | `NLCPGGraph` 去重契约、冻结快照 |
| **P1** | 4 张二级 CSR 邻接表 `NLCPGEdge[]`→`int[]` ordinal | 6×72 B 乘法因子 | 每表元素区 −94.4%，并去引用追踪 | 中（查询多一次间接） | 先做**一张表**对照实验；kind/path 查询契约 |
| **P2** | 索引去重复 104 B 副本（`_nodesByNodeId`、`Dictionary<IOperation,NLCPGNode>`）改用 ordinal | 186.3 + 102.2 MB | ~200 MB | 中低 | 节点身份/锚点契约 |
| **P2** | 把 `new JsonSerializerOptions{…}` 提升为 `static readonly`（`PerformanceDiagnosticsCollector.cs:68`、`YamlConfigurationLoader.cs:89`） | 反复重建元数据契约缓存 → `GetParameters` 反射 | 去掉重复契约构建与 options 分配 | **极低**（一行级） | 产物字节等价测试 |
| **P2** | source-generated `System.Text.Json`（`JsonSerializerContext`，metadata 模式） | `GetParameters`/反射序列化开销 | 官方称“最多 40%+ 启动时间下降、私有内存下降”；**不减堆占比** | 低–中（private 成员/多态需先审计） | 产物字节等价测试 + `JsonSerializerIsReflectionEnabledByDefault=false` 快速失败 |
| **P2** | 长期只读查找表改 `FrozenDictionary`；长期存活字典加 `EnsureCapacity`/`TrimExcess` | `Entry[]` 22.3% 的扩容翻倍与冗余容量 | 消除 1→3→7→17… 扩容复制序列 | 低（TrimExcess 有瞬时峰值） | 分配量前后对照；注意 18 GiB LOH 背景下的瞬时翻倍 |
| **P3** | `[InlineArray]` / `SpanOwner<T>` 处理**固定小容量**临时缓冲 | `NLCPGNode[]` 9,041 实例中真正小容量者 | 去掉数组对象+数组头+GC 扫描 | 低–中（.NET 9+ 需重写 `Equals`/`GetHashCode`；`ref struct` 不能进 async） | 单测 + 分配量对照 |
| **P2** | `StringInterner` 去 `lock`、加有界容量与 `ReadOnlySpan<char>` 查找 | 锁争用 + 字符串表 | 改善并行窗口；字符串 1.15–1.6 GB 需另测 | 中（并发正确性） | 现有 Contract 测试 + DOP12 复跑 |
| **P3** | 先 `GC.GetConfigurationVariables()` 读出实际生效配置，再 A/B 试 `ConserveMemory`(5–7) 与 `HighMemoryPercent` | LOH 碎片 704 MB、长期不降；13.86 GiB 物理内存 | 降低碎片与堆峰值 | 低（仅配置） | 用同一输入跑 A/B，记录 pause 与峰值 |
| **P3** | 评估 `System.GC.RegionRange`（.NET 10 新增 JSON 键）以适配**大量巨型分配** | LOH 18 GiB、148.7 MiB 单数组 | 官方称巨型分配多时需加大预留，避免过频 full-compacting GC | 低（仅配置；默认已预留 256 GB 仅虚拟地址） | A/B 记录 full GC 次数与暂停 |
| **P3** | `DataFlowPass.TryGetCandidates` 的 per-call `List`/`HashSet` 复用 | 短命分配 | 降低 91.7 GiB 流量的一部分 | 低 | 分配量前后对照 |

**不要做**（含本轮核实的具体否定项）：

- 把 `NLCPGNode`/`NLCPGEdge` 改成 struct——它们**已经是** 104 B / 72 B 的 struct；
- 调高 `System.GC.LOHThreshold`——只会让**更多**对象进 LOH，方向相反；
- 用 `GC.Collect()` 当常规优化；把 `GCHeapHardLimit` 当内存墙解法；
- 引入 **`Collections.Pooled`**（真实仓库 `jtmueller/Collections.Pooled`，非 `jacobslusser`）：
  最新稳定 **1.0.82（2019-04-03）**、仅 netstandard2.0、约 2.7k 下载、**实质停维护**；
  其 README 自述“**若忘记 Dispose，什么都不会坏，只是分配和暂停会回到 `List<T>` 的水平**”——
  即**静默退化、无报错**。在本仓库规模上这是真实的泄漏/退化风险。BCL 的
  `EnsureCapacity`/`TrimExcess` 加上 CommunityToolkit 已覆盖同类需求；
- 引入 **`Microsoft.Toolkit.HighPerformance`**：已废弃（最高仅 7.1.2），继任者是
  `CommunityToolkit.HighPerformance`；
- 用 **`SparseBitsets`** 解决稠密 O(N²) 位集：其内部是 `Dictionary<long,ulong>`，
  **每个 64 位字一个字典条目**，会**复现**本仓库已有的 `Entry[]` 膨胀问题；
- 依赖 `CollectionsMarshal.SetValue`：该 API **不存在**于官方 net-10.0 页面；
- 期待 `FrozenDictionary` 降低字节占用：它优化查找/启动，**条目数与字节不变**。

## 五、未验证边界

1. 本文**没有**运行新的端到端 Version4 基准，**没有**测出任何百分比收益。
   表中“预期量级”是对**单点结构尺寸**的实测推算，不是端到端观察。
2. **未采集分配调用栈**：N=35,303 是由 `System.UInt64[]` 的 `TypeSize` 反解 + 代码公式吻合得出的，
   属**强关联**；报告本身也注明“未把具体实例回连到分配点”。坐实需 `dotnet-trace`
   按调用栈做分配归因。
3. **未采集 page fault 计数器**：换页仍是推断。
4. N=35,303 是**单个**实例；样本中 8 个 >100 MB 实例对应的 N 不同（平均约 47,000），
   本文未逐一反解。
5. Loop 4 的“反射来自 System.Text.Json”是**基于代码事实的推断**（无 source generator +
   用了反射式序列化 + trace 窗口不重叠），**不是**从 trace 调用栈直接读出的；
   坐实需读 `Trace/allocation.nettrace` 的调用栈明细。
6. **条目数推算未通过交叉校验**：`_ordinals` 由条目尺寸推算得 ≈410 万条，与
   `BufferedPendingEdge[]` 反推的 ≈286 万条相差约 30%（见 1.4 节口径说明）。
   本文只采用**不依赖条目数**的字节数结论（合计 615.3 MiB / 15.94%）。
   定论需采集分配栈或加字典计数埋点。
7. 本文**没有**验证 `DataFlowPass` 的上限是否会改变分析结论的正确性——只验证了
   “上限机制与溢出策略已在源码中实现且有测试”。设上限后对规则/决策产出的影响面**未测**。
8. **Roslyn 侧的两处未核实项**（本轮子研究明确标注为未取得，不得引用）：
   `MaxUnoptimizedMethodSize` 与 `IsTooComplexToAnalyze` **在已 fetch 的 Roslyn 源码中不存在**；
   `SegmentedArray<T>` 的**名字与用法**已核实（`Microsoft.CodeAnalysis.Collections`，
   `new SegmentedArray<Entry>(SharedSize)`），但其**分块实现源码未取得**（候选路径均 404），
   所以“Roslyn 按多大块切分”这一点**本文不作断言**。
9. `StringPool` 推荐的边界：其条目**可被淘汰**，所以**不保证**字符串引用身份跨时间稳定。
   若本仓库任何逻辑依赖“同一文本必得同一实例/同一 ID”，则**不能**直接用 `StringPool`
   替换 `StringInterner`（后者的 ID 是稳定且单调分配的）。两者语义不同，需分别评估。
6. 外部来源中的 `Frozen`/`CollectionsMarshal`/第三方池化库的具体 API 与版本
   见“来源”一节标注的核实状态；凡未 fetch 原文的均标为未核实。

## 六、复现

```powershell
# 1) 复核 offender 排名与数组占比（从仓库内 CSV）
Import-Csv docs\benchmarks\data\version4-dop12-type-share-0016.csv |
  Where-Object { $_.Type -match '\[\]$' } |
  Measure-Object -Property TotalBytes -Sum

# 2) 反解位集 N（须得到 N=35303 且 +24 == 155898072）
35303 * [Math]::Ceiling(35303/64) * 8

# 3) 托管尺寸与位集规模实测（临时程序，引用 Build/src/Debug/net10.0/NLCPG.dll）
#    程序见本文“1.2/1.3”口径：Unsafe.SizeOf<T>() + GC.GetTotalAllocatedBytes(true) 差值

# 4) 确认运行时无 GC 配置
Get-Content Build\src\Debug\net10.0\NLCPG.runtimeconfig.json
```

## 七、来源

已 fetch 原文（一手）：

- [Large object heap (LOH)](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
  —— 85,000 B 阈值；LOH 只在 Gen2 回收、不清零成本、碎片；**二叉树示例建议用整数索引代替对象引用**；
  “建议复用大对象池而不是分配临时大对象”。
- [Garbage collector config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
  —— `System.GC.Server`/`Concurrent`/`RetainVM`/`ConserveMemory`(0–9)/`HeapHardLimit`(+SOH/LOH/POH)
  /`HeapHardLimitPercent`/`LOHThreshold`/`HighMemoryPercent`/`HeapCount`/`NoAffinitize`/
  `DynamicAdaptationMode`/`DRegionRange`/`RegionSize`/`DGen0Growth*` 的精确键名、取值与版本。
- [Dynamic adaptation to application sizes (DATAS)](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/datas)
  —— .NET 8 opt-in、.NET 9 默认开启；按存活数据设分配预算、动态增减 heap、必要时 full-compacting。
- [dotnet/roslyn `BitVector.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Collections/BitVector.cs)
  —— 内联 `_bits0` + 按需增长 `Word[] _bits`；`EnsureCapacity`/`UnionWith`/`IntersectWith` 实现。
- [dotnet/roslyn `AbstractFlowPass.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/FlowAnalysis/AbstractFlowPass.cs)
  —— **一个当前状态** `State`/`StateWhenTrue`/`StateWhenFalse` +
  **按需填充的 `PooledDictionary<LabelSymbol, TLocalState> _labels`**；
  `Free()` 归还 `Diagnostics`/`PendingBranches`/`_labelsSeen`/`_labels`；
  类文档承认病态输入下会二次方退化。这是“不要按 N×N 预留”的最强反例。
- [`DefiniteAssignment.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/FlowAnalysis/DefiniteAssignment.cs)
  —— 每个状态一个 `BitVector`，`Normalize` 里 `EnsureCapacity(variableBySlot.Count)` 增量增长；
  初始态为零分配的 `BitVector.Empty`；`PooledDictionary`/`PooledHashSet`/`ArrayBuilder` 显式 `Free()`。
- [`MethodCompiler.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/Compiler/MethodCompiler.cs)
  —— `localVariables.Length > 0xFFFE` → 报 `ERR_TooManyLocals` → `return null` 不发射。
  **“硬上界 + 报错 + 放弃”这一生产模式的直接出处。**
- [`ArrayBuilder.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Dependencies/PooledObjects/ArrayBuilder.cs)
  —— `PooledArrayLengthLimitExclusive = 128` 与 `Free()` 的“超大实例不入池、改 `ForgetTrackedObject`”规则；
  `PooledDictionary`/`PooledHashSet`/`PooledStringBuilder`/`ObjectPool<T>` 同目录。
  `ObjectPool<T>` 文档注释明确“池不是存储，超额归还即丢弃”。
- [`StringTable.cs`](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/InternalUtilities/StringTable.cs)
  —— L1(2048 槽,非线程安全) + L2(65536 槽,线程安全) 两级；`Add(ReadOnlySpan<char>)`
  **命中零分配**；`Free()` 故意保留缓存内容。
- [`jitconfigvalues.h`](https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/jit/jitconfigvalues.h)
  —— `JitMaxLocalsToTrack = 0x400`：**超过阈值就停止追踪**，而不是继续扩张内存。
- [CommunityToolkit `StringPool.cs`](https://raw.githubusercontent.com/CommunityToolkit/dotnet/main/src/CommunityToolkit.HighPerformance/Buffers/StringPool.cs)
  —— LFU 淘汰、有界、`GetOrAdd(ReadOnlySpan<char>)` 命中零分配、per-bucket 锁。
- [`SparseBitSet.cs`](https://raw.githubusercontent.com/RupertAvery/SparseBitsets/master/SparseBitsets/SparseBitSet.cs)
  —— `Dictionary<long, ulong> _bitFields`，**稠密场景严格劣于裸 `ulong[]`**（反面证据）。
- [Source generation in System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
  与 [source-generation modes](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes)
  —— “消除运行时元数据收集”“最多 40%+ 启动时间下降”；fast-path 不支持项清单；
  `JsonSerializerIsReflectionEnabledByDefault` 加固开关。
- [Polymorphism in System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)
  —— 多态**只在 metadata 模式支持**，需显式 `[JsonDerivedType]`。
- [Dictionary.EnsureCapacity](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.ensurecapacity)、
  [Dictionary.TrimExcess](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.trimexcess)、
  [List.TrimExcess](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.trimexcess)
  —— “90% 容量内不做任何事”“Clear+TrimExcess 得到最小存储”等确切语义。
- [CollectionsMarshal](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.collectionsmarshal)
  —— `GetValueRefOrNullRef`/`GetValueRefOrAddDefault`/`AsSpan`/`SetCount`；
  **页面上没有 `SetValue`**。
- [InlineArrayAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.inlinearrayattribute)
  —— 固定长度数组内联；.NET 9+ 默认 `Equals`/`GetHashCode` 抛 `NotSupportedException`。
- [QuikGraph `CompressedSparseRowGraph.cs`](https://raw.githubusercontent.com/KeRNeLith/QuikGraph/master/src/QuikGraph/Structures/Graphs/CompressedSparseRowGraph.cs)
  —— CSR（offsets + targets）在 C# 中的参考实现。
- [CommunityToolkit `SpanOwner{T}.cs`](https://raw.githubusercontent.com/CommunityToolkit/dotnet/main/src/CommunityToolkit.HighPerformance/Buffers/SpanOwner%7BT%7D.cs)
  —— `ArrayPool<T>.Shared` 租借的 `ref struct` 包装，必须 `using` 归还。

旧研究中已核验、本文沿用的来源：

- [ArrayPool&lt;T&gt;](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1) 与
  [Return 契约](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return)
- [List&lt;T&gt;.Capacity](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.capacity)
- [String.Intern 性能注意事项](https://learn.microsoft.com/en-us/dotnet/api/system.string.intern#performance-considerations)
- [CommunityToolkit StringPool](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.highperformance.buffers.stringpool)
- [Choosing between class and struct](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct)
- [Boxing and unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing)
- [Memory&lt;T&gt; and Span&lt;T&gt; usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)
- [Object reuse with ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool)
- [CA1846 — Prefer AsSpan over Substring](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1846)
- [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)、
  [dotnet-gcdump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
- [Performance Improvements in .NET 10](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)

GitHub 仓库检索（经 `api.github.com` 搜索确认存在，逐个核实状态如下）：

- [RupertAvery/SparseBitsets](https://github.com/RupertAvery/SparseBitsets)（MIT，纯 C# 位集）——
  **已核实为错误工具，不要采用。** 其内部**不是** `ulong[]` 支撑的位集，而是
  `private Dictionary<long, ulong> _bitFields`，**每个被触碰的 64 位字占一个字典条目**
  （源码：[SparseBitSet.cs](https://raw.githubusercontent.com/RupertAvery/SparseBitsets/master/SparseBitsets/SparseBitSet.cs)）。
  对稠密集合，这比裸 `ulong[]` **严格更差**（每条目字典开销 vs 8 字节，外加哈希，且不池化）。
  其真正算法是 `Runs`（`List<Run>` 存连续 `ulong[]` 块，`Pack()`/`Unpack()`），
  只在“稀疏但有簇”时才划算——**本仓库的 reaching-definition 集合是稠密的**。
  版本状态：netstandard2.0，NuGet 最后发布 1.0.1（2022-12），累计下载约 2.3k。
- [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) 的 `StringPool`——
  **已核实，是本轮字符串方向的推荐实现。** 源码：
  [StringPool.cs](https://raw.githubusercontent.com/CommunityToolkit/dotnet/main/src/CommunityToolkit.HighPerformance/Buffers/StringPool.cs)。
  确认要点：内部是 `FixedSizePriorityMap[] maps`，**LFU 淘汰**（`timestamp` 上的小顶堆，
  默认容量 2048、`MinimumSize = 32`，`public static StringPool Shared`）；
  公开 `GetOrAdd(ReadOnlySpan<char>)` 与 `TryGet(ReadOnlySpan<char>, out string?)`，
  **命中零分配**，未命中才 `span.ToString()` 分配一个字符串；
  线程安全用 per-bucket `lock (map.SyncRoot)`（`SyncRoot => this.buckets`），
  多 map 故争用低。**重要边界**：条目**可被淘汰**，所以字符串的缓存身份是
  **尽力而为、非保证**——不能依赖跨时间的引用相等。
  包：CommunityToolkit.HighPerformance **8.4.2**（MIT，Microsoft），
  csproj 目标 `netstandard2.0;netstandard2.1;net8.0`，net10 应用消费 net8.0 资产。
- [jtmueller/Collections.Pooled](https://github.com/jtmueller/Collections.Pooled)——
  **注意仓库位置**：任务中常见的 `jacobslusser/Collections.Pooled` URL **404**，
  真实仓库是 `jtmueller/Collections.Pooled`。最新稳定 **1.0.82（2019-04-03）**，
  仅 netstandard2.0，下载约 2.7k——**实质已停止维护**，不建议为此引入。
- [ZeroAlloc-Net/ZeroAlloc.Collections](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections)（MIT，
  池化 List/环形缓冲/span 字典）、[qoollo/dotNet-turbo](https://github.com/qoollo/dotNet-turbo)（MIT）——
  存在但新/小众；**引入前需按仓库约束评估新依赖**，本轮未评估其正确性与兼容性。
- `Roaring.Net` 1.4.1 明确列出 .NET 10 支持，但它是 **CRoaring 的原生 P/Invoke 包装**
  （需 vcpkg/CMake 原生构建）——**采用风险高**，且 Roaring 是稀疏/中等基数位图，
  对稠密 O(N²) 场景同样不是对症工具。
