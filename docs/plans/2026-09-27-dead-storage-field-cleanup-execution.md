# ③ 只写不读存储字段清理执行文档

日期：2026-09-27。性质：**已执行**（本轮改了产品代码与测试，并运行了定向测试）。

> ✅ **执行状态：已完成。** 5 个字段全部删除；NLCPG 与 ContractTests 均 **0 错误**编译通过；
> 定向核心子集 **78/78 通过**（含本项新增的 1 个结构守卫测试）。
> 详见 §6 执行结果。

**目标：** 删除 5 个**只写不读**的存储字段（其中 3 个是**无界增长集合**），
消除"写进去再也没人看"的常驻字节与维护噪声。

**权威状态：** [feature_list.json](../../Context/feature_list.json) 是唯一来源。本切片**尚未登记**。

> 📌 **行号基准**：`src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs`（429 行）；
> `src/NLCPG/Builder/CpgShardBuildSession.cs`（700 行）；
> `src/NLCPG/Persistence/Sqlite/CpgCatalogBatchWriter.cs`（197 行）。

> **与④的分工**：本文件**只**处理存储字段。死私有方法 `IsInside` 与
> `CrossShardEdgeCommitter` 的重复边界边构造见
> [④ 死私有成员与重复边界边构造清理](2026-09-27-dead-private-member-cleanup-execution.md)。
> 两者**分开合入**，以便红/绿可归因。

---

## 0. 结论摘要

| 字段 | 位置 | 最后处置 | 是否无界增长 | 判定 |
| --- | --- | --- | --- | --- |
| `SkeletonShardPublisher._publishedOrders` | `:22` | `:209` 唯一 `Add` | **是**（每 operation fragment 一项） | **删** |
| `SkeletonShardPublisher._publishedKinds` | `:23` | `:147`/`:227`/`:386` 三处 `Add` | **是**（每发布项一个字符串） | **删** |
| `CpgShardBuildSession._stagedLocations` | `:24` | `:367`/`:516` 两处 `Add` | **是**（每分片一个 `CpgShardLocation`） | **删** |
| `CpgShardBuildSession._storeLockWaitMilliseconds` | `:23` | `:60` 唯一赋值 | 否（8 B） | **删**（含构造参数） |
| `CpgCatalogBatchWriter._maxQueueDepth` | `:14` | `:30` 唯一赋值 | 否（4 B） | **删** |

检索口径：`src/`、`tests/`、`tools/` 下全部 `*.cs`（排除 `obj/`、`bin/`、`tests/Build/` 快照语料），
以**完整标识符精确匹配**逐一确认"无读者"。结果见 §1 各表。

> ⚠️ **重要限定**：删字段是**纯删除**，不改变任何对外行为，但**必须**用编译证明
> （删除后必须重新构建，让编译器证明无残留引用），而非仅凭静态检索。
> 本轮的静态检索是**必要不充分**证据。

---

## 1. 逐项证据

### 1.1 `_publishedOrders`（`SkeletonShardPublisher.cs:22`）

```
22: private readonly List<int> _publishedOrders = new();
209: _publishedOrders.Add(fragment.SourceOrder);
```

全仓库 `*.cs` 内**仅这两处**出现该标识符。⇒ **只有写入，没有任何读取**。

无界性：`:209` 位于 `PublishDescriptorsAsync`（`:213-228` 的邻近区域）的发布循环内，
每个 operation fragment 追加一项；发布器生命周期覆盖整个文件的全部 fragment，
**无 `Clear()`、无上限、无容量回收**。

**删除方案**：删除 `:22` 声明与 `:209` 的 `Add`。
`:209` 所在的 `fragment.SourceOrder` 若不再被使用，需一并确认该表达式无副作用
（读取记录属性，无副作用）。

> ⚠️ **必须确认 `:209` 是纯写入语句**：若 `fragment.SourceOrder` 的求值有副作用
> （如惰性属性/计数器），删除该语句会改变行为。`SourceOrder` 是记录属性 ⇒ 预期无副作用，
> **但实施时必须回读该类型定义确认**，而不是默认。

### 1.2 `_publishedKinds`（`SkeletonShardPublisher.cs:23`）

```
23: private readonly List<string> _publishedKinds = new();
147: _publishedKinds.Add("operation-fragment");
227: _publishedKinds.Add(kind);
386: _publishedKinds.Add("boundary-adjacency");
```

同样**仅这四处**。⇒ 只写不读。

无界性同上，且**每项都是 `string` 引用**（8 B 引用 + 指向的字符串对象）。
`:227` 追加的是传入的 `kind` 变量，`:147`/`:386` 是**字面量**（被驻留，不新增分配），
但 `List<string>` 的**引用槽数组**仍随发布项数增长。

**删除方案**：删除 `:23` 与三处 `Add`。`:227` 处的 `kind` 是否还有其他用途需在同一方法内确认。

### 1.3 `_stagedLocations`（`CpgShardBuildSession.cs:24`）

```
24: private readonly List<CpgShardLocation> _stagedLocations = new();
367: _stagedLocations.Add(request.Source.Location);
516: _stagedLocations.Add(nextPublication.Location);
```

仅这三处。⇒ 只写不读。

无界性：每发布一个分片追加一个 `CpgShardLocation`。`CpgShardLocation` 是**值类型**，
其字段宽度决定每次追加的字节（含 `CpgFileKey`/`CpgFragmentKey` 等内联字符串引用）。
**实施时按 `sizeof`/字段布局给出精确宽度，不得估算。**

**删除方案**：删除 `:24` 与两处 `Add`。

> **为什么这个最值得删**：会话（`CpgShardBuildSession`）**长驻整个构建**，
> 而该列表随分片数线性增长且永不读取——它是纯泄漏式的常驻增长。
> 对比：`_stagingRoot`（`:22`）在 `DisposeAsync`（`:377`）里被用于
> `Directory.Delete(_stagingRoot, recursive: true)`（`:397-399`），是**真读者**，必须保留。

### 1.4 `_storeLockWaitMilliseconds`（`CpgShardBuildSession.cs:23`）

```
23: private readonly long _storeLockWaitMilliseconds;
54: … long storeLockWaitMilliseconds)      // 构造参数
60: _storeLockWaitMilliseconds = storeLockWaitMilliseconds;
```

仅这三处（声明、参数、赋值）。⇒ 赋值后**永不被读**。

关键点：`BeginAsync`（`:154`）是**静态**方法，它读的是
`options.StoreLockWaitMilliseconds`（`:159`）**而不是**这个字段；
构造时传入的 `lockStopwatch.ElapsedMilliseconds`（`:173`）是**实测等待耗时**，
与"配置的等待上限"是**两个不同的量**。字段名却暗示它是配置值 ⇒ 具有**误导性**。

**删除方案**：删除 `:23` 字段、`:54` 构造参数、`:60` 赋值，并同步
`:167-173` 的构造调用（去掉最后一个实参）。

> ⚠️ **反射风险**：`CpgShardBuildSession` 的构造参数被测试经反射间接耦合——
> `CpgPersistenceStateTests.cs:175` 通过 `GetMethod("BeginAsync", Static|NonPublic)` 调用它。
> 该调用**走的是 `BeginAsync`**，不是构造函数，故删构造参数**预期不破坏它**；
> 但 `BeginAsync` 的签名与反射 `GetMethod` 的匹配方式（按名 + 无参类型过滤器）
> **必须在实施时核对**，见 §3.2。

### 1.5 `_maxQueueDepth`（`CpgCatalogBatchWriter.cs:14`）

```
12: private readonly int _maxRows;
13: private readonly int _maxBytes;
14: private readonly int _maxQueueDepth;
28: _maxRows = options.MaxCatalogBatchRows;
29: _maxBytes = options.MaxCatalogBatchBytes;
30: _maxQueueDepth = options.MaxPendingShardPublications;
32: _queue = Channel.CreateBounded<CpgCatalogPublication>(
33:   new BoundedChannelOptions(options.MaxPendingShardPublications) …
93: if (estimatedRows >= _maxRows || estimatedMetadataBytes >= _maxBytes)
```

⇒ `_maxRows`/`_maxBytes` **有读者**（`:93`），`_maxQueueDepth` **无读者**。
有界通道在 `:32-33` **独立地**重新读取了同一个 option，字段纯属多余。

**删除方案**：仅删 `:14` 与 `:30`。**不动** `:32-33`（通道仍需该值）。

---

## 2. 改动清单汇总

| 文件 | 删除内容 |
| --- | --- |
| `src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs` | `:22`、`:23` 声明；`:147`、`:209`、`:227`、`:386` 的 `Add` 调用 |
| `src/NLCPG/Builder/CpgShardBuildSession.cs` | `:23`、`:24` 声明；`:54` 参数；`:60` 赋值；`:367`、`:516` 的 `Add`；`:167-173` 构造调用的末位实参 |
| `src/NLCPG/Persistence/Sqlite/CpgCatalogBatchWriter.cs` | `:14` 声明、`:30` 赋值 |

**净变化**：约 −18 行，无新增。**不改任何算法、不改任何对外签名**
（`CpgShardBuildSession` 的**私有**构造签名变化不属对外契约）。

### 2.1 **不**要顺手做的改动

| 诱惑 | 阻止理由 |
| --- | --- |
| 删 `_publishedNodeIds`（`:21`）/ `_primaryLookupByNodeId`（`:24`） | **有读者**：`:86`、`:103`、`:244`、`:302`；`:245`、`:258-259` |
| 删 `_boundaryBatches`（`:25`）/ `_boundaryShardOrdinals`（`:26`） | **有读者**：`:286`、`:336-339`、`:353-359` |
| 删 `_stagingRoot`（`:22`） | **有读者**：`:344`、`:370`、`:397-399` 的清理逻辑 |
| 删 `_maxRows`/`_maxBytes`（`:12`/`:13`） | **有读者**：`:93` |
| 删 `SkeletonShardPublisher+PendingCandidateBuckets` / `+BoundaryBucket` 嵌套类型 | `NLCPGNodeIdContractTests.cs:185-186` 用名字断言它们是**非公开值类型**，删了会红 |

---

## 3. 验证门禁

### 3.1 既有 oracle（**必须全绿，不得修改期望值**）

本项删的是**只写不读**成员，正确性 oracle 的作用是**证明"没读"这个判断成立**——
即删除后行为**零变化**。

| 测试 | 位置 | 锁住什么 |
| --- | --- | --- |
| `BuildFromSource_StreamingPersistence_PublishedShardsRestoreBaseGraphAndBuilderRestoresFullGraph` | `CpgShardBuildCoordinatorTests.cs:754` | 发布→还原整图往返；覆盖 `SkeletonShardPublisher` + `CpgShardBuildSession` 全链路 |
| `BuildFromSource_StreamingPersistence_MatchesSerialSnapshotAtConfiguredDop` | `:1112` | 多 DOP 等价 |
| `BuildFromSource_StreamingPersistence_CatalogStagesOperationFragmentsInSourceOrderWhenWritesCompleteOutOfOrder` | `CpgShardBuildCoordinatorTests.cs:938` | 发布顺序（乱序完成） |
| `BuildFromSource_StreamingPersistence_StagedPrepublishRemainsInvisibleUntilCompletion` | `:502` | 完成前暂存不可见 |
| `BuildFromSource_StreamingPersistence_ReusesUnchangedOperationFragmentAfterLaterMethodEdit` | `:998` | 片段复用 |
| `BuildFromSource_PersistenceMetrics_ExposeNonStreamingRestoreBreakdown` | `:113` | 持久化计数指标字段**不变**（删除不得改变 `CreatePersistenceMetrics` 的产出） |
| `CpgShardBuildSession_DisposeAsync_CanBeCalledMoreThanOnce` | `CpgPersistenceStateTests.cs:175` | **反射**调用 `BeginAsync`（`Static|NonPublic`）——见 §3.2 |
| `BuildFromSource_Persistence_WorkerFailureInvalidatesSessionAndCleansStaging` | `CpgShardBuildCoordinatorTests.cs:226` | 失败路径暂存清理（依赖 `_stagingRoot`，**不**依赖被删字段） |
| `BuildFromSource_Persistence_ConcurrentSharedStoreWaitsAndBothBuildsComplete` | `:269` | `StoreLockWaitMilliseconds` 行为不变 |
| `InternalCpgDomainCarriers_AreNonPublicValueTypes` | `NLCPGNodeIdContractTests.cs:178-206` | `CpgFrozenShardGraphFacts` 等仍为非公开值类型 |

其余须按 [测试目录入口](../../tests/AGENTS.md) 走 `Run-TestTiers.ps1` 的相应层。

### 3.2 反射耦合核查（**本项特有，必须做**）

删除 `CpgShardBuildSession` 的构造参数前，**逐一核查**所有反射入口：

| 反射点 | 位置 | 影响 |
| --- | --- | --- |
| `GetMethod("BeginAsync", Static\|NonPublic)` | `CpgPersistenceStateTests.cs:175` | 走 `BeginAsync`，**不**走构造 ⇒ 预期无影响，但需确认 `GetMethod` 不用参数类型过滤 |
| `GetProperty("CheckpointObserver", Static\|NonPublic)` | `CpgShardBuildCoordinatorTests.cs:230,275,329,386,508,944` | `CheckpointObserver`（`CpgShardBuildSession.cs:85-89`）**不删** |
| `GetProperty("AfterReadForTesting", …)` | `CpgShardBuildCoordinatorTests.cs:1001` | 在 `CpgShardStore`，与本项无关 |
| `GetProperty("ExportCheckpointObserver", …)` | `:447` | 在 `CpgShardBuildCoordinator`，与本项无关 |

### 3.3 拟新增门禁（**新增**）

1. **`SkeletonShardPublisher_DoesNotRetainWriteOnlyPublicationLogs`**（新增，结构断言）
   —— 反射取 `SkeletonShardPublisher` 全部 `Instance|NonPublic` 字段，
   断言不存在字段名 `_publishedOrders` / `_publishedKinds`。
   这是**回归守卫**：防止将来又把"发布日志"加回来。
   （仿 `NLCPGNodeIdContractTests.cs:254-262` 的
   `NLCPGBuilder_DoesNotRetainGlobalOperationToNodeCaches` 写法——先例已存在。）
2. **`CpgShardBuildSession_DoesNotRetainStagedLocationLog`**（新增）
   —— 同上，断言无 `_stagedLocations`、无 `_storeLockWaitMilliseconds`。

> ⚠️ 结构断言必须**只锁被删的名字**，不要锁"字段总数"或"全部字段名单"——
> 后者会在任何无关新增字段时误红，属脆弱测试。

### 3.4 变异检验（必做）

本项的特殊性：删死代码**没有**"改坏就红"的自然变异。因此判别力检查改为反向：

1. 先删**一个**字段（建议 `_publishedKinds`）→ 构建必须**成功**（证明确无读者）。
2. 再**故意**在代码里加一处 `_publishedKinds.Count` 读取 → 构建**前**把该字段加回 →
   构建应仍成功；若此时**不加回**字段只留读取，构建必须**失败**。
   这一步证明"编译器确实是这里的权威判据"。
3. 全部删除后跑 §3.1 全套，必须与删除前**同一组结果**（含已知的既有失败，见 §5.1）。

### 3.5 命令

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardBuildCoordinatorTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgPersistenceStateTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~NLCPGNodeIdContractTests'
```

**编译警告必须为零新增**：删字段后若出现 `CS0414`（赋值未使用）等，说明漏删了对应赋值语句。

---

## 4. 收益口径（**均为算术推导，未实测**）

| 项 | 结论（【推导】） |
| --- | --- |
| `_publishedOrders` | `fragmentCount × 4 B` 元素 + `List<int>` 的 24 B 数组头与增长期翻倍冗余 |
| `_publishedKinds` | `publishedItemCount × 8 B` 引用 + 数组头；字面量字符串本身被驻留，不额外计入 |
| `_stagedLocations` | `shardCount × sizeof(CpgShardLocation)` + 数组头（**精确宽度实施时按字段布局给出**） |
| `_storeLockWaitMilliseconds` | 8 B/会话 |
| `_maxQueueDepth` | 4 B/写者 |

> ⚠️ **合格结论形式**："三个无界列表不再随构建规模增长"。**不得**给出端到端峰值数字。

---

## 5. 本文件不得越读的边界

1. 本轮**已改产品代码并运行定向测试**（见 §6）；"无读者"由**编译器 + 定向测试**支持，
   而**不是**仅靠静态检索。
2. §4 全部为**算术推导**，不含本轮实测；§4 的"元素数×宽度"未做任何分配采样。
3. **静态检索是必要不充分证据**：反射（`GetField`/`GetProperty` 按名字符串）、
   `dynamic`、源生成器均可能构成检索不到的读取。§3.2 已列出**已发现的**反射点；
   无法证明"不存在未知反射读取"。⇒ 已由**编译器构建 + 定向测试**兜底（§6.3/§6.4），
   但**全量**测试未跑。
4. 已知**既有**失败**不得**归因于本项（见 §5.1）。
5. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)；
   本文件不构成验收证据。
6. `Context/feature_list.json` 是 feature 状态唯一来源；本文件**不**修改任何 feature 状态。

### 5.1 需与既有失败区分

以下失败在**改动前**即存在，**不得**作为本项红/绿判据：

- `CpgInterproceduralEdgeOrderTests.…PreservesBridgeEmissionOrder`
  —— 在未改动的工作树上**字节级相同**地失败（`156:167` vs `167:178`）。
- `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
  —— 由工作树中**未跟踪**的 `src/NLISSN.Infrastructure/ProjectJson/` 引起。

⇒ 实施时**先记录基线**（改动前的失败集合），再与改动后比对，
只有**新增**失败才归因于本项。

---

## 6. 执行结果（本轮实测）

### 6.1 改动

| 文件 | 净变化 |
| --- | --- |
| `src/NLCPG/Builder/Streaming/SkeletonShardPublisher.cs` | −8 行 |
| `src/NLCPG/Builder/CpgShardBuildSession.cs` | −15 行 |
| `src/NLCPG/Persistence/Sqlite/CpgCatalogBatchWriter.cs` | −2 行 |
| `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs` | +46 行（结构守卫测试） |

### 6.2 计划外的额外删除

`CpgShardBuildSession.BeginAsync` 中的 `lockStopwatch`（原 `:153`/`:158`/`:170`）**一并删除**。
理由【源】：该 `Stopwatch` 的唯一用途是把 `ElapsedMilliseconds` 传给那个死字段；
字段删除后它成为无读者的纯开销（每次 `BeginAsync` 一次 `Stopwatch` 分配 + 两次系统调用）。
**这不是超范围**——它是同一个"只写不读"缺陷的一部分。

### 6.3 编译证据（**编译器是"无读者"的权威判据**）

```
build src/NLCPG/NLCPG.csproj                  → 0 警告, 0 错误
build tests/NLISSN.ContractTests              → 0 错误, 21 警告（全部为既有 xUnit 分析器规则，与本项无关）
```

**未出现 `CS0414`（字段已赋值但从未使用）**——确认赋值语句也都删净了。

### 6.4 测试证据（定向 10% 核心子集）

```
--filter CpgShardContractTests|NLCPGNodeIdContractTests|CpgPersistenceStateTests
  → 已通过! 失败 0, 通过 44, 总计 44

--filter CpgShardBuildCoordinatorTests
  → 已通过! 失败 0, 通过 34, 总计 34

合并 4 个过滤器（终态确认）
  → 已通过! 失败 0, 通过 78, 总计 78, 耗时 51 s
```

其中 `BuildFromSource_StreamingPersistence_PublishedShardsRestoreBaseGraphAndBuilderRestoresFullGraph`
与 `…MatchesSerialSnapshotAtConfiguredDop` 覆盖 `SkeletonShardPublisher` + `CpgShardBuildSession`
全链路——是本项最强的"行为零变化"证据。

### 6.5 变异检验（判别力已证）

新增结构守卫测试**不是**恒真断言，已实测其判别力：

| 变异 | 结果 |
| --- | --- |
| 把 `_maxQueueDepth` 重新加回 `CpgCatalogBatchWriter` | `StreamingWriters_DoNotRetainWriteOnlyPublicationLogs` **变红**（`Assert.DoesNotContain() Failure: Filter matched`）✅ |

随后已还原。⇒ 若将来有人把这类"发布日志"字段加回来，守卫会失败。

### 6.6 边界更新

- §5 第 1 条"本轮只写文档"**已过期**：本轮**已改代码并跑测试**。
- 本项**未**跑全量测试套件（按用户指示执行 ~10% 核心子集）；
  其余层级未验证，见 §6.7。
- `Context/feature_list.json` **未被修改**（未登记本切片）。

### 6.7 未验证边界

1. **未做任何内存采样**：§4 的字节数仍全部是**算术推导**，本轮**未**测量实际分配或峰值。
   ⇒ 收益**未经实测**，不得引用为已证结论。
2. **未跑全量测试**：仅 `CpgShardContractTests` / `NLCPGNodeIdContractTests` /
   `CpgPersistenceStateTests` / `CpgShardBuildCoordinatorTests` 四个类共 78 例。
   HostTests、UnitTests、PerformanceTests **未运行**。
3. **未验证** `CpgShardBuildSession` 私有构造签名变更对未知反射读取的影响：
   §3.2 列出的已知反射点走的是 `BeginAsync`（未改签名）而非构造，**且编译与 78 例测试均通过**；
   但"不存在未知反射"仍不可证。
4. §5.1 的既有失败**本轮未观测**（未运行到那两个类）。

---

