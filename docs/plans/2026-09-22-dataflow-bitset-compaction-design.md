# DataFlow 位集压缩设计

**状态：** 已实现并验证（2026-09-22）。执行记录见
[执行计划](2026-09-22-dataflow-bitset-compaction-execution.md) 的「执行结果」小节，
实测收益见 [Version4 DOP12 诊断记录](../../docs/benchmarks/nlissn-version4-dop12-diagnostics.md)
的「落地与实测」小节。下文 §3.4 的倍数是设计期按假定 `D` 的推算，**实测值以执行记录为准**。

**目标：** 把 `DataFlowPass` 的 reaching-definitions 位集从"按流节点数 N 开位"改为
"按定义数 D 开位"，并消除 `inSets` 在 fixpoint 循环内的死存储，从而把巨型方法的
LOH 分配量降低 50–100 倍，且**不改变任何图的语义、查询结果、DOP 结果或持久化格式**。

**范围：** 只改 `src/NLCPG/Builder/Passes/DataFlowPass.cs` 及其契约/度量落点的同步。
不改 `NLCPGDataFlowOptions` 的公开语义，不改持久化 schema，不改 `DominancePass`。

---

## 1. 背景与动机

Version4（Terraria，967 个 `.cs`）DOP-12 诊断中，`System.UInt64[]` 是**单点峰值占用冠军**
（2,324.3 MB，占峰值样本堆的 60.20%）也是**未截断样本的累计冠军**（4,826.4 MB）。
经尺寸反解，其分配点唯一指向 `DataFlowPass.cs:618-619`：

```csharp
var wordsPerSet = BitSetWordCount(plan.FlowNodes.Length);              // ceil(N/64)
var inSets  = new ulong[checked(flowNodes.Length * wordsPerSet)];      // 332.0 MB @N=52743
var outSets = new ulong[checked(flowNodes.Length * wordsPerSet)];      // 332.0 MB @N=52743
```

规模是 **O(N²)**：`N × ceil(N/64)` 个 `ulong`。实测反解出的 N 为
28,110 / 35,303 / 39,076 / 52,743，单方法两数组合计 188.7–664.0 MB。

关键背景事实（**均已核对源码**）：

| 事实 | 位置 |
| --- | --- |
| 全文件**只有一处**置位 bit | `DataFlowPass.cs:978` |
| 该置位只在定义节点分支内执行 | `DataFlowPass.cs:657` |
| 位集 helper 调用点共 8 组 | 见 §3.2 表 |
| `inSets` 在 fixpoint 内**只写不读** | 写：L655；读：L689/698/716（全在循环后） |
| 生产代码从不设置 `DataFlowOptions` | `src/` 中 `DataFlowOptions =` 出现 **0** 次 |
| 既有契约断言 `WordsPerSet == ceil(FlowNodeCount/64)` | `NLCPGPartitionedBuilderTests.cs:275` |

---

## 2. 核心洞察

### 2.1 位宇宙应该是 D，不是 N

`DataFlowPass.cs:978` 是唯一的置位语句：

```csharp
output[nodeOrdinal / 64] |= 1UL << (nodeOrdinal % 64);
```

它只在 L657 成立时执行：

```csharp
if (definitionFactsByOrdinal[nodeOrdinal] is { } definedFact)   // L657
```

因此 **bit 只会落在 `definitionFactsByOrdinal[u] != null` 的序号 u 上**。
其余 `N − D` 个流节点（字面量、二元表达式、条件表达式、调用实参……）
**永远不会有 bit 被置起来**，`IsBitSet`/`EnumerateSetBits` 在它们身上永远查不到东西。

但当前代码为**每个**流节点预留 `ceil(N/64)` 个 word。Terraria 巨型方法里
`D << N`（几万个 operation 中定义语句只占少数），这个浪费就是收益来源。

### 2.2 `D` 与 `definitionFactsByOrdinal` 非空槽位数严格相等

这是压缩**正确性的前提**，必须钉死：

- `D = definitionFactsByNode.Count`（L561 起构造）= 参数定义节点 ∪ 有定义的 operation 节点。
- `definitionFactsByOrdinal`（L629）长度 `flowNodes.Length`；L630–636 把
  `definitionFactsByNode` 的每个 pair 写入其 ordinal 槽位（仅当 key 在 `flowNodeOrdinals` 中）。
- 参数定义节点与 operation 节点**都已被收进 `flowNodes`**（`BuildCfgSensitivePartitionPlan`
  L456–488 逐个 `AddUniqueFlowNode`）。

因此 `definitionFactsByOrdinal` 的非空槽位数 **== D**，压缩到 D 不会丢任何可置位 bit。
反向不成立的部分（`flowNodes` 里存在但无定义的节点）本来就永远不置位。

### 2.3 `inSets` 在 fixpoint 内只写不读

**术语更正（重要）**：L655 并**不是**经典意义的"死存储"——它写入的值在
循环结束后确实被读到（L689/L698/L716）。准确表述是：
**`inSets` 在 fixpoint 内只写不读，其唯一有意义的写是每个节点的最后一次。**

`inSets` 在文件中的**全部**出现（已穷尽检索，共 5 处）：

| 行 | 操作 | 位置 |
| ---: | --- | --- |
| L618 | 分配 | — |
| L655 | **写** `CopyBitSet(inSets, nodeOffset, incomingScratch, wordsPerSet)` | fixpoint 内 |
| L689 | 读 `IsBitSetEmpty` | 循环后 |
| L698 | 读（`TryGetCandidates`） | 循环后 |
| L716 | 读（`EnumerateSetBits`） | 循环后 |

L655 写的值与 `incomingScratch` **逐字相同**（L645 清零、L650 累加前驱
`outSets`，L655 只是把它复制进 `inSets`），紧接着 L656 又把同一个
`incomingScratch` 复制进 `updatedScratch` 做转移与比较。

**为什么"最后一次写"就是收敛解（正确性论证）**：worklist 的不动点性质保证——
若节点 v 的任一前驱 p 的 `out` 集发生变化，p 会把自己的后继（含 v）重新入队
（L673-680），于是 v 会被重新出队并重算 `in` 集。因此在 worklist 清空时，
v 的**最后一次出队必然发生在所有前驱 `out` 集定型之后**，
那次写入的 `inSets[v]` 恰等于 `OR(outSets[p] for p in preds(v))` 收敛值。

所以 fixpoint 期间 `inSets` 从未被读；它在收敛后可由
`inSets[v] = OR(outSets[p] for p in preds(v))` **无损重算**。
这是标准数据流方程——`inSets` 是 `outSets` 的纯函数，无需在迭代中物化整张矩阵。

**重要区分**：`outSets` **不能**这样处理——它是 fixpoint 的迭代状态，
L650 与 L667/L672 都在读它。

**行数不变、位宽变小**：注意 `outSets` 的**行数仍是 N**（因为非定义节点也要
把前驱的 `in` 集原样透传到 `out`，即 L657 不成立时转移为恒等映射），
**只有每行的宽度**从 `ceil(N/64)` 缩到 `ceil(D/64)`。
§3.4 的 `N × ceil(D/64)` 正是这个口径。

---

## 3. 目标设计

### 3.1 位集寻址契约

引入两张映射，替代直接用 `nodeOrdinal` 寻址：

| 名称 | 类型 | 语义 |
| --- | --- | --- |
| `definitionOrdinals` | `int[flowNodes.Length]` | `nodeOrdinal -> definitionOrdinal`；非定义节点为 `-1` |
| `definitionNodes` | `NLCPGNode[D]` | `definitionOrdinal -> 节点`（供反查） |

- `wordsPerSet` 的语义变为 `ceil(D/64)`（不再是 `ceil(N/64)`）。
- **置位**（L978 等价物）：先查 `definitionOrdinals[nodeOrdinal]`；为 `-1` 则不置位。
- **查询/枚举**（L698/L716）：改为按 `definitionNodes` 反查节点，
  `bitCount` 参数由 `flowNodes.Length` 改为 `D`。

`ApplyDefinitionTransfer`（L958）内层循环原本遍历 `output` 的所有置位 bit，
用 `definitionFactsByOrdinal[definitionOrdinal]` 判 `FactsConflict`——压缩后
索引空间与 `definitionFactsByOrdinal`（按定义序数紧凑排列）**天然对齐**，
这部分反而变简单：不再需要 `if (definitionOrdinal < definitionFactsByOrdinal.Count)` 的边界判断。

### 3.2 helper 的改动面（全部调用点已核对）

| helper | 定义行 | 调用行 | 是否需改 |
| --- | ---: | --- | --- |
| `BitSetWordCount` | L890 | L573 | **改**：入参由 `N` 改 `D` |
| `OrBitSet` | L900 | L650 | 否（只搬 word） |
| `CopyBitSet` | L908 | L655, L672 | 否（只搬 word） |
| `BitSetEquals` | L913 | L667 | 否（只比 word） |
| `IsBitSetEmpty` | L926 | L689 | 否（只扫 word） |
| `EnumerateSetBits` | L939 | L716 | **改**：`bitCount` 由 `N` 改 `D` |
| `IsBitSet` | L895 | L226（`AddReachable`） | **改**：寻址改走 `definitionOrdinals` |
| `ApplyDefinitionTransfer` | L958 | L659 | **改**：置位与冲突判定改走定义序数 |

即**只有 4 个函数需要改逻辑**，其余 4 个只搬运/比较 word，与位宽无关。

### 3.3 `inSets` 消除方案

**方案 A（推荐，收益最大）**：完全删除 `inSets` 矩阵，在 fixpoint 收敛后
按需重算。第四阶段只遍历 `plan.OrderedOperations`（L685），所以可以：

1. 删除 L618 的 `inSets` 分配与 L655 的复制（死存储）。
2. 收敛后为**被读到的** operation 节点按需计算 in 集：
   `OR(outSets[p] for p in preds(v))`，复用 `incomingScratch`。
3. 若同一节点被多次读取，用一个小 `Dictionary<int, ulong[]>` 缓存
   （或按 `operationOrdinals` 有序遍历时复用单块 scratch）。

**方案 B（保守，改动小）**：保留 `inSets`，但把行数从 `N` 缩到
`operationNodes.Length`（只有 operation 节点会被读）。收益小于 A，但仍省一半矩阵。

本设计**推荐 A**，因为它与 §2.3 的代数事实一致，且 `inSets` 本来就只是一个
可重算的派生量。A 的风险是"重算引入额外 CPU"，必须在 §6「验证边界」里量化。

### 3.4 组合后的规模

以实测 N=52,743 为例（`inSets`+`outSets` 合计）：

| 口径 | 占用 | 相对现状 |
| --- | ---: | ---: |
| 现状 | 664.0 MB | 1× |
| 仅压到 D=5,000 | 63.6 MB | 10.4× |
| 仅压到 D=1,000 | 12.9 MB | 51.6× |
| 压到 D=1,000 + 去 `inSets` | 6.4 MB | **103×** |
| 压到 D=500 + 去 `inSets` | 3.2 MB | 206× |

四个实测 N 在 D=1,000 + 去 `inSets` 后：

| 方法 N | 现状 | 优化后 | 收益 |
| ---: | ---: | ---: | ---: |
| 52,743 | 664.0 MB | 6.44 MB | 103× |
| 39,076 | 364.3 MB | 4.77 MB | 76× |
| 35,303 | 297.4 MB | 4.31 MB | 69× |
| 28,110 | 188.7 MB | 3.43 MB | 55× |

**口径诚实说明**：本次运行**没有落盘 `DefinitionCount`**，所以真实 `D` 未知，
上表按假定的 D 给出。要把它变成实测结论，必须先落盘该度量
（见执行计划 `2026-09-22-dataflow-bitset-compaction-execution.md` 的 Task 1）。
但方向是确定的：只要 `D < N` 就一定缩；`D = N` 的极端情形无收益也无损失。

### 3.5 顺带修复的两个硬边界

**(a) `MaxDefinitionsPerMethod` 今天封不住内存。** 现状位集大小 `N × ceil(N/64)`
与 D **完全无关**，所以即便设置了 `MaxDefinitionsPerMethod`，内存仍由 N 决定。
压缩后位集大小由 D 完全决定，该预算才真正成为内存阀门：

| D | `ceil(D/64)` | `inSets`+`outSets` |
| ---: | ---: | ---: |
| 512 | 8 | 0.06 MB |
| 1,000 | 16 | 0.24 MB |
| 5,000 | 79 | 6.03 MB |
| 20,000 | 313 | 95.52 MB |
| 50,000 | 782 | 596.62 MB |

**(b) `int` 溢出边界大幅外推。** L618/L619 用 `checked(N * wordsPerSet)`（两者均 `int`）。
现状 `N × ceil(N/64)` 在 **N = 370,704** 时首次超过 `int.MaxValue`，
而 N = 370,703 时单数组已需 **16 GiB**。压缩后（`wordsPerSet = ceil(D/64)`）：

| D | `ceil(D/64)` | N 上限 |
| ---: | ---: | ---: |
| 1,000 | 16 | 134,217,727 |
| 5,000 | 79 | 27,183,337 |
| 20,000 | 313 | 6,860,970 |

即溢出边界从 3.7e5 外推到 6.9e6–1.3e8。**但这不取消加上限的必要性**——
O(N·ceil(D/64)) 仍随 N 线性增长，仍需执行计划的 Task 6 补 N 上限。

---

## 4. 必须正面处理的契约冲突

这三处是本次改动**真实会碰到**的既有约束，不能回避：

### 4.1 `WordsPerSet` 的既有断言会失败（阻断性）

`tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:275`：

```csharp
Assert.Equal((runMetrics.FlowNodeCount + 63) / 64, runMetrics.WordsPerSet);
```

这条断言把"`WordsPerSet` == `ceil(N/64)`"**固化成了契约**，且字段名
`WordsPerSet` 本身语义就是"每组多少 word"，压缩后它变成 `ceil(D/64)`。

**处理方式（本设计的裁定）**：`WordsPerSet` 的语义**改为 `ceil(D/64)`**（即它真正
描述的"每个位集的 word 数"），并把该断言改为
`Assert.Equal((runMetrics.DefinitionCount + 63) / 64, runMetrics.WordsPerSet)`。
理由：字段名描述的是位集宽度，压缩后位集宽度就是由 D 决定；
断言原意是"位集宽度等于位宽计算"，改后仍守住同一意图。

**必须同时**在 `NLCPGDataFlowMethodMetrics` 增加 `WordsPerSetBeforeCompaction`
（或保留等价信息），否则会丢失"优化前会分配多少"的可观测性——这是
`docs/benchmarks/` 诊断报告赖以复现的数据。

### 4.2 性能事实持久化 schema 的兼容性

`WordsPerSet` 会经
`CpgPerformanceFactMapper`（L44）→ `CpgDataFlowMethodPerformanceFact`（L144）
→ `PerformanceSummaryDocument`（L476/L479）落到性能摘要 JSON，
`SchemaVersion = 1`（`PerformanceSummaryDocument.cs:73`），
且 `NLISSN.HostTests/Performance/PerformanceSummaryWriterTests.cs:21` 断言
`schemaVersion == 1`。

**裁定**：`WordsPerSet` 的**字段名、类型、位置不变**，只改其**取值语义**。
因为该字段在 schema 中的含义本就是"位集 word 数"，压缩是内部实现变化；
不升 `schemaVersion`（升版本会破坏 HostTests 的既有断言与下游解析）。
**但必须在字段注释中写明"v1 起该值为 `ceil(DefinitionCount/64)`"**，并在
`设计docs/目前设计/性能分析组件.md` 记录该语义变更，避免报告读者按旧语义解读。

### 4.3 `MaxDefinitionsPerMethod` 从"无效阀门"变成"有效阀门"

压缩后该预算**第一次真正决定内存**（§3.5a）。这意味着：一个此前
"设了也不影响内存"的配置项，压缩后会真的开始限制方法。默认值是
`int.MaxValue`，所以**默认行为不变**；但任何已显式设置该值的调用方
（当前 `src/` 中为 0 处，仅 4 处测试）语义会被放大。

**裁定**：保持默认 `int.MaxValue` 不变，因此默认路径零行为变化。
测试中的 4 处显式设置（`CpgWorkBatchDataFlowTests.cs:95/115`、
`NLCPGPartitionedBuilderTests.cs:660/675/692/710`）需逐个复核其断言是否
依赖"位集与 D 无关"的旧行为。

---

## 5. 不做的事（明确排除）

| 排除项 | 理由 |
| --- | --- |
| **`ArrayPool` 复用位集** | 实测 `Rent` 向上取整到 2 的幂（43,512,975 → 67,108,864，单数组 348→537 MB，**1.54×**），且哨兵测试证明池会长期保留该块。峰值更差。除非用 `ArrayPool.Create(maxArrayLength:)` 限定上限，收益仍不如压缩位宇宙。 |
| 改 `outSets` 为稀疏结构 | `outSets` 是 fixpoint 迭代状态（L650/667/672 读），稀疏化会改变迭代复杂度并可能恶化 CPU。本次不做。 |
| 改 `DominancePass.BlockBitSet` | 它是 O(blocks/64)，量级差 N 倍，与 100 MB 级实例无关。 |
| 升性能摘要 `schemaVersion` | 会破坏 `PerformanceSummaryWriterTests.cs:21` 断言，且本次无字段增减。 |
| 改 `NLCPGDataFlowOptions` 公开形状 | 压缩不需要改选项契约；接配置是**独立**任务（见执行计划 Task 5）。 |

---

## 6. 验证边界

**能证明**：
- 同一输入、同一 SDK/runtime、同一配置下的**图等价**（节点/边/序）。
  既有 oracle：`NLCPGPartitionedBuilderTests` 的
  `BuildFromSource_PartitionedDataFlow_PreservesGraphsAcrossDegreesOfParallelism`（DOP 1/8/12/14/16）、
  `BuildFromSource_PartitionedDataFlow_RepeatedReferencesKeepUniqueDeterministicEdges`、
  `BuildFromSource_PartitionedDataFlow_PreservesComplexMethodLocalFlowShape`、
  `BuildFromSource_DataFlowFactCollection_PreservesGraphAcrossDegreesOfParallelism`、
  `BuildFromSource_PartitionedDataFlow_PreservesCallReturnAndPropertyFlowsAcrossDegreesOfParallelism`，
  以及 `CpgWorkBatchDataFlowTests.BuildFromSource_BatchedDataFlow_PreservesGraphAcrossDegreesOfParallelism`。
- **内存下降**：只有在同一输入、同配置、同运行下做 Type 级 gcdump 对比（`System.UInt64[]`
  的 `TotalBytes`）才可声称固定比例。仅凭 `sizeof` 或算术推算不算证据。

**不能证明 / 需额外测量**：
- 真实 `D` 的分布（本次未落盘 `DefinitionCount`）。**Task 1 必须先补此度量**，
  否则"收益 N/D 倍"只是推算。
- 重算 `inSets` 带来的 CPU 变化。方案 A 增加了收敛后的重算成本，
  必须用 `PerformanceStageId` 的构图耗时对比，不能假定净收益。
- 端到端墙钟改善。位集只是内存墙的一个成因；报告已确认低 CPU 还包含
  长尾串行与 GC 暂停因素，压缩位集不保证墙钟同比改善。

**不得声称**：在没有 class 基线（`docs/harness-verification-matrix.md` 的
`dop-1-1`/`dop-1-12`/`dop-12-1`/`dop-12-12` 固定矩阵）和重复测量的情况下，
声称"内存下降 X 倍"或"性能提升 Y%"。

---

## 7. 风险清单

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| 位序映射写错导致丢 bit（静默错误，图不报错但边变少） | **高** | 图等价 oracle 必须全绿；新增"每个定义节点都可达"的定向断言 |
| `WordsPerSet` 语义变更未同步下游解读 | 中 | §4.1/§4.2 的注释与设计页同步；HostTests 断言保持 |
| 方案 A 重算 `inSets` 引入 CPU 回退 | 中 | 先做 Task 1 度量；性能层对比构图耗时 |
| `D = N` 的退化方法无收益 | 低 | 属预期；逻辑仍需正确 |
| 4 处测试显式设 `MaxDefinitionsPerMethod` 的断言依赖旧行为 | 中 | Task 4 逐个复核 |
| 与在位未提交改动冲突（仓库已有约 60 个改动文件） | 中 | 只碰 `DataFlowPass.cs` + 3 个契约/文档点；提交前 `git diff` 复核 |

---

## 8. 相关文档

- 诊断证据与尺寸反解：`docs/benchmarks/nlissn-version4-dop12-diagnostics.md`
  （「`System.UInt64[]` 是哪个数据结构（已归因）」、「如何优化 `System.UInt64[]`」）
- 峰值样本全类型占比：`docs/benchmarks/data/version4-dop12-type-share-0016.csv`
- 数据流度量落点：`src/NLCPG/Builder/NLCPGBuildMetrics.cs`、
  `src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs`
- 验证矩阵：`docs/harness-verification-matrix.md`
- 执行计划：`docs/plans/2026-09-22-dataflow-bitset-compaction-execution.md`
