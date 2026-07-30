# 有界、带类型的结构查询提案

## 目标

为 `NLISSN.Core` 提供基于 NLCPG 冻结索引的显式查询接口。每个规则必须声明方向、允许边种类、节点角色、预算和能力需求；查询返回完整、截断、不可用或不确定状态。它替代 `NLCPGStructureViewBuilder` 对多个 syntax fragment 使用“全边种类、无向最短路”的隐式拼接。

## 当前事实

- `NLCPGSliceQuery` 已有 `AllowedEdgeKinds`、hop/path/definition/call-depth、visited node/edge、cache 与 caller fan-out 预算，并处理调用栈循环。
- `MarkAnalysisSnapshot.QuerySliceBackward` 缓存切片结果，但 `SliceQueryKey` 未纳入 `MaxVisitedNodes`、`MaxVisitedEdges`、`MaxCachedStates` 和 `MaxCallerFanout`。不同预算可能错误复用结果。
- `NLCPGStructureViewBuilder` 先收集各 fragment 内全部图节点，再在任意入/出边上寻找无向最短路径，最后加入选中节点间的所有边。
- NLCPG 已构建语法、符号、operation、CFG、数据流、支配、控制依赖和受限跨过程边；这些边不能被结构规则无差别混用。

Joern traversal DSL 的价值在于显式表达关系，NL 不需要复制 DSL。一个小型、强类型、可预算的 API 更符合 Roslyn-first 和 DOP 稳定性约束。

## 范围

### 包含

- 面向规则的 typed query specification 和固定关系 profile。
- 单锚点邻接、两锚点关系、有限路径和局部投影视图。
- 预算、能力、截断、分片不可用和缓存身份的完整参与。
- 内存图和冻结分片的一致 query result 协议。
- 由新查询构建的结构视图兼容适配器，逐步替换旧 builder。

### 排除

- 一门通用图查询语言或动态 lambda traversal DSL。
- 改写 NLCPG 的 `QueryBackward` 为第三方图库算法。
- 无界 all-path 查询、跨文件全图最短路或把 shard 临时物化为完整大图。
- 由规则自由指定任意字符串边名或绕过 capability 检查。

## 先决修复

先修复 `MarkAnalysisSnapshot.SliceQueryKey`，使其包含：

```text
AllowedEdgeKinds
MaxHops
MaxPaths
MaxDefinitions
MaxCallDepth
MaxVisitedNodes
MaxVisitedEdges
MaxCachedStates
MaxCallerFanout
```

增加同 sink、相同前六项、不同后四项的缓存隔离测试，分别覆盖 node budget、edge budget、cache state budget 和 caller fan-out。该修复独立提交；在此之前不得把切片结果用作删除安全证据。

## 目标模型

| 类型 | 职责 |
| --- | --- |
| `CpgRelationProfile` | 固定关系集合：`StructuralContainment`、`SemanticBinding`、`LocalDataFlow`、`ControlDependence`、`BackwardSlice`。 |
| `CpgQueryDirection` | `Incoming`、`Outgoing`、`Bidirectional`；只有 profile 明确允许才可双向。 |
| `CpgNodeSelector` | NodeKind、稳定 role、file/span、symbol key 或已绑定 NodeId 的交集；不支持任意 predicate。 |
| `CpgTraversalBudget` | hops、paths、definitions、visited nodes/edges、cached states、caller fan-out、加载分片字节。 |
| `CpgRelationQuery` | profile、direction、source/target selectors、budget、required capabilities 与 query purpose。 |
| `CpgQueryResult` | 节点、边、路径、status、truncation、unavailable shards、使用能力和缓存状态。 |

profile 由 NLCPG 维护，禁止规则手写宽边集：

| Profile | 允许的关系 | 典型消费者 |
| --- | --- | --- |
| `StructuralContainment` | `SyntaxChild`、`TokenChild`、限定的 `SyntaxHasOperation` / `OpHasSyntax` | expression/if/switch 宿主和结构视图。 |
| `SemanticBinding` | `DeclaresSymbol`、`ReferencesSymbol`、`Ref`、`HasType`、`EvalType` | symbol 与 declaration host。 |
| `LocalDataFlow` | `DataFlow` 和同方法 CFG 边 | 局部定义、传播与保守切片。 |
| `ControlDependence` | `CfgNext`、`CfgTrue`、`CfgFalse`、`Dominates`、`PostDominates`、`ControlDependence` | 条件/控制结构规则。 |
| `BackwardSlice` | 明确选择的 data-flow / interprocedural edges | 可选、预算化的跨过程辅助。 |

`StructuralContainment` 的双向访问只为已指定的祖先/直接成员关系服务；它不能借 `CallTargets`、`DataFlow` 或任意 `Operation` 邻居连接两个 fragment。

## Core 接口边界

`NLCPG.Analysis` 拥有 profile、query spec、遍历和分片适配。`NLISSN.Core.Analysis` 只注入 `ICpgRelationQueryService`，将已验证 syntax anchor 或主图 NodeId 传入并接收不可变结果。

`RuleDefinition*` 通过 `RequiredCapabilities` 声明所需 profile。例如一个仅需语法宿主的规则请求 `SyntaxSemantic`，控制依赖规则请求 `ControlDependence`。未构建 capability 时 service 返回 `Unavailable`，规则不把它解释为空路径。

新接口与 `NLCPGSliceQuery` 共用排序、预算和 call-stack 语义；实现可抽取内部 traversal primitive，但 `QueryBackward` 的 public 行为和持久化 shard 查询必须保持不变。

## 结构视图迁移规则

1. 单 fragment 视图只包含该 syntax span 内符合 profile 的节点和边。
2. 多 fragment 视图必须声明连接 profile、方向和最大 hops。默认 `StructuralContainment` 只允许同一 syntax tree 内连接。
3. 若无允许关系连接 fragments，返回 `Disconnected`；不能回退到任意边 shortest path。
4. 有多条等长路径时，按 edge kind、structured label、source NodeId、target NodeId 选择稳定路径；若调用者需要全部路径，必须显式配置 `MaxPaths`。
5. view 的 root 由指定 syntax anchor 和 node role selector 决定；没有唯一 root 返回 `Ambiguous`。

## 执行阶段

### 阶段 1：修复缓存身份并锁定现状

为 `NLCPGSliceQueryTests` 和 `MarkAnalysisSnapshot` 增加预算 cache-key 反例。保留当前 `QueryBackward` 的 paths、截断原因、call-stack cycle 与 DOP/分片等价快照。

### 阶段 2：定义 query spec、profile 和结果状态

在 `src/NLCPG/Analysis/` 添加 immutable query 类型，复用 `NLCPGTraversalBudget`。先实现 `StructuralContainment` 和 `SemanticBinding`，每项 capability 映射由单处表维护并受 ContractTests 保护。

### 阶段 3：提取稳定遍历内核

从 `NLCPGSliceQuery` 抽出排序入边/出边、状态去重、预算检查和结果构造。内存与 shard reader 都经同一结果协议；禁止通过 materialize-all 绕过 shard budget。

### 阶段 4：替换结构视图纵切

为 `NLCPGStructureViewBuilder` 添加 profile-aware 入口。先迁移一个多 fragment rule fixture，断言旧的任意边路径不会再出现，并比较结构 selector、规则输出、decision、rewrite 和 diff。

### 阶段 5：增加控制和流关系消费者

只有现有规则真正声明 `ControlDependence` 或 `InterproceduralDataFlow` 需求后，才增加对应 profile consumer。每个新 consumer 先验证 capability unavailable、truncated、disconnected 和 ambiguous 四种状态。

### 阶段 6：删除旧隐式连接入口

全部 consumers 迁移后删除无 profile 的 `Build(fragments, context)` 或将它限制为 `StructuralContainment` 单 fragment 兼容入口。不能保留自动选择全边种类的回退路径。

## 验收门槛

- 不同全部预算字段的等价查询不会共享缓存；相同完整 spec 可稳定命中缓存。
- 所有 query result 在内存和冻结分片路径上有相同 status、路径排序和截断原因。
- 结构规则无法通过 `CallTargets`、`DataFlow` 或未声明 operation 边连接 fragment。
- DOP 1/16 和反复构建的 query JSON、structure view、rule output 与 rewrite diff 相同。
- capability 缺失、budget 截断、shard 不可用、无连接和多根均以状态返回，删除规则不把它们解释为安全。
- 性能测试分别报告 query 执行、缓存命中、分片加载和路径物化；不以中断或资源耗尽运行作为基准。

## 风险与约束

| 风险 | 处理 |
| --- | --- |
| profile 太细导致 API 碎片化 | 只维护当前五类关系；新增 profile 要有至少一个 rule consumer 和 ContractTest。 |
| profile 太宽重新引入错误连接 | edge 集合固定、强类型枚举、测试拒绝未声明边。 |
| 路径数量增长 | 每条 query 都需明确 budget；默认只返回一条稳定路径或局部 view。 |
| shard 与内存实现分叉 | 共用 query result、budget validation 和 fixture；每个 profile 跑两种后端。 |
| 替换旧 builder 改变规则结果 | 每次只迁移一个 rule vertical slice，比较 mark 到 diff 的完整结果。 |

## 依赖与后续关系

图与规则绑定校验器验证 profile/capability 对应关系与 root binding。Flow Summary 2.0 使用 `BackwardSlice` 的预算协议；Symbol Usage Profile 可从 `SemanticBinding` 查询得到稳定图锚点；Decision Evidence DAG 记录完整 query spec、status、路径和截断原因。
