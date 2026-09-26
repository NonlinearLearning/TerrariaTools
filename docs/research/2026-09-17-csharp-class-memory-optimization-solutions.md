# C# class 内存性能优化：方案、证据与高密度图应用

> 日期：2026-09-17。性质：联网研究与优化建议，未实施生产代码改造，未测量本项目的性能收益。
>
> 本报告回答“如何解决 class 的内存性能问题”。前置材料：[class/struct 原理研究](2026-09-17-csharp-class-struct-density-research.md)、[联网来源笔记](2026-09-17-csharp-class-memory-performance-web-research-sources.md)、[NLCPG 模型审查](2026-09-17-nlcpg-class-struct-memory-assessment.md)。

## 结论

可以保留需要身份、共享和多态的 class，同时通过减少重复数据、引用数量、临时分配和同时存活的中间表示，改善内存和访问性能。若最终证实瓶颈是大量小对象自身的开销及访问间接性，再评估连续值数组或列式存储。

最直接相关的一手证据是 Microsoft 的 LOH 文档：其二叉树示例保留 `class Node`，把左右节点引用改成整数索引，以减少 GC 需要追踪的引用。[S1] 这支持调查高密度图的存储表示，但不保证查询更快或整个进程内存减半。

池化、字符串去重、紧凑索引和 GC 调整解决的是不同问题。每个方案必须匹配实际的对象数量、生命周期及访问模式。

## 先辨认成本

| 指标或症状 | 含义 | 优先调查 |
| --- | --- | --- |
| 累计分配或分配速率很高，回收后堆较小 | 大量短命对象反复创建 | 临时字符串、数组、闭包、包装对象、扩容复制 |
| 完成分析后仍有大量存活对象 | 有效数据大，或存在不必要的持有关系 | 对象字段、字符串重复率、索引、缓存根路径 |
| 构建峰值远高于完成后占用 | 多个阶段或多个任务的中间表示重叠 | 流式提交、并发度、缓冲容量、释放时机 |
| Gen2/LOH 压力大 | 大对象分配、存活或碎片可能影响回收 | 大数组、长字符串和反复创建的缓冲区 |
| 内存尚可但扫描慢 | 可能受间接访问和数据布局影响 | 热字段、访问顺序、CPU profile、布局对照实验 |

累计分配量是“流过多少字节”，不等于同时存活的字节数。工作集还包含托管堆之外的驻留内存；GC 已提交空间也不等于活对象大小。`dumpheap -stat` 的类型数量和总大小不能直接当作该类型独占保留的整个对象图大小；共享子对象和根路径需要另外分析。[S2]

## 1. 让共享对象只存一份，二级索引使用整数序号

**适用情形：** 大量实体同时由多个数组、邻接表或查询索引引用。

保留一份节点/边主体数据，二级索引存储 `int` ordinal；访问时再到主体表获取对象。业务稳定 ID 和内存数组位置必须分开定义，避免把重排后会变的序号当成持久化身份。[S1]

这有两种独立收益：索引元素可能更小；纯整数数组无需逐元素追踪托管引用。主体对象和主体引用表仍有 GC 成本。将 `Node[]` 作为主体表不会让独立的 Node 对象自动连续，也不会去掉它们的对象头。

**透明估算，非实测：** 假定目标运行时每个对象引用为 8 bytes、每个整数为 4 bytes，四个各含 4,000,000 个元素的二级引用数组改为整数数组，元素区减少：

```text
4 × 4,000,000 × (8 - 4) = 64,000,000 bytes ≈ 61.0 MiB
```

此数不包含数组头、容量差、主体对象和新增映射表；不能推导整个进程节省 50%。额外查表可能损失查询速度，应同时测构建、冻结、遍历和随机查询。删除、重排、序号溢出及快照有效期也必须有明确规则。

## 2. 共享重复文本和元数据，并按需引入 ID 表

**适用情形：** 多个实例分别创建内容相同的路径、名称、签名或标签。

先验证“相同内容是否确实由不同实例保存”。多个字段早已引用同一个字符串时，再增加去重表没有这部分收益。

| 表示 | 可减少的成本 | 代价 |
| --- | --- | --- |
| 共享字符串实例 | 重复字符串对象及字符内容 | 每个对象仍保存引用；查找和去重表有成本 |
| 对象保存 FileId/NameId，字符串集中存储 | 重复文本以及对象中的引用字段数量；字段宽度可能下降 | 访问多一次定位，需维护 ID 与生命周期 |
| 热字段集中，少见元数据放在旁表 | 稀疏字段占用及热扫描的数据量 | 旁表、额外对象和查找也可能抵消收益 |

前两种的字符串复用机制有官方 API 依据；热/冷字段拆分属于据此提出的工程假设，收益须由字段使用比例和访问轨迹证明。[S3][S4]

`String.Intern` 需要先创建输入字符串，且驻留字符串的内存可能直到 CLR 退出才释放，不宜对无限输入默认使用。[S3] 可评估单次分析拥有的字符串表，或 CommunityToolkit 的有容量上限的 `StringPool`。后者支持从 `ReadOnlySpan<char>` 查找，在命中时避免先构造新字符串；容量满后会淘汰条目，是尽力复用，不能作为永久引用身份契约。[S4] CommunityToolkit 是额外包，本次没有引入依赖。

只把 `int` 缩为 `short`，或重新排列字段，也未必缩小最终实例：对齐、实际字段布局和对象最小尺寸仍影响结果。应核查范围约束与托管布局，不用封送大小代替托管对象及其引用对象图大小。

## 3. 降低构建峰值：限制在途数据，尽早结束中间表示的生命周期

**适用情形：** 最终图不大，但构建时同时保留输入、候选集、排序副本、去重表和最终索引。

对候选生成、提交、索引建立和导出分别统计峰值与阶段分配，评估分批处理和有界队列。消费者处理不过来时应限制生产速度；仅把 API 改成流式、下游却仍收集全部结果，不会降低这部分最终占用。

在后续阶段确实不再需要时解除构建缓存与中间表示的持有关系。GC 能回收的是不再可达的对象，不能靠强制 GC 回收仍被缓存保留的数据。[S2] 本建议不能用于提前释放仍承担稳定 ID、去重或图语义职责的数据。

已知数量时为 `List<T>` 设置合理容量，减少扩容及复制。官方文档确认扩容会重新分配内部数组并复制元素，主动缩容也会复制；`Clear()` 不等于释放容量。[S5] 不要在循环里反复 `TrimExcess()`，也不要按极端最大值为每个并发任务预分配。

## 4. 用 Span 等视图消除临时切片与复制

**适用情形：** 解析、比较或扫描只临时查看一段现有数据。

当下游有 `ReadOnlySpan<char>` 重载时，用 `AsSpan()` 替代为了传参而调用的 `Substring()`。官方规则 CA1846 明确指出，普通子串提取会产生新字符串和文本复制，而 span 可以避免这些成本。[S6]

Span 是现有内存的视图，不是主体数据的压缩器。需要长期保存的结果仍要有拥有数据的存储；用 Memory/切片长期引用大缓冲区，也可能使整个缓冲区继续存活。异步与跨组件场景必须遵守 owner、consumer 和 lease 的生命周期。[S7]

临时 `ToArray`、`ToList`、格式化、迭代器及捕获闭包也应以 profiler 结果决定是否优化；无需把所有易读抽象改成手写循环。

## 5. 池化高频临时缓冲区，谨慎池化对象

**适用情形：** 反复创建的数组或初始化昂贵、可彻底重置的工作对象。[S8][S9]

- `ArrayPool<T>` 可以降低频繁创建销毁数组带来的分配压力；租借长度可能大于请求长度，应另存逻辑长度。
- 归还含托管引用的数组时，需要按所有权清理引用，防止池保留数组时继续持有旧图。`Return(..., clearArray: true)` 为池保留并复用的数组提供清理机制。归还后不能继续访问，且每次租借只归还一次。[S8]
- 池会保留内存；每次操作的分配量下降，不保证空闲或峰值工作集下降。大缓冲区的容量上限、清零成本和池竞争都需要评估。
- `ObjectPool` 官方明确要求先采集真实场景性能数据；廉价对象的获取/归还可能比创建更慢，保留上限也不是总创建数量上限。[S9]

对几百万个同时存活的图节点，池化不能减少同时必须存在的数量。可优先调查临时排序缓冲、导出缓冲和可重置的 builder，而非默认池化整个领域模型。

## 6. 若小对象开销主导，再试连续值存储或列式存储

**适用情形：** 测量已证明对象数量、头部/对齐与访问间接性占主导，并且热点只需要少量紧凑字段。

可比较一个只保存一份的小型值记录数组，以及将 SourceId、TargetId、Kind 分成独立数组的列式存储。二级索引仍保存序号。后者属于待验证的工程方案：只扫描某列时可能减少无用读取，但需要同时访问全部字段或频繁插删时可能增加成本。

必须将“更换底层表示”与“仅把 class 关键字替换成 struct”区分开。大型 struct 被复制到多个容器，会重复完整字段；其中的字符串仍只是引用，不会变成内联字符。方法中的 `ref`/`in` 能避免部分传参复制，却不能消除多个容器各存一份值的占用。[S10]

边界包括值语义、引用身份、装箱、共享可变状态、序号有效期和序列化。上层可以保留 class 门面，但若为每条记录仍永久创建一个门面对象，这部分对象成本依然存在。

## 7. 运行时与 GC 调整作为独立对照实验

.NET 10 扩大逃逸分析的适用范围，能消除部分临时对象、委托或数组的堆分配。[S11] 这是 JIT 在能够证明不逃逸等条件下的优化，并非任意 class 都能自动栈分配。构建后仍被图容器持有的节点不能依赖这种优化消失。

DATAS 在 .NET 8 引入、.NET 9 起默认启用，调整 Server GC 的堆数量和分配预算，使堆规模适应长期存活数据与负载变化。[S12][S13] 它不会缩小字段或回收仍需使用的节点，吞吐与内存收益需要同时测量。默认启用不代表项目没有配置覆盖，应检查实际运行模式。

`HeapHardLimit` 限制 GC 堆和记账内存的提交量，不能作为整个进程的内存硬上限，也不能令超过容量的必要活数据自动变小。[S13] `GC.Collect()` 不应作为常规优化；诊断时的强制回收与正常时延测量应分开。[S2]

## NLCPG 的验证入口与建议顺序

2026-09-17 回读工作区确认：[NLCPGNode](../../src/NLCPG/Model/NLCPGNode.cs) 保留多个字符串及可空字段；[NLCPGEdge](../../src/NLCPG/Model/NLCPGEdge.cs) 包含可选上下文数据。边的端点已经是 NodeId，本建议不是再把端点引用替换一遍。

[NLCPGGraphIndex](../../src/NLCPG/Model/NLCPGGraphIndex.cs) 建立一个排序边数组、四个 CSR 边数组及一个按 kind 的边数组；[CpgShardContracts](../../src/NLCPG/Persistence/CpgShardContracts.cs) 已有整数入边索引。优先试验的位置是内存图的二级边索引。源码只证明该表示存在，不证明它是最大内存来源。

[NLCPG 项目](../../src/NLCPG/NLCPG.csproj) 已面向 net10.0；“升级到 .NET 10”因此不是尚未采用的通用解药。目标框架也不能单独证明部署时的运行时版本或 GC 配置。

建议按测量结果分支，而非机械完成所有优化：

1. 固定输入、实际 SDK/runtime、配置、能力集与并发度，记录构建、冻结、查询阶段。
2. 同时采集每次操作分配、阶段峰值、存活对象统计、GC 暂停和工作集；堆快照用于类型及根路径归因。[S2]
3. 若临时分配主导，先处理重复复制、容量与在途中间数据，再评估池化。
4. 若重复字符串主导，比较共享字符串与任务范围 ID 表，并计入表本身的开销。
5. 若引用索引主导，先对一种二级边索引试验 ordinal，测净节省及查询回归。
6. 若小对象主体仍主导，再做紧凑记录/列式存储原型，同时保留语义与持久化边界验证。

每次只改变一个因素；使用预热和重复样本，至少比较构建与代表性查询时间、每次分配、峰值/存活堆和 GC 暂停。验证图、规则决策、改写、并发与重载等价性。微基准的分配减少不能替代整个分析流程的收益证据。

## 来源与可信边界

本轮先通过互联网搜索定位 Microsoft Learn 和 .NET Blog，再直接阅读页面正文。以下资料支持运行时机制和 API 契约；本报告的方案排序、大小估算、列式与字段拆分提案属于工程推导，不是官方为 NLCPG 做出的性能保证。未采纳旧版 .NET Framework 文章中的固定对象大小或历史跑分作为当前 .NET 10 结论。

- [S1：Large object heap — 引用数组与节点整数索引示例](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap#loh-performance-implications)。该页讨论 Windows；默认 85,000 bytes 阈值含对象自身总大小，不宜推成全部配置下的语言常量。
- [S2：Debug a memory leak in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-memory-leak)：计数器、类型统计、dump 和根路径；另见 [GC.Collect 的使用边界](https://learn.microsoft.com/en-us/aspnet/core/performance/memory?view=aspnetcore-10.0#review-caveats-when-using-gccollect)。
- [S3：String.Intern — Performance considerations](https://learn.microsoft.com/en-us/dotnet/api/system.string.intern?view=net-10.0#performance-considerations)。
- [S4：CommunityToolkit StringPool](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.highperformance.buffers.stringpool?view=dotnet-comm-toolkit-8.4)。
- [S5：List<T>.Capacity](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.capacity?view=net-10.0)。
- [S6：CA1846 — Prefer AsSpan over Substring](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1846)。
- [S7：Memory<T> and Span<T> usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)。
- [S8：ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1?view=net-10.0) 与 [Return 契约](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0)。
- [S9：Object reuse with ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool?view=aspnetcore-10.0)。
- [S10：Reduce memory allocations using new C# features](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/)。
- [S11：Performance Improvements in .NET 10 — Object Stack Allocation](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)。文中微基准只证明所展示的场景。
- [S12：Dynamic adaptation to application sizes (DATAS)](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/datas)。
- [S13：Garbage collector configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)：DATAS、模式、堆硬限制与版本范围。

本次交付的完成条件是可追溯的联网研究报告；上述性能实验属于未来实施的验收要求。本次没有声称 class 优化已落地，亦没有承诺固定节省比例。
