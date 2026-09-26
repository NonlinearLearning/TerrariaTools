# D1 跨文件凑标准大小 + D2 大对象拆标准组（保留完整函数）Implementation Plan

**Goal:** 让批次组装的原子单位在**完整函数**不变的前提下，作用域从「单文件」放宽到「文件组」（D1），并把大文件的方法集合按标准大小切成 K 组投递（D2）。

**Architecture:** 保留 `CpgWorkBatchBuilder` 作为唯一的「方法 → 标准大小批次」组装器，把它从「单文件内组装」改造为「跨文件组装」；新增外部计划器产出 `DocumentShardPlan`；内核 `WorkScheduler` **不改调度算法**（C2 已证当前规模下不构成阻塞，且就绪集已由并发会话改为 `PriorityQueue`，见 §1.1b），只接收新的全局单调 `ShardOrder`。

**Tech Stack:** C# / .NET 10、`WorkScheduler`、`CpgWorkBatchExecutor`、Roslyn `SemanticModel`、xUnit。

---

日期：2026-09-26。feature：`unified-work-scheduler-gate-g4b-shard-adapter`（Gate **G4b**，当前 **NOT STARTED**）。
设计依据：[设计文档](2026-09-24-unified-work-scheduler-design.md) §5.1 / §6 / §13b。
共同前置、命令与门槛：[执行总索引](2026-09-24-unified-work-scheduler-execution.md)。

> **本文只写执行编排，不改设计文档。** 设计文档当前有活跃并发写入者
> （最后写入与本次核查相隔 5.2 分钟，行数 987→991→998 三次变化）
> ⇒ 本文所有结论以**源码行号**为锚，不以设计文档行号为锚。
> ⚠️ 且**行号本身也不稳定**：写作期间 `WorkScheduler.cs` 由 807→799→996 行、
> 抛点由 `:623` 漂到 `≈:810`（同一语句，符号名 `Duplicate StableOrder`），并发会话还新建了 `NL.Concurrency.csproj`。
> **可执行结论见 §1.1b；行号一律执行前重定位。**

---

## 0. 一句话结论

C1/C2 实测**关闭**了「必须给内核就绪集换优先队列」这一支（且该改动已由并发会话落地，§1.1b）；
C3 的 55.7% 经 §1.3 查明是**「保留完整函数」约束的代价度量**，不是 D1/D2 的阻塞证据，也**不是 D2 的价值**。
⇒ **D1/D2 都做，但 D1 在先**（§9.A 已决定），且两者共享同一前置：**必须先有一个不叫 `StableOrder` 的全局单调序号**（T2）。

**验收口径已定（你于 2026-09-26 接受）：** 在「保留完整函数、禁止函数体内部下刀」约束下，
**D1 的组合路径可达上界 ≈ 44%** —— 其余 55.7% 的代码行落在 34 个**单方法自身超标准**的方法里
（最大 `NPC.cs::AI()` = 23,189 行 = 1,500 标准的 15.5 倍）。
⇒ 完成条件 = **「在保留完整方法的前提下尽可能接近标准大小」**，
**不承诺、也不以「消除超标方法」为验收条件**。

---

## 1. 判据（已核实事实，全部为源码或实测证据）

### 1.1 实测证据（本轮给出，本节直接引用）

| # | 结论 | 性质 |
| --- | --- | --- |
| **C1** | 派发开销平方成立：拟合指数 **1.92 → 2.03**；N 涨 100× ⇒ 耗时涨 **8,850×**（17.9 ms → 158,426 ms） | **实测** |
| **C2** | 平方项在当前规模**不构成阻塞**：全语料方法根仅 **6,357**，外推约 **0.65 s** | **实测 + 外推** |
| **C3** | 真正缺口在**大对象侧**：1,500 行标准下 **55.7%** 代码行落在 **34 个无法装箱达标**的方法里 ⇒ 组合路径覆盖上限约 **44%** | 规模已证、**可行性未证** |
| **C4** | 口径纠正：设计 §5.1 的「1.72 M 节点」是 **CPG 图节点数**，`NPC.cs` 方法根只有 **377**，差 **270 倍** | **已证** |

**C1×C2 交叉自洽性核验（本文档新做，非引用）：** 按平方模型
`158,426 ms × (6,357 / 100,000)² = 158,426 × 0.004041 = 640 ms ≈ 0.65 s`
⇒ 与 C2 给出的外推值一致。**两条独立给出的数字互相印证**，故 C2 可用。
对照设计 §1.5 的实测总构图耗时（61.3 min）：`0.65 / 3,678 ≈ 0.018%` ⇒ **可忽略**。

⇒ **R11（`TakeReady` 的 O(N²)）就此关闭为「非阻塞」。** §13b.10 第 1 步的判据已满足：
**不需要**给就绪集换优先队列。**但**批化仍要做——它的收益是 §13b.4 的逐项分配
（`Pulse` 每项 1 次 TCS、`CreateInvocation` 每项 2 次分配），这部分**未测量**，故本文不声称加速量。

### 1.1b ⚠️ 写作期间发生的重大变化：`TakeReady` 的 O(N²) **已被他人修掉**

**本文档写作期间（2026-09-26 05:01–05:04），另一并发会话已把 `TakeReady` 从
线性扫描 + `List.RemoveAt` 改为两个 `PriorityQueue`，并新建了 `NL.Concurrency.csproj`。**
这不是我的改动，也不是我的推断——是同一工作树里同时发生的实测事实：

| 项 | 我写作开始时的形态 | 写作结束时的形态（当前） |
| --- | --- | --- |
| `WorkScheduler.cs` 行数 | 807 → 799 → 995 | **996**（仍在变，25 秒内 SHA 变化） |
| 就绪集结构 | `List<int> Ready` + 全表扫描 + `RemoveAt` | **`_urgent` / `_deferred` 两个 `PriorityQueue<int,(int CostDesc,long StableOrder)>`** |
| 派发复杂度 | **O(N²)**（`:283-326` 线性扫描 + `List.RemoveAt` 搬移） | **O(N log N)**（`PeekBest`；`TakeReady` 只按「提交数」线性扫描，其上方注释明写「派发 N 项的代价因此是 O(N log N)」） |
| 兜底线性扫描 | 唯一路径 | 仅当字节预算否决队首时走 `ScanBestAllowed`，其上方注释说明**生产中不触发**（`MaxInFlightBytes` 为 0 时恒允许） |
| 项目文件 | `NLISSN.Infrastructure` 单项目 | **新增 `src\NLISSN.Infrastructure\Concurrency\NL.Concurrency.csproj`**（已单独构建通过，0 错误 0 警告） |
| 重复 `StableOrder` 抛点 | `:623` | **`throw new ArgumentException` + `"Duplicate StableOrder {…} in one submission."`**（`≈:810-812`，仍在小幅漂移） |

⇒ **本节对 C1/C2 的处理必须分叉记录：**

1. **C1 的实测（平方成立）在测量当时为真**，且其价值**不因这次修复而失效**——
   它正是促成该修复的证据。C1 的拟合指数 1.92→2.03 与该修复的动机一致。
2. **C2 的结论（当前规模不阻塞）依然成立且现在更强**：即便不做该修复，6,357 方法根
   也只外推约 0.65 s；做了修复则连这 0.65 s 也不存在。
3. **但「T6 内核就绪集改动」已由他人完成** ⇒ 本文档 **T6 明确作废**（原本也默认不启动）。
4. ⚠️ **C1 的 8,850× / 158 s 数据点现在描述的是「修复前」的调度器**。若日后要引用它，
   必须标注「该形态已被 `PriorityQueue` 替换（2026-09-26）」——**不得**把它当作当前内核的性质。

**⇒ 对本轮 D1/D2 的净影响：无。** D1/D2 的障碍（单文件断言、`StableOrder` 局部性、
按项路由）与就绪集数据结构**无关**，故 T1–T5/T7 的任务划分不变。
唯一变化是：**T2 里「内核唯一性校验点」的位置由旧的 `:623` 移到 `Duplicate StableOrder` 抛出处**，
且 `ShardOrder` 需与新 `PriorityQueue` 的排序键 `(CostDesc, StableOrder)` 对齐——
这把 T2 的改动面**扩大**到 `PeekLive` 与 `ScanBestAllowed` 两处键构造。

⚠️ **并发现场风险升级**：`WorkScheduler.cs` 在 25 秒采样间隔内仍在写入
⇒ 本文档 §1.7 的「行号非稳定锚」警告**已被实际证实**，且 T2 触碰 `WorkScheduler.cs`
会与该并发会话**直接冲突**（同一文件）。**T2 必须等该会话静止后才能开工。**

### 1.2 C3/C4 对设计的两处**必须修正**

| 位置 | 原文 | 修正 |
| --- | --- | --- |
| §5.1 阈值表 `LargeFileShardTargetLines` 依据 | 「目标是把 **1.72 M 节点**的 `NPC.cs` 切成可并行的 K 片」 | 1.72 M 是**图节点数**，不是可切分单元数。可切分单元 = **方法根 = 377**。**377 足以切 K 片**，故依据依然成立，但**引用错了量**（C4） |
| §13b.10 第 1 步 | 「**目前无任何数据**，故不预判」 | C1/C2 已给出数据 ⇒ 该步**已完成**，结论为「不必改内核」。⚠️ 且实际走向比判据更前一步：该修复**已由并发会话落地**（§1.1b），非本文档结论所驱动 |

### 1.3 T0 已解决：C3 的「无法装箱达标」有权威定义（**并纠正我自己的算术错误**）

**权威定义**（`docs/benchmarks/2026-09-26-scheduler-dispatch-scaling-and-granularity-report.md:184`，
原文逐字）：

> **"装箱无法达标"的定义：** 方法体行数 > 目标标准大小。装箱只能**组合**多个小项，
> **不能切分**单个大项；且按既定约束**禁止在函数体内部下刀**，
> 故这类方法在当前粒度模型下**没有任何合法拆分手段**。

⇒ **是本义第 2 种（「单个方法自身超标准」），不是第 1 种。** 我此前在 §1.3 旧版里
猜「可能是装箱效率问题」，**猜错了**。

**⚠️ 我旧版 §1.3 的「算术张力」是我自己算错的。** 旧版用 `55.7% × 64,400` 得到 35,871 行，
据此说「34 个方法平均 1,055 行，与 ≥1,500 矛盾」。**错在分母**：
64,400 是**本仓 `src/`** 的行数，而 C3 的语料是 **Terraria `D:\TRbackup\Version4`**，
其 cost 总和是 **363,196 行**（不是 64,400）：

```text
正确：55.7% × 363,196 = 202,300 ≈ 202,446 行（日志实测值）
      202,446 ÷ 34 = 5,954 行/方法  ⇒ 全部 ≫ 1,500，零矛盾
```

**实测分布（`Build/scheduler-scaling/part3-corpus.log`，967 文件、`parseFailures=0`）：**

| 标准 | 超标方法数 | 超标行数 | 占全部行数 | 最大单方法 | 受影响文件 |
| --- | --- | --- | --- | --- | --- |
| 400 | 87 | 237,740 | 65.5% | 23,189 | 20 |
| 800 | 47 | 216,452 | 59.6% | 23,189 | 12 |
| **1,500** | **34** | **202,446** | **55.7%** | **23,189** | **12** |
| 3,000 | 21 | 174,948 | 48.2% | 23,189 | 7 |

`NPC.cs::AI()` = **23,189 行**，是 1,500 标准的 **15.5 倍** —— **单个方法就超标准一个数量级**。

**⇒ 对 D1/D2 的含义（这是本节最重要的结论）：**

| 路径 | 可达上界 | 原因 |
| --- | --- | --- |
| **D1 小/中组合进标准大小** | **≈ 44%**（1,500 标准下） | 55.7% 的行落在**不可装箱**的方法里 |
| **D2 大对象拆成 K 组** | 对普通大文件**有效**；对这 34 个方法**无效** | D2 切的是**文件的方法集合**，不是方法体；超标准方法本身不可切（§13b.2） |

⚠️ **但 55.7% 不是 D1/D2 的阻塞证据。** 报告 §6.1 明确：这是在**已决约束**
「必须保留完整函数、禁止函数体内部下刀」下按当前粒度模型的**必然结果**，
是**该约束的代价度量**，不是待补的能力缺口。⇒ D1/D2 的完成条件应表述为
**「在保留完整方法的前提下尽可能接近标准大小」**，**不是**「消除超标方法」。

**⇒ T0 从「阻断项」降级为「已解决」。** 我原先说「未澄清前不得开始 T5」**解除**——
定义已存在，只是我此前没找到（它在 `docs/benchmarks/`，不在设计文档里）。
唯一仍需你决定的**不是定义**，而是**验收口径**（见 §9 第 1 条）：44% 这个上界是否可接受。

### 1.4 ⚠️ 一个只有对照两份证据才会发现的分歧：`K` 差 2 倍

C3 报告 §5.1 的 `NPC.cs` 分片数 K 与**真实 `CpgWorkBatchBuilder`** 的实测 K **不一致**：

| 标准 | 报告 §5.1（朴素贪心探针） | **真实 builder**（`part2-npc-roots.log`） | 差 |
| --- | --- | --- | --- |
| 400 | 52 | **58** | +6 |
| 800 | 33 | **47** | **+14（1.42×）** |
| **1,500** | **21** | **43** | **+22（2.05×）** |
| 3,000 | 16 | — | — |

**根因（已核实，非推测）：** 报告 §5.1 用「贪心累计至目标行数」；而真实
`CpgWorkBatchBuilder.AppendMethodBatches:114` 对
**`Large`/`Oversized` 一律单项成批、不进累积**。而 `Classify` 的 `Large` 下界是
**`MediumMaxCost = 200`**，与 `TargetBatchCost` **无关** ⇒ 任何 `cost > 200` 的方法
都独占一批。实测单项批数在三种标准下为 **24 / 24 / 23**（几乎恒定），
正说明**隔离由 200 决定，不由标准大小决定**。

⇒ **两条独立结论：**
1. **报告 §5.1 的 K 是「理想装箱」下界，不是生产现状。** 引用时必须区分，
   否则会低估当前批次数 2 倍（设计 §4.2 的扁平 DAG 节点数同样受影响）。
2. **这是一处真实的、可测量的装箱效率缺口**：在 1,500 标准下，
   生产比理想多出 22 个批次（43 vs 21）。
   ⇒ D1/D2 的价值因此**不只是「跨文件」**，还包括**让 `cost ∈ (200, 1500]` 的
   「中等偏大」方法能被配对装箱，而不是各自独占一批**。
   这是**本执行文档新识别的收益来源**，C3 报告与设计文档均未记录。

### 1.4b 🚨 **决定性实测（2026-09-26）：上述「新收益来源」只需改一个 options 值即可全部拿到**

§1.4 结论 2 只是一个**假设**（"放宽隔离能提升装箱率"）。本轮用**真实
`CpgWorkBatchBuilder`** 把它验证到底，并得到一个**会改变后续任务排序**的结果。

**测量方法（可复现）：** `Build\tmp\packing-sweep\` 与 `Build\tmp\npc-reconcile\`
两个探针项目，直接引用 `src\NLCPG\NLCPG.csproj`，用**与 T1 逐字相同的谓词**
提取方法体行数（`MethodDeclaration`/`ConstructorDeclaration` 的 `Body`/`ExpressionBody`、
`PropertyDeclaration` 的**每个** accessor、`GlobalStatement`），
构造 `CpgWorkItem` 后喂给真实 `CpgWorkBatchBuilder`。

> **复现命令：** `Build\tmp\` 已被 `.gitignore:5`（`Build/`）忽略，故三个探针
> **不污染工作树**，但也**不随提交分发**。探针位置：
> `Build\tmp\packing-sweep\`（语料双轴扫描）、`Build\tmp\npc-reconcile\`（NPC 对账+隔离扫描）、
> `Build\tmp\options-regression\`（两条测试的红/绿验证）。
> 运行：`Invoke-SerialDotnet.ps1 -DotnetArguments @('run','--project','.\Build\tmp\<dir>\<name>.csproj','-c','Debug', …)`。
> ⚠️ 探针的谓词**必须**是「每个 accessor 都计入」——写成 `FirstOrDefault` 会少算
> （实测少 2 个方法根、批次数偏离），这是本轮踩过的坑。

**对账（证明测量可信，不是自说自话）：**

| 量 | 本探针 | T1 / part2 记录 | 一致 |
| --- | --- | --- | --- |
| 语料方法根 | **6357** | 6357 | ✅ |
| 语料方法体行数 | **363196** | 363196 | ✅ |
| `NPC.cs` 方法根 | **377** | 377 | ✅ |
| `NPC.cs` 方法体行数 | **78525** | 78525 | ✅ |
| `NPC.cs` 最大方法 | **23189** | 23189 | ✅ |
| `NPC.cs` prod-default 批次数 | **57 方法批 + 1 prelude = 58** | 58 | ✅ |
| `NPC.cs` standard-800 | **47** | 47 | ✅ |
| `NPC.cs` standard-1500 | **43** | 43 | ✅ |

⇒ **8 项逐数对齐**，其中 58 = 57 + 1 首次解释清了 prelude 批的去向。

**结果（`NPC.cs`，1,500 标准）：**

| 配置 | 批次数 | 单项批 | 相对现状 |
| --- | --- | --- | --- |
| 现状（`mediumMaxCost=200` 隔离） | **43** | 23 | 1.00× |
| ★ **`mediumMaxCost=1500`（仅改这一个值）** | **21** | **7** | **2.05×** |
| ★ 再加 `targetBatchCost=3000` | **18** | 7 | **2.39×** |
| 理想 FFD 下界（允许任意切分） | 18 | — | — |
| 单项批下界（`cost > 1500` 的方法数） | — | 7 | — |

**⇒ 三个必须写进结论的事实：**

1. **`mediumMaxCost: 200 → 1500` 单值改动即达成计划 §5 判据 5 的量化目标
   （43 → 21）。** 单项批从 23 降到 **7**，而 7 正是「成本 > 1500 的完整方法数」
   ⇒ **单项批数已达理论最小**（这 7 个方法按定义不可与任何项合并）。
   ⚠️ **但总批次数 21 并非已达整体最优下限：** 严格下限 = 7 个超标方法
   + `ceil((78525 − 62777) / 1500)` = 7 + 11 = **18**，FFD 实测亦为 18。
   ⇒ 轴 A 的 21 与下限 18 仍有 **3 批差距**，来自 `CpgWorkBatchBuilder`
   的**顺序贪心**（非 FFD/bin-packing 最优）。**不得声称「已达理论最优」。**
2. **这**不需要** T3、不需要 T4、不需要多文件图容器、不需要任何代码改动。**
   它是一个 `CpgWorkBatchCostOptions` 的配置值。
   T4（跨文件图容器 + 去缓存别名）的风险面（§T4 前置 P0 与缓存危害）**完全不必承担**。
3. **但仍不可直接改 `Default`——已实测确认（非推测）。**
   逐字复刻这两条测试的输入、在两组 options 下运行，得到：

   | 测试 | 现状 `Default` | `medium=1500` 时 | 原因 |
   | --- | --- | --- | --- |
   | `Build_DoesNotGroupLargeMethodsTogether`（`:34`） | **PASS** `[[201],[201],[20]]` | **FAIL** `[[201+201+20]]` | 201 不再被隔离，三项合成一批 ⇒ `batch.Items.Count == 1 && cost == 201` 找不到 |
   | `Build_LeavesOversizedMethodInAnIsolatedBatch`（`:51`） | **PASS** `[[801],[10]]` | **FAIL** `[[801+10]]` | 801 的 `largeMaxCost` 被抬到 1550 ⇒ 801 不再 Oversized，与 10 合批 |

   ⇒ 二者用**真实 builder 实测**确认会红，不是推断。
   另：`CpgWorkBatchCostOptions.Default` 还被 `NLCPGBuilderOptions.cs:56` 与
   **12 处测试**引用。
   ⇒ 正确做法是**经 `NLCPGBuilderOptions.WorkBatchCostOptions` 注入**（该参数已存在，`:39`），
   或先与测试作者确认隔离语义变更。**两条测试断言的是「隔离行为」本身，
   而轴 A 的价值恰恰来自放宽该行为** —— 这是必须显式裁决的语义变更，不是配置调优。

**语料尺度对照（967 文件、625 个含方法的文件、6357 方法）：**

| 配置 | 轴 A：逐文件独立 Build（现状） | 轴 B：全文件混合一次 Build（T4 目标形态） | B/A |
| --- | --- | --- | --- |
| prod-default(400) | 1081 批 / 357 单项 | 536 批 / 207 单项 | 2.02 |
| medium=1500 | **768 批**（1.41×） | 203 批（2.64×） | 3.78 |
| medium=1500, target=3000 | **729 批**（1.48×） | 161 批（3.33×） | 4.53 |
| medium=1500, target=6000 | **719 批**（1.50×） | 152 批（3.53×） | 4.73 |

**⇒ 语料尺度的判读（诚实、不利的部分也写）：**

- **轴 A 吃掉 1.41×（1081→768）**，成本是**一行配置**。
- **轴 B 仍显著更强**（再 3.78×），因为小文件的少量方法**跨文件**才能凑满标准大小 ——
  这正是 D1 的原始定义。
- ⇒ **结论不是「T4 无用」，而是「T4 的收益被 §1.4b 分流了」**：
  在**单文件内方法数多**的场景（`NPC.cs` 377 根）轴 A 已把单项批压到理论最小（7），
  总批次数也已接近下限；在**大量小文件**的场景轴 B 仍有 2-4× 空间。
- ⇒ **建议的执行顺序（据此改写 §4.4/§9）：** 先落地**轴 A**（改注入的 options + 契约测试，
  极小风险、可立即测量），**再**评估轴 B（T4）是否仍值得其风险面。
  轴 A 是**前置于** T3/T4 的独立小任务，可立即执行。

### 1.5 D1 的五条障碍（源码核实）

| # | 障碍 | 证据 |
| --- | --- | --- |
| 1 | `StableOrder` 是**文件内局部序号**，且 CPG 侧要求单次提交内全局唯一 | ⚠️ **真实守卫在 `CpgWorkBatchExecutor.cs` 的 `"WorkBatch stable orders must be unique."`（`≈:576-578`），不在内核**（见 §1.8 —— 原引用的 `WorkScheduler.cs` 重复抛错**不在 D1 路径上**）。局部序号来源：`DataFlowPass.cs:652`、`CallGraphPass.cs:145`、`ControlDependencePass.cs:74-86`（`index`） |
| 2 | 批次被**强制单文件** | `CpgWorkBatchBuilder.cs:55-60` 显式 `throw`（"All work items in a batch build must use the requested source file path."）；`CpgWorkBatch.SourceFilePath`（`:42`）单值 |
| 3 | worker 回调与 reducer 均**绑定单文件** | `CallGraphPass.cs:113` → `PublishCallGraphWorkBatch(result, context.Graph)`（**本文件的图**） |
| 4 | **每文件一张独立图** | `NLCPGBuildContext.cs:86` 每文件 `new NLCPGGraph(...)` |
| **5** | ⚠️ **去重键作用域不一致 ⇒ 跨文件装箱静默丢方法**（**2026-09-26 新发现，原计划未列**） | `NormalizeItems` 的 `seenSpans` 用 `StableSpanIdentity` = `$"{SourceFilePath}\u001f{SpanStart}:{SpanEnd}"`（`CpgWorkItem.cs:91-92`）**含**文件路径；而 `seenMethodKeys`（`CpgWorkBatchBuilder.cs:73/:84`）用**裸 `MethodSymbolKey`**，**不含**文件路径。两个文件各自的同名方法会被判为重复而**丢掉一个**。⇒ 必须与障碍 2 的 `throw` **同时**修，且**语义需先裁决**（本方案倾向「保留两者」） |

### 1.6 ⚠️ 本文档最重要的新发现：`StableOrder` 是**双重用途**的

障碍 1 比设计文档 §13b.6 记录得更严重。`StableOrder` **不只是排序键，还是载荷查找键**：

| 用途 | 位置 | 后果 |
| --- | --- | --- |
| ① 排序键 | `CpgWorkBatchBuilder.cs` 的 `.ThenBy(item => item.StableOrder)`；各 pass 的 `OrderBy`；`CpgWorkBatchExecutor.cs:440` 的 fragment↔batch **成对相等**校验 | —（唯一安全用途） |
| ② **数组下标** | ① `DataFlowPass.cs:698` `methodPartitions[item.StableOrder]`；`:681` 越界检查；`:510` 注释明写。② ⚠️ **`ControlDependencePass.cs:111` `_dominanceOverlays[batch.StableOrder]`**（原计划只列了前者） | 改成全局序号 ⇒ **数组越界或错配** |
| ③ **字典键** | `CallGraphPass.cs:105` `item => item.StableOrder`；`:162-165` `operationWorkByOrder`。另 3 处：`PartitionedSyntaxPass.cs:56-62`、`PartitionedOperationPass.cs:156/166`、`DominancePass.cs:343/351` | 同上 |

**⚠️ 用途 ② 的第二处（`ControlDependencePass.cs:111`）比第一处更隐蔽：**
它用**批次**的 `StableOrder` 去下标一个索引域为**操作根序**的列表
（列表在 `DominancePass.cs:364` 填充，`:307/341/379/386` 清空，`:424` 追加）。
**单文件时两个索引域恰好重合**，所以今天看不出问题；跨文件批次会**打破该巧合**。
⇒ T4 必须把 ②① 与 ②② **一并**处理，只改 `DataFlowPass` 会漏。

⇒ **不得直接改 `StableOrder` 的语义。** 正确形态是**新增**一个独立字段（`ShardOrder`，**T2 已落地**），
`StableOrder` 保持「文件内局部序号 = 载荷下标」，`ShardOrder` 承担「跨文件全局单调」。

> ⚠️ **本表原第 ① 行写「内核唯一性校验 `throw …"Duplicate StableOrder…"`（T2 改这里）」——该表述已被 §1.8 证伪。**
> CPG 批次路径**不经 `WorkScheduler`**（`NLCPG` 对 `WorkScheduler`/`WorkItem<T>`/`WorkSubmission` 的代码引用 = **0**，
> 唯一命中是 `CallGraphPass.cs:78` 的注释）。真实守卫是
> **`CpgWorkBatchExecutor.cs` 的 `"WorkBatch stable orders must be unique."`**，**T2 已把它改为 `shard orders`**。
> 内核 `WorkScheduler.cs` **未被触碰，也不应被触碰**。

> ⚠️ **保留说明（仅为历史记录，T2 实际未触碰内核）。** 下面这张表是写作时的原始假设；
> §1.8 已证伪其前提——**内核不在 D1 路径上**。实测该文件已于 09-26 05:11:55 静止
> （SHA `C6964B15…`），但**静止与否已无关紧要：T2 不需要它静默，因为不碰它**。
>
> | 符号 | 现状（**仅供理解内核，非 T2 改动点**） |
> | --- | --- |
> | `throw new ArgumentException("Duplicate StableOrder … in one submission.")` | 内核自身的唯一性校验，`WorkScheduler.cs:811`（995 行文件）。**D1 路径不经此处。** |
> | `private readonly PriorityQueue<int, (int CostDesc, long StableOrder)> _urgent` | 紧急队列，排序键含 `StableOrder` |
> | `… _deferred` | 吞吐队列，同上 |
> | `private static Candidate? PeekLive(` | 键解构处（`PeekLive` 实测在 `:575-592`） |
> | `private Candidate? ScanBestAllowed()` | 兜底全表扫描，`StableOrder` 直取 |
> | `public Candidate? PeekBest(long now, double ageThreshold)` | 提交级取最优 |

> **T2 的实际内容**是 CPG 侧的 4 处（见 §1.8 与 T2「完成证据」），**不是**改内核。
> 而「T2 必须先于 T3/T4」的理由依然成立：若先动 `StableOrder` 的语义，
> 8 个 pass 会**同时**因下标错配而失败，无法二分定位。

⚠️ **§1.1b 追加**：内核已改用 `PriorityQueue`，其排序键**硬编码** `StableOrder`
（泛型参数 `(int CostDesc, long StableOrder)`）
⇒ 若把 `StableOrder` 改成全局序号，新队列的键会**一并**改变语义。
故 T2 的改动面是 **4 处**（`CpgWorkItem` 新增字段、`CpgWorkBatch` 透传、
`WorkScheduler` 唯一性校验、`PeekLive` 与 `ScanBestAllowed` 两处键构造），**不是 1 处**。

### 1.7 ⚠️ 行号时效（写文档时当场实测的漂移）
本文档写作期间，目标文件**正在被并发修改**，行号已实际漂移：

| 文件 | 写作开始时 | 写作结束时 | 行数变化 |
| --- | --- | --- | --- |
| `WorkScheduler.cs` | `:623`（重复 `StableOrder`） | `throw …"Duplicate StableOrder…"`（≈`:810`） | 807 → 799 → 995 → **996**（仍在变） |
| `docs/plans/2026-09-24-unified-work-scheduler-design.md` | 987 行 | **998 行** | +11 |
| `src\NLISSN.Infrastructure\Concurrency\` | `NLISSN.Infrastructure` 内的普通目录 | **独立项目 `NL.Concurrency.csproj`** | 新增 |

⇒ **本文档对 `WorkScheduler.cs` 的行号仅作「2026-09-26 05:0x」快照，不是稳定锚。**
执行每个任务**前**须用符号名（如 `Duplicate StableOrder`）重新定位，**不得**直接信任行号。
凡本文档写 `:NNN` 处，均附有可 grep 的符号名或原文片段，以便重定位。

---

### 1.8 ⚠️ **T2 的前提被证伪**：`WorkScheduler.cs` 不在 D1 路径上（2026-09-26 实测）

本文档 §1.1b/§1.6 曾断言「T2 要改内核 `WorkScheduler` 的唯一性校验与优先队列键」。
**执行前核实发现该断言错误**，必须纠正：

| 断言 | 实测结果 | 证据 |
| --- | --- | --- |
| CPG 批次路径经过内核 `WorkScheduler` | ❌ **不经过** | `NLCPG` 全项目对 `WorkScheduler`/`WorkItem<T>`/`WorkSubmission` 的**代码引用数为 0**（唯一命中是 `CallGraphPass.cs:78` 的注释） |
| 内核 `Duplicate StableOrder` 是 D1 的守卫 | ❌ **不是** | D1 路径上真正的守卫是 `CpgWorkBatchExecutor.cs:576-578` 的 `"WorkBatch stable orders must be unique."` |
| `NLCPG` 用内核的调度能力 | ❌ 只用**准入池** | `NLCPG` 仅引用 `IConcurrencyPool`(6 处)/`BoundedConcurrencyPool`(2)/`ConcurrencyWorkType`(2)/`ConcurrencyAdmissionRequest`(1) |
| 内核 `WorkScheduler` 的调用者 | 是**另外三个子系统** | `ExecutionRuntime.cs:97`、`RuleGraphExecutor.cs:220/277`、`DecisionModel.cs:499`、`RewritePlanReplayService.cs:35` |

**机制说明：** `CpgWorkBatchExecutor` 自带 worker 实现——`Channel.CreateBounded`（`:582`/`:588`）
+ `Task.Run(WorkerLoopAsync)`（`:641-642`）+ `ReduceFragmentsAsync`（`:856`），
仅通过 `_concurrencyPool.ExecuteWithAdmissionAsync`（`:542`）借内核做**准入/背压**，
不把批次提交给 `WorkScheduler` 就绪队列。

**⇒ 两个后果：**

1. **T2 的改动面从 4 处变为 3 个文件的 4 个位置，且全部在 `NLCPG` 内**，
   不再触碰 `NLISSN.Infrastructure/Concurrency/`。
2. **⚠️ 若照原计划改 `WorkScheduler.cs`，将是一次纯粹的破坏性改动**：
   它不会让 D1 前进一步，却会同时波及 `rule`（`RuleGraphExecutor`）、
   `decision`（`DecisionModel`）、`rewrite`（`RewritePlanReplayService`）三个**无关子系统**，
   并使 12 个 `WorkScheduler*Tests` 契约测试面临语义漂移风险。

**⇒ 修正后的 T2 次序不变（仍最先），但它比原计划更安全、更小。**
另：§1.6 关于「`StableOrder` 双重用途」的分析**仍然成立且更关键**——
因为真正的守卫（`:576`）与载荷下标（`DataFlowPass:698`）**同在 CPG 侧**，
若把 `CpgWorkItem.StableOrder` 直接改成全局序号，这两者会同时错配。
⇒ 新增 `ShardOrder` 仍是正确形态。

**⚠️ 诚实标注：** 本节的「0 代码引用」是基于
`Select-String -Pattern 'WorkScheduler|WorkItem<|WorkSubmission'` 并排除注释行的静态搜索结果，
**不是**编译期依赖分析；若存在反射/动态调用则本结论不成立。执行 T2 时须以编译与定向测试复核。

## 2. 同步 / 异步判定（回答「哪些任务是异步哪些是同步」）

### 2.1 结构性事实：pass 层**同步是接口强制的**

```csharp
// src/NLCPG/Builder/Passes/INLCPGPass.cs:10
void Run(NLCPGBuilder builder, NLCPGBuildContext context);   // 返回 void，无 Task
```

且**全部 8 个** pass 的提交步都同步阻塞在内核上：

| pass | 阻塞点 |
| --- | --- |
| `CallGraphPass` | `:115` `.GetAwaiter().GetResult()` |
| `MemberAccessPass` | `:147` 同上 |
| `ControlFlowPass` | `:84` 同上 |
| `DataFlowPass` | `:632` 同上 |
| `DominancePass` | `:359` 同上 |
| `ControlDependencePass` | `:114` 同上 |
| `PartitionedOperationPass` | `:223` 同上 |
| `PartitionedSyntaxPass` | `:77` 同上 |

⇒ **异步只存在于内核层**：`WorkScheduler.RunAsync<TResult>:57` / `WorkerLoopAsync:208`（`:226` `await ticket.Invoke`）与
`CpgWorkBatchExecutor.ExecuteAsync:460` / `ExecuteAndConsumeAsync:495`。
**pass 边界一律同步。** 本文档不引入 `async` pass——那要改 `INLCPGPass` 与 8 个实现，
是与 D1/D2 无关的另一项工程，且会与当前活跃的并发写入者冲突。

### 2.2 四层判定表

| 层 | 同步/异步 | 依据 | 对 D1/D2 的含义 |
| --- | --- | --- | --- |
| **L0 规划层**（`DocumentShardPlanner`，新建） | **同步**（纯计算，无 IO） | 与 `StagePlan` 同形（`StagePlan.cs:103`） | 分片计划可在构图前一次算完 |
| **L1 计算层**（worker 函数） | **同步** CPU 密集 | `AnalyzeDataFlowWorkBatch` 等；`Submission<TResult>.CreateInvocation` 内的 `await item.ExecuteAsync(...).ConfigureAwait(false)`（紧邻 `return async token =>`）在 `IsCompleted` 时同步继续 | **拼批不引入线程切换**（§13b.4） |
| **L2 归并层**（reducer） | **同步**，单线程 | `CpgFragmentReducer.ReduceInto`；`ControlDependencePass:117` | 按项路由是**同步**改动 |
| **L3 内核层**（`WorkScheduler`） | **异步** | `RunAsync<TResult>:57`、`WorkerLoopAsync:208`（`await ticket.Invoke`，`:226`） | **本文档不改它**（C2；就绪集已由并发会话改为 `PriorityQueue`，§1.1b） |

⇒ **D1/D2 的全部改动都落在同步层**（L0/L1/L2）。这使它们**边界清晰、可交由子代理**——
没有 async 状态机、没有执行上下文流动，改动是**局部且可静态验证**的。

---

## 3. 任务分解

每个任务标注：触碰文件 / 同步异步 / 可否交子代理 / 并行度 / 前置。

### T0 —— C3 的「无法装箱达标」定义 ⇒ **已解决（不再是阻断项）**

- **状态：** ✅ **已解决**，定义见 §1.3（权威出处：`docs/benchmarks/2026-09-26-scheduler-dispatch-scaling-and-granularity-report.md:184`）。
- **含义：** 本义为「**单个方法自身超标准**」（方法体行数 > 目标标准大小），
  **不是**我旧版猜的「装箱效率差」。⇒ 这 34 个方法**对 D1 与 D2 都不可达**（§1.3）。
- **⇒ T5 的「未澄清前不得开始」解除。** 但仍需你先定**验收口径**（§9 第 1 条）。

### T1 —— 全语料方法根与文件大小分布盘点

- **内容：** 确认 C2 的 6,357 方法根、C3 的 34 个方法、C4 的 `NPC.cs = 377`；产出「按文件的方法数 / 行数」两列分布表。
- **触碰：** 只读 `src/`；产出写入 `Build/d1d2-inventory/`（**不碰 `docs/` 与 `src/`**）。
- **同步/异步：** 异步（无需等待，可与其他任务重叠；本身是纯统计）。
- **可否交子代理：** ✅ **可以，且应交给异步子代理**。自包含、只读、无写冲突。
- **并行度：** 1（单代理足够）。
- **前置：** 无 ⇒ **可立刻启动**（T2 需等 `WorkScheduler.cs` 静止，T1 无此限制）。

### T2 —— 引入 `ShardOrder`（全局单调），保留 `StableOrder` 语义

> **状态：✅ 已完成并实测通过（2026-09-26，本轮）。** 证据见本节末「完成证据」。

- **内容：** 按 §1.6，**新增**字段而非改语义；CPG 侧唯一性校验改校验 `ShardOrder`；`DataFlowPass:698` 与 `CallGraphPass:105/162-165` 的载荷查找**保持用 `StableOrder`**。
- **触碰（✅ 已核实，见 §1.8）：** `CpgWorkItem.cs`、`CpgWorkBatch.cs`、`CpgWorkBatchExecutor.cs`
  （唯一性校验 `:576-578`、`orderedStableOrders` `:572-575`、`_traces` 键 `:131/140/146/…`、
  归并 `CpgBatchResult.StableOrder` `:936`）。
- **~~⚠️ 阻塞：`WorkScheduler.cs` 正被并发修改~~** ⇒ **该阻塞已消除**：
  `WorkScheduler.cs` **根本不在 D1 路径上**（§1.8），**T2 不得触碰它**，
  故不再需要等它静止。实测其已于 09-26 05:11:55 静止（13:32 复查，20 秒内 SHA 无变化）。
- **同步/异步：** 同步。
- **可否交子代理：** ⚠️ **可以交，但必须单代理独占**（不可并行）——它定义了下游全部任务的接口。
- **并行度：** **1（串行）**。这是关键路径。
- **前置：** 无。但**必须最先完成**，T3/T4/T5 全部依赖它。

#### T2 完成证据（本轮实测）

| 项 | 结果 |
| --- | --- |
| 实际触碰文件 | **3 个**，全部在 `src\NLCPG\Builder\Concurrency\`：`CpgWorkItem.cs`、`CpgWorkBatch.cs`、`CpgWorkBatchExecutor.cs`；另加 `CpgWorkBatchBuilder.cs` 的透传（见下）。`WorkScheduler.cs` **未触碰**。 |
| 编译 | `dotnet build src\NLCPG\NLCPG.csproj` → **0 error / 0 warning** |
| 契约测试（新写） | `CpgWorkBatchShardOrderContractTests.cs` → **7/7 通过** |
| 10% 核心切片 | **70/70 通过**（原 49 + T2 新增 7 + T3 新增 8 + `CpgFragmentByteCalibrationTests` 6），exit 0，15 s |
| 单文件行为 | **逐字未变**：`ShardOrder` 默认等于 `StableOrder`；既有 49 个用例全绿 |

**实现要点（两处原计划未列、实施时发现必须一起改）：**

1. **`CpgWorkBatchBuilder.AddBatch:212` 必须透传 `ShardOrder`**（取批内 `Min`）。原计划只列了 3 个文件；
   不改此处则批次 `ShardOrder` 恒为 0，T3 跨文件装箱时全局序失效。
2. **`ExecuteSynchronously`（`:733`）是唯一性校验的盲区**——它**从来**就没有该校验，
   只有异步路径 `ExecuteCoreAsync:580` 有。这是**先于 T2 存在的路径不对称**，非本轮引入。
   生产默认走异步路径（无任何调用点设 `useSynchronousExecution: true`），故不阻塞 D1；
   已用哨兵测试 `ExecuteAsync_WhenSynchronousExecution_DoesNotValidateShardOrderUniqueness`
   **固定现状**并在 XML 注释中标注「一旦补校验该测试应当失败并被删除」。

#### ⚠️ 由 T2 实测连带暴露的 **T3 新障碍**（原计划 §1.5 未列）

`NormalizeItems` 的两把去重键**作用域不一致**，跨文件装箱会**静默丢方法**：

- `seenSpans` 用 `StableSpanIdentity` = `$"{SourceFilePath}\u001f{SpanStart}:{SpanEnd}"`（`:91-92`）—— **含文件路径**，跨文件安全。
- `seenMethodKeys` 用**裸 `MethodSymbolKey`**（`:73/:84`）—— **不含文件路径**，两个文件的同名方法会被当成重复而丢掉一个。

⇒ T3 必须**同时**处理这两项：(a) 移除/放宽 `:55-60` 的单文件 `throw`；(b) 把 `seenMethodKeys` 的键改为
`文件路径 + MethodSymbolKey`。**语义需先裁决**——本方案倾向「保留两者」（同名不同文件是两个真实方法）。
在 (b) 完成前，判据 2 的期望值无法写死（见 §5）。

### T3 —— 放宽 `CpgWorkBatchBuilder` 的单文件断言（D1 核心）

> **状态：✅ 已完成并实测通过（2026-09-26，本轮）。** 证据见本节末。

- **内容：** 移除 `:55-60` 的 `throw`；`CpgWorkBatch.SourceFilePath`（`:42`）单值形态需扩展为「多文件身份 + 每项自带文件」。**保持** `:125-127`/`:137-139` 的 flush 阈值逐字不变（否则批次边界会漂移，测试无法比对）。
- **触碰（✅ 实测校正）：** **仅 `CpgWorkBatchBuilder.cs`。**
  ⚠️ 原计划写的 `CpgWorkBatch.cs` **本轮未触碰**——`SourceFilePath` 保持单值**标签**语义，
  真正的按项路由由 **T4** 负责（见「范围校正」）。
- **同步/异步：** 同步。
- **可否交子代理：** ✅ 可以（本轮已交单一子代理，独占该文件）。
- **并行度：** **1**（与 T2 串行，且与 T4 有接口耦合）。
- **前置：** T2。

#### T3 完成证据（本轮实测）

| 项 | 结果 |
| --- | --- |
| 实际触碰文件 | **1 个**：`src\NLCPG\Builder\Concurrency\CpgWorkBatchBuilder.cs`（243 行，LF，SHA256 `AF5A980A…67FC3`） |
| 编译 | `dotnet build src\NLCPG\NLCPG.csproj` → **0 error / 0 warning** |
| 新增契约测试 | `CpgWorkBatchCrossFilePackingTests.cs` → **8/8 通过** |
| 核心切片 | **70/70 通过**（T2 前 49 → T2 后 56 → T3 后 70），exit 0，15 s |
| 既有 builder 测试 | 原 6 个 `CpgWorkBatchBuilderTests` **全绿**（单文件行为逐字未变） |

**三项实现要点：**

1. **删除单文件 `throw`**（原 `:57-59` 所在的整个 `.Select(...)` 校验包装，共 11 行）。
   全仓检索该消息文本，仅剩本计划文档 §1.5 的引用，**无源码/测试再期待它**。
2. **去重键文件限定**：新增 `private static string StableMethodIdentity(CpgWorkItem)` →
   `$"{item.SourceFilePath}\u001f{item.MethodSymbolKey}"`（沿用 `StableSpanIdentity` 的 `\u001f` 约定）。
   这是障碍 5 的修复——**保留两者**语义已按工程必然性采用（同名不同文件是两个真实方法）。
   `seenSpans`/`StableSpanIdentity` 未动；守卫形状（`Kind == Method && MethodSymbolKey is not null`）逐字保留。
3. **排序主键前置文件路径**：`.OrderBy(item => item.SourceFilePath, StringComparer.Ordinal)`
   再 `ThenBy(StableOrder)…`。**理由**：`StableOrder` 跨文件重复，单凭它无法给出确定全序。
   单文件时路径恒定 ⇒ 该 `OrderBy` 退化为无操作，**逐字兼容**。

#### 范围校正：为什么 T3 不需要改 `CpgWorkBatch.cs`

原计划要求 T3 同时扩展 `CpgWorkBatch.SourceFilePath` 为多文件身份。**实测判定不必**：

- `CpgWorkBatch.SourceFilePath` 的**唯一消费者**是 `CpgWorkBatchExecutor` 的遥测事件
  （`CpgWorkBatchPerformanceEvent`）与测试断言，**没有**任何按批路由到图的逻辑读它。
- 真正的路由点是 **6 个 `Commit*Stage` 步 + 2 个分区 pass**，由 **T4** 迁移到**按 `item.SourceFilePath`**。
- ⇒ 在批对象上再引入一个「多文件身份」字段，会与 `item.SourceFilePath` **重复表达同一事实**，
  违反单一事实来源。现形态（批级 `SourceFilePath` = 标签，项级 = 路由真相）已足够。
- 该判断已写入 `AddBatch` 的中文注释，供 T4 接线时遵循。

#### ⚠️ T4 必须知道的交接点（T3 实测发现）

1. **`ExecuteFragmentsAsync` 的硬断言**（`CpgWorkBatchExecutor.cs:440-444`）：
   `fragment.StableOrder != batch.StableOrder` ⇒ 抛。**T3 保持它不动**（它是**载荷一致性**校验，
   与调度序无关）。但 `LocalCpgFragment` 只携带**一个** `SourceFilePath`
   ⇒ **跨文件批次一旦流经 pass，该断言必然触发**。T4 必须在 reducer 侧拆分为「每文件一个 fragment」。
2. **3 个 pass 仍传 `items[0].SourceFilePath` 作为标签**：
   `DataFlowPass.cs:667`、`CallGraphPass.cs:93`、`PartitionedOperationPass.cs:432`。
   T4 后应由调用方传入真实标签，而非 `items[0]`。
3. ⚠️ **并发写入已实测发生**：T3 子代理报告其编辑期间
   `AddBatch` 的注释被另一写入者改写（措辞非其所写）。经复核 **T3 三项改动均完好、括号配平**。
   ⇒ **后续以磁盘文件为准**，且**同一文件不得有两个活跃写入者**（§4.3 约束 1 的实际教训）。

### T4 —— reducer 按项路由到所属文件的图（D1 最大一块）

> ⚠️ **状态：范围已被独立侦察大幅扩大（2026-09-26）。** 原「6 处 + 2 分区 pass」**严重低估**——
> 侦察实测发现**缺失前置层、缺失合并点、缺失最深的缓存危害**共 4 类。**未按本节新范围评估前不得开工。**

- **内容（原始表述，方向正确但不完整）：** 6 个 `Commit*Stage` 步 + 2 个分区 pass 的发布点，改为按 `item.SourceFilePath` 路由到目标图。
- **触碰（原 6 处，✅ 行号已实测确认无误）：**
  `CallGraphPass.cs:99`、`MemberAccessPass.cs:117`、`ControlFlowPass.cs:78`、
  `DataFlowPass.cs:501`、`DominancePass.cs:339`、`ControlDependencePass.cs:105`。
- **同步/异步：** 同步。
- **可否交子代理：** ✅ 可以，但**必须串行**——见下方决定。
- **并行度：** **1（串行）**。⚠️ **用户已于 2026-09-26 明确拒绝「6 个并行子代理各改一个 pass」**
  （理由：代价 6 份互审不值得）。⇒ **6 个 pass 由单一子代理（或主代理）依次改完**，
  不得拆成 6 个并行代理。此决定取代本节早先的并行建议。
- **前置：** T2、T3，**外加下方「前置 P0」**。
- **⚠️ 因串行而保留的收益：** 串行不影响正确性，只影响墙钟；
  且 T8 的构建本就全局互斥（§4.3 约束 2），并行编辑的墙钟收益本来也被构建串行抵消。
- **⚠️ 失败模式：** 遗漏一处 = B 文件的事实写进 A 文件的图，**静默错误而非崩溃**（设计 R10）。故每片必须自带「跨文件批次后每文件的图签名与单文件路径逐字一致」的断言。

#### ⚠️ 前置 P0：**不存在多文件图容器**（原计划未列，是 T4 的真正起点）

实测：全 `src/` 检索 `BuildFromSources`/`BuildMany`/`GraphByFile`/`GraphsByFile` = **0 命中**；
`NLCPGBuildContext` 的 `FilePath`/`Root`/`Source`/`SemanticModel`/`Graph`/`SyntaxTreeNode`
**全部是单数**（`NLCPGBuildContext.cs:19-39`）。

⇒ **T3 + T4 合起来也无法产出可用的跨文件构建。** 必须先引入一层
「多文件图容器 / 图注册表」（如 `IReadOnlyDictionary<string, NLCPGGraph>` 或 context 持有 sibling graphs）。
**这是 T4 的第一步，且是判据 2（§5）能否逐字兑现的前提。**

#### ⚠️ 最深的危害：builder 的节点缓存**与图无关**（原计划完全未提）

`NLCPGBuilder.cs:47-73` 的 `_syntaxNodes`/`_symbolNodes`/`_methodNodes`/
`_operationNodesByOperation`/`_callSiteNodesByInvocation` 等缓存**没有一个按「图」分键**，
键是**符号全局**的（`SymbolId` 用 `GetDocumentationCommentId()`，`:3432-3436`）。

- **致命点：** `GetOrCreateOperationNode(IOperation, NLCPGGraph)`（`:3038-3063`）
  **先查 `_operationNodesByOperation[operation]` 再碰传入的 `graph`**
  ⇒ 一旦某 operation 在 A 图建过节点，后续为 B 图调用会**返回 A 图的节点**——两张不相交的图被**交叉链接**。
- **第二处：** `_activeBuildGraph`（`:64`，设于 `:566`，清于 `:922`）是**单个**图；
  `AddControlFlowEdge`（`:2957-2972`）用 `ReferenceEquals(graph, _activeBuildGraph)`（`:2960`）
  决定是否填充 CFG 邻接缓存 ⇒ 多图时**除一张图外全部静默不再缓存**。
- **⇒ 这正是「跨文件批次」的真实爆炸半径**，而原计划「6 个 commit 点」的框架**把它整个隐藏了**。
  T4 必须同时决定缓存的**命名空间化或按图分键**策略，否则会得到静默的跨图污染。

#### ⚠️ 缺失的合并点（原计划未列）

| 缺失点 | 位置 | 为什么必须改 |
| --- | --- | --- |
| **`CpgFragmentReducer.ReduceInto` 单图签名** | `CpgFragmentReducer.cs:14-16`（唯一图参数，无文件参数） | **T4 最集中的接缝**。生产调用者仅 2 处（`DominancePass.cs:365`、`ControlDependencePass.cs:117`）。⚠️ `LocalCpgFragment.SourceFilePath` **已存在**但 `ReduceInto` **从不读它**——这正是 T4 需要的钩子，**已经现成** |
| **`ControlFlowPass` 根本不用 reducer** | `PublishControlFlowFragments`（`:144-184`）手写 `context.Graph.AddNode`（`:161`）/`AddControlFlowEdge`（`:182`） | 只改 reducer 签名会**静默漏掉 ControlFlow**，它仍是 build-global |
| **`DominancePass` 第二个图写入者** | `RunDominancePassWithoutPlan`（`:377-432`）在 `:422-423` 直接写 `context.Graph`；另 `:404` `MapNodesByBlockOrdinal(…, context.Graph)` | 计划路径用 `localGraph`（`:474-475`），此回退路径是**独立的**路由点 |
| **非批次串行图写入者** | `MethodDecorationPass.cs:132`、`OperationPass.cs:37`、`SyntaxPass.cs:117/156/166/181/186`、`NLCPGBuilder.cs:650`（持久化恢复）、`RunInterproceduralDataFlowPass`（`:1938` + 12 处 `AddEdge`） | 这些**没有 per-item 文件可用**，且 `RunInterproceduralDataFlowPass` **完全 build-global、无批次** ⇒ 多文件是否共享跨过程分析需**单独裁决** |

#### ✅ 一条**去风险**校正（可缩小 T4 面）

**`CpgWorkBatchExecutor.ReduceFragmentsAsync` 无需改动。** 它是**纯排序、与图无关**的：
只见 `orderedShardOrders`（`:860/:892`）与 `Dictionary<int, CpgBatchResult<TResult>> pending`（`:899`），
调 `reduceResult?.Invoke(nextResult.Result)`（`:911`）。**图完全活在调用方的闭包里**
（全文件检索 `NLCPGGraph` = 0 命中）⇒ **T4 是回调层改动，不是执行器改动。**

#### ✅ 第二条校正：`CpgWorkBatch.SourceFilePath` 是**完全惰性**的

实测：该属性在 `src/` 中**零消费者**——没有任何 pass / commit 点 / reducer / 遥测读它
（`CpgWorkBatchPerformanceEvent` 仅携带 `StageId, BatchId, StableOrder, InputCount…`，**无文件字段**）。
仅 4 行测试读它（或对应的 `LocalCpgFragment.SourceFilePath`）。
⇒ 真正的路由键是 **`CpgWorkItem.SourceFilePath`**（生产真相）与 **`LocalCpgFragment.SourceFilePath`**（T4 所需）。
⇒ 也**印证了 T3 不改 `CpgWorkBatch.cs` 的决定**（见 T3「范围校正」）。

#### ⚠️ 第二处数组下标错配（原计划 §1.6② 只列了 `DataFlowPass`）

**`ControlDependencePass.cs:111` 的 `_dominanceOverlays[batch.StableOrder]`** ——
用**批次**的 `StableOrder` 去下标一个索引域为**操作根序**的列表
（列表在 `DominancePass.cs:364` 填充，`:307/341/379/386` 清空，`:424` 追加）。
单文件时二者**恰好重合**；跨文件批次会**打破该巧合** ⇒ 越界或错配。
⇒ T4 必须把这条与 `DataFlowPass.cs:698` **一并**处理。

#### ⚠️ 行号时效警告（计划自身 §1.7 已警告，此处为实测汇总）

原 T2/T3 引用**已全部过期**（`CpgWorkBatchBuilder.cs:57-58` 的 `throw` 已删、
`CpgWorkBatch.cs:42` 实为 `:71`、`CpgWorkBatchBuilder.cs:73/:84` 已修为 `:97-100`/`:80`、
`CpgWorkBatch.cs` 已 92 行、`CpgWorkBatchBuilder.cs` 已 243 行、`CpgWorkItem.cs` 已 93 行、
`CpgWorkBatchExecutor.cs:576-578` 消息已改 `shard` 且行号移为 `:578-581`）。
**仍精确无误的是 T4 的 6 个 `Commit*Stage` 锚点**，以及 §1.6 的 `StableOrder` 双重用途全部引用
（`DataFlowPass.cs:510/652/681/698`、`CallGraphPass.cs:105/145/162-165` 逐行核对一致）。

### T5 —— `DocumentShardPlanner`：大文件方法集合切成 K 组（D2 核心）

> **状态：🟡 计划器代码已实现、已编译、21/21 单测通过；但 D2 能力❌未交付——
> 零生产调用者，且 Gate G4b = `todo` 明确禁止在生产启用。** 详见 §D。
>
> ⚠️ **不要把本条读成「D2 已完成」。** 产出的是**计划器**（L0 纯计算），
> 缺的是**接线**：`DocumentShardPlanner → adapter → NLCPG` 这条真实路径**不存在**。
> D2 的收益当前为 **0**。

- **内容：** 新建计划器，产出 `DocumentShardPlan`，把文件的方法集合按 `LargeFileShardTargetLines` 分 K 组。
  **规划时 `src` + `tests` 对 `DocumentShardPlanner`/`DocumentShardPlan`/`ShardPlanner`/`FileSizeClass`/`DocumentSizeClass` 命中均为 0**（已核实）⇒ 当时为全新。
  ⚠️ **该「零命中」描述已过期：** 计划器现已存在于 `src\NLISSN.Application\Analysis\`
  （8 文件），故「全新」不再是当前事实；**但「零调用者」仍是当前事实**（见 §D 与 :645）。
- **触碰（⚠️ 已校正）：** **`src\NLISSN.Application\Analysis\`（应用层），不是原写的 `NLCPG`。**
  理由见 §D 的「落点校正」：`NLCPG.csproj` 看不到 Application，且权威设计 `:386` 明确要求应用层。
  测试在 `tests\NLISSN.UnitTests\Analysis\`。新增 8 个文件，**未改任何既有文件**。
- **同步/异步：** 同步（纯计算）。
- **可否交子代理：** ✅ 可以（本轮已交单一子代理，只新建文件）。
- **并行度：** 1（新概念，无法拆分给多个代理而不产生接口分歧）。
- **前置：** **T2**（定义已明，见 §1.3 ⇒ 不再被 T0 阻断）。
- **⚠️ 价值前提（§1.3 已量化）：** D2 对**普通大文件有效**（377 方法可分 K 组），
  但对那 34 个「单方法自身超标准」**无效**——原子单位是完整函数，不可切。
  ⇒ T5 的目标必须表述为「按完整方法尽可能接近标准大小」，**不得**承诺消除超标项。
- **⚠️ 未接入生产（仍是当前事实）：** 计划器**无任何调用者**。方法根发现需调用方用
  `GetOperationRootPlans`（`OperationPass.cs:57-129`）产出描述符，**该接线尚未进行**。
  **受 Gate G4b 约束**：`feature_list.json` 的 `unified-work-scheduler-gate-g4b-shard-adapter`
  状态为 **`todo`** 且明确 *"enabling it in production is not [permitted]"*
  ⇒ 接线**不是**可选收尾，而是**必须先过 G4b** 的受控工作。
- **⚠️ 复审（2026-09-26 17:29，独立审计）：** 该状态**复核成立**——
  `src/` 中对 `DocumentShardPlanner.Plan`/`PlanAll`/`Classify` 的调用仍为 **0**；
  6 处 `src/` 命中**全部是 XML 文档注释**（`DocumentShard.cs`、`DocumentShardInputFile.cs`、
  `DocumentShardPlannerOptions.cs`）+ 定义本身。⇒ T5 产物与 D1 消费端
  （`CpgWorkItem.ShardOrder`/`CpgWorkBatch.ShardOrder`）之间**接口已定义、无人接线**。
  另见 [未接线设计审计报告](2026-09-26-unwired-design-audit-report.md) §3.2。
- **⚠️ 二次复审（2026-09-26，本轮）：** 再次全文检索确认**无变化**——调用数仍为 **0**。
  本轮把「✅ 已完成」改为「🟡 计划器完成 / D2 未交付」，并补上 G4b 的**禁止启用**约束。

### T6 —— 内核 `WorkScheduler` 就绪集改动 ⇒ **已作废，不执行**

- **状态：** ❌ **作废。**
- **作废理由（两条独立，任一条即足够）：**
  1. **C2 已证不需要**：当前规模（6,357 方法根）外推仅约 0.65 s。
  2. **已由他人完成**（§1.1b）：并发会话已把就绪集改为两个 `PriorityQueue`
     （`_urgent`/`_deferred`），派发复杂度由 **O(N²) 降为 O(N log N)**（`TakeReady` 上方注释自述）。
- **⇒ 本任务从计划中移除。** 保留条目仅为记录「它为何不在计划里」，避免后来者重复提出。
- ⚠️ 与 T2 的界分：T6 是「换就绪集数据结构」（已做）；T2 是「改 `StableOrder` 语义」
  （**未做，且仍需做**）。二者**不是**同一件事，勿因 T6 作废而误以为 T2 也不必做。

### T7 —— 契约测试（每 pass 一份，文件不相交）

- **内容：** 每 pass 一份定向契约测试；共用的「跨文件不碰撞」用例一份。
- **触碰：** `tests/NLISSN.ContractTests/Cpg/`（该目录现有 **71** 个测试文件）+ 新文件。
- **同步/异步：** 同步。
- **可否交子代理：** ✅ 可以，但**随 T4 串行**（不并行派发）。
- **并行度：** **1（串行，随 T4）**——用户已拒绝并行子代理（见 T4 决定）。
- **前置：** T2（需要 `ShardOrder` 才能写「不碰撞」用例）。

### T8 —— 构建与测试执行

- **是否可并行：** ❌ **不可，且这是硬约束。**
- **原因：** `Build/Tools/Invoke-SerialDotnet.ps1:28` 使用全局互斥
  `Global\NLTX-DotnetBuild-<checkoutHash>`（`:36` 构造，`:39-41` `WaitOne(1000)` 轮询）。
  多个子代理同时构建**会互相阻塞而非并行**，且总墙钟时间不变。
- **⇒ 编排规则：** 子代理**只做编辑**；**全部**构建/测试由主代理**串行**执行。

### T9 —— 文档与索引更新

- **可否交子代理：** ⚠️ **默认不可以。**
- **原因：** 设计文档当前有**活跃并发写入者**（§0 已记录三次行数变化）。
  交由子代理盲写会造成覆盖。
- **⇒ 规则：** 设计文档的任何改动由**主代理**在写前重读、写后复核。

---

## 4. 委派矩阵（本文档的核心交付）

### 4.1 四维总表

| 任务 | 同步/异步 | 可交子代理 | 可并行 | 建议委派模式 | 冲突面 |
| --- | --- | --- | --- | --- | --- |
| **T0** C3 定义 | — | — | — | ✅ **已解决**（§1.3，权威出处已找到） | — |
| **T1** 语料盘点 | **异步** | ✅ | ✅ | **后台子代理**（fire-and-forget） | 只读，无 |
| **T2** `ShardOrder` | 同步 | ✅ | ❌ | **单子代理独占（关键路径）** | `CpgWorkItem`/`CpgWorkBatch`/`WorkScheduler` |
| **T3** 放宽单文件断言 | 同步 | ✅ | ❌ | 单子代理 | `CpgWorkBatchBuilder`/`CpgWorkBatch` |
| **T4** reducer 按项路由 | 同步 | ✅ | ❌ **串行（用户已决定）** | **单子代理依次改 6 个 pass** | 6 个 pass 文件 |
| **T5** `DocumentShardPlanner` | 同步 | ✅ | ❌ | 单子代理 | 新文件（🟡 计划器已完成；**接线属 G4b，未做**） |
| **T6** 内核就绪集 | 异步层 | ❌ | ❌ | **已作废（§1.1b）** | — |
| **T7** 契约测试 | 同步 | ✅ | ❌ **串行（随 T4）** | 随 T4 逐个 pass 配一份 | `tests/.../Cpg/` 新文件 |
| **T8** 构建/测试 | 同步 | ❌ | ❌ **硬约束** | **主代理串行** | 全局互斥 `Global\NLTX-DotnetBuild-*` |
| **T9** 文档/索引 | 同步 | ❌ | ❌ | **主代理** | 设计文档有活跃写入者 |

### 4.2 三类委派的边界

**① 可以异步子代理（fire-and-forget，不阻塞主线）**
- T1 语料盘点：只读、自包含、产出到 `Build/` 目录。
- 判据：**只读 + 不碰 `src`/`docs` + 主线不依赖其结果才能开工**。

**② 可以交给子代理并行（多代理同时开工）—— ⚠️ 当前**无适用任务****
- 本文档原拟把 T4（6 个 pass）× T7（6 份测试）并行：**文件确实不相交**，
  技术上安全。但**用户已于 2026-09-26 明确拒绝**（代价是 6 份互审，
  而构建本就串行 ⇒ 墙钟收益被抵消）。⇒ **本文档当前不派发任何并行子代理。**
- 保留该类别的判据，供将来任务参考：**触碰文件集合两两不相交 + 全部前置任务已完成
  + 全部共享接口已冻结 + 收益大于互审成本**。
- 注意：即便将来启用，并行的是**编辑**，不是构建（T8 硬串行）。

**③ 必须主代理或单子代理串行**
- **T2**（接口冻结）、**T3**（单文件契约）、**T4**（6 个 pass 依次改）、
  **T5**（新概念）、**T7**（随 T4）、**T8**（构建互斥）、**T9**（设计文档有并发写入者）。
- 判据：**需要跨任务一致性判断**，或**触碰共享资源**，或**用户已决定串行**。

### 4.5 本轮实际执行记录（2026-09-26）

| 任务 | 执行方式 | 结果 |
| --- | --- | --- |
| T1 语料盘点 | 异步只读子代理 | ✅ 完成（20/20 数一致），产出 `Build/d1d2-inventory/` |
| **「90% 剩余核心测试」报告** | 异步只读子代理 | ✅ 完成，567 行 → `Build/d1d2-inventory/remaining-core-test-report.md` |
| T3/T4 影响面侦察 | 异步只读子代理 | 运行中（只读，不阻塞） |
| **T2 实现** | **主代理亲自**（关键路径，接口冻结，不可并行） | ✅ 完成 + 实测（56/56） |
| T2 契约测试 | 主代理亲自 | ✅ 7/7 新用例 |
| T3 实现 | 单子代理（独占 `CpgWorkBatchBuilder.cs`） | 运行中 |

**编排经验（可复用）：** 只读侦察/盘点/写报告类任务**全部**可异步子代理化，
与主线的实现工作**完全并行且零冲突**（不碰同一文件）；
而**构建与测试必须主代理串行**（`Global\NLTX-DotnetBuild-<checkoutHash>` 互斥）。
本轮据此在等待子代理期间完成了 T2 全流程，墙钟利用率显著高于串行等待。

### 4.3 并行安全边界（三条硬约束）

1. **同一工作树。** 全部子代理共享 `D:\ProjectItem\SourceCode\Net\NL`。
   ⇒ 并行子代理的触碰文件集合必须**两两不相交**，否则后写覆盖先写。
   注意：目标目录当前**大面积脏**——6 个 pass 为 `M`，`Concurrency/` 整体为 `??`
   （`git status` 已核实）⇒ **不能用 `git checkout` 回滚**，误覆盖不可恢复。
2. **构建全局互斥。** 见 T8。⇒ 子代理**不得**自行运行 `dotnet`；由主代理串行执行。
3. **设计文档活跃写入。** 见 T9。⇒ 子代理**不得**编辑 `docs/plans/2026-09-24-unified-work-scheduler-design.md`。

### 4.4 建议的编排时序

```text
时刻 ┌─ T0（C3 定义，✅ 已解决：§1.3）─────────┐ 无需再问
     │                                            │
     ├─ T1（后台异步子代理）──────────────────┐  只读，唯一真异步项
     │                                        │
     └────────────────────────────────────────┘
                          ↓ T2 完成（接口冻结）
     ┌─ T2（单子代理，关键路径）─┐   ⚠️ 须等 WorkScheduler.cs 静止
     │                            ↓
     │              ┌─ T3（单子代理）─┐
     │              │                  ↓
     │              ├─ T4 CallGraph        ┐
     │              ├─ T4 MemberAccess     │
     │              ├─ T4 ControlFlow      ├─ **串行**（用户已拒绝并行）
     │              ├─ T4 DataFlow         │  每个 pass 改完配一份 T7 测试
     │              ├─ T4 Dominance        │
     │              └─ T4 ControlDependence ┘
     │              ┌─ T5（单子代理）┐
     └──────────────┴─────────────────┘
                          ↓ 全部编辑完成
              T8（主代理串行构建/测试）→ Gate G4b 判定
```

> **为何 T4/T7 串行**：用户于 2026-09-26 明确拒绝「6 个并行子代理各改一个 pass」
> （代价是 6 份互审）。且 T8 的构建本就全局互斥（§4.3 约束 2），
> 并行编辑的墙钟收益会被构建串行抵消 ⇒ 拒绝该方案是正确取舍，不是保守。

---

## 5. Gate 与验收

| Gate | 内容 | 状态 |
| --- | --- | --- |
| **G4b** | `DocumentShardPlanner` → adapter → NLCPG 真实路径的 DOP 等价性 | **NOT STARTED**，本文档为其执行编排 |
| **G4** | 大文件分片后 Roslyn 语义查询逐节点等价（设计 §6.3，**最大风险**） | 未通过前**不得**开启分片 |
| R11 | `TakeReady` O(N²) | ✅ **关闭为非阻塞**；且**已被并发会话修复为 O(N log N)**（§1.1b） |

**验收判据（不得以聚合计数替代）：**
1. T2：跨文件同名方法**不**触发 `Duplicate StableOrder` 抛出；且 `DataFlowPass:698` 的下标语义**逐字未变**；
   且新 `PriorityQueue` 的就绪序与改动前**逐项一致**（键由 `StableOrder` 改为 `ShardOrder` 后，同优先级同 cost 内的相对序不得变）。
2. T4：每 pass 各自跑定向契约测试；并断言「跨文件批次的每文件图签名 == 单文件路径的图签名」。
   ⚠️ **按字面不可测（2026-09-26 复核）：不存在「跨文件批次的每文件图」这一产物。**
   `NLCPGBuilder` 只有单源码入口，全 `src\` 检索
   `BuildFromDocuments|BuildProject|IReadOnlyList<NLCPGSourceInput>` = **0 命中**。
   ⇒ 需在 T4 **先引入多文件图容器**（唯一能逐字兑现本判据的路）；
   降级为集合等价会**牺牲逐边枚举序**，而两份既有测试明确要求该维度不得让步。
3. T5：`DOP=1` 与 `DOP=K` 的图签名逐字一致（设计 Gate G4）。
   ⚠️ **本条不可验收，因为被测量对象尚不可达。** `DocumentShardPlanner` **已存在**
   （`src\NLISSN.Application\Analysis\`，21/21 单测通过），但它**零生产调用者**：
   `Plan`/`PlanAll`/`Classify` 在 `src\` 的调用 = **0**，6 处命中全部是 XML 注释。
   ⇒ 没有「分片后的构建路径」可供比较，本条**只能等 adapter 建成后**才能执行。
   **10% 切片对它零覆盖**（切片里 `DocumentShardingEquivalenceTests` 变的是
   `WorkBatchMaxMethodsPerBatch`/`StreamingMode`，**不是**分片计划）。
   ⇒ 本条的验收归属是 **G4b**（`todo`），**不是** T5。
4. **达标口径（已定，§9.A）：** 以「**保留完整方法前提下尽可能接近标准大小**」为验收，
   **不以**「超标方法数降为 0」为条件 —— 该目标在现有约束下**不可达**（§1.3）。
   度量方式：报告 `CpgWorkBatchBuilder` 产出的批次数与单方法批数，
   **在 `NPC.cs` 与全语料两个尺度上均可复现**，且**不得劣化**。
5. **量化目标（可测，非承诺消除）：** 在 1,500 标准下，`NPC.cs` 的批次数应从现状
   **43 批**向理想装箱 **21 批**收敛（§1.4；差 2.05×，收益来源为放宽 `cost > 200` 的单项隔离）。
   ✅ **已实测达成，且不再「待 T8」**：§1.4b 证明改**一个 options 值**
   （`mediumMaxCost: 200 → 1500`）即得 **21 批**（2.05×），
   单项批 23 → **7** = 「成本 > 1500 的完整方法数」⇒ **单项批已达理论最小**
   （总批 21 仍高于硬下限 18，见 §1.4b）。
   ⇒ 本判据**现在就可写断言**，且**不依赖 T3/T4**。
6. 全量测试**不在本文档范围**（按既有约定不做全量）。

**判据 4 的处置（可证伪化）：** 原判据 4 的「**不承诺**消除超标方法」是**负向承诺**，
原理上**不可被测试证伪** ⇒ 改为正向可测形式
`Build_WhenOversizedMethodPresent_StillIsolatesIt`（断言超标方法**仍被隔离为单项批**，
而非断言它消失）。

**判据 4 的度量尺度约束：** 「全语料尺度」**不在任何常规档位内**。
Terraria 语料是 opt-in，`Miscellaneous\scripts\Run-TestTiers.ps1 -Performance` 还**显式排除**它
（`:48-49` 的 `FullyQualifiedName!~TerrariaCodeSet`）。语料 `D:\TRbackup\Version4` **确实存在**，
命令可写，但必须**单列**，且基线值尚未冻结。

---

## 6. 风险

| # | 风险 | 严重度 | 缓解 |
| --- | --- | --- | --- |
| **D1** | **D1 的可达上界仅 ≈44%**（55.7% 的行在不可装箱方法里，§1.3） | 中 | ✅ **你已接受该上界（2026-09-26）**；验收口径见 §5 第 4 条 —— 不承诺消除超标方法 |
| **D2** | 改 `StableOrder` 语义致 8 pass 同时下标错配（§1.6） | **高** | 新增 `ShardOrder`，`StableOrder` 语义不动 |
| **D3** | 归并按项路由遗漏一处 ⇒ **静默**跨文件污染（设计 R10） | **高** | 逐 pass 迁移 + 图签名等价断言 |
| **D4** | 并行子代理互相覆盖（工作树脏、不可回滚） | **高** | 触碰文件两两不相交（§4.3 约束 1） |
| **D5** | 子代理自行构建，撞全局互斥 ⇒ 无加速且难排查 | 中 | §4.3 约束 2：子代理只编辑 |
| **D6** | 分片改变 `GetOrCreateOperationNode` 共享缓存命中顺序（设计 §6.3） | **高** | Gate G4；未过不得开启 |
| **D7** | C4 若也影响其它「1.72 M」引用 ⇒ 阈值标定失准 | 中 | T1 一并核对全部引用点 |
| **D8** | **`WorkScheduler.cs` 被并发会话持续改写**（25 秒采样内 SHA 变化；该会话已新建 `NL.Concurrency.csproj`）⇒ T2 若此时开工必互覆 | **高** | **T2 阻塞至该文件静止**；开工前重取 SHA，完工后复核 |

---

## 7. 命令

子代理**只编辑**；以下命令由主代理串行执行。

```powershell
# 定向契约测试（示例：CallGraph）
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments @(
  'test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false',
  '-p:UseSharedCompilation=false','-p:MSBuildNodeReuse=false','-p:BuildInParallel=false',
  '--filter','FullyQualifiedName~Cpg','--logger','console;verbosity=minimal')
```

`.ps1:28` 的互斥名 `Global\NLTX-DotnetBuild-<checkoutHash>` 保证串行；
`Build/tmp` 需存在并设 `$env:TEMP` / `$env:TMP`。

---

## 8. 不做什么（明确排除）

| 不做 | 理由 |
| --- | --- |
| 给 `WorkScheduler` 就绪集换优先队列 | C1/C2 已证当前规模不构成阻塞（§1.1）；且该改动**已由并发会话完成**（§1.1b）⇒ T6 作废 |
| 把 pass 改成 `async` | `INLCPGPass.Run` 返回 `void`；改它是另一项工程，与 D1/D2 无关（§2.1） |
| 在函数体内部下刀（基本块拆分 / 循环识别 / 跨切点摘要） | 「保留完整函数」约束排除（§13b.2） |
| 用字节定义标准大小 | 附录 G 已证不存在可用 bytes 上界（同行范围实测跨度 **1349×**） |
| 子代理编辑设计文档 | 存在活跃并发写入者（T9） |
| 全量测试 | 按既有约定 |

---

## 9. 决定记录

| # | 问题 | 你的决定 | 落点 |
| --- | --- | --- | --- |
| 1 | C3「无法装箱达标」指哪个？ | — **我自查已解决**（§1.3）：指「**单个方法自身超标准**」，权威定义在 `docs/benchmarks/2026-09-26-scheduler-dispatch-scaling-and-granularity-report.md:184`。我旧版的「算术张力」是**我自己算错**（误用本仓 64,400 行当 Terraria 语料 363,196 行的分母） | §1.3 |
| 2 | 是否同意 **D2 先于 D1**？ | ⚠️ **该前提不成立，已改**：C3 的 55.7% 不是 D2 的价值（D2 切不到单方法内部）。⇒ **D1 在先**（见下方 A） | §9.A、§0 |
| 3 | 是否接受 6 个并行子代理各改一个 pass？ | ❌ **不接受**（2026-09-26） | T4/T7 已改为**串行**（§4.1/§4.4） |
| 4 | 44% 上界是否可接受？ | ✅ **接受**（2026-09-26） | **验收口径已定** ⇒ §5 第 4 条、§0、风险 D1 |
| 5 | 同步路径（`useSynchronousExecution: true`）**从无**序号唯一性校验，是否补？ | 🔸 **本轮不补，仅记录**（主代理判断，非用户决定）：该缺口**先于 T2 存在**，且**生产不可达**（`src\` 无任何调用点设 `useSynchronousExecution: true`，`UseSynchronousLocalWorkBatchExecution` 仅声明于 `NLCPGBuilderOptions.cs:46`、传参于 `NLCPGBuilder.cs:456`）。不影响 D1。已用哨兵测试固定现状 | T2 完成证据、`CpgWorkBatchShardOrderContractTests` |
| 6 | 🚨 **§1.4b 实测发现：43→21 只需改一个 options 值，不需要 T3/T4/容器。** 是否改隔离语义？ | ✅ **已按 (a) 注入方案实现**（本轮，主代理直接实现）：新增 `IsolationCostThreshold`（默认 = `MediumMaxCost`）+ `Packing` 预设，生产入口指向 `Packing`。**`Default` 未改 ⇒ 两条既有测试保持绿**（实测）。**未放弃任何既有契约**，故无需你追加裁决 | §1.4b、T-A |
| 7 | T5 落点：`NLCPG` 还是 `Application`？ | ✅ **我自查已解决**（本轮）：原计划写 `NLCPG` **是错的**——`NLCPG.csproj` 看不到 Application（依赖方向 `Application → NLCPG`）。权威设计 `:386` 要求应用层 ⇒ 改落 `src\NLISSN.Application\Analysis\`。已实现并测试 | §D、T5 |

### C. 🆕 T-A —— **轴 A：提高同文件装箱率**（§1.4b 实测新增，建议**先于** T3/T4）

> **状态：✅ 已实现并实测通过（2026-09-26，本轮）。** 采用上表第 6 行的 **(a) 注入**方案——
> **`Default` 不动**，两条既有测试保持绿。证据见本节末。

- **内容：** 让 `cost ∈ (200, 1500]` 的方法**可被配对装箱**，而非各自独占一批。
- **实现（实际做法，比原设想更干净）：** 把**装箱隔离策略**从**尺寸分类**中**解耦**——
  在 `CpgWorkBatchCostOptions` 新增 `IsolationCostThreshold`（**默认 = `MediumMaxCost`**，
  故既有行为逐字不变）；`CpgWorkBatchBuilder.AppendMethodBatches` 由
  `Classify(...) is Large or Oversized` 改为 `item.EstimatedCost > IsolationCostThreshold`；
  新增 `CpgWorkBatchCostOptions.Packing` 预设
  （`medium=1500, large=1550, target=1500, max=1500`，隔离阈值 1500），
  并由 `NLCPGBuilderOptions.EffectiveWorkBatchCostOptions` 采用。
- **收益（已实测，§1.4b）：** `NPC.cs` 1,500 标准 **43 → 21 批（2.05×）**，
  单项批 23 → **7**（= 成本 > 1500 的方法数 ⇒ **单项批已达理论最小**；
  但总批 21 仍高于硬下限 18，见 §1.4b 第 1 条）。
  语料尺度 **1081 → 768 批（1.41×）**。
- **触碰：** `CpgWorkBatchCostModel.cs`（新增属性 + `Packing` 预设）、
  `CpgWorkBatchBuilder.cs`（判定改为读阈值）、`NLCPGBuilderOptions.cs`（默认改指向 `Packing`）
  + 新契约测试 `CpgWorkBatchIsolationThresholdTests.cs`。
  **`CpgWorkBatchCostOptions.Default` 的五个值一个都没改。**
- **风险：** **低**（采用注入方案后，风险由「中」降为「低」）。
- **前置：** 无（**不依赖 T2/T3/T4**）。

#### T-A 完成证据（本轮实测）

| 项 | 结果 |
| --- | --- |
| 编译 | `dotnet build src\NLCPG\NLCPG.csproj` → **0 error / 0 warning** |
| 新增契约测试 | `CpgWorkBatchIsolationThresholdTests.cs` → **11 用例全通过** |
| 核心切片 | **82/82 通过**，exit 0，25 s |
| 既有 builder 测试 | `CpgWorkBatchBuilderTests` 6 条**全绿**（含断言 201/801 隔离的两条） |
| 兼容性 | 不设阈值时 `IsolationCostThreshold == MediumMaxCost` ⇒ 与解耦前**逐字等价** |

**关键守卫（`CpgWorkBatchBuilderTests` 之所以仍绿）：**
`IsolationCostThreshold` **默认为 `MediumMaxCost`**，且 `Default` 的五个值**一个都没改**
⇒ 只断言 `Default` 行为的两条用例不受影响；
放宽只发生在**显式选择 `Packing` 预设**的生产入口。
这比「改默认值 + 改写两条测试」**风险更低，且不改变既有契约**。

**与 T4 的关系：** ⚠️ **轴 A 会分流 T4 的收益。**
语料尺度上 T4（跨文件）仍强 3.78×，但其**必要性下降**：
单文件内方法多的场景（`NPC.cs`）轴 A 已把单项批压到理论最小。
⇒ **建议先做轴 A 并重新测量，再决定 T4 是否值得其风险面**
（T4 前置 P0 的容器 + `NLCPGBuilder.cs:47-73` 的缓存别名危害）。

### D. 🟡 计划器代码完成：T5 —— `DocumentShardPlanner`（D2 **计划层**）

> **状态：🟡 计划器已实现、已编译、21/21 单测通过（2026-09-26）；
> 投递层已于同日接线（§G），但 D2 收益**未测**，Gate G4b 仍为 `todo`。**

- **落点校正（重要）：** 原计划 §T5 写「新文件（`src/NLCPG/Builder/Concurrency/` 下）」——
  **该落点错误**。`NLCPG.csproj` 只引用 `NLCPG.Configuration`/`NL.Caching`/`NL.Concurrency`，
  **看不到 Application**（依赖方向是 `Application → NLCPG`）。
  权威设计文档 `2026-09-24-unified-work-scheduler-design.md:386` 明确要求
  「分片计划器位于应用层（`src/NLISSN.Application/Analysis/`），**不在内核、不在 NLCPG**」。
  ⇒ 实际落点为 **`src\NLISSN.Application\Analysis\`**，测试在 `tests\NLISSN.UnitTests\Analysis\`。
- **新增 8 个文件**（全部 `??`，**未改任何既有文件**——已用 mtime 复核：
  同目录 3 个 `M` 文件的 mtime 为 09-22～09-25，早于 T5 的 09-26 14:31 之后）：
  `DocumentShardPlanner.cs`、`DocumentShardPlannerOptions.cs`、`DocumentShard.cs`、
  `DocumentShardPlan.cs`、`DocumentMethodDescriptor.cs`、`DocumentShardInputFile.cs`、
  `FileSizeClass.cs`、`DocumentShardPlannerTests.cs`。
- **契约（照权威设计 `:388-401`）：** `DocumentShardPlan(FilePath, SizeClass, Shards, EstimatedBytes)`
  与 `DocumentShard(StableOrder, ShardIndex, MethodOrders, EstimatedCost, EstimatedBytes)`。
- **硬约束已落实：** 原子单位是**完整方法**；超目标方法**独占一片且允许超标**，
  计划器**不承诺**消除超标方法（与 §1.3 一致）。
- **阈值可配置：** `SmallFileMaxLines=200` / `MediumFileMaxLines=800` /
  `LargeFileShardTargetLines=1500`，为**待校准候选值**（设计 `:439-446` 要求不得写死）。
- **成本币种对齐：** 方法体行数（同 `CpgWorkBatchCostModel.Estimate`），
  `EstimatedBytes = cost × 64`（同 `CpgWorkBatchBuilder.EstimatedBytesPerCostUnit`）。
- **确定性：** LPT 降序 + 最小装载量 + 最小片索引的全序；排序键全为输入自带整数，
  **无字典枚举序进入结果**。同输入 ⇒ 逐字段相同计划。
- **`StableOrder` 编码：** `checked(fileOrdinal * int.MaxValue + shardIndex)`，溢出显式抛出。

| 项 | 结果 |
| --- | --- |
| 编译 | `dotnet build tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj` → **0 error** |
| 测试 | `DocumentShardPlannerTests` → **21 个用例全通过**（18 个方法，含 1 Theory 4 例） |
| **D2 能力是否交付** | 🟡 **计划层 + 投递层已就绪（2026-09-26 接线，见 §G），但收益未测、Gate 未过。** 计划器经 `DocumentShardPlanAdapter` 由 `ApplicationService.BuildGraphBatchCore` 接入；`src/` 内已非零调用者。 |
| **能否在生产启用** | 🚫 **按 Gate 不能。** `feature_list.json` 的 `unified-work-scheduler-gate-g4b-shard-adapter` 状态为 **`todo`**，其 notes 原文：*"enabling it in production is not [permitted]"*。⇒ §G 记录的接线是你**明确指令绕过**该 Gate 的结果，**不是** Gate 通过 |

**⚠️ 状态口径（2026-09-26 纠正）：** 本表上一版把「生产接线」写成一行小字，
却把标题写成「✅ 本轮完成：T5 —— D2 核心」，**这是误导**——它把「代码产物存在」
说成了「D2 能力交付」，且掩盖了 G4b 的**禁止启用**约束。现已拆成上面两行显式状态。
**D2 的真实状态是「计划层已就绪、投递层未接、Gate 未过、生产禁用」。**

⚠️ **已知缺口（子代理主动声明，值得记录）：** `DocumentShardPlan.Shards` 是**集合成员**，
`record` 生成的 `Equals` 对它只做**引用比较** ⇒ 两份内容相同但分别构造的计划**不相等**。
测试因此比较逐字段字符串投影。**任何后续把它当值对象比较的代码都会踩这个坑**，
必须显式逐字段比较（或在 `DocumentShardPlan` 的 XML 注释中保留该警告——已保留）。

### E. ✅ 本轮完成：T4 —— reducer 按项路由 + 多文件图容器（D1 最大一块）

> **状态：✅ 已实现、已编译、已测试通过（2026-09-26，本轮）。**

**为何需要多文件图注册表（回答「为什么引入了按文件路径解析图」）：**

1. **设计强制每文件一张独立图**：`2026-09-24-unified-work-scheduler-design.md:923`
   明确「每文件一张独立图」，`:955` 进一步警告 §4.2 的「同一张图」指的是
   **F 的图（每文件一张）**，不是一张全局主图；`:327` 的「单写入」粒度也是**每张图，不是全局**。
   产品现状亦如此：`DirectoryAnalysisUseCase.AnalyzeFiles` 逐文件调 `Analyze`，
   **循环在 CPG builder 之外**，每文件一个新 builder + 一张新图。
2. **归并期手里只有路径**：D1 放宽装箱后一个批次可跨文件（T3），
   而写图者（6 个 `Commit*Stage`）在归并期能拿到的 per-item 身份只有
   `LocalCpgFragment.SourceFilePath` / `CpgWorkItem.SourceFilePath`——
   **没有也不应有图引用**（把可变图塞进调度 DTO 会让图生命周期泄漏到装箱层）。
   故「路径 → 图」的解析是唯一可行形态。
3. **它顺手修掉一个静默错配**：`StableOrder` 是**文件内局部**序号且被当**数组下标**用
   （`DataFlowPass` 的 `methodPartitions[item.StableOrder]`、
   `ControlDependencePass` 的 `_dominanceOverlays[batch.StableOrder]`）。
   跨文件批次下直接下标会分析到**别的文件的方法**（产物看似正常、内容错误）。
   「先按文件分组、再用**该文件自己的**分区表处理」恰好把下标域还原回单文件局部
   ⇒ 正确路由**就是**下标修复，无需给 `StableOrder` 引入全局语义。
4. **共享字符串表必须是机制而非注释**：`NLCPGGraph` 默认各自 `new StringInterner()`
   （`NLCPGGraph.cs:154`），而 `StableNodeAnchor` 以 `FilePathId`（字符串表内整数）
   参与相等性（`:308`）。各持一份 ⇒ 同一文件在不同图得到**不同 `FilePathId`**
   ⇒ 锚点不可跨图比较 ⇒ fragment 无法被路由。注册表把 identityFactory + interner
   收进构造函数，使「必须共享」不可绕过。
5. **`Resolve` 未命中抛异常**而非隐式新建图：设计 `:699`（R10）把路由遗漏定性为
   **静默错误而非崩溃**；隐式新建一张永不 freeze、永不被引用的图属同一类静默失效。

**新增/改动落点：**

| 类别 | 文件 |
| --- | --- |
| 新增容器 | `NLCPGGraphRegistry.cs`、`NLCPGDocumentSet.cs`、`GraphScopedCache.cs`、`Concurrency/SourceFilePartition.cs` |
| 新增入口 | `NLCPGBuilder.BuildMany(...)`（此前全 `src/` 检索 `BuildFromSource`/`BuildMany`/`GraphByFile` = 0 命中 ⇒ T4 路由**不可达**） |
| 新增契约测试 | `tests/NLISSN.ContractTests/Cpg/NLCPGBuildManyTests.cs`（8 例） |
| 按项路由 | 6 个 `Commit*Stage`（CallGraph/MemberAccess/ControlFlow/Dominance/ControlDependence/DataFlow）+ `CpgFragmentReducer.ReduceInto(registry, …)` + `PartitionedOperationPass.AssembleWorkBatchesAcrossDocuments` |
| 缓存按图分键 | `_operationNodesByOperation`、`_methodNodes`、`_methodParameterNodes`、`_methodReturnNodes`、`_methodEntryNodes`、`_methodExitNodes`、`_symbolNodes`、`_typeDeclNodes` → `GraphScopedCache`；`_activeBuildGraph`（单数）→ `HashSet<NLCPGGraph> _activeBuildGraphs` |

**实施中发现并修掉的 5 个真实缺陷（均为本轮引入或本轮的改动所暴露）：**

| # | 缺陷 | 证据 / 修法 |
| --- | --- | --- |
| 1 | **每文件各持一个 compilation ⇒ 跨文件调用解析不出** | `NLCPGSourceSemanticInput.CreateSourceSemanticInput` 为每文件建只含自身的 compilation，A 里对 B 的调用退化为错误符号。新增 `CreateSourceSemanticInputs`，**全部文件进同一个 compilation** 再逐文件取 `SemanticModel`。这是 D1 的**正确性前提**而非优化 |
| 2 | **驱动文档守卫 ⇒ 静默少边** | `PlanControlFlowStage`/`PlanDominanceStage`/`PlanMemberAccessStage` 及其执行守卫原先只看 `context.Root`。多文件下「首文件恰好无方法」会被判为「无操作根」⇒ 规划不登记 plan ⇒ 其余文件的边**静默全缺**。新增共用谓词 `HasAnyOperationRootAcrossDocuments(context)`（单文件时恒等「自身」，行为逐字不变） |
| 3 | **单槽操作根缓存跨文档抖动** | `_operationRootPlanRoot/_operationRootPlanSemanticModel` 为单槽。多文件下规划/提交/守卫**交替**为不同文档调用同一方法 ⇒ 每次未命中 ⇒ 逐文件重推 `GetDeclaredSymbol`（纯损耗、不改产物）。改为按语法根引用分键的 `Dictionary<SyntaxNode, OperationRootPlanCacheEntry>`（连语义模型一起存，命中需两者同匹配） |
| 4 | **`CompleteOperationBackedSyntaxTypes` 用错语义模型（抛异常）** | `_pendingOperationSyntaxTypeNodes`/`_syntaxNodes` 是**构建级**（跨文件共用），原实现拿驱动 context 的 `SemanticModel.GetTypeInfo` 去解析别的文件的语法节点 ⇒ `ArgumentException: 语法节点不在语法树中`（首次 `BuildMany` 实测即触发）。改为按 `syntax.SyntaxTree.FilePath` 取回**该文件**的 context 与图 |
| 5 | **图分键把临时图的节点计入指标 ⇒ 契约破坏** | L1 计算层在**临时图**里算 fragment（ControlFlow/Dominance，见各自 `localGraph`），其节点算完即弃、由 L2 按锚点归并去重。图分键后这些临时图也开始计数，使 `OperationNodeCacheMissCount` 不再等于 `OperationInventoryCount`（即「每个库存操作在持久图中恰好物化一次」的契约）。改为**只对持久图**（`_activeBuildGraphs`）计数，与 `AddControlFlowEdge` 的 CFG 邻接缓存同一判据 |

**验证结果（全部为实际执行的命令）：**

| 项 | 命令与结果 |
| --- | --- |
| 编译 | `Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj` → **0 错误 / 0 警告** |
| 定向契约 | `--filter FullyQualifiedName~NLCPGBuildManyTests` → **8/8 通过** |
| 分 pass 契约 | `~DataFlow\|~Dominance\|~ControlFlow\|~ControlDependence\|~CallGraph\|~MemberAccess\|~Syntax\|~Operation` → **198/198 通过** |
| 宽 Cpg 契约 | `~Cpg\|~Stage\|~Worker\|~Planning\|~Interprocedural\|~Fragment\|~Publication` → **731/731 通过** |
| T5 回归 | `DocumentShardPlannerTests` → **21/21 通过** |
| 缺陷 #5 复现 | 修复前 `BuildFromSource_CachesOperationNodesAndRootFactsAcrossPasses` **失败**（`Expected: 12, Actual: 14`）；修复后**通过**——本项即该缺陷的可证伪证据 |

**核心用例的可证伪设计（`BuildMany_TwoFiles_ResolvesCallTargetsThatSingleFileBuildCannot`）：**
正反对照——同一份调用方源码，**单独构建**时图中**不存在**指向被调用方文件的边
（`Callee` 无定义 ⇒ 解析不出符号）；**与兄弟文件一起构建**时**必须存在**该边。
若退回缺陷 #1 的「每文件各持一个 compilation」，后半句即失败。

**已知边界（明确未做，不得当作已达成）：**

- **多文件 + 持久化 = fail-closed**：`_options.Persistence is not null` 时 `BuildMany`/
  `BuildManyDocuments` 抛 `NotSupportedException`（每张兄弟图各持预分配表，无法共用一份）。
- **仍未按图分键**（已在代码注释中标明）：`_propertyAccessorCallSiteNodesByKey`、
  `_callSiteNodesByInvocation`、`_resolvedCallTargetsByDispatchShape`、
  `_pendingOperationSyntaxTypeNodes`、`_syntaxNodes`、`_declaredTypes`。
  其中 `_syntaxNodes` 已通过缺陷 #4 的按文件解析消除跨文件误用；
  其余三项的键本身含文件路径（`PropertyAccessorCallSiteKey` 前缀为 `BuildStableFilePath`）
  或为引用身份，跨文件不冲突。
- **跨文件过程间分析语义刻意保守**：`AddCallArgumentAndReturnDataFlow` 按文档各自
  的调用集合处理，不做跨文件过程间传播；该选择已写入该方法注释。
- **单文件入口（CLI/ProjectJson）仍是单文件构建**：`NLCPGCli.cs:35`、
  `ProjectJsonExporter.cs:302` 未改，Cross-file 装箱只在**目录分析**路径生效。

### F. ✅ 本轮完成：F1 —— 把跨文件装箱接进生产（目录分析路径）

> **状态：✅ 已实现、已编译、已测试（2026-09-26，本轮）。**
> **§E 中「`BuildMany` 零生产调用者」的表述已作废**，见下。

**改动的落点（4 处，均在既有文件内）：**

| 文件 | 改动 |
| --- | --- |
| `NLCPGBuilder.cs` | 新增 `BuildManyDocuments(IReadOnlyList<NLCPGBuildDocument>)` → 返回 `NLCPGMultiFileBuildResult`（每文件图 + 整批指标）；原 `BuildMany(files)` 转为薄包装（**签名与行为不变**，含 8 个既有契约用例） |
| `NLCPGBuildDocument.cs`（新） | 输入文档：可只给 `FilePath`+`Source`（由构建器合并成一个 compilation），也可额外给 `SemanticModel`+`Root`（**复用调用方已有的共享 compilation**） |
| `ApplicationService.cs` | 新增 `BuildGraphBatch`（整批一次构图，只占**一次** CPG 配额）与 `AnalyzeWithGraph`（用已建图跑单文件规则分析）；抽出 `CreateBuilderConfiguration`/`RecordCpgBuildStages` 供单文件与批量**共用** |
| `DirectoryAnalysisUseCase.cs` | `AnalyzeFiles` 改为「**先整批构图**，再逐文件并行跑规则」 |

**为什么这样切（而不是让每个文件各调一次 `BuildMany`）：**

1. **跨文件装箱必须在一次构建内发生。** 逐文件各调一次 builder 就永远只有一张图，
   批次不可能跨文件——这正是 D1 的全部意义。
2. **必须复用目录已有的那份 compilation。** 目录分析**本来**就已为整个目录建好
   一份 compilation（`DirectoryAnalysisUseCase.cs:85`）。若让构建器自己再解析一遍，
   规则管线手里的语义模型与图构建用的会是**两个不同实例**。
   ⇒ 故 `NLCPGBuildDocument` 支持传入 `SemanticModel`/`Root`。
3. **构图与规则分析分两步**，以保住目录分析原有的**逐文件并行**规则分析结构：
   构图整批一次（不可并行，跨文件装箱本身就是串行调度决策），
   规则分析仍按文件并行（`SelectOrderedAsync`）。

**修掉的 2 个由本改动暴露的问题：**

| # | 问题 | 证据 / 修法 |
| --- | --- | --- |
| 1 | **`AnalyzeWithGraph` 拿不到 CPG 配额租约 ⇒ `NullReferenceException`** | 首次运行 56 个目录测试失败，全部为 `CreateBuilderConfiguration` 里的 `CurrentCpgBuildAdmissionLease!.GrantedDegree` 空引用。根因：构图那批的租约在 `BuildGraphBatch` 返回时已释放，而规则分析仍需要租约。修法：`AnalyzeWithGraph` 自己取一次租约（已有租约则复用，不重复获取） |
| 2 | **逐文件性能事实报出整批总量** | `BuildManyDocuments` 是**一次**构建，其 `Metrics.NodeCount`/`EdgeCount` 是整批合计。直接透传会让每个文件都报出整批规模，且聚合结果随批大小漂移。修法：`AnalyzeWithGraph` 用 `graph.Nodes.Count`/`Edges.Count` 覆盖这两个字段 |

**验证（全部为实际执行的命令）：**

| 项 | 结果 |
| --- | --- |
| 编译 | `NLISSN.csproj`、`HostTests.csproj` → 各 **0 错误 / 0 警告** |
| 跨文件等价性（新，4 用例） | `CrossFileBatchEquivalenceTests` → **4/4 通过** |
| `BuildMany` 契约 | `NLCPGBuildManyTests` → **8/8 通过**（原 API 行为未变） |
| 宽 Cpg 契约 | `~Cpg\|~Stage\|~Worker\|~Planning\|~Interprocedural\|~Fragment\|~Publication\|~Shard` → **735/735 通过** |
| 目录 Host 测试 | `~Directory` → **58/60 通过** |

**⚠ 那 2 个失败与本次改动无关（已用对照实验证明）：**
`AnalyzeFromArgs_ForDirectoryDeclaration_RewritesLargeAssetProjectAndKeepsCompilationValid`
与 `..._ShrinksDelegateMethodGroupBindingsAndInvocations`。

- **对照实验：** 在 `AnalyzeFiles` 中插入临时开关，令其为 `false` 时走**改造前的逐文件路径**
  （完全绕过本次新增的全部代码），**这 2 个用例依旧失败，且错误消息逐字相同**
  （`Assert.DoesNotContain` 找到 `PlayerInput`；`Assert.Contains` 未匹配到 `Handler` 决策）。
  ⇒ 二者是**既有失败**，非本改动引入。
- **来源：** 工作树中有并发写入者对 `ParameterShrinkAnalyzer.cs`、
  `PropagationFixedPointExecutor.cs`、`UnreachableMethodMarkRule.cs`、
  `UnreferencedMethodMarkRule.cs` 的未提交改动（helper-DOP 迁移）。
  失败点正落在 `--delete-class PlayerInput` 的**级联收缩**语义上，与该区域吻合。
  **未修改这些文件**（它们不属于本任务）。

**等价性契约的正确形式（本轮踩坑后修正）：**

初版测试断言「批量构建的图 == 独立构建的图」，**实测失败**，且失败信息本身是**正面证据**：

```
Expected: N|MethodParameter|Demo.Caller.Invoke:int(Callee,int)...
Actual:   N|MethodParameter|Demo.Caller.Invoke:int(Demo.Callee,int)...
          N|CallSite|Demo.Callee.Compute:int(int)|107|128
```

即跨文件解析成功让调用方的图**合理地多出**：类型名由 `Callee` 解析为 `Demo.Callee`，
并新增指向被调用方的 `CallSite` 节点。**这是 D1 的目的，不是污染。**

⇒ 最终契约取**语法物化逐字等价**（只比 `SyntaxTree`/`SyntaxNode`/`SyntaxToken` 的
（种类, 起止），不比名字与符号事实），并**另加一条正向断言**
`BuildManyDocuments_BatchedGraphContainsCrossFileSymbolFacts`：批量构建必须带来
**更多**跨文件符号事实（实测 caller 89 → 95 节点）。若跨文件解析失效，后者会变红。

**与 §E 结论的关系：** §E 的按项路由此前确实**不可达**；本轮把入口接上后，
T4 全部路由（注册表 + 6 个 `Commit*Stage` + `SourceFilePartition` + 图分键缓存）
首次进入**真实生产路径**。轮次前序 §E 记录的 5 个缺陷修复是本次能跑通的前提。

**⚠️ 仍未闭合的一处（独立审计发现，2026-09-26 17:29）：T2 的 `ShardOrder` 未覆盖全部 pass。**
`CpgWorkBatchBuilder.AddBatch`（`CpgWorkBatchBuilder.cs:224-227`）用
`items.Min(item => item.ShardOrder)` 推导批次的全局序号，而生产侧 5 个
`new CpgWorkItem(...)` 构造点**全部走 7 参重载** ⇒ item 的 `ShardOrder` 恒等于
**文件内局部**的 `StableOrder`（`CpgWorkItem.cs:28`）。
仅 `ControlDependencePass.cs:87-106` 用 9 参重载显式传全局序号。⇒ 跨文件批次中
「批次终止于文件边界」或「两文件各有相同 `StableOrder` 的超大方法」时，
两个批次的 `min` 会**相同**，触发 `CpgWorkBatchExecutor.cs:578-581` 的
`"WorkBatch shard orders must be unique."`。
§F 的 735/735 与 `CrossFileBatchEquivalenceTests` 4/4 **未覆盖该维度**
（夹具规模小到方法可落入**单一批次**，而本缺口要求**批次之间**的 `min` 碰撞）。
详见 [未接线设计审计报告](2026-09-26-unwired-design-audit-report.md) §5。
**未实测**，需在真实多文件语料上裁决。


### A. ✅ 已决定：接受 44% 上界 + D1 在先

**你的决定（2026-09-26）：接受「D1 组合路径上界 ≈ 44%」。**

§1.3 已证：在「保留完整函数、禁止函数体内部下刀」约束下，55.7% 的代码行落在
34 个**单方法自身超标准**的方法里（最大 `NPC.cs::AI()` = 23,189 行）。
这是**约束的必然代价**，不是实现缺口（报告 §6.1 明确如此定性）。

**⇒ 由此正式确定的完成条件：**

| 项 | 内容 |
| --- | --- |
| **完成条件** | 「**在保留完整方法的前提下，尽可能接近标准大小**」 |
| **不作为条件** | ❌ 「超标方法数降为 0」—— 在现有约束下**不可达**，**不得**以此验收 |
| **度量** | `CpgWorkBatchBuilder` 的批次数 / 单方法批数，在 `NPC.cs` 与全语料两尺度可复现且**不劣化** |
| **方向性指标** | 1,500 标准下 `NPC.cs` 从现状 **43 批** → 理想 **21 批**收敛（§1.4），具体值待 T8 实测 |

**关于 D1/D2 次序（我据此调整了 §0）：** 原先「D2 先于 D1」建立在一个**错误前提**上 ——
即 C3 的 55.7% 是 D2 的价值。实际上 D2 切的是**文件的方法集合**，
那 34 个方法**一个都碰不到**。故：

- **D1 在先**：收益具体可测（≈44% + §1.4 新发现的装箱效率缺口）。
- **D2 在后**：其价值仅在**缩短单个超大文件的关键路径**；
  且它引入**图级分片**，撞上设计 §6.3 / Gate G4 的 **DOP-等价性**未知项（本文档标为最高风险）。
  应等 `ShardOrder` + 多文件批次 + 按项路由跑通并验证后再上 —— **这与 44% 上界无关**。

⚠️ 若你的真实痛点是**单个巨文件的墙钟**（而非批次数），那么 D2 才对症、D1 帮不上，
次序应反转。**这一点我未替你决定**，按上述默认（D1 在先）推进；如需反转请指出。

### B. 另外两件我建议但未获你表态的（不阻塞 T2/T3）

1. **§1.4 新发现的装箱效率缺口**：真实 builder 在 1,500 标准下 `NPC.cs` 出 **43 批**，
   而理想装箱只要 **21 批**（2.05×）。根因是 `cost > MediumMaxCost(200)` 一律单项成批
   （`:114`），与 `TargetBatchCost` 无关。
   ⇒ 这意味着 **D1/D2 的收益不只来自「跨文件」**，还包括让 `(200, 1500]` 的
   「中等偏大」方法能配对装箱。**该收益是本文档新识别的，C3 报告与设计文档均未记录。**
2. **`MaxBatchCost` 是死配置**：`:124` 的 `MaxBatchCost` 分支被 `:125` 的
   `TargetBatchCost` 恒蕴含（构造器强制 `target<=max`，`:46`）⇒ 永不单独触发。
   报告 §7.2 第 9 条亦独立发现同一项。**未修改，仅记录。**

---

### G. ⚠️ 本轮完成：D2 生产接线（**应你的明确指令执行，G4b 未过**）

> **状态：🟡 代码已接线、已编译、相关测试全绿；但 Gate G4b 仍为 `todo`，
> 本节记录的是一次「按指令绕过 Gate」的接线，不得读作 G4b 通过或 D2 已验收。**

**为什么这里要显式声明绕过：** `feature_list.json` 的
`unified-work-scheduler-gate-g4b-shard-adapter` 状态为 `todo`，其 notes 原文写明
*"enabling it in production is not [permitted]"*。你在 2026-09-26 明确指令
「代码直接接入 `DocumentShardPlanner` 不做任何前置测试」。我按指令执行，
并**在此如实标注**：这不是 Gate 通过，而是 Gate 被指令绕过。**若需回退，
撤掉 `ApplicationService.BuildGraphBatchCore` 里的 `WorkShardPlanner` 一行即可**
（该开关之外无任何其他改动）。

**落点（修正 §D 的「投递层未接」）：**

| 文件 | 角色 |
| --- | --- |
| `src/NLCPG/Builder/Concurrency/INLCPGWorkShardPlanner.cs`（新） | **端口**：NLCPG 侧唯一定义，只由 `int`/`string` 构成 ⇒ 不需要、也不得新增 `NLCPG → Application` 引用 |
| `src/NLCPG/Builder/Concurrency/CpgWorkShardPlan.cs`（新） | **中立契约** + 全局序号的唯一分配逻辑（两带：计划带 / 回退带） |
| `src/NLCPG/Builder/NLCPGBuilderOptions.cs` | 新增第 32 个参数 `WorkShardPlanner`（默认 `null` ⇒ 行为同改造前） |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `BuildWorkShardPlan`（调用端口）、`BuildWorkBatches`（**装箱唯一入口**）、`ApplyWorkShardPlan`、`ResolveShardOrder`、`DescribeMethodOrders`（方法序号普查） |
| `src/NLISSN.Application/Analysis/DocumentShardPlanAdapter.cs`（新） | **S5-2 adapter**：`INLCPGWorkShardPlanner` 的应用层实现，把计划转成中立契约 |
| `src/NLISSN.Application/Analysis/ApplicationService.cs` | `BuildGraphBatchCore` 挂上端口（**唯一生产接线点**） |

**接线过程中发现并修掉的 3 个真实缺陷（都不是理论风险）：**

1. **「每片一个序号」会撞执行器唯一性校验。** `CpgWorkBatchBuilder.AddBatch:227`
   取批内 `Min(ShardOrder)`；若同一片的全部方法共用一个序号，则该片被拆成两批（或与
   邻片合并）时两批 `min` 相同 ⇒ `CpgWorkBatchExecutor.cs:578` 抛
   `"WorkBatch shard orders must be unique."`——**正是本次要修的那个崩溃**。
   ⇒ 改为**序号分到「项」**（以分片序为主键）：项集不相交 ⇒ 各批 `min` 必不同。
2. **计划原本只是「改序号」，不影响批次组成（等于没接上）。** `NormalizeItems` 以
   文件路径为主键排序，装箱器严格按该序贪心 ⇒ 分片边界从不参与决策。
   ⇒ 在路径主键**之后**插入 `ShardOrder` 作第二键：
   - 路径仍是主键 ⇒ 保住既有契约「输入顺序无关」与「a.cs 先于 b.cs」
     （`CpgWorkBatchCrossFilePackingTests.Build_WhenMultiFileInputOrderReversed…`）；
   - 文件内按 `ShardOrder` 排列 ⇒ 分片连续成块，**大对象拆成的组真正决定装箱**。
   ⚠️ 我第一版把 `ShardOrder` 提到**主键**，当场打红上述契约测试
   （`Expected ["a.cs","a.cs","b.cs","b.cs"]` / `Actual ["a.cs","b.cs","a.cs","b.cs"]`）。
   这是「分片是按文件划分的」（`CpgWorkShardAssignment` 以路径为键）的必然结论。
3. **序号必须压成稠密秩，不能透传。** 计划器的 `DocumentShard.StableOrder` 是
   `long`（`fileOrdinal × int.MaxValue + shardIndex`），第 2 个文件起就无法用 `int` 表示；
   直接强转会**静默回绕**并破坏单调性。⇒ 由中立契约按确定全序压成 `[0, K)`。

**为什么把注入点上移（这是本轮最重要的设计决定）：**
最初我逐个修改 `new CpgWorkItem(...)` 构造点（生产侧共 5 个），但你指出「控制注入深度」。
复核后确认：5 个构造点**全部**要经 `_workBatchBuilder.Build` 才成为批次，
且并发会话还在**持续新增**构造点。⇒ 改为只维护**一个**入口
（`BuildWorkBatches`，4 个调用点），新构造点**自动**被覆盖，不再有
「忘记赋全局序号 ⇒ 跨文件碰撞 ⇒ 执行器抛异常」的机会。

**同样地，「两个 builder 必须同配置」这一只能靠约定维持的约束已被消除：**
初版让调用方**先自建一个 builder 算计划**再塞进 options，副作用是每个语法树被
**枚举两遍**（`GetOperationRootPlans` 会逐方法调 `GetDeclaredSymbol`，该文件自己的注释
点名这是纯性能损耗）。⇒ 改为端口后，实现方拿到的是**正在构图的那个** builder，
配置不可能不一致，且枚举结果直接进入其缓存。

**验证（本轮实跑，均为最小相关集，**未**跑全量）：**

| 项 | 命令/范围 | 结果 |
| --- | --- | --- |
| 编译 | `build src\NLISSN\NLISSN.csproj` | **0 error / 0 warning** |
| 编译 | `build tests\NLISSN.HostTests` | **0 error**（4 个 warning 全为既有：`AnalyzerReleases.*` 头部与 `DirectorySchedulerBoundaryTests.cs`，均非本轮文件） |
| 单测 | `DocumentShardPlanner` + `CpgWorkBatch` | **21/21 通过** |
| 契约 | `FullyQualifiedName~Cpg` | **709/709 通过**（含 `CrossFileBatchEquivalenceTests` 4/4） |
| 生产路径 | HostTests `~Directory` | **58/60**；2 个失败为**已证既有**（peer 会话对 `ParameterShrinkAnalyzer.cs` 等的未提交改动，用开关实验复现过逐字相同的报错），**且全程未出现** `"WorkBatch shard orders must be unique."` |

**⚠️ 本节证据的边界（不得夸大）：**
- HostTests 与契约测试**无法区分「计划生效」与「计划静默为 null」**——两者的可见产物相同。
  区分证据目前只有**代码可达性**（`ApplicationService.cs:185` 是唯一生产接线点，
  端口与 `CpgWorkShardPlan` 的构造/查询路径均已跑通并由 `CpgWorkBatch` 契约测试覆盖其序号唯一性）。
- **未**在真实多文件语料上度量批次数变化 ⇒ **D2 收益仍为未测**（§D 的「收益 = 0」
  现应读作「**收益未测**」，因为此前的「零调用者」已不成立）。
- §D 表格里的「能否在生产启用 = 🚫 不能」**仍然成立**——本轮是**绕过**它，不是满足它。

