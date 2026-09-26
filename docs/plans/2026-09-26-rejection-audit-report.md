# 近期文档「拒绝/未实施」项审计报告

日期：2026-09-26。性质：**只读审计**。本次未修改任何产品代码、未运行任何构建或测试。

## 0. 审计范围与方法

范围：2026-09-22 至 2026-09-25 的设计、计划、执行与 research 文档，共 47 份近期 Markdown，
重点覆盖 `unified-work-scheduler`（设计 + 执行）、内存优化 M1–M7、性能优化 P01–P05、
frozen-edge / dictionary 系列与 `docs/research/`。

判据：对每一条「不采用 / 未实施 / 放弃 / 否决 / 未做 / 待办」判定其**理由是否正当**。

- **正当**：门槛经实测未过（有数字）、会破坏已命名的不变量/等价 oracle、
  前置类型经核实不存在（有类型名）、用户明确另行约定、有已解释的结构性阻塞。
- **可疑**：无理由或只有空泛措辞（「代价过高」「超出范围」）而无证据；
  理由与同文档或兄弟文档**自相矛盾**；理由对代码的**事实陈述错误**；
  门槛**从未真正运行**却被当作已定论；引用的证据**在磁盘上不存在**；循环推迟。

方法：全文精读 → 对每处代码声称用 grep/read 到 `src/`、`tests/` 核实 →
核对引用的 `Build/` 与 `docs/benchmarks/` 证据路径是否真实存在 → 交叉比对 `Context/feature_list.json`。

---

## 1. 结论提要

**未发现「无正当理由即拒绝实现」的条目。** 该仓库近期文档在这一点上异常严谨：
多数拒绝都带实测数字、不变量名或具体风险，且大量条目**主动自我标注为「本条已过期」**。

但审计发现 **3 类共 6 项实质问题**，其中 1 项为高严重度。它们不是「该做的没做」，
而是「状态与权威登记不一致」和「个别数字/事实轻微失准」：

| # | 类别 | 严重度 |
| --- | --- | --- |
| 1 | S3/S4 主体在权威状态文件中**无登记**，形成「推迟黑洞」 | **高** |
| 2 | M3 文档变异结果数字与日志不符 | 低 |
| 3 | `feature_list.json` 状态词汇两种拼写并存 | 低 |
| 4 | `DataFlowPass.cs` 编辑边界被越过（但理由已充分记录） | 低（可接受） |
| 5 | M2 §8.7 夹具偏差（硬约束，非缺陷） | 无（澄清） |
| 6 | 若干「代价过高」类措辞缺少量化（可接受但偏弱） | 低 |

---

## 2. 【高】S3/S4 主体未登记，且其替代方案不存在

### 2.1 事实

`docs/plans/2026-09-24-unified-work-scheduler-design.md:143` 明确记载：

> **不保留函数级并行**（已决策）。方法级 `WorkBatch` 组装（`CpgWorkBatchBuilder`）删除。

`:765` 备选否决表再次强调：

> **保留函数级并行（方法批）** → 已决策放弃；函数级并行是单文件内并行的旧机制，被**外部桶/分片划分**取代。

而 `feature_list.json` 中**没有任何 S3 登记**。全部 `scheduler` 相关条目仅：

```
unified-work-scheduler-s1-kernel                       completed
unified-work-scheduler-s2-rule-dag                     in_progress
unified-work-scheduler-s2-blocked-sites                blocked
unified-work-scheduler-gate-g4-sharding-equivalence    completed
unified-work-scheduler-gate-g4b-shard-adapter          todo
unified-work-scheduler-s4-runtime-log-effective-limits completed
```

- **S3**（8 个 pass 迁移 + 删除 executor）**无登记**。
- **S4 只登记了 `s4-runtime-log-effective-limits`**（标题自述范围是「日志报告内核有效配额」），
  而 S4 的另一半「删死层与死参数」（执行文档 `:396` Task S4-1）**无登记**。

**核实（`Verified by`）**：`DocumentShardPlanner`、`DocumentShardPlan` 在 `src/`、`tests/`
全量检索 **0 命中**（不存在）；`CpgWorkBatchBuilder`、`CpgWorkBatchExecutor`
仍在生产路径（`NLCPGBuilder.cs:117-118`、`:170-174`，被 4 个 pass 调用）。

### 2.2 为何这是问题

这不是「拒绝实现」，而是**执行边界失踪**：

1. 设计已把「删除方法级 WorkBatch」定为**已决策**，但该决策在唯一权威状态来源里**不可见**。
   任何按 `feature_list.json` 判断「还差什么」的读者，都会**看不到 S3/S4 主体仍欠着**。
2. 被承诺的替代方案（「外部桶/分片划分」）**至今不存在**——
   `gate-g4b-shard-adapter` 仍为 `todo`，其前置类型零命中。
   即：**旧机制仍在使用，新机制尚未建立，而拆除任务未被登记。**
3. 执行文档 `:652` 自述「S2-1/S2-2 已完成；**S2-3/S2-4 未做**」，`:278` 标「S3 尚未实施」，
   `:487` 标「S5 设计，尚未实施」——文档层面是诚实的，**但状态文件层面是空的**。

### 2.3 建议（不代为执行）

为 S3、S4-1、S5 主体补登 feature 条目（哪怕状态为 `todo`），
使权威状态文件与设计/执行文档的已知欠账对齐。否则「M1 之外还有哪些大事没做」
这个问题在权威来源上无法回答。

---

## 3. 【低】M3 文档变异结果数字与日志不符

`docs/plans/2026-09-25-workbatch-consume-results-execution.md:161` 称变异结果为：

> 结果：**12 通过 / 1 失败**。

**日志实际值**（`Build/M3-workbatch-consume/mutation-collect-true.log`）：

```
失败! - 失败: 1，通过: 11，已跳过: 0，总计: 12
```

即应为「**11 通过 / 1 失败 / 共 12**」。文档把**总计**写成了**通过数**。

同节 `:166` 称"定向 Contract 复跑仍为 34/34"，与
`after/contract-targeted.restored.log`（`通过: 34，总计: 34`）**一致**，无问题。
另 `:149` 的"35 通过"与 `after/contract-targeted.final.log`（`通过: 35，总计: 35`）**一致**。

结论：**单纯笔误，结论方向（新判据有判别力、旧负对照不失败）未被影响**。
但按仓库自身的证据纪律，数字应与日志逐字对应。

---

## 4. 【低】状态词汇两种拼写并存

`feature_list.json` 的 `status` 取值分布：

```
done          45
in_progress    6
completed      3
complete       2
superseded     2
todo           2
blocked        1
pending        1
```

`completed`(3) 与 `complete`(2) **同义并存**，且分组有规律：
`unified-work-scheduler-*` 用 `completed`，`dataflow-candidate-scan-pruning` /
`dataflow-plan-construction-reuse` 用 `complete`。

任何精确等于 `"complete"` 的查询都会**静默漏掉** 3 条 `completed` 的 feature（其中
`s1-kernel` 带 126 条验证证据）。建议统一为单一拼写。

---

## 5. 【记录】`DataFlowPass.cs` 编辑边界被越过，但理由充分

执行文档 `:307` 声明：

> **DataFlow 并发编辑边界：** 本报告不修改 DataFlowPass.cs 或 NLCPGDataFlowSetTests.cs。

但附录 X（`:2426`）**确实修改了它**（新增 `PlanDataFlowStage` / `CommitDataFlowStage`）。

**判定：可接受。** 附录 X 明确解释了越过原因（关闭 R-3 最后具名缺口），
附录 V `:2127-2129` 更主动标注「**未做的原因是编辑边界，不是技术不可能**」，
并警告不得把**遗留缺口谎报成已关闭**。这是**正面的诚实标注**，不是掩盖。

同类：附录 V 追溯修正了自己一度写错的「先于任何 worker」措辞（`:2131-2145`）。

---

## 6. 【澄清】M2 §8.7 夹具偏差是硬约束

`docs/plans/2026-09-25-interprocedural-edge-index-compaction-execution.md:263-276`
记录新增测试**未**使用计划 §4 指定的 `CpgBuilderSources.cs`，而用手建可变图。

**判定：正当，且理由是可验证的硬约束。** 文档给出的机制链：
`FreezeQueryIndex()` → `ReclaimConstructionCapacity()` → `PendingEdgeBuffer.Release()`，
之后 `EnumeratePendingEdgesLazily()` 必抛
`InvalidOperationException: The pending-edge buffer was released at freeze time...`。
`InterproceduralEdgeSnapshot.Create` 入参正是 `IEnumerable<PendingEdge>`，
故只有可变图能喂给它。文档并注明**产品代码从未为测试放宽**。

---

## 7. 【弱项】若干「代价过高」类措辞缺少量化

以下拒绝**方向合理**，但理由停留在定性措辞，未给出数字或不可行性证明。
不构成缺陷（同表内通常另有更强的理由），仅记录为可加固点：

| 文档 | 行 | 措辞 | 备注 |
| --- | --- | --- | --- |
| M5 邻接压缩 | :26 | 「超出局部存储优化，证明和集成代价过高」 | 同表 `:25` 有具体顺序理由 |
| M1 计划压缩 | :23 | 「精确载荷相等、临时表共存与净收益验证成本过高」 | 属范围界定 |
| M1 计划压缩 | :24 | 「超出两三个局部载体的必要改动」 | 属范围界定 |
| M6 所有权 | :23 | 「引入归还时机、异步生命周期和悬空使用风险」 | **有具体风险，正当** |
| M7 SparseSet | :22 | 「难以证明长期保留净收益，巨型方法会拉高容量」 | **有机制理由，正当** |

这些属**范围管理决策**（总索引 `:27` 已约定「若发现必须修改排除项，停止该项并记录不采用原因，
不自动扩大项目」），且用户原话即「代价过大的不采用」。**故判定为正当。**

---

## 8. 判定为正当的条目（覆盖清单）

以下均经核实为**有据的拒绝/推迟**，不构成缺陷：

**unified-work-scheduler 执行文档（28 个附录，自我批判极彻底）**
- 附录 G：实测证明「行数 × 常量」**在原理上**不能成为 bytes 上界
  （同行范围 0..999 实测载荷差 **1,349.1×**）⇒ R-4 配额数值不做，正当。
- 附录 G.5：`CpgWorkBatchBuilder.cs:5-6` 两个私有默认常量经核实为**死代码**
  （生产调用方 `NLCPGBuilder.cs:170-173` 显式传第三实参）⇒ 记录未删，正当。
- 附录 AA.1：`StageDependencyTable` 中 `Syntax`/`Operation` 两条边**恒真**
  （不经 `RunOptionalPass`，从不进校验序列）⇒ 已补记，属**修复**而非欠账。
- 附录 V：`ControlDependence` 设计上不能前移（依赖 `Dominance` 运行期 `_dominanceOverlays`）⇒ 正当。
- 附录 X：`DataFlow` 前移的依据逐条源码确证（规划只需 `OperationInventory`，不需邻接内容）⇒ 正当。

**M1–M7**
- M2 §8.6：明确「未跑 Performance 档」「不声称进程峰值下降」、
  「68.87% 与 67.24% 口径不同不得混引」⇒ 诚实边界。
- M3 §7.5：只报告**结构改善**，不声称峰值/晋升/端到端分配下降 ⇒ 诚实边界。
- M3 §7.6：如实记录并发工作流导致的编译错误与文件锁竞争（`MSB3027`/`MSB3021`）⇒ 环境归因清晰。
- M4 §1：三项「本轮不采用」均涉及确定性 ID 分配、边输入接口或跨组件契约 ⇒ 正当。
- M5 §7.3：已修正为 3/9 并给出真实夹具名与实测节点数 ⇒ 已修复。
- M6 §1：`availableAnchors` 端点校验保持不削弱（属行为削弱而非优化）⇒ 正当。
- M7 §1：三项「不采用」分别涉及生命周期、别名与另一项存储决策 ⇒ 正当。

**P01–P05**
- P01/P02 的「不采用」均带**实测**依据（P02 T4：`BuildFlowNeighborsFromCache` 仅占
  Collision 3.16% / Sparse 5.95% / JoinLoop 4.54% 分配）⇒ 正当。
- P03/P04/P05 的验证记录明确区分「未验证也不声称」⇒ 诚实边界。
- P02 T3 拒绝理由经**代码核实为真**：`MethodDataFlowPlan.OperationNodes` 投影
  确实已存在（`DataFlowPass.cs:93`、`:762`），T3 会「只减少一次字典查找却增加等量常驻」⇒ 正当。

**research**
- `2026-09-25-additional-memory-primary-sources.md`：每方向均有「源码事实 / 设计方向 /
  边界 / 最小验证建议」，并声明**未执行验证、未测收益**⇒ 诚实边界。
- `2026-09-24-cpg-parallel-pass-data-race-and-fix-options.md`：有取舍表与明确推荐路径
  （按 E 立不变式、用 B 落地、随 S3-1 完成），并明确**不推荐** C 作最终方案 ⇒ 正当。

**证据存在性（全部通过）**
- 47 份近期文档的**相对链接零失效**。
- 抽查引用证据路径**全部存在**：`Build/g5-报告-N5-N6-N1.md`、
  `docs/benchmarks/nlissn-version4-dop12-diagnostics.md`、P01/P02 报告与
  `Build/DataFlowTailMeasurement/p01-pre-r3|p01-post-t2t3|p02-pre|p02-post`、
  `Build/P02Probe/compare_batches.py`、`Build/g0m-calibration/*.log`（226 个文件）、
  `Build/MemoryOptimization/M1..M7`（15 个子目录）。
- 文档引用的 `StageQuotaScope` 过滤器**能正确匹配**真实文件
  `StageQuotaScopeContractTests.cs`（初次检索未命中是我模式过严，已撤回该疑点）。

---

## 9. 边界（本报告不得越读）

1. 本次**只读**：未修改任何产品代码、文档或状态文件；**未运行任何构建、测试或性能采样**。
2. 判定依据是**静态阅读 + 代码检索 + 证据路径存在性**，
   **未**重跑文档声称的任何测量，故不构成对其实测数字的独立复现。
3. 「未发现无正当理由的拒绝」限于**本次覆盖的 47 份近期文档**；
   更早文档与全部 `设计docs/历史设计/` 未逐一审计。
4. 第 2 节的高严重度项是**登记缺失**，不是「实现被无故拒绝」——
   执行文档本身对 S3/S4/S5 未实施是**如实标注**的。
