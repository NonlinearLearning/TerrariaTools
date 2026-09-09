# PRD：IRuleDefinition 编译期规则目录生成

## 1. 概述

当前 `NLISSN` 通过手写 `RuleRegistry` 和多个 `new XxxRule()` 列表维护四个规则阶段。新增规则时，规则实现、手工目录、feature 选择和运行时组合容易出现遗漏或顺序漂移。

本功能使用 Roslyn incremental source generator，在编译 `NLISSN.Rules` 时发现已注册的具体 `IRuleDefinition` 实现，并生成固定的四阶段规则目录。运行时只从生成目录中按 `RuleSelection` 选择规则，由唯一的 `RulePipelineComposer.Compose(...)` 创建本次分析所需的规则实例。

本 PRD 只覆盖规则目录的编译期生成和手工组合迁移；当前运行时规则的分析语义保持不变。

相关文档：

- [当前设计：规则目录编译期生成](../设计docs/目前设计/规则目录-编译期生成.md)
- [执行计划：规则目录编译期生成](../docs/plans/2026-09-08-rule-catalog-source-generator-execution.md)

## 2. 目标

- 新增具体规则时，只需实现正确的四阶段基类并声明一个 `RuleFeature`，不再手动维护规则实例列表。
- 在编译期生成可检查、可排序、可测试的四阶段规则目录。
- 保持现有 `RuleId`、规则阶段、feature 选择、active/disabled 声明和规则图输出的行为等价。
- 让缺少注册的规则得到明确 warning 并被忽略，让目录完整性和身份冲突在编译期失败。
- 将生成器、生成源码、编译产物限制在约定的 `Build` 输出范围内。
- 删除 `RuleRegistry`、`CreateDefaultRules` 及其四个 feature `bool` 入口，使 `RulePipelineComposer.Compose(RuleSelection)` 成为唯一运行时组合入口。

## 3. 用户故事

### US-001：冻结手工目录基线

**描述：** 作为维护者，我希望在迁移前得到手工规则目录的完整快照，以便证明生成目录没有遗漏、重复或顺序变化。

**验收标准：**

- [ ] 清单记录每个具体规则的阶段、完全限定类型名、`RuleId`、feature 和无参数构造函数可见性。
- [ ] 默认 Core 和全部 feature 都有可重复生成的四阶段类型快照。
- [ ] 每个 `RuleFeature` 都有 Mark、Propagate、Lift、Propose 四个阶段的完整成员集合。
- [ ] 基线测试验证规则阶段顺序、`RuleId` 唯一性和 active/disabled 列表稳定。

### US-002：生成四阶段规则目录

**描述：** 作为规则开发者，我希望编译器自动发现已注册的具体规则并生成目录，以便新增规则不再依赖手工注册列表。

**验收标准：**

- [ ] 新增独立的 `netstandard2.0` Roslyn incremental generator，并由 `NLISSN.Rules` 以 Analyzer-only 方式引用。
- [ ] 生成器只扫描当前 `NLISSN.Rules` compilation，不扫描引用程序集，不使用运行时反射。
- [ ] 生成固定类型 `NLISSN.Rules.GeneratedRuleCatalog`，并提供强类型的 `Markers`、`Propagators`、`Lifters`、`Proposers` 四个只读列表。
- [ ] 每个目录项包含 `RuleId`、简单类型名、完全限定名、`RuleFeature` 和可创建新实例的 factory。
- [ ] 目录项按类型名使用 `StringComparer.Ordinal` 排序；同名时按完全限定名 ordinal 排序。
- [ ] 生成源码、DLL、PDB、中间文件只写入 `Build`，不在 `src` 下写入 `.g.cs`、`bin` 或 `obj`。

### US-003：校验注册和生成器诊断

**描述：** 作为规则维护者，我希望错误的规则声明在编译期被指出，以便目录不会静默缺项或生成不确定结果。

**验收标准：**

- [ ] 具体规则通过 `RuleRegistration(RuleFeature.X)` 显式声明 feature；注册属性不重复声明 `RuleId` 或默认状态。
- [ ] 缺少注册的具体阶段规则报告 warning，并忽略该规则；不生成对应 factory 或目录项。
- [ ] `RuleCatalogIgnore` 可以表达有意排除，且不会产生缺少注册 warning。
- [ ] 缺少或无法编译期读取 `RuleId`、空 `RuleId`、重复 `RuleId`、非法 feature、不可访问无参构造函数、抽象类、开放泛型和固定生成类型冲突均报告 error。
- [ ] 出现 generator error 时不生成部分 `GeneratedRuleCatalog`。
- [ ] `CSharpGeneratorDriver` 测试覆盖上述 warning、error、原子失败和稳定排序行为。

### US-004：使用生成目录组装运行时管道

**描述：** 作为分析宿主，我希望通过一个选择对象组装规则管道，以便 feature 配置和规则禁用策略不再耦合到手工注册实现。

**验收标准：**

- [ ] `RulePipelineComposer.Compose(RuleSelection selection)` 是唯一规则组合入口。
- [ ] `RuleFeature.Core` 自动启用；其它 feature 根据 `RequestedFeatures` 选择。
- [ ] 选中 feature 的规则按目录顺序拆分为 active 和 disabled；两者都通过 factory 创建，未选中 feature 的规则不创建实例。
- [ ] disabled 规则继续进入 `RulePipeline` 和规则图声明，但 `IsEnabled` 为 `false`。
- [ ] 每次 `Compose` 创建新的规则实例，不缓存 singleton；任一选中 factory 创建失败时不返回部分管道。
- [ ] 未知 disabled `RuleId` 忽略并产生结构化、稳定排序的 warning；已知但属于未启用 feature 的 `RuleId` 不产生该 warning。
- [ ] 删除 `RuleRegistry`、`CreateDefaultRules`、四个 feature `bool` 参数和旧名称兼容入口。

### US-005：证明迁移前后行为等价

**描述：** 作为项目维护者，我希望用自动化证据比较手工目录和生成目录，以便安全地切换运行时实现。

**验收标准：**

- [ ] 真实 `NLISSN.Rules` MSBuild 集成测试证明 Analyzer 接线、生成目录、factory 和 `NLISSN` 消费路径有效。
- [ ] 默认配置和全部 feature 配置的四阶段类型、`RuleId`、顺序以及 active/disabled 结果一致。
- [ ] `RuleCatalog` 校验、规则图节点和边、required CPG capabilities、decision、evidence、rewrite source、per-file diff 一致。
- [ ] DOP 1、2、16 的结果保持稳定。
- [ ] 只有等价证据通过后，才删除手工四阶段数组，并更新设计文档、执行状态和 feature 交接信息。

## 4. 功能需求

- **FR-1：规则契约。** 系统必须提供 `RuleFeature`、`RuleRegistrationAttribute`、`RuleCatalogIgnoreAttribute` 和 `RuleRegistration<TStage>` 契约；注册属性只接收一个 feature。
- **FR-2：阶段识别。** 系统必须依据规则类型直接或间接继承的 `RuleDefinitionMark`、`RuleDefinitionPropagate`、`RuleDefinitionLift` 或 `RuleDefinitionPropose` 判断规则阶段，不得依据目录、命名空间或文件名猜测。
- **FR-3：编译期发现。** generator 必须只检查当前 `NLISSN.Rules` compilation 的具体规则类型，不得通过运行时反射或扫描引用程序集补齐目录。
- **FR-4：显式注册。** 每个被纳入目录的具体规则必须具有一个 `RuleRegistration(RuleFeature.X)`；同一规则不得重复注册。
- **FR-5：构造边界。** 被纳入目录的规则必须具有生成代码可访问的无参数构造函数；目录 factory 必须在每次组合时创建新的规则实例。
- **FR-6：身份提取。** generator 必须从规则实现的 `RuleId` 属性读取可编译期解析的字符串，并在目录生成前检查非空和全局 Ordinal 唯一性。
- **FR-7：生成类型。** generator 必须生成固定全限定名 `NLISSN.Rules.GeneratedRuleCatalog`，并输出四个按阶段强类型化的只读列表。
- **FR-8：排序。** 生成目录和运行时组合必须使用明确的 ordinal 排序，确保构建和分析结果具有确定性。
- **FR-9：诊断策略。** 缺少注册只产生 warning 并忽略该规则；身份、契约、访问性、类型形状和固定名称冲突等目录完整性问题必须产生 error，且 error 时不得输出部分目录。
- **FR-10：运行时选择。** `Compose` 必须自动启用 Core，按 `RuleSelection` 过滤其它 feature，保留 disabled 规则声明，并为未知 disabled `RuleId` 返回结构化 warning。
- **FR-11：编译输出。** 所有编译产物和 generated source 必须落在 `D:\ProjectItem\SourceCode\Net\NL\Build` 下，不能污染 `src` 源码目录。
- **FR-12：回归验证。** 实现必须同时提供 generator driver 测试、真实 MSBuild 集成测试、运行时组合测试和手工目录行为等价测试。

## 5. 非目标

- 不实现运行时反射扫描、插件自动发现或引用程序集规则合并。
- 不生成规则 DAG，不改变现有规则执行语义、规则图语义或 CPG worker 约束。
- 不引入依赖注入容器、singleton 规则生命周期或新的规则构造配置系统。
- 不恢复 `CapabilityId`，不新增与 `RuleId` 重复的身份字段，也不在注册属性中重复声明 `RuleId`。
- 不引入 `DefaultEnabled`、feature 依赖表或隐式依赖计算。
- 不把 feature 的可读配置开关直接塞回 `RulePipelineComposer` 的四个 bool 参数；上层配置只负责转换成 `RuleSelection`。
- 不在行为等价证据完成前删除手工目录，不用运行时 fallback 掩盖 generator 目录缺失。

## 6. 技术约束和实现注意事项

- `src/NLISSN.Rule/` 是契约源码目录，当前通过 linked `Compile` 编译进 `NLISSN.Core`；generator 不引用 `NLISSN.Core` 或 `NLISSN.Rules`。
- `src/NLISSN.Rule.Generator/` 目标框架为 `netstandard2.0`，Roslyn 包使用 `Microsoft.CodeAnalysis.CSharp 4.14.0`，并以 `PrivateAssets=all` 控制依赖边界。
- `NLISSN.Rules.csproj` 使用 `OutputItemType="Analyzer"` 和 `ReferenceOutputAssembly="false"` 引用 generator。
- 生成目录公开 `IReadOnlyList<RuleRegistration<TStage>>`，不缓存实例；生成代码可访问 `public` 或 `internal` 规则，只要类型和无参构造函数对生成代码可访问。
- 具体规则属于一个且仅一个 `RuleFeature`；每个 feature 必须有四个完整阶段。
- 生成器和运行时排序必须与基线快照保持一致；例如完全限定名可以是 `NLISSN.Rules.Experimental.AtomicRule`。
- 当前仓库使用 .NET SDK `10.0.200-preview.0.26103.119` 和 `net10.0`；实现和验证遵循仓库 harness 顺序。

## 7. 成功指标

- 新增或迁移规则不再要求修改手写 `RuleRegistry` 或四个阶段实例列表。
- 合法规则能够在编译期出现在正确阶段的 generated catalog 中，并能由 `RulePipelineComposer` 创建。
- 缺少注册、非法身份和构造边界问题能够在编译期以可定位诊断暴露；error 不会留下可误用的部分目录。
- 默认和全部 feature 的生成目录与手工基线在类型、`RuleId`、顺序、active/disabled、规则图和分析输出上等价。
- DOP 1、2、16 的等价测试通过，且生成器不会把中间产物写入 `src`。
- 文档、feature 状态和执行交接能够明确区分“设计完成”和“实现完成”。

## 8. 未决问题

以下不阻塞设计或 PRD，可在实现阶段确定：

- generator diagnostics 的最终编号、标题和精确消息文本。
- 生成器测试中使用的最小 Roslyn compilation fixture 目录和测试辅助类位置。
- 真实 MSBuild 集成测试如何读取 generated source 路径，以及各测试项目最终采用的串行构建参数。

