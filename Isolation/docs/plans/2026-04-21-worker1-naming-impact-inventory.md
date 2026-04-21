# 2026-04-21 Worker1 命名治理影响盘点（阶段 1）

## 已落地的低风险收口

- `Logic/Analysis/Engine/Language/ICallResolver.cs` → `CallResolver.cs`
  - 类型：`ICallResolver` → `CallResolver`
  - 原因：静态类误用接口 `I` 前缀，语义与 C# 约定冲突。
- `Domain/Analysis/Engine/Semantic/NodeExtension.cs` → `NodeExtensions.cs`
  - 类型：`NodeExtension` → `NodeExtensions`
  - 原因：扩展方法容器应使用复数后缀，表达“方法集合”。
- `Logic/Analysis/Engine/Language/SarifExtension.cs` → `SarifExtensions.cs`
  - 类型：`SarifExtension` → `SarifExtensions`
  - 原因：同上。
- `tests/ArchitectureTests/Program.cs`
  - 局部变量：`rsnReportDto` → `runReportDto`
  - 原因：移除不可读缩写。

## 新增守护规则

位于：`tests/Isolation.AnalysisTests/Naming/NamingGovernanceConventionsTests.cs`

1. 非接口公开类型禁止使用 `I` 前缀。
2. 公开静态扩展方法容器禁止使用单数 `Extension` 后缀。

## 高影响候选（暂未改名）

### 1. `Contract*` 契约前缀族

- 定义数量：23 个公开类型
- `src/tests` 总引用量：约 438 处
- 典型样本：
  - `ContractRunMode`
  - `ContractCandidateKind`
  - `ContractExposureDto`
  - `ContractRiskScoreDto`
  - `ContractClosureIntegrityAssessmentDto`

#### 风险判断

- 已经跨 `Application / Logic / Domain / tests` 使用。
- 其中一部分虽然位于 `Application.Contracts` 命名空间，但名字仍带 `Contract` 前缀，存在“目录和类型双轨重复”。
- 如果直接改名，会同时打到：
  - `Application.Services/*`
  - `Application.Mappers/*`
  - `ArchitectureTests/Program.cs`
  - 多个 `Workflow` 测试

#### 建议收口顺序

1. 先决定是否保留 “Contract” 作为外部边界显式标签。
2. 若不保留，优先从 4 个 record/DTO 开始：
   - `ContractExposureDto`
   - `ContractRiskScoreDto`
   - `ContractExternalCallerPresenceDto`
   - `ContractClosureIntegrityAssessmentDto`
3. 再处理枚举族，避免一次性爆炸。

### 2. `*RulePreset` / `IRulePresetProvider` 规则对象族

- 总引用量：约 65 处
- 公开类型：
  - `IMarkingRulePreset`
  - `IPropagationRulePreset`
  - `IRewriteWorkflowRulePreset`
  - `MarkingRulePreset`
  - `PropagationRulePreset`
  - `RewriteWorkflowRulePreset`
  - `WorkspaceDefaultRulePreset`
  - `IRulePresetProvider`

#### 风险判断

- 该族名称已经穿透 `Application -> Logic -> Infrastructure -> tests`。
- 现状问题不是“错误”，而是 `Preset` 与 `Provider/Factory` 语义边界有些重叠：
  - `WorkspaceDefaultRulePreset` 更像默认规则来源
  - `IRulePresetProvider` 与 `*RulePreset` 的职责命名存在双轨感
- 直接收口需要先决定统一术语是：
  - `RuleCatalog`
  - `RuleProfile`
  - `RuleDefaults`
  - 还是保留 `Preset`

#### 建议收口顺序

1. 先固定术语：`Preset` 是否代表“固定规则组合”。
2. 若保留 `Preset`，优先只处理 `WorkspaceDefaultRulePreset` / `IRulePresetProvider` 这一对。
3. 若不保留 `Preset`，必须整族迁移并同步 DI 注册与异常消息断言。

## 当前验证结论

- `dotnet restore Isolation/Isolation.slnx`：PASS
- `dotnet build Isolation/Isolation.slnx --no-restore`：PASS
- 两条新增命名守护测试：PASS
- `dotnet run --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build`（cwd=`Isolation`）：PASS
- 整组 `NamingGovernanceConventionsTests`：存在预存失败
  - 失败项：`Legacy_lowercase_source_directories_stay_on_allowlist`
  - 原因：仓库已有 lowercase 目录未入 allowlist，非本轮 rename 引入
