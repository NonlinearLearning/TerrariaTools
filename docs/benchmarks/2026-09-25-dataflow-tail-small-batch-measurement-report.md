# DataFlow 长尾小批量同次测量执行报告

执行时间：**2026-09-25 02:23:59，北京时间**（原始 UTC：2026-09-24T18:23:59Z）。
范围：[测量设计](../plans/2026-09-25-dataflow-tail-small-batch-measurement-design.md)。
本次用户明确授权执行；原设计轮的“仅文档”限制是历史范围，不代表本次没有运行。

## 1. 结论

- **测量批次完成**：21 次真实 BuildFromSource 调用，包含所有预热、三轮正式记录、Collision 三模式校准和 Sparse DOP=2 等价检查。
- **预算达标**：子进程总耗时 **5.1550 秒**；宿主内部批次 **5.0351 秒**；最慢单次 **2.4065 秒**，为首次预热。Release 构建/还原另计 **9.0415 秒**，不计入 45 秒测量预算。
- **语义检查通过**：21 条记录均正常结束，无预算溢出、跳方法或超时；完整节点/边元数据和候选原始提交序列均一致。三个基线之后的 18 次比较均对实际文件逐字节核对，而非只比较边数或快照编号。
- **开销门槛不确定**：三组配对增幅为 +2.55%、+9.44%、−77.29%；Coarse 第三轮出现 11.3662 ms 长值。不能把 Detailed 的中位数较低解释为加速，也不能宣告“开销 ≤10%”。函数级纳秒结果只保留原始值，**未取得归因资格**。
- **最稳的结构证据**：Collision 实际扫描 **37,764 条 posting**，只返回 **324 次候选条目**；其中 **98.80%** 的 posting 因不可达被拒绝。当前应优先研究重复索引扫描与计划重复遍历，暂不启动方法内 fixpoint 并行。
- 本报告只描述三个小夹具；**没有复验 Terraria.NPC.SetDefaults / AI**，没有执行 NPC 全量、trace、dump 或全仓 Performance 层，也没有宣称生产长尾已优化。

## 2. 可复现入口与身份

从仓库根目录运行，输出目录必须尚不存在：

    pwsh -File Miscellaneous/scripts/Run-DataFlowTailMeasurement.ps1 -OutputDirectory Build/DataFlowTailMeasurement/replay-new
    python tools/DataFlowTailMeasurement/summarize.py Build/DataFlowTailMeasurement/replay-new

已有匹配 Release 产物时可传 -NoBuild；脚本不会覆盖已有证据。宿主内部对单次设 5 秒、整批设 45 秒终止；父进程另设 45 秒截止。失败/超时写明 Incomplete，保留已完成记录。不会缩预算、跳过慢样本或把失败当成功。

| 项目 | 记录 |
| --- | --- |
| 构建入口 | tools/DataFlowTailMeasurement/DataFlowTailMeasurement.csproj，Release |
| 生产入口 | NLCPGBuilder.BuildFromSource → 原 DataFlow WorkBatch → 原串行 commit |
| 当前工作树 HEAD | 925161d2003e626fe00000e799cee2f847025067，含既有未提交改动；不是干净 commit 基线 |
| 分支 | codex/dataflow-tail-measurement-20260925 |
| 源码身份 | [source-state.json](dataflow-tail-small-batch-20260925/source-state.json)，463 个构建相关源文件 SHA-256；运行前后未变 |
| NLCPG.dll SHA-256 | 59D248F35EAE5173661CFC775BC7D5D71F1C83E55F02384DDA3E3F778C04A5E9 |
| 宿主 DLL SHA-256 | 3D3D8C78EE6E19C85FC7C0FF094D58A55FE9014D351DCB75E09322CB6A9D69B8 |
| 平台 | Windows 10.0.26200，X64，AMD Ryzen 7 5800H，8 核 / 16 逻辑处理器 |
| SDK / Runtime | 10.0.400 / .NET 10.0.11 |
| 单调时钟 | Stopwatch，10,000,000 ticks/s，1 tick = 100 ns |
| JIT 配置 | DOTNET_TieredCompilation=0，DOTNET_ReadyToRun=1；所有模式相同 |
| DataFlow 预算 | 定义/流节点/原始候选上限均 2,147,483,647；SkipMethod 策略，本次均未触发 |
| 并发 | 20 次 DOP=1；末尾一次 Sparse DOP=2；单进程顺序执行 |

环境及依赖 DLL 哈希见 [environment.json](dataflow-tail-small-batch-20260925/environment.json)。
输入和输出身份如下，字符串 ID 经所属图解析为文本；保留数组顺序，无排序掩盖发布差异：

| 夹具 | 输入 SHA-256 | 完整规范化图 SHA-256 | 原始候选提交序列 SHA-256 |
| --- | --- | --- | --- |
| Sparse | AB680F13E193BEB31973E9F2F8DC687B889911104C86689B46F1199E88C299DB | 6655444A64A1582D16BBB164DFFFAE3E6B19E65CDEDCF634935EF3C465CCF974 | E19B748F6C755A1C631E2B581B8D46408E3199D935D638EF9D48DC5CC06C12E6 |
| Collision | 79BDB8AE4A2FBB2BB6A3B4224224AD555FBB7F91E2C5D31A047BF74C62C2473A | 89BCC6CD92C0FABE58236F7FF34B099387CA1A4BF0D6898087B61C8A492CD8CC | E694D74902E18CACE289FD8F22C725BEAF6E6B1FB24E579B86DC0103CB8DE2C5 |
| JoinLoop | 6D645B2CD273C46DD520F67687FDBC2A26C3E31B66AB85445805045FFC40AF40 | 02F9FAF53C5A6111478FCA467E8FD40BFC355863A546AAC030688AC73FF77B58 | 7BFA1965AF6D49477760A4588164033B4D5EA986A1A3C07AE6BC2D9177AF0427 |

原始完整图、逐次候选序列和 JSONL 位于 [本地证据目录](../../Build/DataFlowTailMeasurement/formal-20260925-a/)。
可跟随报告保存的逐轮 ticks、计数、身份和哈希见 [measurements.csv](dataflow-tail-small-batch-20260925/measurements.csv)。

## 3. 实现边界和夹具覆盖

诊断默认关闭，通过内部只读附加结果暴露；未扩展公开 CLI 参数或公开 BuildMetrics 协议。
计数器属于当前方法，使用非原子 long；worker 经现有工作结果交给串行汇总点。
三种模式使用相同算法、输入和预算。Off 关闭阶段/细项/计数，只保留用于校准的统一外层方法起止时钟和结果摘要；默认不请求诊断的生产调用连这份附加结果也不创建。
Off/Coarse/Detailed 校准只比较当前同一二进制，不据此声称探针代码相对修改前二进制零开销。

| 夹具 | 源码规格与实测非空覆盖 | 实测图节点 / 图边 / DataFlow 边 |
| --- | --- | --- |
| [Sparse](dataflow-tail-small-batch-20260925/Sparse.cs) | 64 个独立局部定义及使用；128 次索引调用、64 次实际成功匹配 | 2,218 / 5,441 / 132 |
| [Collision](dataflow-tail-small-batch-20260925/Collision.cs) | 64 次同基址字段赋值，另有 32 次 seed 参数赋值；四类索引均命中；128 次 seen 重复拒绝 | 3,759 / 10,021 / 262 |
| [JoinLoop](dataflow-tail-small-batch-20260925/JoinLoop.cs) | 16 处源码分支、4 个参数控制循环；329 个流节点、333 次出队；50 次候选前驱重扫 | 1,383 / 3,405 / 68 |

夹具自检暴露了当前 CFG 的重要边界：普通语句内赋值不会自动使条件/块节点的循环传播重访。
因此 Collision 将字段赋值放在条件根操作中，JoinLoop 的循环条件是对 choose 的赋值，仍保持原定规模和参数控制。
初始未通过覆盖的夹具保留在测试失败日志中，没有冒充有效测量；正式批次只在覆盖契约通过后启动。
Sparse 的 64 个局部定义不等于 64 个局部定义均可达；实测的 64 次匹配来自当前模型真正处理的事实，不能扩张为源码分析完备性证明。

空 LocationKey 在这些真实源码夹具中均未出现。独立正确性测试复用既有 DefinitionFact 反射契约方式：
直接验证真实索引返回 false、回退标记加一且 posting 不增加；另用一次失败、一次成功的事实匹配验证实际调用数 2、成功数 1。
这两项不参与计时矩阵，未复制算法。**真实源码的完整回退枚举链仍是覆盖缺口**，不把恒零当作通过。
四类正常索引的返回条件本身已足以满足 FactsMatch，因此这三个夹具匹配通过率 100% 是当前算法性质，不能强造失败样本进入性能结果。

## 4. 原始耗时与校准

单位：ms。每行三个正式样本按轮次保留；每配置另有一次预热。只报告 min/median/max，不报告 P95 或统计显著性。

| 夹具 / 模式 | 轮 1 / 轮 2 / 轮 3 | min | median | max |
| --- | --- | --- | --- | --- |
| Sparse / Detailed | 3.3771 / 2.2505 / 12.0333 | 2.2505 | 3.3771 | 12.0333 |
| Collision / Off | 2.4541 / 2.2174 / 2.8120 | 2.2174 | 2.4541 | 2.8120 |
| Collision / Coarse | 2.8234 / 2.4046 / 11.3662 | 2.4046 | 2.8234 | 11.3662 |
| Collision / Detailed | 2.8955 / 2.6316 / 2.5814 | 2.5814 | 2.6316 | 2.8955 |
| JoinLoop / Detailed | 1.0362 / 1.3284 / 1.0831 | 1.0362 | 1.0831 | 1.3284 |

Collision 正式顺序：Off/Coarse/Detailed → Coarse/Detailed/Off → Detailed/Off/Coarse。

| 配对轮次 | Coarse 方法耗时 | Detailed 方法耗时 | Detailed 相对增幅 |
| --- | --- | --- | --- |
| 1 | 2.8234 | 2.8955 | +2.55% |
| 2 | 2.4046 | 2.6316 | +9.44% |
| 3 | 11.3662 | 2.5814 | -77.29% |

中位数比值为 −6.79%，但观察到的范围足以改变 10% 判定：最大 Detailed / 最小 Coarse 对应 +20.42%。
该范围只是保守的观察范围检查，不是置信区间；没有删除 11.3662 ms 或 Sparse 的 12.0333 ms 样本。
校准结论是 **Inconclusive**，不是“通过”或“探针加速”。不以跨模式差值计算任何阶段耗时。

## 5. 同一运行内的互斥阶段

每个夹具选择“方法总时间居中的那一整条记录”，不把各阶段独立中位数拼起来：

| 夹具 | 代表记录 | MethodTotal ms |
| --- | --- | --- |
| Sparse | 01-Sparse-Detailed-r1-dop1 | 3.3771 |
| Collision | 11-Collision-Detailed-r2-dop1 | 2.6316 |
| JoinLoop | 19-JoinLoop-Detailed-r3-dop1 | 1.0831 |

以下均为原位单调时钟墙钟观测，含探针开销和可能的调度抖动，不能直接解释为无探针 CPU 占比。

| 互斥阶段 | Sparse ms（占比） | Collision ms（占比） | JoinLoop ms（占比） |
| --- | --- | --- | --- |
| UsedFacts | 0.7051 (20.88%) | 0.6997 (26.59%) | 0.3116 (28.77%) |
| PlanBuild | 0.9870 (29.23%) | 0.6989 (26.56%) | 0.3330 (30.75%) |
| DefinitionSetup | 0.4533 (13.42%) | 0.2085 (7.92%) | 0.1144 (10.56%) |
| Fixpoint | 0.1626 (4.81%) | 0.1161 (4.41%) | 0.0528 (4.87%) |
| CandidateIndexBuild | 0.0912 (2.70%) | 0.0227 (0.86%) | 0.0118 (1.09%) |
| CandidateLoop | 0.6153 (18.22%) | 0.7699 (29.26%) | 0.2067 (19.08%) |
| ExplicitSources | 0.2274 (6.73%) | 0.0498 (1.89%) | 0.0212 (1.96%) |
| ReturnBoundary | 0.1121 (3.32%) | 0.0613 (2.33%) | 0.0291 (2.69%) |
| ResultMaterialization | 0.0230 (0.68%) | 0.0045 (0.17%) | 0.0024 (0.22%) |
| Unattributed | 0.0001 | 0.0002 | 0.0001 |

21 条记录都满足阶段和不超过 MethodTotal；非 Off 的最大未归因比例为 **0.0102%**（向上取整）。
残差来自边界外实测差额，未通过填零或平衡项分摊成本。
三个代表样本 fixpoint 约占 4.4%–4.9%，只说明这些小输入形态；不能回填旧 G5 或 NPC 分解数字。

CandidateLoop 子项与残差如下；TryGetCandidates 已包含 AddReachable/Contains，不再把它们重复相加：

| 子项，ms | Sparse | Collision | JoinLoop |
| --- | --- | --- | --- |
| TryGetCandidates | 0.0555 | 0.3917 | 0.0603 |
| DefinitionLookup | 0.0061 | 0.0250 | 0.0060 |
| FactsMatch | 0.0028 | 0.0096 | 0.0026 |
| CandidateCollect | 0.0351 | 0.0480 | 0.0137 |
| CandidateLoopResidual | 0.5158 | 0.2956 | 0.1241 |

原始 NsPerActualFactsMatch 分别为 43.75 / 29.63 / 34.67 ns；它们有分母且来自实际执行的匹配调用，
但受时钟量化和未通过资格判定的观察者开销影响，**仅为未经校准的原始商，不用于函数成本结论**。
细项未覆盖的循环、集合处理和探针记账保留在 CandidateLoopResidual，不强分给 FactsMatch。

排队与串行提交单列：

| 阶段，ms | Sparse | Collision | JoinLoop |
| --- | --- | --- | --- |
| QueueWait | 0.0956 | 0.0874 | 0.0653 |
| Commit | 0.1325 | 0.2360 | 0.0486 |

QueueWait 的边界是 DataFlow 批次组装完成、进入执行器前到方法开始，包含调度准备及批内等待，
不是精确的 channel-only wait，也不是 CPU 工作时间。Commit 只包原串行图提交，二者均不混入方法内 Fixpoint。

## 6. 实际扫描量

所有 Detailed 重复轮次、预热和 Sparse DOP=2 的相应计数逐字段一致，未跨方法/轮次累加。
扫描次数与输出数量分别列出：

| 计数 | Sparse | Collision | JoinLoop |
| --- | ---: | ---: | ---: |
| UsedFacts operation 访问 | 577 | 779 | 324 |
| Plan operation→node 访问 | 577 | 779 | 324 |
| Plan operation node 再扫 | 577 | 779 | 324 |
| Plan return 检测短路前实际访问 | 450 | 770 | 323 |
| CFG incoming / outgoing cache 条目访问 | 68 / 67 | 168 / 167 | 55 / 54 |
| CFG 保留前驱边 / 后继边数量 | 67 / 67 | 167 / 167 | 54 / 54 |
| 定义收集 operation 访问 | 577 | 779 | 324 |
| 有效定义 / 流节点数量 | 65 / 580 | 99 / 784 | 44 / 329 |
| 出队 / transfer / set compare 次数 | 580 / 65 / 580 | 784 / 99 / 784 | 333 / 48 / 333 |
| Fixpoint predecessor 访问 / union | 67 / 67 | 167 / 167 | 58 / 58 |
| Candidate predecessor 重扫 / union | 65 / 65 | 163 / 163 | 50 / 50 |
| used fact 访问 / TryGetCandidates 调用 | 128 / 128 | 614 / 614 | 222 / 222 |
| posting / 实际 Contains 调用 | 128 / 128 | 37,764 / 37,764 | 4,510 / 4,510 |
| 返回候选条目次数 / 实际 FactsMatch 调用 | 64 / 64 | 324 / 324 | 75 / 75 |
| PostingPerUse | 1.00 | 61.50 | 20.32 |
| PostingPerReturnedCandidate | 2.00 | 116.56 | 60.13 |
| 不可达拒绝率（分母 posting） | 50.00% | 98.80% | 98.34% |
| seen 重复拒绝数 / 实际 seen 检查数 | 0 / 64 | 128 / 452 | 0 / 75 |
| seen 重复拒绝率 | 0.00% | 28.32% | 0.00% |
| 匹配通过率 | 100% | 100% | 100% |
| touched 标记 / 清理条目 | 64 / 64 | 324 / 324 | 75 / 75 |
| matches、touched 的 Count / Capacity 高水位 | 各 1 / 4 | 各 1 / 4 | 各 4 / 4 |
| 原始 / 唯一 / 重复候选 | 132 / 132 / 0 | 424 / 262 / 162 | 120 / 68 / 52 |
| CandidateLoop 原始 / 新增唯一 | 64 / 64 | 324 / 162 | 75 / 23 |
| ExplicitSources 原始 / 新增唯一 | 65 / 65 | 97 / 97 | 42 / 42 |
| ReturnBoundary 原始 / 新增唯一 | 3 / 3 | 3 / 3 | 3 / 3 |

Collision 四类索引的精确展开：

| 索引 | 建索引键 / posting 数 | 查询 / 命中 | 扫描 posting | 不可达拒绝 | seen 检查 / 重复拒绝 | 追加 matches |
| --- | --- | --- | ---: | ---: | --- | ---: |
| Location | 5 / 99 | 614 / 614 | 11,844 | 11,552 | 292 / 0 | 292 |
| Root | 4 / 99 | 162 / 162 | 10,368 | 10,304 | 64 / 32 | 32 |
| Base | 1 / 64 | 614 / 162 | 10,368 | 10,304 | 64 / 64 | 0 |
| BasePath | 2 / 64 | 162 / 162 | 5,184 | 5,152 | 32 / 32 | 0 |

总 posting = 37,312 次不可达拒绝 + 452 次 seen 检查；seen 检查 = 128 次重复拒绝 + 324 次追加。
这些都是执行点的实际事件，不把去重后长度伪装成扫描量。没有分母的回退专属比率应为“不可计算”，不是零成本。

## 7. 验证、决定与剩余边界

- 初始化构建通过；正式 Release 构建 0 警告、0 错误。常规测试工程仍输出既有 Verify/xUnit 警告，未称全仓零警告。
- 新增诊断测试经历缺失诊断时 3 项失败，再验证阶段/计数、模式等价、默认关闭、预算退出、多方法隔离、重复运行隔离、真实重访和独立合成回退。
- 最终定向 Contract **30/30 通过**，证据：[TRX](../../Build/DataFlowTailMeasurement/dataflow-focused-final.trx)。
- 完整分层回归：Unit **117/117**，Contract **531 通过 / 1 失败**，不能称全仓回归全绿。证据：[分层命令与退出码](../../Build/TestResults/20260925-022703-c5ef3f3f/run.json)。
- 唯一失败为 ProductionProjectReferences_MatchTheTargetDependencyGraph：NLISSN.csproj 的既有 ProjectJson 引用未列入架构测试的预期清单。该用例仅逐行读取项目引用，不执行 DataFlow；本次未修改这两个文件。独立重跑仍同样失败，见[复现 TRX](../../Build/DataFlowTailMeasurement/reference-contract.trx)。保留此项，不为本测量改写另一功能的项目边界。
- harness 一致性检查（**含真实 CLI smoke**）通过；463 个运行时记录的源码哈希重新核对无变化，报告资产与原始输出逐字节相同，文档链接与新增文件空白检查通过；范围化 git diff --check 通过。结构化证据见[最终验证记录](dataflow-tail-small-batch-20260925/verification.json)。
- [独立制品审计](dataflow-tail-small-batch-20260925/audit.json)重新读取完整图/提交序列、校验哈希、21 次运行顺序、预算、计数守恒及时间父子关系，结果 Verified。

优化决定：先为同基址 posting 的重复枚举/不可达筛选及 PlanBuild 重复扫描准备独立候选方案；
不要因为流节点多就并行 worklist，也不要基于未校准的 FactsMatch 纳秒商决定重写匹配逻辑。
当前批次已经区分结构问题，因此没有扩展 4 倍 Collision 或更大矩阵。
若下一步要作用于 NPC，须保留真实绑定上下文和完整签名，重新获取 SetDefaults 与 AI 的同方法同次证据；本报告不能替代它。

完成的是诊断实现、默认批次执行及有边界的报告。观察者门槛仍为不确定，真实源码回退和 NPC 真实长尾未验证，方法内并行和计划载体压缩均未实施。

## 8. 设计条目验收索引

| 设计要求 | 当前证据与判定 |
| --- | --- |
| 同二进制、输入、方法、完整执行 | environment、source-state、CSV 身份字段；21 条正常结果 |
| 九个互斥阶段、四个子项、两个残差 | 原始 ticks、独立审计父子边界通过 |
| 真实 operation / CFG / posting / 短路计数 | CSV 逐事件计数、守恒检查、原 FactsMatch 短路未修改 |
| 三个非空夹具及轮次预算 | 三个固定源码文件、覆盖检查、21 次完整矩阵、5.155 秒 |
| 校准顺序、10% 门槛与抖动处理 | 三轮 Latin rotation；门槛不确定，原始慢样本全部保留 |
| 完整图与原始发布序列等价 | 18 组逐字节对比 + Sparse DOP=2 |
| 空键与失败匹配 | 独立内部契约检查通过；真实源码回退链覆盖缺口明确保留 |
| 每方法所有权及复用暂存 | 无原子计数；不保留复用 matches 引用；方法/轮次隔离测试 |
| 失败与超时不当成功 | watchdog、失败记录、预算退出契约；正式批次未触发超时 |
| 真实 NPC 归因/并行决定 | 无该证据，明确不推广、不执行方法内并行 |
