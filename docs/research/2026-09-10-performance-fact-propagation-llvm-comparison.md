# NLCPG -> Application -> Directory -> Host 性能事实传递研究

> 状态：研究完成；本次只新增研究文档，未修改生产代码。
>
> 日期：2026-09-10。
>
> 问题：性能事实如何从 NLCPG 传到 Application、Directory、Host？LLVM 和相近的分析工具如何传递同类事实？

## 结论先行

当前项目还没有形成完整的性能事实传递闭环。现状是：

1. NLCPG 已经产生了比 Host 能看到的更多事实。NLCPGBuilder.LastBuildMetrics 包含 pass、anchor discovery、persistence、cache、data-flow、node 和 edge 信息。
2. Application 在构图后只把 node count 和 edge count 投影到 PrototypeAnalysisResult.GraphMetrics；完整的 LastBuildMetrics 没有跨过 Application 边界。
3. Directory 的逐文件结果仍然持有每个文件的 PrototypeAnalysisResult，但 BuildResult 和 CombineResults 构造目录级 PrototypeAnalysisResult 时没有聚合 GraphMetrics、RuleGraphTelemetry 或 CPG pass metrics。
4. DirectoryAnalysisService 和 WorkspaceAnalysisService 最终只把聚合结果返回给 Host，逐文件结果不再可见。
5. Host 的 RuntimeMeasurementLog 只从最终结果读取图规模、规则图峰值和业务计数；它不会读取 NLCPGBuilder，也没有 pass 级或文件级 CPG 性能事实。

因此目前真正存在的链路更接近：

    NLCPGBuilder.LastBuildMetrics
        -> Application: 只投影 GraphMetrics(node, edge)
        -> Directory: 逐文件暂存，但目录摘要丢弃 GraphMetrics
        -> Host: 记录运行时采样、并发池摘要和有限的最终计数

建议的目标链路是：

    NLCPG
        -> CpgPerformanceFacts（中立、不可变、单文件）
        -> ApplicationPerformanceFacts（CPG + RuleGraph + Rewrite）
        -> DirectoryPerformanceFacts（逐文件明细 + 确定性聚合）
        -> RunPerformanceReport（Host 负责运行身份、根阶段和输出）

关键原则是：性能事实走旁路，不进入 CPG 语义对象、Mark/Decision，也不让 Host 反向访问低层实现。低层只发布它拥有的事实；每一层只做一次窄投影或聚合。

## 当前实现取证

### NLCPG：事实已经存在，但只挂在 Builder 上

[NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs:82) 定义的 NLCPGBuildMetrics 已包含：

- Operation node/root cache hit 和 miss；
- NodeCount、EdgeCount、总构建耗时；
- PassElapsedMilliseconds；
- AnchorDiscoveryAnchorCount、AnchorDiscoveryElapsedMilliseconds 和 anchor pass 时间；
- NLCPGPersistenceMetrics；
- DataFlowMethodMetrics；
- BuildInventoryMetrics。

[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:382) 在 Build 完成时创建 LastBuildMetrics。[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs:160) 的 SemanticModel 路径还会执行 anchor discovery，并把预检时间和预检 pass 计时合并回 LastBuildMetrics。

这说明 NLCPG 内部已经有性能事实生产者。问题不在于“没有测量”，而在于事实的所有权和出口：LastBuildMetrics 是 Builder 的可变属性，BuildFromSemanticModel 返回值只有图，没有同时返回 metrics。调用方必须在构图后立即读取它，否则事实就无法继续向上游传播。

### Application：只保留图规模，丢弃 CPG 阶段事实

[ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs:206) 创建 NLCPGBuilder，设置 capability 和 CPG 并发度，然后调用 BuildFromSemanticModel。构图返回后，它创建 AnalysisSession 和 CpgAnalysisContext，但没有读取 builder.LastBuildMetrics。

随后 [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs:129) 创建 PrototypeAnalysisResult。当前只填充：

    GraphMetrics = new CpgGraphMetrics(
        graph.Nodes.Count,
        graph.Edges.Count)

[PrototypeAnalysisResult.cs](../../src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs:12) 明确把 GraphMetrics 描述为“本次单文件分析构建的完整 CPG 规模”；同一个结果还可以携带 RuleGraphTelemetry、RuleGraphMetrics、Evidence 和业务输出，但没有 CPG build metrics 字段。因此 Application 当前做的是低信息量的 snapshot，而不是性能事实传递。

规则图是一个相对完整的对照。RuleGraphExecutor 在 [RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs:181) 为每个节点保留输入数量、输出数量、elapsed 和状态，再由 Application 写入 PrototypeAnalysisResult。这证明“低层生产不可变 telemetry，Application 作为结果的一部分向上传递”的方向已经在规则图上使用，只是还没有扩展到 NLCPG。

### Directory：逐文件事实存在，聚合出口丢失

[DirectoryAnalysisUseCase.cs](../../src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs:32) 定义 DirectoryFileAnalysisResult，内部包含一个 PrototypeAnalysisResult。因此单文件分析返回的 GraphMetrics 和 RuleGraphTelemetry 在 fileResults 中仍可用。

但是 [DirectoryAnalysisUseCase.cs](../../src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs:302) 的 BuildResult 聚合 Mark、Propagation、Lift、Decision、Edit、Diff 和业务 AnalysisStats，却没有聚合 GraphMetrics、RuleGraphTelemetry、RuleGraphMetrics 或未来的 CPG facts。结果对象中的 GraphMetrics 保持默认 null。

Workspace 路径还会经过第二次聚合。[WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs:36) 对每个 project 生成结果，然后 [WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs:74) 调用 DirectoryAnalysisUseCase.CombineResults。CombineResults 同样只组合业务结果和 Evidence，不组合性能事实。

这不是并发顺序问题。Directory 使用稳定 Index，并发池按输入顺序返回结果；真正的问题是聚合模型没有性能字段。对于性能报告，必须同时保存：

- 每个文件的原始事实，供 hotspot 和异常定位；
- 目录或项目级的 sum、max、wall 和计数；
- 并发子项的累计工作量与父阶段 wall time 的区别。

### Host：只拥有运行边界，不应该重新计算低层 metrics

[CommandHost.cs](../../src/NLISSN/Hosting/CommandHost.cs:38) 在选择 directory、workspace 或 file 分支前创建 RuntimeMeasurementLog。分析完成后，所有路径都调用 CompleteRuntimeLogAsync；该方法先按配置写 Evidence，最后把 PrototypeAnalysisResult 交给 RuntimeMeasurementLog。

[RuntimeMeasurementLog.cs](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs:163) 当前 terminal fields 包含：

- 端到端 elapsed；
- seed、propagated、lifted、decision、edit、diagnostic 数量；
- result.GraphMetrics 的 node、edge；
- RuleGraphMetrics 的 ready/concurrent peak；
- 并发池 operation、queue wait、峰值 buffer、峰值 retained records；
- 5 秒一次的 allocation、heap、working set、GC 和 ThreadPool sample。

它没有 pass elapsed、anchor discovery、persistence hit/miss、data-flow method detail、逐文件时延或 diff/evidence/write-back 阶段时延。更重要的是，DirectoryAnalysisService 只返回 outcome.Result：[DirectoryAnalysisService.cs](../../src/NLISSN/Hosting/DirectoryAnalysisService.cs:21)，所以 Host 拿不到 DirectoryAnalysisOutcome.FileResults 来自行补救。

Host 的职责应保持为创建一次 Run 的身份和根阶段、接收最终报告、写出稳定制品。它不应通过类型转换、反射或重新扫描结果来“猜” NLCPG 阶段指标。

## LLVM 和相近设计的可迁移模式

### LLVM New Pass Manager：Instrumentation 是旁路，IR 不是载体

LLVM 官方源码 [PassInstrumentation.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/IR/PassInstrumentation.h) 的设计是：

- PassInstrumentationCallbacks 注册 before/after pass、analysis、invalidated 等回调；
- PassManager 在执行 pass 时传递稳定的 pass name 和 IRUnitRef；
- instrumentation 回调接收 const IR unit，避免性能观测器意外修改 IR；
- PassInstrumentationAnalysis 把 instrumentation 能力注册到各级 PassManager。

[PassTimingInfo.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/IR/PassTimingInfo.h) 中的 TimePassesHandler 使用这些 pass-instrumentation callbacks 测量执行时间，把数据保存到 pass/analysis 的 timer，并通过 registerCallbacks 接入执行器。计时器内部还保留 active pass stack，因此嵌套 pass 不需要让每一个 pass 手工把计时传给父 pass。

映射到 NLCPG：

    NLCPG pass
        -> before/after stage event
        -> CpgPerformanceFacts
        -> Application 结果摘要

可迁移的是“instrumentation 旁路 + 不可变上下文 + 在上层聚合”，不是复制 LLVM 的 C++ API。NLCPG 的 pass 不应把 RuntimeMeasurementLog 当参数层层传下去，也不应因为性能采集而改变 CPG。

### LLVM time trace：用父子阶段关联，而不是扁平 elapsed

LLVM 的 [TimeProfiler.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/Support/TimeProfiler.h) 提供 TimeTraceProfiler 和 TimeTraceScope，用作用域的开始/结束记录嵌套事件，最后由工具侧写出结构化 time-trace。这个模型适合当前问题中的：

    Workspace
      -> Project
        -> Directory
          -> File
            -> CPG.Syntax / CPG.Operation / Rule.Propagate

迁移时应保留 parentStageId、stageId、itemId 和 status。不能把所有子阶段 wall time 直接求和作为父阶段时间，尤其是 Directory 和 CPG 存在并发时；并发子项的累计时间应另存为 accumulatedElapsedMs。

### LLVM llvm-mca：事件生产者和报告 View 分离

LLVM [llvm-mca View.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/MCA/View.h) 将 View 定义为 HWEventListener；Pipeline 发布执行事件，Summary、DispatchStatistics、ResourcePressure 等不同 View 各自消费并输出一个报告切面。主程序负责创建 pipeline、挂接 View 和选择输出格式。

对 NLISSN 的直接启发是把以下对象分开：

- CPG/规则组件：发布窄的 stage 或 node facts；
- summary view：输出端到端和顶层阶段摘要；
- resource view：输出 allocation、GC、working set、pool peak；
- hotspot view：按文件、pass 或 rule node 排序；
- diagnostic attachment：关联 dotnet-trace、counters 或 gcdump。

不要把 RuntimeMeasurementLog 变成所有领域 metrics 的唯一 owner，也不要让一个 JSON writer 反向依赖每个 CPG pass 的实现类。

### LLVM LNT 和 LLVM Test Suite：样本身份与阶段边界先于聚合

LLVM LNT 的官方 [test_suite.py](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py) 将 compile、exec、profile、diagnose 分成不同路径，并把测试 identity、warmup、measurement 和诊断状态写进结果。LLVM Test Suite 的 [CMakeLists.txt](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt) 同时暴露 compile time、RSS、code size、statistics 和诊断 flags。

可迁移的不是 native compiler 的字段名，而是三条规则：

1. 每条性能事实必须有 input/item identity、环境和样本语义；
2. compile/build、execution/rule、artifact/write-back 等阶段要有独立边界；
3. profile 和深诊断不能静默混入 normal 或 benchmark 样本。

这与当前 [性能分析组件设计](../CodeDesign/目前设计/性能分析组件.md:79) 中的 run identity、stage sample、terminal summary 和 normal/diagnostic/profile/benchmark 模式一致，但该设计还没有规定 NLCPG 的 facts 具体如何经过 Application 和 Directory。

### Joern：按文件收集，输出前稳定排序

Joern 官方 X2Cpg 的 [Report.scala](https://github.com/joernio/joern/blob/main/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/Report.scala) 为每个文件保留解析状态、CPG 状态和 duration，并在输出报告前按稳定 identity 排序。

这与当前 DirectoryAnalysisUseCase 的稳定 Index 机制相似。需要补上的不是另一个调度器，而是将 file result 中的性能事实纳入 BuildResult/CombineResults，保留“逐文件明细 + 目录聚合”两种形态。

## 推荐的跨层传递契约

### 两条通道

推荐采用混合模型：

1. 完成摘要通道：每层通过不可变结果 DTO 返回已经完成的事实。这是 normal 和 benchmark 必须依赖的通道。
2. 诊断事件通道：在 diagnostic/profile 模式下，由可选 PerformanceEventSink 接收低层 stage、partition 或 rule-node 事件。这是高基数数据通道，sink 失败只能丢弃诊断数据，不能让分析失败。

不要把二者混成一个可变全局 collector。完成摘要用于结果正确性和稳定报告；事件通道用于定位细节。

### 四层的最小对象

建议在 NLISSN.Core 中定义与 NLCPG 实现解耦的中立类型，Application 在边界处做一次映射：

    CpgPerformanceFacts
      itemId
      sourceIdentity
      buildElapsedMs
      passSamples[]
      anchorDiscovery
      persistence
      cacheCounters
      dataFlowMethodSamples
      nodeCount / edgeCount

    ApplicationPerformanceFacts
      itemId
      cpg: CpgPerformanceFacts
      ruleGraph: RuleGraphPerformanceFacts
      rewrite: RewritePerformanceFacts
      status

    DirectoryPerformanceFacts
      itemId = directory/project identity
      children: ordered ApplicationPerformanceFacts[]
      stageSummary
      graph/rule/artifact snapshot

    RunPerformanceReport
      runId / input identity / environment
      root stage
      directory or workspace facts
      terminal summary
      diagnostic attachment references

这里的 CpgPerformanceFacts 不能直接暴露 NLCPGBuildMetrics 作为公共跨层契约。原因是 NLCPGBuildMetrics 属于 Builder 实现，未来字段变化不应迫使 Host 依赖 NLCPG.Builder；映射还可以把 diagnostic-only 的 DataFlowMethodMetrics 控制在需要的模式中。

### 每层应该做什么

| 层 | 输入 | 允许做的事 | 不应做的事 |
| --- | --- | --- | --- |
| NLCPG | SemanticModel、builder options、pass 执行 | 记录 pass/anchor/persistence/cache/graph facts，构造 CpgPerformanceFacts | 依赖 Host、写最终报告、修改 CPG 语义 |
| Application | CPG facts、规则图 telemetry、rewrite 结果 | 绑定 item identity，组合单文件性能摘要 | 重新测量或读取已释放的 Builder 状态 |
| Directory | 有序 file results、并发池 telemetry、materialization | 保留 children，按稳定 identity 聚合 sum/max/wall/count | 只保留目录总 elapsed 并丢弃文件事实 |
| Host | 最终结果、配置、运行时采样、artifact boundary | 创建 run identity，写 summary/events/attachments，报告失败状态 | 反向访问 NLCPGBuilder、重算 pass 计时、让 telemetry 失败影响分析 |

### 聚合规则

- wallElapsedMs 表示该 scope 的真实端到端墙钟时间；
- accumulatedElapsedMs 表示并发子操作的累计工作量，可以大于父阶段 wall time；
- sum、max、p50/p95、top item 都要有明确的聚合层，不能把一个 elapsed 字段复用成所有含义；
- unknown、disabled、zero、failed、cancelled、skipped 分开表示，未启用 persistence 不能写成 persistence elapsed 为零；
- children 按 source identity 或稳定 Index 排序，不按完成顺序排序；
- aggregate 必须保留输入 identity、rule profile、capability fingerprint、DOP、cache mode 和 sample mode；
- 性能 facts 的异常或输出失败不能改变 CPG、规则、decision、rewrite 和 diff 语义。

## 对当前项目的最小落地顺序

这不是本轮实现计划，而是根据当前边界整理出的最小改动顺序：

1. 在 Core 定义中立的单文件性能 facts，并给 PrototypeAnalysisResult 增加一个可选 Performance 字段。Application 在读取 builder.LastBuildMetrics 的同一调用栈中完成映射。
2. 为 DirectoryFileAnalysisResult 保留该字段；BuildResult 和 CombineResults 显式实现性能 facts 的 deterministic combine，同时保留 per-file children。
3. 为 DirectoryAnalysisService 的 materialization 和 WorkspaceAnalysisService 的 load/project/document 边界增加 parent/child stage facts；不把 Workspace loader 事实塞进 CPG facts。
4. 扩展 RuntimeMeasurementLog 或新建报告 writer，使 Host 接收 RunPerformanceReport 后写 terminal summary 和可选 per-item events。RuntimeMeasurementLog 继续拥有轻量 process/pool sample，不拥有所有领域 metrics。
5. 用测试锁定传递而非只锁定数值存在：
   - CPG builder 的 pass/anchor/persistence metrics 被映射到单文件 result；
   - Directory 并行与串行的 children 顺序和聚合结果一致；
   - Workspace 多 project/多 TFM 聚合不丢 item identity；
   - Host terminal report 含 CPG stage、rule stage、artifact stage 和 root wall time；
   - sink 抛异常或写盘失败时业务结果仍完成；
   - graph、decision、evidence、rewrite、diff snapshot 与未启用 instrumentation 时等价。

## 最终判断

当前项目最接近 LLVM 的正确做法是：把 NLCPG 当作性能事实生产者，把 Application 当作单文件边界适配器，把 Directory 当作稳定的 fan-in 聚合器，把 Host 当作 run/report 边界。性能事实不能依靠“把一个 profiler 对象传过四层”来实现，也不能只在 Host 重新测一次总时间。

最值得先修的断点是 Application 读取并映射 LastBuildMetrics，以及 Directory/Workspace 聚合时保留逐文件性能 facts。完成这两步后，Host 才可能可靠地报告 CPG pass、规则节点和 artifact 阶段，而不是只看到一个最终总耗时和几个计数器。

## 参考资料

### 当前项目

- [性能分析组件设计](../CodeDesign/目前设计/性能分析组件.md)
- [分析流程耗时性能研究报告](2026-09-09-analysis-pipeline-performance-study.md)
- [NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs)
- [NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs)
- [ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs)
- [PrototypeAnalysisResult.cs](../../src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs)
- [DirectoryAnalysisUseCase.cs](../../src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs)
- [DirectoryAnalysisService.cs](../../src/NLISSN/Hosting/DirectoryAnalysisService.cs)
- [WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs)
- [CommandHost.cs](../../src/NLISSN/Hosting/CommandHost.cs)
- [RuntimeMeasurementLog.cs](../../src/NLISSN/Telemetry/RuntimeMeasurementLog.cs)
- [RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs)

### 外部一手资料

- LLVM [PassInstrumentation.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/IR/PassInstrumentation.h)
- LLVM [PassTimingInfo.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/IR/PassTimingInfo.h)
- LLVM [TimeProfiler.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/Support/TimeProfiler.h)
- LLVM [llvm-mca View.h](https://github.com/llvm/llvm-project/blob/main/llvm/include/llvm/MCA/View.h)
- LLVM [LNT test_suite.py](https://github.com/llvm/llvm-lnt/blob/main/lnt/tests/test_suite.py)
- LLVM [Test Suite CMakeLists.txt](https://github.com/llvm/llvm-test-suite/blob/main/CMakeLists.txt)
- Joern [X2Cpg Report.scala](https://github.com/joernio/joern/blob/main/joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/utils/Report.scala)
