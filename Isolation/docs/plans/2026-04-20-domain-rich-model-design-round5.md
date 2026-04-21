# 2026-04-20 第5轮 Domain primitive obsession 清理设计：RewriteArtifact / FileChange / StaticReasoningEvidence

## 1. 本轮目标

继续按“小步、闭环、复用现有值对象”的方式收口剩余字符串热点。本轮聚焦执行产物与验证证据中仍裸流动的 target 语义：

- `CodeRewriteArtifact.TargetName`
- `FileChange.AffectedTargets`
- `StaticReasoningEvidence.SubjectName`

它们都不是普通文案，而是明确的“目标标识”语义，适合继续统一到 `TargetName`。

## 2. 设计依据

### 网络资料
- Martin Fowler — Domain Model  
  <https://martinfowler.com/eaaCatalog/domainModel.html>
- Martin Fowler — DDD Aggregate  
  <https://martinfowler.com/bliki/DDD_Aggregate.html>
- Microsoft Learn — Tactical DDD  
  <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
- ABP — Domain Driven Design  
  <https://abp.io/docs/latest/framework/architecture/domain-driven-design>

### 本地资料
- `docs/DDD/领域模型设计Checklist.md`
- `docs/DDD/DDD战术设计.md`
- `docs/DDD/src实际设计与事件风暴理想设计差异.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\Aggregate-DDD\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\catalog-of-terms\ValueObject-DDD\README.md`

## 3. 热点扫描结论

本轮优先级最高的剩余点是“执行链路里 target 已在上游值对象化，但到产物/证据层又退回字符串”：

1. `CodeRewriteArtifact.TargetName` 仍是字符串
2. `FileChange.AffectedTargets` 仍是 `IReadOnlyCollection<string>`
3. `StaticReasoningEvidence.SubjectName` 仍是字符串

如果不继续收口，会形成：
- `PlanTarget / ChangeCandidate` 已使用 `TargetName`
- 到 `RewriteArtifact / RewriteResult / VerificationEvidence` 又回退为裸字符串

这会削弱统一语言在执行与验证边界的连续性。

## 4. 方案比较

### 方案 A：继续保持字符串，仅在外部比较时小心处理
优点：最省改动。  
缺点：没有建模收益，继续让 target 语义在领域边界退化。

### 方案 B：复用 `TargetName`，保留字符串投影
做法：
- `CodeRewriteArtifact` 内部持有 `TargetName`
- `FileChange` 内部持有 `IReadOnlyCollection<TargetName>`
- `StaticReasoningEvidence` 内部持有 `TargetName`
- 对外仍保留字符串投影属性，兼容现有 Application/DTO

**本轮采用方案 B。**

## 5. 本轮设计

### 5.1 Rewrite Artifact
- `CodeRewriteArtifact.TargetNameValue : TargetName`
- 保留 `TargetName : string`

### 5.2 Rewrite Result
- `FileChange.AffectedTargetValues : IReadOnlyCollection<TargetName>`
- 保留 `AffectedTargets : IReadOnlyCollection<string>`
- 支持字符串与 `TargetName` 构造输入

### 5.3 Verification Evidence
- `StaticReasoningEvidence.SubjectNameValue : TargetName`
- 保留 `SubjectName : string`

### 5.4 本轮暂不处理
- `BehaviorEvidence.ScenarioName`：它更偏“场景标签”而不是目标标识
- `PropagationFactReference.SourceNodeId/TargetNodeId/Kind`：其语义更接近图节点标识与关系类型，若收口应另建专用值对象，不适合在本轮硬套 `TargetName`

## 6. 预期收益

- `PlanTarget -> CodeRewriteArtifact -> FileChange -> StaticReasoningEvidence` 的 target 统一语言链进一步闭环
- 执行结果与验证证据不再把 target 语义退化成裸字符串
- 继续保持对外兼容，避免 Application/Contracts 大面积改签名

## 7. 改动范围

### Domain
- `src/Domain/Rewrite/Artifacts/RewriteArtifactModels.cs`
- `src/Domain/Execution/RewriteResult.cs`
- `src/Domain/Output/Verification/VerificationEvidence.cs`

### Tests
- `tests/Isolation.AnalysisTests/Workflow/ValueObjectClosureTests.cs`
- 必要时补执行/验证链路断言

## 8. 验证计划

1. `dotnet build .\src\Domain\Domain.csproj`
2. `dotnet build .\src\Logic\Logic.csproj --no-restore`
3. `dotnet build .\src\Application\Application.csproj --no-restore`
4. `dotnet build .\src\Infrastructure\Infrastructure.csproj --no-restore`
5. `dotnet build .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-restore`
6. `dotnet build .\tests\ArchitectureTests\ArchitectureTests.csproj --no-restore`
7. `dotnet test .\tests\Isolation.AnalysisTests\Isolation.AnalysisTests.csproj --no-build`
8. `dotnet run --project .\tests\ArchitectureTests\ArchitectureTests.csproj --no-build`

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
