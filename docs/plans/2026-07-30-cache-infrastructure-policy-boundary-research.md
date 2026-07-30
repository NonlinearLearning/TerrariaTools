# 缓存基础设施与策略边界调研

> 状态：调研结论，未实施。外部源码快照时间：2026-07-30。

## 结论

`NL.Caching` 不应接管跨领域的全局缓存策略。它可定义单个专用 cache engine 的不可变技术选项，例如总预算、逐出算法、扫描节奏与通用统计钩子；实例拥有者在创建时提供这些值。容量数值、TTL、失效触发、领域键、缓存实例生命周期，以及指标的领域归属仍由缓存拥有者定义。

全局策略只适合覆盖同一缓存实例内所有条目都能遵守的机械约束。当前不存在这种跨 `AnalysisRuntime` 与 `CpgShardQueryResolver` 的统一单位、生命周期或失效语义，因此不应增加跨模块的 `CacheOptions`、默认 TTL 或总预算；当前也没有必须立即抽取的实例选项对象。

## 本仓库事实

| 缓存 | 当前拥有者 | 已有机制 | 必须留在拥有者的策略 |
| --- | --- | --- | --- |
| compilation cache | `AnalysisRuntime` | `WeakTypedCacheRegistry<Compilation>` | `Compilation` 弱键生命周期；`NextEpoch`/`InvalidateCaches` 的 source 与 cache version 语义 |
| frozen shard | `CpgShardQueryResolver` | `ByteBudgetLruCache<string, CpgFrozenShard>` | shard id 键、`ByteLength` 权重、构造时的保留预算、catalog 查询顺序、I/O 失败与取消边界 |

`WeakTypedCacheRegistry` 只能按弱键和值类型复用实例，不能知道 compilation 何时因源码变更而失效。`ByteBudgetLruCache` 只能执行调用者给出的 byte weight 与预算，不能判断 shard 的新鲜度或业务优先级。把这些判断移入 `NL.Caching` 会让基础设施反向依赖 CPG 或分析运行时语义。

## 官方源码证据

### .NET MemoryCache

- [`MemoryCacheOptions`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Memory/src/MemoryCacheOptions.cs) 定义的是单个 cache engine 的扫描频率、容量上限、压缩百分比和统计开关；`SizeLimit` 的单位由写入者自己定义。
- [`MemoryCacheEntryOptions`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Abstractions/src/MemoryCacheEntryOptions.cs) 将 absolute/sliding expiration、change token、逐出回调、priority 和 size 放在条目选项中。条目写入方因此持有具体 TTL、失效和权重语义。
- [`MemoryCache`](https://github.com/dotnet/runtime/blob/7a6987d6e1ca353163a8684054b71693c9db8fc6/src/libraries/Microsoft.Extensions.Caching.Memory/src/MemoryCache.cs) 只执行这些选项：超出 `SizeLimit` 时按 `CompactionPercentage` 压缩；启用 `TrackStatistics` 后汇总 hits、misses、evictions 与当前估算大小。它不推断条目的领域键或有效性。
- [ASP.NET Core 官方缓存文档](https://github.com/dotnet/AspNetCore.Docs/blob/9c02a6b940abba5b3871798b098fafadd7b1d46c/aspnetcore/performance/caching/memory.md) 明确警告：DI 的 shared cache 设置 `SizeLimit` 后，所有使用者都必须提供 size，应用可能失败；需要受限缓存时创建专用 cache singleton。文档也要求用 absolute expiration 约束只使用 sliding expiration 的无限续期风险。

这套分层说明容量算法和计数可由 cache engine 提供，但容量单位、条目 TTL 与失效原因属于拥有该实例和写入条目的模块。共享实例无法安全强加统一策略。

### ASP.NET Core Redis cache 与 Aspire

- [`RedisCacheOptions`](https://github.com/dotnet/aspnetcore/blob/2e05e269f599be5615cea4fcd2d27f7080f6e54f/src/Caching/StackExchangeRedis/src/RedisCacheOptions.cs) 只配置连接、multiplexer 工厂、实例前缀和 profiling session。`InstanceName` 是部署级键空间隔离，不替代领域键定义。
- [`RedisCache`](https://github.com/dotnet/aspnetcore/blob/2e05e269f599be5615cea4fcd2d27f7080f6e54f/src/Caching/StackExchangeRedis/src/RedisCache.cs) 的每次 `Set`/`SetAsync` 接收 `DistributedCacheEntryOptions`，据此写 absolute/sliding expiration 和 Redis TTL；后端负责执行，调用者给出有效期。
- [`RedisBuilderExtensions`](https://github.com/dotnet/aspire/blob/a04b82fa8ca765662ad06952a375b603152b3328/src/Aspire.Hosting.Redis/RedisBuilderExtensions.cs) 把 Redis 的连接、卷和持久化快照参数绑定到 named resource。它没有为应用数据定义键、TTL 或失效规则。

真实 .NET 基础设施将资源连接、存储执行、通用观测与应用条目策略分离；本仓库的 in-process cache 应沿用该责任划分。

## 建议

1. 保持 `NL.Caching` 的 BCL-only 机制边界。`WeakTypedCacheRegistry` 和 `ByteBudgetLruCache` 不接收 `AnalysisEpoch`、`Compilation` 版本、`CpgFrozenShard`、catalog 或规则类型。未来确有多个同类实例时，可在该项目定义 immutable engine options，但调用方显式创建并传值。
2. 容量与逐出：`ByteBudgetLruCache` 保留传入的单实例 byte budget 与 LRU 实现；预算值和每项的 byte weight 继续由 `CpgShardQueryResolver` 决定。不要引入全局 byte budget，因为 compilation cache 没有相同的大小单位与保留目标。
3. TTL 与失效：只在拥有者能说明数据新鲜度时引入。分析事实应由 compilation 弱键和 epoch/version 驱动；shard 读取结果只有在 catalog/store 的版本协议提供可验证新鲜度后才考虑 TTL 或版本化 key。基础设施可以提供显式删除、clear 或通用过期容器，但调用方必须在写入时提供策略和失效信号。
4. 可观测性：可在基础设施增加低成本、可选的通用 snapshot，例如 hit、miss、insert、replace、eviction、rejected、retained bytes 与 count。指标名称、缓存实例标识、`Activity`/日志归属和告警阈值由拥有者注入；不要让基础设施了解规则、shard 或 compilation 名称。
5. 加功能前建立 warmed measurement：按拥有者分别记录 hit/miss、读取或构建延迟、分配量和 retained bytes，并验证输出等价。当前没有 warmed 实测，不能以全局默认容量、TTL 或淘汰策略替换现有语义。

## 可接受的接口形状

基础设施可接收纯技术参数，例如 `maxCachedBytes`、`byteWeight`、可选 clock、每条目的 TTL 和通用 metrics sink。调用模块应保留构造这些参数的职责。接口不能要求领域对象实现缓存策略接口，也不应把领域 key 序列化、生命周期事件或失效原因下沉到 `NL.Caching`。

后续若有两个以上缓存实例共享同一种可证明的单位、同一新鲜度来源和同一逐出 SLA，再提取一个由调用方显式创建的专用 policy object；该对象仍应由对应应用/领域模块拥有，不能作为所有缓存的隐式全局默认值。
