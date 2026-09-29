# ④ 死私有成员与重复边界边构造清理执行文档

日期：2026-09-27。性质：**已执行**（本轮改了产品代码与测试，并运行了定向测试）。

> ✅ **执行状态：已完成。** `IsInside`、`CrossShardEdgeCommitter` 均已删除，
> 边界边字段抽取已收敛为 `CpgFrozenBoundaryEdge.FromEdge`；反射测试已改写为直接调用**并加强**判别力。
> 定向核心子集 **78/78 通过**。详见 §6 执行结果。

**目标：** 清除两类**没有行为贡献**的代码：

1. `CpgShardBuildCoordinator` 中**全仓库零调用**的私有方法 `IsInside`；
2. 生产中**未接线**、且其逻辑**已被复制多份**的
   `CrossShardEdgeCommitter.Create`，并把重复的
   「`NLCPGEdge` → `CpgFrozenBoundaryEdge` 字段抽取」收敛为一处。

**权威状态：** [feature_list.json](../../Context/feature_list.json) 是唯一来源。本切片**尚未登记**。

> 📌 **行号基准**：`src/NLCPG/Builder/CpgShardBuildCoordinator.cs`（425 行）；
> `src/NLCPG/Builder/Streaming/CrossShardEdgeCommitter.cs`（23 行）；
> `src/NLCPG/Persistence/CpgFrozenShardExporter.cs`（239 行）。
> 引用**同时给出符号名**，行号漂移时以符号为准。

> **与③的分工**：本文件**不**处理存储字段；③删的是只写不读字段
> （见 [③ 只写不读存储字段清理](2026-09-27-dead-storage-field-cleanup-execution.md)）。
> 两者**分开合入**，以便红/绿可归因。

---

## 0. 结论摘要

| 对象 | 位置 | 证据 | 判定 |
| --- | --- | --- | --- |
| `CpgShardBuildCoordinator.IsInside` | `:392-396` | 全仓库精确匹配**仅定义行**；`IsInsideKernelWorkItem` 是同名前缀的无关方法 | **删**（零风险） |
| `CrossShardEdgeCommitter.Create` | `CrossShardEdgeCommitter.cs:9-22` | 生产**零调用方**；唯一调用方是**反射测试** `CpgShardContractTests.cs:348-366` | **删类型 + 改测试**（§2.2） |
| 同体字段抽取逻辑 | **4 处**：`Create` `:11-21`、`CpgFrozenShardExporter.cs:58-68`、`:142-152`、`CpgShardBuildCoordinator.cs:136-146` | 逐字段同序、同表达式 | **收敛为 1 处**（§2.3） |

> ⚠️ **风险不对称**：`IsInside` 是**纯删除**（`private static`，编译器可完全验证）；
> `CrossShardEdgeCommitter` 涉及**删除一个类型**并**修改一个测试**，风险明显更高。
> ⇒ **强烈建议拆成两次提交**，先 `IsInside` 后 `CrossShardEdgeCommitter`。

---

## 1. 逐项证据

### 1.1 `IsInside`（`CpgShardBuildCoordinator.cs:392-396`）

```
392: private static bool IsInside(Model.NLCPGNode node, TextSpan span)
393: {
394:   return node.SpanStart.HasValue && node.SpanEnd.HasValue &&
395:     node.SpanStart.Value >= span.Start && node.SpanEnd.Value <= span.End;
396: }
```

检索口径：`src/`、`tests/`、`tools/` 下全部 `*.cs`（排除 `obj/`、`bin/`、`tests/Build/` 语料），
对 `IsInside` 做**完整标识符**匹配，命中仅此一处。同名前缀的两个无关私有方法位于 HostTests：

- `DeepParallelFacilityInventoryTests.cs:176,186`（`IsInsideKernelWorkItem`）
- `ParameterShrinkScanNestingTests.cs:256,262`（`IsInsideKernelWorkItem`）

它们在**测试程序集**内是私有方法，**不可能**调用 `NLCPG` 程序集内的 `private static`。

`IsInside` 是 `private static` ⇒ **不可能**被外部程序集或反射以外的任何路径调用；
又因其为 **`private`**，反射测试要调用它必须写出字符串 `"IsInside"`——已检索，无命中。

**删除方案**：删除 `:392-396` 及其后的空行。

> **`using` 影响（已核对，无孤儿）**：删除后
> - `TextSpan` 仍被 `:85`、`:344`、`:418` 使用 ⇒ `using Microsoft.CodeAnalysis.Text`（第 3 行）**保留**；
> - `Model.` 限定名仍被 `:344`、`:419` 使用 ⇒ **无** `using` 受牵连。
>
> ⇒ 本次删除**不产生**任何孤儿 `using`，也**不**需要改动 `using` 区。

### 1.2 `CrossShardEdgeCommitter.Create`（`CrossShardEdgeCommitter.cs:9-22`）

```
 9: internal static CpgFrozenBoundaryEdge Create(NLCPGEdge edge)
10: {
11:   return new CpgFrozenBoundaryEdge(
12:     edge.SourceNodeId.Value,
13:     edge.TargetNodeId.Value,
14:     edge.Kind.ToString(),
15:     edge.StructuredLabel?.StableKey,
16:     edge.ContextId?.Value,
17:     edge.CallSiteContext?.FilePath,
18:     edge.CallSiteContext?.SpanStart,
19:     edge.CallSiteContext?.SpanEnd,
20:     edge.CallSiteContext?.DisplayName,
21:     CpgFrozenFlowSummaryLabel.From(edge.StructuredLabel));
22: }
```

生产路径**零调用方**（`src/` 内无 `CrossShardEdgeCommitter.` 引用）。
唯一调用方是反射测试 `CpgShardContractTests.cs:348-366`：

```
351: var committerType = assembly.GetType("NLCPG.Builder.Streaming.CrossShardEdgeCommitter");
353: var create = committerType!.GetMethod("Create", Static | NonPublic);
360: var boundary = Assert.IsType<CpgFrozenBoundaryEdge>(create!.Invoke(null, new object[] { edge }));
362: Assert.Equal((uint)2, boundary.SourceNodeId);
363: Assert.Equal((uint)7, boundary.TargetNodeId);
364: Assert.Equal("input.cs", boundary.CallSiteFilePath);
365: Assert.Equal(3, boundary.CallSiteSpanStart);
```

> **注意**：该测试的**行为断言是有效的**（它验证了字段抽取的正确性），
> 只是**通过反射**访问了 `internal` 类型。这也是它必须被**改写**而非删除的原因
> ——见 §2.2。

### 1.3 四处同体重复（逐字段比对）

| # | 位置 | 源对象 | 目标类型 |
| --- | --- | --- | --- |
| (a) | `CrossShardEdgeCommitter.cs:11-21` | `edge` | `CpgFrozenBoundaryEdge` |
| (b) | `CpgFrozenShardExporter.cs:58-68` | `item.Candidate`（`CpgEdgeCandidate`） | `CpgFrozenBoundaryEdge` |
| (c) | `CpgFrozenShardExporter.cs:142-152` | `edge`（`NLCPGEdge`） | `CpgFrozenEdge` |
| (d) | `CpgShardBuildCoordinator.cs:136-146` | `edge`（`NLCPGEdge`） | `CpgFrozenBoundaryEdge` |

四处**逐字段同序、同表达式**：`SourceNodeId`、`TargetNodeId`、`Kind.ToString()`、
`StructuredLabel?.StableKey`、`ContextId?.Value`、`CallSiteContext?.{FilePath,SpanStart,SpanEnd,DisplayName}`、
`CpgFrozenFlowSummaryLabel.From(StructuredLabel)`。

细分类别：

- (a) 与 (d) 是**同一函数**（源类型、目标类型、表达式全同）——**真正的重复**。
- (c) 目标类型是 `CpgFrozenEdge`（用 `LocalIndex` 而非全局 NodeId），
  且第 1 个实参是 `localIndexes[edge.SourceNodeId]` ⇒ **只是字段清单同形**，不是可直接合并的重复。
- (b) 的源是 `CpgEdgeCandidate`（描述符阶段，尚无 `NLCPGEdge`）⇒ 同样**只同形**。

⇒ **可判定的真重复只有 (a)/(d) 一对。** (b)/(c) 的"同形"是数据结构决定的，
**不应**为此强行抽象（会把三个不同的源类型塞进一个接口，得不偿失）。

---

## 2. 改动清单

### 2.1 删除 `IsInside`（低风险，建议单独提交）

| 文件 | 删除内容 |
| --- | --- |
| `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` | `:392-396` 方法体 + 其后空行 |

约 −6 行，无新增，无 `using` 变化。

### 2.2 删除 `CrossShardEdgeCommitter`（中风险，**必须同步改测试**）

1. **删除** `src/NLCPG/Builder/Streaming/CrossShardEdgeCommitter.cs` 整个文件（23 行）。
2. **改写** `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs:347-366`：
   把 `Create_CrossShardEdge_PreservesGlobalNodeIdsAndCallSiteContext` 的**反射调用**
   改为对**收敛后唯一实现**的直接调用（§2.3 的 `CpgFrozenBoundaryEdgeFactory.Create`
   或其他选定的落点）。
   - **保留全部 4 条断言**（`:362-365`）——它们是有价值的字段契约；
   - 移除 `GetType`/`GetMethod`/`Invoke` 三处反射胶水（`:350-356`、`:360`）。
3. 改写后该测试应变为**直接、编译期检查**的调用，**判别力不下降**
   （仍断言同样 4 个字段）。

> ⚠️ **为什么必须改测试而不是留着类型**：该类型在生产中零调用，
> 保留它等于为一个"只有测试用"的方法维持一个生产类型。
> 若判定"反射测试本身也是该 API 的唯一契约"，则可选择**保留类型但删反射、改直接调用**
> ——**两条路都可行**，实施时二选一并**明确记录选择理由**。
> **不得**既保留类型又把测试改成直接调用后**不**记录（那会留下"为什么留着"的疑问）。

### 2.3 收敛 (a)/(d) 为唯一实现

把字段抽取下沉为 `CpgFrozenBoundaryEdge` 上的一个静态工厂（或 `CpgFrozenShardExporter`
内的 `internal static` 方法——**位置选择需与现有代码组织一致，实施时确认**）：

```
internal static CpgFrozenBoundaryEdge FromEdge(NLCPGEdge edge)  // (a)/(d) 共用
```

- `CpgShardBuildCoordinator.cs:136-146` 的 `.Select(edge => new CpgFrozenBoundaryEdge(…))`
  改为 `.Select(CpgFrozenBoundaryEdgeFactory.FromEdge)`；
- (a) 的调用方在 §2.2 中已按同一落点改写。

**(b)/(c) 不动**（§1.3 已说明理由）。

### 2.4 **不**要顺手做的改动

| 诱惑 | 阻止理由 |
| --- | --- |
| 把 (a)/(b)/(c)/(d) 全都抽象到一个接口后合并 | (b)/(c) 的源类型不同（`CpgEdgeCandidate` vs `NLCPGEdge`）且 (c) 用 `LocalIndex`，强行抽象会引入无用接口层 |
| 把 `CpgFrozenBoundaryEdge` 改成类/加基类 | 它是**值类型契约**，`NLCPGNodeIdContractTests.cs:178-206` 一类测试锁定了内部载体的值类型性 |
| 顺手删 HostTests 里的 `IsInsideKernelWorkItem` | **不在本项范围**；那两个是测试内的活代码（`:176`/`:256` 有调用） |
| 顺手做①③的改动 | 分切片合入，见各文件的边界章节 |

---

## 3. 验证门禁

### 3.1 既有 oracle（**必须全绿，不得修改期望值**）

| 测试 | 位置 | 锁住什么 |
| --- | --- | --- |
| `Create_CrossShardEdge_PreservesGlobalNodeIdsAndCallSiteContext` | `CpgShardContractTests.cs:347-366` | **本项直接改写的测试**：4 条字段断言必须原样保留 |
| `ExportDescriptors_PreallocatedLocalNodes_…` | `:248-299` | (b) 路径的边界边与节点/边产出不变 |
| `Export_FrozenGraph_PreservesOrderedNodeIdsAndEdges` | `:224-237` | (c) 路径节点序与边种类 |
| `WriteAsync_BoundaryEdgeManifest_PreservesGlobalNodeIds` | `:43-71` | **边界边清单**全局 NodeId——(d) 路径的端到端 oracle |
| `BuildFromSource_StreamingPersistence_PublishedShardsRestoreBaseGraphAndBuilderRestoresFullGraph` | `CpgShardBuildCoordinatorTests.cs:754` | 协调器全链路（含 `:131-147` 的边界边构造） |
| `InternalCpgDomainCarriers_AreNonPublicValueTypes` | `NLCPGNodeIdContractTests.cs:178-206` | `CpgFrozenShardGraphFacts` 等仍为非公开值类型 |
| `NLCPGBuilder_DoesNotRetainGlobalOperationToNodeCaches` | `:254-262` | 既有"不要保留某字段"的结构断言写法——**本项新增断言的模板** |

### 3.2 反射耦合核查（**必须做**）

删除/改写 `CrossShardEdgeCommitter` 前，确认它是**唯一**按名字符串引用该类型的点：

| 检索式 | 期望结果 |
| --- | --- |
| `GetType("NLCPG.Builder.Streaming.CrossShardEdgeCommitter")` | 仅 `CpgShardContractTests.cs:351` |
| `"CrossShardEdgeCommitter"` 字面量（`*.cs`，排除 `obj/`/`bin/`/`Build/`） | 仅上述一处 + 源码自身 |
| `GetMethod("Create", Static\|NonPublic)` | 仅 `:353` |

> ⚠️ **`GetMethod("Create", Static|NonPublic)` 的匹配缺陷**：不指定参数类型时，
> 若目标类型有**多个**名为 `Create` 的重载，`GetMethod` 会抛
> `AmbiguousMatchException`。删除该类型后此风险自然消失；但在**改写**时
> **不要**把同样的无类型反射写法搬到别处。

### 3.3 拟新增门禁（**新增**）

1. **`CpgShardBuildCoordinator_HasNoDeadGeometryHelpers`**（新增，可选）
   —— 反射取该类型全部 `DeclaredOnly|Instance|Static|NonPublic` **方法**，
   断言不含名为 `IsInside` 的方法。
   （仿 `NLCPGNodeIdContractTests.cs:254-262` 的写法。）
   **若判定为过度约束（`IsInside` 只是恰好死掉，不是被禁止的设计），可不加并记录原因。**
2. **`BoundaryEdgeFactory_MapsAllGlobalFieldsFromEdge`**（新增，**替代**原反射测试的判别力）
   —— 对 `NLCPGEdge`（含完整 `CallSiteContext` 与 `StructuredLabel`）断言
   10 个字段**逐个**映射正确。原测试只断言了 4 个（`:362-365`）；
   改写时应**至少保持**这 4 个，**建议补齐**到全部字段——
   否则"删反射"这个动作会让判别力**下降**。
3. **`CpgFrozenBoundaryEdgeConstruction_HasSingleImplementation`**（不建议加）
   —— "只有一处 `new CpgFrozenBoundaryEdge(`"这类**文本级**断言属脆弱测试
   （格式变动即误红）。**建议不加**，靠代码评审保证。

### 3.4 变异检验（必做）

1. **`IsInside` 部分**：对 `:394-395` 的边界条件做变异（`>=` → `>`），
   然后跑全套——**预期全部仍绿**（因为无人调用）。
   ⇒ 这正是"死代码"的**证明**：一个**改不红任何测试**的方法，就是死代码。
   恢复原样。
2. **边界边工厂部分**：把 `FromEdge` 中 `edge.CallSiteContext?.SpanStart` 改为 `SpanEnd`
   （或漏传 `ContextId`），确认 §3.1 中至少
   `Create_CrossShardEdge_…`（改写后）与
   `WriteAsync_BoundaryEdgeManifest_PreservesGlobalNodeIds` 变红，然后**还原**。
   ⚠️ **若只红一条甚至不红**，说明边界边清单测试对字段变化不敏感，
   **必须补测试**而不是放行。

### 3.5 命令

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardContractTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CpgShardBuildCoordinatorTests'
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~NLCPGNodeIdContractTests'
```

**编译警告必须为零新增**；删类型后若出现 `CS0649`/`CS0414`，说明还有未清理的赋值或引用。

---

## 4. 收益口径

| 项 | 结论 |
| --- | --- |
| `IsInside` | **0 字节内存**收益；收益是**代码尺寸**（约 −6 行）与"改不红测试"的误导面 |
| `CrossShardEdgeCommitter` | **0 字节内存**收益；收益是消除一个"只有测试用"的生产类型，约 −23 行 |
| (a)/(d) 收敛 | **0 字节内存**收益；收益是**消除真实重复**（同一函数两份），约 −12 行 |

> ⚠️ **本项是纯可维护性收益，不得声称任何内存或性能改善。**
> 与①/②/③不同，本项**不改变**任何运行时分配行为。

**合格结论形式**："删除了 1 个零调用私有方法与 1 个未接线类型，
并把 1 处真实重复的双份实现收敛为单份；无行为变化。"

---

## 5. 本文件不得越读的边界

1. 本轮**已改产品代码并运行定向测试**（见 §6）；"零调用"由**编译器 + 定向测试**支持，
   而**不是**仅靠静态检索。
2. **静态检索是必要不充分证据**：反射按名字符串、`dynamic`、源生成器
   均可能构成检索不到的调用。§3.2 列出**已发现的**反射点；
   无法证明"不存在未知反射调用"。
3. §4 明确**不主张**任何内存/性能收益。
4. (b)/(c) 的"同形"**未被**判为重复；本文件**不**建议为它们引入抽象。
5. 已知**既有**失败**不得**归因于本项（见 §5.1）。
6. 本切片**尚未登记**进 [feature_list.json](../../Context/feature_list.json)；
   本文件不构成验收证据，也**不**修改任何 feature 状态。

### 5.1 需与既有失败区分

以下失败在**改动前**即存在，**不得**作为本项红/绿判据：

- `CpgInterproceduralEdgeOrderTests.…PreservesBridgeEmissionOrder`
  —— 在未改动的工作树上**字节级相同**地失败（`156:167` vs `167:178`）。
- `LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph`
  —— 由工作树中**未跟踪**的 `src/NLISSN.Infrastructure/ProjectJson/` 引起。

⇒ 实施时**先记录基线**（改动前的失败集合），再与改动后比对，
只有**新增**失败才归因于本项。

### 5.2 工作树注意事项

改动前工作树已含大量未提交修改与未跟踪备份文件
（如 `src/NLCPG/Model/CanonicalEdgeStore.T1T2-GOOD.bak`、
`src/NLCPG/Model/NLCPGGraphIndex.T1T2-GOOD.bak`）。

⇒ **禁止** `git reset --hard`、`git clean` 或整文件覆盖；
回退本项时只应用**本项自身的反向补丁**或删除**本项新增**的内容。

---

## 6. 执行结果（本轮实测）

### 6.1 改动

| 文件 | 净变化 |
| --- | --- |
| `src/NLCPG/Builder/Streaming/CrossShardEdgeCommitter.cs` | **整文件删除**（23 行） |
| `src/NLCPG/Persistence/CpgShardContracts.cs` | +19 行（`FromEdge` 工厂） |
| `src/NLCPG/Builder/CpgShardBuildCoordinator.cs` | −18 行（`IsInside` + 内联构造替换为方法组） |
| `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs` | +73 行（1 个反射测试 → 4 个直接调用测试） |
| `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs` | +46 行中的一部分（`IsInside` 守卫） |

### 6.2 与计划的差异（需如实记录）

1. **重复份数的修正**：计划 §1.3 统计为 **4 处**同体字段抽取。实施时发现**第 5 处**——
   `SkeletonShardPublisher.cs:263` 亦为同形构造。它与其他三处的源类型同为 `CpgEdgeCandidate`
   （非 `NLCPGEdge`），故按 §1.3 的同一理由**不纳入收敛**。计划中的"4 处"应更正为 **5 处**。
2. **收敛范围**：只收敛了 (a)/(d) 这一对**真重复**（源 `NLCPGEdge`、目标 `CpgFrozenBoundaryEdge`），
   与 §2.3 一致。(b)/(c)/(e) 三处保持原样。
3. **`ExportDescriptors` 的 `boundaryEdges` 未改**：它写入调用方的 `ICollection<CpgFrozenBoundaryEdge>`
   （`CpgFrozenShardExporter.cs:58`），源为 `CpgEdgeCandidate`，非本项范围。

### 6.3 测试改写的判别力**提升**（而非持平）

原反射测试只断言 **4** 个字段（`SourceNodeId`/`TargetNodeId`/`CallSiteFilePath`/`CallSiteSpanStart`）。
改写后为 **4 个测试**：

| 测试 | 覆盖 |
| --- | --- |
| `FromEdge_CrossShardEdge_PreservesGlobalNodeIdsAndCallSiteContext` | 原 4 个断言**原样保留** |
| `FromEdge_CrossShardEdge_MapsEveryPersistedField` | 全部 10 个字段 |
| `FromEdge_UnlocatedEdge_LeavesOptionalFieldsNull` | 可空字段的 null 路径 |
| `FromEdge_FlowSummaryBridge_PreservesTypedSummaryLabel` | `FlowSummaryLabel` 的类型化桥接 |

⇒ 判别力**上升**，符合 §3.3-2 的"至少保持、建议补齐"要求。

### 6.4 测试暴露的真实领域不变量（**重要**）

`FromEdge_CrossShardEdge_MapsEveryPersistedField` 首轮**失败**：

```
System.ArgumentException : CallSiteContext must derive the same ContextId when both are provided.
   at NLCPG.Model.NLCPGEdge..ctor(...) NLCPGEdge.cs:line 16
```

原因【源】：`NLCPGEdge` 的公开构造函数（`src/NLCPG/Model/NLCPGEdge.cs:9-26`）**强制**
`contextId == callSiteContext.ToContextId()`（`ContextId` 由 `CallSiteContext` **派生**）。
我原先自造了一个任意的 `"context-1"`，违反该不变量。

修正：改用 `callSite.ToContextId()` 作为 `contextId`。
⇒ **这不是产品缺陷**，是测试数据构造错误；本项**未**改动 `NLCPGEdge`。
这条不变量应被后续任何构造 `NLCPGEdge` 的测试记住。

### 6.5 编译证据

```
build src/NLCPG/NLCPG.csproj        → 0 警告, 0 错误
build tests/NLISSN.ContractTests    → 0 错误, 21 警告（全部为既有 xUnit 分析器规则）
```

删除类型后**无** `CS0649`/`CS0414` 残留 ⇒ 无未清理的赋值或引用。
`CrossShardEdgeCommitter` 在全仓库（`src/`、`tests/`、`tools/`，排除 `obj/`/`bin/`/`Build/`）
**已零命中**。

### 6.6 测试证据（定向 10% 核心子集）

```
--filter CpgShardContractTests|NLCPGNodeIdContractTests|CpgPersistenceStateTests
  → 已通过! 失败 0, 通过 44, 总计 44

--filter CpgShardBuildCoordinatorTests
  → 已通过! 失败 0, 通过 34, 总计 34

合并 4 个过滤器（终态确认）
  → 已通过! 失败 0, 通过 78, 总计 78, 耗时 51 s
```

覆盖本项最强 oracle：
`ExportDescriptors_PreallocatedLocalNodes_ExportsLocalEdgesAndReturnsBoundaryEdges`（(b) 路径）、
`WriteAsync_BoundaryEdgeManifest_PreservesGlobalNodeIds`（边界边端到端）、
`BuildFromSource_…RestoreBaseGraphAndBuilderRestoresFullGraph`（协调器边界边构造）。

### 6.7 变异检验（判别力已证，§3.4 要求）

| # | 变异 | 结果 |
| --- | --- | --- |
| 1 | `FromEdge` 中把 `SpanStart` 错传为 `SpanEnd` | `…MapsEveryPersistedField` **与** `…PreservesGlobalNodeIdsAndCallSiteContext` **双双变红** ✅ |
| 2 | 把 `CpgCatalogBatchWriter._maxQueueDepth` 加回（③的守卫） | `StreamingWriters_DoNotRetainWriteOnlyPublicationLogs` **变红** ✅ |
| 3 | 把 `IsInside` 加回 `CpgShardBuildCoordinator` | `CpgShardBuildCoordinator_HasNoDeadSpanHelper` **变红** ✅ |

全部已还原并复测绿。⇒ §3.3 的三条新增守卫**均非恒真断言**。

**未做** §3.4-1 设想的"对 `IsInside` 边界条件变异预期全绿"检验——
该检验的目的是**证明它是死代码**；本项直接**删除**它并由**编译器**证明无调用方（§6.5），
证据强度更高，故不再保留该方法做变异。**此处偏离计划 §3.4-1，理由如上。**

### 6.8 未验证边界

1. **未做任何内存采样**：§4 已声明本项不主张内存/性能收益；本轮**未**测量，保持该结论。
2. **未跑全量测试**：仅四个类共 78 例。HostTests、UnitTests、PerformanceTests **未运行**。
3. **未验证**未知反射调用：`CrossShardEdgeCommitter` 已零命中，但"不存在未知反射"仍不可证。
4. §5.1 的既有失败**本轮未观测**（未运行到那两个类）。
5. `Context/feature_list.json` **未被修改**（未登记本切片）。
