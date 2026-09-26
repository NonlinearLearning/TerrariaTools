# Version4 DOP12 全并行诊断记录

> 状态：未完成（运行被用户主动停止，未产生分析产物）。
>
> 测量日期：2026-09-22。运行 ID：`version4-dop12-20260922-173202`。
>
> 本页记录一次真实 Version4 长尾运行在“六个并行度全部设为 12、三个并行开关全部开启”
> 下的线程利用率与内存诊断证据。分析未跑完，因此本页不声称 Version4 性能结论。

## 运行配置

输入为目录 `D:\TRbackup\Version4`（967 个非 `bin`/`obj` 的 `.cs` 文件），
工具为 `tool: nlissn`。诊断 harness 生成的 `nlissn.yml` 关键字段：

```yaml
schemaVersion: 3
tool: nlissn
input:
  path: D:/TRbackup/Version4
execution:
  directoryMaxDegreeOfParallelism: 12
  cpgMaxDegreeOfParallelism: 12
  groupMaxDegreeOfParallelism: 12
  helperMaxDegreeOfParallelism: 12
  replayMaxDegreeOfParallelism: 12
  maxConcurrentOperations: 12
  directoryParallelism: true
  groupParallelism: true
  helperParallelism: true
```

用户给出的原始值为 `4/12/4/4/4/8`；按“所有并行参数使用 12 并全部开启并行”的要求
全部覆盖为 12、三个开关全部 `true`。`runtime.log` 的 `evt=started` 行证实生效：

```
directoryDop=12 cpgDop=12 groupDop=12 helperDop=12 replayDop=12 maxConcurrentOperations=12
```

输入不被改写：`writeBack: false`、`skipRewrite: true`、diff/evidence 关闭。
运行后核对，`D:\TRbackup\Version4` 下 `Build\` 之外 0 个文件被写入，
非 `bin`/`obj` 的 `.cs` 数量仍为 967。

启动的是已构建入口 `Build/src/Debug/net10.0/NLISSN.exe`，不是 `dotnet run`
（`dotnet run` 在该输入下会以 1.4 s CPU / 150 s 挂起，见“已知陷阱”）。

**构建来源未能固定（引用本文源码结论时必读）**：harness 的 `run-environment.yml`
记录了工具版本、DOP、启动时刻，但**没有记录源码提交号或构建哈希**；而
`src/NLCPG/Builder/Passes/DataFlowPass.cs` 在本次运行（17:32 启动）**之后**
被修改过（文件 mtime 2026-09-22 23:23），`Build/src/Debug/net10.0/NLCPG.dll`
也随之在 23:23 与 23:46 重建。因此：

- 运行时的二进制**已被覆盖**，无法再从磁盘取回比对；
- 本文引用的 `DataFlowPass` 位集分配代码取自 **`git show HEAD:`（提交 `925161d`）**，
  它是与运行时间最接近的可核对状态，但**不是**对运行二进制的证明；
- 当前工作区版本已与 HEAD 不同（`+231/−85` 行），位集已从
  `wordsPerSet = BitSetWordCount(plan.FlowNodes.Length)` 两数组（`inSets`+`outSets`）
  改为 `BitSetWordCount(definitionNodes.Count)` 单数组（`outSets`，`in` 集收敛后重算）。
  即**本文描述的 O(N²) 双数组形态属于运行时的代码，现行工作区已部分修复**；
  两者不可混引。

复现同类运行前，应在 harness 里补记构建哈希（例如落盘
`git rev-parse HEAD` 与工作区是否 dirty），否则事后无法判定结论对应哪一版代码。

## 采样方案

三个诊断工具均为全局工具 `10.0.745401`：

| 工具 | 用途 | 本页产物 |
| --- | --- | --- |
| `dotnet-counters` | GC、托管堆、分配速率、Working Set 趋势 | `Counters/system-runtime.csv` |
| `dotnet-gcdump` | 按对象类型统计实例数量与总占用 | `Gcdump/`、`GcdumpTypeStats/` |
| `dotnet-trace` | 调用栈耗时采样 + **分配 tick 调用栈**（见下） | `Trace/allocation.nettrace` |

⚠ 这里要区分**两件事**（原表述把二者混为一谈，已更正）：

1. **`dotnet-trace report topN` 的语义**是"**在调用栈上停留最久的方法**"
   （`dotnet-trace report --help`），即 CPU/栈时间采样——**这个命令本身不给出分配归因**。
2. **但原始 `.nettrace` 里含分配调用栈**。`--profile gc-verbose` 启用的关键字是
   `0x8003` = `GC(0x1) | GCHandle(0x2) | OverrideAndSuppressNGenEvents(0x8000)`，
   而 **`GC/AllocationTick` 属于 `GC(0x1)`，已被启用**。
   用 `TraceEvent` 解析实测：**127,295 个事件、61,433 个 `GC/AllocationTick`，
   其中 61,432 个带完整调用栈**，窗口内累计 11.30 GiB
   （与 counters 同窗口 11.2 GiB 吻合，说明是完整记录而非抽样）。
   归因结果见下文"分配调用栈归因"表。

因此：**按类型的分配归因来自这份 trace 的 `AllocationTick`**，
存活对象构成来自 gcdump，趋势来自 counters。但该 trace **只覆盖前 60.03 s**
（占 1042 s 墙钟的 5.8%），凡涉及窗口外时点的归因（如 t=216 s 的 `System.UInt64[]` 峰值）
仍需另采一份覆盖长尾的分配采样才能坐实。

要求的节奏是“前 2 分钟每 3 s 一次，之后每 30 s 一次”。该节奏**在本输入上无法达成**，
见“测量开销边界”。

`dotnet-gcdump` 与 `dotnet-trace`（`--profile gc-verbose`）不能同时进行：trace 重叠
期间采集到的每个 gcdump 都会把全部类型写成 `UNKNOWN 0x7ffa…`（实测 271–326 行、
约占堆字节 99.99%），且事后无法还原（干净行不含 method-table 地址，按尺寸签名匹配
有歧义）。因此 trace 占据运行最前 61.12 s，对象类型采样在其结束后才开始。
`dotnet-counters` 覆盖全程。

**时钟口径**：`Gcdump/*` 的 `elapsedSecond` 与 `Phase` 以**采样起点**为 0，
而 `RuntimeLog/runtime.log` 与 `Counters/` 以**分析起点**为 0，两者相差
`samplingStartOffsetSeconds = 61.12 s`。下文凡把 gcdump 与运行日志对齐处均已加上该偏移。

## 线程利用率

`Threads/threads.csv` 共 230 行（3 s 间隔），覆盖采样器启动后 **694.7 s**：

| 指标 | min | max | avg |
| --- | ---: | ---: | ---: |
| `threadCount` | 17 | 41 | 22.7 |
| `threadsRunning` | 0 | 5 | 1.18 |
| `threadsWait` | — | — | 21.5 |
| `distinctWaitReasons` | — | — | 2.4 |
| `processCpuPercent` | — | 30.74 | 7.77 |
| `systemCpuPercent` | — | 87 | 50.1 |
| `workingSetBytes` | 50 MB | 8466 MB | — |
| `privateBytes` | — | 25092 MB | — |

**两个口径提醒**（否则会误读上表）：

1. `threadPoolThreadCount` 列由 `[System.Threading.ThreadPool]::ThreadCount` 取得，
   读的是**采样器自身 pwsh 进程**的线程池（实测 2–7），不是目标进程的。
   目标进程的线程池应以 `runtime.log` 的 `tpThreads` 为准，实测 **3–25**（峰值 25）。
2. `threads.csv` 的时间窗（694.7 s）短于 `Counters/`（1042 s）。采样器在
   09:43:38 停止，且**没有写出正常结束行**（无 `processAlive=false` 行），
   stdout/stderr 均为 0 字节，说明它未走正常退出路径；停止原因无记录。
   因此线程利用率只覆盖运行的前约 11.6 分钟，后段（堆涨到 18 GB 以上、GCDump 大量失败
   的区间）没有线程采样。

`processCpuPercent` 以 16 个逻辑处理器归一化，因此峰值 30.74% = **约 4.9 / 16 核**
（出现在 213.8 s），均值 7.77% = 约 1.24 核。这与 `dotnet-counters` 的 CPU 计数器一致：
全程进程 CPU 合计 1146.8 s、窗口 1042 s，平均 **1.1 / 16 核**。

核心结论：**即使六个并行度和三个开关全部拉满，进程平均只占用约 1.1 个核，峰值约 4.9 个核**
（16 逻辑处理器，AMD Ryzen 7 5800H）。平均有 1.18 个线程处于 `Running`、21.5 个处于
`Wait`，等待原因只有 2.4 种。空闲不是并行度不足导致的——并行度已经给到 12，
瓶颈在别处（见“dotnet-trace 独占热点”）。

`systemCpuPercent` 峰值 87% 是**整机**读数，包含本进程之外的负载（本次诊断同时运行
`dotnet-counters`/`dotnet-gcdump`/`dotnet-trace` 以及 `Sample-ProcessThreads` 采样），
不能当作本进程占用。

## 内存与 GC 趋势

`Counters/system-runtime.csv`：342 个采样点（`--refresh-interval 2`），覆盖 1042 s（17.4 分钟），
29 个计数器。计数器的 `Mean/Increment` 是**每个刷新间隔内的增量**，而该间隔在有 gcdump
竞争时会抖动（实测相邻时间戳差 min 0 s、max 68 s、avg 3.06 s，22 个间隔 > 4 s），
因此增量求和会**低算**真实总量。下表同时给出进程内 `runtime.log` 自报的最大值（`GC` 计数
与分配量在那里是单调累计量，更可靠）：

| 指标 | counters 区间 | 进程内 `runtime.log` 峰值 |
| --- | ---: | ---: |
| `working_set` / `wsBytes` | 101 MB – 8578 MB | 8.26 GiB |
| `committed_size` | 45 MB – 25.3 GiB | — |
| `heap.size[gen2]` | 11 MB – 7.27 GiB | — |
| `heap.size[loh]` | 21 MB – 18.0 GiB | — |
| 托管堆 `heapBytes` | — | 23.65 GiB |
| `total_allocated` / `allocBytes` | 峰值 6636 MiB / 2 s | 91.7 GiB 累计 |
| `gc.collections[gen0]` | 1176 | 3297 |
| `gc.collections[gen1]` | 2246 | 2127 |
| `gc.collections[gen2]` | 137 | 132 |
| `dotnet.gc.pause.time` | 合计 318.8 s | — |
| `dotnet.monitor.lock_contentions` | 增量峰值 858 | — |
| `dotnet.thread_pool.thread.count` | 见下注 | 见下注 |
| `dotnet.thread_pool.queue.length` | 增量，峰值 2 | 队列峰值 0 |
| `dotnet.assembly.count` | 45 → 57 | 45 → 57 |

`dotnet-counters` 的 `Mean/Increment` 列对 `System.Runtime` 的多数计数器是**增量/速率**
而非瞬时值（`dotnet.thread_pool.thread.count` 在该列出现 21 个负值，
证明它不是 gauge），因此该行不能读作“线程池线程数峰值”。
目标进程线程池的真实读数取自 `runtime.log` 的 `tpThreads`：3–25（峰值 25），
`tpPending` 全程为 0——即线程池队列**从未积压**，与“平均仅 1.1 核”互相印证：
工作没有被排到队列里等待，而是主流程本身串行。

**最关键的发现**：`runtime.log` 的 `tpCompleted`（目标进程线程池累计完成工作项）
显示**并行阶段在 t≈192 s 就结束了**：

| 阶段 | 区间 | `tpCompleted` 增量 | 速率 |
| --- | --- | ---: | ---: |
| 并行阶段 | t=0 s → 192 s（192 s） | 2 → 148,106 | 约 **771 项/s** |
| 串行阶段 | t=192 s → 787 s（595 s） | 148,106 → 148,430 | 约 **0.54 项/s** |

同期 `tpThreads` 从 17–25 降到 4–5，`tpPending` 始终为 0。
但**分配量在串行阶段仍未停止**：t=192 s 时累计分配 36.2 GiB，
到日志最后一条可读采样（t=787 s）已到 91.7 GiB——即后 595 s 里又分配了 **55.5 GiB**，
全部落在几乎没有线程池活动的单线程路径上。（日志尾部丢失，故 787 s 之后无法计量；
`Counters/` 显示该窗口末尾仍有 66 MiB/2 s 的分配活动。）

这解释了平均 CPU 只有 1.1 核：**并行度只影响最前约 3 分钟，
之后到运行结束（1042 s）的约 850 s 是同一种单线程收尾**（占总时长约 82%）。
因此把六个 DOP 从 4/12 提到 12 对总时长的杠杆有限，
真正的优化目标是那段单线程阶段在做什么（`dotnet-trace` 指向反射取参与字典重建）。

注意 `gen1` 的 counters 合计（2246）大于 `gen0`（1176），物理上不可能自洽；
这来自 `Mean/Increment` 的口径与刷新间隔抖动二者的叠加，不能把两者当同口径直接比较。
进程内日志的 gen0/gen1/gen2 = 3297 / 2127 / 132 才是同源、单调、自洽的读数。
上一节表格中 counters 列仅用于表示量级区间，不应据此下结论。

暂停时间合计 318.8 s 占 1042 s 窗口的 30.6%，是本次运行最值得注意的开销信号；
但采样期间 gcdump 每次都会强制一次完整 Gen2 GC，该数字包含观测开销，不是纯净基线。

趋势形状：Working Set 前 ~2 分钟涨到约 4.5 GiB，~6 分钟到约 6.8 GiB，
之后 committed 持续攀升至 25.3 GiB 而 Working Set 在 3.4–8.5 GiB 间震荡——
典型的“大量瞬态分配 + 尚未回收”形态，而非稳定驻留。

## 对象类型统计（实例数量与总占用）

21 次成功 gcdump 全部解析成功（0 失败）。**每个采样点的“按类型字节合计”与
`GC Heap bytes` 逐点精确相等**（21/21），这是该统计方法的正确性不变式：
它证明解析没有丢行、也没有重复计数（对 3 个被截断的采样点同样是子集内部自洽，
但子集本身的绝对值不可用于总量，见“测量开销边界”第 3 条）。

原始 `GcdumpReport` 的 21 个报告与 21 个 dump 一一对应，`GcdumpTypeStats` 由这些 dump
重新解析生成（解析 409 MB 堆约 181 ms，成本可忽略）。

- 累计类型统计长表：`GcdumpTypeStats/gcdump-type-stats.csv`（153,916 行，按尺寸桶明细）
- **每样本每类型实例数与总占用**：`GcdumpTypeStats/gcdump-type-totals-per-sample.csv`
- 全类型极值汇总：`GcdumpTypeStats/gcdump-type-summary.csv`
- 堆总量：`GcdumpTypeStats/gcdump-heap-totals.csv`

聚合口径：`dotnet-gcdump` 对数组类型按尺寸桶分行（`System.UInt64[] (Bytes > 1K)`、
`… > 10M` 等），每行只是该桶的精确计数与字节。因此一个类型的真实实例数与总占用是
**跨桶求和**。本页与上述 CSV 全部使用求和后的口径；`gcdump-type-stats.csv` 保留桶级明细。

峰值样本 `0016-late-156s`（t=156 s，堆 4.05 GB / 4,754,515 对象 / 7289 个类型）前 12 名：

| # | 类型 | 实例数 | 总占用 |
| ---: | --- | ---: | ---: |
| 1 | `System.UInt64[]` | 21 | 2324.3 MB |
| 2 | `Entry<NLCPG.Model.NLCPGGraph+PendingEdgeBuffer+PendingEdgeKey,System.Int32>[]` | 1 | 375.3 MB |
| 3 | `BufferedPendingEdge[]` | 1 (+1 个 24 B 空数组) | 240.0 MB |
| 4 | `Entry<NLCPG.Model.StableNodeAnchor,NLCPG.Model.NLCPGNode>[]` | 1 | 186.3 MB |
| 5 | `Entry<Microsoft.CodeAnalysis.IOperation,NLCPG.Model.NLCPGNode>[]` | 66 | 102.2 MB |
| 6 | `NLCPG.Model.NLCPGNode[]` | 9,041 | 66.1 MB |
| 7 | `System.String` | 484,208 | 56.6 MB |
| 8 | `Entry<NLCPG.Model.NLCPGNode,NLCPG.Model.NLCPGNode[]>[]` | 128 | 55.9 MB |
| 9 | `System.Int32[]` | 90,683 | 37.7 MB |
| 10 | `Entry<Microsoft.CodeAnalysis.SyntaxNode,NLCPG.Model.NLCPGNode>[]` | 1 | 37.1 MB |
| 11 | `Entry<NLCPG.Model.NLCPGNode,System.Int32>[]` | 6 | 33.1 MB |
| 12 | `NLCPG.Builder.OperationInventoryEntry` | 225,941 | 32.8 MB |

## 全程极值（21 个采样点）

数据源：`GcdumpTypeStats/gcdump-type-summary.csv`（7,593 行，每行一个类型在全部采样点上的
极值）与 `gcdump-heap-totals.csv`。**注意 3 个采样点被截断**（`0025/0027/0028`，
见“测量开销边界”第 3 条），因此下面对每张表都标出该极值是否来自截断样本。

### 堆级极值

| 指标 | min | max | avg | 21 点合计 |
| --- | ---: | ---: | ---: | ---: |
| 存活堆字节 | 207.6 MB（`0014-early-119s`） | **3,860.9 MB**（`0016-late-156s`） | 1,442.2 MB | 30,287.1 MB = 29.58 GiB |
| 对象数 | 2,674,613 | 10,000,000 ⚠ | 4,606,599 | — |
| 类型数 | 7,007 | 7,289 | 7,165 | — |

⚠ 对象数上限取自 3 个截断样本，是采样上限而非真实峰值。

### 按类型峰值占用（Top 15）

| # | 类型 | 峰值占用 | 出现在 | 该点实例数 | 覆盖采样点 |
| ---: | --- | ---: | --- | ---: | ---: |
| 1 | `System.UInt64[]` | **2,324.3 MB** | `0016-late-156s` | 21 | 21 |
| 2 | `System.String` | 1,600.2 MB ⚠ | `0025-late-427s` | 6,040,171 | 21 |
| 2′ | `System.String`（不含截断点） | **1,155.2 MB** | `0002-early-8s` | 3,319,825 | 21 |
| 3 | `NLCPG.Builder.Passes.InterproceduralDataFlowPlan[]` | 1,515.6 MB | `0019-late-246s` | 6 | 21 |
| 4 | `PendingEdge[]` | 589.5 MB | `0019-late-246s` | 47,365 | 21 |
| 5 | `Entry<…PendingEdgeKey,System.Int32>[]` | 375.3 MB | `0016-late-156s` | 1 | 15 |
| 6 | `NLCPG.Model.NLCPGEdge[]` | 257.6 MB | `0013-early-107s` | 4 | 6 |
| 7 | `BufferedPendingEdge[]` | 240.0 MB | `0002-early-8s` | 2 | 21 |
| 8 | `Entry<StableNodeAnchor,NLCPGNode>[]` | 186.3 MB | `0019-late-246s` | 1 | 18 |
| 9 | `Entry<NLCPG.Model.NLCPGEdge>[]` | 106.5 MB | `0002-early-8s` | 1 | 6 |
| 10 | `Entry<IOperation,NLCPGNode>[]` | 102.2 MB | `0016-late-156s` | 66 | 12 |
| 11 | `System.Int32[]` | 68.0 MB | `0013-early-107s` | 71,844 | 21 |
| 12 | `NLCPG.Model.NLCPGNode[]` | 66.1 MB | `0016-late-156s` | 9,041 | 21 |
| 13 | `NLCPG.Model.NLCPGEdgeLabel` | 63.4 MB | `0002-early-8s` | 1,039,096 | 9 |
| 14 | `System.ValueTuple<NLCPGNode,StableNodeAnchor>[]` | 63.4 MB | `0010-early-86s` | 140 | 13 |
| 15 | `Entry<NLCPGNode,NLCPGNode[]>[]` | 55.9 MB | `0016-late-156s` | 128 | 3 |

除第 2 行外，所有峰值都来自**未截断**样本，可直接引用。

### 按类型峰值实例数（Top 12）

| # | 类型 | 峰值实例数 | 出现在 | 该点占用 | 截断? |
| ---: | --- | ---: | --- | ---: | :---: |
| 1 | `System.String` | 6,040,176 | `0027-late-487s` | 1,600.2 MB | ⚠ 是 |
| 1′ | `System.String`（不含截断点） | **3,319,825** | `0002-early-8s` | 1,155.2 MB | 否 |
| 2 | `NLCPG.Model.NLCPGEdgeLabel` | 1,039,096 | `0002-early-8s` | 63.4 MB | 否 |
| 3 | `NLCPG.Builder.OperationInventoryEntry` | 225,941 | `0016-late-156s` | 32.8 MB | 否 |
| 4 | `UsedFactRecord` | 223,592 | `0016-late-156s` | 8.5 MB | 否 |
| 5 | `UsedFactRecord[]` | 132,518 | `0016-late-156s` | 4.8 MB | 否 |
| 6 | `…NonErrorNamedTypeSymbol` | 126,287 | `0019-late-246s` | 4.8 MB | 否 |
| 7 | `…BinaryExpressionSyntax` | 98,219 | `0019-late-246s` | 4.5 MB | 否 |
| 8 | `…LiteralExpressionSyntax` | 98,095 | `0008-early-74s` | 3.0 MB | 否 |
| 9 | `System.Int32[]` | 90,683 | `0016-late-156s` | 37.7 MB | 否 |
| 10 | `…ExpressionStatementSyntax` | 83,957 | `0019-late-246s` | 3.8 MB | 否 |
| 11 | `…Syntax.LiteralExpressionSyntax` | 82,529 | `0019-late-246s` | 3.8 MB | 否 |
| 12 | `…IdentifierNameSyntax` | 69,604 | `0019-late-246s` | 3.2 MB | 否 |

第 3–12 行的数值在**未截断**样本（`0019-late-246s` 等）中同样达到，故可直接引用；
只有第 1 行 `System.String` 的 604 万实例是截断样本下的下界。

### 覆盖度：谁是全程常驻、谁是瞬时

7,593 个类型中 **6,832 个在全部 21 点出现**，103 个只出现 1 次。完整分布：

| 覆盖采样点数 | 类型数 | | 覆盖采样点数 | 类型数 | | 覆盖采样点数 | 类型数 |
| ---: | ---: | --- | ---: | ---: | --- | ---: | ---: |
| 21 | 6,832 | | 14 | 30 | | 7 | 40 |
| 20 | 6 | | 13 | 47 | | 6 | 14 |
| 19 | 96 | | 12 | 14 | | 5 | 5 |
| 18 | 75 | | 11 | 4 | | 4 | 30 |
| 17 | 19 | | 10 | 19 | | 3 | 160 |
| 16 | 6 | | 9 | 20 | | 2 | 34 |
| 15 | 29 | | 8 | 10 | | 1 | 103 |

（合计 7,593 = `gcdump-type-summary.csv` 行数。表中“1 点”= 只在某一次 dump 中出现过的瞬时类型。）

按**占用之和**排序（"谁最常驻"）。这里必须给出两个口径，因为结论会反转：

**口径 A：全部 21 点**（含 3 个截断样本，其堆只上报 1,895.9 MB，不可靠）

| # | 类型 | 21 点合计 | 覆盖点数 |
| ---: | --- | ---: | ---: |
| 1 | `System.String` | 7,918.6 MB | 21 |
| 2 | `System.UInt64[]` | 4,826.4 MB | 21 |
| 3 | `InterproceduralDataFlowPlan[]` | 4,301.8 MB | 21 |
| 4 | `Entry<…PendingEdgeKey,System.Int32>[]` | 2,440.2 MB | 15 |
| 5 | `BufferedPendingEdge[]` | 1,744.0 MB | 21 |
| 6 | `PendingEdge[]` | 1,026.3 MB | 21 |

**口径 B：仅 18 个未截断样本**（可靠；总堆 24,599.3 MB）——**排名反转**：

| # | 类型 | 18 点合计 | 覆盖点数 |
| ---: | --- | ---: | ---: |
| 1 | `System.UInt64[]` | **4,826.4 MB** | 18 |
| 2 | `InterproceduralDataFlowPlan[]` | 4,301.7 MB | 18 |
| 3 | `System.String` | 3,118.0 MB | 18 |
| 4 | `Entry<…PendingEdgeKey,System.Int32>[]` | 2,440.2 MB | 15 |
| 5 | `BufferedPendingEdge[]` | 1,744.0 MB | 18 |
| 6 | `Entry<StableNodeAnchor,NLCPGNode>[]` | 916.5 MB | 18 |

差异全部来自那 3 个截断样本：它们把 `System.String` 抬到 1,600.2 MB/点，
却不计入 `System.UInt64[]`（该三点为 24 B）。

**结论**：`System.UInt64[]` 既是单点峰值冠军（2,324.3 MB），也是可靠口径下的
**累计占用冠军**（4,826.4 MB）——它虽然只在 3 个点显著，但在那 3 个点各占 666–2,324 MB，
量级足以压过全程小步累积的 `System.String`。`System.String` 是**基线大户**
（18 点稳定在 41.5–1,155.2 MB），但不是总量第一。

### 增长与回落极值

`gcdump-type-summary.csv` 的 `LastTotalBytes` 取自**最后一个采样点**（`0028-late-517s`），
而该点是截断样本；为避免把截断造成的假象当成收缩，下面只用**未截断样本**
（末点 = `0019-late-246s`，t=246 s）计算首末。

**增长最多**（首点 → 未截断末点）：

| 类型 | 首 | 末 | 增长 |
| --- | ---: | ---: | ---: |
| `PendingEdge[]` | 45.2 MB | 589.5 MB | **+544.3 MB** |
| `InterproceduralDataFlowPlan[]` | 1,019.3 MB | 1,515.6 MB | +496.2 MB |
| `Entry<…PendingEdgeKey,System.Int32>[]` | 181.0 MB | 375.3 MB | +194.3 MB |
| `Entry<StableNodeAnchor,NLCPGNode>[]` | 10.1 MB | 186.3 MB | +176.2 MB |
| `BufferedPendingEdge[]` | 120.0 MB | 240.0 MB | +120.0 MB |

**回落最多**（峰值 → 该峰值之后的谷值，仅用未截断样本；这是“相位性缓冲”的直接证据）：

| 类型 | 峰值 | 峰在 | 谷值 | 谷在 | 回落 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `System.UInt64[]` | 2,324.3 MB | t=156 s | ≈0 MB | t=246 s | **−2,324.3 MB** |
| `System.String` | 1,155.2 MB | t=8 s | 41.5 MB | t=58 s | −1,113.7 MB |
| `InterproceduralDataFlowPlan[]` | 1,019.3 MB | t=0 s | ≈0 MB | t=8 s | −1,019.3 MB |
| `NLCPG.Model.NLCPGEdge[]` | 257.6 MB | t=107 s | 0.6 MB | t=119 s | −257.0 MB |
| `BufferedPendingEdge[]` | 240.0 MB | t=8 s | ≈0 MB | t=58 s | −240.0 MB |
| `Entry<…PendingEdgeKey,System.Int32>[]` | 181.0 MB | t=0 s | 0.3 MB | t=119 s | −180.7 MB |
| `Entry<NLCPG.Model.NLCPGEdge>[]` | 106.5 MB | t=8 s | 0.1 MB | t=119 s | −106.3 MB |
| `PendingEdge[]` | 93.6 MB | t=93 s | ≈0 MB | t=107 s | −93.6 MB |

注意这与上表的“增长”**不矛盾**：同类型既可能先回落后再增长，也可能相反。
上表比较的是**首末两点**，本表比较的是**峰值与随后的谷值**，是两个不同的量。

### 阶段性：三个 GB 级类型都不是单调增长

逐样本轨迹（未截断点）显示这些大数组**随阶段涨落、随后归零**：

| 类型 | >100 MB 的点 | 其余点 |
| --- | --- | --- |
| `System.UInt64[]` | t=156/186/216 s（2,324 / 1,838 / 664 MB） | 18 点为 1 个实例、24 B |
| `InterproceduralDataFlowPlan[]` | t=0/93/99/246 s（1,019 / 1,012 / 755 / 1,516 MB） | 14 点为 2 个实例、≈0 MB |
| `PendingEdge[]` | t=246 s（589.5 MB） | 其余多为 4 个实例、≈0 MB；t=93/99 s 为 93.6 MB、t=69 s 为 51.0 MB、t=0 s 为 45.2 MB |

**这排除了"持续泄漏"的判断**：它们在相位结束后回落到极小值（`System.UInt64[]` 从
2,324.3 MB 回到 24 B）。但它们仍造成内存墙——因为**每次达到峰值都需要重新分配数百 MB
至 2.3 GB 的 LOH 内存**，而且不压缩、只能等 Gen2 回收。

`System.String` 的轨迹则相反，全程在 41.5–1,155.2 MB（未截断点）间波动，
是唯一在所有相位都占显著份额的类型。

## 全类型占用占比（峰值样本）

以下占比以峰值样本 `0016-late-156s`（t=156 s，**3,860.9 MB / 4,754,515 个对象 /
7,289 个类型**）为口径。该点未被截断，且按类型字节合计与该样本 `GC Heap bytes`
精确相等（diff=0），因此占比是自洽的。

**完整明细**：全部 7,289 个类型逐行占比如下文件，含 `Rank, Type, Module, InstanceCount,
TotalBytes, TotalMB, PercentOfHeap, CumulativePercent, SizeBuckets, BucketCount, TypeSize`：

```
docs/benchmarks/data/version4-dop12-type-share-0016.csv
```

### 按大类聚合

| 类别 | 类型数 | 实例数 | MB | 占比 |
| --- | ---: | ---: | ---: | ---: |
| `System.UInt64[]`（位集类） | 1 | 21 | 2,324.3 | **60.20%** |
| Dictionary `Entry<…>[]` 数组 | 139 | 80,073 | 861.2 | **22.30%** |
| 其余碎片类型（含数组与非数组） | 3,792 | 815,263 | 279.1 | 7.23% |
| Roslyn 对象（非数组） | 1,034 | 2,373,148 | 121.9 | 3.16% |
| 其他 NLCPG 自有数组 | 62 | 13,849 | 80.2 | 2.08% |
| 其他 System 数组 | 157 | 101,887 | 57.8 | 1.50% |
| `System.String` | 1 | 484,208 | 56.6 | 1.47% |
| NLCPG/NLISSN 对象（非数组） | 179 | 531,653 | 55.5 | 1.44% |
| Roslyn 数组 | 154 | 163,433 | 13.8 | 0.36% |
| 其他 System 对象（非数组） | 1,770 | 190,980 | 10.5 | 0.27% |

（实例数合计 4,754,515，与该样本 `GC Heap objects` 一致。）

**数组 vs 非数组**：数组类型 841 个占 **3,588.7 MB（92.95%）**；
非数组 6,448 个仅占 **272.3 MB（7.05%）**。这直接印证了 LOH 主导的判断。

### 占用最集中的前 10 个类型

| # | 类型 | 实例数 | MB | 占比 | 累计 |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | `System.UInt64[]` | 21 | 2,324.3 | 60.20% | 60.20% |
| 2 | `Entry<NLCPG.Model.NLCPGGraph+PendingEdgeBuffer+PendingEdgeKey,System.Int32>[]` | 1 | 375.3 | 9.72% | 69.92% |
| 3 | `BufferedPendingEdge[]` | 1 (+1 个 24 B 空数组) | 240.0 | 6.22% | 76.13% |
| 4 | `Entry<NLCPG.Model.StableNodeAnchor,NLCPG.Model.NLCPGNode>[]` | 1 | 186.3 | 4.82% | 80.96% |
| 5 | `Entry<Microsoft.CodeAnalysis.IOperation,NLCPG.Model.NLCPGNode>[]` | 66 | 102.2 | 2.65% | 83.61% |
| 6 | `NLCPG.Model.NLCPGNode[]` | 9,041 | 66.1 | 1.71% | 85.32% |
| 7 | `System.String` | 484,208 | 56.6 | 1.47% | 86.79% |
| 8 | `Entry<NLCPG.Model.NLCPGNode,NLCPG.Model.NLCPGNode[]>[]` | 128 | 55.9 | 1.45% | 88.23% |
| 9 | `System.Int32[]` | 90,683 | 37.7 | 0.98% | 89.21% |
| 10 | `Entry<Microsoft.CodeAnalysis.SyntaxNode,NLCPG.Model.NLCPGNode>[]` | 1 | 37.1 | 0.96% | 90.17% |

### 集中度

占用极度集中——**占 0.014% 的 1 个类型吃掉 60.2%**：

| 前 N 个类型 | 累计 MB | 累计占比 | 占 7,289 个类型的比例 |
| ---: | ---: | ---: | ---: |
| 1 | 2,324.3 | 60.20% | 0.014% |
| 3 | 2,939.5 | 76.13% | 0.041% |
| 10 | 3,481.4 | 90.17% | 0.137% |
| 50 | 3,727.3 | 96.54% | 0.686% |
| 100 | 3,802.9 | 98.50% | 1.372% |
| 200 | 3,841.7 | 99.50% | 2.744% |
| 1,000 | 3,860.4 | 99.99% | 13.719% |

### 对象数量 vs 占用（两个不同的“最大”）

按**占用**最大的 3 个类型全部是巨型数组（单个 2–21 个实例）；按**实例数**最大的却是
小对象，且占用微不足道：

| 按实例数 | 实例数 | 占对象总数 | MB | 占堆字节 |
| --- | ---: | ---: | ---: | ---: |
| `System.String` | 484,208 | 10.18% | 56.6 | 1.47% |
| `NLCPG.Builder.OperationInventoryEntry` | 225,941 | 4.75% | 32.8 | 0.85% |
| `UsedFactRecord` | 223,592 | 4.70% | 8.5 | 0.22% |
| `UsedFactRecord[]` | 132,518 | 2.79% | 4.8 | 0.12% |
| `…NonErrorNamedTypeSymbol` | 126,287 | 2.66% | 4.8 | 0.12% |

按**单实例平均大小**排序则完全由巨型数组占据：
`Entry<…PendingEdgeKey,System.Int32>[]` **375.3 MB/个**（1 个）、
`Entry<StableNodeAnchor,NLCPGNode>[]` **186.3 MB/个**（1 个）、
`BufferedPendingEdge[]` **120 MB/个**（2 个）、
`System.UInt64[]` **110.7 MB/个**（21 个）。

**结论**：最大占用者是 `System.UInt64[]`（单类型 60.2%，21 个实例、单实例平均 110.7 MB）。
前 4 名都是巨型数组类型（单实例平均 110.7–375.3 MB，仅 1–21 个实例），
合计 **80.96%**；而实例数最多的 `System.String`（48.4 万个）只占 1.47%。
这些巨型数组直接落在 LOH，与“LOH 18.04 GiB 不降、committed 达物理内存 1.83 倍”
的内存墙结论互为印证。

### `System.UInt64[]` 是哪个数据结构（已归因）

**结论：它是 `DataFlowPass` 的到达定义位集（reaching-definitions bitset），
即下文的 `inSets` / `outSets` 数组。这不是推测——数组尺寸能精确反解出代码中的分配公式。**

`src/NLCPG/Builder/Passes/DataFlowPass.cs:613-621`：

```csharp
var flowNodes = plan.FlowNodes;
var wordsPerSet = BitSetWordCount(plan.FlowNodes.Length);      // = ceil(N/64)
var inSets  = new ulong[checked(flowNodes.Length * wordsPerSet)];   // ← 巨型数组
var outSets = new ulong[checked(flowNodes.Length * wordsPerSet)];   // ← 巨型数组
var incomingScratch = new ulong[wordsPerSet];                       // ← 小数组
var updatedScratch  = new ulong[wordsPerSet];                       // ← 小数组
```

N = 该方法分区的流节点数（`flowNodes.Length`）。每个节点用 N 个 bit 表示"哪些定义可达"，
于是单个数组有 `N × ceil(N/64)` 个 `ulong`。**这是 O(N²) 的规模。**

**尺寸反解证据**（样本 `0018-late-216s`，观测到 2 个 `>100M` + 2 个 `>1K` 的 `System.UInt64[]`）：

| 观测 | 数值 | 反解 |
| --- | ---: | --- |
| `>100M` 桶，2 个实例 | 每个 348,103,824 B | `(348103824-24)/8 = 43,512,975` 个 ulong |
| `>1K` 桶，2 个实例 | 每个 6,624 B | `(6624-24)/8 = ` **825** 个 ulong |

取 `wordsPerSet = 825`（由 scratch 数组直接读出），则 `N = 43,512,975 / 825 = 52,743`，
并满足两个独立的自洽条件：

- `ceil(52743 / 64) = 825` ✔ （正是 scratch 数组读出的 `wordsPerSet`）
- `52743 × 825 = 43,512,975` ✔ （正是大数组的元素数）
- 字节复核：`24 + 8 × 43,512,975 = 348,103,824` ✔ （正是 gcdump 报告的尺寸）

`.NET` x64 数组头为 24 字节（同步块 8 + 方法表 8 + 长度 8；本次数据里所有空数组的
`TypeSize` 实测都是 24，可独立验证），所以
**`348,103,824 = 24 + 8 × N × ceil(N/64)` 精确成立**。

更关键的是**同尺寸签名**：在 `0018-late-216s`，两个桶的“桶内均值”都恰好等于 `TypeSize`
（`696207648/2 = 348103824` ✔、`13248/2 = 6624` ✔），说明该时刻存在
**2 个完全相同的大数组 + 2 个完全相同的小数组**，正好对应代码里那 4 个数组：

| 代码 | 尺寸公式 | 计算值 | 落桶 |
| --- | --- | ---: | --- |
| `inSets` | `24 + 8 × N × wordsPerSet` | 348,103,824 B | `>100M` |
| `outSets` | `24 + 8 × N × wordsPerSet` | 348,103,824 B | `>100M` |
| `incomingScratch` | `24 + 8 × wordsPerSet` | 6,624 B | `>1K` |
| `updatedScratch` | `24 + 8 × wordsPerSet` | 6,624 B | `>1K` |

四个数组由同一个 `wordsPerSet = 825` 串起来，实例数（2+2）、尺寸、以及
`ceil(52743/64) = 825` 的关系**全部吻合**。

**分配点在仓库内唯一**：全 `src/` 中 `new ulong[` 的多元素分配只有
`DataFlowPass.cs:618-619` 这两行（`DominancePass.cs:49` 的 `BlockBitSet` 是
`(capacity+63)/64`，即 O(N)，量级差 N 倍，与 100 MB 级实例不符）。

**其他采样点的同一指纹**（N 由该桶 `TypeSize` 反解；实例数取自同桶）：

| 样本 | 桶内实例数 | 桶 `TypeSize` | 解出 N | `ceil(N/64)` | 该桶合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `0016-late-156s` | 8 | 155,898,072 | 35,303 | 552 | 2,135.5 MB |
| `0016-late-156s` | 2 | 98,947,224 | 28,110 | 440 | 188.7 MB |
| `0017-late-186s` | 6 | 191,003,512 | 39,076 | 611 | 1,838.1 MB |
| `0018-late-216s` | 2 | 348,103,824 | 52,743 | 825 | 664.0 MB |

即 **Version4（Terraria）里存在数万流节点的单个方法**——`Terraria\WorldGen.cs`
有 73,355 行、`NPC.cs` 有 79,688 行，这类巨型方法正好能产生 3.5 万–5.3 万个
`IOperation` 流节点，与反解出的 N 量级一致。

口径说明：`0016` 与 `0017` 的 `>100M` 桶内均值（279.9 / 321.2 MB）**不等于**该桶 `TypeSize`
（148.7 / 182.2 MB），说明这两个桶内混装了不同尺寸的数组。因此这两行的 N 是
“**桶中至少存在一个这么大的方法**”（下界），不是唯一确定值；只有 `0018` 由于
均值恰等于 `TypeSize`，其 N=52,743 是**精确**的。这不影响结论方向——三处反解都落在
同一个公式上，量级一致。

**为什么它是最大占用者**：因为该分配**没有任何上限**。`wordsPerSet` 随 N 线性增长，
数组元素数随 N² 增长，而 `MaxFlowNodesPerMethod` 默认为 `int.MaxValue`
（`NLCPGDataFlowOptions.Unbounded`），本次 `nlissn.yml` 也未设置预算字段，所以
N 多大就分配多大。N=52,743 时单个数组就是 332 MB，两个就是 664 MB，全部直接进 LOH。

> 归因强度：数组尺寸的算术自洽是**强证据**（同时满足三个独立等式，且分配点唯一）。
> **更正（2026-09-23）**：本报告其实**采集到了**分配调用栈（见下文"分配调用栈归因"），
> 但那份 trace 的窗口是**前 60.03 s**，而此处反解用的样本是 t=216 s，**不在窗口内**，
> 所以本节的归因依旧**没有被分配栈直接证实**，仍属强证据而非直证。
>
> 注意 `TypeSize` 是 `dotnet-gcdump` 对一个桶给出的**代表尺寸**，并非桶内每个实例的尺寸
> （例如 `0001` 的 `System.Int32[] >10K` 桶：16 个实例、均值 36,141 B、`TypeSize` 24,836 B）。
> 上表的反解之所以成立，是因为 `0018` 的两个桶恰好“均值 == `TypeSize`”，
> 即桶内实例同尺寸；对均值不等于 `TypeSize` 的桶（如 `0016` 的 `>100M` 桶）
> 不能直接做这种反解。

### 跨样本稳健性

上表是单点快照。为排除“只在某一时刻如此”，下表给出**排除 3 个截断样本后**
每类型在各采样点的**最大占用**（不同样本混合，仅用于排序）：

| # | 类型 | 最大 MB | 出现在 |
| ---: | --- | ---: | --- |
| 1 | `System.UInt64[]` | 2,324.3 | `0016-late-156s` |
| 2 | `NLCPG.Builder.Passes.InterproceduralDataFlowPlan[]` | 1,515.6 | `0019-late-246s` |
| 3 | `System.String` | 1,155.2 | `0002-early-8s` |
| 4 | `PendingEdge[]` | 589.5 | `0019-late-246s` |
| 5 | `Entry<…PendingEdgeKey,System.Int32>[]` | 375.3 | `0016-late-156s` |
| 6 | `NLCPG.Model.NLCPGEdge[]` | 257.6 | `0013-early-107s` |
| 7 | `BufferedPendingEdge[]` | 240.0 | `0019-late-246s` |

口径提醒：`System.String` 的全程峰值（604 万实例 / 1600.2 MB）落在 3 个**被截断**样本上，
故此处用未截断样本的最大值 1,155.2 MB；两者的差异全部来自采样截断，不是真实波动。

## 测量开销边界

这些是本次运行的实测约束，记录在此以免后续误读数据：

1. **3 s 节奏不可达**。早期实际落点为
   `0, 8, 21, 52, 58, 63, 69, 74, 80, 86, 93, 99, 107, 119` 秒。
   单次 gcdump 的实际占用可由 dump 文件落的 mtime 间隔复现，且随堆增长显著恶化：

   | 区间 | 相邻 dump 写入间隔 |
   | --- | --- |
   | 早期（`0002`–`0015`，堆约 0.2–3.4 GiB） | 5.3 – 12.1 s |
   | 中期（`0016`–`0019`，堆约 3.3–3.9 GiB） | 29.1 – 33.2 s |
   | 后期（`0025`–`0028`，堆 ≥18 GiB LOH） | 65.5 – 186.4 s |

   另一种可复现口径是“计划时刻 vs 实际写入完成时刻”的滞后：21 次成功采样累计滞后
   **209 s**，占 1042 s 窗口的 20.1%（最大单次滞后 70.6 s，出现在 `0028`）。
   该滞后≈单次采样占用（等待 + dump + 落盘），是目标进程被阻塞时长的**上界**；
   16 次失败尝试未计入，其中 2 次已确认命中 `-t 30 s` 超时。
   采样会强制一次完整 Gen2 GC 并暂停目标进程，因此高密度采样会实质扰动被测运行；
   这是“按 3 s 采集每类型统计”的固有代价。

   注：逐次 `sampleMs` 在 harness 中仅经 `Write-Host` 输出、**未落盘**
   （`Logs/gcdump-*.out.txt` 只有 `Finished writing N bytes`），
   因此报告只采用上表这种可由磁盘文件时间戳复现的证据。
2. **37 次尝试中 16 次失败**，harness 逐次记录并继续：
   - `collectRootIndexException` 13 次（`System.ApplicationException: RootIndex not set`，
     来自 `Graph.AllowReading`）——全部落在后段（采样钟 t=276 s 起，分析钟约 337 s），
     首次失败时 `runtime.log` 自报托管堆约 13.7 GiB，失败区间内堆在 8.2–23.7 GiB 波动。
     失败与成功**交错**出现（同样的高堆下 `0025/0027/0028` 仍产出了 dump），
     所以这不是一个硬性大小阈值，而是 `dotnet-gcdump` 在高堆下的不稳定行为；
   - `collectFailed` 2 次（`-t` 默认 30 s 超时）；
   - `noDump` 1 次（运行被停止时该采样仍在进行）。
3. **3 个采样点报告恰好 10,000,000 个对象**（`0025/0027/0028`），几乎确定为**截断上限**
   而非实测值：三个相隔 90 s 的不同时刻恰好等于同一整数，且每点自报堆仅 1.85 GiB
   （1,988,031,542 / 1,988,027,536 / 1,988,026,544 B，三点相差不到 5 KB），
   而前一个未被截断的采样点 `0019` 是 4,299,388 个对象 / 3.28 GiB。
   同一时刻（分析钟 t≈488/548/578 s）`runtime.log` 自报托管堆为 20.6–23.1 GiB。
   因此这 3 个采样的图只覆盖被截断的子集，其“按类型字节合计”虽然仍与该采样自报的
   `GC Heap bytes` 精确相等（不变式成立），但**绝对值不可用于总量结论**，
   只能用于“已捕获子集的类型占比”。上文 `System.String` 的 604 万实例峰值即取自这 3 点，
   故标为下界。
4. **gcdump 活堆远小于运行日志堆**（峰值 3.77 GiB vs 23.65 GiB）：gcdump 先强制 Gen2 回收，
   只统计存活对象；`runtime.log` 的 `heapBytes` 是未回收的已分配堆。两者不可直接比较。
   加上时钟偏移后，同一时刻两者的比值在 8%–107% 间大幅波动，原因就是采样时点相对于
   GC 周期的相位不同。
5. **trace 期间类型名不可用**（见“采样方案”），trace 仅覆盖前 61.12 s（46.4 MB）。
6. 本次运行由用户主动停止。分析时钟上有效证据的终点是 `Counters/` 的最后一个采样点
   （分析起点后 1042 s，约 17.4 分钟）。`runtime.log` 只有 65536 字节（恰为 64 KiB），
   其可读尾部停在 `elapsedMs=786602`（13.1 分钟）——因为进程被强杀，
   `TextLogFileSink` 的缓冲区尾部没有落盘。因此日志尾部**丢失**，不能据此判断分析终点。
   `runtime.log` 无 `evt=completed`，`Run/artifacts/` 下没有任何分析产物。
   harness 的收尾步骤因此未执行，`gcdump-index.csv` 与
   `run-summary.reconstructed.json` 是由磁盘证据重建的（标有 `reconstructed: true`）。

## dotnet-trace 独占热点

`Trace/allocation.nettrace`（`--profile gc-verbose`，前 61.12 s）的
`dotnet-trace report topN` 前 5 名：

| 函数 | Inclusive | Exclusive |
| --- | ---: | ---: |
| `RuntimeMethodInfo.GetParameters()` | 7.36% | 7.36% |
| `[…__Canon].Resize(int32,bool)` | 6.45% | 6.45% |
| `Enumerable.ToDictionary(...)` | 20.03% | 5.35% |
| `[…].<RunDependencyGraphAsync>b__7()` | 26% | 4.58% |
| `GC.GetGCMemoryInfo(GCKind)` | 3.89% | 3.89% |

反射取参（`GetParameters`）与字典重建（`ToDictionary`/`Resize`）合计构成主要独占开销，
指向 per-node 的反射式元数据访问与字典扩容。这是后续优化的第一顺位线索；
它与“平均仅 1.1 核”一起说明当前瓶颈是单线程开销而非并行度。

**口径限制（勿过度解读）**：`topN` 统计的是**调用栈时间占比**，不是分配字节占比。
所以“`Resize` 占 6.45%”只能说**这些代码路径消耗 CPU 时间**，**不能**推出“位集数组的
分配占了多少内存”。

> **更正（2026-09-23）**：这里原先接着写“使得本文无法用 trace 验证分配构成”——
> **该结论错误，已撤回**。`topN` 报告确实不含分配构成，但**同一份 `.nettrace`
> 含 `GC/AllocationTick` 事件**（`--profile gc-verbose` 启用了 `GC(0x1)` 关键字），
> 用 `TraceEvent` 解析即可得到按类型的分配量与**完整调用栈**。
> 实测该文件有 61,432 个带栈的 `AllocationTick`（详见"采样方案"与下方归因表）。

### 分配调用栈归因（60.03 s 窗口）

从 `allocation.nettrace` 解析 `GC/AllocationTick` 得到。窗口内总量 **11.30 GiB**，
其中 `System.String` **2.215 GiB（占 19.60%）**、22,210 个 tick（占全部 tick 的 36.2%）——
即字符串是**分配字节第二、分配次数第一**的类型。字符串内部按栈顶
`NLCPG.*`/`NLISSN` 帧归因：

| 栈顶帧 | 采样字节 | 占字符串 churn | tick 数 | 来源 |
| --- | ---: | ---: | ---: | --- |
| `NLCPGBuilder.NodeSortKey` | **1.632 GiB** | **73.65%** | 16,480 | `NLCPGBuilder.cs` L1053-1056 |
| `NLCPGCallSiteContext.ToContextId` | 0.322 GiB | 14.52% | 3,244 | `NLCPGCallSiteContext.cs` L11 |
| `PostRewriteDiagnostics.BuildStableDiagnosticKey` | 0.042 GiB | 1.90% | 427 | — |
| `PostRewriteDiagnostics.GetErrorDiagnostics` | 0.037 GiB | 1.68% | 375 | — |
| `NLCPGBuilder.SymbolId` | 0.036 GiB | 1.63% | 363 | L1683-1687 |
| `ComposeTypeFullName` | 0.030 GiB | 1.37% | 306 | L1695-1700 |
| `RuleNodeId.get_Value()` | 0.012 GiB | 0.55% | 123 | — |
| `NLCPGEdgeLabel.get_StableKey()` | 0.010 GiB | 0.43% | 97 | L44-51 |
| `ComposeMethodSignature` / `ComposeMethodFullName` | 0.019 GiB | 0.86% | 191 | — |
| `CreateSyntaxNode` | 0.009 GiB | 0.40% | 88 | — |
| （无 NLCPG 帧） | 0.023 GiB | 1.05% | 79 | 运行时/BCL 内部 |

字节加权与次数加权给出的份额一致（73.65% vs 74.2%；14.52% vs 14.61%），
说明归因**不依赖单一大对象采样**，是稳健的。

窗口内**总分配第一名**是 `InterproceduralDataFlowPlan[]`：**3.275 GiB / 28.97% / 5,541 tick**，
其中 `List<InterproceduralDataFlowPlan>.set_Capacity` 1.885 GiB、`OrderedIterator.ToArray()` 0.846 GiB、
`AddWithResize` 0.521 GiB。**它与 `NodeSortKey` 出自同一个 pass**（`RunInterproceduralDataFlowPass`），
所以该 pass 才是值得整体优化的单位，而不是单独一个 `NodeSortKey`。

> ⚠ **两个必须同时引用的口径边界**：
> 1. **分母**：`73.65%` 是**占字符串**的比例。同一笔 1.632 GiB 占
>    窗口内全部字节 **14.44%**、占全运行 93.73 GiB **1.74%**。
> 2. **窗口**：这份 trace 只有 **60.03 s / 1042 s（5.8%）**，且不能线性外推
>    （峰值分配速率 3.24 GiB/s，长尾 ≈0.03 GiB/s）。
>    `InterproceduralDataFlowPlan[]` 在 t=246 s 的 gcdump 里仍有 1,515.6 MB，
>    说明该 pass **长尾仍在跑**，但它在长尾的分配占比**未被本窗口测量**。

## 为什么 CPU 占用率很低

结论：**低 CPU 不是并行度不足，而是“并行窗口极短”与“长尾本身串行”两件事叠加**。
六个 DOP 与三个开关确实生效（`evt=started` 已证实），但工作量没有被切成可并行的形态。

1. **进程平均仅 1.10–1.24 / 16 核，峰值 4.92 / 16 核**。`Counters/` 全程 CPU 合计
   1146.8 s / 1042 s 窗口 = 1.10 核；`Threads/` 的 `processCpuPercent` 均值 7.77%
   （约 1.24 核）、峰值 30.74%（约 4.92 核，t=213.8 s）。峰值也只用掉 16 个逻辑处理器的 31%。
2. **线程池队列从未积压**：`tpPending` 在全部 124 个采样点上都是 0。没有“活排队等线程”，
   因为工作根本没提交到队列，是主流程自己在串行跑；`tpThreads` 峰值 25，
   而同期 `threadsRunning` 只有 1–2。
3. **并行窗口很短**，三个独立口径互相印证：
   - `tpCompleted` 速率由 t=0→192 s 的约 771 项/s 降到 t=192→787 s 的约 0.54 项/s，
     `tpThreads` 由 17–25 降到 4–5；
   - `dotnet.monitor.lock_contentions` 的 20,494 次争用**全部落在 17:32:10–17:35:14**
     （分析钟 t≈7–191 s），此后为 0；
   - `Threads/` 在 t>192 s 的 166 个采样点中，**28 个点 `threadsRunning=0`、141 个点 ≤1**，
     `threadsWait` 中位数 17。

   以 1042 s 窗口计，**约 82% 的运行时长与并行度无关**，而这段仍贡献了 91.7 GiB 中的 55.5 GiB。

   一处修正：CPU 峰值（4.92 核、`threadsRunning=5`）出现在 **t≈211–229 s**，略晚于
   `tpCompleted` 的拐点，说明 192 s 后还有一次约 20 s 的 5 线程爆发。它不改变“长尾串行”的判断。
4. **低 CPU 不能全归给 GC 暂停**。暂停合计 318.8 s（占 30.6%），但把暂停期按 1 核扣除后，
   非暂停期仍有 828 s CPU / 721 s 墙钟 = **约 1.15 核**。即使 GC 完全不暂停，
   该负载的并行度也上不去——瓶颈在串行代码路径本身，与 `dotnet-trace` 的独占热点
   （`RuntimeMethodInfo.GetParameters()` 7.36%、`Enumerable.ToDictionary`/`Resize`）一致。

因此“提高 DOP”对总时长几乎没有杠杆：并行度只在最前约 3 分钟内起作用。

## 是否撞到内存墙

结论：**是，撞到了；但墙不是“托管堆不够大”，而是 LOH（大对象堆）**，
且已越过物理内存边界、发生换页。

### 实测读数

| 证据 | 数值 | 来源 |
| --- | ---: | --- |
| 物理内存 | 13.86 GiB | `Win32_ComputerSystem` |
| `committed_size` 峰值 | 25.32 GiB | `Counters/` |
| `privateBytes` 峰值 | 24.5 GiB | `Threads/` |
| GC 堆峰值（gen0+1+2+loh） | 25.31 GiB | `Counters/` |
| `heap.size[loh]` 峰值 | 18.04 GiB | `Counters/` |
| LOH 碎片峰值 | 704 MB（均值 313 MB） | `Counters/` |
| 工作集峰值 | 8.27–8.38 GiB（三处口径） | 见下注 |
| pagefile `PeakUsage` | 9424 MB | `Win32_PageFileUsage` |

四条关键读数：

1. **GC 堆 ≈ 全部提交内存**：25.31 GiB vs 25.32 GiB。提交内存几乎全是托管堆，
   其中 LOH 占峰值份额的 71.3%。
2. **LOH 长期停在 18 GiB 不降**：自 17:38:06 起有 **136 个采样点** `LOH ≥ 17.5 GiB`
   （峰值 18.04 GiB，为物理内存的 1.3 倍）。LOH 不压缩、只在 Gen2 回收，
   碎片峰值 704 MB 说明回收后仍留下大量不可用空洞。
3. **换页的直接签名**：t=325–334 s 间 `workingSetBytes` 由 **6,543 MB 塌到 407 MB**，
   而同期 `privateBytes` 仍停在 **14,421 MB**，随后工作集以约 **100 MB/s 线性爬回**
   （407→690→1,032→1,263→1,476→1,838 MB）。`Counters/` 的 `working_set` 在同一窗口
   给出相同形状（3,282→236→349→535→729→881 MB）。私有内存不变而工作集被清空，
   是操作系统裁剪工作集、把页面写入 pagefile 的行为。
4. **堆/工作集比值极端升高**：`heapBytes / wsBytes` 由早期 0.4–0.9 升到
   t=334 s 的 **38.6**（堆 13.72 GiB、工作集仅 0.36 GiB，即塌陷谷底），
   以及 t=729 s 的 **19.2**（堆 23.64 GiB、工作集 1.23 GiB）
   ——托管堆的绝大部分**不在物理内存里**。

### 内存墙如何导致低 CPU

- **内核态 CPU 占 33.6%**（system 385.5 s / 总计 1146.8 s）；长尾段更高：
  system 350.2 s vs user 556.2 s = **38.6%**。内存管理（缺页、页面回收）是内核时间的主要来源。
- **GC 暂停集中在长尾**：318.8 s 总暂停中 **282.9 s（88.7%）**发生在 t>192 s，
  正是堆最大且已开始换页的区间；最大单次间隔 48.63 s。
- **分配形态是高流失**：累计分配 91.7 GiB，而 gcdump 存活堆峰值仅 3.77 GiB
  （即 **95.9% 是垃圾**）；峰值分配速率 3.24 GiB/s（17:37:56，2 s 间隔）。
  这些垃圾主要是 LOH 巨型数组，只能靠昂贵的 Gen2 回收。
- 在按单实例尺寸分的 LOH 桶里，峰值样本 `0016` 的 **91.8%** 堆字节
  （3,545.9 MB / 3,860.9 MB，65 行）落在 `>100K` / `>1M` / `>10M` / `>100M`
  四个桶（单实例 >100 KB）。这是**下界**：85–100 KB 的 LOH 对象会被归入 `>10K` 桶而不计入。
  单实例 >100 MB 的对象在 9 个采样点出现；`0016` 的 8 个 `System.UInt64[]` 落在 `>100M` 桶，
  合计 2,135.5 MB（桶内均值约 267 MB/个，`TypeSize` 字段给出 155,898,072 B ≈ 148.7 MB，
  两个口径不同：前者是桶内平均占用，后者是 `dotnet-gcdump` 报告的单实例尺寸）。
  该类型在 `0016` 的 `>10M` 桶另有 2 个实例、合计 188.7 MB。

因果链：**大数组 → 直接进 LOH → LOH 不压缩且回收昂贵 → 堆涨到 25 GiB（物理内存的 1.83 倍）
→ 操作系统换出页面 → 线程阻塞在等页面/等 GC → `threadsRunning` 长期为 1、CPU 仅约 1.1 核**。
低 CPU 在这里是内存墙的**症状**，而非并行度问题。

### 代码级根因

分配点与归因证据见上文“`System.UInt64[]` 是哪个数据结构（已归因）”：
`DataFlowPass.cs:618-619` 的 `inSets` / `outSets`，规模 **O(N²)**。

这里只补两点：

**为什么没有上限**：`NLCPGDataFlowOptions.MaxFlowNodesPerMethod` 默认
`int.MaxValue`（`Unbounded`），本次 `nlissn.yml` 未设置任何预算字段，所以 N 多大就分配多大。
`wordsPerSet` 随 N 线性增长，数组元素数随 N² 增长：

| 流节点 N | 单个数组 | `inSets`+`outSets` | 是否 LOH |
| ---: | ---: | ---: | --- |
| 1,000 | 0.12 MB | 0.24 MB | 是 |
| 2,000 | 0.49 MB | 0.98 MB | 是 |
| 10,000 | 12.0 MB | 24.0 MB | 是 |
| 30,000 | 107.3 MB | 214.7 MB | 是 |
| 52,743（`0018` 实测） | 332.0 MB | 664.0 MB | 是 |
| 100,000 | 1,192.5 MB | 2,384.9 MB | 是 |

LOH 阈值 85,000 字节 ⇒ **N ≥ 818 时单个数组即进入 LOH**（两个数组是独立分配，
判定按单个数组）。Version4 的巨型方法 N 达 2.8 万–5.3 万，因此单次分配就是数十至数百 MB。

**为什么无法靠 Gen0 回收**：这些数组 ≥85 KB，直接进 LOH；LOH 不压缩、只在 Gen2 回收，
于是它们抬高的是 `committed`（25.32 GiB）而非 `working set`，与“堆/工作集比值达 38.6”
的换页现象直接对应。

### 如何优化 `System.UInt64[]`

先给结论：**根因是位宇宙选错了**——位集按 `N`（流节点数）开位，但只有**定义节点**
才能置位。把位宇宙压到 `D`（定义数）能把 `inSets`/`outSets` 缩小约 **N/D 倍**
（`Terraria` 巨型方法里这个比例通常是几倍到十几倍），且不改变算法语义。

#### 为什么位宇宙可以压缩（关键结构事实）

`DataFlowPass.cs` 里**只有一处会置位**（L978）：

```csharp
output[nodeOrdinal / 64] |= 1UL << (nodeOrdinal % 64);
```

而它只在 L657 的条件成立时才执行：

```csharp
if (definitionFactsByOrdinal[nodeOrdinal] is { } definedFact)   // L657
```

也就是说，**bit 只会落在 `definitionFactsByOrdinal[ordinal] != null` 的 ordinal 上**。
其余 `N - D` 个流节点（字面量、二元表达式、条件表达式……）**永远不会有 bit 被置起来**，
`IsBitSet`/`EnumerateSetBits` 在自己身上永远查不到东西。

但当前代码仍为每个流节点预留了 `ceil(N/64)` 个 word。`Terraria` 的巨型方法里
`D << N`（几万个 operation 里定义语句只占少数），这个浪费就是 5–10 倍。

**压缩方式**：给每个流节点建一张 `nodeOrdinal -> definitionOrdinal` 的映射表
（`int[N]`，`-1` 表示非定义节点），位集宽度改为 `wordsPerDefinition = ceil(D/64)`，
`ApplyDefinitionTransfer` 里置位时写 `definitionOrdinals[nodeOrdinal]`。
`IsBitSet`/`EnumerateSetBits` 的调用点（L698、L716）本来就已经在遍历集合后
用 `flowNodes[ordinal]` 反查节点，只需把反查表从 `flowNodes` 换成
`definitionOrdinalToNode[]` 即可。

实测收益（`inSets`+`outSets` 合计，用本次反解出的 N）。收益本质上就是 **≈ N/D**
（`ceil(N/64)` 对 `ceil(D/64)`）：

| 方法 N | 现状 | D=N/2 | D=N/4 | D=N/10 |
| ---: | ---: | ---: | ---: | ---: |
| 52,743（`0018`） | 664.0 MB | 332.0 MB（2×） | 166.6 MB（4×） | 66.4 MB（10×） |
| 39,076（`0017`） | 364.3 MB | 182.2 MB（2×） | 91.2 MB（4×） | 36.4 MB（10×） |
| 35,303（`0016`） | 297.4 MB | 148.7 MB（2×） | 74.3 MB（4×） | 29.7 MB（10×） |
| 28,110（`0016`） | 188.7 MB | 94.4 MB（2×） | 47.2 MB（4×） | 18.9 MB（10×） |

**口径诚实说明**：本次运行**没有落盘 `DefinitionCount`**，所以真实的 `D` 未知，
上面的倍数按 `D/N` 比例给出，不是实测值。要把它变成实测结论，只需把
`NLCPGDataFlowMethodMetrics` 的 `DefinitionCount`/`FlowNodeCount` 写进运行产物
（字段已存在，仅未持久化）。但方向是确定的：只要 `D < N`（巨型方法里定义语句必然
远少于全部 operation），就一定缩。极端情形 `D = N`（每个节点都是定义）时无收益也无损失。

而且 `D` **已经被算出来了**——`definitionFactsByNode.Count` 就是它，现在只是传给
`CreateDataFlowMetrics`（L588）记进 `DefinitionCount` 而没用于分配。

#### 更省一档：`inSets` 在 fixpoint 里根本没被读

`inSets` 的读写点只有 4 处：

| 行 | 操作 | 位置 |
| ---: | --- | --- |
| L618 | 分配 | — |
| L655 | **写**（`CopyBitSet(inSets, nodeOffset, incomingScratch, ...)`） | fixpoint 循环内 |
| L689 | 读（`IsBitSetEmpty`） | fixpoint **之后** |
| L698 / L716 | 读 | fixpoint **之后** |

fixpoint 循环（L639–L681）内部只用 `outSets` 传播、用 `incomingScratch`/`updatedScratch`
做暂存；`inSets` 全程**只写不读**，直到循环结束后（第四阶段，L682 起）才开始读。

所以 `inSets` 不必是 `N × words` 的整块矩阵——两种改法：

1. **只为被读到的节点存**：第四阶段只遍历 `plan.OrderedOperations`（L685），
   即只对 operation 节点读 `inSets`。可以改成在循环内把命中节点的 in 集
   写进 `Dictionary<int, ulong[]>`，或干脆在 fixpoint 收敛后**重放一遍**
   计算各 operation 节点的 in 集（只算需要的那些），省掉整块矩阵。
2. **退一步的保守改法**：至少把 `inSets` 从 `N` 行缩到 `operationNodes.Length` 行。

只做 (1) 的一项，`inSets`（当前占两个数组中的一半，如 `0018` 的 332 MB）就能降到与
"被读节点数 × wordsPerDefinition" 同量级。

#### 落地与实测（2026-09-22，两条优化均已实现）

上文的倍数是**按假定 `D` 的推算**。两条优化现已落地，下面是同输入、同配置、
同 runtime 的**实测**对比。实现见 `docs/plans/2026-09-22-dataflow-bitset-compaction-design.md`
与执行计划 `…-execution.md`：

- **优化①（位宇宙 N→D）**：`DataFlowPass.cs` 现按 `BitSetWordCount(definitionNodes.Count)`
  分配位宽，并建 `definitionOrdinals`（`int[N]`，`-1` 表示非定义节点）与
  `definitionNodes`（`NLCPGNode[D]`）两张映射表；`ApplyDefinitionTransfer` 按定义序数置位。
- **优化②（消除 `inSets`）**：`inSets` 整块矩阵已删除；fixpoint 只维护 `outSets`，
  第四阶段按需重算 `in = OR(out[p] for p in preds)`（`plan.Predecessors` 已缓存）。
- **溢出边界**：位集长度改用 `long` 计算，`N × ceil(D/64) > int.MaxValue` 时按
  `FlowNodeLimitExceeded` 走既有预算策略，而不是抛 `OverflowException` 打断整次构建。

**测量方法**：同一份 `Build/src/Debug/net10.0/NLCPG.dll` 之外的唯一变量是下述两行——
对照组的做法是临时把位宽改回 `ceil(N/64)` 并在 fixpoint 内恢复 `inSets` 物化，
图输出不变；`ulong[]` 的分配字节数 `24 + 8×元素数` 已用运行时实测校验（见下）。
`GC.GetTotalAllocatedBytes(precise: true)` 取本次构建的分配总量，`DOP=1`、
构图为 `NLCPGBuilderOptions.CreateDefault()`。

| 输入 | 方法数 | 基线分配总量 | 优化后分配总量 | Δ分配 | 位集（`inSets`+`outSets`） | 倍数 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `Terraria/Main.cs` | 155 | 2,554,658,152 B | 2,461,091,024 B | **−93.6 MB** | 94.9 → 6.2 MiB | **15.36×** |
| `Terraria/Player.cs` | 474 | 4,215,390,512 B | 4,171,791,552 B | **−43.6 MB** | 45.1 → 2.0 MiB | **22.56×** |
| `Terraria/WorldGen.cs` | 690 | 17,124,549,376 B | 16,045,139,360 B | **−1,079.4 MB** | 1070.2 → 41.3 MiB | **25.93×** |
| `Terraria/NPC.cs` | 373 | （未测，见下） | 见下 | — | 5419.5 → 181.8 MiB | **29.81×** |

单个最大方法的收益（同一次实测）：

| 方法 | N | D | 位宽 旧→新 | 该法位集 |
| --- | ---: | ---: | --- | ---: |
| `Terraria.NPC.AI()` | 122,536 | 7,451 | 1915 → 117 | 3,754,503,088 B → 114,693,720 B（**32.74×**）|
| `Terraria.WorldGen.AddPasses()` | 61,630 | 4,664 | 963 → 73 | 949,595,088 B → 35,991,944 B（**26.38×**）|
| `Terraria.Main.Initialize_TileAndNPCData1()` | 12,821 | 2,105 | 201 → 33 | 41,232,384 B → 3,384,768 B（**12.18×**）|

**`D/N` 实测分布**（这是全部收益的来源，此前只有推算）：
`WorldGen.cs` p50 = 0.0757、`NPC.cs` p50 = 0.0606、`Player.cs` p50 = 0.0779、
`Main.cs` p50 = 0.0965。即巨型方法里**定义数只占流节点的 6–10%**，验证了
`D << N` 的前提。同时 `D = 0` 的方法真实存在（`WorldGen.cs` 28 个、`NPC.cs` 91 个、
`Player.cs` 76 个、`Main.cs` 28 个），此时位宽下界仍为 1 word，行为不变。

**构图耗时（检查优化②的重算是否引入 CPU 回退）**，各测 3 次：

| 输入 | 基线（原实现）中位 | 优化后中位 | 判定 |
| --- | ---: | ---: | --- |
| `Main.cs` | 11,182 ms | 11,381 ms | +1.8%，在噪声内 |
| `Player.cs` | 19,180 ms | 19,029 ms | −0.8%，在噪声内 |

即按需重算 in 集**没有可测量的 CPU 代价**。

**换算常量的运行时校验**（使上表不只是纸面算术）：
`24 + 8×元素数` 与 `GC.GetTotalAllocatedBytes` 实测逐一吻合，
其中 `14,336,712` 个元素 → `114,693,720 B`，正是 `NPC.AI()` 压缩后的位集大小：

```
elements   predicted(B)      actual(B)  match
       1             32             32     OK
      64            536            536     OK
     117            960            960     OK
   1,915         15,344         15,344     OK
```

**口径边界（不得外推）**：

- `NPC.cs` 基线的 `inSets`+`outSets` 合计 **5,419.5 MiB**，在本机（14 GiB 物理内存）
  无法在同一进程内完成"基线 + 优化"两次构建，故其**分配总量未测**；
  表中 `NPC.cs` 的位集字节由该次实测的 `DefinitionCount`/`FlowNodeCount` 精确换算，
  不是猜测，但没有对应的进程级 Δ。
- 上表是**位集数组本身**与**进程分配总量**，不是完整 Version4 目录运行的 gcdump
  峰值。要得到与 `docs/benchmarks/data/version4-dop12-type-share-0016.csv`
  可直接相减的 `System.UInt64[]` 占用，仍需按本文"复现"节对整目录做 `GcdumpExactStats` 采样。
- 基线对照是**在本机临时改写源码后测量的**，不是历史提交；两次构建之间除位集宽度与
  `inSets` 物化外无其它差异，且最终代码已回退到不含任何对照代码的状态
  （`DataFlowPass.cs` SHA256 `278F1C00…E6557`）。

#### 不要用 `ArrayPool`（修正我此前报告里的建议）

我在上文旧版里写过"位集改为分块/复用（`ArrayPool`）"。**实测证明这个方向是错的**：

```
请求元素       new ulong[] 字节      ArrayPool.Rent 实得      放大
       825              6,624                  8,216        1.24x
43,512,975        348,103,824            536,870,936        1.54x
```

`ArrayPool<T>.Shared` 把请求向上取整到 2 的幂：`43,512,975 → 67,108,864`（2^26），
**单个数组反而从 348 MB 涨到 537 MB**。而且哨兵法（写入标记 → `Return` → 再 `Rent`）
验证出**池会长期保留这块内存**（大数组复用同一实例、标记仍在），
所以它既放大峰值、又把内存钉在池里不还给 GC。

若坚持用池，必须用 `ArrayPool<T>.Create(maxArrayLength: …)` 自定义实例限制上限，
但收益仍不如直接压缩位宇宙。

#### 预算配置：现状、为什么要做、以及为什么**不该现在做**（本节已修正）

现状确认：`NLCPGDataFlowOptions` 的三个预算**在生产代码里从无赋值点**——`src/` 中
`DataFlowOptions =` 出现 **0 次**（只有 4 处测试赋值），所以
`EffectiveDataFlowOptions` 恒为 `NLCPGDataFlowOptions.Unbounded`
（`NLCPGBuilderOptions.cs:63-64`）：`MaxDefinitionsPerMethod`、`MaxFlowNodesPerMethod`、
`MaxCandidateEdgesPerMethod` 全是 `int.MaxValue`。溢出机制完整
（`SkipMethod`/`FailBuild`、`NLCPGDataFlowOverflowReason`）却从未被启用。

但"接预算配置"这一条被我此前**高估了优先级**，理由逐条给出证据。

**（一）"不产出任何 DataFlow 边"是错的，损失范围被我夸大。**
`CommitCfgSensitivePartition`（L541-544）在溢出时确实直接 `return`，丢掉的是
**方法内** reaching-definition 候选边。但 `RunDataFlowPass` 在流水线之后
**无条件**调用 `AddCallArgumentAndReturnDataFlow`（L274），仍会产出
实参→形参、方法返回→调用点（L1087）以及属性访问器摘要（L1099+）的
`DataFlow` 边。所以溢出是**部分**损失，不是该方法的 DataFlow 边全灭。

**（二）更要紧的是：删除规则链路并不读方法内 DataFlow 边。** 逐一核过：

| 潜在消费者 | 结论 |
| --- | --- |
| 规则侧 `QueryStructureView`（`StageRuleContexts.cs:92`、`:119`） | 只用 `CpgRelationProfile.StructuralContainment` |
| `LocalDataFlow` / `BackwardSlice` / `ControlDependence` profile | `src/` 中**无调用点**，仅定义 + 测试使用 |
| `AnalysisSession.QueryRelation` / `QuerySliceBackward`（`:168-172`） | 仅定义与转发，**无任何调用者** |
| 规则文件引用 `NLCPGEdgeKind.` | **0 处**——规则不看任何具体边类型 |
| `MethodLinkageRoots` 入口可达性 | 用 `MethodLinkageGraphBuilder` 从 Roslyn 语法独立自建的方法图，非 CPG 边 |
| 唯一声明 `InterproceduralDataFlow` 的 `ExternalSummaryFlowPropagationRule` | 经 `CallFlowResolver` → `NLCPGFlowSummaryRegistry` 解析，**输入是 `Compilation` 而非 CPG 边**（`FlowSummaryResolverFactory.cs:13-16`） |

即：方法内 DataFlow 边在**当前**删除判定链路里没有被消费。
（`NLCPGGraph` 的 `GetGraphEdgesByKind` 暴露给了规则上下文，但无规则带具体 kind 调用它。）

**（三）但它确实是**对外契约面**，所以不能说"完全无影响"。**
`ProjectJsonExporter` 写出 `graph.Edges` 的**全部**边且带 `edge.Kind.ToString()`
（`ProjectJsonExporter.cs:498-513`），所以 `nlcpg-project-export` 的 JSON 里
能看到这些边；同时 `NLCPGBuilder.cs:664-667` 把
`MaxDefinitionsPerMethod`/`MaxFlowNodesPerMethod`/`MaxCandidateEdgesPerMethod`/
`OverflowBehavior` 一起编进**持久化指纹**——改预算值会强制重建已持久化的
catalog，不是免费的运行时旋钮。因此改预算应**一次性、有意地、按实测阈值**改，
不应投机先设一个数。

**（四）真正的阻塞点是"度量拿不到"，不是"阈值没定"。**
`_dataFlowMethodMetrics` 是**无条件**采集的（`DataFlowPass.cs:429` 每个 partition 都加，
`NLCPGBuilder.cs:432` 汇入 `DataFlowMethodMetrics`），并且已经映射进性能摘要
（`CpgPerformanceFactMapper.cs:41-49` → `PerformanceSummaryDocument.cs:194-205`，
含 `DefinitionCount`、`FlowNodeCount`、`WordsPerSet`、`OverflowReason`）。
但 `PerformanceSummaryPublisher.Publish` 只在 `CommandHost.cs:422-434` 的
**运行收尾**调用。本次运行虽然 `resolved-configuration.json` 里
`writePerformanceSummary: true`、`performanceMode: "diagnostic"`，
运行根目录下**根本没有 `Performance\` 目录、没有 `summary.json`**——
因为运行被中途终止（"先停止查看分析报告"）。

结论：**量化召回损失的度量早已接好、本次也已开启，缺的只是一次跑完的运行。**
所以"接预算配置"的正确前提不是"先量化召回损失"（循环依赖：不接预算就没法跑完，
跑不完就拿不到度量），而是**把度量提前落盘**（例如在 CPG 构建结束时增量写一次，
而非只在收尾写）。这一步不改任何语义、不改召回、不涉及阈值选择。

#### 还有一处硬边界：`int` 溢出

L618/L619 用 `checked(flowNodes.Length * wordsPerSet)`，两者都是 `int`。
`N × ceil(N/64)` 在 **N = 370,704** 时首次超过 `int.MaxValue`（2,147,483,647），
此时不会走到 `MaxFlowNodesPerMethod` 检查（默认无限），而是直接抛
`OverflowException`。而在 N = 370,703 时单个数组已需 **16 GiB** ——
物理内存 13.86 GiB 的 1.15 倍，必然换页或 OOM。
即：**即使把位宇宙压缩 10 倍，也需要给 N 设上限**，因为 O(N²) 迟早撞穿 `int`。

#### 顺带修正：峰值随 `cpgDop` 线性放大

`0016` 的 8 个 `>100M` 实例 = **4 个方法各自 2 个数组同时在飞**（10M 桶另有 1 个方法，
1K 桶的 10 个 `6,624 B` scratch 交叉印证 w=825、共 5 个方法有大位集）；
`0018` 则只有 1 个方法在飞（2+2 实例）。

| 单方法 N | 2 数组/方法 | ×4 并发 | ×12 并发 |
| ---: | ---: | ---: | ---: |
| 52,743 | 664.0 MB | 2,655.8 MB | 7,967.5 MB |
| 39,076 | 364.3 MB | 1,457.2 MB | 4,371.7 MB |
| 35,303 | 297.4 MB | 1,189.4 MB | 3,568.2 MB |
| 28,110 | 188.7 MB | 754.9 MB | 2,264.7 MB |

实测峰值 2,324.3 MB 与 ×4 一列量级吻合。**这就是"调高 DOP 反而更糟"的机制**：
内存上限随并发方法数线性放大，而 `DataFlowPass` 的批次执行
（`AnalyzeDataFlowWorkBatch`，L388）对每个 batch 顺序处理 item，
批次大小由 `CpgWorkBatchCostModel` 按**行数**切分（`Oversized` 单独成批，L114–120），
所以同时进入巨型位集相位的方法数 ≈ 工作线程数。
在压缩位宇宙之前，`cpgDop=12` 会让这个峰值直接乘以约 3。

#### 优先级（已按上节证据重排）

1. **把位宇宙从 N 压到 D**（≈ N/D 倍，纯收益，语义不变，无召回风险）。
2. **给 N 加上限以规避 `int` 溢出**（否则 N≥370,704 必然异常或换页；
   这一条是**崩溃防护**，与召回权衡无关，因此优先于预算）。
3. **利用 `inSets` 只写不读**（再省一半，改动略大）。
4. **把 DataFlow 度量提前落盘**（不改语义，是"接预算"的可度量前提）。
5. **接上预算配置**——**排在最后**：它是**召回契约变更**，且会改持久化指纹
   （`NLCPGBuilder.cs:664-667`）；在完成第 4 步拿到 `FlowNodeCount`/`DefinitionCount`
   分布之前设阈值等于盲设。另需注意当前删除链路并未消费方法内 DataFlow 边
   （上表），所以它眼下的收益是"保护对外导出契约与未来消费者"，而非"修复当前判定"。
6. ❌ **不要用 `ArrayPool`** 处理这几个大数组（实测 1.54× 膨胀 + 长期钉住）。

回归保护：`NLCPGPartitionedBuilderTests` 里已有 DOP 1/8/12/14/16 与 baseline 的
图等价断言（`AssertGraphsEqual`、`PreservesGraphsAcrossDegreesOfParallelism`），
压缩位宇宙后应保持全绿——这是"语义不变"的现成验证网。

> 与本文其他部分的措辞对齐：该归因是**基于尺寸反解的强证据**（见上文）。
> **更正（2026-09-23）**：本报告**确实采集了**分配调用栈（`--profile gc-verbose`
> 启用了 `GC(0x1)` 关键字，含 `GC/AllocationTick`；实测 61,432 个 tick 带完整栈），
> 详见下文"分配调用栈归因"表。但需注意：该 trace **只覆盖前 60.03 s**，
> 而 `System.UInt64[]` 的峰值出现在 t=216 s（`0018-late-216s`），
> **不在 trace 窗口内**，因此 `System.UInt64[]` 的归因**仍然只能靠尺寸反解**，
> 不能用这份 trace 直接证明。
> 上文的算法结论（位宇宙、`inSets` 只写不读、溢出边界、`ArrayPool` 膨胀）
> 均直接来自 `DataFlowPass.cs` 源码与本地实测，不依赖该归因。

### 关于 `PendingEdgeBuffer` 两个容器"预设容量"：不建议做

`NLCPGGraph.cs:838-839` 的两个容器确实没有预设容量：

```csharp
private readonly Dictionary<PendingEdgeKey, int> _ordinals = new();
private readonly List<BufferedPendingEdge> _items = new();
```

按倍增扩容，每次扩容都整体复制旧内容。看起来是明显的优化点，但**实测表明不值得做**：

**（1）它只影响主图的一个缓冲区，不是每个方法一个。** 三个 pass 的 `localGraph`
（`ControlFlowPass.cs:58`、`DominancePass.cs:399`、`ControlDependencePass.cs:77`）
只调用 `localGraph.AddNode(...)` 并读 `localGraph.Edges`（冻结集），
**从不调用 `AddEdge`**——因此它们的 `PendingEdgeBuffer` 始终为空。
原始报告可证：样本 `0016` 里 `BufferedPendingEdge[]` 只有
**1 个 240 MB 数组 + 1 个 24 B 空数组**。

**（2）元素大小与容量可被精确解出，且随文件变化 64 倍。**

这个图是**每个文档一个**（`NLCPGBuilder.cs:168` `NLCPGBuildContext.Create(..., filePath, ...)`），
所以容量取决于"当前文件有多少条边"，不是全局常量。用实测字节数可反解出唯一解：

| 容器 | 实测对象字节 | 解出的元素大小 | 解出的容量 | 校验 |
| --- | ---: | ---: | ---: | --- |
| `List<BufferedPendingEdge>._items` | 251,658,264 | 120 B | 2,097,152 = **2²¹** | 容量必须是 2 的幂 ✓ |
| `Dictionary<…>. _entries` | 393,481,888 | 136 B | 2,893,249 | 容量必须是**质数** ✓ |

两个解**各自独立地被证实**，不是拟合出来的：

- **字节反解的唯一性**：要求"列表容量是 2 的幂"与"字典容量是质数"两个约束
  由**同一个 key 大小**同时满足，穷举 40–168 B（8 字节对齐）只有 **120 B 一个解**。
- **源码字段布局独立复算**（按 CLR 对齐规则，与实测字节无关）：
  `StableNodeAnchor` = 7×4 = 28 B；`PendingEdgeKey`
  = 28+28 + kind(4) + [4 填充] + label(8) + `NLCPGContextId?`(16) + `NLCPGCallSiteContext?`(32)
  = **120 B**；`Entry` = hashCode(4)+next(4)+key(120)+value(4) → 8 对齐 = **136 B**。
  与字节反解**完全一致**。

由此反推**最大那个文件的边数**：列表容量 2,097,152 给上界 2,097,152、下界 1,048,577
（扩容发生在越过上一级容量之后）。**≈ 105 万–210 万条边（单个文件）**。

> 口径提示：字典容量（2,893,249）与列表容量（2,097,152）不相等，
> 这符合两者独立的扩容节奏（字典走质数阶梯、列表走 2 的幂），
> 不能反过来用"容量应相等"去校验；只能用**容量区间**夹逼真实条目数。
> 另注：字典容量不在 `ExpandPrime(3)` 递推阶梯上，说明该 `Dictionary` 不是从
> 默认容量逐级翻倍而来（可能走了含预分配的构造路径），故不据此收窄区间。

而各采样点解出的容量跨度极大：

| 采样点 | 列表容量 | 字典容量 |
| --- | ---: | ---: |
| `0008-early-74s` | 32,768 | 36,353 |
| `0015-late-125s` | 262,144 | 324,449 |
| `0012-early-99s` | 1,048,576 | 1,395,263 |
| `0016-late-156s` | 2,097,152 | 2,893,249 |

**最小 32,768 ↔ 最大 2,097,152，相差 64 倍。所以不存在一个合适的常量。**

**（3）扩容垃圾确实很小。** 倍增扩容的垃圾是几何级数，其和 ≈ **1× 最终数组大小**：

| 容器 | 最终数组 | 逐级丢弃之和 |
| --- | ---: | ---: |
| 列表 `_items` | 240.0 MB | ≈ 240 MB |
| 字典 `_entries` | 375.3 MB | ≈ 375 MB |

两者合计约 **615 MB**，对**单个大文件**而言，约合该次累计分配 91.7 GiB 的 0.66%。
（口径注：这是"一个大文件贡献的量"。全程总量 = 各文档之和，取决于 967 个文件里
大文件占多少；本产物没有 per-file 边数，**故不能把 0.66% 当作全程占比**。
但即使按最不利的"全部文件都和最大文件一样大"来放大，量级也在个位数百分比。）

**（4）更关键：常量预设会把"垃圾"变成"长期存活"，反而更糟。**
用观测到的最大值当常量（列表 2.1M、字典 2.9M），**若** 12 路并发文档同时各持一份：

```
12 × (240 MB 列表 + 375 MB 字典) ≈ 7.4 GB 长期存活
```

而它省下的垃圾只有 615 MB/文件。**即峰值内存会上升约一个数量级**——
这与"降低 LOH 压力"的目标正好相反。预设容量只有**精确等于实际需要**时才划算，
而实际需要如上所述在 64 倍范围内变化。

> **口径更正**：上面 7.4 GB 是"12 份同时存活"的**假想上界**。实测 gcdump 显示
> 该 `Entry[]` 的 `MaxInstanceCount = 1`（21 个样本），即**同一时刻只有 1 份大缓冲存活**。
> 所以 7.4 GB **不是实测值**，只用来论证"按最坏情况预留会显著抬高存活"这一方向性结论；
> 真实反噬量级应以"1 份"计，即约 615 MB 由垃圾转为长期存活。

**（5）构建时也拿不到估计值。** `PendingEdgeBuffer` 是字段初始化式构造的
（`= new()`），而 `NLCPGGraph` 构造时只知道可选的 `DeterministicNodeIdTable`，
它**不暴露 `Count`**（`DeterministicNodeIdTable.cs` 只有私有 `_ids` 与 `Create`）。
没有可推导的来源，只能新增配置面从外部传入。

**结论：不要为这两个容器设固定容量。** 若确实要做，正确形态是
**按文档推导 + 分批预留**（例如按语法节点数线性外推并设上限），
而不是一个常量；且收益量级只有个位数百分比的分配，并在 12 并发下可能反噬。
**它不是内存墙的解**——真正的量级差在 `System.UInt64[]`（60.2%）与
DataFlow 位集的 N/D 压缩（见上节）。

#### 已实施的修复：去重索引改用 `HashSet`

`_ordinals` 的 key 与 `_items` 的元素**字段完全相同**（都是 2 个 `StableNodeAnchor`
+ kind + label + contextId + callSiteContext），且 `int` value 从未被读取
（全仓库 grep 只有 `ContainsKey` 与 `Add` 两处），**它其实只被当集合用**：

| 容器 | 元素 | 单条边成本 |
| --- | --- | ---: |
| `List<BufferedPendingEdge>._items` | 120 B | 120 B |
| `Dictionary<PendingEdgeKey,int>._entries` | 136 B | 136 B |

**已改为 `HashSet<PendingEdgeKey>`**（`NLCPGGraph.cs:838`、`851-864`）：

```csharp
private readonly HashSet<PendingEdgeKey> _keys = new();
...
// 一次查找同时完成"判重 + 登记"，取代原先的 ContainsKey + Add 双查找
if (!_keys.Add(new PendingEdgeKey(...)))
{
  return;
}
```

收益有两项，**主要是 CPU 而非内存**。以下全部是**本轮实测**，不是推导。

**（1）CPU：去重路径快 1.36–1.5×**

原 `ContainsKey` + `Add` 对每条边查两次哈希，`HashSet.Add` 一次完成。用**布局等价的
120 B 键**（实测真实 `PendingEdgeKey` = 120 B）各插入 2,097,152 条做的微基准：

| 轮次 | `ContainsKey`+`Add` | `HashSet.Add` | 加速 |
| --- | ---: | ---: | ---: |
| 1 | 655 ms / 746.0 MiB | 437 ms / 703.4 MiB | **1.50×** |
| 2 | 569 ms / 746.0 MiB | 419 ms / 703.4 MiB | **1.36×** |
| 3 | 670 ms / 746.0 MiB | 484 ms / 703.4 MiB | **1.38×** |

这与 trace 里 `[…].Resize(int32,bool)` 的高占用同属"去重/扩容"路径。

**（2）内存：每条边省 8 B ⇒ 峰值约 22 MiB，单大文件累计约 43 MiB**

**此处修正本文早先的错误说法**：早先写的"去重索引只存 hash + 序号，可省 353 MB"
**是错的**——只存 hash 会在**哈希碰撞**时把两条不同的边误判为重复而丢弃，**改变图内容**。
正确做法是保留完整 key，省下的只是 `Dictionary.Entry` 中多出的 `int value` 字段。

用 `Unsafe.SizeOf` + `Marshal.OffsetOf` **实测**两种布局：

| 结构 | 实测字段偏移 | 实测大小 |
| --- | --- | ---: |
| `Dictionary<PendingEdgeKey,int>.Entry` | hashCode@0, next@4, **key@8**, **value@128** | **136 B** |
| `HashSet<PendingEdgeKey>.Entry` | HashCode@0, Next@4, **Value@8** | **128 B** |

> 命名更正：.NET 10 里 `HashSet<T>` 的嵌套结构叫 **`Entry`**，不是早先写的 `Slot`
> （那是旧版命名）。两者容量阶梯实测**完全相同**（`3,7,17,…,1395263,2893249`），
> 故换容器**不改变容量**，节省可安全按"每元素 8 B × 同一容量"计算。

即每条边省 **8 B**。分两个口径：

| 口径 | 计算 | 结果 |
| --- | --- | ---: |
| **存活**（观测峰值样本 `0016`，容量 2,893,249） | 393,481,888 − (24 + 2893249×128) | **22.07 MiB** |
| **累计分配**（含所有扩容轮次，累计/存活 = 1.931×） | 759,917,728 − 715,216,712 | **42.63 MiB** |

累计口径被上表微基准**独立交叉验证**：实测分配差 746.0 − 703.4 = **42.6 MiB**，
与算术值 42.63 MiB 吻合。

**占峰值堆的比例**：峰值样本堆合计 4,048,479,859 B（3,861 MiB），该数组占 9.72%；
改后节省 22.07 MiB = **峰值堆的 0.57%**。

> **存活实例数 = 1，不乘并发**：`gcdump-type-summary.csv` 中该数组
> `MinInstanceCount = 1, MaxInstanceCount = 1`（21 个样本、15 个样本出现），
> 峰值样本也只有 1 个实例。故上面的 22 MiB 就是**整个进程**的峰值节省，
> **不应按 DOP 12 再乘**。（旁证：`BufferedPendingEdge[]` 为 2 个实例 = 1 个大 + 1 个共享空数组。）

**语义不变的验证**（本轮实测，非推断）：

- `NLCPG` 构建 0 错误；`UnitTests` **117/117**；`ContractTests` **430/430**（串行）。
- 其中含各并行度的图等价断言（`…PreservesGraphsAcrossDegreesOfParallelism`），
  即"改动后各 DOP 产出的图仍与基线逐项相等"。
- 两者都用 `EqualityComparer<PendingEdgeKey>.Default`，`PendingEdgeKey` 是
  `record struct`（按全部字段值相等），去重语义与原先一致；原 `int` value 无人读取。

> **测试口径提醒（重要）**：`Run-TestTiers.ps1` **不禁用 xUnit 集合并行**，而本仓库
> 多个测试会跑真实目录构建、共享进程级状态。实测并行时 `ContractTests` 有 6–17 项
> **随机**失败，**串行则 430/430 全绿**；`HostTests` 的 7 项既有失败
> （见 `Context/progress.md:33`）在改动前后逐项相同。**判定本区域是否回归必须用串行模式**，
> 否则会把并行干扰误判为回归（本轮一度出现 6→11 的假差异）。
>
> **串行模式的正确开关（2026-09-23 实测确认，含一个易踩的坑）**：写一个
> `xunit.runner.json`（内容 `{ "parallelizeTestCollections": false }`），
> **必须放在输出目录** `Build\test\Debug\net10.0\`（`Build/` 已 gitignore，用完删除）。
> 放入后全量 `ContractTests` **440/440 全绿**、`Run-TestTiers.ps1 -Fast` 退出码 0。
>
> ⚠️ **放源目录 `tests/NLISSN.ContractTests/` 不生效**：该 csproj **没有**
> `Content`/`None` + `CopyToOutputDirectory` 项，源目录的 json 不会被复制到输出目录
> （实测 `config copied to output: False`，仍随机失败 12–14 项，极易误判成"该开关无效"）。
> 若要长期启用，需在 csproj 中显式加 `<None Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />`。
>
> **最小复现（用于自查）**：只跑 `NLCPGDisplayTextTests` + `NLCPGNodeIdContractTests`
> （22 项）即稳定复现 **2 失败**（`BuildFromSource_DifferentDegreesOfParallelism_PreserveLegacyToNodeIdMapping`、
> `BuildFromSource_RepeatedBuilds_PreserveLegacyToNodeIdMapping`）。

### 如何优化 `System.String`（含"已有优化为什么不起效果"）

#### 先摆正问题：这是"实例数第一"，不是"占用第一"

`System.String` 在两个口径上的排名完全不同，混用会得出错误的优化方向：

| 口径 | `System.String` 的读数 | 排名 |
| --- | ---: | --- |
| 峰值样本 `0016-late-156s` 实例数 | **484,208** | **第 1** |
| 峰值样本 `0016-late-156s` 占用 | 56.6 MB（1.47%） | 第 7 |
| 截断样本 `0025-late-427s` 实例数 | **6,039,651** | **第 1** |
| 截断样本 `0025-late-427s` 占用 | 1,600.2 MB | 第 1（但受 10,000,000 对象截断偏置） |
| 21 点累计（含 3 个截断点） | 7,918.6 MB | 第 2 |
| 18 点累计（剔除截断点） | **3,118.0 MB** | 第 3 |

所以准确表述是：**`System.String` 是实例数第一、是分配churn 第一，
但在可靠样本里不是存活占用第一**——存活占用第一始终是 `System.UInt64[]`（60.20%）。
字符串的代价主要落在 **GC 扫描成本、Gen0/Gen1 提升频率与分配速率**上，
而不是稳态堆占用。这与"内存墙"那条主线是**两个独立的优化目标**，
不要指望压缩字符串能解决 `System.UInt64[]` 的 60%。

#### 已有的字符串优化代码：`StringInterner` + `uint` 节点文本

仓库**已经有一套完整的字符串驻留设计**，不是没有优化：

| 位置 | 作用 |
| --- | --- |
| `src/NLCPG/Model/StringInterner.cs` | 唯一的字符串驻留器：`string -> uint`，带反向还原 |
| `src/NLCPG/Model/NLCPGNode.cs` L6-18 | 节点只存 `uint NameId/FullNameId/SignatureId/TypeFullNameId/FilePathId`，**不存 string** |
| `src/NLCPG/Model/NLCPGGraph.cs` L139-152 | `InternDraft` 把这 5 个文本字段逐个驻留 |
| `src/NLCPG/Model/StableNodeIdentityFactory.cs` L8,34,39 | 稳定锚点用**自己的一套** interner 存 `filePath`/`extraKey` |
| `src/NLISSN.Core/Decision/DecisionModel.cs` L705 | `static readonly StringInterner DecisionStringTable`，供决策键使用 |

设计意图是对的，而且**在它覆盖的范围内确实生效了**：`NLCPGNode` 是 24 B 量级的
`readonly record struct`，如果没有驻留，每个节点要挂 5 个引用 + 5 份字符串。

#### 为什么不起效果：6 条实测原因

**① 覆盖率只有约 2%，因为驻留表本身极小。**
`StringInterner` 的两个字典在 gcdump 里有可直接读到的支撑数组。
`Entry<string,uint>[]` 与 `Entry<uint,string>[]` 在 `src/` 中**只可能**来自
`StringInterner.cs` L8-9（已确认全仓库无其他同型字典），因此它们的实例数
就是**当前存活的 interner 个数**：

| 样本（`gcdump-type-totals-per-sample.csv`，未分桶权威值） | 实例数 | 支撑数组总字节 | 槽位（÷24，容量≥条目数） |
| --- | ---: | ---: | ---: |
| `0001-early-0s` | 2 | 119,280 B | 4,970 |
| `0004-early-52s` | 2 | 622,560 B | 25,940 |
| `0005-early-58s` | 2 | 192 B | 8 |
| `0016-late-156s` | 2 | **248,448 B** | **10,352** |
| `0028-late-517s` | 1 | 46,368 B | 1,932 |

正向表与反向表**在每一个采样点上字节数完全相同**（21/21 一致），这就是下面第 ④ 条。

在峰值样本 `0016-late-156s` 上，全进程 interner 容量合计 **10,352 条**
（≈248 KB），而同一时刻存活的 `System.String` 实例是 **484,208** 个。
即使驻留表**全部装满**、且每条恰好对应一个存活字符串，它最多也只解释了
**10,352 / 484,208 = 2.14%**。也就是说：**约 98% 的存活字符串与 interner 无关**，
它们来自 Roslyn 语法树/符号以及自己拼出来的临时键。
优化一个最多覆盖 2% 的机制，天花板就是 2%。

**② `Intern(string)` 是"先分配、后去重"，拦不住分配。**
`StringInterner.Intern` 的形参是 `string? text`（L13）——**调用方必须先把字符串造出来**
才能传进来。驻留只能阻止"同样的文本被保留很多份"，无法阻止"同样的文本被反复分配"。
所有真正的分配点都在调用 interner **之前**，实测每次调用的分配量：

| 调用点 | 每次分配 | 备注 |
| --- | ---: | --- |
| `node.Kind().ToString()`（`SyntaxPass.cs` L249,L414） | **24.00 B** | 返回的是**缓存实例**（`ReferenceEquals` 为真），24 B 全部是 `Enum.ToString()` 的**装箱** |
| `node.RawKind.ToString()` | **32.00 B** | `int.ToString()` |
| `BuildNodeKey`：路径与 3 个整数用竖线拼接（`DecisionModel.cs` L780） | **96.05 B** | |
| `$"syntax:{path}:{kind}:{a}:{b}"` / `$"token:…"`（`NLCPGBuilder.cs` L1704,L1709） | **144.00 B** | |
| `$"method:{SymbolId(...)}"`（`MethodDecorationPass.cs` L87） | **304.00 B** | 取决于符号 id 长度 |

注意第一行这个反直觉读数：**枚举 `ToString()` 每次仍分配 24 B**，
因为 `Enum.ToString()` 要把值装箱后走虚调用；返回的字符串虽然是缓存的，
装箱对象照样产生。`SyntaxKind` 底层是 `UInt16`、无 `[Flags]`，已实测确认。
`SyntaxPass` 对**每个语法节点和每个 token** 都调一次 ⇒ 调用次数 = 语法节点数 + token 数。

**③ 键本身高度唯一 ⇒ 去重率≈0，但保留率 100%，净亏。**
`SymbolId` 走 `GetDocumentationCommentId()`，`SyntaxId`/`TokenId`/`BuildNodeKey`
都含 `SpanStart`/`SpanEnd`/`RawKind`（`NLCPGBuilder.cs` L1704,L1709；
`DecisionModel.cs` L780）。这些键**每个语法节点各不相同**：
- 去重收益 ≈ 0（没有重复文本可省）；
- 但一旦 `Intern` 过，键就进了 `_idsByText` **且** `_textsById`（L29-30），
  **永久保留**——`StringInterner` 没有 `Clear`/`Remove`/淘汰（全文 49 行，无任何清理方法）。

于是驻留从"优化"变成"把瞬态字符串转成永久字符串"。
唯一例外是 `method:`/`methodparam:` 这类**同一方法反复查询**的键，那里去重确实有效。

**④ 反向表让内存翻倍。**
L29-30 同时写 `_idsByText[text] = id` 和 `_textsById[id] = text`，两个字典的
支撑数组在**21 个采样点上字节数全部相同**（见 ① 的表，两个类型逐点一致：
119,280 / 622,560 / 192 / … / 248,448 / 46,368）。
`_textsById` 只在 `TryResolve`（展示/导出）时才需要，却让驻留表占用 ×2。
另外这两个数组的**实例数恒为 2**（`0016` 起），与"每个 `NLCPGGraph` 一个
graph-interner + `StableNodeIdentityFactory` 一个 identity-interner"吻合；
每次 `AddNode`（L107）都会经 L110 `MaterializeCompatibilityIdentity` →
L681 `_identityFactory.GetStableAnchor(node, _stringInterner, stableIdentityText)`
走一遍 identity-interner，所以**每个节点至少触发一次 `Intern`**（`filePath` + `extraKey`）。

**⑤ 驻留器实例短命且数量多，去重窗口只有"单个 graph/pass"。**
支撑数组字节数逐样本**振荡**而不是单调增长——
`119,280 → 119,280 → 622,560 → 192 → 192 → 143,568 → 68,448 → 194,400 → 192
→ 143,568 → … → 248,448 → … → 46,368`——
**从 622,560 B 直接掉到 192 B（容量 25,940 槽 → 8 槽）**，
这只有在"旧 interner 被整体丢弃、新 interner 从零重建"时才可能出现。
来源：

- `NLCPGGraph` L28 每个图默认 `new StringInterner()`；
- `ControlFlowPass.cs` L58、`ControlDependencePass.cs` L77、`DominancePass.cs` L399
  各自 `new NLCPGGraph(...)` ⇒ **每个 pass 每批一次全量重建**；
- `CpgFrozenShardGraphReader.cs` L16,L129,L192、`CpgShardRelationQueryService.cs` L159、
  `NLCPGSliceQuery.cs` L231 各 `new StringInterner()` ⇒ 每次读分片/查询又一套。

同一份文本在不同 graph/pass/分片里被**反复重新分配 + 反复重新驻留**，
跨图完全不去重。唯一真正全局的是 `DecisionStringTable`，但它是
`static readonly` 且同样永不清理 ⇒ 那一份**跨文件、跨运行累积，只增不减**。

**⑥ 驻留器上的一把全局锁。**
`Intern`（L20）与 `TryResolve`（L44）各自 `lock (_gate)`，每个 interner 一把。
`cpgDop=12` 时 12 个 worker 打同一把锁；这与计数器里
`dotnet.monitor.lock_contentions` 实测 **20,494 次争用全部落在
17:32:10–17:35:14**（即并行窗口内）是同一类现象的候选来源之一。
（注：争用计数**不是**按锁归属的，此处只能说"候选来源"，不能断言全部来自 interner。）

**另外两条结构性缺口（不是 interner 的锅，但决定了字符串总量）：**

- **`StringComparer.Ordinal` 在 `src/` 出现 440 次**（其中 373 次是字典构造或
  `ToDictionary`/`Distinct` 的比较器实参），即以**裸 `string` 为键**的查找结构
  仍大量存在，完全绕过 interner；节点文本驻留了，但这些查找表没有。
  这是"字符串总量为何下不来"的结构性原因。
- **`SymbolId()` 在每个方法上被调用两次**：`MethodDecorationPass.cs`
  L87-88、L112-113、L141-142 都是
  `var key = $"method:{SymbolId(methodSymbol)}"; var methodSymbolKey = SymbolId(methodSymbol);`
  ——插值里一次、下一行又一次。`SymbolId` 内部还要 `ToDisplayString` + `Replace`
  （`NLCPGBuilder.cs` L1685-1686, L1691-1692），**纯浪费一倍**。

#### `IComparer<NLCPGNode>` 逐字段比较到底能不能替代 `NodeSortKey`？

**结论：能，但"逐字段"必须是"逐字符"——按字段做 `CompareOrdinal` 一定错，特判 0x7C 也不够。**

原理上完全可以不要字符串：`NodeSortKey` 是 `(Kind, FullName, Name, FilePath, SpanStart, SpanEnd)`
六个字段的**纯函数**，而排序只需要一个**全序**，不需要真的把那 148 个字符拼出来。
所以"用比较器代替拼串"这个方向本身是成立的（实测分配 **320 → 0 B/次**）。
但代码里那个 key 是**一条字符流**，`'|'`(0x7C) 只是普通字符，于是有三处必须复刻：

| 陷阱 | 反例 | 拼接键真值 | 朴素逐字段 | 后果 |
| --- | --- | ---: | ---: | --- |
| **前缀关系翻转** | `"Method"` vs `"MethodParameter"`（`NLCPGNodeKind` 里真实存在 4 组这种前缀对） | `MethodParameter` 在前 | `Method` 在前 | 序反了。拼接后第 7 位比 `'\|'`(0x7C) vs `'P'`(0x50)，`'\|'` 更大，**短的反排后面** |
| **数字按字符串比** | `SpanEnd` = `9` vs `10` | `10` 在前（`"10" < "9"`） | 数值序 `9` 在前 | 序反了。`int?` 被插值成十进制文本参与字典序 |
| **`null` 渲染成空串** | `FullName` = `null` vs `"T.A(int)"` | `null` 那个**排后面**（比的是 `'\|'` vs `'T'`） | `null` 排前面 | 序反了。`Resolve*` 返回 `string?`，插值把 `null` 和 `int?` 的 `null` 都渲染成 `""` |

**为什么"前缀处显式比较 0x7C"这种写法也不够**（本报告早先版本如此建议，实测否定）：
当短字段结束后的分隔符 0x7C 恰好等于长字段的下一个字符时，
该写法提前返回 0 判等，而真值还要继续比后面的段。最小反例（`Name`+`FilePath` 两段）：

| A 的键 | B 的键 | 拼接键真值 | "特判 0x7C"写法 |
| --- | --- | ---: | ---: |
| `C\|F\|A\|Z` | `C\|F\|A\|B\|` | `24`（B 在前） | **`0`（判等，错）** |

原因是 A 的 `Name` 段结束后的分隔符与 B 的 `Name` 第 2 个字符都是 0x7C，
比较必须继续进入 `FilePath` 段（`Z` vs `B`），提前收工就丢了后续信息。

**唯一严格等价的零分配实现**：逐字符遍历"虚拟拼接流"——段间**真实产出** 0x7C、
`null` 段渲染成空串、**空段照样占位**（会产生连续 `||`，这个"占位"效果一旦删段就整体错位）。
实测等价性（N=120,000 对抗集，含前缀对、字段内 `|`、空串、`null`、尾部空格）：

| 实现 | 与现状 3 级排序等价 | 位置不同 |
| --- | --- | ---: |
| 朴素逐字段 `CompareOrdinal`（无 `\|`） | ✗ | 119,987 |
| 6 段流式（段间插 `\|`，含前两级已比过的段） | **✓** | **0** |
| 化简 4 段流式（删掉已比过的段） | ✗ | 16,599 |
| 比较器内每次 `new string[]` 重建字段 | — | 分配 **103.76 MB**，比现状 27.44 MB **更差** |

两条工程提醒：

1. **比较器本身必须零分配**，否则更差。`OrderBy(selector)` 每元素算 **1 次**键（共 n 次），
   而 `OrderBy(comparer)` 每**比较**调 **1 次**（约 n·log₂n 次，N=120,000 时 **16.9 倍**）。
   所以正解是 `readonly struct` 键 + `IComparable<T>`（选择器 O(n)、比较零分配），
   而不是在比较器里现造数组——后者实测放大到 **103.76 MB**。
2. **别顺手把 `OrderBy` 换成 `Array.Sort`**：`OrderBy/ThenBy` 是**稳定**排序，
   `Array.Sort` 是**不稳定**排序，键完全相同的元素相对次序会变，而对本仓库是同键即同结果
   的语义敏感点。

#### 逐字符虚拟流的实现风险（逐项实测）

上面这个方案**能实现，但有 9 类真实风险**，其中 3 类会直接产出错误的图（不是慢，是错）：

| # | 风险 | 实测证据 | 后果 | 规避 |
| --- | --- | --- | --- | --- |
| 1 | **把 `Resolve` 写进比较器** | 比较器调用次数是选择器的 **n·log₂n/n**；N=50,000 实测锁调用 50,000 → **2,081,014（41.6 倍）** | `NLCPGGraph.Resolve` L197-200 每次 `lock(_gate)`；本次运行 `lock_contentions` 峰值已 858/2 s | **必须**把 `Resolve` 提到键选择器里（O(n)） |
| 2 | **只实现非泛型 `IComparable`** | N=20,000 实测 **22,069.6 KB** vs 泛型 **1,016.1 KB**（21.7 倍） | 每次比较装箱，零分配目标当场失效 | 实现 `IComparable<T>`，不加非泛型版 |
| 3 | **在比较器里现造字段数组** | N=120,000 实测 **103.76 MB** vs 现状 27.44 MB | 比不改还差 | 键选择器物化，比较器只读字段 |
| 4 | **`Kind` 改按枚举数值比** | N=80,000 实测位置不同 **80,000**；`SyntaxNode`(1) vs `SyntaxTree`(0) 文本序与数值序**相反** | 静默改序 ⇒ 改 `targets[0]` 选择 | 一律按**文本**比，用静态表 |
| 5 | **`Kind.ToString()` 以为是 0 分配** | 定义值 **24 B/次**；插值内 **56 B/次**（`Enum.GetName` 与静态表才是 **0.00**） | 键选择器按 n 次付这笔钱 | 用 `static readonly string[]`，**带回退** `((int)k).ToString()`（越界值给数字串） |
| 6 | **`int` 缓冲 < 11 字符** | `int.MinValue` = `"-2147483648"`（11 字符）；`char[8]` 下 `TryFormat` **失败且返回 0** | 键少一段 ⇒ 静默错序 | 缓冲 ≥ 11，且**检查返回值** |
| 7 | **Culture 不一致** | `sv-SE`/`fi-FI` 负号为 U+2212、`ar-SA` 为 U+061C；`Invariant` 恒为 U+002D | 负数 `SpanStart` 文本不同 ⇒ 序不同 | 必须与插值一致用 **`CurrentCulture`**，不能用 `Invariant` |
| 8 | **化简/删段** | 穷举 43,046,721 有序对：删段但保留占位仍错 **906,136**；彻底删段错 **1,814,344** | 错序 | 照抄**完整 6 段**，不要"省几个字符" |
| 9 | **三条键链共用一个 Key** | 见下 | 错序 | 三条链的层级**本来就不同**，必须各自实现 |

> **风险 9 单独展开**（最容易误判为"同一件事"）：
> `targets` 链（L778-779）比 `orderedCallSites` 链（L767-769）**少一层 `SpanStart`**。
> 若两处复用同一个 `Key`，会**多比一层** `SpanStart`：实测 N=40,000 位置不同 **39,985**。
> 反例：`Full=F` 固定时，A=`SpanStart 9, Kind=Method`、B=`SpanStart 10, Kind=CallSite`——
> 现状比 `Kind` 文本（`'M'>'C'`）⇒ **B 在前**；多插一层则先比 `9<10` ⇒ **A 在前，结论相反**。
> 而 `orderedPlans`（L875-882）是第三种形状：`StableCallSiteOrder → BridgeKind →
> 4×NodeSortKey → ArgumentOrdinal → BridgeKind`，**且 L880 的 `BridgeKind` 与 L876 重复**。

> **附带的死代码发现**：`targets` 排序（L774-780）在生产默认值下**排序结果一定被丢弃**。
> `MaxCallTargetsPerSite` 默认 = **1**（`NLCPGBuilderOptions.cs` L160），
> 而 L789 对 `targets.Length > 1` 直接 `RecordCut("AmbiguousTarget")` 并 `continue`——
> 也就是说**能走到 L795 `targets[0]` 的只可能是长度恰为 1 的数组**（`Distinct()` 在 L777，
> 早于 L789 的长度判定）。单元素数组的 `OrderBy/ThenBy` 不可能改变任何输出。
> ⚠ **但它不是"零成本"**：`OrderBy` 会为每个元素调用一次键选择器（实测恰好 1.00n），
> 所以 L779 的 `NodeSortKey` **仍被调用一次并被丢弃**——这是"73.65% 里语义上最纯的浪费"
> （**纯度最高，但不必是占比最大的一份**：它是每个 call site 一次，与真正决定顺序的 L769 同量级；
> 本次未单独测量这两处的调用次数之比）。
> 把它改成"先判长度、再直接取 `targets[0]`"即可整段省掉这次调用。
> 全仓搜 `MaxCallTargetsPerSite` 仅 L668（日志）、L789（判定）、L160（定义）三处（其余命中都在
> `Build/PerformanceResults` 的 JSON 与 `Miscellaneous/tmp` 的源码副本里），
> **无任何测试或配置把它改成 >1**，`NLCPGInterproceduralDataFlowOptions` 也**无显式构造点**
> ⇒ 生产恒为默认值。此项**只解释成本、不改变语义**，属等价改写；但改代码不在本次范围内，**未实施**。

#### 逐字符虚拟流的具体实现（可直接照抄的骨架）

核心思路：**不构造字符串，而是把"拼接后的那条字符流"抽象成一个游标**——
`SegmentCursor` 按段推进，段内逐字符产出，段之间产出 `'|'`，`null` 段长度为 0。
外层 `NodeStreamKey` 在**构造时**（O(n) 次）就把 `Resolve` 的文本取好并缓存为引用，
`CompareTo` 里只读字段、只写 `stackalloc`，因此比较本身零分配。

```csharp
// 放在 NLCPG/Builder/ 下（internal，与 NodeSortKey 同文件或同目录）
internal readonly struct NLCPGNodeStreamKey : IComparable<NLCPGNodeStreamKey>
{
    // 枚举文本静态表：定义值命中即为 0 分配；越界值回退 Enum 语义（数字串）
    private static readonly string[] KindTexts = BuildKindTexts();

    private readonly string _kindText;
    private readonly string? _fullName, _name, _filePath;   // Resolve* 返回 string?，必须可空
    private readonly int? _spanStart, _spanEnd;

    // ★ 关键：Resolve 只在构造时调用（O(n)）；绝不放进 CompareTo
    public NLCPGNodeStreamKey(NLCPGGraph graph, NLCPGNode node)
    {
        _kindText = KindText(node.Kind);
        _fullName = graph.ResolveFullName(node);
        _name = graph.ResolveName(node);
        _filePath = graph.ResolveFilePath(node);
        _spanStart = node.SpanStart;
        _spanEnd = node.SpanEnd;
    }

    public int CompareTo(NLCPGNodeStreamKey other)
    {
        // 必须 CurrentCulture（与 $"" 插值一致）；Invariant 在 sv-SE/fi-FI/ar-SA 下负号不同
        Span<char> ls = stackalloc char[12], rs = stackalloc char[12];
        Span<char> le = stackalloc char[12], re = stackalloc char[12];
        var lc = new SegmentCursor(_kindText, _fullName, _name, _filePath,
            ls[..FormatInt(_spanStart, ls)], le[..FormatInt(_spanEnd, le)]);
        var rc = new SegmentCursor(other._kindText, other._fullName, other._name, other._filePath,
            rs[..FormatInt(other._spanStart, rs)], re[..FormatInt(other._spanEnd, re)]);

        while (true)
        {
            var hasLeft = lc.TryNext(out var left);
            var hasRight = rc.TryNext(out var right);
            if (!hasLeft) { return hasRight ? -1 : 0; }
            if (!hasRight) { return 1; }
            if (left != right) { return left < right ? -1 : 1; }
        }
    }

    private static int FormatInt(int? value, Span<char> destination)
    {
        if (!value.HasValue) { return 0; }                    // 插值把 null 渲染成空串
        if (!value.Value.TryFormat(destination, out var written, default, CultureInfo.CurrentCulture))
        {
            throw new InvalidOperationException("int buffer too small");  // 绝不静默用 0
        }
        return written;
    }

    private static string KindText(NLCPGNodeKind kind)
    {
        var index = (int)kind;
        if ((uint)index < (uint)KindTexts.Length && KindTexts[index] is { } text) { return text; }
        return index.ToString(CultureInfo.CurrentCulture);    // 越界值：Enum.ToString() 给数字串
    }

    private static string[] BuildKindTexts()
    {
        var values = (NLCPGNodeKind[])Enum.GetValues(typeof(NLCPGNodeKind));
        var names = Enum.GetNames(typeof(NLCPGNodeKind));
        var max = 0;
        foreach (var value in values) { if ((int)value > max) { max = (int)value; } }
        var texts = new string[max + 1];
        for (var i = 0; i < values.Length; i++) { texts[(int)values[i]] = names[i]; }
        return texts;
    }
}

// 六段游标：四个 string 段 + 两个栈上数字段，段间产出 '|'
internal ref struct SegmentCursor
{
    private readonly string? _first, _second, _third, _fourth;   // null 段长度按 0 处理
    private readonly ReadOnlySpan<char> _fifth, _sixth;
    private int _segment, _offset;

    public SegmentCursor(string? first, string? second, string? third, string? fourth,
        ReadOnlySpan<char> fifth, ReadOnlySpan<char> sixth)
    {
        _first = first; _second = second; _third = third; _fourth = fourth;
        _fifth = fifth; _sixth = sixth; _segment = 0; _offset = 0;
    }

    public bool TryNext(out char value)
    {
        while (_segment < 6)
        {
            var taken = _segment switch
            {
                0 => Take(_first, out value),
                1 => Take(_second, out value),
                2 => Take(_third, out value),
                3 => Take(_fourth, out value),
                4 => Take(_fifth, out value),
                _ => Take(_sixth, out value),
            };
            if (taken) { return true; }
            if (_segment < 5) { _segment++; _offset = 0; value = '|'; return true; }  // ★ 空段照样占位
            break;
        }
        value = '\0';
        return false;
    }

    private bool Take(string? text, out char value)
    {
        if (text is not null && _offset < text.Length) { value = text[_offset]; _offset++; return true; }
        value = '\0';
        return false;
    }
    private bool Take(ReadOnlySpan<char> text, out char value)
    {
        if (_offset < text.Length) { value = text[_offset]; _offset++; return true; }
        value = '\0';
        return false;
    }
}
```

**替换点**（只改 3 个 `.ThenBy`，其余链条不动）：

> ### ⚠️ 本方案已于 2026-09-23 执行并被实测否决（**请勿再照此实施**）
>
> 结构体键的**语义完全正确**——在真实诊断输入 `Version4/Terraria/Player.cs` 上
> **437,371 对逐对比较、差异 0**，且用字符串键重跑整条 8 层链**逐元素相同**。
> 但**收益不成立，产品代码已回退**：
>
> | 口径 | 字符串键（现状） | 结构体键 |
> | --- | ---: | ---: |
> | pass 内分配 | 376.4 MB | 353.2 MB（**−6.2%**） |
> | pass 耗时（分段比较） | 786 / 823 ms | 891 / 907 ms（**+12.9%**） |
> | 端到端墙钟 | 26,908 ms | 27,640–28,495 ms（**+2.87~5.90%**） |
>
> **根因**：字符串键在**构造期**渲染一次（`n` 次），比较期用 **SIMD `CompareOrdinal`**；
> 结构体键把渲染搬进了 `CompareTo` ⇒ 同一份工作被做 **`n log n`** 次。
> `n=20162` 实测 **构造 81,557 次 vs 比较 374,742 次 = 放大 4.6 倍**。
> "省下 `n` 次分配"换来"多做 4.6 倍渲染"——**比较器里的每一纳秒都要乘 `log n`**。
>
> 加上**分母警告**（同一笔 1.632 GiB 只占全运行 93.73 GiB 的 **1.74%**），
> 即使完全清零也只有 ~1.7%，而实测连这个都没拿到。
>
> 下文实现细节**保留作为等价性方法与非显然陷阱的记录**（尤其"不能按字段独立比较"
> 与 `'|'` 哨兵在**末段**会翻符号这两个坑），**但不要作为待办**。
> 完整实测与回退记录见 `docs/plans/2026-09-23-zero-allocation-key-execution.md` Task 6 Step 5。

| 位置 | 现在 | 改成 |
| --- | --- | --- |
| L769 | `.ThenBy(node => NodeSortKey(graph, node), StringComparer.Ordinal)` | `.ThenBy(node => new NLCPGNodeStreamKey(graph, node))` |
| L779 | `.ThenBy(target => NodeSortKey(graph, target), StringComparer.Ordinal)` | 同上（注意此链**没有** `SpanStart` 层，Key 不变；**且实测该链负载为 0**，见下） |
| L877/878/881/882 | `.ThenBy(plan => NodeSortKey(graph, plan.X), StringComparer.Ordinal)` | `.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.X))` |

> **另两处已实测更正**：① L779 `targets` 链的 `OrderBy` **负载为 0**——
> 全部 22 个 `targets` 数组长度恰为 1，`OrderBy` 对 `n ≤ 1` **完全短路**，
> 故它**不是**"算了就扔的非零成本"；② 真正的调用量集中在 `orderedPlans`
> （**472/494 = 95.5%**），`orderedCallSites` 仅 4.5%。

**实测（本机 Release，真实形状：长公共前缀 + 对照现状三级链）**：

| 项 | 结果 |
| --- | --- |
| **用仓库真实类型编译**（`ProjectReference` 指向 `src/NLCPG`，`Nullable=enable` + `LangVersion=preview`） | **0 错误 0 警告** |
| 一级：`Key` 与 `NodeSortKey` 单独排序 | 次序相同 ✓（N=6,000） |
| 完整三级链（含 `ResolveFullName`+`SpanStart` 两层） | 次序相同 ✓（N=6,000） |
| 穷举有序对（含 `null`/空串/字段内 `\|`/负数/未定义枚举） | **67,600 对，不同 0** |
| 比较本身分配 | **0 B**（52,000 次比较） |
| 三级链端到端 | 耗时 **10.3 → 5.4 ms**（−47.6%）、分配 **2,669.9 → 821.1 KB**（−69.2%） |

> ⚠ 端到端那次只降到 821.1 KB 而非 0：剩余部分是 `OrderBy` 自身的缓冲
> （`IndexedIterator` 的索引数组 + 装箱的排序器状态）。**要归零得连 `OrderBy` 一起换掉**，
> 但那会碰到"稳定排序"这条红线（见上一节风险 2）。
> ⚠ 上面代码是**在探针里验证过的骨架**，`NLCPGNodeKind` 的实际成员名与
> `NLCPGGraph.Resolve*` 的返回可空性已按仓库现状对齐；**本次未落进 `src/`**。

#### 优化方案（按"收益/风险"排序）

> **排序依据**：下面 1–2 项来自**实测分配归因**（`AllocationTick` 调用栈，
> 合计占字符串 churn 的 88.2%）；3 项及以后来自**本地微基准 + 源码**，
> 未经本次运行的分配栈证实，量级更小、更分散。

1. **消掉 `ToContextId` 的重复调用**（`NLCPGBuilder.cs` L908 与 `NLCPGEdge.cs` L11、
   `NLCPGBuilder.cs` L991 与 L992）。
   *实测 0.322 GiB / 14.52% of string churn。* 现在同一个 `NLCPGCallSiteContext`
   在两个地方各插值一次，**字符串完全相同、纯重复**。
   改法：算一次、传进去复用（`NLCPGEdge` 构造函数已有"两者必须一致"的断言，
   所以这是**零语义风险**的等价改写）。
2. **`NodeSortKey` 改成零分配比较器**（`NLCPGBuilder.cs` L1053-1056）。
   *实测 1.632 GiB / 73.65% of string churn、约 592–594 万次调用（按 295–296 B/次反推）。*
   **收益最大，但必须逐字节保持排序语义**——因为 L795 `targets[0]` 用它选定
   唯一目标方法，排序变了图就变了。实测已验证：拼接键的序**不等于**逐字段元组序
   （`'|'` 是 0x7C，会被当普通字符参与比较；前缀情形如 `"A"` vs `"AB"` 顺序翻转）。
   所以**"每层拿一个字段 `CompareOrdinal`、前缀处特判 0x7C"这种常见写法也不够**
   （本报告早先版本曾如此建议，实测否定）：当短字段结束后的分隔符 0x7C 恰好等于
   长字段的下一个字符时（`"A"`+`"Z"` vs `"A|B"`+`""`），该写法提前返回 0 判等，
   而真值还要继续比后面的段。唯一严格等价的零分配实现是**逐字符走完整条虚拟拼接流**：
   段间真实产出 0x7C、`null` 段按插值语义渲染成空串、空段照样占位（产生连续 `||`）。
   实测该实现（6 段流 + 前两级按值比较）在 N=120,000 对抗集上**位置不同 0**；
   而朴素逐字段在 N=120,000 上位置不同 119,987。另注意 `Array.Sort` 是**不稳定**排序，
   现状 `OrderBy/ThenBy` 是稳定排序，改写时若换掉 `OrderBy` 会改变同键元素的相对次序。
   > 注意 `NodeSortKey` 的**真实作用**：L767/L768 已经比过 `FullName` 和 `SpanStart`，
   > 第三层只在二者完全相同时生效。它把"平局由枚举顺序决定"变成"平局也确定"。
   > 由于 `AddNode` 按 `(Kind, filePath, spanStart, spanEnd, FullName)` 去重，
   > 对**互不相同**的 CallSite 节点它永不并列——**不能简单删掉第三层**。
3. **先把 `SymbolId` 的双调用消掉**（`MethodDecorationPass.cs` L87-88/L112-113/L141-142）。
   一行改动、零语义风险、直接省一半 `SymbolId` 成本。
4. **`SymbolId`/`BuildNodeKey`/`SyntaxId`/`TokenId` 改成结构化键 + 值缓存**：
   键的唯一性来自 `(filePath, spanStart, spanEnd, rawKind)` 这几个整数，
   **用 `readonly record struct` 做字典键就完全不需要拼字符串**，
   把上面 96–304 B/次 的分配直接变成 0。
   > ⚠ **本项已修正（2026-09-23）**：此处原先还写了"若必须字符串化，用 `string.Create`
   > 写入栈上缓冲，而不是 `$""` 插值"。该建议**经实测无效，已撤回**——
   > `string.Create` 的分配量与 `$""` 插值**相同甚至略高**（328 B vs 320 B/次）。
   > 原因是 `$""` 插值对全 `string`/`int` 操作数走 `DefaultInterpolatedStringHandler`，
   > **本来就只有一次分配**，没有中间缓冲可省。唯一有效方向是**不构造这个字符串**。
5. **`Kind().ToString()` 换成静态查表**：`SyntaxKind` 是 `UInt16` 枚举，
   用 `static readonly string[]`（或 `FrozenDictionary<SyntaxKind,string>`）
   按值取回缓存串，消掉每次 24 B 的装箱；`NLCPGNodeKind`/`NLCPGEdgeKind` 同理。
   这一条覆盖"每个语法节点 + 每个 token"，调用次数最多。
6. **`StringInterner` 的查表加 `GetAlternateLookup<ReadOnlySpan<char>>` 重载**，
   让手上有 span 的调用方能免掉临时 string（实测 40 B → 0 B）。
   > 注意：**不能**用 `Dictionary<ReadOnlySpan<char>,T>` 直接替代——
   > 那是编译期错误 `CS9244`（`ref struct` 不能作泛型实参）。
7. **`DecisionStringTable` 去 `static`**，改为随运行作用域存活（或用 `ConditionalWeakTable`
   挂到图/运行上下文）；否则它是最直接的进程级无界增长点。
8. **给 `StringInterner` 加边界**：`_textsById` 改为惰性/可选（只有需要还原时才建），
   并给 `_idsByText` 设容量上限或提供 `Clear()`，让 per-pass 的图能真正释放表。
9. **让 interner 跨 pass/分片复用**：`ControlFlowPass`/`ControlDependencePass`/
   `DominancePass` 新建图时把外层 interner 传进去（`NLCPGGraph` 构造函数 L23
   已经接受 `StringInterner?` 参数，只是这三个调用点没传）。
10. **补齐裸字符串字典**：把 440 处 `StringComparer.Ordinal` 字典中真正热的
    改为以驻留 id 为键。

#### 判定边界

- **实测**：驻留表支撑数组尺寸与覆盖率（≤2.1%）、`System.String` 的实例数与占用排名、
  各调用点的每次分配字节数（本地 net10.0 探针）、枚举 `ToString()` 装箱 24 B、
  表尺寸振荡、`_idsByText` 与 `_textsById` 等大、"双调用"代码位置、
  **以及分配调用栈归因**（见下）。
- **⚠ 本节原"未验证"结论已撤回（2026-09-23）**：原先写"本报告没有采集分配调用栈
  （`dotnet-trace` 用的是 `gc-verbose`，非分配采样）"。**该结论错误**：
  `--profile gc-verbose` 启用的关键字是 `0x8003` = `GC(0x1) | GCHandle(0x2) | OverrideAndSuppressNGenEvents(0x8000)`，
  而 **`GC/AllocationTick` 属于 `GC(0x1)`，已被启用**。实际解析 `allocation.nettrace` 得到
  **127,295 个事件、61,433 个 `GC/AllocationTick`，其中 61,432 个带完整调用栈**，
  窗口内累计 **11.30 GiB**（与 counters 同窗口 11.2 GiB 吻合）。
- **据此得到的归因**（60.03 s 窗口，占 `System.String` 采样 2.215 GiB 的比例）：

  | 栈顶 `NLCPG.*`/`NLISSN` 帧 | 采样字节 | 占字符串 churn | tick 数 |
  | --- | ---: | ---: | ---: |
  | `NLCPGBuilder.NodeSortKey` | 1.632 GiB | **73.65%** | 16,480 |
  | `NLCPGCallSiteContext.ToContextId` | 0.322 GiB | 14.52% | 3,244 |
  | `PostRewriteDiagnostics.BuildStableDiagnosticKey` | 0.042 GiB | 1.90% | 427 |
  | `PostRewriteDiagnostics.GetErrorDiagnostics` | 0.037 GiB | 1.68% | 375 |
  | `NLCPGBuilder.SymbolId` | 0.036 GiB | 1.63% | 363 |

  字节加权与次数加权的份额一致（73.65% vs 74.2%、14.52% vs 14.61%），归因稳健。
- **口径警告（务必同时引用）**：`73.65%` 的分母是**字符串**，不是全程序。同一笔
  1.632 GiB 换分母后是：占 trace 窗口全部字节 **14.44%**（1.632/11.30）、
  占全运行 **1.74%**（1.632/93.73）。且分子只来自**前 60.03 s（占 1042 s 墙钟的 5.8%）**，
  不能线性外推到全程——分配速率极不均匀（峰值 3.24 GiB/s，长尾 ≈0.03 GiB/s）。
- **不成立的推论**：不能由"`System.String` 实例数第一"推出"压缩字符串能显著降低峰值内存"——
  可靠样本里它只占 1.47%，峰值内存的主项是 `System.UInt64[]`。

#### 能不能换一种"更省的字符串类型"？——不能，逐项实测否定

问法是"用别的字符串类型减少分配大小"。**结论：换类型解决不了这个问题**，
因为代价不在"每个字符占 1 或 2 字节"，而在"**每次调用都产生一个新对象**"。
下面是本机 net10.0（`10.0.11`）实测，样本取本仓库真实形状
（`Full`=108 字符、`Name`=24、`Path`=48，拼成 148 字符的键），每项 100 万次调用：

| 方案 | B/次 | 相对现状 | 说明 |
| --- | ---: | ---: | --- |
| 零分配基线（返回常量） | **0.00** | — | 证明探针本身不分配 |
| **A 现状：`$"{...}\|{...}"` 插值** | **320.00** | 基准 | 恰好**一次**分配 |
| B `string.Create` + span 写入 | 328.00 | **+2.5%（更差）** | 无中间缓冲可省 ⇒ **原建议已撤回** |
| C UTF8 `Encoding.UTF8.GetBytes(拼接串)` | 568.00 | **+77%（更差）** | 先分配 string 再分配 byte[]，**两个** |
| D `stackalloc byte[512]` 直写 UTF8 | **0.00** | **−100%** | 零分配，但**必须完全不落堆** |
| E 结构比较器（不构造字符串） | **0.00** | **−100%** | 排序场景的最优解，但**必须逐字符复刻拼接语义**（见下） |
| F `HashCode` 组合三字段 | **0.00** | **−100%** | 字典键场景的最优解 |

**逐条否定的理由：**

1. **`Utf8String` 不存在。** 运行期反射 `Type.GetType("System.Utf8String, System.Private.CoreLib")`
   返回 `null`；C# 11 的 `u8` 后缀只给 `ReadOnlySpan<byte>` 字面量，
   提案 `dotnet/runtime#933` 至今未落地（该提案本身写着"生态已把
   `ReadOnlySpan<byte>` 当事实标准，引入 `Utf8String` 的概率每天都在下降"）。
2. **`byte[]`/UTF8 在"临时键"场景反而更贵。** 见 C 行：`GetBytes` 要求先有 `string`，
   于是**同时**分配 `string` + `byte[]`。UTF8 只有在**存储**场景才省——
   UTF16 是 `16 + 2n`、UTF8 是 `24 + n`，实测 20,000 条省 **35.8%**
   （113 B → 72 B/条）。但**本仓库的存储侧已经没有字符串可省**：
   `NLCPGNode` 的 5 个文本字段全是 `uint` id，实测 interner 两张表合计
   **485 KB = 峰值堆 3,860.9 MB 的 0.0123%**。
3. **`ReadOnlySpan<char>` 不能当字典键——这是编译期错误，不是性能取舍。**
   实测 `Dictionary<ReadOnlySpan<char>,T>` 与 `List<ReadOnlySpan<char>>`
   均报 **`CS9244`**：`ref struct` 不允许作为泛型类型参数，
   因为 BCL 的 `Dictionary`/`List` 未声明 `allows ref struct` 约束。
   `Span` 不能存字段、不能跨 `await`——只能做**临时参数**。
4. **可行且确实零分配的是 `GetAlternateLookup`（.NET 9+，本仓库全部 `net10.0` 可用）。**
   表里照旧存 `string`，但**查询可用 `stackalloc` 拼出的 span**：
   实测 `alt[span]` 分配 **0 B**，对照 `dict[new string(span)]` 分配 **40 B**。
   这是"免掉临时 string"的正解，但它只适用于**查表**，不适用于
   `NodeSortKey` 那种"必须产出一个可比较键"的场景。
5. **`FrozenDictionary` 是净增内存**：`ToFrozenDictionary` 实测额外分配
   **11.03 MB**（N=100,000），它是**替代**（建完丢弃原表）而非叠加，
   对本仓库"表本来就小"的现状无收益。
6. **`ArrayPool` 已在前文被实测否定**（1.54× 放大 + 长期占用）。

**唯一真正的出路是"不构造这个字符串"**，而不是换一种字符串表示：

| 场景 | 现状 | 正确改法 | 省 |
| --- | --- | ---: | ---: |
| 排序键（`NodeSortKey`） | 拼 148 字符键 | `readonly struct` 键，实现 `IComparable<T>` 逐**字符**走虚拟拼接流 | **320 → 0 B/次** |
| 字典键（`BuildNodeKey`） | 拼键字符串 | `readonly record struct` 做键 | **160 → 0 B/次**（`CoverageEvidence` 变体 **216**，见下） |
| 查表（`StringInterner`） | 先造 string 再查 | `GetAlternateLookup<ReadOnlySpan<char>>` | **40 → 0 B/次** |

> ⚠ **本行（字典键）已在 2026-09-23 修正**，原先写的 `96–144` 是早期短键探针的值，
> 与本仓库真实键长不符。按真实表达式逐个实测（路径 49 字符、100 万次）：
>
> | 真实键表达式 | B/次 | 分配次数 | 键长 |
> | --- | ---: | ---: | ---: |
> | `DecisionCpgFactory.BuildNodeKey(SyntaxNode)`（`DecisionModel.cs` L780） | **160.00** | 1 | 66 |
> | `CoverageEvidence.BuildNodeKey`（`CoverageEvidence.cs` L113-118，`string.Join` 4 段） | **216.00** | **4** | 63 |
> | `NLCPGBuilder.SyntaxId`（L1704） | 168.00 | 1 | 73 |
> | `NLCPGBuilder.SymbolId` fallback（L1686） | 147.27 | 2 | 56 |
> | 对照：`readonly record struct`（4 个 `int`） | **0.03** | 0 | — |
>
> **三条必须同时说清的限定**：
> 1. **`CoverageEvidence` 变体贵在 4 次分配**：3 个 `int.ToString(CultureInfo.InvariantCulture)`
>    各自分配一个数字串，`string.Join` 再分配结果串。这是四个变体里最贵的，
>    **改结构体键对它的收益也最大**（216 → 0）。
> 2. **`SyntaxId`/`TokenId` 是死代码**：全仓（`src` + `tests`）grep 只有定义、**0 个调用点**
>    （`NLCPGBuilder.cs` L1702/L1707 是唯二命中）。所以"字典键 168 B/次"这条**当前根本不发生**，
>    改不改都不影响本次运行。
> 3. **`DecisionCpgFactory.BuildNodeKey(NLCPGNode)`（L784-792）不该按"拼键"算**：
>    它**先查驻留表**，命中时 `return fullName` 直接返回一个**已驻留的 `string` 引用**，
>    实测 **0.00 B/次**——它**本来就已经是零分配**。只有 `FullNameId` 未命中/空白
>    才走 `$"{ResolveFilePath(node)}|{node.SpanStart}|{node.SpanEnd}|{node.Kind}"` 这个 fallback
>    （实测 **168.03 B/次**，含枚举 `ToString` + 结果串 2 次分配）。
>    **对它做结构体键改造的收益只落在 fallback 分支上，不是无条件 160 B。**
> 4. **`BuildNodeKey` 已有三处返回元组、本来零分配**：
>    `ExpressionFlowPropagationRuleBase.cs` L137、`LiftedMarkFacts.cs` L87、
>    `DeclarationSymbolReferencePropagationRule.cs` L148 都返回
>    `(int Start, int Length, int RawKind)` 元组（`ValueTuple` 无堆分配）。
>    **说明"改成结构体键"在本仓库已有先例，不是新发明**；
>    但要注意它**丢了 `FilePath` 字段**，所以只适合"单文件内去重"，不能直接
>    替换跨文件的 `BuildNodeKey(SyntaxNode)`（后者含 `SyntaxTree.FilePath`）。

#### 这三个"省到 0"的共同原理：把"值与身份"从堆对象降级为**栈上的字节**

三行的省法看着不同，其实是**同一条原理的三次应用**：

**① 先把账算清楚：那笔分配到底买了什么。**
`string` 是**不可变堆对象**，堆尺寸实测 `align8(22 + 2n)`（对象头 16 + `_stringLength` 4 +
长度 2 字节 + 尾部填充），实测：`n=1` → 24 B、`n=8` → 40 B、`n=36` → 96 B、
`n=60` → 144 B、`n=148` → 320 B、`n=10` → 48 B。
**注意 `24 + 2n` 只在 `n % 4 == 0` 时成立**，`n=10` 时公式给 44 而实测 48
（报告别处的 `24+2n` 是简化式，通用式是 `align8(22+2n)`）。

**② 那笔分配唯一的作用是"给值一个能进堆容器的引用身份"。**
`NodeSortKey` 的字符串只被 `StringComparer.Ordinal` 读一次就丢弃；
`BuildNodeKey` 的字符串只被 `Dictionary` 哈希一次就丢弃；
`StringInterner` 的名字只被查一次表就丢弃。**三者都不需要"可长期持有的堆身份"**，
只需要"能比较/能哈希"。既然如此，堆对象就是纯粹的搬运损耗。

**③ 于是把"身份"降到栈上：**
- **排序**（`NodeSortKey`）：排序器只需要一个**全序**。让 `readonly struct` 实现
  `IComparable<T>`，`CompareTo` 里用 `ref struct` 游标逐字符吐拼接流——
  比较过程中不产生任何对象（实测 **0 B**）。
- **字典键**（`BuildNodeKey`）：字典只需要**哈希 + 相等**。把字段塞进
  `readonly record struct`（实测键尺寸仅 **12 B**，**按值内联进 `Dictionary.Entry[]`**，
  不是独立堆对象），由 `record struct` 自动生成 `IEquatable<T>` 与 `GetHashCode`。
- **查表**（`StringInterner`）：表里照旧存 `string`（**必须可变长驻留**），
  但**查询用 `ReadOnlySpan<char>`**——`GetAlternateLookup` 让字典接受 span 而不要求
  `string`，于是"从源文本切片"这一步不再需要先落一个 `string`。

**④ 为什么"0"是干净的 0 而不是"省一点"：**
这三条路径的**结果对象本身就是浪费**，不是"还能更省的浪费"。
一旦不再构造它，分配就是字面意义的 0，而不是"降到某个更小的常数"。

**⑤ 三个关键前提（缺一个就不是 0）：**
- **必须实现 `IEquatable<T>`/`IComparable<T>`，不能只靠非泛型版。**
  实测：普通 `struct` 未实现 `IEquatable<T>` 时，`EqualityComparer<T>.Default`
  退化成 `ObjectEqualityComparer<T>`，**每次查表装箱 4 次 = 96 B/次**；
  `record struct` 自动生成，实测 **0 B**。这也解释了为什么"结构体键"有时反而更慢。
- **字段必须是值类型。** 若结构体里仍放 `string`（如 `StrKey` 持 3 个 `string`），
  结构体本身仍在栈上、查表 0 分配，但**那 3 个 `string` 的分配一分没省**。
  真省下来靠的是"字段本来就是 `int`/`uint`"——`NLCPGNode` 的 5 个文本字段全是 `uint` id，
  这正是本仓库能走这条路的原因。
- **`string.GetHashCode` 不缓存**：实测 8 字符 21.1 ns、4000 字符 2648.0 ns
  （长度差 500 倍、耗时差 125.5 倍）⇒ **每次查表都重算全部字符**。
  所以结构体键还顺带省 CPU：`RecKey.GetHashCode` 1.9 ns vs
  `string.GetHashCode`（28 字符）17.6 ns，**约 9 倍**。

**⑥ `GetAlternateLookup` 那 40 B 的准确构成：**
省掉的**不是**哈希成本，而是"把 span 物化成 string"这一次分配。
实测：`alt.TryGetValue(span)` **0 B**；`dict.TryGetValue(new string(span))`
**48 B**（10 字符 ⇒ `align8(22+20)` = 48）；
真实仓库形态"从源文本切片造键"实测 `src.Substring(2,18)` 再查 **64 B**
vs `src.AsSpan(2,18)` 再查 **0 B**，耗时 15.47 → 13.00 ms（**1.19 倍**）。
⚠ 注意它**只省查表侧**：往表里**插入**仍然必须给出真正的 `string`，
所以该方案只对"查多插少"的 `StringInterner` 有效。

> ⚠ **口径提醒**：上表三行是**本机微基准**（net10.0，100 万次，Server GC），
> 用于比较**相对**代价，不是本次运行的归因。本次运行的归因见上一节
> `AllocationTick` 表（`NodeSortKey` 1.632 GiB / 73.65% of string churn）。
> 微基准的 320 B/次与实测反推的 **295–296 B/次**（1.632 GiB ÷ 592–594 万次）同量级，
> 差异来自样本字符串长度不同（本仓库真实全限定名更短）。
>
> ⚠ **`.NET 10` 的逃逸分析帮不上忙**：实测非逃逸的插值字符串**仍分配 56 B/次**、
> 非逃逸 `string.Concat` 仍分配 40 B/次，而非逃逸 `stackalloc Span<char>` 为 0 B。
> 即 .NET 10 的栈分配覆盖**数组**（`int[4]` 非逃逸时实测 40 B → 栈分配），
> **不覆盖 `string`**（`string` 不是数组，且其分配点在内联的
> `String.Ctor`/`FastAllocateString` 中）。**不能指望升级运行期消除这笔分配。**

### 判定边界（实测 vs 推断）

- **实测**：RAM 13.86 GiB；committed 25.32 GiB；private 24.5 GiB；LOH 18.04 GiB；
  工作集塌陷 6,543→407 MB 且私有内存不变；内核态 CPU 33.6%（长尾 38.6%）；
  GC 暂停 318.8 s（长尾占 88.7%）；累计分配 **93.73 GiB**（`dotnet.gc.heap.total_allocated`
  全部 341 个窗口求和 = 1,006.4 亿字节；计数器在此期间只产出 341 个窗口而墙钟
  1040 s 应有 520 个，缺的 179 个是 gcdump 抢占期未采到，故实际值只会更高）；
  LOH 桶占峰值样本字节 91.8%。
- **推断（无直接计数器）**：**本次未采集任何 page fault / paging 计数器**，因此
  “发生了换页”是由“工作集塌陷而私有内存不变 + 内核态 CPU 升高 + 堆/工作集比值达 38.6”
  三者共同推出的，不是直接测量。坐实需补采 `\Memory\Pages/sec`、
  `\Process(*)\Page Faults/sec` 或 ETW `Microsoft-Windows-Kernel-Memory`。
- **口径注**：工作集峰值三处读数略有差异——`Counters/` 8.38 GiB、`Threads/` 8.27 GiB、
  `runtime.log` 8.26 GiB，因为三者采样时点不同（工作集在数秒内即可变动数 GiB，
  见上条塌陷）。引用时应带来源，不宜混用。
- **不可定量**：`Win32_PageFileUsage.PeakUsage = 9424 MB` 无时间戳（系统已运行 40.5 h），
  与本次运行一致但不构成证明；Windows 事件日志在 17:25–18:00 窗口内**没有**
  内存耗尽/资源耗尽事件，说明未发生硬 OOM，是“换页导致变慢”而非“分配失败”。
- **观测污染**：37 次 gcdump 每次强制完整 Gen2 GC，且 16 次失败集中在后段——堆最大时
  采样器自身也在争内存。因此 25.32 GiB 的 committed 峰值与 318.8 s 暂停都**含观测开销**，
  方向不变但幅度被放大。

## 判定

本次运行**未完成**，不能作为 Version4 的性能或正确性验收：

- 分析在约 17.4 分钟时被停止，无 `evt=completed`，无 graph/query/rule/diff 产物；
- 因此没有吞吐、P95/P99 或端到端耗时结论，也没有 DOP 1/2/12 的语义等价比较；
- 采样本体会扰动被测进程（每次 gcdump 强制 Gen2 GC），线程利用率与 GC 数据是
  “带观测开销”的读数，不是纯净基线；
- `System.String` 等大户的实例数、以及 `System.UInt64[]` 的阶段性峰值是可用的诊断事实，
  但它们描述的是被采样干扰下的中间状态。

可直接引用的结论有五条：(1) 六个并行度与三个开关全部拉满时，进程平均约
1.1 / 16 核、峰值约 4.9 / 16 核，瓶颈不在并行度；(2) **并行阶段在 t≈192 s 就结束**
（`tpCompleted` 771 项/s → 0.54 项/s，`tpThreads` 25 → 5），此后约 595 s 是单线程阶段，
却仍然贡献了 91.7 GiB 累计分配中的 55.5 GiB（约 61%）——**约 82% 的运行时长与并行度无关**；
(3) 进程内累计分配 91.7 GiB、托管堆峰值 23.65 GiB、Working Set 峰值 8.26 GiB
（`runtime.log` 口径；`Counters/` 为 8.38 GiB，见“口径注”），
属于高瞬态分配形态；(4) `dotnet-trace` 将独占开销指向
`RuntimeMethodInfo.GetParameters()` 与 `Enumerable.ToDictionary`；
(5) **确认撞到内存墙**——committed/私有内存峰值 25.3/24.5 GiB 达物理内存 13.86 GiB 的 1.83 倍，
LOH 单独 18.0 GiB 且长期不降，内核态 CPU 占 33.6%，并观测到工作集由 6,543 MB 塌到 407 MB
而私有内存不变（换页签名）。低 CPU 主要是**内存墙的症状**，而非并行度不足。

因此本次最有价值的结论是**否定性的**：把 DOP 从 4/12 提到 12 并全开并行开关，
只能影响前约 3 分钟；对总时长的实际杠杆很小。优化优先级应为：
**(a) 消除 LOH 巨型数组**（`DataFlowPass` 的 O(N²) 位集，见“代码级根因”）；
**(b) 削减 t=192 s 之后的单线程路径开销**（反射取参与字典重建）。
在 (a) 完成前，单纯调高 DOP 不会改善结果。

采集侧还有一条可复用的工具结论：37 次 gcdump 尝试失败 16 次（43%），且失败高度集中
在运行后段——**早段（`early`）14 次尝试只失败 1 次，后段（`late`）23 次尝试失败 15 次**。
其中 `RootIndex not set` 13 次全部出现在托管堆涨到约 13.7 GiB 以后，且与成功采样交错，
因此 `dotnet-gcdump` 在本输入的大堆上不可靠但并非确定性失败。
“按类型统计大堆”必须接受采样点缺失，并把失败逐点记录，而不是假定每个采样点都有数据。

## 复现

```powershell
# 采集（toolsRoot 存放 GcdumpExactStats 与 harness 脚本，均在 Version4 之外）
pwsh -File D:\TRbackup\Version4\Build\NL-diagnostics-dop12\Invoke-NlVersion4Diagnostics.ps1 `
  -Tool nlissn -Version4Root 'D:\TRbackup\Version4' `
  -OutputRoot 'D:\TRbackup\Version4\Build\NL-diagnostics-dop12' `
  -RepoRoot 'D:\ProjectItem\SourceCode\Net\NL' -ToolsRoot 'D:\TRbackup\NL-diag-tools' `
  -Dop 12 -RunId version4-dop12-<timestamp> -SkipBuild `
  -TraceMode separate -TraceDurationSeconds 60 `
  -InitialIntervalSeconds 3 -InitialWindowSeconds 120 -LaterIntervalSeconds 30

# 精确按类型统计（必须用 GcdumpExactStats，不要用 report 的字节列）
D:\TRbackup\NL-diag-tools\GcdumpExactStats\bin\Release\net8.0\GcdumpExactStats.exe `
  --merge D:\TRbackup\Version4\Build\NL-diagnostics-dop12\version4-dop12-<timestamp>
```

`dotnet-gcdump report` 的第一列是**每个尺寸桶的代表尺寸**，不是该类型总占用
（例如 `byte[1024]×1 + byte[2048]×2 + byte[4096]×3` 只显示 `1,048  6`，真值 17,552），
且会截断类型列表。原始 `GcdumpReport/*.report.txt` 仅作留存，
其字节列不得当作“总占用”。

## 已知陷阱

- **`dotnet run` 对该输入挂起**：`dotnet run --no-build --project .\src\NLISSN\NLISSN.csproj`
  在 150 s 内只有 1.4 s CPU、19 个线程全部 `Wait`、主线程 `Wait UserRequest`、
  85 MB WS，无输出无产物。直接启动 `Build\src\Debug\net10.0\NLISSN.exe`
  则正常工作（80 s 内 110 s CPU、约 3.8 GB WS）。harness 因此直接启动 exe。
- **`dotnet-trace` 会污染 gcdump 类型名**（见“测量方案”），必须串行而非并发。
- `dotnet-counters` 必须带 `--refresh-interval 2`，因为计数器名字内嵌 `/ 2 sec`；
  `dotnet-counters list` 已失效（提示信息已迁移到在线文档，退出码 1）。
- `dotnet-gcdump` 的节点数上限在 `dotnet-gcdump.dll` / `TraceEvent.dll` 中未以常量暴露
  （反射查不到值为 10000000 的 literal），`10,000,000` 是三点实测的截断值；
  该截断不报错，只在 `GC Heap objects` 行显示整数上限，需与 `runtime.log` 交叉核对才能发现。
