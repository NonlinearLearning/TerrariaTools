# DDD架构取舍指导

## 1. 目的

本文是 `Isolation` 仓库评估和收敛 DDD 架构取舍的**规则约束**，不是泛泛而谈的读书笔记。

它同时约束两类目标：

- DDD 纯度：领域语义、聚合边界、应用层纯度、基础设施隔离。
- 工程适配性：可测试、可验证、可演进、可替换、可交付。

本仓库后续讨论“架构是否合理”“当前分层该如何扣分”“某段代码该放哪一层”，默认都以本文为判断尺子。

## 2. 资料依据

### 2.1 外部资料

- Martin Fowler, `Service Layer`
  - <https://martinfowler.com/eaaCatalog/serviceLayer.html>
- Martin Fowler, `Domain Model`
  - <https://martinfowler.com/eaaCatalog/domainModel.html>
- Martin Fowler, `Anemic Domain Model`
  - <https://martinfowler.com/bliki/AnemicDomainModel.html>
- Microsoft Learn, `Use tactical DDD to design microservices`
  - <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
- ABP, `Application Services Best Practices & Conventions`
  - <https://abp.io/docs/latest/Best-Practices/Application-Services>
- ABP, `Domain Services`
  - <https://abp.io/docs/latest/framework/architecture/domain-driven-design/domain-services>
- ABP, `Repository Best Practices & Conventions`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/repositories>
- ABP, `Data Transfer Objects Best Practices & Conventions`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/data-transfer-objects>
- Thoughtworks, `Architectural fitness function`
  - <https://www.thoughtworks.com/en-us/radar/techniques/architectural-fitness-function>
- Thoughtworks, `Building Evolutionary Architectures`
  - <https://www.thoughtworks.com/content/dam/thoughtworks/documents/books/bk_building_evolutionary_architectures_second_edition_free_chapter.pdf>

### 2.2 本地参考仓资料

- `C:\Users\shan\Downloads\api开源教程学习\CleanArchitecture-main\src\Clean.Architecture.UseCases\README.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0010-use-clean-architecture-for-writes.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0011-create-rich-domain-models.md`
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0012-use-domain-driven-design-tactical-patterns.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\module-architecture.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\application-services.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\data-transfer-objects.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\entities.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\repositories.md`

### 2.3 综合结论

综合上述资料后，本仓库采用以下平衡立场：

- 不追求教条式“层越薄越纯越好”。
- 不接受“工程方便”作为长期污染边界的理由。
- 允许阶段性妥协，但必须能说清楚妥协点、收益、代价、收敛方向。
- 评估架构时，既看 DDD 建模质量，也看架构是否有自动化约束来持续守边界。

## 3. 本仓库的核心判断准则

### 3.1 依赖方向先于细节纯度

先守住大方向，再治理细节纯度。

本仓库当前主方向是：

```text
Domain
  ↑
Logic
  ↑
Application
  ↑
Infrastructure
```

强制规则：

- `Domain` 不能依赖 `Logic / Application / Infrastructure`。
- `Logic` 只能依赖 `Domain`。
- `Application` 只负责用例编排和边界契约。
- `Infrastructure` 只负责技术实现，不反向决定业务语义。

### 3.2 Application 只做 orchestration

这是当前仓库最重要的 DDD 纯度约束之一。

允许：

- 接收请求 DTO。
- 调用 `Domain` 和 `Logic` 暴露的稳定能力。
- 组织流程顺序。
- 做边界 DTO 映射。

不允许：

- 在 `AppService` 内直接拼装可复用规则对象。
- 在 `AppService` 内临时定义本应沉到 `Logic` 或 `Domain` 的业务导出规则。
- 把 `AppService` 当成“流程方便层”继续堆推理细节。

当前仓库的典型警示例子：

- `src/Application/Services/RewriteWorkflowAppService.cs`
  - 传播、决策、工作流装配已经切回编排主链，`AppService` 当前主要负责 request -> orchestration -> DTO mapping。
  - 决策评估解释已经继续下沉到 `Domain.Decision.RewriteDecisionAssessmentPolicy`。
  - 后续关注点集中在编排壳是否继续变胖，而不是旧的同模块 `AppService` 串调问题。

### 3.3 Domain 优先承载行为和不变量

综合 Fowler、Microsoft 和本地 ADR 经验，本仓库默认采用**富领域模型优先**。

强制规则：

- 聚合根、实体、值对象优先承载业务规则。
- 领域对象公开 API 应体现业务动作，而不是只暴露 settable data bag。
- 领域事件应由领域行为自然触发，而不是主要由应用层拼接。

允许例外：

- 纯事实快照。
- 兼容旧接口的载体对象。
- 只读投影或报表结果。

但例外必须能说明它为何不是业务不变量的承载点。

### 3.4 Logic 是可复用单阶段能力层，不是第二个 Application

结合本仓库现状和 Clean Architecture / ABP 经验，`Logic` 的定位不是“杂物层”，而是稳定的复用能力层。

适合放在 `Logic`：

- 单阶段推导。
- 可被多个应用服务复用的 builder / assembler / evaluator。
- 消费领域对象并产出中间结果的逻辑。

不适合放在 `Logic`：

- 纯技术适配。
- 领域不变量本身。
- 又大又散的“超级工作流总管”。

当前仓库的典型热点：

- `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs`
- `src/Logic/Workflow/Events/WorkflowEventSequenceBuilder.cs`

这些位置可以保留为工作流支撑能力，但不得继续无上限吸收跨阶段复杂度。

当前收敛进展：

- `IRewriteWorkflowArtifactAssembler` 已收敛为单一公开入口 `Assemble(RewriteWorkflowAssemblyInput)`。
- 阶段拆分细节保留在 `Logic.Workflow` 内部，不再向外暴露并行的阶段装配公开接口。

### 3.5 Contracts 是边界契约，不是 Domain 直通车

综合 ABP `Application.Contracts` 经验和当前仓库现状，`Application.Contracts` 应优先保持边界独立。

强制规则：

- DTO 只表达边界输入输出。
- DTO 不承载业务逻辑。
- DTO 不应无节制直接暴露 `Domain.*` 类型。

当前仓库的已知妥协：

- `Application.Contracts` 当前主体已切换到 `Contract*` / DTO 自有边界类型。
- `tests/ArchitectureTests/Program.cs` 已对 `Application.Contracts` 目录做 `Domain.*` 直接引用检查。
- 后续关注点应从“是否仍直通 Domain 类型”收敛为“契约是否继续泄露领域语义命名、是否把应用内部中间模型回推到边界”。

这说明 Contracts 纯化已有明显进展。后续评估应继续扣紧边界语义，而不是沿用旧结论重复扣分。

### 3.6 架构约束必须自动化

Thoughtworks 的 fitness function 经验在本仓库是硬约束，不是可选项。

强制规则：

- 重要架构边界应有自动化守护。
- 文档约束如果可以转成架构测试，优先转成测试。
- 新的阶段性妥协如果预计持续存在，应补充相应的 guard rail。

当前仓库已有良好基础：

- `tests/ArchitectureTests/Program.cs`

后续应继续把以下内容纳入自动检查：

- `Application.Contracts` 对 `Domain.*` 的直接引用上限。
- `Application.Services` 是否直接构造低等级规则对象。
- `Logic.Workflow` 是否继续失控扩张。
- `IRewriteWorkflowArtifactAssembler` 是否继续保持单一公开装配入口。
- `RewriteDecisionAssessment` / `RewriteDecisionAssessmentPolicy` 是否继续留在 `Domain.Decision`。

## 4. DDD 评分尺子

总分建议为 `100` 分。该分数用于评估趋势和优先级，不用于追求形式主义打分。

### 4.1 领域语言与上下文边界，15 分

看术语是否稳定、上下文是否清楚、文档与代码是否一致。

### 4.2 富领域模型与聚合行为，20 分

看行为是否进入聚合和值对象，不变量是否由领域对象保护。

### 4.3 分层纯度与依赖方向，20 分

看依赖方向是否稳定，Application 是否只是 orchestration，Contracts 是否泄露 Domain。

### 4.4 Logic 复用层质量，15 分

看可复用阶段能力是否有清晰落点，是否避免了“胖 Application”与“胖 Logic”双重问题。

### 4.5 可演进性与 fitness functions，15 分

看边界约束是否自动化、是否能持续防回退。

### 4.6 基础设施隔离与替换成本，15 分

看技术实现是否被隔离，替换底层实现时是否不会击穿业务主链。

## 5. 本仓库的默认取舍顺序

当纯度、速度、改造成本冲突时，默认按下面顺序决策：

1. 先保住 `Domain -> Logic -> Application -> Infrastructure` 方向。
2. 再保住 `Application` 只做 orchestration。
3. 再推动 `Domain` 富化并收敛不变量。
4. 再纯化 `Application.Contracts`。
5. 最后再考虑更细的子层拆分或新项目拆分。

这意味着：

- 不要为了“看起来更 DDD”而先做大规模项目拆分。
- 也不要因为“当前能跑”就容忍应用层继续变胖。

## 6. 允许的阶段性妥协

以下妥协可以短期存在，但必须记录并收敛：

- `Application.Contracts` 暂时复用少量 `Domain.Shared` 语义等价物。
- `Logic.Workflow` 暂时集中若干工作流支撑能力，但对外入口应保持单一。
- `ArchitectureTests` 尚未覆盖到的边界先通过文档约束兜底。

以下妥协默认不允许：

- 让 `AppService` 长期持有规则构造、规则推理、规则评估职责。
- 让 DTO 持有逻辑。
- 让基础设施类型上浮到 `Application.Contracts`。

## 7. 使用方式

后续遇到以下问题时，先回本文判断：

- 某段逻辑该放 `Application`、`Logic` 还是 `Domain`。
- 某个 DTO 是否已经越界。
- 某个类的增加是在修边界还是在制造新中间层。
- 某项重构应该先做什么。

如果新设计违反本文约束，必须同时写清楚：

- 为什么现在必须违反。
- 违反后短期收益是什么。
- 风险落在哪。
- 计划如何收敛。

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：仓库约束文档。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `AGENTS.md`
- `tests/ArchitectureTests/Program.cs`
- `.omx/plans/*.md`
- `log/*.log`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
