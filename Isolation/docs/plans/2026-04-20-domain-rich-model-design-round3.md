# 2026-04-20 第三轮领域模型强化设计：ProjectDescriptor / ReferenceDescriptor / InputDescriptor / PropagationTrace / 传播事件

## 1. 背景

前两轮已经完成：
- `RewritePlan / VerificationEvidence / RunReport` 聚合增强
- `WorkspaceContext / ChangeCandidate / SolutionPath / TargetName / MemberSignature` 收口

当前剩余的 primitive obsession 已经形成一条连续链：

- `ProjectDescriptor.Path` 仍是字符串
- `ReferenceDescriptor.Name / Version` 仍是字符串
- `InputDescriptor.SourcePath` 仍是字符串
- `PropagationTrace.SourceName / TargetName` 仍是字符串
- 多个传播阶段 `DomainEvent` 里的 `targetName / parentTargetName / linkedActionName` 仍是字符串

如果这条链不继续收口，就会出现：
- 上游聚合已值对象化，下游传播与事件又退回字符串
- 统一语言不能在事件边界上持续保持强约束
- 路径与引用语义仍无法集中表达和比较

## 2. 设计依据

### 网络资料
- Martin Fowler — Domain Model  
  <https://martinfowler.com/eaaCatalog/domainModel.html>
- Martin Fowler — DDD Aggregate  
  <https://martinfowler.com/bliki/DDD_Aggregate.html>
- Martin Fowler — Bounded Context  
  <https://martinfowler.com/bliki/BoundedContext.html>
- Microsoft Learn — Tactical DDD  
  <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
- Microsoft Learn — Microservice domain model  
  <https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model>
- ABP — Domain Driven Design  
  <https://abp.io/docs/latest/framework/architecture/domain-driven-design>

### 本地资料
- `docs/DDD/领域模型设计Checklist.md`
- `docs/DDD/DDD战术设计.md`
- `docs/DDD/src实际设计与事件风暴理想设计差异.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\Aggregate-DDD\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\ValueObject-DDD\README.md`

## 3. 设计目标

### 3.1 路径语义统一
用一个通用工作区路径值对象承接：
- `ProjectDescriptor.Path`
- `InputDescriptor.SourcePath`

为什么不继续全靠 `string`：
- 路径标准化逻辑已经在多个对象里重复出现；
- 这些路径都属于“工作区内部输入路径”语义，而不只是文本。

### 3.2 引用语义显式化
为 `ReferenceDescriptor` 引入：
- `ReferenceName`
- `ReferenceVersion`

目的不是炫技，而是：
- 禁止空白引用名/版本继续裸流动
- 把比较与标准化收口到领域值对象

### 3.3 传播轨迹与传播事件保持统一语言
把：
- `PropagationTrace.SourceName`
- `PropagationTrace.TargetName`
- 传播阶段各 DomainEvent 的 target 相关字段

升级为内部 `TargetName` 值对象持有，外部保留字符串兼容投影。

## 4. 备选方案

### 方案 A：全字符串保持不动，只加注释
**缺点**：没有任何建模收益。  
**结论**：不选。

### 方案 B：第三轮继续最小闭环收口
做法：
- 只改 Domain 内部持有方式
- 构造函数仍接受字符串，避免上层爆炸
- 增加值对象属性 + 保留字符串投影属性

**优点**：
- 继续遵守最小、可回滚、可验证
- 让“聚合 → 传播 → 事件”统一语言链闭环

**结论**：**本轮采用。**

## 5. 本轮设计

### 5.1 新值对象
- `WorkspacePath`
- `ReferenceName`
- `ReferenceVersion`

### 5.2 模型改造
- `ProjectDescriptor` 持有 `WorkspacePathValue`
- `InputDescriptor` 持有 `SourcePathValue`
- `ReferenceDescriptor` 持有 `ReferenceNameValue / ReferenceVersionValue`
- `PropagationTrace` 持有 `SourceNameValue / TargetNameValue`
- 传播阶段事件持有 `TargetNameValue`，必要时加 `ParentTargetNameValue / LinkedActionNameValue`

### 5.3 兼容策略
- 继续保留：`Path`、`SourcePath`、`Name`、`Version`、`TargetName` 等字符串投影
- DTO、Mapper、Application Service 不需要大规模签名迁移

## 6. 改动范围

### Domain
- `src/Domain/Workspaces/ProjectDescriptor.cs`
- `src/Domain/Workspaces/ReferenceDescriptor.cs`
- `src/Domain/Workspaces/InputDescriptor.cs`
- `src/Domain/Propagation/PropagationModels.cs`
- `src/Domain/Propagation/Events/*.cs`
- 新增：
  - `src/Domain/Workspaces/WorkspacePath.cs`
  - `src/Domain/Workspaces/ReferenceName.cs`
  - `src/Domain/Workspaces/ReferenceVersion.cs`

### Tests
- 新增或补充：
  - `WorkspacePath` 标准化
  - `ProjectDescriptor / InputDescriptor` 值对象兼容投影
  - `ReferenceDescriptor` 值对象兼容投影
  - `PropagationTrace` 与传播事件值对象兼容投影

## 7. 验证计划

1. `dotnet build .\src\Domain\Domain.csproj`
2. `dotnet build .\src\Logic\Logic.csproj --no-restore`
3. `dotnet build .\src\Application\Application.csproj --no-restore`
4. `dotnet build .\src\Infrastructure\Infrastructure.csproj --no-restore`
5. `dotnet build .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-restore`
6. `dotnet build .\tests\ArchitectureTests\ArchitectureTests.csproj --no-restore`
7. `dotnet test .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-build`
8. `dotnet run --project .\tests\ArchitectureTests\ArchitectureTests.csproj --no-build`

## 8. 预期收益

- `WorkspaceContext -> InputDescriptor -> ProjectDescriptor` 的路径语义统一
- `ReferenceDescriptor` 不再是无约束文本对
- `ChangeCandidate -> PropagationTrace -> DomainEvent` 的 target 统一语言链闭环
- 不破坏现有 Application / DTO 主签名

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
