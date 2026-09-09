# Mark、Decision 边界与 Replace/外层 if 冲突研究

> 状态：研究完成，未修改生产代码。
>
> 日期：2026-09-09。
>
> 研究问题：审查哪些规则应留在 Mark，分析局部 Replace 与外层 if 删除候选的冲突，并确定限制 Mark 与 Decision 行为的设计。

## 结论摘要

不建议把 Delete、Replace、冲突裁决或重叠编辑过滤移动到 Mark。Mark 的职责应是产出最小目标和可追溯的语义事实；它可以做 compilation-wide 证明，但不应产生最终改写动作。

当前问题有两个相互叠加的来源：

1. MarkCoverage 只要看到 required node 自身被标记，就把它当成完整覆盖。FlowLogicalExpression 这类“逻辑宿主/拓扑事实”因此可能被误读为“整个 if 条件已经证明可删除”。
2. 决策阶段和 ApplicationService 各有一套按 span 的过滤逻辑。它们没有区分“结构删除覆盖了子编辑”和“局部 Replace 只拥有自己的表达式锚点”，所以父子决策的语义被语法包含关系替代。

推荐的目标形态是：

~~~text
Mark       -> 原子候选、全局目标、带类型的证据能力
Propagate  -> 拓扑、兄弟、引用和调用关系事实
Lift       -> 结构完整性、逻辑规约 payload、被消费的结构节点
Propose    -> 把已证明事实翻译为 Delete/Replace 的 DecisionUnit
Decision   -> 基于显式编辑足迹解决冲突，唯一输出最终决策
Rewrite    -> 只执行已解决的决策，并保留重叠校验作为防御
~~~

对于“外层 if Delete + 内层 logical Replace”：

- 只有当外层 if 带有明确的完整结构证明，并且其 consumed nodes 明确包含内层逻辑宿主时，才允许外层 Delete 支配内层 Replace。
- 如果外层候选只是因为逻辑宿主 mark 命中了 if 条件而产生，则该 Delete 候选在 Lift 阶段就不应生成。
- 如果两个候选都有效但没有显式支配或组合关系，必须 fail closed：返回冲突诊断并跳过该冲突域，不能仅按 span 猜一个赢家。

当前测试已经把部分行为当作契约：以 s 为目标时，ready && s.IsReady 的 if 删除测试期望 DeleteWholeIf。因此引入 typed coverage 后，需要保留这类行为的显式完整证明；不能把所有 logical host 都简单降级为局部 Replace，也不能未经确认直接改写这些测试。

## 研究范围与限制

本报告读取了 NLISSN 的 Marking、Propagation、Lift、Propose、Decision、Application 和 Rewrite 代码，以及相关 Host、Unit、Contract 测试。代码证据使用仓库当前行号；后续代码变更后应重新定位行号。

用户提供的 Codex 任务链接：

    codex://threads/019fb70a-1c50-7360-8b7f-8c7573370176

已尝试通过 Codex 任务读取接口加载，但返回 thread not loaded。因此本报告不能把该任务中的未读取内容当作事实，关于冲突的判断仅基于当前仓库代码、测试和下列 GitHub 一手源码。

GitHub API 搜索在本次研究中受到 rate limit，未做 star 数或全网项目排名。外部结论只引用已经读取或可直接定位的官方仓库源码、README 和官方实现。

## 阶段职责审查

| 阶段 | 应产出的内容 | 不应承担的内容 | 当前代码证据 |
| --- | --- | --- | --- |
| Mark | 原子目标、全局候选、最小语义事实和 provenance | Delete/Replace、父子赢家、重叠编辑裁决 | MarkRecord 没有 DecisionActionKind；MarkingEngine 只执行规则、校验节点、绑定图节点并去重，见 MarkRecord.cs:11-87 与 MarkingEngine.cs:8-70 |
| Propagate | 表达式拓扑、兄弟关系、符号引用、调用/声明关系 | 结构完整性和最终动作 | LogicalExpressionPropagationRule 对 logical host 和 direct sibling 传播事实，见 LogicalExpressionPropagationRule.cs:140-172 |
| Lift | 从多个事实构造结构完整性、逻辑规约和结构 payload | 直接执行 rewrite 或跨候选决定赢家 | IfStructureLifter 依据条件覆盖生成 DeleteWholeIf/Replace* 结构 payload；LogicalExpressionLiftingRule 生成 removable/survivor payload，见 IfStructureLifter.cs:13-64 和 LogicalExpressionLiftingRule.cs:121-138 |
| Propose | 将已证明的 Lift/Mark 事实转换为 DecisionUnit | 重新证明结构完整性；用 span 推测其他规则的语义 | LogicalExpressionProposalRule 由 payload 构造 Replace，IfStructureProposalRule 由 if completion payload 构造结构决策 |
| Decision | 冲突域收口、编辑足迹组合、父子支配关系和最终 RuleDecision | 重新分析原始语义或依赖某个 proposal 的执行顺序 | RuleDecisionEngine 在 proposal 结束后按 conflict domain 收口，但当前仍混用多套 span 过滤 |
| Rewrite | 执行已解决的编辑并做最后的重叠防御 | 反向修改决策语义 | PrototypeRewriter 只把 RuleDecision 转为计划；BuildEffectiveRewritePlan 以 span 包含作为兜底 |

### 哪些规则不应迁移到 Mark

以下规则应继续保留当前阶段边界：

| 规则 | 判断 | 原因 |
| --- | --- | --- |
| LogicalExpressionProposalRule | 不迁移 | 它需要从 Lifted payload 构造 replacement node 和 DecisionUnit；这是动作候选，不是 Mark 事实 |
| IfStructureProposalRule | 不迁移 | 它把 IfStructureLiftPayload 映射为 DeleteWholeIf、ReplaceIfWithElseTail 等最终候选；Mark 无法承担结构动作 |
| DefaultRemovalProposalRule | 不迁移 | 它是默认删除的最后翻译层，还要排除已被结构规则消费的节点 |
| LogicalExpressionPropagationRule | 不迁移到 Mark | 它传播逻辑宿主和 sibling，是拓扑事实，应该留在 Propagate |
| IfStructureLifter / LogicalExpressionLiftingRule | 不迁移到 Mark | 它们需要聚合多个节点的证据并判断结构或规约 payload，正是 Lift 的职责 |
| UnreferencedMethodMarkRule | 保留在 Mark，当前做法合理 | compilation-wide linkage 证明最终仍输出方法目标 Mark，见 UnreferencedMethodMarkRule.cs:34-76 |
| UnreachableMethodMarkRule | 保留在 Mark，当前做法合理 | 调用图可达性证明输出方法目标 Mark，不直接产生删除动作，见 UnreachableMethodMarkRule.cs:37-63 |
| PrivatizeInternalOnlyPublicMethodRule、ClearUnusedInterfaceImplementationRule | 保留 Mark 事实 | 它们把全局引用事实收束为目标声明候选，后续仍需要 Propagate/Lift/Propose 完成链路 |

这里的“保留在 Mark”不等于允许 Mark 生成决策。全局规则可以在 Mark 中运行昂贵或 compilation-wide 的证明，但输出仍必须是带 fact kind 和 provenance 的目标事实。

## 当前代码中的问题链

### 1. 逻辑传播产生宿主事实

LogicalExpressionPropagationRule 在 logical and/or 上先产出 FlowLogicalExpression 宿主，再在特定模式下产出 direct sibling 的 TargetExpression。其代码明确表达的是“拓扑允许继续传播”，不是“整个父级 if 可以删除”：

    src/NLISSN.Rules/Propagate/LogicalExpressionPropagationRule.cs:140-172

这类事实是必要的，因为 LogicalExpressionLiftingRule 需要用它寻找宿主。但它不应该天然拥有结构删除能力。

### 2. MarkCoverage 把事实类型抹平

当前 MarkCoverage 的核心逻辑是：

    src/NLISSN.Rules/Lift/Support/MarkCoverage.cs:9-28

如果 markedKeys 包含 requiredNode 的节点键，就直接返回 true；否则递归要求所有子节点都被覆盖。它没有检查 MarkRecord.FactKind、OutputKind、来源阶段或事实能力。

因此，下面两种事实在 coverage API 中等价：

~~~text
“这个节点是 logical host，供后续规约使用”
“这个节点的所有可删除语义已经完整证明”
~~~

二者实际上不等价。父级 FlowLogicalExpression 或 ExpressionHost mark 命中 if condition 时，IfStructureLifter 可能把它当成完整条件覆盖。

### 3. If Lift 将 coverage 转成结构候选

IfStructureLifter 在 TryBuildPayload 和 BuildIfStructureLiftedMarks 中调用 MarkCoverage，然后构造 IfStructureLiftPayload。payload 进一步可以被 Propose 转换为 DeleteWholeIf 或结构 Replace：

    src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs:13-64
    src/NLISSN.Rules/Lift/Structures/IfStructureLifter.cs:66-100
    src/NLISSN.Rules/Propose/Support/ProposalFacts.cs:226-289

所以问题不是 Propose 单独“选错了动作”，而是结构完整性证明在 Lift 入口处已经可能被过宽的 coverage 认定为成立。

### 4. 同一个宿主还可能产生局部 Replace

LogicalExpressionLiftingRule 的 TryBuildPayload 会从 logical host 的 operands 中筛出 removable 和 survivors；二者都存在时就可形成 LogicalExpressionReductionPayload：

    src/NLISSN.Rules/Lift/LogicalExpressionLiftingRule.cs:121-138

LogicalExpressionProposalRule 随后用这个 payload 生成以 logical host 为 anchor 的 Replace：

    src/NLISSN.Rules/Propose/LogicalExpressionProposalRule.cs:39-56

这意味着同一个源节点区域可能同时出现：

~~~text
IfStructureLiftPayload      -> Delete(if)
LogicalExpressionReduction  -> Replace(logical-host)
~~~

两者不是简单的同类候选。前者声明整个结构已被证明可以移除，后者声明只对一个表达式锚点做局部规约。

### 5. 决策过滤存在三套不一致的规则

当前 RuleDecisionEngine.ResolveUnits 先按 BuildConflictGroupKey 分组，再在每个域内调用 FilterCompetingAncestors，最后调用 FilterCoveredDecisions：

    src/NLISSN.Core/Decision/DecisionModel.cs:370-399

具体问题：

1. FilterCompetingAncestors 只在 Delete 的 anchor 与 Replace anchor 完全 ReferenceEquals 时过滤 Delete，不能表达祖先 Delete 是否真的消费了后代 Replace，见 DecisionModel.cs:491-521。
2. FilterCoveredDecisions 只按 FinalNode.Span.Contains 过滤，见 DecisionModel.cs:523-533。它没有区分 Delete、Replace、结构 Replace 和可组合的独立编辑。
3. BuildConflictGroupKey 对非 Replace 候选会向 logical host 和祖先 conflict node 启发式上推；Replace 则固定到自己的 anchor key，见 DecisionModel.cs:536-590。这种“根据语法树猜 conflict domain”的方式容易把不同语义域收成一个域，或把相关候选分开。
4. ApplicationService 又做了一次 FilterNestedDeleteDecisions。它只在 Replace span 包含 Delete span 时删除 Delete，无法表达“外层 Delete 包含内层 Replace”这种相反方向，见 ApplicationService.cs:245-289。

这说明当前实现把三个概念混合了：

~~~text
syntax containment       语法节点是否包含另一个节点
decision dominance       一个动作是否证明并消费了另一个动作
rewrite overlap           最终文本编辑是否重叠
~~~

它们不能互相替代。

### 6. Rewrite 层只能兜底，不能补语义证明

PrototypeRewriter 先将决策转换为编辑计划，然后在 BuildEffectiveRewritePlan 中按文本 span 处理重叠：

    src/NLISSN.Core/Rewrite/PrototypeRewriter.cs:31-137
    src/NLISSN.Core/Rewrite/PrototypeRewriter.cs:309-367

外层编辑包含内层编辑时，Rewrite 层保留外层结果。这能避免同一源片段被重复改写，但不能证明外层 Delete 正确。如果外层 Delete 是由误判的 parent coverage 生成的，Rewrite 层只会把错误结果稳定执行。

## 冲突模型

建议把以下情况作为明确的决策契约，而不是由通用 span 过滤隐式推断：

| 候选集合 | 推荐结果 | 必须满足的条件 |
| --- | --- | --- |
| 只有 logical host 的局部 Replace | 保留 Replace | Lift payload 指明 anchor、removable operands 和 survivor operands |
| 外层 if Delete + 内层 logical Replace | 外层 Delete，丢弃内层 Replace | IfStructure 必须有 StructureComplete 证明，且 consumed nodes 明确包含 logical host |
| 外层 if Delete + 内层 logical Replace，但只有 host/topology mark | 丢弃外层 Delete；保留内层 Replace或整个域 Skip | 不能把 host mark 当作 WholeCondition 证明；默认应 fail closed |
| 外层 if Replace + 内层编辑 | 通常外层 Replace | 只有外层 payload 明确消费内层节点并生成完整 replacement 时才支配子编辑 |
| 祖先 Delete + 后代 Delete | 祖先 Delete | 两者属于同一结构消费域；后代只是被外层覆盖 |
| 不同冲突域且编辑不重叠 | 两者都保留 | 由显式 conflict/footprint 证明独立 |
| 部分重叠但互不包含 | 整个冲突域 Skip 或诊断失败 | 不允许 Rewrite 层按顺序猜结果 |
| replacement node 是新建节点、不在原始 syntax tree | 仍可 Replace | 冲突足迹只使用原始 anchor/consumed nodes，不使用 replacement node 的虚构 span |

“外层 Delete 是否优先”不应是全局 span 规则，而应是外层 Lift 证据的属性。推荐使用以下判定：

~~~text
ParentDeleteWins =
    Parent.HasStructureCompleteProof
    && Parent.ConsumedNodes.Contains(Child.Anchor)
    && Parent.ActionFootprint.Dominates(Child.ActionFootprint)
~~~

没有这三个条件时，外层 Delete 不能自动覆盖内层 Replace。

## 推荐设计

### 1. 给事实增加 typed coverage/capability

当前 MarkRecord 已有 FactKind、SemanticTag、OutputKind 和 Origins，但 coverage 逻辑没有消费这些类型。建议增加一个受控的能力维度，名称可按现有命名风格确定，例如：

~~~csharp
enum CoverageCapability
{
    TargetCandidate,
    TopologyHost,
    ChildComposable,
    GlobalTarget
}
~~~

能力语义建议如下：

| 能力 | 可以证明什么 | 不可以证明什么 |
| --- | --- | --- |
| TargetCandidate | 某个原子节点是规则目标 | 父结构可以删除 |
| TopologyHost | 节点是表达式传播/规约宿主 | 宿主覆盖的所有语义都可删除 |
| ChildComposable | 子节点可以参与某个已知组合 | 组合已经完成或应当执行 Delete |
| GlobalTarget | compilation-wide 证明命中目标声明 | 任意调用方或父结构都可删除 |

FlowLogicalExpression 应归类为 TopologyHost，不应被 MarkCoverage 当作 WholeNode。StructureComplete 不是普通 seed mark 能力，而应由 Lift 在聚合证据后产生。

不建议用 Reason 字符串或简单的 OutputKind 名称承担这项约束。能力应进入 RuleFactKind/typed payload 或一个明确的 CoverageClaim，便于 RuleSyntaxContractValidator 和测试验证。

### 2. 将 coverage 改成带需求类型的 API

不建议继续保留一个无上下文的 IsCovered(requiredNode, marks)。至少应区分：

~~~csharp
IsCovered(requiredNode, marks, CoverageRequirement.LogicalReduction)
IsCovered(requiredNode, marks, CoverageRequirement.StructureComplete)
~~~

规则约束：

- LogicalReduction 可以接受 operand-level 的 ChildComposable 事实，并生成 removable/survivor payload。
- StructureComplete 只能接受显式的完整条件证明，或者由 Lift 递归验证所有需要的子节点和短路边界后生成。
- TopologyHost 只能作为搜索宿主，不能单独满足 StructureComplete。
- exact parent mark 只有在其能力类型明确允许时才算完整覆盖。

这样既能保留“多个子节点共同覆盖一个逻辑条件”的能力，也能防止一个宿主标记越权成为 if 删除授权。

### 3. 让 Lift 产出结构证明和消费范围

建议扩展 LiftedMarkRecord 的 payload，而不是在 Mark 中直接放 DecisionActionKind：

~~~text
LogicalExpressionReductionPayload
    Host
    RemovableOperands
    SurvivorOperands
    AllowedCapability = ExpressionReplace

IfStructureCompletionPayload
    AnchorIf
    Tail
    ConsumedNodes
    Proof = StructureComplete
    AllowedCapability = StructureDelete or StructureReplace
~~~

IfStructureLiftPayload 当前的 DeleteWholeIf、ReplaceIfWithElseTail 等枚举可以继续描述结构化结果，但应把它理解为“已证明的结构结论”，而不是最终 rewrite 命令。最终动作仍由 IfStructureProposalRule 创建 DecisionUnit。

### 4. 给 DecisionUnit 增加显式编辑足迹

DecisionUnit 已有 fragments、relations、ConflictKey 和 MergeKey，但当前冲突过滤主要重新看 SyntaxNode span。建议增加概念上的 DecisionFootprint：

~~~text
DecisionFootprint
    AnchorNodeKey
    ConsumedNodeKeys
    ActionKind
    Composition = Independent | DominatesChildren | ComposesWithChildren | Exclusive
~~~

具体实现可以是字段，也可以是带结构标签的 CPG relation。关键不变量是：

- AnchorNodeKey 永远指向原始树中的编辑起点。
- ConsumedNodeKeys 由 Lift/Propose 根据证明显式列出。
- replacement node 只是 Replace 的结果 payload，不参与原树覆盖判断。
- DominatesChildren 只能由结构完整证明产生，不能由 span 自动推导。

推荐的统一处理顺序：

~~~text
DecisionUnit[]
    -> 按显式 footprint 构建冲突图
    -> 同 anchor 按动作策略收口
    -> ancestor/descendant 只按 Dominates/Composes 关系收口
    -> partial overlap 产生诊断并 fail closed
    -> 输出 RuleDecision[]
~~~

RuleDecisionEngine 应成为唯一语义 resolver。ApplicationService 中的 FilterNestedDeleteDecisions 应删除或改为只调用 resolver 输出；不能再保留第二套方向相反的 span 过滤。

### 5. 不要从语法树猜冲突域

BuildConflictGroupKey 中“遇到 logical host 就向上归并”的启发式适合临时兼容，但不适合长期作为动作语义。建议迁移为：

- Propose 明确设置 ConflictKey/footprint；
- 无显式 key 时只使用 anchor 自身；
- 结构候选通过 ConsumedNodeKeys 宣布它消费的逻辑宿主；
- Resolver 用显式关系决定是否同域，而不是从候选 anchor 的 descendants 反推。

这样 Replace(logical-host) 和 Delete(if) 可以在同一分析中同时存在，但只有外层具备结构支配证明时才合并。

### 6. 推荐的渐进实施顺序

1. 先增加 coverage/capability 和 footprint 的数据结构、诊断和观测，不改变当前决策结果。
2. 为 FlowLogicalExpression、ExpressionHost、IfStructure、LogicalReduction 分配明确能力，补充禁止跨能力消费的契约测试。
3. 收紧 IfStructureLifter：只有 StructureComplete 才能产出结构完成 payload；保留现有 if 删除测试的显式完整证明路径。
4. 让 LogicalExpressionProposalRule 和 IfStructureProposalRule 填充 footprint/consumed nodes。
5. 把 RuleDecisionEngine 的三套过滤合并成一个 resolver，移除 ApplicationService 的重复过滤。
6. 保留 Rewrite 层重叠检查，但将其视为不变量断言和防御性错误报告，不再让它决定业务语义。

## 当前测试契约与缺口

### 已有契约

- DecisionStructureValidationTests 期望逻辑 and 右侧目标在左侧传播后最终删除整个 IfStatement，见 tests/NLISSN.HostTests/Decision/DecisionStructureValidationTests.cs:40-55。
- GraphAnalyzerTests 期望 ready && s.IsReady 的 if、body 和目标成员都从重写结果中消失，见 tests/NLISSN.HostTests/Application/GraphAnalyzerTests.cs:274-288。
- DecisionComplexTests 覆盖完整逻辑 and 导致 if 删除、逻辑 or 局部 Replace，以及外层控制宿主压过内部 body 删除，见 tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs:12-63。
- RuleStructureContractTests 已验证只覆盖一个逻辑 sibling 时不是完整覆盖，见 tests/NLISSN.HostTests/Application/RuleStructureContractTests.cs:89-106。但它目前没有验证不同 FactKind 的 coverage 权限。
- PrototypeRewriter 已有父 Delete 覆盖子 Delete 的重叠测试，见 tests/NLISSN.HostTests/Application/PipelineComponentTests.cs:1506-1536。

### 必须新增的回归场景

1. 外层 Delete(if) 与内层 Replace(logical expression)，并明确检查最终保留的是哪一个以及为什么。
2. 外层 Replace(if) 与内层 Delete，验证结构 replacement 的 consumed nodes 是否抑制子决策。
3. 祖先和后代位于不同 conflict domain，但文本 span 嵌套，验证 resolver 不再只按 span 丢弃候选。
4. replacement node 不在原始 syntax tree、anchor 在原始树中的逻辑规约，验证 footprint 不读取 replacement span。
5. 同一个 logical host 同时产生 IfStructure completion 和 LogicalReduction，验证没有结构完整证明时不会自动 Delete if。
6. 部分重叠但互不包含的两个 Replace，验证返回诊断/Skip 而不是依赖 Rewrite 顺序。
7. 复现当前测试中的 ready && s.IsReady，并证明它通过显式 StructureComplete 能力而非“任意父 mark 即覆盖”继续通过。

### 已执行验证

以下定向测试已通过：

~~~text
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~DecisionComplexTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~RuleStructureContractTests"
30 passed

dotnet test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~Decision|FullyQualifiedName~Rewrite"
3 passed
~~~

这些测试证明当前契约，但还没有覆盖上面列出的“外层 if Delete + 内层 logical Replace”决策单元组合。

## GitHub 一手资料对比

| 项目 | 官方源码/文档 | 与本问题直接相关的做法 | 对 NLISSN 的启发 |
| --- | --- | --- | --- |
| Roslyn SyntaxEditor | [SyntaxEditor.cs](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/Portable/Editing/SyntaxEditor.cs) | 编辑通过 annotation 跟踪节点；父子节点同时编辑时需要明确顺序和跟踪关系，不能把普通 span 猜测当作节点身份 | 使用 stable anchor/annotation 和显式 parent-child edit 关系；决策层不要只按 span 过滤 |
| LLVM/Clang Replacement | [Replacement.cpp](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Core/Replacement.cpp) | 重叠 replacement 默认报告 overlap conflict；只有可证明兼容时才接受组合 | partial overlap 应 fail closed；兼容组合需要显式证明 |
| LLVM/Clang AtomicChange | [AtomicChange.cpp](https://github.com/llvm/llvm-project/blob/main/clang/lib/Tooling/Refactoring/AtomicChange.cpp) | 多个 replacement 作为一个原子变更聚合，冲突时整体失败 | 将一个结构决策及其 consumed edits 看作原子 footprint，而不是先生成散乱 edits 再猜赢家 |
| OpenRewrite | [TreeVisitor.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/TreeVisitor.java)、[Tree.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/Tree.java)、[Markers.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/Markers.java)、[SearchResult.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/marker/SearchResult.java) | Markers 是树上的元数据/搜索结果上下文，不等于 rewrite action；visitor 才返回树变换 | Mark 可以携带结构化证据和 provenance，但不要把 Marker/Mark 直接当成 Delete/Replace |
| Scalafix | [Patch.scala](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/patch/Patch.scala)、[PatchInternals.scala](https://github.com/scalacenter/scalafix/blob/main/scalafix-core/src/main/scala/scalafix/internal/patch/PatchInternals.scala) | replaceTree 不应与同节点或子节点 patch 随意组合；需要更精确的 token patch 或明确组合 | 一个 Replace 的作用域必须明确，不能同时假定它是 parent edit 和 child edit |
| Uber PolyglotPiranha | [README.md](https://github.com/uber/piranha/blob/master/README.md) | 使用 seed rule、rule graph 和 parent scope 传播来组织删除规则 | 可借鉴“seed/传播/父作用域”分离，但动作和 scope 仍应在最终规则执行层显式决定 |

这些项目的共同点不是“父 span 永远赢”，而是把事实、节点身份、编辑范围和冲突处理分开。最接近本任务的可复用模式是：

~~~text
facts/markers       -> 可追溯元数据
tree topology       -> 结构关系
patch/edit footprint -> 明确的编辑范围
resolver/editor     -> 冲突失败或有证明的组合
~~~

## 最终建议

1. 不把任何 Delete/Replace 决策规则移动到 Mark。
2. 允许 Mark 增加 typed capability，但禁止 TopologyHost 或普通 TargetCandidate 隐式升级为 StructureComplete。
3. 保留全局方法规则在 Mark 中做项目级证明，因为它们输出的是方法目标事实，不是改写动作。
4. 让 IfStructureLifter 负责完整性证明，让 LogicalExpressionLiftingRule 负责局部规约 payload。
5. 让 Propose 产生带 consumed nodes 和 edit footprint 的 DecisionUnit。
6. 让 RuleDecisionEngine 成为唯一的父子冲突 resolver；ApplicationService 和 Rewrite 只做调用与防御性校验。
7. 对没有显式支配关系的外层 Delete/内层 Replace 冲突 fail closed，不用 span containment 静默丢弃候选。
8. 先补充回归测试再收紧 coverage，尤其要把当前 ready && s.IsReady 的期望行为转成显式结构证明测试。

