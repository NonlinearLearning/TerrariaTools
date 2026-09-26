# M7 SparseSet 无效合并与单次 spill 分配 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 让不改变集合的 Union 不分配，改变且溢出的 Union 只分配一次最终 spill，保留数组不可变共享。

**Architecture:** 从现有内联槽/不可变 spill 直接读取有序元素；第一遍合并只计算结果基数，未改变就返回，改变时第二遍直接写新内联值和最终 spill。所有输入在写入前捕获，填完后一次发布，绝不修改共享旧 spill。

**Tech Stack:** C# / .NET、现有 SparseSetStore、排序序数集合、ContractTests 与受控分配实验。

---

日期：2026-09-25。feature：`sparse-set-union-allocation`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置和门槛见 [总索引](2026-09-25-memory-optimization-execution-index.md)。本轮只交付执行文档。

## 1. 取舍与成本

| 方案 | 决定 | 理由 |
| --- | --- | --- |
| 空集/自合并/不变结果早返回 | 采用 | 避免高频无效合并的临时分配，逻辑局部 |
| 计数一遍、直接写最终 spill 一遍 | 采用并测耗时 | 消除 merged 和 source/target 堆缓冲，不增加长期 scratch |
| 每 store 保留最大合并缓冲 | 不采用 | 难以证明长期保留净收益，巨型方法会拉高容量 |
| ArrayPool、可变共享 spill、调整 InlineSlotCount | 不采用 | 分别增加生命周期、别名或另一项存储决策 |
| 重写 transfer/fixpoint 或自适应集合框架 | 不采用 | 超过本项 1–2 人日局部成本 |

本项优先级低于前面大载荷与生命周期问题。不复做已完成的定宽位集稀疏化。

## 2. 当前依据与关键约束

[DataFlowPass.cs](../../src/NLCPG/Builder/Passes/DataFlowPass.cs) 的 `SparseSetStore.Union` 在两个输入都非空时
分配 merged；ReadInto 的大输入还分配 source/target 数组，WriteFrom 溢出时再次分配 spill。
Copy 当前直接共享不可变 spill 引用，必须维持；另一个槽不能因目标合并而变化。

InlineSlotCount 当前为 1，StackBufferCapacity 为 8。继续保留现有内联宽度，不以本项顺带调参。
集合元素严格升序且唯一；定义序数不是流节点序数。候选的预算/遍历依赖该顺序。
WriteFrom 还被 ApplyDefinitionTransfer 使用，本项不能为消除 Union 的第二次复制而破坏 transfer 的共享约束。

## 3. 具体算法

1. 保留 sourceCount=0 返回、targetCount=0 调 Copy；增加 targetSlot==sourceSlot 返回。
2. 在任何写入之前读取两侧 count、内联值和 spill 引用。两侧可能是不同槽但共享同一个 spill；按只读输入处理。
3. 第一遍有序归并仅计算 `unionCount`。并集必为 target 的超集，因此 unionCount==targetCount 时结果完全不变，可直接返回，不复制 spill。
4. 若 unionCount==sourceCount，说明 target 是 source 的子集，可以调用原 Copy；这同样保留不可变共享。
5. 其他情况只分配 `int[unionCount - InlineSlotCount]`（需要溢出时）；第二遍合并将第一个元素写局部内联结果，其余直接写最终新 spill。
6. 完成所有读取/填充后，更新 target 的 inline/count/overflow 引用；不调用会再复制 spill 的 WriteFrom。
7. 只读取已捕获的旧输入，避免边写 target inline 边读同一槽造成读写别名。旧 spill 永远不被写入。

当前 k=1 可用显式局部标量保存两侧 inline；若实现时 k 已变化，则先复核成本和读取模型，不能假装硬编码仍通用。
读取第 i 个元素的逻辑是 `i==0 ? capturedInline : capturedSpill[i-1]`；使用私有值方法或局部静态方法，避免接口枚举和闭包分配。
基数计算与数组长度使用可检查的整数运算；不改变输入合法性和预算语义。

两遍归并增加只读比较次数，其代价必须计入实验。若更高基数夹具耗时不合格，则只交付快速返回子补丁并保留单次 spill 子项未验收，不能强行上线复杂 scratch 管理。

## 4. Task 1：集合语义与共享别名测试

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs` 中 SparseSetStore；不改候选索引。
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowSparseSetTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowBitsetCompactionTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/SparseSetUnionAllocationTests.cs`。
- Fixtures: `tests/NLISSN.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs`。

1. 冻结真实 BuildFromSource 的循环、汇合、ref/out、多参数溢出与预算边界输出，确保夹具实际触发 spill。
2. 增加独立集合序列 oracle，覆盖空/单元素、相等、包含、相交、不交、自合并、基数 1/2/8/9 和较大输入。
3. 对 Copy 后共享 spill 的 A/B 两槽，合并 A 后验证 B 的完整升序内容仍不变；同 spill 不同 inline 的组合也覆盖。
4. 用简单 SortedSet<int> 做测试侧数学 oracle，比较完整序列；不复制生产归并算法进测试。
5. 默认继续经公开构图验证行为。精确分配测试若需私有存储入口，可将原嵌套类提升为 internal（保留在原位置）并使用现有 ContractTests 友元；不新增公开 API、反射逐次调用或广泛抽取通用库。

内部表示测试只负责可达分配/共享约束，不能替代完整构图契约。现有注释称“无 InternalsVisibleTo”可能滞后，以当前 Properties/AssemblyInfo.cs 为准。

## 5. Task 2：先落早返回，再落最终缓冲

1. 先加自合并与计数判定的不变返回；保持旧发生变化的写回路径，单独运行语义和分配对照。
2. 捕获只读 inline/spill 输入，取消 Union 的 ReadInto 临时数组；两遍归并直接写最终 spill。
3. 保留 Copy/WriteFrom/ApplyDefinitionTransfer 其他契约；新 helper 只服务 Union，避免顺带重写全部集合操作。
4. 跑共享别名、transfer 后再 union、union 后再 copy 的交错序列，检查旧引用内容不变。
5. 每个子补丁单独留回退点，最终产品不保留性能开关和另一份 Union 正常路径。

## 6. Task 3：分配与耗时验收

受控同步实验预先创建 store 和输入，预热后测操作本体；夹具重建/清理不能计入每次 Union 的收益。
必须既测重复不变合并，也测每次都增加集合的合并，防止只测快速路径。

拟定结构门槛：

- 空、自合并、结果不变、可 Copy 的包含关系路径：稳态每次 Union 新增堆分配为 0。
- 需要新溢出结果的路径：只创建一份精确长度 spill，不存在 merged/source/target 中间数组。首次 `_overflow` 字典及扩容是额外成本，单列并计入冷态实验，不能伪称总共永远只分配一个对象。
- 共享源 spill 从不修改，结果严格升序/去重，完整图与预算行为不变。
- 在真实小夹具和较高基数压力夹具分别记录 union 次数、累计分配、fixpoint 阶段耗时，满足共同 ≤5% 劣化门槛。
- 如果真实夹具几乎没有非空双输入合并，记录适用范围有限，不把微基准结果推成整体 GC 收益。

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SparseSetUnionAllocationTests|FullyQualifiedName~NLCPGDataFlowSparseSetTests|FullyQualifiedName~NLCPGDataFlowBitsetCompactionTests|FullyQualifiedName~CpgWorkBatchDataFlowTests'
```

先执行总索引构建。M5 若已经完成，以其验收后的工作树为基线；两项逐个执行，不混合修改或重复计算内存收益。

## 7. 执行结果（2026-09-25）

结论：**通过**。空/自合并/不变/可 Copy 路径稳态零分配，改变且溢出的并集只创建一份精确长度 spill；
语义、完整图与预算等价。详细证据与复现命令见
[Build/MemoryOptimization/M7/r1/EVIDENCE.md](../../Build/MemoryOptimization/M7/r1/EVIDENCE.md)。

### 7.1 语义与分配

- 定向 4 类：PRE `17/17`、POST `41/41` 通过（新增 24 例，含 2 条读写别名定向用例）。
- 受控分配账本（隔离镜像 A/B，仅差 `Union` 一处；store 构造与播种均在计时区间外）：

  | 路径 | PRE B/op | POST B/op |
  | --- | --- | --- |
  | 源空 / 目标空 | 0 / 0 | 0 / 0 |
  | 自合并 | 104 | **0** |
  | 结果不变 | 120 | **0** |
  | target ⊂ source（Copy） | 120 | **0** |
  | 改变且溢出 | 88 | **40** |

  POST 的 40 B 逐字节等于 `int[unionCount - InlineSlotCount]`，即只有一份最终 spill；
  溢出字典的首次创建与扩容单列为独立成本，未声称"永远只分配一个对象"。

### 7.2 耗时

- 算子级 ABBA 交错（24 样本中位数，附 A/A 空对照）：6 条路径全部满足 ≤5% 门槛，
  其中 `SelfUnion` −95.7%、`UnchangedResult` −65.5%、`ChangedOverflow` −54.0%。
- 真实夹具 Fixpoint 相位：9 个夹具中 8 个在 ±13% 内且符号在轮次间翻转；
  `NestedBranches48` 一轮 +12.4%（绝对值 +0.04 ms）。
  **本机（常驻 17 个 dotnet 进程）A/A 噪声底达 ±28%，单方法相位仅 0.10–0.54 ms，
  真实夹具相位耗时不可分辨**——按 §6 要求如实记录，不用微基准结果冒充整体收益。

### 7.3 适用范围（§6 要求）

ledger 插桩统计显示：9 个小夹具中**只有 `JoinLoop4x4` 触达非空双输入并集（8/108）**，
其余全部走"源空"或"目标空"（`Clear` + 单前驱填充）。
刻意追加的 `DiamondChain40`、`MergeSwitch48`、`ManyMethods64` 三个汇合点密集夹具
仍未能触发该路径。故**本项收益在小夹具上不构成整体 GC 收益**；
需要多方前驱汇合的大型真实代码库才能体现，该占比未被本轮证据覆盖。

### 7.4 环境说明

本轮共享工作树被其他工作流并发编辑，`NLCPGBuilder.cs` 多次处于编译中间态。
全部 A/B 与账本测量均在 `Build/` 下**隔离镜像**中完成（`ab/{pre,post,ledger}`），
两镜像只差 `Union` 一处，因此结论不依赖共享工作树的瞬时状态。
`Run-TestTiers.ps1 -Fast` 的 17 项 ContractTests 失败全部属于其他工作流在建特性
（`InterproceduralPlanCompactionTests`、`DataFlowCandidateScanPruningTests`、
`LocalCpgFragmentContractTests`、`LayoutArchitectureTests`），无一属于本项测试类。

