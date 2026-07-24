# CPG 最小 catalog 路由索引执行提案

> **状态：实现与 Contract 验证已完成；真实源三样本发布基准待执行。**
>
> **目标：** 在不改变持久化图、shard-backed slice、规则或 rewrite 结果的前提下，将高基数 node/span/symbol 路由记录从 SQLite 移到受校验的构建级二进制 routing sidecar，保留 SQLite 的 session、shard 与跨 shard 路由职责。

## 背景与问题

2026-07-24 的固定 103 文件、DOP 1、Strict、`catalog-batch-rows=4096` 基准耗时
`1,857,288 ms`。其中 catalog 提交 `896,695 ms`，catalog 队列饱和
`758,206 ms`，`ExecuteNonQuery` `663,124 ms`。严格 shard 写入与验证分别为
`99,230 ms` 和 `49,248 ms`，不是主要瓶颈。

该样本生成 `5,309,597` 个节点、`12,451,142` 条边、`11,182` 个 shard，却产生
`19,517,368` 条 catalog 行和 `140,790` 条 SQL 语句。最终 `.cpgbin` 为约
`0.82 GiB`，运行期 StoreRoot 峰值为 `6,733,432,001` bytes；额外空间主要来自
catalog 的高基数位置表及其索引。

当前 `SqliteCpgShardCatalog.StageOneAsync` 会写入：

- `session_node_locations`
- `session_span_locations`
- `session_symbol_locations`
- `session_boundary_node_locations`

这些表支持 `ICpgShardCatalog.FindByNodeAsync`、`FindBySpanAsync` 和
`FindBySymbolAsync`。因此本提案不是直接删除表，而是先提供等价的二进制查询路径。

## 目标架构

| 层 | 保留内容 | 作用 |
| --- | --- | --- |
| SQLite catalog | build、file、fragment、shard location、fragment owner、boundary adjacency | 快速路由和 session 可见性 |
| 构建级 shard routing sidecar | `NodeId -> primary shard/local offset`、boundary node 到 adjacency shard、span 范围、symbol 到 shard/local offset | 精确定位，按需加载 |

sidecar 位于 `builds/<buildId>/routing.cpgidx`。它是一个构建级、按 shard 分区的
二进制索引，而非逐 shard 扫描文件：一次查找即可得到目标 `shardId`，随后仍由
SQLite 返回 `CpgShardLocation`，并由现有 `CpgShardQueryResolver` 读取 `.cpgbin`。

`routing.cpgidx` 的首版格式必须包含：

1. magic、格式版本、`buildId`、schema/profile 指纹、完整 payload hash。
2. 按 `NodeId` 排序的 primary-node 条目：`NodeId`、`shardId`、local offset。
3. 按 `NodeId` 排序的 boundary-node 条目：`NodeId`、boundary-adjacency `shardId`。
4. 按 file/span 排序的条目：file identity、span start、span length、`shardId`。
5. 按 symbol key 排序的条目：symbol key、`shardId`、local offset。

node 查找先命中 primary-node 条目；SQLite 再以 `session_fragment_owners` 和
`session_fragment_adjacencies` 补齐该 primary fragment 的 boundary shards。若只命中
boundary-node 条目，则直接返回其 boundary shards。这保持现有 `FindByNodeAsync`
的结果集合与排序语义。

## 不可变边界

- `ICpgShardCatalog` 和 `CpgShardQueryResolver` 对调用者的 `FindByNodeAsync`、
  `FindBySpanAsync`、`FindBySymbolAsync` 行为与结果排序保持不变。
- 仅 `Complete` build 可被 sidecar 查询；`Building`、`Invalid`、缺失、截断或 hash
  不匹配的 sidecar 不可见。
- `Strict` 仍对 shard 保持现有 flush 与读回结构校验；sidecar 也必须写入 `.tmp`、
  flush、读回 hash/结构校验后再原子移动。
- 同一 `StoreRoot` 仍只有一个实际 writer；catalog 仍是单一、按源序的提交者。
- 旧 completed build 使用旧位置表；新 build 只有在 sidecar manifest 完整后才切换到
  sidecar。不得自动删除仍被旧 build 查询的表或记录。
- DOP 1、8、12、14、16 以及 streaming/non-streaming 的图快照、slice、规则和
  rewrite 输出必须等价。

## 非目标

- 不改变默认 DOP、`MaxConcurrentShardFileWrites`、Strict 默认值或 SQLite 同 writer
  规则。
- 不以关闭 SQLite durability、增加多个 SQLite writer 或扩大队列隐藏背压。
- 不在本提案中重做 `.cpgbin` 图格式、fragment 复用或规则/改写流程。

## 执行阶段

### 阶段 0：锁定读取契约和基线

**文件：**

- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- 修改：`tools/CpgPersistenceBenchmark/Program.cs`

**步骤：**

1. 为 node、boundary node、span、symbol 查询增加行为测试：新旧 completed build
   的选择、返回 shard 集合及按 `ShardId` 的稳定排序。
2. 以现有 SQLite 路径建立同一 fixture 的结果快照；断言 shard-backed slice 与内存图
   的节点、边、truncation 和 unavailable 结果一致。
3. 在 benchmark JSON 增加 catalog 文件字节数、各位置表行数、sidecar 字节数与
   node/span/symbol 查询计数；采样实际 benchmark 子进程而不是 `dotnet run` 启动器。
4. 运行 RED/GREEN 前的现有测试，保留基准 JSON，不修改任何默认值。

**门槛：** 明确每一个位置表对应的公开查询行为；不能以表结构或私有方法断言替代
查询结果断言。

### 阶段 1：实现可验证的 routing sidecar

**文件：**

- 新建：`src/MinimalRoslynCpg/Persistence/CpgBuildRoutingIndex.cs`
- 新建：`src/MinimalRoslynCpg/Persistence/CpgBuildRoutingIndexWriter.cs`
- 新建：`src/MinimalRoslynCpg/Persistence/CpgBuildRoutingIndexReader.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/CpgShardContracts.cs`
- 新建：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgBuildRoutingIndexTests.cs`

**步骤：**

1. 先写失败测试，覆盖 primary node、boundary node、span、symbol 的精确查询、空结果、
   排序、重复值去重、截断文件、错误版本和 hash 不匹配。
2. 定义仅包含稳定值的二进制格式；不序列化 Roslyn 对象、字典枚举顺序或绝对临时路径。
3. writer 接收按源序的 `CpgFrozenShard` 与 `CpgShardLocation`，写 `.tmp` 后执行与 Strict
   相同级别的读回 hash/结构校验，最后原子移动为 `routing.cpgidx`。
4. reader 使用二分查找或目录偏移定位排序段；读取损坏内容必须抛出
   `InvalidDataException`，不能返回部分路由结果。
5. 仅在测试中比较 sidecar 与旧 catalog 查询结果；此阶段不改变生产读取路径，也不停止
   SQLite 位置表写入。

**门槛：** sidecar 对所有已锁定查询返回与旧 catalog 完全相同的 shard IDs；损坏或
不完整文件不会被视为可用索引。

### 阶段 2：在 build session 中双写并原子发布

**文件：**

- 修改：`src/MinimalRoslynCpg/Builder/CpgShardBuildSession.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/CpgCatalogBatchWriter.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardSchema.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgShardBuildCoordinatorTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

**步骤：**

1. 增加 build-level sidecar manifest：build ID、相对 sidecar 路径、格式版本、字节数、
   payload hash。它属于 SQLite 的 build 元数据，不保存 node/span/symbol 明细。
2. 让 `CpgShardBuildSession` 在接收已写 shard publication 时累积 routing 条目；保持
   catalog dispatcher 的源序与单提交者语义。
3. 在 `CompleteAsync` 中按以下顺序完成：等待 worker 与 catalog drain，写并验证
   `routing.cpgidx.tmp`，原子移动 sidecar，在同一个 catalog finalize transaction 中写入
   manifest 并将 build 标为 `Complete`，最后写 completion marker。
4. sidecar 写入、校验或 manifest 提交任一步失败时，使 session invalid、删除 staging
   内容并保持旧 completed build 可查询。
5. 对取消、文件写失败、catalog fault、sidecar mutation 和 finalize fault 编写测试；断言
   不会出现“Complete build 缺 sidecar”或“sidecar 可见但 build 未完成”。

**门槛：** 双写 build 的 legacy catalog 与 sidecar 查询结果逐项相等；任何失败都不泄漏
可查询的新 build。

### 阶段 3：迁移查询路径并保留旧 build 回退

**文件：**

- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/MinimalRoslynCpg/Analysis/CpgShardQueryResolver.cs`
- 修改：`src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`（仅在需要暴露 routing telemetry 时）
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/RoslynCpgSliceQueryTests.cs`

**步骤：**

1. 为 catalog 增加内部查询：选择符合现有 newest-completed 规则的 build，按 build ID 与
   shard ID 批量解析 `CpgShardLocation`，并通过 owner/adjacency 扩展 primary shard。
2. `FindByNodeAsync`、`FindBySpanAsync`、`FindBySymbolAsync` 对有有效 manifest 的 build
   使用 sidecar；没有 manifest 的旧 build 继续查询旧位置表。
3. 保持 `ICpgShardCatalog` 方法签名、`CpgShardQueryResolver` 缓存键和返回顺序不变。
   任何 sidecar 校验错误均不得静默回退到不同 build；应报告不可用索引，并保持旧 build
   可按其原有路径查询。
4. 为每种 lookup 增加 sidecar 命中、legacy fallback、校验失败、sidecar bytes read 和
   查询耗时 telemetry；不能把 sidecar 的磁盘读取计入 shard payload bytes。

**门槛：** persisted slice、跨 shard boundary、symbol 与 span lookup 在新旧 build 上均与
基线相同；缓存命中/未命中计数保持解释一致。

### 阶段 4：停止新 build 的高基数 SQLite 位置表写入

**文件：**

- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/CpgCatalogBatchWriter.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardCatalog.cs`
- 修改：`src/MinimalRoslynCpg/Persistence/Sqlite/SqliteCpgShardSchema.cs`
- 修改：`src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/CpgCatalogBatchWriterTests.cs`
- 修改：`tests/RoslynDeletionPrototype.ContractTests/Cpg/SqliteCpgShardCatalogTests.cs`

**步骤：**

1. 新增内部、默认关闭的 routing-sidecar 写入开关；双写验收通过后才将新 build 默认写入
   sidecar 并停止写入四张 session 位置表。
2. 保留 `session_files`、`session_fragments`、`session_shards`、
   `session_fragment_owners`、`session_fragment_adjacencies`、复用元数据、物理 shard
   引用与 build manifest。
3. 新 StoreRoot 的 schema 不再创建高基数位置表；既有 StoreRoot 保留旧表和旧 build 的
   回退能力。仅在所有 legacy build 已 prune 后，允许显式维护操作回收旧表空间，禁止
   自动 destructive migration。
4. 验证新 build 的位置表行数为零、routing sidecar 可定位全部 node/span/symbol/boundary
   查询，且 catalog manifest/owner/adjacency 仍足以恢复跨 shard 查询。

**门槛：** 新 build 不再向四张位置表写明细；旧 build 保持可读；切换开关可在不破坏
已完成 session 的情况下回退。

### 阶段 5：基准、发布判定与文档状态

**文件：**

- 修改：`tools/CpgPersistenceBenchmark/Program.cs`
- 修改：`scripts/Run-CpgPersistenceBenchmark.ps1`
- 修改：`tests/RoslynDeletionPrototype.PerformanceTests/Cpg/CpgPersistenceBenchmarkConfigurationTests.cs`
- 修改：`progress.md`
- 修改：`feature_list.json`
- 修改：本提案

**步骤：**

1. 在同一 103 文件、DOP 1、Strict、file write 1、catalog batch 4096 fixture 上，分别运行
   legacy 与 sidecar 路径：1 次预热、至少 3 个样本，报告中位数与离散度。
2. 记录 wall-clock、catalog commit、queue saturation、`ExecuteNonQuery`、SQL 行/语句数、
   catalog bytes、sidecar bytes、StoreRoot 峰值、managed heap、working set、node/edge/shard
   计数和查询 p50/p95。
3. 对 DOP 1、8、12、14、16 的 streaming/non-streaming 做等价验证；只有图、slice、规则
   与 rewrite 均相等时才比较性能。
4. 更新 `feature_list.json` 的完成条件和验证证据；`progress.md` 只保留当前结论、阻塞与
   下一步。不得用单样本或不同图规模的结果改变默认值。

**发布门槛：**

- 所有语义回归通过，且新 build 的 catalog 行数与 bytes 明显下降。
- 对 103 文件基线，`CatalogExecuteNonQueryMilliseconds` 和 queue saturation 均下降；若只
  降低磁盘空间但恶化查询 p95 或端到端时间，则保持 dual-write 实验状态，不切默认。
- Strict 默认、单 writer、session visibility 与恢复契约不变。

## 验证顺序

每个阶段先运行最小 owning project，再扩大范围：

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path

dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~CpgBuildRoutingIndexTests|FullyQualifiedName~SqliteCpgShardCatalogTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~CpgShardBuildCoordinatorTests|FullyQualifiedName~CpgCatalogBatchWriterTests"

dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false

pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

真实源基准必须使用同一输入 manifest、同一 DOP、同一 durability 与相同并发上限；每个
端点至少三次预热后采样，并保留 JSON、runtime JSONL 与 StoreRoot 字节证据。

## 风险与回滚

| 风险 | 控制措施 | 回滚 |
| --- | --- | --- |
| sidecar 漏掉 node/span/symbol 路由 | 先双写并按 lookup 行为逐项比对 | 保持 legacy 位置表读取 |
| sidecar 损坏后返回错误 shard | 完整 hash/结构校验，查询失败不返回部分结果 | build 不标记 Complete，或读取 legacy build |
| sidecar 引入冷查询退化 | 记录 bytes read、查询 p50/p95 与缓存命中 | 保持 routing-sidecar 开关关闭 |
| schema 迁移破坏历史 StoreRoot | 旧 build 继续走旧表，不自动删除旧表 | 停止新路径写入，保留旧 schema 数据 |
| catalog 变小但规则/改写结果漂移 | graph/slice/Mark/Decision/Rewrite 三层等价测试 | 禁止默认切换，修复后重新双写验证 |

## 完成条件

- SQLite 对新 build 仅保存最小路由和可见性元数据，不再保存 node/span/symbol/boundary
  的高基数明细行。
- sidecar 能精确且确定性地定位所有现有 node、span、symbol 与 boundary 查询。
- 新旧 completed build 可并存、可查询、可恢复；无效或不完整 build 永不可见。
- 所有 Contract、Host、规则/rewrite 以及 DOP 等价验证通过。
- 重复真实源基准证明 catalog SQL 时间、队列背压和 StoreRoot 空间下降，且没有端到端或
  查询延迟回归。
