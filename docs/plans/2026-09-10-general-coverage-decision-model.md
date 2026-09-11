# Coverage 与 Decision 通用约束模型（设计文档）

> 文档类型：规范性设计文档。
>
> 状态：提议设计，尚未实现。
>
> 日期：2026-09-10。
>
> 范围：统一约束表达式、控制流、声明同步、多文件编辑和未知证据。
>
> 执行入口：[Coverage 与 Decision 通用约束模型执行文档](2026-09-10-general-coverage-decision-execution.md)。

本文定义领域边界、证明模型、父子决策约束和验收不变量；它不授权直接修改生产代码。实施时必须按执行文档的阶段顺序推进，每个阶段先写失败测试、再实现最小变更、最后运行该阶段验证。执行文档未覆盖的扩展不得把动作授权下沉到 Mark。

## 1. 结论

当前问题不应通过把更多决策规则移动到 Mark 来解决。建议采用如下单向链路：

```text
Fact -> Proof -> EditIntent -> DecisionPlan -> Rewrite
```

各阶段的责任是：

| 阶段 | 可以产生 | 不可以产生 |
| --- | --- | --- |
| Mark | 原子目标、拓扑宿主、全局目标、引用/调用事实 | `Delete`、`Replace`、父子赢家、结构完整授权 |
| Propagate | 结构关系、兄弟关系、调用关系、保留完整 provenance/payload 的派生事实 | 最终动作、支配关系、原子组完成结论 |
| Lift | 绑定具体目标的 coverage proof、逻辑规约 proof、结构完整 proof、同步 proof | 直接改写、跨候选选择赢家 |
| Propose | 带 footprint、composition、atomic group 的 `EditIntent` | 重新解释事实或解决其他候选的冲突 |
| DecisionPlanner | 组合、支配、冲突、原子事务、`Unknown` 策略和可观察拒绝结果 | 重新猜测 Mark 事实含义 |
| Rewrite | 执行已解析计划，检查非法重叠和源版本 | 用 span 决定业务语义或补发决策 |

`Mark` 可以增加 typed capability，但 capability 是事实的能力声明，不是动作授权。特别是 `TopologyHost`、`AtomicTarget` 或 `ChildComposable` 都不能单独升级为 `StructureComplete`。

## 2. 当前代码暴露的通用缺口

### 2.1 布尔 coverage 丢失证明目标

[MarkCoverage.cs](../../src/NLISSN.Rules/Lift/Support/MarkCoverage.cs:9) 目前按节点 key 命中即返回 `true`，否则递归要求所有子节点命中。它没有检查 `FactKind`、`OutputKind`、来源阶段、payload、provenance 或事实用途。

因此，下列事实在现有 API 中等价，但语义并不等价：

```text
“这个节点是供逻辑传播使用的宿主”
“这个节点的所有可删除语义已经完整证明”
```

[IfStructureLifter.cs](../../src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs:73) 还会把传播记录投影成 `item.Mark`，丢失 `PropagatedMarkRecord.Payload`、源标记和深度。这使后续 Lift 无法判断一个命中是拓扑事实还是完整结构证据。

### 2.2 当前过滤器混合了四种关系

当前 `RuleDecisionEngine`、`ApplicationService` 和 `PrototypeRewriter` 分别实现了不同形式的过滤：

- `FilterCompetingAncestors`：按候选锚点和动作方向过滤；
- `MergeBySyntaxCoverage`、`FilterCoveredDecisions`：按语法 span 包含过滤；
- `ApplicationService.FilterNestedDeleteDecisions`：再次按 span 过滤；
- `PrototypeRewriter.BuildEffectiveRewritePlan`：按文本编辑重叠处理。

这些关系必须分开：

```text
syntax containment       语法树上是否为祖先/后代
semantic coverage        目标需要的事实是否完整可证明
decision dominance       一个动作是否有权消费另一个候选
rewrite overlap          最终文本编辑是否重叠
```

任何一个关系都不能替代另外三个关系。

### 2.3 最小反例

下面的反例是设计必须覆盖的最小集合。

#### 反例 A：外层 Delete 与内层 logical Replace

```csharp
if (ready || s.IsReady || fallback)
{
    Use();
}
```

目标是 `s`。内层 logical expression 可以生成保留 `ready` 和 `fallback` 的 `Replace`。如果 `FlowLogicalExpression` 只证明“这里是逻辑宿主”，却被当成整个条件已覆盖，外层 `if Delete` 会通过 span 包含静默吞掉内层 Replace。正确结果是：没有 `StructureComplete` 时，外层 Delete 被拒绝或冲突，内层 Replace 不能消失。

#### 反例 B：同类代码但结构证明成立

```csharp
if (ready && s.IsReady)
{
    Use();
}
```

若规则明确证明整个条件和控制流消费范围都成立，外层 Delete 可以支配内层 Replace。这不是因为外层 span 更大，而是因为存在 `StructureComplete` proof、完整 consumed set 和 `DominatesChildren` composition。

#### 反例 C：外层 Replace 保留了子节点

```csharp
if (condition)
{
    A();
}
else
{
    B();
}
```

父候选把 `if` Replace 为原来的 `else` body，子候选同时删除 `B()`。父 replacement 若只是复制旧的 else body，而没有把子编辑重新映射到 replacement tree，直接按旧 span 丢弃子候选会产生错误结果。父 Replace 必须提供 residual mapping，或与子候选冲突并 fail closed。

#### 反例 D：声明与调用点同步不完整

方法参数收缩可能同时修改方法声明、普通调用、named/optional/params 调用、method group、lambda 和 delegate binding。只要一个跨文件调用点 unresolved、dynamic 或查询被截断，单独修改声明就可能破坏编译。它们必须是一个带完整性 proof 的 atomic group。

#### 反例 E：两个不同事实落在同一节点

同一 syntax node 可能同时拥有 `AtomicTarget`、`TopologyHost` 或不同 payload 的 propagated facts。若仍按 rule/span/length 去重，会静默丢掉其中一个证明输入。去重 key 必须包含 fact kind、payload identity 和 provenance identity。

#### 反例 F：部分重叠而非包含

两个 Replace 的 spans 部分重叠但谁都没有完整包含谁。它们不能依赖生成顺序或 rewrite 排序；应成为显式 conflict domain，并返回 `Conflict` 或整个域 `Skip`。

#### 反例 G：副作用不是“被标记”就能删除

```csharp
if (Check() && await LoadAsync())
{
    Use();
}
```

删除或改写条件需要声明 getter/call、`await`、异常、赋值、`ref/out` 和 short-circuit 的保留义务。节点 coverage 完整不等于 effect-preserving removal 完整。

#### 反例 H：未知不等于空集合

“没有找到调用点”可能表示确实没有调用点，也可能表示存在 dynamic/unresolved 调用、跨项目索引不可用、查询被截断或预算耗尽。后者必须进入 proof 状态，不能转换成 `Complete`。

### 2.4 默认入口与通用入口的实际边界

本轮加入的回归场景是：

    if (s.IsReady && ready || fallback)
    {
        return 1;
    }

在当前默认 RuleGraph 中，这个场景不会产生外层 if Delete。原因是 IfStructureLiftingRule.Consumes 不接收 FlowLogicalExpression，而 graph contract 会过滤不符合声明的传播事实；逻辑宿主因此不能直接升级成结构完整证据。对应回归测试位于 tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs。

这个结果只说明默认入口当前有一道有效的契约阻止了该具体反例，不说明通用接口已经安全。DefaultRemovalProposalRule 仍可从 raw、propagated 或 lifted mark 创建 Delete，而 RuleDecisionEngine、ApplicationService 和 PrototypeRewriter 仍存在按 ancestor/span 过滤候选的路径。自定义规则、契约旁路、未来新增传播能力或直接调用这些通用 API 时，仍可能把 topology host 当成结构删除依据，或用外层 span 静默吞掉内层 Replace。

因此本问题的当前审查结论应写成：

1. 通过默认 RuleGraph 尚未复现“外层 if Delete 错误吞掉内层 Replace”；
2. raw coverage、proposal 和多处 span filter 的接口仍允许这种越权，属于真实的设计风险；
3. TopologyHost 与 StructureComplete 的类型化 proof、显式 EraseSet/PreserveSet 和唯一 DecisionPlanner 仍是必要的长期约束，而不是对当前回归测试的重复实现。

## 3. 四层领域模型

以下是概念接口，用于约束模块的职责；不是要求一次性照搬的公共 C# 类型。

### 3.1 Fact：可共享的观察结果

Fact 是单调累积的观察结果，不是线性资源。Fact 可以被多个 Lift 或 Proposal 读取，不能因为一个候选“消费”了它就从其他候选视图中删除。

```text
Fact
  Id: StableFactId
  Kind: AtomicTarget | TopologyHost | Relation | GlobalTarget | ...
  Capability: AtomicTarget | TopologyHost | ChildComposable | GlobalTarget
  Anchor: SourceIdentity(file, treeVersion, nodeKey)
  Payload: typed payload, never only a display string
  Provenance: source rule, parent facts, propagation path, depth
  Certainty: Available | Truncated | Unavailable
```

建议将 `FlowLogicalExpression` 定义为 `TopologyHost`；将原子目标定义为 `AtomicTarget`；将 compilation-wide 的未引用/不可达方法定义为 `GlobalTarget`。`StructureComplete` 不作为普通 Mark capability。

事实去重至少使用：

```text
(source tree version, anchor, fact kind, payload identity, provenance identity)
```

不能继续只使用 rule/span/length，也不能用 `Reason` 字符串恢复 payload。

### 3.2 Proof：针对目标的可检查证明

Proof 必须绑定一个明确的目标和证据需求。建议状态至少为：

```text
Complete  所有必要证据存在且约束满足
Partial   只能支持局部或较弱操作
Unknown   由于缺失/截断/不支持而无法判断
Rejected  已知违反约束
```

```text
CoverageProof
  Goal: AtomicTarget
        LogicalReduction
        ExpressionReplacement
        StructureComplete
        DeclarationSync
        CallsiteCompleteness
        EffectPreservingRemoval
  Status: Complete | Partial | Unknown | Rejected
  AcceptedEvidence: fact/proof ids
  RejectedEvidence: fact/proof ids + reason
  MissingEvidence: typed requirements
  ConsumedNodes: source node ids
  PreservedObligations: effects, bindings, control flow, types
  Dependencies: proof ids
```

必须保持以下不变量：

- `TopologyHost` 只能满足宿主查找和拓扑关系需求；
- `AtomicTarget` 只能说明原子目标，不说明父结构可删；
- `TargetExpression` 与 `StructureComplete` 不是同一类证明；
- `Unknown` 永远不能作为 `Complete` 使用；
- `StructureComplete` 只能由 Lift 根据完整输入生成，不能由 Mark 直接发布。

### 3.3 EditIntent：带编辑足迹的候选动作

Proposal 将 Proof 翻译成 EditIntent，但此时仍不选择其他候选的赢家。

```text
EditIntent
  Id: StableIntentId
  Anchor: 原始语法树中的锚点
  Operation: Delete | Replace
  ReadSet: 生成 replacement 所读取的原始节点和语义事实
  ConsumedNodeSet: 动作会覆盖/消费的原始节点
  PreservedObligations: 必须保留的行为或绑定
  ProducedObligations: replacement 引入的绑定/类型/结构义务
  Composition: Independent | Transparent | Composable
                | ListComposable | OpaqueDominates | Exclusive
                | AtomicTransaction | Unknown
  ResidualMapping: parent old node -> retained/new/removed/unknown
  AtomicGroup: 可选的事务组 id
  ProofReferences: 授权该 intent 的 proof ids
  Provenance: rule、来源事实和解释
```

区分三个集合很重要：

- `Anchor` 是动作的起点，不代表动作覆盖整个祖先/后代范围；
- `ConsumedNodeSet` 是有证明的输入消费范围；
- `ReplacementNode` 是新树结果，不是原始 coverage footprint。

### 3.4 DecisionPlan：唯一的组合与裁决结果

```text
DecisionPlan
  SelectedEdits
  ComposedEdits
  RejectedCandidates: candidate + reason + proof references
  Conflicts: candidate ids + relation + diagnostic
  DominanceRelations: parent -> child + explicit proof
  AtomicGroups: group status and all members
  Unknowns: unresolved constraints
```

任何被支配、拒绝、冲突或跳过的候选都必须可观察。`Where` 直接丢弃候选不再是合法的 resolver 行为。

## 4. Coverage 需求矩阵

Coverage evaluator 不再回答无上下文的 `IsCovered(node) -> bool`，而是回答 `Evaluate(goal, evidence) -> Proof`。

| 领域 | 最小目标 | `Complete` 需要的证据 | 不能替代的证据 |
| --- | --- | --- | --- |
| 原子表达式 | `AtomicTarget` | 目标节点和规则能力 | logical host、任意 descendant |
| 逻辑表达式 | `LogicalReduction` | host、removable operands、survivors、operator/short-circuit 规则 | host 自身命中 |
| 表达式替换 | `ExpressionReplacement` | replacement 类型、语法上下文、保留义务 | 仅有目标标记 |
| 控制结构 | `StructureComplete` | 条件结构、分支尾部、消费范围、控制流和副作用约束 | `TopologyHost` 或一个父 mark |
| 循环/`switch` | `StructureComplete` | loop condition/body、section/default、break/continue/flow 约束 | span 包含 |
| 声明同步 | `DeclarationSync` | 声明、全部可见使用关系和兼容 replacement | 已发现的部分调用点 |
| 调用点完整性 | `CallsiteCompleteness` | 查询范围、绑定结果、dynamic/unresolved 状态都已封闭 | “当前列表为空” |
| 多文件事务 | `AtomicTransaction` | 所有 member 的 proof 和稳定 source identity | 单文件成功 |
| 副作用移除 | `EffectPreservingRemoval` | getter/call/await/assign/exception/short-circuit 等义务决策完毕 | 节点 coverage |

逻辑 AND 与 OR 的差异必须通过 proof 规则表达：

- `ready && s.IsReady` 可以在确认规则语义、短路边界和结构消费范围后形成 `StructureComplete`；
- `ready || s.IsReady || fallback` 通常只有 `LogicalReduction`，因为 `ready` 和 `fallback` 是 survivor；
- 自定义 operator、dynamic、error type 或证据不完整时不生成 `StructureComplete`。

## 5. 控制父子候选的正式规则

### 5.1 外层 Delete 支配内层 Replace

父删除只能在以下条件同时满足时支配子候选：

```text
ParentDeleteWins(child) =
    parent.Proof(goal = StructureComplete, status = Complete)
    && child.Anchor in parent.ConsumedNodeSet
    && parent.Composition == OpaqueDominates
    && parent.PreservedObligations does not require child edit
```

等价地，父删除必须证明“子编辑所在的原始节点会被整体移除，而且子编辑不会产生尚未保留的义务”。

如果条件不满足：

1. 父 Delete 不能自动获胜；
2. 子 Replace 不能静默消失；
3. Planner 返回 `Conflict`、拒绝父候选，或在该冲突域上 `Skip`；
4. rejection 必须记录缺少的 proof 或 footprint 关系。

这正是“决策过滤会让包含 Replace 锚点的外层 if 删除候选失效”问题的约束答案：不要用 span containment 作为父 Delete 的授权条件。

### 5.2 外层 Replace 必须有 residual mapping

若父 Replace 保留或变换了子节点，父 intent 必须提供：

```text
ResidualMapping(oldNode) =
  Retained(newPath)
  | Replaced(newPath, obligations)
  | Removed
  | Unknown(reason)
```

父候选与子候选的组合规则：

| 父状态 | 子节点映射 | 结果 |
| --- | --- | --- |
| `Transparent`/`Composable` | `Retained` 或 `Replaced`，且类型/语义义务满足 | 把子 intent 重映射到 replacement tree 后组合 |
| `OpaqueDominates` | 子节点在 `ConsumedNodeSet` 内且不需保留 | 父候选支配子候选 |
| 任意 | `Removed` 但子 proof 要求保留 | 冲突，不能静默合并 |
| 任意 | `Unknown` | 组合状态 `Unknown`，破坏性动作 fail closed |
| `Exclusive` | 任意子 intent | 冲突域只保留显式选中的整组候选 |

不能把原始树上的 child text edit 直接套在已经替换的新文本上。可选实现是 Roslyn tree-level composition：先应用父 replacement，再用 tracked node/annotation 找到 residual child；或由父 intent 生成完整 replacement。否则必须拒绝组合。

### 5.3 部分重叠

两个 footprint 部分重叠但互不包含时，默认结果为 `Conflict`。只有提供了明确的组合证明（例如同一 list 的独立删除且位置可稳定重映射）时，才能使用 `ListComposable`。

## 6. 原子组与声明同步

参数收缩、delegate、indexer、extension 方法等同步动作不应先拆成互相独立的最终决策。建议先形成：

```text
AtomicGroup
  GroupId
  DeclarationIntent
  CallsiteIntents[]
  MethodGroup/Lambda/DelegateBindingIntents[]
  CompletenessProof
  FailurePolicy = RejectWholeGroup | SkipWholeGroup
```

组状态只有在所有成员都满足时才可选中：

```text
Select(group) iff
    group.CompletenessProof == Complete
    && every member has a valid binding and replacement
    && no member has unresolved conflict
    && all source tree versions are compatible
```

以下情况必须使组进入 `Unknown` 或 `Rejected`：

- dynamic/unresolved invocation；
- 跨 compilation/项目的引用范围未封闭；
- method group、lambda、delegate conversion 无法确认；
- query 被截断或预算耗尽；
- 任一调用点与另一个候选产生冲突；
- 声明 replacement 成功但某个 callsite replacement 失败。

默认采用 `RejectWholeGroup`。宁可不收缩，也不能只改变声明而留下不匹配的调用点。多个已经证明独立的 callsite 文本编辑可以在同一 atomic group 内 `ListComposable`，但不能因此降低 group 的完整性要求。

## 7. 未知证据和效果义务

### 7.1 Unknown 的来源必须可区分

至少区分：

```text
NoMatch       已确认没有匹配
Unavailable    查询/索引未提供
Unresolved     符号或动态绑定无法解析
Truncated      预算或结果上限截断
Unsupported    语法/语义未支持
Failed         分析阶段失败
```

`NoMatch` 可以参与 `Complete`；其他状态不能被当作空集合。`AnalysisEvidence` 已有 `Unavailable`、`Truncated` 方向，但必须让它们成为 Proof 的依赖和 Planner 的拒绝原因，而不只是输出图上的描述。

### 7.2 保留义务不是附加字符串

`PreservedObligations` 应至少能表达：

- invocation/getter/constructor 的执行或不执行；
- `await`、异常和短路控制流；
- assignment、`ref/out/in`、捕获变量和 definite assignment；
- 类型、转换、nullable、pattern 和 overload binding；
- `break`、`continue`、`return`、`throw` 和 `switch` fall-through；
- declaration/callsite/method-group 的签名一致性。

若规则没有能力证明某项义务，可以生成局部 `ExpressionReplacement`，但不能生成宣称行为安全的 `EffectPreservingRemoval` 或 `StructureComplete`。

## 8. Evidence、冲突和去重

### 8.1 唯一 resolver

`RuleDecisionEngine` 应逐步收敛为唯一的 `DecisionPlanner`。`ApplicationService` 只能编排和传递结果；`PrototypeRewriter` 只能做计划不变量检查。应移除或旁路以下重复语义：

- `FilterCompetingAncestors` 的动作方向猜测；
- `FilterCoveredDecisions` 的普通 span 静默过滤；
- `ApplicationService.FilterNestedDeleteDecisions` 的第二套父子过滤。

Rewrite 层仍可拒绝非法 partial overlap，但该拒绝应是计划不变量失败，而不是用更大的 span 选择业务赢家。

### 8.2 所有候选都要有结果

Planner 的输出必须能解释：

```text
candidate A selected
candidate B composed into A
candidate C rejected because proof P is Partial
candidate D conflicted with C due to partial overlap
candidate E skipped because atomic group G is Unknown
```

`AnalysisEvidence` 的 relation 可以继续使用 `Supports`、`RejectedBy`、`MergedInto`，但边的原因必须指向 typed proof/footprint；不能只根据两个 anchor 的 span 推断 `MergedInto`。

### 8.3 来源和 payload 不得在 Lift 丢失

当前类似：

```csharp
seedMarks.Concat(propagatedMarks.Select(item => item.Mark))
```

的投影应替换为保留完整传播记录的证据集合。至少保存 source mark、FactKind/capability、payload、depth、provenance、certainty 和 consumed/source node keys。

此外，`RuleBindingValidator` 的 propagation payload 白名单应覆盖 `ExpressionTopologyPayload`，并有 validation-enabled 回归测试，确保逻辑传播不会被错误报告为 binding failure。

## 9. 对“是否把决策规则移动到 Mark”的审查结论

### 可以留在 Mark 的内容

- 原子语法目标命中；
- compilation-wide 的未引用/不可达方法事实；
- 明确的符号、引用、调用关系事实；
- 不依赖其他候选赢家的局部目标 capability；
- 完整 provenance 和 typed payload。

### 不应移动到 Mark 的内容

- `Delete`/`Replace`/`Skip` action；
- “父候选覆盖子候选”的规则；
- 决定 `ready && s.IsReady` 是否删除整个 if 的结构结论；
- 读取其他候选后选 winner 的过滤器；
- replacement 的 residual mapping；
- 参数声明和调用点的原子完成结论；
- rewrite span 合并和部分重叠处理。

`UnreachableMethodMarkRule`、`UnreferencedMethodMarkRule` 可以继续在 Mark，因为它们产生的是全局目标事实，而不是最终改写动作。`IfStructureProposalRule`、`LogicalExpressionProposalRule`、`DefaultRemovalProposalRule` 不应下移到 Mark；它们分别依赖结构 proof、逻辑规约 payload 和决策上下文。

## 10. 分阶段迁移

### Phase 0：冻结行为并增加观察

- 增加 `Fact`、`Proof`、`EditIntent`、`DecisionPlan` 的最小内部模型；
- 不改变已有 AND 删除、OR 局部 Replace 结果；
- 对所有当前被过滤的候选记录 rejection/dominance 诊断；
- 修正去重 key 和传播 payload 传递；
- 增加 `TopologyHost` 不能满足 `StructureComplete` 的契约测试。

### Phase 1：typed coverage

- 将 `MarkCoverage.IsCovered` 替换为 requirement-aware evaluator；
- `FlowLogicalExpression` 只作为 `TopologyHost`；
- `IfStructureLifter` 只接受完整的 structure proof；
- 补充 effect、loop、switch 和未知状态测试。

### Phase 2：显式 footprint 和唯一 resolver

- Proposal 为每个候选填充 anchor、read set、consumed set、composition 和 proof references；
- DecisionPlanner 根据 footprint 构建冲突图；
- 先处理显式 dominance，再处理 composition，再处理 partial overlap；
- 旁路 `ApplicationService` 的重复过滤；
- 保持 Rewrite 的 overlap 检查作为防御性断言。

### Phase 3：residual mapping 和 atomic group

- 为父 Replace 增加 old-to-new mapping；
- 支持 tracked-node/tree-level composition，无法映射时 fail closed；
- 将参数声明、所有调用点和绑定点纳入 atomic group；
- 把多文件 source identity 和 tree version 纳入计划校验。

### Phase 4：收紧未知和效果证明

- 将 `Unavailable`、`Truncated`、`Unresolved` 贯穿 Proof 和 DecisionPlan；
- 对破坏性结构动作要求 `Complete`；
- 对无法证明副作用保持义务的动作拒绝 `EffectPreservingRemoval`；
- 对完整 evidence graph 做 deterministic serialization 和诊断回归。

## 11. 验收标准与测试矩阵

### 必须成立的不变量

- Mark/Propagate 永远不会直接产生 `Delete`、`Replace` 或 winner；
- `TopologyHost` 永远不能单独满足 `StructureComplete`；
- `StructureComplete` 只能来自绑定目标、完整证据和 Lift proof；
- 父 Delete 只有 explicit dominance proof 才能消费子 Replace；
- 父 Replace 保留子节点时必须有 residual mapping；
- partial overlap 默认返回冲突，不依赖生成顺序；
- atomic group 任一成员未知或失败时默认整组不选；
- replacement node 不参与原始树 coverage footprint；
- 所有被拒绝、合并、支配和跳过的候选均可解释；
- 同一节点上的不同 fact kind/payload 不会因简单 span 去重而丢失。

### 最小测试集合

| 测试 | 预期 |
| --- | --- |
| `ready && s.IsReady` | `StructureComplete`，外层 Delete 支配内层 Replace |
| `ready || s.IsReady || fallback` | 只有 logical Replace，保留 survivors |
| 只有一个 logical sibling | coverage 为 Partial，不生成结构 Delete |
| topology host exact hit | 只满足宿主查找，不满足结构完整 |
| 父 Replace + 子 `B()` 删除 | 有 mapping 则重映射后组合，无 mapping 则冲突 |
| 父 Delete 无完整 proof + 子 Replace | 父拒绝/冲突，子不静默消失 |
| 两个 partial-overlap Replace | 冲突或域 Skip |
| method parameter shrink + unresolved callsite | 整个 atomic group Unknown/Rejected |
| declaration/callsite 跨文件 | 同一组内 source identity 稳定且全选或全不选 |
| dynamic/reflection/截断查询 | Unknown 不得升级为 Complete |
| getter/await/throw/ref/out | 缺少 effect proof 时不得声称安全删除 |
| 同节点不同 payload | facts/proofs 全部保留并可追溯 |

现有回归必须继续通过：

- `DecisionStructureValidationTests` 的逻辑 AND 结构删除；
- `GraphAnalyzerTests.Analyze_LogicalAndCondition_RightTargetDeletesIf`；
- `DecisionComplexTests` 的 nested AND/OR；
- `RuleStructureContractTests.MarkCoverage_WhenOnlyOneConditionSiblingIsMarked_ReturnsFalse`；
- 父控制结构覆盖 body 的 rewrite 测试；
- method/local function/indexer/delegate/extension 参数同步测试。

## 12. GitHub 一手资料与设计映射

以下资料用于验证“事实/标记、编辑足迹、组合和原子性应分层”的方向。具体核对结果另见 [coverage-decision-composition-research.md](../research/2026-09-10-coverage-decision-composition-research.md)。

- [Roslyn SyntaxEditor](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs)：通过 tracked/annotated nodes 管理编辑对象；后续编辑可以基于更新后的树定位节点，适合 residual mapping。
- [Clang Replacement](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Core/Replacement.cpp)：重叠 replacement 需要明确处理，不能把部分重叠交给排序猜测。
- [Clang AtomicChange](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Refactoring/AtomicChange.cpp)：相关 replacements 可以聚合成一个原子变化，适合声明与调用点同步的 atomic group 思路。
- [OpenRewrite Markers](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/Markers.java)：marker 是树元数据；搜索结果或 marker 不等于 rewrite action。
- [Scalafix Patch](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/patch/Patch.scala)：patch 组合有独立语义，tree replacement 与子 patch 不能无条件叠加。

共同启示可以压缩为：

```text
facts/markers -> typed proof -> edit footprint -> resolver/editor
```

这些项目不支持“父 span 更大所以父动作永远赢”的规则；它们支持的是有节点身份、编辑范围、组合能力和冲突结果的显式模型。

## 13. 本轮决策

1. 不执行旧的 `2026-09-09-coverage-decision-constraints-execution.md`，因为它仍以两个候选场景为主要实施轴，缺少 residual mapping、atomic group、effect obligation 和统一 Unknown 语义。
2. 先采用本文模型作为设计基线，待确认后再按 Phase 0 开始实现。
3. 在确认前不移动生产代码中的 Mark/Decision 规则，不删除现有过滤器；先通过新增契约测试和诊断确认迁移行为。
4. 本文只规定语义和接口不变量，不强迫一次性引入所有公共类型；实现时应优先选择现有项目能承载的 deep module seam，例如一个唯一的 `DecisionPlanner` 接口。

## 14. 严格化修订：封闭 raw Mark 到动作的旁路

上一版模型已经区分了 Fact、Proof 和 EditIntent，但还没有把“谁有资格创建 EditIntent”写成硬门禁。结合当前实现，必须增加一层 `CandidateAuthorization`：

```text
Fact
  -> CoverageContract
  -> Proof
  -> CandidateAuthorization
  -> EditIntent
  -> ConstraintGraph
  -> DecisionPlan
  -> TreeRewrite
```

`CandidateAuthorization` 是针对一个具体目标和一个允许动作的授权，不是 Mark 上的布尔 capability：

```text
CandidateAuthorization
  CandidateId
  Target: SemanticRegion
  AllowedActions: Delete | Replace
  ProofReferences
  ReadSet
  EraseSet
  WriteSet
  PreserveSet
  Composition
  AtomicGroup
  SourceVersion
  Status: Complete | Unknown | Rejected
```

没有 `Complete` authorization 的候选不得创建 `Delete` 或 `Replace`。因此，当前 `DefaultRemovalProposalRule` 中从 `EnumerateActiveDerivedMarks` 和 `EnumerateUncoveredSeedMarks` 直接调用 `DeleteDecisionFactory.CreateDeleteDecision` 的路径必须关闭或仅保留为兼容诊断路径。目标事实可以继续由 Mark 产生，但动作必须经过一个 Lift/Proof 授权。

### 14.1 当前代码中的旁路清单

| 位置 | 当前行为 | 约束结论 |
| --- | --- | --- |
| `MarkingEngine` | 按 rule/span 去重 seed mark | key 必须加入 fact kind、payload identity、provenance 和 source version |
| `RuleGraphAnalysisExecutor` | seed/propagated/lifted 各自按节点位置去重 | 去重不能合并不同事实、来源或 payload |
| `MarkCoverage` | 节点精确命中或所有子节点递归命中即 `true` | 改为按 goal 和 semantic region 评估，不能返回无上下文布尔值 |
| `IfStructureLifter` | 只检查 `if.Condition` coverage | 还必须证明 body/tail/control-flow/effect obligations |
| `ControlStructureLiftingRule` | 主要检查循环/return 的 required expressions | 不能把 header 完整当作整个结构可删除 |
| `SwitchStructureLifter` | 按 section/statement span key 收束 switch | 必须表达 default、fall-through、break/continue 和保留分支义务 |
| `DefaultRemovalProposalRule` | raw seed/propagated/lifted mark 可直接生成 Delete | 只接受 `CandidateAuthorization`，禁止 raw Mark 授权动作 |
| `DefaultDecisionPolicy` | 按 ancestor/span 合并 DecisionUnit | 只能消费 footprint 和 proof，不可由 span 猜 winner |
| `ApplicationService` | 再次执行 `FilterNestedDeleteDecisions` | 语义 resolver 必须唯一，Application 只编排 |
| `PrototypeRewriter` | 按 text span 选择更大的编辑 | 只能验证已解析计划；父 Replace 需要 residual mapping |

这也说明哪些规则不应移动到 Mark：`IfStructureProposalRule`、`LogicalExpressionProposalRule`、`ControlStructureRemovalProposalRule` 和 `DefaultRemovalProposalRule` 的动作创建/冲突语义都依赖 proof、footprint 或其他候选，不能下移。可以下移的是“发现事实”和“产生证明输入”，不能下移的是动作授权和候选选择。

### 14.2 SemanticRegion 取代 AST descendant coverage

`SemanticRegion` 不是“一个节点加上所有 descendants”。它是一个有边界和义务的原始源区域：

```text
SemanticRegion
  RegionId
  SourceVersion
  AnchorNodes
  RequiredFacts
  ForbiddenFacts
  EffectObligations
  BindingObligations
  ControlFlowObligations
```

建议的最小 region 类型为：

| region | 例子 | Complete 的含义 |
| --- | --- | --- |
| `AtomicExpression` | `s.IsReady`、getter、invocation | 目标事实、类型/绑定和效果状态已知 |
| `LogicalChain` | `ready && s.IsReady || fallback` | removable operands、survivors、operator 和短路边界均已知 |
| `ControlStructure` | `if`、loop、`switch` | header、body、tail、控制流出口和效果义务均已封闭 |
| `BindingClosure` | 方法声明及调用点 | declaration、所有相关使用、dynamic/unresolved 状态已封闭 |
| `CompilationTarget` | 未引用/不可达声明 | compilation 范围内的查询结论已封闭 |

`MarkCoverage` 只能作为某个 requirement evaluator 的内部递归工具，不能再被当作结构完整性的定义。对于 `StructureComplete`，精确命中 `FlowLogicalExpression` 只能满足 `TopologyHost`，必须拒绝；对于 `LogicalReduction`，它还需要明确的 removable/survivor payload。

### 14.3 五个集合和三个版本

每个授权的 EditIntent 都应携带以下集合，避免把 anchor、覆盖范围和 replacement 混成一个 span：

```text
ReadSet      读取的原始节点、符号、事实和 proof
EraseSet     已证明会从原始树中移除或被整体消费的语义区域
WriteSet     replacement 引入的节点、绑定和类型义务
PreserveSet  必须在结果中继续存在的效果、绑定和控制流义务
ProofSet     授权该候选的 typed proof ids
```

同时绑定：

```text
SourceVersion       文件路径、tree identity/text hash、原始 span/node identity
BindingVersion      compilation、semantic model 和 symbol binding 版本
AnalysisEpoch       本次 facts/proofs/decisions 的不可变分析轮次
```

`FilePath + SpanStart + SpanLength + RawKind` 只能作为迁移期显示键，不能作为跨树、跨 compilation 或 replacement 后的节点身份。

### 14.4 父子候选的约束图

Planner 不再把父子关系实现成排序或 `Where`。它先在原始树上构造关系边，再验证约束：

```text
requires(parent, proof)
dominates(parent, child)
composes(parent, child, residual-map)
conflicts(candidate-a, candidate-b, reason)
member-of(candidate, atomic-group)
```

外层 `Delete` 只有在下面条件全部成立时才能消费内层 `Replace`：

```text
CanDominate(parent, child) =
    parent.Authorization.Status == Complete
    && parent.Action == Delete
    && parent.Composition == OpaqueDominates
    && child.AnchorRegion in parent.EraseSet
    && child.PreserveSet is compatible with parent.PreserveSet
    && parent.Proof contains StructureComplete
```

这条规则直接解决“外层 `if Delete` 包含内层 logical `Replace`”问题：

- 有完整 `StructureComplete` 和 `EraseSet` 时，父 Delete 可以显式支配 child；
- 只有 topology host、condition 的部分 coverage 或未知效果时，父 Delete 被拒绝或冲突，child 不得静默消失；
- parent 的 span 更大、生成更早或 `FinalNode` 包含 child 都不是授权条件。

对于两个没有 dominance proof 的嵌套候选，迁移期应选择 `ConflictDomain.Skip` 或拒绝父候选并保留 child，不能返回一个看似成功但没有可解释来源的单一决策。

### 14.5 Parent Replace 的 residual map

父 Replace 如果保留 child，必须给出旧树到 replacement tree 的映射：

```text
ResidualMap(oldNode) =
  Retained(newPath)
  Replaced(newPath, new obligations)
  Removed
  Unknown(reason)
```

组合规则为：

| parent composition | residual map | child 结果 |
| --- | --- | --- |
| `Composable` | `Retained`/`Replaced` 且类型和义务满足 | 在新树中重定位后组合 |
| `OpaqueDominates` | child 位于 `EraseSet` | 父候选支配 child |
| 任意 | `Removed` 但 child `PreserveSet` 非空 | 冲突 |
| 任意 | `Unknown` | `Unknown`，破坏性动作 fail closed |

推荐使用 Roslyn 的 tracked node/annotation 或等价的 tree-level composition。不能把旧树上的 child text span 直接套到已经替换的新文本上。无法建立映射时，结果必须是冲突或跳过，而不是静默丢弃 child。

### 14.6 AtomicGroup 与 Unknown

声明同步、跨文件参数收缩、delegate/method-group/lambda 调整应先形成一个 atomic group，再将组内编辑投影为 EditIntent：

```text
Select(group) iff
  CompletenessProof == Complete
  && every member is bound and conflict-free
  && no member has Unknown
  && all SourceVersion/BindingVersion are compatible
```

`NoMatch` 是已证明没有成员；`Unavailable`、`Unresolved`、`Truncated`、`Unsupported` 和 `Failed` 都是 Unknown，不能当作空集合。默认 `FailurePolicy = RejectWholeGroup`，不能只收缩声明而留下未解析调用点。

### 14.7 实施顺序的收敛版

1. **观察期**：保留旧输出，但记录每个 raw-mark action、被过滤 candidate、span relation、missing proof 和 unknown reason。
2. **闭合入口**：新增 `CandidateAuthorization`，先让默认删除/结构删除在没有授权时产生拒绝证据；不急于删除旧字段。
3. **Typed coverage**：引入 `CoverageContract -> Proof`，让 `IfStructureLifter`、loop 和 switch 只消费对应 region 的 Complete proof。
4. **唯一 planner**：用 footprint 关系取代 `FilterCompetingAncestors`、`FilterCoveredDecisions` 和 `ApplicationService.FilterNestedDeleteDecisions` 的业务语义。
5. **树级重写**：实现 residual map；不可映射的 parent/child 组合 fail closed。
6. **原子组和版本**：将多文件同步、unknown 和 source/binding version 纳入计划有效性。

在第 2 步完成前，不应把更多 `Delete` 规则移动到 Mark；那只会让动作旁路变多。

## 15. 严格版验收标准

- raw `MarkRecord`、`PropagatedMarkRecord` 或普通 `LiftedMarkRecord` 不能单独创建 `Delete`/`Replace`。
- 每个破坏性候选都有 `CandidateAuthorization.Status == Complete` 和 typed proof reference。
- `TopologyHost`、`AtomicTarget`、`ChildComposable` 不可直接升级为 `StructureComplete`。
- 父 Delete 只有 explicit dominance、`EraseSet` 和 preserve compatibility 才能支配 child。
- parent Replace 保留 child 时必须存在 `ResidualMap`；不存在则为 conflict/skip。
- partial overlap、unknown、unresolved callsite 和 atomic group failure 都可观察且 fail closed。
- Mark/Propagate/Lift/Propose/Planner/Rewrite 每层只有一个动作责任；Application 不再实现第二套 winner 选择。
- 现有 AND/Delete、OR/Replace、parent-control-delete 测试继续通过。

## 16. Authority funnel 与公开 API 封闭

仅增加 `FactKind`、`Proof` 和 `Footprint` 字段仍不足以约束行为。当前 `RuleDefinitionPropose.Propose` 可以直接构造带 `Delete` 的 `DecisionUnit`，`RuleDecision` 有公开构造器，而 `PrototypeRewriter.Rewrite` 接受任意 `IEnumerable<RuleDecision>`。因此调用者可以绕过 Planner，把未经证明的动作直接送入 Rewrite。

必须把可执行对象按权限分成不同阶段：

```text
UntrustedRuleOutput
  -> ValidatedFact
  -> GoalSpecificProof
  -> IntentDraft
  -> AuthorizedIntent
  -> ExecutablePlan
  -> Rewrite
```

阶段约束如下：

| 类型 | 生产者 | 是否可执行 |
| --- | --- | --- |
| `MarkRecord`/`PropagatedMarkRecord` | Mark/Propagate | 否 |
| `LiftedMarkRecord`/`CoverageProof` | Lift/Proof evaluator | 否 |
| `DecisionUnit`/`IntentDraft` | Proposal | 否 |
| `AuthorizedIntent` | 唯一 DecisionPlanner | 只能参与计划 |
| `ExecutablePlan` | Plan validator | 是 |
| `RuleDecision` | 兼容/显示投影 | 否 |

`PrototypeRewriter` 的正式入口只能接受 `ExecutablePlan`。旧的 `Rewrite(IEnumerable<RuleDecision>)` 如果必须保留，应通过兼容适配器进入，并满足以下条件：

- 没有 typed ProofReference 的结构性 Delete/Replace 直接拒绝；
- 没有 SourceVersion、footprint 或 residual mapping 的嵌套候选视为 `Unknown`；
- 兼容路径不得使用 span 大小替代 Planner 的 dominance 关系；
- 兼容路径的结果只能作为诊断，不能绕过正式计划执行。

`DecisionBindingValidator` 不能只是可选报告器。建议分成三个强制门：

```text
CandidateValidator
  invalid candidate -> Rejected

PlanValidator
  invalid relation/atomicity -> NoExecutablePlan

PostRewriteValidator
  failed preserve/binding check -> rollback affected group
```

配置中的 `ValidateBindings = false` 可以关闭详细诊断，但不能关闭这些安全门。诊断开关和可执行性约束必须分离。

### 16.1 Public record 不能充当授权 token

当前 `MarkRecord` 是可以由外部直接构造并通过 `with` 修改 `FactKind` 的 public record。即使未来增加 `Capability` 字段，也不能把它视为不可伪造的授权 token。迁移期应把所有跨阶段输入当作 untrusted value，在每个阶段重新验证：

```text
producer node + declared output contract
  -> fact kind/payload/provenance validation

fact set + coverage contract
  -> proof validation

proof + transformation contract
  -> authorization validation

authorized intents + relation graph
  -> executable plan validation
```

长期可以把可执行对象的构造器设为 internal，并只暴露返回已验证对象的 factory；但即使保留 public rule/plugin API，Rewrite 仍必须只接受 `ExecutablePlan`。

### 16.2 Authority funnel 的测试合同

除源码结果测试外，必须加入 API 级测试：

```text
untyped Mark -> cannot authorize structural Delete
TopologyHost -> cannot create StructureComplete Proof
forged LiftIfStructure fact -> rejected by producer/proof contract
RuleDecision without ProofReference -> cannot enter ExecutablePlan
ValidateBindings=false -> safety gate remains active
parent Delete without dominance proof -> child Replace remains observable
post-rewrite validation failure -> atomic group has no partial effect
```

因此“是否把某些规则移动到 Mark”应按权限回答：

- 可以移动到 Mark：目标发现、拓扑发现、引用/调用观察、查询状态和 provenance；
- 不能移动到 Mark：结构完整性、行为变化授权、父子支配、冲突选择、原子组提交和 Rewrite 执行；
- 可以由 Lift 产生：绑定具体目标的 Proof；
- 只能由 Planner 产生：`AuthorizedIntent` 和 `ExecutablePlan`。

没有这条 authority funnel，Fact/Proof/Intent 只是约定俗成的数据字段，旧 API 或自定义规则仍然可以绕过约束。
