# Dictionary `Entry<>` 与边缓冲内存优化执行计划

> **上游依据**：`docs/plans/2026-09-23-dictionary-edge-memory-optimization-research.md`（6 轮调研）。
> 本文件只讲**怎么做**；结论、实测数据与被否决方案的理由见上游报告。
> **状态**：尚未开始实施。所有收益为研究阶段的实测/推算值，**端到端峰值未经 gcdump 验证**。

---

## 1. 目标与验收

### 1.1 目标

降低 NLCPG 构建期**峰值堆**，针对两个已被实测确认的消费者：

| 项 | 现状 | 归属 |
| --- | ---: | --- |
| `Dictionary Entry<>` 合计（145 行 / 81,327 实例）| **861.2 MiB**（堆的 22.31%）| 主要是 `PendingEdgeKey` 128 B/slot |
| `BufferedPendingEdge[]` | **240.0 MiB** | `List` 内部数组 |

### 1.2 验收标准

| # | 标准 | 判定方式 |
| ---: | --- | --- |
| A1 | 同输入两次构建产出**字节相同**的 shard | 新增确定性回归测试 |
| A2 | Contract 测试全绿（**串行**跑，见 §2.3）| `Run-TestTiers.ps1 -Fast`（含 unit + contract 两层），串行方式见 §2.3 |
| A3 | 图内容不变：边集合、属性、顺序全部一致 | 黄金样本对比 + A1 |
| A4 | 峰值堆下降（目标 ≥ 300 MiB）| **gcdump A/B**（唯一有效的验收手段）|

> **A4 是唯一能证明收益的标准。** 研究阶段的 −57.6% / −81.9% 是**子系统稳态占用**的实测，
> 不等价于端到端峰值下降。

---

## 2. 前置门禁（必须先完成，否则不要动生产代码）

### 2.1 门禁 G1：节点身份折叠的回归测试

**背景**：`StableNodeAnchor.cs:9-20` 的 `CreateFallback` 把 `Ordinal` **硬编码为 0**。
早期分析认为「所有 fallback 节点会在 `Ordinal=0` 上碰撞」——
**Round 7 实测已修正该判断**：`ExtraKeyId` 仍是有效区分字段，`Ordinal=0` 本身**不产生任意碰撞**。

**真正的折叠风险**来自 `StableNodeAnchor` **不覆盖**的三个 `NLCPGNode` 字段
（`DispatchKind`、`TypeFullNameId`、`IsImplicit`，见研究报告 §17.2）。
实测确认：`IsImplicit` 不同的两个节点会生成**相同锚点**。

**但这在图层是既有语义**：`NLCPGNode.Equals`（`NLCPGNode.cs:22-26`）只要任一方有锚点就
**只比较锚点**，故上述字段在数据流中本来就不参与节点身份。

**动作**（风险已下调，但仍须做）：
1. 写测试固化契约：**`ExtraKeyId` 相同、其余字段也相同的两个节点不得被误当作不同节点**；
   `ExtraKeyId` 不同的节点**不得被合并**。
2. 该测试保护的是方案 2/3 的降键正确性，属**常规回归**，不再是阻塞性门禁。

### 2.2 门禁 G2：确定性回归测试

**动作**：同一输入构建两次，断言两次导出的 shard **字节相同**。

**依据**：研究已证插入序不承重（`NLCPGGraphIndex.cs:126` 的 9 键全序排序、
`CpgFrozenShardExporter.cs:34-36` 的重排）。本测试把该结论**固化为契约**，
防止方案 1 的 `PendingEdgeBuffer` 重写破坏确定性。

### 2.3 门禁 G3：测试串行执行

**问题**：`Run-TestTiers.ps1` **不关闭** xUnit collection 并行，
而仓库存在既有隔离缺陷（`NLCPGDisplayTextTests` 等调用进程级 `Directory.SetCurrentDirectory()`，
与 `NLCPGNodeIdContractTests` 的相对路径冲突）。

**动作**：改 DataFlow / Graph 相关代码后，**必须串行**跑 Contract，
否则随机失败会被误判为本改动引入的回归。

本仓库可用分层（`Miscellaneous/scripts/Run-TestTiers.ps1`）：

```powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast      # unit + contract
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -HostTier  # host
```

**该脚本不关闭 collection 并行**，故需要串行时改用逐项目直接跑：

```powershell
pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments @(
  'test','tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false',
  '-p:UseSharedCompilation=false','-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
```

---

## 3. 实施阶段

### 阶段 1：`PendingEdgeBuffer` 消重（收益最大、风险最低）

**目标**：消除「同一份 120 B 记录存两遍」。

**现状**（`NLCPGGraph.cs:836-909`）：

```csharp
private readonly HashSet<PendingEdgeKey> _keys = new();      // ② 370.3 MiB
private readonly List<BufferedPendingEdge> _items = new();   // ① 251.7 MiB（字段完全相同！）
```

**改法（推荐：开放寻址 + 单份 payload）**：

1. 保留一份 `PendingEdgeKey[]` payload 数组（去重后的唯一副本）。
2. 用 `int[]` bucket 做开放寻址索引（负载 ≤ 0.5 时，4 M 槽 × 4 B = 16 MiB）。
3. 删除重复的 `BufferedPendingEdge` 结构（它 6 个字段与 `PendingEdgeKey` **完全相同**）。
4. **分批预留容量**：按已见边数预估，设**上限**，不要固定常量预留
   （既有文档否决固定预留：12 路并发会放大成 ~7.4 GB 长驻）。

**实测收益**（1.5 M 边）：

| | live | 相对现状 |
| --- | ---: | ---: |
| 现状 | 604.2 MiB | — |
| 本阶段 | 256.0 MiB | **−57.6%（省 354 MiB）** |

**附带收益**：耗时 956 ms → 530 ms；消除 **579.2 MiB** 的翻倍扩容垃圾。

**门禁**：G2 + G3 + §1.2 的 A2。

---

### 阶段 2：C/D 类字典降键**且降值**

**目标**：把内联的 104 B `NLCPGNode` 换成 28 B `StableNodeAnchor`。

**关键约束（研究已证，务必遵守）**：

1. **键与值必须同时降。** 只降一侧会**更差**：
   `Dictionary<int, NLCPGNode>` = 342.1 MB，**高于**现状的 331.1 MB。
2. **峰值时 `NodeId` 不存在**（峰值在 `FreezeQueryIndex()` 之前）。
   ⇒ **不允许**用 `NodeId`。可用 `StableNodeAnchor`（28 B）。
3. **键集必须全部来自 `AddNode` 路径**——**该前置条件已于 Round 8 完成核实，无例外**
   （研究报告 §18）：4 个 `NLCPGNode` 构造点**全部**设置 `StableAnchor`，
   `NLCPGNode.Equals`（`NLCPGNode.cs:22-26`）在有锚点时只比较锚点，故降键**语义等价**。

**✅ 已产出的「可降键字典白名单」**（研究报告 §18.2）：

| 字段 | 位置 | 现值 Entry | 可降键 |
| --- | --- | ---: | --- |
| `_syntaxNodes` | `NLCPGBuilder.cs:47` | 120 | ✅ |
| `_symbolKeysByNode` | `NLCPGBuilder.cs:55` | — | ✅ |
| `_methodOwnerSymbolKeysByBoundaryNode` | `NLCPGBuilder.cs:56` | — | ✅ |
| `_methodParameterOrdinalsByNode` | `NLCPGBuilder.cs:57` | 116 | ✅ |
| `_cfgPredecessorsByNode` | `NLCPGBuilder.cs:62` | — | ✅ |
| `_cfgSuccessorsByNode` | `NLCPGBuilder.cs:63` | — | ✅ |
| `_operationNodesByOperation` | `NLCPGBuilder.cs:67` | 120 | ✅ |
| `ParameterDefinitionFacts` | `DataFlowPass.cs:104` | 116 | ✅ |
| `Predecessors` / `Successors` | `DataFlowPass.cs:108-109` | 120 | ✅ |

**产出物**：「可降键字典白名单」——每个字典一行，写明键来源与安全性论证。

**收益**：模型推算 **200.1 MB**（未实测）。

**门禁**：白名单完成 + G2 + G3。

---

### 阶段 3：稀疏属性 + 序数化 payload

**目标**：120 B → 24 B。

**依据**：**64 个 `AddEdge` 调用点中，0 个显式传 label/context/callsite**
⇒ 那三个字段在 `PendingEdgeKey` 里固定占 **8+16+32 = 56 B/条**，却几乎全是 null。

**改法**：
1. 边键降为 `(uint src, uint dst, byte kind, int attrIndex)` = **24 B**。
2. `label` / `ctx` / `callsite` 移入**惰性分配的侧表**，由 `attrIndex` 索引。
3. 序数索引复用既有 `OrdinalNodeList` 模式（`src/NLCPG/Model/OrdinalNodeList.cs`）。

**实测收益**：604.2 MiB → 109.3 MiB（**−81.9%，省 504 MiB**）；328 ms。

**门禁**：G1 的回归测试通过即可（Round 7 已把该风险从「阻塞性」下调为「常规回归」，见研究报告 §17）。

**注意**：`NLCPGContextId?` 是 **16 B 而非 8 B**（`Nullable<T>` 的 `hasValue` + 对齐）；
`NLCPGCallSiteContext?` 是 **32 B**。侧表化时不要按 8/24 B 估算。

---

### 阶段 4：`Materialize()` 去中间数组

**问题**（`NLCPGGraph.cs:875-891`）：每次调用分配整块 `PendingEdge[]`，
`PendingEdge` = **240 B/条**（2×104 + 4 + 3×8 对齐）。2.10 M 条时 = **480 MiB/次**。
共 6 个调用点（研究报告 §14.2 已全部穷举）。

**改法（择一）**：
- 回调式遍历：`Materialize(Action<PendingEdge>)`，不物化整块。
- 复用缓冲：调用方传入可复用数组。
- `ReadOnlySpan` 遍历。

**约束**：`SkeletonShardPublisher.cs:76,:163` 用 `.Select(...).ToArray()`，
需保留可枚举语义（但**不**需保留插入序，见 §2.2）。

**收益**：主要是 churn（480 MiB/次）。**峰值影响未实测**——需先确认是否与其它大块同时存活。

---

## 4. 明确不要做的事（研究已否决，附理由）

| 方案 | 否决理由 |
| --- | --- |
| `FrozenDictionary` | 对 104 B struct 键是**回归**（254.6 → 401.8 B/entry）|
| 开放寻址**重写哈希表**（非去重）| 哈希簿记只占 8/136 = 5.7%，**上限 7%** |
| `CollectionsMarshal.GetValueRefOrAddDefault` | 实测省 **0 B** |
| `ReferenceEqualityComparer` | 本身**0 B**（是替换比较器，非新增字段）|
| 合并 66 个 `IOperation→Node` 字典 | 只省 **3 KB**（payload 总量不变）|
| CSR 化邻接表 | **冻结后已落地**（`NLCPGGraphIndex.cs:137-146`），收益为 **0** |
| 仅存 64 bit 哈希做去重 | 碰撞率 1/840 万 ⇒ **静默丢边** |
| 固定常量预分配容量 | 12 路并发会放大成 ~7.4 GB 长驻（既有文档已否决）|

---

## 5. 回滚点

| 阶段 | 回滚方式 | 风险 |
| --- | --- | --- |
| 1 | 恢复 `PendingEdgeBuffer` 原实现（单文件、自包含）| 低 |
| 2 | 逐字典回退（白名单使改动可分批）| 低 |
| 3 | **最难回滚**——需保留 `attrIndex` → 全字段的还原路径 | 中 |
| 4 | 恢复 `Materialize()` 返回数组 | 低 |

**建议**：阶段 1 单独成一次提交并实测（A4），确认收益后再进入阶段 2。
不要在未验证阶段 1 的情况下连续推进。

---

## 6. 任务清单

```
[ ] G1  节点身份折叠回归测试（见 §2.1）
[ ] G2  确定性回归测试
[ ] G3  确认串行测试跑法
[ ] 1   PendingEdgeBuffer 消重 + 分批预留
[ ] 1v  gcdump A/B 验证阶段 1
[x] 2   可降键字典白名单（已于 Round 8 完成，见研究报告 §18）
[ ] 2b  C/D 类降键且降值
[ ] 3   稀疏属性 + 序数化 payload（依赖 G1 通过）
[ ] 4   Materialize() 去中间数组
[ ] V   整体 gcdump A/B + Contract 串行全绿
```

---

## 7. 验证边界（诚实声明）

1. **本计划尚未执行任何改动**；`src/` 未被修改。
2. 阶段 1/3 的百分比是**子系统稳态占用的实测**（1.5 M 边、复刻结构、独立探针）；
   **不是**端到端峰值。
3. 阶段 2 的 200.1 MB 是**容量模型推算**。
4. 峰值堆必须靠 **gcdump A/B** 证实——这是唯一有效的验收手段（A4）。
5. 数据来源：`docs/benchmarks/data/version4-dop12-type-share-0016.csv`，
   样本 `0016-late-156s`（t=156 s，堆 4,048,479,859 B）。
6. 百分比引用时**必须写明分母**：当前堆 3,861 MB vs 位集优化后投影堆 1,607 MB。

---

## 8. 研究阶段状态（10 轮已完成）

| 项 | 状态 |
| --- | --- |
| 研究轮次 | ✅ **Round 1–10 全部完成**（`docs/plans/...-research.md`）|
| 方案广度 | ✅ 14 项候选经筛选，**5 项通过**、**9 项被否决**（各附实测理由）|
| 待落地方案收益 | ✅ 方案 1/2 **已实测**（−57.6% / −81.9%）；方案 3 为模型推算 |
| 阶段 2 前置（白名单）| ✅ **已完成**（研究报告 §18，7 字典 + 4 构造点全部通过）|
| 关键风险（节点身份折叠）| ✅ **已实测界定**（研究报告 §17），已从阻塞降为常规回归 |
| 峰值窗口全景 | ✅ **100.0000% 对账**（研究报告 §19）|
| 生产代码改动 | ❌ **未开始**——本计划全部条目仍为待办 |

**⇒ 研究目标已达成；下一步是把方案 1 落地并做 gcdump A/B。**



