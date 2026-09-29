# 统一工作调度内核执行计划

> **阅读边界：** 本次按 grilling 方法审查并修订报告，不启动下面的实施步骤或命令。用户要求不进行访谈，因此可查事实直接核对源码，方案选择以建议和验收门槛写入报告。后续实现以明确的实施范围为准，不因打开本文件自动执行。
> **状态来源：** [feature_list.json](../../Context/feature_list.json) 是完成状态和验收证据的唯一来源。已完成任务中的代码/命令仅作实施记录；不得把历史示例重新覆盖到当前源码，也不得重复执行其中的“先验证类型不存在”步骤。

**Goal:** 把四层并发设施（通用池、准入控制器、CPG 度预算、WorkBatch 执行器）
合并为单一 `WorkScheduler` 内核，并在其上实现小/中文件成批与大文件分片提交，
不改变任何 CPG 图语义、节点集、边序或持久化格式。

**Architecture:** 内核拥有 P 个长期 worker，`RunAsync` / `RunWithMetricsAsync` 共用同一提交实现；
`WorkItem` 统一承载顺序、依赖、优先级、成本与字节估算。目标是在目录协调层
按有界窗口组织扁平提交（提交深度恒为 1）；本次建议在阶段 barrier 之后规划下一份静态图，详见 S3-1c。静态规划、保留内存与生命周期必须先满足迁移门槛，不能默认物化整个目录。P 约束本 run 由该内核负责的分析工作并发，不等于 CLR 线程总数，也不自动控制 Roslyn 等依赖内部的并行。
分桶是小/中/大文件的外部划分结果，内核只看到数值。

**Tech Stack:** .NET 10、C# preview、`System.Threading.Channels`、Roslyn 4.14、
现有 `NLCPG` stable anchors / `LocalCpgFragment` / `CpgFragmentReducer`、
xUnit Contract/Performance 测试、`Build/Tools/Invoke-SerialDotnet.ps1`。

**设计依据：** [统一工作调度内核设计](2026-09-24-unified-work-scheduler-design.md)

---

## 工作规则（每一步都适用）

- 工作目录 `D:\ProjectItem\SourceCode\Net\NL`；**保留工作区中已有的无关改动**
  （当前有大量未提交文件，其中 `DataFlowPass.cs`、`NLCPGDataFlowSparseSetTests.cs`
  等可能有并发写入者——**本计划不触碰这两个文件**；若发现必须改，停下来回报）。
- 每个可交付步骤都必须可编译，目标正确性测试不得新增失败。已有失败须有同输入、同过滤器的基线证据并单列；任何被该步骤改变的语义守卫不得豁免。禁止用总体通过数掩盖失败/跳过，也不把不同阶段未验收的改动一次性标为完成。
- **后续构建/测试前**确认 TEMP/TMP 指向存在且可写的临时目录。历史运行曾在 harness 临时目录清理时报“拒绝访问”，这是需核验的环境条件，不是所有机器必然发生的错误。例如：

  ```powershell
  $taskTemp = Join-Path (Get-Location).Path 'Build/tmp'
  New-Item -ItemType Directory -Path $taskTemp -Force | Out-Null
  $env:TEMP = $taskTemp
  $env:TMP = $taskTemp
  ```

- **所有可编译命令走串行包装器**。注意不要把 `-p:` 参数内联进脚本命令行
  （会触发 `parameter name 'p' is ambiguous`）：

  ```powershell
  $dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
    '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
    '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
  & .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
  ```

- 生成物只落 `Build\`；不得在源码旁写基准产物。
- **每一档只报告实际执行过的命令与结果**；不得把未执行的档位记为通过。
- 测试命名遵循 `Method_Scenario_ExpectedResult`；测试夹具放
  `tests/NLISSN.Testing/`。
- 改前先备份哈希，便于回退核验；`Copy-Item` 还原后**必须刷新 mtime**
  （`(Get-Item $f).LastWriteTime = Get-Date`），否则 MSBuild 会跳过重建而跑旧 DLL。

### 不可违反的正确性前提（共 6 条）

1. **同一图只能有一个写入者**。`NLCPGGraph.AddNode`/`AddEdge` 不能被当作并发写接口；计算工作项产候选，由指定归并者按稳定顺序物化。归并者可在外层或作为显式串行工作项；不同文件的独立图可分别归并。
2. **跨片身份用 `StableNodeAnchor`**。分片内不得独立分配最终全局 NodeId；通过 `CpgFragmentReducer` 的有序归并建立最终图中的身份映射，不把某个历史代码行称为所有构建路径的唯一分配点。
3. **跨片引用必须显式化为 boundary reference**，保持 `LocalCpgFragment` 的校验与归并协议。
4. **最终图在所需归并完成后统一冻结**。保持 `NLCPGBuilder` 既有 `FreezeQueryIndex()` 生命周期；局部图的合法冻结与最终图分开判断，不提前冻结未完整的共享图。
5. **interprocedural pass 等待它依赖的全部批/片完成归并**。保留 `_interproceduralBarrierCompleted` 语义；S3 沿用既有方法分批，S5 才验证外部分片下的屏障。
6. **遥测 fail-open**：保持 `RuntimeMeasurementLog`、`CpgWorkBatchPerformanceEventSinkExtensions` 与内核 sink 的边界；遥测失败不得改变调度、成功或取消语义。

---

## 阶段总览

| 阶段 | 目标 | 行为与验收边界 |
| --- | --- | --- |
| **S1** | 已实现内核原语及契约 | 不代表生产内存与生命周期已经闭环；见 S1 与 G0 |
| **S2** | 规则 DAG、replay、ResolveUnits 部分迁移 | 目录与 ForEachScan 继续走现有路径；G2a 是过渡门，G2 才关闭迁移 |
| **S3** | 分离 pass 计划、计算、归并；消除深层提交；在有界窗口协调跨文件工作 | 必须先取得 G4a；显式处理 group/helper 并行配置变化，不跳过 DataFlow 后声称 8/8 |
| **S4** | 删除已无生产调用的旧层、统一日志消费 | S4-3 的 G4a 前移到 S3 之前；其余删除在 G2/G3 之后 |
| **S5** | 新增外部分片计划与 adapter，再引入失败隔离 | G4b 验证真实新路径后才能启用；小/中/大阈值仍为待测候选 |

| 门槛 | 通过条件 | 本次判定 |
| --- | --- | --- |
| **G1** | 当前内核已覆盖契约及嵌套/关闭回归 | 有历史验收；不扩大到尚未接线的生产保证 |
| **G2a** | 已迁移调用点分别验收；未迁移调用点仍用受守卫保护的既有路径 | 有局部历史证据；不等于完整 S2 通过 |
| **G0** | 下述 G0-M/L/N/P 均有具体设计、候选入口接线与对应验证；可在 S3 中分步补齐，切换默认目录路径前须全部通过 | 本次只补设计与门槛，未实现/验证 |
| **G3** | 8 个 pass、规则/决策/提升输出及边序等价；真实并发、barrier、内存与取消验证成立 | 待实施；单有 DOP 参数变化不够 |
| **G2** | G0、G3 成立，目录与 ForEachScan 迁移验收完成，所有活跃旧调用均有明确处理 | 尚未关闭；不能由“规则内联”直接推导 |
| **G4a** | 现有方法分批、并行及 streaming 路径的同版本等价证据归档；沿用用户先做 G4、再做 S3 的顺序 | feature 仍 blocked；新交接证据需核对，详见 S4-3 |
| **G4b** | 新 DocumentShardPlanner → adapter → NLCPG 的实际路径等价，包含边序、载荷、持久化与规则结果 | 尚无实现或验证；现有 G4a 测试不能替代 |

**修订后的依赖链：** G1 + G2a → S4-3/G4a（先核验既有路径，不修改 `src/NLCPG/**`）→ S3-1/1b（同时补齐 G0 设计和接线）→ S3-1c/3-3（窗口协调与 G3）→ S2-3/2-4/G2 → S3-2/S4 删除旧层 → S5-1/2 的隔离实现与 G4b → 启用外部分片和 S5-3 隔离行为。

这里保留用户已选择的路线 A：目录迁移等待扁平化，不把完整 AnalyzeFile 再包进内核，也不改走未选择的路线 B。**G4b 是新路径的启用门，而非禁止编写该路径的循环前置**；G4a 保留在 S3 前。现有 feature 的 G4 状态仍由 feature_list.json 管理，本报告拆门不自动修改它。

### Grilling 审查：逐个封闭隐含前提

以下由源码和状态材料自行回答。标为“建议”的是本次修订后的设计，不是已经实现或已测出的收益；本次不要求用户逐项答复，也不执行它们。

| 追问 | 已核实的回答 | 修订决策 / 验收门 |
| --- | --- | --- |
| 任务结束、归还 EstimatedBytes，就能释放 fragment 吗？ | 不能。内部 `_results`、依赖结果、闭包和最终输出仍可能持有资源 | **G0-M**：分别约束执行中额度与结果存活期，按最后消费者/有序提交释放 |
| 单个 submission 有预算，就能限制整个 run 吗？ | 不能。并发 submission 分别记账，单超项也是 submission 局部放行 | 在 run owner 明确总额度分配、窗口并存数和超大项策略；禁止把单次预算当全局硬上限 |
| 谁关闭 AnalysisRuntime 自建的长期 worker？ | 派生 runtime 共享内核已实现；目前查到明确自有释放的是 RuleGraphExecutor fallback，生产 run owner 尚未证明 | **G0-L**：自建/借用/派生所有权与成功、失败、取消的关闭测试必须补齐；不直接宣称已泄漏 |
| 把 RuleGraphExecutor 内联，深层提交就消失了吗？ | 不会。ResolveUnits 将落入 rules(F)，ForEachScan 仍在规则体内；Builder 还有独立 Parallel.For | **G0-N**：盘点真实调用闭包；建议 worker 内 helper 直连，独立入口保留外层调度 adapter；逐处验证 |
| 一次能构建从 CPG 到规则的完整静态 DAG 吗？ | 当前内核只支持提交前静态图；后续 pass 计划可能依赖前一阶段产物，报告没有证明可提前冻结 | **G0-P**：先画实际阶段依赖；建议由 worker 外的窗口协调器在 barrier 后生成下一份静态提交，见 S3-1c |
| 一张混合 DAG 可以同时套用六类配额吗？ | 不可以。Category、MaxConcurrency 都属于 submission，且结果只有一个 TResult | 分阶段同类型提交优先；若坚持整流水线单 DAG，必须先另行设计配额与类型协议，不能使用 `WorkSubmission<...>` 占位实现 |
| “分片等价测试通过”能证明新 adapter 吗？ | 当前测试改变的是已有方法分批与 streaming 配置，没有调用待建的 DocumentShardPlanner | 拆成 G4a 既有路径基线和 G4b 真实新路径；保留边序敏感性与实际分片的非空断言 |
| try/catch 每个文件就能安全继续改写吗？ | 不能。取消、内核错误和跨文件缺失事实不可视为普通局部失败 | S5-3 使用明确结果状态、后继阻断和 Unknown/fail-closed；不发布半图或授权未完成的改写 |
| 删了旧类就证明唯一 P 与唯一遥测了吗？ | 不能。Builder 独立并行路径及旧日志消费者仍存在 | 递归检索加运行期并发证据；每个旧指标注明来源及可用性，缺失不填 0 |
| 31.8/29.8 ms 或规则累计 1.5% 能证明收益/退化上限吗？ | 前者落在噪声内，后者不是 wall-time 关键路径占比 | 不据此宣称加速或固定退化上限；后续仅按授权范围做可复现对照 |

### G0：进入目录替换前必须补齐的设计与验证

**G0-M：存活内存与背压。** 为一个窗口列出 source/语法树、共享 compilation、上下文闭包、items/依赖数组、各阶段 fragment、图、内核结果、排序输出和 List 保留容量的 owner、首个分配点、最后消费者与释放点。共享 compilation 是单独的基线，不承诺通过窗口释放。设计采用以下两级约束：

- 内核 `MaxInFlightBytes` 仅管执行中工作；窗口 owner 管保留中的产物。二者属于不同生命周期，不能简单相加或用同一计数相互抵消。
- **建议**有界微批完成后按稳定顺序归并并释放；高成本产物留在窗口资源表，调度结果只留轻量、明确类型的完成记录。所有权、引用清理和后继读写协议需测试，这不是现有 API 的保证。
- 窗口不能先复制整个目录再切片；记录实际 Count/Capacity 与队列/待归并数量。反例用“第一个批很慢、后续批很快”验证 retained backlog 上界，并验证连续窗口不会阶梯式增长。
- 估算须来自相同夹具的实测校准；源码字符数、行数乘常量不是可靠 bytes 上界。先声明可用内存和安全余量、估算误差与停止线，再逐档运行；不能等越过物理内存后才停。单项超过窗口预算时定义受控独占/拒绝/回退，不能把当前局部放行当安全证明。

**G0-L：run 生命周期。** 明确一个 run 的唯一 scheduler owner；自建、注入借用、InvalidateCaches/NextEpoch 派生实例的责任分别列出。完成、首次异常、外部取消均等待已接受工作结束并释放自有内核；借用方不 Dispose。验收包含关闭竞争、重复 run 和取消期间无挂起；Dispose 的现有语义是排空，不是取消已接受队列。

**G0-N：提交深度与实际并行边界。** 以 `rules(F)`/CPG 工作项为根逐条追到 RuleGraphExecutor、ResolveUnits、ForEachScan、pass helper 和 Builder 的独立并行调用；记录每处为直连、外层提交或尚未迁移。禁止 worker 内创建第二个 scheduler、Task.Run 包装或同步等待来绕开同实例嵌套守卫。原有 Parallel.For 也须证明不再形成 P×DOP，才可声称统一 P。

**G0-P：计划、类型、配置与分层。** 每个实际阶段说明计划依赖什么输入、何时冻结、TResult、Category、MaxConcurrency、byte 估算、barrier、归并 owner。建议先完成“一个 run owner + 分阶段窗口静态 DAG”；这是对原“整条流水线必须一次提交”的收窄，不是已有实现。将六个 YAML 字段与三个并行开关映射到具体提交/窗口，说明 overrides；NLCPG 只接受下层中立契约，不能反向依赖 Application 的计划类型。

---

# S1：内核独立实现

**状态以 feature_list.json 的 unified-work-scheduler-s1-kernel 为准：内核契约有历史验收，生产内存控制和整个 run 的生命周期不因此完成。** 本节移除已经过期的“先编译失败再实现”模板，保留实际契约、证据范围和仍需补齐的门槛；本次没有重新运行测试。

源码入口：[WorkScheduler](../../src/NLISSN.Infrastructure/Concurrency/WorkScheduler.cs)、[WorkItem](../../src/NLISSN.Infrastructure/Concurrency/WorkItem.cs)、[WorkSubmission](../../src/NLISSN.Infrastructure/Concurrency/WorkSubmission.cs)、[WorkSchedulerOptions](../../src/NLISSN.Infrastructure/Concurrency/WorkSchedulerOptions.cs)。

## Task S1-1：内核数据类型（已实现）

- ExecuteAsync 的真实签名接收“直接前置结果的 IReadOnlyDictionary<long, TResult> + CancellationToken”，返回 Task<TResult>。只有一个 token 参数的旧示例已失效。
- 一个 WorkSubmission 只接受一种 TResult；CPG fragment、图、规则结果不能直接塞进未定义的 WorkSubmission<...>。
- StableOrder 在一次提交内必须唯一，依赖必须存在；当前 Create 会在入队前检查重复键、缺失依赖及环。不同窗口可有自己的局部顺序，跨窗口归并另保留原始文件身份。
- 契约入口：WorkItemContractTests、WorkSchedulerDependencyTests。测试存在与历史通过不等于新阶段表示已经可用。

## Task S1-2：调度选项与 P 的推导（已实现，须明确作用域）

- P 来自 WorkSchedulerOptions.WorkerCount；工作项共享这个实例的 P 个 worker。
- Category 位于 submission，未显式覆盖时用于 ResolveLimit；MaxConcurrency 是该 submission 的上限。当前 TakeReady 检查 submission.ActiveCount，**不是同类别所有 submission 的合计上限**。
- 显式 MaxConcurrency 会覆盖类别默认解析；调用方必须先遵守并行开关，不能只看启动日志的默认上限就声称实际提交被限制。
- 混合 CPG/rules 的单个提交不会自动分别应用六类配额。进入 S3 前须给出窗口文件数、每次提交上限和阶段内并行的对应表；不能借迁移无声改变配置含义。

## Task S1-3：内核执行循环（已实现）

- worker 惰性启动；实际调度在共享状态门内选择就绪项，优先级/老化、EstimatedCost、StableOrder 等共同决定领取顺序。
- PreserveOrder=false **仍分配 _results**，因为后继需要前置结果；只是 CollectResults 返回空列表。不得把这个开关当作“不保留产物”。
- PreserveOrder=true 的结算还会生成有序输出数组；评估内存时计入与内部结果数组共存的时刻。
- 历史 R3 微基准为直接 31.8 ms / 包装 29.8 ms（5 轮最优、单轮离散约 10%）；仅说明该样本下开销落在噪声内，不能声称加速、≤1% 或真实目录吞吐收益。原临时探针已删除，不作为当前可复现验收命令。

## Task S1-4：依赖调度（已实现）

Create 构建并校验静态依赖图，完成生产者后释放后继。RunAsync 入队后没有动态追加工作项的接口；S3 若不能在提交前知道任务与依赖，须先解决计划生成边界，不能在 worker 内再次提交来补图。

保序结果只保证最终收集次序，不保证并行副作用的顺序。任何图物化仍由该图唯一的串行归并者执行；不同文件的独立图可以各自有一个归并者。

## Task S1-5：嵌套回归测试（已有守卫，覆盖范围须扩展）

当前同一异步流内向同一 scheduler 再次 RunAsync，会立即抛出包含 flat 的 InvalidOperationException。不得移除这个守卫或在内层 new WorkScheduler 来绕过共享 P。

现有 WorkSchedulerNestingTests 与 DirectorySchedulerBoundaryTests 保留。未来 S3 还须从真实 rules(F) 覆盖 ResolveUnits、ForEachScan 和所有 pass helper，证明“图执行器内联”没有留下深层提交。

## Task S1-6：取消、失败与 Dispose（内核原语已实现，生产 owner 待核验）

- 当前首错使同一 submission 取消并排空已启动项，不取消其他 submission。
- Dispose 标记停止接受新的非空提交，但继续服务已接受提交中的待执行项；不是立即停止领取它们。移除完成提交后必须 Pulse，否则 worker 无法退出。这一历史挂起已修复并有记录。
- 生产 owner 与内核自身 Dispose 测试是两件事。静态检索只发现 RuleGraphExecutor 为其自建 fallback scheduler 显式调用 DisposeAsync；AnalysisRuntime 自建/派生/注入实例的 run 结束释放接线仍须确认，见 G0-L。
- 首次实例、InvalidateCaches/NextEpoch 派生实例和注入实例必须共享明确的所有权：自建者负责释放，借用者不得提前释放，异常和取消也要排空。保留 RunAsync/Dispose 竞争回归。

## Task S1-7：字节背压 M1（执行中额度原语已实现，存活内存闭环未完成）

当前 IsBytesAllowed 检查**单个 submission 的执行中 EstimatedBytes**；FinishItem 在工作项结束时立即归还额度。但 _results、后继输入和工作委托捕获的资源可能仍存活，**任务完成不等于产物释放**。

- MaxInFlightBytes<=0 在当前实现表示不限；EstimatedBytes=0 不受此门限制。
- 单超项只有在该 submission.InFlightBytes==0 时放行；这不等于所有 submission 的全局独占，也不证明物理内存装得下。
- 多个同时提交各自拥有额度，不能把一个 submission 的预算称为 run 全局预算。
- 启用生产限制前补齐负估算、非法预算、long 求和溢出、零估算及单超项契约；负数不能被用来抵消已占用字节。该补齐尚未实现，不改变已有历史测试状态。
- WorkSchedulerByteBudgetTests 只能证明其覆盖的内核准入行为；不能证明窗口、fragment、图或整个进程的峰值受控。生产接线与存活内存门槛见 G0-M。

## Task S1-8：统一遥测（内核记录已有实现，日志消费未统一）

WorkTelemetry 包含 Category、InputCount、MaxConcurrency、PeakActiveCount、PeakReadyCount、PeakInFlightBytes、MaxQueueWait、Elapsed、WasCanceled、可空 RunId 共 10 个字段。字段形状以 [WorkTelemetry.cs](../../src/NLISSN.Infrastructure/Concurrency/WorkTelemetry.cs) 为准。

PeakInFlightBytes 只表示当前执行中估算，不是 managed heap、retained bytes 或 RSS。当前 collector 已接入自建内核，但 RuntimeMeasurementLog 仍读取旧池操作记录；两套数据不能相加冒充唯一来源。结果保留和归并等待仍是需要观测的真实成本，不能因删除旧 executor 就断言这些概念消失。

## Task S1-9：Gate G1 验证（历史证据，不自动覆盖后续改动）

历史证据为 ~Concurrency 39/39，后补 aging 与变异检查后为 49/49；后续 feature 记录另有新增测试。引用时必须携带对应版本、过滤器与原始结果，不能把不同轮次通过数拼成同一次验收。

G1 表示已实现内核契约的历史验收；进入 S3 需要 G2a、当前路径的 G4a 证据及 G0 设计。候选路径可在 S3 内补齐 G0 的接线与验证，通过后才切换默认目录入口。任何相关源码变化后，受影响的最小测试需重新验证；本次文档审查没有运行这些测试。

---

# S2：调用方迁移

**当前边界：** S2-1、S2-2 与 S2-4 的两处调用已迁移；S2 feature 仍为 in_progress。目录和 ForEachScan 是结构性阻塞，不是遗漏的简单替换。以下描述现状和剩余步骤，不重复执行历史迁移。

## Task S2-1：接线 Scheduler 到 AnalysisRuntime（已有实现）

入口：[ExecutionRuntime.cs](../../src/NLISSN.Application/ExecutionRuntime.cs)、[AnalysisRuntimeFactory.cs](../../src/NLISSN.Application/AnalysisRuntimeFactory.cs)。AnalysisRuntime 暴露 Scheduler，按执行选项构造；注入实例与派生 runtime 共享关系已有契约。旧 ConcurrencyPool/CpgBuildAdmissionBudget 仍服务未迁移路径，不能删除。

新增的生产关闭责任见 G0-L；“共享同一实例”只避免重复创建，不能代替整个 run 的释放接线。

## Task S2-2：规则 DAG 迁移（四个生产构造点已有实现）

入口：[RuleGraphExecutor.cs](../../src/NLISSN.Rule/RuleGraphExecutor.cs)；构造点位于 RuleGraphAnalysisExecutor、MarkingEngine、DecisionModel、MarkLiftingEngine。

- 当前通过 `RunWithMetricsAsync` 获得结果与 PeakReadyCount/PeakActiveCount/Elapsed；普通 `RunAsync` 是薄包装，不能在文档示例中丢失原有规则指标。
- `BuildInputs(node, dependencyResults, graph.NodeIndexes)` 是真实三参调用；前置结果以 long StableOrder 为键。删除过期的两参、单 token 委托模板。
- StableOrder 来自 NodeIndexes；规则项 EstimatedCost 当前为 0。就绪次序仍须由依赖、优先级及稳定索引共同解释，不能把按索引排序当作拓扑证明。
- 未注入时 executor 创建 fallback scheduler，并在 finally 中释放。这是独立入口的现状，不能在未来内核 worker 内借此再起内核。
- 顺序敏感验证覆盖规则、决策、标记、提升与最终输出。S3 内联会改变执行实现，必须重新验收，不能继承 S2 的结论。

核对构造点使用递归检索，匹配项需区分生产调用和 fallback，而非只断言固定行数：

```powershell
rg -n -F 'new RuleGraphExecutor(' src --glob '*.cs'
```

## S2 历史证据与适用边界（2026-09-24）

| 证据 | 当时结果 | 可得结论 / 限制 |
| --- | --- | --- |
| 同机 HEAD 925161d HostTests 全量对照 | 基线 8 失败 / 652 通过；迁移树 7 失败 / 666 通过 | 当时迁移树失败均在基线存在；不能当作当前工作区全绿，也不能只用数量证明逐条输出等价 |
| 早期 `~Concurrency` | 46/46 | S1 39 条与 S2 runtime 7 条；后续轮次不可拼成同一次运行 |
| 早期 Fast | Unit 117/117；Contract 489/490 | 1 条 LayoutArchitectureTests 项目引用差异；既有失败不等于已修复 |
| RuleGraph、RuleStructure、PipelineComponent 联合过滤 | 163/166 | 3 条失败与当时基线相同；不是完整正确性通过 |
| S2-4 replay / decision 定向过滤 | 12/12、7/7 | 两处迁移的局部证据，不含目录和 ForEachScan |
| 后续规则/目录等回归过滤 | 274/278 | 4 条失败同样有基线；其中 LogicalAnd 用 ResolveUnits A/B 回退排除关联 |
| DirectorySchedulerBoundaryTests | 2/2 | 证实目录文件体会提交 RuleGroup，外包整目录到 work item 会命中 flat 守卫 |

实施时修改了两种旧池断言，改为检查内核的提交次数与 MaxConcurrency；ResolveUnits 迁移后相关兼容测试从 3 条改为 4 条 rule-group 记录，每条仍要求上限 1。不能继续引用旧的 3 条作为当前断言。

当时 NLCPGGraphIndex 与 FrozenEdgeProjectionEquivalenceTests 曾受并发编辑影响而编译失败，后续恢复；这些仅属那次环境记录，不能写成当前仍失败或保证当前可复现。原始版本、过滤器、失败身份与命令见 [feature 状态](../../Context/feature_list.json)。本次没有重跑，亦未修改这些源码或测试。

## Task S2-3：目录分析迁移（阻塞，沿用路线 A）

当前 [DirectoryAnalysisUseCase.cs](../../src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs) 仍走 ConcurrencyPool.SelectOrderedAsync 与 CpgBuildAdmissionBudget。既有链路：

```text
目录文件体 → ApplicationService.RunAnalysis → RuleGraphAnalysisExecutor
          → RuleGraphExecutor → 同一 Scheduler.RunWithMetricsAsync
```

因此不能把完整 AnalyzeFile 再封装为 WorkItem；DirectorySchedulerBoundaryTests 已固定此反例。旧报告的可复制代码已删除：它不仅形成嵌套，还用了错误委托签名、未核实配置字段，并把 Source.Length 当作保守内存估算。

**解锁条件：** 先满足 G4a，再由 S3 拆开阶段/提交并解决 G0-N 的全部深层调用；G0-M/L/P 与 G3 验证通过后，目录协调器接入窗口调度。规则图单独内联不构成解锁。

**后续实施边界：**

1. 使用既有目录路径建立相同输入、配置、代码版本的基线，记录原始文件顺序、结果/诊断、heap/working set 与资源保留。
2. 先完成无嵌套的窗口协调与单文件正确性，再在已声明的内存停止线内按小档放大；同时断言跨文件确实重叠、本 run 受管分析工作实际并发不超过 P。不同文件并行不允许共享同一可变 graph writer。
3. 新的执行准入和资源存活控制接线、验证前，保留 lease 及旧预算。禁止只替换成 MaxInFlightBytes 就删预算；那是未选择的路线 B，且两种生命周期并不等价。
4. 通过后才删 ApplicationService 的 lease 分支、runtime 的相关字段与旧预算类型；删除范围由实际引用决定。

**回退路径：** 已验收的既有目录池 + CPG 执行路径。不得回退到本报告已否定的“整文件 WorkItem 外包”形态。

## Task S2-4：replay 与 helper（2/3 已迁移，另有未来嵌套风险）

| 调用点 | 现状 | S3 需要处理的边界 |
| --- | --- | --- |
| RewritePlanReplayService.ReplayAsync | 顶层 Replay 提交，已迁移 | 保留顶层 owner、顺序与取消；不得包进其他内核项后再次提交 |
| DecisionModel.ResolveUnits | 规则图返回后提交 RuleGroup，当前入口已迁移 | 若整个分析阶段进入 rules(F)，这里仍在 worker 内，必须改直连或外提阶段 |
| ParameterShrinkAnalyzer.ForEachScan | 规则体内部仍用旧池，阻塞 | 建议 worker 内按稳定次序直接计算，或由外层提前规划；禁止直接替换为 Scheduler.RunAsync |

**建议保留双入口边界而不复制算法：** 纯计算 helper 可直连；独立调用需要并行时由 worker 外 adapter 提交。适用 helperParallelism、groupParallelism 的实际串行化范围都应记录并告警；不把当前有效的并行选项提前写成已失效。

相关入口：[DecisionModel.cs](../../src/NLISSN.Core/Decision/DecisionModel.cs)、[ParameterShrinkAnalyzer.cs](../../src/NLISSN.Rules/Propose/ParameterShrink/ParameterShrinkAnalyzer.cs)、[RewritePlanReplayService.cs](../../src/NLISSN/Artifacts/RewritePlanReplayService.cs)。

## Task S2-5：过渡门 G2a 与最终门 G2

- **G2a** 接受可定位的局部迁移证据，保留目录、helper 的活跃旧实现及嵌套守卫；允许在 G4a 之后进入 S3 的结构调整，不强迫先完成不可能的 S2-3。
- **G2** 要求目录和 ForEachScan 的真实路径完成迁移、ResolveUnits 深层风险解除、G0/G3 验证成立。其后才能删除相应旧层和测试替身。
- 不再用“4 个调用方全部完成”掩盖每类下的多处调用。报告按具体生产调用、行为测试、未迁移项逐项关闭；未跑的档位明确保留。

---

# S3：CPG pass 解耦与窗口协调（尚未实施）

**入口：G1、G2a 与 G4a。** 先搭建隔离候选入口并验收，再由 S2-3 切换默认目录路径。本阶段交付的是可验证的提交边界、内存所有权和配置映射，不是机械地把 executor 名称换成 scheduler。当前内核拒绝嵌套且只接收静态图；旧报告中的单 token 委托、固定 workerId=0 和等待全量结果再归并模板均删除。

## Task S3-1：8 个 pass 分离计划、计算与归并

| # | Pass 入口（均在 src/NLCPG/Builder/Passes） | 首批相关既有测试 |
| --- | --- | --- |
| 1 | PartitionedSyntaxPass.cs | CpgWorkBatchSyntaxTests |
| 2 | PartitionedOperationPass.cs | CpgWorkBatchOperationTests |
| 3 | CallGraphPass.cs | CpgWorkBatchLocalPassTests |
| 4 | MemberAccessPass.cs | CpgWorkBatchLocalPassTests |
| 5 | ControlFlowPass.cs | CpgWorkBatchLocalPassTests |
| 6 | DataFlowPass.cs | CpgWorkBatchDataFlowTests、CpgWorkBatchInterproceduralTests |
| 7 | DominancePass.cs | CpgWorkBatchLocalPassTests、DocumentShardingEquivalenceTests |
| 8 | ControlDependencePass.cs | CpgWorkBatchLocalPassTests、CpgWorkBatchDeterminismTests |

这些测试是定位入口，不表示已经覆盖新增调度协议；实施前从测试方法确认触发了实际改动分支。

**每个 pass 按同一份迁移卡交付：**

1. 列明计划输入、前置阶段、读写的共享上下文、process 产物、稳定归并顺序、跨过程 barrier、取消点和当前遥测。不能仅因为返回 fragment 就认定 process 完全无副作用；Builder 的共享缓存也在审查范围内。
2. 提取可由外层调用的计划/计算/归并边界；尽量复用原算法和排序语义。NLCPG 保持中立的阶段契约，Application 的 adapter 负责 scheduler 提交；不得反向引用 Application 的计划或 runtime。
3. 只在 worker 外的协调入口提交。独立构建入口也通过同一阶段边界工作；不能在 process 内又启动 WorkScheduler、旧 executor 或 Parallel.For 来补充并行。
4. 为同一图指定唯一 writer，并按稳定顺序归并；多个文件的独立图可以分别归并。若 writer 本身作为工作项执行，必须有显式依赖与图级互斥所有权；PreserveOrder 只约束返回结果，不约束写图副作用。
5. 按 G0-M 限制待归并产物。建议对已冻结的阶段批次分成有界微批：完成一个微批后有序归并、解除产物引用，再创建下一提交。先证明该阶段计算不依赖尚未提交的兄弟批产物；不满足时必须保留原阶段屏障和受控流式归并协议，不能自行改变语义。
6. 原有 batch identity、实际并发与 processing/reducer wait 等指标须有明确替代来源。现有 WorkItem 委托不携带 workerId；禁止传常量 0 冒充真实 worker。补中立执行上下文或修改指标定义时，必须同步消费者及契约。
7. 核对相同输入的节点身份、边序、边载荷、跨过程关系和持久化结果，再验证本 pass 的取消/失败/保留上界。下一 pass 仅在它依赖的归并与 barrier 完成后进入。

**DataFlow 并发编辑边界：** 本报告不修改 DataFlowPass.cs 或 NLCPGDataFlowSparseSetTests.cs。未来无法安全取得该文件修改范围时，停留在部分迁移；不得跳过后删除 executor 或声称 8/8 完成。

## Task S3-1b：规则及深层 helper 的直连执行

拟涉及 ApplicationService、RuleGraphAnalysisExecutor、RuleGraphExecutor 以及 DecisionModel/ParameterShrinkAnalyzer 的计算与调度边界，实际文件范围以调用图为准。

- **推荐第一版**：rules(F) 内串行执行规则计算；所有依赖必须已满足才进入节点，多个就绪节点按原稳定策略取用。不能只遍历 `graph.Nodes.OrderBy(NodeIndexes)`，它没有检查缺失依赖、禁用节点、失败后继或取消状态。
- 复用现有 BuildInputs、节点状态、结果聚合和指标逻辑，避免维护两份规则算法。已有独立调用方继续由外层 adapter 调度；worker 内使用明确直连入口，不通过捕获异常再切换路线。
- **同时处理 ResolveUnits 与 ForEachScan**：前者即使位于“规则图返回之后”，仍可能在 rules(F) 内；后者本来就在规则体内。第一版在同一工作项直接计算，或在提交前可完全规划时提升为外层阶段；不允许从 worker 追加提交。
- 盘点 Builder 中 NodeSortKey 预计算、FlushParallelPublishWindow 等独立 Parallel.For 分支。若它们从工作项到达，须纳入外层计划或在当前工作项内串行，才可认为全路径受共享 P 控制。此处只是静态定位，尚未完成整个 run 的并发证明。

**配置行为必须明确：** groupParallelism=true 的单文件规则组并行，以及 helperParallelism=true 的嵌套扫描并行，在采用直连路线时会收窄。告警必须指出阶段、请求值、实际模式与作用范围；独立 replay/helper 仍可使用其正常额度，不要全局误报。默认 groupParallelism=false 也不能推出“全部默认行为不变”，因为 helperParallelism 的默认值不同。

规则累计耗时 78.3 s / 1.5% 属既有观察，不是整轮 wall-time 的退化上限；文件窗口并行也不自动保证抵消串行化成本。未来用相同工作量与结果的对照回答，不预承诺收益。

**验收：** 从真实目录规则阶段触发深层 helper，断言提交深度始终为 1、没有额外池、依赖/状态/输出逐条等价；覆盖 GroupParallelism 与 HelperParallelism 开关组合、失败节点及取消。原 DirectorySchedulerBoundaryTests 证明旧入口不能嵌套，保留该保护；另补新直连入口的正向用例，不删除反例来让测试通过。

## Task S3-1c：由窗口协调器组织跨文件静态阶段 DAG

**本次设计选择：建议先实现分阶段提交。** 原“从所有 shard 到 rules 一次冻结整张跨文件图”尚无可行性证明，也会遇到异构 TResult、submission 类别额度和结果长期保留问题。将提交边界收窄到已知计划的阶段，仍保持一个 run owner、共享 P、深度为 1；不恢复目录外包整文件的错误路线。

**这项建议与原设计的差异：** 从“整流水线恰好一次 RunAsync”改为“协调器在 barrier 外顺序提交多份静态图”。未来实施时须先同步设计文档及对应 feature 完成条件；本次仅在执行报告中提出，不把它记为已接受的产品实现。若仍要求一次提交，则 G0-P 必须先证明全部任务可提前构造，并补出带类型的阶段结果协议、分阶段额度及资源消费/释放协议；在此之前不得照占位代码上线。

建议执行顺序如下（流程说明，不是现有 API）：

```text
run owner（在 worker 外）
  枚举有界文件窗口，保留原始 file identity
  根据当前已就绪输入，规划既有 CPG 阶段的静态微批
    提交同类工作 → 等待完成 → 按图/稳定顺序归并 → 清理已消费资源
    达到该阶段 barrier 后，规划下一阶段
  各文件 CPG 完成并冻结后，提交无内部调度的规则计算
  根据原有目录授权与改写边界收集结果，不提前执行改写副作用
  窗口消费完成后释放资源，继续下一个窗口
  整个 run 收尾：发布已允许的结果，关闭自有 scheduler
```

**重要约束：**

- S3 继续使用**已有方法分批与 descriptor**，不引用 S5 才创建的 `_shardPlanner`。窗口协调不等于外部 DocumentShardPlanner；后者是 S5 的额外能力，不能倒置依赖。
- 协调器每次只保持一个受限阶段提交，避免同类别多个 submission 绕开总额度。独立 replay 等若可与之重叠，run owner 另行分配保留额度；不能只引用每次 MaxConcurrency 就证明总上限。
- 每次静态提交冻结后采用 checked long 前缀枚举 StableOrder，另存 file identity、pass、batch 身份；验证唯一性和依赖有效性。不要使用可能溢出或碰撞的 `fileIndex * StageStride` 猜测编码，也不以完成顺序分配标识。
- 不同阶段分别使用明确 TResult；大图/fragment 不无期限留在通用结果数组中。轻量完成记录到窗口资源的访问权和释放责任必须遵守 G0-M；PreserveOrder=false 仍会保留内部数组。
- interprocedural 阶段等待**同一文件所需的全部前置片/批归并**；FreezeQueryIndex 的时点保持与原构建流程等价。原本跨文件的依赖、授权和目录级决策不能被窗口边界截断；窗口外仍保留所需的轻量事实或共享 compilation。
- 阶段屏障可能降低流水线重叠，这是明确的吞吐代价。先验证语义与内存，再用同版本对照决定是否设计跨阶段重叠；不能直接声称扁平化一定更快。

**配置映射建议（待实现与契约验证）：**

| 配置范围 | 窗口协调时的含义 | 需要避免的误判 |
| --- | --- | --- |
| DirectoryLimit / DirectoryParallelism | 窗口同时存活的文件数；关闭时最多 1 个文件 | 文件数不是 CPG 批次 DOP；多个窗口不可无界并存 |
| CpgLimit | CPG 阶段提交的活跃工作项上限 | 下层 Parallel.For 不能再把它相乘 |
| RuleGroupLimit / GroupParallelism | 独立规则图保留原语义；窗口内 rules(F) 使用明确的阶段额度与直连规则计算 | 关闭时保守按 1 执行；改变跨文件适用范围必须记录，不静默覆盖 |
| HelperLimit / HelperParallelism | 独立 helper adapter 可提交；规则体内 helper 第一版直连 | 不承诺嵌套 helper 仍有独立并行；日志写实际模式 |
| ReplayLimit | 顶层 replay 提交上限 | 不意味着与 CPG 共用一份 submission 的配额 |
| MaxConcurrentOperations | 未分类操作默认提交上限 | P 取各配置上限最大值，不能用默认值代替所有阶段 |

六个 YAML 字段名、必填性和三个并行开关保留；新含义在实际接线前均为设计。显式 MaxConcurrency 必须先应用开关并受到设计映射约束，日志同时区分请求值、类别默认与提交实际值。

## Task S3-2：旧 WorkBatch 执行设施的删除门槛

本任务在 S3-3/G3、S2-3/2-4/G2 后执行，编号不表示提前删除。

- 逐项确认 8 个 pass、Builder、独立构建、目录与 helper 不再调用旧 executor/queue budget；测试替身的处理与新的行为断言同批进行。
- 删除 CpgWorkBatchExecutor 与它拥有的队列预算前，新的有界产物保留、归并等待、失败和取消协议必须有替代证据。仅检索到零字段引用不构成完整验收。
- **执行器与计划描述符分开处理**：CpgWorkBatchBuilder、batch descriptor、cost model 等仍可能用于 S3 计划，不能因删除 executor 一并删除，也不能假设 S5 的计划器已经存在。
- 对旧 IConcurrencyPool/CpgBuildAdmissionBudget 的删除还要满足 S4 的引用盘点。保持每次改动可回退到对应已验证状态，不把混合迁移中间态误标为完成。

## Task S3-3：Gate G3 验证与回退

| 验证面 | 必须观察到的证据 |
| --- | --- |
| CPG 语义 | 隔离候选入口在相同源码/配置下 DOP 1/2/16、单批/多批的节点身份与完整边序/载荷等价；真实批次和活跃 worker 数证实并发确实发生 |
| 规则语义 | 规则、决策、标记、提升、改写计划逐条相同；禁用、失败、取消节点状态正确；不只比较通过数 |
| 提交边界 | 从实际目录调用达到 pass、ResolveUnits、ForEachScan；无嵌套/隐藏内核，配置开关与提交实际额度对应 |
| 内存 | 小窗口、慢首批、大产物、多个连续窗口和单超项：执行中计数与 retained backlog 分开；峰值在事先定义的停止线内 |
| 生命周期 | 成功、失败、取消、重复 run 与 Dispose 竞争均收尾；自有实例被释放，借用实例仍可使用 |
| 回归 | 最小相关测试先行，之后按 harness 验证矩阵扩大；既有失败逐条匹配基线，不掩盖被改变的语义守卫 |

相关真实测试入口见 S3-1 表及 S1，新增验证是**拟补充项，当前不存在通过证据**。本次不运行测试。

任何边序、预算、嵌套或生命周期门槛失败，停止切换生产入口，回到既有目录池 + 原 CPG 路径；保留差异和复现，不通过排序边、降低断言或重起内核掩盖问题。不得回退到已知不可行的 S2-3 整文件工作项示例。

---

# S4：删除旧层、统一日志与等价性门槛

S4-3/G4a 前移到 S3 之前；S4-1 的删除在 G2/G3 之后。阶段编号用于追踪，不再暗示可按文件顺序盲目执行。

## Task S4-1：仅删除已无活跃调用的设施

原设计的删除清单仍是候选，不是删除授权的证据。实施前同时检查生产、测试、独立调用与项目引用：

```powershell
rg -n -F -e 'IConcurrencyPool' -e 'ConcurrencyPool' -e 'CpgBuildAdmissionBudget' -e 'CpgWorkBatchExecutor' src tests --glob '*.cs'
rg -n -F -e 'CommitTwoStageOrdered' -e 'CommitOrdered(' -e 'SelectCpuBoundOrdered' -e 'RunPartitionedOperationPassOrderedCompatibility' src tests --glob '*.cs'
rg -n -F -e 'Parallel.For' -e 'Task.Run' -e 'new WorkScheduler' src --glob '*.cs'
```

对每个命中分别标明定义、仍活跃调用、测试替身或内核本身实现；不是所有命中都应删除。无匹配时 rg 的 exit=1 表示检索无结果，不是构建失败。

- SelectCpuBoundOrdered、CommitTwoStageOrdered 等曾被 ConcurrencyPoolContractTests、PipelineComponentTests 使用，删除时将“旧调用被触发”的断言替换为对应顺序/取消/并发行为，不能只删失败测试。
- 尚在生产路径上的目录池、helper 池和预算继续保留；CpgWorkBatchBuilder/描述符是否需要保留按 S3-2 判断。
- 新增中立阶段契约、调整项目引用时检查 ArchitectureBoundaryTests 与 LayoutArchitectureTests；不新增无必要的 InternalsVisibleTo 或反向引用。
- 每次只处理显式文件列表，不执行 `git add -A`，不自动提交用户工作区中的其他改动。

## Task S4-2：日志消费、配额语义与数据来源

**状态以 unified-work-scheduler-s4-runtime-log-effective-limits 为准：该 feature 已完成的是有效配额日志及接收端接线，不是全部 S4。**

| 范围 | 已有事实 | 尚需做什么 |
| --- | --- | --- |
| 类别默认解析与 P | 日志读取真实 Scheduler.Options，输出六个 Effective 值、WorkerCount、三个开关和 directoryWindowSemantics；normal 视图可见 | 区分默认解析与每次提交的显式 override；字段出现不等于目录窗口已经实施 |
| 注入 scheduler | SchedulerOptions 指向注入实例真实 Options；红测试曾以 WorkerCount=9 对比 execution options=2 | 保留注入所有权，不能从旧 ExecutionOptions 重新计算并冒充实配额 |
| 内核 collector | 自建内核接 WorkTelemetryCollector，派生 runtime 共享；注入实例的 collector 由注入者负责，runtime.WorkTelemetry 可为 null | “接上 sink”不等于日志已消费它；不可重复归属/计数 |
| 运行日志消费 | RuntimeMeasurementLog 的 pool* 仍来自旧 ConcurrencyTelemetry.Operations | 实施旧/新指标兼容映射与来源标识，完成迁移后才删旧消费者 |
| CommandHost 映射 | 先构造 RoslynPrototypeExecutionOptions，再由 runtime 映射，当前映射正确 | 直接构造 WorkSchedulerOptions 是可选简化，不作为必须修复的缺陷 |
| 规则/helper 内联告警 | 当前规则 DAG 的 groupParallelism 仍有效 | 只在 S3 真正启用直连且改变语义时添加对应告警，范围包含 helper |

**指标迁移规则：**

- 为旧字段逐个记录含义、单位、聚合方式、新来源和兼容策略。只有语义相同才沿用 pool* 名称；新指标不能因名称相近就替换旧值。
- WorkTelemetry 没有旧的 weighted turns / aging promotions 计数，不能填 0 冒充已观测；改为明确不可用、保留旧来源，或在另行设计计数后迁移。
- PeakInFlightBytes 是执行中估算，不能当作真实 heap/working set 或保留产物峰值。多个 submission 的各自峰值也不能直接相加得出同一时刻的全局峰值。
- 总和、最大值、时间轴和计数必须按 RunId/阶段/提交范围关联。现有 RunId 可空；未关联时明确限制，不伪造全 run 归因。
- 保留真实的 queue wait、processing、归并等待与 retained backlog 观测需求；删除旧 executor 不代表这些等待成本消失。sink 失败仍 fail-open，不改变执行/取消结果。

**历史验证边界：** 轮次 20 的日志/视图定向测试与变异检查、轮次 21 的注入配额红绿验证、轮次 22 的 collector 接线变异检查均记录在 feature_list.json。轮次 22 小子集为 Contract ~Concurrency 53/53、Host 日志 3/3 及 harness 一致性；不据此声称完整 Host/Performance 档通过。本次未重跑。

## Task S4-3：G4a / G4b 分片等价性

### 当前状态：已按用户决定拆分并关闭 G4a（2026-09-25 更新）

> **本节已于 2026-09-25 更新。** 下面原来的「尚未对齐」判断已被真实复跑取代，保留其原文仅供追溯差异；**以本段为准**。

- **权威状态（更新后）**：原 `unified-work-scheduler-gate-g4-sharding-equivalence` 已按用户 2026-09-25 的决定**拆分为两条**：该 id 现改标题为 **G4a**（状态 `completed`），并新增 **`unified-work-scheduler-gate-g4b-shard-adapter`**（状态 `todo`，无证据）。保留原 id 是为了让本节原有引用不失效。
- **G4a 关闭依据**：附录 C 的 3/3 次真实复跑（14 通过 / 0 失败 / 0 跳过，EXIT=0）＋ 反串行化护栏为绿 ＋ 运行窗口内 `src/NLCPG/**` 零改动。轮次 23 的 FAIL 不是被叙述推翻，而是被同版本、可重复的命令取代。
- **G4b 仍未通过**：`DocumentShardPlanner`、`DocumentShardPlan` 与 Application adapter **不存在**，故 **S5 仍不得启用**；G4a 的既有测试只改 `WorkBatchMaxMethodsPerBatch` 与 `Persistence.StreamingMode`，不能替代 G4b。
- **本次判断（保留原意）**：不能只凭交接文字或"源码已解除 Skip"就把门槛标为通过。这正是本轮补做真实复跑与哈希归因的原因；**S3 前所需的是 G4a，S5 前所需的是 G4b，二者不可互相顶替。**

以下为 2026-09-25 之前的原文（已被上段取代，保留以便核对差异）：

- ~~**权威状态**：unified-work-scheduler-gate-g4-sharding-equivalence 仍为 blocked，记录的是 2026-09-24 轮次 23 的 dominance 并发字典崩溃。复现要求多批与多个实际 worker；单批高 DOP 不构成覆盖。~~
- **后续交接**：progress.md 轮次 32 记录另一工作流已修复并行 dominance，DocumentShardingEquivalenceTests 与反串行化守卫合计 14/14、0 跳过。当前测试源码的相关 Skip 已移除。
- ~~**本次判断**：……本次不运行复测、不改 feature 状态。~~（该次未复跑；2026-09-25 已补做，见附录 C。）

历史复现中 DOP=8/200、16/200、16/400 方法曾触发失败，DOP=8/70 未复现；同配置三次中两次失败。此前单批 DOP=2/4/8 的通过只证明该形态的保序，不能证明并行 dominance 正确。保留这些反例的用途是要求真实并发与重复性证据，不把它们当作当前源码的故障结论。

### G4a：既有分批与 streaming 的基线门（在 S3 前）

真实入口：[DocumentShardingEquivalenceTests.cs](../../tests/NLISSN.ContractTests/Cpg/DocumentShardingEquivalenceTests.cs)、[DominanceRaceReachabilityProbe.cs](../../tests/NLISSN.ContractTests/Cpg/DominanceRaceReachabilityProbe.cs)。

1. 校对现有测试：方法每批 1/2/4/8、单批改变 DOP、多批且并行、StreamingMode 及生产默认可达性。记录哪些配置确实产生了多个批及多个活跃 worker；保留非空断言、反串行化与反排序守卫。
2. 当前签名的节点按 NodeId 排序比较字段；边按 graph.Edges 的实际枚举序逐位置比较。不能称“节点和边都不排序”，也不能用其他会先排序边的 comparer 替代它。
3. GraphSnapshotVersion、节点字段与边载荷一起核验；流式路径确实产生持久化文件。一次现有签名通过不等于所有未来载荷字段均已覆盖，缺失项在 G4b 补齐。
4. 复用可信的同版本证据；版本、触发形态或日志不足时在后续授权执行中重跑最小过滤器。既有失败不能通过减少并发、放宽边序或重新 Skip 来“通过”本门。

以下仅是后续实施时的真实命令模板，**本次不执行**：

```powershell
$dotnetArgs = @('test','./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
  '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false',
  '--filter','FullyQualifiedName~DocumentShardingEquivalenceTests|FullyQualifiedName~DominanceRaceReachabilityProbe')
& ./Build/Tools/Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

### G4b：真实外部分片 adapter 的启用门（S5-2 后）

G4a 调整的仍是 NLCPG 既有方法分批和 streaming 选项；它没有调用未来的 DocumentShardPlanner/Application adapter。因此新增隔离测试必须**实际经过新计划 → adapter → NLCPG**，并用同源的旧 DOP=1 路径作 oracle。

- 断言 planner 产出多片、adapter 消费全部片、跨片引用被解析；新路径未调用时用例应失败。单纯重用 G4a 的构建选项不会证明这一点。
- 比较稳定锚点、最终 NodeId 与完整节点载荷、边枚举序/端点/标签/上下文、跨过程关系、能力与降级标记、冻结索引的查询结果、持久化往返以及下游规则/决策输出。
- 覆盖分片边界跨方法引用、空片/单片、乱序完成、取消和构建失败；证明没有重复发布、遗漏归并、提前 freeze 或提前授权改写。
- 保留 order-only 反例证明 oracle 的判别力；对未来增加的 schema 字段同步比较。失败则关闭新路径，记录具体差异，沿用已验收旧路径。

**G4b 不阻止为验证它而编写隔离 adapter，但在通过前禁止生产启用。** 不因本报告增加名字就改变现有 feature 的完成状态。

---

# S5：外部分片与文件失败隔离（设计，尚未实施）

本阶段以完成 S3/G2/G3 和既有 G4a 为前提；先在隔离入口实现并验证 adapter，通过 G4b 后再启用。分片与失败隔离是两个独立行为变更，应分别验收，不能捆绑上线后无法归因。

## Task S5-1：外部分片计划器

拟新增（当前不是已实现入口）：

- `src/NLISSN.Application/Analysis/DocumentShardPlanner.cs` 与 `DocumentShardPlan.cs`。
- 对应 `tests/NLISSN.UnitTests/Analysis/DocumentShardPlannerTests.cs`；测试路径在实施时按测试目录约束确认。

计划器只接受已读取的文档/方法元数据并做纯计算，不在 DirectoryAnalysisUseCase 或 planner 内进行 File/Directory I/O；保持现有 ArchitectureBoundaryTests 的 Application 边界。

**输入与输出契约：** 固定输入顺序、稳定文件身份、方法/声明边界、估算成本与可用窗口预算；输出可重放的分组/分片描述，不提前构建或保留全部图。每个合法输入范围恰好覆盖一次，跨片引用可表达；不为达到目标行数切断不可独立构建的方法。

**阈值只是候选：** 原设计 SmallFileMaxLines=200、MediumFileMaxLines=800、LargeFileShardTargetLines=1500 尚无调优证据；集中配置、记录版本，不描述成已验证默认最优值。行数不能同时充当 bytes 上界或耗时预测。单个超大方法若不能合法分片，走受控整项路径，另做长尾算法研究，不在此隐式启动方法内并行。

**首批小测试：** 空文件、单方法、阈值两侧、同成本稳定排序、大文件含跨方法引用、不可再切的单超项；分别断言覆盖、无重复、稳定身份、预算/容量边界及分片确实改变。小/中文件成批与大文件分片分别有可观察用例，不用一个“有结果”断言代替三类覆盖。

## Task S5-2：Application adapter 到 NLCPG 中立契约

**依赖方向：** Application 拥有 DocumentShardPlan；adapter 将其转换成 NLCPG 或更下层共享契约所表达的分片范围/稳定身份。**NLCPGBuilder 不直接接收 Application.DocumentShardPlan，也不新增 NLCPG → Application 项目引用。** 下层契约放置位置在实施时按实际项目依赖和架构测试确定。

1. 复用已存在且仍需要的 WorkBatchBuilder/descriptor；删除 executor 不意味着描述符也已删。新 adapter 的替换边界由 S3 的阶段契约决定。
2. worker 产候选或 LocalCpgFragment，boundary reference 明确；同一图唯一 writer 依稳定顺序归并、分配最终 NodeId。不能让不同片各自发布全局 NodeId。
3. interprocedural 计算在它所依赖的全部片/批归并后执行，最终 freeze/索引/持久化边界与基线相同；沿用 G0-M 的 last-consumer 清理，不让通用结果集合延长整个窗口内大产物的寿命。
4. 隔离测试从真实 planner 进入 adapter，并通过 G4b。失败时新路径保持禁用，原路径可重放同一输入；通过后才迁移生产入口。

## Task S5-3：文件失败隔离与取消传播

现有目录池的首错取消整次操作，是当前行为；改为局部失败后继续其他文件必须单独记录，不能用 AnalyzeShard 外包 catch(Exception) 草率实现。

**建议采用有类型的结果协议**（名称仅示意，不是当前已有 API）：

| 结果 | 后继与对外行为 |
| --- | --- |
| Success | 只有所需分片完整、归并/冻结成功后才能产生文件级成功；轻量结果可供目录决策使用 |
| FileFailed | 仅由已分类的可隔离文件分析错误产生；保留文件身份、阶段、原因与诊断，阻断该文件的 reduce/rules/发布 |
| BlockedByDependency | 因所依赖文件/阶段失败而不能继续；保留因果链，不伪造空图或“无引用”事实 |
| 外部取消 | 保持取消传播，停止新窗口，等待并清理已接受工作；不能转成 FileFailed 后继续 |
| 内核/不变量/资源失效 | 继续传播为整个 run 失败；图身份冲突、非法依赖、预算算术错误等不纳入普通文件隔离 |

内核目前对抛出的工作项异常实施 submission 首错取消；若文件错误被转为结果变体，后继必须显式检查它，否则内核会把“返回失败变体”当正常完成并继续解锁依赖。这是结果协议必须覆盖的关键分支。

- 一个文件任一必要分片失败时，该文件全部结果不得发布为完整图；取消/释放兄弟片与缓存的规则要明确。其他文件只有在原领域不变量允许时才继续。
- 跨文件引用或规则事实缺失时使用 Unknown/fail-closed；“文件分析失败”不等于“没有引用，所以可以删除”。目录级诊断标记降级，并沿原授权链路阻止无法证明安全的改写。
- 分析和改写副作用分开：不能在 rules(F) 结束时直接提交文件改写、持久化半图或授予删除许可，再期待目录失败时回滚。发布边界遵循原有完整性/授权条件。
- 失败隔离不扩大捕获范围到任意异常；取消 token、异常类别、调用阶段与共享状态是否完整共同决定能否局部恢复。

**拟补测试：** 两文件一失败一成功、同文件一片失败、跨文件事实依赖失败、外部取消、不可隔离内核错误；断言成功结果完整、失败/阻断诊断可定位、无半图/未授权改写，以及 run 正确释放。可在 `tests/NLISSN.HostTests/Analysis/DirectoryFailureIsolationTests.cs` 新建，当前未实现、未执行。

## Task S5-4：分层验收、证据归档与状态更新

未来实施按“最小有判别力契约 → 相关 Host → 验证矩阵要求的其他档位”推进，性能与正确性分开记录。历史小子集通过不能换算为全量通过；本次只完成报告审查。

**先设计三组快速且可重复的调度夹具：**

1. 两文件、每文件少量既有批：验证不同图可重叠而同一图单 writer、按序输出、关闭后没有悬挂工作。
2. 用显式闸门控制“首批慢、后续批快”，不用长时间 sleep：检查实际积压数量、结果引用存活与窗口停止推进。内存断言区分引用生命周期、估算计数与真实堆峰值，不以一次 GC 后的数值替代协议验证。
3. 连续多个小窗口，分别注入可隔离错误和取消：验证资源清理、后继阻断、借用内核仍可用、自有内核最终关闭。

每组记录真实批数、有效并发、输入/配置/代码哈希与原始结果；新路径不触发时必须失败。快速契约解决确定性与生命周期，真实峰值/吞吐仍需后续受控代表输入，不从合成夹具外推。

**性能对照要求：** 使用相同数据和能力集，明确热身/测量、重复次数及停止线；交错运行旧/新路径，报告分布与环境干扰，不只报五次最优。分别记录 wall time、分阶段成本、分配/存活堆、working set、执行中估算、未归并产物与保留容量。存在共享 compilation 或页交换时明确归因边界，不把内核计数当全进程内存。

完成实际实施后才更新：

- `Context/feature_list.json` 中对应现有 feature 的状态、完成条件和同版本证据；不创建重复“已完成”条目掩盖原 blocked 项。
- `Context/progress.md` 中仍影响下一步的事实、已验证与未验证边界；替换过期内容，不追加互相矛盾的完成记录。
- [统一工作调度设计](2026-09-24-unified-work-scheduler-design.md)、[运行时基础设施](../CodeDesign/目前设计/运行时基础设施.md)、[日志与并发](../CodeDesign/目前设计/日志与并发.md)及[运行时并发配置](../CodeDesign/目前设计/运行时并发配置.md)的实际契约；旧 WorkBatch 设计按实际替代范围标注。

本次不执行这些状态更新、实现、测试、基准或提交操作。报告完成检查仅运行文档一致性（跳过 CLI smoke）及 Markdown/链接/空白静态检查；检查结果不计作 G0/G2/G3/G4 的通过证据。

---

## 完成条件清单（实施状态，不是本次文档任务状态）

- [x] G1 已有历史内核契约证据；S1 39/39 与后续 49/49 分属不同轮次。
- [x] 四个规则 DAG 生产构造点、replay、ResolveUnits 已有局部迁移与历史证据。
- [x] 有效类别默认/P 日志、normal 视图、注入实例 Options 与自建 collector 接线已有证据；仅限 S4-2 已说明范围。
- [x] G4a：已按 2026-09-25 用户决定拆条，并以 3/3 次真实复跑确认既有分批/流式路径的等价基线（附录 C）。记录 `unified-work-scheduler-gate-g4-sharding-equivalence` 已标 `completed`。**注意：这只关闭 S3 前的 G4a，不解除 S5。**
- [ ] G0-M：执行额度与存活产物双生命周期、窗口上界、估算校准、非法/溢出/超大项策略均有生产验证。**进展（附录 D，2026-09-25 轮次 5）：「非法项」与「溢出」两项策略已实现并验证——在途字节比较由可回绕的 `InFlight + bytes <= Max` 改为溢出安全的减法式（与既有 legacy `CpgWorkBatchExecutor.cs:701` 一致），并在提交前拒绝负 `EstimatedBytes`/`EstimatedCost`。两处均有先红后绿用例 + 变异检查（各恰好失败其自身 1 条）。`~WorkSchedulerByteBudgetTests` 7/7、`~Concurrency` 64/64、Contract 542/543（唯一失败为既有 LayoutArchitectureTests）。仍未关闭：**估算校准未做**（`EstimatedBytes = 行数×64`，正是本计划 :114 点名不可靠的形式；设计 R6 要求校准后才作硬额度）；**`MaxInFlightBytes` 仍无生产调用方**，故该修复当前不改生产内存行为；窗口上界、retained backlog 停止线、连续窗口阶梯增长均未验证。****进展（附录 F，2026-09-25 轮次 7）：「超大项策略」在**配置面**的落点已实现并验证——`WorkSchedulerOptions` 新增 `MaximumWorkerCount = 1024` 与 `Validate()`，`WorkScheduler` 构造期即拒绝超出上界的配置。此前 `WorkerCount` = **六个无上界字段**的最大值，而首个提交会按该值逐个 `Task.Run`（`WorkScheduler.cs:192-195`），故一个通过全部校验的合法配置即可让首次提交排队上亿个任务；轮次 6 的溢出守卫只看 `maxConcurrentOperations` 一个字段，无法覆盖此缺口。刻意**不静默截断**（截断会改变用户配置意图而不告知）。先红后绿（红暴露的是我自己测试断言过严，未改产品代码）+ 变异检查（注释掉 `Validate()` 恰好失败 2 条判别用例，其余 6 条仍通过；恢复后与备份 SHA256 一致）。`~WorkSchedulerWorkerBoundTests|~ExecutionQuotaBoundaryTests` 8/8、`~Concurrency` **72/72**（轮次 5 为 64，+8 全为本轮新增，零回归）、Unit 117/117。仍未关闭：**估算校准**、窗口上界、retained backlog 停止线；1024 为工程判断**非实测标定**。****进展（附录 G，2026-09-25 轮次 8）：**估算校准已按计划 :114 要求实施**，结论是否定性的——`src/` 内**不存在任何**真实字节测量（`GC.GetTotalMemory`/`GetAllocatedBytesForCurrentThread`/`Unsafe.SizeOf` 命中数全为 0），所有 bytes 都是常量乘法。实测结构宽度：`CpgNodeDescriptor` **92 B**（公式假定 64）、`CpgEdgeCandidate` **120 B**（公式假定 48），同一夹具比值 **2.234×** 且因含引用字段仍属下限。**更关键的是原理性结论：**`Estimate(startLine, endLine)` 自变量**只有行号**，同一行范围下真实载荷实测跨度达 **1349×**，故**不存在任何常量 k** 使「行数 × k」成为上界——这是缺自变量的结构性缺陷，非「常量不够准」。附带发现：默认 `TargetBatchCost=400` 与 1 MiB 字节上限并存时，实测峰值仅 **25,600 B（额度 2.4%）**，**字节额度恒为松弛**，不能当作独立内存门槛。新增 `CpgFragmentByteCalibrationTests` 7/7；两次变异检查各恰好失败其对应用例（2 条 / 1 条）。**未修改任何生产估算公式**（正确修法属设计变更）；窗口上界、retained backlog 停止线仍未关闭。**
- [ ] G0-L：run 自建/借用/派生 owner 清楚，成功/失败/取消/竞争/重复 run 均正确收尾。**进展（附录 B.2/B.3/B.4）：所有权已接线并验证——`AnalysisRuntime.OwnsScheduler` 区分自建/借用/派生，`CommandHost` 经 `finally` 释放，`ApplicationService` 两个自建重载亦已释放（含测试扩展实际走的五参路径）。19/19 + ~Concurrency 61/61 + Host 全量零新增失败。仍未关闭：**外部取消在生产中无触发源（附录 A.2）**，故取消语义未验证；无重复 run 的句柄/线程级证据（线程计数已实测**无判别力**，见 B.4）。**
- [ ] G0-N：从目录到 pass/rules/ResolveUnits/ForEachScan/Builder 深层并行均有实际覆盖，无隐藏嵌套或额外内核。**进展（仅静态，附录 A）：当前生产源码无「内核工作项内再提交」；但 CPG 侧自带 worker 与两处 `Parallel.For` 都在内核之外，「统一 P」尚未成立；深层调用的真实阻塞只剩 ForEachScan。运行期断言仍缺。****进展（附录 H，2026-09-25 轮次 9）：运行期断言开始补齐——发现并修复嵌套守卫的**跨实例盲区**：守卫原用 `ReferenceEquals(CurrentScheduler.Value, this)`，只识别**同一实例**，而计划 :118 明文禁止的「worker 内新建第二个 scheduler」可完全绕过（先红实证为 `No exception was thrown`），后果是内存中同时存在**两份独立 P**。已改为「只要处于任一工作项内即拒」，并对同/异实例给出不同消息。`~WorkSchedulerCrossInstanceNestingTests|~WorkSchedulerNestingTests` 6/6、`~Concurrency` **75/75**（+3 全为本轮新增，零回归）、Host 全量失败名单与基线逐字一致；变异检查把守卫改回 `ReferenceEquals` 后**恰好失败该 1 条**。已核对调用图确认放宽不误伤生产（`RuleGraphExecutor` 兜底内核在 `src/` 中零命中，4 处生产调用全部显式注入 `session.Runtime.Scheduler`）。**进展（附录 H.6，轮次 10）：推翻了轮次 9 自己写下的另一条边界**——实测证明 `Task.Run` 包装与同步等待**本来就被守卫覆盖**（`AsyncLocal` 随 `ExecutionContext` 流入 `Task.Run`；同步等待在同线程），轮次 9 的「未加守卫」说法**作废**；同时发现**真实残余缺口** `ExecutionContext.SuppressFlow()`，并**证明**该缺口不能靠「全局在途计数」修补（变异后**恰好失败 2 条**：缺口固定用例 + 反例用例 `RunAsync_WhenAnotherSchedulerHasAnItemInFlight_IsNotSpuriouslyRejected`，后者用显式闸门确定性复现，不需要竞态）。**进展（附录 I，轮次 11）：覆盖了此前从未被执行的目录并行分支**——既有断言只传 1 个源文件，被 `DirectoryAnalysisUseCase.cs:253-255` 的 `sources.Count <= 1` 分流到**串行**分支，`SelectOrderedAsync` 并行路径从未跑过。新增用例用 3 源 + `DirectoryMaxParallelism: 3` 真正进入并行分支并断言同样被拒：**3/3 通过** ⇒ 守卫在并行分支**同样生效**，且该池**未**压制 `ExecutionContext`（H.6 担心的生产投影**不成立**已完成实证）。变异（停用守卫）后串行与并行**两条都失败**，证明非空转；Host 全量失败集合与基线**逐用例名 A/B 为 IDENTICAL**。同时**实证**了 P×DOP 重叠真实存在（`SelectOrderedAsync` 确实并发执行多个 `AnalyzeFile`，各自带 DOP）。**仍未关闭：更深层 pass/rules/`ResolveUnits`/`ForEachScan` 未逐层断言；「统一 P」仍未成立；`SuppressFlow` 缺口只记录未修。****进展（附录 K，轮次 13）：**补上了附录 J 自己承认的缺口——`ForEachScan` 首次被运行期覆盖**。轮次 12 走不到它是因为最小目录夹具不含索引器；本轮改为**直接以索引器为入口**驱动 `ParameterShrinkAnalyzer.TryBuildIndexerPlan`，并构造**三个语法树**使 `scans.Count == 3` 落入并行分支（入口传的是**索引器参数的 `TypeSyntax`**，不是 `IndexerDeclarationSyntax`）。实测（`foreachscan-nesting.txt`）：`ForEachAsync=1`（确实走到，轮次 12 此处为 0）、`forEachAsyncMaxDegrees: 2`（**确为并行分支** ⇒ `:1357` 的同步阻塞真的执行了）、`nestedObservations: 0`（该阻塞**不在**内核工作项内）、`kernelSubmissions: 1`（就是本次探测自己的提交）。⇒ 附录 A「深层调用的真实阻塞只剩 ForEachScan」**准确**，它目前**在核外**、尚未构成隐藏嵌套。`nestedObservations: 0` 与 `kernelSubmissions: 1` **必须一起看**才自洽（探测从工作项之外发出 ⇒ 不被拒 ⇒ 记为未嵌套且留下 1 条遥测）。变异检查：把 `:1357` 的旧池调用**替换为向内核提交**（模拟 S2-4 禁止的形态）后**测试失败**，且失败在**正确断言**上（`未观测到 ForEachAsync 调用`）⇒ 证明能判别、非空转；已按原样恢复、**SHA256 逐字节一致**、无残留。**仍未关闭：其余 pass、`ResolveUnits` 与规则体内其他核外并行点仍未覆盖；「统一 P」仍未成立。**——用 `IConcurrencyPool` 装饰器（转发全部 8 个成员）在每个回调内做嵌套探测：在内核工作项内提交平凡任务，若抛出含 `"flat"` 的异常即证明「身处内核工作项内」（只用公开契约，不反射私有状态）。实测（`Build/g0m-calibration/deep-facility-inventory.txt`）：`SelectOrderedAsync=1、nestedObservations=0、kernelSubmissions=4（default=1 + rule-group=3）`。⇒ ①该路径上**无隐藏嵌套**（运行期确认，此前仅静态推断）；②核外并行**确实存在**（目录层 `SelectOrderedAsync`）；③内核**确实被使用**，盘点非空转。同时**运行期确证 P×DOP 重叠**。**⚠️ 但该夹具**未**覆盖 `ForEachScan`（`ForEachAsync` 计数为 0）**——最小夹具走不到参数收缩的特定语法路径，故它在核内还是核外**仍未实测**；本附录**不**声称覆盖它（否则就是把「夹具覆盖不足」当成「性质成立」，重犯轮次 10 的错）。**
- [ ] G0-P：每阶段静态规划时点、结果类型、配额作用域、分层与窗口外依赖明确；建议变更同步到设计及完成条件后才实施。**进展（附录 N，轮次 16）：首个设计输入已给出——从源码确证「阶段依赖是隐式的」。** `NLCPGBuilder.cs:411-417` 用 `RunOptionalPass(buildPlan.Requires*, ...)` 驱动 7 个后置 pass，但 `Requires*`（`:583-589`）**全部来自 capability 位**，只回答「能力是否被请求」，**完全不表达阶段依赖**；真实依赖**仅由那 7 行的书写顺序保证**。已确证两条：①`DataFlow` 需要 `CallGraph`（`DataFlowPass.cs:1934-1937` 有 `throw`，但**实测不可达**）；②`InterproceduralDataFlow` 需要 `CallGraph`+`DataFlow` 的 reducer 都完成（`NLCPGBuilder.cs:1041-1044` **纯注释，零守卫**）。新增 `BuilderStageDependencyContractTests` 3/3（用跨过程桥接边为判据）；变异（跳过 `CallGraphPass`）后 **3/3 失败**，证明判据有判别力。**⚠️ 过程记录：同一断言连续写错三次**——第一次断言「异常被运行期强制」（变异后仍通过，实测该 throw 不可达）；第二次断言「`DataFlow` 边非空」（变异后仍通过，因为方法内数据流不经 CallGraph，夹具结构性区分不了）；第三次改用 `InterproceduralDataFlow` 桥接边才成功。**关键对照：前两次失败时同伴的 `CpgInterproceduralEdgeOrderTests` 在同一变异下失败 3 条 ⇒ 不是变异没生效，是我的观测面选错了。** 对 S3 的结论：重排前须把隐式依赖显式化；**不得用 `Requires*` 推导阶段依赖**；验收判据须取「该阶段特有的产物」。**仍未关闭：其余 5 个 pass 之间的依赖未逐一确证，完整阶段依赖表尚未给出，本附录不声称已给出完整 DAG。** **进展（附录 O，轮次 17）：依赖表由 2 条扩到 4 条**——③`ControlDependence` 需要 `Dominance` 填充 `_dominanceOverlays`（`ControlDependencePass.cs:35` 在为空时**整体静默 return**）；④`DataFlow` 的 CFG 邻接规划读 `ControlFlow` 写入的 `_cfgPredecessors/SuccessorsByNode`（`DataFlowPass.cs:1227-1229` ← `NLCPGBuilder.cs:2062-2063`）。**2/3/4 零守卫，1 的守卫不可达 ⇒ 四条全靠调用顺序维系。** **并修正附录 N 的低估：真正的通道不是「7 行的书写顺序」，而是 `NLCPGBuilder` 上 30+ 个可变实例字段**（`_resolvedCallTargetsByInvocation`、`_cfg*ByNode`、`_dominanceOverlays`、`_syntaxNodes`…）——它们**不是参数**，无法经签名传递，S3 拆分阶段时若不同阶段不共享同一实例状态，字段会**静默为空**。依赖③经**先变异**确认**已有 5 条测试覆盖**（故**未新增**测试）；依赖④新增 2 条判据，变异后 **2 条失败**、判据有判别力。⚠️ **同一「夹具选错观测面」的教训第四次重现**：依赖③用例首跑失败，因复用了无分支夹具；已改用既有 `CpgBuilderSources.ControlDependenceOverlay`。**残留边界：`MemberAccess`/`ControlFlow`/`PartitionedSyntax` 之间的前置依赖仍未确证（未找到证据 ≠ 已证伪）；`_cfg*ByNode` 的写入面覆盖整个 `ControlFlowPass`，依赖④影响面未测量。本附录给出的是依赖表，G0-P 真正要求的「每阶段静态规划时点、结果类型、配额作用域、分层」仍无设计 ⇒ G0-P 仍未关闭。** **进展（附录 P，轮次 18）：依赖表 4 → 6 条，并用反向变异矩阵给出规模与方向。** 方法：逐个把后置 pass 门控置 `false`，跑同一滤器 `~Cpg`，**干净基线 546/546 全绿** ⇒ A/B 对照成立。跳过失败数：`MemberAccess` **4**、`Interprocedural` **36**、`CallGraph` **48**。**⑤新增：`DataFlow` 消费 `MemberAccess` 产物**，并**纠正附录 O 的方向性错误**——O 说「`MemberAccess` 未测得对前序 pass 的依赖」，方向反了：它**不是消费者，而是被消费者**。**⑥新增：CallGraph→DataFlow 的第二条独立通道**——`_propertyAccessorCallSiteNodesByKey`（写 `CallGraphPass.cs:238`、读 `DataFlowPass.cs:2103`，未命中**返回 null 静默降级**，零守卫）；依赖①走 `_resolvedCallTargetsByInvocation`，**两条互不覆盖**。⚠️ **第五次「夹具选错观测面」**：依赖⑥判据最初断言 `Ref` 边非空，但 `Ref` 由 `MemberAccessPass.cs:152` 也能产出，**跳过 CallGraph 后仍通过**；改用 CallGraph **专属**产物（桥接边）后，变异 **4 条失败**（修正前该用例**完全不失败**）。**收敛规则（S3 可用）：只选该阶段专属产物；用能真正触发它的最小夹具；先变异再决定写不写测试；变异后读失败用例名而非只读失败数。** **残留边界：依赖表共 6 条仍不完整（`ControlFlow`/`PartitionedSyntax` 相对位置未确证）；本矩阵只测「跳过某阶段」，未测「阶段顺序互换」——后者才是 S3 的真正操作，且可能产生「两边都非空但语义已错」的形态，本矩阵对此**不敏感**；失败数（4/36/48）是**测试覆盖度**代理，**不是依赖强度**。G0-P 仍未关闭。** **进展（附录 Q，轮次 19-20）：互换实验补上了上述"不敏感"盲区并给出否定结论——互换\*\*是\*\*可检测的，检测器是\*\*精确数量/语义断言\*\*而非"非空"**：互换 `ControlFlow`↔`DataFlow` 失败 **19** 条（含 `Assert.Equal(expectedDataFlowEdges, ...)` 精确值 `262`、`Assert.Equal(flowNodes, incomingVisits)`、以及语义断言 `AllDefinitionsStillReachUseSite`）⇒ **P.7 边界 2 解除**。互换 `Dominance`↔`ControlDependence` 失败 **5** 条。**⚠️ 并推翻了对依赖②的表述**：互换 `DataFlow`↔`Interprocedural` **548/548 全绿**，且 `_interproceduralBarrierCompleted` 全仓**仅 2 处**（`NLCPGBuildMetrics.cs:101` 声明 + `NLCPGBuilder.cs:467` 赋值），**从不参与门控、只是诊断字段**；它真正读 `graph.EnumeratePendingEdgesLazily()`，而 `AddEdge` **不区分阶段** ⇒ **正确表述是"快照内容依赖"，不是"前置条件依赖"**。**🔴 并在 HEAD 上抓到一处真实回归**：`ControlDependence` 被排到 `Dominance` 之前（违反依赖③）⇒ 全量 `~Cpg` **5 失败/543 通过**；**已于轮次 20 修复**（两行换回 + 就地加顺序约束注释），修复后 **548/548 全绿**（`fix-cpg-full.log`）。**这是依赖表的首次闭环：隐式依赖→被回归捕获→修复→验证。** **进展（附录 R，轮次 20）：已给出 G0-P 的\*\*首个完整设计\*\*（R.1 现状盘点 / R.2 `StagePlan` 四要素契约 / R.3 四层分层 / R.4 三类窗口外依赖 / R.5 四步落地 / R.6 边界）。** 关键要点：①规划时点定义为"任何 worker 启动之前"，`Plan*` 只读、`Commit*` 唯一写图；②引入公共 `IStageWorkResult`，为**尚无结果类型的 4 个阶段**补齐（`ControlFlow`/`ControlDependence`/`MemberAccess`/`Interprocedural`）；③配额默认 `Shared`，`Dedicated` **需先实测标定**（因附录 G 已证不存在可用 bytes 公式上界）；④用 `StageDependencyTable` + **启动期拓扑断言（fail-closed）** 取代隐式书写顺序——**下次对调两行会在测试跑起来之前就失败**。**最高性价比第一步是 R-1（只加校验、不改行为）。** **⚠️ 边界：附录 R 是\*\*设计而非实现\*\*——`StagePlan`/`StageDependencyTable`/`IStageWorkResult` 均不存在，无一行代码；R-1…R-4 均未开工；④的表只覆盖已实测的 6 条，`ControlFlow`/`PartitionedSyntax`/`PartitionedOperation` 相对位置仍未确证 ⇒ G0-P 仍未关闭。** **进展（附录 S，轮次 21）：R-1 已实现并验证——阶段依赖从「靠书写顺序」变成「fail-closed 强制」。** 新增 `src/NLCPG/Builder/StageDependencyTable.cs`（依赖唯一权威表 + `TryValidateOrder` 纯函数校验）与 `StageDependencyTableTests`（9 条）；`NLCPGBuilder` 的 `RunOptionalPass` 增记**实际执行阶段**，全部 pass 后调 `EnsureStageDependencyOrder()` 校验。**关键实现判断：序列由\*\*实际执行记录\*\*而非手抄副本**——第一版硬编码期望序列，变异（对调 `Dominance`/`ControlDependence` 两行，即**真实复现附录 Q.4 回归**）后**只失败 1 条且无守卫异常**，根因是**校验与被校验对象可以一起漂移**（与 N.4/P.4 的「夹具选错观测面」同源）；改为记录实际执行后，同一变异**抛出** `InvalidOperationException : CPG builder stage dependency violated: ... ControlDependence 需要 Dominance 先完成 ... 实测后果：ControlDependencePass.cs:35 读到空的 _dominanceOverlays 后【整体静默 return】`。**⇒ 附录 Q.4 那类回归从「静默少边」变为「显式异常 + 直接说明实测后果」。** 证据：`~StageDependencyTableTests` **9/9**；全量 `~Cpg` **557/557 全绿**（轮次 20 为 548 ⇒ +9 全为新增、零回归）；源码逐字节恢复（`5742DE1CD8C1D4A9`）、无残留。**⚠️ 边界：仅 R-1 落地，R-2/R-3/R-4 均未开工 ⇒ G0-P 完成条件仍未满足、仍未关闭。** 另：校验位于**全部 pass 执行之后**（违反时图已构建但未发布），这是刻意取舍——执行前校验需要一份与书写顺序同步的副本，而那正是本轮证明会漂移的形态；`Syntax`/`Operation` 未接入（不走该重载）；本设施不阻止新增**未登记**的调用路径。** **进展（附录 T，轮次 22）：R-2 已实现并验证——4 个「无结果类型」的阶段补齐统一记账（G0-P 四要素第②项落地）。** 新增 `IStageWorkResult`（`Stage` + `ProducedNodeCount`）与三个适配器（fragment / 事实计数 / 跨过程）；`NLCPGBuilder` 增 `_stageWorkResults` 记账表 + `_recordingStage` 窗口 + `LastStageWorkResults`；`ControlFlow`/`ControlDependence`/`MemberAccess` 各自回报**本次实际产出**。**实测发现 4 个阶段分属两种形态**（不能靠猜）：`ControlFlow`/`ControlDependence` 产 `LocalCpgFragment`，而 **`MemberAccess` 产 `MemberAccessFact`（不是 fragment）**、`InterproceduralDataFlow` **不产 fragment** ⇒ 用三个适配器而非一个。**关键决定：由 pass 主动回报实际产出（归并前），而非事后重算**——重算会引入第二份"真相"，正是 N.4/P.4/S.2 反复踩到的形态。**变异（记账键错位）被 4 条判据抓住**（含专为错位/多余记录写的 `EachRecordedResultReportsItsOwnStage`、`DoesNotRecordUnrequestedStages`；若只写"各阶段产出非空"则变异**仍会全部通过**）。证据：`~StageWorkResultAccountingTests` **6/6**；针对性回归（5 个测试类）**36/36 全绿**、零回归；源码逐字节恢复（`CA0258AEE259EF57`）、无残留。**⚠️ 边界：仅 R-2 落地，R-3/R-4 未开工 ⇒ G0-P 仍未关闭。** 另：`MemberAccess` 与 `InterproceduralDataFlow` 的节点数是**规模代理**（分别为当前图节点数、全图边数），**不是**精确产出量，已在代码中标注；现有 5 个私有 record **未**接入记账表 ⇒ `LastStageWorkResults` 当前只含补齐的 4 个阶段；记账**不参与**任何执行决策，R.4 **尚未**消费它。** **进展（附录 U，轮次 23）：R-3 已实现并验证——静态规划相位真正前移到执行之前（四要素第①项落地）。** 新增 `IStagePlan<TBatch>`/`StagePlan` 与 `PlanStagesBeforeExecution` **规划相位**（在任何**后置阶段**的 worker 启动前统一算完并登记）；`ControlFlow`/`ControlDependence` 完成 `Plan*`/`Commit*` 二分，执行时**只消费**既有 plan。**过程记录：首版把"给既有两段代码起新名字"当成前移，编译与测试都过，但那没有推进 R-3**——故核心判据必须是**时序**（`PlanSnapshotAtEndOfPlanningPhase` 非空 / `WorkResultsAtEndOfPlanningPhase` 必为空），而不是 plan 的**存在性**（后者在执行相位内规划同样成立）。变异（掏空规划相位）后 **3 条失败**。证据：`~StagePlanContractTests` **5/5**；针对性回归（7 个测试类）**41/41 全绿**。**⚠️ 边界：仅 R-3 落地且只覆盖 `ControlFlow` 一个阶段；R-4 未开工 ⇒ G0-P 仍未关闭。** `ControlDependence` 依赖 `Dominance` 运行期填充的 `_dominanceOverlays` ⇒ **不能**前移；`MemberAccess`/`Dominance`/`DataFlow`/`InterproceduralDataFlow` 的 `Plan*` 尚未提取；规划相位**尚未被 R.4 消费**（只建立了能力，未据此配额度）。** **进展（附录 V，轮次 24）：R-3 覆盖面 1 → 4 个阶段，并把「时点」从注释升级为 fail-closed 机制。** 逐阶段读源码确立判据——**plan 的输入是否只来自语法/语义模型、既有缓存与无状态构造器**（不靠阶段名猜）：`CallGraph`（输入是两个快照集合）、`MemberAccess`、`Dominance` 均可前移，加上原 `ControlFlow` 共 4 个；`ControlDependence` **设计上不能**前移。`StagePlan` 增 `StagePlanTiming` + `RequiresRuntimeInputFrom`；`RecordStagePlan` 增**两条 fail-closed 校验**（声明自洽；**只有规划相位开放中登记的 plan 才能声明 `StaticBeforeExecution`**）；相位状态用**三态**而非 `bool`（`NotStarted` 与 `Closed` 对"静态前移"含义不同）。**本轮两次被自己的证据纠正：** ①首版注释写「先于任何 worker」——**不准确**，规划相位位于 `Operation` 阶段之后，而后者自己已用过 `_workBatchExecutor` ⇒ 实际保证的是「先于**后置 pass 阶段**的 worker」（`CallGraph` 等阶段的 plan 恰恰**需要** Operation 阶段的产物）；②首版 `CallGraph` 测试用无调用夹具，plan 为 `null` 会被**误判为已前移**，已改用含调用/属性访问的专用输入。**变异两条均被抓住**：不登记 `CallGraph` ⇒ 2 条失败 + 守卫抛异常；`ControlDependence` **谎报** `StaticBeforeExecution` ⇒ 3 条失败（正是附录 U 旧判据**抓不住**的「单阶段谎报」形态）。证据：`~StagePlanContractTests` **11/11**；针对性回归（5 个测试类）**99/99 全绿**。**⚠️ 边界：R-4 仍未开工且规划相位**仍未被消费** ⇒ G0-P 仍未关闭。** `DataFlow` **技术上前移可行**（`AssembleDataFlowWorkBatches`/`CreateDataFlowMethodPartitions` 对 `OperationInventory` 都是纯的）但**本轮未做**，原因是编辑边界而非技术不可能 ⇒ 这是**遗留缺口**，不得与 `ControlDependence` 的设计约束混为一谈；`PartitionedSyntax`/`PartitionedOperation` 未接入。** **进展（附录 W，轮次 25）：R-4 配额**作用域**分级落地——四要素第 ③ 项从"设计原文"变为"机制 + fail-closed 守卫"。** 关键澄清：R-4 管的是**作用域**（用哪个池）**不是大小**（多少字节）；本轮**不推导任何额度**，因为附录 G 已实测 `Estimate` 的自变量只有行号、同一行范围下真实载荷跨度 **1349×** ⇒ **不存在**可用上界，凭空造额度正是计划 :114 点名禁止的形态。落地内容：`StageQuotaScope{Shared,Dedicated}`（默认全 `Shared`）、`StageQuotaBasis{PlannedBeforeExecution,DeferredPlanning,NoBatchPlan}`、`StageQuotaCalibration`、`StageQuotaPolicy.Resolve`。**关闭了 V.6 边界 1 的"规划相位仍未被消费"**：`Resolve` 的输入**就是**规划相位快照的**不可变副本**（`PlansAtEndOfPlanningPhase`），而非仍可变的 `_stagePlans`——否则"消费了快照"与"读了个活字段"在观测上不可区分。**"缺席"的成因被分类**（这是附录 N.4/P.4 反复踩到的形态）：`ControlDependence` 是 `DeferredPlanning`（依赖 `Dominance` 运行期产物），`InterproceduralDataFlow` 是 `NoBatchPlan`（纯转发，R-3/R-4 对它 **N/A 而非待办**），二者不得合并成"暂不支持"。**变异三条均被抓住**：①配额改用空表而非快照 ⇒ 3 条失败；②停用 `Dedicated` 标定守卫 ⇒ 4 条失败；③把 `Basis` 三分支拍平 ⇒ 4 条失败；三条均**逐字节恢复**（`source-hashes-round25-prefreeze.csv` 一致，无 `if (false` 残留）。证据：`~StageQuotaScopeContractTests` **12/12**；针对性回归（5 个测试类）**78/78 全绿**。**⚠️ 边界：G0-P 仍未关闭——③ 只做到"分级 + 默认值"，未为任何阶段配额度**（W.6 边界 1/2）；`Dedicated` **被拒绝**一侧已测，"被授予后真的独占"一侧**未覆盖**（无标定即无从构造）；R-3 遗留缺口（`DataFlow` 未前移、`PartitionedSyntax`/`PartitionedOperation` 未接入）**仍在**。** **进展（附录 X，轮次 25 续）：关闭 R-3 的最后一个**具名**缺口——`DataFlow` 已前移。** 规划相位覆盖由 4 阶段增至 **5 阶段**（`CallGraph`/`ControlFlow`/`MemberAccess`/`Dominance`/`DataFlow`）。关键区分：本阶段**确有**运行期依赖（CFG 邻接缓存 `_cfgPredecessors/SuccessorsByNode`），但它在 `BuildCfgAdjacency`——即**计算**相位；规划只需**规模**（批次与行号），不需**内容**，故把邻接塞进 plan 反而是错的（那是把运行期状态偷渡进规划）。产物**逐字未变**（DataFlow 专项回归 **173/173** 为证），故本项**不**声称性能/内存改善。证据：`~StagePlanContractTests` **17/17**；针对性回归（7 个过滤器）**251/251 全绿**。**⚠️ 一条变异首次实测存活，值得单列**：让提交步**无视 plan、自行重算批次**——因调用的是同一纯函数、同一份输入，产物与"消费 plan"**逐字等价**，**197/197 全部通过** ⇒ **该差异在行为层不可观测，任何行为断言都不可能抓住它**。补法不是继续找行为断言，而是把"每阶段只规划一次"这一**既有**不变量（`RecordStagePlan` 本就对重复登记抛异常）延伸到批次构造：新增 `DataFlowPlanAssemblyCount` 留证计数 + 2 条用例（请求 ⇒ =1、未请求 ⇒ =0）。重跑该变异 ⇒ **被抓住**（计数读到 2）。**教训比附录 N.4/P.4 更进一步：前几轮是"观测面选错"，本轮是"该差异在行为层根本不存在"——此时必须换到结构/计数层。** **并自查更正一处误分类（附录 W.4 更正）：首版把 `Syntax`/`Operation` 归入 `DeferredPlanning`（"窗口前规模未知"）——错的，成因恰好相反：它们在规划相位开启**之前**就已跑完（`NLCPGBuilder.cs:374`/`:401` vs `:452`）。已新增第四成因 `ExecutedBeforePlanningPhase` + 集合 `StagesExecutedBeforePlanningPhase`，并补表驱动用例一次钉死全部 **9 个**阶段的成因。**这正是本设计自己要防的"把'没有 plan'的不同成因合并成一个"**：`DeferredPlanning` 是"时候未到"（有后续工作），`ExecutedBeforePlanningPhase` 是"时候已过"（R-3 对它不适用，无后续工作）；把后者说成前者会凭空造出一项永远不会被完成的待办。变异（拍平两成因）⇒ **2 条失败**。证据：`~StagePlanContractTests` **17/17**、`~StageQuotaScopeContractTests` **15/15**；针对性回归（7 个过滤器）**251/251 全绿**。** **进展（附录 Y，轮次 26）：把四要素第 ④ 项「分层」的 **L0 只读**从注释升级为 fail-closed 不变量。** 此前"整条规划链不写图"**只有一段注释**在声称（`NLCPGBuilder.cs` 规划相位处"两者均不写图"），属附录 N.4/P.4 的"声明代替机制"形态——该性质被破坏时唯一发现途径是"某测试恰好断言了图内容"，而多数阶段并无此类断言。落地：`NLCPGGraph` 新增**只读窗口**（`EnterReadOnlyWindow`/`ExitReadOnlyWindow`，深度计数而非 bool），守卫置于 `EnsureMutable`——它是**全部**构图入口（`AddNode`/`AddEdge`/`AddKnownNodeCartesianEdges`/`ImportMutableFacts`/`RegisterSource`）共用的唯一收口，故新入口无法绕过；规划相位以 `try/finally` 开闭窗口。**两条方向相反的变异均被抓住**：①规划相位**不进入**窗口 ⇒ **恰好 1 条**失败（`EntersReadOnlyWindowExactlyOncePerBuild`）——该**数字本身**即证明在此用例之前**没有任何其他用例**能区分"守卫被执行"与"守卫是死代码"（同附录 X.5 教训：行为层不可观测的差异只能靠计数层判别）；②**停用守卫** ⇒ 3 条失败。**并自查纠正一处守卫误挂**：`SnapshotMutableFacts`（纯读）也走该收口，首版会让"窗口内取快照"被**误报**为分层违反——而 R.3 把 L0 定义为「**只读**」，读是 L0 的职责，被拦的只应是写；已改为读路径不走该守卫并补用例钉住。证据：`~PlanningLayerReadOnlyContractTests` **8/8**；针对性回归（8 个过滤器）**258/258 全绿**（较上轮 251 ⇒ +7 全为新增、零回归）；图级回归 **71/71**；源码逐字节恢复、无 `if (false` 残留。**产物逐字未变**，故**不**声称性能/行为改善。** **⚠️ 边界：G0-P 仍未关闭——③ 仍未为任何阶段配额度**（W.6 边界 1/2）；④ 的"分层"仅完成 **L0 一处**（**L1「worker 内只产 fragment」仍未被机制强制**），"窗口外依赖"三类中仅第 1 类有机制（Y.6 边界 2/3）；`Dedicated` "被授予后真的独占"一侧**未覆盖**；R-3 仅剩 `PartitionedSyntax`/`PartitionedOperation` 未接入，且它们属**另一类改动**（需重排执行顺序，因其本就在规划相位之前跑完，X.6 边界 2）。** **进展（附录 Z，轮次 27）：把「窗口外依赖」的声明与权威表**对账**。** `StagePlan.RequiresRuntimeInputFrom` 此前是**只写不读**的（全仓仅 2 处引用，无消费者），而 `StageDependencyTable` 里**已有一条同样的登记**——两者互不校验，故存在两种漂移：①计划可**凭空点名**一个根本不是自己前置的阶段；②表改了、计划没改。这正是附录 N.4/P.4 的"同一事实两处声明、无人对账"形态。已在 `RecordStagePlan`（登记**唯一收口**）新增两条 fail-closed 对账：**来源须真是权威表登记的该阶段前置**（把"名字合法"收紧为"**关系**合法"）、**来源须在本**次**构建中真的执行过**（把声明从静态名字升级为"关于本次运行期事实的断言"）。**并自查找出一处误报**：第 2 条首版无条件判定，把"构建尚未开始⇒无执行历史"错报成"来源未执行"，已改为**仅在规划相位闭合后**判定（生产路径必然满足，故不损覆盖）。变异两条各被 1 条用例抓住。**⚠️ 并记录一次否定结果（勿误读本附录的价值）：本机制**抓不住**附录 Q.4 的重排**——实测对调 `Dominance`/`ControlDependence` 两行时，异常**全部**来自 R-1 的 `EnsureStageDependencyOrder`，本附录的对账**从未被触发**（且 `_dominanceOverlays` 为空时该阶段直接返回 null，不走登记）。**故抗重排价值为 0，那一层已由 R-1 覆盖**；本附录真正新增的是上述两种**无其他守卫覆盖**的漂移。证据：`~StagePlanContractTests` **22/22**；针对性回归（8 个过滤器）**264/264 全绿**（较上轮 259 ⇒ +5 全为新增、零回归）；源码逐字节恢复、无 `if (false` 残留。纯校验设施，**不改变**任何执行顺序与产物。** **⚠️ 边界：G0-P 仍未关闭——③ 仍未为任何阶段配额度；④ 的分层仅 L0，窗口外依赖第 2 类（跨文件）、第 3 类（`FreezeQueryIndex`/持久化须在全部提交后）**仍无机制**** **进展（附录 AB，轮次 29）：L1 计算层「worker 不写共享图」由机制强制。** 附录 R.3 四层中 L0（规划只读）已于轮次 26 变 fail-closed，但 **L1 一直只有注释**——如 `PartitionedSyntaxPass.cs:153` 写着「不创建图节点，避免 worker 线程污染共享状态」，属 N.4/P.4 的「声明代替机制」。修法：`NLCPGGraph` 增 worker 计算窗口 + `EnsureMutable` 内守卫；`CpgWorkBatchExecutor` 用 `ProcessBatchInWorkerWindow` 把**每次** `processBatch` 调用包进窗口（8 个阶段的唯一公共通道，避免逐阶段接线退化成又一处置信）；builder 把钩子接到**当前构建**的图。**两条约束是实测得出的，不是保守起见**：①窗口必须按**图实例**隔离——worker 内会新建私有 `localGraph` 并写它（ControlFlow/ControlDependence/Dominance 各一处），按"禁写任何图"做会当场打断这三个阶段；②必须按**线程**隔离——reducer 与 worker **并发**，而 reducer 正是 L2、必须能写共享图，用图级计数会让 DataFlow 合法归并写入误报（比没有守卫更糟）。**并意外实测到该缺口的真实后果**：新增用例首版用 DoP=2 让 worker 并发写同一共享图，**3 次 `AddNode` 只留 2 个节点**——构图态容器非线程安全，**丢写且不抛异常**，即静默数据损坏（用例已改单 worker）。证据：`~WorkerComputeLayerContractTests` **11/11**；针对性回归（9 过滤器）**91/91**；`~Cpg` **642/642** 且守卫消息命中 **0** 次；三项互不覆盖的变异分别杀死 4/3/4 条对应用例（守卫 / 防死代码 / 承重收口），源码三次逐字节回滚。**边界：只覆盖 L1 的"不写图"这一半**（"不共享可变中间态"未涉及）；守的是**共享图**而非全部共享状态（`_baseTypeCache` 是有意线程安全的 `ConcurrentDictionary`）。**⚠️ 边界：G0-P 仍未关闭——③ 仍未配额度（`_stageQuotaAllocation` 生产侧无消费者）；④ 分层现为 L0 ✅ + L1 ✅（仅"不写图"半），窗口外依赖第 2/3 类仍无机制。** **进展（附录 AA，轮次 28）：补上 R-1 **自身**的一处「声明代替机制」——权威表里有两条边**永远不可能失败**。** `StageDependencyTable` 声明了 `Operation ← Syntax` 与 `CallGraph ← Operation`，但 `Syntax`/`Operation` 在本 builder 中**不**经 `RunOptionalPass`（`_executedStageOrder.Add` 的**唯一**原有入口）执行，而由 `MeasureStage` 包裹 ⇒ 二者**从未**进入被校验序列；`TryValidateOrder` 又把「缺席的前置」当作「未请求⇒已满足」（该语义**本身正确**，用于"能力未开启"），于是这两条边**恒真**——**声明在，机制不在**。修法：`MeasureStage` 增 `recordedStage` 参数补记；**并把 `_executedStageOrder.Clear()` 从后置 pass 之前前移到方法开头**（否则它会在两阶段刚记录后立刻抹掉，补记等于没做——这是本改动唯一的非局部影响）。新增 `LastExecutedStageOrder` 只读观测面 + `StageExecutionRecordingTests` 4 条。**为什么必须新增观测面**：「从未记录」与「没有机制」在**行为层完全等价**（去掉补记后全部行为用例仍绿，同附录 X.5/Y.4）⇒ 判据只能落在**序列层**。证据：`~StageExecutionRecordingTests` **4/4**；变异（删去补记）⇒ **恰好 3/4 失败**——**该比例本身即判据正确性的证据**：第 4 条断言的是"真实构建不误报"，删去补记后**理应仍通过**，若它也失败说明该用例测的不是它声称的东西；针对性回归（4 个过滤器）**50/50 全绿**；源码逐字节恢复（SHA16 `0BA7F335AFB3F864`）、无 `if (false` 残留。**不改变**任何执行顺序与产物——补记前的两条边本就成立，本改动只是让它们**可被证伪**。** **⚠️ 边界：G0-P 仍未关闭——③ 仍未为任何阶段配额度（且 `_stageQuotaAllocation` 生产侧**无消费者**：仅有测试读 `LastStageQuotaAllocation`，即"消费了规划快照"成立、但"据此配额度并驱动调度"仍未成立）；④ 的分层仅 L0，**L1 未被机制强制**，窗口外依赖第 2/3 类仍无机制。** **进展（附录 AC，轮次 30）：R-4 的**声明表**有了第一个消费者——与执行期观测 fail-closed 对账。** 本轮把 AB.6 边界 4 那句"`_stageQuotaAllocation` 生产侧无消费者"拆成两个问题：①读配额表（**不解决**——配置全 `Shared`，无 `Dedicated` 可消费，凭空造额度被附录 G 与计划 :114 禁止）；②**声明表无人对账**（**本轮解决**）。②必须先于①，否则配的额度会建立在一张无人校验的表上。缺口形态：`BatchPlanCapableStages` 漏写一个阶段的后果不是"分级不准"，而是被判 `NoBatchPlan`（"它不提交批次"）⇒ 整套 R-4 治理对它**静默失效且无任何观测面**（与附录 AA"声明在，机制不在"同源）。修法：`CpgWorkBatchExecutor` 增 `stageExecutionObserved` 钩子，触发点在 `ExecuteWithCollectionModeAsync`——三个公开入口与同步/异步两路径的**唯一公共祖先**，故新增入口无法绕过；触发在**调用线程、worker 启动之前**（避免引入新并发写面，r29 刚实测过非线程安全容器的静默丢写）；builder 钩子读**当前构建**的表并调 `ReconcileObservedStageExecution`。判定取**结构层**：`NoBatchPlan` 断言"不提交批次"与"执行器正拿着它的批次"不可同时为真。映射由 `"CPG.WorkBatch." + 阶段名` **后缀反解**，**刻意不建第二张表**（两表必然漂移，漂移后对账静默失效）。三类必须放行否则误报：表为 `Empty`（规划相位之前，`Syntax`/`Operation` 正当如此）、`ExecutedBeforePlanningPhase`/`DeferredPlanning`（两者**确实**走执行器）、解析不出的 id（执行器是通用组件，测试自造 4 个 id 与 `CPG.Syntax.*` 都经过同一观察点）。**接线与判定必须分开测**：生产下声明表是对的⇒fail-closed 永不触发，只写抛出逻辑则删掉钩子与"一切正常"**行为上不可区分**（N.4/P.4）——故增只读观测面 `ObservedStageExecutions` 并断言"观测集合==声明表"（用例内先断言非空，否则两个空集合相等会让天线拔掉后仍通过）。证据：`~StageQuotaDeclarationReconciliationTests` **11/11**；`~Cpg` **655/655** 零回归；专项组 **63/63**；四条**互不覆盖**的变异分别杀死 1/1/1/6 条对应用例（接线 / 声明表 / 判定 / 放行面），源码四次逐字节回滚。⚠ 变异 1 首跑因**并发写者的半成品文件**编译失败而无法判定，已隔离重跑——那次失败不计入判据有效性。**不改变任何执行顺序与产物**（观察在 worker 启动前、只读分类、只可能抛出），故**不**声称性能或调度改善。并顺带修正两处陈旧引用：①`GrantedScope` 文档写"被拒绝时降级为 `Shared`"而代码是**抛出**（文档与代码相反）；②`DataFlowPass.cs:530` 实为 **`:618`**，且 `Syntax`/`Operation` 行号引用已漂移两次（`:374`/`:401`/`:452` → `:393`/`:421`/`:479`）⇒ 该处改为按可搜索符号引用并写明**刻意不写行号**。**⚠️ 边界：G0-P 仍未关闭——③ 仍未为任何阶段配额度；本机制只拦"不该提交却提交"，**不拦"该提交却没提交"**（反向漏报未覆盖）；`Dedicated`"被授予后真的独占"一侧仍未覆盖；④ 分层仍为 L0 ✅ + L1 ✅（仅"不写图"半），窗口外依赖第 2/3 类仍无机制。**
- [ ] G3：8 个 pass 全部完成等价与生命周期验证，DataFlow 未迁移时不得勾选。
- [ ] G2：目录、helper 真实路径完成迁移；新内存控制已经接线，再关闭 S2 和删除仍活跃的旧层。
- [ ] 单一分析调度内核与可解释的内存账本、统一日志消费成立；旧/新指标无冒名、重复计数或缺失填 0。
- [ ] 六个 YAML 字段和开关契约有实际提交验证；发生规则/helper 串行化的范围已在日志与文档明确。
- [ ] G4b：真实外部分片 adapter 的节点/边序/载荷/持久化/规则输出等价，三类大小路径都有非空验证。**状态：`todo`，无实现无测试（记录 `unified-work-scheduler-gate-g4b-shard-adapter`）；计划器与 adapter 尚不存在，故 S5 不得启用。**
- [ ] 文件失败隔离、取消、依赖阻断与 Unknown/fail-closed 有测试，未发布半图或未授权改写。
- [ ] 窗口内存与吞吐变化按相同版本实际测量，没有把估算、历史样本或微基准噪声描述为优化收益。
- [ ] 相关架构、harness 与分层验证完成并如实归档；未执行的档位仍标未验证。

## 主要风险与停止条件

| 风险 | 触发信号 | 处理与回退 |
| --- | --- | --- |
| 原准入退化解除后内存放大 | 相同输入的 retained backlog/heap/working set 越过事先声明的停止线 | 停止扩大窗口；保留旧预算直到 G0-M/G2，不能等耗尽物理内存 |
| 执行额度掩盖结果保留 | EstimatedBytes 已归零，但数组/闭包/图及 List 容量仍保留 | 按最后消费者清理；验证慢首批与连续窗口，不用 PreserveOrder=false 代替释放 |
| 深层提交或 P×DOP | rules(F) 内 ResolveUnits/helper 再提交，或 Builder 开 Parallel.For | 直连/外提，保留嵌套守卫；禁止新内核或 Task.Run 绕过 |
| 静态规划不可提前完成 | 当前阶段输出决定后续任务数，或混合 TResult/Category 无明确协议 | 按建议在外层 barrier 后规划下一份静态提交；不假定动态追加 API 存在 |
| 生产 owner 不明 | 重复 run 后任务/资源不收尾，或借用实例被提前关闭 | G0-L 明确所有权并验证；当前仅认定证据缺口，不直接宣称生产泄漏 |
| 并发与配置语义变化 | 默认日志与显式 MaxConcurrency 不一致，helper/group 发生串行化 | 记录请求、解析与实际提交，验证开关；不能用历史累计时间推导退化上限 |
| G4 假阳性 | 高 DOP 实际单 worker、分片选项未触发、新 adapter 未经过、比较前排序边 | 保留非空、实际并发和 order-only 守卫；G4a 不代替 G4b |
| 跨文件失败造成错误决策 | 缺失事实被当成“无引用”，或取消被当局部失败继续执行 | Unknown/fail-closed，阻断依赖与发布；传播取消/全局不变量失败 |
| 窗口切断目录语义 | 后续窗口所需事实被提前释放，或 rules(F) 直接执行改写 | 区分可释放大产物与跨窗口轻量事实，保持原授权/发布边界 |
| 旧层删早或反向依赖 | DataFlow/独立入口仍用 executor，NLCPG 引用 Application 计划 | 门槛前保留旧层；中立契约由 adapter 转换，架构测试核验 |
| 性能归因错误 | 只看流节点数、best-of-5 或旧累计占比即认定主要耗时/收益 | 同次分段与受控对照；调度器不自动解决候选生成的算法长尾 |
| 并发编辑污染证据 | 测试期间源码/二进制变化，或结果属于另一工作树 | 标记证据不可归因，等待稳定版本；保留用户改动，不进行整树回退 |

---

# 附录 A：G0-N 提交深度与并行边界源码盘点（2026-09-25 核实）

**性质：源码检索得到的事实盘点，不是门槛通过证据，也不改动任何代码。** 来源：2026-09-25 本会话对当前工作区（HEAD `925161d` + 未提交改动）的检索。
**命令偏差：** 本机 PATH 无 `rg`，S4-1 的三条 `rg` 命令无法直接执行；已改用等效检索（harness grep 工具与 `Select-String`），命中结果逐条人工判定。

| 位置 | 形态 | 当前是否从内核工作项可达 | 判定 |
| --- | --- | --- | --- |
| `NLCPGBuilder.cs:855` | `Parallel.For`，DOP = `EffectiveMaxDegreeOfParallelism` | **否** | 当前无 P×DOP：CPG 构建由 `BuildFromSource → Build` 在规则图**之外**完成 |
| `NLCPGBuilder.cs:1111`（`FlushParallelPublishWindow`） | `Parallel.For`，同上；只并行纯计算段②③，④写图严格串行 | **否** | 同上。**S3 一旦把 CPG pass 放进内核工作项，此二处立刻变成 P×DOP**，必须先处理 |
| `CpgWorkBatchExecutor.cs:438` | 每 worker 一个 `Task.Run`，worker 数自定 ⇒ 自成第二套 worker 池 | 经 dominance pass 从 `Build` 到达 | 当前 CPG 构建既在内核之外，又自带 worker ⇒ **「统一 P」尚未成立** |
| `BoundedConcurrencyPool.cs`（8 处 `Task.Run`） | 旧通用池自有 worker | 是（目录 `DirectoryAnalysisUseCase.cs:267`、`ParameterShrinkAnalyzer.cs:1357`） | 仍在生产路径，S2-3/S2-4 未迁移 |
| `WorkScheduler.cs:194` | `Task.Run` | — | 属内核自身实现，非违规 |
| `DecisionModel.ResolveUnits`（提交于 `:499`） | 内核提交 | **不可达** | 唯一入口 `RuleDecisionEngine.Decide`（`:401`）在 `src/` 中**无任何实例化者**；仅被 `PipelineComponentTests`、`DecisionStructureValidationTests` 触发 |
| `MarkingEngine.Run:34` / `MarkLiftingEngine.Run:103` | 内核提交 | **不可达** | `src/` 中无 `.Run(` 调用；生产走 `ExecuteRule`（工作项内纯计算，`RuleGraphAnalysisExecutor.cs:142/:184`） |
| `ParameterShrinkAnalyzer.ForEachScan:1357` | 旧池 `ForEachAsync` | **可达** | 规则体（Propagate/Propose）内扫描 ⇒ **S2-4 唯一仍真实阻塞项** |
| `RuleGraphExecutor.cs:220` | fallback 自建内核，`:237-240` `ownsScheduler` 时释放 | 独立调用/测试 | 所有权正确 |

**四条判定（与计划原文的差异逐条标出）：**

1. 当前**不存在**从内核工作项到同一内核的再提交路径。「worker 内隐藏嵌套」在**当前生产源码**中没有实例。
2. 但当前**也没有统一的 P**：CPG 构建同时具备自带 worker 池与两处 `Parallel.For`，且都在内核之外。这意味着计划「统一 P」的现状前提比原文估计的更弱——不是"P 之外还有 DOP"，而是"CPG 侧整条并行设施从未纳入 P"。
3. ⚠️ **计划的 S2-4 表格把 ResolveUnits 列为「worker 内仍在，必须改直连或外提阶段」，但在当前生产源码中它不可达。** 该表述描述的是**测试路径风险**，不是生产风险。S3 若把 Propose 规则内联进 `rules(F)`，需重新评估；但不得据此声称"生产已存在深层提交"。
4. 真正阻塞 S2-3 解锁的深层调用**只有 ForEachScan 一处**（另需 G4a，已见附录 C）。

**未覆盖（仍待办）：** 本盘点只判定"是否可达"与"是否是第二套 worker"，**没有**运行期证据证明实际并发数；G0-N 要求的「从实际目录调用逐条追到 pass 并证实无嵌套」仍需运行期断言（S3-1b 验收项）。

## A.2 补充：取消传播当前**根本不接**（2026-09-25 轮次 4 核实）

计划把「外部取消」列为 G0-L 与 S5-3 的验收项。核实结果是：**当前生产入口没有取消源可传播**。

- `CommandHost` 全文**不含** `CancellationToken` / `CancellationTokenSource` / `Console.CancelKeyPress`。
- `src/` 生产代码中**没有任何**位置给 `RoslynPrototypeExecutionOptions.CancellationToken` 赋值（该参数默认 `default`）。
- 因此 `runtime.ExecutionOptions.CancellationToken` 在生产路径上恒为 `CancellationToken.None`；CTRL+C 不会传播到内核，而是直接终止进程。
- 既有测试中出现 `CancellationToken` 的地方（如 `CancellationToken.None` 传给 Roslyn API）**不构成**取消传播证据。

**影响：** 计划的「取消路径正确收尾」在生产上**当前不可达**，故本会话**不声称**已验证取消语义（前一节的 `finally` 会经过取消路径，但该路径在生产中没有触发源）。S5-3 的取消传播设计**必须先新增一个取消源**（例如 `CommandHost` 接 `Console.CancelKeyPress` 并写入 ExecutionOptions），否则其验收项无法成立。这是**新增能力**，不是修复已有缺陷。

---

# 附录 B：G0-L run 生命周期（2026-09-25 已实施并验证）

**性质（更新后）：所有权接线已实施，并以契约测试 + 变异检查取证。** 下方保留实施前的盘点结论作为对照。

## B.1 实施前盘点（源码事实，已由 B.2 关闭）

| 归属 | 创建点 | 释放点（实施前） | 判定 |
| --- | --- | --- | --- |
| **生产自建** | `ExecutionRuntime.cs:94`（`scheduler == null`） | **无** | `AnalysisRuntime` 不实现 `IDisposable`；`CommandHost.AnalyzeCoreAsync`（`:52` 创建 runtime）在成功/异常/取消路径**均未释放** |
| 派生 | `InvalidateCaches` / `NextEpoch` 把 `Scheduler` 原样传入新 runtime | 无 | **共享正确**（不重复起 worker）；`src/` 内无调用者 |
| 注入 | 构造函数 `:100`；测试经该参数注入 | 归注入方 | 执行器只传递不持有 ⇒ 不误释放，**正确** |
| fallback | `RuleGraphExecutor.cs:220` | `:237-240`（`ownsScheduler` 时 `DisposeAsync`） | **正确**，实施前 `src/` 中唯一显式释放点 |

## B.2 已实施的改动（2026-09-25）

以「自建者释放、借用者不释放」为唯一判据，改动三处：

1. **`AnalysisRuntime`（`src/NLISSN.Application/ExecutionRuntime.cs`）新增所有权状态。**
   - 私有构造新增 `ownsScheduler` 形参，写入只读字段 `_ownsScheduler`。
   - 公开构造传入 `ownsScheduler: scheduler is null`——即**仅自建时为 true**。
   - `InvalidateCaches()` / `NextEpoch()` 显式传 `ownsScheduler: false`：派生实例共享同一内核，**提前释放会让原 runtime 的内核在后续阶段不可用**。
   - 新增 `public bool OwnsScheduler` 与 `public ValueTask DisposeSchedulerAsync()`；后者在**非所有者**时是空操作。
2. **`CommandHost.AnalyzeCoreAsync` 接入 `finally`**（`src/NLISSN/Hosting/CommandHost.cs`）。
   - 成功、首次异常、外部取消三条路径全部经过该 `finally`；`DisposeAsync` 的语义是**排空**而非取消，故异常路径上调用不改变原异常传播。
3. **刻意不实现 `IAsyncDisposable`。** 直接引入 `await using var runtime` 会改变既有的释放顺序与异常语义（运行日志与性能摘要在 `finally` 之后才收尾）。改为由调用方在收尾后显式调用 `DisposeSchedulerAsync()`，接入面更小。这是**设计选择，不是遗漏**。

## B.3 验证证据（2026-09-25）

| 判据 | 结果 |
| --- | --- |
| `~AnalysisRuntimeSchedulerTests` | **19/19 通过**（原 11 条 + 新增 8 条 G0-L 用例） |
| `~Concurrency`（契约） | **61/61 通过**，EXIT=0 |
| **变异检查（所有权）** | 临时去掉 `if (!_ownsScheduler) return;` 后，两条借用方保护用例**失败并抛 `ObjectDisposedException`**（`..._WhenSchedulerIsInjected_LeavesInjectedSchedulerUsable`、`..._WhenDerivedRuntimeIsDisposed_KeepsSharedSchedulerUsable`），其余 16 条仍通过 ⇒ 该守卫是**承重**的，测试非空转。恢复后 SHA256 与改动后完全一致（`AEB836C2A73CBD76…`），无残留 |
| 关闭竞争 / 排空语义 | `DisposeSchedulerAsync_WhenWorkIsInFlight_DrainsInsteadOfCancelling`：在途工作期间发起释放，断言释放**未提前完成**、在途工作**未被取消**且最终执行完 |
| 重复释放 | `..._WhenCalledTwice_IsIdempotent` 通过 |
| 借用/派生不受影响 | 释放 runtime 后注入内核与共享内核仍可正常调度（见上） |
| Host 分析路径 | `~CommandHostCompositionTests|~DirectorySchedulerBoundaryTests|~WithRuntimeLog|~ExposesEffectiveLimits|~WorkspaceConfigurationTests` **13/13 通过** |
| **Host 全量 + 基线 A/B** | 接线版与「临时摘除接线」版**逐条相同：7 失败 / 670 通过 / 677 总计**，失败用例名逐一一致，且均无 `ObjectDisposedException`/调度器签名 ⇒ 这 7 条是**既有失败**，非本改动引入 |
| 分层 | Contract 全量 **538 通过 / 1 失败**；唯一失败是既有 `LayoutArchitectureTests.ProductionProjectReferences_...`（只读 csproj，与本改动无关） |

原始日志：`Build/g0l-lifecycle/`（`contract-analysisruntime.log`、`mutation-ownership.log`、`host-analyze-path.log`、`host-full.log`、`host-full-baseline-AB.log`、`contract-full-retry.log`、`contract-final.log`、`source-hashes-after.csv`）。

## B.4 轮次 4 补充：库内调用方也接入释放（2026-09-25）

B.2 只接了 `CommandHost`（CLI 入口）。库内还有**两个自建 runtime 的公开重载**，它们此前同样不释放：

| 位置 | 处理 |
| --- | --- |
| `ApplicationService.Analyze(source, filePath, settings)` | 改为显式建 runtime + `try/finally` 释放（内部创建者负责释放） |
| `ApplicationService.Analyze(source, filePath, settings, semanticModel, root)` | 同上。**该重载正是 `tests/NLISSN.Testing/AnalysisLegacyOptionsTestExtensions.cs:40` 实际走的路径**，覆盖面广 |
| `AnalysisSession` 的 `runtime ?? CreateDefault()` 兜底 | **不改**：生产仅有一处 `new AnalysisSession(`，且显式传 `runtime`（`ApplicationService.cs:364`），兜底只在测试中走到 |

**验证：** `~ApplicationServiceLifecycleTests` **3/3 通过**（新增，Host 项目）；Host 全量 **7 失败 / 673 通过 / 680 总计**——失败仍是那 7 条既有失败，**零新增**，通过数正好 +3。

### ⚠️ 一条被变异检查否证的做法（必须记录，防止后人重犯）

本轮先写了一个「重复调用后 `ThreadPool.ThreadCount` 不得异常增长」的用例，首版**通过**。
但变异检查（摘掉释放接线）**该用例仍然通过** ⇒ 它**无判别力**。实测原因：

`WorkScheduler.WorkerLoopAsync` 在 `await gate.Task` 上让出，内核 worker 是**异步**的，
众多逻辑 worker 由约 17 个物理线程服务。实测**释放与不释放都是 `ThreadPool.ThreadCount = 17 → 17`（delta=0，cpu=16）**。

⇒ **线程计数不能用来判别内核释放**。该用例已改为只断言「释放之后结果仍可读、重复调用与五参重载仍正确」，
并在测试文件里**写明这条判别力边界**；真正的释放接线由 `AnalysisRuntimeSchedulerTests` 的
`DisposeSchedulerAsync_*` 一组（含变异检查）覆盖。

## B.5 仍未关闭的部分（不得越读）

- **外部取消在生产中不可达**：`CommandHost` 不含任何 `CancellationToken` 源，`src/` 中也没有任何位置给
  `RoslynPrototypeExecutionOptions.CancellationToken` 赋值 ⇒ 该 token 恒为 `None`。
  故本会话**不声称已验证取消语义**；`finally` 形式上覆盖取消路径，但生产里没有触发源。详见附录 A.2。
- **无重复 run 的线程/句柄级证据**（见 B.4 的否证结论）。
- **未测量 worker 线程数**，不宣称"重复 run 不再增长线程数"。
- `DirectoryAnalysisUseCase` 与其他库内入口的自建 runtime 边界，待 S3 一并明确。

---

# 附录 C：G4a 门槛复验（2026-09-25 本会话）

**性质：真实命令执行结果。** 轮次 23 记录的 dominance 竞态在当前树上**不再复现**。

- 命令：`dotnet test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:BuildInParallel=false --filter 'FullyQualifiedName~DocumentShardingEquivalenceTests|FullyQualifiedName~DominanceRaceReachabilityProbe'`，经 `Build/Tools/Invoke-SerialDotnet.ps1`。
- 结果：**3/3 次均 14 通过 / 0 失败 / 0 跳过，EXIT=0**（24 s / 23 s / 25 s）。原始日志 `Build/g4a-round1/g4a-filter{,-rep2,-rep3}.log`。
- **可重复性是判别判据**：轮次 23 记录该竞态为不确定（同参数 3 次运行败 2 次）；单次绿不足以取代它，3/3 绿才是。
- 修复体：`NLCPGBuilder._cacheGate`（`:101`），4 个 worker 可达文件共 16 处加锁。
- 反串行化护栏为绿，故竞态未靠改回串行掩盖（`DominanceRaceReachabilityProbe.cs:113-121` 断言 `distinctWorkers>1` 且 `peakActive>1`）。
- **归因干净**：运行窗口（02:48:10–02:50:47）内 `src/NLCPG/**` **零文件被改动**；并发写入者的 `DataFlowPass.cs` mtime 02:23:48，早于首次运行 24 分钟且全程稳定。哈希见 `Build/g4a-round1/`。
- 详见 `Context/feature_list.json`：`unified-work-scheduler-gate-g4-sharding-equivalence` 已于 2026-09-25 按用户决定改为 **G4a** 并标 `completed`；新记录 `unified-work-scheduler-gate-g4b-shard-adapter` 为 `todo`、无证据。

**边界：** 本附录只支撑计划的 **G4a**（既有方法分批与 streaming 基线）。计划的 **G4b**（真实 `DocumentShardPlanner → adapter → NLCPG`）**无实现、无测试**，该计划器与 adapter 尚不存在，故 G4b 未通过，S5 不得启用。

## 本次审查交付边界

报告已将已核实源码、历史证据、设计建议和未验证门槛分开；移除已知不可行的复制模板及自动提交步骤。完成本文编辑不表示任何优化已执行。本次只修改本报告，不更改产品代码、测试、配置、feature 状态或另外两份长尾/计划对象内存设计。

---

# 附录 D：G0-M 内核字节额度——两处缺陷已修 + 估算校准仍未完成（2026-09-25 轮次 5）

**性质：真实命令执行结果 + 变异检查。** 本附录关闭 **G0-M 的「非法项策略」与「溢出策略」**两项，**不**关闭「估算校准」与「窗口上界」。

## D.1 缺陷一：在途字节比较会整数溢出，从而**错误放行**超额度项

内核原写法（`WorkScheduler.cs:356`）：

```csharp
if (submission.InFlightBytes + bytes <= submission.MaxInFlightBytes)
```

`InFlightBytes` 接近 `long.MaxValue` 时该加法**回绕为负**，比较恒真 ⇒ 超出额度的项被放行。

- **同仓库的既有 legacy 层一直是溢出安全写法**：`CpgWorkBatchExecutor.cs:701` 用 `_queuedBytes <= _maxBytes - batch.EstimatedBytes`。内核塌缩时丢掉了这个性质。
- **实证（先红后绿）**：新增 `RunAsync_WhenInFlightPlusEstimateOverflowsInt64_StillHonoursTheBudget`，在旧写法下**失败**为 `expected the budget to serialize the two items, peak concurrency was 2`（额度 `long.MaxValue` 被首项占满后，第二项 100 字节本应串行）。
- **修复**：改为 `submission.InFlightBytes <= submission.MaxInFlightBytes - bytes`（此处 `MaxInFlightBytes > 0`、`bytes >= 0` 已由前置检查与 D.2 保证，故不溢出）。
- **变异检查**：把修复改回溢出写法，**恰好该 1 条失败**（`peak concurrency was 2`），其余 5 条仍通过 ⇒ 该用例承重。已按原样恢复。

## D.2 缺陷二：非法估算（负值）未被拒绝，会污染在途记账

`Submission.Create` 原本只校验重复 `StableOrder`、缺失依赖与环（`WorkScheduler.cs:610-645`），**不校验** `EstimatedBytes` / `EstimatedCost` 的符号。负的 `EstimatedBytes` 会让归还时 `InFlightBytes -= 负数` **反向推高**计数。

- **修复**：在提交前诊断性拒绝负值（与既有的重复 `StableOrder`/缺失依赖同类，属提交前校验）。
- **实证**：新增 `RunAsync_WhenEstimatedBytesIsNegative_FailsBeforeExecutingAnything` 与 `RunAsync_WhenEstimatedCostIsNegative_FailsBeforeExecutingAnything`，均断言抛 `ArgumentOutOfRangeException` 且**执行计数为 0**（证明是提交前拒绝，不是执行中失败）。
- **变异检查**：短路掉负 `EstimatedBytes` 守卫后，**恰好该 1 条失败**，其余 5 条仍通过 ⇒ 承重。已按原样恢复。

## D.3 本轮验证

| 检查 | 结果 |
| --- | --- |
| `~WorkSchedulerByteBudgetTests` | **7/7 通过**（原 4 条 + 新 3 条） |
| `~Concurrency` | **64/64 通过**（原 61 + 新 3） |
| Contract 全量 | **542 通过 / 1 失败 / 543**；唯一失败是既有 `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`（csproj 引用，输出无调度器/预算签名） |
| Unit / Host | 见 `Context/feature_list.json` |

## D.4 **仍未完成**的部分（不得越读）

- **估算校准未做。** `EstimatedBytes = EstimatedCost × 64`（`CpgWorkBatchBuilder.cs:7,226`），而主 pass 的 `EstimatedCost` 是**方法体行数**（`CpgWorkBatchCostModel.Estimate:85-87`，`endLine - startLine + 1`）；`DataFlowPass.cs:515` 与 `PartitionedOperationPass.cs:428` 均传入行跨度。这正是本计划第 114 行点名**不可靠**的「行数乘常量」。设计文档 **R6**（`2026-09-24-unified-work-scheduler-design.md:676`）早已要求「M1 初版只做观测与告警，额度校准后再生效」。**本轮未做校准**，故不得声称额度是可靠的堆上界。
- **`MaxInFlightBytes` 仍无生产调用方**：`src/` 中唯一赋值处是内核自身（`WorkScheduler.cs:451`），赋值来源只有测试（`WorkSchedulerByteBudgetTests.cs`）。故本轮的修复**当前不影响任何生产路径**，属「内核契约正确性」而非「生产内存行为改变」。
- **其他项**（`CallGraphPass.cs:64`、`ControlDependencePass.cs:47` 的 `estimatedCost: 1` 常量）仍是常量估算，未校准。
- 窗口上界、retained backlog 停止线、「第一个批很慢、后续批很快」反例、连续窗口阶梯增长：**均未验证**。
- `CpgWorkBatchBuilder.EstimateBytes` 自身用 `checked(...)`（`:226`），故单 batch 的字节乘法会抛 `OverflowException` 而非静默回绕；这是既有行为，本轮未改动。

---

# 附录 E：配置额度 → 准入配额的溢出（轮次 6，2026-09-25）

**性质：真实命令执行结果，先红后绿。** 关闭 G0-M 中「非法/溢出项策略」在**配置边界**上的另一半（附录 D 关的是内核侧的字节比较与估算符号）。

## E.1 缺陷：合法配置值在构造期抛裸 `OverflowException`

`RoslynPrototypeExecutionOptions.EffectiveMaxConcurrentOperations` 只做 `Math.Max(1, ...)`（**无上界**），而 `AnalysisRuntime` 构造时把它交给 `CreateConcurrencyAdmissionOptions`：

```csharp
var maximumParallelism = executionOptions.EffectiveMaxConcurrentOperations;
return new ConcurrencyAdmissionOptions(
  MaxConcurrentOperations: maximumParallelism,
  MaxReservedItemCount: checked(maximumParallelism * 2),   // ← 溢出点
  ...
```

`maxConcurrentOperations` 是两个**用户可配**字段之一：

- YAML 校验（`YamlConfigurationLoader.cs:365-368`，诊断码 `NLISSN117`）只要求 `> 0`；
- JSON Schema（`Miscellaneous/schemas/nlissn.schema.2.json`）只声明 `"minimum": 1`。

两者都**没有上界**。因此 `2000000000` 这样的值**通过全部校验**，却在构造 runtime 时以裸 `OverflowException` 失败 —— 用户看到的是「配置合法 + 与配置无关的溢出类型」，无法诊断。

**实证（先红）：** 新增 `ExecutionQuotaBoundaryTests.AnalysisRuntime_WhenMaxConcurrentOperationsOverflowsTheAdmissionReservation_FailsDiagnosably`，在修复前失败为 `Assert.IsType() Failure: ... Actual: typeof(System.OverflowException)`。

**修复：** 显式检测溢出并抛**可诊断**的 `ArgumentOutOfRangeException`（带字段名与实际值），与其余配额字段的失败形态一致。保留 `checked` 语义（溢出仍被检测），只是把异常类型与消息改成调用方可理解的。

**实证（后绿）：** 修复后同过滤器 **2/2 通过**（`quota-final.log`）。

**变异检查（已补做）：** 把修复改回 `checked(maximumParallelism * 2)` 后，**恰好该 1 条判别用例失败**（`Assert.IsType() Failure: ... Actual: typeof(System.OverflowException)`），同过滤器另一条边界内用例仍通过 ⇒ 判别成立。已按原样恢复（`ExecutionRuntime.cs:312` 保留溢出守卫，无变异标记残留）。

> 该变异检查一度因**并发写入者**正在编辑 `src/NLCPG/Builder/Passes/DataFlowPass.cs`（本会话明令不得触碰）而阻塞：其未完成状态令 `NLCPG.csproj` 依次报 `CS0051` / `CS0122`（后又在 `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexConstructionReuseTests.cs` 报 `CS0117`），**ContractTests 一度整体无法编译**。已按 30 s 间隔轮询直至写入者稳定后补做完成。故障源自并发写入者中间态，与本改动无关。

## E.2 本轮同时**删除**了一条我自己写的无判别力用例

新增的 `ConcurrencyAdmissionOptions_WhenReservedItemCountOverflows_IsNotConstructibleWithHugeValues` 只是**枚举若干常量后 `Assert.Equal` 回读**，不驱动任何产品代码分支 ⇒ 无判别力。这与我轮次 4 记录并否证的做法同类，故**删除**，不留在仓库里充数。

## E.3 **边界与未完成**

- `EffectiveMaxConcurrentOperations` 仍**无上界**：本轮只把溢出变成可诊断错误，**没有**定义合理的上限策略（例如按物理内存/核数设界）。这是**未完成**项。
  > **轮次 7 已关闭此项**：见附录 F——六个额度的上界策略已实现并验证，`EffectiveMaxConcurrentOperations` 亦受该上界约束。
- 本修复只在**构造期**生效；未验证真实 CLI 以极大 `maxConcurrentOperations` 运行时的端到端报文形态。
- 其余 G0-M 项（估算校准、窗口上界、retained backlog 停止线）状态不变，见附录 D.4。

---

# 附录 F：G0-M「超大项策略」——六个额度的 worker 上界（2026-09-25 轮次 7）

**性质：真实命令执行结果 + 变异检查。** 本附录关闭 G0-M 的**「超大项策略」在配置面上的落点**，即附录 E.3 列为未完成的那条「没有定义合理的上限策略」。**不**关闭「估算校准」「窗口上界」「retained backlog 停止线」。

## F.1 缺陷：worker 数由**六个无上界**字段的最大值推导

`WorkSchedulerOptions.WorkerCount` 取六个额度字段的最大值（设计 §7 的 `P = max(...)`），而 `WorkScheduler.EnsureWorkersStarted`（`WorkScheduler.cs:192-195`）在**首次提交**时按该值逐个 `Task.Run`：

```csharp
for (var index = 0; index < _options.WorkerCount; index++)
{
    _workers.Add(Task.Run(WorkerLoopAsync));
}
```

六个字段在 `WorkSchedulerOptions` 中**都不校验**；YAML（`YamlConfigurationLoader.cs:340-368`，诊断码 `NLISSN110/111/114/115/116/117`）与 JSON Schema 都只要求 `>= 1`。因此一个**通过全部校验的合法配置值**就能让首次提交排队上亿个长期任务。

**这是附录 E 那条守卫覆盖不到的缺口：** 轮次 6 的 `checked(maximumParallelism * 2)` 只检查 `maxConcurrentOperations` **一个**字段的**溢出**，既不检查其余五个字段，也不检查 worker 数量。例如只把 `cpgMaxDegreeOfParallelism` 设为 `1000000`（不触碰 `maxConcurrentOperations`）可完全绕过它，而 `WorkerCount` 仍等于一百万。

**同仓库已有封顶先例：** `ProjectExportOptions.cs:72` 用 `Math.Min(12, Math.Max(1, MaxDegreeOfParallelism))` 把导出 worker 封顶 12，`docs/cli-reference.md` 与 `Miscellaneous/schemas/README.md` 均写明「实际上限为 12」。内核此前缺这一层。

## F.2 修复：显式上界 + 可诊断拒绝（**不是**静默截断）

- 新增 `WorkSchedulerOptions.MaximumWorkerCount = 1024` 与 `Validate()`：任一字段超出即以 `ArgumentOutOfRangeException` 点名**具体字段与实际值**。
- `WorkScheduler` 构造函数调用 `options.Validate()`，使非法配置在**构造期**立即失败，而不是在首次提交时耗尽进程。
- **刻意不截断**：`WorkerCount` 仍等于六者最大值。静默 `Math.Min` 会改变用户配置意图却不告知；显式拒绝更诚实。取 1024 是因为 CPU 密集型工作的 DOP 超过核心数本无收益，故不会拒绝任何合理配置。
- 上界只拒绝**非法配置**，不改写**类别额度**本身（`ResolveLimit` 不变），以免违反 R4「日志必须打印实际生效的类别上限」。

## F.3 实证

- 新增 `WorkSchedulerWorkerBoundTests`（5 条）+ `ExecutionQuotaBoundaryTests` 增 1 条（`AnalysisRuntime_WhenMaxConcurrentOperationsExceedsTheWorkerCeiling_FailsDiagnosably`）。
- **先红后绿：** 首次运行时 `WorkScheduler_WhenALimitExceedsTheCeiling_FailsDiagnosablyBeforeStartingAnyWorker` 失败，暴露**我自己测试断言过严**——产品消息点名字段（`DirectoryLimit`）而非常量名。已把断言改为校验 `ParamName` + 实际值 + 上界值，**产品代码未因此改动**（行为本就正确）。
- **后绿：** `~WorkSchedulerWorkerBoundTests|~ExecutionQuotaBoundaryTests` **8/8 通过**（`workerbound-green.log`）；`~Concurrency` **72/72**（轮次 5 为 64，+8 全部为本轮新增，**零回归**，`concurrency-r7.log`）。
- **变异检查：** 注释掉 `WorkScheduler` 构造函数中的 `options.Validate();` 后，**恰好 2 条判别用例失败**——内核层 1 条与 `AnalysisRuntime` 集成层 1 条——其余 6 条边界/不截断用例仍通过 ⇒ 判别成立且两层都承重。已按原样恢复，恢复后文件与变异前备份 **SHA256 完全一致**（`B365FB0BF471B343`），全仓库无变异标记残留。

## F.4 **边界与未完成**

- 本轮把附录 E.3 的「无上界」从「只让溢出可诊断」推进到「有明确上界并拒绝」，但**仍未**验证真实 CLI 以超大 `cpgMaxDegreeOfParallelism` 运行时的端到端报文形态。
- 1024 是**工程判断**，非实测标定：未测量内核在 1024 worker 下的实际内存/调度开销，也未证明该值对本次目标（13.86 GiB 大文件）足够或过多。
- `RunAsync_WithALargeButSupportedLimit_CompletesInsteadOfQueuingMillionsOfTasks` 只断言「能完成」，**不**断言物理线程数或吞吐——内核 worker 在 `await gate.Task` 上让出，线程计数无法判别 worker 数量（轮次 4 已否证）。
- 其余 G0-M 项（**估算校准**——`EstimatedBytes = 行数×64`，计划 :114 点名不可靠；窗口上界；retained backlog 停止线）状态不变，见附录 D.4。

---

# 附录 G：G0-M「估算校准」——实测结论：**不存在**可用的 bytes 上界（2026-09-25 轮次 8）

**性质：真实命令执行结果（实测结构宽度 + 真实打包器）+ 两次变异检查。** 本附录实现了计划 :114 要求的「相同夹具的实测校准」，并给出一个**否定性**结论：现行估算公式**在原理上无法**改造成 bytes 上界。**不**关闭「窗口上界」「retained backlog 停止线」。

## G.1 现状盘点：仓库里**每一个**"字节数"都是公式，没有任何实测

在 `src/` 全量检索：`GC.GetTotalMemory` / `GC.GetAllocatedBytesForCurrentThread` / `GC.GetTotalAllocatedBytes` / `Unsafe.SizeOf` / `Marshal.SizeOf` **命中数全部为 0**。即生产代码**从不测量**任何真实内存，所有 bytes 数值都是常量乘法：

| 数值 | 出处 | 公式 |
| --- | --- | --- |
| `EstimatedBytes` | `CpgWorkBatchBuilder.cs:7,226` | `EstimatedCost × 64`，而 `EstimatedCost` = **方法体行数** |
| `FragmentBytes` | `ControlFlowPass.cs:104`、`DominancePass.cs:466` | `节点数 × 64 + 边数 × 48` |
| `FragmentBytes` | `ControlDependencePass.cs:150` | 同上（未加 `checked`） |
| `FragmentBytes` | `DataFlowPass.cs:507` | 同上（按 partition 求和） |

**关键陷阱：遥测同时输出 `EstimatedBytes` 与 `FragmentBytes`，看起来像「估算 vs 实测」，实则两者都是公式，且自变量不同**（行数 vs 节点/边数）。二者相除**不是**校准误差。

## G.2 实测：硬编码常量是**低估**

用本仓库既有手法（`Unsafe.SizeOf<T>()`，先例见 `LocalCpgFragmentContractTests.cs:430-431`）在同一进程实测结构宽度：

| 结构 | 实测宽度 | 公式假定 | 结论 |
| --- | --- | --- | --- |
| `StableNodeAnchor` | **28 B** | — | — |
| `CpgNodeDescriptor` | **92 B** | 64 B | **低估 1.44×** |
| `CpgEdgeCandidate` | **120 B** | 48 B | **低估 2.5×** |

同一夹具（1000 节点 / 4000 边）：生产公式 `256,000 B`，实测宽度载荷 `572,000 B`，**比值 2.234×**。且 `CpgEdgeCandidate` 含**引用字段**（`NLCPGEdgeLabel`、`string FilePath`、`string DisplayName`），真实堆占用还更高 —— 故这 2.234× 仍是**下限**。

## G.3 否定性结论：**不存在**任何常量 k 使「行数 × k」成为上界

`CpgWorkBatchCostModel.Estimate(startLine, endLine, options)`（`:78-95`）的自变量**只有行号**。因此行范围相同的两个片段得到**完全相同**的 `EstimatedBytes`，而真实载荷可以差几个数量级。实测（同一行范围 `0..999`）：

```text
same line span 0..999 -> identical cost=1000
sparsePayload = 2,120 B
densePayload  = 2,860,000 B
spread        = 1,349.1x
```

要覆盖 dense 需 `k = 2860 B/行`，而同一个 k 会把 sparse **高估 1349×**。**任何单一常量都无法同时成立** —— 这不是「常量调得不够准」，而是该函数**缺少必要自变量**（节点/边数量），属结构性缺陷。计划 :114 的判断（「源码字符数、行数乘常量不是可靠 bytes 上界」）由此获得**实测确证**。

## G.4 附带发现：默认配置下字节额度**根本不起作用**

生产默认有**两条**批次上限（`CpgWorkBatchBuilder.cs:104-144`）：

1. 成本上限 `TargetBatchCost = 400`（`CpgWorkBatchCostOptions.Default`）
2. 字节上限 `WorkBatchMaxEstimatedBytesPerBatch = 1 MiB`（`NLCPGBuilderOptions.cs:41`，经 `NLCPGBuilder.cs:164` 进入生产）

实测（500 个 cost=200 的方法，真实打包器）：

```text
byteLimit=1048576; costLimit=400; maxObservedCost=400; maxObservedBytes=25600;
costBoundBytes=25600; batches=250
```

`maxObservedBytes = 25,600` 仅为字节上限的 **2.4%**，且恰好等于 `400 × 64`。**结论：成本上限先触发，字节额度在默认配置下恒为松弛**，因此 `G0-M` 不能把「批次字节有界」当作独立的存活内存门槛。若未来调低字节上限或调高 `TargetBatchCost` 使二者可比，需重新评估哪条上限在起作用（已固化为断言）。

## G.5 实证与变异检查

新增 `CpgFragmentByteCalibrationTests`（**7 条**，`calibration-bound.log` 7/7）：

- 结构宽度断言 ×2（低估即失败）、公式-实测比值 ×1、行数映射非单射 ×1、遥测两量不可相除 ×1、常量漂移守卫 ×1、字节额度松弛 ×1。
- **变异检查 A：** 把 `EstimatedBytesPerCostUnit` 由 64 改为 128 ⇒ **恰好 2 条**依赖该常量的用例失败（漂移守卫 + 遥测关系），4 条结构宽度用例仍通过。
- **变异检查 B：** 把 `WorkBatchMaxEstimatedBytesPerBatch` 由 1 MiB 改为 8 KiB ⇒ **恰好 1 条**判别用例失败（松弛性），其余 6 条通过。
- 两次均恢复原样，SHA256 与变异前备份**逐字节一致**（`74E8DC549539F530` / `1CC1D2724F0870BD`），无残留标记。

> **⚠️ 本轮踩到并记录的构建陷阱（真实验证事故，非产品缺陷）：** 第一次变异检查用 `Copy-Item` 恢复文件，而 `Copy-Item` 会**保留备份的旧时间戳**，导致 MSBuild 判定「源比产物旧」而**跳过重新编译**，测试于是跑在**变异后的二进制**上并报出假失败（`Expected: 64000, Actual: 128000`）。已定位（源 mtime 09/21 早于 DLL 09/25）并改为写回内容后**重新触碰时间戳**，复跑 7/7 通过。**教训：变异检查的恢复必须确保产物重建，不能只看源码文本已还原。**

> **⚠️ 变异检查暴露的另一件事（有效发现）：** 第一次变异我改的是 `CpgWorkBatchBuilder.cs:6` 的私有常量 `DefaultMaxEstimatedBytesPerBatch`，结果**测试全绿**。追查后确认这不是测试无判别力，而是该常量**在生产中不可达**：唯一生产调用方 `NLCPGBuilder.cs:161-164` **显式**传入第三个实参 `_options.EffectiveWorkBatchMaxEstimatedBytesPerBatch`，故构造参数的默认值分支永不执行；而所有测试也都不省略该实参。**结论：`CpgWorkBatchBuilder.cs:5-6` 两个私有默认常量与 `NLCPGBuilderOptions.cs:40-41` 的同名默认值是重复定义，前者在现有调用图下为死代码。** 已记录，未删除（删除属清理改动，需与 S4 删除清单一并评估）。

## G.6 **边界与未完成**

- 本轮**没有修改任何生产估算公式**。原因是 G.3 已证明「调常量」在原理上不能得到上界；正确修法是给估算补上节点/边自变量或引入真实测量，属**设计变更**，超出本轮「校准」范围，需与设计 R6 一并决策。
- `Unsafe.SizeOf<T>()` 是**载荷结构宽度**，不含 GC 对象头/对齐/装箱、数组头、`List` 保留容量、字符串驻留与图结构开销。故本附录**不**声称任何数值是峰值内存上界。
- 未测量真实大文件（13.86 GiB 目标）上的上述关系是否仍成立；仅在合成夹具上验证。
- 未关闭：**窗口上界**、**retained backlog 停止线**、连续窗口阶梯增长、「首个批很慢」反例，见附录 D.4。

---

# 附录 H：G0-N「提交深度恒为 1」的**跨实例**盲区（2026-09-25 轮次 9）

**性质：真实命令执行结果，先红后绿 + 变异检查。** 本附录补上 G0-N 长期记为「运行期断言仍缺」中的**一条**：嵌套守卫此前只识别**同一实例**，计划 :118 明文禁止的「worker 内新建第二个 scheduler」可完全绕过。**不**关闭 G0-N 的其余部分（真实目录调用链的运行期覆盖、`Parallel.For` 的 P×DOP）。

## H.1 缺陷：守卫用 `ReferenceEquals`，只看同一个实例

`WorkScheduler.RunWithMetricsAsync` 原本是：

```csharp
if (ReferenceEquals(CurrentScheduler.Value, this))
{
    throw new InvalidOperationException("... same scheduler ...");
}
```

而计划 :118 要求：

> 禁止 worker 内创建第二个 scheduler、Task.Run 包装或同步等待来绕开同实例嵌套守卫。

**两者之间的缺口是真实的：** 在 worker 内 `new WorkScheduler(...)` 再 `RunAsync`，`CurrentScheduler.Value` 是**外层**实例，与内层实例不相等，守卫放行。后果正是 G0-N 要防的「统一 P 不成立」——内存中同时存在**两份独立的 P**（内层自带一套长期 worker）；若外层 worker 被该同步等待占满，还会退化为死锁。

**先红实证：** 新增 `WorkSchedulerCrossInstanceNestingTests.RunAsync_WhenAWorkItemCreatesASecondScheduler_FailsDiagnosablyInsteadOfMultiplyingP`，修复前失败为 **`Assert.Throws() Failure: No exception was thrown`** —— 即该形态**完全未被拦截**（`crossinstance-probe.log`）。

## H.2 修复：识别「任意已处在工作项内」的情形

改为读取 `CurrentScheduler.Value`，非空即拒，并对**同实例/异实例**给出**不同消息**（异实例消息明确点出「两份 P」与「应在 run owner 上提交」）。这保持了原有对同实例的诊断质量，同时堵住跨实例旁路。

## H.3 实证

- **后绿：** `~WorkSchedulerCrossInstanceNestingTests|~WorkSchedulerNestingTests` **6/6**（3 新增 + 3 既有，`crossinstance-green.log`）。
- **`~Concurrency` 75/75**（轮次 7 为 72，+3 全为本轮新增，**零回归**，`concurrency-r9.log`）。
- **Host 全量 7 失败 / 673 通过 / 680**，失败名单与历轮基线**逐字一致**（`host-full-r9.log`）⇒ 放宽守卫**未**在生产路径上产生新失败。
- **变异检查：** 把守卫改回 `ReferenceEquals(CurrentScheduler.Value, this) ? ... : null` 后，**恰好该 1 条**跨实例用例失败，其余 5 条（含 3 条既有同实例契约）仍通过 ⇒ 判别成立。已按原样恢复，与变异前备份 **SHA256 逐字节一致**（`AC9139E0C8C2587B`），无残留标记。

## H.4 为什么放宽守卫**不会**误伤生产：调用图已核对

放宽守卫的风险是误伤「合法地使用第二个内核」的代码。核对结论：**`RuleGraphExecutor` 的兜底内核只在测试中出现**。生产侧 4 处调用（`RuleGraphAnalysisExecutor.cs:51`、`MarkingEngine.cs:34`、`DecisionModel.cs:455`、`MarkLiftingEngine.cs:103`）**全部显式传入** `session.Runtime.Scheduler`，故不触发 `_injectedScheduler ?? new WorkScheduler(...)` 兜底分支；`new RuleGraphExecutor()`（无参）在 `src/` 中**零命中**，仅存在于 `RuleGraphCompilerTests` / `RuleStructureContractTests` 等测试。

其余生产提交点均为**顶层**、不在工作项内：`RewritePlanReplayService.cs:35`（回放，`CommandHost` 直接调用）与 `DecisionModel.cs:499`。Host 全量基线不变从经验上也印证了这一点。

## H.5 **边界与未完成**

> **⚠️ 轮次 9 此处原有的说法已被轮次 10 实测推翻，见 H.6。** 原文写「`Task.Run` 包装与同步等待未加守卫也未有测试」，那是**阅读推断而非实测**，且**是错的**。

- 本轮**只**补了「跨实例旁路」这一条（该部分是实测有效的，见 H.1-H.4）。
- **仍未**从真实目录调用链跑到 pass/rules/ResolveUnits/ForEachScan 并断言提交深度恒为 1（G0-N 的主体要求）。
- 「统一 P」**仍未成立**：`NLCPGBuilder.cs:1123`/`:1449` 两处 `Parallel.For` 与 `CpgWorkBatchExecutor.cs:503` 的 `Task.Run` 都在内核之外，P×DOP 问题未解。
- 本守卫是**同进程内**的 `AsyncLocal` 判定；跨进程搬运不在其覆盖范围。

## H.6 轮次 10 更正：H.5 的「Task.Run 与同步等待未加守卫」是**错的**

**性质：自己写下的边界结论被自己的实测推翻。** 轮次 9 的 H.5 与 feature_list 都写了「`Task.Run` 包装与同步等待未加守卫、也无测试」。轮次 10 复核时发现：这句话**没有跑过任何验证**，是**阅读推断**。而它很可能是错的——`AsyncLocal<T>` 默认随 `ExecutionContext` **流入** `Task.Run`。

**实测（最小隔离探针，精确复制守卫机制：`AsyncLocal` 置位/复位 + 入口读值非空即抛）：**

| 形态 | 轮次 9 的说法 | 实测结果 |
| --- | --- | --- |
| 直接再次提交（同实例） | 已拦 | **已拦** `InvalidOperationException` |
| `Task.Run` 包装 | 「未加守卫」 | **已拦**——`AsyncLocal` 随 `ExecutionContext` 流入 |
| 同步等待（同线程 `GetResult`） | 「未加守卫」 | **已拦**——同一线程，`AsyncLocal` 必然可见 |
| `AsyncLocal` 在 `Task.Run` 内的取值（对照） | 未评估 | **流入**（非 null） |
| `ExecutionContext.SuppressFlow()` + `Task.Run` | 未考虑 | **未拦**（`completed with 1`）——真实缺口 |
| `SuppressFlow` 后 `AsyncLocal` 取值（对照） | 未评估 | **不流入**（null） |

**结论修正：** 计划 :118 并列列举的三种绕开方式中，**`Task.Run` 包装与同步等待本来就已被守卫覆盖**（因为守卫的载体是 `AsyncLocal`，而它的流向恰好覆盖这两者）。轮次 9 关于它们的「未加守卫」说法**作废**。

**新发现的真实缺口：`ExecutionContext.SuppressFlow()`。** 它阻止 `ExecutionContext` 流入新线程，守卫读到 null 即放行，内层提交得以执行。**该缺口不能用「全局在途计数」之类手段修补**——那会误伤「两个互不嵌套的内核合法并行」这一被 `WorkSchedulerCrossInstanceNestingTests` 明确保护的形态。生产侧 `src/` 中 `SuppressFlow` **零命中**（已 grep 确认），故这是**记录在案的边界**，不是生产风险。

**证据与固化：** `WorkSchedulerNestingBypassTests` 4 例——两条**正向契约**（`Task.Run` 包装、同步等待均被拒且不死锁）、一条**缺口固定用例**（`SuppressFlow` 绕过确实生效；若将来被堵上，该用例会失败并强迫更新此处记录，避免边界悄悄漂移）、一条**反例保护**（两个互不嵌套内核跨线程并行不得受阻）。

**方法论教训（对后续轮次有效）：** 边界结论**必须实测**，不能靠读代码推断「应该没覆盖」。本轮之所以能发现，正是因为对被自己写进 feature_list 的断言做了反向复核——**写进权威状态的句子同样需要证据**。

---

# 附录 I：G0-N 在**目录并行分支**上的运行期断言（2026-09-25 轮次 11）

**性质：补上 G0-N 「运行期断言仍缺」的主体一步——从真实目录调用链进入，覆盖此前从未被执行的并行分支。**

## I.1 发现的测试盲区：既有断言只覆盖了**串行**分支

`DirectorySchedulerBoundaryTests` 原有的 `Analyze_InvokedInsideKernelWorkItem_IsRejectedAsNestedSubmission` 只传**一个**源文件。而 `DirectoryAnalysisUseCase.cs:253-255` 有：

```csharp
if (!runtime.ExecutionOptions.EnableDirectoryParallelism ||
    runtime.ExecutionOptions.EffectiveDirectoryMaxDegreeOfParallelism == 1 ||
    sources.Count <= 1)
{
    return sources.Select(...).ToList();   // 串行分支
}
```

`sources.Count <= 1` 成立 ⇒ 该测试**始终走串行分支**，`SelectOrderedAsync` 的**并行目录路径**（`DirectoryAnalysisUseCase.cs:267-285`）**一次都没被执行过**。即：此前记录的「嵌套守卫在真实目录链路上有效」只对串行分支成立。

## I.2 为什么并行分支更值得固定（真实风险而非形式完备）

并行分支经 `runtime.ConcurrencyPool.SelectOrderedAsync` 在**线程池线程**上执行每个文件的 `AnalyzeFile`，而每文件内部又向同一个内核提交规则图。若该池内部压制了 `ExecutionContext` 流转，`AsyncLocal` 守卫就会**失效**——这正是附录 H.6 记录的 `SuppressFlow` 边界在生产路径上的可能投影。因此新测试同时是**对该风险的运行期探测**。

## I.3 实测结果：守卫生效，且该池**未**压制 ExecutionContext

新增 `Analyze_WithDirectoryParallelismInsideKernelWorkItem_IsRejectedAsNestedSubmission`：**3 个源文件** + `DirectoryMaxDegreeOfParallelism: 3`（必然 >1，确保进入并行分支），把目录级分析放进内核工作项，断言其被嵌套守卫拒绝；带 30 秒超时保护（若守卫在并行分支失效，内层提交可能排队等待，不能让套件无限挂起）。

**结果 3/3 通过**（`dirbound-r11.log`）⇒ **并行分支同样被守卫拦截**，且 `SelectOrderedAsync` **没有**压制 `ExecutionContext`。这是 H.6 所担心的生产投影**不成立**的实证。

## I.4 变异检查：证明新测试**不是**空转

把守卫整体停用（`if (false && activeScheduler is not null)`）后重跑：

| 用例 | 变异后 |
| --- | --- |
| `Analyze_InvokedInsideKernelWorkItem_...`（串行） | **失败**（3 s） |
| `Analyze_WithDirectoryParallelismInsideKernelWorkItem_...`（并行，新增） | **失败**（407 ms） |
| 第 3 条 | 通过 |

失败原因为 `Assert.IsType() Failure: Value is null / Expected: typeof(InvalidOperationException) / Actual: null`——即**守卫停用后确实没有抛出**，两条用例都具备判别力。已按原样恢复，与备份 **SHA256 逐字节一致**（`AC9139E0C8C2587B`），无残留。

**顺带的事实：** 守卫停用时并行分支是**正常完成**（407 ms）而非死锁——说明该形态即便绕过守卫也不会立刻挂起，因此**只靠"没挂起"无法发现守卫失效**，必须有显式断言。这佐证了新测试的必要性。

## I.5 回归证据

- `~DirectorySchedulerBoundaryTests` **3/3**。
- **Host 全量 7 失败 / 674 通过 / 681**，失败集合与轮次 9 基线做**逐用例名 A/B 比对**为 `IDENTICAL`（`host-full-r11.log`）⇒ 新增断言未引入任何新失败（+1 总量即本用例）。

## I.6 **边界与未完成**

- 本轮覆盖的是**目录层**进入内核的真实链路。G0-N 主体要求中更深的 pass/rules/`ResolveUnits`/`ForEachScan` 仍未逐层断言提交深度。
- 「统一 P」**仍未成立**：**本轮已实证** `DirectoryAnalysisUseCase.cs:267-285` 的 `SelectOrderedAsync`（目录度 P）确实在**并发执行**多个 `AnalyzeFile`，而每文件内部各自做 CPG 构建并带自己的 DOP ⇒ **P×DOP 重叠是真实存在的**（此前只是静态推断，本轮读到确证代码）。`NLCPGBuilder.cs:1123`/`:1449` 的 `Parallel.For` 与 `CpgWorkBatchExecutor.cs:503` 的 `Task.Run` 仍在核外。
- 本测试证明的是「**被拒**」这一契约，**不**证明「若不拒会怎样」；后者未评估。

---

# 附录 J：G0-N **运行期设施盘点**——把「核外并行」从静态推断变成实测（2026-09-25 轮次 12）

**性质：附录 A 此前对「哪些层在核外并行」只有静态源码盘点。本附录给出运行期实测，并如实标出该夹具**未**覆盖到的部分。**

## J.1 方法：用公开契约判定「是否身处内核工作项内」

新增 `DeepParallelFacilityInventoryTests`。做法是把 `BoundedConcurrencyPool` 用装饰器包起来（`IConcurrencyPool` 的**全部 8 个成员**都转发并记录），在每个设施回调内部做一次嵌套探测：

> 尝试 `scheduler.RunAsync(一个平凡提交)`。内核的「提交深度恒为 1」守卫会对**任何位于工作项内的提交**抛出含 `"flat"` 的 `InvalidOperationException`，因此**「会不会被拒」就是「是否身处内核工作项内」的可观测判据**。

这样**不反射私有状态**，只用公开契约。探测带超时（未被拒时最多等 10 s），避免异常情形挂住套件。

## J.2 实测结果（`deep-facility-inventory.txt`，由测试自身落盘）

```
inventory: SelectOrderedAsync=1
totalObservations: 1
nestedObservations: 0
kernelSubmissions: 4
kernelCategories: default=1, rule-group=3
```

三条结论：

1. **`nestedObservations: 0`** —— 真实目录管线中**没有任何**层在内核工作项内部调用旧并发设施。这是「无隐藏嵌套/无额外内核」在**该路径上**的运行期确认（此前只是静态推断）。
2. **核外并行确实存在**：`SelectOrderedAsync=1` 即目录层 `DirectoryAnalysisUseCase.cs:267` 的那一次调用，它在**核外**并发执行多个 `AnalyzeFile`。
3. **内核确实被使用**：4 次提交（`default=1` + `rule-group=3`），说明内核路径被覆盖，盘点不是空转。

由于 `SelectOrderedAsync` 在核外并发、而每个 `AnalyzeFile` 内部各自做 CPG 构建并带自己的 DOP，**P×DOP 重叠在本轮得到运行期确证**（此前只有源码推断）。

## J.3 ⚠️ 该夹具**未**覆盖 `ForEachScan`——必须明说

盘点是 `SelectOrderedAsync=1`，**`ForEachAsync` 计数为 0**。即：

> **`ForEachScan`（`ParameterShrinkAnalyzer.cs:1357`，其落点就是 `ForEachAsync`）在当前夹具下根本没有被调用。**

计划附录 A 把 `ForEachScan` 记为「深层调用的真实阻塞只剩 ForEachScan」，而本次实测表明：**用「3 个原子表达式源文件」这种最小目录夹具走不到它**——参数收缩规则需要**索引器/委托/可选参数**等特定语法形态才会触发 `TryCollectElementAccessRewrites` 等路径。因此：

- 本附录**不**声称覆盖了 `ForEachScan`；它在核内还是核外**仍未实测**。
- 这一点之所以值得写下来，是因为它正是轮次 10 的教训的应用：**没被观测到 ≠ 不存在**。若我把「`nestedObservations: 0`」表述为「已证明无隐藏嵌套」，那就是把**夹具覆盖不足**当成了**性质成立**。

## J.4 防止本盘点退化为空测试

测试内建两条反向约束，避免它将来悄悄变成永远通过的空转：

- `Assert.True(pool.TotalObservations > 0, ...)` —— 若全部设施都迁入内核、旧池不再被调用，本测试**失败**并迫使更新盘点结论。
- `Assert.NotEmpty(workTelemetry.Records)` —— 若内核路径没被走到，本测试**失败**。

两条都带完整诊断信息（含实测 inventory 字符串），失败时直接给出当时的盘点，而不是一句「断言失败」。

## J.5 回归证据

- `~DeepParallelFacilityInventoryTests` **1/1**（`inventory-r12.log`）。
- Host 全量 **7 失败 / 675 通过 / 682**，失败集合与轮次 9 基线做**逐用例名 A/B 比对为 `IDENTICAL`**（`host-full-r12.log`；+1 总量即本用例）。

## J.6 **边界与未完成**

- **`ForEachScan` 未覆盖**（见 J.3），故 G0-N 主体要求**未完成**：既没有覆盖 pass/rules/`ResolveUnits` 的逐层深度断言，也没有覆盖 `ForEachScan`。
- 盘点只覆盖**一个**目录夹具（3 个原子源、目标名 `s`）。不同规则集/语法形态下的设施调用集合**可能不同**，本轮未做多夹具矩阵。
- 本轮**未改产品代码**；只新增一个观测型测试。
- 探测判据依赖内核守卫；若守卫被停用，本测试的嵌套探测会**失效**（会一律判为「未嵌套」）。这一点与附录 I.4 的变异结果一致，属已知限制。

---

# 附录 K：补上附录 J 承认的缺口——`ForEachScan` 首次被**运行期**覆盖（2026-09-25 轮次 13）

**性质：轮次 12 明确写下「该夹具未覆盖 `ForEachScan`，其在核内还是核外仍未实测」。本附录把这个缺口补上，而不是留在边界里。**

## K.1 为什么轮次 12 走不到它，以及怎么走到

`ForEachScan` 的**并行分支**要求 `scans.Count > 1`（`ParameterShrinkAnalyzer.cs:1335-1337`：`scans.Count <= 1` 走串行）。而它只被**参数收缩的索引器/委托链路**调用（`:812`、`:855`）。轮次 12 的最小目录夹具是 3 个原子表达式源，不含索引器 ⇒ 永远进不去。

本轮改为**直接以索引器为入口**驱动 `ParameterShrinkAnalyzer.TryBuildIndexerPlan`，并构造**三个语法树**（`PlayerInput` 声明 / 带双参索引器的 `Buffer` / 含访问点的 `Game`），使 `scans.Count == 3` 从而落地并行分支。

- 入口约定：传入的是**索引器参数的 `TypeSyntax`**（`TryResolveIndexerParameter` 由它向上解析回索引器），不是 `IndexerDeclarationSyntax`。取第二个参数 `input` 的类型节点——该参数在声明与调用点**都被使用**，`requireCallsites` 才满足。

## K.2 实测结果（`Build/g0m-calibration/foreachscan-nesting.txt`，由测试自身落盘）

```
inventory: ForEachAsync=1
forEachAsyncMaxDegrees: 2
nestedObservations: 0
kernelSubmissions: 1
```

四条都有明确含义：

1. **`ForEachAsync=1`** —— `ForEachScan` **确实被走到**（轮次 12 这里是 0）。
2. **`forEachAsyncMaxDegrees: 2`** —— 走的**是并行分支**，即 `:1357` 那个 `GetAwaiter().GetResult()` **同步阻塞**调用真的执行了。这排除了「测试因走串行分支而空转」。
3. **`nestedObservations: 0`** —— 该同步阻塞调用**不在内核工作项内部**。⇒ 附录 A 把 `ForEachScan` 记为「深层调用的真实阻塞只剩它」是**准确的**；它目前在**核外**，尚未构成隐藏嵌套。
4. **`kernelSubmissions: 1`** —— 且这 1 次**就是我自己的探测提交**（从核外发出，故成功）。测试用「非探测提交数必须为 0」表达这一点（按探测特有的 `MaxConcurrency=2` 区分）。

**`nestedObservations: 0` 与 `kernelSubmissions: 1` 是自洽的**：探测从**工作项之外**提交 ⇒ 守卫不拒 ⇒ 记为「未嵌套」，同时留下 1 条遥测。若探测曾被拒，则 `nestedObservations` 非空而该记录不会出现。两个数字必须一起看，单独看任一个都会误读。

## K.3 变异检查：证明本测试**能判别**（不是恒为 0 的空转）

把 `:1357` 的旧池阻塞调用**替换为向内核提交**（`WorkSubmission<bool>` + `.GetAwaiter().GetResult()`）——精确模拟 S2-4 注释所**禁止**的「规则体内再提交」形态。

**结果：测试失败**，且失败在**正确的断言**上：

```
未观测到 ForEachAsync 调用，说明走的是串行分支（scans.Count <= 1）；设施盘点：
```

⇒ 该断言确实在判别「本路径用的是旧池还是内核」，而不是恒真。已按原样恢复，与备份 **SHA256 逐字节一致**（`E75C0687A5C534E3`），无残留。

**一个必须记下的过程事实：** 本变异做了 4 次才跑起来——前 3 次分别因「锚点文本 CRLF/LF 不匹配」「按行号插入插到了**参数列表**里（CS1519）」「`WorkItem<int>` 的委托返回 `Task.CompletedTask`（CS1662/CS0266）」而**编译失败**。关键纪律：**编译失败 ≠ 变异生效**——前 3 次覆盖率为零，若把它们当成「测试通过/失败」都会得出错误结论。

## K.4 回归证据

- `~ParameterShrinkScanNestingTests` **1/1**（`foreachscan-r13b.log`）。
- Host 全量 **7 失败 / 676 通过 / 683**，失败集合与轮次 9 基线**及**轮次 12 均逐用例名比对为 `IDENTICAL`（`host-full-r13.log`；+1 总量即本用例）。
- **未改产品代码**：`WorkScheduler.cs` `AC9139E0C8C2587B`、`ParameterShrinkAnalyzer.cs` `E75C0687A5C534E3`、`ExecutionRuntime.cs` `D9F7BEA851D96C60` 均与轮次 12 相同（`source-hashes-round13.csv`）。

## K.5 **边界与未完成**

- 本轮证明了 `ForEachScan` **在核外**且**其同步阻塞不构成嵌套**。但**未**证明「若把它迁进内核会怎样」——恰恰相反，变异显示那样做会让它**不再走旧池**（本测试即失败）。
- 仍然**没有**覆盖：其余 pass、`ResolveUnits`、以及规则体内其他潜在核外并行点。G0-N 主体要求**仍未完成**。
- 本轮仍未触及「统一 P」：`NLCPGBuilder.cs:1123`/`:1449` 的 `Parallel.For` 与 `CpgWorkBatchExecutor.cs:503` 的 `Task.Run` 依旧在核外。
- 测试用的是**自建最小 `ISemanticRuleContext`** 与**自建 compilation**，而非完整管线；因此它证明的是该**代码路径**的性质，不等于「完整目录分析中一定如此」。

---

# 附录 L：`ResolveUnits` 一层的可行性声明——从注释变成回归可测事实（2026-09-25 轮次 14）

**性质：把 `DecisionModel.cs:486` 的一句注释（可行性声明）变成受测事实；并**记录一次自己写错测试**的完整经过。**

## L.1 被检验的声明

`DecisionModel.cs:486` 写道：

> conflictDomains 的解析发生在规则图执行完成之后（非工作项内部），可安全提交给内核。

紧接着 `:499` 就调用 `session.Runtime.Scheduler.RunAsync(...)`。**这是一句"位置前提"**：若冲突域解析改在**规则体内**执行（S3 内联后正是这种风险），`:499` 的提交就会落进内核工作项内部，被守卫拒绝。

## L.2 结论与证据

**已实测：该声明当前成立**，且内核守卫会**确实**拒绝违规形态。

变异实验：在 `:499` 的 `RunAsync` **之前**插入一次外层内核提交，把冲突域解析**包进工作项内部**（精确模拟 S3 内联可能带来的形态）。结果：

```
System.InvalidOperationException : WorkScheduler.RunAsync was called from inside a work item
of the same scheduler. Submissions must be flat: express dependent stages as items of a single
submission with WorkItem.Dependencies instead of nesting RunAsync calls.
```

⇒ ①守卫按设计拒绝；②`:486` 的前提是**承重的**（不是可有可无的注释）；③若将来 S3 把该解析内联进规则体，**会被立刻发现**而不是静默出错。已按原样恢复、**SHA256 逐字节一致**（`DE388EB55D9C2434`）、无残留。

**固定手段：** 增强既有用例 `RuleDecisionEngine_Decide_SchedulesOnlyConflictResolution`（**hermetic**）：`Decide` 正常返回本身即证明该提交**未被守卫拒绝**；追加 `Assert.Equal(0, ...WasCanceled)`。**没有**修改产品代码。

## L.3 ⚠️ 必须记录的自我纠错：我最初的测试写错了，而且它是**非 hermetic** 的

第一版我新建 `DecisionResolveUnitsBoundaryTests`，用**读 `src/` 源码文本**的方式断言「`ResolveUnits` 没有生产调用点」。它**单独跑通过**，但在**全量套件中失败**：

```
ResolveUnits 出现了新的生产调用点，需重新评估其提交是否仍在核外：NLISSN.Application\Analysis\RuleGraphAnalysisExecutor.cs
```

**根因：** 该断言耦合了**磁盘上的实时源码文本**，而我的**变异作业正并发地改同一个文件**。于是它读到了"被接回生产"的假象，报出 `productionCallersOfResolveUnits: 1`。**这是一次自制的假失败，不是产品问题。**

**教训（方法论层面，比本层结论更重要）：**

1. **读源码文本做架构断言不是 hermetic 的**——任何并发构建、变异、同伴编辑都会让它随机红/绿。断言应当走**公开行为**（跑一次、看遥测），而不是 grep 磁盘。
2. **「单独通过、全量失败」是并发耦合的典型信号**——不要先归因环境或同伴，先查**自己的测试是否依赖了可变外部状态**。
3. 该测试还与既有用例 `RuleDecisionEngine_Decide_SchedulesOnlyConflictResolution` **重复**（后者本就断言了「最后一次 RuleGroup 提交 = 2 项」）。**已删除**该文件，改为在既有用例上增强，避免重复脚手架（`ParallelTypeMarkRule`/`DeclarationDecisionRule` 都是 `PipelineComponentTests` 的**私有嵌套类**，外部无法复用——这也是重复的诱因）。

## L.4 本层的一个结构性事实

`ResolveUnits` 只被 `RuleDecisionEngine.Decide`（`:468`）调用，而 `Decide` 在 `src/` 中**没有生产调用点**（生产路径走 `RuleGraphAnalysisExecutor`，它自行调用 `session.CreateProposeContext()` 并**不**经 `Decide`）。⇒ 该冲突域提交目前**不在实时生产路径上**。

**注意表述边界：** 这只是**静态**结论（本轮变异中已被肉眼观察到并发干扰），**不**作为断言固化——正因 L.3。它说明的是"当前没有调用点"，**不**等于"没有风险"：一旦接回，L.2 的守卫就是唯一的防线。

## L.5 回归证据

- `~RuleDecisionEngine_Decide_SchedulesOnlyConflictResolution` **1/1**（`decide-boundary-r14.log`）。
- Host 全量 **7 失败 / 676 通过 / 683**，失败集合与轮次 9 基线逐用例名比对为 **IDENTICAL**（`host-full-r14b.log`）。
- **一次中间态已记录：** 修正前的全量跑出 **8 失败 / 677 通过 / 685**，多出的那一条**正是我那个非 hermetic 的新用例**（`host-full-r14.log`）。删除后回到 7，与基线一致。**这个中间态如实保留**，因为它是 L.3 结论的证据。
- 产品源码：仅 `DecisionModel.cs` 被变异后**原样恢复**（`DE388EB55D9C2434`），`WorkScheduler.cs` 仍为 `AC9139E0C8C2587B`。

## L.6 **边界与未完成**

- 本附录**只**覆盖 `ResolveUnits` 一层的**位置前提**。G0-N 主体要求**仍未完成**：其余 pass 与规则体内其他核外并行点未覆盖。
- **未**证明「S3 内联后该解析是否仍安全」——只证明了「若不安全，守卫会拒绝」。**修复方式是 S3 的设计问题，不是本轮范围。**
- 「统一 P」仍未成立（`NLCPGBuilder.cs:1123`/`:1449` 的 `Parallel.For`、`CpgWorkBatchExecutor.cs:503` 的 `Task.Run` 仍在核外）。
- 本层结论来自**测试路径**（`Decide` 经测试驱动）；尽管产品路径不走 `Decide`，两者共用同一内核与守卫契约。

---

# 附录 M：G0-L「重复运行句柄级证据」——以及一个**被变异证明为无判别力**的断言（2026-09-25 轮次 15）

**性质：本轮的主要产出不是"补上了缺的证据"，而是**发现我补的第一版证据是假的**，并换成真能判别的版本。**

## M.1 目标

G0-L 长期记为缺「**重复运行**句柄级证据」：连续多轮「创建 runtime → 提交 → 释放」之后，长期 worker 是否真正结束、而不是逐轮累积。

## M.2 ❌ 第一版：线程数断言，**经变异证明无判别力**

我用「每轮释放后采样进程线程数，末轮相对首轮的增长必须 < 轮数×worker 数」作判据。干净运行时：

```
threadCountPerRound: 33,33,33,33,33
delta(first,last): 0
```

看起来非常干净。**但我做了隔离变异**——把 `WorkScheduler.DisposeAsync` 里的 `await Task.WhenAll(_workers)` 删掉，**保留** `_disposeRequested` 置位：

```
### mutation-2 applied (workers NOT awaited; submissions still rejected) ###
已通过! - 失败: 0，通过: 1     ← 仍然通过！
threadCountPerRound: 33,33,33,33,33    ← 与干净运行完全相同
```

⇒ **该断言零判别力。** 根因（读源码确认）：`WorkScheduler.cs:244` 的 worker 循环在 `_disposeRequested` 置位且无在途提交时**自行 return**——worker 的终止**不依赖**是否被 await；那个 await 只是**同步确认**。故只要置位发生，线程终将自行退出，线程数**因构造而无法判别**。

**已把这一结论写进该用例的 XML 注释**（标明它不是泄漏证据、不得读作「已验证无泄漏」），并**降低其地位**为「重复运行不累积」的上界描述。

## M.3 ❌ 第二版：仍无判别力——因为**没有在途工作**

改断言「返回时 worker 已停止」后，**变异下仍然通过**。原因：该版只提交**已被 await 完成**的工作再释放 ⇒ 释放时**根本没有在途工作可等** ⇒ 删掉 await 也照样通过。

**这正是"边界断言不是证据"的又一次实例**：我以为写了"更强的断言"，实际写的是一条**空转**断言。

## M.4 ✅ 第三版：提交**在途**工作，变异下**确实失败**

最终版本：提交一件被显式闸门（`TaskCompletionSource`）**阻塞住**的工作 → 在途期间发起释放 → 断言 `Assert.False(disposing.IsCompleted)`（释放必须等待，不得提前返回）→ 放闸 → 断言在途工作全部完成、且返回后提交被拒。

**变异验证（同一 mutation-3：删掉 `await Task.WhenAll(_workers)`）：**

```
失败 ...DisposeSchedulerAsync_WhenItReturns_WorkersHaveAlreadyStopped [93 ms]
Expected: False
Actual:   True
```

⇒ **判别力成立**。已按原样恢复、`WorkScheduler.cs` **SHA256 逐字节一致**（`AC9139E0C8C2587B`）、无残留。

**同时确认既有用例更强：** 既有 `DisposeSchedulerAsync_WhenWorkIsInFlight_DrainsInsteadOfCancelling` 在同一变异下**也失败**（`Expected: False`）⇒ 该语义此前**已有**判别力覆盖；本轮的净增量是**重复运行**维度 + **一次被证伪的自我纠正**。

## M.5 本轮最值得记下的方法论

> **一个断言在干净运行时"通过"，完全不能说明它有判别力。**
> 本轮两条断言在干净运行时都给出**完美读数**（`33,33,33,33,33`），却**都无判别力**。
> **只有隔离变异能区分"通过"与"能判别"。** 这与轮次 10/13 的教训同源，但更尖锐：
> 轮次 13 的变异是"证明能判别"，本轮是"证明**不能**判别"——**后者更容易被漏掉**，
> 因为它的表现是**绿灯**，而不是红灯。

## M.6 关于"取消"的静态事实（明确不声称已验证）

G0-L 主体曾含「真实取消」一项，长期记为**受阻：生产无取消源**。本轮复核确认该判断：

- `RoslynPrototypeExecutionOptions.CancellationToken`（`ExecutionRuntime.cs:20`）在生产中**始终为 default/None**：`src/` 中**没有**任何把非默认 token 传入它的位置。
- `src/` 中的 `CancellationTokenSource.Cancel()` 调用**全部**是**内部失败短路**或**超时**语义（如 `CpgWorkBatchExecutor.cs:489/580` 的流水线提前终止、`ExternalDiagnosticAttachment.cs:310` 的 `CancelAfter`、`RuntimeMeasurementLog.cs:162` 的采样停止），**没有**一处是「用户/宿主发起取消并因此中断分析」。

⇒ **本轮不声称取消语义已验证**；该子项**仍然受阻**，且受阻原因**具体且已复核**（不是"没时间"）。若将来引入取消源，需要的是**新的**验收项，而不是本轮结论的延伸。

## M.7 回归证据与归因

- `~AnalysisRuntimeSchedulerTests` **21/21**（`repeated-run-r15c.log`）。
- Contract 全量 **5 失败 / 758 通过 / 763**（`contract-full-r15.log`）。新增 4 条失败**全部**是
  `DataFlowAdjacencyCompactionTests.BuildFromSource_CandidatePublicationOrder_IsIndependentOfDegreeOfParallelism(dop: 1/2/4/8)`，
  属**同伴**在改的表面（`DataFlowAdjacencyCompactionTests.cs` mtime 17:03、`InterproceduralDataFlowPlan.cs` mtime 17:31）；
  **归因证据：我这三个文件对 `DataFlow|Adjacency|Interprocedural` 的引用数为 0**。第 5 条是既有 `LayoutArchitectureTests`。
- 本轮**未改产品代码**：`WorkScheduler.cs` `AC9139E0C8C2587B`、`ExecutionRuntime.cs` `D9F7BEA851D96C60` 均与轮次 14 相同。

## M.8 **边界与未完成**

- **取消语义仍未验证**（见 M.6），受阻原因已复核。
- 第一版线程数断言**保留但已降级**并明确标注无判别力；**不**删除是因为「重复运行不累积」的**上界**描述仍有价值，但**必须**带着 M.2 的警告一起读。
- 线程数受并发测试影响，**不是**精确量；即使将来要重用它，也只能发现**量级级**泄漏。
- 「统一 P」仍未成立；G0-N 其余 pass 仍未覆盖。

---

# 附录 N：G0-P 首个设计输入——**阶段依赖是隐式的**，以及三次被证伪的断言（2026-09-25 轮次 16）

**性质：G0-P 此前**没有任何设计**（计划 :571 是空条目）。本附录给出它的**事实基础**，并记录我在同一件事上**连续三次写错断言**的完整经过。**

## N.1 为什么 S3 会踩到这个坑（这是 G0-P 的核心风险）

`NLCPGBuilder.cs:411-417` 顺序驱动 7 个后置 pass：

```
CallGraph → MemberAccess → ControlFlow → DataFlow → InterproceduralDataFlow → Dominance → ControlDependence
```

而这 7 行的**第一个参数**是 `RunOptionalPass(buildPlan.Requires*, ...)`，其中
`Requires*`（`:583-589`）**全部来自 capability 位**：

```csharp
RequiresDataFlow: (resolved & NLCPGCapability.DataFlow) != 0,
```

⇒ **`Requires*` 只回答「这个能力被请求了吗」，完全不回答「这个阶段依赖谁」。**
真实的阶段依赖是**隐式**的，**只由那 7 行的书写顺序保证**。

**S3-1 恰恰要求重排**（拆成计划/计算/归并、由窗口协调器重新组织提交）。一旦重排，
**没有任何编译期或运行期机制会拦住你**——这是 G0-P 存在的理由。

## N.2 已确证的隐式依赖（源码）

| # | 依赖 | 载体 | 是否有守卫 |
| --- | --- | --- | --- |
| 1 | `DataFlow` 需要 `CallGraph` 已缓存调用目标 | `DataFlowPass.cs:1934-1937` | 有 `throw`，但**实测不可达**（见 N.4） |
| 2 | `InterproceduralDataFlow` 需要 `CallGraph`+`DataFlow` 的 reducer 都完成 | `NLCPGBuilder.cs:1041-1044` 注释 + `_interproceduralBarrierCompleted` | **完全没有** |

**依赖 2 是纯注释约定**：`:1041` 写着「CallGraphPass and DataFlowPass have completed their reducers
before this stage」，但**没有任何代码强制它**。

## N.3 新增契约测试（`BuilderStageDependencyContractTests`，3/3 通过）

判据用**可观察产物**而非源码文本（hermetic）：请求 `InterproceduralDataFlow` 时，
`NLCPGEdgeKind.InterproceduralDataFlow` 桥接边**非空**且**桥接种类 ≥ 2**。

## N.4 ⚠️ 三次被自己的变异实验证伪——过程必须记录

**这是本轮的主要教训，比结论本身更重要。**

**❌ 第一次：断言「依赖 1 被运行期强制」。** 依据是 `DataFlowPass.cs:1936` 会抛
`InvalidOperationException("Data-flow requires CallGraphPass to cache resolved call targets.")`。
**变异（把 `CallGraphPass` 门控改为 `RunOptionalPass(false, ...)`）后 3 条测试全部仍通过**
⇒ 该异常在正常构建路径上**不可达**。**我的断言建立在"读到了一行 throw"之上，而不是"观测到它真的会抛"。**

**❌ 第二次：改断言 `DataFlow` 边非空。** **变异后仍然全部通过。** 原因经测量确定：
`DataFlow` capability 产出的是**方法内**数据流（实测 15 条），它**不经过** CallGraph
⇒ 夹具**结构性地区分不了**。

**✅ 第三次：改用 `InterproceduralDataFlow` + 桥接边判据。** 同一变异下**3 条全部失败**：

```
失败 ...BridgesCoverMultipleKinds        → 跨过程桥接种类不足（实测 0 种）
失败 ...StillProducesBridgesSoOrdering... → Assert.NotEmpty() Failure: Collection was empty
失败 ...EmitsCrossMethodBridgeEdges       → Assert.NotEmpty() Failure: Collection was empty
```

**关键对照：** 在第一、二次失败时，同伴的 `CpgInterproceduralEdgeOrderTests` 在同一变异下**失败 3 条**
（`Collection was empty`）——**同一变异，同伴测得到、我测不到**。这个对照正是我第三次改对方向的依据：
**不是"变异没生效"，而是"我的夹具选错了观测面"。**

**方法论（与附录 M 同源、本轮更极端）：** 我在**同一件事**上连续写错**三次**断言，
前两次的形态都是「干净运行时**全绿**」。⇒ **绿灯是最有欺骗性的信号**；
**只有隔离变异 + 与同伴测试的对照**能区分「通过」与「能判别」。

## N.5 对 S3 的直接结论（本附录的交付物）

1. **S3 重排阶段前，必须先把 N.2 的隐式依赖显式化**（声明式依赖表或显式 barrier），
   否则依赖 2 被破坏时**表现为静默少边**，而不是崩溃。
2. **不得用 `Requires*` 推导阶段依赖**——它是 capability 门控，语义完全不同。
3. 迁移每个 pass 时，**验收判据应取"该阶段特有的边/产物"**，而不是
   "方法内数据流非空"这类**跨阶段都会产出**的量（N.4 第二次错误的根因）。
4. `DataFlowPass.cs:1936` 那行 `throw` **不应被当作保障**：实测它在正常路径上不可达。

## N.6 回归证据与边界

- `~BuilderStageDependencyContractTests` **3/3**（`stagedep-r16c.log`）。
- 变异（跳过 `CallGraphPass`）后 3/3 失败（`mutation-stagedep4.log`）；源码**逐字节恢复**（`602EECAEE1577BC1`）、无残留。
- **未改产品代码**（`source-hashes-round16.csv`）。
- **边界：** 本轮只确证了**两条**隐式依赖（CallGraph 相关）。其余 5 个 pass 之间的依赖
  （如 `Dominance` 与 `ControlDependence`、`ControlFlow` 的前置）**尚未逐一确证**；
  完整阶段依赖表是 G0-P 的后续工作，**G0-P 仍未关闭**。本附录**不**声称已给出完整 DAG。

---

# 附录 O：G0-P 依赖表由 2 条扩到 **4 条**——并发现真正的通道是 **30+ 个可变实例字段**（2026-09-25 轮次 17）

**性质：直接承接附录 N.6 承认的边界（「其余 5 个 pass 之间的依赖尚未逐一确证」）。本轮把依赖表补到 4 条，并纠正了 N 的一个**低估**。**

## O.1 依赖表（4 条，全部源码确证）

| # | 依赖 | 源码位置 | 守卫情况 | 被破坏时的表现 |
| --- | --- | --- | --- | --- |
| 1 | `DataFlow` 需要 `CallGraph` | `DataFlowPass.cs:1934-1937` | 有 `throw`，但**实测不可达** | 跨过程桥接边**静默变空** |
| 2 | `InterproceduralDataFlow` 需要 `CallGraph`+`DataFlow` reducer 都完成 | `NLCPGBuilder.cs:1041-1044` | **纯注释，零守卫** | 静默少边 |
| 3 | `ControlDependence` 需要 `Dominance` 填充 `_dominanceOverlays` | `ControlDependencePass.cs:35` | **零守卫**（`Count == 0` 直接 `return`） | 该阶段**整体静默跳过** |
| 4 | `DataFlow` 的 CFG 邻接规划读 `ControlFlow` 写入的 `_cfgPredecessors/SuccessorsByNode` | `DataFlowPass.cs:1227-1229` ← `NLCPGBuilder.cs:2062-2063` | **零守卫** | 邻接规划读到**空缓存** |

**其中 2/3/4 完全没有运行期守卫；1 的守卫实测不可达。⇒ 四条依赖全部只靠调用顺序维系。**

## O.2 对附录 N 的**修正**：真正的通道不是「7 行的书写顺序」，而是**实例字段**

附录 N 说依赖「仅由那 7 行的书写顺序保证」——**方向对，但低估了复杂度**。
本轮扫描 `NLCPGBuilder` 分部类，发现 pass 之间的实际数据通道是
**30+ 个可变实例字段**，例如：

```
_callSiteNodesByInvocation, _operationNodesByOperation, _resolvedCallTargetsByInvocation,
_resolvedCallTargetsByDispatchShape, _cfgPredecessorsByNode, _cfgSuccessorsByNode,
_dominanceOverlays, _syntaxNodes, _symbolNodes, _methodNodes, _methodParameterNodes,
_partitionedSyntaxFacts, _propertyAccessorCallSiteNodesByKey, ...
```

⇒ **S3 的风险比 N 描述的更具体也更严重：** 把 8 个 pass 拆成独立「计划/计算/归并」阶段时，
**这些字段不是参数**，无法通过函数签名传递；它们要求所有阶段共享**同一个 `NLCPGBuilder` 实例状态**。
若 S3 让不同阶段跑在不同实例/不同协调器上，**字段会静默为空**——正是 #3 的 `Count == 0` 提前 return 形态。

## O.3 依赖 3：**已被现有测试覆盖**（一个有用的否定结论）

对依赖 3 我先做了变异（跳过 `DominancePass`）**再决定是否新增测试**，结果：

```
失败 CpgWorkBatchLocalPassTests.BatchedDominanceBarrier_PreservesDominanceAndControlDependenceFacts(dop: 1/2/16)
失败 DominancePassContractTests.BuildFromSource_DominanceBitSetOverlay_PreservesEdgeOrderAcrossRepeatedBuilds
失败 NLCPGPartitionedBuilderTests.BuildFromSource_ControlDependenceCapability_ProjectsStableConditionalOverlays
失败: 5，通过: 47
```

⇒ 依赖 3 **已有充分回归保护**，**不需要新增测试**。这是**有意先验证再动手**省下的重复工作
（与附录 N.4 三次写错形成对照：**先变异，再决定写不写**）。

## O.4 依赖 4：新增 2 条判据，变异后**都能区分**

变异（跳过 `ControlFlowPass`）后：

```
失败 ...CfgAdjacencyCacheWasPopulatedByControlFlow   → Assert.NotEmpty() Failure: Collection was empty
失败 ...StillHasCfgEdgesProvingOrderSuppliesThem     → 只请求 DataFlow 时没有 CFG 边（自定义诊断文本）
失败: 2，通过: 4
```

⇒ 依赖 4 的判据**有真实判别力**。源码逐字节恢复（`602EECAEE1577BC1`）、无残留。

## O.5 ⚠️ 又一次「夹具选错观测面」——同一教训的第四次重现

新增的依赖 3 用例**第一次运行就失败**（`Collection was empty`）：我复用了本类里那份
**无分支**的 `CallSource`，而控制依赖边**只在存在分支时**才产生。

**这是附录 N.4 那个教训的第四次重现**，且形态完全一致：**我再次默认"能构建成功"就等于"该观测面能测到目标量"。**
修正方式：改用**仓库既有夹具** `CpgBuilderSources.ControlDependenceOverlay`（含 if/else，与同伴
`NLCPGPartitionedBuilderTests` 同源），而不是自造源。

**收敛出的规则（对 S3 直接可用）：** 为新阶段写验收判据时，
**必须选该阶段特有的产物，且必须用能触发该产物的最小夹具**；
"构建成功 + 断言某个通用量非空"是本轮与上轮共同的失败形态。

## O.6 回归证据与边界

- `~BuilderStageDependencyContractTests` **6/6**（`stagedep-r17b.log`）。
- 依赖 3 变异：5 条失败 / 47 通过（`mutation-dominance.log`）⇒ **已有覆盖，未新增测试**。
- 依赖 4 变异：2 条失败 / 4 通过（`mutation-controlflow.log`）⇒ 新判据有判别力。
- 两次变异源码均**逐字节恢复**（`602EECAEE1577BC1`），4 个文件**无残留**；**未改产品代码**（`source-hashes-round17.csv`）。
- **边界：**
  1. 依赖表**仍不完整**：`MemberAccess`、`ControlFlow`、`PartitionedSyntax/Operation` 之间的前置依赖
     **尚未逐一确证**（`MemberAccess` 与 `ControlFlow` 未测得对前序 pass 的状态依赖，
     但**未证伪**，只是本轮未找到证据）。
  2. `_cfgPredecessors/SuccessorsByNode` 的读者**只有 `DataFlowPass`**（实测），
     但该字段的写入点覆盖整个 `ControlFlowPass`，故依赖 4 的**完整影响面**未测量。
  3. 本轮**未**为依赖 1/2 增加新判据（轮次 16 已覆盖）。
  4. **G0-P 仍未关闭**：本附录给出的是**依赖表**，不是 G0-P 要求的
     「每阶段静态规划时点、结果类型、配额作用域、分层」——那部分**仍无设计**。

---

# 附录 P：G0-P 依赖表 4 → **6 条**，并用**反向变异矩阵**把"未找到证据"变成可判定（2026-09-25 轮次 18）

**性质：承接附录 O.6 边界 1（「`MemberAccess`/`ControlFlow` 未测得对前序 pass 的依赖，但未证伪」）。本轮用反向变异矩阵一次性给出依赖的规模与方向，并纠正 O 的一个方向性错误。**

## P.1 反向变异矩阵（关键方法）

把每个后置 pass 的门控**逐个**置 `false`，跑**同一滤器** `FullyQualifiedName~Cpg`：

- **干净基线：546/546 全绿**（`baseline-cpg-r18.log`）⇒ 所有失败都是变异引入的，**A/B 对照成立**。
- 逐个跳过后失败数：

| 被跳过的 pass | 失败数 | 主要失败表面 |
| --- | --- | --- |
| `MemberAccess` | **4** | `NLCPGPartitionedBuilderTests.*OperationConsumers*` + 3×`DataFlowAdjacencyCompactionTests.*KeepsFrozenGraphAndPublicationOrder` |
| `InterproceduralDataFlow` | **36** | `InterproceduralPlanCompactionTests`（下游消费其产物） |
| `CallGraph` | **48** | `FlowSummaryResolverTests` + `BuilderStageDependencyContractTests` ×3 |

**读数方式：** 失败数**多** ⇒ 该阶段产物被**广泛消费**；失败数**少但不为零** ⇒ 依赖**存在但狭窄**。
`MemberAccess` 只有 4 条，且**没有一条是它自己的产物断言**——这正是 O.6 说"未找到证据"的原因：
**它几乎不被依赖，但那"几乎"是不可忽略的 4 条。**

## P.2 新增依赖五：`DataFlow` **消费** `MemberAccess` 的产物

**同时纠正附录 O 的方向性错误。** O 说「`MemberAccess` 未测得对前序 pass 的依赖」——
方向搞反了：**`MemberAccess` 不是消费者，而是被消费者。**

证据：跳过 `MemberAccess` 后，
`NLCPGPartitionedBuilderTests.BuildFromSource_OperationConsumers_PreserveCallMemberAndDataFlowAcrossDegreesOfParallelism`
与 3 条含**期望数据流边数**的 `DataFlowAdjacencyCompactionTests.BuildFromSource_EveryFixture_KeepsFrozenGraphAndPublicationOrder`
失败 ⇒ **数据流边集依赖成员访问已建图**。

## P.3 新增依赖六：`DataFlow` ← `CallGraph` 的**第二条独立通道**

依赖一走 `_resolvedCallTargetsByInvocation`（`DataFlowPass.cs:1934`）；
依赖六走 **`_propertyAccessorCallSiteNodesByKey`**：

- 写：`CallGraphPass.cs:238`
- 读：`DataFlowPass.cs:2103` `FindPropertyAccessorCallSiteNode`
- 未命中时**返回 `null`（静默降级）**，**不抛异常** ⇒ **零守卫**

⇒ CallGraph→DataFlow 有**两条**互不覆盖的通道。只堵一条仍会漏。

## P.4 ⚠️ **第五次**「夹具选错观测面」——依赖六判据曾被变异证伪

依赖六最初断言「请求 `DataFlow` 时 **`Ref` 边非空**」。
**跳过 `CallGraphPass` 后该用例仍然通过**——因为 `Ref` 边 `MemberAccessPass.cs:152` **自己也会产出**，
**不是 CallGraph 的专属产物**。

修正：改用**桥接边**（`InterproceduralDataFlow`）——CallGraph 的专属产物。
修正后：干净基线 **8/8**，跳过 `CallGraph` **4 条失败**（含该用例）。

**这是同一错误的第五次重现（轮次 16 两次、17 一次、18 一次），形态完全一致：
选了一个"跨阶段都会产出"的量当判据。** 已收敛成硬规则见 P.6。

## P.5 依赖五判据的变异验证

跳过 `MemberAccess` ⇒ `BuildFromSource_WhenDataFlowRequested_MemberAccessArtefactsArePresent`
**唯一失败（1 失败 / 7 通过）** ⇒ 判据**精确且有判别力**（只打中该依赖）。

## P.6 收敛出的判据选择规则（S3 直接可用）

1. **只选该阶段的专属产物**——若某产物别的阶段也会产出，它**不能**作为该依赖的判据。
2. **必须用能真正触发该产物的最小夹具**——无分支的源测不到控制依赖（轮次 17）；
   无调用的源测不到桥接边（轮次 16）。
3. **先变异，再决定写不写测试**——依赖三据此省掉重复测试（轮次 17）。
4. **变异后必须比对"失败数"与"失败面"**——失败数少≠无依赖；要读**失败用例名**。

## P.7 回归证据与边界

- `~BuilderStageDependencyContractTests` **8/8**（`stagedep-r18b.log`）。
- 依赖五变异：1 失败 / 7 通过（`mutation-dep56-MemberAccess.log`）。
- 依赖六变异：**4 失败 / 4 通过**（`mutation-dep6-fixed.log`）；未修正前该用例**不失败**（`mutation-dep56-CallGraph.log`）。
- 反向变异矩阵基线 **546/546 全绿**（`baseline-cpg-r18.log`）。
- 全部变异源码**逐字节恢复**（`602EECAEE1577BC1`），5 个文件**无残留**，备份已清理；**未改产品代码**（`source-hashes-round18.csv`）。
- **边界：**
  1. 依赖表**共 6 条，仍不完整**：`ControlFlow`、`PartitionedSyntax/Operation` 的相对位置**仍未确证**
     （未纳入本轮变异矩阵）。
  2. 本矩阵只测**"跳过某阶段"**的后果，**未测"阶段顺序互换"**——后者才是 S3 真正的操作，
     且可能产生"两边都非空但语义已错"的形态，本矩阵对这种形态**不敏感**。
  3. 失败数（4/36/48）是**测试覆盖度**的代理指标，**不是依赖强度**的度量。
  4. **G0-P 仍未关闭**：本附录仍是**依赖表**，G0-P 要求的
     「每阶段静态规划时点、结果类型、配额作用域、分层」**仍无设计**。

---

# 附录 Q：**顺序互换**实验——补上附录 P.7 边界 2 的盲区；并**当场抓到 HEAD 上一处真实回归**（2026-09-25 轮次 19）

**性质：直接攻附录 P.7 边界 2（「本矩阵只测『跳过某阶段』，未测『阶段顺序互换』——后者才是 S3 真正的操作」）。本轮做了三组互换，结论是**互换可检测**，并顺带在 HEAD 上抓到一个正在生效的回归。**

## Q.1 三组互换实验（方法：对调两行 `RunOptionalPass`，跑同一滤器 `~Cpg`；基线 548/548 全绿）

| 互换 | 失败数 | 结论 |
| --- | --- | --- |
| `ControlFlow` ↔ `DataFlow`（依赖④） | **19** | **可检测**；失败面集中在 `DataFlowAdjacencyCompactionTests` |
| `Dominance` ↔ `ControlDependence`（依赖③） | **5** | **可检测**；失败面即依赖③的下游 |
| `DataFlow` ↔ `InterproceduralDataFlow`（依赖②） | **0** | **全绿** ⇒ 见 Q.3，**推翻我对依赖②的表述** |

## Q.2 关键：互换的检测器是**精确数量/语义断言**，不是"非空"

这回答了 P.7 边界 2 的担心（"两边都非空但语义已错"的形态是否不可检测）。**答案为：可检测。**

互换 `ControlFlow`↔`DataFlow` 失败的 5 个去重用例中：

- `DataFlowAdjacencyCompactionTests.BuildFromSource_EveryFixture_KeepsFrozenGraphAndPublicationOrder`
  断言 `Assert.Equal(expectedDataFlowEdges, graph.Edges.Count(edge => edge.Kind == DataFlow))`（**精确数量**，
  且 fixture 参数里就写着 `expectedDataFlowEdges: 262` 等）
- `...NeighborVisitCounts_MatchTheLegacyMaterialization` 断言 `Assert.Equal(flowNodes, incomingVisits)`（**精确计数**）
- **`NLCPGDataFlowSparseSetTests.CardinalityExceedingInlineSlot_AllDefinitionsStillReachUseSite`** —— 这是
  **语义正确性**断言（"所有定义仍到达使用点"），**正是"非空但语义已错"的检测器**

⇒ **"非空"断言对互换不敏感，但仓库已有的精确数量/语义断言覆盖了它。** 这是 P.7 边界 2 的**解除**。

## Q.3 ⚠️ 互换 `DataFlow`↔`Interprocedural` **全绿**——推翻附录 N/O 对依赖②的表述

附录 N 把依赖②写成「`InterproceduralDataFlow` **要求** `CallGraph`+`DataFlow` 的 reducer **都已完成**」，
依据是 `NLCPGBuilder.cs:1041-1044` 的注释。**本轮证伪了这个表述的两点：**

1. **互换顺序 → 548/548 全绿**（含 4 个 Interprocedural 专用测试类：
   `CpgInterproceduralEdgeOrderTests`、`CpgWorkBatchInterproceduralTests`、
   `InterproceduralEdgeIndexSnapshotTests`、`InterproceduralPlanCompactionTests`）。
2. **源码确证**：`_interproceduralBarrierCompleted`（`:81`）只在 `:1044` 被**写**、在 `:467` 被**读进一个 metrics record**；
   全仓搜索 `InterproceduralBarrierCompleted` 只有 **2 处**（`NLCPGBuildMetrics.cs:101` 的字段声明 + `NLCPGBuilder.cs:467` 的赋值）
   ⇒ **它从未参与任何门控判断，只是一个诊断字段。**

**它真正依赖的是"当时图里已存在的 pending edges"**：`:1062-1063`
`InterproceduralEdgeSnapshot.Create(graph.EnumeratePendingEdgesLazily(), ...)`，
而 `AddEdge`（`NLCPGGraph.cs:365`）**总是**写 `_pendingEdges`、**不区分阶段**。
⇒ 互换后它读到的是**少了 DataFlow 那批边的快照**，但这是**内容差异**而非**门控失败**，
且**在现有测试覆盖下不改变可观察结论**。

**⇒ 依赖②的正确表述应为："`InterproceduralDataFlow` 的快照**包含**当时已加入的边；
`DataFlow` 先跑会使其快照**更大**。这是**内容依赖**，不是**前置条件依赖**。"
本轮**不**声称依赖②为"运行期强制"——它**不是**。**

**⚠️ 同时留下的真问题：** 一个**更小的快照**是否在**语义上等价**，本轮**没有证伪**，只是**现有测试测不到差异**。
这是 S3 重排时**值得单独验证**的点（若等价，则该顺序可自由重排；若不等价，则存在**测试盲区**）。

## Q.4 🔴 **当场抓到 HEAD 上的一处真实回归**（本轮最重要的产出）

**在做上述实验期间，`NLCPGBuilder.cs` 被并发改动了**（mtime `09-25 19:09:21`；
我的三个备份 `r19/r19b/r19c` 哈希均为旧值 `602EECAEE1577BC1`，第四个 `r19d` 已是新值 `332537423AA45DB6`
——**这组备份本身就是改动发生时点的证据**）。

改动内容：**把 `ControlDependence` 移到了 `Dominance` 之前**（`:416` 与 `:417` 对调）。

**这正是我在 Q.1 表里预测会失败的互换。实测确认：**

```
当前 HEAD（FullyQualifiedName~Cpg）：失败 5 / 通过 543
  失败 BuilderStageDependencyContractTests.BuildFromSource_WhenControlDependenceRequested_EmitsControlDependenceEdges
  失败 CpgWorkBatchLocalPassTests.BatchedDominanceBarrier_PreservesDominanceAndControlDependenceFacts(dop: 1/2/16)
  失败 NLCPGPartitionedBuilderTests.BuildFromSource_ControlDependenceCapability_ProjectsStableConditionalOverlays
```

**失败面与我的互换实验二号（5 条）完全同批 ⇒ 预测被实证。**

**根因（依赖③）：** `ControlDependencePass.cs:35` —— `if (_dominanceOverlays.Count == 0) return;`。
`_dominanceOverlays` 由 `DominancePass` 填充；顺序反了 ⇒ 该列表为空 ⇒ **`ControlDependence` 阶段整体静默跳过**，
**不抛异常、不打日志**。这正是附录 O.1 表格里"整阶段静默跳过"那一行的实例化。

**价值兑现：** 轮次 17 新增的 `WhenControlDependenceRequested_EmitsControlDependenceEdges`
（当时是"先变异、确认已有覆盖后仍补的一条"）**现在是抓住该回归的判据之一**。
⇒ **依赖表的投入在本轮以"抓住真实回归"的形式回报了**，而不是停留在文档。

## Q.5 回归证据与边界

- 三组互换：19 失败（`swap-cfg-dataflow.log`）、5 失败（`swap-dom-cd.log`）、**0 失败**（`swap-df-interproc.log`）。
- 互换基线 **548/548 全绿**；`DataFlow` 门控关闭时 **86 失败 / 462 通过**（`mutation-nodataflow.log`）。
- **HEAD 回归实测**：`quick-cd-dom.log` 6 失败/6 通过（子集滤器）、`head-cpg-r19.log` **5 失败 / 543 通过**（全量 Cpg）。
- 我的全部变异**已逐字节恢复**，`NLCPGBuilder.cs` **无 `MUTATION` 残留**（`source-hashes-round19.csv`）。
- **边界：**
  1. Q.4 的回归**不是我的改动**（我的备份链可证），但**修复动作会改产品代码，超出本轮验证工作的范围** ⇒
     本轮**只记录与复现**，**未擅自修复**（避免与它人的阶段重排意图冲突）。**该回归在 HEAD 上仍然生效。**
  2. 只做了 **3 组**互换，不是全排列；`MemberAccess`/`CallGraph`/`PartitionedSyntax·Operation` 的互换**未测**。
  3. Q.3 只证明"**现有测试**测不到该互换的差异"，**不等于**语义等价（见 Q.3 末的真问题）。
  4. **G0-P 仍未关闭**：依赖表仍是"事实清单"，G0-P 要求的
     「每阶段静态规划时点、结果类型、配额作用域、分层」**仍无设计**。

## Q.6 ✅ 回归已修复（2026-09-25 轮次 20）

**修复动作：** `NLCPGBuilder.cs` 中两行**换回** `Dominance` 在前、`ControlDependence` 在后，
并就地加上**顺序约束注释**（标注依赖③的失效形态与回归证据出处），使该约束**在源码里可读**，
而不再只存在于本计划的附录中。

**验证：**

| 范围 | 修复前 | 修复后 |
| --- | --- | --- |
| `~ControlDependence\|~Dominance\|~BatchedDominanceBarrier` | 6 失败 / 6 通过 | **12/12 全绿**（`fix-cd-dom.log`） |
| 全量 `~Cpg` | 5 失败 / 543 通过 | **548/548 全绿**（`fix-cpg-full.log`） |

哈希：`332537423AA45DB6` → `D3471B7F8ED0C4FF`（仅此一处改动 + 注释）。

**这是 G0-P 依赖表的第一次"闭环"：** 隐式依赖被显式化 → 被回归捕获 → 被修复 → 修复被验证。
但它**同时也暴露了 G0-P 仍未解决的问题**：该约束目前**只靠一行注释**提醒，
**没有任何机制阻止**下次再被重排 ⇒ 这正是下面附录 R 要设计的。

---

# 附录 R：G0-P 设计——**每阶段静态规划契约**（第一个完整设计，2026-09-25 轮次 20）

**性质：G0-P 的**首个完整设计**。附录 N/O/P/Q 给出的是「依赖事实清单」，本附录补上计划 :571 实际要求的四要素：
①每阶段静态规划时点 ②结果类型 ③配额作用域 ④分层与窗口外依赖。**

## R.1 现状盘点（源码确证）

| 要素 | 现状 | 缺口 |
| --- | --- | --- |
| ① 静态规划时点 | **无统一定义**。`DominancePass.cs:349-356` 在 pass **内部**临时规划（`GetOperationRootPlans` → `AssembleWorkBatches`）；`ControlDependencePass.cs:41` 更是**内联** `new CpgWorkBatch(...)` | 规划发生在**计算阶段内部**，无法在窗口开始前知道规模 ⇒ 无法提前配额度 |
| ② 结果类型 | **5 个私有嵌套 record**：`SyntaxWorkBatchResult`(`PartitionedSyntaxPass.cs:22`)、`OperationWorkBatchResult`(`:34`)、`CallGraphWorkBatchResult`(`CallGraphPass.cs:44`)、`DataFlowWorkBatchResult`(`DataFlowPass.cs:436`)、`DominanceWorkBatchResult`(`DominancePass.cs:284`) | 各自为政、**无公共契约**；`ControlFlow`/`ControlDependence`/`MemberAccess`/`Interprocedural` **连结果类型都没有** |
| ③ 配额作用域 | `stageId` **只用于 telemetry**（`CpgWorkBatchExecutor.cs:367` 注释明说「telemetry 阶段标识」） | **配额不按 stage 分离** ⇒ 一个阶段的巨型批会挤占其他阶段额度 |
| ④ 分层与窗口外依赖 | 依赖为**隐式**，仅由 `NLCPGBuilder.cs:411-417` 的**书写顺序**保证（附录 N–Q 确证 6 条） | 无声明式依赖表；顺序被改后**唯一防线**是测试（附录 Q.6 刚验证过这条防线**能**拦住，但依赖运气） |

## R.2 设计：`StagePlan` 四要素契约

**核心主张：把"规划"从计算阶段内部**提取到窗口开始之前**，并让每个阶段**声明**其依赖与配额作用域。**

```csharp
// 伪代码：位置 src/NLCPG/Builder/Concurrency/StagePlan.cs（新建）
internal sealed record StagePlan(
  CpgWorkBatchPerformanceStageId Stage,        // ① 阶段标识（已有枚举可复用）
  IReadOnlyList<StageDependency> Requires,     // ④ 显式依赖（取代隐式书写顺序）
  StageQuotaScope QuotaScope,                  // ③ 配额作用域
  IReadOnlyList<CpgWorkBatch> Batches,         // ① 规划产物（时点=窗口前）
  long PlannedBytes);                          // ① 供额度预检

internal enum StageQuotaScope
{
  Shared,        // 与同层其他阶段共享池（现有行为）
  Dedicated,     // 独占（如 InterproceduralDataFlow 的桥接发布段）
}
```

**① 静态规划时点 —— 定义为"任何 worker 启动之前"。**
每个阶段拆成两个方法：

```csharp
StagePlan PlanXxxStage(NLCPGBuildContext context);   // 只读：算批次与预估，不碰图
void CommitXxxStage(NLCPGBuildContext context, StagePlan plan);  // 唯一写图者
```

- `Plan*` **不得调用任何 `graph.AddEdge`/`AddNode`**（由既有"单写者"不变量保证，见计划 :118）。
- 收益：窗口开始前即知 `PlannedBytes`，可做**准入预检**（现在做不到，见 R.1 ①）。

**② 结果类型 —— 引入公共基契约，消灭"各自为政"。**

```csharp
internal interface IStageWorkResult
{
  CpgWorkBatchPerformanceStageId Stage { get; }
  long ProducedNodeCount { get; }   // 归并前统计，供 ③ 的配额回填
}
```
现有 5 个 record 实现该接口；为**尚无结果类型的 4 个阶段**（`ControlFlow`/`ControlDependence`/`MemberAccess`/`Interprocedural`）
补齐 —— **这是本设计最实的工程量**，也是 ③ 的前提。

**③ 配额作用域 —— 从 `Shared` 起步，只对已实测的例外开 `Dedicated`。**

- **默认 `Shared`**：不得凭空引入新额度（计划 :114 明确要求"校准后才作硬额度"，而附录 G 已实测**不存在**可用 bytes 上界）。
- `InterproceduralDataFlow` 候选 `Dedicated`：其桥接发布段实测存活 755 MiB（`NLCPGBuilder.cs:1067` 注释），
  与局部 pass 不在同一量级。**但标注为"候选，需实测标定后才可启用"**，本轮**不启用**。

**④ 分层与窗口外依赖 —— 用声明式表取代书写顺序，并在启动时校验。**

```csharp
internal static class StageDependencyTable
{
  // 与附录 O/P 的 6 条实测依赖一一对应；本表是唯一权威。
  public static bool IsSatisfied(Stage stage, IReadOnlySet<Stage> completed);
}
```
- **启动期断言**：窗口协调器在首个 worker 启动前校验拓扑序，**违反即抛**（fail-closed，而非静默少边）。
- 这把附录 Q.6 那行"提醒注释"升级为**机制**：下次再有人对调两行，**在测试跑起来之前就失败**。

## R.3 分层（回答 :571 的"分层"）

```
L0 规划层   Plan*        —— 只读，无 worker，窗口前完成
L1 计算层   worker 内     —— 只算 fragment，不写共享图
L2 归并层   Commit*/reducer —— 唯一写图者，按 StableOrder 合并
L3 发布层   FreezeQueryIndex / PersistenceWrite —— 既有不变
```
**现有代码已隐含 L1/L2 分离**（`CpgFragmentReducer.ReduceInto` 就是 L2），缺的是**把 L0 显式提出来**（R.1 ①）。

⚠️ **进展（附录 Y，轮次 26）：L0「只读」已由机制强制**——规划相位进入
`NLCPGGraph` 的**只读窗口**，窗口内任何构图调用 fail-closed 抛出并指出违反的层次。
此前该性质**只有一段注释**在声称（"两者均不写图"）。**L1（worker 内只产 fragment）
仍未被机制强制**，见 Y.6 边界 2。

## R.4 窗口外依赖（回答 :571 的"窗口外依赖"）

三类，均须在 `StagePlan.Requires` 中显式声明：
1. **阶段间**：附录 O/P 的 6 条（如 `ControlDependence` → `Dominance`）。
2. **跨文件**：`InterproceduralDataFlow` 的快照含**所有**已入边（附录 Q.3 确证），故它**必须在同窗口内看到全部文件**——这是 S3-1c「窗口协调器」的硬约束。
3. **窗口外**：`FreezeQueryIndex` 与持久化**必须在所有阶段提交后**，不得与任何计算阶段重叠。

⚠️ **进展（附录 Z，轮次 27）——第 1 类的"声明"已与权威表对账**：
实现中该字段名为 `StagePlan.RequiresRuntimeInputFrom`。它此前**只写不读**，
而 `StageDependencyTable` 里已有一条同样的登记 ⇒ 两处声明可各自漂移。
现已在 `RecordStagePlan` 内 fail-closed 对账：来源须**真是**权威表登记的该阶段前置，
且须在**本次**构建中**真的执行过**。
**⚠️ 第 1 类**的**顺序**由 R-1 的 `EnsureStageDependencyOrder` 强制，
本对账**不**增加抗重排能力（附录 Z.5 的否定结果）。

**⚠️ 进展（附录 AD，轮次 31）——第 2/3 类已各自定性（原"仍只有文字描述，无任何机制"已不准确）**：

- **第 2 类（跨文件）：判定为【空真满足】**，而非"缺机制"。源码确证**不存在**把多个文件
  放进同一个 `NLCPGGraph` 的路径：`CpgWorkBatchBuilder` 显式**拒绝**跨文件 item，
  且没有任何调用点构造过跨文件 `NLCPGGraph`。既然"同窗口看到多个文件"这一前提
  在今天的架构里**不可能成立**，第 2 类的约束就无从被违反——它对应的机制
  （S3-1c「窗口协调器」）**尚未开工**，见 AD.5。**故本类不构成本轮遗留缺口**，
  但一旦真出现跨文件单图构建，该约束立刻变成硬需求。
- **第 3 类（发布）：拆成两半后判定为【一半早已被既有机制强制，另一半本轮补上】。**
  其一"**冻结后不得再写**"——由 `NLCPGGraph.EnsureMutable` 的 frozen 检查
  **原本就已**传递性强制（5 个图变更入口共用该唯一瓶颈点，见 AD.2）；
  其二"**发布必须晚于全部阶段提交且与其不重叠**"——此前**确实无任何机制**，
  本轮新增 `EnsurePublicationAllowed`（顺序前提 + 跨线程窗口计数为 0）
  与 `RecordPublicationStage`，见 AD.3。

**⚠️ 同一轮补齐 **R.3「分层」L1 的第二半**。L1 的"worker 只算 fragment"此前**只强制了
"不写共享图"这半**（附录 AB，`NLCPGGraph` worker 窗口守卫），而"**不共享可变中间态**"这半
**仅由注释声称**（AB.6 边界 2 已自承）。本轮新增 `WriteSharedState` 单一写入口，
把"窗口存活期间对 builder 级共享容器的写入必须串行化"变成结构性约束，见 AD.4。

## R.5 分阶段落地（不要求一次做完）

| 步骤 | 内容 | 可否独立验证 |
| --- | --- | --- |
| R-1 | 建立 `StageDependencyTable` + 启动期拓扑断言（**只加校验，不改顺序**） | ✅ 用现有 6 条依赖直接验证 |
| R-2 | 为 4 个缺失阶段补 `IStageWorkResult` | ✅ 逐阶段变异 |
| R-3 | 提取 `Plan*`（从 `DominancePass`/`ControlDependencePass` 起步，**不碰 DataFlowPass**） | ✅ 等价性对比（**已实现**，见附录 U/V：规划相位覆盖 4 阶段，`ControlDependence` 按设计延迟；**附录 X 追加 `DataFlow` ⇒ 覆盖 5 阶段，R-3 具名缺口已关闭**） |
| R-4 | 配额作用域分级（**默认 Shared，Dedicated 需先标定**） | ⚠ 依赖估算校准，见附录 G；**分级机制已实现**（附录 W）：`StageQuotaScope` 落地、默认全 `Shared`、无标定的 `Dedicated` fail-closed；**仍未配任何额度**（附录 G 证明无可用上界） |

**R-1 是最高性价比的第一步**：**只加校验、不改行为**，却能把附录 Q.6 那类回归从"靠测试发现"变成"启动即拒绝"。

## R.6 边界（本设计**未**解决的事）

1. **本附录是设计，不是实现**——撰写时 `StagePlan`/`StageDependencyTable`/`IStageWorkResult` **均不存在**。
   ⚠️ **该状态已过期**：R-1 见附录 S、R-2 见附录 T、R-3 见附录 U/V、R-4 **分级机制**见附录 W，四者**均已落地**；
   R-4 的**额度数值**部分仍**未做**（附录 W.1/W.6 边界 2 说明为何不得做）。
   本附录保留为**设计原文**，其"不存在"表述**不应**被当作现状引用。
2. R-1…R-4 **均未开工**；G0-P 的完成条件仍**未满足**。
   ⚠️ **该状态已过期**：R-1/R-2/R-3 已落地（附录 S/T/U/V），R-4 的**分级机制**已落地（附录 W）。
   ⚠️ **但 G0-P 仍未关闭**：四要素中 ③ 只做到"分级 + 默认值"，**未为任何阶段配额度**
   （附录 G 证明不存在可用 bytes 上界），且 R-3 仍有规划相位未覆盖
   `PartitionedSyntax`/`PartitionedOperation`（W.6 边界 4；`DataFlow` 已由**附录 X** 前移，
   X.6 边界 2 说明为何这两个阶段属**另一类改动**而非本附录的遗留待办）。
3. ③ 的 `Dedicated` 分级**依赖估算校准**，而附录 G 已实测**不存在**可用 bytes 上界
   ⇒ 在补齐 `Estimate` 的自变量之前，`Dedicated` 只能基于**实测标定**而非公式，**本轮未标定**。
4. ④ 的表只覆盖**已实测的 6 条**；`ControlFlow`/`PartitionedSyntax`/`PartitionedOperation`
   的相对位置仍未确证（附录 P.7 边界 1），故该表**不完整**。
   ⚠️ **补充（附录 Y）**：④ 的另一半「**分层**」已开始落地——**L0 只读**已由机制强制；
   但 **L1（worker 内只产 fragment）仍未被机制强制**，"窗口外依赖"三类中
   仅第 1 类（阶段间）有机制，第 2/3 类（跨文件、窗口外）仍只有文字描述（Y.6 边界 2/3）。
5. **`DataFlowPass.cs`（2270 行）在本设计中标记为"暂不触及"**，R-3 从其他阶段起步。

---

# 附录 S：**R-1 已实现**——阶段依赖从"靠书写顺序"变成"**fail-closed 强制**"（2026-09-25 轮次 21）

**性质：附录 R 是设计；本附录是 R-1 的\*\*实现与验证\*\*。G0-P 第一个从"设计"落成"机制"的步骤。**

## S.1 交付物

| 文件 | 内容 |
| --- | --- |
| `src/NLCPG/Builder/StageDependencyTable.cs`（新） | 依赖**唯一权威表** + `TryValidateOrder` 纯函数校验 |
| `src/NLCPG/Builder/NLCPGBuilder.cs`（改） | `RunOptionalPass` 增记**实际执行阶段**；全部 pass 后 `EnsureStageDependencyOrder()` 校验 |
| `tests/NLISSN.ContractTests/Cpg/StageDependencyTableTests.cs`（新） | 9 条用例 |

## S.2 关键设计决定：**序列由实际执行记录，而不是对照手抄副本**

**这是本轮最重要的实现判断，且它来自一次自我纠错。**

**第一版实现**把期望序列**硬编码**在 `EnsureStageDependencyOrder` 里，与实际书写顺序各写一遍。
变异实验（对调 `NLCPGBuilder.cs` 中 `Dominance`/`ControlDependence` 两行，**即真实复现附录 Q.4 的回归**）后：
**只失败 1 条，且没有抛出任何守卫异常**（`mutation-r21-swaporder.log`）。

**根因：** 校验比的是那份**手抄副本**，而副本没跟着改 ⇒ **校验与被校验对象可以一起漂移**。
这与附录 N.4/P.4 的「夹具选错观测面」同源：**校验了一个不是真正被测对象的量。**

**修正：** 让 `RunOptionalPass` 在**真正调用 pass 前**把 stage 追加进 `_executedStageOrder`，
全部 pass 结束后再校验这份**实际记录**。⇒ 校验对象**不可能**与真实执行顺序不一致。

## S.3 变异验证（决定性）

对调 `Dominance`/`ControlDependence` **书写顺序**后，抛出的正是设计的守卫：

```
System.InvalidOperationException : CPG builder stage dependency violated:
阶段依赖被违反：ControlDependence 需要 Dominance 先完成，
但执行序列中 ControlDependence 位于下标 2、Dominance 位于下标 3。
 实测后果：ControlDependencePass.cs:35 读到空的 _dominanceOverlays 后【整体静默 return】，
 不抛异常、不打日志，控制依赖边全部缺失（附录 Q.4 实证：全量 ~Cpg 由 548/548 变为 5 失败）。
```

**这正是 R-1 的目的：把附录 Q.4 那类回归从「静默少边、只有测试（若恰有覆盖）能发现」
变成「显式异常 + 直接说明实测后果」。** 异常消息里带**实测后果**，是因为该依赖的失效形态
**不直观**——只报"顺序错了"不足以让人判断危害。

## S.4 回归证据

| 项目 | 结果 |
| --- | --- |
| `~StageDependencyTableTests` | **9/9** |
| 全量 `~Cpg` | **557/557 全绿**（轮次 20 基线 548 ⇒ **+9 全为本轮新增，零回归**） |
| 变异（对调两行） | 校验**抛出**上述异常 |
| 源码 | 逐字节恢复（`5742DE1CD8C1D4A9`）、无残留；`source-hashes-round21.csv` |

`~StageDependencyTableTests` 覆盖：正确序列通过、**轮次 19 真实回归被拒**、依赖④/①⑥被拒、
省略阶段不算违反、重复阶段被拒、空序列、**前置关系图逐条固化**、**表无环**。

## S.5 边界（R-1 **未**解决的事）

1. **只实现了 R-1**。R-2（`IStageWorkResult`）、R-3（提取 `Plan*`）、R-4（配额分级）**均未开工**
   ⇒ **G0-P 完成条件仍未满足，仍未关闭。**
2. 校验位于**全部 pass 执行之后** ⇒ 违反时**图已被构建**（虽未发布）。
   这是刻意的取舍：在**执行前**校验需要一份与书写顺序同步的副本，
   而那正是 S.2 证明会漂移的形态。**若将来要前移，必须让期望序列由与执行同源的单一来源生成。**
3. 表只覆盖**已实测的 6 条**（加上 `Syntax`/`Operation` 的建图前置）。
   `ControlFlow`/`PartitionedSyntax`/`PartitionedOperation` 的相对位置**仍未确证**（附录 P.7 边界 1）。
4. **`Syntax`/`Operation` 未接入校验**：它们在 `:411` **之前**执行，不走 `RunOptionalPass`（该重载当前只服务后置 pass）。
5. 本设施**不阻止**新增一个未登记的阶段；`TryValidateOrder` 对未登记阶段会拒绝，
   但那只在该阶段进入 `_executedStageOrder` 时生效 ⇒ 新阶段若沿用别的调用路径仍会漏网。

---

# 附录 T：**R-2 已实现**——4 个"无结果类型"的阶段补齐统一记账（2026-09-25 轮次 22）

**性质：附录 R.1 ② 记录了缺口；本附录是 R-2 的\*\*实现与验证\*\*。G0-P 四要素中第 ② 项落地。**

## T.1 缺口（R.1 ② 原文）

5 个阶段各有**私有嵌套 record**（`SyntaxWorkBatchResult` / `OperationWorkBatchResult` /
`CallGraphWorkBatchResult` / `DataFlowWorkBatchResult` / `DominanceWorkBatchResult`），
**彼此无公共契约**；而 `ControlFlow` / `ControlDependence` / `MemberAccess` / `InterproceduralDataFlow`
**连结果类型都没有** ⇒ 窗口层无法统一记账，R.4 的配额作用域也没有回填来源。

## T.2 交付物

| 文件 | 内容 |
| --- | --- |
| `src/NLCPG/Builder/IStageWorkResult.cs`（新） | 公共契约：`Stage` + `ProducedNodeCount` |
| `src/NLCPG/Builder/StageWorkResults.cs`（新） | 三个适配器（fragment / 事实计数 / 跨过程） |
| `NLCPGBuilder.cs`（改） | `_stageWorkResults` 记账表 + `_recordingStage` 窗口 + `LastStageWorkResults` |
| `ControlFlowPass.cs` / `ControlDependencePass.cs` / `MemberAccessPass.cs`（改） | 各回报**本次实际产出** |
| `tests/…/StageWorkResultAccountingTests.cs`（新） | 6 条用例 |

## T.3 实测发现的形态差异（**不能靠猜**）

R.1 曾把 4 个阶段笼统描述为"无结果类型"。实测后**发现它们分属两种形态**：

| 阶段 | 实际产出 | 证据 |
| --- | --- | --- |
| `ControlFlow` | `LocalCpgFragment` 列表 | `ControlFlowPass.cs:46` |
| `ControlDependence` | `LocalCpgFragment` 列表 | `ControlDependencePass.cs:61` |
| `MemberAccess` | **`MemberAccessFact`**（**不是** fragment） | `MemberAccessPass.cs:78-97` |
| `InterproceduralDataFlow` | **不产 fragment**，直接向图发布桥接边 | `NLCPGBuilder.cs:1177` 起 |

⇒ 故用**三个适配器**而非一个：强行统一会掩盖 `MemberAccess` 与另外两者的真实差异。
**这是"先读源码再定接口"的直接收益**——若照 R.1 的笼统描述直接写，会为 `MemberAccess` 套上一个不存在的 fragment 包装。

## T.4 关键设计决定：由 pass **主动回报实际产出**，而非事后重算

`ReportStageFragments` / `ReportMemberAccessFacts` 由 pass 在归并**之前**调用，
且只在 `_recordingStage` 非 null（即确实处于某次 `RunOptionalPass` 中）时接受回报。

**为什么坚持这一点：** 事后重算会引入**第二份"真相"**——正是附录 N.4（断言读源码文本）、
P.4（判据用了非专属产物）、S.2（校验对照手抄副本）反复踩到的形态。
**记账对象必须 === 执行对象。**

## T.5 变异验证：**记账错位**被抓住

把 `_stageWorkResults[stage]` 的键改成**固定错值**（一律记到 `DataFlow` 名下）后：

```
失败 WhenControlFlowRequested_RecordsControlFlowStageResult
失败 WhenControlDependenceRequested_RecordsControlDependenceStageResult
失败 WhenAllStagesRequested_EachRecordedResultReportsItsOwnStage   ← 专为"错位"写的判据
失败 WhenOnlyCfgRequested_DoesNotRecordUnrequestedStages           ← 专为"多余记录"写的判据
失败: 4，通过: 2
```

**判据选择说明：** 若只写"各阶段产出非空"，上述错位**仍会全部通过**
（因为每个阶段名下都非空，只是内容错了）。故特意加了
`EachRecordedResultReportsItsOwnStage`（逐条核对 `IStageWorkResult.Stage` 与字典键一致）
与 `DoesNotRecordUnrequestedStages`（未请求的能力不得留下记录）。
**这是把附录 P.4 的教训落到判据设计上的结果。**

## T.6 回归证据与边界

- `~StageWorkResultAccountingTests` **6/6**。
- **针对性**回归（`~StageDependencyTableTests|~StageWorkResultAccountingTests|~BuilderStageDependencyContractTests|~CpgWorkBatchLocalPassTests|~DominancePassContractTests`）
  **36/36 全绿**，零回归（`r22-targeted.log`）。
- 变异（记账键错位）**4 条失败**（`mutation-r22-misattribution.log`）；源码逐字节恢复（`CA0258AEE259EF57`）、无残留（`source-hashes-round22.csv`）。
- **边界：**
  1. **只实现了 R-2**。R-3（提取 `Plan*`）、R-4（配额分级）**均未开工**
     ⇒ **G0-P 完成条件仍未满足，仍未关闭。**
  2. `MemberAccess` 的 `ProducedNodeCount` 取**当前图节点数**作为**规模代理**，**不是**精确节点增量。
     已在代码注释中标注为代理，**不得**当作精确值使用。
  3. 现有 5 个私有 record **未改造**为 `IStageWorkResult`——它们仍可用，只是尚未接入记账表。
     故 `LastStageWorkResults` 当前只含**本轮补齐的 4 个阶段**。
  4. 记账**不参与**任何执行决策，纯观测；R.4 的配额作用域**尚未**消费它。
  5. `InterproceduralDataFlow` 的记账取的是 `graph.Edges.Count`（**全图边数**），
     而该阶段只**增**桥接边 ⇒ 它**不是**该阶段的产出量，只是规模量级代理；同样已标注。

---

# 附录 U：**R-3 已实现**——静态规划相位真正前移到执行之前（2026-09-25 轮次 23）

**性质：附录 R.1 ① 记录了缺口；本附录是 R-3 的**实现与验证**。G0-P 四要素第 ① 项落地。**

## U.1 一次中途纠错：**拆分 ≠ 前移**

本轮先按"给既有两段代码起新名字"的方式改了 `ControlDependencePass` 与 `ControlFlowPass`
（`Plan*` + `Commit*`），编译通过、测试也过。**但随即发现这没有推进 R-3。**

R.1 ① 的缺口原文是「规划发生在计算阶段内部 ⇒ **窗口开始前不知规模，无法预配额度**」。
而那时 `Plan*` 仍在 `RunOptionalPass` 内、按原顺序被调用——**规划时点根本没变**，
只是换了函数名。若就此收尾并声称"R-3 已实现"，就是把空壳当成果。

**真正的 R-3 必须是一个规划**相位**：** 在所有阶段执行**之前**统一算完并登记 plan。

## U.2 关键前置验证：规划链是纯函数

把规划提前是否安全，取决于规划**有没有副作用**。实测（读源码，非推断）：

- `AssembleWorkBatches`（`PartitionedOperationPass.cs:411-433`）：只读 `context`、算成本、调 `_workBatchBuilder.Build`，**不写图、不改 builder 字段**。
- `CpgWorkBatchBuilder.Build`（`CpgWorkBatchBuilder.cs:25-46`）：**无实例状态变更**，只用局部变量。
- `GetOperationRootPlans`：只读语法/语义模型。

⇒ **整条规划链是纯函数，提前规划不改变任何行为。** 这是 R-3 能按此形状实施的前提，而非假设。

## U.3 交付物与可判定性

| 文件 | 内容 |
| --- | --- |
| `src/NLCPG/Builder/StagePlan.cs`（新） | `IStagePlan<TBatch>` 契约 + `StagePlan` 实现（`EstimatedNodeCount/Bytes/ItemCount`） |
| `NLCPGBuilder.cs`（改） | `PlanStagesBeforeExecution` 规划相位、`_stagePlans` 登记、`TakeRecordedStagePlan`、两个相位快照 |
| `ControlFlowPass.cs`（改） | `PlanControlFlowStage`（只读）+ `CommitControlFlowStage`（唯一写图者）；执行时**只消费**既有 plan |
| `ControlDependencePass.cs`（改） | 同样的 `Plan*`/`Commit*` 二分 |
| `tests/…/StagePlanContractTests.cs`（新） | 5 条用例 |

**让"时序"可判定：** builder 在规划相位结束瞬间记录两个快照——
`PlanSnapshotAtEndOfPlanningPhase`（应有内容）与 `WorkResultsAtEndOfPlanningPhase`（**必为空**，因尚无 worker 运行）。
若 `Plan*` 只是改了名、仍留在执行相位内，则前者为空、后者非空 ⇒ 契约测试立刻失败。

## U.4 变异验证：**"只改名、未前移"被抓住**

**掏空规划相位**（`PlanStagesBeforeExecution` 调用处改为注释，模拟"Plan* 仍在执行相位内"）后：

```
失败 WhenCfgRequested_PlanningPhaseCompletesBeforeAnyWorkResultIsRecorded   ← 核心时序判据
失败 WhenCfgRequested_PlanEstimatesMatchItsOwnBatches
失败 WhenCfgRequested_ControlFlowPlanExistsBeforePassesRun
失败: 3，通过: 2
```

**判据选择说明：** 若只写"plan 存在且批次数 > 0"，上述变异**仍会通过**
（因为在执行相位里规划同样能产出非空 plan）。故核心判据必须断言**时序**，而不是**存在性**。
这是 U.1 那次纠错直接催生的判据设计。

## U.5 附带发现：G0-M 估算公式缺自变量的**源码级证据**

`CpgWorkBatchBuilder.AddBatch`（`:218`）写入的是 `estimatedNodeCount: items.Count`——
即**预估节点数 = 条目数（方法数）**，与节点/边规模**无关**。
这为附录 G 已记录的"估算公式缺节点/边独立变量"提供了**源码级确证**（此前只是观察结论）。
`EstimatedBytes` 同理：`EstimateBytes = EstimatedCost × 64`（`:224-227`）——**不读任何图规模**。

## U.6 回归证据与边界

- `~StagePlanContractTests` **5/5**。
- **针对性**回归（7 个测试类）**41/41 全绿**，零回归（`r23-targeted.log`）。
- 变异（掏空规划相位）**3 条失败**（`mutation-r23-planphase.log`）；源码逐字节恢复（`B9778EC9E4BB555E`）、无残留（`source-hashes-round23.csv`）。
- **边界：**
  1. **只实现了 R-3**。R-4（配额分级）**未开工** ⇒ **G0-P 完成条件仍未满足，仍未关闭。**
  2. **静态规划相位目前只覆盖 `ControlFlow` 一个阶段。**
     `ControlDependence` 依赖 `Dominance` **运行期**填充的 `_dominanceOverlays`（其 plan 要用到该字段的 `Count`）
     ⇒ **不能**提前规划，仍留在执行相位内（本轮只做了 `Plan*`/`Commit*` 二分，**未前移**）。
  3. `MemberAccess` / `Dominance` / `DataFlow` / `InterproceduralDataFlow` 的 `Plan*` **尚未提取**。
  4. `PartitionedSyntax` / `PartitionedOperation` 未接入。
  5. 规划相位**尚未**被 R.4 消费——本轮只建立了"窗口开始前可知规模"的能力，**未据此配额度**。
  6. `PlanControlFlowStage` 返回 `null`（无操作根）时**不登记** plan，故 `LastStagePlans` 可能缺该阶段；
     这是刻意保留原提前 `return` 的语义，非缺陷。

---

# 附录 V：**R-3 覆盖面扩展与"时点"机制化**——4 个阶段前移，1 个阶段如实声明延迟（2026-09-25 轮次 24）

**性质：附录 U 把规划**相位**建立起来，但只覆盖 `ControlFlow` 一个阶段（U.6 边界 2、3）。
本附录把覆盖率扩到 4 个阶段，并把"时点"从**注释约定**升级为**fail-closed 机制**。**

## V.1 判据前置：什么才算"可静态规划"（不靠阶段名猜）

附录 U 只前移了 `ControlFlow`，其余阶段是否"不能前移"当时**未经源码确证**。本轮逐阶段读源码，判据是：

> **plan 的输入是否只来自语法/语义模型、既有缓存与无状态构造器**
> ——即**不读任何前序 pass 的运行期产物**。

按此判据得到的结论（每条都有源码位置）：

| 阶段 | 判定 | 输入 |
| --- | --- | --- |
| `ControlFlow` | ✅ 可前移 | `GetOperationRootPlans` + `AssembleWorkBatches` |
| `MemberAccess` | ✅ 可前移 | 同上（其**事实抽取**仍留提交步，见 V.3） |
| `Dominance` | ✅ 可前移 | 同上；真正的运行期依赖只在 `AnalyzeDominanceRoot`（**计算**相位） |
| `CallGraph` | ✅ 可前移 | `context.InvocationOperations` / `PropertyReferenceOperations` 两个**快照** |
| `ControlDependence` | ❌ **设计上不能** | 依赖 `Dominance` **运行期**填充的 `_dominanceOverlays`（plan 规模 = 该字段 `Count`） |
| `DataFlow` | ⚠ **可以但本轮未做** | `AssembleDataFlowWorkBatches(CreateDataFlowMethodPartitions(OperationInventory))`，两者对 `OperationInventory` 都是纯的 |
| `InterproceduralDataFlow` | ➖ **N/A** | 无 `CpgWorkBatch` 形态的 plan（纯转发，不走批次执行器） |

⚠ **`DataFlow` 一行是本轮最重要的诚实标注**：它**技术上前移是可能的**，
未做的原因是编辑边界（该文件存在并发写入风险，本轮不触碰），**不是**技术不可能。
若把它与 `ControlDependence`（真正的设计约束）混为一谈，就会把**遗留缺口**谎报成**已关闭**。

## V.2 一次被测试抓住的过度声明：**"先于任何 worker"是错的**

本轮首版注释写的是"在任何 worker 启动之前"。**这是不准确的**：

规划相位位于 `Build` 的第 431 行附近，而**其上方**的 `Operation` 阶段
（`RunPartitionedOperationPass` → `...WithWorkBatches`，`PartitionedOperationPass.cs:158`）
**自己已经用过 `_workBatchExecutor`**。

⇒ 本相位实际保证的是「先于**全部后置 pass 阶段**的 worker」，
**不是**「先于进程内任何 worker」。

这不是措辞细节：`CallGraph`/`MemberAccess`/`Dominance` 的 plan 恰恰**需要** Operation 阶段的产物
（`InvocationOperations` / `PropertyReferenceOperations` / `OperationInventory`）。
若真按"先于任何 worker"去实现，就必须把这些输入提前到 Operation 之前——那是另一件事，且会改变构建结构。
注释与附录 U.6 边界 5 已按精确边界修订。

## V.3 交付物

| 文件 | 变更 |
| --- | --- |
| `StagePlan.cs` | 新增 `StagePlanTiming` 枚举（`StaticBeforeExecution` / `DeferredUntilRuntimeInputs`）；`StagePlan` 增 `Timing` + `RequiresRuntimeInputFrom`，及 `WasPlannedBeforeExecution` / `IsSelfConsistent` 自校验 |
| `NLCPGBuilder.cs` | 规划相位扩到 4 阶段；`RecordStagePlan` 增**两条 fail-closed 校验**；`_planningPhaseState` **三态** |
| `CallGraphPass.cs` | `Plan*`/`Commit*` 二分；抽出 `BuildCallGraphOperationWork` 使**规划与提交共用同一套稳定编号** |
| `MemberAccessPass.cs` | `Plan*`/`Commit*` 二分；事实抽取**留在提交步** |
| `DominancePass.cs` | `Plan*`/`Commit*` 二分 |
| `ControlFlowPass.cs` | `Run*` 改为消费 plan，并补 fail-closed 守卫（V.4 末段） |
| `ControlDependencePass.cs` | 规划仍在执行相位，但**如实登记** `DeferredUntilRuntimeInputs` + `RequiresRuntimeInputFrom: Dominance` |
| `tests/…/StagePlanContractTests.cs` | 5 → 11 条用例 |

**两处刻意不做的事（避免"顺手扩大范围"）：**

1. **`MemberAccess` 的事实抽取未前移**。规划只产出批次与预估（只读、廉价）；
   事实抽取会遍历全部字段/属性访问操作，前移会改变构建期的内存与耗时分布——超出「只前移规划时点」。
2. **`CallGraph` 的稳定编号只写一份**（`BuildCallGraphOperationWork`）。
   若规划与提交各写一份排序，提交步会按**错误的序号**取操作——那是**静默错配**而非编译错误。

## V.4 把"时点"从注释升级为机制

附录 U 的判据是**相位快照**（`PlanSnapshotAtEndOfPlanningPhase` 非空）。
它能抓住"整体掏空规划相位"，但**抓不住单阶段的谎报**：某阶段声称已前移、实则没有，
只要它在规划相位内被登记过就看不出来。

本轮因此给 `RecordStagePlan` 加了两条 fail-closed 校验：

1. **自洽性**：`StaticBeforeExecution` 不得同时声明 `RequiresRuntimeInputFrom`，反之亦然。
2. **声明与相位一致**：只有**规划相位开放中**登记的 plan 才能声明 `StaticBeforeExecution`；
   其余情况（构建未开始 / 相位已关闭）声称静态即抛。

第 2 条要求相位状态是**三态**而非 `bool`：`NotStarted` 与 `Closed` 对"静态前移"的含义不同——
前者无从判定，后者是明确的不符。压成一个 `bool` 就无法把"构建外部的孤立登记"
与"执行相位内的假前移"区分开。（相位用 `try/finally` 闭合：规划中途抛异常时相位也必须闭合。）

**此外，4 个消费侧入口的"取不到 plan"一律 fail-closed**（`ControlFlow` / `MemberAccess` /
`Dominance` / `CallGraph` 对称处理）：`Plan*` 返回 `null` 的**唯一**合法成因是无操作根/无可分析操作，
故取不到 plan 时先复查该前提——若仍有输入，说明规划相位没有覆盖本阶段，**抛异常**而非静默跳过。
这与附录 R.1 ① 的原始缺口同源：静默跳过整阶段的边，正是 G0-P 要消灭的失效形态。
刻意**不**在消费侧重新规划：那会把规划时点悄悄拉回执行相位。

## V.5 变异验证（两条，均被抓住）

| 变异 | 结果 |
| --- | --- |
| ① 不登记 `CallGraph` 的 plan（`if (false && …)`） | **2 条失败**（9 通过 / 2 失败）+ `RunCallGraphPass` 的 fail-closed 守卫抛 `InvalidOperationException`（`mutation-r24-callgraph.log`） |
| ② `ControlDependence` 谎报 `StaticBeforeExecution` | **3 条失败**（14 通过 / 3 失败），`RecordStagePlan` 抛"规划时点声明与实际不符"（`mutation-r24-falsecred.log`） |
| ③ 不登记 `ControlFlow` 的 plan（同 ①，验证 V.4 末段的守卫**非空转**） | `RunControlFlowPass` 的守卫抛 `InvalidOperationException`（同一形态，未单列日志） |

**变异 ② 的意义**：它正是附录 U 的旧判据**抓不住**的形态——单阶段谎报而非整体掏空。
追加的 3 条用例（V.3 表中 `Timing` 声明、延迟诚实性、相位一致性）才有鉴别力。

两次变异后均逐字节恢复：`source-hashes-round24.csv` 与恢复后哈希完全一致，且无 `if (false` 等残留标记。

## V.6 回归证据与边界

- `~StagePlanContractTests` **11/11**（`r24-plan-tests.log`）。
- **针对性**回归（5 个测试类）**99/99 全绿**（`r24-targeted.log`）。
- **全 Cpg 过滤器**回归 **575/575 全绿**，零失败（`r24-cpg-regression3.log`；
  此前同一过滤器在 `source-hashes` 冻结前为 **580/580**——计数差异来自并发写入者同期增删测试，非本轮引入）。
- 源码哈希留证：`source-hashes-round24.csv`（冻结于 575/575 之后）。
  恢复校验：两次变异后逐字节恢复，`ControlFlowPass.cs` 与 `MemberAccessPass.cs` 因追加守卫而
  相对 `r24-plan-tests.log` 时期发生变化（属本轮正常改动），无 `if (false` 等残留标记。
- ⚠️ **过程记录（并发写入者）**：本轮执行期间检测到**另一写入者在同一工作区**并发修改
  `NLCPGBuilder.cs`（我改过的同一文件）与 `InterproceduralDataFlowPlanGroup.cs` /
  `MethodDecorationPass.cs` / `InterproceduralPlanCompactionTests.cs`（另一 feature：
  interprocedural-plan-compaction）。
  期间出现过**一次非确定性失败**（`InterproceduralPlanCompactionTests.PublishedSlots_RetainNoResidualSortRowsAfterClear`
  报 `Expected: 0 / Actual: 8191`，且同一过滤器再次运行时变成 27 条失败并报**不同的异常类型**）。
  该失败**不可复现**，且在**重新构建后 580/580 全绿** ⇒ 判定为**构建中途 DLL 被并发重建**造成的干扰，
  **不是**本轮改动引入的回归。记录于此以便复核：`r24-cpg-regression.log`（含该次失败）与
  `r24-cpg-regression2.log`（全绿）为同一过滤器的前后两次运行。
  另需注意 `NLCPGBuilder.cs` 的哈希在本轮内由 `4EF83778E47BFA6B`(仅我的改动) 变为
  `70DF54D0B8D8B018`(叠加并发写入者的改动)，故 V.3/V.4 引用的行号以当前文件为准。
- **边界：**
  1. **R-4（配额分级）仍未开工** ⇒ **G0-P 完成条件仍未满足，仍未关闭。**
     规划相位**仍未被消费**——本轮只把"窗口开始前可知规模"的能力从 1 个阶段扩到 4 个，**未据此配额度**。
  2. **`DataFlow` 未前移**，且原因是**编辑边界而非技术不可能**（见 V.1 表）⇒ 这是 R-3 的**遗留缺口**。
     ⚠️ **该缺口已由附录 X 关闭**（`DataFlow` 已前移）。**引用本条时须同时读附录 X**——
     V 轮次的"未前移"已过期。
  3. **`ControlDependence` 按设计不能前移**（依赖 `Dominance` 运行期产物）。
     本轮只做到"如实声明"，**未**改变其时点；它仍是唯一在执行相位内规划的阶段。
  4. `PartitionedSyntax` / `PartitionedOperation` **未接入**规划相位。
     后者的 `AssembleWorkBatches` 与上述阶段同类，理论上同样可前移。
  5. 规划相位的作用域是「先于**后置 pass 阶段**的 worker」，**不是**「先于任何 worker」（见 V.2）。
  6. 无操作根时 `Plan*` 返回 `null` 而**不登记** plan（保留原提前 `return` 语义），
     故 `LastStagePlans` 可能缺该阶段。这使"未登记"有**两种**成因——
     能力位未开启、或无操作根；两者都与"已前移但登记空壳"不同（后者被 V.4 的校验拦住）。
  7. 契约测试中 `CallGraph` 相关断言必须用**含调用/属性访问**的输入
     （`CallSiteSource`）；用原 `Source`（只有赋值与返回）会因 plan 为 `null` 而**误判为已前移**。
     这是本轮首版测试的真实失败原因，已固化为独立测试输入。

---

# 附录 W：G0-P R-4 配额作用域分级（2026-09-25 轮次 25）

对应 ③（`StageQuotaScope`）、④ 的设计原文见 :1791-1795。
**R-4 此前未开工**，V.6 边界 1 的"规划相位仍未被消费"是本轮的直接目标。

## W.1 先界定：R-4 做的是什么，**不是**什么

**做的是"作用域"，不是"大小"。** 这两件事在附录 R 的措辞里紧邻，极易被合并成
"给每阶段配内存额度"，而后者**本轮刻意不做**：

- 附录 G 已**实测**`Estimate(startLine, endLine)` 的自变量**只有行号**，
  同一行范围下真实载荷跨度 **1349×** ⇒ **不存在**任何常量 k 使"行数 × k"成为上界。
- 计划 :114 要求"校准后才作硬额度"。
- 故任何"给 X 阶段配 Y 字节"的数字都只能是**凭空造的**——正是 :114 点名禁止的形态。

**本轮的唯一变量是"该阶段用哪个池"。** 且所有阶段仍为 `Shared`，
故**生产行为逐字不变**（W.6 边界 1）。

## W.2 交付物

| 文件 | 内容 |
| --- | --- |
| `src/NLCPG/Builder/StageQuotaAllocation.cs`（新增） | `StageQuotaScope{Shared,Dedicated}`、`StageQuotaBasis`、`StageQuotaCalibration`、`StageQuotaGrant`、`StageQuotaPolicy`、`StageQuotaAllocation` |
| `src/NLCPG/Builder/StagePlan.cs` | `IStagePlan<TBatch>` 增补 `EstimatedBytes`（对应设计 R.2 ① 的 `PlannedBytes`） |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | 新增 `PlansAtEndOfPlanningPhase`（快照副本）与 `LastStageQuotaAllocation`（消费结果） |
| `tests/NLISSN.ContractTests/Cpg/StageQuotaScopeContractTests.cs`（新增） | 12 条 |

## W.3 关键点：消费的是**快照**，不是活字段

R-3 只建立了"窗口开始前可知规模"的**能力**，没有任何消费者据此配额度
（= V.6 边界 1 记的"规划相位仍未被消费"）。本轮加的消费者有一个刻意的设计选择：

```csharp
// 冻结一份规划产物的不可变副本；配额分级消费这个副本，而不是读仍可变的 _stagePlans。
PlansAtEndOfPlanningPhase = new Dictionary<Stage, IStagePlan<CpgWorkBatch>>(_stagePlans);
_stageQuotaAllocation = StageQuotaPolicy.Resolve(PlansAtEndOfPlanningPhase, …);
```

**为什么必须复制：** `_stagePlans` 在执行相位仍会被延迟规划的阶段继续写入。
若直接读它，"消费的是规划相位快照"与"读一个之后还会变的活字段"**在观测上不可区分**——
于是"规划真的前移了"与"配额据此分级了"就可能各自漂移。
复制之后，两者由**同一个事实**驱动（W.5 变异 ① 专门验证这一点）。

## W.4 "缺席"的成因必须分类（`StageQuotaBasis`）

| 值 | 含义 | 实例 |
| --- | --- | --- |
| `PlannedBeforeExecution` | 规划相位已产出 plan ⇒ 可据以定作用域 | **5 个**：`CallGraph`/`ControlFlow`/`MemberAccess`/`Dominance`/`DataFlow`（DataFlow 见附录 X） |
| `DeferredPlanning` | 依赖前序阶段的**运行期产物** ⇒ 窗口前规模未知（"时候未到"） | `ControlDependence`（需 `Dominance` 的 `_dominanceOverlays`） |
| `NoBatchPlan` | **没有批次型 plan** ⇒ R-3/R-4 对它是 **N/A**，非待办 | `InterproceduralDataFlow`（纯转发，不走批次执行器） |
| `ExecutedBeforePlanningPhase` | **早已执行完** ⇒ plan 概念对它不适用（"时候已过"） | `Syntax` / `Operation`（见下方更正） |

后几者极易在文档里被合并成一句"暂不支持"，但**行动上完全不同**：
`DeferredPlanning` 意味着"将来会前移"（**有**后续工作），
`NoBatchPlan` 与 `ExecutedBeforePlanningPhase` 意味着"R-3 对它不适用"（**无**后续工作）。
故用独立用例把它们分开（W.5 变异 ③ 证明该区分非空转）。

### ⚠️ W.4 更正：`Syntax`/`Operation` 曾被**误分类**（轮次 25 续自查发现）

**W 轮次首版把这两个阶段归入了 `DeferredPlanning`——这是错的**，且错的方式正是
本附录自己要防的那一类：**把"没有 plan"的不同成因合并成一个**。

- 两者**确实**走批次执行器（故在 `BatchPlanCapableStages` 里），
  又**确实**不在规划相位快照里 ⇒ 朴素的三分支实现必然把它们判为 `DeferredPlanning`。
- 但事实相反：`RunSyntaxPass`（`NLCPGBuilder.cs:374`）与
  `RunPartitionedOperationPass`（`:401`）都位于规划相位开启（`:452`）**之前**
  ⇒ 它们是"**时候已过**"，不是"时候未到"。
- **后果不是措辞问题**：把"R-3 不适用"说成"规划被推迟"，
  会凭空造出一项**永远不会被完成**的待办，而 G0-P 的关闭判据正是"还有哪些待办"。

**修法**：新增第四个成因 `ExecutedBeforePlanningPhase`，并新增集合
`StagesExecutedBeforePlanningPhase`。**两个集合都是必需的**——
把它们从 `BatchPlanCapableStages` 删掉会让"这些阶段走批次执行器"这一事实失真；
只有两者并存才能同时表达"走批次执行器"与"在规划相位之前就跑完了"。

**这也解释了规划相位的作用域**：它被定义为"先于**后置 pass 阶段**的 worker"，
而非"先于任何 worker"（附录 V.2）——正因为 Syntax/Operation 在它之前就已经跑过。

**变异验证**：停用该分类（`false && …`）⇒ `BuildFromSource_SyntaxAndOperation_AreClassifiedAsExecutedBeforePlanningPhase`
**失败**（13 通过 / 1 失败）；把两个成因**拍平**（`ExecutedBeforePlanningPhase` → `DeferredPlanning`）⇒
**2 条失败**（上述用例 + 表驱动用例）；恢复后 **32/32** 全绿
（`mutation-r25-basisfix.log` / `mutation-r25-basistable.log` / `r25-basistable.log`）。

**并补了一条表驱动用例** `BuildFromSource_EveryStage_HasItsDeclaredQuotaBasis`——
把全部 **9 个**阶段的期望成因一次钉死，并断言该表**恰好覆盖** `AllStages`。
理由：此前每个用例只断言一两个阶段，"某阶段被悄悄改了成因"仍可能漏网；
表驱动后新增阶段会立刻失败，从而**强制作者显式决定**其成因，而不是让它落进某个默认分支。

`BatchPlanCapableStages` 的每个条目均由 `_workBatchExecutor.*` 调用点逐条确证
（`PartitionedSyntaxPass.cs:112` / `PartitionedOperationPass.cs:158` / `CallGraphPass.cs:107` /
`MemberAccessPass.cs:127` / `ControlFlowPass.cs:80` / `DataFlowPass.cs:530` /
`DominancePass.cs:345` / `ControlDependencePass.cs:107`）。

## W.5 变异验证（三条，均被抓住）

| 变异 | 结果 |
| --- | --- |
| ① 配额改用**空表**而非规划快照（`Resolve(new Dictionary<…>(), …)`） | **3 条失败**（20 通过 / 3 失败：快照一致性、四阶段依据、估算留证）`mutation-r25-notconsumed.log` |
| ② 停用 `Dedicated` 的标定守卫（`if (false && …)`） | **4 条失败**（8 通过 / 4 失败：缺标定 / 标定非正 / 无证据 / 依据不符）`mutation-r25-silentdowngrade.log` |
| ③ 把 `Basis` 三分支拍平为单一值 | **4 条失败**（8 通过 / 4 失败：延迟性、N/A、快照一致性、依据不符）`mutation-r25-basisflat.log` |

三条变异后**均逐字节恢复**：`source-hashes-round25-prefreeze.csv` 与恢复后哈希完全一致，
且无 `if (false` 等残留标记（实测计数 0）。

**恢复之后另做了一处纯注释清理**（`StagePlan.cs`：把一段**错位**的 `IStagePlan` 摘要
从 `StagePlanTiming` 上方移回接口本体——该段描述的是 `Plan*`/`Commit*` 契约，
却悬在枚举前，属文不对题），故 `StagePlan.cs` 最终哈希 `56302D4BE9B95FB1`
**不**等于 prefreeze 的 `2695A75450436F73`；清理后已重跑测试 **23/23** 全绿。
其余三个文件最终哈希与 prefreeze 一致（`StageQuotaAllocation.cs` `46F7E45AC763618E`、
`NLCPGBuilder.cs` `81A3CFC0D4DBE3A8`、测试文件 `F4FDADA4AB82B708`）。
**本次清理不涉及任何行为改动**（仅文档注释归属），故不影响上表三条变异结论。

**变异 ② 的意义：** 它验证的是"**没有被观测面覆盖的守卫等于不存在**"这一形态。
生产策略本轮全为 `Shared`，故那条守卫在生产配置下**永远不会被走到**。
为此 `StageQuotaPolicy.Resolve` 保留一个 `requestedScopes` 覆盖入参**专供测试**——
否则该守卫只能靠读源码确认，而本仓库的既有教训（附录 N.4/P.4）正是这种断言不可信。

## W.6 回归证据与边界

- `~StageQuotaScopeContractTests` **12/12** 全绿。
- **针对性**回归（5 个测试类：`StageQuotaScope`/`StagePlan`/`StageWorkResultAccounting`/
  `StageDependencyTable`/`InterproceduralPlanCompaction`）**78/78 全绿**，零失败（`r25-targeted.log`）。
- **全 Cpg 过滤器**回归 **597/600**，**3 条失败全部**属于上述并发写入者的新文件
  `SyntaxPassTelemetryContractTests.cs`（`r25-cpg-regression.log`）。
  **归因证据（三重）：** ①失败用例名全部含 `SyntaxPassTelemetry`，
  我方 `StageQuotaScope`/`StagePlanContract`/`StageDependencyTable`/`StageWorkResult` 相关失败数 **0**；
  ②同一时刻 `NLCPGBuilder.cs` 内 `SyntaxPassTelemetry` 声明数 **0**（写入者生产侧尚未落地）；
  ③计数由 r24 的 **575** 变为 **600**（+25 全为该新文件），与 `source-hashes` 冻结前
  575 → 580 的扰动同一成因。**故这 3 条不是本轮引入的回归。**
- 源码哈希留证：`source-hashes-round25-prefreeze.csv`（变异前冻结，用于恢复校验）。
- ⚠️ **过程记录（并发写入者，第 2 次）**：本轮期间另一写入者正在把 `SyntaxPassTelemetry`
  从 `SyntaxPass.cs:62` 的嵌套类型提升为 `NLCPGBuilder` 的公开成员，
  **只落了测试侧**（`SyntaxPassTelemetryContractTests.cs` 引用 `NLCPGBuilder.SyntaxPassTelemetry`），
  生产侧尚未暴露该类型 ⇒ 测试工程**一度无法编译**
  （`error CS0246: 未能找到类型或命名空间名"SyntaxPassTelemetry"`）。
  该失败**与本轮改动无关**：已核对我方 4 个文件哈希在同一时刻全部 `OK`（与冻结值逐字节一致），
  且 `NLCPGBuilder.cs` 内查无 `SyntaxPassTelemetry`/`LastSyntaxPassTelemetry` 任何声明。
  待写入者落地后重跑，测试工程恢复可编译，`78/78` 全绿；
  全 `~Cpg` 过滤器下该写入者的 3 条用例仍失败（归因证据见上条）。**记录于此以便复核。**
- **边界（不得越读）：**
  1. **无任何 `Dedicated` 被启用** ⇒ 本轮**不改变**生产内存行为，`Shared` 单池语义与既有逐字一致。
     `Dedicated` 路径**只有**"被守卫拒绝"一侧被测试覆盖，"被授予后真的独占"一侧**未被覆盖**
     （无标定即无从构造，属 W.2 刻意留空）。
  2. **未推导任何字节额度**，`:571` 的"配额作用域"一项只做到**分级机制 + 默认值**，
     **未**做到"每个阶段有额度"。R.6 边界 3（依赖估算校准）**依然成立**。
  3. **规划相位在 W 轮次时未覆盖 `DataFlow`**：它技术上可前移，W 轮次因编辑边界未动。
     ⚠️ **该边界已在同轮次由附录 X 关闭**：`DataFlow` 已前移，其 `Basis` 现为
     `PlannedBeforeExecution`。**引用本条时须同时读附录 X**——W 轮次的"未覆盖"已过期。
     R-3 的遗留缺口仅剩边界 4（`PartitionedSyntax`/`PartitionedOperation`）。
  4. `PartitionedSyntax` / `PartitionedOperation` **仍未接入**规划相位（V.6 边界 4 不变），
     故它们的 `Basis` 同样是 `DeferredPlanning`。
  5. `InterproceduralDataFlow` 的 `Dedicated` 候选地位**未改变**：
     其桥接发布段实测存活 755.3 MiB（`NLCPGBuilder.cs:1440`）与局部 pass 不同量级，
     但它属 `NoBatchPlan` ⇒ 本轮既不能也不需要为它配作用域（见 W.4）。
  6. **估算是估算，不是上界**：`EstimatedBytesAtPlanningTime` 仅作留证，
     **不**参与任何额度决策（有独立用例断言"记录估算值"与"授予作用域"互不影响）。

---

# 附录 X：DataFlow 前移——关闭 R-3 的最后一个具名缺口（2026-09-25 轮次 25）

W.6 边界 3 记录了 R-3 的遗留缺口："规划相位仍未覆盖 `DataFlow`"。
本附录关闭它，并记录一条**行为测试抓不住的变异**及其补法。

## X.1 为何 DataFlow 能前移（逐条源码确证，非推断）

| 规划输入 | 来源 | 是否在规划相位之前就绪 |
| --- | --- | --- |
| `CreateDataFlowMethodPartitions(context.OperationInventory)` | **Operation 阶段**填充 | ✅ 是（Operation 在规划相位之前） |
| `AssembleDataFlowWorkBatches(...)` | 各分区语法树的**行号** + 无状态 `CpgWorkBatchBuilder.Build` | ✅ 是 |

**关键区分：本阶段有"运行期依赖"，但它不在规划里。**
`DataFlow` 真的读运行期状态——CFG 邻接缓存 `_cfgPredecessors/SuccessorsByNode`
（由 `ControlFlow` 写入，依赖④）。但那发生在 **`BuildCfgAdjacency`，即计算相位**。
规划只需要**规模**（批次与行号估算），不需要**内容**。
两条线索一致：`AssembleDataFlowWorkBatches` 只读 `partition.MethodBlock.Syntax` 的行号，
不读任何邻接。

⇒ 把邻接塞进 plan 反而**是错的**：那是把运行期状态偷渡进规划相位。

## X.2 交付物

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | 新增 `PlanDataFlowStage` / `CommitDataFlowStage`；`RunDataFlowPass` 只消费 plan（取不到即 fail-closed） |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `PlanStagesBeforeExecution` 登记 DataFlow plan；新增留证计数 `DataFlowPlanAssemblyCount` |
| `tests/NLISSN.ContractTests/Cpg/StagePlanContractTests.cs` | 新增 6 条（+2 条计数用例） |

**`PlanDataFlowStage` 恒返回非 null**（即使无方法分区）——与 `ControlFlow` 的
"无操作根即返回 null"**不同**，因为本阶段在无分区时仍要执行摘要流
（`AddCallArgumentAndReturnDataFlow`）并回报 `_dataFlowWorkerCount`。
若照抄 `ControlFlow` 的 null 语义，会**改变既有可观测值**。

## X.3 与 `Dominance` 的分工一致

`CommitDataFlowStage` **重新求**方法分区与操作索引（对 `OperationInventory` 的纯派生，
属"输入准备"），但**只消费 `plan.Batches`**——与 `CommitDominanceStage` 同一形态。
分区/索引是"喂给 worker 的输入"，批次是"规划产物"；只有后者必须来自 plan。

## X.4 回归证据

- `~StagePlanContractTests` **17/17**（含 6 条新增）。
- 针对性回归（7 个过滤器：StagePlan / StageQuotaScope / StageWorkResultAccounting /
  StageDependencyTable / DataFlow / CpgWorkBatch / InterproceduralPlanCompaction）
  **251/251 全绿**，零失败（`r25-final-all2.log`）。
- DataFlow 专项回归 **173/173**（`r25-dataflow-regression.log`）⇒ **产物等价**。

## X.5 变异验证：一条**存活**的变异，及它的补法

| 变异 | 结果 |
| --- | --- |
| ① 不登记 DataFlow 的 plan（`if (false && …)`） | **5 条失败**（22 通过），且 `RunDataFlowPass` 的 fail-closed 守卫抛"规划相位没有登记它的 plan"（日志出现 10 次）`mutation-r25-noDataFlowPlan.log` |
| ② 提交步**无视 plan、自行重算批次** | ⚠️ **首次实测：存活**（197/197 全过）`mutation-r25-recompute.log` |

**变异②是一次诚实的失败记录，值得单列。**
重算调用的是**同一个纯函数、同一份分区输入**，故产物与"消费 plan"**逐字等价**——
**任何行为断言都不可能区分它们**（我第一版尝试的"批次计数相等"断言对两者都成立）。
这不是测试写得不好，而是**该变异在行为上不可观测**。

**补法：把"只规划一次"这一既有不变量延伸到批次构造。**
`RecordStagePlan` 本就对重复登记抛异常（既有机制），故"每阶段只构造一次批次"是同源要求。
新增 `DataFlowPlanAssemblyCount` 留证计数 + 2 条用例：
- 请求 DataFlow ⇒ 计数 **必须 = 1**（`0` = 没规划，`2` = 提交步又算了一遍）；
- 未请求 ⇒ 计数 **必须 = 0**（否则"恒为 1"可能只是常量吻合）。

重跑变异② ⇒ **被抓住**：计数读到 `2`，`BuildFromSource_WhenDataFlowRequested_AssemblesBatchesExactlyOnce`
失败（16 通过 / 1 失败，`mutation-r25-recompute3.log`）。

**本条的普遍教训（与附录 N.4/P.4 同源但更进一步）：**
前几轮的教训是"观测面选错"；本轮的教训是"**该差异在行为层根本不存在**"。
遇到后者时，继续找行为断言是徒劳的——必须换到**结构/计数层**，
并且要说明为什么该计数是**既有不变量的合法延伸**，而不是为测试而加的钩子。

## X.6 边界（不得越读）

1. **产物逐字未变**：前移只改变**规划时点**，不改变任何批次参数或图内容
   （DataFlow 专项 173/173 为证）。故本附录**不**声称性能或内存改善。
2. **`PartitionedSyntax` / `PartitionedOperation` 仍未接入**规划相位（V.6 边界 4 不变）。
   后者的 `AssembleWorkBatches` 与已前移阶段同类，理论上同样可前移。
   ⚠️ **但"未接入"不等于"待办会完成"**：这两者在**规划相位开启之前**就已执行完
   （`NLCPGBuilder.cs:374` / `:401` vs `:452`），故其配额成因是
   `ExecutedBeforePlanningPhase` 而**非** `DeferredPlanning`。
   把它们从"未接入"改造成"已前移"**需要重排执行顺序**（让它们晚于规划相位），
   那不是 R-3 的遗留工作，而是另一类改动。**引用本条时勿把它读成"将来会前移"。**
3. **`ControlDependence` 仍是唯一按设计不能前移的阶段**（依赖 `Dominance` 运行期
   `_dominanceOverlays`），其 `Basis` 保持 `DeferredPlanning`。
4. **`InterproceduralDataFlow` 仍是 `NoBatchPlan`**（纯转发，不走批次执行器）。
5. `DataFlowPlanAssemblyCount` 是**留证计数**，不参与任何生产决策；
   它存在的理由已在 X.5 写明（行为层不可观测）。
6. **G0-P 仍未关闭**：四要素中 ③ 仍**未为任何阶段配额度**（W.6 边界 1/2），
   R-3 虽已关闭具名缺口，但边界 2/3 仍在。

---

# 附录 Y：把「L0 只读」从注释变成 fail-closed 不变量（2026-09-25 轮次 26）

## Y.1 为什么做这一项

G0-P 四要素中 ①（静态规划时点）②（结果类型）③（配额作用域）均已落地且各有专项用例，
唯独 ④「**分层**与窗口外依赖」中的**分层**此前**只有一段注释**在声称：

> ⚠ 安全性前提（已实测）：整条规划链是【纯函数】——`AssembleWorkBatches` 只读 context
> 并调用 `_workBatchBuilder.Build`，`CpgWorkBatchBuilder.Build` 无实例状态变更；
> 两者均不写图。故提前规划不改变任何行为。

这正是附录 N.4/P.4 反复踩到的形态：**把一条性质写成声明，而不是机制**。
该性质一旦被破坏（例如某个 `Plan*` 顺手写了一条边），唯一发现途径是
"某个测试恰好断言了图内容"——而多数阶段**并无**这类断言。
对照 R-1 的判据：依赖表把"靠书写顺序"升级为"启动即拒绝"，本附录做的是同一件事，
只是对象从**阶段顺序**换成了**层次职责**。

## Y.2 交付物

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Model/NLCPGGraph.cs` | 新增只读窗口（`EnterReadOnlyWindow`/`ExitReadOnlyWindow`）、`EnsureMutable` 内 fail-closed 守卫、留证计数 `ReadOnlyWindowEntryCount` |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | 规划相位用 `try/finally` 开闭只读窗口；原"已实测"注释改为指向机制 |
| `tests/NLISSN.ContractTests/Cpg/PlanningLayerReadOnlyContractTests.cs` | **新增 8 条** |

**守卫位置选在 `EnsureMutable`** 是刻意的：它是**全部**构图入口
（`AddNode`/`AddEdge`/`AddKnownNodeCartesianEdges`/`ImportMutableFacts`/`RegisterSource`）
共用的唯一收口，故守卫不可能被某个新入口绕过。

**窗口用深度计数而非 `bool`**：`bool` 下嵌套窗口的内层退出会把外层一并关掉，
从而**静默**留下一个不再受保护的外层窗口。该缺陷在单层用例下完全不可见，
故专门有一条嵌套用例。

## Y.3 一个被自查纠正的守卫误挂

首版把守卫留在 `EnsureMutable` 里，而 `SnapshotMutableFacts`（取快照，**纯读**）
也调用该收口 ⇒ 在只读窗口内取快照会被**误报**为"分层被违反"。
**错误的诊断比没有守卫更糟**：它把排查引向"谁在写图"这个根本不存在的问题。

已改为：`SnapshotMutableFacts` 不再走该守卫（保留冻结检查），因为附录 R.3 把 L0
定义为「**只读**」——**读是 L0 的职责**，被拦的只应是写。
并补用例 `SnapshotMutableFacts_InsideReadOnlyWindow_IsAllowedBecauseItOnlyReads` 钉住该区分。

## Y.4 变异验证（两条，方向相反，均被抓住）

单测"守卫会抛"是不够的：**把守卫删掉**与**把窗口关掉**是两种不同的退化，
且后者会让"守卫有效"的全部用例**依然全绿**（因为没人进窗口 ⇒ 守卫是死代码）。
故两个方向各测一次：

| 变异 | 结果 |
| --- | --- |
| ① 规划相位**不进入**只读窗口（`if (false) EnterReadOnlyWindow();`） | **恰好 1 条失败**（`BuildFromSource_EntersReadOnlyWindowExactlyOncePerBuild`），其余 6 条通过 `mutation-r25-windownotentered.log` |
| ② **停用守卫**（`if (false && _readOnlyWindowDepth > 0)`） | **3 条失败**（`AddNode…` / `AddEdge…` / 嵌套用例）`mutation-r25-guardremoved.log` |

变异①"恰好 1 条失败"这个**数字本身**是结论：它证明在加入该用例之前，
**没有任何其他用例**能区分"守卫存在且被执行"与"守卫存在但从未执行"。
这正是附录 X.5 的同源教训——行为层不可观测的差异只能靠**计数层**判别。

## Y.5 回归证据

- `~PlanningLayerReadOnlyContractTests` **8/8**（`r25-readonly3.log`）。
- 针对性回归（8 个过滤器：上述 + StagePlan/StageQuotaScope/StageWorkResultAccounting/
  StageDependencyTable/DataFlow/CpgWorkBatch/InterproceduralPlanCompaction）
  **258/258 全绿**，零失败（`r25-readonly-final.log`）。较上轮 251 ⇒ **+7 全为新增、零回归**。
- 图级回归（`NLCPGGraph`/`CpgFragmentReducer`/`FrozenNode`/`CpgGraphValidator`/
  `NLCPGNodeId`/`PendingEdgeOrdinalization`）**71/71 全绿**（`r25-readonly-graph.log`）。
- 源码逐字节恢复，无 `if (false` 残留。

**⇒ 产物逐字未变**：只读窗口只在规划相位开启，而规划相位内本就无构图调用
（否则守卫会抛、上述回归会红）。故本附录**不**声称性能或行为改善。

## Y.6 边界（不得越读）

1. **只覆盖"规划相位"这一个窗口**。执行相位（L2 归并层）**不在**窗口内——
   它本来就要写图。窗口是**窗口**，不是全局开关；有专门用例断言退出后构图恢复正常。
2. **不保证 L1（计算层，worker 内）只产 fragment 不写图**。设计 R.3 的 L1 约束
   **仍未**被机制强制——那需要改 `_workBatchExecutor` 的契约，属另一类改动。
3. **不覆盖"窗口外依赖"的第三类**（`FreezeQueryIndex`/持久化必须在所有阶段提交后）。
   该类依赖**仍无**机制，只有 R.4 的文字描述。
4. `ReadOnlyWindowEntryCount` 是**留证计数**，不参与生产决策。
5. **G0-P 仍未关闭**：四要素中 ③ 仍**未为任何阶段配额度**；
   ④ 的"分层"仅完成 **L0 一处**，"窗口外依赖"三类中仅第 1 类（阶段间）有机制。

---

# 附录 Z：把「窗口外依赖」的声明与权威表对账（2026-09-25 轮次 27）

## Z.1 缺口：同一事实两处声明，无人对账

`StagePlan.RequiresRuntimeInputFrom` 是 R-3 引入的、**唯一**描述"规划为何不能前移"的字段。
但它的消费者在此前**一个都没有**（全仓仅 `IsSelfConsistent` 与异常消息两处引用）——
即它是**只写不读**的。与此同时，`StageDependencyTable.RequiredPredecessors` 里
**已经有一条同样的登记**：

| 声明处 | 内容 |
| --- | --- |
| `StageDependencyTable.RequiredPredecessors[ControlDependence]` | `{ Dominance }` |
| `ControlDependencePass.PlanControlDependenceStage` | `RequiresRuntimeInputFrom: Dominance` |

两者**互不校验**，于是有两种漂移无人发现：

1. **凭空造依赖**：plan 可以点名一个**根本不是自己前置**的阶段，没有任何检查。
2. **表改了、计划没改**：权威表里那条被改动后，计划里的旧声明不会跟着失败。

这正是附录 N.4/P.4 的"同一事实两处声明、无人对账"形态——
与 R-1 用声明式表取代书写顺序是**同一类**问题，只是对象换成了"窗口外依赖的来源"。

## Z.2 交付物

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `RecordStagePlan` 内新增**两条 fail-closed 对账** |
| `tests/NLISSN.ContractTests/Cpg/StagePlanContractTests.cs` | 新增 **5 条** |

**两条对账**（都放在 `RecordStagePlan` 这一**登记唯一收口**上，与 R-1 的拓扑校验同源）：

1. **来源必须真是权威表登记的该阶段前置**——把"名字合法"收紧为"**关系**合法"。
2. **来源必须在本**次**构建中真的执行过**——把声明从"静态名字"升级为
   "**关于本次运行期事实的断言**"。

第 2 条正是关键：只做第 1 条验证的是静态关系，而 `RequiresRuntimeInputFrom`
声称的语义是**运行期**依赖。一个 plan 完全可以"点名一个合法的前置"，
却在一次**根本没跑过该前置**的构建里被登记。

## Z.3 一次自查找出的误报（诊断正确性）

第 2 条首版**无条件**判定，立刻打红了一条既有用例
（`RecordStagePlan_WhenSameStageRecordedTwice_Throws`）：它在**从未构建过**的 builder 上
登记延迟 plan，于是"执行历史为空"⇒ 被报成"来源未执行"。

**但那是误报**：构建尚未开始时**不存在**执行历史，
"无从判定"被错说成了"确实违反"。**错误的诊断会把排查引向不存在的问题**
（与附录 Y.3 的守卫误挂同型）。已改为**仅在规划相位闭合后**判定——
生产路径上延迟规划的登记必然发生在执行相位内，故该守卫**覆盖真实路径**，
同时不再误伤"尚未开始"的契约测试。

## Z.4 变异验证（两条均被抓住）

| 变异 | 结果 |
| --- | --- |
| ① 停用"来源须是登记前置"（`if (false && …)`） | **1 条失败**（`RecordStagePlan_WhenRuntimeSourceIsNotADeclaredPredecessor_Throws`）`mutation-r27-nopredcheck.log` |
| ② 停用"来源须已执行"（`if (false && …)`） | **1 条失败**（`RecordStagePlan_WhenRuntimeSourceDidNotRunInThisBuild_Throws`）`mutation-r27-noruncheck.log` |

两条对账各有**成对**用例（非法被拒 + 合法放行），故"能抛异常"被收紧为"**按关系/按事实**判定"。

## Z.5 ⚠️ 一次**否定**结果：本机制**抓不住**附录 Q.4 的重排

诚实记录：我曾用**真实的 Q.4 重排**（对调 `Dominance`/`ControlDependence` 两行）
验证第 2 条，结果**第 2 条从未被触发**（`mutation-r27-q4reorder.log`，2 条失败但
异常消息全部来自 **R-1 的 `EnsureStageDependencyOrder`**）。原因有两条，均为设计事实：

1. **R-1 的守卫先触发**：全部 pass 跑完后 `EnsureStageDependencyOrder()` 对照权威表校验，
   重排在该点被拒 ⇒ **根本走不到**本附录的对账。
2. **即便走到也不会触发**：`PlanControlDependenceStage` 在 `_dominanceOverlays.Count == 0`
   时**返回 null**，压根不登记 plan ⇒ 对账无从执行。

**故本附录的抗重排价值为 0**——那一层已由 R-1 覆盖。本附录真正新增的是
**Z.1 的两种漂移**（凭空造依赖、表改了计划没改），它们**没有**任何其他守卫覆盖。
**不得**把本附录说成"又加了一道防重排的保险"。

## Z.6 边界（不得越读）

1. **只覆盖"延迟规划的来源"这一处**。设计 R.4 的三类窗口外依赖中，
   第 2 类（跨文件）、第 3 类（`FreezeQueryIndex`/持久化必须在全部提交后）**仍无机制**。
2. **不改变任何执行顺序与产物**——纯校验设施（与 R-1 同性质）。
3. **不替代 R-1**：阶段顺序由 `StageDependencyTable` + `EnsureStageDependencyOrder` 强制；
   本附录只保证"plan 里那句窗口外依赖的声明"与权威表**一致**。
4. 第 2 条对账**只在规划相位闭合后**判定（见 Z.3），故"构建尚未开始"时不生效。
5. **G0-P 仍未关闭**：③ 仍未配任何额度；④ 的分层仅 L0，窗口外依赖三类中
   第 1 类（阶段间）由 R-1 强制、其**来源声明**由本附录对账，第 2/3 类仍无机制。

---

# 附录 AA：R-1 自身的"声明代替机制"补漏（2026-09-25 轮次 28）

## AA.1 缺口：权威表里有两条边**永远不可能失败**

`StageDependencyTable` 声明了 9 个阶段，其中两条边涉及 `Syntax`/`Operation`：

| 声明处 | 边 |
| --- | --- |
| `RequiredPredecessors[Operation]` | `{ Syntax }` |
| `RequiredPredecessors[CallGraph]` | `{ Operation }` |

但这两个阶段在本 builder 中**不**经 `RunOptionalPass` 执行，而是由 `MeasureStage` 包裹
（它们是建图起点，先于所有后置 pass）。而 `_executedStageOrder.Add` 此前**只有**
`RunOptionalPass` 一个入口 ⇒ 两者**从未**进入被校验的序列。

**后果**：`TryValidateOrder` 对"缺席的前置"的处理是
`if (!position.TryGetValue(predecessor, out var predecessorIndex)) continue;`
——这条语义**本身是对的**（能力未开启时该阶段整体不运行，视为已满足），
但它让上述两条边**恒真**：无论怎么改执行顺序，`Syntax`/`Operation` 都不在序列里，
永远不会被判为"顺序错误"。

这与附录 N.4/P.4、Y.4 同型：**声明在，机制不在**。
更值得注意的是——**它出在 R-1 自己身上**（R-1 正是为消灭这类形态而建的）。

## AA.2 交付物

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `MeasureStage` 增 `recordedStage` 参数并补记；`_executedStageOrder.Clear()` **前移到方法开头**；新增 `LastExecutedStageOrder` 只读观测面 |
| `tests/NLISSN.ContractTests/Cpg/StageExecutionRecordingTests.cs`（新） | 4 条 |

**关键实现判断：`Clear()` 必须前移。** 原 `Clear()` 位于后置 pass 之前（`:427`），
它在 `Syntax`/`Operation` 执行**之后**——若只加补记而不动它，
本行会在两个阶段刚被记录后立刻抹掉，**补记等于没做**。这是本改动唯一的非局部影响。

**为什么必须新增观测面：** "从未记录"与"没有机制"在**行为层完全等价**——
去掉补记后，全部行为用例（图内容、plan 快照、配额表）**仍然全绿**
（与附录 X.5/Y.4 同型）。故判据只能落在**序列层**：断言
`LastExecutedStageOrder` **确实包含** `Syntax`/`Operation` 且**相对位置正确**。

## AA.3 验证

| 项 | 结果 | 日志 |
| --- | --- | --- |
| `~StageExecutionRecordingTests` | **4/4** | `r28-rec.log` |
| 变异：删去补记（`if (false && …)`） | **3/4 失败**（3 条依赖记录，1 条不依赖） | `mutation-r28-rec.log` |
| 针对性回归（4 个过滤器） | **50/50** | `r28-tight.log` |
| 源码逐字节恢复 | SHA16 `0BA7F335AFB3F864`，`if (false` 残留 0 | — |

变异**恰好 3/4** 是本用例集**判据正确**的证据：第 4 条
（`…RecordedOrderSatisfiesTheAuthoritativeTable`）断言的是"真实构建**不误报**"，
它在补记被删除后**理应仍然通过**——若它也失败，说明该用例测的不是它声称的东西。

## AA.4 边界（不得越读）

1. **不改变任何执行顺序与产物**——只是把既有的执行事实**如实记进**被校验序列。
   补记前的两条边本就成立（`Syntax` 真的先于 `Operation`），本附录让它们**可被证伪**。
2. **不扩展依赖表**：表中仍只有已实测的 6 条（+2 条建图起点边），本附录不新增依赖。
3. **`MethodModel`/`Persistence*`/`StreamingBasePublish` 仍不进序列**——它们**不是**
   权威表登记的阶段，如实不记录。故"未请求 `MethodModel` 时 `Operation` 不出现"由用例守住。
4. **G0-P 仍未关闭**：③ 仍未配额度；④ 的分层仅 L0，L1 未被机制强制，
   窗口外依赖第 2/3 类仍无机制。

---

# 附录 AB：L1 计算层「worker 不写共享图」由机制强制（2026-09-25 轮次 29）

## AB.1 缺口：L1 此前**只有注释**

附录 R.3 把分层定义为 L0 规划层（只读）/ L1 计算层（worker 内只算 fragment，不写共享图）/
L2 归并层（`Commit*`/reducer，**唯一写图者**）/ L3 发布层。轮次 26 已用只读窗口把 **L0** 变成
fail-closed，但 **L1 一直没有机制**——它的"只算不写"仅由注释与书写习惯维系，例如：

> `PartitionedSyntaxPass.cs:153`「这里只产出缓存数据，不创建图节点，**避免 worker 线程污染共享状态**。」

这正是附录 N.4/P.4/Z.1 反复出现的**「声明代替机制」**：worker 真去写共享图时，
**没有任何东西会失败**，除非某个测试恰好断言了图内容。

**且这不是理论风险——本轮实测到了它的实际后果。** 见 AB.4。

## AB.2 交付物

| 文件 | 改动 |
| --- | --- |
| `NLCPGGraph.cs` | 新增 worker 计算窗口（`EnterWorkerComputeWindow`/`ExitWorkerComputeWindow`/`IsInWorkerComputeWindow`/`WorkerComputeWindowEntryCount`）+ `EnsureMutable` 内守卫 |
| `CpgWorkBatchExecutor.cs` | `ProcessBatchInWorkerWindow` 把每次 `processBatch` 调用包进窗口；可选钩子注入 |
| `NLCPGBuilder.cs` | 构造 executor 时把钩子接到**当前构建**的图 |
| `WorkerComputeLayerContractTests.cs`（新） | 11 条 |

## AB.3 两个**实测得出**的设计约束（不是保守起见）

⚠ 这两条都是先查证再定案的，若按直觉做会立刻打断现有构建：

**① 窗口必须按【图实例】隔离。** worker 内会**新建私有 `localGraph` 并写它**——
`ControlFlowPass.cs:94`、`ControlDependencePass.cs:125`、`DominancePass.cs:451` 各一处。
那是 fragment 的构造过程，属 L1 的**职责**。若做成"worker 期间禁写任何图"，
会当场打断这三个阶段。由于计数按实例存放，`localGraph` 自身深度为 0，天然放行，**不需特判**。

**② 窗口必须按【线程】隔离。** executor 先起 `reducerTask`（`:494`）再起 workers（`:502`），
二者**并发**；而 reducer 正是 L2、**必须**能写共享图。若用图级/进程级计数，
DataFlow 的合法归并写入会被误报——那比没有守卫更糟，因为诊断信息会把排查
引向"谁在写图"这个根本不存在的问题（与轮次 26 那次 `SnapshotMutableFacts` 误报同型）。
故用 `ThreadLocal<int>`。计数由多 worker 并发递增，用 `Interlocked`（L0 那个是单线程的，可直接 `++`）。

**③ 落点选 executor 而非逐个 pass。** 8 个阶段全部经 `processBatch` 这一条通道；
逐个接线会让"新阶段忘了加守卫"退化成又一处"声明代替机制"。变异 3 专门验证了
这一收口是**承重**的（AB.5）。

## AB.4 先做的静态审计，及一条**实测到的真实后果**

加守卫前逐一确认 8 个 worker 委托都只产只读事实（否则守卫一上线就误报）：

| 阶段 | worker 产出 | 写图者 |
| --- | --- | --- |
| Syntax | `SyntaxPartitionResult`（只读事实字典） | reducer |
| Operation | `OperationPartitionResult` | reducer（`:177-222`） |
| CallGraph | `CallGraphWorkBatchResult` | `PublishCallGraphWorkBatch`（`:113`） |
| MemberAccess | `MemberAccessFact[]` | reducer（`:133`） |
| ControlFlow | `LocalCpgFragment`（私有图） | `PublishControlFlowFragments` |
| ControlDependence | `LocalCpgFragment`（私有图） | `CpgFragmentReducer`（`:117`） |
| Dominance | `DominanceWorkBatchResult` | `:360+` |
| DataFlow | `DataFlowWorkBatchResult` | `CommitDataFlowWorkBatch`（`:725`） |

结论：**L1 当前行为上成立、机制上为零。**

**实测后果（本轮意外获得）：** 新增用例 `ExecuteAsync_WithoutWindowHook_BehavesAsBefore`
首版用 `MaxDegreeOfParallelism = 2` 让 worker 在无钩子时并发写同一个共享图，结果
**3 次 `AddNode` 只留下 2 个节点**——`NLCPGGraph` 的构图态容器（`List`/`Dictionary`）
**非线程安全，丢写且不抛异常**。这正是本守卫要防的形态：**静默数据损坏**，
而不是崩溃。该用例已改为单 worker（用例本身不应制造它要防的竞争）。

**诚实边界：** 本守卫守的是**共享图**，不是全部共享状态。worker 仍会合法触及
builder 实例字段，例如 `_baseTypeCache` 是 `ConcurrentDictionary`
（`NLCPGBuilder.cs:60`）——那是**有意**线程安全的缓存，属 L1 允许的读侧。

## AB.5 验证

| 项 | 结果 | 日志 |
| --- | --- | --- |
| `~WorkerComputeLayerContractTests` | **11/11** | `r29-worker2.log` |
| 针对性回归（9 个过滤器） | **91/91** | `r29-tight.log` |
| `~Cpg` 全量 | **642/642**，守卫消息命中 **0** 次 | `r29-cpg-final.log` |
| 变异 1：`EnsureMutable` 守卫改 `if (false)` | **4 失败**（守卫用例；计数用例正确存活） | `mutation-r29-guardonly.log` |
| 变异 2：builder 不接钩子 | **3 失败**（防死代码用例） | `mutation-r29-nowiring.log` |
| 变异 3：executor 绕过收口直接调 `processBatch` | **4 失败** | `mutation-r29-bypass.log` |

三项变异**互不覆盖**且各自只杀死对应用例——这是判据分层正确的证据。
源码三次均逐字节回滚，无 `if (false` 残留。

## AB.6 边界（不得越读）

1. **不改变任何执行顺序与产物**——守卫只在"worker 写共享图"这一**本不该发生**的
   情况下抛出。642/642 且守卫消息 0 命中即证据。
2. **只覆盖 L1 的"不写图"这一半。** L1 的"只算 fragment"还包含"不共享可变中间态"，
   本附录未涉及。
   ⚠️ **该状态已过期**：轮次 31 已补上第二半（`WriteSharedState` 单一写入口），见附录 AD.4。
3. **不覆盖窗口外依赖第 2 类（跨文件）与第 3 类（`FreezeQueryIndex`/持久化须在全部提交后）**，
   仍无机制（Z.6 边界 1）。
   ⚠️ **该状态已过期**：轮次 31 已对二者定性——第 2 类判为**空真满足**（无跨文件单图构建路径），
   第 3 类拆半后"冻结后不得再写"本就有机制（`EnsureMutable`）、"发布须晚于全部提交且不重叠"
   已由 `EnsurePublicationAllowed` 补上，见附录 AD.2/AD.3。
4. **G0-P 仍未关闭**：③ 仍未配额度（且 `_stageQuotaAllocation` 生产侧无消费者）；
   ④ 的分层现为 L0 ✅ + L1 ✅（仅"不写图"半），窗口外依赖第 2/3 类仍无机制。
   ⚠️ **该状态的部分内容已过期**：L1 第二半与窗口外依赖第 2/3 类已于轮次 31 处理，
   见附录 AD；③ 未配额度**仍然成立**，故 G0-P **仍未关闭**。

# 附录 AC：R-4 的**声明表**有了消费者——与执行期观测对账（2026-09-25 轮次 30）

## AC.1 本轮要关的那句话

附录 W.5 / AB.6 边界 4 一直写着同一句实测事实：

> `_stageQuotaAllocation` 生产侧**无消费者**（只建立了能力，没据此配额度）。

轮次 30 把它拆成两个**不同**的问题，只解决**能解决的那个**：

| 问题 | 本轮处置 | 理由 |
| --- | --- | --- |
| ① 没有消费者读取配额表 | **不解决** | 配置里全是 `Shared` ⇒ 无 `Dedicated` 可消费，"消费"当前是空操作。凭空造额度被附录 G 与计划 `:114` 禁止。 |
| ② 声明表无人对账 | **本轮解决** | `BatchPlanCapableStages` 是**手写声明**，漏写即静默豁免整套 R-4 治理，与是否存在 `Dedicated` **无关**。 |

**②必须先于①**：若在②未做的情况下先做①，配的额度会建立在一张**无人校验**的声明表上。

## AC.2 缺口的准确形态

`StageQuotaPolicy.BatchPlanCapableStages` 声明"这 8 个阶段走批次执行器"。它的**唯一**用途是
`Resolve` 里的成因分类：

```
有 plan                      ⇒ PlannedBeforeExecution
早已执行完（Syntax/Operation） ⇒ ExecutedBeforePlanningPhase
在 BatchPlanCapableStages 里 ⇒ DeferredPlanning
其余                         ⇒ NoBatchPlan   ← "R-3/R-4 对它是 N/A"
```

于是**漏写一个阶段**的后果不是"分级不准"，而是：

- 该阶段被判 `NoBatchPlan` = "它不提交批次"；
- `EnsureCalibrated` 对它的 `Dedicated` 请求给出的是"无从据以配额度"而非"先标定"；
- 整套 R-4 治理对它**静默失效**，且**没有任何观测面**能显示这一点。

这与附录 AA 的形态**同源**：`BatchPlanCapableStages` 是"声明"，而**机制不在**。
区别在于 AA 修的是 R-1 表里两条恒真的边，本附录修的是 R-4 表里**无消费者的声明**。

## AC.3 修法：在唯一公共通道上观测，再与声明对账

**观测点选在执行器**（`CpgWorkBatchExecutor`），理由是 r29 已确立的同一理由：
它是 8 个阶段的**唯一公共通道**，逐阶段接线会让"新阶段忘了接线"退化成又一处"声明代替机制"。

- `CpgWorkBatchExecutor` 增构造参数 `Action<string>? stageExecutionObserved`，
  在 **`ExecuteWithCollectionModeAsync`**——三个公开入口（`ExecuteFragmentsAsync` /
  `ExecuteAsync` / `ExecuteAndConsumeAsync`）与同步/异步两条路径的**唯一公共祖先**——触发。
  故**新增入口无法绕过观察**。
- 触发点刻意在**调用线程**上、worker 启动**之前**：观察者用普通 `List` 记账，
  放进 worker 会引入新的并发写面（r29 刚实测过非线程安全容器的静默丢写）。
- `NLCPGBuilder` 的钩子读**当前构建**的 `_stageQuotaAllocation`（builder 可复用，
  每次构建分类都可能不同），并调用 `StageQuotaPolicy.ReconcileObservedStageExecution`。

**判定是结构层的，不是行为层的**：`NoBatchPlan` 断言"本阶段不提交批次"，
而执行器此刻正拿着它的批次——**两者不可同时为真**。故不看产物、不看耗时，直接比对分类。

### 映射由后缀反解，不新增第二张表

`CpgWorkBatchPerformanceStageId` 的 8 个常量**恰好**是 `"CPG.WorkBatch." + <Stage 成员名>`
（实测逐条相同），故 `TryResolveDeclaredStage` 由后缀反解阶段。
**刻意不写一张"id → 阶段"的映射表**：两张表必然漂移，而漂移后对账会**静默失效**。

## AC.4 两类必须放行的阶段（否则是误报）

| 形态 | 放行理由 |
| --- | --- |
| 配额表为 `Empty`（规划相位之前） | 那时**没有可对账的声明**。`Syntax`/`Operation` 正当如此（附录 V.2）。此时抛出会让**每次**构建都在 Syntax 阶段失败。 |
| 解析后归入 `ExecutedBeforePlanningPhase` / `DeferredPlanning` | 两者都**确实**走批次执行器，只有 `NoBatchPlan` 才声称"不走"。 |
| `stageId` 解析不出阶段 | 执行器是**通用组件**，`stageId` 只是标签。测试自造 id（`CPG.WorkBatch.ConsumptionContract` 等 4 个）与分区遥测 id（`CPG.Syntax.*`，到不了执行器）都经过同一观察点。把它们当违规会让守卫因**噪声**被关掉，而不是因为它是错的。 |

## AC.5 "接线存在"必须与"判定会拒绝"分开测

生产配置下声明表是对的 ⇒ **fail-closed 分支永远不会触发**。若只写抛出逻辑，
把 `stageExecutionObserved` 钩子删掉，与"一切正常"在行为上**完全不可区分**
（附录 N.4/P.4：未被观测面覆盖的断言等于不存在）。

故新增只读观测面 `NLCPGBuilder.ObservedStageExecutions`（r30），
并让契约测试断言两个方向：

- **接线判据**：全能力构建下观测集合**恰好等于**声明表。
  钩子未接线 ⇒ 集合为空 ⇒ 失败；某阶段被移出声明表 ⇒ 两集合不等 ⇒ 失败。
  用例内**先断言非空**——否则"两个空集合相等"会让它在天线被拔掉后依然通过。
- **判定判据**：`NoBatchPlan` 阶段报告批次 ⇒ 抛出，且消息含 `NoBatchPlan` 与"声明表与执行期观测不符"。

## AC.6 证据

| 项 | 结果 | 日志 |
| --- | --- | --- |
| `~StageQuotaDeclarationReconciliationTests` | **11/11** | — |
| `~Cpg` 全量回归 | **655/655**，零回归 | — |
| `~StageQuota\|~StagePlan\|~StageExecution\|~WorkerComputeLayer` | **63/63** | — |
| 变异 1：builder 不接钩子 | **1 失败**（`Assert.NotEmpty() Failure: Collection was empty`） | `mutation-r30-M1-no-wiring.log` |
| 变异 2：`ControlFlow` 移出声明表 | **1 失败**（`Assert.Equal() Failure: Collections differ`） | `mutation-r30-M2-stage-dropped-from-declaration.log` |
| 变异 3：对账改为空操作 | **1 失败**（`Assert.Throws() Failure: No exception was thrown`） | `mutation-r30-M3-reconcile-is-noop.log` |
| 变异 4：解析不出的 id 视为违规 | **6 失败**（防误报用例） | `mutation-r30-M4-unparsable-is-violation.log` |

四条变异**互不覆盖**：1 打接线、2 打声明表、3 打判定、4 打放行面。
源码四次均逐字节回滚（见 `run-r30-mutations.ps1` / `run-r30-m1-retry.ps1`）。

> ⚠ 变异 1 首跑因**并发写者**的半成品文件（`SyntaxPassTelemetryContractTests.cs:263`）
> 编译失败而无法判定，已隔离重跑——那次失败不是本变异所致，不得计入判据有效性。

**不改变任何执行顺序与产物**：观察点在 worker 启动前、只读分类、只可能抛出。
`~Cpg` 655/655 零回归即证据。

## AC.7 顺带修正的两处陈旧引用

1. `StageQuotaAllocation.cs` 的 `GrantedScope` 文档原写"被拒绝时降级为 `Shared`"，
   而代码是 `granted = requested` **并抛出**（`EnsureCalibrated`）——**文档与代码相反**。
   已改为"被拒绝时本字段不会被写到，`Resolve` 会抛出"。
2. 同文件 `:530` 的 `DataFlowPass.cs` 调用点实为 **`:618`**；`Syntax`/`Operation` 的
   行号引用（`:374`/`:401`/`:452` → 现为 `:393`/`:421`/`:479`）**已随相邻改动漂移两次**。
   行号失效是**静默的**（读者无法分辨引对与引错），故该处改为按可搜索符号引用并写明
   **刻意不写行号**的理由。

## AC.8 边界（不得越读）

1. **③ 仍未为任何阶段配额度。** 本附录只让**声明表**可被证伪，
   没有、也不能推出任何字节额度（附录 G）。
2. **`Dedicated`"被授予后真的独占"一侧仍未覆盖**（W.6 边界 2，无标定即无从构造）。
3. **不是"配额驱动了调度"。** 生产配置全 `Shared`，本轮**不改变**任何调度决策；
   `BatchPlanCapableStages` 的消费者是**校验**，不是**额度来源**。
4. **未覆盖**：某阶段有批次型 plan 却**从不提交批次**（反向漏报）——
   本机制只拦"不该提交却提交"，不拦"该提交却没提交"。
5. **④ 的分层仍为 L0 ✅ + L1 ✅（仅"不写图"半）**，窗口外依赖第 2/3 类仍无机制（AB.6 边界 3）。
   ⚠️ **该状态已过期**（轮次 31），见附录 AD 与上文 AB.6 的过期标记。
6. **G0-P 仍未关闭。**（③ 未配额度仍然成立 ⇒ 本条**不过期**。）

# 附录 AD：R.4 窗口外依赖第 2/3 类定性 + L1「不共享可变中间态」（2026-09-25 轮次 31）

## AD.1 本轮要关的两句话

轮次 30 结束时，G0-P 四要素只剩两处**没有机制**，且都被**明确写下来**（不是我的推测）：

| # | 原话 | 出处 |
| --- | --- | --- |
| ① | "第 2 类（跨文件）与第 3 类（`FreezeQueryIndex`/持久化）**仍只有文字描述，无任何机制**" | R.4 节，`:1836` |
| ② | "**只覆盖 L1 的『不写图』这一半。** L1 的『只算 fragment』还包含『不共享可变中间态』，本附录未涉及" | AB.6 边界 2 |

两句的共同形态是附录 N.4/P.4/Z.1/AA 反复点名的**「声明代替机制」**：性质只存在于注释与
散文里，破坏它不会触发任何东西。

## AD.2 审计：先把"缺口到底是什么"查清，而不是直接上手修

**第 3 类（发布）——拆成两半后，结论是"一半早就有机制了"。**

原话把两件事混成一句。拆开：

- **"冻结后不得再写图"**：`NLCPGGraph.EnsureMutable` 的 frozen 检查**原本就已**强制它，
  且那是**唯一瓶颈点**——5 个图变更入口（`AddNode` / `AddEdge` /
  `AddKnownNodeCartesianEdges` / `ImportMutableFacts` / `RegisterSource`）**全部**经它。
  ⇒ **这半不是缺口，原话低估了既有机制。**
- **"发布必须晚于全部阶段提交、且与执行不重叠"**：**确实无任何机制**。此前
  `EnsureStageDependencyOrder()` 只校验**阶段之间**的顺序，`FreezeQueryIndex` /
  `PersistenceWrite` **不在** `StageDependencyTable.Stage` 枚举里（它们不是
  work-batch 阶段），故**发布动作落在该表的校验范围之外**。

**第 2 类（跨文件）——判定为【空真满足】，不是"缺机制"。**

源码确证**不存在**把多个文件放进同一个 `NLCPGGraph` 的路径：`CpgWorkBatchBuilder`
显式**拒绝**跨文件 item；`DirectoryAnalysisUseCase.AnalyzeCore` 逐文件构建。
⇒ "同一窗口内看到全部文件"这一前提在今天的架构里**不可能成立**，
故该约束**无从被违反**。对应的机制（S3-1c「窗口协调器」）**尚未开工**。
**这个判定很重要**：把它记成"遗留缺口"会凭空造出一项永远不会被完成的待办
（与附录 X.6 边界 2 对 `PartitionedSyntax`/`PartitionedOperation` 的区分同源）。

**L1「不共享可变中间态」——确实存在窗口期内的共享写入。**

枚举 builder 全部具名容器的写点后确认：这些写入**确实**发生在**窗口存活期间**
（归并线程上），且此前**没有任何机制**保证其串行性。⚠ 但必须如实定性：
**它不是今天的活跃数据竞争**——今天的串行性来自两条**从未写下**的事实：
① 结果通道是 `SingleReader`（归并回调只有一条，故彼此串行）；
② 这些容器的读者都排在**串行相位**（如 `DataFlowPass.FindCallSiteNode` 读
`_callSiteNodesByInvocation`，在本阶段结束之后）。
**故本轮机制不改变当前可观测行为**，它消除的是"把 L1 不变式寄托在这两条巧合上"。
把它写成"修了一个并发 bug"会**夸大证据**，故按机制现状记录。

## AD.3 修法一：发布守卫（第 3 类的后半）

`EnsurePublicationAllowed(publicationStage)` 两条断言，**都在动作之前**调用
（事后检查只能证明"已经晚了"）：

1. **顺序前提**：`_stageDependencyOrderVerified` 必须为 `true`。该标志在
   `EnsureStageDependencyOrder()` 的**成功分支内置位**，而不在调用点 —— 保证新增调用点
   无法绕过（"声明在、机制不在"正是本附录要消灭的形态）。
2. **非重叠前提**：**跨线程**窗口计数 `_activeWorkerWindowCount == 0`。
   读的是全局计数而非"本线程是否在窗口内"——**发布线程自己必然不在窗口内**，
   故"只看本线程"会给出**恒定通过的空洞判定**（附录 Y.4/X.5 同源教训：
   观测面必须能区分"机制生效"与"机制是死代码"）。

**判定与抛出分开（变异实测逼出来的，不是风格偏好）**：判定被抽成**纯函数**
`EvaluatePublicationGate(stage, orderVerified, activeWindowCount)`，
`EnsurePublicationAllowed` 只负责把生产字段传进去并在返回非 null 时抛出。
理由见 AD.5 的**变异 2**：把判定内联在抛出方法里时，拒绝分支**零覆盖**——
生产路径两个前提必然满足，任何行为用例都触达不到它。
抽成纯函数后，用例可注入前提取值**直接**钉住拒绝逻辑。
**只保留一处实现**：接线处与用例调用同一个函数，故不存在"测试测的那份与生产跑的那份
各自漂移"（附录 N.4/P.4 的形态）。

`RecordPublicationStage` 在**校验通过之后**留证，故留证里**绝不会**出现被拒绝的发布。
两级发布点（`FreezeQueryIndex` / `PersistenceWrite`）**各查一次**——它们之间隔着
`ReleaseTransientBuilderState()` 与分支判断，是真实的代码距离。
`_stageDependencyOrderVerified` 与 `_publicationStageOrder` 在 `Build` 入口重置：
若不重置，上一次成功构建会把本次的"未校验"状态**掩盖**掉。
该留证通过 `LastPublicationStageOrder` 暴露为**只读观测面**——首版只写了私有字段、
无任何读者，于是"留证"在观测上**完全不存在**（"从未记录"与"没有机制"行为等价）。

## AD.4 修法二：L1 第二半 —— `WriteSharedState` 单一写入口

**为什么不是"窗口内禁止写入"**：归并层（L2）**必须**写 builder 级记账
（调用点映射、数据流指标等），否则这些结果无处累积。可行的不变量只能是
**"窗口存活期间的写入必须经门串行化"**。

**为什么是"唯一入口"而不是"写点自查守卫"——本轮最有价值的自我纠正，
两次摆法都不成立，如实记录：**

- 首版写成 `EnsureSharedStateWrite` + 在写点断言"当前是否持门"。
  **摆在 `lock` 之前**：归并线程此刻必然**尚未**持门 ⇒ **合法归并会被全部误抛**，
  把今天正确的路径打死。
- **摆在 `lock` 之内**：`Monitor.IsEntered` 恒为真 ⇒ 断言**永不触发**，
  退化为"注释式声明"——正是本附录 N.4/P.4/Z.1/AA 点名的形态。

⇒ 改为**结构性预防**：`WriteSharedState(Action mutation)` 按窗口状态**自己**决定是否取门，
使"窗口内存活期间的写入被串行化"成为**不可绕过的单一入口**，比事后探测更强。
调用点因此**不可能忘记**判断取门时机。

**⚠ 一处会永久打死发布的并发陷阱（已修，值得单列）**：窗口计数**首版写成
`ThreadLocal<int>`**，错的——归并回调是 `async`，**进入窗口的线程与退出窗口的线程
可能不是同一个**（`await` 后续可切线程池线程）。若 A 线程 +1、B 线程 −1：
A 线程被永久污染（此后每次写入都白取锁），而**全局计数因 B 线程读到 0 而提前返回、
永不递减** ⇒ `EnsurePublicationAllowed` 随后**永久抛错**，每次构建都失败。
改用**全局计数**（`Interlocked`）——它没有"进出的线程必须相同"这一前置条件，
故对 `async` 路径天然正确。退出计数**饱和在 0**：宁可使计数停在 0（发布守卫随后
放行一次本应被拒的发布），也不能让它变负——负值会让 `!= 0` 判据**永久**为真。
这是"守卫自身故障时选哪一边失败"的取舍，偏向可用性。

**接线方式（防"半接线"）**：`CpgWorkBatchExecutor` 是全部 8 阶段的**唯一公共通道**，
故钩子接在那里，新阶段无法漏配。图侧（不写共享图）与 builder 侧（不共享可变中间态）
两对钩子由 `CombineWindows` / `CombineWindowsReversed` **合并为同一对开合点**
（退出按相反顺序，同嵌套 `using`），使窗口的**开合点只有一个**。
归并任务**刻意只开共享态窗口、不开图侧窗口**——归并层（L2）本就是唯一的图写入者，
开图侧窗口会让它的**合法构图**被误判为 L1 违规。

**覆盖边界（勿越读）**：本机制强制"**经此入口**的写入必被串行化"；它**不**具备
编译期能力阻止某处绕过入口直接对私有字段赋值（partial 类内可直写字段）。
故仍需审阅纪律。**另**：`NLCPGBuildContext` 的 `_operationInventory` /
`_invocationOperations` 等由归并回调写入，**同样依赖"单归并者"这一未强制的巧合，
本轮未纳入该入口**。

**可证伪性（变异实测逼出来的）**：本机制**不改变任何行为**，故变异"拔掉
`sharedStateWindowEnter/Exit` 接线"后**全量 671 条 `~Cpg` 用例仍全绿**
（详见 AD.5 变异 3）。这意味着它在**行为层不可证伪**——按附录 X.5/Y.4/AA 的结论，
判据必须换到**计数层**。故新增只读观测面
`SharedStateWindowEnterCount`（累计开窗次数）与 `SharedStateWindowOpenCount`
（存活窗口数），并在 `Build` 入口按构建重置累计值，
使"一次真实构建确实开过窗口、且存活数归零"成为可断言事实。

**一个被否决的形态（留证）**：初版给该方法加了 `memberName` 参数"用于异常定位"，
但该方法**不抛异常**，故该参数**永不被读取**——它看上去在产出诊断、实则不产出，
正是本附录要消灭的"声明代替机制"。已删除。

## AD.5 证据（轮次 31，已编译并运行核心测试）

**编译**：`NLCPG` 与 `RoslynDeletionPrototype.ContractTests` 均 **0 error**。
（构建期警告 20–22 条**全部**为既有测试文件的 xUnit/可空性分析器告警，**无一**来自本轮改动。）

**新增契约测试** `PublicationStageOrderContractTests`（**13/13**）：覆盖
发布顺序记录、未启用持久化时不谎报、命中持久化目录时不谎报、复用 builder 不累积、
发布晚于阶段执行、**判定函数的拒绝分支**（5 组前提取值）、
**拒绝消息须点名成因**、以及 **L1 窗口接线的计数层判据**。

**变异验证（三条，均逐字节回滚并校验 SHA256 一致）**：

| # | 变异 | 首轮结果 | 修正后结果 |
| --- | --- | --- | --- |
| 1 | 删去两处 `RecordPublicationStage` | 5/5 失败 ✅ | — |
| 2 | 非重叠判定恒不触发（`!= 0` → `false`） | **5/5 通过 ❌ 存活** | **3 条失败 ✅** |
| 3 | 拔掉 `sharedStateWindowEnter/Exit` 接线 | **11/11 通过 ❌ 存活** | **2 条失败 ✅** |

**变异 2 与 3 首轮存活是本轮最重要的发现，且都直接改变了交付内容：**

- **变异 2 存活** ⇒ 发布守卫的**拒绝分支是零覆盖的死代码**。生产路径下两个前提必然满足，
  任何行为用例都触达不到它。修法：把判定抽成纯函数
  `EvaluatePublicationGate(stage, orderVerified, activeWindowCount)`，
  **接线处与用例调用同一个函数**（不再有"两份判定各自漂移"的余地），
  并用 5 组前提取值直接钉住拒绝逻辑与其边界。
- **变异 3 存活** ⇒ L1 第二半的接线**在行为层不可证伪**（与 AD.2 的定性一致：
  今天不是活跃竞争）。修法：新增计数层观测面
  `SharedStateWindowEnterCount` / `SharedStateWindowOpenCount`，
  断言"一次真实构建确实开过窗口且存活数归零"。这正是附录 X.5/Y.4/AA/AC.5 的
  同一结论：**该差异在行为层不存在时必须换到结构/计数层**。

**回归**：`~Cpg` **671/671**（轮次 30 为 658 ⇒ +13 全为新增、零回归）；
专项组（8 个过滤器 + 本附录新增）**107/107**。

**⚠ 一条流程教训（值得记录）**：变异回滚用 `Copy-Item` 会**保留备份文件的旧 mtime**，
MSBuild 据此判定"无需重编"⇒ 测试实际跑的是**变异后的旧 DLL**。
我因此观察到一次**假失败**（源文件已正确却仍报 3 条失败），
在刷新 mtime 后消失。**结论：回滚后必须刷新源文件 mtime（或强制重建），
否则变异结论与回归结论都不可信**——本附录上表"修正后结果"均已带 mtime 刷新重跑。

## AD.6 边界（不得越读）

1. **不改变任何执行顺序与产物**：AD.3 只在"发布早于校验/与 worker 重叠"这一
   **本不该发生**的情况下抛出；AD.4 在串行相位**零额外开销**（计数为 0 时不取门），
   窗口期取的是**既有** `_cacheGate`，且 `Monitor` 可重入。671/671 零回归即证据。
   故**不**声称性能或调度改善。
2. **AD.4 不修复今天的活跃竞争**（见 AD.2 末端）——它消除的是"不变式靠两条
   未写下的巧合成立"。**不得**表述为"修了一个并发 bug"。
   ⚠ 其直接后果是**该机制在行为层不可证伪**（变异 3 首轮存活已实测），
   故其验收判据**只能**是计数层（AD.5）。
3. **③ 仍未为任何阶段配额度**（附录 G 证明不存在可用 bytes 上界；计划 `:114`
   点名禁止凭空造额度）。**G0-P 仍未关闭。**
4. **第 2 类是"空真满足"而非"已实现"**：S3-1c「窗口协调器」仍**不存在**。
   一旦出现跨文件单图构建，该约束**立刻**变成硬需求。
5. **④ 的分层现为 L0 ✅ + L1 ✅（两半齐）**；但 L1 第二半**只覆盖 builder 具名容器**，
   不含 `NLCPGBuildContext`（AD.4 边界），且**无编译期强制**（审阅纪律）。
6. **未覆盖**：`DominancePass.Instance` 是**静态单例**且持有可变 `_dominanceOverlays`，
   未在 `ReleaseTransientBuilderState` 中逐次清空——跨构建的静态可变状态**不在**
   本机制范围内。
7. **第 3 类的"非重叠"只断言窗口计数**，不断言持久化与**构建外**并发（如另一
   builder 实例、`CpgRelationQueryService` 构造期的独立冻结路径）。
8. **未做全量测试**：本轮只跑 `~Cpg` 与专项组，**未**运行
   `Run-TestTiers.ps1 -Fast/-Host/-Performance`，故不声称全仓无回归。