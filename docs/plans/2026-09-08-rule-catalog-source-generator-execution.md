# 规则目录编译期生成执行计划

> 状态：已收口；当前 generated catalog 无 feature 维度，四阶段完整。
>
> 目标设计：[规则目录：编译期生成](../CodeDesign/目前设计/规则目录-编译期生成.md)。
>
> 研究依据：[编译期规则目录研究报告](../research/2026-09-08-rule-catalog-source-generator-study.md)。

## 1. 目标和边界

把 `NLISSN.Rules` 中具体 `IRuleDefinition` 实现的手工组合迁移为编译期生成目录，并保持：

- Mark、Propagate、Lift、Propose 四阶段成员和顺序；当前目录整体四阶段完整；
- `RuleId`、规则图节点和规则图边；
- active / disabled 规则声明；
- required CPG capabilities；
- decision、evidence、rewrite source、per-file diff 和 DOP 结果。

本计划不实现：

- 运行时反射扫描、插件发现或引用程序集自动合并；
- 规则 DAG 生成器；
- 依赖注入式规则构造；
- `CapabilityId`、`DefaultEnabled` 或新的规则身份字段；
- feature 依赖表或隐式依赖计算；
- 修改 `IRuleDefinition` 的执行语义。

当前源码已切换到生成目录和 `RulePipelineComposer`；`RuleRegistry` 手工路径已删除。四个延期规则族均不进入当前 generated catalog，Composer 不保留 legacy 兼容路径。

## 2. 执行前置

每次开始或切换阶段前运行：

```powershell
pwsh -File .\Miscellaneous\init.ps1
```

开始前保存当前工作树状态，不覆盖用户已有变更。读取：

```text
AGENTS.md
Context/progress.md
Context/feature_list.json
docs/harness-runtime.md
```

本计划涉及的目标项目：

```text
src/NLISSN.Rule/                  契约源码目录，链接编译进 NLISSN.Core
src/NLISSN.Rule.Generator/        新增 generator 项目
src/NLISSN.Rules/                 具体规则和 generated catalog
src/NLISSN/                       RulePipelineComposer
tests/NLISSN.ContractTests/       GeneratorDriver 和目录契约测试
tests/NLISSN.HostTests/           运行时组合和行为等价测试
```

## 3. 阶段 0：冻结手工基线

### 工作

1. 从当前 `RuleRegistry` 和四个阶段基类生成具体规则清单。
2. 为每条规则记录：阶段、完全限定类型名、`RuleId`、构造可见性。
3. 记录当前目录四阶段类型快照。
4. 检查当前目录整体的阶段集合，并将其与生成目录快照绑定。当前目录必须为 `Mark + Propagate + Lift + Propose`。`UnreachableMethodDeletion`、`UnreferencedMethodDeletion`、`UnusedInterfaceImplementationCleanup` 和 `InternalOnlyPublicMethodPrivatization` 均排除。

缺失阶段是 generator error；不得用 no-op 的 Propagate/Lift 规则填充目录。

如果实际阶段集合与已冻结快照不一致，先修复规则标注或手工目录，停止 generator 接入；不得通过 no-op 规则或隐式空 descriptor 掩盖差异。

### 测试

新增或扩展 Host/Contract 测试，证明：

- 生成目录没有漏掉具体规则；
- 当前目录快照可重复生成；
- 四个延期规则族不出现在 generated 四阶段集合，也没有启用后的 legacy 组合路径；
- 四阶段顺序、`RuleId` 唯一性和 disabled 列表稳定。

### 检查点

只有基线快照和完整性测试通过，才进入阶段 1。当前 generated 快照基线为 `19/12/5/32`、`68` 个 factory。

## 4. 阶段 1：增加目录契约

### 修改范围

在 `src/NLISSN.Rule/` 增加或调整：

```text
RuleRegistrationAttribute
RuleCatalogIgnoreAttribute
RuleRegistration<TStage>
```

约束：

- `RuleRegistrationAttribute` 是无参数 marker；
- 不增加 `DefaultEnabled`；
- 不在属性中重复声明 `RuleId`；
- `RuleRegistration<TStage>` 保存 `RuleId`、简单类型名、完全限定名和 factory；
- 不加入 Roslyn symbol 类型或 generator runtime 类型；
- 继续使用当前迁移完成的 `RuleId`，不恢复 `CapabilityId`。

### 测试

添加契约测试，验证：

- 属性只有无参数 public constructor，且不能重复；
- descriptor 四阶段类型约束成立；
- generated catalog 的公开契约可被 `NLISSN` 引用；
- 当前目录整体有四个阶段 descriptor；缺失阶段由 `NLRCG011` 报错。

## 5. 阶段 2：建立并接入 generator

### 项目

新增：

```text
src/NLISSN.Rule.Generator/NLISSN.Rule.Generator.csproj
```

项目约束：

```text
TargetFramework: netstandard2.0
Microsoft.CodeAnalysis.CSharp: 4.14.0
Roslyn 包：PrivateAssets=all
```

generator 不引用 `NLISSN.Core` 或 `NLISSN.Rules`。使用目标 compilation 的 Roslyn symbols 和稳定 metadata name 识别：

```text
IRuleDefinition
RuleDefinitionMark
RuleDefinitionPropagate
RuleDefinitionLift
RuleDefinitionPropose
RuleRegistrationAttribute
RuleCatalogIgnoreAttribute
```

### MSBuild 接线

在 `src/NLISSN.Rules/NLISSN.Rules.csproj` 增加 Analyzer-only ProjectReference：

```xml
<ProjectReference
    Include="..\NLISSN.Rule.Generator\NLISSN.Rule.Generator.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

不要把 generator 作为运行时程序集引用。

### 生成文件

启用 compiler-generated files 输出，但将路径固定在 `Build` 下：

```text
Build/src/
Build/src/obj/NLISSN.Rules/generated/
```

不在 `src/` 写入 `.g.cs`、`bin` 或 `obj`。

## 6. 阶段 3：实现发现、诊断和目录发射

### 发现规则

只扫描当前 `NLISSN.Rules` compilation 中直接或间接继承四个阶段基类的具体类：

- 抽象类、开放泛型和非阶段类不生成 factory；
- 规则类型可以是 `public` 或 `internal`；
- 无参数构造函数必须能被同一 compilation 的生成代码访问；
- 不扫描引用程序集。

### 元数据处理

- 有 `RuleRegistration`：加入当前目录；
- 无 `RuleRegistration`：warning，并忽略该规则；
- 有 `RuleCatalogIgnore`：明确忽略，不产生缺少注册 warning；
- `RuleId` 从规则实现的可编译期属性值读取；
- 目录项使用 `static () => new ConcreteRule()` factory。

### 诊断

编译期 error 包括：

- 规则契约类型缺失；
- 无法确定四阶段；
- 注册元数据非法；
- `RuleId` 缺失、为空、无法编译期解析或重复；
- 类型/构造函数不可访问；
- 固定类型名 `NLISSN.Rules.GeneratedRuleCatalog` 冲突；
- 当前目录缺少 Mark、Propagate、Lift 或 Propose 中任一阶段；
- 目录不能完整生成。

出现 error 时不生成部分 catalog。

### 目录输出

生成：

```text
NLISSN.Rules.GeneratedRuleCatalog
    Markers
    Propagators
    Lifters
    Proposers
```

四个属性类型分别是：

```text
IReadOnlyList<RuleRegistration<RuleDefinitionMark>>
IReadOnlyList<RuleRegistration<RuleDefinitionPropagate>>
IReadOnlyList<RuleRegistration<RuleDefinitionLift>>
IReadOnlyList<RuleRegistration<RuleDefinitionPropose>>
```

生成代码内部用只读包装；目录不缓存规则实例。

### GeneratorDriver 测试

在 `tests/NLISSN.ContractTests/` 增加 generator 测试，至少覆盖：

- 四阶段合法规则；
- 未注册规则 warning 和排除；
- `RuleCatalogIgnore`；
- 非法阶段、非法注册元数据、抽象类、开放泛型；
- 无可访问无参数构造函数；
- 重复 `RuleId`；
- 固定生成类型冲突；
- error 时无部分 catalog；
- 当前目录缺少任一阶段时的 `NLRCG011` 和原子失败；
- 四列表和完全限定名排序稳定。

## 7. 阶段 4：迁移规则元数据和运行时组合

### 规则标注

按照阶段 0 的基线为当前目录的所有具体规则添加一个无参数 `RuleRegistration`：

```text
当前目录的现有规则
    -> 当前 generated catalog
```

四个延期规则族不登记、不生成 descriptor/factory/规则图节点，也不保留 legacy descriptors。未来若重新纳入，必须重新定义真实四阶段输入、输出、规则图节点和行为等价基线，不能通过 generator 添加 no-op。

### 组合入口

删除 `RuleRegistry`，新增唯一入口：

```csharp
RulePipelineComposer.Compose(RuleSelection selection)
```

删除 `CreateDefaultRules` 及其四个延期规则 bool 参数，不保留旧名称 facade。

`Compose`：

1. 直接消费四个 generated catalog 列表，不进行 feature 过滤；
2. 先按 `TypeName` ordinal、再按完全限定名 ordinal 排序；
3. 按禁用 `RuleId` 分 active / disabled；
4. active 和 disabled 都调用 factory；
5. 保留 disabled 规则到 `RulePipeline`，供规则图声明使用；
6. 调用现有 `RuleCatalog` 做 RuleId 非空和 Ordinal 全局唯一校验；
7. 返回 `RulePipeline` 和结构化规则选择 warning。

规则 factory 每次组合创建新实例。任一选中 factory 抛出异常，组合失败，不返回部分管道。

未知禁用 `RuleId` 按大小写不敏感去重，生成一个稳定排序 warning 后忽略。

### 配置适配

现有 YAML 的可读能力开关可以继续存在，但只能在上层转换为 `RuleSelection`。不得把四个 bool 重新加入 `RulePipelineComposer`。

## 8. 阶段 5：行为等价和收口

### 定向验证

只运行本次收口所需的核心过滤测试：

```powershell
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RuleCatalog|FullyQualifiedName~RuleIdentityContractTests"
dotnet test .\tests\NLISSN.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false --filter "FullyQualifiedName~RulePipelineComposerTests|FullyQualifiedName~RuleSelectionAdapterTests|FullyQualifiedName~GeneratedRuleCatalogEquivalenceTests|FullyQualifiedName~MethodDeletionFeatureScopeTests"
```

不运行无 filter 的 Unit/Contract/Host 全量测试、Fast/Host tier 或 Performance tier。

文档改动和工作树收口检查：

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

实际运行前按当前环境决定是否需要串行 MSBuild 参数；不得把未执行的命令写成通过证据。

### 等价验收

当前目录与冻结基线比较：

- 四阶段规则类型、`RuleId` 和顺序；
- active / disabled 目录和图声明；
- RuleId 校验结果；
- required CPG capabilities；
- compiled rule graph 节点和边；
- decision、evidence、rewrite source、per-file diff；
- factory 生命周期和 disabled 图声明。

所有核心过滤测试通过后，将本页与当前设计页中的状态更新为实际验收状态；手工目录已删除这一事实必须由生产边界测试持续守护。

## 9. 回滚

在等价证据完成前保留可回滚的手工组合路径。若 generator error、规则集合差异、图差异、输出差异或 DOP 不稳定：

1. 停止删除手工目录；
2. 保存 generator diagnostics 和快照；
3. 恢复 `RulePipelineComposer` 接入前的组合路径；
4. 不使用运行时反射补齐缺失规则；
5. 重新定位差异后再推进迁移。

## 10. Definition of Done

- [ ] 阶段 0 的生成目录清单和实际阶段集合通过；四个延期规则族没有 legacy 范围。
- [ ] `NLISSN.Rule` 契约、generator 项目和 Analyzer-only 接线完成。
- [ ] generator 只扫描当前 `NLISSN.Rules` compilation，并生成四个强类型只读列表。
- [ ] 缺少注册只产生 warning 并忽略；generator error 不生成部分目录。
- [ ] `RulePipelineComposer.Compose` 是唯一组合入口，不再有 feature 过滤、旧 `RuleRegistry` 和四个 bool 入口。
- [ ] active / disabled、RuleId 校验、排序、规则图和 factory 生命周期保持契约。
- [ ] GeneratorDriver、真实 MSBuild 集成和少量 Host/Contract 核心过滤验证通过。
- [ ] generated source 和编译产物只位于 `Build`，文档、状态和回滚证据已更新。
