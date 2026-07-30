# 图与规则绑定校验器提案

## 目标

建立两层验证器：NLCPG 验证冻结图和能力覆盖；NLISSN.Core 验证规则输出、语法锚点、图绑定和决策绑定。验证器把当前隐式假设变为稳定诊断，优先在测试、调试和构建边界运行，不进入常规规则热路径。

## 当前事实

- `MarkingEngine.BindMarkRecord` 通过 `IRuleGraphBinding` 把 syntax node 绑定到主图节点；缺失时抛异常。
- `MarkAnalysisSnapshot` 按文件/span 建 binding index，并以节点种类优先级选择主节点。同一 span 同时有 syntax、operation、call site 或 symbol 节点是允许的。
- `NLCPGGraph.FreezeQueryIndex()` 生成稳定邻接索引，持久化层还验证分片哈希与二进制结构。
- 规则已有 `Allowed*NodeKinds`、`Consumes`/`Produces` 和 semantic tag 校验，重点在单条输出；图、能力、绑定和 decision 的整体关系缺少统一报告。

Joern 的 post-frontend validator 检查唯一 full name、REF fan-out、AST/ARGUMENT 入边和 order 冲突。NL 应保留其“可压缩、稳定、可选择 fail-fast”的形式，检查项必须按 Roslyn 与 NLCPG 的多表示模型重写。

## 范围

### 包含

- 冻结图的 node ID、stable anchor、edge endpoint、排序和索引一致性验证。
- 所请求 CPG capability 与实际已构建 overlay 的覆盖验证。
- syntax-to-primary-graph binding 的可解析性、span/file 一致性和优先级唯一性验证。
- mark、propagate、lift、proposal、decision 的结构契约和 evidence root 验证。
- 分级、压缩、稳定排序的 validation report 与严格模式。

### 排除

- 将每个 `SyntaxNode` 映射为唯一 CPG 节点。
- 在验证器中重新运行 Roslyn 数据流、构图 passes 或规则。
- 用校验器修复图或替规则猜测缺失输出。
- 让生产 CLI 默认对大型目录执行全图深度验证。

## 验证分层

| 层 | 位置 | 输入 | 必查项 |
| --- | --- | --- | --- |
| `CpgGraphValidator` | `src/NLCPG/Validation/` | 已冻结 `NLCPGGraph`、build metadata | NodeId 唯一、edge 端点存在、稳定 anchor 一致、索引与边集合一致、capability 依赖闭包。 |
| `RuleBindingValidator` | `src/NLISSN.Core/Validation/` | `RuleContext`、阶段输出、compiled rule graph | 规则声明、输出结构、semantic tag、syntax anchor、primary binding、payload 状态。 |
| `DecisionBindingValidator` | `src/NLISSN.Core/Validation/` | `DecisionUnit`、`RuleDecision`、evidence | fragment binding、replacement binding、冲突结果、evidence root、rewrite anchor。 |
| `AnalysisValidationReport` | Core 公共模型 | 分层结果 | 稳定错误 key、数量、样例锚点、严重度、是否可继续。 |

所有 validator 只读。`CpgGraphValidator` 可被 ContractTests 使用；Core validator 通过接口读取图验证摘要，避免 NLCPG 依赖 Rule 类型。

## 关键检查项

### CPG 图

1. 每个 `NodeId` 在同一冻结图中唯一，所有 edge 源/目标都存在。
2. 每个 `StableNodeAnchor` 指向有效文件/span；同锚点允许多种 node role，但同 role/ordinal 不能重复。
3. `NLCPGGraphIndex` 的入边、出边、按 kind 索引和按文件 span 索引与冻结边/节点集合相等，且各列表排序稳定。
4. `RequestedCapabilities` 的依赖闭包与真正运行的 passes 一致。请求 `ControlDependence` 时必须有 CFG、Dominance 和 control-dependence edges 或明确“不适用”的原因。
5. 分片导出前验证全局 NodeId、boundary edge 和路由索引引用的分片均存在；该检查不读取未发布构建。

### 规则与绑定

1. 每个 emitted mark 都满足 producer 的 node kind、结构 selector 与 semantic tag。
2. primary graph node 与 syntax anchor 同文件、同 span，或属于允许的显式 synthetic binding；绑定优先级并列时报告 `AmbiguousPrimaryBinding`。
3. `Consumes`/`Produces` 的每个已交付值来自已声明 producer port；禁用、空输出、失败和取消状态保持可区分。
4. payload 有已注册类型、完整 query status 和原始 source mark；未知 payload 不能进入 proposal。
5. rule 所需 capability 缺失时，规则节点必须被标记为 unavailable/disabled，不能以空结果伪装成功。

### 决策和改写

1. 每个 fragment 的 NodeId 都有同一 analysis epoch 的 syntax binding。
2. Replace 决策有且只有一个 replacement binding；Delete/Skip 没有 replacement。
3. 选择 winner 的冲突组包含所有候选，并为淘汰或合并保留证据边。
4. rewrite 使用的 anchor 仍是原始 tree 的 descendant；跨 epoch 或跨 tree 引用为错误。

## 结果协议

```text
ValidationIssue
  - Code: CPG001 / BIND014 / DEC009
  - Severity: Error / Warning / Info
  - StableKey: code + rule/node + file/span
  - Message: stable, human-readable
  - Anchor: optional file/span/NodeId

AnalysisValidationReport
  - Issues ordered by StableKey
  - Counts by code and severity
  - IsValid
  - CapabilityAvailability
```

严格模式遇到 Error 失败；诊断模式返回 report 并阻止不安全 decision 写入。Warning 只适用于确认可降级的情况，例如未请求的 optional overlay；它不能掩盖缺边、错锚点或未声明 payload。

## 执行阶段

### 阶段 1：确定不变量的反例测试

在 `tests/NLISSN.ContractTests/Cpg/` 与 `tests/NLISSN.HostTests/` 新增人为构造的无效图、重复 role anchor、悬空 edge、错排序索引、缺 capability、错误 primary binding、未声明输出、坏 replacement 和无 evidence decision。每个用例断言稳定 code，不断言脆弱的整句文本。

### 阶段 2：实现冻结图验证器

新增 `CpgGraphValidator`，在 `FreezeQueryIndex()` 完成后由测试显式调用。先验证内存图，再为分片/路由索引增加独立验证器，避免把持久化 I/O 混进基本图验证。

### 阶段 3：实现 Core 绑定验证器

在 stage execution 的输出归并后运行 `RuleBindingValidator`。初期只在 Host/ContractTests 和显式调试选项启用；输出报告存到 analysis result，不改变生产默认。

### 阶段 4：接入 decision 与 evidence

Decision Evidence DAG 落地后，`DecisionBindingValidator` 验证最终 root、合并和拒绝关系。没有 evidence 的 legacy fixture 必须显式标记过渡模式，不能静默跳过。

### 阶段 5：形成架构守卫

将稳定的源文件边界和 capability closure 检查加入 ContractTests。保留运行时 validator 对动态事实的覆盖，不把所有规则行为复制为静态文本扫描。

## 验收门槛

- 所有列出的非法图和非法规则输出得到稳定错误 code。
- 合法多表示同 span fixture 通过；只有同 role/ordinal 或同 primary 优先级冲突才失败。
- DOP 1/16 的验证 report 排序、计数和 stable keys 相同。
- 深度图验证只在明确选择时执行；默认 CLI 的结果和性能基线不变。
- 持久化构建仅在完整、已验证的 routing index 和分片引用下可见。
- decision validator 阻止错 tree、无 binding、错 replacement 或无 evidence root 的改写。

## 风险与约束

| 风险 | 处理 |
| --- | --- |
| 校验器把正常多表示图误报为冲突 | 以 stable role/ordinal 和 primary priority 为准，不以 span 唯一性为准。 |
| 大图验证影响目录吞吐 | 采样和全量模式分开；默认只计数关键不变量。 |
| 错误文本成为脆弱测试契约 | 测试稳定 code、anchor 和严重度，文本只供人读。 |
| runtime report 与 rewrite 脱节 | `DecisionBindingValidator` 在 rewrite 前执行，Error 直接阻止写入。 |
| capability “未请求”与“构建失败”混淆 | report 区分 NotRequested、Unavailable、Built 和 NotApplicable。 |

## 依赖与后续关系

该提案先于决策证据 DAG 的强制根校验。它为有界结构查询验证 edge kind、预算和 primary binding；Flow Summary 2.0 与 Symbol Usage Profile 将各自的解析状态作为可验证输入，而非普通字符串诊断。
