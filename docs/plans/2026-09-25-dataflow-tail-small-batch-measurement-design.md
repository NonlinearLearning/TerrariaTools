# DataFlow 长尾：小批量、同次运行测量设计

日期：2026-09-25。状态来源：[feature_list.json](../../Context/feature_list.json)，feature `dataflow-tail-same-run-measurement`。

设计轮交付仅为设计；本文保留该轮的提案与验收约束。2026-09-25 用户随后明确授权执行，诊断、默认批次及报告已落地，实际结果见[执行报告](../benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md)。21 次调用在 5.155 秒内完成，输出等价；观察者开销门槛因抖动仍不确定。测量完成不代表长尾优化完成，也未启动方法内并行。

## 1. 要回答的问题

在**同一二进制、同一输入、同一方法、同一次完整分析**中，分别获得计划构建、fixpoint、候选生成和事实匹配的时间与实际扫描次数，然后决定优化哪一层。流节点最多不等于耗时最长；`Contains` 调用栈也不能单独证明 fixpoint 占比。

当前入口是 [DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs) 的 `AnalyzeDataFlowWorkBatch` → `BuildCfgSensitivePartitionPlan` → `AnalyzeCfgSensitivePartition`。后者先运行 fixpoint，再构造 `DefinitionFactIndex`，随后执行候选匹配。`DefinitionFactIndex.AddReachable` 本身也调用可达集合的 `Contains`。

## 2. 已有覆盖与证据边界

| 已有材料 | 已覆盖内容 | 本设计新增内容 / 不可沿用的结论 |
| --- | --- | --- |
| [10pass 任务拆分](../../Build/10pass-任务拆分.md) T7/T8/T9 与 [G5 报告](../../Build/g5-报告-N5-N6-N1.md) | 已开展长尾诊断，评估过 fixpoint 和候选阶段 | 不是“历史上完全没测过”；本设计补齐同方法、同次运行的直接归因 |
| [T7 分解日志](../../Build/verify-pass-optimization/g5-t7-decomp-npc.log) | 用不同预算配置的独立运行相减，估算组成 | 预算改变会提前退出；最长方法/批次也会变化，出现负的候选耗时估计，不能当作互斥阶段的实测时间 |
| [N0 执行计划](../../Build/N0-执行计划.md) | 候选索引使用定义序号及复用去重暂存等改动已有记录，当前源码也已包含相关结构 | 不重复实现 N0；测量当前索引真实 posting 扫描量与筛选成本 |
| [现有 DataFlow 契约测试](../../tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs) | 构图结果、并行等价与预算语义 | 正确性测试不能代替阶段耗时证据 |

G5 材料中的不同快照指纹、不同预算和不同最长批次不得拼接成一条因果证据。先记录输入、编译配置、二进制哈希、源码状态、预算、方法身份和输出快照；不能解释指纹差异的旧运行只作线索。禁止把另一个输入的已知指纹硬套到小样本。

## 3. 测量方案选择

| 方案 | 优点 | 问题 | 决定 |
| --- | --- | --- | --- |
| 改预算、跳阶段、跨运行相减 | 无需细粒度探针 | 改变实际工作量；最长方法会切换；负差值无法归因 | 排除 |
| 复制私有算法或反射调用私有方法做 microbenchmark | 单次很短 | 绕开真实计划、预算、集合形态及发布路径，容易测错对象 | 不作为验收入口 |
| 在原生产调用链增加默认关闭的方法局部诊断 | 时间与计数对应同一执行；结果可比较 | 细粒度计时有观察者开销 | 采用；先通过开销与等价门槛 |

复用 `NLCPGBuilder.BuildFromSource` 和现有测试的配置构造方式。诊断实现将来应放在 NLCPG 内部，沿工作结果交给现有串行汇总点；不引入第二套分析算法，不为此新增公开 CLI 参数。现有 [NLCPGBuildMetrics.cs](../../src/NLCPG/Builder/NLCPGBuildMetrics.cs) 可承接诊断汇总，但是否扩展公开记录须另行评审；本设计优先内部只读附加结果。

## 4. 同次运行的计时边界

每条记录的键为 `RunId + 输入哈希 + 文档标识 + 完整方法签名 + Span + StableOrder`。不能只用 `AI`、方法名或“最长批次编号”关联。单次方法记录保留以下**互斥粗阶段**：

| 阶段 | 原调用链边界 | 配套计数 |
| --- | --- | --- |
| UsedFacts | `AnalyzeUsedFactPartition` | 实际扫描 operation 数、used fact 数 |
| PlanBuild | `BuildCfgSensitivePartitionPlan` | 实际访问的 operation 数、节点数、CFG 边数；分开记录扫描量和输出量 |
| DefinitionSetup | 定义事实收集、定义序号/位集合/工作队列初始化 | 定义收集访问量、有效定义数、初始化槽数 |
| Fixpoint | 工作队列循环开始至收敛 | 入队/出队、predecessor 访问、transfer 执行、集合 union/比较次数 |
| CandidateIndexBuild | `new DefinitionFactIndex(...)` | 四类索引各自的键数、posting 条目数及源定义扫描数 |
| CandidateLoop | 每个 flow node 的 incoming 重建至其 used facts 匹配完成 | predecessor 重扫、used fact 访问、索引/回退路径、匹配、收集计数 |
| ExplicitSources | 显式 value-source 候选边处理 | 源访问量、原始/新增边数 |
| ReturnBoundary | 显式 return 边界及末尾隐式返回路径 | return/末尾语句访问量、原始/新增边数 |
| ResultMaterialization | 候选结果物化及方法结果包装 | 原始/唯一候选数、物化条目数 |

外层 `MethodTotal` 包住上述调用链；汇总 `Unattributed = MethodTotal - Σ互斥阶段`。方法间排队、串行图提交另记 `QueueWait` / `Commit`，不能算进方法内的 fixpoint。计时使用单调时间戳，记录频率和原始 ticks，写文件与格式化移到计时区外。

`CandidateLoop` 内额外直接计时 `TryGetCandidates`、定义事实 lookup、实际执行的 `FactsMatch`、候选收集；标明这些是父阶段的子项。`TryGetCandidates` 时间已经包含 `AddReachable` / `Contains`，不得再相加。细项未覆盖的循环与集合处理归入 `CandidateLoopResidual`，不能强行分摊给 `FactsMatch`。

计划构建也需直接记录扫描数：如果同一 operation 因多个步骤被访问，分别计数，而不是仅用最终数组长度冒充访问次数。

## 5. 候选路径的精确计数

计数器归当前方法所有，使用非原子的 64 位整数；线程间只在现有汇总点合并。开启诊断不能改变迭代顺序、短路条件、复用列表生命期或集合内容。

| 位置 | 必须记录的实际事件 |
| --- | --- |
| `TryGetCandidates` | 调用数、空 LocationKey 返回 false 数、返回 true 数、返回列表总长度 |
| 四类 `AddReachable` | 每类字典查询/命中；遍历的 posting 总数；实际 `Contains` 调用与不可达拒绝；实际 seen 检查与重复拒绝；追加 matches 数 |
| 去重暂存 | touched 标记/清理条目数；matches/touched 的 Count 与 Capacity 高水位 |
| 索引路径 | definition lookup 次数/缺失数；**真正执行**的 `FactsMatch` 调用数/成功数；收集器调用数 |
| 回退路径 | 枚举 reaching ordinal 数、definition lookup 次数/缺失数、实际 `FactsMatch` 调用数/成功数 |
| 候选收集 | 原始候选数、唯一候选数、重复候选数、预算溢出与退出原因 |

由计数推导 `PostingPerUse`、`PostingPerReturnedCandidate`、重复拒绝率、匹配通过率和 `NsPerActualFactsMatch`；分母为零时报告“不可计算”，不填零成本。四类索引扫描的是 posting，不能用去重后的候选列表长度替代。

`FactsMatch` 要保留原来的 `&&` / `||` 短路：前置 lookup 失败时不调用，内部某条件满足后不为统计而继续执行其余分支。复用的 `_matches` 只在当前调用消费，探针不能留存其引用作为历史样本。

## 6. 小批量样本与耗时约束

默认只选三个单目标方法的源码夹具，通过真实构图入口运行；数字为**待实现的输入规格，不是已测节点数或结果**。参数驱动分支，保留返回值/可观察使用，避免样本退化成无候选路径。

| 夹具 | 小规格 | 目的与非空覆盖门槛 |
| --- | --- | --- |
| Sparse | 64 个独立局部定义及使用 | 索引路径、成功匹配和实际候选边均非空，建立低扫描基线 |
| Collision | 64 次同基址/相关路径赋值，分支汇入后使用 | 至少两类索引命中，并实际观察重复 posting / seen 拒绝；观察不到则夹具设计失败 |
| JoinLoop | 16 处分支汇合、4 个由参数控制的循环结构 | fixpoint 重访与 incoming 重建均发生；输出包含可核对的数据流边 |

空 LocationKey 回退路径单独作为一个最小正确性用例设计：必须找到实际源码能触发该分支的输入，或使用已有内部契约测试缝；无法触发就报告覆盖缺口，不能把计数恒零写成分支通过。它不扩展默认计时矩阵。

每夹具 1 次预热、3 次正式测量；DOP=1，单进程顺序执行。默认采 Detailed 模式：每次同时具备互斥阶段时间、精确计数与函数细项时间。Collision 另加 Off、Coarse 各 1 次预热 + 3 次测量，与其 Detailed 样本配对校准；三个正式轮次轮换 Off/Coarse/Detailed 的运行顺序，避免把固定先后顺序的预热漂移算作探针成本。最小 Sparse 再做一次 DOP=2 输出等价检查，不展开 DOP=1/2/16 × 多规模 × 多配置矩阵。

**预算目标**：已有构建产物下整批不超过 45 秒，单次不超过 5 秒；计入预热、校准和等价检查。构建/还原单列，不藏入“探针运行时间”。这些是未来运行限制，尚无达标证据；探针宿主负责超时终止和保存已完成记录。超时即“不完整”，禁止自动跳方法、清空候选、缩预算或减去慢样本后宣布通过。若首次校准超限，停止本批并另行调整夹具规格。

只有默认批次仍不能区分问题时，才追加一个 Collision 的 4 倍规模，用扫描量增长验证斜率；不默认启动整份 NPC、dotnet-trace、内存转储或全仓 Performance 层。

## 7. 观察者开销与判定规则

三种模式均须执行相同算法、预算与输入：Off 关闭探针；Coarse 仅阶段时间与结果摘要；Detailed 加精确扫描计数及函数细项时间。所有模式的完整规范化结果与原始发布顺序分别比较，规范化只用于身份表示，不能排序掩盖顺序差异。

- 默认归因先看 Detailed 的同次记录，不能用 Off / Coarse / Detailed 的差值伪装某个阶段耗时。
- 校准门槛先设为 Detailed 相对 Coarse 的方法耗时增幅不超过 10%；这是设计阈值，尚非测得值。3 次测量只报告原始值、min/median/max，不宣称显著性或 P95。若抖动足以左右门槛，结论仍为不确定。
- 超过门槛时，函数级纳秒数据标记“探针扰动过大”。保留粗阶段和精确计数作定位；可另设计固定采样，但必须标注采样率与样本数，不能把采样耗时当作完整函数耗时。
- 各阶段结束必须正常；任何预算溢出、SkipMethod、异常或输入/结果不一致使该记录退出性能比较，仍保留失败原因。
- `Σ阶段 <= MethodTotal`，子项不超过父项（容许时间戳分辨率误差）；残差过大先补边界，不用平衡项凑占比。所有输出数量、调用数量、扫描数量单位分别标明。

未来最小自检：令诊断关闭/开启得到相同的图；用能确定发生重复 posting 的夹具验证 seen 拒绝计数非零；用有效失败匹配验证 `FactsMatchCalls > FactsMatchTrue`；检查计数器不跨方法/重复轮次累加。耗时不作为易抖动的 CI 通过阈值。

## 8. 从结果到优化决定

| 同次证据 | 先考虑什么 | 暂不据此决定什么 |
| --- | --- | --- |
| PlanBuild 占比高且 operation/CFG 重扫多 | 避免重复建表、重复枚举及无效物化 | 方法内 fixpoint 并行 |
| CandidateIndexBuild 或 TryGetCandidates 占比高、posting 放大大 | 索引粒度、重复扫描、可达筛选、key 构造与暂存容量 | 仅因流节点多而优化 worklist |
| FactsMatch 占比高、每候选成本高 | 事实关系判定及字符串比较；先减少不必要调用 | 把索引扫描成本算给匹配函数 |
| Fixpoint 确为主因、工作队列重访多 | 传播顺序、稀疏集合运算及增量传播 | 直接共享可变集合做并行 |
| 方法总耗时低而 QueueWait/Commit 高 | 外层分批、调度或串行发布 | 方法内拆任务 |

小样本只能证明诊断可用及某种形态的成本。若要把结论用于 NPC 长尾，还须在预算允许的真实目标方法（至少核对 `SetDefaults` 与 `AI` 的完整签名）复验；保留真实绑定上下文，不用语义不同的 stub 冒充原方法。做不到快速真实复验就明确留下该边界，不运行全量来填补本次范围。

只有真实目标的直接证据仍支持方法内并行，且已经厘清局部集合所有权、scratch 隔离、稳定合并与任务粒度，才另行提出并行设计。当前 `_matches` / `_seen` 共享暂存说明现实现不能直接并行，不证明所有并行设计均不可行。

## 9. 后续验收证据与本轮边界

未来应保存逐方法逐轮原始时间/计数表、模式、超时/溢出标记、方法身份、机器/运行时/构建配置、输入和二进制哈希，以及未排序的输出等价结果。快照摘要必须包含实际关联的节点/边元数据，不能只比较边数。

本设计交付条件是：边界可落到现有调用链；三个默认样本与时间预算明确；观察者开销、短路语义和失败判据明确；已有工作不重复安排。优化 feature 继续保持未开始，直到后来明确启动并留下运行证据。

原设计轮只允许文档、状态与链接静态检查，未运行样本、构建、测试、trace 或性能实验；后续授权执行的证据与剩余边界单独记录在执行报告中。配套内存议题见 [计划与排序载体缩窄设计](2026-09-25-interprocedural-plan-compaction-design.md)，该议题没有随测量执行而实施。
