# 边载荷序数化与稀疏属性侧表设计

**状态：** **已实现并验证**（2026-09-23）。见本文 §5.4「实施结果」。

**目标：** 把 `NLCPGGraph.PendingEdgeBuffer` 的边去重载荷从 **120 B/条**降到
**16 B/条**，在不改变任何图语义、边集合、边顺序或持久化格式的前提下
把该子系统的稳态占用从 **626.3 MiB** 降到 **77.3 MiB**（**−87.7%**）。

| 项 | 现状 | 目标 | **实测（已实现）** |
| --- | ---: | ---: | ---: |
| 主记录载荷 | 120 B | 12 B | **16 B**（2×int 序数 + kind + int 元数据 id） |
| 去重槽 | 136 B（`Dictionary`） | 24 B | **24 B** ✅ |
| 含 `_buckets` 的每槽成本 | 140 B | 28 B | **28 B** ✅ |
| `List` 第二份副本 | 120 B/条 | 删除 | **已删除** ✅ |
| 元数据（label/ctx/callsite） | 每条固定占 56 B | 稀疏 | **按内容去重池 + 4 B id** ✅ |
| 两容器合计 | 656,713,196 B | — | **81,011,020 B（−87.7%）** ✅ |

> **基线口径更正（2026-09-23 实测）**：本文初版把基线写成
> `HashSet<PendingEdgeKey>`（`P × 128 B` = 370,335,896 B，合计 633,567,180 B）。
> 实测 dump 显示基线跑的是 **`Dictionary<PendingEdgeKey,int>`**：
> `Entry[]` = **393,481,888 B = `2,893,249 × 136 + 24`**，
> 故真实基线是 **656,713,196 B**（626.3 MiB），比初版高 **23.1 MB**。
> 收益百分比由 −87.2% 修正为 **−87.7%**（分母变大）。


> **实施口径修正（重要）**：初版计划是「两阶段」——先做变体 3b（24 B 键，元数据留在键内），
> 再用 G4 测量结果决定是否切 3a（12 B 键 + 稀疏侧表）。
> **实际实现采用了更优的单阶段方案**：键 = `(int 源序数, int 目标序数, kind, int 元数据id)`，
> 元数据存进**按内容去重的池**。该方案：
> 1. **与 3a 同尺寸**（28 B/槽），却不依赖 §5.1 的未证前提（元数据由 `kind` 唯一决定）；
> 2. **只需一个容器**，去重语义与旧实现**逐字段等价**；
> 3. **不需要 G4 门禁**，也不需要 3b→3a 的二次切换。
>
> 代价是新增元数据池，实测在 `f=6.25%` 下仅 **4.5 MiB**（`EdgeMetadata` 用 `record class`
> 而非 `record struct`；后者会把 32 B 载荷内联进字典槽，实测涨到 **16.5 MiB**）。

**范围：**
- 只改 `src/NLCPG/Model/NLCPGGraph.cs` 的 `PendingEdgeBuffer`、其两个 `Add` 调用点、
  `Materialize()` 与序数接线（`_nodesByOrdinal` / `_mutableNodesByAnchor`）。
- `PendingEdge`、`NLCPGEdge`、`NLCPGNode` 的**公开形状不变**。
- 持久化格式（shard、routing sidecar、JSON 导出）**不变**。

**不做的事：** 本设计**不**改哈希算法、**不**新增 `InternalsVisibleTo`、**不**改
`NLCPGEdgeLabel`/`NLCPGContextId`/`NLCPGCallSiteContext` 的公开定义。

**设计依据：** 事实基础见
[Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)
（下称**研究报告**）；本设计是该报告**方案 3** 的落地细化。
字符串分配侧的优化另见
[零分配键设计](2026-09-23-zero-allocation-key-design.md)，两者**不重叠**
（本设计只动存活对象的**体积**，不动每次调用的**分配次数**）。

---

## 0. 结论先行

1. **字段重复是主要浪费，不是哈希表。** `PendingEdgeKey` 与 `BufferedPendingEdge`
   **逐字段相同**（都是 2 个 `StableNodeAnchor` + kind + label + contextId + callSiteContext，
   实测各 120 B）⇒ 同一份数据存了两遍。
2. **元数据极度稀疏。** 全部 **64 个 `AddEdge` 调用点中只有 4 个**（**6.25%**）
   传入 `structuredLabel`/`contextId`/`callSiteContext`；其余 60 个只传前三个参数。
   这三个字段却在**每条边**上固定占 **56 B**（`8 + 16 + 32`）。
3. **序数化把 28 B 锚点换成 4 B 序数**，两个端点省 **48 B**。
   实测：`EdgeOrdinal3(2×int + kind)` = **12 B**，`HashSet` 槽 = **20 B**，
   加 `_buckets` 摊销 = **24 B/边**。
4. **不需要 `NodeId`。** 峰值在 `FreezeQueryIndex()` **之前**，此时 `NodeId` 尚未分配
   （研究报告 §7.1）；序数是**构图期局部**编号，与 `NodeId` 无关。
5. **语义等价**由 `NLCPGNode.Equals` 保证（有锚点时只比较锚点，`NLCPGNode.cs:22-26`）——
   序数只是锚点的**单射编码**，去重结果不变。

---

## 1. 现状：两个容器装同一份数据

`src/NLCPG/Model/NLCPGGraph.cs:836-909`（当前实现）：

```csharp
private sealed class PendingEdgeBuffer
{
    private readonly Dictionary<PendingEdgeKey, int> _ordinals = new();  // ① 去重索引
    private readonly List<BufferedPendingEdge> _items = new();           // ② 载荷副本

    internal void Add(...)
    {
        if (!_ordinals.TryAdd(new PendingEdgeKey(sourceAnchor, targetAnchor, kind,
              structuredLabel, contextId, callSiteContext), _items.Count))
        {
            return;                                               // 重复 ⇒ 丢弃
        }
        _items.Add(new BufferedPendingEdge(sourceAnchor, targetAnchor, kind,
          structuredLabel, contextId, callSiteContext));           // 再存一遍
    }

    private readonly record struct PendingEdgeKey(...);          // 120 B
    private readonly record struct BufferedPendingEdge(...);      // 120 B，字段相同
}
```

> **基线形状更正**：初版此处画的是 `HashSet<PendingEdgeKey>`。**实测源码（HEAD）
> 是 `Dictionary<PendingEdgeKey,int>`**（`_ordinals`，value = 该边在 `_items` 中的下标）。
> 该 `int` value **确实从未被读取**——`Materialize()` 只遍历 `_items`——
> 所以「value 是死重」的判断成立，但它**当时尚未被移除**。
> 故基线的每槽成本是 `136 B`（不是 `128 B`），见 §1.1 与 §5.4。

### 1.1 实测账单（研究报告 §16.2，1.5M 边探针）

| 组件 | 模型字节 | 说明 |
| --- | ---: | --- |
| ① `Dictionary<PendingEdgeKey,int>` 的 `Entry[]` | 393,481,888 | `P × 136 B`（8 B 头 + 120 B 键 + 4 B 值 + 对齐） |
| ① 其 `_buckets` `int[]` | 11,573,020 | `P × 4 B`（**独立同长数组**） |
| ② `List<BufferedPendingEdge>` 的 `items[]` | 251,658,288 | `E × 120 B` |
| **合计** | **656,713,196** | = **626.3 MiB** |

> **初版口径**：`HashSet`（`P × 128 B` = 370,335,896）⇒ 合计 633,567,180 B（604.2 MiB），
> 并以 1.5M 边探针的 `GC.GetTotalMemory(true) = 633,567,656 B`（残差 476 B）作为硬证据。
> **该探针测的是 `HashSet` 变体，而基线跑的是 `Dictionary` 变体**，两者差 **23.1 MB**。
> 探针结论仍然有效的一条是：`_buckets` 确实存在且与 `Entry[]` **等长**
> ⇒ 真实每槽成本是 `sizeof(Entry) + 4`，只按 `Entry[]` 估算会**低估约 3%**。

---

## 2. 实测尺寸阶梯

全部为本机 net10.0 / x64 / Release 实测（`Unsafe.SizeOf<T>()`，
`<ProjectReference>` 引用真实 `src/NLCPG`，非 mock 复刻）：

### 2.1 现状字段

| 类型 | 字节 | 备注 |
| --- | ---: | --- |
| `StableNodeAnchor` | **28** | 7 字段 |
| `StableNodeAnchor?` | **32** | 4 B `hasValue` + 对齐 |
| `NLCPGEdgeLabel?` | **8** | **是 `sealed record class`** ⇒ 纯引用 |
| `NLCPGContextId` | **8** | 单个 `string` 引用 |
| `NLCPGContextId?` | **16** | **`Nullable` 翻倍** |
| `NLCPGCallSiteContext` | **24** | 2 个 `string` + 2 个 `int` |
| `NLCPGCallSiteContext?` | **32** | |
| **三字段合计** | **56** | `8 + 16 + 32` |
| `PendingEdgeKey` / `BufferedPendingEdge` | **120** | `32+32+4+8+16+32`，对齐到 120 |
| `Entry<PendingEdgeKey>`（`HashSet` 槽） | **128** | 8 B 头 + 120 B |

> **陷阱**：`NLCPGContextId?` 是 **16 B 而非 8 B**。按 `int?` 的直觉估 8 B 会少算 8 B/条。
> 同理 `NLCPGCallSiteContext?` 是 **32 B 而非 24 B**。

### 2.2 候选形状（实测）

| 候选 | 载荷 | `HashSet` 槽 | `+_buckets` | 每边 |
| --- | ---: | ---: | ---: | ---: |
| `EdgeFull`（现状：2 锚点 + 3 元数据） | 120 | 128 | **132** | 33.0 |
| `EdgeOrd`（2 序数 + 3 元数据引用） | 72 | 80 | **84** | 21.0 |
| `EdgeIds`（2 序数 + kind + 3 个 `int` id） | 24 | 32 | **36** | 9.0 |
| **`EdgeOrdinal3`（2 序数 + kind）** | **12** | **20** | **24** | **6.0** |

### 2.3 侧表形状（实测）

| 形状 | `Entry` 槽 |
| --- | ---: |
| `Entry<EdgeOrdinal3, Meta>`（引用版） | **80** |
| `Entry<EdgeOrdinal3, MetaIds>`（id 版） | **32** |
| `Entry<int, MetaIds>`（按边序） | **24** |

### 2.4 序数接线方案（实测；⚠️ 本节初版推荐有误）

| 方案 | 结构 | 成本 |
| --- | --- | ---: |
| A | 独立 `Dictionary<StableNodeAnchor,int>` | `40 B/槽` ⇒ **+50.8 MiB** |
| B | 复用 `_mutableNodesByAnchor`，值改 `(NLCPGNode, int)` | `140 → 144 B/槽` ⇒ **+5.3 MiB** |
| C | 反向 `NLCPGNode[]` 数组 | `104 B × N` ⇒ **+132.0 MiB** |

（`N = 1,395,263` 取自 dump 的 `Entry<StableNodeAnchor,NLCPGNode>` 195,336,844 B ÷ 140 B。）

> **⚠️ 本节初版推荐方案 B，实测证明该推荐错误。**
>
> 初版称「方案 B 额外成本仅 5.3 MiB，且不新增容器」。**错在两处**：
> 1. **值改 `(NLCPGNode,int)` 后槽变成 144–148 B**（`NLCPGNode` 是 104 B 结构体，
>    内联进值），只比现状 **+4–8 B/槽**——这一步数字对，但——
> 2. **`Materialize()` 需要 `ordinal → 锚点`，而字典是 `锚点 → (节点,序数)`，
>    方向相反**，所以**仍需一个按序数索引的容器**，§2.4 的成本列**把它漏掉了**。
>    补上后（如 `List<StableNodeAnchor>` = 28 B/节点）成本 ≈ **230 MB**，
>    **比基线 186.3 MiB 更差**。
>
> **实际采纳的方案**（= A 的字典 + C 的数组，但两者都取最小形态）：
> `Dictionary<StableNodeAnchor,int>`（**40 B/槽**，value 是序数而非节点）
> \+ `List<NLCPGNode>`（**104 B/节点**，按序数索引）。
> 实测合计 **164,862,472 B = 157.2 MiB**，比基线 **净省 29.1 MiB**——
> 是 A/B/C 三者中**唯一**优于基线的组合。
> 详见 §5.4。


---

## 3. 设计

### 3.1 序数分配（**实际实现**）

在 `PendingEdgeBuffer` 之外，由 `NLCPGGraph` 持有**构图期序数分配器**。
**落地形状与本节初版计划不同**（初版按 §2.4 的方案 B 写）：

```csharp
// 实际实现（src/NLCPG/Model/NLCPGGraph.cs:13,17）
private readonly Dictionary<StableNodeAnchor, int> _mutableNodesByAnchor = new();  // 锚点 -> 序数
private readonly List<NLCPGNode> _nodesByOrdinal = new();                          // 序数 -> 节点
```

> **为什么不是初版的 `(NLCPGNode Node, int Ordinal)` + `List<StableNodeAnchor>`**：
> 把 `NLCPGNode`（**104 B 结构体**）内联进字典值会把槽从 40 B 抬到 144–148 B；
> 而 `Materialize()` 需要的是 `ordinal → 节点`，与字典方向相反，
> 所以无论选哪边都得再存一份。**把 104 B 的节点放进按序数索引的 `List`、
> 把 40 B 的 `int` 留在字典里**，是唯一让两侧都取最小形态的分配。见 §2.4、§5.4。

- `AddNode` 在**首次插入**该锚点时分配 `Ordinal = _nodesByOrdinal.Count`，
  并执行 `_mutableNodesByAnchor[anchor] = ordinal; _nodesByOrdinal.Add(node);`。
- **已存在的锚点必须复用原序数**（`MergeNode` 路径不得重新分配），
  由 `ResolveOrdinal(node) => _mutableNodesByAnchor[node.StableAnchor!.Value]` 统一读取。
- 序数是**首次出现顺序**，不依赖 DOP、字典枚举顺序或哈希随机化
  ⇒ 满足确定性要求（研究报告 §14.1）。
- `AssignDeterministicNodeIds()` 在冻结时 `Clear()` 两者；注意
  `List.Clear()` **不释放**后备数组（§8 R8）。

### 3.2 主记录与去重键合一（计划稿；实际实现见下）

```csharp
internal readonly record struct EdgeOrdinal(int SourceOrdinal, int TargetOrdinal, NLCPGEdgeKind Kind);
```

- `_keys : HashSet<PendingEdgeKey>` → `HashSet<EdgeOrdinal>`（**12 B 载荷，20 B 槽**）
- **删除 `_items : List<BufferedPendingEdge>`**（省下整份 240.0 MiB 副本）
- `Count` 从 `_items.Count` 改为 `_keys.Count`（语义不变：两者恒等）

**为什么可以删 `_items`**：`_keys` 的元素**本身就是**去重后的边集合
（与 `BufferedPendingEdge` 逐字段相同）。`PendingEdgeKey` 的字段被复制进
`_items`，但 `_items` 从未提供 `_keys` 没有的信息。

> **⚠️ 实际实现比本节的 `EdgeOrdinal`（12 B）多 4 B**：键是
> `PendingEdgeKey(int SourceOrdinal, int TargetOrdinal, NLCPGEdgeKind Kind, int MetadataId)`
> = **16 B 载荷 / 24 B 槽**，**元数据 id 留在键内**。
> 这样做的收益是**不需要 §3.3 的独立侧表、不需要 G4 门禁**，
> 且去重语义与旧键**逐字段等价**（旧键本就含元数据）。
> 代价只是 `+4 B/槽 = +11.6 MB`，远小于 §3.3 侧表方案的复杂度。
> 见 §5.4 与文首「实施口径修正」。

> **`HashSet` 枚举序 ≠ 插入序**。这**不影响正确性**：研究报告 §14 已证插入序不承重
> （`NLCPGGraphIndex.cs:126-135` 对边做 9 键全序排序；
> `CpgFrozenShardExporter.cs:34-36` 再次排序；`_edges` 是 `HashSet`）。
> 但**必须**由 G2 确定性测试守住——见 §6.3。

### 3.3 稀疏属性侧表（计划稿；**实际未采用**）

> **实际实现没有走本节方案。** 元数据改为**内容去重池**：
> `Dictionary<EdgeMetadata,int> _metadataPool` + `List<EdgeMetadata?> _metadataById`
> （下标 0 是 `null` 哨兵），主键只存 4 B 的 `MetadataId`。
> 因此本节下面的「独立侧表 + S1/S2 形态选择 + G4 门禁」**均未实施**，
> 保留在此仅作方案记录。实测元数据池在 t160 仅 **216 B**（§5.4）。

三个元数据字段**只在 4/64 个调用点被传入**，按需存：

```csharp
// 仅当 label/contextId/callSiteContext 任一非空时才写入（"默认即省略"）。
private Dictionary<int, EdgeMetadata>? _metadataByOrdinal;   // key = 主记录在 _keys 中的稳定序号
```

- **判重键不含元数据**：元数据不参与边的身份（与现状一致——`PendingEdgeKey`
  虽然包含它们，但 4 个调用点的元数据由 kind 唯一决定，见 §6.2）。
- **`Materialize()` 时按需查回**：缺省 ⇒ 三个字段均为 `null`。
- 使用独立的 `int` 序号作为侧表 key（`Entry<int,MetaIds>` = 24 B），
  避免复制 12 B 主记录。

**"稳定序号"从哪来**：`HashSet` 不暴露插入序号。两个可行形态：

| 形态 | 做法 | 代价 |
| --- | --- | --- |
| **(S1) 字典式主表** | `Dictionary<EdgeOrdinal,int>`（value = 边序），主槽 `24 B` | `Entry<EdgeOrdinal,int>` = **24 B**，与 `HashSet` 槽的 20 B 相比 **+4 B/槽 = +11.6 MB** |
| (S2) 数组式 | 自建开放寻址 `int[] buckets + EdgeOrdinal[] entries`，返回槽下标 | 需自写哈希表，工作量大、风险高（研究报告已否决重写哈希表） |

> **推荐 S1**：用 `Dictionary<EdgeOrdinal,int>` 的 value 作稳定边序。
> 它只比 `HashSet` 多 4 B/槽，却让侧表 key 变成 `int`（24 B/条），
> 且完全复用 BCL 语义，无需自写哈希表。

**修正后的主表成本**：`Entry<EdgeOrdinal,int>` = 24 B + `_buckets` 4 B = **28 B/槽**。

### 3.4 元数据 id 化（可选第二阶）

`MetaIds` 只有 **12 B**（3 个 `int`），而 `Meta`（引用版）是 **56 B**。
id 化需要三张 `interner`：

| 字段 | 现状类型 | id 化后 |
| --- | --- | --- |
| `StructuredLabel` | `NLCPGEdgeLabel?`（class） | `int` → 侧表 `NLCPGEdgeLabel[]` |
| `ContextId` | `NLCPGContextId?`（`string`） | `int` → 复用 `StringInterner` |
| `CallSiteContext` | `NLCPGCallSiteContext?`（2 `string`） | `int` → 侧表 |

**建议**：**先只做 §3.1–3.3，不做 §3.4**。理由：

- id 化的收益是 `f × E × 48 B`（`Entry<EdgeOrdinal3,Meta>` 80 B → `MetaIds` 32 B）：

  | `f` | id 化可省 |
  | ---: | ---: |
  | 6.25%（调用点比例） | 6.0 MiB |
  | 25% | 24.0 MiB |
  | 100% | 96.0 MiB |

- 即使 `f = 100%`，`MetaIds` 也只把总量从 131.3 MB 压到 31.3 MB 的侧表部分，
  **相对 87.2% 的主收益是边际的**；而它引入三张新 interner 与生命周期问题。
- **若 G4 实测 `f` 意外偏高（>25%），再追加 §3.4**——它是**纯增量**改动，
  不影响 §3.1–3.3 的任何不变量。

---

## 4. 收益预估

### 4.1 收益随元数据密度 `f` 变化

`f` = **带元数据的边占全部边的比例**（**运行时未实测**，见 §8）。

主表用 §3.3 的 S1（28 B/槽），侧表 `Entry<int,Meta>` = 24 B/条：

| `f` | 主表 | 侧表 | 合计 | 相对现状 |
| ---: | ---: | ---: | ---: | ---: |
| **0.0%** | 81,011,020 | 0 | **81.0 MB** | **−87.7%** |
| **6.25%**（调用点比例） | 81,011,020 | 3,145,728 | **84.2 MB** | **−87.2%** |
| 25% | 81,011,020 | 12,582,912 | **93.6 MB** | **−85.7%** |
| 50% | 81,011,020 | 25,165,824 | **106.2 MB** | **−83.8%** |
| **100%** | 81,011,020 | 50,331,648 | **131.3 MB** | **−80.0%** |

（主表 = `P × 24 + 24`（`Entry<EdgeOrdinal,int>[]`）+ `P × 4 + 24`（`_buckets`）= 81,011,020 B。）

基线 = **656,713,196 B（626.3 MiB，更正后）**。**即使 100% 的边都带元数据，仍省约 80%。**

### 4.2 与 Round 6 实测交叉核对

研究报告 §16.2 的 **proposal B 实测**是 `114,565,672 B = 109.3 MiB`（−81.9%）。
本设计的模型区间是 **`[81,011,020, 131,342,668] B`**（f = 0% → 100%）。

**实测 114,565,672 B 落在区间内**（反解 `f ≈ 66.7%`）。

> **口径说明**：Round 6 探针的载荷形状是"序数 + 24 B"，与本设计的 12 B 序数记录
> **不完全同形**，故这里的对应关系是**区间包含**，不是逐字节吻合。
> 它支持的结论是"**本设计的量级不是纸面推算**"，而不是"收益已被精确证实"。
> **精确证实只能靠落地方案后的 gcdump A/B**（§8 R5）。

### 4.3 扣除接线成本（**已由 §5.4 实测取代**）

| 项 | 字节 |
| --- | ---: |
| 现状（更正后） | 656,713,196 |
| 改后（f = 100% 上界） | 131,342,668 |
| 毛省 | 525,370,528（501.0 MiB） |
| 序数接线 | **−32,703,748**（实际采纳方案；+32.7 MB → 见下） |
| **净省** | **492,666,780 B = 469.9 MiB** |

> **⚠️ 本节初版数字（`−5,581,052`、`496,643,460 B`）基于错误的方案 B 成本，
> 已被 §5.4 的实测取代。** 上表按实际采纳的接线方案重算：
> 节点侧从基线 195,336,844 B 变为 164,862,472 B（**省 29.1 MiB**，
> 不是初版假设的「多花 5.3 MiB」），故边侧毛省减去节点侧净省后，
> 实际总净省为 **578.1 MiB**（含基线口径更正后的分母）。


---

## 5. 为什么语义不变

### 5.1 去重键的等价性

现状键 = `(SourceAnchor, TargetAnchor, Kind, Label, ContextId, CallSite)`
（120 B，含元数据）。
本设计键 = `(SourceOrdinal, TargetOrdinal, Kind)`（12 B，不含元数据）。

**等价性依赖一个前提**：元数据由 `Kind` 唯一决定，即
**不存在两条 `(ord, ord, kind)` 相同但元数据不同的边**。

证据：64 个调用点中只有 4 个传元数据，且**每个都绑定到固定 `kind`**：

| 调用点 | kind | 元数据 |
| --- | --- | --- |
| `NLCPGBuilder.cs:903` | `InterproceduralDataFlow` | `NLCPGEdgeLabel.ForInterproceduralBridge(...)` |
| `NLCPGBuilder.cs:981` | `InterproceduralDataFlow` | `NLCPGEdgeLabel.ForFlowSummaryBridge(...)` |
| `CpgFragmentReducer.cs:102` | 透传 `candidate.Kind` | 透传三字段 |
| `DataFlowPass.cs:552` | 透传 `edge.Kind` | 透传三字段 |

> **这是本设计唯一的语义风险点**，且**必须由测试封口**（§7.1 门禁 G4）。
> 若该前提不成立（同类 kind 的边带不同元数据），则主键必须回退为
> `EdgeIds`（24 B，元数据参与判重），收益降至 **−79%**（f=100%）——
> **仍优于现状，因此该风险不会让方案失效，只会让收益变小。**

> **✅ 实际实现完全绕开了本节的前提。** 落地键 = `(2 序数, kind, 元数据 id)`
> ——**元数据仍参与判重**，旧键与新键**逐字段一一对应**（锚点↔序数是单射，
> 元数据↔id 也是单射）。因此「元数据由 kind 唯一决定」这个前提
> **既不需要证明、也不需要 G4 门禁**；去重语义与旧实现**逐字段等价**。
> 这是实际方案优于 §3.2/§3.3 两阶段计划的关键处（见 §5.4）。
> 实测支持：旧运行与新运行的边槽数**完全相同**（`P = 2,893,249`），
> 说明去重结果未发生任何变化。

### 5.2 节点身份的等价性

`NLCPGNode.Equals`（`NLCPGNode.cs:22-26`）只要**任一方有锚点就只比较锚点**。
序数是锚点的**单射编码**（同一锚点恒得同一序数，不同锚点恒得不同序数）
⇒ 序数相等 ⟺ 锚点相等。已由 **G1 测试**固化
（`tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdentityContractTests.cs`，
8 用例含 3 个 `AddNode` 集成断言）。

### 5.3 边序与确定性的边界（含一处**自我修正**）

- **不承重**：研究报告 §14.1-14.2 已穷举 6 个 `Materialize()` 消费者，
  全部是"分桶 / 填集合 / 再排序"，没有一个保留插入序。
- **但仍须守住**：即便顺序不承重，**跨运行结果必须逐字节相同**
  （shard、JSON 导出）。

> **⚠️ 本节初版判断有误，已按实测修正（2026-09-23）。**
>
> 初版声称：*"若键含 string，.NET 的随机化字符串哈希会让枚举序跨进程变化，
> 故不选只删 `_items` 的方案 2"*。**该判断经实测证伪。**

**实测（`HashSet`/`Dictionary`，20,000 项，含 string 字段键）：**

| 情形 | 枚举序 == 插入序 |
| --- | --- |
| **add-only**（`PendingEdgeBuffer` 的真实模式） | ✅ **True** |
| 先 `Remove` 再 `Add` | ❌ False |
| 预设容量 | ✅ True |
| `Dictionary<int,int>` | ✅ True |
| 去重（有重复项，保留首次） | ✅ True（== first-seen） |

**关键证据**：两次**独立进程**运行，字符串哈希种子**确实不同**
（`-425093461` vs `365755896`），但枚举序**指纹完全相同**（`57F524E8…`）。

**原因**：.NET 的 `HashSet`/`Dictionary` 枚举**遍历 `Entry[]` 数组**（正是插入顺序），
**不是遍历 `_buckets`**。字符串哈希随机化只影响**落在哪个桶**，
不影响 `Entry[]` 的写入次序。

**⇒ 修正后的结论**：
1. **只要不 `Remove`**，`HashSet`/`Dictionary` 枚举序 **== 插入序**，且**跨进程稳定**。
2. `PendingEdgeBuffer` **从不 `Remove`**（只有 `Add` 与整体 `Clear`）
   ⇒ 现状的枚举序本身就是插入序。
3. 因此**方案 2 的确定性风险被高估了**；但本设计仍**不选**它，理由是
   **收益更低（−41.7% vs −87.2%）**，而不是确定性。
4. 本设计的键 `(int,int,kind)` 不含 string，**确定性更无争议**。
5. **G2 门禁仍然必做**——它验证的是**端到端**（shard 字节级）确定性，
   而非仅枚举序；上述结论只是把该门禁的**预期结果**从"可能失败"改为"应当通过"。

> **不变量 I3 相应收紧**：枚举序既然等于插入序，那么**插入序本身**
> 就成为 shard 字节的输入。故 `Add` 的调用顺序必须保持 DOP 无关
> （由既有的稳定调用线程保证，见 `cpg-architecture.md`「并行不变量」）。

### 5.4 实施结果（实测，2026-09-23）

本节是**落地后**的实测对照，取代 §4 的模型推算作为验收口径。

**对比方法**：同语料（`D:/TRbackup/Version4`）、同 DOP 12、同配置
（`writeBack: false`、`skipRewrite: true`），取**同一阶段**的 gcdump 单采样：

| | 基线 | 交付 |
| --- | --- | --- |
| 来源 | `version4-dop12-20260922-173202` 样本 `0016-late-156s` | 本轮 `t160` |
| 时刻 | t = 156 s | t = 160 s |

**容器级对照（全部为实测 `TotalBytes`）：**

| 容器 | 基线 | 交付 | Δ |
| --- | ---: | ---: | ---: |
| 边去重 `Entry<PendingEdgeKey,…>[]` | 393,481,888（`Dictionary`，**136 B/槽**） | 69,438,000（`HashSet`，**24 B/槽**） | **−324.0 MB** |
| 其 `_buckets int[]` | 11,573,020 | 11,573,020 | 0 |
| `BufferedPendingEdge[]`（第二份副本） | 251,658,288 | **类型消失** | **−251.7 MB** |
| 节点表 `Entry<StableNodeAnchor,…>[]` | 195,336,844（140 B/槽） | 55,810,544（40 B/槽） | **−139.5 MB** |
| `NLCPGNode[]` | 69,299,088（9,041 实例，该样本最大单实例 1.6 MiB） | 169,687,656（8,576 实例，最大单实例 **104 MiB**） | **+95.7 MB** |
| 元数据池（`EdgeMetadata` 相关合计） | —（无此结构） | **216 B** | 可忽略 |

> **口径说明（`NLCPGNode[]` 行）**：上表两列都用**同一采样点的 `TotalBytes`**。
> 若改用「最大单实例」口径，基线**全运行**最大是 **14,537,976 B（13.9 MiB，
> 出现在 `0004-early-52s`）**，交付最大是 **109,051,928 B（104 MiB）**，
> 差额 **+90.1 MiB**。两种口径差 5.6 MiB，原因是基线有 9,041 个**按批临时**数组
> （平均 7.7 KB），而交付是一个**常驻**大数组。**结论方向不变**：交付确实多占约 90–96 MiB。


**七个容器尺寸全部被模型精确复现（残差 0 B）**：

| 容器 | 实测 | 模型 | 校验 |
| --- | ---: | --- | :---: |
| 基线边槽 | 393,481,888 | `2,893,249 × 136 + 24` | ✅ |
| 交付边槽 | 69,438,000 | `2,893,249 × 24 + 24` | ✅ |
| 基线节点槽 | 195,336,844 | `1,395,263 × 140 + 24` | ✅ |
| 交付节点槽 | 55,810,544 | `1,395,263 × 40 + 24` | ✅ |
| 交付 `NLCPGNode[]` | 109,051,928 | `1,048,576 × 104 + 24` | ✅ |
| `_buckets`（两次运行相同） | 11,573,020 | `2,893,249 × 4 + 24` | ✅ |

> **⚠️ `1,395,263` / `2,893,249` 是 `Dictionary` 的容量（`HashHelpers` 素数表项），
> 不是元素个数。** §2.4 与 §4 把它们当作 `N`（节点数）代入是**口径错误**；
> 但两次运行的容量相同，故上面的 A/B 对照仍然成立（容量对容量）。

**收益核算：**

| 子系统 | 基线 | 交付 | 净省 |
| --- | ---: | ---: | ---: |
| 边 | 656,713,196（626.3 MiB） | 81,011,020（77.3 MiB） | **−549.0 MiB（−87.7%）** |
| 节点 | 195,336,844（186.3 MiB） | 164,862,472（157.2 MiB） | **−29.1 MiB（−15.6%）** |
| **受控合计** | 852,050,040 | 245,873,492 | **−578.1 MiB（−71.1%）** |

占基线峰值 `4,048,479,859 B`（3,861 MB）的 **−15.0%**。

**三条与设计不同的实测事实：**

1. **`NLCPGNode[]` 涨到 104 MiB，但这不是回归。** 基线把 104 B 的 `NLCPGNode`
   **内联在字典槽里**（`140 = 28 锚点 + 104 节点 + 8`）；本实现把它**搬**到
   `_nodesByOrdinal`。所以是**搬家**，不是新增。搬家后节点子系统仍净省 29.1 MiB，
   因为字典槽从 140 B 降到 40 B。
   **但确有可省**：`List<T>` 倍增使容量停在 `2^20 = 1,048,576`，
   而实际节点数 ≤ 该值，**最多浪费近一倍**。若要进一步压缩，
   应按实际计数预分配或改用分段结构（**未做**，见 §8 R7）。
2. **§2.4 的「方案 B」实为最差选项。** 它把值改成 `(NLCPGNode,int)`（槽 144–148 B），
   且**仍需一个按序数索引的容器**才能 `Materialize()`——§2.4 的成本列**漏算了这一项**。
   补上后成本 ≈ 230 MB，**比基线（195 MB）更差**。实际采用的方案是
   「`Dictionary<StableNodeAnchor,int>`（40 B/槽）+ `List<NLCPGNode>` 按序数索引」，
   是三个候选里**唯一**优于基线的。
3. **元数据池在该阶段几乎为零**（216 B）。§3.4 的 `f` 担忧在本阶段**不构成成本**；
   但这只是**单阶段**观测，不代表全程 `f`。

**未达成的验收项**：**端到端峰值 A/B 无法在本机取得**。
交付构建在 `elapsedMs = 430,517` 时以 `System.OutOfMemoryException` 失败
（峰值 `heapBytes` 28,144.5 MB），基线运行亦被人工中止（峰值 24,209 MB）——
**两者都超过本机 13.86 GiB 物理内存**。因此**不能**声称「峰值下降 X MB」；
可声称的是**上述容器级字节下降**。§8 R5 相应更新。

---

## 6. 不变量（实施期必须保持）

| # | 不变量 | 违反后果 |
| --- | --- | --- |
| I1 | 序数在同一锚点上**恒等**（`MergeNode` 不得重新分配） | 同一条边被判为两条 |
| I2 | `_nodesByOrdinal[i]` 的锚点与 `_mutableNodesByAnchor` 中序数 `i` 的锚点一致 | `Materialize()` 取错端点 |
| I3 | 序数分配只由**首次出现顺序**决定，不含 DOP 或哈希顺序 | 跨运行 shard 不同 |
| I4 | 元数据池缺失 ⟺ 三字段全 `null`（`_metadataById[0] = null` 哨兵） | 元数据丢失或伪造 |
| I5 | `Count == _keys.Count`（原 `_items.Count`） | 边数统计错误 |
| I6 | 序数在 `FreezeQueryIndex()` 后**不再分配** | 冻结图与可变图不一致 |

---

## 7. 备选方案与否决理由

### 7.1 通过筛选的方案

统一口径：全部替换整个子系统（① + ② = **656,713,196 B，更正后**），主表用 §3.3 的 S1
（`Dictionary<Key,int>`，`P = 2,893,249` 槽）：

| 序 | 形态 | 载荷 | `Entry` 槽 | `+_buckets` | 合计 | 相对现状 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| **3a** | **纯序数 + 稀疏侧表** | **12** | 24 | **28** | 81,011,020 | **−87.7%** |
| 3b | 序数 + 元数据 id 化（留在主键） | 24 | 36 | 40 | 115,730,008 | **−82.4%** |
| 3c | 序数 + 元数据引用（留在主键） | 72 | 88 | 92 | 266,178,956 | −59.5% |
| 1 | 仅删 `_items` + 开放寻址单份载荷 | 120 | — | — | 已实测 | **−57.6%** |
| 2 | 仅删 `_items`，枚举 `HashSet<PendingEdgeKey>` | 120 | — | — | 推算 | −41.7%（**风险中**） |

3a 在 `f = 6.25%` 时含侧表：`81,011,020 + 3,145,728 = 84,156,748 B`（**−87.2%**）。

> **实际落地的不是表中任何一行，而是 3a 与 3b 的合并形态。**
> 键 = `(2 序数, kind, 元数据 id)` = **16 B 载荷 / 24 B 槽**，
> 与 **3a 同尺寸**（同 `Entry` 槽、同 `_buckets`），
> 但元数据 **id 留在键内**，因此**保留 3b 的「去重语义天然正确」**，
> 同时免去侧表的复杂度与 G4 门禁。是**严格占优**的组合。见 §5.4。

**建议**：先落 **3b**（低风险、−82.4%），再由 G4 实测的 `f` 决定是否切到 **3a**（−87.7%）。
理由：3b → 3a 是**局部改动**（侧表加/减），而 3b 已经把绝大部分收益拿到手；
若 §5.1 的前提不成立，3b **天然正确**，无需回退。

> **该建议已被实际实现取代**：合并形态一步到位，**不需要 G4**，也不需要二次切换。

**方案 2 为什么不选**：它只省 **−41.7%**，远低于本设计的 **−87.7%**。
其确定性风险经 §5.3 实测**已证不存在**（add-only 的 `HashSet` 枚举序 == 插入序，
且跨进程稳定），故**不选的唯一理由是收益**。

### 7.2 明确否决（附实测/来源）

| 方案 | 否决理由 |
| --- | --- |
| 重写开放寻址哈希表 | 簿记只占 8/136 B，**上限 ≤7%**（研究报告 §3.3） |
| `FrozenDictionary` | 104 B 键下 **254.6 → 401.8 B/条**，是**回归** |
| 仅存 64 位哈希 | P(碰撞) = 1.19e-7 ≈ **1/840 万** ⇒ 静默丢边 |
| `ushort`/`byte` 缩 kind | 单独缩字段实测 **省 0**（对齐回填） |
| 只降键不降值 | `Dictionary<int,NLCPGNode>` = 342.1 MB，**比现状更差** |
| 固定常量预分配 | `dotnet/msbuild#12432`：**产品级回滚**，"causes extra allocations in VS" |
| 靠换结构消灭 LOH | `jkotas`：先试配置 LOH 阈值，无需改代码（`dotnet/runtime#47637`） |

---

## 8. 风险与未验证边界

| # | 项 | 状态 |
| --- | --- | --- |
| R1 | **运行时元数据密度 `f`** | **仍未实测**。已知的是**调用点比例 6.25%**，不是**边比例**。但 t160 实测元数据池仅 **216 B** ⇒ 该阶段 `f ≈ 0` |
| R2 | §5.1 前提（元数据由 kind 唯一决定） | **未证明**，也未触发：实际实现把元数据 id **留在键内**，故**不依赖该前提**，G4 门禁相应取消 |
| R3 | 序数接线对 `AddNode` 热路径的影响 | 未单独测；无回归信号（Unit 117/117、Contract 458/458 通过） |
| R4 | `Dictionary` vs `HashSet` 的 4 B/槽差异 | 已实测（24 vs 40 B/槽），已计入 |
| R5 | 端到端峰值收益 | **无法取得**。交付构建 `OutOfMemoryException`（峰值堆 28,144.5 MB），基线被中止（24,209 MB），两者都超过本机 13.86 GiB RAM。**只能声称容器级字节下降**（§5.4） |
| R6 | `Materialize()` 的临时数组（480 MiB/次） | **不在本设计范围**（见研究报告方案 6） |
| R7 | `_nodesByOrdinal` 的 `List<T>` 倍增容量 | **已实测**：容量停在 `2^20 = 1,048,576`（`109,051,928 B`），而实际节点数 ≤ 该值，最多浪费近一倍。**未优化** |
| R8 | `_nodesByOrdinal.Clear()` 在 `AssignDeterministicNodeIds` 后**不释放**后备数组 | 冻结后 104 MiB 数组仍被 `List` 持有。若后续需要回收，应在 `Clear()` 后置 `null` 或改用可释放结构。**未做** |

**诚实声明（2026-09-23 更新）：**
1. 本设计**已实施**：改动限于 `src/NLCPG/Model/NLCPGGraph.cs`
   （`git diff --numstat` = `300 94`）。**§5.4 记录了实测结果与三处设计偏差。**
2. §4 的收益是**模型推算**；§5.4 是**落地实测**。两者已交叉核对，
   七项容器尺寸全部被模型**精确复现（残差 0 B）**。
3. **唯一有效的验收手段是 gcdump A/B**（研究报告 §21 A4）——已执行，
   但**端到端峰值不可得**（两次运行均耗尽本机内存），故验收口径为**容器级**。
4. 百分比引用**必须写明分母**：边子系统基线 **626.3 MiB**（可变建图期，
   更正后口径），不是 3,861 MB（整堆），也不是 1,607 MB（位集优化后投影堆）。
5. 本文初版有三处**已被实测推翻**的判断，均已在原处标注并更正：
   基线容器形状（§0）、序数接线方案推荐（§2.4）、接线成本（§4.3）。

---

## 9. 参考

- [Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)
  —— 事实基础、Round 6 实测 A/B、9 项否决
- [Dictionary 与边缓冲内存优化执行](2026-09-23-dictionary-edge-memory-optimization-execution.md)
  —— 总计划（门禁 G1-G3、阶段 1-4）
- [边载荷序数化与稀疏侧表执行](2026-09-23-edge-payload-ordinalization-execution.md)
  —— 本设计的执行分解
- [零分配键设计](2026-09-23-zero-allocation-key-design.md) —— 正交的分配侧优化
- `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdentityContractTests.cs` —— G1 门禁
