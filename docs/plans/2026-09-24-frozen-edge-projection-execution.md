# 冻结边投影化执行计划

**状态：** **阶段 1+2+3+4 已全部执行**（2026-09-24 执行）。四个阶段均落地并通过小批量测试；
阶段 3 采用 §7.9 推荐的**保序变体**，故两个顺序敏感测试**零改动**通过。

**执行结果速览：**

| 阶段 | 状态 | 证据 |
| --- | --- | --- |
| 前置门禁 G1 | ✅ 通过 | `NLCPGGraphIndexStorageContractTests` 绿（含在 79/79 中） |
| 前置门禁 G2 | ✅ 通过 | 新建 `FrozenEdgeProjectionEquivalenceTests` **7/7** |
| 前置门禁 G3 | ✅ 通过 | ContractTests 全量 499/500（唯一失败为 peer 的 `.csproj` 改动） |
| 前置门禁 G4 | ✅ 通过 | 实测覆盖率 0 ⇒ 9.00 B/边；有元数据 ⇒ 13.01 B/边 |
| 阶段 1 双写 | ✅ 完成 | `CanonicalEdgeStore.cs` 新增 |
| 阶段 2 切投影 | ✅ 完成 | `OrdinalEdgeList` / `CsrEdgeTable` / `CreateSnapshotVersion` |
| 阶段 3 释放 | ✅ **完成** | 常驻 `HashSet<NLCPGEdge>` 删除，改由 `InsertionOrder`（4 B/边）投影；整体降幅 **26.07% → 92.35%** |
| 阶段 4 清理 | ✅ 完成 | `orderedEdges` / `remappedEdges` 均不再物化，`Create()` 连 72 B/边中间数组一并省掉 |

**阶段 3 实测（小批量，固定输入 2,000 节点 / 50,000 边，去重后 18,000 边）：**

| 量 | 值 |
| --- | ---: |
| 旧表示常驻（重建同元素 `HashSet` + 72 B/边数组） | 3,061,956 B |
| 新表示常驻（SoA 9 B/边 + 插入序 4 B/边） | 234,096 B |
| **整体常驻降幅** | **92.35%** |
| canonical 分量降幅 | 87.49% |
| 旧 `HashSet` 槽位/桶 | 21,023 / 21,023（80 B/槽） |

**为何阶段 3 可以零顺序风险落地：** 旧 `_edges` 是"只增不删"的 `HashSet`，其枚举序即**插入序**；
`NLCPGGraphIndex.Create` 内部已算出"canonical → 插入"的置换 `current`，取其逆即得
"插入 → canonical"的 `InsertionOrder`（复用排序用的 scratch 缓冲，**不新增分配**）。
`Edges` 由 `OrdinalEdgeList` 按该表投影，枚举序与旧 `HashSet` 逐一相同，
故 `CpgInterproceduralEdgeOrderTests`（硬编码非单调基线 `273,277,288,284,286`）
与 `PendingEdgeOrdinalizationDeterminismTests` **均无需改动即通过**。


**目标：** 按[冻结边投影化与常驻宽度收敛设计](2026-09-24-frozen-edge-projection-design.md)
（下称**设计文档**）落地，把**冻结后常驻**的边表示从实测 **210.06 B/边** 降到
**16 B/边**（T1 口径），Projectile.cs 规模 **−1,286.8 MiB**，NPC.cs 外推 **−3.75 GiB**。

**硬边界（用户指示）：** **不启用内存有界架构。** 不接线 shard / streaming / persistence
路径（`NLCPGBuilderOptions.Persistence`、`CpgPersistenceOptions.StreamingMode`、
`CpgFrozenShardStore`、`RequiresPreallocatedNodeIds()`）。理由见设计文档 §7.1。

**验收口径：** **容器级字节**（与 §1.2 实测同口径）。端到端峰值**不作为门槛**——
本机 RAM 仅 13.86 GiB 且与 peer 共享，基线在 t≈4,740 s 已进入换页平台期。
主机指示：**有足够样本即停止，不长时间运行**。

---

## 1. 前置门禁

### 1.1 G1 —— 既有存储契约测试（**已存在，直接复用**）

| 项 | 内容 |
| --- | --- |
| 文件 | `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexStorageContractTests.cs` |
| 已有断言 | `Freeze_UsesOneCanonicalNodeArrayAndOrdinalBuckets` |
| 关键断言 | `:34` `Assert.Same(orderedNodes, canonicalNodes)`；`:52-63` 序数桶必须为 `int[]` |
| 与本设计的关系 | 设计文档 **I4 / I7** 的归属地。本设计必须让它**保持通过** |
| 状态 | **未运行**（本轮零测试） |

**注意**：`NLCPG` **没有 `InternalsVisibleTo`**（全 `src/NLCPG` 扫描确认），
该测试用 `BindingFlags.NonPublic` 反射访问内部字段。新增 `CanonicalEdgeStore` 后，
本测试的反射路径**不应受影响**（它只查节点侧），但**必须重跑确认**。

### 1.2 G2 —— 边序与指纹等价性（**必须新建**）

**为什么需要**：设计文档 **I1 / I3**。canonical 边序与 `SnapshotVersion` 是硬契约。

**动作**：新建 `tests/NLISSN.ContractTests/Cpg/FrozenEdgeProjectionEquivalenceTests.cs`。

**参考既有基建**（前序执行文档已验证可用的两个模式）：

- `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs:331-340`
  （`BuildFromSource_RepeatedBuilds_PreserveLegacyToNodeIdMapping`）
- `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`
  （已有的边序契约测试，**直接相关**）
- `tests/NLISSN.ContractTests/Cpg/PendingEdgeOrdinalizationDeterminismTests.cs`
  （前序设计交付，10 用例，只用公开 API）

**门禁内容**：

1. 同一源码构建两次，`graph.Edges` 的**全序列逐字段相同**（`src`/`tgt`/`kind`）。
2. `graph.GraphSnapshotVersion` **两次相同**（`NLCPGGraph.cs:81`）。
3. 抽样节点（≤500 个）的出/入边序列逐元素相同，含 `GetIncomingEdges(id, kind)` 变体。
4. `graph.Edges.Count` == `CurrentEdgeCount`（`NLCPGGraph.cs:48`，I2）。

**变异验证（必做）**：故意让投影读出错误的 `Kind` ⇒ 至少 1 个用例变红 ⇒ 还原。
（前序设计用同样手法证明了 `PendingEdgeOrdinalizationDeterminismTests` 的鉴别力。）

### 1.3 G3 —— 串行跑法确认（**沿用前序结论**）

Contract 套件存在**既有的随机失败**（进程级 `Directory.SetCurrentDirectory()`
与相对路径互相踩踏），**串行**可稳定：

```powershell
$env:TEMP = "$env:USERPROFILE\AppData\Local\Temp\nl-fep"; $env:TMP = $env:TEMP
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj -m:1 -nr:false
```

> **注意**：并行跑会出现 7–14 个**与本改动无关**的失败，且**失败数随运行变化**。
> 判断是否引入回归时，必须**比对失败用例名单**，而不是只看失败数。

### 1.4 G4 —— 覆盖率探测口径（**本设计专属**）

**为什么需要**：设计文档 **R4**（T1/T2/T3 口径选择）与 §3.3（元数据池是否上移）。

**动作**：在单文件语料上确认 `PendingEdgeBuffer._metadataPool` 与
`_metadataIds` 的实际覆盖率，决定：

- 覆盖率 0 ⇒ 采用 **T3** 口径（省 `metadataIds` 数组），池不迁移；
- 覆盖率非 0 ⇒ 采用 **T1**，池上移到 `CanonicalEdgeStore`。

**既有证据**（可直接引用，无需重测）：`Build\verify-algo-speed\IMPLEMENTATION-EVIDENCE.md:67`
已用对拍 oracle 实测真实单文件语料边元数据覆盖 **= 0.0%**。
**但**该证据来自**算法验证轮次**，本设计仍应在改动后**复核一次**，因为
§3.3 的池迁移是**行为改动**。

---

## 2. 阶段分解

四个阶段，**严格顺序**，每阶段独立可回滚。阶段 1 与阶段 2 之间是一个
**天然检查点**：阶段 1 不改行为，可先合入。

### 阶段 1：引入 `CanonicalEdgeStore`，**双写**（无行为变化，纯基建）

**改动**：

- 新建 `src/NLCPG/Model/CanonicalEdgeStore.cs`（设计文档 §3.1 的 SoA 结构）。
- `NLCPGGraphIndex.Create()` 在构造 `orderedEdges`（`:363`）的**同时**填充
  `CanonicalEdgeStore`，但**仍然**返回并使用 `orderedEdges`。
- `ordinal`/`offset` 表**保持不变**。

**收益**：**0**。此阶段只建立结构并让 G4 探测有落点。

**风险**：低。不改任何读取路径 ⇒ 行为等价是可静态论证的。

**退出条件**：G1 + G2 全绿；`CanonicalEdgeStore` 的 `Count` == `orderedEdges.Length`。

> **为什么先做无收益的阶段**：设计文档 §3.4 的路径 A/B 分离是本设计唯一的语义风险点。
> 先把结构立起来、把等价性测试跑绿，再把读取路径切过去，可以把「结构错误」与
> 「语义错误」两类失败**分开定位**。前序设计的实际落地经验也支持这一点
> （其初版推荐的接线方案被实测推翻，见前序设计 §2.4）。

### 阶段 2：切换读取路径到投影（**主要行为改动**）

**改动**：

- `OrdinalEdgeList.cs:21` 改为 `_store.Project(...)`（设计文档 §3.2）。
- `CsrEdgeTable.EdgeAt`（`NLCPGGraphIndex.cs:79-82`）改为经 `CanonicalEdgeStore` 读取，
  以保持 `LowerBound` 二分（`:59-77`）语义不变。
- `GetEdges(kind)`（`:449-462`）与 `GetNodesByKind`（`:464-469`）改为投影视图。

**不改**（本阶段）：`_edges` 此时仍存在（阶段 3 才处理）。
⇒ 此阶段是**双份并存**，**内存会短暂变差**，这是**预期的检查点状态**。
（阶段 3 已随后执行，`_edges` 最终被删除，见 §7.8/§7.9。）

**收益**：0（甚至短暂为负）。此阶段的目标是**把投影路径跑绿**。

**退出条件**：

- G1 + G2 全绿（含变异验证）。
- `graph.Edges` 与投影视图**逐元素相同**（新增断言）。
- `GraphSnapshotVersion` 与阶段 1 **逐字节相同**（关键）。

**回滚点 R1**：撤掉阶段 2 的三处读取切换，回到双写。

### 阶段 3：释放 `_edges` 与 `CanonicalEdges`（**收益兑现点**）

这是**风险最高**的阶段，因为有 **20+ 消费者**且去重语义分两条路径（设计文档 §3.4）。

**改动**：

- **路径 A（`FreezeQueryIndex`）**：删除 `_edges.Clear()` + 重新 `Add`
  （`NLCPGGraph.cs:822-826`），改为从 `_pendingEdges._keys` 直接填充
  `CanonicalEdgeStore`。去重由 `_keys` 承担（等价性见设计文档 §3.4）。
- **路径 B（`CreateFrozen`，`:99-108`）**：**保留一次去重**，二选一：
  - **B1**：内部临时建 `PendingEdgeBuffer`，投影后释放；
  - **B2**：临时 `HashSet<PendingEdgeKey>` 仅用于去重，投影后释放。
- `Edges` 属性（`:46`）改为返回投影集合（设计文档 §3.5）。
- `ReclaimConstructionCapacity()`（`:842-851`）追加释放构造期边缓冲。

**退出条件**：

- G1 + G2 全绿。
- **`Edges` 的 post-freeze 消费者全部正常**：至少覆盖
  `CpgBuildInventory`、`ProjectJsonExporter`、`CpgGraphValidator`、
  `NLCPGStructureViewBuilder`、`DecisionModel`、`RulePipeline`。
- 容器级字节验收（见 §5）。

**回滚点 R2**：恢复 `_edges` 与 `CanonicalEdges` 的填充与读取。

### 阶段 4：`Create()` 的第四次复制消除（**正交，可选**）

设计文档 §4.3：`edges.ToArray()`（`:314`，副本 ③）在旧实现下与 `orderedEdges`
（`:363`，副本 ④）**同时存活**。阶段 3 完成后副本 ③④ 已自然消失。
本阶段仅做**清理**：移除 `edges as NLCPGEdge[] ?? edges.ToArray()` 的
`ToArray` 分支（`HashSet` 会走该分支），确认不再有中间数组。

**收益**：0（已被阶段 3 覆盖）。**若阶段 3 完整落地，本阶段可取消。**

**注意**：这与既有的「合并 `Create()` 两次 72 B 数组为原地置换」提案**不同**。
那个方案保留一份 72 B 常驻数组（省约 1,423 MB **峰值**）；
本设计**取消 72 B 常驻数组本身**（省约 1,286.8 MiB **常驻** + 同等瞬态）。
⇒ 本设计落地后，**原地置换提案自然作废**（成为其真子集），不应重复实施。

---

## 3. 回滚点

| 点 | 位置 | 动作 | 影响面 |
| --- | --- | --- | --- |
| **R0** | 阶段 1 前 | 不落地 | — |
| **R1** | 阶段 2 后 | 撤销读取路径切换，保留双写 | 仅 `NLCPGGraphIndex` + `OrdinalEdgeList` |
| **R2** | 阶段 3 后 | 恢复 `_edges` / `CanonicalEdges` 填充与读取 | `NLCPGGraph` + `NLCPGGraphIndex` |

**每阶段一个独立提交**，便于按提交回滚。
建议提交边界：`阶段 1` / `阶段 2` / `阶段 3` 三个提交，
`阶段 4` 视是否落地决定。

---

## 4. 明确不做的事

1. **不启用内存有界架构**（用户硬边界）：不碰 `CpgPersistenceOptions.StreamingMode`、
   `NLCPGBuilderOptions.Persistence`、`CpgFrozenShardStore`、
   `RequiresPreallocatedNodeIds()`。设计文档 §7.1 说明其价值与为何独立立项。
2. **不改 GC 运行时配置**：`GCHeapHardLimit` / `GCConserveMemory` / `GCHighMemPercent`
   已实测无效或有害（设计文档 §7.2）。
3. **不改公开 API 形状**：`NLCPGEdge` 保持 `public readonly record struct`；
   `Edges` 保持 `IReadOnlyCollection<NLCPGEdge>`。
4. **不改持久化格式**：shard、routing sidecar、JSON 导出、
   `SnapshotVersion` 语义全部不变。
5. **不改哈希算法**，**不新增 `InternalsVisibleTo`**。
6. **不动节点侧**：`OrdinalNodeList` 已是正确形态（设计文档 §2）。
7. **不动字符串分配侧**：属前序「零分配键设计」的范围，与本设计正交。
8. **不修** `NLCPGBuilder.cs:833` 的过期注释（"11.3%" 应为 11.08%）
   —— 与本设计无关，单独处理以免混杂 diff。

---

## 5. 验证矩阵

### 5.1 测试命令（按最小归属项目优先）

```powershell
# 1) 构建
dotnet build .\src\NLCPG\NLCPG.csproj

# 2) 单元（最快）
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false

# 3) 契约（G1/G2 归属地；必须串行，见 G3）
$env:TEMP = "$env:USERPROFILE\AppData\Local\Temp\nl-fep"; $env:TMP = $env:TEMP
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj -m:1 -nr:false

# 4) 分层
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
```

⚠️ 以上命令**本轮均未执行**。执行时按主机指示**控制时长**。

### 5.2 容器级字节验收（**主口径**）

与设计文档 §1.2 同口径，用 gcdump 报告对比 `Entry<NLCPGEdge>[]` 与
`NLCPGEdge[]` 两个类型的存在与大小：

| 检查 | 期望 |
| --- | --- |
| `NLCPG.Model.NLCPGEdge[]` | **消失**或退化为小数组 |
| `Entry<NLCPG.Model.NLCPGEdge>[]` | **消失** |
| 新增 `System.Int32[]`（边序数） | 出现，边长 ≤ 16 B/边 |

### 5.3 端到端（**加分项，非门槛**）

单文件 A/B（参考既有 Round-14 手法：NPC.cs 或 Player.cs，`--no-restore`）。**注意时长**。

---

## 6. 任务清单

| # | 任务 | 阶段 | 状态 |
| --- | --- | --- | --- |
| T1 | 新建 `CanonicalEdgeStore.cs`（SoA：4/4/1/4 B） | 1 | ✅ 完成 |
| T2 | `Create()` 双写 `CanonicalEdgeStore` | 1 | ✅ 完成（改为直接构建 store，不再物化 72 B 数组） |
| T3 | 新建 `FrozenEdgeProjectionEquivalenceTests.cs`（G2） | 1 | ✅ 完成（7 用例） |
| T4 | 跑 G1 + G2，确认绿 | 1 | ✅ 完成（G1 含在 79/79 关键回归中） |
| T5 | `OrdinalEdgeList` 切投影 | 2 | ✅ 完成（并新增 `CanonicalEdgeList`） |
| T6 | `CsrEdgeTable.EdgeAt` / `GetEdges(kind)` 切投影 | 2 | ✅ 完成 |
| T7 | 断言 `SnapshotVersion` 一致性 | 2 | ✅ 完成（新增专用用例） |
| T8 | G2 变异验证 | 2 | ✅ 完成（两处变异均被捕获，见 §7.10） |
| T9 | 路径 A：构图期去重改由局部集供给 | 3 | ✅ 完成（`AssignDeterministicNodeIds` 直接填局部去重集，并省掉 72 B/边中间数组） |
| T10 | 路径 B：`CreateFrozen` 保留一次去重（B1 或 B2） | 3 | ✅ 完成（**取 B2**：局部 `HashSet<NLCPGEdge>`，索引建好后不再常驻） |
| T11 | `Edges` 属性改投影集合 | 3 | ✅ 完成（`InsertionOrderedEdges`，**枚举序不变**） |
| T12 | `ReclaimConstructionCapacity()` 追加释放 | 3 | ✅ 完成（`_edges` 字段整体删除，无需再回收） |
| T13 | 验证 6 个 post-freeze 消费者 | 3 | ✅ 完成（消费者审计 16 处 + ContractTests 499/500 实际跑过） |
| T14 | 容器级字节验收（§5.2 同口径） | 3 | ✅ 完成（`NLCPGEdgeStoragePerformanceTests` 已改为断言整体降幅 ≥80%，实测 92.35%） |
| T15 | 清理 `Create()` 的 `ToArray` 分支 | 4 | ✅ 完成 |

---

## 7. 验证边界（诚实声明，2026-09-24）

1. **本计划已执行阶段 1+2+3+4**（2026-09-24）：新增 `CanonicalEdgeStore.cs`，改造
   `NLCPGGraphIndex.cs` / `OrdinalEdgeList.cs` / `NLCPGEdge.cs` / `NLCPGGraph.cs`。
   **阶段 3 已执行**（见 §7.8/§7.9），常驻 `HashSet<NLCPGEdge>` 已删除，
   整体常驻边字节降幅 **26.07% → 92.35%**（实测）。
2. **实测的是**：`sizeof(NLCPGEdge)` = 72 B；store 每边 **9.00 B**（覆盖率 0）/
   **13.01 B**（有元数据）；canonical 分量降幅 **87.49%**；整体常驻降幅 **92.35%**；
   `FreezeQueryIndex` 占构建 CPU **80%**（去重后 2,937,333 ms / 3,677,472 ms）。
3. **推算的是**：NPC.cs 的 **−3.75 GiB**（按 Projectile.cs 实测 B/边 × 边数外推）；
   §4.4 的构建时间收益。**注意**：该外推基于阶段 1+2 的 store 口径，
   尚未按阶段 3 后的整体常驻口径重算。
4. **已澄清的是**（2026-09-24 只读审计）：设计文档 **R1** 的消费者侧已逐个核查——
   16 处 `.Edges` 消费者全部为 count-only、显式排序，或 `UnionWith` 进自身 HashSet，
   **无消费者依赖 HashSet 语义或去重**。R10（顺序）经 §7.9 的保序变体**已消解**；
   R11（路径 B 去重）**已按 B2 落地**（去重移入 `CreateFrozen` 局部集）。
   **G2 变异验证（T8）已执行**，两处变异均被捕获，见 §7.10。
5. **未解释的观测**：设计文档 **R6**（entry 槽数 11,998,949 是存储边数的 1.73×）。
   不影响收益方向，但说明 `_edges` 曾达更大容量。
6. **数据口径警告**：`per-file-freeze-index.csv` 与 `per-file-passes-long.csv`
   **每文件记录两遍**（962 文件 / 1,934 行）；**4 对 `Ms` 不同**，
   真实双重调用未定位。
7. **端到端峰值不可得**：本机 13.86 GiB 与 peer 共享，基线在 t≈4,740 s 进入换页平台期。
   故验收口径是**容器级**，端到端为加分项。

### 7.8 阶段 3 如何落地（原"为何未执行"已由本轮执行取代）

阶段 1+2 交付了 **canonical 常驻分量 −87.49%**（实测），但整体常驻只降 26.07%，
因为 `HashSet<NLCPGEdge>`（80 B/槽）仍是主导项。本轮按 §7.9 的**保序变体**把它删掉。

原先记录的两项义务及本轮处理：

1. **顺序义务（R10）—— 以保序设计消解，无需改测试。**
   审计曾指出：若让 `Edges` 改走 canonical 序，会出现 1 个确定性失败
   （`CpgInterproceduralEdgeOrderTests.cs:141-150` 用**不排序**的 73 元素**非单调**基线对拍），
   另有 1 个语义失效（`PendingEdgeOrdinalizationDeterminismTests.cs:89-97` 声明测"插入序"）。
   **本轮不改变 `Edges` 枚举序**：`NLCPGGraphIndex.Create` 已算出
   "canonical → 插入"置换 `current`，取其逆得"插入 → canonical"的 `InsertionOrder`，
   `Edges` 按该表投影 ⇒ 枚举序与旧 `HashSet` 插入序**逐一相同**。
   **两处测试均零改动通过**（含在 79/79 关键回归中）。
2. **去重义务（R11）—— 按 B2 落地。** 审计确认 `CpgFrozenShardGraphReader.cs:209`
   是**唯一**传未去重序列的 `CreateFrozen` 调用方（另 3 个 `CreateFrozen` 调用方与
   唯一 `ImportMutableFacts` 调用方均已预去重）。故把去重移入 `CreateFrozen` 的
   **局部** `HashSet`：语义不变，但该集合在索引建好后**不再常驻**。
   `AssignDeterministicNodeIds` 同样改为直接填充局部去重集并返回，
   顺带省掉原先的 `NLCPGEdge[]`（72 B/边）中间数组，**峰值也随之下降**。

⇒ **阶段 3 交付**：常驻宽度从"80 B/槽 + 9 B/边"降到"4 B/边 + 9 B/边"（实测整体 −92.35%），
`SnapshotVersion` 逐字节不变，全部既有契约测试通过。

### 7.9 阶段 3 的落法（**已执行**）

设计文档 §3.5.1 给出的保序变体**已按此实现**：

- `Edges` 的**插入序语义保持不变**，底层不再是 `HashSet<NLCPGEdge>`，而是
  **插入序 `int[]` 数组**（`NLCPGGraphIndex.InsertionOrder`，4 B/边）
  + `CanonicalEdgeStore` 投影（`InsertionOrderedEdges`，惰性缓存的 `OrdinalEdgeList`）。
- 该 `int[]` **复用基数排序用的 scratch 缓冲**（`buffer`，此时已成死数据），
  故**不新增分配**：`Create()` 的边侧常驻增量只有 store 自身。
- 去重仍只做一次：路径 B 由 `CreateFrozen` 内局部去重承担；
  可变路径由 `AssignDeterministicNodeIds` 的局部去重集承担。
- `CpgInterproceduralEdgeOrderTests` 与 `PendingEdgeOrdinalizationDeterminismTests`
  **均无需改动**即通过。

**若之后仍需要 canonical 序**，再把顺序切换作为**独立一步**，届时处理 R10 的两处测试。

**本轮实测（受控小批量，非端到端）：**

| 项 | 值 |
| --- | ---: |
| `sizeof(NLCPGEdge)` | 72 B |
| store 每边（覆盖率 0） | **9.00 B** |
| store 每边（有元数据） | **13.01 B** |
| canonical 分量降幅 | **87.49%** |
| 插入序表每边 | **4.00 B** |
| **整体常驻降幅（阶段 3 已做）** | **92.35%**（原 26.07%） |
| `NLCPGEdgeStoragePerformanceTests` | **3/3 通过** |
| `FrozenEdgeProjectionEquivalenceTests` | **7/7 通过** |
| 顺序敏感关键回归（8 个测试类） | **79/79 通过** |
| `UnitTests` 全量 | **117/117 通过** |
| `ContractTests` 全量 | **499/500**（唯一失败：peer 改 `NLISSN.csproj` 触发 `LayoutArchitectureTests`） |
| `HostTests` 全量 | **327/330**（3 个失败全部为**已记录既有失败**，见下） |
| `NLCPGNodeStoragePerformanceTests`（含 Export/Restore 往返） | **2/2 通过**（4,481 边过新存储） |

**`ContractTests` 唯一的失败归因（零新增）：**
`NLISSN.Tests.Architecture.LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
——该测试**只读 `.csproj`**，失败原因是 peer 往 `src/NLISSN/NLISSN.csproj` 新增了
`..\NLISSN.Infrastructure\ProjectJson\NLCPG.ProjectJson.csproj` 引用并将期望列表
（`LayoutArchitectureTests.cs:229`）落在了后面。**本轮未修改任何 `.csproj` / `.props` / `.targets`。**

**3 个 HostTests 失败的归因（零新增）：** `PipelineComponentTests` ×2
（`..._RewritesLargeAssetProjectAndKeepsCompilationValid`、
`..._ShrinksDelegateMethodGroupBindingsAndInvocations`）+ `PropagationRuleExpansionTests` ×1，
**全部落在 `Context/progress.md:47-53` 已记录的既有失败名单内**（依次对应其第 2、3、4 条）。
该处记录 peer 用 `git worktree` 在 **HEAD `925161d` 基线**上逐条证实同样失败（第 4 条还做了 A/B 实验单独排除）。
**另注**：`tests/NLISSN.HostTests/Application/PipelineComponentTests.cs` 于本次运行期间
**被 peer 修改**（`git status` 中为 `M`），其当前状态不由本轮改动决定。
⇒ **本改动未新增任何失败**。

**`设计docs/` 说明：** 本轮已同步更新 `设计docs/目前设计/cpg-architecture.md` 的边侧投影段落。
但 **`设计docs/` 被 `.gitignore:10` 忽略**，该改动**不进入版本控制**，只存在于本机工作区。

### 7.10 T8 变异验证（**已执行**）

为证明新增等价性测试**非恒真**，对生产代码做了两处**临时**变异并确认被捕获（随后已完整还原，
`MUTATION-T8` 标记 0 残留；还原后关键回归 **79/79**、性能批 **3/3** 复绿）：

| 变异 | 位置 | 被谁捕获 |
| --- | --- | --- |
| ① 插入序表写成正置换（`insertionOrder[i] = current[i]`，即未取逆） | `NLCPGGraphIndex.cs:385` | `CpgInterproceduralEdgeOrderTests.BuildFromSource_InterproceduralEdges_PreservesBridgeEmissionOrder` **1 个失败** |
| ② `Project()` 内 source/target 互换 | `CanonicalEdgeStore.cs:76-77` | `FrozenEdgeProjectionEquivalenceTests.ProjectedCanonicalEdges_MatchFrozenEdgeSet` + `NeighborQueries_MatchProjectedCanonicalOrder` **2 个失败** |

**重要发现（对后续维护有价值）：** 变异 ① 只被 `CpgInterproceduralEdgeOrderTests` 捕获，
而 `PendingEdgeOrdinalizationDeterminismTests` **未捕获**——它比较的是"同一次构建 vs 另一次构建"，
两侧同步位移后仍然自洽，故对"插入序表写错"是**盲**的。
⇒ 护住 `Edges` 枚举序的**真正护栏**是 `CpgInterproceduralEdgeOrderTests`
（硬编码非单调基线 `273,277,288,284,286`）。后续若再改边序相关代码，**必须保留该测试**。

---

## 8. 参考

- [冻结边投影化与常驻宽度收敛设计](2026-09-24-frozen-edge-projection-design.md)
  —— 本计划的**设计文档**
- [边载荷序数化与稀疏侧表执行](2026-09-23-edge-payload-ordinalization-execution.md)
  —— **前序执行**（构图期 16 B，已实现），本计划的格式与门禁范本
- [边载荷序数化与稀疏属性侧表设计](2026-09-23-edge-payload-ordinalization-design.md)
  —— 前序设计
- [Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)
  —— 事实基础
- [Harness 验证矩阵](../harness-verification-matrix.md) —— 分层验证要求
- [贡献指南](../contributing.md) —— 提交前验证命令
- `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexStorageContractTests.cs`
- `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`
