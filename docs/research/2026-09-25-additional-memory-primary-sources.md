# 前述六项之外的内存优化方向：源码与官方资料交叉审查

审查日期：2026-09-25（Asia/Shanghai）。范围：当前工作树静态审查、已有性能报告复核、官方资料检索。本文不执行实现计划，不修改产品代码，不运行构建、测试或性能采样，不改变 feature 状态。

## 1. 结论与证据边界

新增方向主要在**跨阶段对象生命周期、持久化表示、导出汇总、查询加载准入和字符串表的读写边界**。这些方向不同于已经讨论的计划宽度、跨过程边索引、批次结果保留、冻结图副本、DataFlow 邻接结构和 LocalCpgFragment 防御性复制；本文不把那些问题重新计数。

下表的顺序用于定位适用场景，不是实测热点排名。源码能证明分配点、强引用和控制流顺序，不能单独证明当前耗时占比、GC 次数或能够节省多少内存。

| 新方向 | 适用路径 | 当前源码可以证明的事实 | 优先评估的变化 |
| --- | --- | --- | --- |
| A. 字符串表的查询/写入分离与紧凑反向表 | 图的文件跨度查询；所有使用 StringInterner 的图 | 未命中文件路径也会 Intern；反向映射使用整数键字典 | 无副作用的查 ID；评估连续 ID 对应的数组/列表 |
| B. 紧凑表示贯穿持久化边界 | 分片导出与读取 | 冻结图在导出时展开为节点/边对象；读取再创建字符串丰富的 DTO | 数值/字符串表编码；直接有序写出；减少完整中间投影 |
| C. 加载前准入与实际驻留计费 | 分片关系查询 API | 先打开所有命中分片，再检查查询加载预算；缓存按文件长度计费 | 元数据阶段准入；限制在途反序列化；区分磁盘字节和驻留字节 |
| D. Workspace/Roslyn 工作集分层 | 工作区/解决方案分析 | 全部选中项目先形成快照；源码字符串、语法树与 Compilation 同时可达 | 延迟派生数据；项目级缓存生命周期；减少无用文本实体化 |
| E. 构建 IR 的方法级共享与阶段释放 | CPG 构建 | 每个 operation 建一个含完整节点的引用对象；库存参与构建末尾审计 | 方法头共享、紧凑 operation 行；最后消费者之后释放 |
| F. 导出负载与全局汇总解耦 | Project JSON 导出 | 写完文件后仍保留汇总字典；恢复模式也有项目级汇总副本 | 明确唯一汇总所有者；紧凑边键；必要时外部汇总 |

若目标是默认图构建，先评估 E；启用持久化再评估 B。若目标是大解决方案或 JSON 导出，D/F 更直接。C 需要实际使用分片查询入口才有收益；A 的控制流问题确定，但体量取决于查询量和独特字符串数。

## 2. A：字符串表不应因只读查询未命中而增长

**源码事实。** [NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs) 的文件跨度查询在约第 527 行调用 `_stringInterner.Intern(filePath)`，之后才查询文件索引。不存在于图中的路径也先进入字符串表。不同未命中路径会一直占据该 interner 的映射，直到其生命周期结束。

[StringInterner.cs](../../src/NLCPG/Model/StringInterner.cs) 是实例类型，包含 `Dictionary<string, uint>` 和 `Dictionary<uint, string>`。ID 从 1 递增，0 表示空引用；当前没有非插入式的文本到 ID 查询接口。两张字典并不等于复制了两份字符缓冲，主要额外成本是桶、条目、容量和不必要的新文本存活。

**设计方向。** 增加与 Intern 分开的只读 ID 查询，使未命中直接返回空结果。利用连续 ID，评估将反向字典改成索引表，并区分构建期写入视图与查询视图。这里提出的是设计候选，不表示接口已经存在。

**边界。** interner 可以被注入并由多个图共享，不能在任意子图冻结时冻结整个 interner，也不能无条件删除仍被文本查询使用的正向映射。保留并发语义、ID 稳定性和 null/空字符串区别。

**最小验证建议。** 对同一图查询一小批不同的不存在路径，记录字符串表条目数是否增长；再覆盖正常命中和共享 interner 并发访问。本文未执行这些验证。

## 3. B：减少持久化边界上的对象与文本重新展开

**源码事实。** [CpgFrozenShardExporter.cs](../../src/NLCPG/Persistence/CpgFrozenShardExporter.cs) 的 `Prepare`（约 98–114 行）先对节点排序并形成数组/索引，再对每个节点的出边执行 `ToArray()`。`Export` 接着为节点和边构造持久化 DTO。[CpgShardContracts.cs](../../src/NLCPG/Persistence/CpgShardContracts.cs) 中 `CpgFrozenNode`、`CpgFrozenEdge` 是包含多个字符串字段的 `sealed record`。

[CpgShardStore.cs](../../src/NLCPG/Persistence/CpgShardStore.cs) 已使用 FileStream、增量哈希和 BinaryWriter；问题不是缺少流式字节写出。其反序列化约第 320–328 行逐项 `new CpgFrozenNode` / `new CpgFrozenEdge`，字段通过读取字符串恢复，当前格式没有用于这些字段的分片级字符串表。

这形成了一个独立于图 Freeze 的成本层：紧凑图视图 → 完整导出投影 → 字符串丰富的持久化对象 → 读取后的对象图。Kind、文件路径等重复字段值得单独计量，不能将全部 System.String 分配归因于这里。

**设计方向。** 先评估从有序图视图直接编码，减少完整中间数组；再评估持久化内存模型和格式中的数值枚举、共享字符串表与紧凑行。需要同时覆盖读端，否则写端节省之后，查询端仍会再次展开。

**路径限制。** [CpgShardBuildCoordinator.cs](../../src/NLCPG/Builder/CpgShardBuildCoordinator.cs) 的常规构建后持久化路径调用 `Prepare`；[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs) 在流式 publisher 已完成时可绕过该路径。因此不能把该投影成本套给所有流式运行。真正流式路径中的 [SkeletonShardPublisher.cs](../../src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs) 仍有 `SnapshotMutableFacts` 和候选实体化，需要按其执行阶段分别计量。

**验证与约束。** 用一个小分片比较分配量、解码后驻留量、文件大小与读写耗时，分别测写端和读端。格式变更必须保留版本兼容、哈希、稳定顺序和原子发布；不要机械地把字符串丰富的 class 改成大 struct。

## 4. C：查询预算需要发生在加载之前，并覆盖驻留与在途对象

**源码事实。** [CpgShardQueryResolver.cs](../../src/NLCPG/Analysis/CpgShardQueryResolver.cs) 的 `FindResolvedByNodeAsync`（45 行）先经 `OpenLocationsAsync` 打开所有命中位置，再返回对应的分片列表。[CpgShardRelationQueryService.cs](../../src/NLCPG/Analysis/CpgShardRelationQueryService.cs) 先调用该方法（62 行），随后才检查 `MaxLoadedShardBytes`（72 行）。

因此，这个预算可以限制后续接受的查询内容，却不能据此断言未接受的分片没有发生 I/O 或反序列化。Resolver 的单次打开循环是顺序的，但多个调用可同时加载不同分片；现有 `_inflightLoads` 合并相同 ID 的加载，并不形成跨 ID 的总字节准入。

同一 resolver 的 `_cache.Set(location.ShardId, shard, location.ByteLength)`（98 行）把**文件长度**传给自定义 LRU 作为权重。实际驻留还包括节点/边 DTO、字符串和辅助索引。[CpgFrozenShardGraphReader.cs](../../src/NLCPG/Persistence/CpgFrozenShardGraphReader.cs) 使用的按分片辅助索引也不是该文件长度的等价物。调用者仍持有返回列表时，缓存淘汰不会自动终止那些引用；这不等于 LRU 淘汰实现有错。

**设计方向。** 将位置查询和分片打开分开，在目录元数据阶段决定准入，再按受限窗口读取。分别定义磁盘读取预算、解码在途预算和驻留缓存预算；对调用者持有的结果定义明确生命周期。若用估算驻留权重，需要以代表性分片校准，不能把估算宣称为精确堆上限。

**适用边界。** 这是分片查询 API 路径；本次没有证据证明它构成默认 CLI 构建的主要内存来源。[NLCPGSliceQuery.cs](../../src/NLCPG/Analysis/NLCPGSliceQuery.cs) 相关分片查询也是先得到完整 shard，再选择投影，投影条数限制不自动减少解码量。

**最小验证建议。** 单节点命中几个分片、预算只允许一部分，检查实际 store 读取次数；另以少量不同 ID 的并发查询检查在途峰值。分别统计磁盘字节、缓存估算值、实际存活对象，而非只看缓存命中率。

## 5. D：分开解决方案绑定基线与活动项目的派生工作集

**源码事实。** [MsBuildWorkspaceInputLoader.cs](../../src/NLISSN.Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs) 先将选中项目加载成快照列表，再返回给分析服务。项目快照同时保留 Compilation 和文档；文档保留 SyntaxTree 与 Source 字符串。约第 556 行取得全文字符串，随后才执行该分支的生成文件排除判断。

[WorkspaceAnalysisService.cs](../../src/NLISSN/Hosting/WorkspaceAnalysisService.cs) 在 `_loader.LoadAsync` 完成后才逐项目分析（40、59 行），其快照列表在循环期间仍被引用。目标文档过滤也晚于输入加载。[ApplicationService.cs](../../src/NLISSN.Application/Analysis/ApplicationService.cs) 中 `_defaultCallFlowResolvers` 是以 Compilation 为键的强引用 ConcurrentDictionary（28、292 行）；未见淘汰，其保留期限取决于服务的实际生命周期。工作区入口中的复用至少跨当前项目循环，不能直接据此宣称跨运行泄漏。

**设计方向。** 将必须用于正确绑定的解决方案/Compilation 基线，与仅供当前项目处理的 Source 字符串、resolver 和分析派生结果分开管理。优先让过滤和必要性判断先于可避免的全文实体化，并使派生缓存按运行/项目结束释放。现有 [ExecutionRuntime.cs](../../src/NLISSN.Application/ExecutionRuntime.cs) 已有弱键缓存机制，应先评估复用与生命周期契约，避免另建一层永久缓存。

**边界。** SyntaxTree 和 Source 的引用并不证明字符内容必然复制；`SourceText.ToString()` 是否新分配取决于具体表示。不能为省内存丢弃跨项目绑定依赖，也不能改变输入指纹、生成代码策略或仅目标文档分析的语义。SemanticModel 应在活动分析中复用，同时避免在最后消费者之后被不必要地强引用。[S1] 支持这一取舍。

**最小验证建议。** 用少量项目、包含一个排除文档的工作区，分别在输入加载完成、单项目完成和运行结束观察 Compilation/Source/resolver 的存活根及分配；区分必须保留的绑定对象与可以释放的派生数据。

## 6. E：压缩构建 IR，并按阶段终止其所有权

**源码事实。** [NLCPGBuildContext.cs](../../src/NLCPG/Builder/NLCPGBuildContext.cs) 第 53 行为每个 operation 创建 `OperationInventoryEntry`；其定义（143–148 行）包含 `IOperation`、`MethodRoot`、`OwningMethod`、根标志和完整 `NLCPGNode`。同一方法的 MethodRoot/OwningMethod 反复出现，每项还有独立引用对象的成本。Context 同时维护多个特化 operation 集合。

[NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs) 冻结图后调用 `ReleaseTransientBuilderState`，但后续 `BuildInventoryAudit` 仍消费 context 中的库存。因此“builder 已释放暂态字段”不等于“所有构建 IR 已经不可达”。不能只在 Freeze 后直接 Clear 库存而忽略后续审计。

**设计方向。** 评估方法头共享加紧凑 operation 行：方法级信息存一次，行内保留必要 operation 身份与节点序号。明确每类库存的最后消费者；末尾审计如只需统计，评估提前形成小摘要后释放大库存。这里关注的是前端构建 IR 的表示与阶段生命周期，与跨过程计划对象压缩是不同层次。

**证据限制。** [已有 LOH/频繁分配报告](2026-09-24-loh-frequent-allocation-objects-research.md) 记录过该类型的大量实例和尺寸，可作为筛选线索；其历史运行不能在缺乏相同源码/DLL 标识时当作当前版本的量化收益。

**验证与约束。** 检查库存条数、方法头重复度、构建各阶段存活量，以及末尾审计/数据流结果。机械改为 struct 会让较大的内嵌节点在集合和传参间复制，不能事先定性为低风险收益。保留 operation 身份和现有语义输入缓存边界。

## 7. F：JSON 文件写出与跨文件汇总要有不同的保留策略

**源码事实。** [ProjectJsonExporter.cs](../../src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs) 当前公共入口调用 `ExportOrderedOracleAsync`（36 行），是顺序生产路径。非恢复模式在整个导出期间保留 `globalNodes/globalEdges`（54–55 行）；`BuildFileProjection` 先创建每文件的节点/边投影，再更新全局汇总。边去重键通过包含多字段的 `string.Join` 构造（657 行）。

`ResumeExistingOutput` 才创建 SQLite ResumeCatalog（208–213 行）。即使使用 catalog，每项目仍创建 `projectionNodes/projectionEdges`（237–242 行），新构建文件向其累积；写入 catalog（321 行）之后没有随文件释放这些字典。复用已有 payload 的分支与新构建分支不同，不能笼统声称所有恢复文件都同时产生这份内存汇总。

已有 `Utf8JsonWriter`（476 行）和原子文件写出解决了字节输出方式，无法消除此前已构造的 DTO 列表及后续仍持有的汇总表。微软的 writer 文档 [S4] 不能作为“本仓库需要从非流式改流式”的证据。

**设计方向。** 分开单文件 payload 和跨文件聚合的所有权；确认 catalog 成为汇总所有者后，避免继续保留重复项目级聚合。对大型非恢复导出评估复用外部汇总机制；对边键评估紧凑的类型化键，保留字段区分及碰撞后的完整相等比较，不能用短哈希无条件替代完整身份。

**最小验证建议。** 用几个包含重复跨文件符号/边的小文件，比较导出文件数增长时的汇总条数、键字符串分配及存活量，覆盖新建/恢复/部分失败路径。保持稳定 ID、输出契约、原子发布和恢复语义。

## 8. 官方检索依据

检索使用 Microsoft Learn 搜索接口与官方原文；下列页面本次均成功读取（HTTP 200）。网页说明机制，仓库调用链才决定是否适用。本项目使用自定义 LRU，不能把 ASP.NET MemoryCache 的具体实现直接套用。

| 编号 | 官方来源 | 本文使用的依据与边界 |
| --- | --- | --- |
| S1 | [Roslyn SemanticModel](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.semanticmodel?view=roslyn-dotnet-4.13.0) | 模型缓存局部符号和语义信息；重复查询复用更有效，但长持有可能阻止大量对象回收。支持有界生命周期，不支持每次查询重新建模型。 |
| S2 | [ASP.NET Core 内存缓存指南](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0) | 应由应用定义并限制缓存增长，不应把外部输入键无界加入缓存。用于预算/输入边界原则，非本项目 LRU 的实现证据。 |
| S3 | [ConditionalWeakTable](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0) | 表成员关系本身不会让键一直存活；不能把仓库已经使用的弱键表一概判为泄漏，还须追踪其他强引用。 |
| S4 | [Utf8JsonWriter 用法](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter) | UTF-8 低层写出器；已知属性名可预编码。它不负责释放上游投影/聚合容器，也不支持无界缓存任意业务字符串。 |
| S5 | [Memory<T> 使用准则](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines) | 区分 owner、consumer、lease；用于阶段间所有权设计。引入 Memory/池不会自动缩短引用对象生命周期。 |
| S6 | [实现 DisposeAsync](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync) | await using 支持确定性资源清理；托管内存回收仍由 GC 完成。不能把 Dispose 等同于立即释放所有堆内存。 |

调度器 G0-L 的运行所有权与退出清理已属于现有调度计划的独立检查项，不在本文重复计为新增方向，也不以“未找到 Dispose”直接认定永久泄漏。

## 9. 需要修正的旧报告解释

1. **StringInterner 不是进程级 static。** 当前代码是实例对象，可注入/共享；应根据实际图与服务引用确定生命周期。
2. **StableAnchor 并非冻结后即可删除。** 当前 exporter 仍读取稳定锚点字段，节点身份也依赖相关语义；若做冷热分离，需要保留行为。
3. **OperationInventoryEntry 改 struct 不是已证实的低风险收益。** 当前字段包含较大节点值，应一起评估共享、宽度和复制次数。
4. **流式输出不等于全链路有界内存。** 现有 writer/store 已流式写字节，工作集问题仍可能存在于上游实体化、下游保留或并发加载。
5. **旧采样与当前热点分开陈述。** 历史大小、分配量和峰值不能混用；在途估算、文件长度、对象存活量、GC committed 和进程 RSS 也不是同一个指标。

这些是本文对旧结论的校正，不表示本次已经重测或覆盖旧报告。

## 10. 复核快照与交付边界

审查时 HEAD 为 `925161d2003e626fe00000e799cee2f847025067`，但工作树包含并行进行的未提交修改，HEAD 不能唯一代表本次读到的源码。以下为关键文件在本次复核时的 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `src/NLCPG/Model/NLCPGGraph.cs` | `DF23812347D4074664FA8947F69747B7907E72B0AA57C6A34C01D5BF31C4A281` |
| `src/NLCPG/Persistence/CpgFrozenShardExporter.cs` | `E93EE1BAB1DE4A4452161DD7AAA5FAA91CCE59DDF35B49404FE7B3D0E748E276` |
| `src/NLCPG/Analysis/CpgShardQueryResolver.cs` | `595E98CE0FA526383C18D4BFD1D736669D0DED368D97617C863ED8FBDC581738` |
| `src/NLISSN.Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs` | `4577D93CCDF3143A6D9B166F40D2B3CC4E73D2C981C105CF0ECC0FD8F4DE9891` |
| `src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs` | `ED935A9724D9187B4EEA44696F6F6675DAD360235118BFC8E678D50C821956F1` |
| `src/NLCPG/Builder/NLCPGBuildContext.cs` | `E3EFAE04E58A0B72204BABC41A58B88B4E645D01A13D856F8424013EDD8E4687` |

本文交付源码定位、设计候选、适用路径与小规模验证建议。未运行上述建议验证，未测得新的字节收益或 GC 改善，也未执行任何产品优化计划。功能状态仍以 `Context/feature_list.json` 为准。
