# M6 LocalCpgFragment 数组所有权转移 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 消除可信生产者 ToArray 后构造器再次 ToArray 的重复载荷复制，保留公开构造器的防御性复制。

**Architecture:** 为内部生产者提供显式 CreateOwned 工厂，接收独占的新数组，复用同一校验逻辑并包裹为只读视图。公开构造器保持复制；只有能证明数组不再被修改或复用的两个局部 pass 迁移。

**Tech Stack:** C# record、数组、Array.AsReadOnly、现有 LocalCpgFragment/ContractTests。

---

日期：2026-09-25。feature：`local-fragment-array-ownership`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置和验收口径见 [总索引](2026-09-25-memory-optimization-execution-index.md)。
**本文件已执行完毕**：Task 1–3 全部落地，结果与证据见文末[第 7 节](#7-执行结果与证据)。

## 1. 范围与选择

| 方案 | 决定 | 理由 |
| --- | --- | --- |
| internal CreateOwned 明确转移数组 | 采用 | 零额外载荷复制，调用点与所有权可审查 |
| 公开构造器遇到数组就不复制 | 不采用 | 调用方可继续修改，破坏现有不可变观察契约 |
| ArrayPool/IMemoryOwner/Dispose 协议 | 不采用 | 引入归还时机、异步生命周期和悬空使用风险 |
| 去掉 availableAnchors/端点校验 | 不采用 | 属于行为削弱，不是重复复制优化 |

粗估 0.5–1 人日。产品文件仅三个：

- [LocalCpgFragment.cs](../../src/NLCPG/Builder/Concurrency/LocalCpgFragment.cs)。
- [ControlFlowPass.cs](../../src/NLCPG/Builder/Passes/ControlFlowPass.cs)。
- [ControlDependencePass.cs](../../src/NLCPG/Builder/Passes/ControlDependencePass.cs)。

构造器目前对 nodes、edges、summaries、boundaries、diagnostics 全部 ToArray；两个 pass 的 nodes/edges 已是刚生成的数组。
本项不更改局部图冻结、查询算法或 fragment 统一发布时机。

## 2. 所有权契约

1. CreateOwned 接收五个明确的数组参数，不接收任意 IReadOnlyList 来暗示所有权。
2. 工厂仅 internal，不增加公开 bool 开关；公开构造器仍按原规则校验并复制全部集合。
3. 成功返回后，生产者放弃数组的写入和复用；fragment 持有只读包装，不能从公开属性取回可写数组。
4. 工厂校验失败不修改输入；所有权仍属于生产者。共享 Array.Empty 是不可写的零长度特例。
5. 只转移数组存储，不宣称深拷贝元素内部引用；元素的原不可变/值语义保持不变。
6. 两条入口共享同一份边界与参数校验，保留异常类型、参数名和现有校验顺序。

建议形状：private 构造器接收原集合参数与 private ownership 标记；public 入口固定复制，internal 工厂用数组参数固定接管。
在校验完成后统一赋值，各属性只决定是否复制数组，再用 Array.AsReadOnly 包裹。
禁止为 public 入口先在构造器实参中调用 ToArray，导致 null 的异常参数名从 nodes 变为 source。

内部节点赋值的核心逻辑（owned 只能来自数组工厂）：

```csharp
var nodeArray = owned ? (CpgNodeDescriptor[])nodes : nodes.ToArray();
Nodes = Array.AsReadOnly(nodeArray);
```

其他四个集合使用同一模式，避免两份校验代码。不得只因 `nodes is T[]` 就接管公开传入的数组。

## 3. Task 1：防御性复制和所有权护栏

**Files:**
- Test: `tests/NLISSN.ContractTests/Cpg/LocalCpgFragmentContractTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchLocalPassTests.cs`。
- Fixtures: `tests/NLISSN.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`（仅在缺少适当素材时新增）。

1. public 构造后逐一修改原五个数组，fragment 观察结果必须不变；也覆盖传入可变 List。
2. 新工厂未实现前其测试应无法通过；实现后用可信独占数组构造，与 public 入口逐项比较所有属性和顺序。
3. 非法边端点、空参数、负序号、空 sourceFilePath，两入口报错行为一致。
4. 公开属性不是可写数组；通过 IList 写入只读包装时应被拒绝。
5. 通过生产 pass 构造多个 fragment，确认后一个批次不污染前一个批次。

“工厂传入数组之后再故意写该数组并要求隔离”不属于接管契约；用生产者不保留写别名的代码审查和多批次测试验证所有权。

## 4. Task 2：工厂和两个生产者迁移

1. 抽出/复用当前校验与赋值构造路径，添加 internal CreateOwned。当前 ContractTests 已有 InternalsVisibleTo，使用现有入口，不新增广泛暴露。
2. ControlFlowPass 在 nodeDescriptors/edgeCandidates ToArray 后，把本地数组交给工厂并立即返回。
3. ControlDependencePass 同样迁移 descriptors/edges；数组之后没有写入、缓存或对象池归还。
4. 保留所有 metrics、诊断、边界引用及方法摘要；不改变公开构造器调用者。
5. 运行两条生产路径的完整节点/边元数据和枚举序比较。

## 5. Task 3：证明少一次复制而非只换 API

在已预热、同线程的受控构造实验中，输入数组预先创建，分别测 public 与 owned 构造的累计分配。
各规模包含 0、小数组和跨 LOH 尺寸数组，节点、边两种载荷分别变化，保持校验工作相同。

预期少掉的主载荷是 `nodes.Length × 实测 descriptor 宽度 + edges.Length × 实测 candidate 宽度`，
再加其他非空输入数组本体；包装对象和 availableAnchors HashSet 仍存在。需要记录它们，不能把保留校验说成零分配。
owned 路径要求没有第二份节点/边载荷数组；完整生产片段创建阶段的累计分配必须净下降。

## 6. 命令、验收和回退

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~LocalCpgFragmentContractTests|FullyQualifiedName~CpgWorkBatchLocalPassTests|FullyQualifiedName~CpgFragmentReducerContractTests'
```

先执行总索引构建。通过条件：公开入口防御性复制不变；owned 无重复载荷数组；校验、完整输出、并发批次隔离均一致。
耗时遵守共同门槛；收益是重复物化消除，不声称整个图峰值有同等比例下降。

若某生产者仍有写别名，只把该调用点留在 public 复制入口，不引入复杂租约；两个目标调用点全部验证后才关闭 M6。

## 7. 执行结果与证据

**状态：已完成。** 产品代码改动正好落在第 1 节限定的三个文件内：

| 文件 | 改动 |
| --- | --- |
| [LocalCpgFragment.cs](../../src/NLCPG/Builder/Concurrency/LocalCpgFragment.cs) | 公开 9 参构造器转发到新的 private 10 参构造器（`ownsCollections: false`）；新增 `internal static CreateOwned(...)`（五个显式数组形参） |
| [ControlFlowPass.cs](../../src/NLCPG/Builder/Passes/ControlFlowPass.cs):89 | `new LocalCpgFragment(` → `LocalCpgFragment.CreateOwned(` |
| [ControlDependencePass.cs](../../src/NLCPG/Builder/Passes/ControlDependencePass.cs):142 | 同上 |

`DominancePass.cs:451` **刻意保留在复制入口**（第 1 节只允许迁移两个生产者）。

### 7.1 定向验收（第 6 节原文命令）

**37 通过 / 0 失败 / 0 跳过**。原始日志见
`Build/MemoryOptimization/M6/20260925-m6/final-verify-1.log`（另有 `post-restore-1.log` 同一结果）。

### 7.2 Task 3 受控分配证据

输入数组预先创建、两条入口均预热、同线程累计分配（`GC.GetAllocatedBytesForCurrentThread`）。非载荷集合传共享
`Array.Empty<T>()`（其 `ToArray` 实测零分配），使两条入口的差异**严格只剩 nodes/edges**。
`saved` = public 分配 − owned 分配。

| nodes / edges | 实测载荷 | 实测两次复制 | saved |
| --- | ---: | ---: | ---: |
| 2048 / 1536 | 372,736 | 372,784 | **372,784** |
| 64 / 48 | 11,648 | 11,696 | **11,696** |
| 1 / 1 | 212 | 264 | **264** |
| 0 / 0 | 0 | 0 | **0** |

宽度同运行时实测：`CpgNodeDescriptor` = 92 B、`CpgEdgeCandidate` = 120 B。
日志 `m6-alloc-evidence.log`。

> **期望值不是猜的常数**：断言比较的是 `saved == 同运行时实测的两次 ToArray 成本`，
> 而不是 `saved == 按宽度推导的载荷`。二者相差 24 B（数组对象头，`Unsafe.SizeOf` 只算元素宽度）。
> 早先一版写成 `saved == payload`，**连空载荷也失败**（差 96 B = 三个非空集合的复制成本）。
> 现版本同时锁住「省下的正是载荷副本」与「没有省到别的东西」。

### 7.3 结构性所有权证据（不依赖分配计数器）

`CreateOwned_WhenGivenProducerArrays_KeepsThoseExactArraysInsteadOfCopying`：
取出 `ReadOnlyCollection<T>` 的内部承载数组，对**五个集合**断言
owned 路径 `Assert.Same`（就是生产者那一个实例）、public 路径 `Assert.NotSame`（防御性复制仍在）。
把「少一次复制」与「公开入口仍复制」同时钉死，且不依赖任何计数器或时序。

### 7.4 变异检查（判别力证明）

| 变异 | 结果 |
| --- | --- |
| **A**：工厂传 `ownsCollections: false` | 4 条测试失败 ⇒ 承重 |
| **B**：公开构造器改传 `true`（等于去掉防御性复制） | 7 条测试失败 ⇒ 承重 |
| **C**：`ControlDependencePass` 回退为 `new LocalCpgFragment(` | **只有**源码读取式接线护栏抓到 |

日志：`mutation-A-3.log` / `mutation-B-1.log` / `mutation-C-retry-1.log`。三次变异均已按原样恢复，
`LocalCpgFragment.cs` 恢复后 SHA256 = `5CFFF0E2045D77951ECE96097E19641502BE240685492C89D293AA531C2B10AA`。

> **变异 C 是本轮最重要的方法论产出。** 两条入口产出**逐字段、逐顺序完全相同**的 fragment，
> 因此**任何行为测试都无法区分生产者调用了哪一个**。若只保留图等价测试与 A/B，
> 有人把生产者改回 `new LocalCpgFragment(...)` 会**静默恢复重复载荷复制**，而全部测试依然通过。
> 故新增源码读取式护栏 `LocalFragmentProducers_UseOwnedFactoryInsteadOfDefensiveCopyConstructor`，
> 并同时断言 `DominancePass` 仍在复制入口（范围漂移会被拦下）。
> 这与 `Context/progress.md` 轮次 4 记录的 `ThreadPool.ThreadCount` 教训同类：**无判别力的测试等于没有测试**。

### 7.5 更宽档位与归因（不得越读）

`Run-TestTiers.ps1 -Fast`：Unit **117/117**；Contract **685 通过 / 7 失败**。7 条**没有一条归因于 M6**：

- `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`：既有失败，
  另一工作流的未跟踪 `ProjectJson` 目录；该测试只读 `.csproj`。
- `CpgExecutionMatrixTests`（DOP=12 / persistence）：`System.IO.IOException`——共享
  `Build/test/**/NLISSN.Core.dll` 被并发构建中的 `testhost` 进程锁定。
- `DataFlowCandidateScanPruningTests` 的 5 条：**用对照实验证明与本项无关**。把工厂临时改回
  `ownsCollections:false`（M6 在行为上退化为改动前的复制路径），这组测试**仍然失败**（2/11）。
  对照脚本 `Build/MemoryOptimization/M6/20260925-m6/attribution-control.ps1` 在 `finally` 中
  恢复源文件并校验恢复后的 SHA256，日志 `attrib-noop-control-1.log`。
  ⚠️ 启用侧同时期另一工作流正在改 `DataFlowPass.cs` 与**该测试文件本身**
  （mtime 06:59:53 / 06:58:48，落在 A/B 窗口内），故两侧计数不可作有效 A/B 对比；
  可用的结论只有「关掉 M6 后这些失败依旧存在」。

### 7.6 边界（不得越读）

- 收益是**重复载荷物化消除**，**不是**整图峰值内存下降；未测端到端峰值、未跑 Performance 档。
- 未引入 `ArrayPool` / `IMemoryOwner` / Dispose 协议，未新增公开 bool 开关，
  未削弱 `availableAnchors` 端点校验（第 1 节"不采用"项全部保持不采用）。
- 接管契约按第 3 节末段的口径验证：**不做**"传入后再故意写数组"的反例，
  所有权靠**代码审查 + 多批次隔离测试**证明。

