# DDD 战术设计

## 1. 文档目的

本文档解决战略设计之后的落地问题：

- 上下文内部应该有哪些模型；
- 每个模型为什么存在；
- 每个模型与其他模型的关系是什么；
- 应该有哪些接口；
- 接口消费和产出哪些模型；
- 旧代码应如何迁移到新结构。

本文档不再讨论“是否应该做这件事”，  
而直接讨论“既然决定要这样做，系统内部应该如何组织”。

---

## 2. 重写动机

旧版战术设计存在的核心问题包括：

- 模型列举不全；
- 模型关系靠读者猜；
- 接口名字和模型名字混乱；
- 历史代码经验没有被对象化；
- 仍然沿用 `TargetId`、`MarkDecision` 等实现导向名词；
- 没有明确哪些模型属于输入、事实、候选、计划、结果、证据。

因此，本次重写的目标是：

- 先把模型分层；
- 再把接口职责定清；
- 再把迁移路径写明。

---

## 3. 本文参考资料

### 3.1 外部资料

重点参考方向：

- DDD Tactical Design
- Aggregate / Entity / Value Object / Domain Service / Application Service
- 软件详细设计文档的常见结构
- GitHub 上类似项目的模型和执行方式

### 3.2 GitHub 对标项目

- `joernio/joern`
- `github/codeql`
- `returntocorp/semgrep`
- `openrewrite/rewrite`
- `dotnet/roslyn-analyzers`

### 3.3 本地代码历史

重点参考：

- `ClassRefactorer.cs`
- `MethodRefactorer.cs`
- `AdvancedCodeAnalyzer.cs`
- `dome/src/Core/Models.cs`
- `dome/src/Plan/AuditPlanCompiler.cs`
- `dome/src/Rewrite/Roslyn/RoslynRewriteExecutor.cs`
- 当前 `DefaultRoslynCpgBuilder.cs`
- 当前 `CpgGraph.cs`

---

## 4. 战术设计总原则

### 4.1 先事实，后候选，后决策，后计划，后执行，后证据

模型和接口必须沿着这一顺序排列。  
任何跨层设计都应被视为异常。

### 4.2 执行器不能补做决策

这是从 `dome` 历史代码中提炼出来的硬规则。

### 4.3 候选不等于最终动作

这是从旧版重构器代码中提炼出来的硬规则。  
否则：

- 规则会过于激进；
- 风险无法显式控制；
- 计划层会失去意义。

### 4.4 证据不等于日志

证据必须独立建模，而不是执行后的附属文本。

---

## 5. 模型分层

### 5.1 输入层模型

#### `RunRequest`

- **层次**：输入层
- **作用**：描述一次运行的用户请求
- **为什么需要它**：CLI、测试、API 入口都需要统一请求对象
- **关系**：生成 `WorkspaceContext`

#### `WorkspaceContext`

- **层次**：输入层
- **作用**：表达一次运行的工程上下文
- **为什么需要它**：分析不应直接依赖 CLI 参数或文件路径字符串
- **关系**：被 `AnalysisSnapshot` 消费

#### `InputDescriptor`

- **层次**：输入层
- **作用**：描述输入集合的边界，例如路径、模式、规则集
- **为什么需要它**：让 `WorkspaceContext` 不必承载所有原始输入细节
- **关系**：属于 `WorkspaceContext` 的组成部分

### 5.2 事实层模型

#### `AnalysisSnapshot`

- **层次**：事实层
- **作用**：表示一次分析运行后的统一快照
- **为什么需要它**：后续所有规则、查询、切片必须共享同一事实真源
- **关系**：
  - 上游：`WorkspaceContext`
  - 下游：`QueryResult`、`SliceResult`、`ChangeCandidate`

#### `GraphSnapshot`

- **层次**：事实层
- **作用**：统一承载程序图视图
- **为什么需要它**：避免上层直接依赖底层图实现细节
- **关系**：作为 `AnalysisSnapshot` 的子模型

#### `ProgramFact`

- **层次**：事实层
- **作用**：程序事实的统一父概念
- **为什么需要它**：为类型事实、调用事实、流程事实提供统一语义
- **关系**：被细分为多种事实子类

#### `TypeFact`

- **层次**：事实层
- **作用**：表达类型级事实
- **为什么需要它**：类型关系对切片、保护、覆盖裁决至关重要
- **关系**：属于 `ProgramFact`

#### `CallFact`

- **层次**：事实层
- **作用**：表达调用级事实
- **为什么需要它**：历史代码中的类删减、方法删减都高度依赖调用关系
- **关系**：属于 `ProgramFact`

#### `FlowFact`

- **层次**：事实层
- **作用**：表达控制流和数据流相关事实
- **为什么需要它**：不理解流程，就不敢安全重写
- **关系**：属于 `ProgramFact`

### 5.3 查询与切片层模型

#### `QueryRequest`

- **层次**：查询层
- **作用**：统一表达查询请求
- **为什么需要它**：不同调用方都应通过统一查询入口使用底座
- **关系**：消费 `AnalysisSnapshot`

#### `QueryResult`

- **层次**：查询层
- **作用**：表达查询结果
- **为什么需要它**：避免规则层直接读底层节点和边
- **关系**：被 `ChangeCandidate` 生成过程消费

#### `SliceRequest`

- **层次**：切片层
- **作用**：描述一次切片意图
- **为什么需要它**：前向切片、后向切片、成员切片应统一表达
- **关系**：消费 `AnalysisSnapshot`

#### `SliceResult`

- **层次**：切片层
- **作用**：表达切片范围与原因
- **为什么需要它**：后续候选、决策、证据都需要它
- **关系**：被 `ChangeCandidate`、`RewriteDecision`、`VerificationEvidence` 消费

### 5.4 候选层模型

#### `RuleTarget`

- **层次**：候选层
- **作用**：表达“规则关注的对象”
- **为什么需要它**：并非所有程序事实都直接进入变更流程
- **关系**：由 `AnalysisSnapshot`、`QueryResult`、`SliceResult` 派生

#### `ChangeCandidate`

- **层次**：候选层
- **作用**：表达“可能被变更”的对象
- **为什么需要它**：让“被规则命中”和“最终变更”之间有缓冲层
- **关系**：
  - 上游：`RuleTarget`
  - 下游：`RewriteDecision`

#### `CandidateReason`

- **层次**：候选层
- **作用**：记录候选出现的原因
- **为什么需要它**：否则后续证据链无法还原
- **关系**：属于 `ChangeCandidate`

### 5.5 决策层模型

#### `RewriteDecision`

- **层次**：决策层
- **作用**：表达经过传播、保护、冲突处理后的最终决策
- **为什么需要它**：候选不等于最终动作
- **关系**：
  - 上游：`ChangeCandidate`
  - 下游：`RewritePlan`

#### `DecisionProtection`

- **层次**：决策层
- **作用**：表达为什么某个候选不能进入计划
- **为什么需要它**：保护规则不能只体现在 `if` 分支里
- **关系**：属于 `RewriteDecision`

#### `DecisionConflict`

- **层次**：决策层
- **作用**：表达决策冲突
- **为什么需要它**：冲突必须结构化，而不是字符串失败
- **关系**：属于 `RewriteDecision`

#### `PropagationTrace`

- **层次**：决策层
- **作用**：表达候选如何传播成最终决策
- **为什么需要它**：历史代码里传播逻辑一直很重要，但缺少显式对象
- **关系**：属于 `RewriteDecision`

### 5.6 计划层模型

#### `RewritePlan`

- **层次**：计划层
- **作用**：可执行变更的单一真源
- **为什么需要它**：执行器不应直接消费决策集合
- **关系**：
  - 上游：`RewriteDecision`
  - 下游：`RewriteResult`

#### `PlanChangeItem`

- **层次**：计划层
- **作用**：表达单条计划项
- **为什么需要它**：计划是多个可排序、可追踪、可覆盖的动作集合
- **关系**：属于 `RewritePlan`

#### `PlanTarget`

- **层次**：计划层
- **作用**：表达计划作用的目标
- **为什么需要它**：定位目标必须独立建模
- **关系**：属于 `PlanChangeItem`

#### `PlanAction`

- **层次**：计划层
- **作用**：表达计划动作
- **为什么需要它**：删除、私有化、清空体、注释等动作需要显式枚举
- **关系**：属于 `PlanChangeItem`

#### `PlanReason`

- **层次**：计划层
- **作用**：表达计划项形成的原因链
- **为什么需要它**：执行后证据需要追溯到计划原因
- **关系**：属于 `PlanChangeItem`

#### `PlanConflict`

- **层次**：计划层
- **作用**：表达计划编译阶段未解决冲突
- **为什么需要它**：这是 `dome` 线历史经验的核心资产
- **关系**：属于 `RewritePlan`

### 5.7 执行层模型

#### `RewriteResult`

- **层次**：执行层
- **作用**：表达计划执行的整体结果
- **为什么需要它**：计划和执行必须分开
- **关系**：
  - 上游：`RewritePlan`
  - 下游：`VerificationEvidence`

#### `FileChange`

- **层次**：执行层
- **作用**：表达单文件变更结果
- **为什么需要它**：旧版执行逻辑是文档级聚合，应该显式保留
- **关系**：属于 `RewriteResult`

#### `ExecutionFailure`

- **层次**：执行层
- **作用**：表达执行失败
- **为什么需要它**：定位失败、动作失败、写回失败都需要结构化表达
- **关系**：属于 `RewriteResult`

#### `ExecutionTrace`

- **层次**：执行层
- **作用**：表达执行顺序和定位过程
- **为什么需要它**：计划驱动系统必须可追踪
- **关系**：属于 `RewriteResult`

### 5.8 证据层模型

#### `VerificationEvidence`

- **层次**：证据层
- **作用**：统一承载本次运行的全部证据
- **为什么需要它**：证据不应只是日志
- **关系**：
  - 上游：`AnalysisSnapshot`、`RewriteResult`
  - 下游：`RunReport`

#### `CompilationEvidence`

- **层次**：证据层
- **作用**：表达编译验证结果
- **为什么需要它**：自动变更至少要知道是否仍然可编译
- **关系**：属于 `VerificationEvidence`

#### `StaticReasoningEvidence`

- **层次**：证据层
- **作用**：表达静态原因链
- **为什么需要它**：计划原因、传播路径、查询结果应当成为正式证据
- **关系**：属于 `VerificationEvidence`

#### `BehaviorEvidence`

- **层次**：证据层
- **作用**：表达动态验证结果
- **为什么需要它**：历史上的差分与影子验证必须有归宿
- **关系**：属于 `VerificationEvidence`

#### `RiskSummary`

- **层次**：证据层
- **作用**：表达剩余风险
- **为什么需要它**：自动变更不能只说通过，也要说清楚边界
- **关系**：属于 `VerificationEvidence`

### 5.9 报告层模型

#### `RunReport`

- **层次**：报告层
- **作用**：对外表达一次运行结果
- **为什么需要它**：外部不应直接消费内部模型集合
- **关系**：聚合 `AnalysisSnapshot`、`RewritePlan`、`RewriteResult`、`VerificationEvidence`

#### `ReportSummary`

- **层次**：报告层
- **作用**：表达面向人类阅读的摘要
- **为什么需要它**：机器结果和人类可读摘要不应混在一起
- **关系**：属于 `RunReport`

---

## 6. 接口设计

### 6.1 输入接口

#### `IWorkspaceContextFactory`

- **作用**：从 `RunRequest` 创建 `WorkspaceContext`
- **输入**：`RunRequest`
- **输出**：`WorkspaceContext`
- **为什么需要它**：隔离 CLI / 配置系统与工程装配逻辑

### 6.2 分析接口

#### `IAnalysisSnapshotBuilder`

- **作用**：从 `WorkspaceContext` 构建 `AnalysisSnapshot`
- **输入**：`WorkspaceContext`
- **输出**：`AnalysisSnapshot`
- **为什么需要它**：让分析层成为可替换实现，而不是写死在入口里

### 6.3 查询接口

#### `IQueryService`

- **作用**：在 `AnalysisSnapshot` 上执行查询
- **输入**：`AnalysisSnapshot`、`QueryRequest`
- **输出**：`QueryResult`
- **为什么需要它**：规则层不应直接遍历底层图

#### `ISliceService`

- **作用**：在 `AnalysisSnapshot` 上执行切片
- **输入**：`AnalysisSnapshot`、`SliceRequest`
- **输出**：`SliceResult`
- **为什么需要它**：切片是高复用支撑能力

### 6.4 候选接口

#### `ICandidateBuilder`

- **作用**：根据事实、查询和切片结果构建 `ChangeCandidate`
- **输入**：`AnalysisSnapshot`、`QueryResult`、`SliceResult`
- **输出**：`IReadOnlyList<ChangeCandidate>`
- **为什么需要它**：把规则目标识别从决策层中剥离出来

### 6.5 决策接口

#### `IDecisionService`

- **作用**：从候选生成 `RewriteDecision`
- **输入**：`IReadOnlyList<ChangeCandidate>`
- **输出**：`RewriteDecision`
- **为什么需要它**：传播、保护、冲突、覆盖是显式业务能力

### 6.6 计划接口

#### `IPlanCompiler`

- **作用**：把 `RewriteDecision` 编译成 `RewritePlan`
- **输入**：`RewriteDecision`
- **输出**：`RewritePlan`
- **为什么需要它**：继承 `AuditPlanCompiler` 的成熟模式

### 6.7 执行接口

#### `IRewriteExecutor`

- **作用**：执行 `RewritePlan`
- **输入**：`WorkspaceContext`、`RewritePlan`
- **输出**：`RewriteResult`
- **为什么需要它**：执行器必须单一职责化

### 6.8 证据接口

#### `IEvidenceService`

- **作用**：构建 `VerificationEvidence`
- **输入**：`AnalysisSnapshot`、`RewriteResult`
- **输出**：`VerificationEvidence`
- **为什么需要它**：证据层不应散落在执行器和测试代码里

### 6.9 报告接口

#### `IReportBuilder`

- **作用**：构建 `RunReport`
- **输入**：`AnalysisSnapshot`、`RewritePlan`、`RewriteResult`、`VerificationEvidence`
- **输出**：`RunReport`
- **为什么需要它**：对外产物应统一组织

---

## 7. 模型关系图

战术层的主关系是：

`RunRequest -> WorkspaceContext -> AnalysisSnapshot -> QueryResult/SliceResult -> ChangeCandidate -> RewriteDecision -> RewritePlan -> RewriteResult -> VerificationEvidence -> RunReport`

其中：

- `WorkspaceContext` 是输入层真源；
- `AnalysisSnapshot` 是事实层真源；
- `RewritePlan` 是执行层真源；
- `VerificationEvidence` 是可信层真源。

---

## 8. 聚合设计

### 8.1 `RewritePlan` 是聚合根

因为它统一管理：

- `PlanChangeItem`
- `PlanTarget`
- `PlanAction`
- `PlanReason`
- `PlanConflict`

不变量：

- 不允许未解决冲突进入执行；
- 不允许同一 target 挂多个未解析动作；
- 覆盖裁决必须在计划内完成。

### 8.2 `VerificationEvidence` 是证据聚合根

因为它统一管理：

- `CompilationEvidence`
- `StaticReasoningEvidence`
- `BehaviorEvidence`
- `RiskSummary`

不变量：

- 证据必须对应一次明确运行；
- 不允许不同运行结果混在同一个证据对象内。

### 8.3 `AnalysisSnapshot` 是事实聚合边界

它不一定是传统业务聚合根，但应被视为一次分析的稳定边界。  
不变量：

- 查询和切片都应基于同一快照；
- 下游上下文不得暗改它来“帮”执行层成功。

---

## 9. 历史代码到新模型的映射

### 9.1 `ClassRefactorer`

- **保留经验**：全局语义引用判断、迭代删除
- **迁移目标**：`ChangeCandidate + RewriteDecision + RewritePlan`
- **不再保留**：直接边分析边写回磁盘

### 9.2 `MethodRefactorer`

- **保留经验**：动作集合化、按文档分组、批量重写
- **迁移目标**：`PlanAction + IRewriteExecutor`
- **不再保留**：动作类型直接挂在底层分析器输出上

### 9.3 `AdvancedCodeAnalyzer`

- **保留经验**：分析服务化、图与结构结果聚合
- **迁移目标**：`IAnalysisSnapshotBuilder`

### 9.4 `AuditPlanCompiler`

- **保留经验**：覆盖裁决、冲突检测、计划化
- **迁移目标**：`IPlanCompiler + RewritePlan`

### 9.5 `RoslynRewriteExecutor`

- **保留经验**：执行器不二次推理、定位失败立即失败
- **迁移目标**：`IRewriteExecutor + RewriteResult`

### 9.6 当前 `DefaultRoslynCpgBuilder`

- **保留经验**：前端与 Pass 分层、分析后统一校验
- **迁移目标**：`IAnalysisSnapshotBuilder`

---

## 10. 迁移顺序

### 10.1 第一步：冻结命名

先统一本文定义的：

- 输入层名词；
- 事实层名词；
- 候选层名词；
- 决策层名词；
- 计划层名词；
- 执行层名词；
- 证据层名词。

### 10.2 第二步：抽取应用服务

优先引入：

- `IWorkspaceContextFactory`
- `IAnalysisSnapshotBuilder`
- `IDecisionService`
- `IPlanCompiler`
- `IRewriteExecutor`
- `IEvidenceService`

### 10.3 第三步：迁移真实场景

优先迁移四个高价值历史场景：

- 类删除；
- 方法删除；
- 方法私有化；
- 方法体清空。

### 10.4 第四步：恢复证据链

逐步将历史里的：

- 差分验证；
- 影子运行；
- 动态补强；

迁入 `Evidence` 模块。

---

## 11. 反模式

### 11.1 反模式一：继续沿用模糊旧名

例如继续大量使用：

- `TargetId`
- `Models.cs`
- `MarkDecision`

却不解释层次和角色。

### 11.2 反模式二：执行器里补规则

这会直接毁掉计划层。

### 11.3 反模式三：查询层直接返回底层图节点给上层业务

这会导致所有上层再次耦合到底层图结构。

### 11.4 反模式四：证据只存在于测试和日志里

这会导致“可信性”永远停留在代码细节，而无法进入正式模型。

---

## 12. 结论

战术设计层最重要的结论不是“某个类怎么写”，而是：

> 必须建立稳定的分层模型链：输入、事实、候选、决策、计划、执行、证据、报告。

只要这条链稳定下来：

- 历史代码经验就能被正确吸收；
- 当前分析底座就不会失焦；
- 后续接口与实现才能避免再次命名漂移和层次混乱。

---

## 13. 应用资料

### 外部资料

- Joern：`https://github.com/joernio/joern`
- CodeQL：`https://github.com/github/codeql`
- Semgrep：`https://github.com/returntocorp/semgrep`
- OpenRewrite：`https://github.com/openrewrite/rewrite`
- Roslyn Analyzers：`https://github.com/dotnet/roslyn-analyzers`
- Martin Fowler：`https://martinfowler.com/bliki/BoundedContext.html`
- Microsoft Learn：`https://learn.microsoft.com/zh-cn/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice`

### 本地历史资料

- `git show 28b63fe:RewriteCodeExpressions/ClassRefactorer.cs`
- `git show 4e15788:RewriteCodeExpressions/MethodRefactorer.cs`
- `git show 4e15788:Analysis/AdvancedCodeAnalyzer.cs`
- `git show 9fd0f68:dome/src/Core/Models.cs`
- `git show 9fd0f68:dome/src/Plan/AuditPlanCompiler.cs`
- `git show 9fd0f68:dome/src/Rewrite/Roslyn/RoslynRewriteExecutor.cs`
- `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
- `src/Analysis/Core/CpgGraph.cs`

---

## 14. 重写补强：模型完整说明矩阵

| 模型 | 层次 | 存在原因 | 关键内容 | 上游模型 | 下游模型 | 不允许承担 |
|---|---|---|---|---|---|---|
| `RunRequest` | 输入层 | 将用户意图变成结构化运行请求 | 工程路径、规则选择、运行模式、输出策略 | 用户、CLI、配置 | `WorkspaceContext` | 不装载项目，不分析代码 |
| `WorkspaceContext` | 输入层 | 表达一次运行的工程边界 | Solution、Project、Document、引用、语言版本 | `RunRequest` | `AnalysisSnapshot`、`RewriteResult`、`VerificationEvidence` | 不判断候选，不决定改写 |
| `InputDescriptor` | 输入层 | 保留输入来源和解析方式 | 来源类型、路径、解析状态 | `RunRequest` | `WorkspaceContext` | 不持有领域事实 |
| `AnalysisSnapshot` | 事实层 | 固化一次分析得到的事实集合 | 图快照、符号表、诊断、构建时间 | `WorkspaceContext` | `QueryResult`、`SliceResult`、`ChangeCandidate` | 不做变更裁决 |
| `GraphSnapshot` | 事实层 | 隔离底层 CPG 实现 | 节点、边、索引、版本 | CPG Builder、Pass | `ProgramFact`、查询服务 | 不暴露可变图给上层 |
| `ProgramFact` | 事实层 | 统一表达可被业务消费的程序事实 | 事实标识、位置、类型、来源 | `GraphSnapshot` | `RuleTarget`、证据 | 不携带执行动作 |
| `TypeFact` | 事实层 | 表达类型声明和类型关系 | 类型名、基类、接口、成员 | `ProgramFact` | 候选、切片、证据 | 不决定删除或保留 |
| `CallFact` | 事实层 | 表达调用关系 | 调用方、被调方、分派方式 | `ProgramFact` | 查询、切片、候选 | 不表达改写动作 |
| `FlowFact` | 事实层 | 表达数据流或控制相关事实 | 源点、汇点、路径、边类型 | 数据流 Pass | 切片、证据 | 不替代验证 |
| `QueryRequest` | 查询层 | 结构化查询意图 | 查询种类、过滤条件、投影 | 规则、用户 | `QueryResult` | 不持有执行上下文 |
| `QueryResult` | 查询层 | 保存查询后的事实子集 | 命中项、解释、排序 | `AnalysisSnapshot` | `RuleTarget` | 不直接进入执行器 |
| `SliceRequest` | 查询层 | 表达切片目标和方向 | 起点、方向、深度、边过滤 | 规则、用户 | `SliceResult` | 不决定风险等级 |
| `SliceResult` | 查询层 | 保存影响范围 | 节点、边、路径、边界原因 | `AnalysisSnapshot` | `ChangeCandidate`、证据 | 不生成计划项 |
| `RuleTarget` | 候选层 | 把规则关注对象从事实中抽出来 | 目标键、事实引用、目标类型 | `QueryResult`、`SliceResult` | `ChangeCandidate` | 不代表最终变更 |
| `ChangeCandidate` | 候选层 | 表示可能被变更的对象 | 候选键、目标、原因、风险提示 | `RuleTarget` | `RewriteDecision` | 不保存写回动作 |
| `CandidateReason` | 候选层 | 解释候选产生原因 | 规则名、命中事实、路径解释 | 查询、切片、规则 | `ChangeCandidate`、证据 | 不做保护裁决 |
| `RewriteDecision` | 决策层 | 表示最终变更裁决 | 允许项、拒绝项、保护、冲突 | `ChangeCandidate` | `RewritePlan`、证据 | 不修改文件 |
| `DecisionProtection` | 决策层 | 表示候选被保护的原因 | 保护规则、保护事实、严重级别 | `ChangeCandidate`、事实 | `RewriteDecision` | 不生成替代动作 |
| `DecisionConflict` | 决策层 | 表示多个候选或动作冲突 | 冲突项、冲突类型、解决建议 | `ChangeCandidate` | `RewriteDecision`、证据 | 不自动吞掉冲突 |
| `PropagationTrace` | 决策层 | 记录候选如何传播成决策 | 起点、传播边、终点、解释 | `ChangeCandidate`、切片 | `RewriteDecision`、证据 | 不替代计划排序 |
| `RewritePlan` | 计划层 | 执行器唯一可信输入 | 元数据、计划项、冲突、前置条件 | `RewriteDecision` | `RewriteResult`、证据 | 不重新做候选识别 |
| `PlanChangeItem` | 计划层 | 表示计划中的单个变更项 | 序号、目标、动作、原因 | `RewriteDecision` | `IRewriteExecutor` | 不脱离计划独立存在 |
| `PlanTarget` | 计划层 | 将变更定位到可执行位置 | 文件、成员、Span、校验文本 | `ChangeCandidate` | 执行器 | 不表达规则命中原因 |
| `PlanAction` | 计划层 | 表示执行器可理解的动作 | 删除、替换、插入、注释、私有化 | `RewriteDecision` | 执行器 | 不访问底层图 |
| `PlanReason` | 计划层 | 保留计划项生成原因 | 决策引用、候选引用、说明 | `RewriteDecision` | 证据、报告 | 不做裁决 |
| `PlanConflict` | 计划层 | 表示计划编译阶段冲突 | 冲突计划项、原因、建议 | `RewriteDecision` | 证据、报告 | 不在执行期才发现 |
| `RewriteResult` | 执行层 | 表示执行后的真实结果 | 成功状态、文件变更、失败、耗时 | `RewritePlan` | `VerificationEvidence`、`RunReport` | 不补做计划 |
| `FileChange` | 执行层 | 表示单文件真实修改 | 路径、旧文本摘要、新文本摘要、Diff | 执行器 | 证据、报告 | 不做业务判断 |
| `ExecutionFailure` | 执行层 | 结构化失败原因 | 失败代码、目标、消息、是否可重试 | 执行器 | 证据、报告 | 不用字符串替代 |
| `ExecutionTrace` | 执行层 | 记录执行步骤 | 计划项、定位、应用、耗时 | 执行器 | 证据 | 不替代日志系统 |
| `VerificationEvidence` | 证据层 | 证明变更可信 | 编译证据、静态证据、行为证据、风险摘要 | 事实、决策、计划、结果 | `RunReport` | 不执行改写 |
| `CompilationEvidence` | 证据层 | 证明编译层面可接受 | 编译命令、诊断差异、结果 | `RewriteResult` | `VerificationEvidence` | 不判断业务正确性 |
| `StaticReasoningEvidence` | 证据层 | 证明静态语义理由 | 事实引用、传播链、保护规则 | `AnalysisSnapshot`、`RewriteDecision` | `VerificationEvidence` | 不替代编译 |
| `BehaviorEvidence` | 证据层 | 证明行为未明显破坏 | 测试、示例、快照、约束 | `RewriteResult` | `VerificationEvidence` | 不伪装成完整形式化证明 |
| `RiskSummary` | 证据层 | 汇总残余风险 | 风险等级、来源、建议 | 证据子项 | `RunReport` | 不决定是否执行 |
| `RunReport` | 报告层 | 面向人类和 CI 的最终报告 | 摘要、变更、证据、风险、建议 | 全链路模型 | 用户、CI | 不反向影响领域模型 |
| `ReportSummary` | 报告层 | 快速说明本次运行结果 | 数量、状态、关键风险 | `RunReport` | 用户 | 不保存完整证据 |

---

## 15. 重写补强：接口契约矩阵

| 接口 | 所属层 | 输入 | 输出 | 失败语义 | 为什么需要 | 禁止行为 |
|---|---|---|---|---|---|---|
| `IWorkspaceContextFactory` | 输入层 | `RunRequest` | `WorkspaceContext` | 输入不存在、项目不可加载、语言版本不支持 | 隔离入口与工程装配 | 不分析规则、不写文件 |
| `IAnalysisSnapshotBuilder` | 事实层 | `WorkspaceContext` | `AnalysisSnapshot` | 编译上下文失败、Pass 失败、事实校验失败 | 隔离 Roslyn / CPG 实现 | 不输出候选和计划 |
| `IQueryService` | 查询层 | `AnalysisSnapshot`、`QueryRequest` | `QueryResult` | 查询语义非法、事实缺失 | 避免规则直接遍历图 | 不返回可变节点 |
| `ISliceService` | 查询层 | `AnalysisSnapshot`、`SliceRequest` | `SliceResult` | 起点不存在、边类型不支持、深度超限 | 复用切片能力 | 不决定改写 |
| `ICandidateBuilder` | 候选层 | `AnalysisSnapshot`、`QueryResult`、`SliceResult` | `IReadOnlyList<ChangeCandidate>` | 规则不匹配、候选解释缺失 | 分离命中和裁决 | 不输出 `PlanAction` |
| `IDecisionService` | 决策层 | 候选集合、事实快照 | `RewriteDecision` | 保护规则失败、冲突未解决 | 集中传播、保护、冲突逻辑 | 不修改文件 |
| `IPlanCompiler` | 计划层 | `RewriteDecision` | `RewritePlan` | 冲突、覆盖、排序、不可定位 | 把决策转成可执行蓝图 | 不重新跑查询 |
| `IRewriteExecutor` | 执行层 | `WorkspaceContext`、`RewritePlan` | `RewriteResult` | 目标不可定位、文本不匹配、写回失败 | 统一执行策略 | 不补做决策 |
| `IEvidenceService` | 证据层 | `WorkspaceContext`、`AnalysisSnapshot`、`RewriteDecision`、`RewritePlan`、`RewriteResult` | `VerificationEvidence` | 编译失败、证据缺失、风险过高 | 让自动变更可被信任 | 不改写代码 |
| `IReportBuilder` | 报告层 | 全链路模型 | `RunReport` | 报告模板失败、证据缺失 | 面向人类和 CI 输出 | 不改变任何领域模型 |

---

## 16. 重写补强：模型关系闭环

```text
RunRequest
  -> WorkspaceContext
  -> AnalysisSnapshot
  -> QueryResult / SliceResult
  -> RuleTarget
  -> ChangeCandidate
  -> RewriteDecision
  -> RewritePlan
  -> RewriteResult
  -> VerificationEvidence
  -> RunReport
```

关系规则：

- `RunRequest` 是外部意图，不是领域事实；
- `WorkspaceContext` 是输入边界，不是执行结果；
- `AnalysisSnapshot` 是事实快照，不是查询结果；
- `QueryResult` 和 `SliceResult` 是事实投影，不是候选；
- `ChangeCandidate` 是可能性，不是裁决；
- `RewriteDecision` 是裁决，不是执行计划；
- `RewritePlan` 是蓝图，不是执行结果；
- `RewriteResult` 是结果，不是证据；
- `VerificationEvidence` 是证明，不是报告；
- `RunReport` 是消费视图，不是领域真相。

---

## 17. 重写补强：旧代码迁移细化

| 历史对象 | 观察到的有效经验 | 新模型承接 | 新接口承接 | 为什么这样迁移 |
|---|---|---|---|---|
| `ClassRefactorer` | 类型引用判断、全局影响分析、迭代删除 | `ChangeCandidate`、`RewriteDecision`、`PropagationTrace` | `ICandidateBuilder`、`IDecisionService` | 类删除不是单点动作，必须经过影响传播和保护 |
| `MethodRefactorer` | 动作集合化、按文档分组、批量修改 | `PlanAction`、`PlanChangeItem`、`RewriteResult` | `IPlanCompiler`、`IRewriteExecutor` | 方法级动作适合计划化执行，不适合直接写回 |
| `AdvancedCodeAnalyzer` | 分析结果服务化、图和结构结果聚合 | `AnalysisSnapshot`、`ProgramFact` | `IAnalysisSnapshotBuilder` | 分析能力应成为事实服务，不应绑定某个重构场景 |
| `MarkDecision` | 记录目标和动作意图 | `ChangeCandidate`、`RewriteDecision` | `IDecisionService` | 原模型混合候选和裁决，必须拆开 |
| `AuditPlanCompiler` | 冲突检测、覆盖裁决、计划排序 | `RewritePlan`、`PlanConflict` | `IPlanCompiler` | 这是计划上下文的核心资产 |
| `RoslynRewriteExecutor` | 目标定位、文本匹配、失败即返回 | `RewriteResult`、`ExecutionFailure` | `IRewriteExecutor` | 执行器只应消费计划并返回结果 |
| `DefaultRoslynCpgBuilder` | 前端构建、Pass 编排、分析后校验 | `AnalysisSnapshot`、`GraphSnapshot` | `IAnalysisSnapshotBuilder` | 当前底座应被上升为事实快照构建器 |
| 当前 `DataFlowSlicer` | 数据流影响范围 | `SliceResult`、`FlowFact` | `ISliceService` | 切片能力应复用给候选和证据 |
| 当前 `QueryEngine` | 查询任务创建与求解 | `QueryRequest`、`QueryResult` | `IQueryService` | 查询应隔离底层图访问 |
| 当前 `TargetId` | 标识目标 | `ProgramElementKey`、`RuleTargetKey`、`PlanTargetKey` | 各层值对象 | 单一 `TargetId` 无法表达不同层含义 |

---

## 18. 重写补强：命名替换细则

| 禁用 / 限制名 | 替换名 | 适用层 | 替换原因 |
|---|---|---|---|
| `TargetId` | `ProgramElementKey` | 事实层 | 表示程序元素稳定标识 |
| `TargetId` | `RuleTargetKey` | 候选层 | 表示规则关注对象 |
| `TargetId` | `PlanTargetKey` | 计划层 | 表示可执行定位目标 |
| `MarkDecision` | `ChangeCandidate` | 候选层 | 表示可能变更，不是最终裁决 |
| `MarkDecision` | `RewriteDecision` | 决策层 | 表示经过保护和冲突处理后的裁决 |
| `AuditPlan` | `RewritePlan` | 计划层 | 表示可执行改写计划 |
| `PlannedChange` | `PlanChangeItem` | 计划层 | 表示计划内部条目 |
| `AnalysisView` | `AnalysisSnapshot` | 事实层 | 表示一次稳定分析结果 |
| `RunResult` | `RewriteResult` | 执行层 | 表示改写执行结果 |
| `RunResult` | `RunReport` | 报告层 | 表示用户消费报告 |

---

## 19. 重写补强：战术验收清单

- 新模型是否能回答“为什么存在”；
- 新模型是否能说出上游和下游；
- 新接口是否只有一个层次职责；
- 执行器是否只消费 `RewritePlan`；
- 候选层是否不产生写回动作；
- 决策层是否不依赖 Roslyn SyntaxNode；
- 计划层是否能表达冲突和覆盖；
- 证据层是否能引用事实、决策、计划和结果；
- 报告层是否不反向污染领域模型；
- 旧名是否已被替换或明确标为迁移遗留。
