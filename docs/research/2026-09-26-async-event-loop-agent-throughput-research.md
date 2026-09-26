# 异步事件循环与外置 runtime 能否加速 agent 执行：机制、一手来源与本仓库已实测的边界

- 日期：2026-09-26（Asia/Shanghai）
- 触发：关于「异步事件循环 + 外置 runtime + 任务颗粒度」能否显著提升 agent 整体完成速度的讨论
- 状态：**资料审查与本地证据盘点，未运行任何性能测量**。本文不修改产品代码、不修改任何配置、不改 feature 状态。
- 范围：把该说法拆成可检验的机制；核对其一手来源；与**本仓库已有的并发实测证据**对账；给出当前配置下**可验证**的提速路径。

---

## 0. 结论摘要

| # | 结论 | 强度 |
| --- | --- | :---: |
| **C1** | 该说法描述的**不是更快的模型**，而是把「等待」从串行改成重叠。它提升的是**总完成时间/吞吐**，不是单任务延迟 | 一手来源 |
| **C2** | 「外置 runtime」在一手文献里有明确定义：orchestrator 作为**异步事件驱动**系统独立于推理引擎，两者通过薄 API 通信以启用重叠 | 一手来源 |
| **C3** | 「颗粒度不够就循环不起来」有**可量化的对应机制**：一次迭代的后继 prompt 中 **50–80% 与工具输出无关**，可提前 prefill；颗粒度太粗 ⇒ 该可重叠段消失 ⇒ 事件循环无物可交错 | 一手来源 |
| **C4** | **本仓库已经是该模式的实现**：`WorkScheduler` 的设计目标就是「全异步单循环」+ 扁平分片。这不是新方向，而是**在途工作** | 源码与设计文档 |
| **C5** | **但本仓库的并发实测证据表明：在这台机器上，提高并发不是第一杠杆，甚至可能反向。** 单线程曾占进程 CPU 50.2%、并行效率仅 5.7%、平均并发文件数仅 0.69，而**并发争用使单次成本放大最高 19×**（12 路 53.4 s vs 串行本可 33.2 s） | 实测（既有报告） |
| **C6** | 本机 **13.86 GB** 物理内存，而本仓库主导负载中**单个文件的瞬态分配曾达 14.17 GB**（1.02× 物理内存）⇒ 该负载是**内存受限**而非 CPU 受限，加并发的收益面被内存墙封住 | 实测（既有报告） |
| **C7** | 本仓库**已用全局互斥锁强制串行化**所有 dotnet 构建/测试（`Invoke-SerialDotnet.ps1`），并有**多次并发写入者导致的假失败**记录 ⇒ 「把长任务并发跑」在此仓库有明确的反面证据 | 源码与历史记录 |
| **C8** | 当前 DSH 配置里有**四个可用的并发面**（step 内并行工具、后台 job、外置 runtime 工作流、后台子 agent），且**并发上限都不是当前瓶颈**——真正未被利用的是**用法**（前台等长命令、一次只发一个请求） | 配置核对 |
| **C9** | 用户配置中 `llm-pi-ai.subagentEffort` 是**未被识别的键**（全 `resources` 树零命中） | 检索证据 |
| **C10** | 该说法所引用的「1.5 小时 9 个任务 + 3 轮审查」**不构成证据**——缺并发度、token 账本与独立性三个量。是否可信与是否可归因是两个问题 | 方法学 |

**一句话：**
> 这套机制是真实且成熟的，而且**本仓库正在自己实现它**；但在**这一台机器 + 这一个负载**上，
> 收益的约束是**内存与颗粒度**，不是「事件循环还没写」。当前最大的可验证提速空间在
> **消除前台阻塞**与**不制造并发写冲突**，而不是调大并发数。

---

## 1. 问题

「任务执行得相当慢，需要一种机制加速 AI 完成整个执行的速度」。被引述的说法是：

> 我实现了一个异步的事件循环，外置 runtime，用异步提高了吞吐量，这个事件循环的效果比我想得好得多，
> 原理其实并不难，主要是要做好基本任务的颗粒度，不然循环不起来。v41f 速度确实奇快，
> 单独一个 session，1 个半小时，完成了 9 个小任务 + 3 轮审查。

本文回答两件事：

1. 这段话描述的机制**究竟是什么**，以及它的一手来源如何表述；
2. 把它落到**本仓库与当前 DSH 配置**时，哪些部分已被本地证据支持、哪些已被本地证据**反对**。

---

## 2. 机制翻译：三个说法各自对应什么

### 2.1 「异步事件循环」= 把等待重叠，不是把计算变快

一手来源：Sutradhara（[arXiv:2601.12967](https://arxiv.org/abs/2601.12967)，Microsoft Research）。
它把这类系统按**结构**拆成三层，并明确 orchestrator 的形态：

> "The orchestrator implements the control logic for agentic execution. Built as an **asynchronous
> event-driven system** (typically using Python's asyncio or similar frameworks), it manages the
> iterative loop..."

实现层面它自述为：

> "We implement Sutradhara as an **asynchronous event-driven orchestrator** built on top of vLLM v0.11.0..."

它给出的**收益量级**是克制的，且明确了度量口径。
⚠️ **该论文存在版本口径差异，两版必须分开引用**（本文读到的是两个不同版本）：

| 指标 | v3 摘要（[arxiv.org/abs/2601.12967](https://arxiv.org/abs/2601.12967)） | 较早版本正文（[ar5iv](https://ar5iv.labs.arxiv.org/html/2601.12967)） |
| --- | --- | --- |
| 工具调用占 FTR 比例 | **30–85%** | **30–80%** |
| 中位 FTR 降低 | up to **15%**（同负载下） | **15%** |
| 端到端延迟降低 | up to **11%** | **10%** |
| 吞吐 | 同 FTR 下可承受 **up to 77% 更高负载** | **without degrading throughput** |
| 单请求最高降低 | —（摘要未给） | up to **35%** |

**本文不裁定哪一版为准**——只记录：引用时必须写明版本，两版的百分比不可混用
（这与本仓库 `loh-large-object-optimization-guide.md:275-277` 记录的「同一数字不同组成不得混用」同型）。

> ⚠️ **口径提醒**：FTR 是「用户提交请求 → 最终答案首个 token 渲染」。它是**单请求延迟**指标，
> 不是吞吐指标。「降低 FTR」与「提高可承受负载」是并列结论，**不能互相推导**。

### 2.2 「外置 runtime」= 把控制面与执行面解耦

一手来源：Temporal 的 durable HITL agent 架构说明
（[HOW_IT_WORKS.md](https://raw.githubusercontent.com/temporal-community/durable-hitl-agents/refs/heads/main/HOW_IT_WORKS.md)）。
它给的规范表述是：

> "**Frameworks own the loop.** The agent loop — observe → reason → act — and the agent abstractions
> belong to the agent framework... **Temporal is the durable-execution runtime (the substrate)**
> beneath the frameworks: persistence, retries, replay, HITL waits, versioning."

以及该模式**唯一的独有能力**：

> "...the **only thing that coordinates ACROSS frameworks.** Two frameworks each only orchestrate
> themselves; the **cross-framework** boundary is what the runtime owns."

⇒ 「外置 runtime」的价值主张有两个不同层次：
1. **可靠性**（durable：崩溃重放、重试、跨进程恢复）——这是它真正的强项；
2. **编排能力**（跨框架/跨生命周期的 fan-out 与 join）。

**吞吐**只是这两者的副产品，而不是它宣称的核心机制。把「外置 runtime」直接等同于「更快」是**读宽了**。

### 2.3 「任务颗粒度」= 决定事件循环有没有东西可以交错

这是三个说法里唯一**真正承重**的一条，且有一手量化。

Sutradhara 的机会 1（prefill–tool 重叠）给出关键数字：

> "...**50–80% of iteration i+1's prompt is available when iteration i finishes decode.**
> System instructions, conversation history, and templates don't depend on tool outputs.
> Only the final 20–50% needs tool results."

机会 2（decode–tool 重叠）：

> "Current orchestrators wait for the entire array before dispatching tools. But once the first
> JSON object completes (closing `}`), that tool can execute immediately."

**这就是「颗粒度」的确切工程含义**：如果一个工作单元内部**没有任何与等待无关的子结构**，
那么无论事件循环多异步，都没有可交错的对象——这正是「循环不起来」。

Amdahl 式的表述：一段工作中**必须独占的同步段**（一次不可分割的长模型调用、一个独占工具、
一个共享可变文件）决定了并发的上限。

同一约束在任务分解文献里有独立表述。Microsoft Learn
[优化分解粒度](https://learn.microsoft.com/en-us/training/modules/aaai-apply-task-decomposition-multi-agent-azure/6-optimize-decomposition-granularity)
给出的判据可直接引用：

> "Could a different specialist complete the next step **using only the output from this step**,
> or do they need to understand how this step worked internally? If they need internal
> understanding, the boundary is wrong."

它同时点名三个反模式：**nano-task**（拆到不含推理的粒度）、**冗余协调**（共享同一上下文却拆开）、
**为拆而拆**。其教学表（**原文标注为示意值，非产品保证**）：

| | 单 agent | 3 agent | 6 agent |
| --- | --- | --- | --- |
| 总延迟 | 42 s | 50 s (+19%) | 61 s (+45%) |
| token | 8,200 | 10,600 (+29%) | 14,300 (+74%) |
| 质量分 | 72 | 89 | 92 |

⇒ **从 3 个拆到 6 个，质量 +3 分，资源 +38%。** 颗粒度不是「越细越好」。

### 2.4 harness 作为可执行对象

若要把「颗粒度」做成可复用资产而非个人手感，一手来源是
[Natural-Language Agent Harnesses / IHR](https://arxiv.org/abs/2603.25723)：把 harness 策略表示为
**可执行的文档**，由共享 runtime 解释为 *agent 调用、handoff、状态更新、验证闸门、产物契约*。

这条对本仓库的适用性有限——它是研究原型，不是可直接引入的依赖。列出仅为界定概念边界。

### 2.5 该模式的**代价**（一手来源明确承认）

- Claude Code 官方文档：[agent teams](https://code.claude.com/docs/en/agent-teams)
  ——"Agent teams **use significantly more tokens** than a single session."
- 扇出规模：[dynamic workflows](https://code.claude.com/docs/en/workflows)
  一个 orchestrator 可扇出**上百个** subagent。文献侧（[apidog 综述](https://apidog.com/es/blog/claude-code-dynamic-workflows-opus-4-8/)）
  对代价的描述是「数百个 xhigh worker 意味着数百万 token」——**二手来源，仅作量级参考**。

⇒ **吞吐量的分子是任务数，分母其实是钱。** 任何「提速」结论都必须带 token 账本，否则不可比。

---

## 3. 本仓库已经是这个模式——并且已经测出了它的边界

**这是本文最重要的一节。** 该说法描述的架构，本仓库在 `docs/plans/2026-09-24-unified-work-scheduler-design.md`
里已经设计并部分实现，因此**不需要从零引入**，而应该先读已有证据。

### 3.1 设计目标就是「全异步单循环」

设计文档 §3.2（`2026-09-24-unified-work-scheduler-design.md:258`）标题即
**「worker 模型：全异步单循环」**：

```text
构造时：只记录 P = WorkSchedulerOptions.WorkerCount，不启动线程
首次提交时：启动 P 个长期 worker（Task.Run），此后常驻
        每个 worker 循环：等待信号门 → 领取一个就绪项 → await ExecuteAsync
提交时：把 WorkItem 放入就绪集，并脉冲信号门
        worker 完成后：递减后继依赖计数，归零者入就绪队列，并脉冲信号门
结算时：全部完成后按 StableOrder 归并返回，然后从活动集合移除该提交（再次脉冲）
释放时：停止领取新提交，但继续服务在途提交，二者皆空后 worker 退出
```

实现落在 `src/NLISSN.Infrastructure/Concurrency/WorkScheduler.cs`（`public sealed class WorkScheduler : IAsyncDisposable`，:15）。

**该文档对「异步包装的成本」的处理方式值得单独注意**——它正是本文 §6 所要求的验证标准：

| 口径 | 最优耗时 |
| --- | --- |
| 直接调用 | 31.8 ms |
| 内核包装（`Task.FromResult`） | 29.8 ms |

原文结论（`:292-294`）：

> 差值 −6.26%，但**单轮离散度约 10%** ⇒ 结论只能诚实地写为：
> **包装开销落在噪声内，未观察到可归因于包装的回归**。
> **不得**据此声称精确的 ≤1%，也不得声称有任何加速。

⇒ 面对「事件循环效果比我想得好得多」这类断言，本仓库已有的标准是：**落在噪声内就写落在噪声内。**

### 3.2 「颗粒度」在本仓库有对应的硬约束：分片

设计文档 §6「大文件分片」（`:469`）把整个文件切成 K 片并行后合并，理由是：

> ⇒ **架构不需要重新发明，只需要保证所有 pass 都遵守「worker 产出候选 → 单写入物化」。**

四条分片契约（`:487-497`）中，第 1 条与第 3 条正是「颗粒度」的边界条件：

1. **节点身份是稳定锚，不是 NodeId**——分片内**不得**分配全局 NodeId，归并时由 reducer 统一分配；
2. **跨片引用必须显式化为 boundary reference**；
3. **interprocedural pass 必须等全部片完成**（barrier 从「方法批之间」变成「分片之间」）；
4. `FreezeQueryIndex()` 只在全部归并后执行一次。

而 §6.3「明确的不确定（诚实标注）」把它标为**本设计最大的风险点**：

> ⚠️ **本设计未验证**：把一个文件的方法集合切成 K 片后，各片的 Roslyn 语义查询
> （`GetTypeInfo`/`GetSymbolInfo`/`GetOperation`）与不切片是否**逐节点等价**。
> ...这**是**本设计最大的风险点，必须在 Stage 4 用 DOP 等价契约测试正面证明，
> **未通过前不得开启大文件分片。**

⇒ **「把颗粒度做细」不是免费的**：它改变了跨方法共享缓存的命中顺序，可能改变产物。
这正是 §2.3 那条判据在真实代码上的形态。

### 3.3 反向证据：在这台机器上，提高并发**曾经更慢**

`docs/research/2026-09-23-freezequeryindex-single-thread-tail-research.md` 是 967 文件全量运行的
根因报告。其与本文直接相关的实测数字（均引自该文，非本文重测）：

| 观察 | 数值 |
| --- | --- |
| 单线程占进程 CPU | **50.2%**（tid=25956 累计 2,284.7 s / 进程总计 4,555.3 s） |
| 并行效率 | **5.7%**（CPG.Build 合计 3,678 s ÷ 12 ÷ 5,335 s） |
| 平均并发文件数 | **0.69**（3,677.5 s ÷ 5,335.1 s） |
| 池本身是否瓶颈 | **否**：`poolPeakActive=12`、`poolQueueWaitMaxMs=1.21` |

⇒ **调度器没有排队，并发却上不去。** 瓶颈是**巨文件内部的纯串行阶段**（`FreezeQueryIndex`），
于是「并发度从 12 塌缩到 ≈1」。

更关键的是实验 2d 的结论（`:76-79`）：

> **分析间并发争用** —— 真实 `directoryDop=12` 下还有文件之间的争用：
> 单次成本可放大 **19×**（K=1 2.77 s → K=12 52.68 s，GC 暂停占 83%）；
> 1/4 规模下 K=2 即 **3.29×**、K=3 即 **5.29×**。
> **并发甚至比串行更慢**（12 路实测 53.4 s vs 串行本可 33.2 s）。

并据此给出与「提高并发」**方向相反**的建议（`:1086-1087`）：

> **反直觉但重要**：这与「提高并行度」方向相反。
> **在内存超配的工作负载上，降低特定阶段的并发反而能缩短总时长。**

### 3.4 为什么是内存而不是 CPU：本机的物理上限

`docs/research/2026-09-23-freezequeryindex-single-thread-tail-research.md:60-61`：

> NPC.cs（E = 20,723,806）在**单文件内**产生约 **14.2 GB 瞬态分配**（734 B/edge × E），
> 约为本机 **13.86 GB 物理内存的 1.02 倍**。

本次核对机器环境：

| 项 | 值 |
| --- | --- |
| 逻辑处理器 | **16** |
| 物理内存 | **13.86 GB** |

⇒ **单个文件的瞬态分配就略超物理内存。** 在这种负载上加并发，等于让多个超配工作集互相触发
gen2 GC 与换页。这是本仓库所有「并发反噬」结论的物理根因。

### 3.5 关于「降并发」：仓库里两处结论的**作用域不同，不可混用**

两处都提到「降并发」，但**目标不同**，必须分开读：

| 出处 | 目标 | 结论 |
| --- | --- | --- |
| `docs/loh-large-object-optimization-guide.md:292` | 降低 **LOH 高位常驻** | **已实测无效**（真实并发 ≈ 0.70） |
| `2026-09-23-freezequeryindex...:1078-1087` | 缩短 **墙钟总时长** | 限制巨文件阶段并发重叠**有效**（方向 2，次高优先） |

⚠️ **这是本文发现的一处容易被误读的地方**：前者说「降并发治不了常驻高位」，
后者说「降并发能缩短墙钟」。二者**不矛盾**（不同被解释变量），
但**任何引用都必须写明是哪一个口径**——这正是 `loh-large-object-optimization-guide.md:275-277`
自己警告过的「同一数字不同组成不得混用」的同型问题。

### 3.6 一处**未对齐的引用**（记录，非本文结论）

设计文档 §5.2（`:433-435`）用「成本降序就绪序（LPT）」让巨片**最先**启动，并引用：

> 这与 `2026-09-23-freezequeryindex-single-thread-tail-research.md:1091` 的既有建议一致。

但该文件 `:1089-1097` 的标题正是
**「3. 撤销"巨文件前置"（原方案 1 已被实验 2d 推翻）」**：

> **该建议现已被推翻**：实验 2d 表明巨文件的 `FreezeQueryIndex` 与其它文件**并发会互相放大成本**
> （K=2 即 3.29×，K=3 即 5.29×）。前置巨文件恰恰会**最大化**这种有害重叠。
> ⇒ **不应采用。**

被引用行 `:1091` 是**被推翻建议的原陈述**，而非结论。

**本文不判定调度设计错**：LPT 只在「巨片已按 §6 切成 K 片」之后才成立，
而 §6.3 明确「未通过前不得开启大文件分片」——即该论证的正确性**依赖一个尚未启用的前提**。
记录此处的目的仅是：**该引用当前会把读者导向已被撤回的建议**，需要实现者对齐。

### 3.7 并发写冲突：本仓库最硬的「不能并发」证据

**(a) 构建/测试已被显式串行化。** `Build/Tools/Invoke-SerialDotnet.ps1` 用**按检出路径哈希命名的全局命名互斥体**
强制同一工作树内一次只跑一个 dotnet 命令：

```powershell
$mutexName = '{0}NLTX-DotnetBuild-{1}' -f $mutexScope, $checkoutHash
```

并被多份执行计划列为强制规则（如 `2026-09-20-cpg-workbatch-concurrency-execution.md:18`）：

> Every compile-capable command must go through `Build\Tools\Invoke-SerialDotnet.ps1`

⇒ **「把多个构建/测试并发跑」在此仓库不是未优化，而是已被机制明确禁止。**

**(b) 并发写入者造成的失败有大量记录。** 均为既有记录，非本文新测：

| 形态 | 出处（示例） |
| --- | --- |
| 编译失败（`CS0246 StagePlanWithDataFlowContext`，该类型全仓零定义） | `Context/progress.md` §⑤「并发写入记录」 |
| `ContractTests` 整体无法编译（`CS0051`/`CS0122`/`CS0117`） | `docs/plans/2026-09-24-unified-work-scheduler-execution.md:813` |
| 文件锁 `MSB3027/MSB3021`（另一 vstest 占用 `RoslynDeletionPrototype.ContractTests.dll`） | `docs/benchmarks/2026-09-25-p01-dataflow-candidate-scan-pruning-report.md:115` |
| 全 Cpg 过滤器 `597/600`，3 条失败全属并发写入者 | `docs/plans/2026-09-24-unified-work-scheduler-execution.md:2364` |
| 「不可复现」的 13 失败，落在他人重写 `NLCPGBuilder.cs` 的时间窗内 | `Context/progress.md` §②「两个必须复用的坑」 |

> ⚠️ **引用方式说明**：`Context/progress.md` 是**活文档**且正被并发会话改写——
> 本文写作期间该文件从 4,844 行增长到 4,853 行，涉及标题从 `:400` 位移到 `:409`、
> 其下被引用的那行从 `:424` 位移到 `:433`。
> 因此本文对该文件的引用一律使用**可检索的标题/引文**而非行号。
> 这一现象本身即 §3.7 论点的又一实例。

**(c) 并行 pass 曾真实数据损坏（Gate G4 FAIL）。** `docs/research/2026-09-24-cpg-parallel-pass-data-race-and-fix-options.md`
记录无锁「读—改—写」共享字典导致的
`Operations that change non-concurrent collections must have exclusive access`，
**同参数 3 次运行 2 次失败**；且 `Context/progress.md` 轮次 29 记录更严重的形态：

> **3 次 `AddNode` 只留下 2 个节点**——构图态容器非线程安全，**丢写且不抛异常**。

⇒ 最终形态是**静默数据损坏**，不是崩溃。

**(d) 并行子 agent 曾整体失败。** `.learnings/ERRORS.md` `[ERR-20260910-SUBAGENT-503]`：
三个只读 research/review subagent **全部**因外部服务 503 失败，
其输出**未被采用为证据**。

---

## 4. 当前 DSH 配置里实际可用的并发面

以下为**读源码核对**（非推测）。所有默认值均为当前安装版本的实际值。

| 机制 | 包 | 默认值 | 语义要点 |
| --- | --- | --- | --- |
| step 内并行工具 | `dsh-agent-loop` | `maxParallelToolCalls: 10` | 并行安全调用用**有界滚动池**；**独占调用单独运行并构成排序屏障** |
| 后台任务运行时 | `dsh-jobs-local` | `maxConcurrentJobsPerOwner: 10` | 统计 `running + stopping`；超限时 `start` **直接报错** |
| 外置 runtime 工作流 | `dsh-workflow-worker-thread` | `maxConcurrentAgents: 0` → **按 CPU 解析** | 脚本在**宿主事件循环之外的独立 worker thread** 中运行 |
| 后台子 agent | `dsh-tool-subagent` | `backgroundMode: continuable`（base bundle 层） | 默认后台、立即返回 id；`maxDepth: 3` |
| 上下文压缩 | `dsh-compaction-basic` | `thresholdRatio 0.8` / `retainRatio 0.16` / `maxTokens 8192` | 先跑确定性工具结果裁剪（`8192/4096/1024` 码点）再调模型 |
| fresh-agent 轮次 | `dsh-tool-ralph` | `maxRounds: 64` | 无对话种子，workspace 当持久记忆 |

**关于 `maxConcurrentAgents: 0` 的解析**（`dsh-workflow-worker-thread/lib/index.js`）：
配置为 `0` 时按可用并行度解析为 `min(16, max(1, availableParallelism() - 2))`。
本机 16 逻辑处理器 ⇒ 实际为 **14**。

**关于用户 patch 层**：`C:\Users\shan\.dsh\profiles\desktop\cordis.patch.yml` 当前**没有任何并发行**，
它只做三件事：禁用 `archify-skill-filesystem` / `ui-usage-billing` / `modsearch`，
以及把 web seam 指回官方 provider。

⚠️ **该文件自己记录的语义**（`dsh-base/cordis.patch.yml` 头部）：

> A patch replaces the targeted row's whole `config` rather than merging into it,
> so a row whose value differs by mode does NOT live here.

⇒ **若要通过 patch 调并发，必须重述该行的全部字段**，只写一项会静默丢掉其余项。
这是配置改动的主要风险点。

### 4.1 一个无效配置键

用户 `settings.yaml` 的 `llm-pi-ai:` 分节下有 `subagentEffort: max`。
在 `resources` 全树检索字符串 `subagentEffort` ⇒ **零命中**。
它不是 `llm-pi-ai` 的 schema 字段，也不是任何包的配置项。

⇒ 该行**不起作用**。行为上没有坏处（子 agent 本就通过 `agentOptions` 继承父级
provider/model/reasoningEffort），但它是死配置。

### 4.2 四个串行源（这才是「慢」的直接来源）

| # | 串行源 | 为什么它是串行源 | 证据 |
| --- | --- | --- | --- |
| **S1** | **一个 turn 只有一个模型请求在飞** | 这是最硬的串行源。只有 `subagent` / `workflow` 才能引入第二条并发流 | `dsh-agent-loop` 的 step 状态机 |
| **S2** | 长命令在前台等 | 构建/测试是**教科书级的后台候选**；本仓库的验证流程全是这种长命令 | §3.7(a)：本仓库反而要求构建**串行** |
| **S3** | 多个短查询分多次 assistant message 发出 | 并行池只在**同一 step 内**生效；分步发等于退化成串行 | `dsh-agent-loop`：池按 step 组队 |
| **S4** | 并发编辑同一棵工作树 | §3.7(b)(c)：编译失败、文件锁、**静默丢写** | 大量既有记录 |

⇒ **S1 与 S2 是可在不改任何代码的情况下改善的；S4 是必须用机制隔离的。**

---

## 5. 综合判断

将 §2 的机制与 §3 的本地证据对账：

| 说法 | 是否成立 | 本地对账 |
| --- | --- | --- |
| 异步事件循环能提高吞吐 | **成立，但有前提** | 本仓库已实现该形态（§3.1）；前提是内存不超配、颗粒度足够细（§3.3/§3.4） |
| 外置 runtime 是关键 | **部分成立** | 其核心价值是**可靠性/跨框架编排**，吞吐是副产品（§2.2）。本仓库的 `WorkScheduler` 是同一形态 |
| 颗粒度做不好就循环不起来 | **成立，且最承重** | 本仓库的分片设计（§3.2）就是这个约束的实例，且**带着未验证的等价性风险** |
| 该模式下必然更快 | **不成立** | 本仓库实测：并行效率 5.7%、并发争用放大 19×、并发比串行更慢（§3.3） |
| 「1.5 小时 9 个任务」可作为证据 | **不成立** | 缺并发度、token 账本、审查独立性（§6.2） |

---

## 6. 可验证的提速路径（按「证据强度 ÷ 风险」排序）

> ⚠️ **本节是建议，不是已测结论。** 本文**未运行任何测量**。

### 6.1 第一档：零产品代码改动、零并发风险

1. **消除前台阻塞（对应 S2）**：把构建/测试等长命令交给**后台 job**，
   在它运行时继续做只读工作。这是唯一不需要碰并发度、也不需要改配置的优化。
2. **合并 step 内的独立调用（对应 S3）**：把相互独立的读/搜/查放进**同一条**
   assistant message，让已有的 `maxParallelToolCalls` 池真正生效。
3. **清理死配置**：删除 `settings.yaml` 的 `subagentEffort`（§4.1）。
4. **只读并行**：审计/搜索/复核类任务可安全扇出（这类子 agent 不写共享状态）。

### 6.2 第二档：把「提速」变成可归因的结论

**这是本文最强烈的一条建议。** 该说法目前**不可验证**，原因是缺三个量：

| 缺的量 | 为什么必须 |
| --- | --- |
| **并发度** | 9 个任务若并发，墙钟只反映**最长链**，与「12 个单元之和」无关 |
| **token 账本** | 吞吐的分子是任务数、分母是钱；无账本无法与串行基线比 |
| **审查独立性** | 3 轮审查若在同一上下文内自我确认，不是独立验证 |

本仓库对这类问题的现有标准是**配对采样 + 阳性对照**。
`Context/progress.md` §③ 记录了一次范例：阳性对照注入已知 +900 ms，
观测 +880.4 ms（均值无偏）但 **CI 跨 0** ⇒ 判定「端到端计时在此机器上原理性不可行」，
并进一步由 sd 反推所需样本数（分辨 3% 需约 41 对，1% 需约 361 对）。

⇒ 同一标准应施加到「提速」上：若不能给出配对样本与 token 账本，
就只能报告**「未观察到可归因的加速」**（与 §3.1 对 `Task.FromResult` 的处理同型）。

### 6.3 第三档：需要先解决隔离，再谈并发

**并发编辑同一棵工作树**（S4）是唯一会**破坏正确性**的一档。
在 §3.7(c) 的形态下，失败是**静默丢写**而非崩溃——即错误会进入产物。

任何「写型并发」方案**必须**先给出隔离与合并契约。备选（按隔离强度）：

| 方案 | 隔离 | 代价 |
| --- | --- | --- |
| git worktree 隔离 | 文件系统级 | 合并成本由实现者承担 |
| worker 采集 + 单写入 reducer | 逻辑级 | 需要显式 boundary 契约（§3.2 的四条） |
| 不做写型并发，只做只读并行 | 无需隔离 | 吞吐上限受限 |

⚠️ **本仓库已明确否决过「靠锁把并发路径退化成串行」**
（`docs/plans/2026-09-21-cpg-project-workbatch-pool-execution.md:445`，原文：

> 不能只在最后对 JSON 文本排序掩盖 graph/query 层的不确定性，
> **也不能通过锁把并发路径强制退化成逐文档串行**。

）——即隔离方案不能以「加一个大锁」实现，否则等于没做。
同型的反串行化护栏还出现在 `2026-09-24-unified-work-scheduler-execution.md:724`：
断言 `distinctWorkers>1` 且 `peakActive>1`，**确保竞态不是靠改回串行掩盖的**。

---

## 7. 未验证边界（勿越读）

1. **本文未运行任何性能测量。** 所有数字均为**引用既有报告**，文内已逐条标注出处与口径。
2. **§2 的一手来源运行在 A100 GPU + vLLM 上**（Sutradhara），与「本机 16 核 / 13.86 GB / 远程 API 模型」
   不可直接类比。其 15%/10% 的收益**不可外推**到本机。
3. **MS Learn 的粒度表原文标注为示意值**，不是产品保证，不可作为定量依据。
4. **§3.5 的两处「降并发」结论作用域不同**，本文只做并列陈述，**未**判定其是否可统一。
5. **§3.6 的引用未对齐，本文未判定设计对错**，只记录该引用会把读者导向被撤回的建议。
6. **§4 的 DSH 参数为读源码所得**，未运行中验证其实际生效值（例如未观测真实并发是否达到 14）。
7. **未验证 **：任何提速方案的实际收益。§6 是路径建议，不是结论。
8. **未评估 **：调大 `maxParallelToolCalls` / `maxConcurrentJobsPerOwner` 的副作用（如上游 API 的并发配额）。
   用户当前使用第三方 relay（`codego` / `zero.cat`），其并发上限**未知**。
9. **未检索 **：`dsh-goal-round-driver` 的轮次上限是否可由配置覆盖（其包文档未列出该字段，
   二手来源给 256，本文未采信为事实）。

---

## 8. 来源

### 8.1 一手来源（论文 / 官方文档 / 源码）

| 编号 | 来源 | 本文使用的依据与边界 |
| --- | --- | --- |
| P1 | [Sutradhara: An Intelligent Orchestrator-Engine Co-design (arXiv:2601.12967)](https://arxiv.org/abs/2601.12967)；正文另见 [ar5iv 全文](https://ar5iv.labs.arxiv.org/html/2601.12967) | 工具调用占 FTR 30–80%/30–85%（**两版本不同，见 §2.1**）；50–80% 后继 prompt 与工具无关（ar5iv 正文）；orchestrator 为异步事件驱动；15%/10–11% 收益（A100 + vLLM，不可外推） |
| P2 | [Natural-Language Agent Harnesses / IHR (arXiv:2603.25723)](https://arxiv.org/abs/2603.25723) | harness 作为可执行对象、共享 runtime 解释为 agent 调用与产物契约；研究原型 |
| P3 | [Temporal durable HITL agents — HOW_IT_WORKS.md](https://raw.githubusercontent.com/temporal-community/durable-hitl-agents/refs/heads/main/HOW_IT_WORKS.md) | 「frameworks own the loop, runtime is the substrate」；外置 runtime 的独有能力是跨框架编排；durable 的核心价值是可靠性 |
| P4 | [MS Learn — Optimize decomposition granularity](https://learn.microsoft.com/en-us/training/modules/aaai-apply-task-decomposition-multi-agent-azure/6-optimize-decomposition-granularity) | 颗粒度张力、三个反模式、「capability interface」判据；**表内数值原文标注为示意值** |
| P5 | [Claude Code — agent teams](https://code.claude.com/docs/en/agent-teams) | agent teams 的 token 代价显著高于单会话 |
| P6 | [Claude Code — dynamic workflows](https://code.claude.com/docs/en/workflows) | 单个 orchestrator 可扇出上百 subagent |
| P7 | [Fennec-AI/agent-runtime](https://github.com/Fennec-AI/agent-runtime) | 一层 loop 递归到所有层级；**sequential-batch** 工具分发器：保持模型声明的顺序，只把**连续的** concurrency-safe 调用放入 `Semaphore` 并行 |
| P8 | [DeepSeek V4.1 Flash 发布说明](https://api-docs.deepseek.com/zh-cn/news/news260910/) | 单次调用速度与整体墙钟是两件事；KV Cache 压缩降低 agent 场景成本 |
| P9 | [callmux](https://github.com/edimuj/callmux) | MCP 工具调用多路复用（并行/批/缓存/流水线）的参考实现 |

### 8.2 本仓库来源

| 编号 | 文件 | 本文使用的依据 |
| --- | --- | --- |
| L1 | `docs/plans/2026-09-24-unified-work-scheduler-design.md` | 「全异步单循环」设计（§3.2）、无嵌套扁平 DAG（§4）、分片四契约与风险（§6/§6.3）、`Task.FromResult` 成本落在噪声内（§3.2） |
| L2 | `src/NLISSN.Infrastructure/Concurrency/WorkScheduler.cs`、`WorkSchedulerOptions.cs` | 实现存在性；`WorkerCount` 取六个上限的最大值；`MaximumWorkerCount = 1024` |
| L3 | `docs/research/2026-09-23-freezequeryindex-single-thread-tail-research.md` | 单线程 50.2%、并行效率 5.7%、平均并发 0.69、池无排队、并发争用放大 19×、并发比串行更慢、LPT 建议已被推翻 |
| L4 | `docs/loh-large-object-optimization-guide.md` | 反模式清单（含「降并发对 LOH 已实测无效」）、口径不可混用的警告 |
| L5 | `Build/Tools/Invoke-SerialDotnet.ps1` | 全局命名互斥体强制构建/测试串行 |
| L6 | `docs/research/2026-09-24-cpg-parallel-pass-data-race-and-fix-options.md` | 并行 pass 的无锁读—改—写竞态；同参数 3 次运行 2 次失败 |
| L7 | `Context/progress.md` | 轮次 29「3 次 `AddNode` 只留下 2 个节点」；§③ 阳性对照失败与样本数反推；§⑤ 并发写入者 |
| L8 | `.learnings/ERRORS.md` | `[ERR-20260910-SUBAGENT-503]`：并行只读子 agent 整体失败且输出未被采用 |
| L9 | `D:\SoftwarePackage\DSH\DSH Desktop\resources\app\node_modules\@deepseek-ai\dsh-*` | DSH 并发面的默认值与语义（§4） |
| L10 | `C:\Users\shan\.dsh\settings.yaml`、`profiles\desktop\cordis.patch.yml` | 当前生效配置；`subagentEffort` 为死键；用户 patch 层无并发行 |

### 8.3 二手来源（仅用于量级参考，未采信为事实）

| 来源 | 用途 |
| --- | --- |
| [DeepSeek Harness 长任务三件套（七牛云）](https://news.qiniu.com/archives/1789379039235) | goal/compact/jobs 三组插件与默认参数的综述；其 goal 轮次上限未被本文采信（见 §7.9） |
| [apidog — Claude Code Dynamic Workflows](https://apidog.com/es/blog/claude-code-dynamic-workflows-opus-4-8/) | 「数百 xhigh worker ⇒ 数百万 token」的量级参考 |
