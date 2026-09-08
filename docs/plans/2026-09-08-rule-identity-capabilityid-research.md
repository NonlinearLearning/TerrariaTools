# `IRuleDefinition` 身份字段研究：以 `CapabilityId` 取代 `RuleId`

> 本方案已被 2026-09-08 的 RuleId-only 决策取代，不得按本文执行。请改用 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)。

> **状态：历史研究（不再是当前决策）。** 本文保留早期以 `CapabilityId` 取代 `RuleId` 的研究证据；最新目标是删除 `CapabilityId`、保留重新定义后的 `RuleId`。当前执行入口为 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)。

日期：2026-09-08
范围：设计讨论和外部资料研究；本轮不修改规则接口、运行时、测试或现有用户改动。

> 历史研究声明：本文保留了早期对兼容迁移、旧身份映射和额外身份字段的比较，供理解取舍使用；这些内容不是当前方案。当前方案已经改为一次性破坏性迁移：删除 `CapabilityId`，只保留重新定义后的 `RuleId`，不保留任何过渡字段、旧 ID 映射、双读单写或旧 rewrite plan 兼容读取。当前实施顺序见 [RuleId-only 执行文档](2026-09-08-rule-identity-ruleid-execution.md)。

## 结论摘要

建议删除 `IRuleDefinition.RuleId`，让 `CapabilityId` 成为规则声明层唯一的规范身份，但不能把这理解为“系统中只剩一个字符串字段”。应明确拆分以下几种身份：

| 身份 | 职责 | 是否由规则实现自行填写 |
| --- | --- | --- |
| `CapabilityId` | 规则的稳定语义/能力键；用于目录、配置、结果归因和跨版本识别 | 是，且必须稳定、非空、在目录作用域内唯一 |
| `RuleNodeId` | 执行图中的节点键；至少由 `RuleKind + CapabilityId` 结构化派生 | 否，由注册目录或执行引擎生成 |
| `RuleInstanceId` | 同一能力以不同配置、目标或实例多次注册时的运行实例键 | 仅在确实支持多实例时生成 |
| `LegacyRuleId` | 旧 rewrite plan、日志、diff category、replay 的兼容键 | 只在兼容层/旧工件适配器中保留 |
| `SourceAnchor` / `EvidenceId` | 某次命中、证据或语法节点的身份 | 否，来自分析输入和证据图 |
| `DefinitionFingerprint` | 规则契约或实现变化后的缓存、复现和审计指纹 | 否，由目录/构建过程计算 |

所以最终的接口可以保持很小：

```csharp
public interface IRuleDefinition
{
    string CapabilityId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }
}
```

这里的关键不是把 `RuleId` 的字符串机械改名，而是规定：`CapabilityId` 是规范身份；阶段、执行节点、旧 ID 和某次产出的来源分别由各自的模型承载。

## 当前仓库证据

当前接口同时暴露 `CapabilityId` 和 `RuleId`（[`IRuleDefinition.cs`](../../src/NLISSN.Rule/IRuleDefinition.cs)）。四个阶段基类都把 `CapabilityId` 默认实现成 `RuleId`，同时要求实现类继续提供抽象 `RuleId`（[`RuleDefinitionMark.cs`](../../src/NLISSN.Core/Marking/RuleDefinitionMark.cs)、[`RuleDefinitionPropagate.cs`](../../src/NLISSN.Core/Propagation/RuleDefinitionPropagate.cs)、[`RuleDefinitionLift.cs`](../../src/NLISSN.Core/Lifting/RuleDefinitionLift.cs)、[`RuleDefinitionPropose.cs`](../../src/NLISSN.Core/Decision/RuleDefinitionPropose.cs)）。这构成了重复的双身份接口，而不是两个真正独立的概念。

目录目前只校验 `CapabilityId` 非空和唯一，不校验 `RuleId`（[`RuleCatalog.cs`](../../src/NLISSN.Application/Catalog/RuleCatalog.cs)）。对当前规则声明进行静态扫描得到 78 个 `CapabilityId` 声明，全部唯一；79 个 `RuleId` 声明中存在两个 `DEL-DEAD-001`，分别属于 Mark 和 Propose 的不可达方法规则。这说明当前代码的真实关系是：

```text
CapabilityId：已经按目录被当作唯一的规范键
RuleId：旧的阶段/工件键，只能在某些上下文中与 Stage 组合后唯一
```

`RuleId` 仍然有较大的运行时扩散面，不能在接口删除的同一小改动中全部消失：

- `RulePipeline` 以 `RuleNodeId.For(declaration.Kind, declaration.Rule.RuleId)` 创建合同图和执行图节点（[`RulePipeline.cs`](../../src/NLISSN.Application/Analysis/RulePipeline.cs)）。
- `RuleNodeId.For` 当前通过字符串拼接生成 `"{kind}:{ruleId}"`（[`RuleGraph.cs`](../../src/NLISSN.Rule/RuleGraph.cs)）。
- `MarkRecord`、`PropagatedMarkRecord`、`LiftedMarkRecord`、`DecisionUnit`、`RuleDecision`、`RewritePlanEdit` 和证据节点仍以 `RuleId` 记录产出来源。
- `RuleBindingValidator` 从图节点字符串截取冒号后的部分，再与 mark 的 `RuleId` 比较（[`RuleBindingValidator.cs`](../../src/NLISSN.Core/Validation/RuleBindingValidator.cs)）。这是一种字符串反解析，迁移时必须同时取消。
- diff category registry 以旧 `DEL-*` / `CLR-*` / `PRIV-*` 字符串作为键（[`RuleDiffCategory.cs`](../../src/NLISSN/Artifacts/RuleDiffCategory.cs)）；传播事实去重也把 `RuleId` 放入 key（[`PropagationFactKey.cs`](../../src/NLISSN.Core/Propagation/PropagationFactKey.cs)）。

当前 `CapabilityId` 的命名还需要做一个领域确认：`RuleFactPorts` 中的语义标签（例如 `Target.Expression`）描述的是事实端口，而各规则的 `CapabilityId`（例如 `mark.target.member-access`、`mark.target.invocation`）描述的是不同的规则能力。也就是说，当前 `CapabilityId` 实际上更接近“可注册规则的稳定能力键”，不是一个可以由多个实现共享的事实端口名。如果未来允许多个规则实现同一能力，唯一性作用域必须改为 `(RuleKind, CapabilityId)`，不能继续假设单独的 `CapabilityId` 全局唯一。

## 外部项目如何标识规则

下面只使用官方文档或项目源码作为依据；资料访问日期为 2026-09-08。

| 项目 | 规范身份及作用域 | 同时携带的字段 | 对本仓库的启示 |
| --- | --- | --- | --- |
| Roslyn analyzer | `DiagnosticDescriptor.Id`；官方建议唯一、带项目专属 prefix，且改变 ID 会使已有 suppression 失效，因此是稳定的外部契约 | `Title`、`MessageFormat`、`Category`、`DefaultSeverity`、`IsEnabledByDefault`、`Description`、`HelpLinkUri`、`CustomTags` | 稳定 ID 与展示、诊断策略、帮助链接分离；旧 ID 迁移不能静默重用 |
| ESLint | 规则 ID 出现在配置和 `context.id` 中；规则模块本身通过 `meta` 描述行为 | `meta.type`（problem/suggestion/layout）、`docs.description/url`、`schema`、`defaultOptions`、`messages/messageId`、`fixable`、`hasSuggestions`、弃用信息 | 规则键、配置 schema、消息键和修复能力是不同层次；`messageId` 不能冒充规则 ID |
| Semgrep | YAML 顶层 `id` 是“唯一且可描述”的标识；作用域由规则集合/注册环境决定 | `message`、`severity`、`languages`、匹配算子、`options`、`fix`、任意 `metadata`、Semgrep `min-version` / `max-version`、`paths` | 规则 ID 外还需要执行引擎兼容范围、启用路径、修复和自由扩展元数据 |
| CodeQL | 查询的 `@id` 必须唯一；`@previous-id` 专门表示查询改名后的历史身份 | `@description`、`@kind`、`@name`、`@tags`、`@precision`、`@problem.severity`、`@security-severity` | 结果稳定性需要“当前 ID + 旧 ID 映射”；严重度、准确率、查询种类不应塞进 ID |
| OpenRewrite | `Recipe.getName()` 是技术身份，默认来自实现类名；`equals` / `hashCode` 也按 name 判断。另有面向读者的 `getDisplayName()` 和带配置摘要的 instance name | `description`、tags、estimated effort、options、子 recipe 列表、maintainer/contributor、examples、recipe source、是否需要额外 cycle | 技术身份、实例显示名、配置选项、组合关系和来源应分开；同一个 recipe 能产生不同配置实例 |
| Drools | `package` / rule unit 提供命名空间；每个 rule unit 内 rule name 必须唯一 | `salience`、`enabled`、生效/过期时间、`no-loop`、`activation-group`、timer、calendar、dialect 等执行属性 | 规则名解决识别，优先级、生命周期、调度和循环控制是独立执行元数据 |
| SonarQube | 自定义规则有自动生成的 `Key`，与人类可读 `Name` 分离；规则还按质量、类型、分类和状态被管理 | `Type`、Category/Attribute、Software Quality、Severity、Status、Tags、描述、代码示例和 references | 规则键不应携带质量维度、生命周期状态或展示信息；删除/改写工具需要独立的质量影响和状态元数据 |
| PMD | 规则引用使用规则集/类别路径和规则名；同一个规则可以在规则集引用处覆盖消息、优先级和属性 | `message`、`priority`、`properties`，并由规则文档描述属性语义 | 规则定义身份与一次配置/启用实例分开；阈值等选项不应编码到规则 ID |

这些实现并没有一个统一的字段命名，但有相当稳定的结构：

1. 一个不随显示文案、实现类重构或执行顺序变化的稳定键。
2. 一个明确的作用域或命名空间。
3. 独立的描述、分类、严重度/风险、帮助链接和消息字段。
4. 独立的配置 schema、默认值、启用状态和兼容版本。
5. 对会改变结果含义的变更提供旧键映射、版本或指纹。
6. 对执行顺序、重复执行、固定点、修复或副作用的控制字段。

## 三种设计方案

### 方案 A：`CapabilityId` 是唯一规范规则键，阶段由类型/注册上下文提供（推荐）

规则实现只声明 `CapabilityId`；Mark、Propagate、Lift、Propose 的阶段来自基类和目录注册，而不是另一个字符串 ID。目录强制 `CapabilityId` 全局唯一，并为每个声明构造：

```text
RuleDescriptor
  CapabilityId       = mark.target.member-access
  Stage              = Mark
  DisplayName        = Match s-rooted member access expressions
  Contract           = Consumes / Produces / InputCardinality
  RequiredCapabilities = Default
  Compatibility      = engine/schema constraints
  LegacyRuleId       = DEL-SOBJ-MARK-MEMBER-001   // 仅迁移期间
```

执行图使用结构化的 `RuleNodeId(Stage, CapabilityId)`。即使当前 `CapabilityId` 已全局唯一，也保留 Stage 作为图身份的一部分，避免未来同一语义能力在不同阶段出现时再次引入字符串碰撞。图节点由目录派生，规则实现不手写，也不允许 validator 从 `NodeId.Value` 反解析。

优点是接口更深：调用方只需要认识规范能力键和已有输入/输出合同；规则 ID 与旧 artifact 格式脱钩；缓存、证据和 telemetry 可以稳定地指向能力键。代价是需要迁移所有结果模型和旧工件适配器。

### 方案 B：接口只保留 `CapabilityId`，但规范规则身份是 `(Stage, CapabilityId)`

如果领域上允许 Mark 和 Propose 共享同一个能力名，那么 `CapabilityId` 不是唯一规则键，而是语义能力名。目录改为校验 `(RuleKind, CapabilityId)`，规则身份写成：

```text
RuleKey = (Mark, "target.unreachable-method")
RuleKey = (Propose, "target.unreachable-method")
```

此时结果来源、图节点和 evidence 必须保存结构化 `RuleKey`；不能只把 `CapabilityId` 放进 `MarkRecord`，否则跨阶段的同名产出无法区分。方案 B 更符合“一个能力可由多个阶段协作实现”的语义，但它意味着用户提出的“只保留 `CapabilityId` 进行标识”只能理解为“规则接口不再暴露第二个字符串”，不能理解为“全系统只用 `CapabilityId` 一个字段”。

### 方案 C：保留 `RuleId`，把 `CapabilityId` 作为附加语义标签

这可以降低短期迁移量，但延续了当前两个字符串的重复和不一致：目录只保证其中一个唯一，图节点依赖另一个，产出归因又依赖第三种隐含作用域。它还会继续诱导实现类手工维护旧 ID。除非必须维持尚未迁移的外部 API，否则不建议作为目标设计；最多只能是短期兼容适配器。

## 推荐的字段分层

### 保留在规则声明接口或规则合同中的字段

这些字段直接影响规则是否能被安全执行，不能只放在 UI 元数据中：

- `CapabilityId`：非空、规范化、不可复用；建议使用稳定的小写、带项目/域命名空间的形式，不从类名或注册顺序生成。
- `InputCardinality`：`All`、`ExactlyOne`、`Optional` 等输入消费语义。
- `Consumes` / `Produces`：事实端口和语法合同。
- `RequiredCapabilities`：构建 CPG 所需的能力集合；它目前在阶段基类中存在，即使暂时不放进 `IRuleDefinition`，目录也应将其纳入 descriptor。
- 阶段 `RuleKind`：建议由注册上下文/强类型基类提供，避免每个实现重复填写字符串。

### 由目录构造的 `RuleDescriptor` / `RuleRegistration`

不建议为了删除一个重复 ID 而把下面所有字段都塞进 `IRuleDefinition`；它们更适合由目录从规则实现、基类和配置构造：

```text
CapabilityId              // 规范键
Stage / RuleKind           // 结构化阶段
DisplayName / Description  // 面向人和文档
Category / Tags            // 检索和分组
EnabledByDefault          // 是否默认运行
OptionSchema / Defaults   // 配置约束
RequiredCapabilities      // CPG 需求
Consumes / Produces       // 规则合同
ExecutionPolicy           // 并行安全、确定性、固定点/重复运行语义
RuleRevision              // 规则语义修订号；可选
DefinitionFingerprint      // 机器生成的契约/实现指纹
Compatibility              // 引擎、工件 schema、CPG profile 兼容范围
Provenance                 // 程序集、类型、包版本、源码/帮助地址
LegacyRuleIds              // 迁移期间的阶段限定旧键
```

其中有几项需要谨慎使用：

- `Severity` 只在规则确实产生风险等级时增加；删除规则更可能需要 `Risk`、`DecisionPriority` 或动作影响，而不是照搬 analyzer 的 warning/error。
- `Priority`、`Order`、`Salience` 不应作为身份的一部分；只有当调度语义确实依赖它时才加入执行策略。
- `RuleRevision` 表达规则含义变化；`DefinitionFingerprint` 表达可复现/缓存所需的实际定义变化。两者不要混成一个可读字符串。
- `LegacyRuleId` 不是新规则的公开身份，不能参与新图节点去重、传播事实去重或新的 diff 分类查找。

### 结果、证据和图对象

规则实现产出时，来源最好不是任意字符串，而是由执行引擎注入的结构化 provenance：

```text
RuleProvenance
  Producer = RuleNodeId(Stage, CapabilityId)
  CapabilityId
  LegacyRuleId?             // 只在旧工件/日志投影中输出
```

实际结果还要单独保存 `SourceAnchor`（文件、span、语法节点/CPG 节点）和 `EvidenceId`。它们回答的是“在哪里命中”和“由哪些事实证明”，不是“哪条规则定义”。`RuleNodeId` 应直接比较结构化字段；`RuleBindingValidator` 应比较 producer provenance 与当前执行节点，而不是切分冒号字符串。

## 迁移边界和顺序

1. 先冻结身份不变量：`CapabilityId` 的规范化规则、作用域、不可复用规则，以及 Stage 是否纳入唯一性。
2. 增加 `RuleDescriptor` / 结构化 `RuleNodeId` 和基线测试；先让旧 `RuleId` 适配层可以映射到新身份。
3. 迁移 `RulePipeline`、规则合同图、执行器、固定点去重和 validator；消除从 `NodeId.Value` 反解析。
4. 将 mark、propagation、lift、decision、evidence 和 rewrite plan 的新写入路径改为 `CapabilityId` 或结构化 `RuleProvenance`。
5. 将 diff category、replay、CLI 和旧 artifact 读取改为 `(Stage, LegacyRuleId) -> CapabilityId` 的显式映射。由于 `DEL-DEAD-001` 同时出现在 Mark 和 Propose，映射不能只以旧字符串为 key。
6. 生成一个带 artifact schema 版本的双读/单写窗口：新工件写规范身份，旧工件只读兼容字段；确认旧工件和外部脚本迁移后，再删除 `LegacyRuleId`。
7. 最后删除 `IRuleDefinition.RuleId` 以及四个阶段基类中的抽象属性，并同步更新测试、快照和设计文档。

这个顺序的核心是先迁移“身份的消费者”，再删接口字段。直接删除接口属性会留下编译错误；直接把旧 `RuleId` 全部替换成 `CapabilityId` 则可能让旧 diff、replay 和跨阶段来源失去可解释性。

## 建议的验收不变量

- 相同 `CapabilityId` 在同一个规则目录作用域内只能对应一个规范规则声明；若采用方案 B，则唯一键明确写成 `(Stage, CapabilityId)`。
- 改变显示名、帮助链接、实现类名或注册顺序不会改变 `CapabilityId` 或 `RuleNodeId`。
- 改变规则语义或输出合同会改变 `RuleRevision` 或 `DefinitionFingerprint`，并使相关缓存/回放按策略失效。
- 同一规则在 DOP 1 和更高 DOP 下的 graph、evidence、decision 和 rewrite 结果仍使用相同的结构化 producer identity。
- `RuleNodeId` 不再依赖字符串前缀切割；validator 不从 `NodeId.Value` 推断来源。
- 新工件不把 `LegacyRuleId` 当作规范键；旧工件可以通过阶段限定映射读取。
- 未配置多实例语义时，同一个 `CapabilityId` 不能通过重复注册产生两个隐含实例；若未来需要多实例，必须引入显式配置/实例身份。
- `CapabilityId` 不承载版本、严重度、优先级、文件位置或命中序号。

## 一手资料

- [Microsoft Learn：Choosing diagnostic IDs](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/choosing-diagnostic-ids)：ID 的唯一性、项目 prefix、稳定性和 suppression 兼容性。
- [dotnet/roslyn：DiagnosticDescriptor.cs](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Diagnostic/DiagnosticDescriptor.cs)：`Id`、标题、消息、分类、严重度、启用状态、帮助链接和 custom tags 的源码契约。
- [ESLint：Custom Rules](https://eslint.org/docs/latest/extend/custom-rules)：`context.id`、`meta`、消息 ID、schema、fix 和建议字段。
- [Semgrep：Rule structure syntax](https://semgrep.dev/docs/writing-rules/rule-syntax)：规则 `id`、匹配语言、消息、严重度、fix、metadata、版本和 paths。
- [CodeQL：Metadata for CodeQL queries](https://codeql.github.com/docs/writing-codeql-queries/metadata-for-codeql-queries/)：唯一 `@id`、`@previous-id`、query kind、tags、precision 和 severity。
- [OpenRewrite：Recipes](https://docs.openrewrite.org/concepts-and-explanations/recipes)；[Recipe.java](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/Recipe.java)：技术 name、display/instance name、description、tags、options、组合 recipe 和来源描述。
- [Drools：Rule Language Reference](https://docs.drools.org/latest/drools-docs/drools/language-reference/index.html)：rule unit 内名称唯一，以及 enabled、salience、生命周期、循环、分组和调度属性。
- [SonarQube：Adding coding rules](https://docs.sonarsource.com/sonarqube-server/2025.1/extension-guide/adding-coding-rules/)：自定义规则的 Key、Name、Type、质量维度、Severity、Status 和分类字段。
- [PMD：Configuring rules](https://pmd.github.io/pmd/pmd_userdocs_configuring_rules.html)：规则集引用处对 message、priority 和 properties 的覆盖。

## 历史建议与当前结论

本文早期曾建议以方案 A 为目标，并在迁移期保留阶段限定的旧身份适配；该建议已被用户的最新边界明确否决。当前结论只以 `CapabilityId` 作为规则声明和规则产出归因，阶段由 `RuleKind` 提供，执行图使用结构化 `RuleNodeId(RuleKind, CapabilityId)`。旧 rewrite plan 不转换，读取时直接拒绝；实施顺序以 [执行文档](2026-09-08-rule-identity-capabilityid-execution.md) 为准。
