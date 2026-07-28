# CallGraphPass 与 DataFlowPass 性能优化执行提案 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**Goal:** 在不改变冻结图、稳定 NodeId、调用目标、DataFlow 边、预算行为或 DOP 等价性的前提下，消除 CallGraphPass 与 DataFlowPass 已确认的重复计算、候选边膨胀和无界中间态。

**Architecture:** 保持“worker 只读取 Roslyn 语义事实，主线程按源序物化图节点与边”的边界。先实施不会改变最终图集合的去重、缓存和死路径删除；再将 DataFlow 重构为有界双阶段流水线。bitset、CSR 或其他图算法只在新的分阶段数据证明 fixpoint 是主要成本后另立提案。

**Tech Stack:** .NET 10、C#、Roslyn `IOperation`/`IMethodSymbol`、xUnit、现有 `NLCPGBuilder` 分区构建器。

---

## 范围、决策与不可变约束

本提案来自对以下当前实现的静态审查：

- `src/NLCPG/Builder/Passes/CallGraphPass.cs`
- `src/NLCPG/Builder/Passes/DataFlowPass.cs`
- `src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- `src/NLCPG/Builder/NLCPGBuilder.cs`
- `src/NLCPG/Model/NLCPGGraph.cs`

执行范围按优先级限定为：

1. 删除 DataFlow 的无消费者统计和不可达节点遍历；
2. 将重复 DataFlow 候选在方法内压缩，并在超预算时立刻停止保留候选；
3. 复用 OperationPass 已物化的 operation-to-node 对应关系；
4. 缓存调用点的已排序目标集合，并将 DataFlow 复用该集合；
5. 把 used-fact 收集、主线程计划构建、CFG 求解和有序提交改为有界流水线；
6. 以事实索引和空集短路减少 reaching-definition 扫描；bitset 仅在现有或另行批准的阶段数据证明必要时评估。

本提案不包括：默认 DOP 调整、CallTargets 语义扩张、数据流精度调整、持久化格式变更、冻结图结构重构，或把图写入并行 worker。

必须保持的契约：

- DOP 1、8、12、14、16 产生相同节点、边、稳定 NodeId 和边排序。
- `DataFlow` 的精确边集合、重复引用的最终唯一边、参数链接、调用返回流和属性访问器流不变。
- CallTargets 的候选顺序及 CallSite 的 dispatch 信息不变；外部目标与内部目标的现有优先级不变。
- `NLCPGDataFlowOptions.MaxCandidateEdgesPerMethod` 在阶段 2 仍按当前“原始候选数”判定，避免去重改变已有 skip/fail 行为。
- `BoundedPartitionWorkWindow` 的源序提交、reorder allowance 和最大保留记录数语义不变。

当前 `init.ps1` 仍引用缺失的 `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`。执行时先运行它记录该 harness 阻塞，再使用下列当前 `NLCPG` 测试工程验证；修复脚本属于单独的 harness 任务。

## 任务 0：锁定缺失的 CallTargets 契约与现有 DataFlow 基线

**文件：**

- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- 修改：`tests/RoslynDeletionPrototype.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`

**步骤：**

1. 新增一个公开构图测试源，覆盖同一基类方法的 override、接口实现、同签名内部候选、外部回退和扩展方法接收者。
2. 写失败测试，从 `NLCPGBuilder.BuildFromSource` 取得冻结图，按 CallSite 的源码 span 定位并断言每个 `CallTargets` 终点的完整集合和排序；同时断言 CallSite dispatch flags。
3. 写失败测试，对该源在 DOP 1、8、12、14、16 运行 `AssertGraphsEqual`。
4. 复用现有复杂局部流、重复引用、调用/属性流的精确边测试；新增一个候选预算夹具，覆盖 `SkipMethod` 与 `FailBuild` 的现有结果。
5. 在任何生产改动前运行这些测试，记录当前基线通过情况。

**验收：** 后续缓存和索引改动有直接的 CallTargets 行为保护；DataFlow 仍由公开冻结图验证，不测试私有 helper。

## 任务 1：删除无观察者的 DataFlow 统计路径

**文件：**

- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**步骤：**

1. 写失败回归：使用复杂分支/循环源，锁定完整 DataFlow 边集合及 DOP 图等价。
2. 删除 `DataFlowPassMetrics`，以及只向该对象写入的 `Stopwatch`、计数和 `CfgSensitivePartition` 未消费字段。
3. 删除 `CountUnreachableNodes` 调用和实现；该 BFS 的结果目前既不影响溢出决定，也不进入对外遥测。
4. 保留 overflow reason、方法名和 `FailBuild` 异常文本；这些是已观察的行为。
5. 运行任务 0 的全部回归。

**验收：** 图和异常完全相同；每个方法少一次不可达 CFG 遍历，且默认路径不再维护未发布的统计对象。

## 任务 2：压缩候选边并把预算检查移入生成循环

**文件：**

- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**步骤：**

1. 写失败测试：对 `value + value + value` 等重复引用，断言最终 DataFlow 边仍唯一；增加一个低预算用例，锁定当前 skip/fail 结果和异常消息。
2. 在每个 CFG-sensitive 分区中维护两个量：原始候选计数用于保留预算语义，唯一 `CpgEdgeCandidate` 集合用于后续提交和内存占用。
3. 每产生一个原始候选即递增计数；超过上限后立即按现有 `SkipMethod`/`FailBuild` 分支退出，禁止继续枚举 used facts 或分配候选。
4. 仅将唯一候选转为 `LocalFlowCandidateSet`；保持原始首次出现顺序，避免改变有序提交的输入顺序。
5. 在候选提交处避免针对同一 anchor 反复构造“锚点专用节点”；为图增加受限的内部批量入口，或在本 pass 内维护 anchor-to-node 提交缓存。该入口必须仍走预分配 NodeId 与可变图检查。
6. 运行重复引用、复杂局部流、预算、DOP、slice 确定性和持久化图恢复测试。

**验收：** 最终图集合和预算结果不变；重复候选不再经历数组复制、四次节点合并和 `PendingEdge` 的迟后去重。

## 任务 3：从 OperationPass 传递稳定的 operation-to-node 事实

**文件：**

- 修改：`src/NLCPG/Builder/NLCPGBuildContext.cs`
- 修改：`src/NLCPG/Builder/Passes/OperationPass.cs`
- 修改：`src/NLCPG/Builder/Passes/PartitionedOperationPass.cs`
- 修改：`src/NLCPG/Builder/NLCPGBuilder.cs`
- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**步骤：**

1. 写失败测试：在一个多方法、嵌套表达式、属性访问器源上锁定 operation 节点数、`SyntaxHasOperation`/`OpHasSyntax` 边、CallSite、CFG 和 DataFlow 完整快照。
2. 扩展 build-local operation inventory，使每条记录持有已在主线程物化的 `NLCPGNode`，并保留 root、拥有方法和 Roslyn reference identity。
3. 分区和 legacy OperationPass 都在取得 operation node 后写入该事实；禁止 worker 写图或向 inventory 写未物化节点。
4. 以 inventory 中的 root/拥有方法分组构建 DataFlow 方法序列，替换 `DescendantsAndSelf().ToArray()` 与 `CreateDataFlowOperationIndex` 的第二次树遍历和 `GetOrCreateOperationNode` 调用。
5. 保持现有 block-root 过滤、operation 顺序和表达式体的既有支持范围；新增分组不得把局部 block 误作为方法根。
6. 运行任务 0/2 测试，以及 partitioned 与 streaming 的 source-order/NodeId 回归。

**验收：** 每个 operation 在 OperationPass 期间只物化一次；DataFlow 不再重新遍历操作树或以空壳节点合并既有 operation 节点。

## 任务 4：缓存调用目标，随后索引动态分派候选

**文件：**

- 修改：`src/NLCPG/Builder/NLCPGBuilder.cs`
- 修改：`src/NLCPG/Builder/Passes/CallGraphPass.cs`
- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`

**步骤：**

1. 写失败测试：同一调用点的 CallTargets、`ParameterLink`、参数 DataFlow 和 MethodReturn-to-CallSite DataFlow 在缓存开关前后完全相同。
2. 在 builder 生命周期内保存“调用 operation -> 已排序有效候选”的不可变集合；Build 开始和 `ReleaseTransientBuilderState` 时清空。
3. CallGraph 写入 CallTargets 时填充该缓存；DataFlow 只读取缓存，不再调用 `ResolveCallTargetCandidates` 或 `PreferCallTargets`。
4. 为缓存缺失建立显式失败保护，而非静默重算；DataFlow capability 已强制依赖 CallTargets，缺失代表流水线契约破坏。
5. 增加以 canonical target、静态 receiver type 和扩展方法接收者形状为键的分派结果缓存。先保持现有全类型/全扩展方法扫描作为缓存未命中的唯一实现。
6. 仅在分阶段计数显示缓存未命中仍显著时，再将 `_declaredTypes` 和 extension method 扫描替换为按方法名、参数形状、声明类型和 extension receiver 的索引；该索引必须保留候选排序前的完整集合。
7. 运行任务 0 的 override/interface/extension 测试、DOP 快照、UnreachableMethod 规则相关回归和 interprocedural DataFlow 回归。

**验收：** 每个调用点只做一次候选解析；相同静态分派形状复用缓存；CallTargets、dispatch flags、参数流和返回流无任何差异。

## 任务 5：将 DataFlow 改为有界双阶段流水线

**文件：**

- 修改：`src/NLCPG/Builder/BoundedPartitionWorkWindow.cs`
- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

**步骤：**

1. 写失败测试：让第一个方法的 used-fact 收集阻塞、后续方法先完成；断言图提交仍严格按源序，且有界窗口不会保留超过 `reorderAllowance` 与 `MaxOrderedResultRecordCount` 允许的已完成分区。
2. 将目前“收集所有 `UsedFactPartition`，构建所有 `MethodDataFlowPlan`，再求解”的三段式流程改为双阶段窗口：worker 收集 Roslyn used facts；主线程按顺序物化计划并调度 CFG 求解；主线程按同一顺序提交候选并立即释放计划和 Roslyn 事实。
3. 主线程持有 graph、operation-node 映射和 CFG 邻接；worker 只读取冻结的计划事实和 Roslyn 语义事实，不调用 `AddNode`/`AddEdge`。
4. 将窗口的 record 计数从“候选数组长度”扩展为能反映 retained used facts、flow nodes 和候选的保守额度，防止靠前慢分区导致内存失控。
5. 保持取消、最先失败异常、overflow skip/fail 和 source-order commit 行为。
6. 运行大方法、多方法、DOP、streaming shard、slice、DataFlow 精确边与重复引用确定性测试。

**验收：** DataFlow 的峰值中间态由窗口上限约束，而非所有方法数；图写入仍只发生在主线程，顺序与现有实现一致。

## 任务 6：以事实索引优化 reaching-definition；bitset 只走准入门

**文件：**

- 修改：`src/NLCPG/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/NLCPGSliceQueryTests.cs`
- 修改：`docs/plans/2026-07-28-callgraph-dataflow-performance-execution-proposal.md`

**步骤：**

1. 先做无语义风险的短路：当某 operation 的 `inSets` 为空时，不展开其 `UsedFactRecord`。
2. 写失败测试，覆盖 local、parameter、field、property、container、part 与 alias 匹配；断言精确边集合和 slice 结果。
3. 建立方法内 definition fact 的只读索引，使常见 `LocationKey`/`BaseKey`/`PathKey` 匹配避免扫描全部 reaching definitions；保留现有 `FactsMatch` 作为索引不能证明完整性时的回退。
4. 当前 checkout 的公开 CPG 遥测接口已被有意移除；本批次不恢复该接口，也不通过第二次遍历采样。后续基准应使用现有运行面或另行批准的遥测提案记录 fact 展开、匹配比较、唯一/原始候选、fixpoint 迭代和每方法保留峰值。
5. 使用同一二进制、固定输入、一次预热和至少三次有效样本比较中型与真实源；缺少完整结束事件、超时或资源耗尽的样本无效。
6. 只有当 `FixpointElapsed` 或 reaching-definition 匹配持续占 DataFlow 时间的主要部分，才新建单独提案实现 definition ordinal + pooled `ulong[]` bitset；该方案不可与本任务混合提交。

**验收：** 索引路径与 `FactsMatch` 回退的结果相同；是否实施 bitset 有可复现的阶段数据支撑。

## 验证顺序与完成条件

每个任务先运行其 focused test，再按依赖顺序运行：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path

dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~NLCPGSliceQueryTests"

dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false

pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

构建和测试须顺序运行，避免共享输出目录的 MSBuild 锁。当前 `init.ps1` 和 harness consistency 脚本仍引用已移除的旧项目路径；本优化批次记录其失败但不修改 harness。若 harness 或 restore 仍阻塞，记录“已读源码确认契约，但未完成端到端运行验证”，不得把阻塞误报为性能回归。

完成条件：

- 任务 0 的 CallTargets 语义测试、既有精确 DataFlow 边测试和 DOP 图快照全部通过。
- 每项优化通过其对应的图、slice、streaming 和预算契约测试；默认 DOP 与数据流预算语义未变。
- 有效的预热多样本数据能在后续独立基准中将收益归属到候选压缩、调用目标缓存、操作复用、流水线或 fact 索引中的具体一项；本实施验证不将契约测试替代为性能证据。
- bitset 只有通过任务 6 的准入门后才进入新的实施提案。

## 回滚边界

每项任务独立提交。若任一项改变 CallTargets 集合/排序、CallSite dispatch、冻结图、NodeId、DataFlow 精确边、budget skip/fail、source-order commit 或 shard-backed slice，则关闭该项新路径并回滚到前一已验证提交。不得通过降低测试覆盖、跳过 Strict/streaming 验证或更改默认 DOP 掩盖回归。
