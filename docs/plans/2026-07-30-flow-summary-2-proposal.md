# Flow Summary 2.0 提案

## 目标

把 NLCPG 的外部调用流摘要从“多个 source 指向一个 target”的静态记录，升级为可组合的端点映射表。它为 `NLISSN.Core` 提供受来源、精度和预算约束的跨过程事实，支持删除规则判断包装器、回调、参数和返回值的保守影响范围。

## 当前事实

- `NLCPGFlowSummaryRegistry` 已按项目覆盖、框架摘要、未知三层解析稳定键。
- `NLCPGFlowSummary` 使用程序集、类型、方法名和泛型参数数量匹配，端点支持 receiver、parameter、return、pass-through 与 block。
- `NLCPGDefaultFlowSummaries.All` 当前为空。
- 当前摘要只表示一组来源到一个目标；同一个方法的多条独立映射需要拆成多条摘要，注册表却按键保留单条记录。
- `InterproceduralDataFlow` 仍只为确定的内部唯一目标建立桥；这个边界必须保留。

Joern 的 `FlowSemantic`、`FlowMapping` 和 `PassThroughMapping` 说明摘要应描述精确的端点关系，并允许项目规则覆盖默认语义。它不能成为外部调用的乐观默认传播。

## 范围

### 包含

- 多 mapping 的方法摘要与稳定、可审计的覆盖优先级。
- 端点：实例 receiver、命名或位置参数、返回值、`ref`/`out` 参数和显式阻断。
- 项目、框架、用户配置三类摘要来源及来源优先级。
- 摘要匹配、应用、拒绝和未知结果的结构化原因。
- 面向 rule 的只读查询接口，供传播规则请求一个具体调用点的已验证流关系。

### 排除

- 为未知外部方法自动生成 pass-through。
- 多目标动态分派、反射、表达式树、P/Invoke、远程调用或跨进程流的确定性声明。
- 用摘要替代 Roslyn 对内部源代码的真实语义分析。
- 在第一阶段增加默认框架摘要大表。

## 目标模型

```text
Method identity
  -> ordered mappings
       Receiver     -> Parameter(1)
       Parameter(2) -> OutParameter(2)
       Block(Parameter(1), Return)
```

建议替换单一 `Sources + Target` 表达为：

| 类型 | 职责 |
| --- | --- |
| `FlowSummaryMethodKey` | 由程序集身份、元数据类型、成员名、泛型形状、参数数量和参数名组成；从 Roslyn `IMethodSymbol` 规范化。 |
| `FlowSummaryEndpoint` | Receiver、Parameter、Return、RefParameter、OutParameter。参数同时保留 ordinal 与可选名称。 |
| `FlowSummaryMapping` | `Source`、`Target`、`MappingKind`；一个摘要包含有序去重的 mappings。 |
| `FlowSummaryBehavior` | `Explicit`, `PassThrough`, `Block`, `Unknown`，并说明是否允许返回传播。 |
| `FlowSummaryResolution` | 项目、用户、框架、未知、冲突或签名不匹配。 |
| `ResolvedCallFlow` | 已绑定调用点、映射、摘要版本、精度和拒绝原因。 |

`Parameter -> Return` 只表示 CPG 和证据中的调用边界数据流；它不得被传播为删除候选，或导致调用表达式及其宿主生成删除决策。`PassThrough` 的语义只在已命中摘要时生效：每个可流动输入只流向同位置输出和显式允许的返回端点。它不能跨参数交叉污染。`Block` 显式阻断指定端点组合；未命中摘要始终是 `Unknown`，不是透传。

## 匹配和优先级

1. 先从 `IInvocationOperation.TargetMethod` 取得规范化 `IMethodSymbol`，处理 reduced extension method、构造器和泛型构造。
2. 按完整 method key 查项目摘要，再查用户配置，再查框架摘要；同一来源内重复 key 是加载错误，不执行静默合并。
3. 参数映射先按 ordinal 绑定，再核对 named argument；混合命名/位置调用只能在 Roslyn 已给出唯一参数绑定时使用。
4. `ref` 与 `out` 只能匹配同类端点。普通 parameter 摘要不能证明对外写入。
5. 命中摘要但调用形状不兼容时返回 `SignatureMismatch`，不得回退到宽泛匹配。
6. 解析结果必须写入 Decision Evidence DAG 的 `UsesSummary` 节点，含来源与版本。

## Core 接口边界

摘要注册和序列化放在 `src/NLCPG/Analysis/FlowSummaries/`。`NLISSN.Core` 只依赖一个只读 `ICallFlowResolver`：输入 `IInvocationOperation` 和请求的 endpoint pair，输出 `ResolvedCallFlow`。规则不能读取注册表内部字典，也不能自己构造模糊方法名。

内部唯一目标仍优先用真实 `InterproceduralDataFlow` 边。摘要只补充没有可分析主体的外部或已配置边界；同一调用同时有真实内部边和摘要时，以真实边为准并记录 `SummaryIgnoredForInternalTarget`。

## 执行阶段

### 阶段 1：锁定保守语义

在 `tests/NLISSN.ContractTests/Cpg/` 先写失败用例：项目覆盖框架、同键冲突、泛型/扩展方法规范化、命名参数、`ref`/`out`、signature mismatch、unknown 不传播，以及多 mapping 的稳定顺序。

### 阶段 2：演进摘要数据模型

在 `src/NLCPG/Analysis/FlowSummaries/` 新增 method key、mapping 和解析结果模型。保留旧 `NLCPGFlowSummary` 作为单 mapping 兼容适配器，直到所有调用者迁移；不能把多 mapping 丢回一个目标。

### 阶段 3：实现解析器与验证器

注册表在创建时验证：端点 ordinal 有效、mapping 无重复、block 和允许传播无矛盾、来源层无重复 key。解析器只接受 `IMethodSymbol` 或 `IInvocationOperation`，使方法名字符串不能越过 Roslyn 绑定。

### 阶段 4：将摘要变成可选的 CPG 事实

在跨过程 pass 中为已解析、外部且预算允许的调用生成带 `NLCPGEdgeLabel` 的 bridge 候选。标签写入摘要键、来源和 mapping，不改变原有内部桥边。未知和不匹配调用只写诊断计数。

### 阶段 5：接入一个删除规则纵切

只选择现有 `TypeExtensionMethodMappedCallsitePropagationRule` 或一个参数收缩规则。该规则仅在摘要明确证明所需映射时扩展候选；保守路径保持当前跳过行为。对目录 fixture 比较摘要关闭与开启的 rewrite、diff 和诊断。

## 验收门槛

- 多 mapping、覆盖优先级、未知、签名不匹配和 `ref`/`out` 均有合约测试。
- 未配置外部调用不生成 `InterproceduralDataFlow` 边，也不扩大删除候选。
- 已配置摘要的边稳定包含 method key、来源和 endpoint mapping。
- 同一输入下 DOP 1/16 的 graph、slice、decision 和 diff 等价。
- 摘要注册表加载错误有稳定、可定位的消息；不默默覆盖或合并。
- 纵切规则在摘要未命中、被 block、被截断和内部真实目标四种状态下都保守处理。

## 风险与约束

| 风险 | 处理 |
| --- | --- |
| 摘要写错导致不安全删除 | 默认 unknown；项目摘要优先且必须通过真实调用 fixture。 |
| 稳定键忽略 overload 形状 | method key 包含参数 count、类型形状和 generic shape；测试重载。 |
| 多 mapping 造成组合爆炸 | 每调用点、每方法和每切片配置独立预算；超限返回截断状态。 |
| 框架摘要膨胀 | 第一阶段只提供注册机制和少量经过 fixture 验证的摘要。 |
| 与内部源冲突 | 真正的 Roslyn/CPG 内部桥优先，摘要仅作外部边界补充。 |

## 依赖与后续关系

该提案依赖有界、带类型的结构查询提供预算和边类别，也向决策证据 DAG 提供摘要来源。Symbol Usage Profile 可复用 `ICallFlowResolver` 描述调用影响，但不依赖默认摘要集合。
