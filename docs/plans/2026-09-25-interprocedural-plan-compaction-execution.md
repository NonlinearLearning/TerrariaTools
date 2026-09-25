# M1 跨过程计划缩窄与窗口容量 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 删除每条计划重复的调用点/目标方法载荷，并限制所有空闲排序槽的合计容量。

**Architecture:** 采用已有设计 B，保留两个完整端点；调用点信息由组持有。沿用已实现的 PlanIndex 排序、原预算前缀与串行发布。计划收集只保留最终预算内前缀，排序缓冲在组发布后释放元素引用并接受总容量治理。

**Tech Stack:** C# / .NET、List<T>、现有 NLCPG 构图及 Contract 测试。

---

日期：2026-09-25。feature：`interprocedural-plan-carrier-compaction`，权威状态见 [feature_list.json](../../Context/feature_list.json)。
本轮只编写执行文档。前置、共同命令、测量口径与回退规则见 [总索引](2026-09-25-memory-optimization-execution-index.md)。

## 1. 取舍与成本边界

| 选择 | 决定 | 原因 |
| --- | --- | --- |
| 方案 B：组级字段 + 两端点计划 | 采用 | 每条计划去掉两个完整节点，局部改动即可保留节点合并输入 |
| 预算内前缀收集 + 空闲槽总容量上限 | 采用 | 同时处理瞬态复制与历史最大容量保留 |
| 方案 A：PlanIndex | 已有基线 | 当前排序行已实现，不重复改造/计收益 |
| 方案 C：端点 interning 后只存 ordinal | 不采用 | 精确载荷相等、临时表共存与净收益验证成本过高 |
| 新调度池、全局 ArrayPool、修改节点相等语义 | 不采用 | 超出两三个局部载体的必要改动 |

粗估 2–3 人日。产品范围仅计划类型、Builder 相关方法，最多新增一个组载体文件。
若必须改 AddNode/MergeNode、排序键语义或跨过程预算定义，停止并记录范围超限。

## 2. 当前代码和必须保留的语义

- [InterproceduralDataFlowPlan](../../src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs)：四个完整节点；保留 readonly record struct 类型及名称。
- [NLCPGBuilder](../../src/NLCPG/Builder/NLCPGBuilder.cs)：`RunInterproceduralDataFlowPass` 构造三类 bridge，`FlushParallelPublishWindow` 发布，`BuildAndSortPlanRows` 排序。
- [已有设计](2026-09-25-interprocedural-plan-compaction-design.md)第 3、6、8 节提供预算、载荷与容量约束。
- `TargetMethodNode`、`StableCallSiteOrder` 在当前计划发布路径没有字段读取；局部 targetMethod 的目标解析和 return 门控仍然需要。实施前再检索所有消费方。
- 预算截取的是三种 bridge 依原次序生成的前缀，发生在排序之前。`recordedReturnMethods` 是整个 pass 的串行门控，超预算也不得跳过它的更新。
- 排序键仍为 BridgeKind(int)、ArgumentOrdinal、原 SourceKey、原 TargetKey、PlanIndex；不改变字符串 Ordinal 比较。

## 3. 目标表示与所有权

计划行的目标定义：

```csharp
internal readonly record struct InterproceduralDataFlowPlan(
    NLCPGNode SourceNode,
    NLCPGNode TargetNode,
    NLCPGInterproceduralBridgeKind BridgeKind,
    int ArgumentOrdinal = -1);
```

组载体保存精确的 `CallSiteNode`、原调用点顺序和计划列表；不为跳过的组额外构造上下文。
两个端点仍是原始完整快照值，发布继续走原 `graph.AddEdge`。组密封后不能追加/重排，所有 worker 结束后才可发布/释放。

容量策略分两层：

1. **计划载荷**：正预算 B 时，初始容量取 `min(原上界, B)`；逐条生成时只将前 B 条加入列表，同时保留 `sawCandidate` 和 `overflowed`。生成循环和 return-method 门控照常执行。最终只记录一次原预算截断事件；取消无条件 `TrimExcess`。没有溢出但上界严重偏大的组，记录实际 slack，不为省 slack 再复制整组。
2. **排序缓冲**：发布一组后 `Clear()` 释放 key 引用；清理只在 worker 全部结束后进行。每次刷新后统计全部 64 槽的 `Capacity × 当前实测行宽`。默认内部保留预算拟取 `131072 × 行宽`，超出时按容量从大到小替换为空列表，容量相同按槽号决定。超大单组的排序数组发布后不保留。

这里限制的是**空闲排序缓冲**，不是所有活跃计划、更不是进程内存。131072 行仍是加入组后触发刷新，单大组可超过阈值。

零/负预算不是顺手修复项：先用公开构图入口冻结现有异常类型和可观察计数。
正预算才走前缀收集；非正预算保留原构造/截断/发布边界，以免把既有异常静默变为成功。
这是非法/边界输入的兼容分支，不维护两套正常输入优化算法。若无法低成本保留，撤下“前缀收集”子补丁，只交付独立的 B 与槽容量治理并在状态中标明未完成子项。

## 4. Task 1：冻结基线和容量压力夹具

**Files:**
- Test: `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchInterproceduralTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/InterproceduralPlanCompactionTests.cs`。
- Fixtures: `tests/NLISSN.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`。

1. 保存当前完整有序图及提交序 oracle，覆盖三种 bridge、同方法多调用点、相同排序键的多条计划。
2. 增加预算 0/1/边界值/负值、超过 64 组、单组超过行阈值、不同槽轮流出现大组的夹具。
3. 增加结构/分配实验：当前行宽、每组 Count/Capacity、全部槽容量总和、裁剪次数。其“改善门槛”应在旧代码失败；语义基线本来就应通过。
4. 运行本节过滤器，确认夹具确实触发预算与窗口，不用“边数大于零”替代目标路径覆盖。

## 5. Task 2：只落地 B，验证后再动容量

**Files:** Modify `InterproceduralDataFlowPlan.cs`、`NLCPGBuilder.cs`；对应测试沿用 Task 1。

1. 新增内部组载体，把调用点元数据移至组头，计划构造器缩为两端点。
2. 窗口持有组；发布从组头创建原调用点上下文，通过 PlanIndex 取计划。
3. 保留现有预算与容量逻辑，单独运行语义和类型契约测试。
4. 保存 B 的独立 diff 和尺寸证据；这一步不把容量治理收益混入布局收益。

## 6. Task 3：前缀收集和空闲槽治理

1. 按第 3 节替换正预算下的超额载荷保留；保持三个生成循环顺序、统计和 return 门控。
2. 去掉该路径末尾无条件 TrimExcess；验证未出现 List 倍增至远超预算。
3. 发布完成后清元素引用，加入所有槽的合计保留预算与确定性淘汰。
4. 重新运行零/负预算兼容、窗口和预算前缀测试；单独记录容量补丁的分配与保留效果。

## 7. 验证命令与验收

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore --property:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~CpgWorkBatchInterproceduralTests|FullyQualifiedName~NLCPGNodeIdContractTests|FullyQualifiedName~InterproceduralPlanCompactionTests'
```

在测试入口外先执行总索引的构建命令。拟新增类缺失时不算完成，检查 TRX 中有实际测试。

验收标准：

- 原有序图/完整载荷/原始提交序均相等，DOP 1/2 和窗口跨越均覆盖。
- 计划元素宽度拟要求不超过旧宽度的 60%；历史 428→216 B 仅为选择依据，填写当前实测 N/P/排序行宽。
- 正预算溢出组的保留计划数/容量不超过 B；非正预算保持已冻结行为。
- 所有已发布槽 key 引用已清理，刷新后合计保留字节不超过第 3 节内部预算；含轮流大槽压力测试。
- 受控窗口峰值账本含组头、slack、旧/新数组共存，必须有净改善且满足共同耗时门槛。

任一步不通过只回退该子补丁。未通过的子项留在 feature 完成条件中，不把“B 完成”写成整个 M1 完成。

## 8. 第三会话独立复验附记（2026-09-25）

复验记录：`Build/MemoryOptimization/M1/indep-verify/FINDING-key-release-guard.md`。
本会话**未修改任何产品代码**；`NLCPGBuilder.cs` 复验前后哈希一致（`602EECAEE1577BC1…`）。

### 8.1 已确认：验收标准第 4 条的「key 引用已清理」**曾**无判别力（已由 §8.3 修复）

第 7 节验收标准第 4 条要求「所有已发布槽 key 引用已清理」。
该轴由 `InterproceduralPlanCompactionTests.PublishedSlots_ReleaseSortRowKeyReferences` 承担，
但它断言的是账本计数器：

```csharp
Assert.True(ledger.SortBufferClearedSlots > 0, "...");
Assert.True(ledger.SortBufferClearedSlots >= ledger.FlushCount);
```

产品侧（`NLCPGBuilder.cs:1505-1506`）计数器紧跟在 `rows.Clear();` **之后**递增。
**变异实测**：删掉 `rows.Clear();` 整行、保留计数器 ⇒ 该测试类 **39/39 通过，无一条失败**。

⇒ 它锁的是「**计数器被加过**」，不是「**引用真的被释放**」。
总索引 §4 的「强引用可达性」这一证据轴，**目前仍是注释级承诺，不是可观测事实**。

### 8.2 为何本轮未直接修复

本会话两度尝试补断言，**第二次也撤掉了**，原因值得记录：

1. 从 API 侧无法观察窗口槽——`windowRows` 是
   `PublishInterproceduralPlansInWindow` 的**局部变量**（`NLCPGBuilder.cs:1167`），
   构建结束后不可达，无法断言「槽内是否仍残留行」。
2. 改用 `WeakReference` 自行构造并清空 `List<PlanSortRow>`——**同样无判别力**：
   它压根不经过被测产品代码，删掉产品的 `Clear()` 后照样通过。

**教训（与本轮 M5 复验同源）**：断言必须落在**被测对象**上。
「自己 new 一个容器、自己清空、断言它是空的」看起来在测引用释放，实际在测 `List.Clear()` 的语义。

### 8.3 最小修法**已实施**（2026-09-25 第四会话，8.1/8.2 的缺口就此关闭）

§8.1 记录的缺口（「key 引用已清理」只锁计数器、无判别力）**已修复**，修法即本节原先的建议：

- 产品侧 `InterproceduralPlanCapacityLedger` 新增 `SortBufferResidualRows`，
  在 `NLCPGBuilder.cs` 的发布段 **`rows.Clear();` 之后**累加 `rows.Count`（正常恒 0）。
  这是本项**唯一**能观测「强引用可达性」的量——窗口槽 `windowRows` 是
  `FlushParallelPublishWindow` 的形参，构建结束后测试无法观察槽内容（8.2 的两个失败尝试正因此撤回）。
- 新增契约测试 `PublishedSlots_RetainNoResidualSortRowsAfterClear`：断言
  `SortBufferClearedSlots > 0`（防夹具空转）且 `SortBufferResidualRows == 0`。
- 既有 `PublishedSlots_ReleaseSortRowKeyReferences` 保留原样，并**改注**为
  「锁清空动作发生过」，与新判据分工——两者不是重复。

**判别力已用变异实测证明**（这是本节的关键，不是断言）：

| 变异（删掉 `rows.Clear();` 整行、保留计数器） | 结果 |
| --- | --- |
| `PublishedSlots_RetainNoResidualSortRowsAfterClear`（**新**） | **Failed** ✅ |
| `PublishedSlots_ReleaseSortRowKeyReferences`（**旧**） | **Passed** ← 证实旧判据确实无判别力 |
| 该测试类合计 | **1 失败 / 39 通过** |

变异已按行级编辑还原，`NLCPGBuilder.cs` SHA256 **逐字节相同**（`70DF54D0…`）、无 `MUTATION` 残留。

**回归**：§7 原文过滤器（四类）**65 通过 / 0 失败**（原 64 + 新增 1，零回归）；
`NLCPG.csproj` 构建 **0 警告 0 错误**。

⇒ 总索引 §4 的「强引用可达性」轴**不再是注释级承诺**，已成为可观测事实。
**未做全量测试档**（按用户本轮指令）。

### 8.4 其余轴不受影响

第 1 轴（累计分配）见 verification [26]；第 2/4/5 轴（容器存活/扩容双数组/保留 Capacity）
见 [8]/[19]/[24]；第 6 轴（阶段耗时）仍为 **Inconclusive**，由实现者记录，本会话未改动该结论。
**第 3 轴（强引用可达性）原为注释级承诺，已于 §8.3 升级为可观测事实。**
`InterproceduralPlanCompactionTests` 现为 **40/40 通过**（原 39 + §8.3 新增 1）。

