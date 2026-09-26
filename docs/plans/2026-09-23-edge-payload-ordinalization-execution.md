# 边载荷序数化与稀疏侧表执行计划

**状态：** **已执行**（2026-09-23）。实际落地为**单阶段**方案（阶段 1+2 合并、
阶段 3 改为元数据 id 留在键内、G4 取消）。实测结果见设计文档 §5.4。

**目标：** 按[边载荷序数化与稀疏属性侧表设计](2026-09-23-edge-payload-ordinalization-design.md)
（下称**设计文档**）落地，把 `NLCPGGraph.PendingEdgeBuffer` 的稳态占用
从 **626.3 MiB** 降到 **77.3 MiB**（**−87.7%**）。

**总账（实测，取代设计文档 §4 的模型推算）**：边子系统 −549.0 MiB、
节点子系统 −29.1 MiB，受控合计 **−578.1 MiB**（设计文档 §5.4）。
**端到端峰值不可得**（两次运行均耗尽本机内存），验收口径为容器级。

---

## 1. 前置门禁

### 1.1 G1 —— 节点身份折叠回归测试（**已完成**）

| 项 | 状态 |
| --- | --- |
| 文件 | `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdentityContractTests.cs` |
| 用例数 | **8**，全部通过（单独串行 3 次稳定，64/78/67 ms） |
| 变异验证 | 已注入 `ExtraKeyId → 0u`，**2 个用例变红**，随后字节级还原 ✅ |
| 结论 | 序数只是锚点的单射编码 ⇒ 去重语义等价（设计文档 §5.2） |

**不需要重跑 G1**，但在阶段 2 完成后**必须重跑**（见 §5 验证矩阵）。

### 1.2 G2 —— 确定性回归测试（**已完成**）

**为什么需要**："同输入 ⇒ 逐字节相同 shard"是硬契约。

> **预期修正（2026-09-23 实测）**：初版写"枚举序 ≠ 插入序"——
> **该前提已证伪**（设计文档 §5.3）：对 **add-only** 的 `HashSet`/`Dictionary`，
> **枚举序 == 插入序**，且**跨进程稳定**（哈希种子改变但枚举序指纹不变）。
> 因此 G2 的**预期结果是应当通过**，而非"可能失败"。
> **门禁仍然必做**——它守的是**端到端** shard 字节级确定性，范围大于枚举序。

**动作**：写测试验证**同一输入连续构建两次**，输出的 shard 字节完全相同。
优先复用既有基建：

- 参考 `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDeterminismTests.cs`
- 参考 `NLCPGNodeIdContractTests.cs:331-340`
  （`BuildFromSource_RepeatedBuilds_PreserveLegacyToNodeIdMapping` 已是同型模式）

**门禁**：两次构建的 **节点行 + 边行全序列**必须逐字符相同。

**实际交付**：`tests/NLISSN.ContractTests/Cpg/PendingEdgeOrdinalizationDeterminismTests.cs`
（**10 用例，全部通过**）。因 `NLCPG` 无 `InternalsVisibleTo`，测试只用公开 API。
**变异验证**：把 `ResolveOrdinal` 强制返回 `0` ⇒ **10 个用例中 2 个变红**，
随后**完全还原** ⇒ 证明该测试确有鉴别力。

### 1.3 G3 —— 串行测试跑法确认（**已完成**）

Contract 套件存在**既有的随机失败**（进程级 `Directory.SetCurrentDirectory()`
与相对路径互相踩踏），**串行**可稳定：

```powershell
$env:TEMP = "$env:USERPROFILE\AppData\Local\Temp\nl-ord"; $env:TMP = $env:TEMP
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj -m:1 -nr:false
```

> **注意**：并行跑会出现 7–14 个**与本次改动无关**的失败，且**失败数随运行变化**
> （实测两次分别为 14 和 7）。判断是否为本改动引入的回归时，
> 必须**比对失败用例名单**，而不是只看失败数。

### 1.4 G4 —— §5.1 前提的封口测试（**已取消，不需要**）

**这是本设计唯一的语义风险点**（设计文档 §5.1）：稀疏侧表要求
**元数据由 `kind` 唯一决定**——即不存在两条 `(源序数, 目标序数, kind)` 相同
但 `label`/`contextId`/`callSiteContext` 不同的边。

**动作**（初版计划）：写测试遍历真实构建的图，断言**对每个 `kind`，
带元数据的边其元数据取值集合与 `kind` 一一对应**；若发现同一
`(端点, kind)` 组合出现不同元数据，测试**必须失败**。

> **✅ 实际实现取消了 G4。** 落地键 = `(2 序数, kind, 元数据 id)`，
> **元数据 id 留在键内 ⇒ 元数据仍参与判重**，与旧键**逐字段等价**。
> 因此 §5.1 的前提**既不需要证明、也不需要门禁**。
> 实测支持：两次运行的边槽容量**完全相同**（`P = 2,893,249`），
> 说明去重结果未变。见设计文档 §5.1、§5.4。

---

## 2. 阶段分解

> **顺序原则**：先拿低风险的大头（阶段 1 + 2），再评估是否追加阶段 3。
>
> **⚠️ 实际执行结果**：**阶段 1 + 2 合并为一次改动**，**阶段 3 未按原方案做**
> （改为元数据 id 留在键内），**阶段 4 未做**。
> 下面保留各阶段原计划以便追溯，实际形状以每节末尾的「实际」块为准。

### 阶段 1：序数分配器（**无收益，纯基建**）—— 已合并进阶段 2

**目的**：为阶段 2 提供 `锚点 → 序数` 映射。

**改动**（`src/NLCPG/Model/NLCPGGraph.cs`）：

| 位置 | 改动 |
| --- | --- |
| L11 | `Dictionary<StableNodeAnchor, NLCPGNode>` → `Dictionary<StableNodeAnchor, (NLCPGNode Node, int Ordinal)>` |
| 新增 | `List<StableNodeAnchor> _anchorByOrdinal` |
| L118-126 | 首次插入时分配 `Ordinal = _anchorByOrdinal.Count` 并追加；**已存在则复用** |
| L38/56/375/734 | 读取 `.Values` 的 4 处适配为 `.Node` |
| L778 | `_mutableNodesByAnchor.Clear()` 后**同步清空 `_anchorByOrdinal`** |

**成本**：`+4 B/槽 × 1,395,263 = +5.3 MiB`（设计文档 §2.4 方案 B）。

**验收**：G1 + G2 全绿；gcdump 确认该字典从 140 → 144 B/槽。

> **实际**：改用 `Dictionary<StableNodeAnchor, int>`（**40 B/槽**）+
> `List<NLCPGNode> _nodesByOrdinal`（**104 B/节点**），
> 因为 `NLCPGNode` 是 **104 B 结构体**，内联进字典值会把槽抬到 144–148 B
> 且仍需一个按序数索引的容器。实测节点侧**净省 29.1 MiB**（不是净增 5.3 MiB）。
> 设计文档 §2.4 的方案 B 推荐**已被实测推翻**。

---

### 阶段 2：序数主键 + 删除 `_items`（**3b，低风险，−82.4%**）

**改动**：

| 位置 | 改动 |
| --- | --- |
| L838-839 | `HashSet<PendingEdgeKey> _keys` + `List<BufferedPendingEdge> _items` → **`Dictionary<EdgeOrdinal, int> _edges`**（value = 稳定边序） |
| L841 | `Count => _edges.Count` |
| L843-873 | `Add(...)` 改为查序数 → 组合 `EdgeOrdinal`（**含元数据 id**，阶段 2 保持元数据在主键） |
| L875-892 | `Materialize()` 从 `_edges` 重建，序数 → 锚点 → 节点 |
| L894-908 | 删除 `PendingEdgeKey` / `BufferedPendingEdge` 两个私有记录 |

**关键决策**：

1. **元数据在主键内**（3b）⇒ **不依赖 G4**，天然正确。
2. **value 存稳定边序**（`int`）⇒ 侧表可后续按 `int` 键接入（阶段 3 无需重构）。
3. **键不含 `string`** ⇒ 确定性无争议（设计文档 §5.3；注：含 string 也已实测稳定）。

**验收**：G1 + G2 全绿；gcdump A/B 显示 `Entry<PendingEdgeKey>`（375.3 MB）
与 `BufferedPendingEdge[]`（240.0 MB）**双双消失**。

> **实际**：改成 **`HashSet<PendingEdgeKey>`（16 B 键 / 24 B 槽）**，
> 无 value（`_items` 已删，无需边序）；元数据存进
> `Dictionary<EdgeMetadata,int> _metadataPool` + `List<EdgeMetadata?> _metadataById`。
> 实测 `Entry[]` = **69,438,000 B**（`2,893,249 × 24 + 24`），
> `BufferedPendingEdge[]` **类型消失**，元数据池 **216 B**。
> 与旧实现的对比（仅这两个容器）：`393,481,888 + 251,658,288 = 645,140,176 B → 69,438,000 B`。

---

### 阶段 3：稀疏属性侧表（**3a，需 G4，−87.7%**）—— **未按原方案实施**

**前置**：**G4 必须通过**。

**改动**：

| 项 | 做法 |
| --- | --- |
| 主键 | `EdgeOrdinal`（12 B）**去掉**元数据 |
| 侧表 | `Dictionary<int, EdgeMetadata>? _metadataByOrdinal`，仅元数据非空时写入 |
| `Materialize()` | 缺失 ⇒ 三字段全 `null`（不变量 I4） |

**验收**：G1 + G2 + G4 全绿；`f` 实测值记录进诊断报告。

> **若 `f > 25%`**：追加设计文档 §3.4 的元数据 id 化（纯增量，不改不变量）。

> **实际**：**未做**。落地方案是 3a 与 3b 的**合并形态**——
> 键 = `(2 序数, kind, 元数据 id)` = 16 B / 槽 24 B，
> **与 3a 同尺寸**，但元数据 id **留在键内** ⇒ 保留 3b 的「语义天然正确」，
> 同时免去侧表复杂度与 G4。**严格占优**，故阶段 3 无独立实施价值。
> 实测元数据池在 t160 仅 216 B（`f ≈ 0`），该阶段的收益空间本来也极小。

---

### 阶段 4：`Materialize()` 去临时数组（**可选，正交**）

**背景**：`Materialize()` 现在每次调用分配 `new PendingEdge[_items.Count]`
≈ **480 MiB/次**，共 **6 个调用点**：

| 调用点 | 用途 |
| --- | --- |
| `NLCPGGraph.cs:44` | `PendingEdges` 属性 |
| `NLCPGGraph.cs:57` | `SnapshotMutableFacts` |
| `NLCPGGraph.cs:763` | `AssignDeterministicNodeIds` |
| `NLCPGBuilder.cs:730` | 分桶进 `List<>` |
| `SkeletonShardPublisher.cs:76` | `.Select(...).ToArray()` |
| `SkeletonShardPublisher.cs:163` | `.Select(...).ToArray()` |

**关键发现**：6 个消费点**全部是只读枚举**（分桶 / 填集合 / 再排序），
**没有一个需要数组**。

**风险**：**不得**返回跨集合变更存活的 lazy enumerable——
`AssignDeterministicNodeIds`（L778）随后 `_mutableNodesByAnchor.Clear()`。
必须用**回调**或**显式快照**。

**状态**：**建议暂缓**。它不影响阶段 1–3 的收益，且属独立风险面。

---

## 3. 回滚点

每个阶段完成后是一个**独立可回滚点**：

| 回滚点 | 内容 | 回滚方式 |
| --- | --- | --- |
| **R1** | 阶段 1 完成（序数分配器） | `git revert` 该提交；G1 不依赖它 |
| **R2** | 阶段 2 完成（序数主键） | 恢复 `PendingEdgeKey`/`BufferedPendingEdge` 与 `_items` |
| **R3** | 阶段 3 完成（侧表） | 把元数据移回主键（退化为 3b） |
| **R4** | 阶段 4 完成（去数组） | 恢复 `new PendingEdge[]` |

**每阶段必须单独提交**，便于二分定位。

---

## 4. 明确不做的事

| 不做 | 理由 |
| --- | --- |
| 自写开放寻址哈希表 | 簿记只占 8/136 B，**上限 ≤7%**；研究报告已否决 |
| 换 `FrozenDictionary` | 104 B 键下 **254.6 → 401.8 B/条**，是**回归** |
| 只降键不降值 | `Dictionary<int,NLCPGNode>` = 342.1 MB，**比现状更差** |
| 固定常量预分配容量 | `dotnet/msbuild#12432` **产品级回滚**（"causes extra allocations in VS"） |
| 仅存 64 位哈希 | P(碰撞) ≈ **1/840 万** ⇒ 静默丢边 |
| `ushort`/`byte` 缩 `kind` | **实测省 0**（`EdgeOrdinal` 与 `EdgeOrdinalByte` 同为 12 B） |
| 用 `ushort` 缩节点序数 | 序数上界 1,395,263 **超出 `ushort`(65,535)**，需 ≥21 bit ⇒ 不能缩 |
| 令 `NodeId` 参与构图期键 | 峰值阶段 `NodeId` **尚不存在**（研究报告 §7.1） |
| 改持久化格式 | 本设计**不动** shard / sidecar / JSON |

---

## 5. 验证矩阵

| 阶段 | 单元 | G1 | G2 | G4 | gcdump A/B |
| --- | :-: | :-: | :-: | :-: | :-: |
| 1 序数分配器 | ✅ | **必须** | **必须** | — | 建议 |
| 2 序数主键 | ✅ | **必须** | **必须** | — | **必须** |
| 3 稀疏侧表 | ✅ | **必须** | **必须** | **必须** | **必须** |
| 4 去数组 | ✅ | ✅ | **必须** | — | 建议 |

**命令**：

```powershell
# 单元（最快）
dotnet test .\tests\NLISSN.UnitTests\ --nologo

# G1 + G2（定向）
$env:TEMP = "$env:USERPROFILE\AppData\Local\Temp\nl-ord"; $env:TMP = $env:TEMP
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --filter 'FullyQualifiedName~NLCPGNodeIdentityContractTests' -m:1 -nr:false

# 快速层（unit + contract）
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
```

**gcdump A/B 是唯一有效的验收手段**——上表的百分比是**子系统稳态占用**，
不是端到端峰值。**本轮已执行容器级 A/B**（设计文档 §5.4），
但**端到端峰值在本机不可得**（两次运行均耗尽 13.86 GiB 内存）。

---

## 6. 任务清单

```text
[x] G1  节点身份折叠回归测试（8 用例，变异已验证）
[x] G2  确定性回归测试（10 用例全绿，变异验证 2/10 变红后还原）
[x] G3  串行测试跑法确认
[-] G4  §5.1 前提封口测试（**已取消**：元数据 id 留在键内，前提不再需要）
[x] 1   序数分配器（实际改用 Dictionary<anchor,int> + List<NLCPGNode>）
[x] 1v  阶段 1 验证（G1 + G2 全绿：Unit 117/117、Contract 458/458）
[x] 2   序数主键 + 删除 _items（16 B 键 / 24 B 槽 + 元数据池）
[x] 2v  阶段 2 gcdump A/B（①② 双双消失：645,140,176 → 69,438,024 B）
[-] 3   稀疏属性侧表（**未做**：合并形态已严格占优）
[-] 3v  阶段 3 gcdump A/B + 记录实测 f（**不需要**）
[ ] 4   Materialize() 去临时数组（可选，仍建议暂缓）
[ ] R7  _nodesByOrdinal 预分配（避免 List 倍增到 2^20，最多可省近一倍）
[ ] R8  冻结后释放 _nodesByOrdinal 后备数组（Clear() 不释放）
[~] V   整体 gcdump A/B（**部分达成**：容器级 A/B 已完成；
        端到端峰值**不可得**——交付构建 OOM，基线被中止）
[ ] A   剩余：HostTests 层、PerformanceTests 层、Run-TestTiers.ps1 -Fast 未跑
```

---

## 7. 验证边界（诚实声明，2026-09-23 更新）

1. **本计划已执行**：改动限于 `src/NLCPG/Model/NLCPGGraph.cs`
   （`git diff --numstat` = `300 94`）。Unit 117/117、Contract 458/458 通过。
2. 收益数字**已由容器级 gcdump A/B 实测**（设计文档 §5.4），
   七项容器尺寸全部被模型**精确复现（残差 0 B）**。
   **基线口径已更正**：真实基线是 `Dictionary` 变体 656,713,196 B（626.3 MiB），
   初版写的 633,567,180 B（604.2 MiB）来自 `HashSet` 变体探针，**低了 23.1 MB**。
3. **`f`（运行时元数据密度）仍未全程实测**。已知的 6.25% 是**调用点比例**，
   **不是**边比例。t160 单点实测元数据池仅 216 B（`f ≈ 0`）。
4. **端到端峰值收益无法取得**：交付构建在 `elapsedMs = 430,517` 时
   `System.OutOfMemoryException`（峰值堆 28,144.5 MB），
   基线运行被人工中止（峰值 24,209 MB）——**两者都超过本机 13.86 GiB RAM**。
   故验收口径是**容器级字节下降**，不是峰值下降。
5. 百分比引用**必须写明分母**：626.3 MiB 是**可变建图期边子系统**，
   不是 3,861 MB（整堆），也不是 1,607 MB（位集优化后投影堆）。
6. Contract 套件有**既有随机失败**（7–14 个，与本次改动无关）；
   判断回归必须**比对失败用例名单**。本轮运行未出现该抖动。

---

## 8. 参考

- [边载荷序数化与稀疏属性侧表设计](2026-09-23-edge-payload-ordinalization-design.md) —— 本计划的依据
- [Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md) —— 事实基础
- [Dictionary 与边缓冲内存优化执行](2026-09-23-dictionary-edge-memory-optimization-execution.md) —— 总计划
- `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdentityContractTests.cs` —— G1
- `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDeterminismTests.cs` —— G2 参考基建
- `src/NLCPG/Model/NLCPGGraph.cs` L836-909 —— 改动主体
