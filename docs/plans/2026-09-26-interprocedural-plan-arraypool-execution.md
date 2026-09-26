# P03 跨过程计划数组池化（ArrayPool）Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 把跨过程桥计划的**生成期数组**从「每次 `new` + GC」改为 `ArrayPool<InterproceduralDataFlowPlan>.Shared` 租借，使该类型不再每一批都往 LOH 灌 216 B/元素的整份数组。

**Architecture:** 只替换**数组来源**，不替换列表语义与所有权。计划载体（216 B）与组头（`InterproceduralDataFlowPlanGroup`）保持不变；`AddEdge` 调用序列、组内排序、窗口冲刷条件、预算前缀收集、非正预算兼容分支逐位不变。因 `InterproceduralDataFlowPlan` 及其两个端点字段 `NLCPGNode` 都是**纯值类型、无任何引用字段**，归还时可用 `clearArray: false`，**无需清零**。

**Tech Stack:** C#/.NET 10、`System.Buffers.ArrayPool<T>`、现有 NLCPG 构图与 Contract 测试。

---

日期：2026-09-26。切片 `interprocedural-plan-arraypool` 尚未加入 [feature_list.json](../../Context/feature_list.json)；实施前先登记状态与完成条件。

## 执行范围（本轮，用户指定）

| 项 | 本轮决定 |
| --- | --- |
| 前置验证（冻结基线、oracle 固化、变异验证、分配量 A/B、语料分布侦查） | **全部不做**，直接改产品代码 |
| 测试范围 | **只做 10% 核心测试** —— §4 的 **4 条判据**，不建完整测试矩阵 |
| 未测量的收益/风险 | **不声称**（见 §6、§7） |

**因此本轮放弃的东西，必须写明而不是默认**：本计划**不产出任何内存收益数字**，
也**不回答**"池化到底省了多少"。它只回答"**改完之后图还是不是原来那张图、池子有没有漏**"。
任何"省了 X GiB"的结论都**超出本轮证据范围**，不得写进 feature 状态或交付说明。

前置、共同命令、测量口径与回退规则见 [总索引](2026-09-25-memory-optimization-execution-index.md)；
本轮的裁剪是对该总索引 §3 第 3 条与 §4 的**显式豁免**，不是遗漏。

## 0. 本计划推翻的既有结论（必须先读）

本项**直接推翻**两处已写进仓库的"不采用"决定，不是遗漏，是范围变更：

| 出处 | 原文 | 本计划态度 |
| --- | --- | --- |
| [M1 执行文档](2026-09-25-interprocedural-plan-compaction-execution.md) §1 | 「**新调度池、全局 ArrayPool**、修改节点相等语义 \| **不采用**」 | **推翻**，改为采用 `ArrayPool<T>.Shared` |
| [缩窄设计](2026-09-25-interprocedural-plan-compaction-design.md) §8 第 144 行 | 「不能每窗口无条件 TrimExcess，也**不默认引入 ArrayPool**，把 GC 分配转成长期保留」 | **推翻**，但其警告本质仍按 §2.3 处理 |

设计文档那条拒绝理由**并不错，它指的是别的东西**：它当时的语境是"排序缓冲容量治理"，即
**把空闲 `List<PlanSortRow>` 的容量长期留着**。本计划池化的是**计划数组**，且
`ArrayPool<T>.Shared` 有 GC 压力触发的裁剪、每桶数组数上限、租借超限直接不池化三重约束，
**不是无界长期保留**（依据见 §2.3，**本轮不复测**）。

## 1. 为什么这一项值得做（证据来自既有报告，本轮不复测）

### 1.1 两条证据，两个不同口径，不可混算

| 口径 | 读数 | 来源 | 含义 |
| --- | ---: | --- | --- |
| **LOH 分配量**（60 s nettrace，累计） | `InterproceduralDataFlowPlan[]` **3,167.45 MiB / 51.752%**，**榜第 1** | `.../Analysis/LohAlloc/loh-alloc-by-type.csv` | 该类型是**分配速率**主源 ⇒ 池化直接命中 |
| **常驻峰值**（gcdump，某时刻存活） | `InterproceduralDataFlowPlan[]` **755.3 MiB，仅 2 个样本** | `type-occupancy-*.csv` | 是**尖峰**，不是平台 |

⇒ **本项的目标是削掉那 3,167 MiB 的分配量**，不是削平台高度。
按总索引 §4 纪律：**分配量下降不等于常驻内存下降**，结论文档里**不得**写成"峰值内存下降 X GiB"。

### 1.2 为什么 LOH 而不是 SOH

`LOH` 阈值 = **85,000 B**（默认，只能调高）。元素宽 **216 B** ⇒ 单数组超过
`85,000 ÷ 216 = 394` 条即整体落 LOH。实测单组最大 **5,878 行**、峰值行 **133,173**
（[BASELINE.md](../../Build/MemoryOptimization/M1/run-20260925-01/BASELINE.md) §5.5）
⇒ **该数组必然全部落 LOH**，且每次扩容/新建都是整份大对象。

### 1.3 清零成本确实为零（本项成立的唯一关键前提，已用源码验证）

`InterproceduralDataFlowPlan` 四字段：两个 `NLCPGNode`（各 104 B）、
`NLCPGInterproceduralBridgeKind`（enum→int）、`int ArgumentOrdinal` = **216 B**（实测）。

`NLCPGNode` 12 个字段（`NLCPGNode.cs:6-18`）：`NLCPGNodeKind`、4×`uint`、
`NLCPGDispatchKind?`、`int?`×2、`bool`、`NodeId?`、`StableNodeAnchor?`
—— `StableNodeAnchor` 内部（`StableNodeAnchor.cs:6`）亦全是值类型。

⇒ **整条链无 `string`/`object`/数组引用** ⇒ `Return(array, clearArray: false)` 安全：
GC 不需要为零化付费，池化省下的就是净收益。**这是本项与"池化引用类型数组"的本质区别。**
**本轮不再为该前提做变异验证**（§4 的 10% 已含宽度断言，可防字段被改坏）。

## 2. 必须守住的语义与三个真实风险

### 2.1 语义不变量（逐位不许变）

1. `graph.AddEdge` 的**调用序列**逐位不变 ⇒ 冻结边序 oracle
   （[CpgInterproceduralEdgeOrderTests](../../tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs)）必须原样通过，**不允许改 oracle**。
2. 组内有效排序键不变：`BridgeKind → ArgumentOrdinal → NodeSortKey(Source) → NodeSortKey(Target)`，末键 `PlanIndex` 保稳定（`NLCPGBuilder.cs:2919-2920`、`:3241-3242`）。
3. 预算语义不变：正预算走**前缀收集**（`:2750-2756`、`:2761`），非正预算**保留**原
   `RemoveRange`/`plans[0]` 异常契约（`:2851-2862`、`:2989-2994`）。
4. `recordedReturnMethods` 门控**不受预算与池化影响**（`:2799-2801`）。
5. 窗口双上界（64 组 / 131,072 行）与冲刷时机不变（`:2667-2668`、`:2873-2874`）。

### 2.2 风险 R1：`List<T>` 不能直接吃池化数组（**本项最容易做错的地方**）

`List<InterproceduralDataFlowPlan>(int capacity)` 会**自己 `new T[]`**，
当前代码正是这样用的（`:2755-2756`）。**没有任何公开 API 能把外部数组交给 `List<T>` 当后备存储。**

⇒ 若只是"把 `new List<T>(cap)` 换成池化数组"，等于**什么都没池化**，还会多一次拷贝。**禁止这种做法。**

**正确做法**：把计划收集的载体从 `List<T>` 换成**我们持有数组的轻量结构**（§3），
数组经 `ArrayPool` 租借、超容时**租更大的并归还旧的**。

**连带影响（必须一起处理，否则编译不过）**：

| 受影响物 | 位置 | 处理 |
| --- | --- | --- |
| `InterproceduralDataFlowPlanGroup.plans` 类型 | `InterproceduralDataFlowPlanGroup.cs:21` | 改为 buffer 类型 |
| `Plans` 属性 / `Count` | 同上 `:30-32` | 跟随类型 |
| `BuildAndSortPlanRows(List<…>)` 形参 | `NLCPGBuilder.cs:3135`、`:2952`、`:2962` | 改为 buffer |
| `capacityLedger.ObserveGroup(count, Capacity, initialCapacity)` | `:2830-2833` | 语义不变，读 buffer 的 `Capacity` |
| `PlanCapacitySlackBytes` 计算 | `:2843-2845` | 不变 |

### 2.3 风险 R2：池化把"分配"换成"保留"——本轮只守最小约束

`ArrayPool<T>.Shared` 不是无界缓存：桶按 2 的幂对齐（租 5,878 条会拿到 8,192 条，
**最多 1.39× 超额**）；每桶数组数有上限，超出的归还直接丢弃；租借长度超过内部上限时不池化；
Gen2 GC 会触发裁剪。

⇒ 本轮**不建保留上界账本、不设淘汰策略**（原 T3 已删，见 §5）。理由：
`ArrayPool<T>.Shared` 自带 GC 压力裁剪，**我们再加一层缓存才是设计 §8 第 144 行真正警告的事**。
唯一保留的池化专属读数是 **`RentCount` / `ReturnCount`**（§2.4），因为泄漏**功能测试看不出来**。

### 2.4 风险 R3 与唯一新增不变量：归还时机 + 不许漏归还

组的所有权是「构造该组的同一次循环迭代内追加，进入窗口后只读」
（`InterproceduralDataFlowPlanGroup.cs:12-17`）。窗口内 ②③ 会**并行读** `group.Plans`
（`:2952`、`:2962`）。⇒ **归还只能发生在串行发布段该槽 `rows.Clear()` 之后**（`:3014`），
即"所有 worker 已结束、该组不再被任何线程读取"的那一刻。

**禁止**：在 `BuildAndSortPlanRows` 里归还、在 worker 内归还、提前归还给下一个组复用。
异常/取消路径同样要在 worker 全部结束后统一归还。

**唯一新增不变量**（本轮 10% 里最重要的那一条）：

```
PlanBufferReturnCount == PlanBufferRentCount
```

池化最典型的错误是**漏归还**，它会把"省分配"变成"永久漏内存"，而**功能测试完全看不出来**。
故在 `InterproceduralPlanCapacityLedger` **新增两个字段**（不改旧字段口径）：
`PlanBufferRentCount`、`PlanBufferReturnCount`。

## 3. 目标表示（只改数组来源）

新增内部 struct（放 `InterproceduralDataFlowPlanGroup.cs`，与组载体、账本同属一个观测面）：

```csharp
// 只负责"从池里取数组 + 计数"，不含排序、不含调用点元数据。
internal struct InterproceduralPlanBuffer
{
    private InterproceduralDataFlowPlan[] _array;
    internal int Count { get; private set; }
    internal int Capacity => _array.Length;
    internal void EnsureCapacity(int required);                   // 满则 Rent 更大者、Return 旧者
    internal void Add(InterproceduralDataFlowPlan plan);          // 先 EnsureCapacity(Count + 1)
    internal readonly ref readonly InterproceduralDataFlowPlan At(int index) => ref _array[index];
    internal void Return(InterproceduralPlanCapacityLedgerBuilder ledger);
}
```

（`At` 写成方法而非索引器：`ref readonly` 索引器不能是自动属性，必须带方法体。）

`InterproceduralDataFlowPlan` 与 `NLCPGNode` **类型定义不改一个字**（宽度护栏
`InterproceduralPlanCompactionTests.cs:94-108` 的 216/104 断言必须继续通过）。

**桶对齐使 `Capacity` 语义变更（必须显式记录）**：旧 `List<T>.Capacity` 是精确增长值，
池化后 `Capacity` 变成**桶大小（2 的幂）**。

> **实施订正（2026-09-26，已落地）**：本稿原预测
> `PositiveBudget_InitialCapacityNeverExceedsBudget`（`InterproceduralPlanCompactionTests.cs:174-188`）
> **会因此变红、必须改断言**。**实测该预测是错的，该测试原样通过、未改一个字。**
>
> 原因：落地实现采用了**门槛池化**——只有请求容量
> `≥ InterproceduralPlanBuffer.PoolMinimumCapacity = 85,000 ÷ 216 + 1 = 394` 时才走 ArrayPool，
> 低于门槛走**精确分配**（`new T[capacity]`）。而该测试的预算取 `1/2`，请求容量远低于 394
> ⇒ 走精确分配 ⇒ `Capacity` 仍**恰好等于** `min(上界, B)` ⇒ 原断言成立。
>
> ⇒ **桶对齐只影响 LOH 量级的大组**（正是收益来源所在区间），小容量保持精确。
> 这不只是"逃过一条测试"：它让 `Capacity ≤ 预算` 这条既有契约在**小预算下继续为真**，
> 从而**不必放宽任何既有护栏**。阈值取 394 而非 1 的理由即在此（见 §2.2 实现说明）。
>
> **仍未验证**：`≥394` 的组在桶对齐下 `Capacity` 可能达 `next_pow2`（最多 1.39×），
> 这部分浪费如实计入 `PlanCapacitySlackBytes`（未粉饰），但**它对净收益的影响未测量**。

## 4. 本轮工作（无前置验证）

### T1：改产品代码（直接开工）

**Files:** 修改 `InterproceduralDataFlowPlanGroup.cs`、`NLCPGBuilder.cs`。

1. 新增 `InterproceduralPlanBuffer`（§3）与两个账本字段（§2.4）。
2. `NLCPGBuilder.cs:2755-2820` 三段追加改成走 buffer；**保持三段顺序、每段内顺序、
   预算判断的位置与语义逐位不变**。
3. `:2866-2869` 组头改持 buffer；`:2952`/`:2962`/`:3135` 的形参跟随类型。
4. `:3014` 之后（`rows.Clear()` 已执行、worker 已结束）归还该组数组并记账。
5. 按 §3 修正 `PositiveBudget_InitialCapacityNeverExceedsBudget` 的断言（**改测试，不是加测试**）。
6. 构建到 0 警告 0 错误。

**不做**：不先冻结基线、不先跑 oracle、不改 GC/DOP/预算。

### T2：10% 核心测试（4 条判据，覆盖 2 个致命失效模式）

| # | 判据 | 文件 | 防的是 |
| --- | --- | --- | --- |
| 1 | 冻结边序 oracle **原样通过** | `CpgInterproceduralEdgeOrderTests`（**已有，不改**） | 顺序/边集被改坏 —— **致命** |
| 2 | `PlanBufferReturnCount == PlanBufferRentCount > 0` | 新增 `InterproceduralPlanArrayPoolTests`（**1 个 Fact**） | 漏归还 ⇒ 永久漏内存，**功能测试看不出来** —— **致命** |
| 3 | `Unsafe.SizeOf<InterproceduralDataFlowPlan>() == 216`、`NLCPGNode == 104` | `InterproceduralPlanCompactionTests:94-108`（**已有，不改**） | 载体被顺手改宽，抵消收益 |
| 4 | 非正预算仍抛 `ArgumentOutOfRangeException` | `InterproceduralPlanCompactionTests:301-331`（**已有，不改**） | `:2989-2994` 兼容分支被 buffer 化吞掉 |

**判据 2 的夹具只需**：一个多调用点小夹具，触发 ≥1 次 `Rent` 与 ≥1 次扩容（"租大还旧"），
断言两计数相等且都 > 0。**不建**大组/窗口跨越/行上界夹具（原 T1 的大矩阵已删）。

**本轮不做的验证**（明确记录，不假装做过）：

- 冻结改前基线、保存 SHA256、DOP 1/2 等价对照；
- 变异验证（临时删 `Return` 看判据是否变红）；
- 累计分配量 A/B、阶段耗时、`InterproceduralDataFlowPlan[]` LOH 排名复测；
- 真实语料的组大小分布、桶对齐浪费实测；
- 保留上界与淘汰策略（原 T3，见 §2.3）。

## 5. 验证命令与验收

```powershell
# 从仓库根执行。注意：不能传 -p:...（PowerShell 会把 p 解析为 -ProgressAction 歧义），
# 统一用 --property:（此处语法已按 2026-09-26 的实测修正，原稿的 -p: 形式不可用）。
& ./Build/Tools/Invoke-SerialDotnet.ps1 build ./src/NLCPG/NLCPG.csproj --no-restore --property:UseSharedCompilation=false
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore --property:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgInterproceduralEdgeOrderTests|FullyQualifiedName~InterproceduralPlanCompactionTests|FullyQualifiedName~InterproceduralPlanArrayPoolTests'
```

拟新增类缺失时不算完成；TRX 必须含实际测试，不能 0 tests。

**验收标准（就是 §4 的 4 条）：**

1. 冻结边序 oracle 原样通过，**oracle 文件本身零改动**（用 `git diff` 核对）。
2. `RentCount == ReturnCount > 0`。
3. 216 / 104 宽度不变。
4. 非正预算异常契约不变。

外加：`NLCPG.csproj` 构建 0 警告 0 错误。

**不通过的处置**：任一条红 → 只回退本项补丁（不改他人文件、不整文件覆盖）。

## 6. 明确不做的事（防止范围蔓延）

- **不改** `InterproceduralDataFlowPlan` / `NLCPGNode` 的字段与宽度（那是 M1 已交付的 B 方案）。
- **不改**排序键、比较器、稳定性末键。
- **不引入**自建第二层数组缓存（那正是设计 §8 第 144 行警告的"长期保留"）。
- **不池化** `PlanSortRow` 数组与 `nodeSortKeys` 字典（前者同为 `List<T>`，不能直接吃外部数组；
  后者是长生命周期缓存，不属于生成期瞬态）。
- **不动** GC 设置、DOP、预算（总索引 §3 第 4 条：改变它们的运行不能作为单变量 A/B）。
- **不做**前置验证与完整测试矩阵（本轮显式裁剪，见 §4）。

## 7. 实施记录（2026-09-26，已落地）

### 7.1 落地的形状（与本稿 §3 的差异，逐条说明）

| 项 | 本稿草稿 | 实际落地 | 为什么改 |
| --- | --- | --- | --- |
| 载体类型 | `struct` | **`class`** | 组头是 readonly struct，按值复制极常见。若载体是 struct，副本各持一份数组引用，`Return` 只清掉当时那一个副本 ⇒ 悬空别名让 worker **静默读到别人正在写的槽**。class 只有一个实例，`Return` 后再访问立刻 NRE（**响亮失败**）。附带收益：组头宽度不变（原字段也是引用） |
| 池化范围 | 全部数组 | **仅 `≥394` 条**（`PoolMinimumCapacity = 85_000/216 + 1`） | 见 §3 实施订正：小数组保持精确 `Capacity`，既有预算护栏无需放宽。且 394 恰是 LOH 起点——本项收益的唯一来源区间 |
| 索引器 | `ref readonly` 索引器 | 普通索引器 `this[int]`（按值返回） | `ref readonly` 索引器不能是自动属性；且按值返回与原先 `List<T>[i]` 语义逐位相同，不引入 `ref` 生命周期争议 |
| 扩容记账 | 未说明 | **`Grow` 内归还旧池数组，且 `Return` 把载体累计归还数并入账本** | 否则账本一边收扩容期租借、一边漏记扩容期归还，"租借==归还"会在**发生过扩容的组上假失败** |
| 空组路径 | 未覆盖 | `Count == 0` 的 `continue` 之前**也归还** | 该分支永不进入窗口 ⇒ 永远不会走到发布段归还。若上界 ≥394 而实际 0 条通过过滤，不归还即永久失衡 |
| 命令语法 | `-p:UseSharedCompilation=false` | **`--property:UseSharedCompilation=false`** | `-p:` 被 PowerShell 解析为 `-ProgressAction` 歧义（与 `docs/plans/2026-09-26-declared-symbol-test-handoff.md` §4 记录一致）。本稿 §5 已同步修正 |

### 7.2 4 条判据的实际结果

| # | 判据 | 结果 |
| --- | --- | --- |
| 1 | 冻结边序 oracle 原样通过 | ✅ **未改 oracle 一个字** |
| 2 | `RentCount == ReturnCount > 0` | ✅ 新增 1 Fact 通过 |
| 3 | 216 / 104 宽度不变 | ✅ |
| 4 | 非正预算仍抛 `ArgumentOutOfRangeException` | ✅ |

**回归**：§5 的 3 个过滤器合计 **46 通过 / 0 失败**（新增 1 条在内），12–14 s。
`NLCPG.csproj` 构建 **0 警告 0 错误**；测试项目 0 错误。

### 7.3 判据 2 的**判别力已用变异实测证明**（本节关键，不是断言）

照 M1 执行文档 §8.1 的教训（那条护栏删掉被测代码后**照样通过**、39/39 无感），
本轮对判据 2 做了同样的变异：

| 变异 | 结果 |
| --- | --- |
| 注释掉发布段的 `group.Plans.Return(capacityLedger);` | **`Assert.Equal() Failure: Values differ` → 1 失败 / 0 通过** ✅ |
| 还原后核对 | `NLCPGBuilder.cs` SHA256 **逐字节相同**（`DACF65E5…`）、行数 4514、`MUTATION-PROBE` 残留 **0** |

⇒ 判据 2 **不是装饰**：它真的能拦住"漏归还"。

### 7.4 本轮**未做**的事（与 §4 一致）

- 未冻结改前基线/SHA256 对照；未做 DOP 1/2 等价对照；
- **未测累计分配量 A/B、阶段耗时**，故 **本项不产出任何内存收益数字**；
- 未测真实语料组大小分布、未测桶对齐浪费的实际影响。
- ⇒ **不得**宣称 `InterproceduralDataFlowPlan[]` 的 3,167 MiB 已下降。

### 7.5 外部阻塞（如实记录）

实施期间**另一并发会话**两次启动 `NLISSN.exe` 全量运行，长时间锁定共享输出目录
`Build/src/Debug/net10.0/`（`NLCPG.dll`、`NLISSN.Configuration.dll` 等），导致默认输出的
构建/测试 `MSB3027`/`MSB3021` 失败两次。按纪律**未终止他人进程**，
改用私有输出目录 `--output=Build/MemoryOptimization/M8/p03-testout` 绕开；
对方进程结束后默认路径亦恢复。此为**环境并发**，非本项改动缺陷。

## 8. 未验证边界（本轮结束时这些仍然成立）

- **本计划不产出内存收益数字。** 3,167.45 MiB 是 **nettrace LOH 分配量（60 s 累计，来自既有报告）**，
  该类型**常驻峰值仅 755.3 MiB / 2 样本**；两者不可互相换算，本轮也未复测。
- **跳过前置验证 ⇒ 以下为未验证假设**（依据是静态阅读，不是测量）：
  ① `ArrayPool<T>.Shared` 在本机 .NET 10 上的桶上限、每桶数组数、超限不池化阈值的**实际值**；
  ② 真实语料的**组大小分布**（决定桶对齐浪费 ≤1.39× 的实际影响面）；
  ③ 池化的**净分配收益是否为正**（桶浪费可能吃掉一部分）。
  ⇒ 若后续要主张收益，**必须先补测 ①②③**。
- 本项只覆盖**单进程、单次构建**；跨 Build 的池复用行为未验证。
- 3,167.45 MiB 的排名与 BASELINE.md 的 5,878 / 133,173 均为**既有报告读数**，
  本轮未复跑，故**不得**作为本轮的效果证据引用。

## 9. ⚠ 本项已被 ⑥ 取代（2026-09-27）——记录而非删除

**结论：本项（⑤ ArrayPool 池化）的收益来源已被后续切片 ⑥ 消除，池化分支按 ⑥ §4.4 处置 2 退役。**
本节的**唯一目的**是显式记录这次推翻，而不是静默删除历史结论——第五节当初引入 `ArrayPool`
时还**推翻过** M1 的"不采用全局 ArrayPool"决定（见 §0），故它的退役同样必须留痕。

**机理（两项的内在冲突，不是 bug）**：

| 事实 | 数值 |
| --- | --- |
| ⑤ 的前提 | 载体元素宽 **216 B** ⇒ LOH 门槛 `85,000 ÷ 216 + 1 = 394` 条 |
| ⑤ 的收益来源 | 跨过 394 条的那些**组数组**原本每一批都要往 LOH 灌一份 |
| ⑥ 的改动 | 载体只存 `(池内序号, 实参序)` ⇒ **8 B** |
| ⑥ 后的门槛 | `85,000 ÷ 8 + 1 = 10,626` 条 |
| 实测**最大单组行数** | **5,050**（源码注记 5,878）—— **远低于 10,626** |

⇒ 在已实测语料上**没有任何一个组会再达到池化门槛**：池化代码成为**不可达路径**，
⑤ 的收益（LOH 分配量）本身也被 ⑥ 消除。

**实际退役内容**（产品 + 测试同批）：

| 位置 | 处置 |
| --- | --- |
| `InterproceduralPlanBuffer.Create` 的 `ArrayPool<…>.Shared.Rent` 分支 | **删除**，恒为精确分配 |
| 常量 `PoolMinimumCapacity` | **删除**（8 B 下门槛不再是本类型的判别量） |
| 账本字段 `PlanBufferRentCount` / `PlanBufferReturnCount` | **删除**（"漏归还"这个失败模式随池化一起消失） |
| `InterproceduralPlanBuffer.Return` / `RecordRent` 与两处调用点 | **删除**（空组路径与发布段各一处） |
| `tests/…/InterproceduralPlanArrayPoolTests.cs` | **删除**（它护的是已不存在的租借/归还配对） |

**⑤ 的哪些结论仍然有效**（不得一并当作被推翻）：

- §7.1 那条**为什么载体必须是 `class` 而不是 `struct`** 的论证**仍然成立**且**更重要了**——
  与池化无关，它讲的是"组头按值复制时副本各持一份数组引用"的所有权问题。`InterproceduralPlanBuffer`
  的类型注释已保留该论证。
- §7.2 判据 1（**冻结边序 oracle 原样通过**）与判据 4（**非正预算仍抛 `ArgumentOutOfRangeException`**）
  是**语义护栏**，与本项是否池化无关，二者在 ⑥ 下**仍然通过**（见 ⑥ 执行报告 §7）。
- §8 全部未验证边界**仍然成立**；此外现在多一条：**⑥ 也未测内存收益**（同上）。

**未验证边界（本节新增）**：本次退役是**基于"实测最大单组 5,050 < 10,626"的算术**，
**未**重新实测该语料的组大小分布；`c · p > 10,626` 的极端尾部（理论上限 13,882）仍会落回 LOH，
但那条路径现在走**精确分配**，没有池可用——若将来语料放大到那量级，需重新评估是否恢复池化。

