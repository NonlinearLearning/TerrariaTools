# 分片级并行投影 + 字节预算闸门（Sharded Parallel Projection with Byte Budget Gate）执行方案

**目标：** 把「单文档分片的投影 + 落盘」从**固定并行度**升级为**受字节预算约束的并行**，
使并行只在内存允许时开启、超预算时自动退回串行，从而把并行带来的 CPU 收益
真正兑现，同时不让峰值失控。

**思路来源：** 数据库的**准入控制（admission control）**——执行器不按固定并发度放行，
而是按「本次请求的预估内存代价」向预算记账，装不下就排队；这与本项目
`MemoryAdmissionGate` 已有的 CAS 记账模式同源，只是当前那个闸门管的是**文档**粒度，
而本方案要管的是**分片**粒度。

**Tech Stack：** C#/.NET 10、`Parallel.ForEachAsync`、`SemaphoreSlim`、
`Interlocked.CompareExchange` 记账、既有 Contract 测试与 `Build/shard-perf/` 夹具。

---

日期：2026-09-28。

> # ✅ 状态：**已执行（2026-09-28）**，但按用户指定**跳过 T1 前置校准**、只做 10% 核心测试
>
> **已交付：** 分片级字节闸门已接入并行路径（T2/T3/T4 + 文档），默认串行路径**完全不变**。
>
> **两处与原计划的偏差（均为改进，已记录）：**
> 1. **不新增 `ShardAdmissionGate`，直接复用 `MemoryAdmissionGate`。**
>    原计划 §3.1 担心它的 `current == 0` 无条件放行是语义错误，我重读源码后确认：
>    那正是分片场景**需要**的「防饿死」策略（否则单片额度超预算时永远排不到），
>    故**保留**而非移除。同时给该类加了 `PeakInFlight`/`WaitMilliseconds` 观测面。
> 2. **T1 校准跳过** ⇒ 估算器不引入未校准的新常数，改为**按记录数把整篇估算分摊到各片**
>    （`EstimateShardMemoryBytes`），复用既有的、已实测的 `EstimateDocumentMemoryBytes` 系数。
>    各片份额之和恰为整篇估算，故预算口径不因分摊而失真。
>
> **⚠ 未验证边界（务必与"已完成"区分）：**
> - **T5 只做了 1 次端到端运行**（不是原计划的每档 3 次），故 **projection/write 的耗时数字不可作结论**。
> - **未做 T1 校准** ⇒ 分摊是**预算口径**，**不是**精确内存模型；不可用于容量规划。
> - 默认预算下闸门**可能根本不生效**（见 §1.5 新增）。

> 📌 **本轮性质：只写文档，不改产品代码、不跑测试。**
> 下文所有收益量级都标注口径：「实测」= 来自 `Build/shard-perf/MEASUREMENT.md`
> 或源码内既有实测注释；「推算」= 由实测事实推出的上界。

> 📌 **先读这一段：本方案的收益上限已被上一轮实测压得很低。**
> 并行分片写已实现并实测：投影 **8474 → 4183 ms（约 2.0x 加速）**，但写盘
> **3434 → 6705 ms（约 2.0x 变慢）**，净墙钟持平甚至略慢（46.6 → 48.8 s）。
> **本方案不改变「写盘会被并发拖慢」这一事实**——它只解决「并行度是固定值、
> 内存风险不可控」这个问题。若目标是总墙钟，应优先看 §7 的「重叠」形态，
> 而不是继续加大并行度。

> 📌 **行号基准**：`src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs` 1,608 行。
> 引用同时给出**符号名**，行号漂移时以符号为准。

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 当前并行度是怎么定的？ | `ProjectExportOptions.DocumentShardParallelism`，**固定值**，默认 1；调用方只能猜一个数 |
| 固定值的问题是什么？ | ① 猜大 → 峰值可能失控（N 片 DTO 同时驻留）；② 猜小 → 拿不到 CPU 收益。**两个错都不能被系统自身发现** |
| 已有闸门能直接用吗？ | **不能**。`MemoryAdmissionGate` 的记账单位是**文档**（`EstimateDocumentMemoryBytes`，源码长度 × 2000）。分片粒度需要**按片**的估值 |
| 分片的每片内存怎么估？ | **可精确估**：`FileNodeEntry`/`FileEdgeEntry` 是定长记录 + 字符串引用，按片的节点数/边数即可算（§3.2） |
| 关键不变式是什么？ | 并行路径必须与串行路径**逐字节相同**——已由 `ProjectJsonDocumentShardTests` 的突变验证锁定（§5.3） |
| 预计收益？ | **CPU 侧最多 ~2.0x**（实测），**墙钟 ≈ 0**（实测）。本方案的真正价值是**让这个开关变得安全**，而不是让总时间下降 |
| 什么时候才值得开？ | 当「投影 ≫ 写盘」时（例如落盘到 tmpfs/NVMe、或 payload 远小于中间表示）。当前语料不满足 |

## 1. 事实基线

### 1.1 现有两条路径

`ProjectJsonExporter.WriteDocumentShardsAsync`（`:815`）已实现串行/并行双路径：

| 路径 | 触发 | 机制 |
| --- | --- | --- |
| 串行 | `parallelism <= 1` | 惰性接缝 `BuildDocumentShardProjections`（`:712`）逐片 `MoveNext` → 写 |
| 并行 | `parallelism > 1` | 绕过惰性接缝，`PrepareDocumentProjection`（`:1012`）+ `MaterializeShardEdgeInputs`（`:1087`）整篇算一次，再 `Parallel.ForEachAsync` 逐片 `BuildShardProjection`（`:1165`）→ 写 |

### 1.2 现有闸门只覆盖文档粒度

`MemoryAdmissionGate`（`:1382`）在文档循环里 `AcquireAsync`，记账单位是整篇文档：

```
EstimateDocumentMemoryBytes(sourceLength) = sourceLength * 2000 + 48 MB   // :1369
BuildMemoryBudget(configured)            = max(512MB, TotalAvailableMemoryBytes / 2)  // :1349
```

⚠ **且它在串行文档循环下恒不阻塞**：准入条件 `current == 0 || current + charge <= budget`
（`:1403`）中 `_outstanding` 恒为 0（`using` 已释放），第一个分支恒真。

⇒ 现在**没有任何机制**约束「N 片同时在途」的总量。并行度是纯信任调用方。

### 1.3 实测数字（`Build/shard-perf/MEASUREMENT.md` §5）

fixture：`Big1.cs` 591,023 节点 / 1,386,027 边；`documentShardCount: 8`。

| 配置 | build | projection | write | 总墙钟 | peakWS |
| --- | --- | --- | --- | --- | --- |
| 串行（=1） | 12851 | **8474** | **3434** | 46.6 s | 1442 MB |
| 并行（=4） | 11195 | **4183** | **6705** | 48.8 s | 1528 MB |

- 投影加速 **2.03x**，与独立探针的 CPU 可扩展性一致（dop=4 近饱和，约 3.9x 上界）。
- 写盘**同样幅度变慢**，净墙钟持平 ⇒ 多写手争用同一磁盘。
- 峰值 **+6%**（1442 → 1528 MB）⇒ 4 片 DTO 同时驻留的代价。

### 1.4 并行度 4 而非 8 的经验依据

独立探针（`ShardParallelProbe`，60k 节点 / 16 片，只测投影）实测
`coresUsed`（= cpu/wall）：dop=1 → 1.01，dop=2 → 1.92，dop=4 → 3.12，
dop=8 → 6.88，dop=16 → 6.34。
⇒ **dop=4 已经拿到绝大部分收益**，再往上边际很小而内存线性上升。

**这正是「按预算自动定并行度」的动机**：固定 4 在 16 核机器上偏保守、
在 4 核机器上偏激进，而 `ProcessorCount = 16` 与 `RAM = 13.9 GB` 都已在生产实测中确认可变。

### 1.5 ✅ 执行后实测：默认预算下闸门**恒不生效**（重要边界）

实现完成后的算术核对（本机 physical = 13.86 GB ⇒ 默认预算 = phys/2 = **6.93 GB**）：

| 项 | 值 |
| --- | --- |
| 闸门生效条件 | 整篇估算 > 预算 |
| 整篇估算 | `sourceLength × 2000 + 48 MB` |
| ⇒ 换算成源码长度 | 源码 > 预算/2000 ≈ **3.55 MB** |
| 语料最大文档 | `NPC.cs` = **2.05 MB** ⇒ 估算 4.05 GB |
| fixture 最大文档 | `Big1.cs` = 0.79 MB ⇒ 估算 1.59 GB |

**结论：两个语料的整篇估算都 < 预算 ⇒ 各片份额之和 < 预算 ⇒ 闸门永不阻塞。**

数学上的原因很直白：**各片份额之和恒等于整篇文档估算**（`shardRecords` 之和 = `totalRecords`），
而文档级闸门在**串行**文档循环下本就恒不阻塞（`_outstanding` 恒为 0）。
因此除非源码 **> 3.55 MB**，分片级闸门只是把「本来就不会超的额度」记了一遍账。

⇒ **这项改动的实际作用是「把上限接好、并让它可观测」，而不是在当前语料上改变行为。**
它今天是**安全网 + 观测面**，不是性能开关。若要让它在真实语料上生效，
需要把预算调小（例如显式设 `ProjectMemoryBudgetBytes`），或把估算口径改准（T1 校准）。

## 2. 设计

### 2.1 核心思路

把 `Parallel.ForEachAsync` 的固定 `MaxDegreeOfParallelism`，换成
「**worker 循环 + 按片记账的字节闸门**」：

```
for each shard (下标序):
    charge = EstimateShardBytes(shard)          // 按片的节点/边数估
    await gate.AcquireAsync(charge)             // 装不下就在这里等
    spawn task: BuildShardProjection → Write → gate.Release(charge)
```

- 闸门保证 `Σ(在途片的 charge) <= budget`。
- 装不下时的行为：**等待**（而非失败），与既有 `MemoryAdmissionGate` 的 `Task.Delay(15)` 一致。
- 预算取自**已存在的** `ProjectMemoryBudgetBytes`（`ProjectExportOptions`），
  与文档级闸门共用同一个数，避免出现两套预算口径。

### 2.2 为什么不用 `SemaphoreSlim(parallelism)` 就够

`SemaphoreSlim` 只能限制**数量**，不能限制**总量**。而片的代价差异极大——实测
8 片时节点数 73,878 ~ 279,682（**3.79x**）。固定 count=4 可能放行 4 个最大的片
（约 4 × 191k~280k），也可能放行 4 个最小的片（约 4 × 74k~89k），差 3.8 倍。
**字节闸门把「数量」换成「总量」，这正是 3.8x 偏斜下需要的。**

### 2.3 必须保留的不变式

| 不变式 | 依据 |
| --- | --- |
| 整篇身份（id/ordinal/路径/边序）**串行算一次** | `PrepareDocumentProjection` 的 occurrenceCounts 必须是整篇口径，否则跨片 id 重复 |
| `entries` 按 `shardIndex` 归位 | 否则 manifest `files[]` 顺序随完成序漂移（已实测能抓到） |
| 各片写不同路径 | `part-NNN` 后缀保证；故 `.tmp`（`path + ".tmp"`）天然不冲突 |
| 图只读 | `StringInterner.TryResolve` 有锁；`_sourceByPath` 冻结后由 `EnsureMutable()` 拒绝写入 |

## 3. 实现

### 3.1 新增分片级闸门

复用 `MemoryAdmissionGate` 的 CAS 记账骨架，但**不共用实例**（文档级与分片级
生命周期不同）。新增一个 `ShardAdmissionGate`：

```csharp
// 与 MemoryAdmissionGate 的关键差异：不设 SemaphoreSlim 的数量上限，
// 只按字节记账；且允许"单个片超过整个预算"时放行（否则最大片永远导不出来）。
private sealed class ShardAdmissionGate
{
    private readonly long _budget;
    private long _outstanding;
    ...
}
```

**能否直接复用 `MemoryAdmissionGate`？** 可以复用**记账骨架**，但有两处必须改：

1. 它的 `AcquireAsync` 在 `current == 0` 时**无条件放行**（`:1403`），
   这是「优先保证至少一个在跑、避免死锁」的策略，分片场景**同样需要**
   （否则单片 charge > budget 时永远导不出来）⇒ **保留这条**。
2. **串行路径必须完全不经过闸门。** 若让并行度 1 也过闸门，就会给串行路径
   引入额外的等待/记账时序，而这正是 §5.2 要维持「默认路径不变」的地方。
   ⇒ 闸门只在解析后的 `parallelism > 1` 分支里创建。

⚠ 结论：`MemoryAdmissionGate` 的 `maxConcurrency`（`SemaphoreSlim` 数量上限）
在分片场景要**设为并行度上限**，字节预算作为**第二道上限**——两者取小。

### 3.2 每片字节的估算

`FileNodeEntry`（`:1570`）13 个字段、`FileEdgeEntry`（`:1585`）7 个字段，
且都含多个 `string?`。**按字段逐个累加**会引入大量魔数并与定义漂移。故：

**先试 `Unsafe.SizeOf<T>()`，但要清楚它的语义边界：**

```csharp
// ⚠ 这些 DTO 是 sealed record ⇒ 引用类型（class），不是 struct。
// 对引用类型，Unsafe.SizeOf<T>() 返回的是**引用宽度（8 字节 on x64）**，
// 不是对象布局宽度。故它只对"字段宽度之和"这类值类型计算有意义。
```

⇒ **对引用类型 DTO，`Unsafe.SizeOf` 给不出想要的值。** 两种可行做法：

| 做法 | 说明 | 风险 |
| --- | --- | --- |
| **A. 纯实测拟合**（推荐） | 不猜字段宽度，直接拟合 `bytes ≈ a*nodeCount + b*edgeCount + c` | 系数随字符串长度分布变化，需按语料校准 |
| B. 反射累加字段宽度 | `RuntimeHelpers.GetUninitializedObject` + 反射读字段，或按字段表手工维护 | 与 DTO 定义漂移；且反射本身有开销 |

**推荐 A**：它把「字段宽度」与「字符串载荷」合并成两个**可实测的系数**，
不需要为 13 个字段维护宽度表，也不会因 DTO 增删字段而失真
（只需在校准语料变化时重拟合）。

⚠ **这依赖两个未验证的系数。** **第一步必须是校准，不是编码**（§4 T1）。
在校准之前不得把任何系数写进断言、日志或文档。

### 3.3 校准方法

用既有 `Build/shard-perf/` fixture 与一次性探针：

1. **一个进程只测一个配置**（吸取 `MEASUREMENT.md` 记录的教训：同进程跑多档会互相污染）。
2. 在 `BuildShardProjection`（`:1165`）返回后、写盘之前，对**单片**测驻留增长。
   ⚠ 直接用 `GC.GetTotalMemory` 的绝对值不可靠（前面几版探针就栽在这里：
   约 110 MB 的图主导读数，两种配置都读成约 215 MB）。可行做法是
   **对同一张图、同一片重复物化 N 次**，取每次的增量并做线性拟合。
3. 对 8 片逐片测量，拟合 `bytes ≈ a*nodeCount + b*edgeCount + c`。
4. **把校准结果与语料、运行时、架构、GC 模式一起记录**
   （系数随架构与 `Server GC`/`Workstation GC` 变化）。

### 3.4 并行度解析与闸门的组合

保留 `DocumentShardParallelism` 的**两个作用**：

- `= 1`：强制串行（绕过闸门与 `Parallel.ForEachAsync`），与现在一致。
- `> 1`：作为**上限**（`numWorkers`），实际放行还受字节预算约束。
- 新增 `= 0` 或省略时的语义？**不新增**。保持默认 1，理由见 §0 最后一行。

即：`parallelism` 是上限，预算是另一个上限，两者取小。

## 4. 任务拆分（含执行结果）

| # | 任务 | 状态 | 实际结果 |
| --- | --- | --- | --- |
| **T1** | 校准每片内存估算 | ⛔ **跳过**（用户指定不做前置验证） | 改用「按记录数分摊整篇估算」，不引入未校准常数。⇒ 分摊是**预算口径**，非精确模型 |
| **T2** | ~~实现 `ShardAdmissionGate`~~ → **复用 `MemoryAdmissionGate`** | ✅ 完成 | 不新建类型；给既有类加 `PeakInFlight`/`WaitMilliseconds` 观测面 |
| **T3** | 并行路径接入闸门 | ✅ 完成 | `parallelism > 1` 时按片记账；`= 1` 分流到串行分支，**完全不建闸门** |
| **T4** | 契约测试 | ✅ 完成 | 新增 2 条（紧张预算不饿死且输出不变、并发 ≤ 预算允许数）；**已突变验证有牙** |
| **T5** | 小批量实测 | ⚠️ **降级完成** | 只跑 **1 次**端到端（非每档 3 次）⇒ 耗时数字**不可作结论**；字节同一性结论有效 |
| **T6** | 文档 | ✅ 完成 | 本文件 + `MEASUREMENT.md` 记录实测与 §1.5 的「默认预算下恒不生效」边界 |

## 5. 验证

### 5.1 必须复用的既有断言

`tests/NLISSN.ContractTests/Cpg/ProjectJsonDocumentShardTests.cs` 已有 8 条，
其中两条直接覆盖本方案：

- `ParallelShardWrites_ProduceIdenticalEntriesAndOrderingToSerial` —— 并行↔串行逐条等价。
  **已完成突变验证**：把归位改成 `perShardEntry[(shardIndex*3) % n]` 后该测试失败
  （`part-001` vs `part-003`），恢复后通过 ⇒ 该断言有牙。
- `ParallelismOne_MatchesTheLazySeamExactly` —— 默认路径不漂移。

### 5.1b ✅ 本次执行的实际验证证据（2026-09-28）

| 证据 | 结果 |
| --- | --- |
| 构建 | `NLCPG.ProjectJson` 与 ContractTests 均**已成功生成**，0 error |
| 聚焦测试 | 15/15 通过（`ProjectJsonDocumentShardTests` 10 条含新增 2 条 + `CpgProjectExportScheduleTests` + `NlissnSchemaContractTests`） |
| **突变验证（新增断言有牙）** | 把 `EstimateShardMemoryBytes` 突变为恒返回 1（使闸门形同虚设）⇒ 测试失败：**「预算只允许 2 片在途，实测峰值 4」**；恢复后全绿 |
| **端到端字节同一性** | 同一 8 片配置、并行度 4（带闸门）vs 串行：**identical=24 different=1 missing=0 total=25**，唯一差异是 `manifest.json` 的 `outputRoot`（运行目录不同，预期内） |
| `Big1.cs` payload | 826062… 两侧均为 **1,000,272,022 B / 591023 节点 / 1386027 边**，与闸门接入前记录的数值完全一致 |
| 端到端耗时（⚠ 仅 1 次） | 串行 projection=8512 write=3309；并行 projection=4337 write=6973。方向与此前一致，**样本量不足，不作结论** |

### 5.2 本方案新增的验证

| 验证项 | 方法 | 判据 |
| --- | --- | --- |
| 闸门不超发 | 单测，并发提交已知 charge | `Σ` 峰值 ≤ budget |
| 闸门不饿死 | 单片 charge > budget | 仍能放行（与既有 gate 的「优先保证至少一个在跑」同策略） |
| 预算紧张时退化 | 把 budget 设为只够 1~2 片 | 输出仍逐字节等同串行；并发实际 ≤ 预算 |
| 输出与预算无关 | 三档 budget 各跑一次 | payload SHA-256 全部相同 |

### 5.3 判据（与既有 `Export-PayloadSha256.ps1` 一致）

必须比对**原始字节**哈希，不得先解码为文本（payload 带 UTF-8 BOM）。
上一轮的比对脚本在 `MEASUREMENT.md` §复现 中，**已知坑**：用 `$s.Length + 1` 切相对路径
会产生错误键（我第一次就踩了，报出 50 个 MISSING）；必须用
`.Substring($root.Length).TrimStart('\')`。

## 6. 风险

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| **净收益 ≈ 0，本方案白做** | **高** | §0 已诚实标注。T5 的判据里明确包含「若墙钟无改善则记录为负结果」 |
| 估算偏差导致峰值失控 | 中 | T1 先校准；闸门取**上界**估计（宁可保守） |
| 估算偏差导致并行度退化为 1 | 中 | T1 的 < 15% 判据；若退化为 1，日志需可见 |
| 与并发会话的改动冲突 | 中 | 该文件此前已被并发编辑（`ProjectExportMetrics` 等）。每次编辑前重读 + 比对 mtime/hash |
| `Unsafe.SizeOf` 数值随架构/运行时变化 | 低 | 记录测量环境；不在断言里硬编码绝对值（或标注 x64 前提） |
| 大文档（NPC.cs）从未跑过真实语料 | 中 | 本方案**不声称**在 2.29 GB 上验证过；只在 fixture 上证过 |

## 7. 备选与推荐

### 7.1 推荐：先做「重叠」而非「都并行」

本方案的实测宿命是**墙钟持平**（§1.3）。真正的形态应该是
**生产者/消费者重叠**：

```
投影 worker（1 个）→ 有界 Channel<DocumentShard>(capacity: 1~2) → 写盘 worker（1 个）
```

- 投影是 CPU 密集（8474 ms），写盘是 I/O（3434 ms）。串行下二者严格交替，
  写盘时 CPU 空闲。
- 重叠后理论上墙钟 → `max(8474, 3434) ≈ 8474 ms`，
  即把写盘的 3434 ms **藏进**投影时间，收益约 **29%**（8474+3434 = 11908 → 8474）。
- capacity=1 时最多 2 片在途（1 在写 + 1 在产），峰值几乎不涨。
- 官方文档对背压语义有明确支持（[Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)）。

⚠ 但有三个必须解决的难点：
1. **`.tmp` 冲突**：多个写手并发时 `path + ".tmp"` 会不会撞？不会——每片路径不同。
   但**将来的单文件路径**（不并行）要小心。
2. **顺序**：仍须按 `shardIndex` 归位，不能按完成序。
3. **收益依赖「写盘不饱和」**：若磁盘已经饱和，重叠也拿不到 29%。

### 7.2 与本方案的关系

本方案（字节闸门）是 7.1 的**前置**：有界 Channel 的 `capacity` 若用**片数**表达，
同样会遇到 3.8x 偏斜问题；用**字节**表达则需要 §3.2 的估算器。
⇒ **T1（校准）无论走哪条路都必须先做**，它是两个方案共享的地基。

## 8. 与其它方案的关系

| 方案 | 关系 |
| --- | --- |
| `docs/plans/2026-09-28-stj-source-generation-*.md`（源生成） | **正交**。那条改的是「单片序列化多快」，本方案改的是「几片同时做」。二者可叠加，但都受 §7「写盘是瓶颈」的约束 |
| `docs/plans/2026-09-27-export-cpg-build-reuse-execution.md`（复用构图） | **量级更大**。导出 338 s 里 `build` 占 ~10.3 s/文档，是投影+写盘的量级之上。若目标是总时间，优先那条 |
| 按边连通性分区（未开计划） | **互补**。分区能同时压低膨胀（+109%）、时间与内存，且不动并发模型。**它不依赖本方案，且收益更确定** |

## 9. 未做的事（明确边界）

- 未在真实 967 文件语料上验证（NPC.cs 2.29 GB 单 payload 从未跑过）。
- 未校准 §3.2 的两个常数（这是 T1，尚未执行）。
- 未验证 NVMe/多卷下落盘并行是否转为正收益（测量机是单盘）。
- 未验证「按边连通性分区」对膨胀的影响。
- 本方案**不承诺**总墙钟下降。实测已表明净收益 ≈ 0；它的价值是**让开关安全**，
  以及为 §7.1 的重叠形态准备字节估算这一地基。
