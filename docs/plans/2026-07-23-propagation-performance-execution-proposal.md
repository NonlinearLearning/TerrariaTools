# 传播阶段性能修复执行提案 Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**目标：** 在保持 Mark、PropagatedMark、LiftedMark、Decision、Rewrite 和同组链式传播语义不变的前提下，消除传播阶段无条件结构视图构建、组内线性去重和重复语义扫描造成的可扩展性风险，并取得可归因的真实源码测量。

**架构：** 先为 `PropagationEngine` 建立规则/组级遥测与等价基线；再由规则显式声明是否需要 `StructureView`，避免给纯 Roslyn 规则构建局部 CPG 视图；最后把只在命中 local definition 时执行的全树符号引用扫描提升为一次构建、可复用的索引。`GroupKey` 仍是链式依赖和确定性顺序的边界，不以拆组并行换取吞吐。

**技术栈：** .NET 10、Roslyn `SemanticModel`、MinimalRoslynCpg、xUnit、现有 CLI 文本日志。

---

## 范围、基线与不可变边界

本提案只处理 `Propagate` 阶段的执行器、其结构视图、局部符号引用路径和传播专属测量。

不在范围内：

- 改变 `DeleteClass` 或 `DeleteSObject` 的 `GroupKey`；
- 调整默认 DOP、`EnableGroupParallelism` 或 helper parallelism 默认值；
- 改变 `PropagatedMarkRecord` 的 rule/span/payload 去重语义；
- 修改 CPG builder、持久化、Mark 优化提案或当前未提交的目录 I/O 提案；
- 以机器相关毫秒阈值作为普通单元测试断言。

已知小样本只说明当前阶段占比，不能外推为大文件结论：对 `TextDisplayCache.cs` 的 `--delete-class PlayerInput --skip-rewrite --no-diff`，DOP 1 记录 `propagate=3 ms`、`cpg-build=1050 ms`，DOP 8 记录 `propagate=15 ms`、`cpg-build=927 ms`。四个大文件的临时样本在 124 秒内未完成且没有完成文件日志，因此没有阶段归因数据。

每个任务必须保持：

1. 同一 group 内的规则注册顺序、后续规则可见的 mark 集和返回 mark 顺序不变。
2. 最终传播去重键仍为 group key、rule ID、syntax span 和 raw kind；不同 rule 在同一 syntax node 上的 payload 不得被合并。
3. DOP 1 与启用 group parallelism 的结果快照相同；只允许不同 group 并行。
4. `StructureView` 对显式声明需要它的自定义规则仍非空，且包含原先可见的绑定节点。
5. 真实 Terraria 测量采用 1 次预热、至少 3 次正式运行和中位数；不据单次结果更改默认值。

## 任务 0：建立传播阶段的可归因遥测与等价夹具

**文件：**

- 修改：`src/RoslynPrototype/Propagation/PropagationEngine.cs`
- 修改：`src/RoslynPrototype/Rewrite/PrototypeAnalysisResult.cs`
- 修改：`src/Application/DeletionApplicationService.cs`
- 修改：`src/Host/Logging/RunTextLogWriter.cs`
- 修改：`src/Host/DeletionCommandHost.cs`（仅接线需要时）
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

1. 在 `PropagationEngine` 内创建只读 telemetry 值对象：按 group 与 rule 记录输入 mark 数、产出数、组内重复跳过数、结构视图请求/命中/未命中、视图节点/边数和 elapsed ticks。计数必须在现有枚举路径上累加，不允许为遥测再扫描语法树或图。
2. 将 telemetry 作为 `PrototypeAnalysisResult` 的可选字段，由 `DeletionApplicationService.RunAnalysis` 传出；无 propagator 时返回零值而不是 `null`。
3. 运行日志增加一条稳定的 `propagation summary` 记录，包含总规则数、总输入/产出、去重数、view request/hit/miss 和最慢 rule ID/耗时。每 rule 明细仅在明确的 Debug 过滤下写出。
4. 扩展现有 chain、dedup、scheduler 和 `ViewAwarePropagationRule` 测试：断言计数、规则顺序和最终 mark key；不要断言绝对 elapsed 时间。
5. 日志测试只断言新增字段存在、过滤遵守现有 profile，并验证关闭 Debug 时不创建逐 rule 日志字符串。

**验收：** 一次 CLI 运行能区分“规则本身、结构视图和去重”的成本；现有等价测试仍覆盖同组链式可见性与跨 rule 同 span payload 的保留。

## 任务 1：让规则显式声明结构视图依赖

**文件：**

- 修改：`src/RoslynPrototype/RuleServices/RuleDefinition.cs`
- 修改：`src/RoslynPrototype/Propagation/PropagationEngine.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 先写失败测试：一个默认 `RuleDefinitionPropagate` 必须收到 `context.StructureView == null`；一个显式 opt-in 的 `ViewAwarePropagationRule` 必须收到与当前契约相同的非空视图和节点集合。
2. 给 `RuleDefinitionPropagate` 添加默认 `false` 的 `RequiresStructureView` 契约。它只描述调用期需求，不改变 `AllowedPropagateNodeKinds`、capability 或 group 语义。
3. 仅在 `rule.RequiresStructureView` 为真时调用 `BuildRuleContext`；否则直接传入原始 `RuleContext`。仍以当前 `groupMarks` 作为 opt-in 规则的 fragments，保证同组前序产物可见。
4. 将测试中的 `ViewAwarePropagationRule` 显式标记为需要视图；审查 `src/Rules/Implementations/Propagate/` 的全部生产规则，只有实际读取 `context.StructureView` 的规则才 opt in。当前没有生产规则读取该属性时，不为它们添加无意义 override。
5. 运行默认 delete-class、delete-s-object、chain 和 view-aware fixture，比较 PropagatedMark key、payload 类型/内容、Decision、Rewrite 文本和 diff。

**停止条件：** 若发现任一生产规则通过间接 helper 依赖 `StructureView`，先为该 helper 建立明确契约和回归，再决定该规则是否 opt in；不得仅依据当前直接文本搜索关闭视图。

**验收：** 无视图需求的规则不再请求或缓存局部 CPG 视图；需要视图的规则保持原有可观察结果和 fragment 可见范围。

## 任务 2：把组内新 mark 成员检查改为稳定的 O(1) 索引

**文件：**

- 修改：`src/RoslynPrototype/Propagation/PropagationEngine.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 写失败测试：构造同组内多个规则持续产生相同和不同 span 的 mark，断言后续规则只看到首次进入 `groupMarks` 的 syntax node，且最终输出保留不同 rule ID 对同一 span 的记录。
2. 在 `RunGroup` 初始化时，以当前 `groupSeedMarks` 的 `(SpanStart, Span.Length, RawKind)` 建立 `HashSet`。每个 `producedMark` 先通过该集合判断是否加入 `groupMarks`，替换 `groupMarks.Any(...)` 的线性扫描。
3. 不用最终输出的 dedup key 作为 group membership key：group 内可见性仍按 syntax node 判断，而最终 `DistinctBy` 仍保留 rule ID。
4. 保持 `propagatedMarks` 的产出顺序和重复记录，最终只在现有 `DistinctBy` 处收口；不要把去重提前到规则产出路径。
5. telemetry 记录 membership lookup 次数和重复跳过数；压力 fixture 断言 lookup 数等于产出候选数，而不是对增长后的列表重复扫描。

**验收：** 同组链式规则、最终 dedup、payload 以及输出顺序完全不变；组内 membership 检查不再随已可见 mark 数线性增长。

## 任务 3：为局部符号引用传播建立惰性、按 scope 的语义索引

**文件：**

- 修改：`src/RoslynPrototype/RuleServices/RuleContext.cs`
- 新建：`src/RoslynPrototype/Analysis/LocalSymbolReferenceIndex.cs`
- 修改：`src/Rules/Implementations/Propagate/DeleteClassSymbolReferencePropagationRule.cs`
- 修改：`src/Rules/Implementations/Propagate/DeleteSObjectPropagationRules.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Mark/MarkRuleEffectTests.cs`

1. 写失败测试：同一 executable scope 内的多个 marked local definition 只触发一次 identifier traversal；不同 method、local function、lambda 和同名 shadowed local 必须保持当前 `IsSameScope` 和 source-order 结果。
2. 实现一次分析生命周期内惰性创建的索引，按 executable scope 保存 `ILocalSymbol ->` 已按 span 排序的 `IdentifierNameSyntax` 引用。索引建造时仅解析 identifier 的 symbol；禁止对每个 marked local 再全树 `GetSymbolInfo`。
3. 将两个 symbol-reference propagation rule 改为查询该索引，再保留各自既有前置条件：DeleteClass 仍要求 object-creation definition 且 reference 在定义之后；DeleteSObject 保留当前初始化 definition、scope 和允许的 local/parameter 行为。
4. 缓存归属 `DeletionAnalysisRuntime` 的 compilation scope，避免跨 compilation 或 epoch 复用 Roslyn 节点；索引只保存当前树的 syntax node，不向全局静态集合泄漏。
5. 增加 shadowing、跨 executable scope、重复 reference、多个已标记定义以及 DOP 等价回归；比较完整 PropagatedMark key 和 reason text。

**停止条件：** 如果既有 `GetSymbolInfo` 对错误恢复语法或候选符号有无法用 `ILocalSymbol` index 表达的行为，保留受限 fallback，并将其次数写入 telemetry；不得静默丢弃该类 reference。

**验收：** 同一 scope 的局部引用只扫描和绑定一次；保守传播边界、source ordering 与所有可观察输出不变。

## 任务 4：验证参数收缩辅助扫描的缓存边界，而非盲目重构

**文件：**

- 修改：`src/Rules/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs`（仅 telemetry 或已证实的局部修复）
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Decision/DecisionStructureValidationTests.cs`

1. 使用任务 0 的规则级耗时和新增 TreeScan materialization/index-build 计数，先记录 method/local-function/indexer/delegate 传播规则在多文件 fixture 上首次及再次调用的工作量。
2. 证明 `CompilationScanCache` 能在一次 runtime/compilation 内复用后，才针对实际重复的 index build 改动；不要重写已经由 `Lazy<TreeScan>` 覆盖的路径。
3. 若找到重复 build，收敛到已有 `CompilationScanCache` 的同一索引，并保留 `EnableHelperParallelism`、取消和 source-order sort 行为。
4. 运行参数缩减、delegate、extension mapping 与 declaration-host 的现有 fixture，比较 payload、proposal、诊断、改写和 DOP 1/8 输出。

**验收：** 该任务要么以测量证明无需生产改动，要么只消除已量化的重复 scan；两种结果都保留为性能报告的一部分。

## 任务 5：端到端等价与真实源码测量

**文件：**

- 修改：`tests/RoslynDeletionPrototype.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- 可选新建：`tests/RoslynDeletionPrototype.Testing/TestCodeSet/Performance/PropagationPerformanceSources.cs`
- 不修改：默认 DOP 与 production CLI 行为。

1. 添加确定性压力 fixture：一个 group 内的链式 propagation、大量同 scope local references、重复 span、一个 opt-in view rule 与一个不需 view rule。测试只断言 telemetry 计数、mark/decision/rewrite 等价和 DOP 一致，不断言毫秒。
2. 以 `--skip-rewrite --no-diff --analysis-log --runtime-log --log-profile benchmark` 对同一真实源码输入运行 1 次预热与 3 次正式测量；分别记录 DOP 1、8、16 的阶段耗时、传播摘要、峰值内存、SDK、commit 和输入文件 hash。
3. 优先使用可在单次工具预算内完成的真实 Terraria 文件作为 smoke 样本；大文件或目录样本用外部进程和足够时限单独运行，超时必须报告为“无归因样本”，不能记为性能回归。
4. 只有三个正式样本的中位数与快照均完整时，才评估传播耗时/分配改善；若 CPG build 仍主导总时间，报告传播优化为局部收益，不扩大为全链路结论。

**最终验证：**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~TextLogSystemTests"
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceOptimizationRegressionTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

## 完成条件、提交与回滚

- 任务 0 完成后才允许更改传播热路径；每个后续任务先加失败回归，再做单一最小实现。
- 每个任务独立提交。提交信息遵循仓库 Lore protocol，至少说明保持的 group/dedup 约束、拒绝的并行化方案和实际验证。
- 所有默认规则未请求结构视图时，telemetry 的 view request 为零；任何显式 opt-in 规则仍通过 view contract 测试。
- DOP 1/8/16 的 Mark、PropagatedMark、Decision、diagnostic、rewrite 与 diff 快照等价。
- 至少一个真实源码的三次 warmed 测量可复现；没有该证据时 feature 保持 `in_progress`，默认 DOP 不变。

回滚单位为单个任务。任何任务若改变同组可见性、最终 dedup、payload、source ordering、rewrite 或诊断，立即关闭该任务的新路径，保留前一任务的已验证改动。不得通过拆分 `DEL-CLASS`/`DEL-SOBJ` group、降低测量规模或跳过 DOP 等价测试掩盖回归。
