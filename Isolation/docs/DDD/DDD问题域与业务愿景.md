# DDD 问题域与业务愿景

## 1. 文档目的

本文档回答三个根问题：

1. `TerrariaTools` 到底在解决什么问题；
2. 这个问题为什么值得长期投入；
3. 这个项目未来希望成为什么，而不希望成为什么。

本文档不是实现说明，不讨论某个类怎么写，也不直接决定接口细节。
它属于 DDD 的问题空间文档，作用是给后续战略设计、战术设计、模型命名和接口抽象提供统一语义。

---

## 2. 重写动机

当前旧版 DDD 文档存在几个明显问题：

- 问题域、战略、战术三层混写；
- 名称沿用历史代码名，语义不清；
- 很多词偏技术视角，不是业务视角；
- 模型存在但没有说明“为什么有这个模型”；
- 模型之间的关系大多靠读者猜；
- 旧项目历史被提及，但没有转化为清晰的业务语言。

因此，这次重写的首要目标不是“内容变多”，而是：

- 语言统一；
- 边界清晰；
- 名称明确；
- 模型有理由；
- 模型之间关系可追踪；
- 旧历史经验能被正确吸收。

---

## 3. 本文参考资料

### 3.1 外部方法资料

本文参考了以下方向的公开资料：

- Martin Fowler 关于 `Bounded Context`、战略建模的资料
- Microsoft Learn 关于 DDD、限界上下文、子域划分的资料
- 关于“如何写战略设计 / 产品愿景 / 架构愿景”的通用方法资料

### 3.2 GitHub 对标项目

本文重点参考了下列项目公开资料、README、文档与项目定位：

- `joernio/joern`
- `github/codeql`
- `returntocorp/semgrep`
- `openrewrite/rewrite`
- `dotnet/roslyn-analyzers`
- `sourcegraph/sourcegraph` 

这些项目并不是要被复制，而是用来回答：

- 外部世界如何命名类似问题；
- 类似工具如何定义问题域和愿景；
- 哪些能力在外部已经成为通用品；
- 哪些能力对 `TerrariaTools` 仍然具有差异化价值。

### 3.3 本地历史资料

本文参考了 `D:\ProjectItem\SourceCode\Net\TerrariaTools` 的代码历史，而不只是提交标题，尤其包括：

- 旧版 `ARCHITECTURE.md`
- 旧版 `DESIGN_CONCEPTS.md`
- 旧版 `ClassRefactorer.cs`
- 旧版 `MethodRefactorer.cs`
- `Analysis/AdvancedCodeAnalyzer.cs`
- `dome` 线中的 `Models.cs`、`AuditPlanCompiler.cs`、`RoslynRewriteExecutor.cs`
- 当前 `Isolation` 分支中的 `DefaultRoslynCpgBuilder.cs`、`CpgGraph.cs` 和集成测试

---

## 4. 项目的真实出身

### 4.1 不是从“图平台”起步

从旧代码看，项目的出发点不是“做一个图数据库”或“做一个查询平台”，而是：

- 自动重构；
- 死代码清理；
- 方法私有化；
- 表达式重写；
- 最小化提取；
- 行为一致性验证。

换句话说，项目最早的业务心智是：

> 如何理解复杂 C# 代码，并且安全地删、改、裁、提取。

### 4.2 为什么后来会出现图、切片和数据流

因为旧版在做自动改写时迟早会撞到这些问题：

- 只看语法不够；
- 只看单文件不够；
- 只看直接调用不够；
- 不理解传播和依赖闭包，就不敢自动改；
- 不理解数据流和控制流，就很难保证动作是安全的。

因此，程序图、切片、调用图、CFG、DDG 并不是项目的目的，而是项目为了实现“安全程序变换”所必然发展出的中间能力。

### 4.3 为什么后来又出现 `dome` 的计划驱动模型

因为旧版的另一个问题是：
“分析”和“执行”太近，“推理”和“改写”耦合太重。

`dome` 这条线的意义在于，它第一次把下面这件事说清楚了：

- 分析不是执行；
- 标记不是计划；
- 计划不是执行器；
- 执行器不应该反向重做规则判断。

这说明项目历史里已经自己摸到了一个关键方向：

> 要从“会改代码的工具”走向“可计划、可审计、可解释的程序变换系统”。

---

## 5. 问题域定义

### 5.1 一句话定义

`TerrariaTools` 的问题域定义为：

> 面向 C# 工程的语义理解、影响分析、程序裁剪、自动变换与变更验证。

### 5.2 为什么这样定义

这个定义不是从技术栈出发，而是从问题出发。

它强调五件事：

1. **语义理解**
   不是文本替换，不是浅层 AST 扫描，而是语义级理解。

2. **影响分析**
   不是只知道“谁调用谁”，而是知道“改一个点会影响哪些点”。

3. **程序裁剪**
   不是只做提示，而是支持删除、清空、最小化、影子提取等动作。

4. **自动变换**
   不是只报告，而是可以产出可执行变更。

5. **变更验证**
   不是改完就算结束，而是要提供证据和风险说明。

### 5.3 为什么不能把问题域写成“程序图平台”

因为程序图只是能力底座，不是用户真正付费或真正痛的地方。
用户的真实痛点是：

- 不知道哪些能删；
- 不敢删；
- 不敢批量改；
- 改完说不清是否可信。

因此，问题域必须围绕“理解 + 变更 + 证据”三联体，而不是围绕某个中间技术表示。

---

## 6. 用户与场景

### 6.1 第一类用户：平台能力开发者

他们关心的是：

- 有没有统一事实层；
- 有没有可复用查询；
- 有没有稳定模型；
- 新规则是否容易接入。

### 6.2 第二类用户：自动化工具构建者

他们关心的是：

- 能否做死代码清理；
- 能否做私有化；
- 能否做最小化提取；
- 能否生成影子代码；
- 能否形成计划并执行。

### 6.3 第三类用户：工程使用者

他们更关心的是：

- 某个系统能否安全瘦身；
- 某个升级能否自动进行；
- 某批规则是否能批量落地；
- 改动是否可信。

### 6.4 典型场景

典型场景包括：

- 类删除；
- 方法删除；
- 方法私有化；
- 方法体清空；
- 成员切片；
- 影子类生成；
- 最小运行闭包提取；
- 计划驱动改写；
- 证据驱动变更审计。

---

## 7. 业务愿景

### 7.1 一句话愿景

> 把复杂 C# 代码的理解、裁剪、改写与验证，变成可复用、可执行、可解释的工程能力。

### 7.2 中期愿景

中期愿景不是“全平台化”，而是：

- 统一语义底座稳定；
- 计划驱动改写跑通；
- 旧版高价值改写场景接回新架构；
- 证据输出成为标准产物。

### 7.3 长期愿景

长期愿景是：

- 成为面向 C# 的程序变换平台；
- 在统一事实层上支撑分析、规则、计划、执行和验证；
- 让项目不再依赖个别实现者经验，而依赖稳定模型和流程。

### 7.4 非愿景

本文明确声明，这个项目当前**不以以下方向为愿景中心**：

- 通用多语言平台；
- 企业治理大盘；
- 图可视化产品；
- 纯查询语言产品；
- 一次性专项工具。

---

## 8. 对标项目给出的启发

### 8.1 Joern

Joern 告诉我们：

- 程序图是有价值的；
- 分析层可以统一；
- 语义增强与查询能力必须分层。

但它的主叙事更偏安全分析，而不是安全程序变换。

### 8.2 CodeQL

CodeQL 告诉我们：

- 程序事实一旦稳定，就可以沉淀规则资产；
- 查询层不只是调试工具，而是知识表达方式。

但它并不把自动改写作为主叙事。

### 8.3 Semgrep

Semgrep 告诉我们：

- 规则入口必须尽可能低门槛；
- 不是什么问题都值得走最重分析链路。

### 8.4 OpenRewrite

OpenRewrite 告诉我们：

- 自动改写完全可以是一等业务；
- 计划化和批量化非常重要；
- “recipe/plan” 优于零散脚本。

### 8.5 Roslyn Analyzers

Roslyn Analyzer 生态告诉我们：

- 诊断和修复天然应该成对存在；
- 统一语言如果能进入 IDE / 开发流，会更容易被采用。

### 8.6 对标后的结论

`TerrariaTools` 最合适的自我定义不是任何一个现成工具的翻版。
更准确的表达是：

> 它是一套以 C# 为中心、以统一语义事实为底座、以计划驱动程序变换为主线、以变更证据为可信机制的领域平台。

---

## 9. 子域划分

### 9.1 核心域一：统一程序事实

职责：

- 输入工程；
- 构建统一事实层；
- 表达结构、类型、调用、控制流、数据流等关系。

为什么是核心域：

- 没有统一事实层，上层规则就只能重复造轮子；
- 当前 `Isolation` 分支最成熟的资产就在这里。

### 9.2 核心域二：程序变换决策

职责：

- 识别变更目标；
- 传播影响；
- 执行保护规则；
- 形成最终裁决。

为什么是核心域：

- 这是项目真正区别于普通查询工具的地方；
- 旧版高价值经验主要沉淀在这里。

### 9.3 核心域三：变更计划与执行

职责：

- 把裁决编译成计划；
- 执行计划；
- 输出变更结果。

为什么是核心域：

- `dome` 线已经证明这条路径是正确方向；
- “计划驱动”是系统走向成熟的关键标志。

### 9.4 核心域四：变更证据

职责：

- 汇总静态原因；
- 汇总编译与动态验证；
- 产出风险摘要。

为什么是核心域：

- 没有证据，自动变更就难以被信任；
- 旧版行为保证能力已经给出历史证明。

### 9.5 支撑域

支撑域包括：

- 输入装配；
- 查询；
- 切片；
- 报告；
- CLI；
- 配置。

### 9.6 通用域

通用域包括：

- 日志；
- 文件系统；
- 序列化；
- 进程与参数解析；
- 通用测试基础设施。

---

## 10. 命名原则

### 10.1 为什么要改名

当前旧文档和部分现有模型命名存在以下问题：

- 有些词是技术术语，不是业务术语；
- 有些词过于抽象，例如 `TargetId`；
- 有些词复用旧历史名称，但新语境已变；
- 有些词没有说明是“事实”“候选”“裁决”“计划”还是“结果”。

因此，问题域文档必须先给出统一命名原则。

### 10.2 命名原则一：优先表达业务角色

应优先使用：

- `ProgramFact`
- `ChangeCandidate`
- `RewriteDecision`
- `RewritePlan`
- `VerificationEvidence`

而不是优先使用只对实现者友好的内部抽象名。

### 10.3 命名原则二：同一层只说同一类语言

例如：

- 问题域层用“事实、候选、计划、证据”；
- 不直接把 `SyntaxNode`、`SemanticModel`、`TargetId` 当成共享语言。

### 10.4 命名原则三：让名字能回答“它为什么存在”

一个好名字，应该让读者一眼看出：

- 它是输入；
- 它是事实；
- 它是候选；
- 它是计划；
- 它是结果；
- 它是证据。

---

## 11. 核心问题域模型总表

下表是问题域层面最重要的模型清单。
这里不讲接口细节，只讲“为什么需要它”和“它与谁有关”。

### 11.1 `WorkspaceContext`

- **作用**：表示一次运行所使用的工程上下文
- **为什么存在**：因为所有分析和变更都必须绑定到一套明确输入
- **上游**：`RunRequest`
- **下游**：`AnalysisSnapshot`

### 11.2 `AnalysisSnapshot`

- **作用**：表示一次分析运行得到的统一事实快照
- **为什么存在**：因为后续查询、规则、计划必须共享同一份分析真源
- **上游**：`WorkspaceContext`
- **下游**：`ChangeCandidate`

### 11.3 `ChangeCandidate`

- **作用**：表示“值得考虑变更”的候选目标
- **为什么存在**：因为“发现问题”和“决定执行”之间必须有中间层
- **上游**：`AnalysisSnapshot`
- **下游**：`RewriteDecision`

### 11.4 `RewriteDecision`

- **作用**：表示已经过传播、保护和冲突处理的决策结果
- **为什么存在**：因为候选不等于最终动作
- **上游**：`ChangeCandidate`
- **下游**：`RewritePlan`

### 11.5 `RewritePlan`

- **作用**：表示可执行的变更计划
- **为什么存在**：因为执行器不能直接消费候选或临时判断
- **上游**：`RewriteDecision`
- **下游**：`RewriteResult`

### 11.6 `RewriteResult`

- **作用**：表示计划执行后的结果
- **为什么存在**：因为系统需要区分“计划”与“结果”
- **上游**：`RewritePlan`
- **下游**：`VerificationEvidence`

### 11.7 `VerificationEvidence`

- **作用**：表示对本次变更的静态和动态证据汇总
- **为什么存在**：因为结果本身不等于可信
- **上游**：`RewriteResult`
- **下游**：`RunReport`

### 11.8 `RunReport`

- **作用**：对外输出的一次运行摘要
- **为什么存在**：因为用户需要可读结果，而不是内部对象集合
- **上游**：`AnalysisSnapshot`、`RewritePlan`、`RewriteResult`、`VerificationEvidence`

---

## 12. 模型关系总图

问题域层的关系应理解为：

`RunRequest -> WorkspaceContext -> AnalysisSnapshot -> ChangeCandidate -> RewriteDecision -> RewritePlan -> RewriteResult -> VerificationEvidence -> RunReport`

这里每一层都必须存在。
如果跳层，会带来具体问题：

- 直接从 `AnalysisSnapshot` 到 `RewritePlan`：缺少显式裁决；
- 直接从 `ChangeCandidate` 到 `RewriteResult`：缺少可审计计划；
- 直接从 `RewriteResult` 到 `RunReport`：缺少证据层。

---

## 13. 本文对后续文档的约束

为了防止后续再次出现“名字乱、层次乱、模型关系不清”，本文明确给出以下约束：

1. 战略设计不得重新定义问题域核心模型；
2. 战术设计必须沿用本文给出的问题域层模型命名；
3. 任何接口设计都必须说明自己消费和产出的是哪一层模型；
4. 任何新增模型都必须说明：
   - 为什么存在；
   - 属于哪一层；
   - 与哪些模型有关；
   - 不能被哪个现有模型替代。

---

## 14. 结论

问题域与业务愿景层最重要的结论只有一句：

> `TerrariaTools` 不应再被定义成“某个分析器”“某个图项目”或“某次专项脚本”，而应被定义成一套面向 C# 代码系统、用于程序理解、程序变换与变更验证的领域平台。

这个结论决定了：

- 后续战略设计必须围绕子域与上下文展开；
- 后续战术设计必须围绕模型、接口和流程展开；
- 所有文档和代码命名都要从“事实、候选、决策、计划、结果、证据”这条主线出发。

---

## 15. 应用资料

### 外部资料

- Martin Fowler：`https://martinfowler.com/bliki/BoundedContext.html`
- Microsoft Learn DDD 资料：`https://learn.microsoft.com/zh-cn/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice`
- Joern：`https://github.com/joernio/joern`
- CodeQL：`https://github.com/github/codeql`
- Semgrep：`https://github.com/returntocorp/semgrep`
- OpenRewrite：`https://github.com/openrewrite/rewrite`
- Roslyn Analyzers：`https://github.com/dotnet/roslyn-analyzers`
- Sourcegraph：`https://github.com/sourcegraph/sourcegraph`

### 本地历史资料

- `D:\ProjectItem\SourceCode\Net\TerrariaTools\ARCHITECTURE.md`
- `D:\ProjectItem\SourceCode\Net\TerrariaTools\docs\DESIGN_CONCEPTS.md`
- `git show 28b63fe:RewriteCodeExpressions/ClassRefactorer.cs`
- `git show 4e15788:RewriteCodeExpressions/MethodRefactorer.cs`
- `git show 4e15788:Analysis/AdvancedCodeAnalyzer.cs`
- `git show 9fd0f68:dome/src/Core/Models.cs`
- `git show 9fd0f68:dome/src/Plan/AuditPlanCompiler.cs`
- `git show 9fd0f68:dome/src/Rewrite/Roslyn/RoslynRewriteExecutor.cs`

---

## 16. 重写补强：资料如何真正应用到问题域

本节补充说明：参考资料不是装饰性列表，而是直接用于校正问题域、业务愿景和命名。

| 资料来源 | 可借鉴点 | 本项目采用方式 | 本项目不照搬的原因 |
|---|---|---|---|
| Martin Fowler `Bounded Context` | 同一名词只能在一个上下文里保持精确含义 | 用来约束 `RuleTarget`、`PlanTarget`、`ProgramElementKey` 不能继续混成 `TargetId` | 本项目不是微服务拆分，不能把上下文等同于服务目录 |
| Microsoft DDD 资料 | 核心域、支撑域、通用域需要围绕业务竞争力划分 | 把“可验证程序变换”定为核心域，把 CLI、报告、项目加载定为支撑域 | 微服务样例不能直接映射到代码工具工程 |
| 阿里云 DDD 文章 | 先统一语言，再做领域建模，再落到代码 | 三份 DDD 文档按问题域、战略、战术分开 | 文章是通用方法，需要结合代码理解和自动改写领域重新解释 |
| Joern | CPG 能统一 AST、CFG、PDG、调用图和数据流 | 确认 `AnalysisSnapshot` 应成为事实底座 | Joern 主目标是安全分析和查询，本项目还要覆盖改写和证据 |
| CodeQL | 代码数据库和查询可以让复杂分析产品化 | 确认查询与切片是重要支撑域 | CodeQL 不以执行代码改写为主线 |
| Semgrep | 规则命中必须可解释、可集成、可自动化 | 要求 `ChangeCandidate` 必须携带 `CandidateReason` | Semgrep 的模式匹配不能覆盖所有 C# 语义重写 |
| OpenRewrite | 自动化重构需要 Recipe、计划、执行分离 | 要求 `RewritePlan` 成为执行器唯一输入 | OpenRewrite 面向 JVM 生态，不能直接复制类型和 API |
| Roslyn Analyzers | C# 分析、诊断、CodeFix 已有成熟边界 | Roslyn 作为事实采集和执行适配层 | Analyzer / CodeFix 不是完整跨项目变更计划模型 |
| Sourcegraph | 大规模工程需要索引、搜索、导航和人类可理解入口 | 强化 `WorkspaceContext` 和报告消费 | Sourcegraph 的核心是代码搜索，不是变更证明 |
| 本地 `dome` 历史 | 计划编译、冲突检测、执行失败语义已被验证 | 将 `AuditPlanCompiler` 思路上升为 `RewritePlan` 与 `IPlanCompiler` | `Audit` 命名会误导业务愿景 |
| 当前 `Isolation` 代码 | CPG 前端、Pass、查询、切片、数据流已形成事实底座 | 将底层图能力包装成 `AnalysisSnapshot` | 不能让底层图模型直接污染候选、计划和报告 |

---

## 17. 重写补强：业务能力地图

| 能力层 | 能力名称 | 解决的问题 | 核心模型 | 为什么需要单独存在 |
|---|---|---|---|---|
| 输入层 | 工程装配 | 本次运行针对哪个工程、哪些项目、哪些文件、哪些规则 | `RunRequest`、`WorkspaceContext` | 避免 CLI、配置和测试夹具污染领域核心 |
| 事实层 | 程序理解 | 程序中有哪些类型、方法、调用、控制流、数据流 | `AnalysisSnapshot`、`ProgramFact` | 事实必须可复用，不能被某个规则私有化 |
| 查询层 | 事实筛选 | 哪些事实满足某个查询或切片条件 | `QueryResult`、`SliceResult` | 规则不应直接遍历底层图节点 |
| 候选层 | 变更候选识别 | 哪些元素“可能”需要变更 | `RuleTarget`、`ChangeCandidate` | 命中不等于最终执行，需要中间层 |
| 决策层 | 变更裁决 | 哪些候选允许变更、哪些被保护、哪些冲突 | `RewriteDecision` | 保护和冲突必须结构化，而不是散落在 `if` 分支 |
| 计划层 | 变更编排 | 应按什么顺序、对哪些位置执行哪些动作 | `RewritePlan` | 执行器需要稳定蓝图，不能消费候选集合 |
| 执行层 | 安全写回 | 计划是否真实应用、哪些文件改变、失败在哪里 | `RewriteResult` | 结果必须与计划分离，便于证据追踪 |
| 证据层 | 可信证明 | 如何证明变更安全、合理、可复核 | `VerificationEvidence` | 自动变更没有证据就无法进入真实工程 |
| 报告层 | 人类消费 | 用户、CI、审查者如何理解本次运行 | `RunReport` | 报告是消费视图，不能反向污染领域决策 |

---

## 18. 重写补强：核心模型关系详表

| 模型 | 为什么存在 | 上游关系 | 下游关系 | 不应依赖 |
|---|---|---|---|---|
| `RunRequest` | 把用户意图结构化，避免入口参数散落 | 用户、CLI、配置文件 | `WorkspaceContext` | Roslyn、CPG、规则实现 |
| `WorkspaceContext` | 固化工程边界和加载结果 | `RunRequest` | `AnalysisSnapshot`、`RewriteResult`、`VerificationEvidence` | 具体变更决策 |
| `AnalysisSnapshot` | 保存一次分析的稳定事实快照 | `WorkspaceContext`、CPG Builder、Pass Pipeline | `QueryResult`、`SliceResult`、`ChangeCandidate` | 计划和执行结果 |
| `ProgramFact` | 统一类型、方法、调用、数据流等事实语言 | `AnalysisSnapshot` | 查询、切片、候选构建 | UI、报告、文件写回 |
| `RuleTarget` | 表示规则关注的事实对象，避免暴露底层图 | `QueryResult`、`SliceResult` | `ChangeCandidate` | 执行器 |
| `ChangeCandidate` | 表示可能变更的对象和出现原因 | `RuleTarget`、`CandidateReason` | `RewriteDecision` | 写回动作 |
| `RewriteDecision` | 表示经过传播、保护、冲突处理后的裁决 | `ChangeCandidate`、`AnalysisSnapshot` | `RewritePlan`、`VerificationEvidence` | Roslyn SyntaxNode |
| `RewritePlan` | 表示可执行变更蓝图 | `RewriteDecision` | `RewriteResult`、`VerificationEvidence` | 查询服务和候选构建器 |
| `RewriteResult` | 表示计划执行后的真实结果 | `RewritePlan`、`WorkspaceContext` | `VerificationEvidence`、`RunReport` | 新决策生成 |
| `VerificationEvidence` | 连接事实、决策、计划、结果，形成可信证明 | `AnalysisSnapshot`、`RewriteDecision`、`RewritePlan`、`RewriteResult` | `RunReport`、CI、人工审查 | 执行改写 |
| `RunReport` | 把机器模型转换成人类可读结果 | 全链路摘要 | 用户、CI、文档 | 领域判断 |

---

## 19. 重写补强：旧命名淘汰理由

| 旧名称 | 问题 | 新语言 | 淘汰原因 |
|---|---|---|---|
| `TargetId` | 不知道属于事实、候选还是计划 | `ProgramElementKey`、`RuleTargetKey`、`PlanTargetKey` | 不同层的目标含义不同，不能用一个泛名贯穿 |
| `MarkDecision` | 混合“命中”和“裁决” | `ChangeCandidate`、`RewriteDecision` | 候选和最终决策必须分离 |
| `AuditPlan` | 容易理解成审计计划 | `RewritePlan` | 项目核心是可执行程序变换，不是审计 |
| `AnalysisView` | “视图”边界过宽 | `AnalysisSnapshot` | 需要强调一次分析事实快照 |
| `PlannedChange` | 语义尚可但归属不清 | `PlanChangeItem` | 强调它只能存在于计划内部 |
| `RunResult` | 太泛，可能是执行结果、验证结果或报告结果 | `RewriteResult`、`RunReport` | 结果必须按层拆开 |

这些淘汰不是为了美化命名，而是为了阻止未来再次出现“一个词在多层含义不同”的设计问题。
