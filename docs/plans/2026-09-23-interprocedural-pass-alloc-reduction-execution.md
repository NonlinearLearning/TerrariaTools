# 跨过程 pass 分配削减执行计划（方案 B + A）

> 配套设计：[设计文档](2026-09-23-interprocedural-pass-alloc-reduction-design.md)。
> 本文记录**已实际执行**的步骤、**实测证据**与验证边界。
> 状态：**已实施并通过验证**（2026-09-23）。

## 1. 前置门禁

| 门禁 | 内容 | 状态 |
| --- | --- | --- |
| G1 | 边序 oracle 存在且改动前通过 | **已满足** —— `CpgInterproceduralEdgeOrderTests` 冻结原始插入序 |
| G2 | 串行构建/测试跑法确认 | **已满足** —— `Build/Tools/Invoke-SerialDotnet.ps1` + `TEMP` 改指 `Build/tmp` |
| G3 | `xunit.runner.json` 位于**输出目录** | **已满足** —— `Build/test/Debug/net10.0/xunit.runner.json`，`parallelizeTestCollections: false` |
| G4 | 回滚基线快照 | **已满足** —— `Build/tmp/taskA-B/NLCPGBuilder.before.cs`、`NLCPGGraph.before.cs` |

**改动前基线哈希**：

| 文件 | SHA256 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `1797D1AE82C11DE78C1DFC419CB6DD4570249A59F3D18B3AD87B9D7AD27B199C` |
| `src/NLCPG/Model/NLCPGGraph.cs` | `BC391424C2B5CC58A868B5492340FB2546B9EEE0D5520A52F7F8F45F9A70F8F4` |

## 2. 改动清单

### 阶段 B —— 消 `ToContextId()` 重复插值（零语义风险）

| # | 文件:位置 | 改动 |
| --- | --- | --- |
| B1 | `NLCPGBuilder.cs` `PublishCallSitePlans` 尾部 | `callSiteContext.ToContextId(), callSiteContext` → `callSiteContext: callSiteContext` |
| B2 | `NLCPGBuilder.cs` `AddExternalSummaryMappings` | `context.ToContextId(), context` → `callSiteContext: context` |

**等价性**：`AddEdge` 只把参数存入 `_pendingEdges`（`NLCPGGraph.cs:327-344`），不构造 `NLCPGEdge`。`PendingEdge.ContextId` 变为 `null`，但 `CallSiteContext` 保留；消费侧经 `CpgEdgeCandidate`（`SkeletonShardPublisher.cs:389-398`）进 `NLCPGEdge` 构造器时，由 `callSiteContext?.ToContextId()` **算出同一值**。⇒ 最终 `NLCPGEdge.ContextId` 逐字节相同。

### 阶段 A —— 消 `PendingEdges` 全量物化（低风险）

| # | 文件:位置 | 改动 |
| --- | --- | --- |
| A1 | `NLCPGGraph.cs` `PendingEdgeBuffer` | 新增 `EnumerateLazily(nodesByOrdinal)`：`yield return`，**不分配** `PendingEdge[]` |
| A2 | `NLCPGGraph.cs` `NLCPGGraph` | `PendingEdges` 属性（每次访问全量物化）→ `EnumeratePendingEdgesLazily()` 惰性方法 |
| A3 | `NLCPGBuilder.cs:726` | `graph.PendingEdges` → `graph.EnumeratePendingEdgesLazily()` |

**A2 顺带删除了死代码**：改完 A3 后，`internal IReadOnlyCollection<PendingEdge> PendingEdges` 属性在**全仓（`src` + `tests`）已无任何调用者**（另 2 处 `PendingEdges` 命中属 `MutableGraphFacts`，是不同类型）。该属性**每次访问都全量物化**，留着等于把刚消除的隐患重新挂回门口 ⇒ **删除**。`NLCPG` 项目无 `InternalsVisibleTo`，删除后 `0 警告 / 0 错误` 即证明无调用者。

**保留不动**：`Materialize`（`SnapshotMutableFacts`、`RemapToNodeIds`、`SkeletonShardPublisher` 仍需要数组）。

**同序论证**：`EnumerateLazily` 遍历的是**同一个 `_keys` HashSet**，与 `Materialize` 的枚举顺序**逐项相同**。

## 3. 实测证据（A/B 对照，同输入同配置）

用**文件追加探针**直接计数，而非估算。测试为 `CpgInterproceduralEdgeOrderTests`（4 个用例，5 次构图，每次 74 条跨过程边 ⇒ 370 条）。

### B 的证据 —— `ToContextId` 调用次数

| 状态 | 调用次数 | 说明 |
| --- | --- | --- |
| **改动前** | **814** | `74 + 2×370` |
| **改动后** | **444** | `74 + 1×370` |
| **差值** | **370 = 恰好每条边 1 次** | ✅ 每条边少插值一次 |

> 74 = 其他非本 pass 路径的调用；370 = 5 次构图的跨过程边总数。差值精确等于"每条边 1 次"。

### A 的证据 —— `Materialize` 调用

| 状态 | 调用次数 | 元素数 | 总量 |
| --- | --- | --- | --- |
| **改动前** | **10** | 848×5 + 922×5 | 8,850 |
| **改动后** | **5** | 922×5 | 4,610 |
| **差值** | **−5 次 / −4,240 元素** | | ✅ 本 pass 那份 922 元素全量数组消失 |

> 改动后仅剩 `SnapshotMutableFacts` 等的 5 次 922 元素物化（**另一条路径，本计划范围外**）。本 pass 自有的那份**已归零**。

## 4. 验证矩阵（全部已执行）

| 层 | 命令 | 结果 |
| --- | --- | --- |
| `NLCPG` 构建 | `dotnet build src/NLCPG/NLCPG.csproj` | **0 警告 / 0 错误** |
| 边序 oracle + 新增守卫 | `--filter CpgInterproceduralEdgeOrderTests` | **4/4 通过** |
| 定向（含 `CpgWorkBatchInterproceduralTests`） | `--filter CpgInterproceduralEdgeOrderTests\|CpgWorkBatchInterproceduralTests` | **7/7 通过** |
| `Cpg` + `NLCPG` 契约 | `--filter Cpg\|NLCPG` | **378/384**（6 项失败，**均为既有** `NLCPGProjectExport*`） |
| Unit 层 | `NLISSN.UnitTests` | **117/117 通过** |
| Contract 全量 | `NLISSN.ContractTests` | **452/458**（同上 6 项既有失败） |

**既有失败排除**：6 项全部是 `NLCPGProjectExport{Export,Persistence,Concurrency}Tests`，错误均为 `NLISSNWS012: The project contains an unresolved metadata reference.`（`MsBuildWorkspaceInputLoader.cs:488`），发生在**加载 MSBuild 工作区阶段**、**早于任何图构建**，且这些文件在 git 中为 **untracked**（另一代理的在制品）。**本改动不涉及该路径。**

## 5. 反向验证（mutation test，证明守卫真的有效）

只跑正例不足以证明测试有牙齿。因此**故意注入回归**：把 `NLCPGEdge.cs` 的
`var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;`
临时改为 `var resolvedContextId = contextId;`

**结果（符合预期）**：

- 新增守卫 `..._DeriveContextIdFromCallSiteContext` **失败**：`Assert.All() Failure: 74 out of 74 items ... Assert.NotNull() Failure`
- 边序 oracle `..._PreservesBridgeEmissionOrder` **同时失败**（`ContextId` 进 span 键）
- **74/74 条边**丢失 `ContextId` ⇒ 反证"每个边确实各插值一次"，与 §3 的 370 次差值互相印证

**随后已还原**：`MUTATION-TEST` 残留计数 = **0**。

## 6. 新增守门测试

`tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`
新增 `BuildFromSource_InterproceduralEdges_DeriveContextIdFromCallSiteContext`：

- `Assert.NotNull(edge.CallSiteContext)` 且 `Assert.NotNull(edge.ContextId)`
- `Assert.Equal(edge.CallSiteContext.Value.ToContextId(), edge.ContextId.Value)`

护住"`ContextId` 由 `CallSiteContext` 派生"这一不变量，防止将来误删派生或漏传 `CallSiteContext`。

## 7. 回滚点

| 回滚粒度 | 操作 |
| --- | --- |
| 只回 B | 恢复两处 `xxx.ToContextId(), xxx` 实参 |
| 只回 A | `EnumeratePendingEdgesLazily()` → `PendingEdges`（惰性成员可保留不删） |
| 全回 | 用 `Build/tmp/taskA-B/*.before.cs` 覆盖，并核对 §1 哈希 |

## 8. 明确不做的事

- **不**改 `NodeSortKey`（零分配方案风险更高，见设计 §2.3）。
- **不**启用 `DataFlowOptions` 预算（会改持久化指纹，属产品决策）。
- **不**把 `OrderBy` 换成 `Array.Sort`（不稳定，会破边序）。
- **不**删 `NLCPGEdge` 构造器的 `ToContextId()` 派生（是 `contextId: null` 时的唯一来源）。

## 9. 验证边界（诚实声明）

- ✅ **已实测**：B 的调用次数差（814→444）、A 的物化次数差（10→5）、构建、4 个测试层的通过数、mutation test。
- ⚠ **未实测**：**真实 71 s 快照上的字节收益**。本计划的 MiB 数字（B 1,061.5 MiB、A 641.4 MiB）来自**既有诊断运行**，是**收益上界**而非本次复现值；本次只有**调用次数**这一线性代理指标。
- ⚠ **不声称**降低内存峰值或消除 LOH：`PendingEdge[]` 单数组 LOH 阈值仅 **313 元素**（`ceil(85000/272)`），改后**仍会进 LOH**；A 消除的是"额外多分配一整份"，B 消除的是重复临时字符串。
- ⚠ **未验证**：`EnumerateLazily` 要求"枚举期间不改图"。已按代码确认为真（索引循环在发布之前，且 `_interproceduralBarrierCompleted` 已置位），但**未用运行时断言**强制该前提；若将来有人在索引循环中插入 `AddEdge`，会抛 `InvalidOperationException` 而非静默出错。
- ⚠ 另一个代理的在制品（`NLCPGEdge` 由 `sealed record` 改 `readonly record struct`、`NLCPG.ProjectExport/`）**非本计划改动**，其 6 项失败与本计划无关。

## 10. 参考

- 设计文档：[2026-09-23-interprocedural-pass-alloc-reduction-design.md](2026-09-23-interprocedural-pass-alloc-reduction-design.md)
- `Context/progress.md` —— 分配归因原始数据
- `docs/plans/2026-09-23-zero-allocation-key-design.md` —— 后续可选批次（`NodeSortKey`）
- `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs` —— 边序 oracle + 新增 `ContextId` 守卫
