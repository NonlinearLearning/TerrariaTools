# 冻结边投影化与常驻宽度收敛设计

**状态：** **已实现**（2026-09-24）。**阶段 1+2+3+4 全部落地并小批量测试验证**；
阶段 3 采用 §3.5.1 的**保序变体**，故 R10 的顺序义务**未发生**、R11 的去重义务**已按 B2 落地**。
整体常驻边字节降幅实测 **92.35%**（原阶段 1+2 时为 26.07%）。
内存有界架构（shard / streaming / persistence）**未启用**。

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| 1 | `CanonicalEdgeStore` 双写 | ✅ 已实现 |
| 2 | 读取路径切投影（`OrdinalEdgeList` / `CsrEdgeTable` / `CreateSnapshotVersion`） | ✅ 已实现 |
| 3 | 释放 `_edges` 与常驻 72 B 数组 | ⬜ **未实现**（见 §3.4 的 B 路径义务） |
| 4 | `Create()` 复制消除（`orderedEdges` 不再物化） | ✅ 已随阶段 1+2 一并完成 |

**实测结果（2026-09-24，受控小批量输入）：**

| 指标 | 实测 | 本文预测 |
| --- | ---: | ---: |
| `sizeof(NLCPGEdge)` | **72 B** | 72 B ✅ |
| `CanonicalEdgeStore` 每边宽度（覆盖率 0） | **9.00 B** | T3 = 9 B ✅ |
| `CanonicalEdgeStore` 每边宽度（有元数据） | **13.01 B** | T2 = 13 B ✅ |
| canonical 常驻分量降幅 | **87.49%** | ~87.5% ✅ |
| 整体常驻降幅（阶段 3 **已完成**） | **92.35%** （阶段 1+2 时曾为 26.07%） | 插入序表 4 B/边取代 80 B/槽 HashSet |

**目标：** 把**冻结后常驻**的边表示从 **72 B/边**（`NLCPGEdge` 内联值）降到
**16 B/边**（序数 + 元数据 id 侧表），在不改变任何图语义、边集合、边顺序、公开
API 形状或持久化格式的前提下，把单文件常驻边占用从实测 **210.06 B/边** 降下来。

**实测达成（2026-09-24，全部 4 个阶段已落地）：** 常驻边表示降为
**13.01 B/边**（无元数据：store 9.00 B + 插入序 4.00 B）/ **17.01 B/边**（有元数据），
受控输入下整体常驻降幅 **92.35%**。

| 项 | 现状（实测） | 目标 | 达成 | 依据 |
| --- | ---: | ---: | ---: | --- |
| `NLCPGEdge` 值宽度 | 72 B | 不常驻 | ✅ 不常驻 | §1.1 实测分解 |
| `HashSet<NLCPGEdge>` 槽 | 80 B | 删除 | ✅ 已删除 | `(959,915,944 − 24) / 80` 整除 ✅ |
| 常驻 canonical 边数组 | 72 B/边 | 0 | ✅ 0 | §3.1 |
| 已清空容器的保留容量 | 72 B/边 | 0 | ✅ 0 | §3.3 |
| **合计常驻（目标口径）** | **210.06 B/边** | **16 B/边** | ✅ **13.01 B/边**（无元数据） | §1.2 / §4 |

**范围：**
- `src/NLCPG/Model/NLCPGGraphIndex.cs` —— `Create()` 的边排布与 `CsrEdgeTable` 接线。
- `src/NLCPG/Model/NLCPGGraph.cs` —— `_edges`、`FreezeQueryIndex()`、`ReclaimConstructionCapacity()`、
  `CreateFrozen()`、`Edges` 属性。
- `src/NLCPG/Model/OrdinalEdgeList.cs` —— 由「读 canonical 数组」改为「按序数现场投影」。
- 新增一个常驻边表类型（§3.1）。

**公开形状不变：** `NLCPGEdge`、`NLCPGNode`、`PendingEdge`、`NLCPGGraphIndex` 的
**公开**成员与语义全部不变；`Edges` 仍返回 `IReadOnlyCollection<NLCPGEdge>`。

**不做的事（本设计的硬边界）：**
1. **不启用内存有界架构。** 即**不**接线 shard / streaming / persistence 路径
   （`NLCPGBuilderOptions.Persistence`、`CpgPersistenceOptions.StreamingMode`、
   `CpgFrozenShardStore`、`RequiresPreallocatedNodeIds()`）。这些代码已存在但在生产中
   从未启用（§7.1）；启用它们会改变图指纹与执行矩阵，属于独立立项，**本设计不涉及**。
2. **不**改 GC 运行时配置（`GCHeapHardLimit`、`GCConserveMemory`、`GCHighMemPercent`）——
   已实测无效或有害（§7.2）。
3. **不**改哈希算法，**不**新增 `InternalsVisibleTo`，**不**改
   `NLCPGEdgeLabel` / `NLCPGContextId` / `NLCPGCallSiteContext` 的公开定义。
4. **不**改持久化格式（shard、routing sidecar、JSON 导出、`SnapshotVersion` 语义）。
5. **不**动节点侧（节点已序数化投影，见 §2）。

**设计依据：** 本设计是
[边载荷序数化与稀疏属性侧表设计](2026-09-23-edge-payload-ordinalization-design.md)
（下称**前序设计**）的**自然续篇**。前序设计解决的是**构图期**的
`PendingEdgeBuffer`（120 B → 16 B，已实现，−87.7%）；本设计解决的是**冻结后常驻期**
的边表示（72 B → 16 B）。两者作用于**同一条流水线的相邻两段**，不重叠、不冲突：
前序设计的产物（`PendingEdgeKey` 16 B）恰是本设计的**输入形态**。

---

## 0. 结论先行

1. **表示层缺陷，不是调参缺陷。** 边的「去重形态」和「查询形态」被绑成同一个
   72 B 值结构体。仓库在构图期已经用 16 B 的 `PendingEdgeKey` 去重，却在 freeze 时
   把 16 B **重新膨胀成 72 B** 并常驻——恰好在内存最紧的时刻丢掉 interning。
2. **72 B 中 56 B 是可证从不填充的元数据**（§1.1）。真实语料里
   `NLCPGCallSiteContext` 在**全部 69 份 gcdump 报告中实例数为 0**。
3. **同一批边被存了两遍**：`HashSet<NLCPGEdge>` 与 canonical `NLCPGEdge[]`，
   实测复制系数 **2.91×**（§1.2）。
4. **峰值由常驻「宽度」决定，不由瞬态「高度」决定。** 这解释了为什么既有瞬态优化
   （P0#1，瞬态 −80.4%）在同时刻只把 LOH 降 **−2.1%**：天花板是常驻宽度设的。
5. **节点侧已有正确先例。** `OrdinalNodeList` 已经是「延迟投影、不复制完整数组」
   （`设计docs/目前设计/cpg-architecture.md:21-25`）。本设计只是把**同一条已验证模式
   从节点推广到边**。

---

## 1. 现状：常驻态的边表示

### 1.1 `NLCPGEdge` 的 72 B 逐字段分解（实测）

`src/NLCPG/Model/NLCPGEdge.cs:6` 声明为 `public readonly record struct NLCPGEdge`：

| 字段 | 类型 | 字节 | 生产环境是否填充 |
| --- | --- | ---: | --- |
| `SourceNodeId` | `NodeId`（`record struct(uint)`） | 4 | ✅ 恒定 |
| `TargetNodeId` | `NodeId` | 4 | ✅ 恒定 |
| `Kind` | `NLCPGEdgeKind`（36 个取值） | 4 | ✅ 恒定 |
| `StructuredLabel` | `NLCPGEdgeLabel?`（引用） | 8 | ⚠️ 稀疏 |
| `ContextId` | `Nullable<NLCPGContextId>`（`record struct(string)`） | 16 | ❌ 见下 |
| `CallSiteContext` | `Nullable<NLCPGCallSiteContext>`（`record struct(string,int,int,string)`） | 32 | ❌ **0 实例** |
| 对齐填充 | | 4 | — |
| **合计** | | **72** | **56 B = 77.8% 为元数据位** |

> 该 72 B 与既有独立验算一致（`PEAK-MEMORY-REPORT.md:124`：
> `4 + 4 + 4 + 8 + 16 + 32 = 72` ✅）。

**元数据覆盖实测：**

- `NLCPGCallSiteContext`：**全部 69 份 gcdump 报告中实例数为 0**
  （`GcdumpReport\*.report.txt` 中只有 `GenericEqualityComparer<...>` 等比较器单例，
  无任何实例）。
- `NLCPGEdgeLabel`：峰值 **17,026 实例**
  （`Analysis\GcdumpTypeStats\gcdump-type-totals-per-sample.csv`，样本
  `0069-late-2475s`），而 NPC.cs 单文件边数为 20,723,806 ⇒ 覆盖 **0.08%**。
- 前序独立证据：`Build\verify-algo-speed\IMPLEMENTATION-EVIDENCE.md:67` 用对拍 oracle
  实测**真实单文件语料边元数据覆盖 = 0.0%**，并据此为 `BuildMetadataRanks`
  加了零元数据快速路径。

⚠️ **诚实边界**：以上覆盖数据来自**单文件语料**。元数据非 0 的场景需要多文件联合编译
并配置 `ICallFlowResolver`（`IMPLEMENTATION-EVIDENCE.md:77-79`）。因此本设计的
**正确性不依赖**「元数据恒为 0」，只有**收益上限**依赖它；§3.3 给出元数据存在时的退化路径。

### 1.2 实测账单（Projectile.cs，样本 `0087-late-4863s`）

样本 `0087` 正在构建 Projectile.cs（`per-file-cpg-cost.csv`：Edges = **6,953,008**）。
该样本的 gcdump 报告给出两个巨型数组：

| 组件 | 实测字节 | 每边 | 说明 |
| --- | ---: | ---: | --- |
| `Entry<NLCPG.Model.NLCPGEdge>[]` | **959,915,944** | 138.06 | `HashSet<NLCPGEdge>` 的 entry 数组 |
| `NLCPG.Model.NLCPGEdge[]` | **500,616,600** | 72.00 | `NLCPGGraphIndex.CanonicalEdges` |
| **合计** | **1,460,532,544** | **210.06** | = **1,392.9 MiB** |
| 纯端点信息（`src`+`tgt`+`kind` = 12 B） | 83,436,096 | 12.00 | = 79.6 MiB |
| **放大倍数** | | | **17.5×** |

**槽几何已闭合（本轮新增）**：
`(959,915,944 − 24) / 80 = 11,998,949` **整除** ⇒ `Entry<NLCPGEdge>` = **80 B**，
与 `hashCode(4) + next(4) + payload(72) = 80` **精确吻合**；
`NLCPGEdge[]` 为 `24 + 72 × 6,953,008 = 500,616,600` **精确吻合**。
两处**残差均为 0 B**。

⚠️ **一处未解释的观测**：槽数 11,998,949 是存储边数 6,953,008 的 **1.73×**。
一个**候选解释**（未证实）是 `NLCPGGraph.cs:822` 的 `_edges.Clear()` 只把 `Count`
归零、**保留峰值容量**，随后 `:823-826` 重新 add 时不再缩容。
本设计**不依赖**该解释：无论槽数为何，摊销必然随 payload 从 72 B 降到 4 B 而同步下降。

---

## 2. 第一原因

> **72 字节定宽边值同时充当「去重单位」和「查询单位」，并在 ≥2 个常驻容器里被复制，
> 而这 72 字节里有 56 字节可证从不被填充。**

流水线两段的形态对照，说明 interning 是在哪里被丢掉的：

| 阶段 | 表示 | 每边 | 状态 |
| --- | --- | ---: | --- |
| 构图期 `PendingEdgeBuffer` | `PendingEdgeKey`（元数据 intern 成 4 B id） | **16 B** | ✅ 前序设计已实现 |
| `FreezeQueryIndex` → 常驻 | `NLCPGEdge`（元数据内联展开） | **72 B** | ❌ 本设计要修 |

**复制链**（同一批边的常驻副本）：

```text
FreezeQueryIndex()                      NLCPGGraph.cs:435-449
  AssignDeterministicNodeIds()          :757-827
    remappedEdges = new NLCPGEdge[Count]     :798      ← 副本 ①（瞬时，已由既有改动避免 PendingEdge[272 B]）
    _edges.Clear(); _edges.Add(...)          :822-826  ← 副本 ②（常驻 HashSet，二次去重）
  NLCPGGraphIndex.Create(_nodesByNodeId.Values, _edges)   :443
    var edgeArray = edges as NLCPGEdge[] ?? edges.ToArray()  :314   ← 副本 ③（瞬时；HashSet 走 ToArray）
    var orderedEdges = new NLCPGEdge[edgeArray.Length]       :363   ← 副本 ④（常驻 canonical）
```

⇒ **常驻两份**（② + ④ = 210.06 B/边），**瞬态叠加两份**（① + ③ = 144 B/边）。
瞬态叠加正是 `Create()` 内部峰值的一部分（§4.3）。

**为什么这解释了既有测量结果：** 既有报告 §4quater 实测瞬态优化 P0#1 使同时刻
瞬态 −80.4%，而 LOH 只降 −2.1%，`System.String` 基本不动
（1,454.4 → 1,448.4 MB）。LOH 时间序列呈**锯齿**并回到 1,697 MB 地板，说明瞬态确实
完全释放。**瞬态只决定每个锯齿的高度，常驻决定基线**——而 committed 里留下的是基线。

**为什么节点侧不是问题：** `OrdinalNodeList`（`src/NLCPG/Model/OrdinalNodeList.cs`）
已是「只存序数、延迟投影、不复制完整 `NLCPGNode` 数组」，并在
`设计docs/目前设计/cpg-architecture.md:21-25` 作为当前设计记录。

> **注（2026-09-24）**：本设计落地后，边侧**已有对应物**，该架构文档也已同步更新
> （见 [最小 CPG 架构](../CodeDesign/目前设计/cpg-architecture.md)）。本节保留原状，
> 用于记录**改造前**的差距。
>
> ⚠️ **`设计docs/` 被 `.gitignore:10` 忽略**，故该同步**不进入版本控制**，
> 只存在于本机工作区。

---

## 3. 设计

### 3.1 常驻边表：SoA 序数表示（实测 9~13 B/边）

新增 `src/NLCPG/Model/CanonicalEdgeStore.cs`（内部类型）：

```csharp
// 常驻边存储：结构化数组（SoA），实测 9 B/边（覆盖率 0）~13 B/边（有元数据），
// 取代 72 B 的 NLCPGEdge 值数组。
// 与 OrdinalNodeList 同构：只存序数，查询时按需投影为 NLCPGEdge。
internal sealed class CanonicalEdgeStore
{
    private readonly int[] _sourceOrdinals;   // 4 B/边
    private readonly int[] _targetOrdinals;   // 4 B/边
    private readonly byte[] _kinds;           // 1 B/边（NLCPGEdgeKind 共 36 个取值）
    private readonly int[]? _metadataIds;     // 4 B/边；覆盖率 0 时为 null
    private readonly EdgeMetadataTable? _metadata;
    private readonly NLCPGNode[] _orderedNodes;
    // 三元打包 4+4+1 = 9 B，加 metadataIds 4 B = 13 B；
    // 为对齐与遍历简单，T1 口径按 16 B/边落账（§4.1 给出 13/9 B 档）。
}
```

要点：

- **序数而非 `NodeId`**：canonical 序数即 `orderedNodes` 下标，`NodeId` 可由
  `orderedNodes[ordinal].NodeId!.Value` 取回，比存 4 B `NodeId` **等价且不增宽**，
  同时天然复用于 CSR offset 表（`BuildKeyOffsets` / `BuildOrdinals` 已按序数工作）。
- `Kind` 用 `byte`：`NLCPGEdgeKind` 实测 **36** 个取值（`kindWidth = 36`），
  1 B 足够。⚠️ 前序设计的否决表记录了「`ushort`/`byte` 缩 kind 单独缩**省 0**」
  （因对齐回填）——**该结论在 72 B 值结构体内成立，在 SoA 下不成立**：
  独立 `byte[]` 数组无回填问题。这是 SoA 相对 AoS 的**结构性收益**。
- `_metadataIds` 可空：覆盖率探测为 0 时**整条数组不分配**，退化为 12 B/边（§3.3）。

### 3.2 投影层：`OrdinalEdgeList` 改造

现 `OrdinalEdgeList.cs:21` 为：

```csharp
public NLCPGEdge this[int index] => _edges[_ordinals[_offset + index]];
```

改为从 `CanonicalEdgeStore` 现场投影（`NLCPGEdge` 是**值类型**，投影**零堆分配**）：

```csharp
public NLCPGEdge this[int index] => _store.Project(_ordinals[_offset + index]);
```

**所有下游消费者零改动**，因为它们只经 `IReadOnlyList<NLCPGEdge>` 接口访问
（`CsrEdgeTable.Get` / `Get(id, kind)` / `NLCPGGraphIndex.GetEdges(kind)` 均已返回
`OrdinalEdgeList` 或 `ArraySegment`）。`CsrEdgeTable.Slice`
（`NLCPGGraphIndex.cs:84-89`）已返回**视图而非副本**，无需改。

### 3.3 元数据池上移

`PendingEdgeBuffer` 的 `_metadataById` 表（`NLCPGGraph.cs:908`，当前 id 0 = `null` 哨兵）
在 freeze 时**转移到 `CanonicalEdgeStore` 而非释放**（`Release()` 目前清空它，
`NLCPGGraph.cs:947-952`）。

- 元数据覆盖率 0 ⇒ `_metadataIds = null`，投影时三个字段传 `null`。
- 元数据非 0 ⇒ 保留 4 B/边 + 池（前序设计实测 `f = 6.25%` 时池仅 **4.5 MiB**）。
- **正确性不依赖覆盖率**：元数据 id 留在边记录内，去重语义与旧实现逐字段等价
  （沿用前序设计的结论：id 参与哈希与相等性）。

### 3.4 两条去重路径必须分离处理（本设计唯一的语义风险点）

`_edges` 是 `HashSet<NLCPGEdge>`（`NLCPGGraph.cs:19`），它**同时**承担去重职责。
它有**两个**写入路径，语义不同：

| 路径 | 入口 | 去重来源 | 处理 |
| --- | --- | --- | --- |
| A. 正常冻结 | `FreezeQueryIndex()` → `:822-826` | **冗余**：`_pendingEdges._keys`（`HashSet<PendingEdgeKey>`，`:909`）已按 `(src, tgt, kind, metadataId)` 去重 | **可省** `_edges`，直接从 `_keys` 投影 |
| B. 直接构造冻结图 | `CreateFrozen()` → `:99-108` | **唯一来源**：该路径**不经过** `PendingEdgeBuffer` | **必须保留一次去重** |

**路径 A 的等价性论证**：`PendingEdgeKey` 的 `MetadataId` 由 `InternMetadata`
（`:965-983`）按**内容**去重（`EdgeMetadata` 为 `record class`，按值相等），
故 `_keys` 的去重粒度 = `(src, tgt, kind, 元数据内容)`，与
`NLCPGEdge` 的 `HashSet` 去重粒度**逐字段相同**。且前序设计已实测
「add-only 的 `HashSet`/`Dictionary` **枚举序 == 插入序**且跨进程稳定」
（`2026-09-23-edge-payload-ordinalization-design.md:401-443`）。

**路径 B 的实现选项**（实施时二选一，见执行文档阶段 3）：

- B1：`CreateFrozen` 内部临时建一个 `PendingEdgeBuffer`，或
- B2：保留一个**临时** `HashSet<PendingEdgeKey>` 仅用于去重，投影后立即丢弃。

两者都不引入常驻 72 B 数组。

### 3.5 `Edges` 属性的替代

`Edges`（`NLCPGGraph.cs:46`）当前 `=> _edges`。改为返回一个仅实现
`Count` + `GetEnumerator` 的投影集合（`IReadOnlyCollection<NLCPGEdge>`）。
已核实的消费模式只有：`.Count`、`foreach`、`.Where/.Select/.SelectMany`、
`UnionWith`、`ToArray`、`.Any`——**全部由投影集合支持**。
`Edges` 的 post-freeze 读取方（`CpgBuildInventory.cs:24`、`NLCPGBuilder.cs:426`、
`ProjectJsonExporter.cs`、`CpgGraphValidator.cs:51/181`、
`NLCPGStructureViewBuilder.cs:141`、`DecisionModel.cs:550`、`RulePipeline.cs:84` 等）
**全部在 freeze 之后**，见 §5 的顺序论证。

> **范围更正（2026-09-24 审计）**：`DecisionModel.cs:550`、`RulePipeline.cs:84`、
> `MarkLiftingEngine.cs:166` 的接收者是 `CompiledRuleStructureContractGraph`，
> **不是** `NLCPGGraph`；`AnalysisEvidence.cs:153` 亦为其自身的 `AnalysisEvidenceGraph`。
> 这三处与本改动**无关**。

#### 3.5.1 阶段 3 的低风险变体（**已按此实现**）

阶段 3 有一个**不改变 `Edges` 枚举序**的落地方式，可绕开 §8 R10 的顺序义务：

- 保留 `Edges` 的**现有插入序语义**，但不再由 `HashSet<NLCPGEdge>` 承载，而是由
  **插入序 `int[]` 序数数组** + `CanonicalEdgeStore` 投影提供（`_edges.Keys` 的枚举序
  在冻结时即等于插入序）。
- 去重仍只做一次：由 `PendingEdgeBuffer._keys`（路径 A）/
  `CreateFrozen` 内的一次去重（路径 B）承担，见 §3.4。

这样 `CpgInterproceduralEdgeOrderTests`（顺序敏感）与
`PendingEdgeOrdinalizationDeterminismTests`（声明测插入序）**都无需改动**，
而被删除的只是 `HashSet` 的 80 B/槽开销——这恰是当前常驻占用的主导项。
若该变体落地后仍需 canonical 序，再把顺序切换作为**独立一步**处理。

---

## 4. 收益预估

### 4.1 每边占用

| 口径 | 常驻/边 | 相对现状 |
| --- | ---: | ---: |
| 现状（实测） | **210.06 B** | — |
| T1：4×int（src/tgt/kind/metadataId） | 16 B | **−92.4%** |
| T2：3×int + 1×byte + 1×int | 13 B | −93.8% |
| T3：T2 且覆盖率 0（省 metadataIds） | 9 B | −95.7% |

### 4.2 外推（**模型推算，非实测**）

> ⚠️ **本节是设计期的模型推算，已被 §4.1 的实测取代，保留作为推算方法记录。**
> 下表按"仅换 canonical 存储（阶段 1+2）"的口径推算；阶段 3 又删掉了 80 B/槽的
> `HashSet`，故**实际收益大于下表**。以实测整体降幅 **92.35%** 为准。

| 规模 | 边数 | 当前常驻 | T1 目标 | 节省 |
| --- | ---: | ---: | ---: | ---: |
| Projectile.cs（实测基准） | 6,953,008 | 1,392.9 MiB | 106.1 MiB | **−1,286.8 MiB** |
| NPC.cs（×2.98 外推） | 20,723,806 | 4,151.5 MiB | 316.2 MiB | **−3.75 GiB** |

NPC.cs 当前常驻边占用占本机 **13.86 GiB** 上限的 **29.3%**（单一文件、单一容器对）。
按实测 92.35% 的整体降幅口径，NPC.cs 的常驻边占用将由约 4,151.5 MiB 降至约 **318 MiB**
（**仍为外推，不是实测**；端到端峰值未测）。

### 4.3 瞬态收益

`Create()` 内 `edges.ToArray()`（`:314`，副本 ③）+ `orderedEdges`（`:363`，副本 ④）
在旧实现下**同时存活**，NPC.cs 规模各约 **1,423 MB**。
改为直接从 `_keys` 投影后，副本 ③④ **均消失**，瞬态峰值再降同等量级。
**阶段 3 另有额外瞬态收益**：`AssignDeterministicNodeIds` 原先还要物化一个
`NLCPGEdge[]`（72 B/边）再逐条 `Add` 进常驻 `_edges`；现在直接填充局部去重集，
该中间数组（NPC.cs 规模约 1,423 MB）**也不再产生**。

> 这与既有的「合并 `Create()` 两次 72 B 数组为原地置换」提案**不同**：那个方案
> 保留一份 72 B 常驻数组（省约 1,423 MB 峰值），本设计**取消 72 B 常驻数组本身**。
> 若本设计落地，原地置换提案**自然作废**（成为其真子集）。

### 4.4 构建时间

`FreezeQueryIndex` 占全部 CPG 构建 CPU 时间的 **80%**：
`per-file-freeze-index.csv` 去重后 `sum(FreezeMs) = 2,937,333 ms = 49.0 min`，
而 `sum(BuildMs) = 3,677,472 ms = 61.3 min`。该阶段的主要工作就是上述复制与哈希，
故预计**同步下降**（**未实测**，不作为承诺）。

⚠️ **数据口径警告**：`per-file-freeze-index.csv` 与 `per-file-passes-long.csv`
**把每个文件记录两遍**（962 个文件却有 1,934 行；958/962 对的 `Ms` 完全相同）。
本节的 49.0 min 是**去重后**的值。另有 4 对 `Ms` **不同**，说明某处确实存在真实
双重调用，未定位（不跑运行无法判定）。

---

## 5. 为什么语义不变

1. **边集合与边顺序不变**：canonical 顺序仍由同一段 LSD 基数排序决定
   （`NLCPGGraphIndex.cs:343-372`，键序 `source > kind > target > metadataRank`）。
   `_keys` 的插入序已验证 == 枚举序，故从 `_keys` 投影得到的集合与从
   `_edges` 得到的**逐元素相同**。
2. **去重粒度不变**：见 §3.4 路径 A 的论证。
3. **`SnapshotVersion` 不变**：`CreateSnapshotVersion`（`:831-862`）只读
   `edge.SourceNodeId / Kind / TargetNodeId / StructuredLabel?.StableKey /
   ContextId?.Value / CallSiteContext?.*` —— 全部是**投影可精确重建**的字段，
   且 `AppendString` 的零分配改写已就位（`:404-442`）。
4. **`NodeId` 语义不变**：仍由 `AssignDeterministicNodeIds`（`:757-827`）分配；
   边仍引用 `NodeId`，只是**不再常驻存储**。
5. **公开 API 不变**：`NLCPGEdge` 仍是公开 `readonly record struct`，
   `Edges` 仍返回 `IReadOnlyCollection<NLCPGEdge>`。
6. **顺序安全性（关键）**：**没有任何 pass 在 freeze 之后读 `.Edges`。**
   `NLCPGBuilder.cs:381-387` 跑完全部 7 个 optional pass，`:390` 才调用
   `FreezeQueryIndex()`。builder 内部的 `.Edges` 读取只在
   `ControlFlowPass.cs:73/126`、`DominancePass.cs:437`、`ControlDependencePass.cs:126`
   —— 全在 freeze **之前**，走 `localGraph`（可变面），不受影响。

---

## 6. 不变量（实施期必须保持）

| # | 不变量 | 违反后果 |
| --- | --- | --- |
| I1 | canonical 边序在改动前后**逐元素相同** | 指纹变化、下游规则输入变化 |
| I2 | `Count` == 去重后边数（现有 `CurrentEdgeCount`，`NLCPGGraph.cs:48`） | 边数统计错误 |
| I3 | 投影出的 `NLCPGEdge` 四个可见字段与旧实现**逐字段相同** | `SnapshotVersion` 变化 |
| I4 | `OrderedEdges` 与 `EdgeStore` 投影结果**逐元素一致** | `CanonicalEdgeList` 的 offset/count 算错则边丢失 |
| I5 | `CreateFrozen` 入口仍**完成一次去重** | 路径 B 出现重复边 |
| I6 | `Edges` 的 post-freeze 读取不返回已释放数据 | 空图或抛错（现有 `ThrowIfReleased` 语义） |
| I7 | `NodesByKind` / `NodesByFilePath` 仍存 `int[]` 序数 | `NLCPGGraphIndexStorageContractTests:52-63` 变红 |

---

## 7. 备选方案与否决理由

### 7.1 「直接启用已有的分片/流式路径」（**本设计明确不做**）

仓库里**内存有界的架构已经写好，只是从未接线**：

| 事实 | 锚点 |
| --- | --- |
| `Persistence = null` 默认，且全 `src/` **从未赋值** | `NLCPGBuilderOptions.cs:32` |
| `StreamingMode = false` 默认 | `NLCPGBuilderOptions.cs:94` |
| `CpgFrozenShardStore` 生产**零调用者** | 全 `src/` 扫描 |
| `RequiresPreallocatedNodeIds()` 恒 `false` | `NLCPGBuilder.cs:450-453` |
| 所有 `new CpgPersistenceOptions` 均在**测试**中 | `tests/**` |

即：**生产跑的是唯一会把整图驻留内存的那种配置。**

**为什么本设计不做**：启用它会改变图指纹、执行矩阵、持久化产物与并发语义，是一个
**架构级独立立项**，风险与验证成本远高于表示层改动。本设计选择在**不改变任何
可观测行为**的前提下消除常驻宽度——这是更小、更可验证的一步，且**与之不冲突**
（将来启用流式路径时，16 B 边表同样是更好的载体）。

### 7.2 明确否决（附实测/来源）

| 方案 | 否决理由 |
| --- | --- |
| `GCHeapHardLimit` | 官方定义是「GC 堆的最大**提交**大小」，超限走 OOM 或更频繁 GC，不会让必要活数据变小。姊妹运行 `20260923-130737` 死于 `0x80131506`（`COR_E_EXECUTIONENGINE`） |
| `GCConserveMemory` / `GCHighMemPercent` | **已实测无效**；且 GC 暂停占空比已达 **45.39%**，进一步换页代价不可接受 |
| LOH 压缩 | 碎片仅 **991 MB**，而 committed **18,558 MB** |
| 降并发 | **已实测无效**（真实并发 ≈ 0.70；`threadsRunning` = 1 占 71.8% 样本） |
| 减少活对象 | 活堆仅 **471.7 MB**，占 committed **2.5%** |
| 压缩瞬态峰值 | **本设计要纠正的原始思路**：天花板由常驻**宽度**决定（§2） |
| 仅删 `_edges`（不换表示） | 只省一份 72 B，其余两份仍在；且 20+ 消费者需等价性证明，风险/收益比劣于本设计 |
| `NLCPGNode` 瘦身 | 实测仅约 **179 MB**，非主要杠杆，且改变指纹 |
| `FrozenDictionary` 等 | 前序设计已实测为**回归**（104 B 键下 254.6 → 401.8 B/条） |

---

## 8. 风险与未验证边界

| # | 项 | 状态 |
| --- | --- | --- |
| R1 | **`_edges` 消除的等价性** | **已澄清 + 已落地**（2026-09-24）。16 处 `.Edges` 消费者全部检查：count-only、显式排序，或 `UnionWith` 进自身 HashSet ⇒ **无消费者依赖 HashSet 语义或去重**。顺序义务经 §3.5.1 保序变体消解（`CpgInterproceduralEdgeOrderTests` 非单调基线零改动通过），路径 B 去重按 B2 落地 ⇒ 见 R10/R11 |
| R2 | `Edges` 投影集合的**枚举性能** | **部分验证**。`InsertionOrderedEdges` 视图已缓存（每次访问不再新建），随机访问为 3 次数组读 + 1 次结构体构造（`NLCPGEdge` 是 `readonly record struct` ⇒ 零堆分配）。**遍历/查询吞吐未单独对拍** ⇒ 仍标记为未完全验证 |
| R3 | NPC.cs 的 **−3.75 GiB** | **模型外推，非实测**。基准是 Projectile.cs 的实测 210.06 B/边 × 边数比例。该数按阶段 1+2 口径推算；阶段 3 完成后按实测 92.35% 口径应更大，**但仍未端到端实测** |
| R4 | T1/T2/T3 口径选择 | **已定**：实测覆盖率 0 时 **9.00 B/边**（T3），有元数据时 **13.01 B/边**（T2） |
| R5 | `CreateFrozen` 路径 B 的去重实现 | **已落地（取 B2）**：`CpgFrozenShardGraphReader.cs:209` 传 `shard.Edges.Select(...).ToArray()`（**未去重**），故 `CreateFrozen` 内保留一次局部 `HashSet<NLCPGEdge>` 去重，语义与原先的常驻 `_edges` 去重一致，索引建好后不再常驻。另 3 个 `CreateFrozen` 调用方与唯一 `ImportMutableFacts` 调用方（`NLCPGBuilder.cs:307`）均已预去重 |
| R6 | 槽数 1.73× 现象 | **未解释**（§1.2）。不影响本设计收益方向，但说明 `_edges` 曾达更大容量 |
| R7 | freeze 阶段 CPU 下降幅度 | 未实测。80% 占比是实测，**下降幅度**不是 |
| R8 | 遥测文件重复计数 | `per-file-*.csv` 每文件记两遍，**4 对 `Ms` 不同**，真实双重调用未定位 |
| R9 | `NLCPGBuilder.cs:833` 注释过期 | 仍写 **"11.3%"**，正确值 **11.08%**。与本设计无关，可顺手修正 |
| **R10** | **阶段 3 会改变 `Edges` 枚举序（新增，2026-09-24 审计）** | 若阶段 3 让 `Edges` 改走 canonical 序，则有 **1 个确定性失败**：`CpgInterproceduralEdgeOrderTests.cs:141-150` 用**不排序**的 73 元素**非单调**基线（`:62-138`，含 `273,277,288,284,286`）对拍 `DescribeInterproceduralBridgeOrder`（`:200-228`）。另有 1 个语义失效：`PendingEdgeOrdinalizationDeterminismTests.cs:89-97` 声明测"插入序"，改序后会变成同义反复（两侧同步位移，仍通过但不再测原命题）。**本轮采用 §3.5.1 保序变体 ⇒ 该风险未发生**；两个测试均零改动通过 |
| **R11** | **阶段 3 的去重义务（新增）** | 见 R5：`CreateFrozen` 必须**保留一次去重**（B1/B2），否则 `CpgFrozenShardGraphReader.cs:209` 路径的重复边会进入 canonical 存储，改变 `Count` / `SnapshotVersion`。**本轮按 B2 落地**：去重移入 `CreateFrozen` 的局部 `HashSet<NLCPGEdge>`，索引建好后不再常驻 |
| **R12** | **已修正的错误断言（本轮）** | 本文先前把 I4 写成「`NLCPGGraphIndexStorageContractTests:34` 的 `Assert.Same` 守卫边数组」——**该行实际断言的是节点**（`orderedNodes`/`canonicalNodes`）。I4 已改写为「`OrderedEdges` 与 `EdgeStore` 投影一致」，并已在本轮新增测试中覆盖 |

**诚实声明（2026-09-24，本轮执行后更新）：**

1. **本轮已实现阶段 1+2+3+4**：`src/NLCPG/Model/CanonicalEdgeStore.cs`（新增）、`NLCPGGraphIndex.cs`、
   `OrdinalEdgeList.cs`、`NLCPGEdge.cs`（新增 `CreateProjected`）、`NLCPGGraph.cs`（删除常驻 `_edges`）。
   **阶段 3 已执行**，常驻 72 B 数组与 80 B/槽 HashSet 均不再存在；整体常驻边字节降幅实测 **92.35%**。
2. **本轮已跑的小批量测试（全部通过）**：
   - `NLCPGEdgeStoragePerformanceTests` **3/3**（含整体降幅 92.35% 的新断言）
   - `FrozenEdgeProjectionEquivalenceTests` **7/7**
   - 顺序敏感关键回归 8 个测试类 **79/79**（含 `CpgInterproceduralEdgeOrderTests` 非单调基线）
   - `UnitTests` 全量 **117/117**
   - `NLCPGNodeStoragePerformanceTests` **2/2**（4,481 边经 Export/Restore 往返过新存储）
   - `ContractTests` 全量 **499/500**，唯一失败为 peer 改动 `NLISSN.csproj` 引入的
     `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
     （该测试只读 `.csproj`；本改动只碰 `.cs`，且未新增任何项目引用）
   - `HostTests` 全量 **327/330**，3 个失败（`PipelineComponentTests` ×2、
     `PropagationRuleExpansionTests` ×1）**均为 `Context/progress.md:47-53` 已记录的既有失败**
     （peer 已在 HEAD 基线上逐条证实同样失败）⇒ **零新增失败**
3. **§1.2 的字节数是实测**（gcdump 原始值，两处残差 0 B）；
   §4.2 的 NPC.cs 与 §4.4 的时间收益是**推算**，已逐处标注。
   §4.1 的 9/13 B 每边现已由受控输入**实测确认**。
4. **端到端峰值收益未测得**：本机 RAM 仅 13.86 GiB 且与 peer 共享，
   基线运行在 t≈4,740 s 进入换页平台期。验收口径优先采用**容器级字节**，
   端到端峰值为**加分项而非门槛**。
5. 本文**不再重复**前序设计的构图期结论；两者是相邻段落，引用而非复制。

---

## 9. 参考

- [边载荷序数化与稀疏属性侧表设计](2026-09-23-edge-payload-ordinalization-design.md)
  —— **前序设计**：构图期 120 B → 16 B（已实现，−87.7%）
- [边载荷序数化与稀疏侧表执行](2026-09-23-edge-payload-ordinalization-execution.md)
  —— 前序执行分解
- [Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)
  —— 事实基础与 9 项否决
- [冻结边投影化执行计划](2026-09-24-frozen-edge-projection-execution.md)
  —— 本设计的执行分解
- [最小 CPG 架构](../CodeDesign/目前设计/cpg-architecture.md)
  —— 节点侧序数化投影（`OrdinalNodeList`）的当前设计记录
- `src/NLCPG/Model/NLCPGGraphIndex.cs`、`NLCPGGraph.cs`、`OrdinalEdgeList.cs`
- `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexStorageContractTests.cs`
  —— 承载「单一 canonical 数组 + 序数桶」契约的既有测试（I4/I7 的归属地）
