# 2026-04-21 Isolation DDD / docs-as-code 证据清单、热点盘点与批次计划

## 1. 任务目标

基于 **权威 DDD / docs-as-code / 架构 fitness function 资料** 与本地参考仓示例，先产出适用于 `Isolation` 的证据清单、热点盘点与批次计划，再把本轮低风险收口项同步落到设计文档与测试锚点。

本轮只做低风险、可验证的收口：

1. 修正文档里对仓库外 authoring sidecar 的硬依赖表述；
2. 为核心设计文档补一层 docs-as-code 回归测试守护。

## 2. 外部权威资料证据清单

> 2026-04-21 检索；链接用于说明“为什么这样收口”，不是把外部模板照搬进仓库。

### 2.1 DDD / 分层 / 富领域模型

- Martin Fowler — `Service Layer`
  - <https://martinfowler.com/eaaCatalog/serviceLayer.html>
  - 结论：Application 应保持薄编排层，不吸收领域规则与技术实现。
- Martin Fowler — `Domain Model`
  - <https://martinfowler.com/eaaCatalog/domainModel.html>
  - 结论：核心业务语义与规则应优先放入领域模型，而不是散落在流程脚本里。
- Martin Fowler — `Anemic Domain Model`
  - <https://martinfowler.com/bliki/AnemicDomainModel.html>
  - 结论：避免把 Domain 退化成纯数据袋，规则与不变量不能长期停留在应用层。
- Microsoft Learn — `Use tactical DDD to design microservices`
  - <https://learn.microsoft.com/en-us/azure/architecture/microservices/model/tactical-domain-driven-design>
  - 结论：聚合、值对象、领域事件与仓储边界要稳定，Application 负责编排而非复制领域真相。
- ABP — `Module Architecture Best Practices`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/module-architecture>
  - 结论：`Application.Contracts` 保持边界契约，`Application` 依赖 `Domain`，基础设施不反向拉高层。
- ABP — `Application Services Best Practices & Conventions`
  - <https://abp.io/docs/latest/Best-Practices/Application-Services>
  - 结论：应用服务应围绕聚合根 / 用例编排；输入输出使用 DTO；不直接暴露实体。
- ABP — `Data Transfer Objects Best Practices & Conventions`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/data-transfer-objects>
  - 结论：DTO 放在 contracts 边界，保持无业务逻辑。
- ABP — `Entity Best Practices & Conventions`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/entities>
  - 结论：实体 / 聚合维持自洽与一致性，通过行为保护状态。
- ABP — `Repository Best Practices & Conventions`
  - <https://abp.io/docs/latest/framework/architecture/best-practices/repositories>
  - 结论：仓储接口属于领域层，应用代码不要把查询能力泄露成任意 IQueryable 风格。

### 2.2 docs-as-code / 持续文档同步

- Write the Docs — `Docs as Code`
  - <https://www.writethedocs.org/guide/docs-as-code/>
  - 结论：文档应与代码进入同一版本控制与评审链路。
- GitLab Docs — `Documentation workflow`
  - <https://docs.gitlab.com/development/documentation/workflow/>
  - 结论：影响行为 / 接口 / 架构的代码变更，应同步更新文档并在同一交付链路闭环。
- Google AIP-192 — `Documentation`
  - <https://google.aip.dev/192>
  - 结论：公共契约文档要完整、清晰、可追踪，避免“代码有了但文档没跟上”。
- Thoughtworks — `Architectural fitness function`
  - <https://www.thoughtworks.com/en-us/radar/techniques/architectural-fitness-function>
  - 结论：关键架构规则应尽量自动化，而不是长期停留在口头和 Markdown。
- Thoughtworks — `Fitness function-driven development`
  - <https://www.thoughtworks.com/en-au/insights/articles/fitness-function-driven-development>
  - 结论：把架构/文档同步规则持续变成可执行守护，可以降低设计漂移。

## 3. 本地参考仓证据清单

- `C:\Users\shan\Downloads\api开源教程学习\CleanArchitecture-main\src\Clean.Architecture.UseCases\README.md`
  - 证据：Use Cases / Application Services 是相对薄的一层，主要包裹领域模型。
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0010-use-clean-architecture-for-writes.md`
  - 证据：命令侧使用整洁架构，把领域逻辑从 API / 基础设施中隔离出来。
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0011-create-rich-domain-models.md`
  - 证据：优先富领域模型，让状态通过行为变化。
- `C:\Users\shan\Downloads\api开源教程学习\modular-monolith-with-ddd-master\docs\architecture-decision-log\0012-use-domain-driven-design-tactical-patterns.md`
  - 证据：聚合、实体、值对象、领域事件、仓储、领域服务是稳定战术构件。
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\module-architecture.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\application-services.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\data-transfer-objects.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\entities.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\framework\architecture\best-practices\repositories.md`
  - 证据：模块分层、Application.Contracts、应用服务、DTO、实体和仓储的边界约束都适合映射到 `Isolation`。

## 4. 当前仓库热点盘点

### 4.1 已经做对的事实

- `src/Domain` / `src/Logic` / `src/Application` / `src/Infrastructure` 四层主线已经稳定存在；
- `src/Application/Services/RewriteWorkflowAppService.cs` 当前以 orchestration 为主；
- `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs` 已收敛为单一装配入口；
- `tests/ArchitectureTests/Program.cs` 已承担一部分架构 fitness function；
- `docs/约束/DDD架构取舍指导.md`、`docs/约束/代码对齐文档约束.md`、`docs/约束/文档对齐代码约束.md` 已把“文档进入实现闭环”说清楚。

### 4.2 当前最明显的 docs-as-code 漂移热点

1. **核心设计文档仍要求不存在的本地 authoring sidecar**
   - 位置：
     - `docs/架构设计.md`
     - `docs/DDD/src目录分层说明.md`
   - 漂移表现：
     - 文档要求显式加载 `ai-rules/common/prompt-spec-writing.mdc`
     - 文档要求同步检查 `ai-rules/`、`.codex/skills/`、`.agents/skills/`
   - 当前事实：
     - `Isolation/ai-rules` 不存在
     - `Isolation/.codex/skills` 不存在
     - `Isolation/.agents/skills` 不存在
   - 风险：
     - 文档把仓库外依赖写成仓库内硬前置条件，导致维护指令失真。

2. **核心设计文档缺少专门的 docs-as-code 自动化守护**
   - 现状：ArchitectureTests 已守代码边界，但尚未把“核心设计文档必须绑定真实锚点、不得依赖缺失 sidecar”沉到自动测试。
   - 风险：文档会继续漂移，但构建面不报错。

3. **修复范围应先收核心文档，再考虑全量批处理**
   - 全仓很多 DDD 文档都复制了相同 sidecar 语句；
   - 但一次性全量清理改动面太大，容易和其他 worker 冲突；
   - 适合先拿核心设计文档 + 守护测试打样。

## 5. 批次计划

### Batch 0：证据清单、热点盘点、批次计划（本文件）

验收：

- 能说清楚外部依据；
- 能说清楚当前仓库热点；
- 能给出低风险落地顺序。

### Batch 1：修正文档中的失真前置条件（本轮执行）

目标：

- 核心设计文档不再把缺失的 authoring sidecar 写成必需条件；
- 文档要求回到“绑定真实代码 / 测试 / 计划锚点”的仓库内闭环。

目标文件：

- `docs/架构设计.md`
- `docs/DDD/src目录分层说明.md`

### Batch 2：增加 docs-as-code 自动化守护（本轮执行）

目标：

- 用测试守护核心设计文档的关键约束；
- 把“文档不能依赖仓库内不存在的路径”“文档要绑定真实锚点”变成回归检查。

目标文件：

- `tests/Isolation.AnalysisTests/Documentation/DocsAsCodeAlignmentTests.cs`

### Batch 3：扩大到更多 DDD 文档（后续）

目标：

- 把相同 sidecar 漂移从核心文档扩大到 `docs/DDD/*.md` 其余文件；
- 视 leader 集成窗口，分多批收口。

## 6. 本轮收口决定

本轮只做 **Batch 1 + Batch 2**，原因：

- 收益直接：立刻修复核心设计文档失真；
- 风险低：不触碰四层运行时代码；
- 可验证：能用 `dotnet test` 直接回归；
- 符合 docs-as-code：设计文档修改与测试守护同批交付。

## 7. 验证锚点

- 设计文档：
  - `docs/架构设计.md`
  - `docs/DDD/src目录分层说明.md`
- 代码锚点：
  - `src/Application/Services/RewriteWorkflowAppService.cs`
  - `src/Logic/Workflow/RewriteWorkflowArtifactAssembler.cs`
  - `src/Logic/Rules/RewriteWorkflowRulePreset.cs`
- 测试锚点：
  - `tests/ArchitectureTests/Program.cs`
  - `tests/Isolation.AnalysisTests/Documentation/DocsAsCodeAlignmentTests.cs`

## 8. 完成定义

本轮任务完成时，至少满足：

1. 已产出证据清单、热点盘点、批次计划；
2. 核心设计文档不再要求仓库内不存在的 sidecar；
3. 新增 docs-as-code 测试守护关键规则；
4. 相关测试通过并附验证证据。
