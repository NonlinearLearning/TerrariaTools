# 缓存优化执行文档

> 状态：执行中。当前工作树已有阶段一、二的缓存改动；是否完成以本文件规定的阶段测试为准。

## 目标与边界

目标是减少相同缓存键的重复构造与重复分片读取，并为后续容量调整提供可靠数据。优化不能改变 CPG 查询结果、分片排序、取消语义、持久化完整性校验或 `AnalysisRuntime` 的 epoch 失效语义。

`NL.Caching` 只包含可复用的机械能力：弱键注册、字节预算 LRU、单飞控制和通用统计。以下策略必须留在缓存拥有者：

- `AnalysisRuntime`：`Compilation` 弱键、epoch、source/cache version 的失效规则。
- `CpgShardQueryResolver`：`ShardId`、`ByteLength` 权重、实例预算、读取失败和调用方取消。
- `SqliteCpgShardCatalog`：候选构建顺序和 routing-index 完整性校验范围。

不新增跨模块缓存单例、全局字节预算、默认 TTL，或对 `NL.Caching` 的领域类型依赖。

## 前置条件

1. 在开始前运行 `pwsh -File .\init.ps1`。
2. 所有 `dotnet build` 和 `dotnet test` 串行执行，避免共享输出目录锁冲突。
3. 每个阶段先写失败测试，再实现最小改动；完成一个阶段即运行该阶段测试。
4. 当前工作树含有删除规则 DAG 的并行改动；缓存工作只修改下列模块与对应测试。

## 阶段一：守住基础设施边界

修改 `tests/NLISSN.ContractTests/Architecture/CacheInfrastructureBoundaryTests.cs` 的旧目录断言，使其检查 `src/NLISSN.Infrastructure/Caching`。

验收：缓存项目仍不引用包、项目或 `Microsoft.CodeAnalysis`、`NLCPG`、`NLISSN` 的源码命名空间。

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~CacheInfrastructureBoundaryTests
```

## 阶段二：弱键缓存的单飞构造

在 `WeakTypedCacheRegistry<TKey>` 中按“弱键 + 值类型”缓存 `Lazy<object>`，使用 `ExecutionAndPublication` 保证并发请求只执行一次工厂。工厂抛出时，只有当前 `Lazy` 仍为字典值才删除它，使后续请求可以重试，且不会删除已由并发请求替换的值。

测试位于 `tests/NLISSN.UnitTests/Caching/WeakTypedCacheRegistryTests.cs`，必须覆盖：

- 相同 key 和 value type 的并发请求只执行一次工厂，且返回同一对象；
- 工厂失败后下一次请求可以重新构造；
- 不同值类型仍各自缓存；
- 不同弱键不会共享结果。

```powershell
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~WeakTypedCacheRegistryTests
```

## 阶段三：LRU 统计快照

新增 BCL-only 的 `CacheStatistics` 快照，且只在 `ByteBudgetLruCache` 的锁内更新与读取。字段至少包括 `HitCount`、`MissCount`、`InsertCount`、`ReplaceCount`、`EvictionCount`、`RejectedCount`、`CachedBytes` 和 `Count`。

统计语义固定如下：

| 操作 | 计数变化 |
| --- | --- |
| `TryGet` 找到条目 | `HitCount +1` |
| `TryGet` 未找到条目 | `MissCount +1` |
| 新 key 成功写入 | `InsertCount +1` |
| 已有 key 成功覆盖 | `ReplaceCount +1` |
| 为容量移除 LRU 条目 | `EvictionCount +1` |
| 零权重或超过预算 | `RejectedCount +1` |

`Clear` 只清空保留条目与字节数，不重置累计计数。为 `CpgShardQueryResolver` 暴露只读诊断快照，不暴露底层缓存对象。

验收测试覆盖命中、未命中、写入、替换、逐出、拒绝、当前条目数和字节数。

## 阶段四：分片读取单飞

在 `CpgShardQueryResolver` 内维护以 `ShardId` 为键的 in-flight task 表。缓存未命中后，各调用方共享一个不绑定任何调用方取消令牌的读取任务；每个调用方使用自身的 `WaitAsync(cancellationToken)` 等待。

实现约束：

- 只成功读取的分片写入 LRU；
- 成功和失败均按 task 身份条件删除 in-flight 项；
- 一个等待者取消不能取消共享 I/O，也不能影响其他等待者；
- 读取失败后新调用必须能够重试；
- 输出顺序仍按既有 `ShardId` 排序。

在 `tests/NLISSN.ContractTests/Cpg/CpgShardQueryResolverTests.cs` 用可阻塞的 test-local store 验证同一 location 的并发未命中只有一次物理 `ReadAsync`，并覆盖局部取消和失败重试。

## 阶段五：冷/热测量

在 `tests/NLISSN.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs` 使用同一 fixture 先运行冷查询，再运行热查询。记录：LRU 统计、物理读取次数、分配字节数、耗时、序列化字节数与进程保留内存变化。

验收不使用机器相关的耗时阈值，而是要求：

- 热查询物理读取更少；
- 冷/热的有序 shard ID 与 slice 结果完全一致；
- 诊断输出包含可归因的缓存数据。

如反序列化驻留内存显著大于 `ByteLength`，再由 `CpgShardQueryResolver` 提供 `CpgFrozenShard` 权重估算器。只有测量证明锁竞争主导时，才评估分片缓存或近似 LRU。

## 阶段六：routing-index 候选流式化门槛

完整性契约已选择为“只验证查询实际检查的候选项”。`SqliteCpgShardCatalog` 按 completed 时间顺序异步枚举候选 routing index；每个被枚举项仍验证文件存在、长度、格式与 payload hash，首个命中后停止，不再为无关历史 build 预加载或验证 routing index。

后续夹具仍应覆盖最新命中、旧构建回退、损坏旧 manifest、取消和返回顺序：

1. 仅验证查询找到结果前实际检查的候选项，可按时间顺序流式读取并在命中后停止；或
2. 每次查询都验证所有 completed build，则保留当前预加载，优化发布、保留策略或独立 manifest 索引。

这不是忽略已检查候选的损坏；它将无关历史构建的损坏检查延后到该构建实际参与查询时。

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~SqliteCpgShardCatalogTests
```

## 最终验证与提交边界

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\NLISSN.Infrastructure\Caching\NL.Caching.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~ByteBudgetLruCacheTests|FullyQualifiedName~WeakTypedCacheRegistryTests"
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~CacheInfrastructureBoundaryTests|FullyQualifiedName~CpgShardQueryResolverTests|FullyQualifiedName~NLCPGSliceQueryTests|FullyQualifiedName~SqliteCpgShardCatalogTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

失败时不合并该阶段。回滚范围限定为当前阶段的缓存实现和测试；不得撤销并行的规则 DAG 改动。完成时更新 `feature_list.json` 的 definition of done，并在 `progress.md` 仅记录当前阶段、已验证命令、未完成门槛与测量结论。

## GitHub 依据

- [.NET `MemoryCacheOptions`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Memory/src/MemoryCacheOptions.cs)：缓存实例的容量与扫描机制属于引擎配置。
- [.NET `MemoryCacheEntryOptions`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Abstractions/src/MemoryCacheEntryOptions.cs)：TTL、失效令牌、优先级和大小由条目写入者提供。
- [.NET `MemoryCache`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Memory/src/MemoryCache.cs)：引擎统一维护命中、未命中和逐出等通用观测。
- [ASP.NET Core 缓存文档](https://github.com/dotnet/AspNetCore.Docs/blob/9c02a6b940abba5b3871798b098fafadd7b1d46c/aspnetcore/performance/caching/memory.md)：共享缓存设置 `SizeLimit` 会要求所有使用者遵守相同 size 语义，受限缓存应使用专用实例。

更完整的仓库事实与外部证据见 `docs/plans/2026-07-30-cache-infrastructure-policy-boundary-research.md`；具体逐文件实施顺序见 `docs/plans/2026-07-30-cache-efficiency-execution-plan.md`。
