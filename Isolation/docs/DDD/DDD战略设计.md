# DDD 战略设计

## 1. 文档目的

本文档解决“问题域之后、实现之前”的战略问题：

- 平台到底应该如何分层；
- 哪些子域属于核心域；
- 哪些上下文应该独立；
- 哪些历史代码方向应该保留；
- 哪些方向虽然看起来诱人，但不应成为当前主线。

本文档直接建立在 `docs/DDD/DDD问题域与业务愿景.md` 之上，  
不再重复定义问题域，而是将其转化成：

- 子域；
- 上下文；
- 关系图；
- 战略原则；
- 迁移策略。

---

## 2. 重写动机

此前战略设计文档存在以下问题：

- 问题域、战略和战术边界没有拉开；
- “分析、规则、计划、执行、证据”之间没有清晰战略分层；
- 历史代码被提到，但没有变成战略约束；
- 名称仍然沿用旧实现气质，没有形成统一平台语言；
- 缺少“为什么是这几个上下文”的解释。

这次重写的目的，是让战略设计真正具备：

- **边界清晰性**
- **命名稳定性**
- **迁移指导性**
- **上下文可协作性**

---

## 3. 本文参考资料

### 3.1 外部资料

本文参考了以下方向的公开资料：

- DDD Strategic Design
- Bounded Context / Context Mapping
- Product Strategy / Architecture Vision
- GitHub 同类项目的项目结构与定位说明

### 3.2 重点对标项目

- `joernio/joern`
- `github/codeql`
- `returntocorp/semgrep`
- `openrewrite/rewrite`
- `dotnet/roslyn-analyzers`
- `sourcegraph/sourcegraph`

### 3.3 本地历史资料

重点参考：

- `ARCHITECTURE.md`
- `DESIGN_CONCEPTS.md`
- `Analysis_Architecture_Design.md`
- `dome/docs/architecture.md`
- `dome` 计划驱动模型代码
- 当前 `Isolation` 分支的 `src/Analysis`

---

## 4. 战略设计的核心判断

### 4.1 平台不应再按“技术模块”理解自己

旧文档常见问题是：

- 以 `Roslyn / CPG / Pass / Query / Rewrite` 为主语；
- 以技术组件命名章节；
- 把实现结构误当成业务结构。

这会直接导致两个问题：

- 核心域和支撑域混在一起；
- 历史能力无法被统一吸收。

因此，这次战略设计的第一原则是：

> 战略设计必须以“职责边界”和“业务价值密度”划分，而不是以代码目录划分。

### 4.2 平台的主线不是“分析”而是“可验证程序变换”

从历史代码看，项目始终不是纯分析项目。  
更准确的主线是：

- 先理解；
- 再决定；
- 再计划；
- 再执行；
- 再证明。

因此，平台战略上应把“程序变换闭环”作为主线，  
而不是把“程序图建设”误当成终局。

### 4.3 平台的关键战略资产

从历史和当前代码综合来看，项目已经具备四类关键资产：

1. **统一事实层资产**  
   当前 `Analysis` 底座

2. **变更动作资产**  
   旧版类删除、方法删除、私有化、清空体

3. **计划驱动资产**  
   `dome` 的 `Plan`、`Conflict`、`Rewrite`

4. **可信交付资产**  
   差分验证、影子运行、动态补强

战略设计的责任，就是决定如何把这四类资产变成同一个系统的一部分。

---

## 5. 子域划分

### 5.1 划分原则

子域划分以四个标准为准：

1. 是否直接决定平台差异化；
2. 是否需要专门领域知识；
3. 是否能独立变化；
4. 是否会被多个上下文复用。

### 5.2 核心域：程序变换域

程序变换域是本项目的最核心子域。  
它包含两部分高价值能力：

- **变更决策**
- **变更计划与执行**

原因：

- 这是项目区别于普通查询工具和普通分析框架的关键；
- 历史上最重要的业务价值都围绕这里；
- 也是最难直接由外部开源项目替代的部分。

### 5.3 核心域：程序事实域

程序事实域承载：

- 统一分析快照；
- 类型、调用、控制流、数据流等关系；
- 对上层所有决策的稳定支撑。

它也是核心域，因为：

- 没有统一事实层，程序变换域就会失去可靠基础；
- 当前 `Isolation` 分支的核心沉淀就在这里。

### 5.4 核心域：变更证据域

变更证据域承载：

- 静态原因链；
- 编译结果；
- 动态验证；
- 风险摘要。

为什么将它提升为核心域：

- 自动变更如果没有证据，就无法形成真实工程价值；
- 历史上这一诉求已经存在，不是新增要求。

### 5.5 支撑域：输入装配域

职责：

- 加载工程；
- 装配编译上下文；
- 屏蔽文件系统与 Roslyn 初始化复杂性。

它重要但不决定差异化，因此归为支撑域。

### 5.6 支撑域：查询与切片域

职责：

- 路径查询；
- 可达性分析；
- 切片；
- 解释性输出。

它是高价值支撑域，但战略上仍然服务于核心域。

### 5.7 支撑域：报告与消费域

职责：

- 运行摘要；
- JSON/Markdown 报告；
- CLI 输出。

### 5.8 通用域

包括：

- 日志；
- 配置；
- 序列化；
- 文件读写；
- 进程和参数基础设施。

---

## 6. 限界上下文设计

### 6.1 为什么必须显式划分上下文

如果不显式划分上下文，当前项目会反复出现以下问题：

- `Analysis` 同时承担事实、决策、执行暗逻辑；
- 执行层重新做规则判断；
- 文档里每一层都在重复使用同一个模糊词；
- 模型不清楚属于“输入”“事实”“计划”还是“结果”。

因此必须显式划出限界上下文。

### 6.2 `Input Context`

#### 作用

统一输入来源，生成一次运行所需的工程上下文。

#### 为什么需要独立上下文

因为输入装配和程序事实不是同一件事。  
前者关心：

- 路径；
- 模式；
- 解决方案加载；
- 编译上下文建立；

后者关心：

- 节点；
- 边；
- 关系；
- 程序事实。

#### 核心模型

- `RunRequest`
- `WorkspaceContext`
- `InputDescriptor`

#### 与其他上下文关系

- 向 `Program Fact Context` 提供分析输入；
- 不直接产生变更候选。

### 6.3 `Program Fact Context`

#### 作用

将工程输入转换为统一程序事实。

#### 为什么需要独立上下文

因为它是整个系统的事实真源。  
如果把它混进规则层或执行层，系统会失去稳定基础。

#### 核心模型

- `AnalysisSnapshot`
- `GraphSnapshot`
- `ProgramFact`
- `TypeFact`
- `CallFact`
- `FlowFact`

#### 与其他上下文关系

- 消费 `WorkspaceContext`
- 向 `Query & Slice Context` 和 `Transformation Decision Context` 提供统一事实

### 6.4 `Query & Slice Context`

#### 作用

在统一事实层之上提供复用型分析结果。

#### 为什么需要独立上下文

因为查询与切片是横向复用能力，  
不应每次都被规则层重新拼装。

#### 核心模型

- `QueryRequest`
- `QueryResult`
- `SliceRequest`
- `SliceResult`
- `ReachabilityResult`

#### 与其他上下文关系

- 消费 `AnalysisSnapshot`
- 为 `Transformation Decision Context` 提供支持

### 6.5 `Transformation Decision Context`

#### 作用

把“程序事实”转化为“程序变更决策”。

#### 为什么需要独立上下文

因为：

- 事实不等于候选；
- 候选不等于裁决；
- 裁决不等于计划。

#### 核心模型

- `RuleTarget`
- `ChangeCandidate`
- `RewriteDecision`
- `DecisionConflict`
- `DecisionProtection`

#### 与其他上下文关系

- 消费 `AnalysisSnapshot`、`QueryResult`、`SliceResult`
- 产出 `RewriteDecision` 给 `Transformation Plan Context`

### 6.6 `Transformation Plan Context`

#### 作用

把决策编译成可执行计划。

#### 为什么需要独立上下文

因为 `dome` 线已经证明：

- 覆盖裁决；
- 计划顺序；
- 冲突处理；

这些都应该在执行前被固化，而不是由执行器隐式处理。

#### 核心模型

- `RewritePlan`
- `PlannedChange`
- `PlanTarget`
- `PlanAction`
- `PlanReason`
- `PlanConflict`

#### 与其他上下文关系

- 消费 `RewriteDecision`
- 产出 `RewritePlan` 给 `Transformation Execution Context`

### 6.7 `Transformation Execution Context`

#### 作用

按计划执行变更。

#### 为什么需要独立上下文

因为执行器应保持单一职责：

- 定位目标；
- 应用动作；
- 返回结果；

而不应承担规则、保护和覆盖逻辑。

#### 核心模型

- `RewriteResult`
- `ExecutionFailure`
- `FileChange`
- `ExecutionTrace`

#### 与其他上下文关系

- 消费 `RewritePlan`
- 向 `Evidence Context` 提供执行结果

### 6.8 `Evidence Context`

#### 作用

将本次变更产生的静态和动态证据统一组织。

#### 为什么需要独立上下文

因为“结果”和“可信度”不是一回事。  
证据层必须明确存在，才能避免“改完就结束”的短路设计。

#### 核心模型

- `VerificationEvidence`
- `CompilationEvidence`
- `StaticReasoningEvidence`
- `BehaviorEvidence`
- `RiskSummary`

#### 与其他上下文关系

- 消费 `AnalysisSnapshot`、`RewriteResult`
- 产出可供 `Reporting Context` 消费的证据包

### 6.9 `Reporting Context`

#### 作用

将运行结果组织为外部产物。

#### 核心模型

- `RunReport`
- `ReportSummary`
- `ReportArtifact`

---

## 7. 上下文关系图

平台上下文主关系为：

`Input Context -> Program Fact Context -> Query & Slice Context -> Transformation Decision Context -> Transformation Plan Context -> Transformation Execution Context -> Evidence Context -> Reporting Context`

这里有三个关键原则：

1. `Program Fact Context` 是事实真源；
2. `RewritePlan` 是执行真源；
3. `VerificationEvidence` 是可信真源。

任何绕过这三层的设计，战略上都应视为退化。

---

## 8. 模型命名重构原则

### 8.1 为什么旧名称不够好

旧名称的问题主要在于：

- 太偏实现抽象；
- 没有表达处于哪一层；
- 没有表达对象角色；
- 没有表达与其他对象的关系。

例如：

- `TargetId` 听不出它是事实标识、候选标识还是计划目标；
- `Models.cs` 这种文件聚合命名，会把不同层的模型塞在一起；
- `AnalysisView` 这类名称如果没有明确上下文，容易变成“什么都能往里塞”的兜底对象。

### 8.2 统一命名原则

建议统一采用“角色 + 层次”命名：

- 输入层：`RunRequest`、`WorkspaceContext`
- 事实层：`AnalysisSnapshot`、`ProgramFact`
- 候选层：`ChangeCandidate`
- 决策层：`RewriteDecision`
- 计划层：`RewritePlan`
- 执行层：`RewriteResult`
- 证据层：`VerificationEvidence`
- 报告层：`RunReport`

### 8.3 命名迁移表

| 旧名 | 新建议名 | 原因 |
|---|---|---|
| `TargetId` | `ProgramElementKey` 或 `FactKey` | `TargetId` 太泛，不知道属于哪一层 |
| `AnalysisView` | `AnalysisSnapshot` | 强调它是一次分析快照，不是任意视图 |
| `MarkDecision` | `ChangeCandidate` 或 `InitialDecision` | “Mark” 太偏实现，业务语义不清 |
| `AuditPlan` | `RewritePlan` | “Audit” 容易误导到审计而非执行变更 |
| `PlannedChange` | `PlanChangeItem` | 更强调计划项角色 |
| `RunResult` | `ExecutionSummary` 或 `RunOutcome` | 让结果语义更明确 |

> 注：最终代码命名可以略有不同，但文档语义必须统一。

---

## 9. 战略约束

### 9.1 不允许决策和执行混写

原因：

- 旧版已经吃过“算法和改写纠缠”的亏；
- `dome` 已经证明计划层独立是正确方向。

### 9.2 不允许程序事实和平台外壳混写

原因：

- 当前 `Analysis` 底座的最大价值，就是作为稳定事实层；
- 一旦与 CLI、报告、写回逻辑缠在一起，就会破坏复用性。

### 9.3 不允许把证据层降为附属项

原因：

- 项目历史里“行为保证”一直是硬诉求；
- 自动变更没有证据，业务价值会打折。

### 9.4 不允许模型名称继续沿用历史歧义名而不解释

原因：

- 文档就是为了消除语义漂移；
- 如果名称继续模糊，后续代码必然继续漂移。

---

## 10. 模型与上下文关系总表

### 10.1 `WorkspaceContext`

- **上下文**：`Input Context`
- **为什么有它**：统一表达一次运行的输入工程与编译环境
- **关系**：被 `AnalysisSnapshot` 消费

### 10.2 `AnalysisSnapshot`

- **上下文**：`Program Fact Context`
- **为什么有它**：统一表达分析真源
- **关系**：为 `QueryResult`、`SliceResult`、`ChangeCandidate` 提供基础

### 10.3 `QueryResult`

- **上下文**：`Query & Slice Context`
- **为什么有它**：避免规则层重复拼分析
- **关系**：被 `ChangeCandidate` 构建过程消费

### 10.4 `SliceResult`

- **上下文**：`Query & Slice Context`
- **为什么有它**：表达裁剪与传播所需范围
- **关系**：被 `ChangeCandidate` 和 `RewriteDecision` 消费

### 10.5 `ChangeCandidate`

- **上下文**：`Transformation Decision Context`
- **为什么有它**：区分“可考虑变更”和“最终变更”
- **关系**：被 `RewriteDecision` 消费

### 10.6 `RewriteDecision`

- **上下文**：`Transformation Decision Context`
- **为什么有它**：承载传播、保护、冲突处理后的最终业务判断
- **关系**：被 `RewritePlan` 消费

### 10.7 `RewritePlan`

- **上下文**：`Transformation Plan Context`
- **为什么有它**：执行器不能直接消费决策集合
- **关系**：被 `RewriteResult` 生产过程消费

### 10.8 `RewriteResult`

- **上下文**：`Transformation Execution Context`
- **为什么有它**：计划和结果必须分层
- **关系**：被 `VerificationEvidence` 消费

### 10.9 `VerificationEvidence`

- **上下文**：`Evidence Context`
- **为什么有它**：变更可信度必须独立建模
- **关系**：被 `RunReport` 消费

### 10.10 `RunReport`

- **上下文**：`Reporting Context`
- **为什么有它**：统一对外表达一次运行的整体输出
- **关系**：聚合前面所有关键层的摘要

---

## 11. 演进路线

### 11.1 第一阶段：统一语义与命名

先解决：

- 名称不清晰；
- 上下文不清晰；
- 模型层级混乱；
- 事实层和计划层语言不统一。

### 11.2 第二阶段：把当前 `Analysis` 收敛到 `Program Fact Context`

让当前底座在战略上归位：

- 它是事实层，不是总控层。

### 11.3 第三阶段：把旧版重构器和 `dome` 迁入新上下文图

优先迁移：

- 类删除；
- 方法删除；
- 方法私有化；
- 计划编译；
- 执行器。

### 11.4 第四阶段：恢复证据域

让旧版差分测试、影子运行等经验进入独立上下文，而不是散落在工具和示例里。

---

## 12. 结论

战略设计层最重要的结论有两个：

1. `TerrariaTools` 的主线必须被定义为“可验证程序变换平台”，而不是“某种程序图框架”；
2. 三个核心真源必须被明确下来：
   - `AnalysisSnapshot`
   - `RewritePlan`
   - `VerificationEvidence`

只要这三个真源不被混写，后续战术设计和实现设计就有稳定基础。

---

## 13. 应用资料

### 外部资料

- Martin Fowler：`https://martinfowler.com/bliki/BoundedContext.html`
- Microsoft Learn：`https://learn.microsoft.com/zh-cn/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice`
- Joern：`https://github.com/joernio/joern`
- CodeQL：`https://github.com/github/codeql`
- Semgrep：`https://github.com/returntocorp/semgrep`
- OpenRewrite：`https://github.com/openrewrite/rewrite`
- Roslyn Analyzers：`https://github.com/dotnet/roslyn-analyzers`
- Sourcegraph：`https://github.com/sourcegraph/sourcegraph`

### 本地历史资料

- `git show 1fbdefd:ARCHITECTURE.md`
- `git show 4e15788:DESIGN_CONCEPTS.md`
- `git show 9fd0f68:dome/docs/architecture.md`
- `git show 9fd0f68:dome/src/Core/Models.cs`
- 当前 `src/Analysis`

---

## 14. 重写补强：上下文契约

### 14.1 `Input Context`

- **拥有模型**：`RunRequest`、`WorkspaceContext`、`InputDescriptor`
- **输出给谁**：`Program Fact Context`、`Transformation Execution Context`、`Evidence Context`
- **禁止职责**：不得解释规则，不得产生候选，不得判断是否变更
- **存在原因**：隔离 CLI、配置文件、测试夹具、项目加载细节，避免领域层被入口污染

### 14.2 `Program Fact Context`

- **拥有模型**：`AnalysisSnapshot`、`GraphSnapshot`、`ProgramFact`、`TypeFact`、`CallFact`、`FlowFact`
- **输出给谁**：`Query & Slice Context`、`Transformation Decision Context`、`Evidence Context`
- **禁止职责**：不得决定改不改，不得执行写回，不得生成最终报告
- **存在原因**：当前 CPG、Pass、DataFlow、Slicing 的价值需要被封装为稳定事实边界

### 14.3 `Query & Slice Context`

- **拥有模型**：`QueryRequest`、`QueryResult`、`SliceRequest`、`SliceResult`
- **输出给谁**：`Transformation Decision Context`
- **禁止职责**：不得把底层图节点直接泄漏给计划层，不得生成写回动作
- **存在原因**：借鉴 Joern / CodeQL 的查询思路，但让查询成为支撑能力，而非压过主线

### 14.4 `Transformation Decision Context`

- **拥有模型**：`RuleTarget`、`ChangeCandidate`、`CandidateReason`、`RewriteDecision`、`DecisionProtection`、`DecisionConflict`
- **输出给谁**：`Transformation Plan Context`、`Evidence Context`
- **禁止职责**：不得修改文件，不得依赖 Roslyn SyntaxNode，不得把冲突写成字符串日志
- **存在原因**：旧版 `MarkDecision` 把命中、传播、裁决混在一起，必须拆开

### 14.5 `Transformation Plan Context`

- **拥有模型**：`RewritePlan`、`PlanChangeItem`、`PlanTarget`、`PlanAction`、`PlanReason`、`PlanConflict`
- **输出给谁**：`Transformation Execution Context`、`Evidence Context`
- **禁止职责**：不得重新查询事实层，不得重新判断候选能否变更
- **存在原因**：本地 `AuditPlanCompiler` 已证明计划化能解决覆盖、排序、冲突问题

### 14.6 `Transformation Execution Context`

- **拥有模型**：`RewriteResult`、`FileChange`、`ExecutionTrace`、`ExecutionFailure`
- **输出给谁**：`Evidence Context`、`Reporting Context`
- **禁止职责**：不得补做决策，不得吞掉失败，不得用“最佳努力”掩盖不可定位目标
- **存在原因**：本地 `RoslynRewriteExecutor` 已表现出“计划不可执行时立即失败”的正确方向

### 14.7 `Evidence Context`

- **拥有模型**：`VerificationEvidence`、`CompilationEvidence`、`StaticReasoningEvidence`、`BehaviorEvidence`、`RiskSummary`
- **输出给谁**：`Reporting Context`、CI、人工审查
- **禁止职责**：不得执行变更，不得伪造通过，不得只保存日志文本
- **存在原因**：自动程序变换必须证明“为什么可信”，否则无法进入真实工程流程

### 14.8 `Reporting Context`

- **拥有模型**：`RunReport`、`ReportSummary`
- **输出给谁**：用户、CI、文档站、后续工具
- **禁止职责**：不得反向修改领域模型，不得决定是否执行计划
- **存在原因**：报告是消费模型，不是领域事实本身

---

## 15. 重写补强：战略资料应用矩阵

| 资料 / 项目 | 战略启发 | 本项目采用 | 本项目不采用 | 原因 |
|---|---|---|---|---|
| Martin Fowler `Bounded Context` | 同一名词在不同上下文可能有不同含义 | 显式拆分 `RuleTarget`、`PlanTarget`、`ProgramElementKey` | 一个 `TargetId` 贯穿全链路 | 旧名会混淆规则目标、计划目标、图节点标识 |
| Microsoft DDD | 核心域应围绕竞争力而不是技术组件 | “可验证程序变换”作为核心域 | 把 CLI、报告、文件扫描设为核心域 | 支撑能力重要，但不是平台壁垒 |
| 阿里云 DDD 文章 | 先统一语言，再建模，再落地 | 三份文档按问题域、战略、战术拆分 | 在问题域文档写接口细节 | 避免层级设计错误 |
| Joern | CPG 能统一程序事实 | 保留当前 `src/Analysis` 为事实底座 | 把平台愿景收窄成 CPG 工具 | 用户要的是变更闭环，不只是图查询 |
| CodeQL | 查询和代码数据库能产品化复杂分析 | 建立 `Query & Slice Context` | 第一阶段建设完整查询语言产品 | 当前更需要稳定变更模型 |
| Semgrep | 规则命中必须可解释 | 候选层保留 `CandidateReason` | 把模式匹配当成唯一规则来源 | C# 语义重写需要更深事实 |
| OpenRewrite | 变更执行前需要计划与执行分离 | 采用 `RewritePlan` 与 `IRewriteExecutor` 分离 | 复制 JVM 类型系统和 Recipe API | 技术栈不同，但计划化思想可复用 |
| Roslyn Analyzers | C# 分析与 CodeFix 有成熟生态 | Roslyn 作为事实采集和执行适配层 | Analyzer 直接作为领域模型 | Analyzer 是技术机制，不是业务语言 |
| Sourcegraph | 工程级代码索引和搜索是规模化基础 | `WorkspaceContext` 必须承载工程装配信息 | 代码搜索成为核心域 | 搜索支撑理解，但不完成变更证明 |
| 本地 `dome` | 计划编译、冲突检测、执行失败语义已经出现 | `AuditPlanCompiler` 上升为 `IPlanCompiler` | 保留 `Audit` 主命名 | `Audit` 不能表达可执行变更 |

---

## 16. 重写补强：模型归属总表

| 模型 | 归属上下文 | 战略角色 | 为什么不能放到别处 |
|---|---|---|---|
| `RunRequest` | Input | 一次运行的外部意图 | 放到事实域会让事实域依赖入口 |
| `WorkspaceContext` | Input | 工程边界和装配结果 | 放到执行域会导致分析和验证重复装配 |
| `AnalysisSnapshot` | Program Fact | 稳定事实快照 | 放到查询域会让查询变成事实所有者 |
| `GraphSnapshot` | Program Fact | 底层图的只读封装 | 放到决策域会泄漏实现 |
| `ProgramFact` | Program Fact | 统一事实语言 | 放到候选域会让事实被规则污染 |
| `QueryResult` | Query & Slice | 查询输出 | 放到事实域会混淆事实和筛选结果 |
| `SliceResult` | Query & Slice | 上下游影响范围 | 放到决策域会导致规则重复实现切片 |
| `RuleTarget` | Transformation Decision | 规则关注对象 | 放到计划域会跳过候选解释 |
| `ChangeCandidate` | Transformation Decision | 可变更候选 | 放到计划域会让计划承担裁决职责 |
| `RewriteDecision` | Transformation Decision | 最终裁决 | 放到执行域会重现边写边判定 |
| `RewritePlan` | Transformation Plan | 可执行蓝图 | 放到决策域会缺少执行排序边界 |
| `PlanChangeItem` | Transformation Plan | 单个计划项 | 脱离计划后没有独立生命周期 |
| `RewriteResult` | Transformation Execution | 真实执行结果 | 放到计划域会混淆计划和事实结果 |
| `VerificationEvidence` | Evidence | 可信证明链 | 放到报告域会变成展示文本 |
| `RunReport` | Reporting | 人类消费视图 | 放到证据域会污染证据模型 |

---

## 17. 重写补强：旧代码战略吸收规则

| 历史对象 | 吸收 | 不吸收 | 战略归属 |
|---|---|---|---|
| `ClassRefactorer` | 全局引用判断、迭代删除、类型级风险识别 | 边分析边改文件 | 候选、决策、计划 |
| `MethodRefactorer` | 动作集合化、按文档分组、批量重写 | 动作直接由分析器输出 | 计划、执行 |
| `AdvancedCodeAnalyzer` | 分析服务化、结构结果聚合 | 大分析服务承载全部业务语义 | 程序事实 |
| `AuditPlanCompiler` | 覆盖裁决、冲突检测、计划编译 | `Audit` 主命名 | 计划 |
| `RoslynRewriteExecutor` | 不二次推理、不可定位目标立即失败 | 执行器持有过多业务推理 | 执行 |
| 当前 `src/Analysis` | CPG 前端、Pass、数据流、切片、查询 | 底层图名词直接暴露给所有上层 | 程序事实、查询与切片 |

---

## 18. 重写补强：战略验收标准

- 任意新功能能明确归入一个上下文；
- 任意模型能明确归属，不能同时属于两个上下文；
- 任意接口的输入输出不能跨越三个以上上下文；
- 执行器只消费 `RewritePlan`，不能消费 `ChangeCandidate`；
- 报告只消费证据和结果，不能反向影响计划；
- 旧名称如果继续出现，必须位于迁移层或适配层，并注明最终淘汰目标。
