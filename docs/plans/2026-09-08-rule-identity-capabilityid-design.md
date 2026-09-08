# 规则身份收敛设计：只保留 `CapabilityId`

> 本方案已被 2026-09-08 的 RuleId-only 决策取代，不得按本文执行。请改用 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)。

> **状态：历史方案（已废弃）。** 本文曾建议删除 `RuleId`、保留 `CapabilityId`；最新决策相反：删除 `CapabilityId`、保留重新定义后的 `RuleId`。请执行 [RuleId-only 执行计划](2026-09-08-rule-identity-ruleid-execution.md)，不要按本文的实施步骤修改源码。

状态：设计基线，尚未实施。
日期：2026-09-08
适用范围：`IRuleDefinition`、四个规则阶段、规则图、规则产出、证据、决策、rewrite plan 和相关测试。

## 1. 需求变更记录

用户原文：

> 不保留过度阶段的字段设计给出设计文档md和执行文档md

本设计将其中的“过度阶段”按上下文解释为“过渡阶段”。本次要求覆盖前一份研究记录中提出的兼容方案：不保留 `LegacyRuleId`、不保留旧字段双读单写、不建立旧 ID 映射，也不为过渡期增加 `RuleInstanceId`、`RuleRevision` 或其他兼容字段。

变更级别建议：重大。原因是规则身份会同时影响运行时图、结果模型、rewrite plan、证据键、测试快照和旧工件读取；这是一次破坏性模型收敛，不是局部属性重命名。

本文件和配套执行文件只定义目标和实施顺序，本轮不修改生产代码、测试代码或旧工件。

## 2. 最终决策

规则实现唯一需要声明的规则身份是：

```csharp
string CapabilityId { get; }
```

`IRuleDefinition.RuleId` 删除，四个阶段基类也删除抽象 `RuleId`。所有规则目录、规则结果、决策、证据和新生成的 rewrite plan 统一使用 `CapabilityId`。

最终接口保持最小形状：

```csharp
public interface IRuleDefinition
{
    string CapabilityId { get; }

    RuleInputCardinality InputCardinality { get; }

    RuleConsumesContract Consumes { get; }

    RuleProducesContract Produces { get; }
}
```

这里的 `CapabilityId` 必须被明确定义为“规则定义的稳定规范键”，而不是事实端口名称。事实端口继续由 `RuleSemanticTag` 表达，例如：

```text
规则身份：mark.target.member-access
事实端口：Target.Expression
```

二者不能互相替代。

## 3. 身份层次

本设计只保留一个规则定义身份；执行图的阶段键不是第二个可声明的规则 ID，而是由运行时派生的结构化键。

| 对象 | 目标字段 | 作用 | 是否由规则实现声明 |
| --- | --- | --- | --- |
| 规则定义 | `CapabilityId` | 目录、配置、结果归因和规则分类使用的稳定规范键 | 是 |
| 阶段 | `RuleKind` | `Mark`、`Propagate`、`Lift`、`Propose` 的强类型上下文 | 否，由阶段基类和注册管道提供 |
| 执行图节点 | `RuleNodeId(RuleKind, CapabilityId)` | 图节点、依赖、状态和调度索引 | 否，由图编译器派生 |
| 规则产出 | `CapabilityId` | 标记、传播、提升、决策和 rewrite edit 的规则归因 | 由执行上下文或规则工厂写入 |
| 语义端口 | `RuleSemanticTag` | 描述事实类型和上下游合同 | 是合同字段，不是规则身份 |
| 命中位置 | 现有语法节点、CPG 节点和 `EvidenceAnchor` | 表示在哪里命中 | 否，来自输入和分析结果 |

推荐的图节点模型：

```csharp
public sealed record RuleNodeId(RuleKind Kind, string CapabilityId)
{
    public string Value => $"{Kind}:{CapabilityId}";

    public static RuleNodeId For(RuleKind kind, IRuleDefinition rule) =>
        new(kind, rule.CapabilityId);
}
```

`Value` 仅是日志、快照和展示用的派生文本。内部比较必须使用 `Kind` 和 `CapabilityId` 两个字段，禁止从 `Value` 切割字符串推断阶段或来源。

## 4. `CapabilityId` 不变量

`RuleCatalog` 继续负责目录级验证，并将契约冻结为：

1. `CapabilityId` 不得为空或仅包含空白。
2. 规则目录内所有阶段的 `CapabilityId` 必须全局唯一，比较器为 `StringComparer.Ordinal`。
3. `CapabilityId` 不包含版本号、注册序号、优先级、命中序号、文件位置或当前启用状态。
4. 修改显示名称、实现类名称、注册顺序或规则执行并行度，不得改变 `CapabilityId`。
5. 如果规则语义需要换一个新身份，直接使用新的 `CapabilityId`；不提供旧 ID 自动别名。
6. `CapabilityId` 不能与 `RuleSemanticTag` 复用相同字段来表达事实端口。

当前代码已经满足重要前提：规则目录只校验 `CapabilityId`，且当前规则声明的 `CapabilityId` 是唯一的。现有 `RuleId` 的跨阶段重复不再影响目标模型，因为它不再参与新身份。

## 5. 字段设计

### 5.1 保留字段

这些字段已有明确职责，继续保留：

| 位置 | 字段 | 说明 |
| --- | --- | --- |
| `IRuleDefinition` | `CapabilityId` | 唯一规则规范键 |
| `IRuleDefinition` | `InputCardinality` | 输入消费数量语义 |
| `IRuleDefinition` | `Consumes` | 规则消费的语法/语义端口合同 |
| `IRuleDefinition` | `Produces` | 规则产出的语法/语义端口合同 |
| 四个阶段基类 | `Name` | 人类可读名称，不参与身份比较 |
| 四个阶段基类 | `RequiredCapabilities` | CPG 构建能力需求，不参与身份比较 |
| 四个阶段基类 | 阶段专属允许节点集合和执行方法 | 阶段行为和验证约束 |
| 产出模型 | `SemanticTag`、`Reason`、`SyntaxNode`、图绑定、证据锚点 | 事实内容和命中位置 |

### 5.2 直接删除字段

以下字段、参数和属性不进入目标模型：

```text
IRuleDefinition.RuleId
RuleDefinitionMark.RuleId
RuleDefinitionPropagate.RuleId
RuleDefinitionLift.RuleId
RuleDefinitionPropose.RuleId
MarkRecord.RuleId
PropagatedMarkRecord.RuleId
LiftedMarkRecord.RuleId
DecisionUnit.RuleId
RuleDecision.RuleId
RewritePlanEdit.RuleId
AnalysisEvidenceNode.RuleId
ValidationIssue.RuleId
PropagationFactKey.RuleId
```

它们对应位置的规范字段统一改为 `CapabilityId`；不添加同义的新字符串字段。

### 5.3 明确不增加的字段

本次目标设计不增加以下字段：

```text
LegacyRuleId
PreviousRuleId
RuleInstanceId
RuleRevision
DefinitionFingerprint
RuleVersion
RuleNamespace
RulePriority
RuleSeverity
RuleAlias
```

理由是当前删除规则链路没有多实例注册、规则版本协商、旧 artifact 兼容或独立严重度配置的需求。未来若出现真实需求，应另立设计变更，不以本次字段迁移为借口预埋。

## 6. 运行时数据流

目标数据流如下：

```text
Rule implementation
    │  CapabilityId
    ▼
RuleCatalog
    │  validate global uniqueness
    ▼
RulePipeline
    │  stage supplied by typed collection
    ▼
RuleNodeId(Stage, CapabilityId)
    │
    ├── contract graph
    ├── dependency edges
    ├── execution status
    └── telemetry / evidence status
    ▼
Rule output
    │  CapabilityId + existing syntax/semantic fields
    ▼
mark → propagate → lift → propose → decision → rewrite
```

规则实现可以在构造 mark 或 decision 时使用自身的 `CapabilityId`，但执行引擎必须在边界处校验输出来源与当前节点一致。validator 直接比较当前节点的 `CapabilityId` 与产出记录的 `CapabilityId`，并使用当前节点的 `RuleKind` 判断阶段；不再解析节点字符串。

## 7. 工件和错误策略

这是一次不兼容的工件格式变更：

1. `RewritePlanEdit` 序列化属性从 `ruleId` 变为 `capabilityId`。
2. rewrite plan manifest 的 `SchemaVersion` 从当前版本提升到新版本。
3. 读取到旧版本 manifest 时直接报告“不支持的 rewrite-plan schema”，不得转换、回退或忽略旧 `ruleId` 属性。
4. `RuleDiffCategoryRegistry` 直接以 `CapabilityId` 为 key。
5. evidence、decision 和 validation 输出统一写 `capabilityId`。
6. 不提供旧 artifact 自动迁移命令；需要重新运行分析生成新工件。

拒绝旧工件是有意行为：否则同一个旧 `RuleId` 在不同阶段的含义无法可靠恢复，也会让新旧身份在同一运行中混用。

## 8. 外部项目对本设计的支持

成熟规则和分析项目普遍把稳定 ID 与其他元数据分离：

- [Roslyn Diagnostic IDs](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/choosing-diagnostic-ids)：诊断 ID 必须稳定且唯一，改变 ID 会使已有 suppression 失效；标题、分类、严重度和帮助链接由其他字段表达。
- [ESLint Custom Rules](https://eslint.org/docs/latest/extend/custom-rules)：规则 ID、`meta`、消息 ID、配置 schema 和 fix 能力分开。
- [Semgrep Rule Syntax](https://semgrep.dev/docs/writing-rules/rule-syntax)：`id` 与语言、severity、匹配模式、fix、版本范围和路径范围分开。
- [CodeQL Query Metadata](https://codeql.github.com/docs/writing-codeql-queries/metadata-for-codeql-queries/)：查询 ID、种类、标签、准确率和严重度分别建模。
- [OpenRewrite Recipe](https://github.com/openrewrite/rewrite/blob/main/rewrite-core/src/main/java/org/openrewrite/Recipe.java)：技术 name、display name、instance name、options 和组合关系分开。
- [SonarQube Coding Rules](https://docs.sonarsource.com/sonarqube-server/2025.1/extension-guide/adding-coding-rules/)：规则 Key 与 Name、质量维度、类型、严重度、状态和标签分开。

这些项目支持本次收敛的核心原则，但不意味着要把它们全部的元数据字段复制到 NLISSN。当前只采用稳定规范键和已有执行合同。

## 9. 非目标

本设计不处理：

- 规则程序集扫描或编译期目录生成；
- 新增规则配置 schema；
- 规则优先级或严重度模型；
- 跨版本规则回放；
- 旧 rewrite plan 迁移；
- 规则能力共享或同一 `CapabilityId` 多阶段复用；
- CPG 节点 ID 的独立重构。

如果未来允许多个实现共享同一语义能力，应另行引入“能力端口”和“规则定义键”的区分，而不是放松本设计的全局唯一约束。

## 10. 验收不变量

- `IRuleDefinition` 中不存在 `RuleId`。
- `src/` 和新工件模型中不存在 `LegacyRuleId`、`PreviousRuleId` 或兼容别名。
- 规则目录只使用 `CapabilityId` 验证唯一性。
- `RuleNodeId` 的内部相等性由 `RuleKind + CapabilityId` 决定，不依赖字符串切割。
- mark、propagation、lift、decision、evidence、rewrite plan 和 diff category 使用 `CapabilityId`。
- 旧 rewrite plan schema 被拒绝，而不是被自动转换。
- DOP 1 和较高 DOP 的 graph、evidence、decision、rewrite 输出中的 `CapabilityId` 一致。
- 相同规则在重命名实现类、重排注册顺序后仍拥有相同 `CapabilityId`。
- 全仓库生产代码、测试和当前设计文档不再把 `RuleId` 当作规范身份。

## 11. 配套执行文件

实施顺序、具体文件、红灯测试、工件 schema 变更和验证命令见：

[2026-09-08-rule-identity-capabilityid-execution.md](2026-09-08-rule-identity-capabilityid-execution.md)
