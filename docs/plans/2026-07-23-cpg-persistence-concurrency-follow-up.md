# CPG Catalog 提交与持久化构建优化执行计划

> **For Codex:** 执行时按本文任务顺序推进；每个任务先新增失败回归，再做最小实现并验证。

**目标：** 减少 CPG 图构建完成后在 shard 发布、catalog 索引和跨 session 复用中产生的串行等待、重复对象物化和 SQLite 提交开销，同时保持 completed-session 可见性和 DOP 图等价。

**架构：** 文件 shard 继续由有界 worker 写入，catalog 保持唯一写入者。worker 的完成顺序经有界重排缓冲恢复为 publication sequence 顺序后再交给 catalog batch writer。复用命中只校验物理 shard 并在 SQLite 内克隆既有索引，避免将旧 shard 反序列化成完整对象图后重新逐行写入。

**技术栈：** .NET 10、`System.Threading.Channels`、`Microsoft.Data.Sqlite`、xUnit、`tools/CpgPersistenceBenchmark`。

---

## 范围和术语

“减少持久化阶段”只覆盖图事实已生成之后的工作：导出 shard、序列化、文件写入和校验、catalog 索引、session 完成和复用。它不改变 Syntax、Operation、CFG、DataFlow 的业务语义，也不承诺在本轮消除完整 CPG 的峰值常驻内存。

不可变边界：

- Building 或 Invalid session 永远不能被 restore 或 query 使用。
- 同一 `StoreRoot` 维持单 catalog writer；不引入 SQLite 多 writer。
- DOP 1、8、12、14、16 的图节点、边、slice、规则和 rewrite 结果必须等价。
- Strict 仍是默认 durability；本计划不改变 Strict/Throughput 的数据安全语义。
- 新队列与重排缓冲必须有容量上限、取消、异常传播和 telemetry。

当前基线：`Build/cpg-persistence-benchmark-20260723-strict-validation-reuse-smoke.json` 的 Strict/DOP 12 中位 wall-clock 为 `3640 ms`，catalog commit 为 `1493 ms`，文件写累计为 `1195 ms`。catalog 是当前关键路径候选，需要逐项基准证明后才改默认值。

## 任务 1：恢复确定的源序 catalog 提交

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 新增回归：人为阻塞低 sequence shard、让高 sequence shard 先完成；断言 catalog stage 顺序仍为 `0..N-1`，最终恢复图与串行基线相同。
2. 运行 focused 测试，确认当前实现按 worker 完成顺序直接入 catalog，无法满足新断言。
3. 在 `DispatchCatalogAsync` 维护 `nextSequence` 和有界 `SortedDictionary<long, CpgShardPublicationResult>`。每收到结果先缓存，再持续提交连续前缀；缓存容量达到 publication queue 上限时对上游产生背压。
4. 保留重复、缺失和越界 sequence 检查；取消或 worker 失败时清理重排缓冲并使 session invalid。
5. 运行 shard coordinator、catalog、slice 和 DOP 等价回归。

## 任务 2：以真实 catalog 成本触发 batch

**文件：**

- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/CpgCatalogBatchWriter.cs`
- 修改：`src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

1. 新增回归：一个大 `.cpgbin` 但 catalog 行数很少的 publication 可与后续 publication 合并；一个小 shard 但 node/span/symbol 行数很多时会按行数提前切批。
2. 将 `MaxCatalogBatchRows` 改为预估的 catalog SQL 行数，不再比较 `List<CpgCatalogPublication>.Count`。
3. 将 `MaxCatalogBatchBytes` 改为预估的 catalog metadata 内存，而不是 `lease.Location.ByteLength`；shard 文件已经落盘，不应拿其 payload 大小限制 SQLite 事务。
4. 保留两项阈值和最少一个 publication 的提交保证；telemetry 同时记录 publication 数、预估行数、实际行数、预估 metadata 字节和 commit 时间。
5. 运行 catalog 回归和基准单测，确认参数语义、稳定切批与异常传播。

## 任务 3：分块 set-based catalog 写入

**文件：**

- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

1. 新增大 shard 回归，记录每 transaction 的 statement 数；覆盖 node、span、symbol、boundary endpoint、重复位置和 SQLite 参数上限附近的切分。
2. 将同一 publication 的 `session_node_locations`、`session_span_locations`、`session_symbol_locations` 和 `session_boundary_node_locations` 改为多行 `INSERT` 分块，分块参数数严格小于 SQLite 上限。
3. 每个 build/file 只插入一次 `session_files`；不要对每个 shard 重复 `INSERT OR REPLACE`。
4. 保持单 connection、单 transaction 和已准备 command 的生命周期；不要重新启用会锁住短生命周期 `catalog.db` 的连接池。
5. 运行恢复、按 node/symbol/span 查询、boundary 查询、取消和失败回归；对比 shard hash 与 catalog 行集。

## 任务 4：使用 SQL 克隆完成增量复用

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 新增 changed-later-method 回归：命中一个 completed operation fragment 后，新的 session 复用其物理 shard 和完整 catalog 索引；断言不会调用 `CpgShardStore.ReadAsync` 或构造 `CpgFrozenShard`。
2. catalog lookup 返回候选的 source build、source shard 与 reusable key。对物理 shard 执行流式 hash/结构校验，验证成功后由 catalog 在单事务内 `INSERT … SELECT` 克隆 fragment owner、node/span/symbol/boundary/reusable 元数据到新 build。
3. 重建 file skeleton、changed fragment 和 boundary shard；仅在 reusable key、NodeId fingerprint、schema 与 profile 全部匹配时克隆。
4. 记录 clone hit/miss/reject、避免的物化字节、避免的 SQL 行数和 clone transaction 时间。
5. 运行增量构建、corrupt candidate、invalid session、取消与 DOP 等价回归。

## 任务 5：去除 streaming publisher 的重复扫描

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/Streaming/SkeletonShardPublisher.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`

1. 新增多方法 fixture，记录 descriptor owner 查找次数和 fragment 内容；基线证明当前 `PublishInitialAsync` 为每个 fragment 过滤完整 descriptor 集。
2. 单次遍历 descriptors，将其加入 skeleton bucket 或 fragment bucket；单次遍历 candidates 后按 owner 路由。禁止逐 fragment `Where` 扫描完整集合。
3. 将 boundary buffer telemetry 从每边调用 `_boundaryBatches.Sum(...)` 改为维护的计数器。
4. 保留 source order、NodeId 单 owner、初始 skeleton 可读性和最终 frozen graph 边恢复。
5. 运行 streaming checkpoint、fragment ownership、DOP snapshot 和 slice 回归。

## 任务 6：设计可回收的 session catalog

**文件：**

- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardSchema.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 测试：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

1. 新增 session 链式复用测试：新 session 复用旧 shard 后，清理旧 session 不得删除仍被引用的物理 shard 或索引。
2. 引入 completed-session manifest、物理 shard reference/liveness 表和显式 retention policy；只回收不可达、超过保留窗口且未被任何 completed session 引用的 session/shard。
3. 清理在独占 store writer 路径执行；restore/query 只读路径不得触发删除、`VACUUM` 或 catalog rebuild。
4. 单独提供 maintenance API 或工具入口；默认构建不做不可控的历史清理。
5. 覆盖多次增量复用、失败 session、损坏 shard、恢复、回收后 catalog 大小和 query 等价。

## 验证与发布裁决

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShard|FullyQualifiedName~SqliteCpgShardCatalog|FullyQualifiedName~RoslynCpgSliceQuery|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests"
dotnet test .\tests\RoslynDeletionPrototype.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-build -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-build -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

每个任务完成后运行对应 focused 测试；任务 2、3、4、5 完成后运行一次三样本预热后 benchmark。报告 wall-clock、catalog commit、SQL statement/row 数、queue/reorder peak、managed heap、working set、shard bytes、reuse/clone 计数和 DOP 图等价。默认值仅在固定单文件、多文件和真实增量 fixture 的中位数都显示稳定收益后调整。

## 风险与回滚

- source-order reordering 会产生 head-of-line wait；缓冲必须有界且通过 queue telemetry 证明其没有扩大内存峰值。
- SQL clone 与 retention 共享同一物理 shard 生命周期；缺少 reference/liveness 证据时禁止启用清理。
- set-based SQL 改动可能触及参数上限或冲突语义；每类索引必须保持与逐行写入相同的行集。
- 任一任务导致 graph、query、规则或 rewrite 结果回归时，关闭该任务新增路径，保留上一阶段已验证实现。
