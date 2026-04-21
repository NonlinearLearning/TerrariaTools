# 2026-04-20 领域聚合强化与值对象收口设计

## 1. 背景

当前仓库已经有聚合根外形，但在以下位置仍明显偏贫血：

- `WorkspaceContext` 主要是路径/集合容器，行为只有 `Add*` 与 `ValidateConsistency`。
- `RewritePlan` 仍由外部编译器主导组装，聚合内不变量主要停留在“重复 target/action”校验。
- `VerificationEvidence` 只负责收集证据，风险摘要仍靠外部调用 `UpdateRiskSummary(RiskSummary.FromEvidence(...))` 回填。
- `RunReport` 由装配器在外部根据执行失败数推导结论，聚合自身没有掌握“证据 -> 结论”的核心语义。

这会直接拉低：

- 聚合/一致性边界强度
- 富领域模型程度
- 值对象建模完整度

## 2. 设计依据

### 外部资料

- Martin Fowler — Domain Model  
  <https://martinfowler.com/eaaCatalog/domainModel.html>
- Martin Fowler — DDD Aggregate  
  <https://martinfowler.com/bliki/DDD_Aggregate.html>
- Martin Fowler — Bounded Context  
  <https://martinfowler.com/bliki/BoundedContext.html>
- Microsoft Learn — Use tactical DDD to design microservices  
  <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
- Microsoft Learn — Designing a microservice domain model  
  <https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model>
- Vaughn Vernon — Effective Aggregate Design  
  <https://www.dddcommunity.org/wp-content/uploads/files/pdf_articles/Vernon_2011_1.pdf>
- ABP Framework — Domain Driven Design  
  <https://abp.io/docs/latest/framework/architecture/domain-driven-design>

### 本地资料

- `docs/DDD/领域模型设计Checklist.md`
- `docs/DDD/DDD战术设计.md`
- `docs/DDD/src实际设计与事件风暴理想设计差异.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\Aggregate-DDD\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\ValueObject-DDD\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\src\BuildingBlocks\Domain\ValueObject.cs`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\framework\src\Volo.Abp.Ddd.Domain\Volo\Abp\Domain\Entities\AggregateRoot.cs`

## 3. 可选方案

### 方案 A：大范围引入统一 ValueObject 基类并重构全部 Target/Path/Signature

**优点**
- 值对象体系最完整
- primitive obsession 改善最大

**缺点**
- 影响 `Domain / Application / Mapper / Tests` 多层
- 当前回归成本高，容易把一次“模型强化”做成一次“大搬家”

**结论**：本轮不选。

### 方案 B：围绕现有关键聚合补“强一致性行为”，并引入最小值对象强化

目标：在不破坏现有接口主形状的前提下，把关键推导和不变量从 Logic/Assembler 收回 Domain。

**优点**
- 直接提升聚合根含金量
- diff 小、容易回滚
- 更符合仓库“最小、可验证修改”约束

**缺点**
- 不能一次性解决所有 primitive obsession

**结论**：**本轮推荐并执行。**

### 方案 C：优先做事件链/Outbox 化，先不动聚合行为

**优点**
- 事件能力提升明显

**缺点**
- 用户当前痛点是“聚合不够硬、模型不够富”而不是集成事件
- 偏离本轮主目标

**结论**：延期。

## 4. 本轮执行设计

### 4.1 聚合强化目标

#### `RewritePlan`
- 让聚合自己负责注册计划项、分配顺序、登记冲突、判断是否可执行。
- 让“无冲突、至少一个计划项、排序唯一且连续”成为聚合内约束，而不是由外部零散维护。

#### `VerificationEvidence`
- 让聚合自己负责接纳证据后刷新风险摘要。
- 让“风险摘要来自证据真相”成为聚合不变量，避免外部忘记 `UpdateRiskSummary(...)`。

#### `RunReport`
- 让聚合/值对象自己根据 `RewriteResult + VerificationEvidence` 推导报告摘要与审计结论。
- 装配器只负责调用，不再承载核心业务判断。

### 4.2 值对象强化目标

#### `RiskSummary`
- 把当前字符串 `LevelName` 收口为明确的 `RiskLevel` 枚举 + 显式工厂。
- 保留 `LevelName` 作为兼容只读投影，避免 Application DTO 全线破坏。
- 让风险级别成为真正可推理的领域值，而不是任意字符串。

#### `ReportSummary`
- 增加从执行结果/证据推导的工厂方法。
- 把“摘要计数 + highlights”从外部拼装迁回值对象自身。

### 4.3 不做的事

- 不重命名 `TargetName` / `SolutionPath` / `MemberSignature` 等所有字符串。
- 不引入新的依赖。
- 不改变四层依赖方向。
- 不顺手大规模调整 Application DTO。

## 5. 代码改动点

### Domain
- `src/Domain/Execution/RewritePlan.cs`
- `src/Domain/Output/Verification/VerificationEvidence.cs`
- `src/Domain/Output/Audit/RunReport.cs`

### Logic
- `src/Logic/Workflow/RewritePlanCompiler.cs`
- `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs`
- `src/Logic/Workflow/RunReportAssembler.cs`

### Tests
- `tests/Isolation.AnalysisTests/Workflow/DddP1WorkflowTests.cs`
- 新增一个聚合行为测试文件，覆盖：
  - plan 自动排序/冲突阻断
  - evidence 自动刷新风险
  - report 从 evidence/result 推导结论

## 6. 验证计划

按仓库规则串行执行：

1. `dotnet build .\src\Domain\Domain.csproj`
2. `dotnet build .\src\Logic\Logic.csproj --no-restore`
3. `dotnet build .\src\Application\Application.csproj --no-restore`
4. `dotnet build .\src\Infrastructure\Infrastructure.csproj --no-restore`
5. `dotnet build .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-restore`
6. `dotnet build .\tests\ArchitectureTests\ArchitectureTests.csproj --no-restore`
7. `dotnet test .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-build`
8. `dotnet run --project .\tests\ArchitectureTests\ArchitectureTests.csproj --no-build`

## 7. 预期收益

- 聚合边界从“可被外部驱动的集合包装器”提升到“自己维持一致性中心”。
- 风险与报告推导回到 Domain，减少 Logic/Application 继续吞业务规则。
- `RiskSummary` 从自由字符串升级为受限值对象，降低 primitive obsession。

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
