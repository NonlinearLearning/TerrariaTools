# CPG 查询、构图与运行时性能后续执行计划

> **状态：当前执行。** 本页取代同主题的 2026-07-15 至 2026-07-16 逐项提案。已完成取舍见 [`设计docs/优化历史/2026-07-15至16-CPG查询构图与内存优化归档.md`](../../设计docs/优化历史/2026-07-15至16-CPG查询构图与内存优化归档.md)。

## 目标

完成仍处于 `in_progress` 的查询/切片、稳定提交、线程池内存、目录 I/O、声明符号查询和 Mark snapshot 优化。任何默认值调整都要以图、规则和 rewrite 等价性加真实工程实测为依据。

## 不可变边界

- worker 只收集 Roslyn semantic facts；图、cache 和顺序只由稳定调用线程提交。
- DOP 1、8、12、14、16 的完整图快照必须一致；查询截断必须可见且不得污染完整结果缓存。
- `MarkAnalysisSnapshot` 的缓存和 per-rule telemetry 仅观察或复用既有事实，不改变 seed mark、binding 优先级、decision 或 rewrite。
- 异步目录读取、计时日志与辅助任务必须受界；失败、取消或未完成写入不能被报告为成功。

## 当前事实

- 冻结索引、预算化 slice、DataFlow 中间态压缩、强类型 `ContextId` 与结构化 callsite 上下文已落地。
- 有界候选生成和稳定提交已落地；目前未验证真实工程的 warmed 中位数，默认 DOP 仍保持 `Environment.ProcessorCount`。
- 声明符号查询已收紧到可声明语法；缺少本工作区外的 WorldGen 实测。
- `MarkAnalysisSnapshot` 已缓存 graph binding、候选、operation、目标匹配和 region facts；per-rule ledger 仍需在当前工作树可编译后取得验证证据。
- 目录异步 I/O 与 channel 日志仍是提案，只有在存在调用线程响应性需求时才推进 P3 异步 API。

## 执行顺序

1. 先恢复当前工作树的 focused build，并为 Mark ledger 补齐串行/组并行输出等价和稳定 telemetry 回归。
2. 补齐候选缓冲释放、DataFlow/查询/Mark 的 compute、commit、cache 和截断遥测；不扩大规则语义。
3. 以固定输入运行 DOP 1、8、12、14、16 图快照等价回归；随后在可用的真实工程上每个 DOP 预热后独立运行三次，记录中位数。
4. 仅在上述证据显示无图/规则/rewrite 回归且满足时间与内存门槛时，裁决默认 DOP 或启用额外并发。
5. 目录异步加载只在单独的 fixture 中证明有界加载、源序完整和正常完成后一文件一条日志；未满足响应性需求时保持同步入口。

## 完成条件

- `feature_list.json` 中下列 feature 的 definition of done 均有对应的当前证据：`cpg-query-and-overlay-performance`、`threadpool-memory-execution`、`stable-graph-commit-parallelization`、`directory-io-and-async-api-optimization`、`minimal-roslyn-cpg-declared-symbol-query-optimization`、`roslyn-deletion-prototype-mark-analysis-snapshot-optimization`。
- 真实工程实测不可用时，明确保留未验证项，不将 feature 标为完成，也不改变默认值。

## 验证

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~PipelineComponentTests"
pwsh -File .\scripts\check-harness-consistency.ps1
```

在有真实源和既有基准脚本时，再运行三次 warmed DOP 比较；报告 wall-clock、managed heap、working set、节点/边计数和图/规则/rewrite 等价性。
