# NLCPGGraph.AssignDeterministicNodeIds 边重映射去哈希化设计

- 状态：已实施并验证
- 主题：`FreezeQueryIndex` 内 `AssignDeterministicNodeIds` 的第 4 子阶段（边重映射）
- 关联代码：`src/NLCPG/Model/NLCPGGraph.cs`
- 上游文档：`设计docs/历史设计/2026-09-24-NLCPGGraphIndex-CreateSnapshotVersion-哈希吞吐优化设计.md`

## 1. 目标

消除 `AssignDeterministicNodeIds` 中边重映射子阶段的全部哈希开销，且
**保证字节级输出不变**（以 `NLCPGGraph.GraphSnapshotVersion` 为 oracle 逐位核对）。

## 2. 已测事实（全部来自本轮实测，附日志）

### 2.1 `FreezeQueryIndex` 三大阶段拆分

日志：`Build/verify-pass-optimization/freeze-3phase-npc.log`、`assign-phases-npc.log`
语料：`D:\TRbackup\Version4\Terraria\NPC.cs`

| 阶段 | 第 1 次 (ms) | 第 2 次 (ms) | 占 `FreezeQueryIndex` |
| --- | ---: | ---: | ---: |
| `AssignDeterministicNodeIds` | 16,511 | 16,147 | **45%** |
| `NLCPGGraphIndex.Create` | 19,793 | 18,633 | 55% |
| `ReclaimConstructionCapacity` | 12 | 12 | ~0% |
| **合计** | **36,316** | **34,792** | 100% |

两次测量的三阶段之和与 `FreezeQueryIndex` stage 墙钟**逐次精确相等**
（36,316 = 36,316；34,792 = 34,792），故该拆分自洽，无未计入区间。

> ⚠️ 本次立项前我曾据 18 个 `Create` 内部相位之和与 stage 墙钟相减，得出
> 「约 20,500 ms 未计入」的结论。该结论**错误**：那是跨运行负载噪声
> （`Create` 相位与 stage 墙钟来自不同运行）。同一次运行内三阶段即可闭合。
> 记为教训 12：**跨运行相减会凭空造出"缺失时间"**。

### 2.2 `AssignDeterministicNodeIds` 五子阶段拆分

日志：`Build/verify-pass-optimization/assign-phases-npc.log`

| # | 子阶段 | ms | 占本方法 |
| ---: | --- | ---: | ---: |
| 1 | `GetStableAnchor` 投影 | 179 | 1.1% |
| 2 | `NodeIdTable.Create` | 1,644 | 10.2% |
| 3 | 节点重映射 + `ToDictionary` | 915 | 5.7% |
| 4 | **边重映射（`HashSet<NLCPGEdge>`）** | **13,191** | **81.7%** |
| 5 | 重建 `_nodesByNodeId` | 218 | 1.4% |

### 2.3 去重计数（与机器负载无关，本设计的核心判据）

日志：`Build/verify-pass-optimization/dedup-counts-npc.log`

```
去重计数: 枚举边=7,756,984 HashSet丢弃=0
```

即 `HashSet<NLCPGEdge>` 的判重分支在 **7,756,984 条边**上
**一次也没有命中**（丢弃数恒为 0）。

## 3. 根因

`NLCPGGraph.cs` 原实现：

```csharp
var remappedEdges = new HashSet<NLCPGEdge>();
foreach (var edge in _pendingEdges.EnumerateLazily(_nodesByOrdinal))
{
    var sourceNode = remappedNodes[edge.SourceNode.StableAnchor!.Value];
    var targetNode = remappedNodes[edge.TargetNode.StableAnchor!.Value];
    remappedEdges.Add(new NLCPGEdge(...));
}
return remappedEdges;
```

每边三笔开销：

1. **两次 `Dictionary<StableNodeAnchor, NLCPGNode>` 查找**
   （`edge.SourceNode.StableAnchor` / `TargetNode.StableAnchor`），
   而 `PendingEdgeKey` **已带 `SourceOrdinal`/`TargetOrdinal`**
   （`NLCPGGraph.cs:1079-1083`）⇒ 可用数组下标替代。
2. **一次 `HashSet<NLCPGEdge>` 插入**（哈希 + 探测 + 槽位写入）。
3. `NLCPGEdge` 是 6 字段 `readonly record struct`（72 B），
   `HashSet` 按值存 72 B/槽，7,756,984 条 ⇒ 常驻约 560 MB 级。

### 3.1 为什么丢弃数必然为 0（可证，非经验）

`PendingEdgeBuffer.Add`（`NLCPGGraph.cs:966-978`）把每条边登记为
`PendingEdgeKey(SourceOrdinal, TargetOrdinal, Kind, MetadataId)` 并放入 `HashSet<PendingEdgeKey>`
⇒ **`_keys` 内部已按值去重**。

映射 `PendingEdgeKey → NLCPGEdge` 是单射：

| 环节 | 单射性依据 |
| --- | --- |
| `SourceOrdinal`/`TargetOrdinal` → 节点 | `AddNode`（`:139-143`）以 `StableAnchor` 为键，每个锚点只 `_nodesByOrdinal.Add` 一次 ⇒ 序数与锚点一一对应 |
| 锚点 → `NodeId` | `DeterministicNodeIdTable.Create`（`DeterministicNodeIdTable.cs:30-34`）对 `Distinct()` 后的锚点逐个赋 `index+1` ⇒ 单射 |
| `Kind` | 枚举值直传 |
| `MetadataId` | `InternMetadata` 同一 id ⇒ 同一元数据实例三元组 |

⇒ **两条 `PendingEdgeKey` 不同 ⟹ 两条 `NLCPGEdge` 必不同**。
故 `HashSet<NLCPGEdge>` 不可能丢弃任何一条，与 §2.3 实测一致。

> 注：单射性的前提是"节点不会被删除或重新编号"。冻结阶段
> `AssignDeterministicNodeIds` 只做重映射，不删节点；`_nodesByOrdinal` 在此之后才 `Clear()`。

## 4. 设计

### 4.1 改法

以**预分配数组 + 游标**替代 `HashSet<NLCPGEdge>`：

- 容量上界 = `_pendingEdges.Count`（即 `_keys.Count`，精确等于枚举边数，无需猜测）。
- 枚举顺序不变（仍是 `EnumerateLazily`，即 `_keys` 的插入序）。
- 源/目标节点改由**序数数组**直接取得，去掉两次字典查找。
- 返回 `NLCPGEdge[]`（实现 `IReadOnlyCollection<NLCPGEdge>`，`.Count` 契约不变）。

### 4.2 为什么序数数组可行

`_nodesByOrdinal` 是 `List<NLCPGNode>`，由 `Add` 按 `_mutableNodesByAnchor[anchor] = _nodesByOrdinal.Count` 登记
⇒ **`_nodesByOrdinal[ordinal]` 恒为该序数对应的节点**。
节点重映射阶段已产出 `remappedNodes`（按锚点 → 含确定性 `NodeId` 的节点）；
只要再建一份**按序数索引**的 `NodeId[]`，边重映射即可退化为两次数组读取：

```
nodeIdByOrdinal[edge.SourceOrdinal] / nodeIdByOrdinal[edge.TargetOrdinal]
```

该数组长度为节点数（NPC.cs 约 260 万），分配 4 B/节点 ⇒ 约 10 MB 级，远小于省下的 560 MB。

### 4.3 顺带收益（`NLCPGGraphIndex.Create`）

`NLCPGGraphIndex.cs:335`：

```csharp
var edgeArray = edges as NLCPGEdge[] ?? edges.ToArray();
```

改前传入 `HashSet<NLCPGEdge>` ⇒ `as` 恒 null ⇒ **必然 `ToArray()` 再复制一份 72 B/边**。
改后传入数组 ⇒ `as` 命中 ⇒ **该次复制消失**。

## 5. 等价性论证

| 维度 | 改前 | 改后 | 等价理由 |
| --- | --- | --- | --- |
| 边集合 | `HashSet` 去重结果 | 全部枚举边 | §3.1 单射 ⇒ 丢弃数恒 0（实测 0/7,756,984） |
| 枚举顺序 | `_keys` 插入序 → `HashSet` 插入序 | `_keys` 插入序 | 二者同为同一 `_keys` 的无删除枚举；`HashSet` 无删除时枚举序即插入序 |
| 每条边取值 | 由锚点查 `remappedNodes` 得节点 | 由序数查 `nodeIdByOrdinal` 得 `NodeId` | §4.2：序数→节点为双射；`NodeId` 来自同一 `nodeIdTable` |
| 元数据 | `edge.StructuredLabel/ContextId/CallSiteContext` 直传 | 同 | 未改动 |
| `.Count` | `HashSet.Count` | 数组 `Length` | 二者相等（丢弃 0） |

## 6. 验证

### 6.1 oracle（字节级，最强判据）

`NLCPGGraph.GraphSnapshotVersion`（`NLCPGGraph.cs:84` → `NLCPGGraphIndex.SnapshotVersion`）。
它是 SHA256 链，对**节点序、边序、边元数据、节点字段**全部敏感。

- 改前基线：`9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896`
- 改后实测：**逐位相同**。

### 6.2 同二进制交叉 A/B（教训 11：禁止跨运行单样本判定）

用同一份二进制内的 `UseLegacyEdgeRemap` 开关交替切换新旧实现，各跑 2 次。
日志：`Build/verify-pass-optimization/n4ab-NEW-1/2.log`、`n4ab-LEGACY-1/2.log`

| 实现 | 边重映射 (ms) | `Assign` 合计 (ms) | 指纹前 16 位 |
| --- | ---: | ---: | --- |
| **NEW**（数组 + 序数） | **7,127** | **11,836** | `9B59A35AEE4378F6` |
| **NEW** | **4,802** | **8,632** | `9B59A35AEE4378F6` |
| LEGACY（`HashSet`） | 16,848 | 21,559 | `9B59A35AEE4378F6` |
| LEGACY | 14,047 | 17,372 | `9B59A35AEE4378F6` |

- **边重映射加速**：16,848/7,127 = **2.36×**；14,047/4,802 = **2.93×**
- **`Assign` 整体加速**：21,559/11,836 = **1.82×**；17,372/8,632 = **2.01×**
- **指纹 4 次运行全部相同**，且与改前基线逐位一致 ⇒ **字节级等价**
- `Assign` 子阶段降幅（约 9.7 s / 8.7 s）**大于**边重映射本身的降幅
  （约 9.7 s / 9.2 s）不可分辨，故只声称边重映射项的降幅

### 6.3 测试（用户要求：只跑约 10% 核心子集）

```
--filter "FullyQualifiedName~Snapshot|FullyQualifiedName~Freeze|
          FullyQualifiedName~GraphIndex|FullyQualifiedName~ShardingEquivalence"
```

结果：**38 通过 / 0 失败 / 2 跳过**（8 s），与基线一致。

### 6.4 计数判据

`枚举边` = 7,756,984，`HashSet丢弃` = 0（见 §2.3）。

## 7. 风险与回滚

| 风险 | 等级 | 处置 |
| --- | --- | --- |
| 单射性论证有漏洞（存在真实重复边） | 低 | 已用 7,756,984 边实测丢弃 0；若 oracle 不符即回滚 |
| `_pendingEdges.Count` 与枚举数不一致 | 低 | 二者同为 `_keys.Count`；实施时加断言 |
| 并发写入者正在改同文件 | **高** | `NLCPGGraph.cs` 属他人工作流；动手前核对 mtime，改动保持最小 |

**回滚**：`Build/verify-pass-optimization/NLCPGGraph.N4-PRE.bak`
（45,853 B，SHA256 前 16 位 `62B16CAE661E800E`）。

## 8. 诚实边界

- **收益是墙钟，不是内存峰值**：数组替代 `HashSet` 确实省约 560 MB 常驻，
  但本设计**未单独测量峰值 committed**；不得声称降低端到端峰值。
- 本设计的 §2 全部数字来自 `NPC.cs` **单文件**语料，未在多文件联合编译下复测。
- `NLCPGGraphIndex.Create`（占 `FreezeQueryIndex` 55%）**不在本设计范围**。
