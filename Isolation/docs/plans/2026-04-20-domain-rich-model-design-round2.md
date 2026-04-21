# 2026-04-20 第二轮领域模型强化设计：WorkspaceContext / ChangeCandidate / 原始字符串收口

## 1. 背景

第一轮已经强化了 `RewritePlan / VerificationEvidence / RunReport`。当前剩余最明显短板是：

- `WorkspaceContext` 仍偏“数据 + Add*”
- `ChangeCandidate` 仍偏“状态容器”
- `SolutionPath / TargetName / MemberSignature` 仍主要以裸字符串在 Domain 内流动

这三点叠加后，会导致：

1. 聚合根仍然不能充分主导一致性边界；
2. 关键领域概念仍然容易被任意字符串污染；
3. 部分路径解析逻辑漂在 Logic 层，甚至把 `.sln` 文件路径当目录拼接。

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
- ABP DDD  
  <https://abp.io/docs/latest/framework/architecture/domain-driven-design>

### 本地资料
- `docs/DDD/领域模型设计Checklist.md`
- `docs/DDD/DDD战术设计.md`
- `docs/DDD/src实际设计与事件风暴理想设计差异.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\Aggregate-DDD\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\ValueObject-DDD\README.md`

## 3. 发现的问题

### 3.1 WorkspaceContext
当前 `WorkspaceContext` 的领域行为过薄：
- 只有 `AddProject / AddDocument / AddReference`
- `AnalysisInputDescriptorBuilder` 在 Logic 层自己决定“主输入路径如何解析”
- `DefaultAnalysisSnapshotBuilder` 在 Logic 层自己决定“优先拿哪些文档”
- `Path.Combine(workspaceContext.SolutionPath, project.Path)` 把 solution 文件路径当目录使用，语义并不正确

### 3.2 ChangeCandidate
当前 `ChangeCandidate`：
- 能装原因/标签/传播轨迹，但不主导传播登记语义
- `SetSliceBoundary` 没有防二次冲突写入
- `MarkCoveredByParentAction` 没有防自覆盖/重复覆盖/冲突覆盖
- 传播阶段仍然在外部用裸字符串决定是否同名、是否自传播

### 3.3 Primitive obsession
以下概念在 Domain 中已经足够稳定，应该升级为值对象：
- `SolutionPath`
- `TargetName`
- `MemberSignature`

但当前它们仍以字符串存在，导致：
- trim/规范化规则分散
- 比较语义不集中
- 领域 API 接口签名过于宽松

## 4. 备选方案

### 方案 A：全仓库立即替换所有相关字符串
**优点**：一次性最彻底。  
**缺点**：影响面过大，会把一次模型强化演变成全仓库改签名。  
**结论**：不选。

### 方案 B：只在核心 Domain 模型引入值对象，并保留字符串兼容投影
做法：
- 聚合内部持有值对象
- 对外继续保留 `string` 只读投影属性，避免 Application/Tests 全线爆炸
- 逐步把比较、解析、拼接收回聚合和值对象

**优点**：
- 收口真实业务语义
- diff 小，可回滚
- 适合第二轮增量演进

**结论**：**本轮采用。**

## 5. 本轮设计

### 5.1 新值对象

#### `SolutionPath`
职责：
- 标准化 solution 路径
- 判断 solution 基目录
- 解析相对项目/文档路径

#### `TargetName`
职责：
- 标准化目标名称
- 统一同名比较
- 支撑 `ChangeCandidate` / `PlanTarget` 的目标语义

#### `MemberSignature`
职责：
- 标准化成员签名
- 避免 `null/空白/Trim` 逻辑散落

### 5.2 WorkspaceContext 富化

把以下行为收回聚合：
- `RegisterProject / RegisterDocument / RegisterReference`
- `ResolveAnalysisSourcePath(requestedSourcePath)`
- `ResolveAnalysisDocuments(maxCount)`

聚合职责升级后，Logic 层只调用聚合，不再自己猜：
- 该优先用 project/document/solution 中哪个输入
- 相对路径该怎样相对 solution 解析

### 5.3 ChangeCandidate 富化

把以下行为收回聚合：
- `CreateFromRuleTarget(...)`
- `MatchesTarget(...)`
- `RegisterPropagation(...)`
- `SetSliceBoundary(...)` 改为冲突保护式写入
- `MarkCoveredByParentAction(...)` 增加一致性约束

目标：让候选不再只是“能放状态”，而是“能保护候选传播与覆盖规则”的中心。

### 5.4 保持兼容的方式

- 对外 DTO 仍保留字符串字段
- Domain 模型增加 `XXXValue` 属性 + 兼容 `string` 投影属性
- 先不追着改所有事件/DTO/外部接口

## 6. 本轮改动范围

### Domain
- `src/Domain/Workspaces/WorkspaceContext.cs`
- `src/Domain/Propagation/ChangeCandidate.cs`
- `src/Domain/Execution/RewritePlan.cs`（仅补 `PlanTarget` 值对象化）
- 新增：
  - `src/Domain/Workspaces/SolutionPath.cs`
  - `src/Domain/Common/TargetName.cs`
  - `src/Domain/Common/MemberSignature.cs`

### Logic
- `src/Logic/Analysis/AnalysisInputDescriptorBuilder.cs`
- `src/Logic/Analysis/DefaultAnalysisSnapshotBuilder.cs`
- `src/Logic/Workspaces/WorkspaceContextBuilder.cs`
- `src/Logic/Propagation/ImpactPropagator.cs`
- `src/Logic/Workflow/RewritePlanCompiler.cs`（仅适配值对象化 PlanTarget）

### Tests
- 新增聚焦测试：
  - `WorkspaceContext` 路径解析与去重语义
  - `ChangeCandidate` 自传播/覆盖保护
  - `PlanTarget` 的值对象兼容投影

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

- `WorkspaceContext` 从容器升级为“输入边界解析中心”
- `ChangeCandidate` 从状态袋升级为“候选传播与覆盖规则中心”
- `SolutionPath / TargetName / MemberSignature` 从 primitive/string 升级为显式值对象
- 解决一处实际路径语义问题：把 `.sln` 文件当目录拼接

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
