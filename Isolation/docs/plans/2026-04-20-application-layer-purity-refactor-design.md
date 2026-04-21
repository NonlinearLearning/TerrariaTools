# 2026-04-20 Application layer purity refactor

## 背景

当前 `Application` 层存在两类明显越界实现：

1. `src/Application/Services/RewriteWorkflowAppService.cs`
   - 直接拼装 `ContractExposure`、`ExternalCallerPresence`、`ClosureIntegrityAssessment`、`DecisionRiskScore`
   - 直接构造 `RuleSet`、`EnabledRule` 并映射 `CpgType -> CandidateKind / RuleTargetKind`
2. `src/Application/Services/RuleTargetAppService.cs`
   - 重复构造 `RuleSet`、`EnabledRule` 与节点类型映射逻辑

这类代码不是“用例编排”，而是可复用的单阶段推导/组装能力，应该下沉到 `Logic`。

## 外部依据

### 官方 / 权威资料

- Martin Fowler, `Service Layer`
  - <https://martinfowler.com/eaaCatalog/serviceLayer.html>
  - 结论：Service Layer 负责定义应用边界、协调单次操作的响应
- Martin Fowler, `Anemic Domain Model`
  - <https://martinfowler.com/bliki/AnemicDomainModel.html>
  - 结论：Application / Service Layer 应保持 thin，负责协调并委派；关键业务知识不应堆在服务层
- Microsoft Learn, `Use tactical DDD to design microservices`
  - <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
  - 结论：Domain service 封装跨实体/聚合的业务规则；Application service 编排 use case，本身不承载业务逻辑
- ABP, `Application Services Best Practices & Conventions`
  - <https://abp.io/docs/latest/Best-Practices/Application-Services>
  - 结论：应用服务应保持为 DTO 边界和用例编排；复用需求优先下沉到 domain/shared class，而不是在应用服务之间复制/调用
- ABP, `Domain Services`
  - <https://abp.io/docs/latest/framework/architecture/domain-driven-design/domain-services>
  - 结论：不自然属于单个实体/值对象的核心逻辑，应放在无状态领域服务

### 本地参考资料

- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\application-services.md`
  - 应用服务是薄编排层；同模块复用应下沉到 domain/shared class
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\domain-driven-design\domain-services.md`
  - Domain Service 承担不属于单个实体的核心逻辑
- `C:\Users\shan\Downloads\api开源教程学习\CleanArchitecture-main\src\Clean.Architecture.UseCases\README.md`
  - Use Cases / Application Services 是对 domain model 的 thin wrapper
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0010-use-clean-architecture-for-writes.md`
  - 写模型采用整洁架构，隔离业务逻辑
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0011-create-rich-domain-models.md`
  - 采用富领域模型，避免把行为抽空
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0012-use-domain-driven-design-tactical-patterns.md`
  - 明确使用 Domain Service 处理不自然属于单个实体的逻辑

## 当前问题归类

### 问题 1: 应用层直接派生决策评估对象

`RewriteWorkflowAppService` 直接根据传播结果和请求参数派生：

- `ContractExposure`
- `ExternalCallerPresence`
- `ClosureIntegrityAssessment`
- `DecisionRiskScore`

这属于“根据领域事实生成决策输入”的单阶段能力，可复用，且不应该散落在 `AppService`。

### 问题 2: 应用层直接构造标记规则执行输入

`RewriteWorkflowAppService` 与 `RuleTargetAppService` 都直接：

- 新建 `RuleSet`
- 新建 `EnabledRule`
- 创建统一的 `RuleExecutionPolicy`
- 映射 `CpgType -> CandidateKind / RuleTargetKind`
- 调用 `IChangeCandidateMarker`

这不是用例编排，而是可复用的低等级构造逻辑。

## 选定方案

保持四层边界不变，只做最小下沉：

1. 在 `src/Logic/Marking` 新增候选构造器
   - 统一封装 `RuleSet` / `EnabledRule` / policy / kind mapping
   - `Application` 只传入 `RuleTarget`、场景标签和规则集名称
2. 在 `src/Logic/Decision` 新增决策评估构造器
   - 统一封装 `ContractExposure` / `ExternalCallerPresence` / `ClosureIntegrityAssessment` / `DecisionRiskScore` 的派生逻辑
   - `Application` 只传入传播事实信号和请求信号
3. `RewriteWorkflowAppService` 只保留：
   - 读取仓储
   - 发布事件
   - 调用传播服务
   - 调用新的 Logic 构造器
   - 调用决策服务
   - 装配 workflow artifacts
4. `RuleTargetAppService` 只保留：
   - 构造 `RuleTarget`
   - 持久化
   - 调用新的 Logic 候选构造器
   - 发布事件

## 不做的事

- 不重写 `DecisionAppService` 的契约
- 不把现有 `IChangeCandidateMarker` / `IRewriteDecisionMaker` 删除
- 不扩大到仓库全部 Application Contracts 的 DTO 纯化
- 不增加新依赖

## 预期收益

- 去掉 `Application.Services` 中重复的规则装配代码
- 去掉 `Application.Services` 中直接构造决策评估值对象的代码
- 让“可复用单阶段能力”回到 `Logic`
- 降低后续新增 workflow / marking use case 时的重复扩散

## 计划改动

### Logic

- 新增 `IRuleTargetCandidateBuilder`
- 新增 `RuleTargetCandidateBuilder`
- 新增 `RuleTargetCandidateBuildInput`
- 新增 `IRewriteDecisionAssessmentBuilder`
- 新增 `RewriteDecisionAssessmentBuilder`
- 新增 `RewriteDecisionAssessmentBuildInput`
- 新增 `RewriteDecisionAssessment`

### Application

- 重构 `RewriteWorkflowAppService`
- 重构 `RuleTargetAppService`

### Infrastructure

- 在 `ServiceCollectionExtensions` 注册新增 Logic 服务

### Tests

- 为新的 Logic 构造器补充测试
- 更新 workflow / architecture tests 中的构造注入

## 验证顺序

1. `dotnet build .\src\Domain\Domain.csproj`
2. `dotnet build .\src\Logic\Logic.csproj --no-restore`
3. `dotnet build .\src\Application\Application.csproj --no-restore`
4. `dotnet build .\src\Infrastructure\Infrastructure.csproj --no-restore`
5. `dotnet build .\tests\ArchitectureTests\ArchitectureTests.csproj --no-restore`
6. `dotnet run --project .\tests\ArchitectureTests\ArchitectureTests.csproj --no-build`
7. `dotnet build .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-restore`
8. `dotnet test .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-build`

## 完成标准

- `RewriteWorkflowAppService` 不再直接 new 决策评估值对象
- `RewriteWorkflowAppService` / `RuleTargetAppService` 不再直接构造 `RuleSet` / `EnabledRule`
- 对应逻辑统一落入 `Logic`
- 分层构建通过
- `ArchitectureTests` 通过
- `Isolation.AnalysisTests` 通过

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
