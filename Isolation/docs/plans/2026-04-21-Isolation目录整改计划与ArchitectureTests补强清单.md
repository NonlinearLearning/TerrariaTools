# Isolation 目录整改计划与 ArchitectureTests 补强清单（2026-04-21）

## 1. 目标

把前一轮外部项目学习结果压缩成一份能直接驱动 `Isolation` 下一阶段整改的执行文档，覆盖：

1. 目录整改计划
2. 验收标准
3. `ArchitectureTests` 补强清单

本文服务于当前真实代码结构：

- `src/Domain`
- `src/Logic`
- `src/Application`
- `src/Infrastructure`
- `tests/ArchitectureTests/Program.cs`

本文的直接依据：

- `docs/DDD/外部项目文件组织与模块划分学习-2026-04.md`
- `docs/约束/项目代码设计取舍指导.md`
- `tests/ArchitectureTests/Program.cs`
- `src/Application/Services/*`
- `src/Logic/Workflow/*`

## 2. 现状判断

### 2.1 当前稳定骨架

当前仓库已经具备稳定主干：

- `Application` 作为用例编排入口
- `Logic` 作为阶段能力与复用能力层
- `Domain` 作为语义、不变量、状态变化承载层
- `Infrastructure` 作为 Roslyn / Persistence / 技术适配层

这条主干已经比“继续拆新项目”更值钱。

### 2.2 当前热点目录

当前最值得整改的目录热点有两类。

#### A. `src/Application/Services`

当前服务文件：

- `AnalysisAppService.cs`
- `AnalysisCpgAppService.cs`
- `CodeIsolationAppService.cs`
- `DecisionAppService.cs`
- `PropagationAppService.cs`
- `RewriteWorkflowAppService.cs`
- `RuleTargetAppService.cs`
- `WorkspaceContextAppService.cs`

这里最需要持续收口的是：

- 稳定规则构造
- 局部规则字符串
- 中间阶段对象拼装
- 过厚的 workflow 编排逻辑

#### B. `src/Logic/Workflow`

当前已存在较完整的阶段接口与实现：

- `IRewriteWorkflowPropagationStage.cs`
- `IRewriteWorkflowDecisionStage.cs`
- `IRewriteWorkflowPlanStage.cs`
- `IRewriteWorkflowExecutionStage.cs`
- `IRewriteWorkflowEvidenceStage.cs`
- `IRewriteWorkflowReportStage.cs`
- `RewriteWorkflowPropagationStage.cs`
- `RewriteWorkflowDecisionStage.cs`
- `RewriteWorkflowPlanStage.cs`
- `RewriteWorkflowExecutionStage.cs`
- `RewriteWorkflowEvidenceStage.cs`
- `RewriteWorkflowReportStage.cs`
- `RewriteWorkflowArtifactAssembler.cs`
- `RewritePlanCompiler.cs`
- `RewritePlanExecutor.cs`
- `RunReportAssembler.cs`

这说明仓库已经进入“阶段能力化”的正确方向，但还需要继续压缩主链热点和补边界守护。

## 3. 目录整改计划

## 3.1 P1：继续纯化 `src/Application/Services`

### 目标

让 `Application.Services` 更接近薄壳层，只保留：

- 请求入口
- 调用顺序
- DTO / Contract 映射
- 少量权限/策略编排

### 具体动作

1. 持续检查 `RewriteWorkflowAppService.cs`
   - 新增阶段逻辑优先下沉到 `Logic.Workflow`
   - 新增稳定规则入口优先下沉到 `Logic.Rules`
2. 持续检查 `WorkspaceContextAppService.cs`
   - 默认规则装配继续收口到 preset / builder / factory
3. 持续检查 `PropagationAppService.cs` 与 `RuleTargetAppService.cs`
   - 保持 preset 入口统一
   - 禁止回流局部 `RuleCode.Create(...)`

### 完成信号

- `Application.Services` 新增代码以编排为主
- 稳定规则解析入口集中到 `Logic.Rules`
- 同模块内部不出现 `AppService -> AppService` 复用

## 3.2 P1：继续稳定化 `src/Logic/Rules`

### 目标

把规则体系进一步做成稳定复用层。

### 具体动作

1. 继续扩充：
   - catalog
   - preset
   - factory
   - defaults builder
2. 继续把 Application 和局部 workflow 中残留的稳定规则字符串挪进统一入口
3. 对规则场景做“显式目录化”

### 目录演进方向

建议继续围绕下面的能力收口：

- `Logic/Rules/*Preset.cs`
- `Logic/Rules/*Factory.cs`
- `Logic/Rules/*Catalog.cs`
- `Logic/Rules/*DefaultsBuilder.cs`

### 完成信号

- 稳定规则名不再散落于多个 AppService 或 workflow 局部分支
- 规则新增点清晰集中
- 规则默认值变更只改一处或少数固定入口

## 3.3 P2：继续拆细 `src/Logic/Workflow`

### 目标

把 `Logic.Workflow` 从“热点目录”继续演进成“稳定阶段能力集合”。

### 具体动作

1. 以阶段接口为骨架，继续收口主链依赖
2. 把“大而全”的 workflow 决策分发到：
   - stage
   - assembler
   - compiler
   - executor
   - evidence collector
3. 对阶段输入输出继续保持显式模型：
   - `*Input.cs`
   - `*Result.cs`
   - `StageInputs.cs`
   - `StageResults.cs`

### 目录演进方向

当前不需要新增项目，优先保持在 `Logic.Workflow` 内继续整理。

适合继续显式化的子能力：

- stage
- artifacts
- execution
- evidence
- events

### 完成信号

- `RewriteWorkflowAppService` 进一步变薄
- workflow 新增需求优先新增 stage / assembler / collector
- workflow 阶段边界更稳定，测试粒度更细

## 3.4 P2：继续把合法性与不变量压回 `src/Domain`

### 目标

让 `Domain` 继续承载：

- 状态变化合法性
- 领域级不变量
- 决策约束
- 聚合内部行为

### 具体动作

1. 检查决策相关规则是否还停留在 `Logic` 或 `Application`
2. 检查候选合法性、状态变化约束是否还能继续回收到 `Domain`
3. 检查领域事件解释是否仍聚合在领域语义中

### 完成信号

- `Domain` 的规则解释更集中
- 贫血模型继续减少
- `Logic` 更像能力组合层，而不是规则真相来源

## 3.5 P3：只在出现真实需求时再拆项目

### 当前结论

当前不建议马上引入：

- 独立 `Domain.Shared`
- 独立 `Application.Contracts`
- 独立 `HttpApi`
- 多交付面矩阵

### 触发条件

满足以下任一条件后再评估新增项目：

- 需要独立发布契约
- 需要远程 API 边界
- 需要多交付面复用统一契约
- 需要编译器级阻断当前污染

## 4. 验收标准

## 4.1 目录整改验收标准

### A. Application 层

1. `src/Application/Services` 中新增逻辑以编排为主
2. 新增共享逻辑默认下沉到 `Logic` 或 `Domain`
3. 不出现同模块内部 `AppService -> AppService`
4. 不直接散落稳定规则构造与稳定规则码解析

### B. Logic 层

1. `Logic.Rules` 成为稳定规则入口
2. `Logic.Workflow` 继续按阶段能力组合演进
3. 新增 workflow 能力优先通过 stage / assembler / compiler / executor 承载
4. 阶段输入输出继续使用显式类型

### C. Domain 层

1. 领域不变量继续集中
2. 状态变化约束继续回到 `Domain`
3. 决策合法性不回流到 `Application`

### D. Infrastructure 层

1. 继续只承载 Roslyn / IO / Persistence / 技术适配
2. 不吸收业务主链语义

## 4.2 验收证据标准

每轮目录整改完成后，至少应提供以下证据中的三类：

1. 代码 diff
2. `tests/ArchitectureTests/Program.cs` 新增或更新规则
3. 相关分析测试或工作流测试
4. 构建通过输出
5. 文档同步更新

## 5. ArchitectureTests 补强清单

## 5.1 应优先补强的规则

### 规则 1：`Application.Services` 不得互调

#### 目标

防止同模块内部把 AppService 当共享逻辑库。

#### 建议检查

- `Application.Services.*` 命名空间中的类型，不应直接依赖其他 `*AppService`

### 规则 2：`Application.Services` 不得直接解析稳定规则码

#### 目标

防止规则入口回流到应用层。

#### 建议检查

- `Application.Services.*` 不得直接出现稳定规则构造
- 重点盯住：
  - `RuleCode.Create(...)`
  - 规则目录绕过 preset / factory 的直接拼装

### 规则 3：`RewriteWorkflowAppService` 只保留编排职责

#### 目标

防止 workflow 主链再次膨胀回 Application。

#### 建议检查

- `RewriteWorkflowAppService` 不直接承担阶段实现
- 阶段实现应位于 `Logic.Workflow`

### 规则 4：`Logic.Workflow` 必须通过显式阶段接口收口

#### 目标

保持 workflow 阶段化。

#### 建议检查

- 保持以下接口存在并被实现：
  - `IRewriteWorkflowPropagationStage`
  - `IRewriteWorkflowDecisionStage`
  - `IRewriteWorkflowPlanStage`
  - `IRewriteWorkflowExecutionStage`
  - `IRewriteWorkflowEvidenceStage`
  - `IRewriteWorkflowReportStage`

### 规则 5：`Logic` 不得反向依赖 `Application`

#### 目标

继续守住主干依赖方向。

#### 建议检查

- `Logic` 只依赖 `Domain`
- `Application` 才允许依赖 `Logic`

### 规则 6：领域解释继续留在 `Domain`

#### 目标

防止决策解释和合法性判断上浮。

#### 建议检查

- 决策评估解释继续留在 `Domain.Decision`
- 领域不变量不应漂移到 `Application` 或 `Infrastructure`

## 5.2 建议补强顺序

1. `Application.Services` 不得互调
2. `Application.Services` 不得直接解析稳定规则码
3. `RewriteWorkflowAppService` 编排纯度检查
4. `Logic.Workflow` 阶段接口存在性与实现关系检查
5. 领域解释落位检查

## 5.3 每条规则的验收口径

每新增一条 ArchitectureTests 规则，至少满足：

1. 能指出被保护的目录或命名空间
2. 能说明违规样式
3. 失败时给出明确错误信息
4. 对应至少一条本仓库约束文档

## 6. 推荐执行批次

## 批次一：边界守护

目标：

- 把现有已达成共识的规则继续固化进 `ArchitectureTests`

内容：

- AppService 不互调
- Application 不直接解析稳定规则码
- Logic 不反向依赖 Application

## 批次二：workflow 热点收口

目标：

- 继续降低 `RewriteWorkflowAppService` 和 `Logic.Workflow` 热点风险

内容：

- AppService 编排纯度
- stage 接口与实现骨架守护
- artifact / compiler / executor / assembler 角色边界守护

## 批次三：目录优化与文档同步

目标：

- 在目录稳定后，把新共识继续写回文档与测试

内容：

- 目录说明同步
- 文档中的“已知问题”与现状对齐
- 测试与文档双向回链

## 7. 风险与缓解

### 风险 1：过早新增项目

缓解：

- 当前只做目录整改与边界守护
- 把新增项目推迟到真实编译边界需求出现后

### 风险 2：ArchitectureTests 写成脆弱字符串匹配

缓解：

- 优先围绕命名空间、类型依赖、公开接口关系建立规则
- 失败信息要绑定具体规则意图

### 风险 3：目录整改只改文档不改测试

缓解：

- 每一批整改至少补一条自动化守护

## 8. 执行完成定义

这份计划执行完毕，至少满足：

1. 已有目录整改方向形成正式文档
2. 验收标准明确可检查
3. `ArchitectureTests` 补强清单明确到可落地规则
4. 文档已进入仓库正式路径
5. 项目级约束文档能回链到本文

## 9. 一句话执行口径

先用目录整改把职责边界收紧，再用 ArchitectureTests 把边界从共识变成回归守护。

## 10. 执行进度（2026-04-21）

### 已完成

已完成第一批 `ArchitectureTests` 补强：

1. `Application.Services` 不得互调
   - 已通过禁止引用其他 `I*AppService` 与其他 `*AppService` 类型收紧
2. `Application.Services` 不得直接解析稳定规则码
   - 已补 `RewriteWorkflowAppService`
   - 已补 `WorkspaceContextAppService`
   - 已保留 `PropagationAppService`
   - 已保留 `RuleTargetAppService`
3. `RewriteWorkflowAppService` 编排纯度检查
   - 已补禁止直接 `new RewriteWorkflowPropagationStageInput`
   - 已补禁止直接 `new RewriteWorkflowDecisionStageInput`

已完成第二批 `ArchitectureTests` 补强的首个子批次：

4. `RewriteWorkflowArtifactAssembler` 阶段装配纯度检查
   - 已补必须委派 `Plan / Execution / Evidence / Report / Event` 五段能力
   - 已补禁止在 assembler 内直接构造 `RewritePlanCompilationInput`
   - 已补禁止在 assembler 内直接构造 `RewritePlanExecutionInput`
   - 已补禁止在 assembler 内直接创建 `VerificationEvidence`
   - 已补禁止在 assembler 内直接构造 `RunReportAssemblyInput`
5. `Logic.Workflow` 阶段职责边界检查
   - 已补 `RewriteWorkflowPlanStage` 必须委派 `IRewritePlanCompiler`
   - 已补 `RewriteWorkflowExecutionStage` 必须委派 `IRewritePlanExecutor`
   - 已补 `RewriteWorkflowEvidenceStage` 必须委派三类 `Evidence Collector`
   - 已补 `RewriteWorkflowReportStage` 必须委派 `IRunReportAssembler`
   - 已补各阶段对跨职责依赖的禁止性断言
6. `RewriteWorkflowAppService` 编排输入收口
   - 已把传播阶段输入构造收口到 `RewriteWorkflowPropagationStageInput.Create(...)`
   - 已把决策阶段输入构造收口到 `RewriteWorkflowDecisionStageInput.Create(...)`
7. `Logic.Workflow` 核心角色边界补强
   - 已补 `RewriteWorkflowPropagationStage` 必须委派 `IImpactPropagator`
   - 已补 `RewriteWorkflowDecisionStage` 必须委派 `IRewriteDecisionAssessmentBuilder` 与 `IRewriteDecisionMaker`
   - 已补 `RewritePlanCompiler` 必须委派 `RewritePlan.Create(...)` 与 `ApplyDecisionOutcome(...)`
   - 已补 `RunReportAssembler` 必须委派 `RunReport.CreateFromExecutionOutcome(...)`
   - 已补上述角色对跨职责依赖的禁止性断言
8. `WorkflowAssemblyInput / StageInputs / EventStage` 护栏补强
   - 已补 `RewriteWorkflowAssemblyInput` 必须显式提供五类 `To*StageInput()` 映射
   - 已补 `RewriteWorkflowAssemblyInput` 不得吸收事件记录与事件构造职责
   - 已补 `RewriteWorkflowStageInputs` 保持纯输入模型职责
   - 已补 `RewriteWorkflowEventStage` 必须委派 `WorkflowEventSequenceBuilder` 与 `IDomainEventRecorder`
   - 已补 `RewriteWorkflowStageResults` 必须提供统一 `ToArtifacts(...)` 回传入口
9. `WorkflowEventSequenceBuilder / DomainEventRecorder` 护栏补强
   - 已补 `WorkflowEventSequenceBuilder` 必须显式提供 `BuildEvents(...)` 主入口
   - 已补 `WorkflowEventSequenceBuilder` 负责补齐 decision / plan / execution / evidence / report 主链事件
   - 已补 `WorkflowEventSequenceBuilder` 对执行、传播、报告装配跨职责依赖的禁止性断言
   - 已补 `IDomainEventRecorder` 最小接口面断言
   - 已补 `InMemoryDomainEventRecorder` 的清理、只读视图、按序查询职责断言
10. DDD 文档事实级同步
   - 已同步 `docs/DDD/src目录分层说明.md`
   - 已同步 `docs/DDD/当前架构取舍评估.md`
   - 已同步 `docs/DDD/逻辑层引入设计.md`
   - 已同步 `docs/DDD/领域事件与上下文协作落地执行文档.md`
   - 已同步 `docs/约束/项目代码设计取舍指导.md` 的“已落地 Workflow 护栏事实”
11. 第二批 DDD 文档事实级重写
   - 已同步 `docs/DDD/src实际设计与事件风暴理想设计差异.md`
   - 已同步 `docs/DDD/上下文边界优化指南.md`
   - 已同步 `docs/DDD/上下文地图.md`
   - 已同步 `docs/DDD/DDD战术设计.md`
   - 已把 `Workflow.Events`、阶段装配链、事件收尾链写回设计文档
12. 第三批 DDD 文档事实级重写
   - 已同步 `docs/DDD/事件风暴.md`
   - 已同步 `docs/DDD/DDD战略设计.md`
   - 已同步 `docs/DDD/DDD问题域与业务愿景.md`
   - 已同步 `docs/DDD/Analysis-Marking-Propagation统一内部事件链落地设计.md`
   - 已把 Workflow 主链装配、事件补齐、测试锚点继续写回设计文档

### 验证结果

- `dotnet build .\\Isolation.slnx` ✅
- `dotnet run --project .\\tests\\ArchitectureTests\\ArchitectureTests.csproj --no-build` ✅
- `dotnet test .\\tests\\Isolation.AnalysisTests\\Isolation.AnalysisTests.csproj --no-build` ✅

### 当前进度百分比

- 批次一：边界守护：`60%`
- 批次二：workflow 热点收口：`78%`
- 批次三：目录优化与文档同步：`80%`

按整份计划口径估算：

- 目录整改计划落地：`100%`
- 第一批与第二批关键护栏落地：`93%`
- 整体执行进度：`88%`
