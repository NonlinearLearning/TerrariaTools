# declared-symbol 查询优化：测试交接文档

> **给接手的 AI**：本文档只讲**测试工作**。状态唯一来源是
> [`Context/feature_list.json`](../../Context/feature_list.json)（注意：`Context/` 被 gitignore，不在提交里）。
> 本文件面向"下一个要接着做测试验证的人"，不是变更日志。

日期：2026-09-26。feature：`minimal-roslyn-cpg-declared-symbol-query-optimization`。

---

## 0. 先读这三条硬性纪律（上一会话踩过坑）

1. **共享工作树有多个并发会话。改文件前先看 mtime**；一旦发现你要改的文件被他人动过，
   **立即停下报告，不要覆盖**。上一会话有两次因为没先看 mtime 而差点覆盖他人改动。
2. **HEAD 不是有效基线**（工作树有 300+ 未提交改动，来自其他工作流）。
   **禁止** `git reset --hard`、`git clean`、从 HEAD 整文件还原。
3. **只读核实优先**。声称"完成/通过"前必须给出**可复现证据**：TRX（含 sha256-16）、
   SHA256、实测数字。**"Build succeeded" 不是证据**；必须看到 `NLCPG -> ...dll` 重链接行。

---

## 1. 被测对象与它要护住的性质

优化点：`SyntaxPass` 与 `PartitionedSyntaxPass` **只对"可声明"的语法节点**调用
`semanticModel.GetDeclaredSymbol(node)`，同时**不丢声明边**。

白名单：`src/NLCPG/Builder/NLCPGBuilder.cs` 的 `CanDeclareSymbol(SyntaxNode)`
（当前 **24** 种 Kind；`CaseSwitchLabelSyntax` / `DefaultSwitchLabelSyntax` **不在其中**）。

**两个门是各自独立实现的，改一个不会修另一个**：

| 路径 | 门的位置 |
| --- | --- |
| legacy | `src/NLCPG/Builder/Passes/SyntaxPass.cs:326` — `if (cachedFacts is null && CanDeclareSymbol(syntax))` |
| partitioned | `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs:183` — `var queriedDeclaredSymbol = CanDeclareSymbol(syntax);` |

### ⚠️ 路径选择规则（实测，不是读来的）

**只有能力集决定走哪条语法路径**：请求的能力蕴含 `MethodModel` ⇒ `OperationRoots` 非空
⇒ 分区路径；只请求 `SyntaxSemantic` ⇒ legacy 路径。
**大文件阈值与路径选择无关** —— 用 3 能力集 × 2 源码规模 × 2 阈值（20/800）共 12 组全排列实测，
两个阈值结果**完全相同**。

⇒ **写测试时必须断言 `LastSyntaxPassTelemetry.Partitioned`**。
上一会话真踩过：请求 `SyntaxSemantic` 却以为在测分区路径，**实际静默跑了 legacy**，
于是"两条路径等价"退化成把同一条路径测两遍。

---

## 2. 关键陷阱：`DeclaresSymbol` 有**三个**发射点，只有一个是 DoD 的对象

这是上一会话查出的最重要的测试陷阱：

| # | 发射点 | 源节点 | 过 `CanDeclareSymbol`？ |
| --- | --- | --- | --- |
| ① | `NLCPGBuilder.cs:2848` | `SyntaxNode` | **是** ← DoD#1 的**唯一**对象 |
| ② | `NLCPGBuilder.cs:2861` | `TypeDecl` | 否（命名类型声明时派生的别名边） |
| ③ | `MethodDecorationPass.cs:151` | `Method` | 否（DoD#2 的对象） |

**实测（12 方法夹具，Roslyn 独立真值 = 39）**：

| 路径 | 总边 | 语法源① | TypeDecl② | Method③ | **余量** |
| --- | --- | --- | --- | --- | --- |
| legacy | 40 | 39 | 1 | 0 | 1 |
| partitioned | 52 | 39 | 1 | 12 | **13** |

⇒ **用"整图 `DeclaresSymbol` 总边数"做下界断言，在分区侧会被 ②③ 垫高 13 条**，
足以掩盖 13 条语法声明边的丢失 —— 而这正是 DoD#1 要护住的性质。
**正确做法：只数源节点是 `SyntaxNode` 的声明边。**

---

## 3. 现有测试资产

| 文件 | 规模 | 说明 |
| --- | --- | --- |
| `tests/NLISSN.ContractTests/Cpg/SyntaxPassTelemetryContractTests.cs` | 7 Fact + 5 Theory，~680 行 | 主套件 |
| `tests/NLISSN.ContractTests/Cpg/MethodDecorationTraversalContractTests.cs` | 4 Fact + 1 Theory，~239 行 | DoD#2 |

其中承担 DoD #1/#3 的四条**必须保住**（它们各自有变异证明）：

1. `BuildFromSource_DeclarationEdges_ArePreservedOnBothSyntaxPaths(bool partitioned)`
   — **只数语法源边**，两侧 `syntax-sourced == 39 == Roslyn 真值`。
2. `BuildFromSource_SyntaxPathSelection_IsGovernedByMethodModelCapability(cap, expectPartitioned)`
   — 钉住路径选择规则，并断言 `SyntaxNodeCount > 0` 防"读到默认值"。
3. `BuildFromSource_MethodDecorationContributesDeclaresSymbolEdgesFromNonSyntaxSources(bool)`
   — 钉住三发射点不变量，断言**无法解释的余量 == 0**（出现第四发射点即响亮失败）。
4. `BuildFromSource_SyntaxSubgraph_IsIdenticalOnBothSyntaxPaths`
   — 比较两条路径的**语法子图**（节点形态 + 两端皆语法节点的 `SyntaxChild` 边 +
   语法源声明边）。守护 `DescribeSyntaxSubgraph` 私有辅助。

**已有的 5 条 tripwire（边界记录，注释里已预言自己会失败）**：
若把 switch 两个 Kind 加入白名单，**确切会红 2 条**（3 个用例）：
`BuildFromSource_SwitchLabels_AreMaterialisedButCarryNoDeclarationEdge`、
`BuildFromSource_SwitchLabels_AddSyntaxNodesButNoDeclaredSymbolQueries`。
其余测试不受影响，原因：**所有钉绝对节点/边数或快照哈希的测试，夹具都不含 `switch`**。

---

## 4. 跑测试的正确姿势

```powershell
# 从仓库根执行。注意：不能传 -p:...（PowerShell 会把 p 解析为 -ProgressAction 歧义）
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore --property:UseSharedCompilation=false `
  --filter 'FullyQualifiedName~SyntaxPassTelemetryContractTests|FullyQualifiedName~MethodDecorationTraversalContractTests' `
  --logger 'trx;LogFileName=my-run.trx' --results-directory Build/M1-methoddecoration/trx
```

- 脚本会**串行化**在互斥锁 `Global\NLTX-DotnetBuild-...` 上；并发会话也会抢锁，
  日志出现 `Waiting for serialized dotnet mutex` **属正常**。
- `-o` 同样有歧义 → 用单个 token `"--output=$dir"`。
- 只跑**定向 filter**。**不要跑全量 tier**（用户明确禁止全量与本 feature 之外的测试）。

---

## 5. 变异测试配方（本 feature 的验收靠它）

**"断言必须落在被测对象上"** —— 上一会话记录过两次反例：
自己 `new` 一个容器自己清空再断言它是空的，**删掉产品的 `Clear()` 照样通过**。

可靠配方：改**产品文件** ⇒ 跑定向测试 ⇒ **必须失败** ⇒ 在 `finally` 中还原 ⇒ 复核哈希。

已验证可用的两个变异：

| 变异 | 应失败于 | 实测结果 |
| --- | --- | --- |
| `PartitionedSyntaxPass.cs:183` 门置 `false` | 语法子图等价测试 | **Failed**，`legacy=137 partitioned=125`（抓到 12 条丢边） |
| 把"语法源"集合清空 | 双路径声明边测试 | **Failed**（两个用例） |

⚠️ **变异后必须复核**：`Get-FileHash` 回到原值、`MUTATION` 标记数 = 0、无 `*.MUTATION-BACKUP` 残留。
⚠️ 变异代码要能**编译**：上一会话有一次变异写成 `syntax is not ParameterSyntax` 而
`CS0103` 编译失败 —— **那次什么都没证明**，不得当证据。

---

## 6. 已确认的边界与未闭合项（不要重复调查）

### 已裁决：不是缺陷

- **switch 标签不建声明边** = **信息缺失，不是图不一致**。
  `GetSymbolInfo(gotoStatement).Symbol` 在 `goto case`/`goto default` 上**恒为 null**
  ⇒ 被跳过的标签**全部引用不可达** ⇒ DoD#1 的"保声明边"对**每个可达声明都成立**。
- **加 switch 白名单不是"只加边"**：每个标签 **+1 `SymbolUnknown` 节点**、
  +1 `DeclaresSymbol`、+1 `ContainsSymbol`；因节点身份**先按 Kind 排序**，
  会**重排 NodeId** 并改变任何含 `switch` 图的 `GraphSnapshotVersion`（预分配/流式模式也一样）。

### 未闭合（需他人裁决，**不是测试能关掉的**）

| # | 事项 | 为何关不掉 | 需要 |
| --- | --- | --- | --- |
| A | DoD#4 **墙钟**子句 | 本机噪声 ~3.5% ≫ 效应 ~1%；**阳性对照已证明**连已知 +3.5% 注入都分辨不出 | 换测量方法，或验收方改判 |
| B | M1 第 6 轴（阶段耗时）`Inconclusive` | 需改**他人持有**的 `NLCPGBuilder.cs` | 文件所有者授权 |
| C | switch 是否入白名单 | **产品语义设计裁决** | 实现者裁决 |

### 已知的 9 项白名单覆盖缺口（可按需补测）

24 项白名单中仅约 **15** 项有按种类的声明边断言（唯一一条是
`NLCPGPartitionedBuilderTests.cs:548-612`）。**9 项无覆盖**：
- **任何夹具都不存在（6）**：`ExternAliasDirective`、`ForEachVariableStatement`、
  `FromClause`、`JoinClause`、`LetClause`、`QueryContinuation`
- **存在但无断言依赖其声明边（3）**：`UsingDirective`、`ForEachStatement`、`CatchDeclaration`

⚠️ **写这些测试时的尖锐性提醒**：现有 `catch (Type)` **不带标识符**、普通 `using System;`
**解析为 null** ⇒ 即便入白名单也**产不出声明边**。非空洞的新测试必须用
**带标识符的 catch**（`catch (Exception ex)`）与**别名形式 using**（`using X = Y;`）。

---

## 7. 当前基线哈希（对齐用）

| 文件 | sha256-16 |
| --- | --- |
| `src/NLCPG/Builder/Passes/SyntaxPass.cs` | `4114BE9C9C567247` |
| `src/NLCPG/Builder/Passes/PartitionedSyntaxPass.cs` | `85049E64798D8561` |
| `src/NLCPG/Builder/Passes/MethodDecorationPass.cs` | `805F99E83ADFD345` |
| `tests/.../SyntaxPassTelemetryContractTests.cs` | `D12F342B24289A44` |
| `tests/.../MethodDecorationTraversalContractTests.cs` | `3F0200FCA5C776B2` |

最近一次绿：`Build/M1-methoddecoration/trx/FINAL-round13.trx`（`5B7C5C424BC9BC4A`）**23/23**。

---

## 8. 给接手者的建议起点

1. **先跑一次第 4 节的定向命令**，确认基线 23/23 仍绿（若红，先查是否他人改了产品文件）。
2. **补第 6 节末尾的 9 项覆盖缺口** —— 这是当前唯一**明确、有界、且不依赖任何裁决**的测试工作。
   建议按"能产出真实声明边的 3 项"（用尖锐化夹具）优先，再处理需要新夹具的 6 项。
3. **不要**再重复"审已有断言是否空洞"——上一会话已连审 5 轮，边际收益已耗尽。
