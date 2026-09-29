# 零分配键设计：排序键、字典键与查表键

**状态：** ⚠ **已执行并被实测否决（2026-09-23）。核心结论是"不值得做"，产品代码已回退。**

Task 0–6 按 `2026-09-23-zero-allocation-key-execution.md` 全部执行完毕，
**① 排序键的结构体实现语义完全正确，但收益不成立，已整体回退**：

- **正确性 ✅**：在 `Version4/Terraria/Player.cs` 上 **437,371 对逐对比较、差异 0**；
  用字符串键重跑整条 8 层链**逐元素相同**（§3.1 的三条不变式经受住了实测）。
- **收益 ❌**：pass 内分配只降 **6.2%**，而端到端墙钟**不变或更慢**
  （分段比较 +2.87~5.90%，渲染+SIMD +0.60~1.03%）。
- **根因（本设计最重要的教训）**：本文 §2.4 的前提"字符串只提供一次性堆身份，
  搬到栈上就净赚"**是错的**。字符串键在**构造期**渲染一次（`n` 次）；
  结构体键把渲染搬进了 `CompareTo`，同一份工作被做了 **`n log n`** 次——
  实测 `n=20162` 时 **构造 81,557 次 vs 比较 374,742 次 = 放大 4.6 倍**。
  **比较器里的每一纳秒都要乘 `log n`；栈上的字节并不比堆上的便宜。**
- **量级上限**：§1 已记录的分母警告才是决定性的——同一笔 1.632 GiB
  只占全运行 93.73 GiB 的 **1.74%**。即使把它**完全清零**，
  最乐观也只有 ~1.7%，而实测连这个都没拿到。

下文保留为**设计推理与验证方法的完整记录**（尤其 §4.3 的测试缺口、
§3.1 的三条不变式、以及"如何证明一个键实现等价"的方法），
但请把 §2.4/§3.1 的性能预期读作**已被证伪**，而不是待办。

**未受影响的结论：** §4.3（既有 oracle 对边顺序是盲的）已被**独立实测坐实**，
并新增了专门的保序护栏测试（见执行计划 Task 2），该护栏**已保留**。

**原始设计说明：** 本设计**不含任何生产代码改动**；
下文实测数字分两类口径，已在 §5 逐条标注来源：
① **本次运行归因**（`D:\TRbackup\Version4` DOP=12 诊断运行的 `nettrace`/`gcdump` 证据）；
② **本机微基准**（`%TEMP%` 下 net10.0 探针，100 万次/项，Server GC），仅用于比较相对代价。

**目标：** 把三处"为了传给某个容器而临时构造 `string`"的分配降为 **0 B/次**，
在不改变任何图语义、边集合、边顺序或持久化格式的前提下：

| # | 场景 | 现状 | 目标改法 | 省 |
| --- | --- | --- | --- | ---: |
| ① | 排序键 `NLCPGBuilder.NodeSortKey` | 拼 148 字符键 | `readonly struct` + `IComparable<T>`，`ref struct` 游标逐字符走虚拟拼接流 | **320 → 0 B/次** |
| ② | 字典键 `BuildNodeKey` | 拼键字符串 | `readonly record struct` 做键 | **160 → 0 B/次**（`CoverageEvidence` 变体 216） |
| ③ | 查表 `StringInterner` | 先造 `string` 再查 | `GetAlternateLookup<ReadOnlySpan<char>>` | **48 → 0 B/次** |

**范围：**
- ① 只改 `src/NLCPG/Builder/NLCPGBuilder.cs` 的 `NodeSortKey`（L1053-1056）与 6 个调用点（L769/779/877/878/881/882），新增一个 `internal` 只读结构体与一个 `internal ref struct` 游标。
- ② 只改键的**表示**，不改键的**取值语义**（`DecisionCpgFactory.BuildNodeKey`、`CoverageEvidence.BuildNodeKey` 的字符串结果必须逐一可复现）。
- ③ 只给 `StringInterner` 增加**按 span 查询**的重载，**不改** `Intern(string?)` 的分配路径。
- **不**改 `NLCPGNode`/`NLCPGEdge` 的公开形状，**不**改 `StringInterner` 的存储（表内仍存 `string`），**不**新增 `InternalsVisibleTo`。

**不做的事**：本设计**不**声称这次运行的内存峰值会下降。三处都是**临时对象**，
只影响 GC 压力（gen0/gen1 频率与暂停），**不改变任何存活对象**（见 §5.4）。

**设计依据：** 本文件；事实基础与归因见
[Version4 DOP-12 诊断报告](../benchmarks/nlissn-version4-dop12-diagnostics.md)。
内存侧（存活对象）的优化另见
[Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)，
两者**不重叠**（见 §9）。

---

## 1. 前序状态与剩余问题

诊断运行（`D:\TRbackup\Version4`，DOP=12，1042 s，967 个 `.cs`）已确立：

- 累计分配 **93.73 GiB**，`GC/AllocationTick` 采样窗口内 **11.30 GiB**；
- `System.String` 在**每个采样点都是实例数第一**（峰值样本 484,208 个 / 1.47% 字节）；
- 字符串分配的**归因**（60.03 s 窗口，19.60% 的字节 = 2.215 GiB）已定位到**少数几个纯工具函数**。

**剩余问题**：这些函数的产物**只被读一次就丢弃**，却付出了完整的堆分配代价。
`System.String` 的"实例数第一"不是因为它被长期持有，而是因为**同一批文本被反复拼进临时键**。

**本设计要回答的问题**：能否在不改动任何容器、不改变任何顺序语义的前提下，
把这三处的分配降为 0，并**证明**顺序语义没有变化。

**答案：能。** 三处都有可编译、可穷尽验证的零分配等价形式（§3、§5.2）。

---

## 2. 实测发现（本设计的事实基础）

### 2.1 归因：三处各自的确切分量

`dotnet-trace --profile gc-verbose` 的 `GC/AllocationTick`（61,433 事件、61,432 带完整调用栈）
在 60.03 s 窗口内归因如下（**字符串**分配，共 2.215 GiB）：

| 函数 | 字节 | 占字符串 churn | tick 数 |
| --- | ---: | ---: | ---: |
| `NLCPGBuilder.NodeSortKey` | **1.632 GiB** | **73.65%** | 16,480 |
| `NLCPGCallSiteContext.ToContextId` | 0.322 GiB | 14.52% | 3,244 |
| `PostRewriteDiagnostics.BuildStableDiagnosticKey` | 0.042 GiB | 1.90% | 427 |
| `NLCPGBuilder.SymbolId` | 0.036 GiB | 1.63% | 363 |

> **口径纪律（三个分母必须同时给出）**：`NodeSortKey` 的 1.632 GiB
> = **73.65%** 的**字符串 churn** = **14.44%** 的**该 60.03 s 窗口**字节（1.632/11.30）
> = **1.74%** 的**全程 93.73 GiB**。
> 分子只覆盖 1042 s 中的 60.03 s（5.8%），且分配速率极不均匀
> （峰值 3.24 GiB/s，长尾 ≈0.03 GiB/s）⇒ **禁止线性外推**。

**本文只处理 `NodeSortKey`（①）。** ②③ 两处**当前没有本次运行的分量证据**——
`BuildNodeKey` 与 `StringInterner` 都**没有出现在**该归因表的任何一行。
这点必须说清：②③ 的收益是**微基准证明的"每次调用省多少"**，不是"本次运行省多少"。

### 2.2 `NodeSortKey` 的形状与调用点

```csharp
// src/NLCPG/Builder/NLCPGBuilder.cs:1053-1056
private static string NodeSortKey(NLCPGGraph graph, NLCPGNode node)
{
    return $"{node.Kind}|{graph.ResolveFullName(node)}|{graph.ResolveName(node)}|{graph.ResolveFilePath(node)}|{node.SpanStart}|{node.SpanEnd}";
}
```

全仓共 **7 处**：定义 1 处 + 调用 6 处，**全部**在
`RunInterproceduralDataFlowPass`（L713 起）内：

| 调用点 | 所在链 | 该链的完整层级 |
| --- | --- | --- |
| L769 | `orderedCallSites`（L765-770） | `FullName` → `SpanStart` → **`NodeSortKey`** |
| L779 | `targets`（L774-781） | `FullName` → **`NodeSortKey`**（**无 `SpanStart` 层**） |
| L877 | `orderedPlans`（L873-883） | `StableCallSiteOrder` → `BridgeKind` → **`NodeSortKey`(CallSite)** → **`NodeSortKey`(TargetMethod)** → `ArgumentOrdinal` → `BridgeKind`(重复) → **`NodeSortKey`(Source)** → **`NodeSortKey`(Target)** |

**三条链的形状互不相同**——这是本设计最容易被误判为"同一件事"的地方（§4.2）。

### 2.3 每次调用的真实代价

`NodeSortKey` 每次调用要：**3 次带锁的文本解析** + **1 次六段插值**。

- `NLCPGGraph.Resolve(uint id)`（`NLCPGGraph.cs:197-200`）→ `_stringInterner.TryResolve(...)`，
  **每次调用都取 `lock (_gate)`**（`StringInterner.cs:44`）。
- 本次运行 `dotnet.monitor.lock_contentions` 峰值 **858/2 s**，总计 **20,494**，
  其中 **43.2% 落在同一个 60 s 窗口内** ⇒ 锁是真实热点，不是理论担忧。
- 插值实测 **295–296 B/次**（由运行反推：1.632 GiB ÷ 592–594 万次），
  与微基准的 320 B/次（148 字符键）同量级，差异来自样本键长不同。

### 2.4 三处共有的原理：`string` 只提供了一次性的"堆身份"

`string` 是**不可变堆对象**，堆尺寸实测 `align8(22 + 2n)`：

| 字符数 n | 实测 B | `24 + 2n` | `align8(22 + 2n)` |
| ---: | ---: | ---: | ---: |
| 1 | 24.0 | 26 | 24 |
| 8 | 40.0 | 40 | 40 |
| 10 | **48.0** | **44** | **48** |
| 36 | 96.0 | 96 | 96 |
| 60 | 144.0 | 144 | 144 |
| 148 | 320.0 | 320 | 320 |

> ⚠ **报告别处使用的 `24 + 2n` 是简化式**，只在 `n % 4 == 0` 时成立；
> `n=10` 时公式给 44 而实测 **48**。**通用式是 `align8(22 + 2n)`**。

那笔分配**唯一**的作用是给值一个"能进堆容器的引用身份"。而三处**都只用一次就丢弃**：
① 被 `StringComparer.Ordinal` 读一次、② 被 `Dictionary` 哈希一次、③ 查表一次。
**没有任何一处需要长期持有** ⇒ 堆对象是纯搬运损耗 ⇒ 把身份降到**栈上字节**。

> ⚠ **本节结论已被实测证伪（2026-09-23），是本设计最大的错误，务必读完再动手。**
>
> 上面那句"⇒ 把身份降到栈上字节"只算了**分配**这一项，漏掉了**次数**：
>
> | | 字符串键（现状） | 结构体键（本设计） |
> | --- | --- | --- |
> | 渲染/解析发生几次 | **`n` 次**（构造期，每元素一次） | **`n log n` 次**（比较期） |
> | 比较用什么 | `string.CompareOrdinal`（**SIMD**，8~16 B/次） | 逐段/逐字符比较 |
>
> `n=20162` 实测：**构造 81,557 次 vs 比较 374,742 次 ⇒ 放大 4.6 倍**。
> 于是"省下 `n` 次分配"换来了"多做 4.6 倍的键渲染"。
>
> **实测三档实现，没有一档赢过字符串键**（端到端墙钟）：
> 逐字符游标 **+12.9×**（pass 内）→ 分段比较 **+2.87~5.90%** → 渲染+SIMD **+0.60~1.03%**。
> 分配在 pass 内只降 **6.2%**，而这一笔在全运行里本来只占 **1.74%**。
>
> **正确的取舍判据**：只有当"每次比较的成本"**不高于** `CompareOrdinal`，
> 并且"构造次数 × 单次省下的字节"足够大时，"零分配键"才成立。
> 对**排序键**（比较次数 ≫ 元素数）这两个条件都难以同时满足。
> 对**字典键/查表键**（②③：每次操作只查一次，比较/哈希次数与构造次数同阶）——
> 本节的推理**仍然可能成立**，但那两条**没有本次运行的分量证据**（见 §5.3）。

### 2.5 为什么"0"是干净的 0

因为这三处的**结果对象本身就是浪费**，不是"还能更省的浪费"。
一旦不再构造它，分配就是字面意义的 0，**而不是降到某个更小的常数**。

### 2.6 `StringInterner` 的现状（③ 的对象）

```csharp
// src/NLCPG/Model/StringInterner.cs
public sealed class StringInterner
{
    private readonly object _gate = new();
    private readonly Dictionary<string, uint> _idsByText = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> _textsById = new();
    private uint _nextId = 1;

    public uint Intern(string? text) { /* L13, lock L20 */ }
    public bool TryResolve(uint id, out string? text) { /* L36, lock L44 */ }
}
```

只有 **2 个 public 成员**，**没有** `Clear`/`Remove`/`Dispose`，`_nextId` 只增不减。
实例：每个 `NLCPGGraph` 一个、每个 `StableNodeIdentityFactory` 一个、
一个 `static readonly DecisionStringTable`（`DecisionModel.cs:705`），
以及 `CpgFrozenShardGraphReader.cs:16,129,192`、`CpgShardRelationQueryService.cs:159`、
`NLCPGSliceQuery.cs:231`。

> **峰值样本的 interner 容量只有 10,352 槽**（`Entry<string,uint>[]` = 248,448 B），
> 而同期存活 `string` 是 **484,208 个** ⇒ **interner 覆盖率 ≤2.14%**。
> 即 ③ 的杠杆**很小**：表本身就是 **485 KB = 峰值堆 3,860.9 MB 的 0.0123%**。
> ③ 的价值在**调用侧的临时 `string`**，不在表本身（§5.4）。

---

## 3. 目标设计

### 3.1 ① 排序键：结构体键 + 六段游标

**原理**：排序只需要一个**全序**，不需要真的把 148 个字符拼出来。
把 `NLCPGNode` 的 6 个字段装进 `readonly struct`，让 `CompareTo` 逐字符产出虚拟拼接流：

```csharp
internal readonly struct NLCPGNodeStreamKey : IComparable<NLCPGNodeStreamKey>
{
    private static readonly string[] KindTexts = BuildKindTexts();

    private readonly string _kindText;
    private readonly string? _fullName, _name, _filePath;   // Resolve* 返回 string? ⇒ 必须可空
    private readonly int? _spanStart, _spanEnd;

    // ★ 关键：Resolve 只在【构造时】调用（O(n)），绝不放进 CompareTo
    public NLCPGNodeStreamKey(NLCPGGraph graph, NLCPGNode node) { /* 3 次 Resolve + Kind 文本 */ }

    public int CompareTo(NLCPGNodeStreamKey other)
    {
        Span<char> ls = stackalloc char[12], rs = stackalloc char[12];
        Span<char> le = stackalloc char[12], re = stackalloc char[12];
        var lc = new SegmentCursor(_kindText, _fullName, _name, _filePath,
            ls[..FormatInt(_spanStart, ls)], le[..FormatInt(_spanEnd, le)]);
        var rc = new SegmentCursor(other._kindText, other._fullName, other._name, other._filePath,
            rs[..FormatInt(other._spanStart, rs)], re[..FormatInt(other._spanEnd, re)]);
        while (true)
        {
            var hasLeft = lc.TryNext(out var left);
            var hasRight = rc.TryNext(out var right);
            if (!hasLeft) { return hasRight ? -1 : 0; }
            if (!hasRight) { return 1; }
            if (left != right) { return left < right ? -1 : 1; }
        }
    }
}

internal ref struct SegmentCursor   // 六段游标：段内逐字符，段间产出 '|'，空段照样占位
```

完整实现（含 `FormatInt`/`KindText`/`BuildKindTexts` 与 `SegmentCursor` 全文）见
诊断报告 `#### 逐字符虚拟流的具体实现`。

**替换点（只改 3 个表达式，链条层级一律不动）**：

| 位置 | 现在 | 改成 |
| --- | --- | --- |
| L769 | `.ThenBy(node => NodeSortKey(graph, node), StringComparer.Ordinal)` | `.ThenBy(node => new NLCPGNodeStreamKey(graph, node))` |
| L779 | `.ThenBy(target => NodeSortKey(graph, target), StringComparer.Ordinal)` | 同上（此链**无** `SpanStart` 层） |
| L877/878/881/882 | `.ThenBy(plan => NodeSortKey(graph, plan.X), StringComparer.Ordinal)` | `.ThenBy(plan => new NLCPGNodeStreamKey(graph, plan.X))` |

### 3.2 ① 的三条不变式（正确性的全部来源）

1. **逐字符等价**：`CompareTo` 的返回值必须与
   `string.CompareOrdinal(NodeSortKey(a), NodeSortKey(b))` 的**符号**逐一相同。
   不得按字段做 `CompareOrdinal`——`'|'`(0x7C) 只是普通字符，字段边界是**软**的。
2. **段序与段数固定**：`Kind|FullName|Name|FilePath|SpanStart|SpanEnd`，**6 段 5 分隔符**，
   一处不减。空段（`null`）产出 0 个字符但**仍产出分隔符**。
3. **`Resolve` 在构造期**：`CompareTo` 内**不得**出现 `graph.Resolve*`。

### 3.3 ② 字典键：`readonly record struct`

`record struct` 自动生成 `IEquatable<T>`、`GetHashCode`、`==`/`!=`，
把字段按值内联进 `Dictionary.Entry[]`（实测键尺寸 **12 B**，**不是独立堆对象**）。

**候选点（按当前实际分量排序）**：

| 键表达式 | 位置 | 实测 B/次 | 分配次数 | 键长 |
| --- | --- | ---: | ---: | ---: |
| `DecisionCpgFactory.BuildNodeKey(SyntaxNode)` | `DecisionModel.cs:778-781` | **160.00** | 1 | 66 |
| `CoverageEvidence.BuildNodeKey` | `CoverageEvidence.cs:111-119` | **216.00** | **4** | 63 |
| `SymbolId` fallback | `NLCPGBuilder.cs:1686` | 147.27 | 2 | 56 |
| `SyntaxId` | `NLCPGBuilder.cs:1702-1705` | 168.00 | 1 | 73 |
| `TokenId` | `NLCPGBuilder.cs:1707-1710` | — | 1 | — |

**必须先做的甄别（否则会改错地方）**：

- **`SyntaxId`/`TokenId` 是死代码**：全仓（`src` + `tests`）grep 只有定义，**0 个调用点**。
  ⇒ **不改**，也不把它们的 168 B/次计入收益。
- **`DecisionCpgFactory.BuildNodeKey(NLCPGNode)`（L784-792）不该按"拼键"算**：
  它**先查驻留表**，命中时 `return fullName` 直接返回**已驻留的 `string` 引用**，
  实测 **0.00 B/次——本来就已经是零分配**。只有未命中才走 fallback（168.03 B/次，2 次分配）。
  ⇒ **收益只落在 fallback 分支**，改造必须保持"命中即返回原引用"的行为。
- **`BuildNodeKey` 已有三处返回元组、本来零分配**（`ExpressionFlowPropagationRuleBase.cs:137`、
  `LiftedMarkFacts.cs:87`、`DeclarationSymbolReferencePropagationRule.cs:148`，
  均返回 `(int Start, int Length, int RawKind)`）。
  ⇒ 说明本模式**已有先例**；但它们**丢了 `FilePath`**，
  只适合**单文件内去重**，**不能**直接替换含 `SyntaxTree.FilePath` 的跨文件变体。

### 3.4 ③ 查表键：`GetAlternateLookup<ReadOnlySpan<char>>`

表里**照旧存 `string`**（驻留必须可变长持有），但**查询**接受 span：

```csharp
// StringInterner 新增（不改 Intern 的分配路径）
private readonly Dictionary<string, uint>.AlternateLookup<ReadOnlySpan<char>> _lookup;
public bool TryGetId(ReadOnlySpan<char> text, out uint id) => _lookup.TryGetValue(text, out id);
```

**只省查表侧**：往表里**插入**仍然必须给出真正的 `string`。
实测 `alt.TryGetValue(span)` = **0 B**；`dict.TryGetValue(new string(span))` = **48 B**（10 字符）。

### 3.5 三个不可省略的前提（缺一个就不是 0）

1. **必须实现 `IEquatable<T>`/`IComparable<T>`，不能只靠非泛型版。**
   普通 `struct` 未实现时 `EqualityComparer<T>.Default` 退化为 `ObjectEqualityComparer<T>`，
   实测**每次查表装箱 4 次 = 96 B/次**；`record struct` 自动生成 ⇒ **0 B**。
   这也解释了"结构体键有时反而更慢"。
2. **字段必须是值类型。** 结构体内含 `string`（如持 3 个 `string` 的 `StrKey`）时，
   结构体本身不分配，但**那几个 `string` 一分没省**。
   本仓库能走这条路，是因为 `NLCPGNode` 的 5 个文本字段**全是 `uint` id**。
3. **`string.GetHashCode` 不缓存。** 实测 8 字符 21.1 ns、4000 字符 2648.0 ns
   （长度差 500 倍、耗时差 125.5 倍）⇒ 每次查表都重算全部字符。
   故结构体键还顺带省 CPU：`RecKey.GetHashCode` 1.9 ns vs 28 字符 string 17.6 ns（约 **9 倍**）。

---

## 4. 必须正面处理的约束

### 4.1 无 `InternalsVisibleTo`：只能经公开 API 验证（③ 的直接后果）

`src/NLCPG` **没有任何 `InternalsVisibleTo`**（全仓 11 处，全部位于
`src/NLISSN*` 的 `Properties/AssemblyInfo.cs`；`src/NLCPG` 下无 `AssemblyInfo.cs`）。
且 `tests/NLISSN.ContractTests/Architecture/ArchitectureBoundaryTests.cs`
对程序集可见性有**既有断言**。

**裁定：不新增 `InternalsVisibleTo`。** 因此：
- 契约测试只能经 `NLCPGBuilder` 公开 API 观测（图快照 + `LastBuildMetrics`）；
- ① 的**逐元素差分**验证用**临时 env 门控探针**完成，验证后删除（§5.1 手法）；
- ②③ 若无法经公开 API 观测，**不为其新增测试**，改为"可编译 + 微基准等价"证据。

> `NLCPGNodeStreamKey`/`SegmentCursor` 声明为 `internal` 即可——
> 它们只被同程序集的 `NLCPGBuilder` 使用，**不需要**测试直接访问。

### 4.2 三条键链形状不同 ⇒ **不能共用一个 `Key`**

这是本设计**最高风险的误用**：把 `orderedCallSites` 的形状复用给 `targets`。
`targets`（L778-779）比 `orderedCallSites`（L767-769）**少一层 `SpanStart`**，
复用会**多比一层**，实测 N=40,000 位置不同 **39,985**。

**具体翻转反例**：固定 `Full=F`，A=`SpanStart 9, Kind=Method`、B=`SpanStart 10, Kind=CallSite`：

| 链 | 比较顺序 | 结果 |
| --- | --- | --- |
| 现状 `targets` | `Kind` 文本：`'M'` > `'C'` | **B 在前** |
| 复用 `orderedCallSites` 形状 | 先比 `9 < 10` | **A 在前（结论相反）** |

⇒ **`NodeSortKey` 的替换必须逐调用点进行，且每处保持本链原有层级。**

### 4.3 现有 oracle **检测不到边顺序变化**（本设计最重要的测试缺口）

`CpgWorkBatchInterproceduralTests.DescribeGraph`（L104-123）在比较前**对边做了排序**：

```csharp
var edges = graph.Edges
  .OrderBy(edge => edge.SourceNodeId)
  .ThenBy(edge => edge.Kind)
  .ThenBy(edge => edge.TargetNodeId)      // ★ 排序抹掉了插入序
  ...;
return nodes.Concat(edges).ToArray();
```

**结论：现有跨 DOP oracle 对"边顺序变了"是盲的。**
而本设计的 ① **恰好会改变边的产出顺序**（`orderedPlans` 的次序决定
`PublishInterproceduralPlans` 的 `AddEdge` 顺序，而 `AddEdge` 是 **append-only**、
插入序可经 `PendingEdges`/`Edges` 观测）。

因此 **① 不能只靠现有 oracle 判定正确**，必须另建**保序**快照（§执行计划 Task 2）。
这也是为什么本设计把"逐字符等价"定为**不变式 1** 而不是"风格选择"。

### 4.4 `orderedPlans` 有一处冗余层级（顺带发现，**本次不修**）

`orderedPlans`（L873-883）中 `BridgeKind` 被比较了**两次**（L876 与 L880）。
它是**冗余但不改变结果**的层。**本次不删除**：① 它不产生分配（枚举比较）；
② 删除会改变 `ThenBy` 层数，属独立语义改动，应单独评估。

### 4.5 `targets` 排序在生产默认值下**负载为 0**（**本次不改**；本节已于 2026-09-23 实测修正）

`targets`（L774-780）的结果**一定被丢弃**：`MaxCallTargetsPerSite` 默认 **1**
（`NLCPGBuilderOptions.cs:160`），L789 对 `targets.Length > 1` 直接
`RecordCut("AmbiguousTarget")` + `continue`，故能到 L795 `targets[0]` 的
**必是长度 1 的数组**（`Distinct()` 在 L777，早于 L789 的长度判定）。

> ⚠ **修正**：本节早先版本称"**它是非零成本**……L779 的 `NodeSortKey` 是'算了就扔'"，
> 并据此把"改成先判长度"列为一项省 1 次 `NodeSortKey` 的独立小任务。
> **该结论已实测否定。**

**Task 1 实测（DOP=1，单文件 10 个方法，真实构建）**：

```
calls=494  callSitesL769=22  plansL877_882=472  fromTargetsL779=0
loopIters=22  t0=0  t1=22  tGt1=0  plansEntered=21  orderedPlans=118
maxCallTargetsPerSite=1
```

自洽校验 `22 + 472 = 494` ✓。关键在 **`t1=22 / tGt1=0`**：22 个 call site 的
`targets` 数组**全部长度恰为 1**，而 **`OrderBy`/`ThenBy` 对 `n ≤ 1` 完全短路**——
BCL 实测（.NET 10.0.11）：`n=0` → 选择器 **0** 次、`n=1` → 选择器 **0** 次、
`n=2` → 2 次、`n=1000` → 1000 次（均为 **1.00n**）。

⇒ **L779 的 `NodeSortKey` 在生产默认值下调用 0 次**，那一行的负载体现在
`.Distinct()`/`.OrderBy(ResolveFullName)`/`.ToArray()` 上（仍会分配数组），
**不在 `NodeSortKey` 上**。

**因此**：设计 §8 早先列的"备选 1（`targets` 结构性简化，可省 1 次 `NodeSortKey`）"
**收益为 0，应从优先级中移除**。真正的大头是下节。

### 4.6 真正的大头是 `orderedPlans`（**Task 1 新发现**）

实测分解：`plansL877_882 = 472/494 = **95.5%**`，`callSitesL769 = 22/494 = 4.5%`，
`fromTargetsL779 = 0%`。

⇒ `NodeSortKey` 的分配几乎**全部**来自 `orderedPlans`（L873-883）的**四个** `ThenBy`。
优化优先级应为：`orderedPlans`（4 处）≫ `orderedCallSites`（1 处）> `targets`（0 处）。

全仓搜 `MaxCallTargetsPerSite` 仅 **L668（日志）/L789（判定）/L160（定义）** 三处
（其余命中在 `Build/PerformanceResults` 的 JSON 与 `Miscellaneous/tmp` 的源码副本里），
**无任何测试或配置把它改成 >1**，`NLCPGInterproceduralDataFlowOptions` **无显式构造点**
⇒ 生产恒为默认值；本次已在**运行时确认** `maxCallTargetsPerSite=1`（原为源码推断）。

**裁定：本次仍按 §3.1 统一改为结构体键**（4 处 `orderedPlans` 是主要收益来源），
**不**顺手把 `targets` 改成"先判长度再取 `targets[0]`"——它现在**收益为 0**，
仅剩可读性价值，属独立改动。

---

## 5. 实测基础与验证边界

### 5.1 本文实测数字的来源（可复现）

| 类别 | 来源 | 可复现性 |
| --- | --- | --- |
| 归因（1.632 GiB / 73.65%） | `D:\TRbackup\Version4\Build\NL-diagnostics-dop12\version4-dop12-20260922-173202\Trace\allocation.nettrace`（46.39 MB） | 需 `dotnet-trace` 解析；探针脚本不在仓库 |
| interner 容量 10,352 槽 | 同目录 `GcdumpTypeStats/gcdump-type-totals-per-sample.csv`（150,473 行） | 可直接读 |
| 锁竞争 858/2 s、20,494 | 同目录 `Counters/` | 可直接读 |
| B/次、ns/次、等价性穷举 | `%TEMP%` 下 net10.0 探针（**未进仓库**） | 需重建探针；口径见下方 ⚠ |

> ⚠ **微基准口径**：net10.0（`10.0.11`）、Release、Server GC、每项 100 万次、
> 先跑一轮预热再 `GC.Collect()` 取基线（零分配基线实测 0.00 B）。
> 样本取本仓库真实形状（`Full`=108、`Name`=24、`Path`=48 ⇒ 148 字符键）。
> **微基准只用于比较相对代价，不是本次运行的归因。**

### 5.2 能证明

- ① **逐字符等价**：穷举有序对 **67,600 对，不同 0**（含 `null`/空串/字段内 `|`/负数/未定义枚举）；
  N=6,000 真实形状下一级排序与完整三级链**次序相同**；
  比较本身分配 **0 B**（52,000 次比较）；三级链端到端 **10.3 → 5.4 ms**、
  **2,669.9 → 821.1 KB**。
- ① **可编译**：骨架用仓库**真实类型**（`ProjectReference` → `src/NLCPG`，
  `Nullable=enable` + `LangVersion=preview`）编译 **0 错误 0 警告**。
- ②③ **每次调用的省量**：`160/216/147.27 → 0 B/次`、`48 → 0 B/次`（微基准）。
- ② 若干键表达式**已经零分配**（`BuildNodeKey(NLCPGNode)` 命中路径 0.00 B/次；
  三处元组版 `BuildNodeKey`）。

### 5.3 不能证明 / 必须额外测量

- **全程内存峰值不会因本设计下降。** ① 的对象是**临时**的（`OrderBy` 的键只在排序期存活），
  不改变任何**存活**对象；② 的键内联进 `Entry[]`（本来就存活），
  仅当**键被长期持有**时才省下存活字节。
- **① 对 CPU 的净影响未在真实负载下测过。** 单次比较实测**变慢 3.28×**
  （拼键 2,081 ns/call vs 逐字符 201.1 ns 的比较，但省掉 2,081 ns 的拼装），
  微基准端到端是净快 47.6%，**真实 DOP=12 负载下的收益未测**。
- **②③ 没有本次运行的分量证据**（§2.1）：它们**未出现在**归因表中，
  所以"能省多少"只能按**调用次数 × 每次省量**估算，而**②③ 的调用次数仍未测**。
  （**① 的调用次数已于 2026-09-23 实测**，见 §4.5/§4.6 与执行计划 Task 1：
  单文件 `calls=494`，其中 `orderedPlans` 95.5% / `orderedCallSites` 4.5% / `targets` 0%。）
- **① 的调用次数只有单文件证据。** `calls=494` 来自**一个**含 10 个方法的文件；
  跨文件、大项目下的**总量与结构占比均未测**（§8 备选 6）。
- **边顺序变化对下游的影响未测。** §4.3 已证明现有 oracle 对此是盲的；
  即使结构体键**被证明与字符串键逐字符等价**，边顺序也应**保持不变**
  （等价 ⇒ 同序），但这一推论**需要在 Task 5 用保序快照实测确认**，不能只靠推理。

### 5.4 不得声称

1. 不得声称本设计**降低本次运行的内存峰值**或改善 §报告的"内存墙"。
2. 不得把 `73.65%` 说成"全程的 73.65%"（它是 **60.03 s 窗口内字符串 churn** 的占比，
   等于全程 **1.74%**）。
3. 不得声称 ②③ 有本次运行的分量证据。
4. 不得在保序差分未零差异前声称 ① 正确。
5. 不得因微基准的 47.6% 提速就声称生产端到端提速 47.6%。

---

## 6. 不做的事（明确排除）

1. **不换字符串类型。** `Utf8String` 不存在（`Type.GetType(...)` 返回 `null`；
   `u8` 后缀只给 `ReadOnlySpan<byte>`）；UTF8 在**临时键**场景**更贵**
   （`GetBytes` 要先有 `string` ⇒ 同时分配 `string`+`byte[]`，实测 568 B/次 vs 320）。
   UTF8 只在**存储**场景省（20,000 条省 35.8%），但本仓库存储侧**已无字符串可省**
   （`NLCPGNode` 文本字段全 `uint`）。
2. **不用 `string.Create`。** 实测 **328 B/次 vs `$""` 320 B/次（更差）**——
   插值已经恰好分配一次，没有中间缓冲可省。**此前报告的建议已撤回。**
3. **不用 `ArrayPool`。** 实测 1.54× 放大 + 长期占用。
4. **不把 `OrderBy` 换成 `Array.Sort`。** `OrderBy/ThenBy` **稳定**、`Array.Sort` **不稳定**，
   同键元素的相对次序会变（§4.3 同类风险）。
5. **不引入 `Length` 前缀优化。** 实测反例：`("A" vs "AB")` 拼接序 `1` vs 长度优先 `-1`（**相反**）。
6. **不"化简"六段流**（删段/合并段）。穷举 43,046,721 有序对：
   删段但保留占位错 **906,136**、彻底删段错 **1,814,344**。
   最小反例：A `|FIXED|||7|` vs B `|FIXED|||A|7|` ⇒ 完整 6 段 `-1`，化简后 `1`。
7. **不在 `CompareTo` 内做 `Resolve`。** 实测锁调用放大 **41.6×**
   （N=50,000：50,000 → 2,081,014）。
8. **不新增 `InternalsVisibleTo`**（§4.1）。
9. **不改枚举的数值排序语义**——`NLCPGNodeKind` 一律按**文本**比（§7 风险 R4）。
10. **不动 `targets` 的结构**（§4.5）与 `orderedPlans` 的冗余层（§4.4）。
11. **不为 interner 增加 `Clear()`/容量上限**——这会使 ① 的"键持引用"前提失效（§7 风险 R10）。

---

## 7. 风险清单

| # | 风险 | 实测证据 | 后果 | 处置 |
| --- | --- | --- | --- | --- |
| R1 | `Resolve` 写进比较器 | 锁调用 **41.6×**（N=50,000：50,000→2,081,014） | 锁竞争放大；运行已有 858/2 s 峰值 | 不变式 3：只能在构造期 |
| R2 | 只实现非泛型 `IComparable` | **22,069.6 KB** vs 泛型 **1,016.1 KB**（21.7×，N=20,000） | 每次比较装箱，零分配目标失效 | 实现 `IComparable<T>` |
| R3 | 比较器内现造字段数组 | N=120,000 **103.76 MB** vs 现状 27.44 MB | 比不改更差 | 键选择器物化 |
| R4 | `Kind` 按枚举**数值**比 | 位置不同 **80,000**（N=80,000）；`SyntaxNode`(1) vs `SyntaxTree`(0) 文本序与数值序**相反** | 静默改序 ⇒ 改 `targets[0]` | 一律按**文本**比（§6.9） |
| R5 | 以为 `Kind.ToString()` 是 0 分配 | 定义值 **24 B/次**；插值内 **56 B/次**（`Enum.GetName`/静态表才 0.00） | 构造期按 n 次付这笔钱 | 静态表 + **回退** `((int)k).ToString()` |
| R6 | `int` 缓冲 < 11 字符 | `int.MinValue` = `"-2147483648"`（11 字符）；`char[8]` 下 `TryFormat` **失败且 `w=0`** | 键**少一段** ⇒ 静默错序 | 缓冲 **12**，且**检查返回值**（失败抛异常，不得用 `w`） |
| R7 | Culture 不一致 | `sv-SE`/`fi-FI` 负号 U+2212、`ar-SA` U+061C vs Invariant U+002D | 负数 `SpanStart` 文本不同 ⇒ 序不同 | 必须与插值一致用 **`CurrentCulture`**，**不能**用 `Invariant` |
| R8 | 化简 / 删段 | 穷举 43,046,721 对：906,136 / 1,814,344 处不同 | 错序 | 照抄**完整 6 段** |
| R9 | 三条链共用一个 `Key` | 位置不同 **39,985**（N=40,000），含 §4.2 翻转反例 | `targets[0]` 选择改变 ⇒ 图改变 | 逐调用点替换（§4.2） |
| R10 | 结构体键持**引用** | 当前安全：`_nextId` 单调、**无** `Clear`/`Remove` | 若将来 interner 被清空/回收，排序键失效 | 维持 §6.11；若日后引入 `Clear()` 必须**重新评估** |
| R11 | 键尺寸 | **48 B**（4 引用 + 2 `int?`）；N=484,208 时缓冲 ≈ **22.2 MB** | 排序期额外存活 | 可接受；不得声称"零成本" |
| R12 | 枚举文本静态表越界 | `((Kind)99).ToString()` → `"99"`（数字串） | 缺回退则键文本为空/错 | R5 的回退即覆盖此点 |
| R13 | 稳定性 | `OrderBy/ThenBy` 稳定；`Array.Sort` 不稳定 | 同键相对次序变化 | §6.4 |
| R14 | 文化相关的 `TryFormat` 默认参数 | `TryFormat(dest, out w)` 的 `default` 即 CurrentCulture | 与插值一致性依赖此默认 | 显式传 `CultureInfo.CurrentCulture`（可读性） |

---

## 8. 备选与后续

1. ~~**`targets` 结构性简化（优先于 ②）**：可整段省掉 L779 那次 `NodeSortKey`。~~
    **已于 2026-09-23 实测否定：收益为 0**（§4.5）——`n ≤ 1` 时 `OrderBy` 完全短路，
   L779 的 `NodeSortKey` 调用 **0** 次。仅剩可读性价值，不再列为优化项。
2. **`orderedPlans` 删冗余层**（§4.4）：`BridgeKind` 重复比较（L876 与 L880）。
   ⚠ 注意与备选 1 不同：这层**确实会执行**（`orderedPlans` 有 118 个元素），
   但它只做枚举比较、不产生分配，故收益是 CPU 而非分配。
3. **`ToContextId` 去重（14.52%）**：`NLCPGBuilder.cs:908` 与 `NLCPGEdge` 构造器 L11
   各自插值一次 `callsite:{...}`。零风险，**未包含在本设计内**（属边子系统）。
4. **② 的元组化**：把三处元组版 `BuildNodeKey` 补齐 `FilePath`，
   使跨文件变体也能用元组键（需评估 `SyntaxTree` 引用是否可长期持有）。
5. **③ 的调用侧**：若将来出现"从源文本切片再查表"的热路径，
   `TryGetId(ReadOnlySpan<char>)` 可直接消除该处临时 `string`。
6. **（Task 1 新增）跨文件实测 `orderedPlans` 的收益占比**：本设计只在**单文件**
   （10 个方法、22 个 call site、118 个 plan）上测出 **95.5%** 来自 `orderedPlans`。
   多文件/大项目下该比例是否稳定**未验证**——建议在真实项目输入上复测 Task 1 的探针。

---

## 9. 相关文档

- [Version4 DOP-12 诊断报告](../benchmarks/nlissn-version4-dop12-diagnostics.md)
  —— 归因、`NodeSortKey` 73.65%、风险实测、实现骨架全在此。
- [Dictionary `Entry<>` 与边缓冲内存优化研究](2026-09-23-dictionary-edge-memory-optimization-research.md)
  —— **互补不重叠**：该文处理**存活对象**（`Entry<>` 861 MB、边缓冲 240 MB、
  `ContextId` 派生），本设计处理**临时键**。该文经核实**未覆盖**
  `NodeSortKey`/`BuildNodeKey`/`StringInterner`/`GetAlternateLookup`（各 0 次命中）。
- [DataFlow 位集稀疏化设计](2026-09-23-dataflow-sparse-bitset-design.md)
  —— 同为"语义保持型表示层重构"，其 §4.3 无 `InternalsVisibleTo` 裁定被本设计沿用。
- [执行计划](2026-09-23-zero-allocation-key-execution.md)
- [C# 风格约束](../../Context/约束/Google-CSharp-Style-Guide-约束.md)
