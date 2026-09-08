# IRuleDefinition 编译期规则目录研究报告

> 状态：研究完成，未修改生产代码。
>
> 日期：2026-09-08。
>
> 研究问题：是否可以使用编译期生成的规则目录，替换 `RuleRegistry` 中逐条手写的 `new XxxRule()`？

## 结论摘要

可以，而且这是当前仓库比运行时反射更合适的自动注册方向。但“编译期生成目录”不等于“扫描到所有 `IRuleDefinition` 后无条件启用”。外部项目的共同经验是：

1. 发现范围必须有编译期锚点：显式属性、生成入口、模块或目标程序集，而不是依赖运行时程序集扫描。
2. 生成器应生成普通 C# 工厂/目录代码，运行时只执行生成代码，不反射、不猜构造函数、不依赖类型返回顺序。
3. 规则发现和规则启用必须分开。当前 NLISSN 的 feature 开关、禁用规则和四阶段管道语义不能由“发现到类型”自动推断。
4. 配置错误必须在编译期或测试期 fail closed：未标注实现类、无法构造、阶段不合法、规则族不完整、重复身份都不能静默跳过。
5. 生成器应保持一个明确的 composition root。对于 NLISSN，这个根仍然是 `RuleRegistry`；生成器只替换规则全集目录的维护方式，不把策略过滤和 DAG 编译搬进生成器。

因此推荐的目标形态是：

```text
NLISSN.Rules 中的具体规则类
    + [RuleRegistration(Feature)]
    ↓  Roslyn incremental source generator
GeneratedRuleCatalog.g.cs
    ↓  生成的静态 factory/descriptor
RuleRegistry
    ↓  feature 过滤、disabled 过滤、规则校验、四阶段组装
RulePipeline
    ↓
规则图编译和执行
```

本报告只形成设计输入，不引入生成器项目、属性、规则注解或生产行为变更。

## 研究范围与方法

本次只采用项目自己的 README、官方仓库源码和 Microsoft 官方 Roslyn 文档作为一手材料。没有把博客转述、第三方教程或搜索摘要当作实现证据。GitHub 匿名 API 和搜索页在研究过程中触发了限流，因此项目选择和实现判断以已读取的固定官方 URL 为准；本报告不声称完成全网项目排名，也不记录未经核验的 star 数。

研究关注以下问题：

- 项目如何确定“哪些类型需要生成”；
- 生成器如何取得语义信息并生成代码；
- 注册表、工厂、模块和 feature 是否在编译期明确表达；
- 构造依赖、重复注册、缺失实现和非法配置如何失败；
- 运行时是否还需要反射或扫描；
- 这些模式能否映射到 NLISSN 的 Mark / Propagate / Lift / Propose 四阶段。

## 一手来源矩阵

| 项目或规范 | 读取的官方材料 | 直接证据 | 对 NLISSN 的可复用结论 |
|---|---|---|---|
| Microsoft Roslyn | [Source Generators overview](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)、[`IIncrementalGenerator`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator)、[`RegisterSourceOutput`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.incrementalgeneratorinitializationcontext.registersourceoutput)、[`ForAttributeWithMetadataName`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxvalueprovider.forattributewithmetadataname) | 增量生成器通过 Roslyn 输入建立可缓存的转换，并把结果注册为生成源码或诊断 | 生成器应处理符号/属性输入，生成普通源码，并把诊断报告到编译器；不要在生成阶段调用 NLISSN 运行时管道 |
| Mediator | [README](https://github.com/martinothamar/Mediator/blob/main/README.md)、[`IncrementalMediatorGenerator.cs`](https://raw.githubusercontent.com/martinothamar/Mediator/main/src/Mediator.SourceGenerator/IncrementalMediatorGenerator.cs) | README 明确目标包括生成 DI 注册、Native AOT、构建期诊断；源码把 `CompilationProvider` 与 `SyntaxProvider` 合并，先构造模型和诊断，再生成选项和实现 | 一个增量生成器可以同时生成目录/工厂和诊断；NLISSN 应把“规则模型验证”和“源码生成”分成两个输出步骤 |
| StrongInject | [README](https://raw.githubusercontent.com/YairHalberstadt/stronginject/main/README.md) | 使用 `IContainer<T>` 作为明确组合根，使用 `[Register]`/`[RegisterModule]` 声明依赖；缺注册时编译失败；README 明确无反射和运行时代码生成 | 不要把所有可实例化类型都当作默认服务；需要显式的规则注册边界和模块/feature 语义 |
| Jab | [README](https://raw.githubusercontent.com/pakrym/jab/main/README.md) | 使用 `[ServiceProvider]` 作为生成入口，支持 module、root service 和多种注册形式；README 明确注册配置错误转化为编译错误，不采用运行时 `IServiceCollection` 注册语法 | NLISSN 应保留单一 composition root；规则 feature 可以看作生成目录的模块，但策略过滤仍由 `RuleRegistry` 掌握 |
| MessagePack-CSharp | [`CompositeResolverGenerator.cs`](https://raw.githubusercontent.com/MessagePack-CSharp/MessagePack-CSharp/master/src/MessagePack.SourceGenerator/CompositeResolverGenerator.cs) | 通过 `ForAttributeWithMetadataName` 找到属性目标，把目标符号和 `Compilation` 合并，生成 resolver/formatter 实例表达式，并使用集合去重 | 使用属性锚点和 `INamedTypeSymbol` 生成 `new ConcreteRule()` 工厂；可在生成阶段检查可访问性和重复项 |
| Foundatio.Mediator | [README](https://raw.githubusercontent.com/FoundatioFx/Foundatio.Mediator/main/README.md) | README 宣称按命名约定在编译期发现 handler、生成拦截/API 入口、零运行时反射和编译期诊断 | 约定式零注册可行，但它依赖 handler 命名、消息形状和拦截器契约；NLISSN 的规则 feature/阶段语义更严格，不应仅按类名推断启用策略 |

以上项目的实现版本以 2026-09-08 读取到的 `main` 分支或仓库默认分支为准。链接指向官方仓库；版本漂移时应重新读取源码，不应把本报告当成永久 API 兼容承诺。

## 外部项目的关键做法

### 1. Mediator：增量模型、诊断和生成分离

`IncrementalMediatorGenerator` 的关键结构是：

```text
CompilationProvider
    + SyntaxProvider（找到 AddMediator 调用）
    ↓ Combine / Collect
Parse -> CompilationModel + Diagnostics
    ├─ RegisterSourceOutput(Diagnostics)
    └─ RegisterSourceOutput(Model)
       ├─ 生成选项
       └─ 生成 mediator 实现
```

源码中的 `Initialize` 使用 `context.CompilationProvider`、`CreateSyntaxProvider`、`Combine` 和 `Collect` 建立输入；之后 `Parse` 返回模型和诊断，两个 `RegisterSourceOutput` 分别处理诊断和代码。README 还明确把生成 DI 注册、AOT 友好性和构建期错误列为目标。

对 NLISSN 的启发不是复制 `AddMediator`，而是复制边界：

- 规则符号收集、元数据解析、构造能力分析属于生成器；
- 规则 feature 过滤、禁用列表和 `RulePipeline` 组装仍属于 `RuleRegistry`；
- 生成器必须把非法规则变成诊断，而不是把类型静默忽略。

### 2. StrongInject：显式组合根优先于环境扫描

StrongInject 要求使用者声明 `IContainer<T>`，并通过 `[Register]` 和 `[RegisterModule]` 表达可达服务。README 给出的行为是：

- 未注册依赖在编译期报错；
- 生成代码用于最终解析；
- 不使用反射或运行时代码生成；
- 模块可以复用一组注册，但冲突注册必须诊断或显式解决。

这直接反驳了一个危险的简化方案：把所有实现 `IRuleDefinition` 的类都当作默认启用规则。StrongInject 的“自动”是生成过程自动化，不是把业务策略隐式化。NLISSN 也需要把“规则属于哪个 feature”作为声明式事实，而不是从名字猜测。

### 3. Jab：生成的容器仍然有明确的入口和 root services

Jab 使用 `[ServiceProvider]` 生成容器，并把 module、root service、生命周期和命名注册纳入编译期模型。README 明确区分了它的编译期注册与运行时 `IServiceCollection` 语法，并把配置错误转为编译错误。

对应到 NLISSN：

- `RuleRegistry` 是策略入口，不应由生成器取代；
- 生成器可以产生“规则全集”，但不能自行决定当前 CLI 配置启用哪些 feature；
- 若将来规则不再是无参构造，必须把依赖工厂或 rule services 纳入显式契约，不能在生成器中偷偷使用反射构造。

### 4. MessagePack：属性锚点、符号模型和生成表达式

`CompositeResolverGenerator` 的实现提供了一个与本任务最接近的 Roslyn 结构：

- 用 `ForAttributeWithMetadataName` 找到声明了目标属性的类型；
- 将属性数据转换为 `INamedTypeSymbol` 和创建表达式；
- 将目标数据和 `Compilation` 结合，做可访问性检查；
- 使用 `RegisterSourceOutput` 生成 resolver 源码；
- 在合并本地 formatter/resolver 时使用集合去重。

NLISSN 可以采用相同的符号路径，但把目标换成：

```text
[RuleRegistration(feature)] 的具体规则类型
    -> 阶段基类判断
    -> 无参构造能力检查
    -> 全限定类型名和简单类型名冲突检查
    -> 生成静态 factory descriptor
```

### 5. Foundatio.Mediator：约定式零注册的边界

Foundatio.Mediator 的 README 宣称 handler 可以不实现接口或基类，生成器按命名约定发现 handler，并通过 source generator/interceptor 消除运行时反射。这说明约定式发现可以显著减少手写注册。

但它的可行性来自一组专门的约定：消息类型、`Handle` 命名、端点推断、拦截器和生成 API。NLISSN 规则不是简单的 handler：

- 四个阶段是类型边界；
- feature 开关决定默认是否进入管道；
- `RuleId`、`CapabilityId`、`Consumes`/`Produces` 共同影响规则图；
- disabled 节点仍参与图声明；
- 规则顺序和 DOP 输出需要稳定。

因此可以借鉴“生成期零手写注册”，不能借鉴“只按命名约定自动启用”。

## 当前 NLISSN 的事实约束

### 手动目录的位置和语义

[`RuleRegistry.CreateDefaultRules`](../../src/NLISSN/Composition/RuleRegistry.cs) 当前按四个阶段显式构造规则，并按四组 feature 开关追加可选规则。之后它调用 `RuleCatalog.ValidateRules`，再按 `disabledRuleTypes` 过滤并构造 `RulePipeline`。

[`IRuleDefinition`](../../src/NLISSN.Rule/IRuleDefinition.cs) 当前只包含 `CapabilityId`、`RuleId`、`InputCardinality`、`Consumes` 和 `Produces`。这些是执行契约，不足以表达“阶段归属、默认 feature、生成策略”。四个阶段基类可以表达阶段，但不能表达 feature。

[`RulePipeline`](../../src/NLISSN.Application/Analysis/RulePipeline.cs) 接收四个具体阶段列表，并负责把规则声明转换为 contract graph 和执行图。它不负责程序集发现，也不应承担生成器职责。

### 不能破坏的行为

生成目录替换手工数组后，以下行为必须保持：

- 默认配置仍只启用当前核心规则；
- 四个可选 feature 默认关闭；
- `disabledRuleTypes` 的大小写不敏感类型名过滤保持不变；
- 被禁用规则仍能以 disabled declaration 进入图；
- `RuleId`、`CapabilityId` 和 `RuleNodeId` 不变；
- `RuleCatalog.ValidateRules` 仍在 feature 过滤后的候选集合上运行；
- `CreateRules` 的稳定排序语义不变；
- DOP 1、2、16 的 stage snapshot、evidence、decision、rewrite 和 diff bytes 不变。

### 为什么不能依赖规则图发现遗漏

当前 contract graph 对 `RuleInputCardinality.All` 允许没有 producer。producer 漏注册时，规则图不一定启动失败，可能只是下游收不到事实。因此“生成器收集所有实现类并生成目录”还必须配合生成期遗漏诊断或完整性 contract，不能把图编译当成注册表完整性检查。

## 推荐的 NLISSN 方案

### 方案选择

| 方案 | 结论 | 原因 |
|---|---|---|
| 运行时反射扫描 `IRuleDefinition` | 不采用 | 构造失败推迟到启动；AOT/裁剪和程序集边界更复杂；feature 仍无法推断 |
| 测试期反射完整性校验 | 作为迁移前置和长期回归 | 成本低，能先把漏注册从静默失效变成测试失败，但仍保留手写 `new` |
| 编译期生成规则目录 | 采用 | 去掉重复维护，保留静态构造、确定性和编译期诊断 |
| 命名约定式全自动启用 | 不采用 | 无法安全表达 feature、规则族和策略边界 |

### 生成器的责任

建议新增独立 analyzer/source-generator 项目，例如：

```text
src/NLISSN.RuleCatalog.Generator/
```

它只依赖 Roslyn API 和生成器所需的 BCL/分析器包，不引用 `NLISSN.Rules` 运行时程序集。`NLISSN.Rules.csproj` 将其作为 analyzer 引用，使生成代码和规则实现进入同一个 `NLISSN.Rules` 编译单元。

生成器应：

1. 从当前 compilation 的符号中发现四个合法阶段基类的非抽象具体类；
2. 要求每个具体规则都有 `[RuleRegistration(...)]` 元数据；
3. 读取 feature，并由继承基类确定 Stage；
4. 检查类型可访问性和无参构造能力；
5. 检查简单类型名冲突、重复注册元数据和非法阶段；
6. 生成静态、无反射的 factory/descriptor 目录；
7. 对生成器自己的错误使用稳定诊断 ID；
8. 生成确定性的源文件名、类型顺序和文本。

生成器不应：

- 执行 `RuleRegistry` 的 feature 开关；
- 编译 `RulePipeline` 或规则 contract graph；
- 解析运行时 `RuleId` 值来决定是否注册；
- 扫描引用程序集并把外部类型混入默认规则集；
- 在生成失败时静默跳过规则。

### 规则元数据的最小集合

建议把元数据放在 Core/Pipeline 可引用的契约层，而不是把 `IRuleDefinition` 扩成策略接口：

```csharp
[RuleRegistration(RuleFeature.Core)]
public sealed class AtomicIdentifierNameMarkRule : RuleDefinitionMark
{
}

[RuleRegistration(RuleFeature.UnusedInterfaceImplementationCleanup)]
public sealed class ClearUnusedInterfaceImplementationRule : RuleDefinitionMark
{
}
```

最小元数据只表达：

- `Feature`：核心或四组现有可选 feature；
- `RuleKind`（阶段）：由四个阶段基类推断，不重复声明；
- 稳定排序：第一版沿用当前 `GetType().Name` 排序，避免行为变化；
- 构造方式：第一版只接受生成器可证明的公共无参构造。

如果未来确实需要显式排序，再增加 `SortKey`，但迁移第一版不能用新排序替换现有排序。`RuleId` 和 `CapabilityId` 继续由规则实例提供，因为它们是运行时执行 contract；重复身份仍由 `RuleCatalog.ValidateRules` 和独立 contract 测试校验。

### 生成目录的形状

生成器应输出类似以下概念模型的代码：

```csharp
internal static class GeneratedRuleCatalog
{
    public static IReadOnlyList<RuleRegistrationDescriptor> All { get; } =
        new RuleRegistrationDescriptor[]
        {
            new(
                "NLISSN.Rules.AtomicIdentifierNameMarkRule",
                "AtomicIdentifierNameMarkRule",
                RuleKind.Mark,
                RuleFeature.Core,
                static () => new AtomicIdentifierNameMarkRule()),
            // ...由生成器继续产生
        };
}
```

实际契约名称和可见性由实施阶段决定；关键不变量是：

- 生成源码中直接调用具体类型构造函数；
- 不使用 `Assembly.GetTypes`、`Activator.CreateInstance` 或运行时扫描；
- 所有规则都能在源码中看到自己的阶段、feature 和 factory；
- 目录是当前 `NLISSN.Rules` 编译单元的产物，不是外部插件发现结果。

### `RuleRegistry` 的迁移边界

迁移后的 `RuleRegistry` 仍负责：

```text
GeneratedRuleCatalog.All
    -> 按 RuleFeature 与调用参数过滤
    -> factory 实例化
    -> 按现有 Type.Name 排序
    -> RuleCatalog.ValidateRules
    -> disabledRuleTypes 过滤
    -> RulePipeline 四阶段组装
```

这样可以把“人工维护规则全集”替换为“人工声明 feature 元数据”，但不改变应用层 `RulePipeline` 的职责和规则图编译入口。

## 研究得到的主要风险

### 1. 发现和启用混淆

如果生成器把没有 feature 元数据的类默认标成 Core，新规则会意外改变默认输出。应对策略是：具体规则缺元数据时生成错误，而不是默认启用。

### 2. 构造依赖扩张

当前规则实现可以由无参构造完成，但这不是永久保证。第一版应在生成器中诊断无公共无参构造；未来若需要服务依赖，应单独设计 `RuleServices` 或显式 factory，不应引入运行时反射作为逃生门。

### 3. 稳定顺序漂移

程序集类型顺序、语法树顺序和生成器输入顺序都不能成为调度顺序。迁移第一版继续让 `CreateRules` 按当前类型名排序，并增加重复简单类型名诊断。

### 4. 规则族不完整

`UnusedInterfaceImplementationCleanup` 和 `InternalOnlyPublicMethodPrivatization` 需要 Mark、Propagate、Lift、Propose 链；`UnreachableMethodDeletion` 和 `UnreferencedMethodDeletion` 的当前形态主要是 Mark + Propose。生成器必须至少保留 feature 信息；完整 stage mask 由独立 contract 测试验证，避免把不完整 feature 误当作完整链。

### 5. 身份不是类型名

类型名只是 disabled 配置兼容入口；规则图身份仍来自 `RuleId`，能力合并仍涉及 `CapabilityId`。生成目录不能用类名替代这些执行身份，也不能在生成器中擅自重写它们。

### 6. 生成器工程引用方向

生成器必须作为 analyzer 被 `NLISSN.Rules` 使用，不能让运行时规则程序集反向引用生成器运行库。生成器读取 compilation 的符号，生成文件编译进规则程序集；这是保持项目依赖方向的关键。

## 分阶段实施建议

### 阶段 0：建立基线

- 记录当前默认和全部 feature 开启的四阶段规则类型集合；
- 增加测试期完整性比较，先证明现有手动目录没有遗漏；
- 锁定 DOP 1、2、16 的规则快照和 rewrite/diff 基线。

### 阶段 1：引入元数据和生成器骨架

- 在 Core/Pipeline 定义 `RuleFeature`、复用现有 `RuleKind`、`RuleRegistrationAttribute` 和 descriptor 契约；
- 给所有具体规则添加 feature 元数据；
- 生成器只生成诊断和只读 catalog，不改变 `RuleRegistry` 的执行路径；
- 验证生成目录与手动目录逐阶段精确相等。

### 阶段 2：切换 `RuleRegistry`

- 让 `RuleRegistry` 从生成目录创建四阶段候选列表；
- 保持 feature 过滤、disabled 追踪、`RuleCatalog.ValidateRules` 和排序顺序；
- 删除手动数组后运行全量行为等价回归；
- 只有通过等价验证才删除旧的手动注册代码。

### 阶段 3：完善编译期诊断

至少定义并测试以下诊断类别：

| 类别 | 失败条件 |
|---|---|
| 未注册实现 | 具体四阶段规则缺 `[RuleRegistration]` |
| 非法阶段 | 实现 `IRuleDefinition` 但不属于四个阶段基类 |
| 不可构造 | 没有可生成的无参构造 |
| 类型名冲突 | 不同全限定类型拥有相同简单名，破坏 disabled 名称兼容 |
| 元数据冲突 | 同一类型重复注册或 feature 值非法 |
| 目录为空/缺失 | 生成输出未包含预期阶段或生成过程异常 |

## 验收标准

该设计只有在以下条件全部满足后才允许标记为实现完成：

1. 新增一个具体规则但不加元数据时，编译期出现稳定诊断；
2. 新增一个带元数据的具体规则但不修改 `RuleRegistry` 时，规则自动出现在对应 feature 的 generated catalog；
3. 默认配置的四阶段类型集合、数量、顺序和当前基线一致；
4. 全部 feature 开启时的集合与当前基线一致；
5. `disabledRuleTypes` 的大小写不敏感行为和 disabled graph declaration 保持一致；
6. 重复 `RuleId`/`CapabilityId` 仍在规则校验中失败；
7. 无公共无参构造的规则不产生不可诊断的启动期异常；
8. DOP 1、2、16 的 stage snapshot、evidence、decision、rewrite 和 diff bytes 一致；
9. 生成代码不包含运行时程序集扫描或 `Activator.CreateInstance`；
10. `Miscellaneous/init.ps1`、受影响项目 build、规则 registry/graph 测试和全量必要回归均通过。

## 暂不做的事情

- 不支持外部规则程序集或插件热加载；
- 不把 feature 开关移入 `IRuleDefinition`；
- 不用命名约定替代四个阶段基类；
- 不在本设计中改变 `RuleId`、`CapabilityId` 或 contract graph；
- 不在没有基线和等价验证前删除手动 `RuleRegistry`；
- 不承诺生成器能自动推断一个 feature 的完整业务语义。

## 研究结论

外部项目的可迁移经验不是“反射扫描可以换成 source generator”这么简单，而是：把发现、元数据、构造、诊断和生成变成编译期事实，同时保留运行时 composition root 对策略的控制。对于 NLISSN，最稳的落点是“属性声明 feature，基类确定 stage，生成器生成无反射目录，`RuleRegistry` 保持策略和管道边界”。
