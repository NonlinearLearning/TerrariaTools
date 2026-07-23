# Streaming CPG 分片与 catalog 性能执行计划

> **状态：已完成。** 实现、聚焦回归、基准测量和完整仓库验证均已通过。本页替代原有 streaming shard 后续计划，聚焦完整图持有、重复分片扫描、伪并发写入、catalog 写放大和共享 store 写入冲突。

后续 Strict 临时文件读回校验、线性结构校验和跨 session 方法分片复用由
`2026-07-23-strict-shard-validation-and-incremental-reuse-execution-plan.md` 追踪；
其验收状态不改变本页已完成的原始 streaming 改造范围。

## 已执行结果（2026-07-22）

- 一次构建 `NodeId -> fragment owner` 索引，协调器与 skeleton 发布不再为每个 fragment 重扫完整图。
- shard 写入改为有界多 worker 管线；catalog 保持单一、按源序重排的提交者。相同 lookup 的重复发布会在入队前去重，避免并发竞争同一个临时文件。
- 增加 `Strict`（默认）和 `Throughput` durability 模式。吞吐模式在 session 完成前集中 flush 并全量读取校验，未完成 session 仍不可见。
- store 锁支持取消与超时等待；同进程 guard 的释放顺序保证等候 writer 可接管锁。
- catalog 增加 boundary 两端节点索引，slice 从任一边端点都能发现对应 boundary shard；schema 初始化改为异步门控。
- SQLite catalog 的 batch writer 在 build session 内复用一个非池化连接。Windows 上池化连接会持有短生命周期 store 的 `catalog.db`，破坏 session 完成后立即删除的恢复/测试契约；批量事务、显式 command disposal 和 schema 初始化去重仍然保留。
- telemetry 现记录锁等待、序列化、校验、flush、catalog 提交、每批 catalog 行数、队列峰值和实际文件写并发。worker 注入失败会使 session 不可见并清理 staging；共享 store 的第二个构建会等待、随后完成。
- 聚焦 CPG shard/catalog/slice/partition/NodeId 回归通过 `122/122`，其中含 DOP 1/8/12/14/16 与 Strict/Throughput 的 shard-backed slice 等价测试，以及两种 durability 下的 worker 失败清理。2026-07-23 完整测试通过 `543/543`（6 分 21 秒），`pwsh -File .\scripts\check-harness-consistency.ps1` 通过。测试工程显式排除运行产物 `Build/**`，避免 Terraria 样本副本被当作测试源；CLI smoke 的 nullable warning 已清除。
- `tools/CpgPersistenceBenchmark` 于 2026-07-22 运行了每组合 1 次预热、3 次采样，最终报告为 `Build/cpg-persistence-benchmark-20260722-final2.json`。所有组合的节点/边计数一致：单文件 `10589/25738`，多文件 `10734/25788`。单文件 Strict 的最佳中位数为 DOP 8 `3812 ms`，Throughput 为 DOP 12 `5305 ms`；多文件 Strict 的最佳中位数为 DOP 16 `1723 ms`，Throughput 为 DOP 1 `1802 ms`。没有跨 fixture 和 durability 的统一最优 DOP，故默认 DOP 不变；Strict 仍为默认，Throughput 仅显式启用。

## 目标

在图节点、边、查询、规则和 rewrite 输出完全等价的前提下，降低 CPG shard 持久化的 CPU、内存和 I/O 开销，并让目录并行分析能够安全复用同一个 `StoreRoot`。

## 已确认问题

1. `RoslynCpgBuilder` 在完整 CPG 构建并冻结后才调用持久化；当前 `StreamingMode` 不降低完整图的峰值常驻内存。
2. `CpgShardBuildCoordinator` 为每个方法 fragment 重扫 `context.Graph.Nodes`，开销至少为 `O(fragmentCount * nodeCount)`。
3. fragment 发布在循环中逐个等待，`MaxConcurrentShardFileWrites` 不会形成真实并发。
4. 每个 shard 都执行序列化、hash、临时文件写入、同步 flush、完整反序列化校验和原子移动。
5. catalog writer 是单消费者；批次会新建未池化连接并重复 schema 检查，node、span 和 symbol 记录逐条创建 SQL command。
6. 同一个 store 的第二个 writer 立即失败，目录并行与共享持久化无法同时使用。

## 不可变边界

- DOP 1、8、12、14、16 以及 streaming/non-streaming 的图快照、slice 查询、规则结果和 rewrite 输出必须等价。
- worker 只读取 Roslyn semantic facts；图节点、边、catalog 提交顺序和可见性仍由稳定调用线程控制。
- 未完成或失效 session 不能参与恢复或查询；损坏 shard、遗留 `.tmp` 和丢失 catalog 必须保持当前的可恢复行为。
- 同一个 `StoreRoot` 同时只允许一个实际 writer；后到构建进入可取消、有界等待，不允许多 writer 并发写 SQLite。
- 持久化默认保持严格 durability；吞吐模式必须显式启用。

## 持久化模式

| 模式 | shard 写入 | 可见性 | 默认值 |
| --- | --- | --- | --- |
| `Strict` | 保留每 shard 的同步 flush 和完整反序列化校验 | 仅完成 session 可见 | 是 |
| `Throughput` | 保留临时文件、payload hash、原子移动和轻量 header 校验；将最终 durability 操作收敛到受控 batch 或 session 完成阶段 | 仅完成 session 可见 | 否 |

`Throughput` 只减少每 shard 的同步磁盘与重复解码成本，不放宽完成 session、hash 校验、catalog 重建或恢复图的正确性要求。

## 执行顺序

### 1. 补齐测量与回归基线

- 扩展 `CpgPersistenceTelemetry`，记录锁等待、队列深度、实际文件写并发、序列化、校验、flush、catalog commit 和每批 SQL 行数。
- 为当前实现建立固定大文件和多文件 fixture 的预热后基线；每个端点运行三次并记录中位数 wall-clock、managed heap、working set、shard bytes 和 telemetry。
- 先锁定当前 shard 内容、source order、session 可见性、恢复图和 DOP 等价回归。

### 2. 消除重复图扫描

- 在 `CpgShardBuildCoordinator` 中一次建立 node-to-fragment ownership 索引，并按 owner 聚合节点。
- 从该索引导出 file skeleton、方法 fragment 和 boundary edge，避免每个 fragment 重扫整张 `context.Graph.Nodes`。
- 保留 source order、operation NodeId 单一 owner、boundary adjacency 和 frozen graph 恢复结果。

### 3. 实现受控 shard 写并发

- 将 shard 导出与 `PublishFragmentAsync` 解耦：稳定顺序生成 publication 描述，经有界队列交给文件写 worker。
- `MaxConcurrentShardFileWrites` 成为文件写入的真实并发上限；catalog publication 按 source order 提交，保证可重现的 catalog 与 telemetry。
- 覆盖 queue 满载、单 shard 写失败、取消和 session 失效，确保排队工作不会在失败后继续发布。

### 4. 降低 shard 写路径成本

- 为 `CpgPersistenceOptions` 增加显式 durability 模式，默认 `Strict`。
- `Strict` 维持当前 flush 和完整反序列化校验。
- `Throughput` 保留原子发布和 hash，改用轻量 header 校验，并在完成 session 的受控阶段执行最终 durability；异常、取消或最终校验失败时将 session 标为 invalid 并清理 staging。
- 对两种模式分别验证恢复、损坏 shard、遗留 `.tmp`、catalog 丢失重建和图等价。

### 5. 收紧 catalog 写入

- `SqliteCpgShardCatalog` 在 build session 内复用连接与预编译 command；启用连接池，schema 初始化只在创建或迁移时执行。
- `CpgCatalogBatchWriter` 保持单一确定性提交者，但以单事务、参数复用和批量行写入处理 node、span 与 symbol location。
- batch 行数、字节上限和队列容量保持配置项；默认值仅在基准显示吞吐或内存受限后调整。

### 6. 将共享 store 竞争改为有界等待

- 将 `CpgShardStoreLock` 改为支持 cancellation token、等待超时和等待 telemetry 的异步 acquire。
- 等待成功后继续创建独立 build session；等待超时或取消时报告明确异常，且不创建半成品 catalog 记录或 shard 文件。
- 保留进程内 guard 与跨进程命名锁，避免同一进程或多个进程同时成为 writer。

## 验证

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~CpgShard|FullyQualifiedName~SqliteCpgShardCatalog|FullyQualifiedName~RoslynCpgSliceQuery|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests"
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

额外执行固定大文件和多文件 fixture 的 DOP 1、8、12、14、16 对比；每个 Strict/Throughput 端点预热后运行三次。报告中位数 wall-clock、managed heap、working set、节点/边计数、shard bytes、文件写耗时、catalog commit、锁等待、队列峰值和实际文件写并发。

## 完成条件

- 节点、边、slice、规则和 rewrite 在全部 DOP 与两种 durability 模式下保持等价。
- 每 shard 不再重复扫描全图；`MaxConcurrentShardFileWrites` 在 fixture 中可观察到实际受控并发。
- 共享 `StoreRoot` 的并发构建能按有界等待完成、取消或超时，不再因竞争立即失败。
- `Strict` 与 `Throughput` 都通过 shard 恢复、损坏、取消、未完成 session 和 catalog 重建回归。
- 基准结果说明每项默认值是否保持；没有收益的优化不得改变默认配置。

## 风险与回滚

- catalog 提交顺序、session 可见性或 shard owner 改动可能破坏恢复和查询；每一阶段先跑 snapshot 与恢复回归，再扩大基准。
- `Throughput` 的最终 durability 边界只能在故障注入测试通过后启用；默认仍为 `Strict`。
- 文件写并发和 catalog 单提交者分离后，异常处理必须取消未提交 publication 并使 session 不可见。
- 任一阶段出现图、查询、规则或 rewrite 回归时，关闭对应新模式，保留前一阶段已验证实现。
