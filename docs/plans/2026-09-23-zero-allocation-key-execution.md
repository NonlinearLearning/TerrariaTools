# 零分配键执行计划

> **状态：待执行（2026-09-23）。** 本计划尚未开始；下文行号均指**当前**
> `NLCPGBuilder.cs`（工作区未提交状态，SHA256 `EC2B867F…40539`）。
> 设计期已完成全部只读实测（含穷举等价性验证与真实类型编译），基线见设计 §5.2。

**Goal:** 把三处"为传给容器而临时构造 `string`"的分配降为 **0 B/次**，
在**不改变任何图语义、节点集、边集、边顺序或持久化格式**的前提下：

| # | 场景 | 现状 | 目标 | 省 |
| --- | --- | --- | --- | ---: |
| ① | `NodeSortKey` | 拼 148 字符键 | `readonly struct` + `IComparable<T>` + `ref struct` 游标 | **320 → 0 B/次** |
| ② | `BuildNodeKey` | 拼键字符串 | `readonly record struct` 键 | **160 → 0 B/次** |
| ③ | `StringInterner` 查表 | 先造 `string` 再查 | `GetAlternateLookup<ReadOnlySpan<char>>` | **48 → 0 B/次** |

**Architecture:** ① 在 `NLCPGBuilder.cs` 内新增 `internal readonly struct NLCPGNodeStreamKey`
与 `internal ref struct SegmentCursor`，把 6 个调用点（L769/779/877/878/881/882）的
`ThenBy(… NodeSortKey …, StringComparer.Ordinal)` 换成 `ThenBy(… new NLCPGNodeStreamKey(graph, x))`，
**链条层级一律不动**。② 按 §1 甄别后只改**真的在拼键**的那些表达式。
③ 给 `StringInterner` 加一个 span 查询重载，**不动** `Intern(string?)`。

**Tech Stack:** C#/.NET 10、`NLCPG` builder、xUnit Contract 测试、
`Miscellaneous/scripts/Run-TestTiers.ps1`、`Build/Tools/Invoke-SerialDotnet.ps1`。

**设计依据：** [零分配键设计](2026-09-23-zero-allocation-key-design.md)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

---

## 工作规则

- 工作目录 `D:\ProjectItem\SourceCode\Net\NL`；**保留工作区中已有的无关改动**
  （当前 **151** 个未提交文件，含 `DataFlowPass.cs`、`NLCPGDataFlowSparseSetTests.cs`、
  `CpgBuilderSources.cs` 等**他人正在写入**的文件——实测它们在会话期间仍被修改）。
- **本任务是纯表示层语义保持型重构。** 保序差分未零差异前，
  **不得**声称任何优化完成或任何倍数。
- **`DataFlowPass.cs` 与 `NLCPGDataFlowSparseSetTests.cs` 有并发写入者。**
  本计划**不**触碰这两个文件；若发现需要改，**停下来回报**。
- **构建/测试前**先把 `$env:TEMP`/`$env:TMP` 指到可写目录。默认 `C:\WINDOWS\TEMP`
  会导致 `check-harness-consistency.ps1` 第 127 行 `Remove-Item` 报"拒绝访问"：

  ```powershell
  $env:TEMP = 'D:\ProjectItem\SourceCode\Net\NL\Build\tmp'; $env:TMP = $env:TEMP
  ```

- 所有可编译命令走串行包装器，避免并发构建争用：

  ```powershell
  $dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
    '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
    '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
  & .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
  ```

  **注意**：不要把 `-p:` 参数直接内联进 `Invoke-SerialDotnet.ps1` 命令行，
  会触发 `Parameter cannot be processed because the parameter name 'p' is ambiguous`。

- 生成物只落 `Build\`；不得在源码旁写基准产物。
- 每一档验证只报告**实际执行过**的命令与结果；不得把未执行的档位记为通过。
- 改前先备份哈希，便于回退核验。
- **⚠ 还原备份后必须刷新 mtime。** `Copy-Item` 会保留原文件的
  `LastWriteTime`，还原后源文件 mtime 可能**早于**已构建的 DLL，
  导致 MSBuild 跳过重建、**测试跑的是改动后的旧 DLL**，
  表现为"还原了却仍然失败"的假象（Task 2 Step 5 实际踩到）。

  ```powershell
  Copy-Item $backup $target -Force
  (Get-Item $target).LastWriteTime = Get-Date   # ← 必须
  ```

  **判定方法**：测试失败时先比对源文件 mtime 与
  `Build/src/Debug/net10.0/NLCPG.dll` 的 mtime；DLL 更新则说明跑的是旧代码。

### 本次改动的既有实现锚点（当前行号）

| 内容 | 位置 |
| --- | --- |
| `NodeSortKey` 定义 | `src/NLCPG/Builder/NLCPGBuilder.cs:1053-1056` |
| `NodeSortKey` 调用点（6 处，全在 `RunInterproceduralDataFlowPass`） | `NLCPGBuilder.cs:769, 779, 877, 878, 881, 882` |
| `orderedCallSites` 链（`FullName`→`SpanStart`→key） | `NLCPGBuilder.cs:765-770` |
| `targets` 链（`FullName`→key，**无 `SpanStart`**） | `NLCPGBuilder.cs:774-781` |
| `orderedPlans` 链（8 层，`BridgeKind` 重复） | `NLCPGBuilder.cs:873-883` |
| `MaxCallTargetsPerSite` 默认值 | `src/NLCPG/Builder/NLCPGBuilderOptions.cs:160` |
| `NLCPGGraph.Resolve`（每次 `lock`） | `src/NLCPG/Model/NLCPGGraph.cs:197-200` |
| `StringInterner`（2 个 public 成员） | `src/NLCPG/Model/StringInterner.cs:13, 36`（`lock` 在 L20/L44） |
| `DecisionCpgFactory.BuildNodeKey(SyntaxNode)` | `src/NLISSN.Core/Decision/DecisionModel.cs:778-781` |
| `DecisionCpgFactory.BuildNodeKey(NLCPGNode)`（命中即 0 分配） | `DecisionModel.cs:784-792` |
| `CoverageEvidence.BuildNodeKey`（4 次分配） | `src/NLISSN.Core/Lifting/CoverageEvidence.cs:111-119` |
| `SymbolId` | `NLCPGBuilder.cs:1683-1687` |
| `SyntaxId`/`TokenId`（**死代码**） | `NLCPGBuilder.cs:1702-1710` |
| 跨 DOP oracle（**排序后比较，对边序是盲的**） | `tests/NLISSN.ContractTests/Cpg/CpgWorkBatchInterproceduralTests.cs:104-123` |
| 边契约测试 | `tests/NLISSN.ContractTests/Cpg/CpgShardContractTests.cs:53` |
| 图等价 oracle | `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`（5 个 DataFlow 跨 DOP） |
| 架构边界断言（禁止新增 `InternalsVisibleTo`） | `tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs` |

**基线哈希（回退锚点，改前核对）**：

| 文件 | SHA256 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | `EC2B867FE48F04F1365904B0DC3925BAB23B6F32E2C47A15C07BFC6D93740539` |
| `src/NLCPG/Model/StringInterner.cs` | `EA329B5455B759940DB81C91F952ECBF35196D970AE57F25F9731E6D58D19000` |
| `src/NLCPG/Model/NLCPGEdge.cs` | `19AF0706D7D6E798120105B6CE9D10FF6A8E6CB0224F79F46E5EBF4C14C71487` |
| `src/NLCPG/Builder/Passes/MethodDecorationPass.cs` | `FD7C2E859C3EFA854332726C78AF7446CE5013CAFF9C8F54334DD642540F996C` |

> ⚠ 上表哈希采集于 2026-09-23 02:44。工作区存在**并发写入者**，
> 执行本计划前**必须重新采集并核对**；不一致时先查清是被谁改的。

**关键正确性前提（不得违反，共 5 条，详见设计 §3.2/§4）**：

1. **逐字符等价**：`CompareTo` 返回值的**符号**必须与
   `string.CompareOrdinal(NodeSortKey(a), NodeSortKey(b))` 逐一相同。
2. **段序与段数固定**：6 段 5 分隔符，**一处不减**；空段产出 0 字符但**仍产出分隔符**。
3. **`Resolve` 只在构造期**：`CompareTo` 内不得出现 `graph.Resolve*`（否则锁调用放大 41.6×）。
4. **三条链形状不同，不得共用一个 `Key`**：`targets` 比 `orderedCallSites` **少一层 `SpanStart`**。
5. **`Resolve` 文本必须按 `CurrentCulture` 渲染 `int?`**（与 `$""` 插值一致），**不得**用 `Invariant`。

---

## Task 0: 环境与基线核验（无代码改动）

**目的：** 确认工作区状态、哈希、并发写入者，避免在错误基线上改动。

**Step 1: 记录哈希与工作区规模**

```powershell
cd D:\ProjectItem\SourceCode\Net\NL
foreach ($f in @('src\NLCPG\Builder\NLCPGBuilder.cs','src\NLCPG\Model\StringInterner.cs')) {
  "$((Get-FileHash $f -Algorithm SHA256).Hash)  $f"
}
(git status --short src/ tests/ | Measure-Object).Count
git rev-parse --short HEAD
```

Expected：`NLCPGBuilder.cs` = `EC2B867F…40539`，`StringInterner.cs` = `EA329B54…19000`，
未提交数 **151**，HEAD `925161d`。

**Step 2: 确认没有并发写入者正在改本计划要碰的文件**

```powershell
Get-ChildItem src\NLCPG\Builder\NLCPGBuilder.cs, src\NLCPG\Model\StringInterner.cs |
  Select-Object Name, LastWriteTime
```

若 `LastWriteTime` 距现在 < 10 分钟，**先等待并确认无人改动**再继续。

**Step 3: 备份**

```powershell
New-Item -ItemType Directory -Force -Path 'Build\tmp\zerokey' | Out-Null
Copy-Item 'src\NLCPG\Builder\NLCPGBuilder.cs' 'Build\tmp\zerokey\NLCPGBuilder.cs.orig' -Force
Copy-Item 'src\NLCPG\Model\StringInterner.cs' 'Build\tmp\zerokey\StringInterner.cs.orig' -Force
```

**Step 4: 记录基线测试结果**（后续判定"是否回归"的唯一依据）

```powershell
$env:TEMP = 'D:\ProjectItem\SourceCode\Net\NL\Build\tmp'; $env:TMP = $env:TEMP
$dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
  '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

Expected：退出码 0。**若基线本就失败，记录确切失败清单**——否则后面无法区分回归与既有缺陷。

---

## Task 1: 实测 `NodeSortKey` 调用次数（补上设计缺失的分母）—— ✅ **已完成（2026-09-23）**

**状态：已完成。** 结论见下方 Step 5 的实测结果；
设计 §4.5 与 §8 备选 1 已按此结果**修正**（原"L779 是非零成本"的说法被否定）。

**目的：** 设计 §5.3 明确记录"**②③ 的调用次数未测**"，且 ① 的调用次数只有
由运行反推的估计（592–594 万次）。本 Task 用 **env 门控只读计数器**给出**单次构建内**
的准确调用次数，作为收益估算的分母。

**为什么必须做**：设计 §4.5 发现 `targets` 排序的结果**一定被丢弃**。
本 Task 的原始假设是"L779 仍会调用一次 `NodeSortKey`，属纯浪费"——
**该假设被实测否定**（见 Step 5）。

**Files:**
- 探针：`Build\tmp\zerokey\`（**不得**留在源码树；`Build/` 已 gitignore）
- 被改文件：`src/NLCPG/Builder/NLCPGBuilder.cs`（**仅临时**，Task 1 结束必须还原）

**Step 1: 插入 env 门控计数器**

在 `NLCPGBuilder` 内加（`NL_ZEROKEY_PROBE=1` 才输出）：

```csharp
private static long _nskCalls;
private static long _nskCallsFromTargets;
```

在 `NodeSortKey` 入口 `Interlocked.Increment(ref _nskCalls);`；
在 L779 的 lambda 内单独再计一次 `_nskCallsFromTargets`。
构建结束时（`RunInterproceduralDataFlowPass` 结束处）把两个值与
`MaxCallTargetsPerSite` 一起输出。

> **实际执行时的增强**：除总数外，还按**调用点**打了标签
> （`callSitesL769` / `plansL877_882` / `other`），并统计了循环迭代数
> `loopIters` 与 `targets` 长度分布 `t0/t1/tGt1`、`plansEntered`。
> **没有这些分项就无法发现 Step 5 的结论。**

**Step 2: 用一个真实 DOP=1 的单文件构建跑一次**

```powershell
cd D:\ProjectItem\SourceCode\Net\NL
$env:NL_ZEROKEY_PROBE = '1'
& .\Build\src\Debug\net10.0\NLISSN.exe   # 用 Miscellaneous/nlissn.yml（DOP=1）
```

> 注意：`dotnet run` 在 Version4 输入上会挂起，**必须**直接启动 exe。
> ⚠ 实测发现两个约束：① `Miscellaneous/tmp/library-entry-smoke` 可能已存在而报
> `Artifact run directory already exists`；② `artifacts.root` **必须是相对路径**
> （`NLISSN132`）。**做法**：在 `Build\tmp\zerokey\run\` 下放一份改过
> `runId`/`input.path`/`artifacts.root: artifacts` 的配置，并把 `input.path`
> 指向同目录下的自定义输入文件。
> ⚠ 探针只在**语法**上被处理时触发；仓库默认的 `Library.cs` 太小
> （`calls=0`），**必须**用含多层调用链的输入。

**Step 3: 记录三件事**

1. `_nskCalls` 总量；2. 其中来自 L779（`targets`）的量与占比；
3. `MaxCallTargetsPerSite` 的实际生效值。

**Step 4: 还原探针**

```powershell
Copy-Item 'Build\tmp\zerokey\NLCPGBuilder.cs.orig' 'src\NLCPG\Builder\NLCPGBuilder.cs' -Force
(Get-FileHash 'src\NLCPG\Builder\NLCPGBuilder.cs' -Algorithm SHA256).Hash
```

Expected：回到 `EC2B867F…40539`。

**Step 5: 实测结果（已执行）**

输入 `Build\tmp\zerokey\run\src\ZeroKeyProbe.cs`（10 个方法，多层调用链），DOP=1：

```
calls=494  callSitesL769=22  plansL877_882=472  other=0  fromTargetsL779=0
loopIters=22  t0=0  t1=22  tGt1=0  plansEntered=21  orderedPlans=118
maxCallTargetsPerSite=1  maxBoundaryEdgesPerMethod=10000
```

自洽校验：`22 + 472 = 494` ✓

**三条结论**：

1. **`fromTargetsL779 = 0`**：`t1=22 / tGt1=0` 说明 22 个 call site 的 `targets`
   数组**全部长度恰为 1**，而 `OrderBy`/`ThenBy` 对 **`n ≤ 1` 完全短路**。
   BCL 单独实测（.NET 10.0.11）：`n=0` → 选择器 **0** 次、`n=1` → **0** 次、
   `n=2` → 2 次、`n=1000` → 1000 次（均为 1.00n）。
   ⇒ **`targets` 那一行对 `NodeSortKey` 的负载是 0**，
   设计 §4.5 原判"非零成本 / 算了就扔"**错误，已修正**。
2. **真正的大头是 `orderedPlans`**：**472/494 = 95.5%**（4 个 `ThenBy`）。
   `orderedCallSites` 仅 **22/494 = 4.5%**。
   ⇒ 优化优先级应为 `orderedPlans` ≫ `orderedCallSites` > `targets`（0）。
3. **`maxCallTargetsPerSite=1` 已在运行时确认**（此前只是源码推断）。

**本 Task 的判定作用（已生效）**：设计 §8 原备选 1（"`targets` 结构性简化，
优先于 ②"）**收益为 0，已从优先级中移除**。Task 4 的资源应集中到
`orderedPlans` 的 4 处（§4.6）。

**残留的证据边界**：`calls=494` 只是**单文件**结果；
跨文件/大项目下的总量与结构占比**未测**，已登记为设计 §8 备选 6。

---

## Task 2: 建"保序"图快照护栏（**本次最强的正确性证据**）—— ✅ **已完成（2026-09-23）**

**状态：已完成。** 新增 `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`
（3 个测试），**已证明它对顺序变化敏感**，且**已实测坐实既有 oracle 是盲的**（见 Step 4）。

**目的：** 设计 §4.3 证明**现有 oracle 对边顺序是盲的**
（`DescribeGraph` 在比较前 `OrderBy(SourceNodeId).ThenBy(Kind).ThenBy(TargetNodeId)`），
而 ① **恰好会改变边产出顺序**（`orderedPlans` 次序 → `PublishInterproceduralPlans`
的 `AddEdge` 顺序；`AddEdge` 是 append-only，插入序可观测）。

**因此必须新建一个不做任何排序的快照断言**，否则"改对了"和"改错了"在测试上无法区分。

**⚠ 这是护栏测试（非 RED→GREEN）**：它应当在**改动前**就通过。
若改动前就失败，说明基线快照不稳定（先查明原因，**不得**直接放宽断言）。

**Files:**
- 新增：`tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`（**已创建**）
- 参考（**不加**到既有 `DescribeGraph`，避免破坏它）：`CpgWorkBatchInterproceduralTests.cs`

**Step 1: 写保序快照**

要点：
1. 用 `NLCPGBuilderOptions.CreateDefault() with { MaxDegreeOfParallelism = 1, RequestedCapabilities = new[] { NLCPGCapability.InterproceduralDataFlow }, LargeFileLineThreshold = 1, LargeFileMethodThreshold = 1, LargeMethodLineSpanThreshold = 1, SyntaxLargeFileLineThreshold = 1 }`（与既有 oracle 的 `CreateOptions(1)` 保持一致）。
2. 输入要**触发 `orderedPlans` 的多个 `ThenBy` 层**：至少 2 个 call site、每个有可解析目标、
   且存在 `BridgeKind` 不同的桥（否则 L876/L880 那两层不被激活）。
3. **断言 `graph.Edges` 的原始顺序**（不排序），逐项记录
   `SourceNodeId|Kind|TargetNodeId|StructuredLabel?.StableKey|ContextId`。
4. 断言 `InterproceduralDataFlow` 类边的**相对次序**（这才是 `orderedPlans` 的产物）。

> **实际实现**：归一化为 `{Source}>{Target}|{BridgeKind}|{SpanStart}:{SpanEnd}`，
> 只取 `InterproceduralDataFlow` 边（74 条），基线冻结在测试里的
> `ExpectedInterproceduralOrder`。另外两个测试分别守
> "重复构建一致性"与"总边数"（后者让失败原因更易读）。

**Step 2: 先证明该快照是"敏感"的**

在**不改产品代码**的前提下，临时把 `orderedPlans` 的 L875/L876 两层对调，
确认**新测试失败**——这证明它真的能捕捉顺序变化（而不是恒真的空断言）。
随后**还原**并确认回到绿。

> 这一步不能省：一个恒真的顺序断言比没有断言更危险。

**Step 3: 确认既有 oracle 确实对此不敏感**

对同一输入，把边顺序打乱后喂给 `DescribeGraph` 的比较逻辑，
确认它**不报差异**（坐实设计 §4.3 的判断）。

**Step 4: 实测结果（已执行）**

**⚠ Step 2/3 的做法已按实测修正。** 原计划"把 L875/L876 两层对调"**不足以**证明敏感性——
实测两次"看似合理"的扰动**都是 no-op**：

| 扰动 | 结果 | 原因 |
| --- | --- | --- |
| 对调 L881/L882（`Source` ↔ `Target` 层） | 护栏**仍通过** | 同一 call site 内 `ArgumentToParameter` 的 `TargetNode` 恒定，交换后序不变 |
| `NodeSortKey` 的 `Kind` 由文本序改**数值序** | 护栏**仍通过** | 同一 call site 内 `Kind` 恒定，改比较方式不改变组内次序 |

⇒ **能真正证伪的扰动是"改变组内区分键"**。用**首段换成 `SpanEnd`**（把
`{Kind}|{FullName}|...` 改成 `{SpanEnd}|{Kind}|...`）后：

```
护栏 CpgInterproceduralEdgeOrderTests:
  Expected: 273>149|..., 277>149|..., 288>149|..., 284>149|..., 286>149|...
  Actual:   284>149|..., 273>149|..., 277>149|..., 286>149|..., 288>149|...
  → 失败 1 / 通过 2   ★ 护栏确实敏感
```

**同一扰动下，既有 oracle 全部通过**：

| 套件 | 结果 |
| --- | --- |
| `CpgWorkBatchInterproceduralTests` | **通过 4/4** |
| `NLCPGPartitionedBuilderTests`（跨 DOP） | **通过 39/39** |

⇒ **设计 §4.3 的判断已被实测坐实：既有 oracle 对边顺序变化完全无感。**
还原后（哈希回到 `EC2B867F…40539`）护栏与既有 oracle **7/7 全绿**。

**Step 5: 踩到的坑（必须写进工作规则）**

`Copy-Item` **保留原 mtime**，还原后源文件 mtime 回到 09-21，
**早于** 已构建的 DLL（09-23），于是 MSBuild 认为无需重建，
**测试跑的是带扰动的旧 DLL**，出现"还原后仍然失败"的假象。
**处置**：还原后显式 `(Get-Item $f).LastWriteTime = Get-Date` 刷新 mtime，
或直接 `dotnet build` 前先 `Remove-Item Build/src/obj/NLCPG -Recurse -Force`。
**判定方法**：失败时先比对源文件 mtime 与 `Build/src/Debug/net10.0/NLCPG.dll` 的 mtime。

---

## Task 3: 实现 `NLCPGNodeStreamKey` 与 `SegmentCursor`

**目的：** 落地设计 §3.1 的结构体键，**先不加调用点**，保证可独立编译与验证。

**Files:**
- 修改：`src/NLCPG/Builder/NLCPGBuilder.cs`（新增两个类型；**此时不改任何调用点**）
- 骨架来源：诊断报告 `#### 逐字符虚拟流的具体实现`

**Step 1: 加入两个类型（`internal`）**

要点（逐条对应设计风险 R1–R14）：
1. `internal readonly struct NLCPGNodeStreamKey : IComparable<NLCPGNodeStreamKey>`。
2. 字段：`string _kindText` + `string? _fullName, _name, _filePath` + `int? _spanStart, _spanEnd`。
   **`string?` 是必需的**（`Resolve*` 返回 `string?`；写成非空 `string` 会有 7 个
   `CS8618`/`CS8601` 警告）。
3. 构造函数内做 3 次 `Resolve*`（**O(n)，只在这里**）。
4. `CompareTo` 用 4 个 `stackalloc char[12]` + 两个 `SegmentCursor`，`while(true)` 逐字符比。
5. `FormatInt`：`int.MinValue` 需 **11** 字符 ⇒ 缓冲 **12**；
   `TryFormat` 返回 `false` 时**抛异常**，**不得**使用 `written`（失败时 `w=0`，会静默丢段）。
6. `KindText`：静态表 `string[]`，**必须带回退** `((int)kind).ToString(CultureInfo.CurrentCulture)`
   （未定义值如 `(NLCPGNodeKind)99` 的 `Enum.ToString()` 给数字串 `"99"`）。
7. 文本渲染一律 **`CultureInfo.CurrentCulture`**（`sv-SE`/`fi-FI` 负号是 U+2212、
   `ar-SA` 是 U+061C；用 `Invariant` 会**静默改序**）。
8. `SegmentCursor` 为 `ref struct`；段用完即吐 `'|'`；最后一个段后**不吐**分隔符；
   **空段照样占位**。
9. **不实现**非泛型 `IComparable`（会引入装箱）。
10. 加注释说明"6 段一处不减"与"空段占位"的理由（防后人"优化"掉）。

**Step 2: 确认编译通过且无警告**

```powershell
cd D:\ProjectItem\SourceCode\Net\NL
$env:TEMP = 'D:\ProjectItem\SourceCode\Net\NL\Build\tmp'; $env:TMP = $env:TEMP
dotnet build .\src\NLCPG\NLCPG.csproj -c Debug --no-restore
```

Expected：0 错误。**新增警告必须为 0**（骨架已用真实类型验证过 0 警告 0 错误）。

**Step 3: 不改调用点，先跑一遍既有测试**

确认"只加类型"不改变任何行为。

```powershell
$dotnetArgs = @('test','.\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj',
  '--no-restore','-m:1','-nr:false','-p:UseSharedCompilation=false',
  '-p:MSBuildNodeReuse=false','-p:BuildInParallel=false',
  '--filter','FullyQualifiedName~CpgWorkBatchInterprocedural')
& .\Build\Tools\Invoke-SerialDotnet.ps1 -DotnetArguments $dotnetArgs
```

Expected：通过（与 Task 0 Step 4 一致）。

---

## Task 4: 逐调用点替换 `NodeSortKey`（**一次一个，每次全绿**）

**目的：** 把 6 个调用点分 **3 批**替换，每批保持本链原有层级。

**⚠ 铁的纪律：不得共用一个 `Key` 形状。** 三条链层级不同（设计 §4.2）：
`targets` 比 `orderedCallSites` **少一层 `SpanStart`**；复用会让 `targets[0]` 选择改变。

**Files:**
- 修改：`src/NLCPG/Builder/NLCPGBuilder.cs` L769, L779, L877/878/881/882

**Step 1: 批 1 —— `orderedCallSites`（L769）**

```csharp
// 改前
.ThenBy(node => NodeSortKey(graph, node), StringComparer.Ordinal)
// 改后（L767-768 的 OrderBy(ResolveFullName)/ThenBy(SpanStart) 两层保持原样）
.ThenBy(node => new NLCPGNodeStreamKey(graph, node))
```

跑 `--filter FullyQualifiedName~CpgWorkBatchInterprocedural`，Expected：通过。

**Step 2: 批 2 —— `targets`（L779）**

```csharp
// 改后（此链只有 OrderBy(ResolveFullName)，不得补 SpanStart 层）
.ThenBy(target => new NLCPGNodeStreamKey(graph, target))
```

跑同一 filter + **Task 2 的保序快照**，Expected：通过。

**Step 3: 批 3 —— `orderedPlans`（L877/878/881/882，共 4 处）**

**一次全改**（同一链内必须一致，否则层级会错位）：

```csharp
.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.CallSiteNode))
.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.TargetMethodNode))
// ... L879 ArgumentOrdinal、L880 BridgeKind 保持不动（冗余但本次不改，设计 §4.4）
.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.SourceNode))
.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.TargetNode))
```

跑同一 filter + 保序快照，Expected：通过。

**Step 4: 确认 `NodeSortKey` 已无调用点**

```powershell
Select-String -Path 'src\NLCPG\Builder\NLCPGBuilder.cs' -Pattern 'NodeSortKey'
```

Expected：**仅剩 L1053 定义**。此时**保留**该私有方法（Task 6 的差分验证需要它作为 oracle），
在 Task 7 决定是否删除。

---

## Task 5: 差分验证（**本次最强的等价性证据**）

**目的：** 用**逐元素**差分证明结构体键与字符串键**完全等价**，而不只是"测试通过"。

**⚠ 受设计 §4.1 约束**：`src/NLCPG` **无 `InternalsVisibleTo`**，且
`ArchitectureBoundaryTests` 有既有断言。**不得新增** `InternalsVisibleTo`。
因此差分验证用 **env 门控临时探针**（`NL_ZEROKEY_DIFF=1`），验证后**删除**。

**Files:**
- 临时修改：`src/NLCPG/Builder/NLCPGBuilder.cs`（探针，用后还原）

**Step 1: 插入差分探针**

在 L769/L779/L877-882 每处，**同时**计算字符串键与结构体键，
逐对比较 `Math.Sign(string.CompareOrdinal(...))` 与 `Math.Sign(keyA.CompareTo(keyB))`，
不一致时把两者的键文本与期望/实际值**输出到日志**并计数。

**Step 2: 用一个真实的、含多 call site 的输入跑一次**

```powershell
cd D:\ProjectItem\SourceCode\Net\NL
$env:NL_ZEROKEY_DIFF = '1'
& .\Build\src\Debug\net10.0\NLISSN.exe
```

**Step 3: 判定**

Expected：**差异计数 = 0**。

若 > 0：**停止**，把首个不一致的键文本与两侧段值记录下来回报。
**不得**在差异非零时继续（说明流式实现有真错）。

**Step 4: 还原探针**

```powershell
Copy-Item 'Build\tmp\zerokey\NLCPGBuilder.cs.orig' 'src\NLCPG\Builder\NLCPGBuilder.cs' -Force
```

> ⚠ 还原后需**重新应用** Task 3/4 的改动（因为它们也在这个文件里）。
> **更稳妥的做法**：Task 4 结束后先把"已改好、无探针"的版本另存为
> `Build\tmp\zerokey\NLCPGBuilder.cs.task4`，Step 4 从**这个**副本恢复。

**Step 5: 记录 `NodeSortKey` 调用次数是否与 Task 1 一致**

差分探针已包含计数，确认替换**没有改变调用次数**（只改变每次的成本）。

**Step 6: 实测结果（已执行）—— 等价性通过**

在 `D:\TRbackup\Version4\Terraria\Player.cs`（619 KB，**真实诊断输入**）上：

```
plans=20162 orderedPlans=20162 callSites=909
pairChecks=437371 pairDiffs=0 planOrderDiffs=0 callSiteOrderDiffs=0 firstDiff=(none)
```

- **437,371 对逐对比较，差异 0**；
- 用字符串键**重跑整条 8 层链**，与结构体键结果**逐元素相同**（`planOrderDiffs=0`）；
- `callSites` 一级排序同样 0 差异。

另在仓库自身 `src/NLCPG/Builder/NLCPGBuilder.cs`（87 KB）上复现：439 plans / 1151 对 / 0 差异。

**⇒ Task 5 通过：结构体键与字符串键语义完全等价。**

**Step 7: Task 5 过程中发现并修掉的三个真错（都是我自己写出来的）**

| # | 错误 | 后果 | 发现方式 |
| --- | --- | --- | --- |
| 1 | 末段（`SpanEnd`）沿用 `'\|'` 哨兵 | `SpanEnd` 为 null 与非 null 相比**符号相反** | 写代码时自查发现 |
| 2 | 分段比较首段用文本比 | 正确但比 `string.CompareOrdinal` 慢 | 分层计时 |
| 3 | 逐字符游标 | 比字符串版**慢 12.9 倍** | 分层计时 |

错误 1 说明：**"看起来对"的哨兵设计会静默改序**，只有 Task 5 那种逐元素差分才能兜住。

---

## Task 6: 实测分配下降（唯一可声称收益的证据）

**目的：** 用**真实构建**的分配计数给出 A/B，而不是只引用微基准。

**Files:**
- 探针：`Build\tmp\zerokey\`（用后删除）

**Step 1: 用 `GC.GetAllocatedBytesForCurrentThread` 包住整次构建**

> ⚠ DOP>1 时该 API 只统计**当前线程**。**必须用 `MaxDegreeOfParallelism = 1`**
> 才能得到可信的 A/B；或者改用 `dotnet-counters` 的
> `dotnet.gc.heap.total_allocated`（进程级，需在运行中采样）。

**Step 2: 跑 A/B**

- A = 基线（`NLCPGBuilder.cs.orig`）
- B = Task 4 后的版本

同一输入、同一 DOP、各跑 ≥3 次取中位数。

**Step 3: 对账**

把实测的 A−B 差额与设计 §5.2 的微基准预期对账：
- 单次省 **320 B**（148 字符键）或 **295–296 B**（真实键长）；
- 乘以 Task 1 测得的实际调用次数。

**显著偏离必须查明原因并如实报告**，不得只报有利的数字。

**Step 4: 判定边界（必须与收益一起写进报告）**

- 本 Task 证明的是**临时分配下降**，**不是**内存峰值下降（设计 §5.3/§5.4）。
- 不得把微基准的 47.6% 端到端提速说成生产提速。

**Step 5: 实测结果（已执行）—— ❌ 收益不成立，已按回退策略整体回退**

**A/B 口径**：两个变体**只差那 6 处 `ThenBy` 表达式**（`Compare-Object` 验证恰为 12 行差异），
同输入、DOP=1、交替各跑 3 次（共 4 轮）。

**（1）pass 内分配与耗时**（输入 `Version4/Terraria/Player.cs`）

| 变体 | pass 内分配 | pass 耗时 |
| --- | --- | --- |
| A 字符串键 | 376.4 MB | 786 / 823 ms |
| B 分段比较 | 353.2 MB（**−6.2%**） | 891 / 907 ms（**+12.9%**） |
| B 渲染+SIMD | 353.1 MB（**−6.2%**） | 972–1097 ms（**+20~36%**） |

**（2）端到端全程墙钟**（各 6 次采样）

| 变体 | 均值 | 中位 | 标准差 |
| --- | --- | --- | --- |
| A 字符串键 | 26,908 ms | 26,868 ms | 1,026 ms |
| B 分段比较 | 28,495 ms（**+5.90%**） | 27,640 ms（**+2.87%**） | 2,468 ms |
| B 渲染+SIMD | 27,070 ms（**+0.60%**） | 27,144 ms（**+1.03%**） | 817 ms |

**（3）结论：三条独立理由说明它不划算**

1. **收益上限只有 1.74%**。归因表里同一笔 1.632 GiB 占全运行 93.73 GiB 的
   **1.74%**（设计 §1 已记录该分母警告）。设计 §5.1 的"320 → 0 B/次"是
   **占字符串 churn** 的比例，不是占全程序。
2. **实测分配只降 6.2%，且时间不降反升**。pass 内省 6.2% 分配换来 +2.87~5.90%
   端到端墙钟；连"渲染+SIMD"这个最好的一档也只是打平（+0.6~1.0%，且在噪声内）。
3. **根因：把 O(n) 的工作挪进了 O(n log n) 的比较器。**
   字符串键在**构造期**渲染一次（`n` 次），比较期只需 SIMD `CompareOrdinal`；
   结构体键把渲染/逐段解析搬进了 `CompareTo`，于是同一份工作被做了
   `n log n` 次。实测 `n=20162` 时 **构造 81,557 次 vs 比较 374,742 次 = 放大 4.6 倍**。
   这直接推翻了设计 §2"把身份从堆搬到栈就净赚"的前提——
   **栈上的字节不是免费的：比较器里每多一个字节的判断，都要乘 log n。**

**（4）触发回退策略**（见下）："若 Task 6 的实测收益显著低于微基准预期
（例如 <1/3），**应停止并回报**"。实测端到端收益 ≈ 0（甚至为负），
远低于微基准预期的 1/3 ⇒ **已整体回退**。

**（5）回退已执行并核验**

| 项 | 状态 |
| --- | --- |
| `src/NLCPG/Builder/NLCPGBuilder.cs` | 恢复基线，SHA256 = `EC2B867F…40539` ✅ |
| `NLCPGNodeStreamKey.cs` / 两个探针文件 | 已删除 ✅ |
| `NLCPGBuilder.cs` 探针残留 | 0 命中 ✅ |
| 6 处调用点 | 全部回到 `NodeSortKey(…), StringComparer.Ordinal` ✅ |
| 全量 Contract（串行） | **440 / 440 通过** ✅ |

**保留的资产**：Task 2 的保序护栏
`tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs`
**独立于本次改动**仍然有价值——它实测证明既有 oracle 对边顺序是盲的
（扰动后既有套件 4/4、39/39 全绿，只有它报错）。**故不回退该文件。**

---

## Task 7: 文档收口

**Step 1: 更新设计文档状态**

把 `2026-09-23-zero-allocation-key-design.md` 的
"**状态：** 设计完成，待执行" 改为实测后的实际状态，
并补入 Task 1/5/6 的实测数字（**只填实际测到的**）。

**Step 2: 按 Task 1 的判定决定 ② 是否提前**

若 Task 1 显示 L779（`targets`）占比 **>10%**，在文档中把设计 §8 备选 1
标注为"应优先于 ② 执行"，并说明它是**独立小任务**。

**Step 3: ②③ 的处置必须诚实**

- ② 的收益**只落在** `DecisionCpgFactory.BuildNodeKey` 的 **fallback 分支**
  （命中路径本来就已经 0 分配，实测 0.00 B/次）；
- `SyntaxId`/`TokenId` 是**死代码**，**不计入**收益；
- ③ **只省查表侧**，插入仍需真 `string`；
- ②③ **没有本次运行的分量证据**（它们未出现在归因表中）。

**Step 4: 更新 `docs/benchmarks/nlissn-version4-dop12-diagnostics.md`**

把报告里"优化方案（按收益/风险排序）"一节中**已落地**的项标注为已落地
（附 Task 6 的实测），**未落地**的保持原样。**不得**把未做的项写成已做。

**Step 5: 更新 `Context/progress.md`**

按"只保留仍影响当前工作的事实"原则改写，不追加日志。

**Step 6: 清理**

```powershell
Remove-Item -Recurse -Force 'Build\tmp\zerokey'
Select-String -Path 'src\NLCPG\Builder\NLCPGBuilder.cs' -Pattern 'NL_ZEROKEY|_nskCalls|ProbeDiff'
```

Expected：探针 0 命中；目录已删除。

---

## 完成条件

> **执行结论（2026-09-23）：目标未达成，按回退策略整体回退。**
>
> Task 0–6 全部执行完毕，**收益判定为不成立**（见 Task 6 Step 5）：
> 分配降 6.2%、端到端墙钟**不变或更慢**，而收益上限只有全运行的 1.74%。
> 产品代码已恢复到基线哈希 `EC2B867F…40539`，全量 Contract **448/448 通过**。
>
> 因此下列条件按"**回退后的终态**"逐条判定，而不是按"改动能否合入"判定。

全部满足才算完成：

1. ~~Task 5 的差分差异 = 0~~ → **已达成**（437,371 对，差异 0；真实诊断输入）。
   该结论**不因回退而失效**：它证明的是"结构体键语义正确"，而**不是**"值得采用"。
2. **Task 2 的保序快照全绿**，且 Step 2 已证明它**对顺序变化敏感**（临时改动后确实失败过）。
   → **已达成且已保留该文件**（它独立于本次改动仍有价值）。
3. ~~`NLCPGBuilder.cs` 中 `NodeSortKey` 只剩定义，6 个调用点全部替换~~
   → **已回退**：6 处调用点恢复 `NodeSortKey`，方法保留。这是回退后的**期望终态**。
4. **三条链的层级逐一核对**：`targets` **没有**被插入 `SpanStart` 层。
   → 已核对，回退后与基线一致。
5. 图等价 oracle 全绿 → **已达成**（含新增护栏，见条件 6 的实测）。
6. 全量 Contract **448/448 通过**（串行）。⚠ 并行下会随机失败 7–13 项，
   这是**既有**的 xUnit 隔离缺陷：已用**基线代码**复现同样失败，
   且单独跑那 3 个"疑似回归"用例连续 3 轮 3/3 通过 ⇒ **非本次引入**。
7. **Task 6 已给出实测**的 A−B 差额（DOP=1、交替采样），并已解释与微基准的偏离
   （见 Task 6 Step 5：O(n) 工作被挪进 O(n log n) 比较器，放大 4.6 倍）。
8. `check-harness-consistency.ps1` OK 且 `git diff --check` 退出 0。
9. **未新增 `InternalsVisibleTo`**（全程用 env 门控探针，未动程序集可见性）。
10. 未改 `NLCPGNode`/`NLCPGEdge` 公开形状、未改持久化 schema、
    未改 `StringInterner.Intern` 的分配路径。
11. `DataFlowPass.cs` 与 `NLCPGDataFlowSparseSetTests.cs` **未被本计划触碰**。
12. 所有临时探针已删除，`NLCPGBuilder.cs` 无探针残留 → **已验证 0 命中**。


## 回退策略

- **Task 4 整体可回退**：按批回退，每批回到
  `.ThenBy(… NodeSortKey …, StringComparer.Ordinal)` 即可；
  Task 3 新增的两个类型**留着不影响**（未被引用时零成本），
  但为保持工作区整洁应一并删除。
- **Task 5 未零差异前不得进入 Task 6。**
- 若 Task 1 显示 `targets` 占比 >10% ⇒ 先做结构性简化（设计 §8 备选 1），
  **不要**继续堆 ② 的改动。
- 若 Task 6 的实测收益显著低于微基准预期（例如 <1/3），
  **应停止并回报**，而不是继续扩大改动面。

## 已知风险与预判

| 风险 | 预判 | 处置 |
| --- | --- | --- |
| 共用一个 `Key` 形状 | 最隐蔽：`targets` 少一层 `SpanStart`，复用会让 `targets[0]` 改变（实测位置不同 39,985） | Task 4 分批 + 完成条件 4 |
| 护栏恒真（没有真在守） | **Task 2 已实测**：两次"看似合理"的扰动都是 no-op，会让人误以为护栏有效 | Task 2 Step 4：必须用"改组内区分键"的扰动证伪 |
| 还原后 mtime 未刷新导致跑旧 DLL | **Task 2 已实测踩到**，表现为"还原了却仍失败" | 工作规则：还原后刷新 mtime + 比对 DLL mtime |
| `Resolve` 落进 `CompareTo` | 锁调用放大 41.6×（该运行已有 858/2 s 峰值） | 不变式 3 + Task 5 对账调用次数 |
| `int` 缓冲过小 | `char[8]` 下 `TryFormat` **静默返回 0** ⇒ 键少一段 | Task 3 Step 1 要点 5；缓冲 12 + 检查返回值 |
| Culture 用 `Invariant` | `sv-SE`/`fi-FI`/`ar-SA` 负号不同 ⇒ 静默改序 | 不变式 5；只在 `zh-CN` 下测**不足以**发现 |
| 枚举静态表越界 | `(NLCPGNodeKind)99` ⇒ 表外访问 | 必须带回退（Task 3 要点 6） |
| 既有 oracle 对边序盲 | **已实测坐实**：扰动后 `CpgWorkBatchInterprocedural` 仍 4/4、`NLCPGPartitionedBuilder` 仍 39/39 | Task 2 专项护栏 |
| 并发写入者 | `DataFlowPass.cs` 等被他人持续修改 | 工作规则；Task 0 Step 2 |
| 全量 Contract 套件的**既有** xUnit 隔离缺陷 | 会随机失败 9–14 项，**非本次回归** | 见下 |
| DOP>1 时 `GetAllocatedBytesForCurrentThread` 不可信 | 只统计当前线程 | Task 6 Step 1：用 DOP=1 或改用进程级计数器 |
| ②③ 无分量证据却被当收益汇报 | 二者**未出现在**归因表中 | 完成条件 8 + Task 7 Step 3 |

**既有测试隔离缺陷（前序已定位，非本次引入）**：
`NLCPGDisplayTextTests` / `NLCPGProjectExportTests` 调用进程级
`Directory.SetCurrentDirectory()`，而 `NLCPGNodeIdContractTests` 传相对路径，
导致 `Path.GetFullPath` 按被改写的 CWD 解析，抛
`Stable anchor for '…' was not included in the supplied preallocated NodeId table`。
若本次全量 Contract 出现随机失败，**先用** `xunit.runner.json` 设
`parallelizeTestCollections: false` 复核**再判定是否回归**
（该文件放 `Build\test\Debug\net10.0\`，`Build/` 本就 gitignore，用完删除）。
