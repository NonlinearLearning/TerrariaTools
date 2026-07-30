# Symbol Usage Profile 提案

## 目标

为一次 `Compilation` 建立按 Roslyn `ISymbol` 身份索引的只读 usage profile，使 `NLISSN.Core` 的删除、参数收缩和可见性规则共享同一份调用、引用、委托、继承和声明宿主事实。规则只消费经过边界分类的 profile，不能各自扫描整份 compilation 得出互相不一致的结论。

## 当前事实

- 类删除和参数收缩已经有 `MethodParameterUsagePayload`、`DelegateUsagePayload`、`ExtensionMethodMappedCallsitePayload` 与 `DeclarationHostPayload`。
- 这些 payload 由多条 propagation rule 分别收集，逻辑集中在 Rule 层，跨规则复用有限。
- `MarkAnalysisSnapshot` 已缓存 operation、atomic candidates、target match 和 slice result，证明 compilation 范围的只读缓存有现成运行时边界。
- 规则图区分结构输入与 payload，但 payload 的 `object` 类型仍让消费者依赖运行时类型判断。

Joern usage slice 的启发是把“某变量如何定义、调用、作为参数和被引用”作为独立分析产品。NL 需要更严格的 Roslyn 符号身份、跨文件工作区和 rewrite 安全条件。

## 范围

### 包含

- 方法、参数、局部、字段、属性、事件、类型和委托的统一 symbol usage profile。
- 调用点、参数绑定、方法组、lambda、delegate invocation、override/interface implementation、引用和声明宿主的分类事实。
- 作用域、可访问性、动态/表达式树/反射等不确定因素的显式阻断状态。
- 单 compilation、单 epoch 的 lazy single-flight 构建与稳定顺序。
- 由 profile 派生现有类型化 propagation payload 的适配器。

### 排除

- 跨 compilation、跨进程或运行时反射事实。
- 把 every symbol 的完整 CPG 子图常驻内存。
- 让 profile 直接发起 rewrite 或决定删除。
- 将无法证明的调用、委托转换或 overload rebinding 判为安全。

## 目标模型

```text
Compilation + SemanticModel
        |
        v
SymbolUsageProfileBuilder
        |
        +-- SymbolDeclarationFacts
        +-- ReferenceFacts
        +-- InvocationFacts
        +-- DelegateBindingFacts
        +-- TypeRelationFacts
        +-- UncertaintyFacts
        v
Rule-specific typed adapters -> propagation payloads -> proposals
```

| 类型 | 职责 |
| --- | --- |
| `SymbolKey` | 由 Roslyn `SymbolKey` 或等价的稳定 project-local identity 表示；禁止以显示名作主键。 |
| `SymbolUsageProfile` | immutable snapshot，按符号和 syntax anchor 查询已排序 facts。 |
| `InvocationUsageFact` | 调用语法、目标方法、每个 argument 到 parameter 的 Roslyn 绑定、调用种类和不确定状态。 |
| `DelegateBindingFact` | 委托声明、方法组/lambda/直接调用链及是否存在不支持转换。 |
| `TypeRelationFact` | base type、interface implementation、override、显式实现和可访问性。 |
| `SymbolUncertainty` | Dynamic、表达式树、多跳转换、反射、未绑定符号、跨 compilation、过量 fan-out。 |
| `UsageProfileQuery` | 有界查询参数，包含最大 callsite 数、最大引用数、是否允许跨可执行作用域。 |

profile 中的每个 fact 均包含文件/span、关联 `NodeId`（若可解析）、事实来源和稳定排序键。规则使用 `SemanticModel` 绑定的 `ISymbol` 进入 profile，profile 不接受字符串目标名。

## 构建与缓存

1. `AnalysisRuntime.GetOrCreateCompilationCache` 持有 profile builder；新 epoch 或 cache invalidation 生成新视图。
2. 先遍历每棵 syntax tree 的声明和 operation，收集局部 facts；工作线程只读取 Roslyn facts。
3. 在稳定提交路径按文件、span、symbol key、fact kind 排序并冻结索引。
4. 查询按 symbol key 返回 facts 和 `UsageProfileStatus`：`Complete`、`Truncated`、`Unavailable`、`Uncertain`。
5. 当计数超过预算时保留已观察数量和截断原因；规则默认拒绝扩大候选。

这与 CPG 构建的“工作线程读语义、稳定线程物化图与顺序”一致，避免并发枚举顺序影响 decision。

## 规则消费边界

| 现有用途 | profile 查询 | 安全前提 |
| --- | --- | --- |
| 方法参数收缩 | `GetParameterCallsites` | 每个 callsite 有唯一参数绑定，且无 dynamic、无混合不可证明重载。 |
| 局部函数/索引器参数 | `GetCallableUsages` | 访问均在可分析 compilation，命名参数与可选参数完整绑定。 |
| 委托参数 | `GetDelegateBindings` | 单层、可分类的 method-group 或 lambda；多跳和表达式树返回 `Uncertain`。 |
| 扩展方法非 receiver 参数 | `GetExtensionCallsites` | reduced method 与静态调用都解析到同一原始定义。 |
| 类型删除与可见性 | `GetTypeRelations`、`GetReferences` | 显式 interface implementation、override 和外部可见性均被枚举。 |
| 不可达分析辅助 | `GetMethodInvocations` | 不把未绑定、反射或外部入口解释为无引用。 |

`RuleDefinitionPropagate` 继续产出领域 payload，但 payload 应改为封装 profile query 的不可变结果或引用，而不是重扫 compilation。不能将 `SymbolUsageProfile` 本身作为泛型 `object` payload 传到所有规则。

## 执行阶段

### 阶段 1：建立测试语料和拒绝边界

在 `tests/NLISSN.HostTests/Propagation/` 增加共享 fixture，覆盖位置/命名/可选/params 调用、重载、extension reduced/static、method group、lambda、delegate chain、override、explicit interface、跨文件引用、dynamic、expression tree 与未绑定代码。每个不确定 shape 都断言 profile 返回拒绝原因。

### 阶段 2：实现最小 profile

在 `src/NLISSN.Core/Analysis/` 新增 `SymbolUsageProfile`、facts 与 builder。第一批只索引方法及参数的声明、调用和 argument binding；单元测试验证 identity、排序、epoch 隔离和 single-flight。

### 阶段 3：迁移参数收缩纵切

将 `TypeMethodParameterUsagePropagationRule` 和一个命名参数规则改为只查询 profile。比较迁移前后 payload、decision、rewrite 和 diff；发现不确定 profile 时保留原来的跳过或诊断行为。

### 阶段 4：迁移委托、扩展方法与类型关系

分三批增加 delegate binding、extension mapping 和 type relation facts。每批先迁移一个 rule family，移除该 family 内重复的 compilation 扫描，再做目录 fixture 与 DOP 等价。

### 阶段 5：收紧 payload 合同

为 profile 派生 payload 创建具体 record，不继续扩大 `object? Payload` 的类型分支。结构契约只路由 rule 输出；profile query status 由 payload 明确携带。

## 验收门槛

- 每个迁移规则都由 profile 支持，生产路径中没有同类 compilation 全量扫描。
- DOP 1/16 下 profile facts、payload、决策、rewrite 和 diff 相同。
- profile 对 dynamic、反射、表达式树、多跳 delegate、未绑定和预算超限一律返回明确的非安全状态。
- SymbolKey 相同的 partial type、override、explicit implementation 与扩展方法在跨文件 fixture 中稳定归并。
- compilation 更换或 `AnalysisRuntime.NextEpoch()` 后不复用旧 profile；并发首次查询只构建一次。
- 性能证据先分别记录 profile 构建和各规则查询耗时，再判断是否移除现有缓存或扫描路径。

## 风险与约束

| 风险 | 处理 |
| --- | --- |
| `SymbolKey` 跨项目解析失败 | profile 限定单 compilation；跨 compilation 返回 `Unavailable`。 |
| 统一索引增加首轮成本 | lazy 分域构建，先只支持已迁移规则所需 facts，并记录构建时间。 |
| 过宽调用收集误报未使用 | 不确定或跨边界调用保守计入阻断，不证明可删除。 |
| payload 迁移破坏已有规则图 | 一个 rule family 一次迁移；保持结构 selector、RuleId 和输出顺序。 |
| 目录并行重用 Roslyn 状态 | 每个 compilation cache 独立，禁止静态全局 symbol 表。 |

## 依赖与后续关系

有界、带类型的结构查询提供可复用的 CPG 邻接与预算策略；Flow Summary 2.0 为外部调用提供补充事实。profile 的 query status、使用的摘要和不确定原因进入决策证据 DAG。图与规则绑定校验器负责验证 profile 暴露的可选 `NodeId` 与 syntax anchor 一致。
