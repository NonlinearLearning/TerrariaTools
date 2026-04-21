# Application.Contracts 高影响命名盘点（2026-04-21）

## 1. 目的与范围

本文为 `task-3` 的 review/documentation 交付物，只做**高影响命名盘点、风险分级、改名批次建议**，不直接落地高风险重命名。

锁定范围：

- `src/Application/Contracts/**`
- `src/Application/Mappers/**`
- `src/Application/Services/**`
- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/Workflow/**`

重点盘点三类名字：

1. `Contract*`
2. `*BuildInput`
3. `*Resolution`

目标不是“把名字全部改短”，而是回答三个问题：

- 哪些名字已经成为跨层耦合点；
- 哪些名字只是内部实现容器，可以先收口；
- 哪些名字一改就会同时波及 Contracts / Mapper / AppService / Tests / Docs，必须先冻结影响面。

---

## 2. 结论先行

### 2.1 风险总览

| 命名族 | 当前分布 | 风险等级 | 当前结论 |
| --- | --- | --- | --- |
| `Contract*` | `Application.Contracts` 根目录 + `Application.Mappers` + AppService + ArchitectureTests + Workflow tests | 高 | 先冻结盘点，不直接重命名 |
| `*BuildInput` | 主要在 `Logic/**`，由 AppService 与测试构造 | 中 | 可作为第二批次改名候选 |
| `*Resolution` | 主要在 `Logic/**`，但已经进入 AppService 输出拼装与 workflow 测试主路径 | 中高 | 必须先区分“阶段结果”与“最终决策”再改 |

### 2.2 批次建议

- **批次 A（立即可做，文档/测试守护）**  
  固化本文盘点；继续把 `Contract* / *BuildInput / *Resolution` 的扩散控制在当前边界，不新增同类泛名。

- **批次 B（中风险，内部实现先收口）**  
  优先评估 `RuleTargetBuildInput`、`WorkspaceContextBuildInput`、`PropagationBuildInput`、`RewriteDecisionBuildInput` 的语义改名；改名时同步 AppService 与 Workflow tests。

- **批次 C（高风险，leader 统一裁决）**  
  处理 `Application.Contracts` 根目录 `Contract*` 共享边界语义类型；如果决定削减 `Contract` 前缀，必须成批同步 `ContractMapper.*`、AppService、ArchitectureTests、Workflow tests 与文档样例。

- **批次 D（高风险，工作流结果命名收口）**  
  对 `PropagationResolution`、`RewriteDecisionResolution` 做语义复盘，先确认它们到底表示“阶段输出”“裁决结果”“执行建议”中的哪一种，再决定是否收口为更强语义名。

---

## 3. `Contract*` 盘点

## 3.1 当前定义面

`src/Application/Contracts` 根目录当前存在 23 个 `Contract*` 文件（不含子目录 DTO 与接口）：

- `ContractAnalysisSourceKind`
- `ContractApprovalReason`
- `ContractAuditConclusion`
- `ContractCandidateKind`
- `ContractCandidateReason`
- `ContractClosureIntegrityAssessmentDto`
- `ContractClosureIntegrityStatus`
- `ContractCodeRewriteKind`
- `ContractConfidenceLevel`
- `ContractCpgNodeType`
- `ContractExposureDto`
- `ContractExternalCallerPresenceDto`
- `ContractInputOrigin`
- `ContractMinimumAnalysisTarget`
- `ContractPlanAction`
- `ContractPlanConflict`
- `ContractPlanReason`
- `ContractRejectionReason`
- `ContractRiskLevel`
- `ContractRiskScoreDto`
- `ContractRunMode`
- `ContractScenarioTag`
- `ContractSliceDirection`

这些名字的共同点是：**它们不是具体某个 use case 的 DTO，而是跨 `Contracts/*` 子域重复复用的共享边界语义**。

## 3.2 当前引用热点

### Application.Services

- `src/Application/Services/WorkspaceContextAppService.cs`
  - 直接消费 `ContractRunMode`
- `src/Application/Services/DecisionAppService.cs`
  - 直接消费 `ContractExposureDto`
  - 直接消费 `ContractExternalCallerPresenceDto`
  - 直接消费 `ContractClosureIntegrityAssessmentDto`
  - 直接消费 `ContractRiskScoreDto`
- `src/Application/Services/PropagationAppService.cs`
  - 直接消费 `ContractCandidateKind`
  - 直接消费 `ContractCandidateReason`
  - 直接消费 `ContractScenarioTag`
  - 直接消费 `ContractSliceDirection`
- `src/Application/Services/RewriteWorkflowAppService.cs`
  - 直接消费 `ContractCandidateKind`
  - 直接消费 `ContractCandidateReason`
  - 直接消费 `ContractScenarioTag`
  - 直接消费 `ContractSliceDirection`
  - 直接消费 `ContractConfidenceLevel`
  - 直接消费 `ContractPlanAction`

### Application.Mappers

`ContractMapper` 已成为 `Contract*` 的主耦合枢纽，当前相关文件包括：

- `src/Application/Mappers/ContractMapper.Analysis.cs`
- `src/Application/Mappers/ContractMapper.ContractSemantics.cs`
- `src/Application/Mappers/ContractMapper.DecisionExecution.cs`
- `src/Application/Mappers/ContractMapper.MarkingPropagation.cs`
- `src/Application/Mappers/ContractMapper.Output.cs`
- `src/Application/Mappers/ContractMapper.RewriteArtifacts.cs`
- `src/Application/Mappers/ContractMapper.Workflow.cs`
- `src/Application/Mappers/ContractMapper.Workspaces.cs`

### Tests / Docs anchors

- `tests/ArchitectureTests/Program.cs`
  - 同时承担 Contracts namespace 约束、Mapper 位置约束、AppService 对 Contracts 的使用样例
- `tests/Isolation.AnalysisTests/Workflow/DddP1WorkflowTests.cs`
- `tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs`

## 3.3 风险判断

### 高风险原因

1. `Contract*` 已经不是局部实现名，而是**Application 边界的共享语言层**；
2. `ContractMapper.*` 将它们绑定到 `Domain / Logic / Contracts` 三方映射；
3. `ArchitectureTests` 和 `Workflow` 测试都把这些名字当作稳定边界事实；
4. 文档 `docs/约束/命名治理执行准则.md`、`docs/应用层设计.md` 已经用 “Contract 边界语义” 描述它们。

## 3.4 当前可疑候选

以下名字值得在批次 C 中评估，但**本轮不建议直接改**：

- `ContractExposureDto`
- `ContractExternalCallerPresenceDto`
- `ContractClosureIntegrityAssessmentDto`
- `ContractRiskScoreDto`

原因：这四个名字同时带有 `Contract` 与 `Dto`，容易出现“双轨重复表达边界”的感觉；但它们又确实承载跨请求/输出的共享边界语义，是否去掉 `Contract` 前缀不能仅凭字面判断。

---

## 4. `*BuildInput` 盘点

## 4.1 当前定义

当前已定义的 `*BuildInput` 类型：

| 类型 | 定义位置 | 当前主要消费者 | 风险 |
| --- | --- | --- | --- |
| `WorkspaceContextBuildInput` | `src/Logic/Workspaces/WorkspaceContextBuildInput.cs` | `WorkspaceContextAppService`，`DddP1WorkflowTests`，`RuleScopeAndPolicyValueObjectTests`，`WorkspaceAndCandidateRichModelTests` | 中 |
| `RuleTargetBuildInput` | `src/Logic/Marking/RuleTargetBuildInput.cs` | `RuleTargetAppService` | 中低 |
| `RuleTargetCandidateBuildInput` | `src/Logic/Marking/RuleTargetCandidateBuildInput.cs` | `DddP1WorkflowTests`，`ArchitectureTests` 锚点 | 中 |
| `PropagationBuildInput` | `src/Logic/Propagation/PropagationBuildInput.cs` | `PropagationAppService`，`DddP1WorkflowTests` | 中高 |
| `RewriteDecisionBuildInput` | `src/Logic/Decision/RewriteDecisionBuildInput.cs` | `DecisionAppService`，`DddP1WorkflowTests` | 中高 |
| `RewriteDecisionAssessmentBuildInput` | `src/Logic/Decision/RewriteDecisionAssessmentBuildInput.cs` | `DddP1WorkflowTests` | 中 |

## 4.2 质量判断

`*BuildInput` 的优点：

- 明确说明“这是供 Builder / Maker / Propagator 组装的内部输入载体”；
- 目前主要停留在 `Logic`，没有直接泄漏到 `Application.Contracts`。

`*BuildInput` 的问题：

- `Build` 是纯技术动作，不表达阶段意图；
- 同类词已经在 `Builder / AppService / Tests` 中重复出现，容易让名字只剩“被 build 的东西”；
- 一旦进入跨阶段工作流（如 `PropagationBuildInput`、`RewriteDecisionBuildInput`），读者仍需额外跳转代码才能知道是“评估输入”还是“执行输入”。

## 4.3 改名批次建议

### 批次 B1：先处理最局部的内部构造输入

优先顺序：

1. `RuleTargetBuildInput`
2. `WorkspaceContextBuildInput`

原因：

- 消费点少；
- 不直接落在工作流主链最深处；
- 改名时只需同步 AppService + 少量测试。

### 批次 B2：再处理跨阶段输入

优先顺序：

1. `PropagationBuildInput`
2. `RewriteDecisionBuildInput`
3. `RuleTargetCandidateBuildInput`
4. `RewriteDecisionAssessmentBuildInput`

原因：

- 这些类型已经嵌入 “传播 / 决策 / 候选评估” 主流程；
- 改名时必须同步 workflow 测试叙事，否则文档与测试会先失真。

---

## 5. `*Resolution` 盘点

## 5.1 当前定义

当前核心 `*Resolution` 类型只有两个：

| 类型 | 定义位置 | 当前主要消费者 | 风险 |
| --- | --- | --- | --- |
| `PropagationResolution` | `src/Logic/Propagation/PropagationResolution.cs` | `PropagationAppService`、`RewriteWorkflowAppService`、`DddP1WorkflowTests`、`StageDomainEventsTests` | 高 |
| `RewriteDecisionResolution` | `src/Logic/Decision/RewriteDecisionResolution.cs` | `DecisionAppService`、`RewriteWorkflowAppService`、`DddP1WorkflowTests` | 高 |

## 5.2 风险判断

`Resolution` 在这里不是普通内部名，而是**阶段产物名**：

- `PropagationAppService` 直接把 `PropagationResolution` 拆成 `PropagationResultDto`
- `RewriteWorkflowAppService` 同时串联 `PropagationResolution` 与 `RewriteDecisionResolution`
- Workflow tests 用它们断言传播轨迹、批准结果、冲突/保护项

这意味着 `Resolution` 已经成为“工作流阶段结果”的事实术语，而不是随手命名。

## 5.3 改名前必须先回答的问题

### `PropagationResolution`

要先明确它到底更像：

- 传播阶段结果
- 传播分析结论
- 传播裁定

如果不先分清，就会出现把“传播轨迹 + 边界 + 候选对象”混在一个更模糊的新名字里的风险。

### `RewriteDecisionResolution`

要先明确它到底更像：

- 决策阶段结果
- 决策结论
- 决策输出封装

它还与 `RewriteDecisionResolutionInput` / `RewriteDecisionResolutionPolicy` / `RewriteDecisionOutcome` 在测试与架构文档里形成语义族，不能单改一个词。

## 5.4 改名批次建议

### 批次 D（leader 裁决后再做）

先做三件事：

1. 在 Domain/Logic 语义上确认 `Resolution / Outcome / Assessment / Result` 的词汇边界；
2. 统一更新 `RewriteDecisionResolutionInput` / `RewriteDecisionResolutionPolicy` / `RewriteDecisionOutcome` 的叙事关系；
3. 再决定是否同步改 `Application` 层变量名与 `Workflow` 测试命名。

---

## 6. 推荐执行顺序

### Step 1：继续冻结高影响边界名

本轮先不直接重命名 `Contract*` 根目录共享边界语义类型。

### Step 2：内部 `BuildInput` 先收口

让 worker-1 后续优先考虑 `RuleTargetBuildInput` / `WorkspaceContextBuildInput` 这类影响面更窄的实现名。

### Step 3：工作流 `Resolution` 统一裁决

由 leader 统一决定 `Resolution` 族是否整体收口为更强语义名，避免只改一个节点导致 `Policy / Input / Outcome` 三套词汇并存。

---

## 7. 对后续实现任务的约束

- 未完成批次 C / D 前，不要新增新的 `Contract*` 根目录共享语义类型，除非确实要服务多个 Contracts 子域；
- 新增内部构造模型时，优先判断是否真的需要 `*BuildInput`，还是可以直接用更强业务主语；
- 不要在 AppService 里创造新的 `*Resolution` 局部语义词，而应沿用已裁定的阶段术语；
- 改名 PR 必须同时带上：
  - `ContractMapper.*` 同步
  - `ArchitectureTests`
  - `Workflow` 相关测试
  - 文档同步

---

## 8. 本轮证据锚点

- `src/Application/Services/WorkspaceContextAppService.cs`
- `src/Application/Services/DecisionAppService.cs`
- `src/Application/Services/PropagationAppService.cs`
- `src/Application/Services/RewriteWorkflowAppService.cs`
- `src/Application/Mappers/ContractMapper.*.cs`
- `tests/ArchitectureTests/Program.cs`
- `tests/Isolation.AnalysisTests/Workflow/DddP1WorkflowTests.cs`
- `tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs`
- `docs/约束/命名治理执行准则.md`

