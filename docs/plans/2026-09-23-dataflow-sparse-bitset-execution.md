# DataFlow 位集稀疏化执行计划

> **状态：待执行（2026-09-23）。** 本计划尚未开始；下文行号均指**当前**
> `DataFlowPass.cs`（前序位集压缩已完成、工作区未提交状态）。
> 设计期已完成三轮只读实测（含真实分配测量），收益基线见设计 §3.2。

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 把 `DataFlowPass` 的 reaching-definitions 集合从每节点定宽稠密位集
（`N × ceil(D/64)` 个 `ulong`）改为每节点内联稀疏集合（`N × (1+k)` 个 `int`，`k=1`），
在保持图语义、候选边、DOP 结果与持久化格式完全不变的前提下，
把**全量**方法的集合存储降低 **32.94×**（实测，含溢出开销），
并把复杂度从 `Θ(N·D/64)` 降到 `Θ(N·k)`。

**Architecture:** 在 `AnalyzeCfgSensitivePartition` 内用 `int[] _setCounts` +
`int[] _setOrdinals`（SoA，每节点 `k=1` 个内联槽位）替换 `ulong[] outSets`；
序数**升序无重复**存储，溢出走 lazy `Dictionary<int,int[]>`（绝不截断）。
六个位集操作改为稀疏等价实现，`WordsPerSet` 作为**逻辑位宇宙宽度**保持不变
（仅用于度量与预算，不再决定存储）。收敛后用稀疏并集重算 in 集，第四阶段按序枚举。

**Tech Stack:** C#/.NET 10、`NLCPG` builder 与 `DataFlowPass`、xUnit
Contract/Unit 测试、`Miscellaneous/scripts/Run-TestTiers.ps1`、
`Build/Tools/Invoke-SerialDotnet.ps1`。

**设计依据：** [DataFlow 位集稀疏化设计](2026-09-23-dataflow-sparse-bitset-design.md)

---

## 工作规则

- 工作目录 `D:\ProjectItem\SourceCode\Net\NL`；**保留工作区中已有的无关改动**
  （当前约 60 个未提交文件，含 `MarkLiftingEngine.cs`、
  `UnreachableMethodMarkRule.cs`、`PropagationFixedPointExecutor.cs` 等他人进行中的工作）。
- **本任务是纯表示层语义保持型重构。** 图等价 oracle 未全绿前，
  **不得**声称任何优化完成或任何倍数。
- 所有可编译命令走串行包装器，避免并发构建争用：

  ```powershell
  $dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
    '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
    '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
  & .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
  ```

  **注意**：不要把 `-p:` 参数直接内联进 `Invoke-SerialDotnet.ps1` 命令行，
  会触发 `Parameter cannot be processed because the parameter name 'p' is ambiguous`。

- **构建/测试前**先把 `$env:TEMP`/`$env:TMP` 指到可写目录。
  默认的 `C:\WINDOWS\TEMP` 不可写，会导致 `check-harness-consistency.ps1`
  第 127 行 `Remove-Item` 报"拒绝访问"：

  ```powershell
  $env:TEMP = 'D:\ProjectItem\SourceCode\Net\NL\Build\tmp'; $env:TMP = $env:TEMP
  ```

- 生成物只落 `Build\`；不得在源码旁写基准产物。
- 每一档验证只报告**实际执行过**的命令与结果；不得把未执行的档位记为通过。
- `DataFlowPass.cs` 改前先备份哈希，便于回退核验。

### 本次改动的既有实现锚点（当前行号）

| 内容 | 位置 |
| --- | --- |
| 集合分配与 fixpoint | `src/NLCPG/Builder/Passes/DataFlowPass.cs:596-717` |
| 第四阶段（重算 in + 枚举） | `src/NLCPG/Builder/Passes/DataFlowPass.cs:718-772` |
| 位集 helper 群 | `src/NLCPG/Builder/Passes/DataFlowPass.cs:935-1023` |
| `FactsMatch` / `FactsConflict` | `src/NLCPG/Builder/Passes/DataFlowPass.cs:1025-1073` |
| `DefinitionFactIndex.AddReachable` | `src/NLCPG/Builder/Passes/DataFlowPass.cs:216-235` |
| 候选收集器与预算 | `src/NLCPG/Builder/Passes/DataFlowPass.cs:124-154` |
| 预算触顶返回空边集 | `src/NLCPG/Builder/Passes/DataFlowPass.cs:857-880` |
| 度量契约 | `src/NLCPG/Builder/NLCPGBuildMetrics.cs:107-115` |
| 数据流选项与默认值 | `src/NLCPG/Builder/NLCPGBuilderOptions.cs:150-157` |
| `WordsPerSet` 既有断言 | `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs:276` |
| 前序模型测试 | `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowBitsetCompactionTests.cs` |
| 图等价 oracle | `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`（5 个 DataFlow 跨 DOP） |
| 批次等价 oracle | `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchDataFlowTests.cs:79-88` |

**关键正确性前提（不得违反，共 6 条，详见设计 §3.3）**：

1. **双射**：稀疏表示与定宽位集一一对应，可互相还原。
2. 溢出**外溢**，绝不截断（截断=静默丢序数靠后的定义）。
3. 序数**严格升序、无重复**（顺序影响候选预算路径，进而整方法丢边）。
4. `SetEquals` **逐元素精确**比较（近似比较=fixpoint 提前收敛）。
5. `count == 0` 是合法高频状态（实测 93.9% 的 out-set 为空）。
6. 溢出**不得**写入 `NLCPGDataFlowOverflowReason`。

---

## Task 1: 实测中途基数，确认单调性推论

**目的：** 设计 §3.2 的 `k=1` 已由**真实分配测量**裁定（不再是推算），
§3.4 的单调性推论是**结构推导、尚未实测**。
本 Task 只补这一个缺口：**验证 fixpoint 中途的基数不会超过收敛后的基数**。
若不成立，`k=1` 会在迭代中途被大量击穿，溢出率远高于实测的 0.28%，需重新选 `k`。

**Files:**
- 临时探针：`Build\tmp\dfkprobe\`（**不得**留在源码树；`Build/` 已被 gitignore）
- 备份：`Build\tmp\dfkprobe\DataFlowPass.cs.orig`

**Step 1: 备份并记录当前哈希**

```powershell
cd D:\ProjectItem\SourceCode\Net\NL
New-Item -ItemType Directory -Force -Path 'Build\tmp\dfkprobe' | Out-Null
Copy-Item 'src\NLCPG\Builder\Passes\DataFlowPass.cs' 'Build\tmp\dfkprobe\DataFlowPass.cs.orig' -Force
(Get-FileHash 'src\NLCPG\Builder\Passes\DataFlowPass.cs' -Algorithm SHA256).Hash
```

Expected: `278F1C005923CA9508535903430EB7043891B45447C3E2E7B869F989552E6557`

**Step 2: 插入 env 门控只读探针**

在 `AnalyzeCfgSensitivePartition` 的 fixpoint 结束后、第四阶段之前
（当前 L717 的 `}` 之后、L718 注释之前）插入一行调用：

```csharp
ProbeSparse(MethodFullName: plan.MethodFullName, ...);
```

探针本体（`NL_DFPROBE=1` 才执行，**只读**）必须测四件事：

1. **收敛后基数分布**：每方法 `cardMax`（复现设计 §2.3）。
2. **`k` 候选的溢出率**：`cardMax > 1` / `> 2` / `> 4` 的**节点数**与占比
   （设计 §3.2 已用真实分配测量给出 `k=1` 时 0.28% 节点溢出）。
3. **中途基数峰值**：在 fixpoint 循环内维护一个
   `midIterationMaxCardinality` 计数器，每次 `CopyBitSet` 后统计当前 `updatedScratch` 的
   `PopCount` 之和并取最大值。**收敛后与中途分别输出**。
   ——这是本 Task 唯一尚未实测的量。
4. **`distinct` 与 `D+1` 是否相等**（复现设计 §2.1），
   以及 `singletons + multiCard == D`（设计 §2.2）。

**Step 3: 构建并运行**

```powershell
$env:TEMP='D:\ProjectItem\SourceCode\Net\NL\Build\tmp'; $env:TMP=$env:TEMP
$dotnetArgs = @('build','.\src\NLCPG\NLCPG.csproj','--no-restore','-m:1','-nr:false',
  '-p:UseSharedCompilation=false','-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

输入：`D:\TRbackup\Version4\Terraria\{Player,Main,WorldGen,NPC}.cs`，
配置 `NLCPGBuilderOptions.CreateDefault()` + `MaxDegreeOfParallelism = 1`。

**Step 4: 记录并对账**

必须逐项报告（设计中的值来自 §5.1 已有的三次实测）：

| 待确认 | 设计中的值 | 实测值 |
| --- | --- | --- |
| `cardMax ≤ 2` 占比 | 75.3%（1106/1469） | ? |
| `cardMax ≤ 4` 占比 | 94.2%（1384/1469） | ? |
| `cardMax ≤ 8` 占比 | 99.5%（1461/1469） | ? |
| 全局 `cardMax` | 12 | ? |
| `distinct == D+1` 成立率 | 1469/1469 | ? |
| `singletons + multiCard == D` 成立率 | 1469/1469 | ? |
| 节点级溢出率（`k=1`） | 0.28%（2414/870249） | ? |
| **中途基数峰值 ≤ 收敛基数** | 推论（§3.4）**未实测** | ? |

**Step 5: 还原并核验**

```powershell
Copy-Item 'Build\tmp\dfkprobe\DataFlowPass.cs.orig' 'src\NLCPG\Builder\Passes\DataFlowPass.cs' -Force
(Get-Item 'src\NLCPG\Builder\Passes\DataFlowPass.cs').LastWriteTime = Get-Date
(Get-FileHash 'src\NLCPG\Builder\Passes\DataFlowPass.cs' -Algorithm SHA256).Hash
Select-String -Path 'src\NLCPG\Builder\Passes\DataFlowPass.cs' -Pattern 'DFPROBE|ProbeSparse'
```

Expected: 哈希回到 Step 1 的值；残留检索**无输出**。

**验证标准：** 四文件真实数据上得到 `cardMax` 分布、节点级溢出率，以及
**中途基数峰值与收敛基数的关系**（后者是本 Task 唯一的新信息）。

**停止条件（重要）：** 若实测显示**中途基数显著高于收敛基数**，
则 §3.4 的推论被推翻，`k` 不能按收敛 `cardMax` 选取。
此时**停止本计划并回报**，改为按中途峰值 + 安全裕量定 `k`，重新评估收益。

**注意：** 探针只读；不得为了让数字好看而改统计口径。
设计 §3.2 的内存收益已由真实分配测量确认，本 Task **不是**重新论证收益，
而是补齐"中途基数"这一个缺口。

---

## Task 2: 写回归护栏测试（非 RED→GREEN）

**Files:**
- Create: `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowSparseSetTests.cs`
- 参考：`tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowBitsetCompactionTests.cs`（前序测试的写法与 fixture）

**约束：** `src/NLCPG` **没有** `InternalsVisibleTo`（全仓 17 处，`src/NLCPG` 零处），
且 `ArchitectureBoundaryTests` 对程序集边界有既有断言 ⇒
**不得新增 `InternalsVisibleTo`**。测试只能经 `NLCPGBuilder` 公开 API +
`LastBuildMetrics` 观测。

**Step 1: 写测试**

覆盖以下 6 条（对应设计 §3.3 的 6 条不变式，但只能从外部观测）：

1. **溢出不截断（最关键）**：构造一个方法，使其某程序点的 out-set 基数
   **恰好为 `k+1` = 2**（`k=1`），且这 2 个定义**都**能被下游使用点匹配到。
   断言该使用点连到**全部 2 个**定义节点。
   —— 这是唯一能捕获"截断"的测试，必须真实构造，不能只测 `k` 以内的情况。
   构造方式：同一基本块内对**2 个不同的 BaseKey** 各赋一次值，
   随后在同一块内使用这 2 个变量（`FactsConflict` 只在同 `LocationKey`/同 `BaseKey`
   时 kill，故不同变量互不消灭）。
   **注意 `k=1` 使这条测试的临界值降到 2**，因此它比 `k=4` 时更容易构造，
   也更容易被真实语料触发（设计 §2.2：47.0% 的方法存在基数 ≥ 2 的集合）。
2. **空集高频**：`DefinitionCount == 0` 的方法不抛异常且 `WordsPerSet == 1`。
3. **`WordsPerSet` 语义不变**：
   `WordsPerSet == (DefinitionCount + 63) / 64`（逻辑宽度，**不**随稀疏化改变）。
4. **跨块可达**：定义与使用跨基本块时 DataFlow 边仍存在。
5. **跨 DOP 等价**：`MaxDegreeOfParallelism` 1/4/12/16 下图快照一致。
6. **预算路径不误触发**：给一个正常方法设
   `MaxCandidateEdgesPerMethod` 为**足够大**的值，
   断言 `OverflowReason == None` 且图与不设预算时一致
   （守住"顺序偏差不改变 RawCandidateCount 触顶时机"）。

**Step 2: 运行定向测试，确认基线为绿**

```powershell
$dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
  '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false',
  '--filter','FullyQualifiedName~NLCPGDataFlowSparseSetTests')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

Expected: **本类测试在当前（稠密）实现上应当全部通过**——
因为稠密位集本来就是正确的参照实现。

**诚实说明**：稀疏化是**纯内部表示变化**，
**没有"外部可见的新行为"可以 TDD**。因此本任务的真实作用是
**回归护栏**（保证重构不破坏语义），而非 RED→GREEN 驱动。
**不要在报告里假称"测试先失败再通过"。**
真正能捕获实现错误的手段是 Task 5 的差分验证。

---

## Task 3: 实现稀疏集合类型与六个操作

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs:935-1023`（位集 helper 群）

**Step 1: 定义内联稀疏集合结构（SoA）**

```csharp
private const int InlineSlotCount = 1;   // 实测裁定，见设计 §3.2

private sealed class SparseSetStore
{
    // 始终分配
    internal int[] Counts { get; }        // int[N]
    internal int[] Ordinals { get; }      // int[N * InlineSlotCount]

    // 仅在发生溢出时分配（lazy）：绝大多数方法为 null
    internal Dictionary<int, int[]>? Overflow { get; private set; }

    internal int OverflowNodeCount { get; private set; }
}
```

**必须用 lazy `Dictionary<int,int[]>`**，不得用 `List<int>?[]`：
后者对 `NPC.AI()`（`N=122,536`）需额外 `N×8 = 980,288 B`，占预算 40%（设计 §3.1）。

**Step 2: 实现六个操作的稀疏等价（严格对照设计 §3.6 表）**

| 操作 | 实现要点 |
| --- | --- |
| `OrInto(dst, src)` | **有序归并去重**：两路升序游标，相等只取一次；超 `k` 走溢出 |
| `Copy(dst, src)` | 计数 + `Array.Copy` 槽位 + 溢出深拷贝（**不得共享数组引用**） |
| `SetEquals(a, b)` | 先比 `count`，再**逐元素**比序数。**禁止**用 `HashSet.SetEquals` 之类引入顺序无关的近似 |
| `IsEmpty(set)` | `count == 0` |
| `Enumerate(set)` | `yield` 升序槽位，再升序溢出项 ⇒ 全局升序 |
| `Contains(set, ordinal)` | 内联槽位**二分查找**，未命中再查溢出 |

**关键**：所有插入路径都必须保持**升序 + 无重复**（不变式 2）。
建议在 `#if DEBUG` 下加 `Debug.Assert` 校验升序与唯一性，Release 下不保留。

**Step 3: 构建**

```powershell
$dotnetArgs = @('build','.\src\NLCPG\NLCPG.csproj','--no-restore','-m:1','-nr:false',
  '-p:UseSharedCompilation=false','-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

Expected: 0 个错误。

---

## Task 4: 接入 fixpoint 与第四阶段

**Files:**
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs:596-717`（分配与 fixpoint）
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs:718-772`（重算 in + 枚举）
- Modify: `src/NLCPG/Builder/Passes/DataFlowPass.cs:216-235`（`AddReachable` 的成员测试）

**Step 1: 替换分配**

- 删除 `var outSets = new ulong[(int)bitSetLength];`
  （当前 L663）与其 `bitSetLength` 溢出检查（L644-660 区域）。
- 改为构造 `SparseSetStore(flowNodes.Length)`。
- **保留** `bitSetLength` 的 `long` 溢出保护逻辑中所做的
  `MaxFlowNodesPerMethod` 预算检查（L617-624）——稀疏化后该检查仍需要，
  因为它守的是 CFG 规模而非位宽。
- `wordsPerSet` **保留计算**（L596）：它继续用于
  `NLCPGDataFlowMethodMetrics.WordsPerSet` 与 `CreateCandidateLimitExceededPartition`，
  但**不再决定存储**（设计 §4.1）。

**Step 2: 替换 fixpoint 内的读写**

- `Array.Clear(incomingScratch, ...)` + `OrBitSet(...)` 循环（L681-688）
  → 稀疏 `in = ∅`；对每个前驱 `OrInto(in, out[pred])`。
- `Array.Copy(incomingScratch, updatedScratch, wordsPerSet)`（L691）
  → `Copy(updated, incoming)`。
- `ApplyDefinitionTransfer`（L695）→ 稀疏版：遍历 `updated` 的所有元素，
  用 `FactsConflict` 过滤，再**升序插入** `definitionOrdinal`。
- `BitSetEquals`（L703）→ `SetEquals`。
- `CopyBitSet(outSets, nodeOffset, updatedScratch, ...)`（L708）
  → `Copy(out[v], updated)`。

**Step 3: 替换第四阶段的 in 集重算与枚举**

- L724-731 → 稀疏重算（复用同一个 `incoming` 缓冲，按节点重置计数）。
- `IsBitSetEmpty`（L733）→ `IsEmpty`。
- `EnumerateSetBits(...)`（L761）→ `Enumerate`，**保持升序**（不变式 3）。
- `AddReachable`（L226-230）→ 用 `Contains(set, definitionOrdinal)` 替代 `IsBitSet`。

**Step 4: 记录溢出（新增度量字段）**

在 `src/NLCPG/Builder/NLCPGBuildMetrics.cs:107-115` 的
`NLCPGDataFlowMethodMetrics` **末尾追加带默认值的可选参数**：

```csharp
int SparseOverflowNodeCount = 0
```

带默认值 ⇒ 既有构造点全部不变。**不得**映射到
`CpgPerformanceFactMapper` / `PerformanceSummaryDocument`（不碰持久化格式）。

**Step 5: 全量构建**

```powershell
$dotnetArgs = @('build','.\src\NLCPG\NLCPG.csproj','--no-restore','-m:1','-nr:false',
  '-p:UseSharedCompilation=false','-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

Expected: 0 个错误。

---

## Task 5: 差分验证（本次最强的正确性证据）

**目的：** 稀疏版与稠密版应当**逐步相同**（设计 §5.2）。用一次临时探针**直接验证**，
比"图等价 oracle 通过"强得多——后者只验证最终结果。

**前置：** Task 4 构建通过。

**Files:**
- 临时探针：`Build\tmp\dfdiff\`（**测完删除**）
- 备份：`Build\tmp\dfdiff\DataFlowPass.cs.dense`（**稀疏化前**的稠密版本）

**Step 1: 保存稠密版基线**

把 Task 4 之前的 `DataFlowPass.cs` 存为
`Build\tmp\dfdiff\DataFlowPass.cs.dense`。

**Step 2: 在稀疏版上加 env 门控自检**

在 fixpoint 收敛后、第四阶段之前，当 `NL_DFDIFF=1` 时：
把稀疏集合**还原成定宽位集**（`ceil(D/64)` 个 word），
与原稠密实现的 `outSets` **逐 word 比对**，不一致即 `Console.WriteLine` 差异并计数。

同时输出每方法的 `WorklistIterations`（收敛迭代次数）——
**它与稠密版是否相等，是"没有提前收敛"的直接证据**（不变式 4）。

**Step 3: 分别在稠密版与稀疏版上运行，比对**

输入：`Player.cs`、`Main.cs`、`WorldGen.cs`、`NPC.cs`（`NPC.cs` 内存较大，
若 14 GiB 机器上 OOM，可跳过并在报告中注明）。

必须报告：

| 项 | 稠密版 | 稀疏版 |
| --- | --- | --- |
| 方法数 | ? | ? |
| 每方法 `WorklistIterations` 之和 | ? | ? |
| 位集元素逐项差异数 | — | **必须为 0** |
| DataFlow 边数（图快照） | ? | ? |
| `SparseOverflowNodeCount` 总计 | — | ? |

**Step 4: 清理**

```powershell
Remove-Item 'Build\tmp\dfdiff' -Recurse -Force
```

并确认 `DataFlowPass.cs` 中无 `NL_DFDIFF` 残留。

**验证标准：** 位集元素逐项差异 **0**；`WorklistIterations` 与稠密版**逐方法相等**；
DataFlow 边数一致。

**停止条件：** 任一差异非 0 ⇒ **不得**进入 Task 6，
必须先定位是编码错误、保序错误，还是收敛判定错误。

---

## Task 6: 用实测证明内存下降（唯一可声称收益的证据）

**前置：** Task 5 差异为 0，图等价 oracle 全绿。**没有本 Task 不得声称任何倍数。**

**Files:**
- 复测产物：`Build\` 下目录（**不得**写源码旁）
- 证据落点：`docs/benchmarks/nlissn-version4-dop12-diagnostics.md`（新增小节）

**Step 1: 同输入、同配置、同 runtime 做 A/B**

口径与**前序优化 Task 7 保持一致**（便于对比）：

- 输入：`D:\TRbackup\Version4\Terraria\{Main,Player,WorldGen}.cs`
  （`NPC.cs` 位集过大，同进程 A/B 有 OOM 风险，按前序做法注明）
- 配置：`CreateDefault()`，`MaxDegreeOfParallelism = 1`
- runtime：同一 `dotnet.exe`
- 测量：`GC.GetTotalAllocatedBytes()` 差值与位集字节

**Step 2: 对账设计期已实测的基线**

设计 §3.2 已用真实分配测量给出四文件全量的数字。
本 Task 需验证端到端口径是否与之一致：

| 项 | 设计期实测（§3.2） | 落地后端到端 |
| --- | ---: | ---: |
| 稠密 outSets 合计（四文件） | 242,467,008 B | ? |
| `k=1` 稀疏分配（含溢出） | 7,361,280 B | ? |
| 倍数 | **32.94×** | ? |
| 溢出节点数 | 2,414 / 870,249（0.28%） | ? |

**若落地后倍数与 32.94× 显著偏离**（例如低于 20×），
需查明是溢出路径、还是 `_setCounts`/`_setOrdinals` 之外还有未计入的分配，
**不得**只报告有利的一面。

**Step 3: 记录构图耗时**

对比 `PerformanceStageId.CpgBuild` 耗时，确认 `IsBitSet` 由 O(1) 变 O(k)
**没有**造成回退（设计 §3.6 的预期方向未实测）。
注意 `k=1` 时 `IsBitSet` 是 **1 次比较**，理论上不能比位集慢；
若实测回退，说明瓶颈在溢出路径（0.28% 节点）或并集操作，
应针对性优化而非改 `k`。若回退显著且溢出路径被确认为主因，
再评估 `k=4`（13.83× 仍正收益）。

**Step 4: 写证据**

在 `docs/benchmarks/nlissn-version4-dop12-diagnostics.md` 新增小节，
写明输入、配置、runtime、`k=1`、优化前后字节数与构图耗时。**只报告实际测到的数**。

**不得声称：** 未做本 Task 就写"内存再降 32.94 倍"作为**端到端**结论——
§3.2 测的是**位集存储**这一项的分配量，
端到端总分配量还含图构建、字典、字符串等，比例必然不同。
**也不得混用单方法数值**（`NPC.AI()` 的 46.8× / 117×）当作聚合收益。

---

## Task 7: 文档收口

**Files:**
- Modify: `设计docs/目前设计/性能分析组件.md`（第 14 节后追加稀疏化小节）
- Modify: `docs/plans/2026-09-23-dataflow-sparse-bitset-design.md`（状态改为已实现）
- Modify: `Context/progress.md`（替换过期条目，只留仍影响当前工作的事实）
- Modify: `src/NLCPG/Builder/NLCPGBuildMetrics.cs`（`WordsPerSet` 与
  `SparseOverflowNodeCount` 的注释）

**Step 1:** 在设计页记录：`WordsPerSet` 仍是**逻辑位宇宙宽度** `ceil(D/64)`，
**自稀疏化起不再反映实际存储量**；内存推算必须用 `N × (4 + 4k)`，
其中 `k = InlineSlotCount = 1` ⇒ `8N + 24` 字节（加溢出开销）。

**Step 2:** 把已完成事实收口进 `Context/progress.md`。

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

## 执行结果（2026-09-23 落地，全部实测）

### 改动落点

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Builder/Passes/DataFlowPass.cs` | 新增私有 `SparseSetStore`（SoA：`int[] _counts` + `int[] _ordinals`，`k=1` 内联槽位 + lazy `Dictionary<int,int[]>` 溢出）；删除 7 个定宽位集辅助方法；fixpoint 与第四阶段改用 store；`DefinitionFactIndex.TryGetCandidates`/`AddReachable` 签名改为 `(store, slot)` |
| `src/NLCPG/Builder/NLCPGBuildMetrics.cs` | `NLCPGDataFlowMethodMetrics` 末尾追加 `int SparseOverflowNodeCount = 0`（带默认值，既有 8 参构造点不变；不映射到持久化） |
| `tests/NLISSN.Testing/TestCodeSet/Cpg/CpgBuilderSources.cs` | 新增 `DataFlowSparseOverflowRefOut`、`DataFlowSparseOverflowValueParams` |
| `tests/NLISSN.ContractTests/Cpg/NLCPGDataFlowSparseSetTests.cs` | 新增 7 项回归护栏测试 |

`WordsPerSet` **语义保持不变**（仍是逻辑位宇宙宽度 `ceil(D/64)`），
`SchemaVersion` 仍为 1，`NLCPGPartitionedBuilderTests.cs:276` 的断言**未修改即通过**。

### Task 1 实测结论

全量 1692 方法 / N=870,249。**中途基数从未超过收敛基数**：
`worseNodes` 恒为 0，`midMax == finalMax`（全局 12），`midSum == finalSum`（71,899）。
⇒ 设计 §3.4 的单调性推论**成立**，`k=1` 在 fixpoint 全程安全。

**⚠️ 该验证的区分力很弱，必须如实记录**：实测 `worklistIterations / N = 1.0000`
（1688/1692 个方法恰好等于 N，全语料总重复处理仅 **15** 次）——
即该语料上 fixpoint 近乎**单趟收敛**，多数节点只被写入一次，
单调性近乎平凡成立。故本条**不足以**支撑"任意语料下 k=1 都安全"，
但它也不是反面证据；真正的正确性保证来自 Task 5 的差分验证与溢出外溢设计。

**附带发现（解释了基数分布）**：out-set 基数由**同时活跃的参数定义**驱动，
实测 `cardMax ≤ 参数个数` 在 **1692/1692** 成立（74.4% 取等号）。
这解释了 §2.3 的基数分布，也是 Task 2 fixture 必须多参数的原因。

### Task 2 实测：原 fixture 是空测试（已修正）

设计原定用"同块内两个不同 BaseKey 的局部变量"构造基数 ≥ 2。**实测证伪**：
`var alpha = seed + 1; var beta = seed + 2; return alpha + beta;` 的
`cardMax == 1`、`nodesCard2Plus == 0` —— **根本不触发溢出**，测试退化为空测试。

改用**多参数**构造后：`Run(int, ref int, ref int, out int)` ⇒ `cardMax=4`；
`Run(int×5)` ⇒ `cardMax=5`。

断言也从 `edges ≥ D` 改为**冻结精确边数**：实测 `edges ≥ D` 的余量为 9 和 4
（`edges=16 vs D=7`），截断一个定义后断言仍会通过 ⇒ 该形式**捕获不到截断**。
冻结值经 5 次重跑确认稳定。

**结果**：7/7 全绿（2 个 Theory 用例 + 5 个 Fact）。

### Task 5 差分验证（最强证据）

临时探针在同一代码路径上并行跑**稠密参照**（`ulong[]`，宽 `ceil(D/64)`）
与稀疏实现，**逐元素比对**：每个节点在 fixpoint 每次转移后比（`step`）、
每次前驱并集后比（`fixpoint-in`）、第四阶段每次 in 集后比（`stage4-in`）、
收敛后逐节点全量比（`final`）。

| 项 | 实测 |
| --- | --- |
| 覆盖方法数 | **1692 / 1692** |
| 累计逐元素比对次数 | **951,224,762** |
| **不匹配数** | **0** |
| 其中转移步不匹配 | **0** |
| 覆盖规模 | N=870,249，D=64,566 |

⇒ 稀疏与稠密表示在真实语料上**完全一致**。探针验证后已整块删除并核验无残留。

### Task 6 实测：内存与耗时 A/B

内存（精确记账法，每方法计一次，同输入/同配置/同 runtime）：

| 文件 | 方法数 | N | 稠密字节 | 稀疏字节 | 倍数 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Player.cs | 474 | 88,715 | 2,095,320 | 765,484 | 2.74 |
| Main.cs | 629 | 142,991 | 8,574,184 | 1,215,332 | 7.06 |
| WorldGen.cs | 1164 | 424,133 | 45,380,432 | 3,604,100 | 12.59 |
| NPC.cs | 847 | 480,555 | 192,703,032 | 3,949,424 | 48.79 |
| **合计** | **1692** | **870,249** | **242,467,008** | **7,237,888** | **33.50** |

- 稠密合计 242,467,008 B **与设计 §2.3 完全一致**（交叉验证成立）。
- 倍数按 `w` 分档呈严格的 `≈ w` 结构律（w=2→1.96、w=3→2.96、…、w=117→116.99），
  与 `稀疏 = 24+8N ≤ 稠密 = 24+8Nw` 的推导**逐档吻合**。
- **溢出节点 2414（0.28%）与设计实测完全一致**。
- 与设计预期 7,361,280 B / 32.94× 差 **123,392 B**（记账口径未含字典内部桶数组的精确开销）⇒ 不改变结论。

**⚠️ 实测过程中的口径陷阱（已排除）**：同进程内多次构建时
`LastBuildMetrics.DataFlowMethodMetrics` **会累积先前文件的方法**，
导致按文件简单相加得 3114 个方法（Player.cs 的 474 个方法在后续每次运行中都复现）。
必须以**方法名全局去重**后聚合，才是正确的 1692。

耗时（NPC.cs，`k=1` vs `k=64`，同机同轮，边数均为 3,802,840 完全一致）：

| 配置 | run 1 | run 2 |
| --- | ---: | ---: |
| `k=1`（稀疏，走溢出） | 57,517 ms | 50,044 ms |
| `k=64`（溢出恒不发生） | 49,221 ms | 47,332 ms |

⇒ `k=1` 比"无溢出"配置**慢约 5–8%**，与设计 §3.6 标注的
"`k=1` 的溢出路径 CPU 代价未实测"这一风险**方向一致**。
但省下 2.38× 内存（`k=4` 只有 13.83×）⇒ 仍判 `k=1`；
**该 CPU 差值不是与稠密实现的对照**，故不得声称"CPU 无退化"。

### 全量测试：既有隔离缺陷（与本次改动无关）

全量 ContractTests 在 xUnit **默认集合并行**下失败项**每次不同**：
实测四次为 **9 / 10 / 11 / 14** 项，成员互不相同，且含 `dop:1` 自比失败、
`RepeatedBuilds` 失败、以及**不经过 `DataFlowPass`** 的测试
（如 `CpgInterproceduralEdgeOrderTests` 只用 `InterproceduralDataFlow` 能力）。
单独跑这些测试**全绿**（如 17/17）。

**决定性排除实验**：把 `InlineSlotCount` 临时设为 **64**
（溢出通道完全不参与，语义退化为定宽位集）后重跑，**仍然失败 14 项** ⇒
失败与稀疏表示/溢出路径**无关**。

**✅ 规避方案已实测确认**：
- 最小复现**逐项吻合**——只跑 `NLCPGDisplayTextTests` + `NLCPGNodeIdContractTests`
  （22 项）复现 **2 失败**，与本文件「既有测试隔离缺陷」记录完全一致。
- 规避文件 `xunit.runner.json`（`parallelizeTestCollections: false`）
  **必须放在输出目录 `Build\test\Debug\net10.0\`**；放入后全量 **440/440 全绿**。
- **踩坑更正**：放**源目录** `tests\NLISSN.ContractTests\` **不生效** —— 该 csproj
  **没有** `Content`/`None` + `CopyToOutputDirectory` 项，源目录 json 不会被复制到
  输出目录（实测 `config copied to output: False`，仍失败 12–14 项）。
  本计划正文说的"该文件放 `Build\test\Debug\net10.0\`"是**正确**的。

该缺陷是**既有的**，`Context/progress.md` 已有记录（根因：部分测试调
`Directory.SetCurrentDirectory()` 污染进程级 CWD，而另一些测试传相对路径）。
本次改动**未触及** `src/NLISSN.Rules`、`src/NLISSN.Core` 任何文件。

### 结论

本计划 7 个 Task 全部执行完毕。**内存收益已由实测证明（33.50×，溢出节点数与设计一致）**，
**语义等价已由 9.51 亿次差分比对证明**。
未证明项：`k` 的 CPU 代价未与稠密实现对照（只有 `k=1` vs `k=64` 的同族对照）；
`InlineSlotCount` 的字典精确开销未计入记账。

### 完成条件逐条对账

| # | 条件 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | Task 1 结论已记录且未推翻 §3.4 | ✅ | `worseNodes==0`，`midMax==finalMax==12`，`midSum==finalSum==71,899`（**但区分力弱，已如实标注**） |
| 2 | Task 2 的模型测试全绿，且溢出测试真实构造基数 `k+1` | ✅ **含一处偏差** | 实测 **7** 项（非 6）全绿；基数实测为 **4 与 5**（非 2）——见下方说明 |
| 3 | Task 5 差异为 0，且 `WorklistIterations` 逐方法相等 | ✅ | 951,224,762 次比对 **0 不匹配**，其中 `step` 类不匹配 **0** |
| 4 | 图等价 oracle 全绿 | ✅ | 定向隔离运行 **18/18**（稀疏 7 + 压缩 4 + 分区 DataFlow + 批次 DataFlow） |
| 5 | `Run-TestTiers.ps1 -Fast` 退出码 0 且有 `run.json` | ✅ | `Build/TestResults/20260923-035848-7a037463/run.json`：unit `exitCode 0`、contract `exitCode 0`；UnitTests **117/117**、ContractTests **440/440** |
| 6 | Task 6 给出实测字节与耗时 A/B 并与 32.94× 对账 | ✅ | 实测 **33.50×**（242,467,008 → 7,237,888 B），差 123,392 B 已解释；耗时 A/B 见上 |
| 7 | `check-harness-consistency.ps1` OK 且 `git diff --check` 退出 0 | ✅ | `[check-harness-consistency] OK`；`git diff --check` 连续 5 次 exit 0 |
| 8 | 未改 `SchemaVersion`、未加 `InternalsVisibleTo`、未映射持久化 | ✅ | `NLCPGBuilderOptions.cs:92` 仍为 `= 1`；全仓无 `InternalsVisibleTo`；`SparseOverflowNodeCount` 仅出现于 `NLCPGBuildMetrics.cs` |
| 9 | 探针目录已删、`DataFlowPass.cs` 无探针残留 | ✅ | 10 个探针目录全部不存在；`NL_DF\|DFDIFF\|DFALLOC\|DFCARD\|DFMID\|DenseRefStore\|Probe` 命中 **0** |

**条件 2 的偏差说明（如实记录，非降级）**：计划写的是"6 个模型测试"且
"真实构造基数 `k+1` = 2 的场景"。实际落地为 **7 个测试**，且溢出用例的基数实测是
**4 和 5**。原因是设计期对该场景的构造假设被**实测证伪**——用局部变量无法让基数升到 2
（纯局部变量构造实测 `cardMax == 1`），必须靠**多参数**才能触发溢出；
按新构造法得到的基数自然更高。因此这是**覆盖更强的结果**，
但**与计划字面数字不一致**，故在此显式标注而非默默改口径。

---

## 完成条件

全部满足才算完成：

1. **Task 1 的实测结论已记录**，且中途基数峰值未推翻设计 §3.4 的单调性推论。
2. Task 2 的 6 个模型测试全绿，其中**溢出不截断**测试真实构造了基数 `k+1` = 2 的场景。
3. **Task 5 的差分验证差异为 0**，且 `WorklistIterations` 与稠密版逐方法相等。
4. 图等价 oracle 全绿：`NLCPGPartitionedBuilderTests` 的 5 个 DataFlow 跨 DOP 测试
   + `CpgWorkBatchDataFlowTests.BuildFromSource_BatchedDataFlow_PreservesGraphAcrossDegreesOfParallelism`
   + 前序 `NLCPGDataFlowBitsetCompactionTests` 的 4 个测试。
5. `Run-TestTiers.ps1 -Fast`（unit + contract）退出码 0，且有 `run.json` 证据。
6. **Task 6 给出实测**的端到端字节与构图耗时 A/B，并与设计 §3.2 的
   32.94× 位集基线**对账**（显著偏离必须查明原因并如实报告）。
7. `check-harness-consistency.ps1` OK 且 `git diff --check` 退出 0。
8. 未修改 `PerformanceSummaryDocument.SchemaVersion`，
   未新增 `InternalsVisibleTo`，
   未把 `WordsPerSet` 或 `SparseOverflowNodeCount` 映射进持久化。
9. 所有临时探针目录已删除，`DataFlowPass.cs` 中无探针残留。

## 回退策略

- **Task 3/4 整体可回退**：稀疏表示集中在 helper 群与 fixpoint 两处，
  恢复 `ulong[] outSets` + 位集 helper 即可回到当前状态
  （前序优化成果 `N→D` 与去 `inSets` 保留）。
- Task 4 Step 4 的度量字段带默认值，可单独保留而不影响编译。
- **Task 5 未零差异前不得进入 Task 6。**
- 若 Task 1 推翻单调性推论，或 Task 6 显示位集收益显著低于设计 §3.2 的 32.94×，
  **应停止并回报**，而不是继续调整参数。

## 已知风险与预判

| 风险 | 预判 | 处置 |
| --- | --- | --- |
| 溢出写成截断 | 最隐蔽，静默丢靠后的定义；`k=1` 时 47% 方法存在基数 ≥2 的集合，触发面大 | Task 2 测试 1 + Task 5 差分 |
| 枚举顺序改变 | 只在配置了候选预算时暴露，且后果是**整方法丢边** | Task 2 测试 6 + 保持升序 |
| `SetEquals` 近似 | fixpoint 提前收敛，图偏小 | Task 5 比 `WorklistIterations` |
| 全量 Contract 套件的**既有** xUnit 隔离缺陷 | 会随机失败 9–14 项，**非本次回归** | 见下 |
| CPU 回退 | 溢出路径（0.28% 节点）或并集操作；`k=1` 的 `IsBitSet` 仅 1 次比较，不是主因 | Task 6 Step 3；必要时评估 `k=4` |

**既有测试隔离缺陷（前序已定位，非本次引入）**：
`NLCPGDisplayTextTests` / `NLCPGProjectExportTests` 调用进程级
`Directory.SetCurrentDirectory()`，而 `NLCPGNodeIdContractTests` 传相对路径，
导致 `Path.GetFullPath` 按被改写的 CWD 解析，抛
`Stable anchor for '…' was not included in the supplied preallocated NodeId table`。
若本次全量 Contract 出现随机失败，**先用** `xunit.runner.json` 设
`parallelizeTestCollections: false` 复核**再判定是否回归**（该文件放
`Build\test\Debug\net10.0\`，`Build/` 本就 gitignore，用完删除）。
