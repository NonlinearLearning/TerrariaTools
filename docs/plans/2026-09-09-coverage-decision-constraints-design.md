# Coverage 证明与 Decision 约束设计

> 状态：设计已确认，尚未实现。
>
> 日期：2026-09-09。
>
> 目标：限制错误的 Mark coverage 升级行为，并使外层 `if Delete` 与内层逻辑 `Replace` 的关系由显式证明决定，而不是由 span 包含关系猜测。

## 1. 决策摘要

不把 `Delete`、`Replace`、父子优先级或冲突过滤移动到 Mark。Mark 只产出最小目标和带 provenance 的事实；Lift 负责把事实聚合为结构证明；Propose 把证明转换成带编辑足迹的决策候选；Decision 是唯一的父子冲突解析器。

推荐采用两个相互独立的接口：

1. **Typed coverage evidence**：回答“某个节点被什么能力、以什么证据覆盖”。
2. **Decision footprint**：回答“一个动作锚定并消费了哪些原始节点，以及它是否能够支配子编辑”。

这两个接口不能合并。Coverage 证明结构完整性；footprint 证明决策之间是否可以组合或支配。Rewrite span 只用于最后的重叠防御，不能承担这两种语义。

## 2. 当前问题与保留契约

当前代码链如下：

```text
FlowLogicalExpression
    -> MarkCoverage 命中 logical host
    -> IfStructureLifter 生成 DeleteWholeIf
    -> LogicalExpressionLiftingRule 同时生成 logical-host Replace
    -> Decision 按 conflict/span 合并并静默丢弃一个候选
```

关键位置：

- `src/NLISSN.Rules/Propagate/LogicalExpressionPropagationRule.cs:140` 产生逻辑宿主事实。
- `src/NLISSN.Rules/Lift/Support/MarkCoverage.cs:9` 只按节点键判断覆盖。
- `src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs:24` 把 coverage 直接转换为结构 payload。
- `src/NLISSN.Core/Decision/DecisionModel.cs:523` 按 span 包含关系过滤决策。
- `src/NLISSN.Application/Analysis/ApplicationService.cs:245` 又维护一套嵌套删除过滤。

必须保留的现有语义契约：

| 源码条件 | 目标 | 期望结果 | 证明要求 |
| --- | --- | --- | --- |
| `ready && s.IsReady` | `s` | 删除外层 `if` | 显式的 AND 结构完整证明可以支配逻辑 Replace |
| `ready || s.IsReady || fallback` | `s` | 保留 `Replace`，结果包含 `ready` 和 `fallback` | 只有局部逻辑规约证明，不产生结构完整证明 |
| 只有一个 logical sibling 被标记 | 任意 | 不能把整个条件当作完整覆盖 | coverage 必须返回 partial |

这意味着不能采用“所有逻辑宿主都降级为局部 Replace”，也不能采用“所有父 span 都覆盖子决策”。AND/OR 的规约规则必须显式声明是否能够生成结构证明。

## 3. 阶段边界

| 阶段 | 允许产出 | 禁止产出 |
| --- | --- | --- |
| Mark | `AtomicTarget`、`TopologyHost`、`GlobalTarget` 等事实 | `Delete`、`Replace`、父子赢家、结构删除授权 |
| Propagate | 宿主、兄弟、引用、调用和拓扑关系 | `StructureComplete`、最终动作 |
| Lift | `LogicalReductionProof`、`IfStructureCompletionProof` | 直接执行 rewrite、从 span 选择赢家 |
| Propose | `DecisionUnit`、`DecisionFootprint` | 重新推断结构完整性 |
| Decision | 组合、支配、冲突、`Skip` 和诊断 | 重新解释 Mark 的事实语义 |
| Rewrite | 执行已解析的编辑、检查非法重叠 | 反向改变决策语义 |

全局未引用方法、不可达方法等 compilation-wide 规则可以继续在 Mark 运行。它们输出的是 `GlobalTarget` 方法事实，不是删除动作。

## 4. Typed coverage evidence

### 4.1 能力类型

建议新增受控的 coverage 能力维度：

```text
AtomicTarget       原子节点是规则目标
TopologyHost       节点是传播或规约宿主
ChildComposable    节点可以参与已知的子节点组合
GlobalTarget       compilation-wide 证明命中目标声明
```

`StructureComplete` 不作为普通 Mark 能力。它只能由 Lift 在验证完整条件后产生，避免一个 Mark 直接授权结构删除。

能力和现有事实的初始映射：

| 事实 | 能力 | 允许的用途 | 禁止的用途 |
| --- | --- | --- | --- |
| 原始目标表达式 | `AtomicTarget` | 作为逻辑操作数或局部删除候选 | 直接删除父结构 |
| `FlowLogicalExpression` | `TopologyHost` | 查找 logical host | 满足 `StructureComplete` |
| 逻辑传播的 direct sibling | `ChildComposable` | 参与逻辑规约 | 单独授权 `Delete(if)` |
| 未引用/不可达方法目标 | `GlobalTarget` | 继续传播到声明决策 | 推断任意调用方结构可删 |

当前 `TargetExpression` 同时表示原子目标和传播后的完整表达式，单独依赖 `FactKind` 不足。应在 mark/propagation 记录中保留 capability 和来源，或拆分为明确的 fact role。

### 4.2 Coverage API

将无上下文的布尔 API：

```csharp
bool IsCovered(SyntaxNode node, IEnumerable<MarkRecord> marks)
```

收敛为带需求的证明接口：

```csharp
CoverageResult Evaluate(
    SyntaxNode requiredNode,
    IReadOnlyList<CoverageEvidence> evidence,
    CoverageRequirement requirement);
```

至少支持：

```text
LogicalReduction       验证操作数可以参与逻辑规约
ExpressionReplacement  验证表达式锚点可以被替换
StructureComplete      验证整个控制结构的条件已完整证明
```

`CoverageResult` 不应只有 `bool`，至少应包含：

```text
IsComplete
MissingNodeKeys
RejectedEvidence
AcceptedEvidence
Reason
```

`TopologyHost` 命中 required node 时，只能满足宿主查找，不能满足 `StructureComplete`。对于 `StructureComplete`，评估器必须递归检查所需操作数，或者验证一个已经由 Lift 产生的结构证明。

### 4.3 保留传播 payload

现在 `IfStructureLifter` 使用：

```csharp
seedMarks.Concat(propagatedMarks.Select(item => item.Mark))
```

这会丢失 `PropagatedMarkRecord.Payload`。后续 coverage evidence 必须从完整记录构造，至少保留：

- source mark 和 source rule；
- propagated mark 的 FactKind/capability；
- `ExpressionTopologyPayload` 等传播 payload；
- consumed/source node keys；
- depth 和 provenance。

不能用 `Reason` 字符串或 `OutputKind` 单独恢复这些信息。

## 5. Lift 证明模型

### 5.1 逻辑规约证明

`LogicalExpressionLiftingRule` 输出的 payload 应明确表示局部编辑能力：

```text
LogicalReductionProof
    Host
    RemovableOperands
    SurvivorOperands
    ConsumedNodeKeys
    AllowedAction = ExpressionReplace
    DominatesChildren = false
```

该证明可以生成 `Replace(logicalHost)`，但不能仅凭自身删除包含它的 `if`。

### 5.2 控制结构完整证明

`IfStructureLifter` 只有在 `CoverageRequirement.StructureComplete` 成功后才生成：

```text
IfStructureCompletionProof
    AnchorIf
    RequiredConditionNodes
    ConsumedNodeKeys
    TailNode
    StructuralKind
    AllowedAction = StructureDelete | StructureReplace
    DominatesChildren = true
```

其中 `ConsumedNodeKeys` 必须包含实际被结构动作消费的原始节点。仅包含 logical host mark，不足以构成该证明。

AND 与 OR 的规则必须显式区分：

- AND 场景：如果目标规则的语义是移除整个受影响控制分支，Lift 可以在验证短路边界和条件消费范围后生成 `StructureComplete`。
- OR 场景：有 survivor 操作数时，通常只生成 `LogicalReductionProof` 和表达式 `Replace`，不生成控制结构完整证明。
- 自定义 operator、checked/lifted operator、dynamic 或证据不完整时，不生成 `StructureComplete`。

## 6. Decision footprint

### 6.1 数据结构

`DecisionUnit` 应增加或等价携带：

```text
DecisionFootprint
    AnchorNodeKey          原始语法树中的锚点
    ConsumedNodeKeys       原始语法树中被动作消费的节点
    Action                 Delete | Replace | Skip
    Composition            Independent
                           ComposesWithChildren
                           DominatesChildren
                           Exclusive
    ProofKind              LogicalReduction | StructureComplete | Other
```

`ReplacementNode` 是新树结果，不属于原始 coverage footprint。冲突判断不能读取新建 replacement node 的 span。

现有 `ConflictKey` 和 `MergeKey` 可以作为迁移期分区/去重字段，但不能继续独自代表支配语义。

### 6.2 冲突解析规则

`RuleDecisionEngine` 应成为唯一 resolver，输入所有 DecisionUnit 后构建显式冲突图：

| 候选关系 | 结果 | 条件 |
| --- | --- | --- |
| 非重叠 | 两者保留 | footprint 独立 |
| 外层 `Delete` + 内层 `Replace` | 外层胜出 | 外层有 `StructureComplete`，且 consumed nodes 包含内层 anchor，并声明 `DominatesChildren` |
| 外层 `Delete` + 内层 `Replace`，无支配证明 | 冲突域 `Skip` 或拒绝外层 Delete | 不允许按 span 静默丢弃内层候选 |
| 外层 `Replace` + 内层编辑 | 外层胜出或冲突 | 只有 replacement 明确消费内层 anchor 时才可胜出 |
| 祖先 `Delete` + 后代 `Delete` | 祖先胜出 | 两者属于同一结构消费域 |
| 同 anchor 的不同 `Replace` | 冲突 | 没有等价 replacement 或显式 merge 证明 |
| 部分重叠但互不包含 | 整个冲突域 `Skip` | Rewrite 不猜执行顺序 |

外层删除的唯一授权条件应等价于：

```text
ParentDeleteWins =
    Parent.ProofKind == StructureComplete
    && Parent.ConsumedNodeKeys.Contains(Child.AnchorNodeKey)
    && Parent.Composition == DominatesChildren
```

### 6.3 可观察的丢弃结果

当前 `FilterCompetingAncestors`、`FilterCoveredDecisions` 和 `FilterNestedDeleteDecisions` 会直接缩小列表，导致“为什么候选消失”不可观察。迁移后 resolver 应输出：

```text
Resolved decisions
Rejected candidates
Conflict diagnostics
Dominance evidence
```

没有显式证明的过滤必须产生 `DecisionConflict` 或 `DecisionRejected` 证据；不能静默 `Where` 掉候选。

`ApplicationService` 不再拥有第二套语义过滤。它最多调用 resolver 的结果，或在 Rewrite 前验证 footprint 不变量。

## 7. 决策域与迁移策略

当前 `BuildConflictGroupKey` 会根据语法树向 logical host 或 ancestor conflict node 启发式上推。迁移分三步：

1. Propose 为每个 DecisionUnit 填充显式 footprint 和 conflict domain。
2. resolver 优先使用 footprint；没有 footprint 的旧候选进入 compatibility domain，并记录诊断。
3. 移除基于 descendant/ancestor 的启发式分组和 ApplicationService 的重复过滤。

兼容期的 span 检查只能作为断言：如果没有 footprint 却发现重叠，应 fail closed 并记录诊断，而不是选择父节点或替换节点。

## 8. 测试设计

### 8.1 Coverage 合同

- 只有 `FlowLogicalExpression` 时，`StructureComplete` 必须为 false。
- 只有一个 logical sibling 时，coverage 必须为 partial。
- 原子目标和 propagated topology host 使用相同节点键时，能力仍必须不同。
- 传播 payload 在 coverage evaluator 中可见，source/depth/provenance 不丢失。
- `StructureComplete` 只能来自 Lift proof，Mark/Propagate 输出必须被契约验证拒绝。

### 8.2 Decision 合同

- 有完整 `IfStructureCompletionProof` 时，外层 `Delete(if)` 支配内层 logical `Replace`，最终只保留外层决策。
- 只有 topology host、没有完整证明时，外层 Delete 被拒绝或冲突域 Skip，内层 Replace 不被静默删除。
- 外层 `Replace(if)` 只有在 consumed nodes 明确包含内层 anchor 时才能支配内层编辑。
- partial overlap 产生诊断/Skip，而不是依赖生成顺序。
- replacement node 不在原始树时，footprint 仍只使用原始 anchor 和 consumed nodes。
- 不同冲突域且不重叠的决策同时保留。

### 8.3 既有行为回归

必须继续通过：

- `DecisionStructureValidationTests` 的逻辑 AND 结构删除；
- `GraphAnalyzerTests.Analyze_LogicalAndCondition_RightTargetDeletesIf`；
- `DecisionComplexTests.Analyze_NestedLogicalAndCondition_DeletesFullyMarkedIf`；
- `DecisionComplexTests.Analyze_NestedLogicalOrCondition_RewritesToRemainingOperands`；
- `RuleStructureContractTests.MarkCoverage_WhenOnlyOneConditionSiblingIsMarked_ReturnsFalse`；
- 父控制结构覆盖 body 删除的已有 rewrite 测试。

## 9. 分阶段实施

### Phase 1：证据类型和诊断，不改变预期决策

- 增加 capability/coverage result/footprint 数据结构。
- 让传播 payload 进入 coverage evidence。
- 对当前 span 过滤记录被丢弃候选和缺失证明。
- 增加上述 coverage 与 decision 合同测试。

### Phase 2：收紧结构 Lift

- `FlowLogicalExpression` 只能作为 `TopologyHost`。
- `IfStructureLifter` 只接受 `StructureComplete` requirement。
- 为 AND 现有删除契约生成显式 proof。
- 为 OR 局部规约保持 `LogicalReductionProof`。

### Phase 3：接入显式 footprint resolver

- Logical/If proposal 填充 anchor、consumed nodes 和 composition。
- resolver 按 dominance/composition 解析父子关系。
- 无证明的嵌套冲突 fail closed，并保留诊断。

### Phase 4：删除重复过滤

- 删除或旁路 `ApplicationService.FilterNestedDeleteDecisions` 的语义判断。
- 移除 `FilterCoveredDecisions` 对普通 span 的静默过滤。
- Rewrite 只保留非法 overlap 的防御性报告。

每个阶段都必须先通过定向测试，再扩大到完整 Host/Unit 测试；不通过阶段不应把旧过滤逻辑和新 resolver 并行当作两个真相源。

## 10. 外部实现对照

- [Roslyn SyntaxEditor](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs)：通过节点跟踪/annotation 管理编辑目标，不把裸 span 当成节点语义。
- [Clang Replacement](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Core/Replacement.cpp)：重叠 replacement 默认进入冲突处理。
- [Clang AtomicChange](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Refactoring/AtomicChange.cpp)：相关 edits 作为原子变更聚合。
- [OpenRewrite Markers](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/Markers.java)：Marker 是树元数据，不等同于 rewrite action。
- [Scalafix Patch](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/patch/Patch.scala)：patch 组合与冲突由 patch 层处理，而不是由搜索标记决定。

共同启示是：

```text
facts/markers -> typed evidence -> explicit edit footprint -> resolver/editor
```

## 11. 验收标准

- `TopologyHost` 永远不能单独满足 `StructureComplete`。
- `StructureComplete` 只能由 Lift proof 产生，并带有完整 consumed node keys。
- 外层 `Delete` 只有显式 dominance proof 才能覆盖内层 `Replace`。
- 无支配证明的嵌套或部分重叠候选不会被 span 过滤静默丢弃。
- `DecisionUnit` 的 footprint 只引用原始树节点，replacement node 不参与原始覆盖。
- RuleDecisionEngine 是唯一的父子冲突语义 resolver。
- AND 结构删除和 OR 局部 Replace 的现有测试契约保持通过。
- 任何被拒绝、冲突或被支配的候选均可通过 evidence/diagnostic 解释。

## 12. 当前验证记录

本设计依据当前仓库代码和研究报告形成。已验证：

```text
DecisionStructureValidationTests + DecisionComplexTests: 9 passed
AND structure-delete / OR logical-replace focused tests: 2 passed
GitHub reference URLs: HTTP 200
```

本设计阶段未修改生产代码，也未修改现有测试。
