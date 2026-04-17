# DDD 事件风暴：七阶段链路下的业务行为、领域事件与领域对象

## 1. 文档目的

本文档基于以下业务场景重做事件风暴：

- 类删除；
- 方法删除；
- 方法私有化；
- 方法体清空；
- 成员切片；
- 影子类生成；
- 最小运行闭包提取；
- 计划驱动改写；
- 证据驱动变更审计。

本文档统一收敛到新的领域主链路：

`加载 -> 分析 -> 标记 -> 传播 -> 决策 -> 执行 -> 输出`

本文档回答四个问题：

1. 七阶段链路中分别发生哪些业务行为；
2. 各阶段沉淀哪些领域事件；
3. 各阶段依赖哪些实体和值对象；
4. 九类场景如何映射到统一链路，而不是继续各写各的。

---

## 2. 基础术语

本文严格沿用现有 DDD 文档主语言：

- `RunRequest`
- `WorkspaceContext`
- `AnalysisSnapshot`
- `RuleTarget`
- `ChangeCandidate`
- `RewriteDecision`
- `RewritePlan`
- `RewriteResult`
- `VerificationEvidence`
- `RunReport`

新增但受控使用的场景型对象：

- `ShadowClass`
- `RuntimeClosure`

本文明确约束：

- 不再把 `TargetId` 当作跨层统一语言；
- 不再把“候选、传播、决策、计划、执行”混成一个重构器；
- 不再让证据仅以日志或测试散落存在；
- 不再让“计划”从问题域主链路里消失，它属于执行阶段内部，而不是被删除。

---

## 3. 七阶段主链路

## 3.1 链路定义

统一主链路如下：

1. **加载**：加载运行请求、工程边界、启用规则集和运行模式；
2. **分析**：构建程序事实、依赖关系、调用关系、数据流和切片基础；
3. **标记**：识别规则目标，生成候选对象，并标记候选理由；
4. **传播**：传播影响范围、覆盖关系、闭包关系和联动动作；
5. **决策**：生成允许项、保护项、冲突项和最终改写裁决；
6. **执行**：编译计划、排序计划、应用改写、记录结果；
7. **输出**：生成证据、风险、审计结果和运行报告。

## 3.2 七阶段的总事件

七阶段的阶段级事件如下：

1. `LoadStarted`
2. `AnalysisCompleted`
3. `MarkingCompleted`
4. `PropagationCompleted`
5. `DecisionCompleted`
6. `ExecutionCompleted`
7. `OutputCompleted`

这些是阶段完成事件，不替代更细颗粒度的业务事件。

---

## 4. 七阶段的业务行为、领域事件与领域对象

## 4.1 加载阶段

### 业务目标

把外部请求转化为后续领域流程可消费的输入边界。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|

| 加载工程边界 | 接收工程路径、项目列表、文档列表、引用信息和语言版本，装配成 `WorkspaceContext` | 后续所有阶段都需要统一的工作区边界；没有这一步，分析结果无法保证可复现
| 生成稳定工作区上下文 | 汇总前面所有加载结果，产出唯一可传播的 `WorkspaceContext` | 加载阶段最终要交付一个稳定输入真源，供分析、执行、输出统一消费 |

### 领域事件

- `WorkspacePrepared`
- `LoadStarted`

### 核心实体

- `WorkspaceContext`

### 核心值对象

- `WorkspaceContextId`
- `DocumentPath`

### 为什么这一阶段独立存在

因为如果加载阶段不独立，后续分析、执行、输出都会重复承担项目解析和边界确定责任，最终会把整个系统重新拖回“一个入口类包打天下”的旧形态。

---

## 4.2 分析阶段

### 业务目标

把工作区转化成可复用、可追溯、可查询的程序事实。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 构建程序事实快照 | 接收 `WorkspaceContext` 中的项目、文档、引用和编译上下文 | 后续所有规则都要建立在同一次分析快照上，否则候选、决策和证据会引用不同事实版本 |
| 生成结构与依赖事实 | 处理类型、方法、字段、调用、控制流、数据流、继承与实现关系 | 业务场景并不直接操作源码文本，而是建立在这些程序事实之上 |
| 建立查询索引 | 处理 `AnalysisSnapshot` 中的节点、边和事实映射关系 | 标记阶段不能直接遍历底层图结构，需要稳定查询入口 |
| 建立切片基础 | 处理数据流、控制相关边和切片起点能力 | 传播阶段要回答“会波及什么”，必须以分析阶段产出的切片基础为前提 |
| 校验分析结果完整性 | 检查事实是否缺失、索引是否可用、关键依赖是否已建立 | 如果分析阶段的事实不完整，后续候选和决策会建立在错误前提上 |
| 发布统一事实 | 输出 `AnalysisSnapshot` 以及配套事实值对象 | 分析阶段的最终责任不是“算完”，而是交付可被后续阶段统一消费的事实真源 |

### 领域事件

- `AnalysisSnapshotBuilt`
- `ProgramFactPublished`
- `CallGraphBuilt`
- `FlowGraphBuilt`
- `SliceCapabilityPrepared`
- `AnalysisCompleted`

### 核心实体

- `AnalysisSnapshot`

### 核心值对象

- `ProgramElementKey`
- `ProgramFact`
- `TypeFact`
- `CallFact`
- `FlowFact`
- `LocationRange`
- `MemberSignature`

### 为什么这一阶段独立存在

因为分析阶段负责回答“代码里客观存在什么”，而不是“应该改什么”。只要这一步与标记、决策混写，后面的规则就会直接耦合底层图结构，领域语言会再次失控。

---

## 4.3 标记阶段

### 业务目标

把程序事实转换成业务上值得继续处理的目标和候选。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 识别规则目标 | 接收 `AnalysisSnapshot` 中的类型、方法、调用、切片结果等事实 | 不是所有事实都值得进入变更流程，必须先筛出业务关注对象 |
| 按启用规则执行标记 | 接收加载阶段选定的规则集，并在 `AnalysisSnapshot` 上按规则逐个或并行命中目标 | 标记阶段不是决定“启用什么规则”，而是决定“已启用规则如何在当前事实集上命中对象” |
| 对目标打业务标记 | 处理识别出的目标，标记其场景语义，如“可删除类”“可私有化方法”“闭包根候选” | 后续传播和决策要知道这个目标为什么被关注，否则候选会失去上下文 |
| 生成变更候选 | 接收 `RuleTarget`，产出 `ChangeCandidate` | 标记阶段需要把“被关注对象”转换成“可能发生的变更对象”，否则无法进入决策 |
| 为候选附加理由 | 记录命中规则、命中路径、依赖依据、切片依据等 `CandidateReason` | 如果不记录理由，后续决策和证据就只能依赖临时推断，无法审计 |
| 为候选附加规则来源与场景来源 | 标注该候选来自哪个启用规则，以及属于类删除、方法删除、影子类生成还是闭包提取场景 | 同一个程序元素可能被多个规则同时命中，必须同时保留规则来源和场景来源 |


### 领域事件

- `RuleTargetIdentified`
- `ChangeCandidateGenerated`
- `CandidateReasonRecorded`
- `ScenarioTagAttached`
- `MarkingCompleted`

### 核心实体

- `RuleTarget`
- `ChangeCandidate`

### 核心值对象

- `RuleTargetKey`
- `CandidateReason`
- `CandidateKind`
- `ScenarioTag`

### 为什么这一阶段独立存在

因为“命中规则”不等于“允许执行”。标记阶段只负责把程序事实翻译成业务候选，不能提前扮演决策者。

补充约束：

- **加载阶段负责确定启用哪些规则**，例如启用类删除规则、方法删除规则、影子类生成规则、闭包提取规则，或启用它们的组合；
- **标记阶段负责运行这些已启用规则**，可以按顺序运行，也可以并行运行，但不能在标记阶段临时改变规则启停；
- **同一个目标可以被多个规则同时命中**，因此 `ChangeCandidate` 必须保留规则来源，而不能只保留单一场景名。

---

## 4.4 传播阶段

### 业务目标

把单点候选扩展成受影响范围、联动目标、覆盖关系和闭包范围。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 执行成员切片 | 接收候选目标、切片起点、切片方向和边界规则 | 传播阶段首先要知道从哪里开始扩散影响范围 |
| 计算调用传播和依赖传播 | 处理调用图、依赖图、数据流和控制相关路径 | 删除类或删除方法通常不是单点动作，必须知道改动会向哪里扩散 |
| 识别受影响成员 | 处理传播结果中的类型、方法、字段、调用者和被调用者 | 后续决策需要知道哪些对象会被联动影响，不能只看原始目标 |
| 识别覆盖关系 | 处理父对象与子对象的改动覆盖关系，例如类删除覆盖方法删除 | 如果不提前识别覆盖，执行阶段会出现重复动作和冲突动作 |
| 识别联动动作 | 处理由传播引出的次级动作，例如删除调用点、同步调整可见性、补影子引用 | 有些改动不能只做主动作，传播阶段要提前发现联动动作 |
| 形成传播轨迹 | 输出 `PropagationTrace`，记录影响是如何从起点扩散出来的 | 决策和证据都需要“为什么会波及这里”的链条说明 |
| 形成运行闭包或影子边界 | 处理闭包根、必要成员集合、影子类保留范围和引用映射边界 | 闭包提取和影子类生成本质上都依赖传播边界，不应作为孤立脚本处理 |

### 领域事件

- `MemberSliceComputed`
- `DependencyPropagationStarted`
- `ImpactRangeDetected`
- `CandidateCoveredByParentAction`
- `LinkedActionDetected`
- `RuntimeClosureBoundaryDetected`
- `ShadowBoundaryDetected`
- `PropagationCompleted`

### 核心实体

- `RuntimeClosure`
- `ShadowClass`

### 核心值对象

- `SliceDirection`
- `SliceBoundary`
- `PropagationTrace`
- `ReferenceMapping`
- `ClosureRoot`
- `ClosureIntegrityStatus`

### 为什么这一阶段独立存在

因为传播阶段回答的是“如果改这个，还会波及什么”。它既不是原始事实，也不是最终裁决；它是从局部目标扩展到系统影响范围的专门阶段。

---

## 4.5 决策阶段

### 业务目标

把候选和传播结果裁决成允许执行、拒绝执行、受保护或发生冲突的正式决定。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 检测保护条件 | 接收候选、传播轨迹、契约信息、外部调用信息和风险提示 | 有些对象虽然被命中，但因为契约、外部访问或闭包完整性原因不能改 |
| 检测候选冲突 | 处理多个候选之间、多个动作之间、父子覆盖之间的冲突 | 如果不显式识别冲突，执行阶段会被迫临时裁决，模型边界会被破坏 |
| 合并传播结果 | 整合切片、依赖传播、联动动作和覆盖关系 | 决策不是看单个候选，而是看候选在整体传播图中的位置 |
| 生成允许项 | 产出被批准进入执行阶段的候选集合 | 执行阶段只应消费已批准对象，不能自己再判定 |
| 生成拒绝项 | 产出被明确拒绝的候选集合 | 被拒绝对象也必须被保留，因为报告和证据需要解释“为什么没改” |
| 生成保护项 | 产出 `DecisionProtection`，明确哪些业务规则阻止了改写 | 保护条件是领域知识，不能只藏在 `if` 语句里 |
| 生成冲突项 | 产出 `DecisionConflict`，明确哪些候选或动作相互冲突 | 冲突必须结构化表达，便于后续计划和审计消费 |
| 形成最终裁决 | 汇总允许、拒绝、保护、冲突，产出 `RewriteDecision` | 决策阶段最终要交付唯一裁决真源，供执行和输出统一引用 |

### 领域事件

- `DecisionProtectionApplied`
- `DecisionConflictDetected`
- `CandidateRejectedByProtection`
- `CandidateApproved`
- `DecisionPropagationMerged`
- `RewriteDecisionMade`
- `DecisionCompleted`

### 核心实体

- `RewriteDecision`

### 核心值对象

- `DecisionProtection`
- `DecisionConflict`
- `ApprovalReason`
- `RejectionReason`
- `ConfidenceLevel`

### 为什么这一阶段独立存在

因为最终裁决必须成为独立真源。否则计划编译、执行和证据系统都会重新做一遍“到底能不能改”的判断，系统必然再次分裂。

---

## 4.6 执行阶段

### 业务目标

把裁决结果转化为有序计划并实际施加到代码上。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 汇总决策结果 | 接收 `RewriteDecision` 中的允许项、保护项和冲突项 | 执行阶段的起点不是候选，而是已经做完业务裁决的正式结果 |
| 编译改写计划 | 把允许项编译为 `RewritePlan` 与 `PlanChangeItem` | 执行器不能直接消费决策集合，必须先变成可排序、可定位、可回放的计划 |
| 检测计划冲突 | 处理计划级别的动作冲突、定位冲突和覆盖冲突 | 有些冲突在决策层看不出来，只有落成计划才会暴露 |
| 解析覆盖关系 | 处理计划项之间的父子覆盖、重复动作和互斥动作 | 这一步是为了防止执行器重复改写同一目标 |
| 排序计划动作 | 根据目标范围、文件顺序、动作依赖和覆盖关系排序 | 执行顺序会影响最终改写结果，必须显式建模 |
| 定位目标代码 | 接收 `PlanTarget` 中的文件、成员、范围和校验文本 | 计划必须能准确落到真实代码位置，否则执行没有可操作对象 |
| 应用改写动作 | 对定位到的目标应用删除、私有化、清空、生成影子类等 `PlanAction` | 这是执行阶段的核心业务价值：把领域裁决真正变成代码改动 |
| 记录文件变更 | 记录每个文件的改动摘要、差异和受影响目标 | 后续证据和报告都需要知道真实发生了哪些改动 |
| 记录执行轨迹 | 记录每个计划项的执行路径、定位过程和应用结果 | 出现错误时必须能回放执行过程，而不是只拿到一个失败字符串 |
| 记录执行失败 | 记录失败目标、失败类型、失败原因和可重试性 | 执行失败是正式领域结果，不是日志噪音 |
| 形成执行结果 | 汇总成功项、失败项和文件改动，产出 `RewriteResult` | 执行阶段最终要交付一个可被证据和报告消费的结果真源 |

### 领域事件

- `RewritePlanCompiled`
- `PlanConflictDetected`
- `PlanCoverageResolved`
- `PlanActionOrdered`
- `RewritePlanPublished`
- `RewritePlanExecuted`
- `FileChangeProduced`
- `ExecutionTraceRecorded`
- `ExecutionFailureRecorded`
- `RewriteResultProduced`
- `ExecutionCompleted`

### 核心实体

- `RewritePlan`
- `PlanChangeItem`
- `RewriteResult`

### 核心值对象

- `PlanMetadata`
- `PlanTarget`
- `PlanAction`
- `PlanReason`
- `PlanConflict`
- `FileChange`
- `ExecutionTrace`
- `ExecutionFailure`
- `ExecutionSummary`

### 为什么这一阶段独立存在

因为执行阶段是唯一可以改动代码文本的阶段。计划编译虽然是执行前半段，但它的职责仍然属于“让裁决变成可执行动作”，所以不应再单独抽成与执行平级的主链路阶段。

---

## 4.7 输出阶段

### 业务目标

把执行结果变成可信证据和可消费输出。

### 核心业务行为

| 业务行为 | 接收什么 / 处理什么 | 为什么要做 |
|---|---|---|
| 汇总分析事实 | 接收 `AnalysisSnapshot` 及其中的事实、依赖和切片依据 | 证据必须能回到原始程序事实，否则审查者无法确认结论基础 |
| 汇总裁决理由 | 接收 `RewriteDecision` 中的批准、拒绝、保护和冲突理由 | 输出阶段不能只展示“改了什么”，还必须解释“为什么这样裁决” |
| 汇总计划和执行结果 | 接收 `RewritePlan`、`RewriteResult`、执行轨迹和文件变更 | 证据和报告必须连接“打算怎么改”和“实际上怎么改了” |
| 生成编译证据 | 处理编译结果、诊断信息和编译前后差异 | 自动变更首先要证明没有明显破坏编译正确性 |
| 生成静态推理证据 | 处理传播轨迹、切片结果、保护条件和依赖依据 | 审计需要知道领域判断不是拍脑袋，而是有静态依据 |
| 生成行为证据 | 处理测试结果、样例运行、闭包完整性或影子类验证信息 | 有些场景仅靠编译通过还不够，需要行为层证明 |
| 汇总风险 | 整合残余风险、未解决冲突、弱证据结论和人工确认点 | 输出阶段必须诚实表达不确定性，不能伪装成完全安全 |
| 生成变更审计结果 | 汇总证据链并形成审计结论，如允许执行、允许合并、仅供参考 | 平台最终要服务真实工程审查，而不是只吐一堆内部对象 |
| 生成运行报告 | 面向用户、CI 和审查者生成 `RunReport` | 领域流程最终需要一个人类可消费出口，否则证据无法真正被使用 |

### 领域事件

- `CompilationEvidenceGenerated`
- `StaticReasoningEvidenceGenerated`
- `BehaviorEvidenceGenerated`
- `RiskSummaryGenerated`
- `VerificationEvidenceCollected`
- `ChangeAuditReported`
- `RunReported`
- `OutputCompleted`

### 核心实体

- `VerificationEvidence`
- `RunReport`

### 核心值对象

- `CompilationEvidence`
- `StaticReasoningEvidence`
- `BehaviorEvidence`
- `RiskSummary`
- `ReportSummary`
- `AuditConclusion`

### 为什么这一阶段独立存在

因为“改完了”不等于“可信”。输出阶段要把事实、裁决、计划和结果重新组织成证据链，否则所有自动变更最终都只能停留在脚本层面，无法进入工程审查流程。

---

## 5. 九类场景如何映射到七阶段

## 5.1 类删除

- **加载**：加载目标工程和类删除规则；
- **分析**：分析类定义、继承、引用和调用关系；
- **标记**：把类标记为删除候选；
- **传播**：传播到成员、引用者和闭包影响范围；
- **决策**：判断是否允许删除类及其覆盖成员；
- **执行**：编译类删除计划并执行；
- **输出**：给出删除证据、风险和审计结果。

### 细粒度事件

- `ClassDeletionRequested`
- `ClassTargetResolved`
- `ClassDependencyAnalyzed`
- `ClassDeletionCandidateGenerated`
- `CoveredMemberCandidateGenerated`
- `ClassDeletionProtected`
- `ClassDeletionApproved`
- `ClassDeletionRejected`
- `ClassDeletionExecuted`
- `ClassDeletionVerified`

## 5.2 方法删除

- **加载**：加载方法删除规则；
- **分析**：分析方法签名、调用图、覆盖关系；
- **标记**：标记方法删除候选；
- **传播**：传播到调用方、实现链和闭包；
- **决策**：判断是否允许删除、是否需联动处理；
- **执行**：编译方法删除计划并执行；
- **输出**：给出删除影响证据和风险说明。

### 细粒度事件

- `MethodDeletionRequested`
- `MethodTargetResolved`
- `MethodCallGraphAnalyzed`
- `MethodOverrideRelationAnalyzed`
- `MethodDeletionCandidateGenerated`
- `MethodCallerImpactDetected`
- `MethodDeletionProtected`
- `MethodDeletionApproved`
- `MethodDeletionRejected`
- `MethodDeletionExecuted`
- `MethodDeletionVerified`

## 5.3 方法私有化

- **加载**：加载私有化规则；
- **分析**：分析可见性和外部调用者；
- **标记**：标记私有化候选；
- **传播**：传播外部访问影响；
- **决策**：判断是否允许私有化；
- **执行**：改写可见性；
- **输出**：生成外部访问消除证据。

### 细粒度事件

- `MethodPrivatizationRequested`
- `MethodVisibilityAnalyzed`
- `ExternalCallerDetected`
- `MethodPrivatizationCandidateGenerated`
- `MethodPrivatizationProtected`
- `MethodPrivatizationApproved`
- `MethodPrivatizationRejected`
- `MethodPrivatizationExecuted`
- `MethodPrivatizationVerified`

## 5.4 方法体清空

- **加载**：加载清空策略；
- **分析**：分析返回约束和副作用；
- **标记**：标记清空候选；
- **传播**：传播行为和调用影响；
- **决策**：判断清空是否优于删除；
- **执行**：改写方法体；
- **输出**：生成行为变化证据。

### 细粒度事件

- `MethodBodyClearingRequested`
- `MethodReturnContractAnalyzed`
- `MethodSideEffectAnalyzed`
- `MethodBodyClearingCandidateGenerated`
- `MethodBodyClearingProtected`
- `MethodBodyClearingApproved`
- `MethodBodyClearingRejected`
- `MethodBodyClearingExecuted`
- `MethodBodyClearingVerified`

## 5.5 成员切片

- **加载**：加载切片目标和方向；
- **分析**：准备数据流和控制相关事实；
- **标记**：标记切片起点；
- **传播**：执行切片传播，形成边界；
- **决策**：判断切片结果用于保护、删除还是闭包；
- **执行**：通常不直接改写，但可为后续执行阶段提供输入；
- **输出**：输出切片结果和解释。

### 细粒度事件

- `MemberSliceRequested`
- `SliceStartResolved`
- `SliceDirectionSelected`
- `MemberSliceComputed`
- `SliceBoundaryDetected`
- `SliceResultPublished`

## 5.6 影子类生成

- **加载**：加载影子生成策略；
- **分析**：分析源类型和需要保留的成员；
- **标记**：标记影子化目标；
- **传播**：传播引用映射和依赖边界；
- **决策**：决定影子类保留范围；
- **执行**：生成影子类及其写入计划；
- **输出**：给出影子类生成证据。

### 细粒度事件

- `ShadowClassGenerationRequested`
- `SourceTypeResolved`
- `ShadowMemberSetSelected`
- `ShadowReferenceMappingPrepared`
- `ShadowClassDrafted`
- `ShadowClassGenerated`
- `ShadowClassVerified`

## 5.7 最小运行闭包提取

- **加载**：加载闭包根和提取策略；
- **分析**：分析闭包相关依赖；
- **标记**：标记候选成员；
- **传播**：传播必要依赖和边界；
- **决策**：决定纳入闭包的最小成员集合；
- **执行**：导出闭包结果或闭包计划；
- **输出**：输出闭包完整性与风险证据。

### 细粒度事件

- `MinimalRuntimeClosureRequested`
- `ClosureRootResolved`
- `DependencyPropagationStarted`
- `RequiredMemberIdentified`
- `RedundantMemberExcluded`
- `MinimalRuntimeClosureFormed`
- `ClosureIntegrityChecked`
- `MinimalRuntimeClosureExtracted`
- `MinimalRuntimeClosureVerified`

## 5.8 计划驱动改写

- **加载**：加载要执行的规则和策略；
- **分析**：分析计划所需上下文；
- **标记**：标记计划相关候选；
- **传播**：传播覆盖和联动关系；
- **决策**：决定正式改写项；
- **执行**：编译计划、排序计划、执行计划；
- **输出**：输出计划执行结果和轨迹。

### 细粒度事件

- `RewriteDecisionCollected`
- `PlanConflictDetected`
- `PlanCoverageResolved`
- `PlanActionOrdered`
- `RewritePlanCompiled`
- `RewritePlanDispatched`
- `RewritePlanExecuted`
- `ExecutionTraceRecorded`
- `RewriteResultProduced`

## 5.9 证据驱动变更审计

- **加载**：加载证据策略和审计标准；
- **分析**：分析已有事实、裁决和结果；
- **标记**：标记需要被证明的关键结论；
- **传播**：传播结论所依赖的证据链；
- **决策**：决定证据是否充分、风险是否可接受；
- **执行**：执行证据收集、聚合和审计编译；
- **输出**：输出审计结论和运行报告。

### 细粒度事件

- `EvidenceCollectionRequested`
- `DecisionReasonAggregated`
- `PlanExecutionEvidenceAggregated`
- `CompilationEvidenceGenerated`
- `StaticReasoningEvidenceGenerated`
- `BehaviorEvidenceGenerated`
- `RiskSummaryGenerated`
- `VerificationEvidenceCollected`
- `ChangeAuditReported`

---

## 6. 七阶段下的统一实体与值对象

## 6.1 实体清单

| 实体 | 所属阶段 | 为什么是实体 |
|---|---|---|
| `WorkspaceContext` | 加载 | 具有稳定工作区身份，会贯穿后续流程 |
| `AnalysisSnapshot` | 分析 | 代表一次分析快照，有独立身份和生命周期 |
| `RuleTarget` | 标记 | 目标需要被候选、决策、计划反复引用 |
| `ChangeCandidate` | 标记 | 候选会经历生成、传播、拒绝、批准等状态变化 |
| `RuntimeClosure` | 传播 | 闭包结果可被验证、导出和比较 |
| `ShadowClass` | 传播 | 影子类具有独立结构和引用映射 |
| `RewriteDecision` | 决策 | 裁决结果是后续计划和证据的独立真源 |
| `RewritePlan` | 执行 | 计划有自己的身份、版本和计划项集合 |
| `PlanChangeItem` | 执行 | 单个改写项需要被单独追踪 |
| `RewriteResult` | 执行 | 执行结果是事实对象，不等于计划本身 |
| `VerificationEvidence` | 输出 | 证据会被持续聚合和审查 |
| `RunReport` | 输出 | 报告是独立输出对象，聚合多源信息 |

## 6.2 值对象清单

| 值对象 | 所属阶段 | 作用 |
|---|---|---|
| `RunRequest` | 加载 | 描述一次运行请求 |
| `InputDescriptor` | 加载 | 描述输入来源 |
| `ProgramElementKey` | 分析 | 稳定标识程序元素 |
| `LocationRange` | 分析 | 表达代码位置 |
| `MemberSignature` | 分析 | 表达成员签名 |
| `RuleTargetKey` | 标记 | 标识规则目标 |
| `CandidateReason` | 标记 | 解释为什么被标记 |
| `ScenarioTag` | 标记 | 标识场景来源 |
| `SliceDirection` | 传播 | 控制切片方向 |
| `SliceBoundary` | 传播 | 表达传播边界 |
| `PropagationTrace` | 传播 | 表达传播链 |
| `ReferenceMapping` | 传播 | 表达原引用到影子引用映射 |
| `ClosureRoot` | 传播 | 表达闭包根 |
| `DecisionProtection` | 决策 | 表达保护条件 |
| `DecisionConflict` | 决策 | 表达冲突内容 |
| `ApprovalReason` | 决策 | 解释为什么批准 |
| `RejectionReason` | 决策 | 解释为什么拒绝 |
| `PlanMetadata` | 执行 | 记录计划元数据 |
| `PlanTarget` | 执行 | 表达执行目标 |
| `PlanAction` | 执行 | 表达执行动作 |
| `PlanReason` | 执行 | 解释计划项来源 |
| `PlanConflict` | 执行 | 表达计划冲突 |
| `FileChange` | 执行 | 表达单文件改写结果 |
| `ExecutionTrace` | 执行 | 表达执行轨迹 |
| `ExecutionFailure` | 执行 | 表达失败原因 |
| `CompilationEvidence` | 输出 | 表达编译证据 |
| `StaticReasoningEvidence` | 输出 | 表达静态推理证据 |
| `BehaviorEvidence` | 输出 | 表达行为证据 |
| `RiskSummary` | 输出 | 表达风险汇总 |
| `ReportSummary` | 输出 | 表达报告摘要 |
| `AuditConclusion` | 输出 | 表达审计结论 |

---

## 7. 七阶段下的关系约束

```text
RunRequest
  -> WorkspaceContext
  -> AnalysisSnapshot
  -> RuleTarget
  -> ChangeCandidate
  -> RuntimeClosure / ShadowClass / PropagationTrace
  -> RewriteDecision
  -> RewritePlan
  -> RewriteResult
  -> VerificationEvidence
  -> RunReport
```

约束如下：

- 加载阶段不决定候选；
- 分析阶段不决定改写；
- 标记阶段不代替裁决；
- 传播阶段不直接写文件；
- 决策阶段不直接操作文本；
- 执行阶段不重新决定业务规则；
- 输出阶段不反向篡改决策和计划。

---

## 8. 关键结论

1. 九类场景不是九套独立流程，而是统一七阶段链路上的不同事件组合；
2. `标记` 与 `决策` 必须分开，否则系统会再次把“命中”误写成“允许执行”；
3. `传播` 必须成为独立阶段，否则切片、闭包、覆盖和联动会被错误塞进分析或决策；
4. `执行` 阶段内部可以包含计划生成、动作排序和改写落地，但对外主链路统一称为 `执行`；
5. `输出` 不是简单报告生成，而是证据、风险、审计和报告的完整出口；
6. `RuntimeClosure` 与 `ShadowClass` 必须视为正式领域实体，不能继续藏在脚本和临时结果里。

---

## 9. 对后续设计的约束

后续如果继续写聚合设计、命令模型、接口设计或代码目录，必须遵守：

- 任何设计都要能明确归属到七阶段之一；
- 不允许再恢复旧九阶段主链路命名；
- 不允许把 `计划` 重新提升为与 `执行` 平级的主链路阶段；
- 不允许把 `传播` 简化成分析阶段附属动作；
- 不允许让执行器补做决策；
- 不允许让证据退化成日志输出；
- 不允许跨阶段继续复用 `TargetId` 这种模糊名称。

---

## 10. 应用资料

### 本地 DDD 文档

- `docs/DDD/DDD问题域与业务愿景.md`
- `docs/DDD/DDD战略设计.md`
- `docs/DDD/DDD战术设计.md`

### 本地历史代码

- `git show 28b63fe:RewriteCodeExpressions/ClassRefactorer.cs`
- `git show 4e15788:RewriteCodeExpressions/MethodRefactorer.cs`
- `git show 4e15788:Analysis/AdvancedCodeAnalyzer.cs`
- `git show 9fd0f68:dome/src/Core/Models.cs`
- `git show 9fd0f68:dome/src/Plan/AuditPlanCompiler.cs`
- `git show 9fd0f68:dome/src/Rewrite/Roslyn/RoslynRewriteExecutor.cs`

### 当前实现参考

- `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
- `src/Analysis/Core/CpgGraph.cs`
- `src/Analysis/Slicing/SliceResult.cs`
