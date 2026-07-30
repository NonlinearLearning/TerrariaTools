# 决策证据 DAG 提案

## 目标

让每个最终 `RuleDecision` 都能追溯到触发它的原子 mark、传播或 lift、使用的 CPG 关系、流摘要和预算截断信息。证据用于审计、CLI 输出、回归比较和失败诊断；它不参与规则调度，也不改变改写决策的优先级。

## 当前事实

- `MarkRecord` 保存 `RuleId`、语法节点、主图节点和文本 `Reason`。
- `PropagatedMarkRecord` 与 `LiftedMarkRecord` 只保存一条 `SourceMark` 和 `Depth`；多输入传播和结构化 `Payload` 的完整依据无法保留。
- `DecisionUnit` 已有 CPG fragments、relations 和 syntax bindings，但 `DefaultDecisionPolicy` 合并覆盖决策时把原因压成字符串。
- `RuleGraphAnalysisExecutor` 已收集节点状态和 telemetry，却没有把它们绑定到输出决策。

这些事实足以解释单一命中，不能稳定解释“哪个规则输入、哪条图边、哪个冲突裁决导致了此改写”。

## 范围

### 包含

- `NLISSN.Core` 中不可变、追加式的 `AnalysisEvidenceGraph`。
- mark、propagate、lift、CPG 查询、流摘要、proposal、冲突合并和最终 decision 的证据节点与有向边。
- 每个 `RuleDecision` 的稳定 `EvidenceRootId`。
- 预算截断、能力不可用、禁用规则、空输出和拒绝候选的结构化说明。
- 面向 CLI 和测试的稳定 JSON 投影。

### 排除

- 将证据 DAG 用作 `RuleGraph` 的依赖或调度来源。
- 保存完整 Roslyn 或 CPG 对象、把 source text 复制进每个节点，或序列化 `SyntaxNode`。
- 为每条候选保存无界完整数据流路径。
- 修改删除、替换、冲突优先级或 rewrite 行为。

## 目标模型

```text
Seed mark ──> propagation ──> lift ──> proposal ──> decision
                 │              │           │            │
                 ├─ CPG edge ───┘           ├─ conflict ─┘
                 ├─ slice result            └─ inherited candidate
                 └─ flow-summary resolution
```

新增以下 Core 类型，均为值对象或只读集合：

| 类型 | 职责 |
| --- | --- |
| `AnalysisEvidenceNode` | 节点 ID、种类、规则 ID、语法锚点、图 NodeId、摘要键、状态和简短描述。 |
| `AnalysisEvidenceEdge` | `Supports`、`DerivedFrom`、`UsesGraphEdge`、`UsesSummary`、`RejectedBy`、`MergedInto`、`TruncatedBy`。 |
| `AnalysisEvidenceGraph` | 按稳定 ID 去重、按插入序保留的节点和边；构造后不可变。 |
| `DecisionEvidence` | 最终 decision 到证据根的引用以及已使用预算摘要。 |
| `EvidenceAnchor` | 文件、span、syntax kind、可选 CPG NodeId；不持有 Roslyn 对象。 |

`MarkRecord`、`PropagatedMarkRecord`、`LiftedMarkRecord` 和 `DecisionUnit` 不新增可变集合。运行时将它们的证据 ID 存在内部分析结果侧表中，最终在 `PrototypeAnalysisResult` 暴露只读 evidence 投影。这样现有规则仍可创建记录，证据收集由执行适配层统一完成。

## 关键契约

1. 同一输入、同一规则图、同一 CPG 快照下，证据节点 ID、边顺序和 JSON 输出在 DOP 1 与 DOP 16 一致。
2. 证据 DAG 始终无环。候选被覆盖时创建 `MergedInto` 或 `RejectedBy` 边，不能修改旧节点。
3. 每个最终 `RuleDecision` 都有一个 `Decision` 节点和至少一条回溯到输入事实的路径；没有证据的决策在测试模式下失败。
4. `QueryBackward` 返回 `WasTruncated` 时，证据必须携带预算、原因和已访问节点/边数量。规则不得把截断结果表述为确定性证明。
5. 图节点和语法锚点只作为引用。源文件改写后，旧 evidence 不可用于新 epoch。
6. 节点描述使用受长度限制的稳定字段；可读原因继续在 CLI 渲染时生成。

## 接入点

| 阶段 | 现有入口 | 追加的证据动作 |
| --- | --- | --- |
| Mark | `MarkingEngine.ExecuteRule` | 为每个绑定后的 mark 建 seed 节点。 |
| Propagate | `PropagationEngine.ExecuteRule` | 从全部已声明输入建立 `DerivedFrom` 边；payload 只记录类型和稳定摘要。 |
| Lift | `MarkLiftingEngine.ExecuteRule` | 记录结构宿主和所有输入 marks。 |
| CPG 查询 | `MarkAnalysisSnapshot.QuerySliceBackward` | 记录 query spec、缓存命中与结果/截断。 |
| Propose | `RuleGraphAnalysisExecutor` | 为 `DecisionUnit` 记录输入端口、fragments、relations。 |
| 冲突收口 | `RuleDecisionEngine` / `DefaultDecisionPolicy` | 为覆盖、同锚点优先级和 replace 胜出写显式边。 |
| Rewrite | `PrototypeRewriter` | 只读取最终 decision evidence root，把 rewrite 结果作为附加状态，不反向影响决策。 |

## 执行阶段

### 阶段 1：先锁定输出契约

在 `tests/NLISSN.HostTests/Decision/` 新增 fixture，覆盖：直接删除、两条传播输入、lift、同锚点 Replace 覆盖 Delete、父节点合并子节点和查询截断。断言节点 ID、边、根引用和 DOP 等价，先不改生产代码。

### 阶段 2：实现 Core 证据模型与运行时收集器

在 `src/NLISSN.Core/Decision/` 增加模型，在 `Pipeline` 增加分析轮次范围的收集器。收集器按稳定锚点和规则节点 ID 分配 ID，使用单一稳定提交线程归并并行规则输出。

### 阶段 3：接入四个阶段和决策收口

依次接入 mark、propagate、lift、propose/resolve。每一步保持现有输出快照相同；缺少绑定或输入时产出 `Unavailable` 节点，不允许吞掉原因。

### 阶段 4：公开结果与 CLI 渲染

将 evidence 放入 `PrototypeAnalysisResult` 的可选字段。CLI 增加显式输出开关，默认不扩大常规结果。JSON 只输出锚点、规则、关系和预算，不输出完整源码。

## 验收门槛

- 所有最终 decision 都有完整 evidence root；人工构造无根 decision 的单元测试失败。
- 合并、冲突淘汰和截断三类情况能从 JSON 中得到机器可读原因。
- DOP 1/16 的 decision、rewrite、diff 和 evidence JSON 完全相同。
- 关闭 evidence 输出时，默认 CLI 内容、规则结果和改写文本不变。
- 单文件和目录分析的证据总量有条目和字节上限；超过上限返回明确截断节点。

## 风险与约束

| 风险 | 处理 |
| --- | --- |
| 并发阶段使 ID 顺序漂移 | 规则线程只产生局部事实；按 `RuleNodeId`、锚点和局部序号稳定归并。 |
| payload 包含不可序列化对象 | 只记录已注册 payload 的摘要；未知类型生成类型名和 `Unavailable`。 |
| 证据图过大 | 每个 analysis epoch 配置节点、边和路径预算；预算是结果的一部分。 |
| 改写后锚点失效 | evidence 绑定原始 epoch；rewrite 只追加生成状态，不重用旧锚点。 |

## 依赖与后续关系

本提案依赖图与规则绑定校验器保证锚点唯一。Flow Summary 2.0、Symbol Usage Profile 和有界类型结构查询完成后，分别通过 `UsesSummary`、`DerivedFrom` 和 `UsesGraphEdge` 扩展 evidence，不改变其基础协议。
