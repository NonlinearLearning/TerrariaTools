# DAG 语义边对照研究

日期：2026-09-08
范围：为 NLISSN 的规则 DAG、Propagation fixed-point 和 CPG relation query 寻找可复查的开源设计对照。本轮不修改运行时、规则合同、CPG 图或测试。

## 结论

没有找到一个可以整体移植的“相同 DAG 框架”，因为本仓库同时有三种不同的图问题：外层的规则调度 DAG、内部的递归事实闭包、以及可查询的多关系 CPG。强行以一个库或一个去重键覆盖三层，正是当前语义边问题容易产生的根源。

四个成熟项目提供了可组合的参照：

| 项目 | 最接近的 NLISSN 问题 | 应迁入的设计原则 | 不应照搬的部分 |
| --- | --- | --- | --- |
| [Google Dagger](https://github.com/google/dagger) | Rule `Consumes` / `Produces` 的端口身份、缺失/重复 producer、环 | 端口键是结构化值；编译图前先验证唯一 producer、缺 producer 和环 | Dagger 的每 key 恰有一个 binding 不适用于 `All` fan-in 或一条 mark 多路消费 |
| [Souffle](https://github.com/souffle-lang/souffle) | `PropagationFixedPointExecutor` 的事实身份、收敛和 provenance | 递归 SCC 是显式 fixed-point region；`new`/`delta` 与退出条件是收敛协议；proof 是独立的解释层 | 不能把 Datalog relation tuple 的集合语义直接当作 NLISSN payload 身份的决定 |
| [Apache TinkerPop / TinkerGraph](https://github.com/apache/tinkerpop) | CPG 的平行语义边、path 和 relation-query 去重 | 边是一等 `Element`，有自身 ID、label、源和目标；path 身份必须包含边序列 | TinkerGraph 的内存图不是本仓库的 DOP/冻结图构建模型 |
| [Microsoft BuildXL](https://github.com/microsoft/BuildXL) | versioned value identity、provenance 与调度图的边界 | 同一 logical path 的不同写入版本必须不同；调度节点、制品身份和人类可读 provenance 分层 | BuildXL 的紧凑依赖边只有邻接/轻重语义，不能表示 CPG 的带 label 平行边 |

对本仓库的直接目标不是引入上述项目，而是将以下四类身份从目前的隐式字符串、列表引用或节点对中分离出来：

```text
PortKey        = semantic tag + canonical syntax-kind domain + direction
RuleNodeId     = stage + stable rule/capability identity
FactKey        = relation/semantic tag + source anchor + 已明确的 payload identity
RelationEdgeId = source node + target node + edge kind + structured label/context + stable ordinal

Evidence/Derivation 不是上述任一 identity 的替身；它是一个 fact 或 decision 的多来源解释集合。
```

这里的 `FactKey` 中是否包含 payload 仍需先定契约。若 payload 能改变下游决定、后续传播或 evidence，则它必须参与值身份或被显式合并；若它只是可合并的解释文本，则应留在 provenance/evidence。不能继续由当前哈希键的遗漏静默决定这一点。

## 本仓库的对照边界

当前设计已正确地区分了外层 DAG 和 Propagate 内部的递归闭包：[`规则DAG.md`](../../设计docs/目前设计/规则DAG.md) 规定外层只由 `Consumes` / `Produces` 端口形成无环图，而 [`deletion-pipeline.md`](../../设计docs/目前设计/deletion-pipeline.md) 规定表达式或关系传播在 `PropagationFixedPointExecutor` 内收敛。这条边界应保留。

本轮此前已验证或复现的风险集中在 identity 不完整，而不是 scheduler 缺少拓扑排序：

| 层 | 当前现象 | 风险 |
| --- | --- | --- |
| 规则端口 | `RuleConsumedSyntax` / `RuleProducedSyntax` 是 record，但 `SyntaxKinds` 为 `IReadOnlyList<SyntaxKind>`；record 默认比较的是列表实例 | 内容相同、数组不同的端口不相等，影响 duplicate 检查和 producer 查询 |
| 规则输出路由 | 同一 tag 下宽、窄 `SyntaxKind` output 重叠时，`FirstOrDefault` 只选择第一个 output | DAG 已连边，而窄 consumer 可能永远收不到实际 mark |
| mark 汇总和 evidence | 同一 rule/span 的不同 semantic tag 在汇总 key/evidence node key 中没有完整分离 | 不同语义路线会被压缩为一条结果或一份证据 |
| fixed point | `PropagationFactKey` 固定为 rule、file、span、kind、tag，未包含 payload/provenance | payload 是否同一事实由实现偶然决定，无法审计 |
| CPG relation query | `visitedStates` 只含 node/hop/call-depth/call-stack，path 去重只含 node 序列 | 同端点平行语义边以及不同 source 收敛的 source path 会被丢弃 |

因此，后文按“端口键、事实键、边键、证据”的四个概念取证，而不是把外部项目统称为工作流 DAG。

## 对照一：Dagger 的 binding key 与图结构验证

Dagger 的官方语义文档将 key 定义为“Java type + 可选 qualifier instantiation”，并明确规定：两个 key 的 type（unboxing 后）和 qualifier 相同才相同。binding 是计算某个 binding key 的函数；其 binding graph 在 code generation 前必须 well-formed：每个 key 恰有一个 binding，零个是 missing binding，多于一个是 duplicate binding，并且禁止普通环（`Provider` / `Lazy` 的特殊边例外）。[官方语义说明：Keys 与 Binding Graph](https://dagger.dev/semantics/)

这对 NLISSN 的价值不在于复制“恰有一个 producer”。当前 `RuleInputCardinality.All` 有意允许多个匹配 producer，且一个 mark 可以向多个 consumer 扇出。可迁入的是 Dagger 的顺序：

```text
先定义可结构化相等的 PortKey
  -> 再根据 cardinality 验证 binding 集合
  -> 最后生成可执行边
```

而不是先用可变/引用型列表制造边、再由运行时猜某个 mark 属于哪条 output。

### 对当前 port contract 的具体推导

1. `SyntaxKinds` 应在构造时 canonicalize：去重、稳定排序、不可变存储；`RuleConsumedSyntax` 与 `RuleProducedSyntax` 的相等和 hash 必须按集合内容及 `SemanticTag` 计算，不能保留 `IReadOnlyList` 的引用相等。
2. `RuleGraphCompiler` 必须在建边前检验同一 producer 的 output domain 是否歧义。对同一 semantic tag，两个 output 的 syntax-kind 集合有交集时，应该拒绝，除非合同明确声明一个 mark 同时发往多个 named output。
3. `ExactlyOne`、`Optional`、`All` 应只在 canonical `PortKey` 匹配完成后解释。`ExactlyOne` 的诊断应列出完整 producer port，而非只列 `RuleId`。
4. 没有 tag 或匹配多个 output 的 mark 必须立即失败关闭；不能保留在 `Values` 中、同时让 `SyntaxOutputs` 看不到它。

这会修复“图存在但没有可路由值”的类别，但不会取代本仓库原有的“producer syntax-kind 集合必须被 consumer 完整接受”的兼容规则；后者是更严格的领域合同，不能降级为集合有交集。

## 对照二：Souffle 的 SCC、delta 与 provenance

Souffle 的官方源码把递归 relation 的执行编译成显式的 SCC fixed-point：

- `generateRecursiveStratum` 先生成 preamble，再建立一个包含 loop body、exit sequence 和 table updates 的 `ram::Loop`；
- 递归 relation 物化为 main、`@new` 和 `@delta` 变体，且这些变体保留相同的 relation signature；
- `generateStratumExitSequence` 在 SCC 全部 `@new`（或 subsumptive relation 的 `@delta`）为空时退出；
- `generateStratumTableUpdates` 先将 `@new` merge 到 main，再交换/清空 `@delta` 与 `@new`。

来源：[半朴素 fixed-point 编译器](https://github.com/souffle-lang/souffle/blob/a1303be3c0166400dee3d1f36f0d96abe03e6901/src/ast2ram/seminaive/UnitTranslator.cpp)。Souffle 还在独立的 `ast2ram/provenance` 组件上叠加 info clauses、subproof subroutines 和 explain interface，而不是让 provenance 字符串决定递归 relation 是否已经收敛。[provenance translator](https://github.com/souffle-lang/souffle/blob/a1303be3c0166400dee3d1f36f0d96abe03e6901/src/ast2ram/provenance/UnitTranslator.cpp)，[explain interface](https://github.com/souffle-lang/souffle/blob/a1303be3c0166400dee3d1f36f0d96abe03e6901/src/include/souffle/provenance/ExplainProvenance.h)。

### 对 `PropagationFactKey` 的具体推导

Souffle 的模式支持本仓库保留 fixed-point region，却要求把它的收敛协议写清楚：

```text
seed facts
  -> candidate facts (@new equivalent)
  -> canonical unseen facts (@delta equivalent)
  -> committed fact relation
  -> until no canonical unseen fact remains
```

NLISSN 不需要模拟 `@new`/`@delta` 表，但应把下列问题变成可测试合同：

- `FactKey` 的字段是否完全覆盖会改变后继传播或 decision 的 payload；
- 两份同 key、不同 provenance 的 fact 是合并 provenance，还是保留多条 derivation；
- 在 DOP 变化时，winner 选择、derivation 排序和 evidence 输出是否稳定；
- `RuleId` 是 rule identity、relation identity，还是两者之一的来源字段。它不应被偶然用作替代 payload identity。

建议的最小模型是 `FactKey` 与 `FactDerivation` 分离：前者决定 fixed-point 是否出现新事实，后者按 `FactKey` 聚合全部可解释来源。只有在产品确认 payload 为解释性而非语义性时，才允许它不进 `FactKey`；这个选择必须有名称、注释和测试，不能只由 `PropagationFactKey.Create` 的字段列表暗示。

## 对照三：TinkerPop 的边作为一等对象

TinkerPop 的结构 API 将 `Edge` 定义为 `Element`；所有 `Element` 有唯一 ID，`Edge` 还显式给出 `outVertex`、`inVertex` 和不可变 label。[`Element.id()`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/gremlin-core/src/main/java/org/apache/tinkerpop/gremlin/structure/Element.java)，[`Edge`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/gremlin-core/src/main/java/org/apache/tinkerpop/gremlin/structure/Edge.java)。

其 reference implementation `TinkerMemoryGraph.addEdge` 会为每次无显式 ID 的插入申请新的 edge ID，并把 edge 放进独立 edge map 和两个顶点的邻接集合；代码没有按 `(outVertex, inVertex, label)` 做 upsert。也就是说，端点、label 相同不自动等于同一条 relation edge。[`TinkerMemoryGraph.addEdge`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/tinkergraph-gremlin/src/main/java/org/apache/tinkerpop/gremlin/tinkergraph/structure/TinkerMemoryGraph.java)。

### 对 `CpgRelationQueryService` 的具体推导

CPG 已能构建同端点但不同 `NLCPGEdgeKind` 的 relation；因此 query 的“走到同一 node”不能等价于“已经观察到所有 relation”。建议明确两层去重：

| 输出/工作项 | 正确的去重单位 |
| --- | --- |
| `selectedEdges` | 完整 `RelationEdgeId`，至少含 source、target、kind、structured label/context 和稳定 ordinal；不能是 node pair |
| continuation state | `(SourceNodeId, NodeId, Hops, RemainingCallDepth, CallStack)`，但必须在压缩 continuation 前记录每个已观察 relation edge |
| `CpgRelationPath` | source node + node sequence + **edge ID sequence**；同一 node sequence 经由不同 semantic edge 是不同 path |
| result cache key | query 语义和 frozen graph snapshot；不可把 request-specific path projection 误缓存成 node-only 结果 |

当前代码先以没有 edge/source 的 visited state 拦截，再把 edge 放进 `selectedEdges`；这解释了已复现的平行边丢失。只把 edge identity 加进 `visitedStates` 会保留更多路径，但也可能把无界图搜索放大；更稳妥的是把“edge materialization”与“continuation expansion”分开，并让 path budget 明确约束 edge-distinct path 数。

同理，多 source selector 汇聚时，`SourceNodeId` 必须属于 continuation/path identity。否则第二个 source 到达相同 node 后会被错误剪枝，即使用户问的是所有 source 的关系路径。

## 对照四：BuildXL 的 versioned artifact 与 provenance 分层

BuildXL 的 `FileArtifact` 明确把 `(Path, RewriteCount)` 作为相等性：相同 path 的 source、第一次 output 和后续 rewrite 是不同制品；`CreateNextWrittenVersion` 递增 rewrite count，并说明任意制造版本会破坏 schedule 的确定性。[`FileArtifact`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Utilities/Utilities.Core/FileArtifact.cs)。

另一个独立的 `PipProvenance` 承载 module、output symbol、source token、qualifier、usage 和 semi-stable hash，并注释其服务于 tracing 和 error logging。[`PipProvenance`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Pips/Dll/Operations/PipProvenance.cs)。这说明版本化 identity 与解释/显示 provenance 可以共存，但不该是同一个无结构字符串。

BuildXL 的底层 `DirectedGraph.Edge` 反而是很有价值的反例：它只打包“另一端 NodeId + light-edge bit”。[`Edge`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Pips/Dll/DirectedGraph/Edge.cs)。这种紧凑边适合其无 label 的调度可达性，不适合本仓库要返回 `DeclaresSymbol`、`RefersToType`、structured label 与 call-site context 的 CPG relation query。

对 NLISSN 的迁入点是：如果 semantic tag、payload、version/revision 或 route 会改变下游语义，就应像 `RewriteCount` 一样在 identity 合同中显式出现；而 Rule/mark/evidence 的显示文本、来源说明和诊断不应被用来偷偷定义相等性。

## 建议的本仓库修复顺序

这不是“先把所有 key 加字段”的建议。每一步都先冻结一层 identity，然后用针对性合同测试证明现有负例已被覆盖，才扩大到下一层。

```text
1. 端口身份与图编译
   -> 2. mark/output 路由
      -> 3. 聚合与 evidence identity
         -> 4. CPG relation edge/path identity
            -> 5. propagation fact/payload policy
```

### 1. 冻结 `PortKey` 并让 compiler 只操作它

新增内部不可变、结构化的 canonical port representation，或为当前两个 record 手写内容相等。它至少需要覆盖 `SemanticTag`、canonical `SyntaxKind` 集合和方向；producer node/output ordinal 应作为定位/诊断信息而不是以引用地址暗含在相等性中。

编译器应新增两个失败关闭检查：

- 相同逻辑 port 的重复声明：不允许因不同数组实例绕过；
- 同一 producer、同一 tag 的 output domain 重叠：若没有显式 multi-output 语义，报告歧义而不是沿 `FirstOrDefault` 选择。

这一阶段不要改变 `RuleSyntaxContractMatcher.IsCompatible` 的“producer 全部 kinds 被 consumer 接受”规则，也不要碰 Propagate-to-Propagate 的 fixed-point 边界。

### 2. 让每个合法 mark 的 output route 唯一且可观察

将 `RuleSyntaxContractValidator.RequireProducedMark` 或等价的输出绑定放在 `RuleNodeResult` 建立前。没有 tag、没有声明 output 或多 output 匹配的 mark 都是 contract violation；之后的 `SyntaxOutputs` 不得再依靠一次新的模糊匹配。

结果对象内部应保存一次已验证的 `OutputPortId` / `PortKey`，令 `GetOutputs` 按 route key 获取。这样 runtime value routing 与 graph compiler 使用的是同一 identity，而不是两个独立的 `Contains(SyntaxKind)` 查找。

### 3. 明确 aggregate 与 evidence 的 key

为 rule-result 汇总和 `AnalysisEvidence` 分别定义“事实是否相同”的字段：至少不得漏 `SemanticTag`；若同 span、同 tag、不同 producer route 仍有不同推导价值，也要显式选择保留多条 derivation 或聚合一组 source。

不要将 `EvidenceId` 当作修补聚合的唯一办法。evidence 是对已选 fact/decision 的证明图；若先在 mark 汇总处丢掉语义 route，后来再增加 evidence node 字段也无法恢复丢失的事实。

### 4. 分离 CPG 的 edge materialization 与 continuation pruning

先为 frozen CPG 声明稳定 `RelationEdgeId`，并确认 `NLCPGEdge` 的 record equality 是否已完整包含 kind、structured label、context 及端点。然后把 relation query 拆成：

1. 遍历一个 adjacency edge 时，先按完整 edge identity 物化 `selectedEdges`；
2. 再决定是否为后继 node 创建新的 continuation state；该 state 必须携带原 source；
3. 命中 target 时，path key 必须包含边序列，且 `MaxPaths` 明确约束 edge-distinct path。

这是一个行为变更：若调用方历史上只要任意一条 node path，应将该需求写为查询 profile/budget，而非由错误的 node-only `DistinctBy` 偶然实现。

### 5. 最后决定 propagation payload 的领域语义

先以矩阵列出全部 `PropagatedMarkRecord` payload 类型，并由每个 consumer 回答：改变 payload 是否可能改变下游 relation、lift、proposal、decision 或安全证明？

- 若会改变：payload 的规范化 identity（或足够的 discriminator）进入 `FactKey`；
- 若不会改变但可解释性重要：canonical fact 不增加，向 `FactDerivation` 收集排序稳定的 provenance；
- 若当前尚不能判断：fail closed 或保留为并行 fact，不能先默默压缩再声称它们相等。

只有这一步确认后，再更改 `PropagationFactKey`；否则“加 payload”可能破坏既有收敛界，而“不加”可能继续掩盖语义事实。

## 最小验收矩阵

以下测试比全量 happy-path 更能证明这些修复确实覆盖了本轮语义边问题。每个 case 都应比较 DOP 1、2、16 的 graph、facts、evidence、decision、artifact bytes 和稳定顺序。

| 编号 | 冻结的输入 | 必须观察到的结果 |
| --- | --- | --- |
| P1 | 两个内容相同、但由不同数组创建的 `SyntaxKinds` port | 相等、hash 一致；duplicate 或 producer lookup 的结论与数组分配无关 |
| P2 | 同 producer、同 tag、宽/窄且重叠 output domain | 图编译在分析前失败；不能出现“宽 consumer 有值、窄 consumer 空”的完成结果 |
| P3 | 无 semantic tag，或一个 mark 同时匹配多个 output | 规则节点失败关闭并给出 producer/output 诊断；不能向下游发布未路由值 |
| P4 | 同 rule、同 source anchor、不同 semantic tag 的 marks | 汇总和 evidence 分别保留两个可查询 semantic route |
| P5 | 同一可达 anchor/tag、不同 payload 的 propagated candidates | 测试先锁定产品契约：要么得到两个 canonical facts，要么一个 fact 带两个稳定 derivation；不能无断言地只保留先到者 |
| P6 | `source -> target` 有 `DeclaresSymbol` 和 `RefersToType` 两条平行 relation edge | relation result 含两条 edge；edge-distinct path profile 含两条 path 或明确因 `MaxPaths=1` 截断，而非误报 complete 的一条 |
| P7 | 两个 source selector 汇聚到同一中间/target node | 每个 source 都有自己的 path；path 的 source identity 不因 node-state 去重而消失 |
| P8 | 同一 node sequence、不同 structured label 或 call-site context | 如果 profile 请求 relation-level path，两条均可区分；如果只请求 node reachability，则该降维必须显式写在 profile 契约中 |

## 项目选择和资料边界

所有外部结论都来自项目维护者拥有的官方语义文档或对应仓库在 2026-09-08 可读取的固定 commit，而不是二手博客。资料版本如下：

| 项目 | 固定版本 | 主要一手资料 |
| --- | --- | --- |
| Dagger | [官方语义页面](https://dagger.dev/semantics/)（访问 2026-09-08）；实现快照 `4fbc045d2ba8d65e28b23bef84a42068702a4a9e`（2026-08-28） | [Dagger Core Semantics](https://dagger.dev/semantics/)，[`Key`](https://github.com/google/dagger/blob/4fbc045d2ba8d65e28b23bef84a42068702a4a9e/dagger-spi/main/java/dagger/spi/model/Key.java)，[binding-graph validators](https://github.com/google/dagger/tree/4fbc045d2ba8d65e28b23bef84a42068702a4a9e/dagger-compiler/main/java/dagger/internal/codegen/bindinggraphvalidation) |
| Souffle | `a1303be3c0166400dee3d1f36f0d96abe03e6901`（2026-07-13） | [semi-naive translator](https://github.com/souffle-lang/souffle/blob/a1303be3c0166400dee3d1f36f0d96abe03e6901/src/ast2ram/seminaive/UnitTranslator.cpp)、[provenance translator](https://github.com/souffle-lang/souffle/blob/a1303be3c0166400dee3d1f36f0d96abe03e6901/src/ast2ram/provenance/UnitTranslator.cpp) |
| TinkerPop | `b9c8b1edf50f9b728d8299f7d530cf360b6bde01`（2026-09-02） | [`Element`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/gremlin-core/src/main/java/org/apache/tinkerpop/gremlin/structure/Element.java)、[`Edge`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/gremlin-core/src/main/java/org/apache/tinkerpop/gremlin/structure/Edge.java)、[`TinkerMemoryGraph`](https://github.com/apache/tinkerpop/blob/b9c8b1edf50f9b728d8299f7d530cf360b6bde01/tinkergraph-gremlin/src/main/java/org/apache/tinkerpop/gremlin/tinkergraph/structure/TinkerMemoryGraph.java) |
| BuildXL | `6a2f5fef3a67bb945c02a6cb9cf3a539432960b4`（2026-09-05） | [`FileArtifact`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Utilities/Utilities.Core/FileArtifact.cs)、[`PipProvenance`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Pips/Dll/Operations/PipProvenance.cs)、[`DirectedGraph.Edge`](https://github.com/microsoft/BuildXL/blob/6a2f5fef3a67bb945c02a6cb9cf3a539432960b4/Public/Src/Pips/Dll/DirectedGraph/Edge.cs) |

局限也应保留：Dagger 没有本仓库的多 producer mark routing；Souffle 的 relation/set 语义不能替本仓库决定任意 C# payload 是否同一事实；TinkerPop 的 reference storage 不证明本仓库的并行构建策略；BuildXL 的调度边不是多关系 CPG 边。它们支持的是 identity 与验证边界的设计结论，而不是 API 级替换建议。

## 当前建议

若只选择一个下一步，应先执行 **P1–P3 所需的 `PortKey`/输出歧义合同设计**。这是最靠近规则 DAG 本体、能在分析前失败关闭的修复，而且它不要求先对 payload identity 作尚未确认的领域决定。

然后按上文顺序分别修复 evidence 聚合和 CPG edge/path identity；`PropagationFactKey` 放在最后，不作为“为凑齐字段”的提前改动。
