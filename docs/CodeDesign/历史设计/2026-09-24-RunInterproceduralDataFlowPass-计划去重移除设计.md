# NLCPGBuilder.RunInterproceduralDataFlowPass 计划去重移除设计

- 状态：已实施并验证
- 主题：`RunInterproceduralDataFlowPass` 中逐调用点发布的组内计划去重
- 关联代码：`src/NLCPG/Builder/NLCPGBuilder.cs`
- 上游文档：`Build/10pass-optimization-plan.md` §6.2.3（N2）

## 1. 目标

移除 `PublishCallSitePlans` 中**恒不命中**的组内计划去重，且**保证字节级输出不变**
（以 `NLCPGGraph.GraphSnapshotVersion` 为 oracle 逐位核对）。

## 2. 已测事实（本轮实测，附日志）

语料：`D:\TRbackup\Version4\Terraria\NPC.cs`

### 2.1 `RunInterproceduralDataFlowPass` 内部拆分

日志：`Build/verify-pass-optimization/n2-split*-npc.log`

| 阶段 | ms | 占比 |
| --- | ---: | ---: |
| 1 建 4 个待定边索引（串行） | 939–1,410 | 3.0–3.6% |
| 2 预处理（方法边界/调用点排序） | 26–65 | 0.1–0.2% |
| **3 调用点循环** | **30,505–38,116** | **96.3–96.9%** |
| 调用点数 | 2,769 | — |
| 最长单次 | 251–937 ms | — |
| **Amdahl 并行上限**（总量÷最长单元） | **40.7–121.6 核** | — |

### 2.2 调用点循环内部拆分（**本轮最关键的修正**）

| 子项 | ms | 占循环 |
| --- | ---: | ---: |
| 目标解析（`callTargetEdgesBySource` → targets） | **20–30** | **0.1%** |
| 发布 `PublishCallSitePlans` | **30,396–31,754** | **99.5–99.6%** |
| ├ 去重 + 组内排序 | **15,193–15,722** | **49.4–50.0%** |
| └ `AddEdge` 写共享图 | **12,856–13,568** | **42.3–42.7%** |

> ⚠️ **这一拆分推翻了我原先对 N2 的判断**：我原以为 N2 的并行机会在"逐调用点解析目标"，
> 实测只有 **0.1%**。把调用点循环接进调度器（原 N2 计划）几乎无收益。
> 真正的大头是**发布**，其中约一半是**纯局部的去重+排序**（可并行），
> 另一半是**必须串行的共享图写入**。

### 2.3 去重计数（与负载无关，本设计的核心判据）

日志：`Build/verify-pass-optimization/n2-dedup-npc.log`

```
去重计数: 尝试=3,954,144  丢弃=0  保留=3,954,144
```

即 `seenPlans`（`HashSet<InterproceduralDataFlowPlan>`）判重分支在 **3,954,144 条计划**上
**一次也没有命中**。

## 3. 根因

`PublishCallSitePlans` 原实现：

```csharp
foreach (var plan in callSitePlans)
{
    if (!seenPlans.Add(plan))   // 恒不命中
    {
        continue;
    }

    sortRows.Add(new PlanSortRow(..., sortRows.Count));
}
seenPlans.Clear();
```

每计划一笔开销：**一次完整结构哈希 + 相等性比较**
（`InterproceduralDataFlowPlan` 含 4 个 `NLCPGNode`（各 104 B）+ `BridgeKind` + 2 个 int）。

### 3.1 为什么丢弃数必然为 0（结构性，非语料巧合）

**真正的去重发生在下游**：`NLCPGGraph.AddEdge` → `PendingEdgeBuffer.Add` →
`HashSet<PendingEdgeKey>`，其键为
`(SourceOrdinal, TargetOrdinal, Kind, MetadataId)`——**比本处按整条计划去重更强**。

本处的 `seenPlans` 只比下游多覆盖一项区分度：`StableCallSiteOrder`。但
`callSitePlans` 是**逐调用点新建**的（循环内 `new List<...>`），组内所有计划的
`StableCallSiteOrder` 恒等于同一个 `callSiteOrder` ⇒ 该项在组内**不提供任何区分**。

⇒ 组内两条计划相等 ⟺ 下游键相等 ⇒ 本处丢弃的任何计划，下游也必然丢弃。
**故删除本处去重不改变最终边集。**

## 4. 设计

### 4.1 改法

删除 `seenPlans`（连同其字段、`Clear()`、以及 `PublishCallSitePlans` 的该参数）。
`sortRows.Count` 仍作为末尾平局键写入 `PlanSortRow`。

### 4.2 为什么排序必须保留

`NLCPGGraphIndex.Create` 的 LSD 基数排序虽用计数排序实现（**稳定**），但它的稳定性
只保证"`metadataRank` 相同的边保持输入序"。而 `metadataRank` 是把元数据**按值**去重求秩
得到的（见上游设计），**按值相等但引用不同的元数据实例会得到同一 rank**。

⇒ 若删除组内排序，这些并列边在最终边序中将退化为**输入序**，而输入序由
`callSitePlans` 的构造顺序决定。保留组内排序（含 `sortRows.Count` 末键构成严格全序）
可使组内顺序**完全确定、与插入序无关**，从而 `Create` 的稳定排序拿到确定的输入序。

**本设计只删除去重，不触碰排序。**

## 5. 等价性论证

| 维度 | 改前 | 改后 | 等价理由 |
| --- | --- | --- | --- |
| 最终边集 | `seenPlans` 去重后 AddEdge | 全部计划 AddEdge | §3.1：本处丢弃的必被下游丢弃 |
| 组内顺序 | 首次出现序 → 排序 | 全部项 → 排序 | 去重不命中 ⇒ 序列逐项相同；排序键含 `sortRows.Count` 构成严格全序 |
| 计划计数 | 3,954,144 | 3,954,144 | 丢弃为 0（实测） |
| 边序 | 由 `Create` 确定性排序决定 | 同 | 排序未改 |
| 指标 | `FlowSummaryMetrics` | 同 | 本处不读写 budget |

## 6. 验证

### 6.1 oracle（字节级，最强判据）

`NLCPGGraph.GraphSnapshotVersion`：

- 改前基线：`9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896`
- 改后实测：**逐位相同**

### 6.2 同二进制交叉 A/B（教训 11）

用 `UseLegacyPlanDedup` 开关在**同一份二进制内**交替切换，各跑 2 次。
日志：`Build/verify-pass-optimization/n2ab-NEW-1/2.log`、`n2ab-LEGACY-1/2.log`

| 实现 | 调用点循环 (ms) | 去重+组内排序 (ms) | 写共享图 (ms) | 指纹 |
| --- | ---: | ---: | ---: | --- |
| NEW | 34,501 | **15,722** | 15,543 | `9B59A35A…` |
| NEW | 34,653 | **16,043** | 15,527 | `9B59A35A…` |
| LEGACY | 38,345 | 19,533 | 15,726 | `9B59A35A…` |
| LEGACY | 35,067 | 17,850 | 14,291 | `9B59A35A…` |

- 「去重+组内排序」**19,533 → 15,722（1.24×）**；17,850 → 16,043（1.11×）
- **指纹 4 次运行全部相同**，且与改前基线逐位一致 ⇒ **字节级等价**
- 写共享图一项 NEW/LEGACY 相当（15,543 vs 15,726）⇒ 收益归因于去重，不是噪声

### 6.3 测试（用户要求：只跑小批量）

`--filter "…~Snapshot|~Freeze|~GraphIndex|~ShardingEquivalence|~Interprocedural"`

## 7. 风险与回滚

| 风险 | 等级 | 处置 |
| --- | --- | --- |
| §3.1 推理有漏洞（存在真实重复计划） | 低 | 已用 3,954,144 条实测丢弃 0；oracle 不符即回滚 |
| 并行写入者正在改同文件 | **高** | `NLCPGBuilder.cs` 属他人工作流；动手前核对 mtime |
| 误删排序导致边序漂移 | 中 | **本设计不触碰排序**；oracle 覆盖边序 |

**回滚**：`Build/verify-pass-optimization/NLCPGBuilder.N2-PRE.bak`

## 8. 诚实边界

- 收益**量级不大**：「去重+组内排序」约 1.1–1.24×，折算到 `InterproceduralDataFlowPass`
  stage 约 **4–9%**。**不得**声称端到端大比例提升。
- 数据全部来自 `NPC.cs` **单文件**语料。
- **未做**：把「去重+组内排序」真正并行化（它占循环 49.4%，是更大的机会，
  但需要把 `sortRows`/`nodeKeyCache` 改为每线程私有并按 `callSiteOrder` 归并，
  改动面大且涉及 `PublishCallSitePlans` 签名重构）⇒ 留作后续 N2-b。
- **未做**：`AddEdge` 写共享图（42.7%）的批量化或延迟写入。
