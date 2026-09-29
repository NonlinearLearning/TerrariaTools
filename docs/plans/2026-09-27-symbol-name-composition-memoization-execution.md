# P06：符号名组合原语记忆化执行计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 给 `ComposeTypeFullName`、`ComposeMethodFullName`、`ComposeMethodSignature`、`ComposeMethodName` 四个符号名字符串组合原语加 builder 级记忆化，消除重复的 `ToDisplayString` 与重复的字符串拼接，同时保证产出的字符串**逐字节不变**。

**Architecture:** 这四个原语是**纯函数**：输入是 Roslyn 符号（或其类型），输出是确定性字符串，无副作用。同一构建内同一符号被反复传入（同一方法在 CallGraph 候选打分、方法装饰、调用边落键处各算一次；同一类型在每次 `ComposeMethodFullName` 里都重算一次 `ContainingType`）。故在 `NLCPGBuilder` 实例上按符号身份分键缓存结果，缓存生命周期与 `_baseTypeCache` 一致——构建开始时随既有清理逻辑清空，`Build()` 结束时由 `ReleaseTransientBuilderState` 释放。

**Tech Stack:** C#/.NET 10、`NLCPGBuilder`（partial class，跨 5 个文件）、`SymbolEqualityComparer.Default`、`ConcurrentDictionary`、xUnit ContractTests。

日期：2026-09-27。范围是用户提出的方案 A（"缓存原语 / 小 / 低风险 / 削最大常数、全链路受益"），本轮只写文档。执行切片 `symbol-name-composition-memoization` 尚未加入 [feature_list.json](../../Context/feature_list.json)；实施前先登记本项状态和完成条件。

---

## 0. 结论先行：为什么是这四个原语，以及真正的代价在哪

用户给出的判断（小、低风险、削最大常数、全链路受益）**在收益侧成立**，但"小"这一项需要修正：**改动集中在 4 个函数体，代价集中在它们的调用上下文**。

`ToDisplayString` 是 Roslyn 里出了名的昂贵调用——它要遍历符号、展开泛型、处理可空与元组，并分配字符串。全仓 `src/` 只有 26 处直接调用 `ToDisplayString`，而它们的分布极不均匀：**7 个字符串组合原语里占了 3 处**（`NLCPGBuilder.cs:4130` 在 `SymbolId`、`:4135` 在 `ComposeFullName`、`:4141` 在 `ComposeTypeFullName`）。也就是说**几乎所有符号名派生都收敛在这一小组函数**，这正是"削最大常数、全链路受益"的准确含义：不是调用点多，而是每个调用点背后都藏一次或多次 `ToDisplayString`。

更重要的是——**四个记忆化目标最终全部汇聚到 `:4141` 这一个 `ToDisplayString`**（已逐跳核验）：

| 目标 | 汇聚路径 |
| --- | --- |
| `ComposeTypeFullName`（`:4139`） | 自身即 `:4141` |
| `ComposeMethodFullName`（`:4180`） | `:4183` → `ComposeTypeFullName`；`:4184` → 另两个目标 |
| `ComposeMethodSignature`（`:4193`） | `:4196`（逐参数）+ `:4197`（返回类型）→ `ComposeTypeFullName` |
| `ComposeMethodName`（`:4211`） | `:4228`（显式接口实现分支）→ `ComposeTypeFullName` |

⇒ 缓存 `ComposeTypeFullName` 是**唯一的乘数点**：它一次命中即可省掉上游所有路径里的 `ToDisplayString`。这决定了 §6 的降级方案（只缓存它）。

重复是结构性的，不是偶发的：

- `ComposeMethodFullName`（`:4180`）**每次调用**都调 `ComposeTypeFullName(ContainingType)`（`:4183`）+ `ComposeMethodName`（`:4184`）+ `ComposeMethodSignature`（`:4184`）。
- `ComposeMethodSignature`（`:4193`）对每个参数调一次 `ComposeTypeFullName`（`:4196`），再加返回类型（`:4197`）。
- 源码自己已经承认过这个代价，且只手工消掉了一处：`CallGraphPass.cs:983-984` 注释写着"`ComposeTypeFullName` 未缓存，每调用一次即一次 `ToDisplayString`，原先每个候选要付 4 次"——他们在 `CallTargetScore`（`:969`）里把两段评分合并来少付 2 次，但**缓存本身从未加**。
- `CallGraphPass.cs:960-966` 的 `RankCallTargets` 把 `ComposeMethodFullName` 放进 `.ThenBy(...)` lambda（`:965`）：排序过程中按比较次数重算，是 O(n log n) 次而非 O(n) 次。
- `ComposeResolvedDispatchKind`（`NLCPGBuilder.cs:4371`）在 `:4381` 一行里对**两个不同符号**各调一次 `ComposeMethodFullName`。

因此：**收益真实、且是"每次调用都省"的常数削减**；但落地形态取决于一个必须先讲清的结构事实（见 §1）。

---

## 1. 关键结构事实：四个原语是 `private static`，而 35 个调用点来自 static 方法

这是本项**真正的成本所在**，也是"改动小"需要打折扣的地方。

四个原语当前签名（均在 `NLCPGBuilder` partial class 内）：

| 原语 | 位置 | 是否为记忆化目标 |
| --- | --- | --- |
| `ComposeTypeFullName` | `NLCPGBuilder.cs:4139` | ✅ 收益最大（38 个调用点） |
| `ComposeMethodFullName` | `NLCPGBuilder.cs:4180` | ✅ |
| `ComposeMethodSignature` | `NLCPGBuilder.cs:4193` | ✅ |
| `ComposeMethodName` | `NLCPGBuilder.cs:4211` | ✅ |
| `SymbolId` | `NLCPGBuilder.cs:4127` | ⚠ 见 §7 不采用项 |
| `ComposeFullName` | `NLCPGBuilder.cs:4133` | ⚠ 见 §7 不采用项 |
| `CanonicalMethodSymbol` | `NLCPGBuilder.cs:4170` | 非记忆化目标（本身极廉价，仅判 `IsExtensionMethod`） |

调用点分布（`src/` 全量，排除声明行）：

| 原语 | 调用点数 |
| --- | ---: |
| `ComposeTypeFullName` | 38 |
| `SymbolId` | 29 |
| `CanonicalMethodSymbol` | 24 |
| `ComposeMethodFullName` | 15 |
| `ComposeMethodSignature` | 8 |
| `ComposeMethodName` | 7 |
| `ComposeFullName` | 5 |

**难点**：缓存必须挂在 `NLCPGBuilder` 实例上（原因见 §2），而原语是 `static`。**把缓存做成实例字段，`static` 方法就访问不到它**。因此必须把原语从 `static` 改为实例方法，这会**沿调用链向上传染**：

- 因调用 `static` 方法而自身也必须是 `static` 的方法，同样得改成实例方法。
- 我按"直接调用原语 ⟶ 逐层向上"算过传递闭包：**20 个方法**必须从 `static` 改为实例（这 20 个之外还要加 4 个原语本身）。

受影响的 20 个方法（静态调用链的全部成员）：

| 文件 | 行 | 方法 |
| --- | ---: | --- |
| `NLCPGBuilder.cs` | 4139 | `ComposeTypeFullName` |
| `NLCPGBuilder.cs` | 4180 | `ComposeMethodFullName` |
| `NLCPGBuilder.cs` | 4187 | `ComposeInvocationMethodFullName` |
| `NLCPGBuilder.cs` | 4193 | `ComposeMethodSignature` |
| `NLCPGBuilder.cs` | 4202 | `ComposeInvocationSignature` |
| `NLCPGBuilder.cs` | 4211 | `ComposeMethodName` |
| `NLCPGBuilder.cs` | 4263 | `ComposeGenericTypeIdentity` |
| `NLCPGBuilder.cs` | 4272 | `ComposeSignature` |
| `NLCPGBuilder.cs` | 4371 | `ComposeResolvedDispatchKind` |
| `NLCPGBuilder.cs` | 4487 | `ComposePropertySignature` |
| `CallGraphPass.cs` | 912 | `PreferCallTargets` |
| `CallGraphPass.cs` | 960 | `RankCallTargets` |
| `CallGraphPass.cs` | 969 | `CallTargetScore` |
| `CallGraphPass.cs` | 1110 | `AddBaseType` |
| `CallGraphPass.cs` | 1119 | `MethodSignatureMatches` |
| `CallGraphPass.cs` | 1177 | `CanDispatchToExtensionReceiver` |
| `DataFlowPass.cs` | 2430 | `DefinitionFactForInvocation` |
| `DataFlowPass.cs` | 2474 | `ComposeInvocationPathKey` |
| `DataFlowPass.cs` | 2501 | `ReceiverKey` |
| `MemberAccessPass.cs` | 371 | `MembersMatch` |

**关键约束**：`CallGraphPass.cs:1110 AddBaseType` 与 `:1119 MethodSignatureMatches`、`:1177 CanDispatchToExtensionReceiver`、以及 `DataFlowPass.cs:2501 ReceiverKey` 等都可能被 **worker 线程**走到——worker 入口是 `:145` 的 `CollectCallGraphWorkBatch` 回调，它经 `:306` 调 `ResolveEffectiveCallTargets`，后者在 `:587` 调 `ResolveCallTargetCandidates`，而 `:542`/`:784`/`:887` 三处都在其内（详见 §3.2）。把它们从 `static` 改为实例方法本身是安全的（partial class 同一实例），但**它们随之访问的缓存必须是并发安全的**——见 §3。

> ⇒ **诚实结论**：本项不是"改 4 个函数"。它是"改 4 个函数 + 20 个签名 + 1 个并发安全缓存 + 1 处清理钩子"，且全部落在 `NLCPGBuilder` 这个**正被并发工作流改写的文件**上（见 §5 风险 R1）。仍然低风险（语义等价可证），但**不是小 diff**。

---

## 2. 缓存放在哪里：生命周期由既有机制决定

必须先钉死"缓存该活多久"，否则会引入跨构建的陈旧缓存。

三条事实（均已核验）：

1. **builder 是每批一个。** `ApplicationService.cs:184` 在 `BuildGraphBatchCore` 内 `new NLCPGBuilder(...)`，该循环（`:170-177`）只构造 documents，builder 在循环**之外**，且 `:184` 之后 `:187` 才 `BuildManyDocuments`。单文件路径（`ApplicationService.cs:453`）同样是"一 builder 一构建"。
2. **整批只跑一次 `Build()`。** `BuildManyDocuments` 在 `NLCPGBuilder.cs:1030` 调 `_ = Build(firstContext!)`，其注释明确写"整批只有这一次 Build ⇒ 指标是整批口径"（`:1028-1029`）。所以**一次 `Build()` 覆盖整批全部文件**——缓存天然获得跨文件复用的收益，无需额外设计。
3. **既有清理点正好两处**，与 `_baseTypeCache` 完全一致：
   - `NLCPGBuilder.cs:1130`（在 `Build()` 内，`:1074` 起）：构建开始时清空。
   - `NLCPGBuilder.cs:1540`（在 `ReleaseTransientBuilderState()` 内，`:1525` 起）：由 `Build()` 在 `:1446` 调用，冻结之后即释放。

`_baseTypeCache`（`:59`）是**完全同构的先例**：`ConcurrentDictionary<string, IReadOnlyList<INamedTypeSymbol>>`，实例字段，两处 `.Clear()`。它甚至在语义上就是"缓存 `ComposeTypeFullName(namedType)` 的派生结果"（`CallGraphPass.cs:1087` 用 `ComposeTypeFullName(namedType)` 当键）。

⇒ **决策：新缓存跟随 `_baseTypeCache` 的形状与清理时序。** 这样缓存生命周期与"一次构建的瞬时状态"严格一致，不会跨构建存活，也不需要新的清理机制。

---

## 3. 必须解决的三个技术问题

### 3.1 分键：用什么当 key

不能用 `string` 当键。若用 `ComposeTypeFullName(symbol)` 自己当键，就成了"为了查缓存先算一遍缓存内容"，**收益归零**。

可用的符号身份比较器在本仓有现成先例：

- `SymbolEqualityComparer.Default`——`Dictionary<ISymbol, ...>` / `Dictionary<IMethodSymbol, ...>` 的原生用法，例：`SymbolReferencePropagationRule.cs:64`、`ParameterShrinkAnalyzer.cs:1668`、`MethodLinkageGraphBuilder.cs:31`。
- `ReferenceEqualityComparer.Instance`——用于"引用身份"对象，例：`NLCPGBuilder.cs:47`（`_syntaxNodes`）、`GraphScopedCache.cs`。

**决策：用 `SymbolEqualityComparer.Default`。**

理由：同一语义符号在不同位置可能由 Roslyn 给出**不同的实例**（尤其跨 `SemanticModel` / 元数据与源码混合时）。`ReferenceEqualityComparer` 会漏掉这些重复，直接损失大半收益；而 `SymbolEqualityComparer.Default` 按符号语义比较，正是"同一符号"的准确含义。⚠ 但要注意它对**泛型实例化**的处理：`List<int>` 与 `List<string>` 的 `NamedTypeSymbol` 语义不同，会正确分到两个键——这正是我们要的（它们的 `ComposeTypeFullName` 结果本来就不同）。

⚠ **必须验证的边界**：`SymbolEqualityComparer.Default` 对 `ITypeParameterSymbol`（类型参数）与错误符号（`IErrorTypeSymbol`）的行为。`ComposeTypeFullName` 会被传入 `parameter.Type`（`ComposeMethodSignature:4196`）、`ReturnType`（`:4197`）、`TypeArguments`（`:4258`）——其中类型参数很常见。若比较器把它们按名字合并而 `ToDisplayString` 结果不同，就会产生**错误命中**。见 T2。

### 3.2 线程安全：worker 会走到这些原语

原语可从 **worker 线程**到达，路径已逐跳核验：

`CollectCallGraphWorkBatch`（`CallGraphPass.cs:276`，worker 回调）→ `ResolveEffectiveCallTargets`（`:306`）→ `ResolvePreferredCallTargets`（`:586`）→ `RankCallTargets`（`:921`/`:927`/`:936`/`:942`）→ `CallTargetScore`（`:969`）→ `ComposeMethodFullName`（`:986`/`:987`）。
同一 worker 回调另经属性引用分支（`:319-320`）到 `ResolveAccessorTargetCandidates`（`:833`），其内部在 `:839`/`:845`/`:882`/`:898`/`:906` 用 `SymbolId`，并经 `:887` 的 `EnumerateBaseTypes` 触发 `ComposeTypeFullName`（`NLCPGBuilder.cs:4141`）。`ResolveEffectiveCallTargets` 自身在 `:579` 还直接拼 `ComposeMethodFullName(canonicalTarget)` + `ComposeTypeFullName(receiverType)` 作为 dispatch 缓存键。

⚠ 注意 `RankCallTargets`（`:960`）与 `CallTargetScore`（`:969`）是 `static`，且**在 worker 内被调用**（`:921`/`:927`/`:936`/`:942` 四处在 `ResolvePreferredCallTargets` 内）。它们是 §1 表中必须改成实例方法的一员，改成实例后 worker 会访问实例缓存——这正是必须用 `ConcurrentDictionary` 的原因。
`RankCallTargets` 的 `.ThenBy(method => ComposeMethodFullName(method), ...)`（`:965`）使同一候选在排序过程中被反复重算，缓存把 O(n log n) 次计算降为 O(n) 次。

本仓对"worker 期间的共享写入"有**成文纪律**：`WriteSharedState`（`NLCPGBuilder.cs:323-340`）——窗口存活时必须取 `_cacheGate` 串行化；`_baseTypeCache` 之所以是 `ConcurrentDictionary`（`:59`），正是为了在 worker 路径上免锁读取。

**决策：沿用 `_baseTypeCache` 的做法——`ConcurrentDictionary`，worker 路径无锁。**

理由：`ConcurrentDictionary.TryGetValue`/索引器在 worker 上无锁且安全；这也是既有先例，不引入新的并发模型。⚠ **但必须明确**：`ConcurrentDictionary` 只保证"字典不被撕裂"，**不保证"同一符号只计算一次"**——并发下同一 key 可能被算两次，然后其中一个结果覆盖另一个。由于原语是纯函数，两次计算结果**逐字节相同**，故这是无害的重复计算，不是正确性问题。这条必须写进代码注释，否则将来会被误当 bug"修"成 `GetOrAdd` 加锁而拖慢 worker。

⚠ **不要用 `ConcurrentDictionary.GetOrAdd(key, factory)`**：其 factory 在竞争下可能被多次调用（这是官方文档明示的行为），且闭包会分配。用手写的 `TryGetValue` → 计算 → 索引器赋值三步，与本仓既有风格一致。

### 3.3 `ComposeInvocation*` 是两个**不规范化**的双胞胎——不能顺手套用缓存

这是最容易踩的坑。存在一对孪生函数：

| 会规范化（函数体首行调 `CanonicalMethodSymbol`） | 不规范化 |
| --- | --- |
| `ComposeMethodSignature`（`:4193`，`:4195` 调 `CanonicalMethodSymbol`） | `ComposeInvocationSignature`（`:4202`，**无** `CanonicalMethodSymbol`） |
| `ComposeMethodName`（`:4211`，`:4213` 调 `CanonicalMethodSymbol`） | `ComposeInvocationMethodName`（`:4234`，**无** `CanonicalMethodSymbol`） |

`CanonicalMethodSymbol`（`:4170-4178`）在扩展方法上返回 `ReducedFrom`（`:4177`），故同一符号经两条路径得到的字符串**可能不同**。

⇒ **两者必须分键或分开缓存，绝不能合并成一个缓存。** 若把 `ComposeInvocationSignature` 也接到 `ComposeMethodSignature` 的缓存上，扩展方法场景会静默返回错误字符串。见 T2 的守卫。

⚠ 这三个 `ComposeInvocation*` **都有外部调用点**，所以"不为它们建缓存"必须是真的不建，而不是"反正没人调"：

| 函数 | 外部调用点 |
| --- | --- |
| `ComposeInvocationMethodFullName`（`:4187`） | `CallGraphPass.cs:382`、`CallGraphPass.cs:448`、`NLCPGBuilder.cs:3677`（构造属性访问器调用点键） |
| `ComposeInvocationSignature`（`:4202`） | `CallGraphPass.cs:383`、`CallGraphPass.cs:449` |
| `ComposeInvocationMethodName`（`:4234`） | 仅 `:4190`（被 `ComposeInvocationMethodFullName` 内部调用） |

⚠ `CallGraphPass.cs:382-383` 与 `:448-449` 就在 `AddCallSite`（`:367`）与 `AddPropertyAccessorCallSite`（`:455`）里——即**归并回调**路径。故它们与 worker 路径（§3.2）是两条不同的线程上下文，两者都会经 `EnumerateBaseTypes` 之外的方式触达 `ComposeTypeFullName`。

---

## 4. 执行步骤

### T1：先证明"重复"确实存在，并建立基线

**动机**：本项收益是"省掉重复计算"，故必须先用数据证明重复率，否则可能优化一个本来就命中率极低的缓存（白付内存与复杂度）。

- [ ] 在四个原语入口加**临时**计数探针（调用次数 + 不同 key 数），跑一次代表性输入（建议与研究报告同尺度的目录，含至少 1 个巨文件）。
- [ ] 记录：总调用次数、去重后 key 数、比值 = 重复率。**若某原语重复率接近 1（几乎无重复），则该原语不建缓存**。
- [ ] 记录基线 `GC.GetTotalAllocatedBytes(precise: true)`。⚠ 用分配字节而非墙钟：`feature_list.json` 里 `interprocedural-plan-compaction` 的 notes 已明确记录墙钟在本机**不可用**（同二进制同输入三次差 −70.9%/+16.3%/+44.6%），而分配字节能复现到 ±0.05%。用墙钟会得出假结论。
- [ ] 探针只用于测量，**不进最终 diff**。

### T2：确认符号比较器的正确性边界（**先于实现**）

- [ ] 写一个一次性断言，对以下符号各取 `ComposeTypeFullName` 两次并比较，同时确认 `SymbolEqualityComparer.Default` 的相等判定与字符串结果一致：
  - 类型参数（`ITypeParameterSymbol`，来自 `methodSymbol.TypeParameters`）
  - 泛型实例化（`List<int>` vs `List<string>` vs `List<T>`）
  - 错误符号（`IErrorTypeSymbol`，来自不完整编译）
  - 元数据符号 vs 源码符号（同一声明两处引用）
  - 扩展方法的 `ReducedFrom` 与本身
- [ ] 对每个符号，断言 `Equals(a,b) == false ⟹ ComposeTypeFullName(a) != ComposeTypeFullName(b)`。**这是缓存正确性的充要条件**：只要"不相等就不同串"，记忆化就不可能改变输出。
- [ ] 若发现反例（不相等但同串），**记录并放弃该原语的缓存**，而不是放宽比较器——放宽会破坏正确性。

### T3：实现缓存与访问器

- [ ] 在 `NLCPGBuilder.cs` 字段区（`_baseTypeCache` 附近，`:59`）新增 3 个实例缓存（`ComposeMethodName` 视 T1 结果决定）。命名遵循 `_camelCase`（[C# 约束](../../Context/约束/Google-CSharp-Style-Guide-约束.md) §2.1）；`static`/`readonly` 不改变命名规则。
- [ ] 每个缓存用 `SymbolEqualityComparer.Default` 构造。
- [ ] 四个原语改为实例方法，函数体改为 `TryGetValue` → 计算 → 赋值 三步。
- [ ] 在缓存赋值处写注释记录 §3.2 的"并发下可能重复计算、结果相同故无害"，防止后人误改成加锁的 `GetOrAdd`。
- [ ] 在两处清理点（`:1130`、`:1540`）加 `.Clear()`，与 `_baseTypeCache` 并列。⚠ 两处都要加：只加 `Build()` 会漏掉 `ReleaseTransientBuilderState`，使缓存活过冻结（虽然此时已无消费者，但会让"构建期瞬时状态"的释放契约不再完整）。
- [ ] **红线检查**：`ComposeInvocationSignature` / `ComposeInvocationMethodName` **不得**接入任何缓存（§3.3）。

### T4：改造 20 个调用上下文的 `static` → 实例

按 §1 的表逐个改签名。

- [ ] 先确认每个方法确实**不需要** `static`（即：没有任何调用方在**没有 builder 实例**的上下文中调用它）。⚠ 这是本步唯一的真实风险：若有 `static` 上下文（如静态构造函数、静态字段初始化器、另一个 static 工具的入口）调用这些方法，改实例会**编译失败**——好在失败是显式的，不会静默。
- [ ] 逐个改，**每改一个小批次就编译**（`docs/AGENTS.md` 要求"每次只做一个小批次 patch"）。编译器会穷尽列出所有需要跟随改动的调用点，这是本步的安全网。
- [ ] 保持语义零改动：只加 `static` 关键字的增删，**不改函数体**，直到 T3 的原语改动。

### T5：等价性验证（**本项的核心断言**）

- [ ] 同一输入，改动前后**全部产物逐字节相同**：优先复用既有 ContractTests 作为 oracle——
      `NLCPGPartitionedBuilderTests`、`NLCPGNodeIdContractTests`、`NLCPGNodeIdentityContractTests`、`LocalCpgFragmentContractTests`、`FrozenEdgeProjectionEquivalenceTests`、`InterproceduralEdgeIndexSnapshotTests`（后者在 `:742` 断言 `node.DispatchKind`，正是 `ComposeResolvedDispatchKind` 的产物）。
- [ ] 特别覆盖**扩展方法**与**显式接口实现**：前者是 `CanonicalMethodSymbol` 的分支（`:4170-4178`），后者是 `ComposeMethodName` 的分支（`:4224-4229`）。这两个分支最可能因缓存分键不当而出错。
- [ ] 断言 `GraphSnapshotVersion` 不变（它对节点与边取 SHA-256，任何字符串变化都会改变它——这是全量等价性的强 oracle）。
- [ ] 记录 T1 基线 vs 改动后的分配字节，给出比值。

### T6：登记与收尾

- [ ] 本计划尚未登记进 [feature_list.json](../../Context/feature_list.json)；实施前先登记 feature 与完成条件。
- [ ] 按 [Harness 验证矩阵](../harness-verification-matrix.md) 执行匹配层级：`pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1`，并如实记录实际执行的命令与未验证边界。
- [ ] 非性能改造不要求 `dotnet-trace`/`dotnet-counters`/`dotnet-gcdump`（验证矩阵明示）。

---

## 5. 风险

| # | 风险 | 形态 | 收口 |
| --- | --- | --- | --- |
| **R1** | **本文件正被并发工作流整体改写** | `NLCPGBuilder.cs` 当前**已被修改**（`git diff --stat` = +26/−8，含 `_callGraphFrozenMethodIndexCount` 等）。`feature_list.json` 的 `interprocedural-plan-compaction` 记录过更严重的情形：并发工作流**用自己的内存快照整体重写**该文件，把已回滚的改动复活，导致测试对着已修好的行报同样的失败 | 全窗口独占该文件；**编辑前后各验一次源码**；不信任首次应用的结果 |
| **R2** | 缓存陈旧（跨构建存活） | 若漏掉任一清理点，第二次构建会用上一次的符号→字符串映射。若两次构建的 compilation 不同，**静默产出错误名字** | T3 要求两处都加 `.Clear()`；T5 覆盖"同 builder 连续两次构建" |
| **R3** | 比较器把不同串的符号判为相等 | 静默返回错误字符串，且**不抛异常** | T2 的充要条件断言；发现反例即放弃该原语缓存 |
| **R4** | `ComposeInvocation*` 误用缓存 | 扩展方法场景静默返回错误字符串（§3.3） | T3 红线检查 + T5 覆盖扩展方法 |
| **R5** | 内存增长 | 缓存随符号数增长，且**跨文件累积**（一次 `Build()` 覆盖整批） | 见 §6；由 T1 测量 + `System.GC.ConserveMemory=7`（`NLCPG.csproj:17`）兜底 |
| **R6** | 收益不达预期 | 重复率低或 `SymbolEqualityComparer` 开销抵消收益 | T1 先测；允许逐原语放弃 |
| **R7** | 缓存本身成为热点 | `SymbolEqualityComparer.Default` 的 `GetHashCode` 对复杂泛型类型可能**比一次 `ToDisplayString` 还贵** | T1 记录去重后 key 数；若 key 数接近调用次数，缓存退化为纯开销，须放弃 |

**R7 必须重视**：符号的哈希与相等比较会遍历泛型实参、可空注解与元组元素，而 `ToDisplayString` 虽贵却是单遍展开。对**只出现一次**的符号，缓存是净亏损（一次哈希 + 一次相等比较 + 一次字典写入 vs 一次 `ToDisplayString`）。这正是 T1 必须先测重复率的原因。

---

## 6. 缓存生命周期与内存（用户点名的关切）

**生命周期（已由 §2 钉死）**：构造于 builder 实例 → 构建开始时清空（`:1130`）→ 构建期存活（**跨全部文件**，因整批只有一次 `Build()`）→ 冻结后由 `ReleaseTransientBuilderState` 清空（`:1540`）。**不跨构建存活。**

**内存上界怎么估**：缓存条目 ≈ 符号对象引用 + 字符串。字符串本身**已经会被产出**（原语无论如何都要返回它），故缓存的**增量**只有：

- 每个唯一符号一条字典条目（`ConcurrentDictionary` 节点 + 桶，约几十字节）。
- 每个唯一字符串一份**额外**引用（字符串驻留不变，因为原语返回的实例被缓存复用——这实际上是**净减少**分配，因为不再产生重复字符串对象）。

⚠ 但有一个**必须诚实指出的放大器**：`ComposeMethodSignature` 的结果串把全部参数类型拼进去，`ComposeMethodFullName` 又把签名嵌进去。对参数多的方法，单个字符串可能上百字节。且 `SymbolEqualityComparer` 的桶会**长期持有符号引用**，进而经 Roslyn 符号图间接持有语法树与 compilation。

⇒ 因此：
- 三个缓存只在 `Build()` 期间存活，峰值与"本批符号数"同阶；对 967 文件量级应做实测（T1）。
- 若实测显示内存增量不可接受，**降级方案**：只缓存 `ComposeTypeFullName`（38 个调用点、返回值最短、复用最广），放弃两个 method 级缓存。这是保收益、砍内存的最优取舍。
- **不采用** LRU/容量上限：本仓无此先例，且会引入非确定性的缓存行为（同一输入在不同淘汰路径下结果应相同，但"结果相同"这一性质会变得难以断言）。宁可整体不缓存，也不引入容量上限。

---

## 7. 不采用项

| 方案 | 不采用理由 |
| --- | --- |
| 缓存 `SymbolId`（`NLCPGBuilder.cs:4127`） | 它先试 `GetDocumentationCommentId()`，**未命中才**落 `ToDisplayString`。文档注释 id 本身已是稳定键且通常命中，重复计算窗口小；且它是 29 个调用点的键生成器，缓存它会把风险扩散到身份层 |
| 缓存 `ComposeFullName`（`:4133`） | 只有 5 个调用点，收益最小，不值得扩大改动面 |
| 缓存 `CanonicalMethodSymbol`（`:4170`） | 本身极廉价（判 `IsExtensionMethod` + 取 `ReducedFrom`），无 `ToDisplayString`；缓存是净亏损 |
| 给 `ComposeInvocationSignature` / `ComposeInvocationMethodName` 建缓存 | 各只有 1 个调用点，且**不规范化**（§3.3），并入缓存会造成扩展方法误命中 |
| 用 `ConcurrentDictionary.GetOrAdd` | factory 在竞争下可被多次调用；且闭包分配。改用 `TryGetValue` + 索引器三步（§3.2） |
| 用 `ReferenceEqualityComparer` 分键 | 同语义符号的不同 Roslyn 实例会被漏掉，丢失大半收益（§3.1） |
| 用 LRU / 容量上限 | 本仓无先例，且引入非确定性淘汰，使"结果不变"难以断言（§6） |
| 用 `static` 全局缓存 | 跨构建存活，且并发构建（多 builder）下会互相污染；本仓有 `GraphScopedCache` 专门记录过"缓存与图无关会导致跨图交叉污染"的教训 |
| 把缓存放到 `NLCPGGraph` / `StringInterner` | 原语在**建图之前**就大量使用（`MethodDecorationPass` 建节点时），此时图尚不可用；且 `StringInterner` 是共享的（scratch 图也共用），不适合承载 builder 局部状态 |
| 顺带缓存 `InheritsFrom`（`:4499`） | 它走 `SymbolEqualityComparer` 而非 `ToDisplayString`，受益机理不同；`_baseTypeCache` 已覆盖了它的主要上游。属独立议题，不并入本项 |
