# CPG 与目录分析性能修复执行提案 Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task.

**目标：** 消除当前 CPG 构建、持久化 slice 查询、catalog 提交和目录分析中的确认重复工作；在不改变图、slice、Mark/Decision/Rewrite 结果或默认并行度的前提下，降低大源码和增量构建的 CPU、I/O、SQLite 事务及峰值内存成本。

**架构：** 优先复用同一次构建已经产生的语义事实和 source buffer；持久化查询只把预算内可能访问的数据物化为临时图；catalog 保持单 writer，但将同一 build 的 reuse clone 与引用计数维护合并进有界、原子的批处理路径。所有新增性能路径先与当前实现做可观察的行集、图快照和结果等价比较，再依据预热中位数决定是否保留。

**技术栈：** .NET 10、Roslyn `IOperation`、Microsoft.Data.Sqlite、xUnit、`tools/CpgPersistenceBenchmark`。

---

## 范围、基线与不可变约束

本提案只处理以下七项已确认问题：operation 树重复遍历、slice 忽略节点/边预算后完整物化、reuse shard 逐个 SQLite 事务、每次完成构建重建全部物理引用、fragment owner 线性扫描、被过滤 Debug 内存日志仍采样、cleanup 重读和重写整份文件。

不在范围内：调整默认 DOP、改变 Strict/Throughput 语义、变更 shard 文件格式、放宽 completed-session 可见性，或顺带重构 interprocedural call-stack 表示。

开始实现前记录以下可重复基线，并将原始 JSON 保留在 `Build/`：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet run --project .\tools\CpgPersistenceBenchmark\CpgPersistenceBenchmark.csproj -- --fixture changed-method-reuse --fixture repository-dataflow-pass-reuse --durability Strict --dop 12 --samples 5 --warmup 2
```

已知对照：2026-07-23 的 `changed-method-reuse` 为 96 个 reuse shard，cold `2486 ms`、incremental `5792 ms`；`repository-dataflow-pass-reuse` 为 47 个 reuse shard，cold `5473 ms`、incremental `6427 ms`。这些数字用于判断回归，不是本提案承诺的目标值。

每一任务都必须保持：

- DOP 1、8、12、14、16 的冻结节点/边集合、稳定 NodeId 与 shard-backed slice 结果等价。
- 修改删除规则 host 时，Mark、Decision、归一化 Rewrite 文本和 diff 等价；输出仍可编译。
- building 或 invalid build 不能被 restore/query；一个 `StoreRoot` 仍只有一个 catalog writer。
- Strict 仍为默认 durability；未得到三种 fixture 的预热中位数证据前不改变任何默认值。

## 任务 0：建立可归因的性能计数与回归夹具

**文件：**

- 修改：`tools/CpgPersistenceBenchmark/Program.cs`
- 修改：`tools/CpgPersistenceBenchmark/BenchmarkConfiguration.cs`
- 修改：`src/MinimalRoslynCpg/Builder/RoslynCpgBuildTelemetry.cs`（或现有 builder telemetry 定义处）
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs`

1. 为 operation inventory build、fragment owner lookup、shard 解码/读取、reuse clone batch、物理引用维护分别增加计数和耗时 telemetry。计数不得通过额外全图遍历取得。
2. 在 benchmark JSON 中输出上述字段，并保留已有 `coldBuildMilliseconds`、`incrementalBuildMilliseconds`、catalog commit、reuse 命中和 heap/working-set 字段。
3. 新增测试夹具：多方法/嵌套 span 源码、跨 shard 多 hop slice、小预算 slice、96 fragment 的 changed-later-method reuse。断言计数可观测而不是断言绝对时间。
4. 先运行新增测试，确认当前版本暴露重复 operation walk、重复 shard decode、逐 clone transaction 和线性 owner lookup；随后只在对应任务中将各计数降至目标。

**验收：** 基线报告可将每项后续优化的时间或次数归属到一个具体阶段，不把 GC、进程采样或 SQLite 时间混为总 wall-clock。

## 任务 1：复用 OperationPass 的稳定 operation inventory

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- 修改：`src/MinimalRoslynCpg/Builder/Passes/CallGraphPass.cs`
- 修改：`src/MinimalRoslynCpg/Builder/Passes/MemberAccessPass.cs`
- 修改：`src/MinimalRoslynCpg/Builder/Passes/DataFlowPass.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/MinimalRoslynCpgPartitionedBuilderTests.cs`

1. 写失败回归：同一 build 的 CallGraph、MemberAccess 和 DataFlow 全部执行后，operation root 规划与 `SemanticModel.GetOperation`/`DescendantsAndSelf` 的调用次数不随 consumer pass 数量重复增长；同时断言节点、边、顺序和 NodeId 与基线一致。
2. 在 `OperationPass` 后建立一次仅供本 build 使用的、稳定排序的 operation inventory；它必须保留现有 root/body 过滤语义及 reference identity，不能以字符串 ID 重建。
3. 将 CallGraph 和 MemberAccess 的 invocation/property/field 枚举改为从该 inventory 作类型筛选；将 `AddCallArgumentAndReturnDataFlow` 改为复用相同 inventory。`DataFlowPass` 的 method-root 计划若有不同生命周期，保持它的现有并行/所有权边界。
4. 删除或收窄 `EnumerateOperationRoots`，确保其不再成为默认 pass 链中的重复 Roslyn 树遍历入口。
5. 运行 DOP 图快照、DataFlow 精确边、重复引用 slice 确定性和规则/Rewrite 等价回归。

**验收：** 默认 build 不再对相同 body 重复执行全树 `GetOperation + DescendantsAndSelf`；图、跨 pass 边和 DOP 输出完全等价。

## 任务 2：将 fragment owner 查询改为保持优先级的区间索引

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/Streaming/FragmentOwnershipIndex.cs`
- 修改：`src/MinimalRoslynCpg/Builder/Streaming/SkeletonShardPublisher.cs`（仅在调用端需要批量路由时）
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 写失败回归：构造重叠/嵌套 fragment span，验证原有语义为“最短 span、随后 span start、最后 source order”，并记录 N 个 node/descriptor、F 个 fragment 时的比较次数。
2. 实现按起点可二分搜索的区间索引，并在候选集合中按上述既有优先级选择 owner；不得假设 fragment span 不重叠。
3. 为连续排序 node span 提供可选 sweep/batch 路由，但只能在输入已按 span 递增且验证为同一文件时启用；其余调用继续走索引，避免引入隐式排序依赖。
4. `FragmentNodeOwnershipIndex.Create` 和 descriptor 路由共享同一 owner 结果，禁止对相同 node/span 再次查找。
5. 运行 source-order shard、NodeId 单 owner、初始 skeleton 可读性、cross-shard edge 恢复、DOP 和 slice 等价回归。

**验收：** owner 选择保持原有 tie-break；随机 span 查询为 `O(log F + K)` 候选检查而非 `O(F)` 全扫描，且冻结节点只解析一次 owner。

## 任务 3：让 shard-backed slice 在预算内逐层解码和物化

**文件：**

- 修改：`src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- 修改：`src/MinimalRoslynCpg/Analysis/CpgShardQueryResolver.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/CpgFrozenShardGraphReader.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 写失败回归：同一大 shard 中请求 `MaxVisitedNodes=1`、`MaxVisitedEdges=1` 的 query 不得完整冻结整 shard；另加多 frontier node 指向同一 shard 的回归，断言每个 shard 每 query 最多解码一次。
2. 将 `LoadFrontierGraphAsync` 接收完整 `RoslynCpgSliceQueryOptions`，而非只接收 `MaxHops`。每层只解析定位当前 frontier 与所需入边的目录/边界记录，在加 node、edge、下一 frontier 前检查 hop/node/edge budget。
3. 为当前 query 的 `shardId -> decoded projection` 建立有界 memo；memo 保存必要的 node/edge/boundary projection，不跨 query 缓存已截断的 slice 结果。
4. 无法在 shard 文件格式中按需读取时，先增加 reader 的索引化/流式投影 API；不要以“先 `ReadGraph` 再丢弃多数数据”的适配层冒充 budget 支持。
5. 保留 source/NodeId/edge kind 的稳定排序、`anchorUnavailable`、取消传播和现有 truncation reason；小预算必须返回同样的截断语义，而不是把加载限制误报为 query 成功。
6. 运行 shard-backed/in-memory slice 结果、跨 shard boundary、DOP 和 unavailable shard 回归；使用人为放大的 shard fixture 验证解码数量和峰值托管内存下降。

**验收：** `MaxVisitedNodes`、`MaxVisitedEdges` 和 `MaxHops` 同时约束加载与遍历；一个 shard 在一个 query 中最多投影一次；完整预算下结果与当前实现字节级可比的节点/边/路径集合相同。

## 任务 4：合并 reuse clone 的事务并增量维护物理引用

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/CpgCatalogBatchWriter.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardSchema.cs`（仅当需要迁移/约束）
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 写失败回归：96 个复用 fragment 必须在一个 catalog connection/transaction 内完成 clone；注入第 N 个 clone 失败时，build 不得 completed，且目标 build 不留部分 clone 行。
2. 将 `_reusableCloneRequests` 交给既有 catalog batch writer 或新增的 build-finalization batch；prepared `INSERT ... SELECT` 按有界批量执行，保留 source-order 和原有 reusable-key 校验。
3. `CompleteBuildAsync` 在同一 finalization transaction 内先使当前 build 可完成、再仅对本 build 的新 `session_shards` 增加 `physical_shard_references`；不得执行 `DELETE` 后扫描所有 completed session。
4. `PruneCompletedBuildsAsync` 在删除 session 前按受影响 physical shard 递减计数、删除零引用记录；保留一个只供维护/损坏恢复使用的全表 rebuild API，正常 build 不可调用它。
5. 写数据库不变量测试：多代复用、prune 祖先 session、取消、重复完成、损坏 candidate 和并发 store writer 下，`physical_shard_references` 与 completed `session_shards` 的 group-by 结果相同。
6. 对 `changed-method-reuse`、`repository-dataflow-pass-reuse`、`large-single-file`、`multi-file` 在 Strict/DOP 12 运行 2 warmup + 5 samples；报告 clone 事务数、catalog commit、cold/incremental、引用维护时间和行数。

**验收：** 96 reuse shard 不再产生 96 次连接/事务；常规完成构建的引用维护复杂度只与本 build 及 prune 集合相关；失败、取消和保留策略不改变已完成 build 的可恢复性。

## 任务 5：把 Debug 内存快照采样放到过滤器之后

**文件：**

- 修改：`src/Host/Logging/AnalysisTextLogWriter.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

1. 写失败回归：默认 Info/profile 过滤掉 `memory snapshot` 时，替身采样器的 GC、process、ThreadPool 调用数必须为零；显式允许该 Debug event 时必须产生完整字段和原有一行文本格式。
2. 在 `WriteMemorySnapshot` 起始处先以固定 level/category/event 调用 filter；不允许时直接返回，之后才调用 `GC.GetGCMemoryInfo`、`Process.GetCurrentProcess`、`ThreadPool.GetAvailableThreads` 与 `GetMaxThreads`。
3. 将环境采样抽成内部可替换小接口或委托，仅为测试可计数，不新增公共配置面，也不改变日志时间戳/格式。
4. 运行 host log filter、legacy flag 和 directory benchmark-profile 回归。

**验收：** 默认被过滤的 Debug snapshot 没有环境 API 开销；启用它时日志字段、过滤语义和错误处理不变。

## 任务 6：使用内存中的原文和 cleanup 结果，不再重放整份 rewrite

**文件：**

- 修改：`src/Host/DeleteClassPostRewriteCleanupService.cs`
- 修改：`src/Host/DeletionDirectoryAnalysisService.cs`
- 新建：`tests/RoslynDeletionPrototype.HostTests/DeleteClassPostRewriteCleanupServiceTests.cs`

1. 写失败回归：传入已加载的 `originalSource` 和含 cleanup 的 `currentSource`，断言没有额外文件读取或 `PrototypeRewriter.ExecutePlan` 调用，同时 `Edits`、`Diff`、`RewritePlans`（需要时）和 `RewrittenSource` 与旧结果一致。
2. 让 cleanup 服务接收/保留 directory analysis 已经读取的 source buffer；`MergeCleanupEdits` 直接以它构造必要的 diff/plan metadata，并把 `currentSource` 作为最终文本。
3. 当调用路径不是 directory mode 或原文 buffer 不可用时，只在该受控分支读取一次原文；禁止无条件 `File.ReadAllText(filePath)`。不要为了设置 `RewrittenSource` 再创建 synthetic whole-file `RewritePlanEdit` 并执行 rewriter。
4. 覆盖无 cleanup、多个 using 清理、空 namespace 清理、仅 diff、rewrite-plan capture 和目录并发分析；再对现有 delete-class fixture 比较归一化输出与可编译性。

**验收：** 每个发生 cleanup 的文件在默认目录路径不再有额外磁盘读和全文件重写执行；最终源、diff、计划工件及 CLI 行为保持等价。

## 分阶段验证、发布与回滚

每个任务先运行其 focused test；任务 1--4 完成后额外运行：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~SqliteCpgShardCatalogTests|FullyQualifiedName~RoslynCpgSliceQueryTests"
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~TextLogSystemTests|FullyQualifiedName~DeleteClass"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

完成全部任务后，再按 `feature_list.json` 的定义执行 Mark/Decision/Rewrite 新旧等价、DOP 1/8/12/14/16 图等价，以及三组 fixture 各 2 warmup + 5 samples 的预热 benchmark。只有所有语义门槛通过、且多组中位数稳定改善，才可讨论默认值变更。

回滚单位是单任务：任何任务若改变冻结图、slice 截断理由、catalog 可见性、物理引用不变量或 rewrite 输出，即关闭该任务的新路径，保留前一阶段的已验证改动。不得用降低测试预算、跳过 Strict 校验或改变默认 DOP 掩盖回归。
