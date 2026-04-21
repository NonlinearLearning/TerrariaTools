# 2026-04-20 第4轮 Domain primitive obsession 清理设计：AnalysisInputDescriptor / AnalysisCpgSnapshot / Analysis & Marking DomainEvents

## 1. 本轮目标

在前三轮基础上，继续沿着“路径语义 + 目标名语义”做最小闭环收口，聚焦仍然裸字符串流动、且跨聚合/事件边界传播的热点：

- `AnalysisInputDescriptor.SourcePath`
- `AnalysisCpgSnapshot.EntrySymbol`
- `AnalysisSnapshotBuiltDomainEvent.EntrySymbol`
- `ProgramFactPublishedDomainEvent.SubjectName`
- `RuleTargetIdentifiedDomainEvent.TargetName`

本轮不追求全域改造，只做一条连续语义链的最小强化。

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

第4轮优先级最高的剩余字符串热点有两类：

1. **路径字符串仍在分析入口层裸流动**  
   `AnalysisInputDescriptor.SourcePath` 仍自行 `Trim()`，没有与 `WorkspacePath/DocumentPath/SolutionPath` 形成统一路径语义。

2. **目标名/入口名在分析-标记-事件链仍裸流动**  
   `AnalysisCpgSnapshot.EntrySymbol`、`ProgramFactPublishedDomainEvent.SubjectName`、`RuleTargetIdentifiedDomainEvent.TargetName` 都本质上承载“目标/主体名”语义，却仍是字符串。

## 4. 方案比较

### 方案 A：直接新造一批 Analysis 专用值对象
优点：名字更贴近分析上下文。  
缺点：与前几轮已经引入的 `TargetName`/`WorkspacePath` 重复，抽象膨胀。

### 方案 B：复用现有值对象，最小闭环收口
做法：
- `AnalysisInputDescriptor` 内部持有 `WorkspacePath`
- `AnalysisCpgSnapshot` 内部持有 `TargetName`
- 相关 DomainEvent 内部持有 `TargetName`
- 对外继续保留字符串投影属性

优点：
- 不新增无必要抽象
- 与前几轮统一语言对齐
- 对上层 DTO / AppService 几乎零破坏

**本轮采用方案 B。**

## 5. 本轮设计

### 5.1 统一路径语义
- `AnalysisInputDescriptor.SourcePathValue : WorkspacePath`
- 保留 `SourcePath : string` 投影
- `Create` 同时支持 `string` 与 `WorkspacePath`

### 5.2 统一目标名语义
- `AnalysisCpgSnapshot.EntrySymbolValue : TargetName`
- `AnalysisSnapshotBuiltDomainEvent.EntrySymbolValue : TargetName`
- `ProgramFactPublishedDomainEvent.SubjectNameValue : TargetName`
- `RuleTargetIdentifiedDomainEvent.TargetNameValue : TargetName`
- 保留原字符串投影属性，兼容现有上层调用

### 5.3 边界策略
- 对 `AnalysisCompositeLayerSnapshot.CompositionName` 暂不改造；它更像组合层名，而不是明确的目标名
- 只在确定属于“目标/主体标识”语义的地方复用 `TargetName`

## 6. 预期收益

- `WorkspaceContext -> AnalysisInputDescriptor` 的路径语义进一步统一
- `Analysis -> Marking -> Workflow Event` 的目标名语义继续闭环
- 减少分析入口与领域事件边界上的 primitive obsession
- 不引入新依赖，不扩大上层签名改造面

## 7. 改动范围

### Domain
- `src/Domain/Analysis/AnalysisInputDescriptor.cs`
- `src/Domain/Analysis/AnalysisCpgSnapshot.cs`
- `src/Domain/Analysis/Events/AnalysisSnapshotBuiltDomainEvent.cs`
- `src/Domain/Analysis/Events/ProgramFactPublishedDomainEvent.cs`
- `src/Domain/Marking/Events/RuleTargetIdentifiedDomainEvent.cs`

### Tests
- `tests/Isolation.AnalysisTests/Workflow/ValueObjectClosureTests.cs`
- 相关阶段事件测试如有必要补断言

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
