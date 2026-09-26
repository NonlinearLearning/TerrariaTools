# DataFlow 位集压缩执行计划

> **状态：已执行完成（2026-09-22）。** Task 1–8 全部落地，结论见文末「执行结果」。
> 下文保留为执行前的原始计划，行号引用均指改动前的 `DataFlowPass.cs`。

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 把 `DataFlowPass` 的 reaching-definitions 位集从按流节点数 `N` 开位改为按定义数 `D` 开位，并消除 `inSets` 在 fixpoint 内的死存储，在不改变图语义、查询结果、DOP 结果和持久化格式的前提下把巨型方法的位集分配量降低 50–100 倍。

**Architecture:** 在 `AnalyzeCfgSensitivePartition` 内建立 `nodeOrdinal -> definitionOrdinal` 紧凑映射，把 `wordsPerSet` 的语义从 `ceil(N/64)` 改为 `ceil(D/64)`；`inSets` 不再作为 `N × words` 矩阵物化，改为收敛后按需由 `outSets` 重算。位集的置位、查询、枚举与转移全部改走定义序数；`outSets` 的迭代语义保持不变。度量字段 `WordsPerSet` 保留名称与位置、仅改取值语义，并在 `NLCPGDataFlowMethodMetrics` 增加优化前位宽以保住可观测性。

**Tech Stack:** C#/.NET 10、现有 NLCPG builder 与 `DataFlowPass`、xUnit Contract/Unit/Host tests、`Miscellaneous/scripts/Run-TestTiers.ps1`、`Build/Tools/Invoke-SerialDotnet.ps1`、既有 gcdump 诊断 harness。

**设计依据：** `docs/plans/2026-09-22-dataflow-bitset-compaction-design.md`

---

## 工作规则

- 工作目录 `D:\ProjectItem\SourceCode\Net\NL`；保留工作区中已有的无关改动。
- **每次构建或测试前**先检查活动的 `dotnet.exe` / `csc.exe` 进程。不得终止归属不明的 `dotnet` 进程（本次会话启动时已存在 9 个 19:59:25 启动的宿主进程，不属于本任务）。
- 所有可编译命令走串行包装器，避免并发构建争用：
  `pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 test <项目> --no-restore -p:UseSharedCompilation=false`
- **本任务是语义保持型重构**：图等价 oracle 未全绿前，不得声称任何优化完成。
- 每一档验证只报告**实际执行过**的命令与结果；不得把未执行的档位记为通过。
- 生成物只落 `Build\`；不得在源码旁写基准产物。
- 文档改动后运行 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1`
  （注意：若 `$env:TEMP`/`$env:TMP` 指向 `C:\WINDOWS\TEMP`，脚本第 127 行的
  `Remove-Item` 会因拒绝访问失败；先设成可写目录）并检查 `git diff --check`。

## 既有实现锚点

- 位集分配与 fixpoint：`src/NLCPG/Builder/Passes/DataFlowPass.cs:613-681`
- 候选边生成（读 `inSets`）：`src/NLCPG/Builder/Passes/DataFlowPass.cs:682-728`
- 位集 helper：`src/NLCPG/Builder/Passes/DataFlowPass.cs:890-979`
- 度量构造：`src/NLCPG/Builder/Passes/DataFlowPass.cs:793-810`
- 度量契约：`src/NLCPG/Builder/NLCPGBuildMetrics.cs:107-115`
- 选项契约：`src/NLCPG/Builder/NLCPGBuilderOptions.cs:150-157`
- 性能事实映射：`src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs:38-51`
- 性能摘要落盘：`src/NLISSN/Performance/PerformanceSummaryDocument.cs:476-484`
- 图等价 oracle：`tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:435-450, 521-542, 639-652, 821-838`
- 批次图等价：`tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs:79-88`
- `WordsPerSet` 既有断言：`tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:275`
- 诊断证据：`docs/benchmarks/nlissn-version4-dop12-diagnostics.md`

---

## Task 1: 先落盘 `DefinitionCount` 与 `FlowNodeCount`，量出真实 D 分布

**目的：** 设计文档的收益倍数是按假定 D 推算的。压缩前必须先把真实 `D` 量出来，
否则无法判断收益是否值得，也无法在压缩后做前后对比。

**Files:**
- 检查（可能无需改动）：`src/NLCPG/Builder/NLCPGBuildMetrics.cs`
- 检查：`src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs`
- 检查：`src/NLISSN/Performance/PerformanceSummaryDocument.cs`

**Step 1: 确认现有落盘链路是否已足够**

`NLCPGDataFlowMethodMetrics` 已含 `FlowNodeCount`/`WordsPerSet`/`DefinitionCount`
（`NLCPGBuildMetrics.cs:107-115`），且已映射到
`CpgDataFlowMethodPerformanceFact` 与 `PerformanceSummaryDataFlowDocument`。
先确认从 NLISSN 运行配置到性能摘要 JSON 的路径是否需要显式开关，
用一次小规模真实运行验证 `dataFlowMethodSamples` 是否出现在摘要里。
若已落盘，本 Task **只做测量**，不改代码。

**Step 2: 对 Version4 跑一次单文件与目录级测量**

用现成 harness（`D:\TRbackup\Version4\Build\NL-diagnostics-dop12\Invoke-NlVersion4Diagnostics.ps1`
或最小单文件 NLISSN 运行）产出性能摘要，读取每个方法的
`flowNodeCount` / `definitionCount` / `wordsPerSet`。

**Step 3: 记录 D/N 分布**

至少报告：D 的最大值、D/N 的分布（min/median/max）、有多少方法的 `N ≥ 818`
（现状 LOH 门槛）。把结果写入交接记录。

**验证标准：** 得到真实的 `D` 最大值与 `D/N` 比例；据此确认设计文档 §3.4
用的是哪个 D 档，并把推算倍数替换为实测倍数。

**注意：** 不得为了凑数改度量口径。若 D 数据显示 `D ≈ N`（即压缩无收益），
**应停止本计划并回到用户处报告**，而不是继续压缩。

---

## Task 2: 建立失败的模型测试（RED）

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowBitsetCompactionTests.cs`
- Test references: `src/NLCPG/Builder/Passes/DataFlowPass.cs`（内部实现，经 `NLCPGBuilder` 间接验证）

**Step 1: 写失败测试**

覆盖：

1. **位宽契约**：构造一个方法，其 `FlowNodeCount` 明显大于 `DefinitionCount`
   （例如大量字面量与二元表达式、少量赋值）。断言优化后
   `runMetrics.WordsPerSet == (runMetrics.DefinitionCount + 63) / 64`
   **且** `runMetrics.WordsPerSet < (runMetrics.FlowNodeCount + 63) / 64`
   （后者证明位宽确实按 D 而非 N 计算）。
2. **D 的边界**：`DefinitionCount == 0` 的方法（无任何定义）不得抛异常，
   且 `WordsPerSet == 1`（`BitSetWordCount` 的 `Math.Max(1, …)` 语义）。
3. **每个定义节点的可达性**：构造"定义 → 使用"跨基本块的方法，
   确认该使用点能连到定义点（等价于"压缩没有丢 bit"）。
4. **D = N 退化情形**：构造每个流节点都是定义的方法，确认结果与压缩前一致。

**Step 2: 运行定向测试，确认 RED**

```powershell
pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 test `
  .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~NLCPGDataFlowBitsetCompactionTests"
```

Expected: 断言 1 失败（当前 `WordsPerSet == ceil(N/64)`，不等于 `ceil(D/64)`）。
其余测试在压缩前应通过（它们描述的是不变量，压缩后必须仍通过）。

---

## Task 3: 把 `WordsPerSet` 既有断言与度量契约同步（先改契约，再改实现）

**Files:**
- Modify: `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:275`
- Modify: `src/NLCPG/Builder/NLCPGBuildMetrics.cs:107-115`
- Modify: `src/NLISSN.Application/Performance/CpgPerformanceFactMapper.cs:38-51`
- Modify: `src/NLISSN/Performance/PerformanceSummaryDocument.cs:476-484`

**Step 1: 更新既有断言**

把 L275 由

```csharp
Assert.Equal((runMetrics.FlowNodeCount + 63) / 64, runMetrics.WordsPerSet);
```

改为

```csharp
Assert.Equal((runMetrics.DefinitionCount + 63) / 64, runMetrics.WordsPerSet);
```

并在同一测试内补一条保留旧信息的断言（见 Step 2）。

**Step 2: 在度量里保留"优化前位宽"**

在 `NLCPGDataFlowMethodMetrics` 增加：

```csharp
int FlowNodeWordsPerSet,   // = ceil(FlowNodeCount/64)，压缩前的位宽
```

并把 `CreateDataFlowMetrics`（`DataFlowPass.cs:793-810`）与
`CpgPerformanceFactMapper`、`PerformanceSummaryDataFlowDocument`、
`NLCPGPerformanceFactMappingTests`（`tests/NLISSN.ContractTests/Cpg/NLCPGPerformanceFactMappingTests.cs:38-49`
的 8 参数构造）同步扩展。

**理由**：`docs/benchmarks/` 的诊断报告靠 `WordsPerSet` 复现"优化前会分配多少"。
改语义而不留旧值会让该数据不可复现。

**Step 3: 不改 schema 版本**

`PerformanceSummaryDocument.SchemaVersion` 保持 `1`
（`NLISSN.HostTests/Performance/PerformanceSummaryWriterTests.cs:21` 断言其为 1）。
在 `PerformanceSummaryDataFlowDocument.WordsPerSet` 的字段注释写明
"自 v1 起该值为 `ceil(DefinitionCount/64)`"，并在
`设计docs/目前设计/性能分析组件.md` 记录该语义变更。

**Step 4: 运行受影响的测试**

```powershell
pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 test `
  .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~NLCPGPerformanceFactMappingTests|FullyQualifiedName~PerformanceSummarySchemaTests"
```

Expected: 此时 Task 2 的断言 1 与更新后的 L275 **仍失败**（实现未改），
其余通过。这是预期的中间 RED 状态。

---

## Task 4: 实现位宇宙压缩（核心）

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`

**Step 1: 建立定义序数映射**

在 `AnalyzeCfgSensitivePartition` 中，`definitionFactsByNode` 构造完成后
（L572 之后）、位集分配之前（L618 之前），增加：

- `definitionOrdinals`（`int[]`，长度 `flowNodes.Length`，初值 `-1`）
- `definitionNodes`（`NLCPGNode[]`，长度 `definitionFactsByNode.Count`）
- 遍历 `flowNodes`，对 `definitionFactsByNode.ContainsKey(node)` 的节点
  依次分配紧凑序数。

要求：分配顺序必须**确定性**（按 `flowNodes` 的 ordinal 升序），
否则会破坏 `AssertGraphsEqual` 的跨 DOP 可复现性。

**Step 2: 改 `wordsPerSet` 计算**

L573 由

```csharp
var wordsPerSet = BitSetWordCount(plan.FlowNodes.Length);
```

改为

```csharp
var wordsPerSet = BitSetWordCount(definitionFactsByNode.Count);
```

注意 L573 位于 `MaxDefinitionsPerMethod` 检查（L575）**之前**，
所以 D 尚未被拒绝时即已可用，顺序无需调整。

**Step 3: 改置位与冲突判定（`ApplyDefinitionTransfer`）**

`DataFlowPass.cs:958-979`：

- 入参由"节点序数 + `definitionFactsByOrdinal`"改为"定义序数 + 定义事实数组"。
- 冲突遍历内层：bit 索引现在**就是**定义序数，直接索引
  `definitionFactsByOrdinal[definitionOrdinal]`，可删除
  `if (definitionOrdinal < definitionFactsByOrdinal.Count)` 的边界判断。
- 置位语句 L978 由 `output[nodeOrdinal / 64] |= 1UL << (nodeOrdinal % 64)`
  改为按定义序数置位；调用点（L657-665）需先查
  `definitionOrdinals[nodeOrdinal]`，为 `-1` 时**跳过整个转移调用**。

**Step 4: 改读取路径的寻址**

- `DefinitionFactIndex.AddReachable`（L216-232）与 `TryGetCandidates`（L178-198）
  的 `flowNodeOrdinals` 参数改为 `definitionOrdinals`；`IsBitSet`（L895-898）
  的 `ordinal` 现在是定义序数，`AddReachable` 里对候选节点
  先取 `definitionOrdinals[ordinal]`，为 `-1` 则跳过（该节点不可能是可达定义）。
- `EnumerateSetBits`（L939-956）的 `bitCount` 由 `flowNodes.Length` 改为
  `definitionNodes.Length`；调用点 L716 的 `flowNodes[reachingDefinitionOrdinal]`
  改为 `definitionNodes[reachingDefinitionOrdinal]`。

**Step 5: 保留 `outSets` 语义不变**

`OrBitSet`（L650）、`BitSetEquals`（L667）、`CopyBitSet`（L672）**不改**
——它们只搬运/比较 word，与位宽来源无关。

**Step 6: 运行 Task 2 + Task 3 的定向测试，确认 GREEN**

```powershell
pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 test `
  .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~NLCPGDataFlowBitsetCompactionTests|FullyQualifiedName~NLCPGPartitionedBuilderTests|FullyQualifiedName~CpgWorkBatchDataFlowTests"
```

Expected: 全绿。**这是本任务最重要的关卡**——图等价 oracle 全绿才证明语义未变。

---

## Task 5: 消除 `inSets`（方案 A）

**前置：** Task 4 必须已 GREEN。本 Task 是纯收益增量，独立可回退。

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs:618, 655, 682-728`

**正确性依据（先读懂再改）：** `inSets` 在文件中只有 5 处出现
（L618 分配、L655 写、L689/L698/L716 读，后三者全在 fixpoint 之后）。
L655 写入的值与 `incomingScratch` 逐字相同，且 worklist 不动点性质保证
"节点最后一次出队必在所有前驱 `out` 集定型之后"，因此收敛后
`in = OR(out[p] for p in preds)` 可无损重算。

**Step 1: 删除 `inSets` 分配与循环内复制**

- 删除 L618 的 `var inSets = new ulong[checked(flowNodes.Length * wordsPerSet)];`
- 删除 L655 的 `CopyBitSet(inSets, nodeOffset, incomingScratch, wordsPerSet);`

**注意**：L672 的 `CopyBitSet(outSets, nodeOffset, updatedScratch, wordsPerSet)` 与
L667 的 `BitSetEquals(outSets, …)` **必须保留**——`outSets` 仍是 fixpoint 的迭代状态。

**Step 2: 收敛后按需重算 in 集**

在第四阶段（L682-728）内，对每个被读的 operation 节点按需计算。
**必须逐字复刻 L645-652 的语义**（清零 → 并上所有在 `flowNodeOrdinals` 中的前驱的 `out` 集）：

```csharp
// v 的 in 集 = 其前驱 out 集的并（等价于 L645-652，但在收敛后按需计算）
Array.Clear(incomingScratch, 0, incomingScratch.Length);
foreach (var predecessorNode in plan.Predecessors[operationNodePair.Second])
{
    if (flowNodeOrdinals.TryGetValue(predecessorNode, out var predecessorOrdinal))
    {
        OrBitSet(incomingScratch, outSets, predecessorOrdinal * wordsPerSet, wordsPerSet);
    }
}
```

`plan.Predecessors` 是 `MethodDataFlowPlan` 的既有成员
（`DataFlowPass.cs:95`），L646 已在 fixpoint 中用同一写法，无需新增数据。

随后把该节点的读取改为直接用 `incomingScratch`（偏移 0）：

- L689 → `IsBitSetEmpty(incomingScratch, 0, wordsPerSet)`
- L698 → 传 `incomingScratch` 且 `reachingOffset` 传 `0`
- L716 → `EnumerateSetBits(incomingScratch, 0, definitionNodes.Length, wordsPerSet)`

**危险点（必须避免）**：重算与读取之间**不得**再调用任何会改写
`incomingScratch` 的逻辑；`incomingScratch` 此时已不再被 fixpoint 使用，
但第四阶段内若有多次读取同一节点，必须重算。L685 是单次 `Zip` 遍历，
每个 operation 节点只读一次，因此**不需要**额外缓存字典。

**Step 3: 在摘要中记录收益（可选）**

若 Task 1 已落盘度量，可在诊断产物里同时输出压缩前后的位集字节数，
便于 Version4 复测对比。

**Step 4: 运行完整 Contract 层**

```powershell
pwsh -File .\Miscellaneous\scripts\Run-TestTiers.ps1 -Fast
```

Expected: unit + contract 全绿。记录 `Build\TestResults\<runId>\run.json` 的
`exitCode` 作为证据，报告时贴出实际输出。

---

## Task 6: 补齐预算与溢出边界（防御性，独立可回退）

**前置：** Task 4/5 已 GREEN。

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs`（L618-619 的 `checked` 乘法）
- Modify: `src/NLCPG/Builder/NLCPGBuilderOptions.cs`（如需新增诊断字段）

**Step 1: 复核 `int` 溢出**

压缩后 `N * ceil(D/64)` 仍可能溢出（N 极大时）。确认 `checked` 是否
仍会抛；若需要更明确的诊断，改为在计算前用 `long` 检查并走
`NLCPGDataFlowOverflowReason.FlowNodeLimitExceeded`，而不是抛 `OverflowException`。

**Step 2: 复核 4 处显式设置 `MaxDefinitionsPerMethod` 的测试**

- `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs:95, 115`
- `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:660, 675, 692, 710`

逐个确认断言不依赖"位集与 D 无关"的旧行为；有依赖的必须显式更新并说明理由。

**Step 3: 运行定向测试**

```powershell
pwsh -File .\Build\Tools\Invoke-SerialDotnet.ps1 test `
  .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~DataFlowDefinitionBudget|FullyQualifiedName~DataFlowNodeBudget|FullyQualifiedName~DataFlowCandidateBudget"
```

Expected: 全绿。

---

## Task 7: 用实测证明内存下降（唯一可声称收益的证据）

**前置：** Task 4/5 全绿。**没有本 Task 不得声称任何倍数。**

**Files:**
- 复测产物：`Build\` 下的诊断运行目录（不得写源码旁）
- 证据落点：`docs/benchmarks/nlissn-version4-dop12-diagnostics.md`（新增对比小节）

**Step 1: 同一输入、同配置、同 runtime 做前后对比**

用与 `docs/benchmarks/` 相同的方法（`GcdumpExactStats`，
位于 `D:\TRbackup\NL-diag-tools\GcdumpExactStats`）对同一输入采样，
读取 `System.UInt64[]` 的 `TotalBytes`，与
`docs/benchmarks/data/version4-dop12-type-share-0016.csv` 的 2,324.3 MB 对比。

**Step 2: 记录构图耗时**

对比 `PerformanceStageId.CpgBuild` 的耗时，确认方案 A 的 in 集重算
**没有**造成明显 CPU 回退。若回退显著，回退 Task 5（保留 Task 4）。

**Step 3: 写证据**

在 `docs/benchmarks/nlissn-version4-dop12-diagnostics.md` 新增小节，
写明：输入、配置、runtime、采样方法、优化前/后 `System.UInt64[]` 字节数、
构图耗时对比。**只报告实际测到的数**。

**不得声称：** 未做本 Task 就写"内存下降 100 倍"。设计文档 §3.4 的倍数是
按假定 D 的推算，不是实测。

---

## Task 8: 文档收口

**Files:**
- Modify: `设计docs/目前设计/性能分析组件.md`（`WordsPerSet` 语义变更）
- Modify: `设计docs/目前设计/cpg-workbatch-concurrency.md`（若涉及数据流批次描述）
- Modify: `Context/progress.md`（只留仍影响当前工作的事实）
- Modify: `docs/plans/2026-09-22-dataflow-bitset-compaction-design.md`（状态改为已实现）

**Step 1:** 更新设计页，记录 `WordsPerSet` 自本次起为 `ceil(DefinitionCount/64)`。
**Step 2:** 把已完成事实收口进 `Context/progress.md`，替换过期的"优化方向"条目。
**Step 3:** 运行收口校验：

```powershell
$w = Join-Path $env:USERPROFILE 'AppData\Local\Temp\nl-check'
New-Item -ItemType Directory -Force -Path $w | Out-Null
$env:TEMP = $w; $env:TMP = $w
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check
```

Expected: `[check-harness-consistency] OK`、`git diff --check` 退出 0。

---

## 完成条件

全部满足才算完成：

1. Task 2 的模型测试与 Task 3 更新后的既有断言全绿。
2. 图等价 oracle 全绿：`NLCPGPartitionedBuilderTests` 的 5 个 DataFlow 跨 DOP 测试
   + `CpgWorkBatchDataFlowTests.BuildFromSource_BatchedDataFlow_PreservesGraphAcrossDegreesOfParallelism`。
3. `Run-TestTiers.ps1 -Fast`（unit + contract）退出码 0，且有 `run.json` 证据。
4. Task 7 给出**实测**的 `System.UInt64[]` 前后对比与构图耗时。
5. `check-harness-consistency.ps1` OK 且 `git diff --check` 退出 0。
6. 未修改 `PerformanceSummaryDocument.SchemaVersion`，HostTests 的性能摘要断言仍通过。

## 回退策略

- Task 5 独立可回退：恢复 `inSets` 分配与 L655 复制即可，Task 4 的收益保留。
- Task 3 的度量字段新增若造成下游编译面过大，可先只做 `WordsPerSet` 语义切换
  并把旧值延后到独立任务，但**必须先更新 L275 断言**否则 CI 必红。
- 整个计划在 Task 4 未 GREEN 前不得进入 Task 5/6/7。

---

## 执行结果（2026-09-22 完成）

**改动文件**

| 文件 | 状态 | 内容 |
| --- | --- | --- |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | 已改 | 位宽 `N→D`、删除 `inSets`、`long` 溢出保护、按需重算 in 集 |
| `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowBitsetCompactionTests.cs` | 新增 | 4 个模型测试（位宽跟 D、`D=0` 下界、跨块可达、跨 DOP 等价）|
| `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs` | 已改 | L275 断言改为 `ceil(DefinitionCount/64)` |
| `docs/benchmarks/nlissn-version4-dop12-diagnostics.md` | 已改 | 新增「落地与实测」小节 |
| `设计docs/目前设计/性能分析组件.md` | 已改 | 新增第 14 节，记录 `WordsPerSet` 语义变更 |
| `Context/progress.md` | 已改 | 收口本次事实 |

**Task 1 的 D 分布（真实数据，此前只有推算）**：巨型方法 `D/N` 中位数为
**0.0606–0.0965**；`Terraria.NPC.AI()` 为 `N=122,536 / D=7,451`，
单个方法的 `inSets`+`outSets` 达 **3.50 GiB**。

**Task 7 实测（同输入/同配置/同 runtime，基线为临时改回旧位宽的对照构建）**

| 输入 | 分配总量 Δ | 位集倍数 |
| --- | ---: | ---: |
| `Main.cs` | −93.6 MB | 15.36× |
| `Player.cs` | −43.6 MB | 22.56× |
| `WorldGen.cs` | −1,079.4 MB | 25.93× |

构图耗时无显著回退（`Main.cs` +1.8%、`Player.cs` −0.8%，均在噪声内），
即优化②的 in 集重算没有可测量的 CPU 代价。

**验证**

- `Run-TestTiers.ps1 -Fast`（Unit 117 + Contract 430）**退出码 0，0 失败**。
- 图等价 oracle 全绿（含 5 个 DataFlow 跨 DOP 测试）。
- `PerformanceSummaryDocument.SchemaVersion` 未改动，HostTests 摘要断言仍通过。

**计划外的重要发现（既有缺陷，与本次优化无关）**

全量 Contract 套件在 xUnit **默认集合并行**下每次会随机失败 9–14 项，
失败集每次不同，且包含完全不依赖数据流的测试（如语法 pass 快照）；
**单独运行则全绿**。根因是测试隔离缺陷：`NLCPGDisplayTextTests` 与
`NLCPGProjectExportTests` 调用进程级 `Directory.SetCurrentDirectory()`，
而 `NLCPGNodeIdContractTests` 等传入**相对路径**（如 `"nodeid-stability.cs"`），
`Path.GetFullPath` 会按被其它测试改掉的 CWD 解析，导致稳定锚点从
`D:\…\nodeid-stability.cs` 变成 `C:\WINDOWS\TEMP\…\<random>\…`，抛
`Stable anchor for '…' was not included in the supplied preallocated NodeId table`。

**最小复现**：只跑 `NLCPGDisplayTextTests` + `NLCPGNodeIdContractTests`（22 项）
即复现 2 项失败；单独跑 `NLCPGNodeIdContractTests`（16 项）全绿。
以 `xunit.runner.json` 设 `parallelizeTestCollections: false` 后，
全量 **430/430 通过**（该文件已删除，`Build/` 本来被 gitignore）。

**建议的后续修复**（未在本计划范围内实施）：把上述测试的相对路径改为基于
测试程序集目录的绝对路径，或给改 CWD 的测试加 `[Collection]` 串行约束。

**Host 层另有 7 项既有失败（全部与本次优化无关）**

`Run-TestTiers.ps1 -HostTier` 报 664 通过 / 7 失败。逐项核对后确认 **7 项中 0 项
涉及 DataFlow 或 `WordsPerSet`**，且全部落在规则/传播层：

| 失败测试 | 性质 |
| --- | --- |
| `RuleStructureContractTests.StructuralKind_ContainsOnlyApprovedStructureConclusions` | **确定性**；`StructuralKind.MethodDeletion` 在 `HEAD` 中即为枚举首成员且该文件零未提交改动，测试的"已批准清单"漏了它 |
| `TestCodeSetCoverageTests`（3 例） | 规则/传播层未完成工作 |
| `PipelineComponentTests`（2 例） | 规则/传播层未完成工作 |
| `PropagationRuleExpansionTests`（1 例） | 规则/传播层未完成工作 |
| `WorkspaceInputLoaderTests`（全量跑红） | **单独跑 19/19 全绿**，属上条测试隔离问题 |

这些失败所依赖的 `MarkLiftingEngine.cs`、`UnreachableMethodMarkRule.cs`、
`PropagationFixedPointExecutor.cs` **都带未提交改动**，属工作区中他人进行中的工作。
本次改动只触及 `DataFlowPass.cs` 与两个测试文件，
**未修改 `src/NLISSN.Rules`、`src/NLISSN.Core/Lifting`、`src/NLISSN.Core/Propagation` 下任何文件**。
