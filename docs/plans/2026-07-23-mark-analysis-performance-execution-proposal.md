# Mark 阶段性能优化执行提案

> **状态：提案。** 对应 `roslyn-deletion-prototype-mark-analysis-snapshot-optimization`。本提案只调整既有 Mark 事实的缓存、索引与遥测，不改变 Mark / Propagate / Lift / Propose 职责。

## 目标

降低 DeleteSObject 原子 Mark 在大文件上的重复语法遍历、region 统计和目标名匹配分配，同时保持 seed marks、后续 marks、决策和改写结果等价。

## 已确认事实

- `AtomicExpressionAnalyzer` 已在单次 `RuleContext` 内缓存完整候选集合，但每条原子 Mark rule 仍遍历候选并再执行结构有效性判断。
- `MarkRegionAnalyzer` 对每个 anchor 都物化所属语句或声明的全部 descendants，并统计节点、表达式和语句数量；`MarkAnalysisSnapshot` 目前以 anchor expression 作为缓存键。一个语句有多个命中表达式时会重复计算相同 region facts。
- target-match 缓存每次查询都排序并拼接 target names 作为 key；递归 operation 匹配对 `IReadOnlyList<string>` 做线性查找。
- runtime 日志仅汇总 operation 与 graph-binding 的 cache hit/miss，无法判断 atomic、region、target-match 缓存是否带来收益。
- `Projectile.cs` 仓内副本约 3.2 MB、75,562 行，并含 1,047 个 `this`；可作为外部 Terraria 源缺失时的可重复单文件压力输入。此前一次大文件探针因系统分页文件分配失败而无有效 Mark 数据，不能作为基准。

## 不可变边界

- Mark 仍只返回当前 mark region 内的对象级原子标记；不得把逻辑宿主、语句或控制结构提前提升到 Mark。
- 仍由 `MarkRegionAnalyzer` 决定 region 边界；缓存可以复用 facts，但不得改写边界规则。
- 不修改 graph-binding 优先级、规则注册顺序、group-parallel 调度条件、规则输出排序或默认 DOP。
- 不添加包依赖；不用 microbenchmark 结果替代真实规则输出等价验证。
- 每个任务单独提交，失败后回滚该任务，不把下一项优化叠加在未验证修改上。

## 验收口径

每个实现任务完成后，先在固定 fixture 上比较：

1. `SeedMarks`：rule id、group key、syntax span、primary graph node id；
2. `PropagatedMarks`、`LiftedMarks`：规则、span、payload 类型与内容；
3. `Decisions`：action、rule、final span、replacement；
4. 规范化后的 rewritten source 与 diff；
5. 改写后的源码编译诊断。

上述比较分别在 DOP 1 和启用 group parallelism 的 DOP 16 下执行。功能验证通过后，才记录性能数据。性能比较使用固定输入，预热一次后运行至少三次，报告 Mark 阶段中位数和分配字节；没有稳定的真实源数据时，不调整默认 DOP，也不将 feature 标记为完成。

## 任务 1：补齐 Mark 遥测口径

**范围文件：**

- 修改：`src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- 修改：`src/Host/Logging/RunTextLogWriter.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

**实施：**

1. 在 `MarkAnalysisTelemetry` 增加下列计数：atomic candidates 总数、按 kind 返回的候选数、region facts 创建数与复用数、target-match key 创建数、target-match 查询数。
2. 计数只描述发生的工作；不可把“缓存命中”与“已计算 facts”混为同一字段。
3. `WriteMarkSummary` 输出每个 cache 的独立 hit/miss，以及上述新计数；保留现有字段兼容性，新增字段不改变日志事件类型或过滤行为。
4. 补测试：同一 `RuleContext` 多次取得同一候选、region 与 target-match 时，遥测可区分首次创建和复用；日志包含独立字段。

**验证：**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~MarkingEngine_Run_SObjectRules_PreservesSeedMarksAcrossGroupParallelismAndUsesSnapshotCaches|FullyQualifiedName~TextLogSystemTests"
```

## 任务 2：按 region node 缓存 region facts

**范围文件：**

- 修改：`src/RoslynPrototype/Analysis/otherAnalyzers/MarkRegionAnalyzer.cs`
- 修改：`src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**实施：**

1. 将“解析 anchor 对应 region node”提取为可由 snapshot 调用的稳定入口；保留现有语句优先、声明回退的规则。
2. `_regions` 的 key 改为解析后的 `RegionNode`；缓存值继续保存 region node、span 和统计值。
3. 返回 `MarkCodeRegion` 时重新绑定当前 anchor，保证调用者仍能获得自己的 anchor node。
4. 使用一个同语句多 target 命中的 fixture，断言多个 anchor 共享一次 region-facts 创建，同时各自返回的 `AnchorNode` 正确。
5. 运行现有 Mark 等价测试，确认 serial/parallel 的 mark keys、primary graph binding 与所有后续输出未变化。

**停止条件：** 若不同 anchor 在现有实现中应产生不同 region node 或统计值，保留 anchor-key 缓存并记录反例；不得用 span 相等强行合并。

## 任务 3：按 `SyntaxKind` 预分桶原子候选

**范围文件：**

- 修改：`src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- 修改：`src/RoslynPrototype/Analysis/RuleSyntaxAnalysisHelpers.cs`
- 修改：`src/RoslynPrototype/RuleServices/RuleContext.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

**实施：**

1. 保留一次完整的 `AtomicExpressionAnalyzer.Analyze(root)` 调用，按原 span 顺序将结果构建为 `SyntaxKind -> ExpressionSyntax[]`。
2. 为单个 allowed kind 的原子 rule 直接返回对应 bucket；多 kind 调用者按全局候选顺序合并 buckets，不改变原来的排序规则。
3. 结构有效性判断保持在现有路径中；本任务只减少无关 kind 的遍历，不缓存或跳过语义判断。
4. 增加 fixture，覆盖所有当前允许的原子 `SyntaxKind`，断言 bucket 合并结果等于优化前的完整候选过滤结果。
5. 遥测记录总候选数和实际交给每条 rule 的候选数，以证明减少的是遍历量。

**停止条件：** 若任一调用点依赖 `IEnumerable` 的延迟枚举副作用，先将调用点改为明确的只读候选集合并用回归测试锁定，再继续分桶。

## 任务 4：规范化 target names 并移除热路径临时分配

**范围文件：**

- 修改：`src/RoslynPrototype/Analysis/MarkAnalysisSnapshot.cs`
- 修改：`src/RoslynPrototype/RuleServices/RuleContext.cs`
- 修改：`src/RoslynPrototype/RuleServices/RuleHelpers/DeleteSObjectMarkRuleHelpers.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/RoslynDeletionPrototype.HostTests/Mark/MarkRuleEffectTests.cs`

**实施：**

1. 将每个原始 `target-name` 值解析为不可变 target descriptor：原始规范化顺序、稳定 cache key、ordinal 名称集合。
2. `GetTargetMatch` 直接接收 descriptor 的 cache key，禁止在每次候选匹配时 `OrderBy` 或 `string.Join`。
3. `ReferencesTarget`、成员绑定和定义左值路径使用 descriptor 的 ordinal 集合查询。
4. 保留逗号分隔、去空白、去重和 ordinal 语义；补 single target、重复 target、不同输入顺序、多 target logical expression 的回归。
5. 验证 target-match key 仅在 descriptor 创建时构造，且多次 Mark 查询复用同一 key。

**停止条件：** 若 target 名称顺序出现在 reason text 或对外可观察输出中，descriptor 必须保留用于展示的原始规范化顺序，cache key 可独立排序。

## 任务 5：端到端等价与性能测量

**范围文件：**

- 修改：`tests/RoslynDeletionPrototype.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- 可选修改：`tests/RoslynDeletionPrototype.Testing/TestCodeSet/Performance/PerformanceSources.cs`
- 不修改：生产默认 DOP 和规则行为。

**实施：**

1. 添加确定性压力 fixture：至少包含同一语句多目标命中、嵌套访问、逻辑操作数和多 target-name；fixture 只验证语义等价，不设置机器相关毫秒阈值。
2. 测量 helper 执行预热一次及三次正式运行，收集 `Timings.MarkMilliseconds`、`GC.GetTotalAllocatedBytes` 差值、新增 Mark telemetry 和 Mark/Decision/Rewrite 快照。
3. 比较 DOP 1 与 DOP 16 的快照；性能输出作为测试诊断信息，断言输出等价、计数符合预期、无异常。
4. 在资源稳定且可访问时，对仓内 `Projectile.cs` 副本或用户提供的 Terraria 根目录执行同一协议；记录源路径、文件 hash、SDK、DOP、每次值和中位数。

**最终验证：**

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests|FullyQualifiedName~TextLogSystemTests"
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceOptimizationRegressionTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

## 完成条件

- 四项局部优化均有独立回归与 Mark/Decision/Rewrite 等价证据。
- Mark 日志能区分 atomic、operation、graph binding、region 和 target-match 的工作量与复用量。
- 压力 fixture 在 DOP 1/16 下无语义差异。
- 有效的三次 warmed 测量证明 Mark 中位数或分配量改善；若未改善，保留语义与遥测修复，回滚无收益的性能改动。
- 未取得可比真实源测量时，feature 仍保持 `in_progress`，默认 DOP 不变。

## 风险与回滚

- region-key 合并错误会改变 anchor 与 region 的对应关系；用同语句多命中和声明回退 fixture 覆盖。
- candidate 分桶可能改变枚举顺序；始终以原子候选的全局 span 顺序为主序。
- target descriptor 可能误改大小写、展示顺序或重复项语义；使用 ordinal、多 target 和 reason text 回归。
- 所有任务均可独立回滚；不与当前 CPG persistence、日志或目录 I/O 的未提交工作混合测量。
