# 2026-04-20 DDD 17 文件方法级改造计划

## 1. 先确定所需知识面

基于当前仓库问题，不是先改代码，而是先补齐以下知识面并用外部资料 + 本地对标项目验证：

1. **聚合边界与不变量设计**
   - 聚合根必须维护一致性，而不是只承载集合。
   - 参考：Microsoft Learn / ABP Entities / modular-monolith-with-ddd 的聚合写法。
2. **领域事件触发位置**
   - 事件应优先由聚合在状态变化时原生记录；应用/Logic 只负责转发、补充兼容序列。
3. **富领域模型 vs 事实快照/只读投影**
   - 不是所有 Domain 类都必须富化；若本质是快照/报告，应明确降级为记录模型，而不是伪聚合。
4. **Domain / Logic / Application 职责切分**
   - “这是什么”放 Domain；“这一步怎么协作完成”放 Logic；“用例怎么对外编排”放 Application。
5. **工作流总装配器瘦身策略**
   - Workflow 不能长期承担核心规则装配，否则 DDD 分数永远上不去。
6. **测试优先的改造顺序**
   - 先锁行为，再搬规则，再收回事件归属。

## 2. 外部资料与本地资料

### 外部资料
- Microsoft Learn: Designing a microservice domain model  
  https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model
- Microsoft Learn: Domain events: Design and implementation  
  https://learn.microsoft.com/ro-ro/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation
- Microsoft Learn: Domain model layer validations  
  https://learn.microsoft.com/cs-cz/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-model-layer-validations
- ABP Docs: Entities / AggregateRoot  
  https://abp.io/docs/latest/framework/architecture/domain-driven-design/entities
- ABP Architecture / DDD  
  https://abp.io/architecture/domain-driven-design

### 本地资料
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev`
- `docs/DDD/领域模型设计Checklist.md`
- `docs/DDD/src实际设计与事件风暴理想设计差异.md`

## 3. 总体改造原则

1. 先判断对象到底是：
   - 强聚合根
   - 实体/值对象
   - 事实快照
   - 输出/报告投影
2. 若是强聚合根：必须补齐不变量方法、终态方法、原生领域事件。
3. 若是快照/报告：不要强行富化成聚合，必要时保留贫血并明确说明。
4. Logic 的 `*EventSequenceBuilder` 只能做：
   - 兼容补齐
   - 聚合原生事件去重
   - 运行链排序
   不能继续主导业务事实产生。
5. Workflow 总装配器逐步瘦身，最终只保留跨聚合协作。

---

## 4. 17 文件方法级必须修改清单

> 标记说明：
> - **删**：删除原方法或停用其主责
> - **挪**：把规则/行为迁移到别处
> - **增**：新增方法
> - **改**：保留方法名但调整语义

### 4.1 `src/Domain/Analysis/AnalysisCpgSnapshot.cs`

**现状问题**：是容器型聚合，只有 `AddNode/AddFlow/AddCall`。

**必须改**
- **改** `AddNode(MinimumNode node)`
  - 增加“快照冻结后不可再写入”校验。
- **改** `AddFlow(CpgFlow flow)`
  - 增加重复边/非法边校验。
- **改** `AddCall(CpgCall call)`
  - 增加重复调用/非法调用校验。
- **增** `Complete(Guid correlationId)`
  - 标记快照完成，记录 `AnalysisSnapshotBuiltDomainEvent`。
- **增** `PublishFacts(Guid correlationId)`
  - 基于当前节点/流/调用数量记录 `ProgramFactPublishedDomainEvent`。
- **增** `EnsureMutable()`
- **增** `ValidateReadyToComplete()`
- **增** `HasPublishedFacts()`

**建议挪出/删除**
- **挪** 分析完成时机判断，不能继续主要由 `AnalysisEventSequenceBuilder` 决定。

---

### 4.2 `src/Domain/Analysis/AnalysisCompositeLayerSnapshot.cs`

**现状问题**：组合层快照也是容器。

**必须改**
- **改** `AddLayer(string layerName)`
  - 增加标准化和冻结校验。
- **改** `AddNode(MinimumNode node)`
  - 增加冻结校验。
- **增** `Complete(Guid correlationId)`
  - 记录 `AnalysisSnapshotBuiltDomainEvent`。
- **增** `PublishFacts(Guid correlationId)`
  - 记录 `ProgramFactPublishedDomainEvent`。
- **增** `EnsureMutable()`
- **增** `ValidateReadyToComplete()`

**建议挪出/删除**
- **挪** 事件生成主责，从 `AnalysisEventSequenceBuilder` 下沉到该聚合。

---

### 4.3 `src/Domain/Marking/RuleTarget.cs`

**现状问题**：更像命中记录。

**必须改**
- **改** `Create(...)`
  - 创建时立即建立“命中已识别”事实，支持记录原生事件。
- **改** `Revise(...)`
  - 限制修订条件：已进入候选阶段后不可随意覆盖。
- **增** `Confirm(Guid correlationId)`
  - 记录 `RuleTargetIdentifiedDomainEvent`。
- **增** `AttachNote(string? note)`
- **增** `EscalateCandidateReason(CandidateReason nextReason)`
- **增** `Lock()` / `EnsureUnlocked()`

**建议挪出/删除**
- **挪** `RuleTargetIdentifiedDomainEvent` 主生产权，从 `MarkingEventSequenceBuilder` 移回此聚合。

---

### 4.4 `src/Domain/Propagation/ChangeCandidate.cs`

**现状问题**：已比其他聚合强，但仍主要是记录集合。

**必须改**
- **改** `AddReason(CandidateReason candidateReason)`
  - 明确理由升级规则，避免无序重复堆积。
- **改** `AddScenarioTag(ScenarioTag scenarioTag)`
  - 增加场景兼容性校验。
- **改** `SetSliceBoundary(SliceBoundary sliceBoundary)`
  - 保持现有不变量，并在首次确认时可触发边界事件。
- **改** `AddPropagationTrace(PropagationTrace propagationTrace)`
  - 增加顺序/覆盖语义校验。
- **改** `RegisterPropagation(TargetName targetName, string reason, int stepOrder)`
  - 登记传播同时支持原生 `ImpactRangeDetected` / `LinkedActionDetected` 依据。
- **改** `MarkCoveredByParentAction(Guid parentCandidateId)`
  - 记录 `CandidateCoveredByParentActionDomainEvent`。
- **增** `ConfirmGenerated(Guid correlationId)`
  - 记录 `ChangeCandidateGeneratedDomainEvent`。
- **增** `DetectImpactRange(Guid correlationId)`
- **增** `DetectRuntimeClosureBoundary(Guid correlationId)`
- **增** `DetectShadowBoundary(Guid correlationId)`
- **增** `RegisterLinkedAction(string actionName, string reason, Guid correlationId)`

**建议挪出/删除**
- **挪** `ImpactRangeDetected/LinkedActionDetected/BoundaryDetected` 主生产权，从 `PropagationEventSequenceBuilder` 下沉。

---

### 4.5 `src/Domain/Execution/RewriteResult.cs`

**现状问题**：结果容器化严重。

**必须改**
- **改** `AddFileChange(FileChange fileChange)`
  - 在成功执行阶段才能写入。
- **改** `AddExecutionTrace(ExecutionTrace executionTrace)`
  - 增加阶段顺序约束。
- **改** `AddExecutionFailure(ExecutionFailure executionFailure)`
  - 转成终态驱动，而不是纯列表追加。
- **增** `StartExecution(Guid correlationId)`
- **增** `CompleteExecution(Guid correlationId)`
  - 记录 `ExecutionCompletedDomainEvent`。
- **增** `FailExecution(Guid planChangeItemId, string failureType, string message, bool retryable)`
- **增** `MarkFileChanged(DocumentPath path, string summary, IReadOnlyCollection<string> affectedTargets)`
- **增** `HasTerminalFailure()`
- **增** `EnsureExecutionOpen()`

**建议挪出/删除**
- **挪** 执行完成事件主责，从 workflow 组装器转回此聚合。

---

### 4.6 `src/Domain/Output/Audit/RunReport.cs`

**现状问题**：偏报告装配对象。

**必须改**
- **改** `Create(...)`
  - 明确只负责基础构造，不承担全部装配语义。
- **改** `CreateFromExecutionOutcome(...)`
  - 若保留，则只负责领域层总结，不允许越过审计边界。
- **改** `AttachVerificationEvidence(Guid verificationEvidenceId)`
  - 继续保留单次挂接不变量。
- **改** `ReviseSummary(...)`
  - 限制只有同一审计链条允许修订。
- **增** `Finalize(Guid correlationId)`
  - 记录 `RunReportGeneratedDomainEvent`。
- **增** `RecalculateFromEvidence(RewriteResult rewriteResult, VerificationEvidence verificationEvidence)`

**建议挪出/删除**
- **挪** 报告最终生成事件主责，从 workflow builder 回收至聚合。

---

### 4.7 `src/Logic/Analysis/Events/AnalysisEventSequenceBuilder.cs`

**现状问题**：在 Logic 中主导分析事件生成。

**必须改**
- **改** `BuildEvents(AnalysisDomainEventPublishInput input)`
  - 改成“优先读取 `AnalysisCpgSnapshot/AnalysisCompositeLayerSnapshot` 的原生事件；无则 fallback”。
- **删/停用主责** 当前直接 `new AnalysisSnapshotBuiltDomainEvent(...)`
- **删/停用主责** 当前直接 `new ProgramFactPublishedDomainEvent(...)`
- **增** `AppendMissingFallbackEvents(...)`
- **增** `CollectAggregateEvents(...)`

---

### 4.8 `src/Logic/Marking/Events/MarkingEventSequenceBuilder.cs`

**必须改**
- **改** `BuildEvents(...)`
  - 优先取 `RuleTarget` / `ChangeCandidate` 聚合原生事件。
- **删/停用主责** 当前直接生成 `RuleTargetIdentifiedDomainEvent`
- **删/停用主责** 当前直接生成 `ChangeCandidateGeneratedDomainEvent`
- **增** `CollectAggregateEvents(...)`
- **增** `AppendMissingFallbackEvents(...)`

---

### 4.9 `src/Logic/Propagation/Events/PropagationEventSequenceBuilder.cs`

**必须改**
- **改** `BuildEvents(...)`
  - 优先取 `ChangeCandidate` 原生事件。
- **删/停用主责** 当前直接生成：
  - `ImpactRangeDetectedDomainEvent`
  - `CandidateCoveredByParentActionDomainEvent`
  - `LinkedActionDetectedDomainEvent`
  - `RuntimeClosureBoundaryDetectedDomainEvent`
  - `ShadowBoundaryDetectedDomainEvent`
- **增** `CollectAggregateEvents(...)`
- **增** `AppendMissingFallbackEvents(...)`

---

### 4.10 `src/Logic/Workflow/RewritePlanCompiler.cs`

**必须改**
- **改** `Compile(RewritePlanCompilationInput input)`
  - 从“主编译器”降为“调用领域计划构造”的薄适配器。
- **挪** 冲突归并、排序连续性判断，优先回到 `RewritePlan`。
- **增** `BuildPlanTarget(...)`
  - 仅保留输入映射。
- **删/停用主责** 直接决定 `PlanReason` 的部分，改为聚合或领域服务判断。

---

### 4.11 `src/Logic/Workflow/RewritePlanExecutor.cs`

**必须改**
- **改** `Execute(RewritePlanExecutionInput input)`
  - 执行器只调用改写能力，并把结果回写给 `RewriteResult` 方法。
- **挪** 失败登记逻辑到 `RewriteResult.FailExecution(...)`
- **挪** 文件变更登记逻辑到 `RewriteResult.MarkFileChanged(...)`
- **增** `ExecuteSingleChange(...)`
- **增** `MapFailure(...)`
- **删/停用主责** 直接 new `ExecutionFailure` / `FileChange` 的部分

---

### 4.12 `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs`

**现状问题**：工作流总装配器过重。

**必须改**
- **改** `Assemble(RewriteWorkflowAssemblyInput input)`
  - 只保留跨聚合协作；删除内部强业务判断。
- **挪** `plan.RecordCompiled(...)` 前后的冲突事件触发逻辑回聚合/领域服务。
- **挪** `evidence.RecordCollected(...)` 之前的风控计算职责，尽量回 `VerificationEvidence`。
- **增** `BuildPlan(...)`
- **增** `ExecutePlan(...)`
- **增** `CollectEvidence(...)`
- **增** `BuildReport(...)`
- **增** `RecordDomainEvents(...)`
- **删/停用主责** 直接拼业务结果的中间逻辑，逐步拆薄。

---

### 4.13 `src/Domain/Workspaces/WorkspaceContext.cs`

**现状**：已较健康，但仍偏集合管理。

**必须改**
- **改** `Create(...)`
  - 创建后可选择记录 `WorkspacePreparedDomainEvent`。
- **改** `RegisterProject(...)`
  - 加强路径归一化 / workspace 边界校验。
- **改** `RegisterDocument(...)`
  - 增加路径是否落在 workspace 的校验。
- **改** `RegisterReference(...)`
  - 增加版本/命名去重语义。
- **改** `ValidateConsistency()`
  - 扩展为完整工作区准备校验。
- **增** `Prepare(Guid correlationId)`
  - 记录 `WorkspacePreparedDomainEvent`。
- **增** `EnsureWorkspaceOwnedPath(string path)`

---

### 4.14 `src/Domain/Rules/RuleSet.cs`

**必须改**
- **改** `AddRule(EnabledRule rule)`
  - 增加执行策略兼容性校验。
- **改** `RemoveRule(RuleCode ruleCode)`
  - 对默认规则/锁定规则做限制。
- **改** `Validate()`
  - 拆为多个具名规则检查方法。
- **增** `ActivateRule(RuleCode ruleCode)`
- **增** `DeactivateRule(RuleCode ruleCode)`
- **增** `EnsurePolicyCompatible(EnabledRule rule)`
- **增** `EnsureVersionCompatible()`

---

### 4.15 `src/Domain/Decision/RewriteDecision.cs`

**现状**：已开始富化，但还需继续。

**必须改**
- **改** `Complete(...)`
  - 拆分为更清晰的领域步骤。
- **增** `EvaluateExposure(...)`
- **增** `EvaluateExternalCallers(...)`
- **增** `EvaluateClosureIntegrity(...)`
- **增** `EvaluateRisk(...)`
- **增** `ApplyProtections(...)`
- **增** `ApplyConflicts(...)`
- **增** `FinalizeDecision(...)`
- **改** `AddProtection(...)`
  - 保持唯一性和排序语义。
- **改** `AddConflict(...)`
  - 保持唯一性和冲突归并语义。

**建议删/停用**
- 未来可继续削弱 `RewriteDecisionMaker`。

---

### 4.16 `src/Domain/Execution/RewritePlan.cs`

**必须改**
- **改** `RegisterChange(...)`
  - 让新增计划项自动经过更强的冲突/排序校验。
- **改** `AddChangeItem(...)`
  - 继续限制重复目标 + 支持动作兼容矩阵。
- **改** `AddConflict(...)`
  - 支持冲突集合归并。
- **改** `ValidateReadyForExecution()`
  - 增加“计划已编译/已冻结”约束。
- **改** `RecordCompiled(Guid correlationId)`
  - 仅在 ready 状态允许触发。
- **增** `Freeze()`
- **增** `EnsureNotFrozen()`
- **增** `DetectConflictFor(PlanChangeItem item)`
- **增** `RenumberOrders()`

---

### 4.17 `src/Domain/Output/Verification/VerificationEvidence.cs`

**必须改**
- **改** `AddCompilationEvidence(...)`
- **改** `AddStaticReasoningEvidence(...)`
- **改** `AddBehaviorEvidence(...)`
  - 三者都应通过统一入口更新风险。
- **改** `UpdateRiskSummary(...)`
  - 限制外部直接覆盖风险。
- **改** `RefreshRiskSummary()`
  - 改成聚合内部唯一刷新入口。
- **改** `RecordCollected(Guid correlationId)`
  - 仅在证据闭合后允许触发。
- **增** `CollectCompilationEvidence(...)`
- **增** `CollectStaticReasoningEvidence(...)`
- **增** `CollectBehaviorEvidence(...)`
- **增** `EnsureEvidenceComplete()`

---

## 5. 实施顺序（必须按阶段执行）

### Phase 1：先锁核心行为
目标文件：
- `RewriteDecision.cs`
- `RewritePlan.cs`
- `VerificationEvidence.cs`
- `RewriteResult.cs`
- 新增/调整对应测试

### Phase 2：收回原生事件归属
目标文件：
- `AnalysisCpgSnapshot.cs`
- `AnalysisCompositeLayerSnapshot.cs`
- `RuleTarget.cs`
- `ChangeCandidate.cs`
- 三个 `*EventSequenceBuilder.cs`

### Phase 3：瘦身 Logic 总装配器
目标文件：
- `RewritePlanCompiler.cs`
- `RewritePlanExecutor.cs`
- `RewriteWorkflowArtifactAssembler.cs`

### Phase 4：补全边界与治理
目标文件：
- `WorkspaceContext.cs`
- `RuleSet.cs`
- `RunReport.cs`

## 6. 执行要求

1. 每个阶段先加失败测试，再写生产代码。
2. 每次阶段完成后至少运行：
   - `dotnet build .\src\Domain\Domain.csproj`
   - `dotnet build .\src\Logic\Logic.csproj --no-restore`
   - `dotnet build .\src\Application\Application.csproj --no-restore`
   - `dotnet build .\src\Infrastructure\Infrastructure.csproj --no-restore`
   - `dotnet test .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-build`
   - `dotnet run --project .\tests\ArchitectureTests\ArchitectureTests.csproj --no-build`
3. 所有主链事件都要变成：**聚合原生优先，Logic fallback 次之**。
4. 若某类最终判定是“事实快照而非强聚合”，需要在代码与文档中显式说明，不得继续伪装成富领域聚合。

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：执行计划文档。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `对应 src/** 实现文件`
- `对应 tests/** 回归测试`
- `对应 log/*.log 验证日志`
- `.omx/plans/*.md`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
