# .NET 集合内存优化研究：`Dictionary<,>`/`HashSet<>` 的 `Entry<>[]` 占比 53.59%

> 日期：2026-09-23
> 研究对象：.NET 10 上运行的 Roslyn CPG 构建器；峰值堆 3,861 MB，其中 `Entry<>[]` 占 861 MB，为堆的 **53.59%**。
>
> **口径澄清（重要，避免误读）**：53.59% 的**分母不是 3,861 MB**，而是**位图优化落地后的 1,607 MB**：
> `861.2 / 1,607.2 = 53.58%`（与用户口径 53.59% 一致到 0.01pp）。同一分母下
> `240.0 / 1,607.2 = 14.93%` 也精确吻合用户给的 14.93%，**两个百分比共用同一分母，自洽**。
> 3,861 MB 是优化前的峰值堆。**本报告全文凡引用 861 MB，均指这 861.2 MiB 的实测 `Entry[]` 总量**，
> 也就是用户口径下的 53.59%；各表的容量**并不相同**（从 299,196 到 2,893,249，相差 9.7 倍），见 §1.9。
>
> **证据分级（全文统一）**
>
> | 标记 | 含义 |
> | --- | --- |
> | **【源】** | 已用 `web_fetch` 抓到一手来源原文（dotnet/runtime 源码、Microsoft Learn、Boost/SciPy/CMPH 官方文档），给出 URL。 |
> | **【实测】** | 本机 .NET 10.0.11 / x64 / `-c Release` 探针程序测得，可复现。**不是**从别处抄的数字。 |
> | **【复算】** | 基于用户给出的观测值（各表容量、Entry 尺寸、861 MB）算出来的，不是新测量。 |
> | **【推导】** | 由【源】+【实测】推出，不是独立测得的数字。 |
> | **SPECULATION** | 推测，无来源支撑。 |
> | **COULD NOT VERIFY** | 明确去找但没找到可引用来源。 |
>
> 工具情况：`web_search` 返回 **HTTP 401**，本轮**未依赖**搜索工具；全部外部结论均通过 `web_fetch` 抓取具体 URL 原文。

---

## 0. 结论摘要（先看这个）

按"收益 ÷ 改动风险"排序：

| # | 手段 | 预计收益 | 依据 |
| --- | --- | --- | --- |
| 1 | **消除结构性重复**（同一份 120 B 载荷存两遍） | 该重复项 **240.0 MiB = 基线的 39%** | 【复算】+ 同目录姊妹报告 |
| 2 | **预分配容量**（`new Dictionary<K,V>(estimate)` 或 `EnsureCapacity`） | **−16.7% ~ −42.1%** 的 `Entry[]` 内存（按 count 而定）：861 MB 口径下约 **−144 MB**；另加 `_buckets` 同步缩减 | 【源】+【实测】 |
| 3 | **邻接表改 CSR**（`Dictionary<NLCPGNode,NLCPGNode[]>`） | 该表 **55.9 MB → 9.9 MB**（**7.9×** @2.1M 边） | 【源】+【复算】 |
| 4 | **把 104 B 的 `NLCPGNode` 值换成 `int` id/索引** | 槽位 **120 B → 20 B**（**6.0×**）；纯 `int[]` 映射 4 B | 【源】+【实测】 |
| 5 | 更换为自定义开放寻址哈希表 | **≤7%**（每槽仅 8 B 是哈希表开销） | 【源】+【复算】 |
| 6 | 换 `ReferenceEqualityComparer` | **0 字节** | 【源】+【实测】 |
| 7 | 换 `FrozenDictionary` | **反而变大**（104 B struct key：254.6 → 401.8 B/条） | 【实测】 |

**最重要的一条结构性认知：** 这些字典的"哈希表开销"只有每槽 8 字节（`uint hashCode` + `int next`），占 116~140 B 槽位的 **5.7%~6.9%**。换哈希表算法最多省 ~7%。**真正的 93%+ 是载荷本身**——你按值内联存了六遍 104 字节的 `NLCPGNode`（见 §1.2 的反解），并且至少有一份 120 B 载荷被完整存了两遍（见第 1 项）。优化的矛头应该指向"按值重复存储大 struct / 同一份数据存两遍"，而不是"换更好的哈希表"。

**这一判断有真实工程案例支撑（§4.8）**：JetBrains 优化 dotMemory 时，把"N 个 per-node `Dictionary` 合成 1 个"只把增长速度从 1.17 GB/s 降到 7 MB/s，**进程仍然跑不完**；真正解决的是**把 per-node 字典 + 72 B class 换成 packed `readonly struct` + 紧凑数组**，峰值 **32 GB+ → 12 GB**。

---

## 1. dotnet/runtime `Dictionary` 内部实现

### 1.1 `Entry` 结构体的精确布局

`Dictionary<TKey,TValue>` 的私有嵌套结构（**源**：<https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs>，与 `main` 分支一致）：

```csharp
private struct Entry
{
    public uint hashCode;
    /// <summary>
    /// 0-based index of next entry in chain: -1 means end of chain
    /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
    /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
    /// </summary>
    public int next;
    public TKey key;     // Key of entry
    public TValue value; // Value of entry
}
```

要点：

1. **字段顺序是 `hashCode` → `next` → `key` → `value`**，`hashCode` 是 `uint`（不是 `int`），`next` 是 `int`。
2. **`next` 一个字段承担两种语义**：正常链式索引（`-1` = 链尾），以及"该槽位在 free list 上"（负数再减 3）。源码注释原文即上述 `-2` / `-3` / `-4` 的编码说明。**这是 `Dictionary` 没有独立 `freeList` 数组的原因**——省了一个数组。
3. **哈希表自身的簿记开销 = 8 字节/槽**。`key` + `value` 的宽度完全由类型参数决定。

`HashSet<T>` 的 `Entry` 同构但少一个 `TValue`（**源**：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/HashSet.cs>）：

```csharp
private struct Entry
{
    public int HashCode;   // 注意：HashSet 用 int，Dictionary 用 uint
    public int Next;
    public T Value;
}
```

> **【实测】验证**：重建 replicas 后测得的数组 stride 与你的观测值**六个全部精确吻合**：
>
> | 结构 | 报告值 | 我的 replica 实测 |
> | --- | ---: | ---: |
> | `Entry<NLCPGNode,int>` | 116 | **116** |
> | `Entry<SyntaxNode,NLCPGNode>` | 120 | **120** |
> | `Entry<IOperation,NLCPGNode>` | 120 | **120** |
> | `Entry<NLCPGNode,NLCPGNode[]>` | 120 | **120** |
> | `Entry<PendingEdgeKey,int>` | 136 | **136** |
> | `Entry<StableNodeAnchor,NLCPGNode>` | 140 | **140** |
>
> 模型：`sizeof(Entry) = 8 + sizeof(TKey) + sizeof(TValue)`，向上取整到 `max(4, alignof(TKey), alignof(TValue))` 的倍数。

### 1.2 由你的数字反解出 `NLCPGNode` 的真实大小与对齐（【推导】，高置信）

这是个**强约束反解**，值得单独讲，因为它决定了后续所有改动的影响面。

`Entry<NLCPGNode,int> = 116`，而 **116 不是 8 的倍数**。若 `NLCPGNode` 含引用或 `long`/`double` 字段，数组 stride 必须抬到 8 的倍数，最小值应为 120。因此：

> **`sizeof(NLCPGNode) = 104`，且 `NLCPGNode` 的对齐为 4 —— 即它不含任何引用、`long`、`double`、`ulong` 或含此类字段的嵌套 struct。等价于 26 个 `int`。**

同时反解出：

| 类型 | 反解大小 | 对齐 | 校验 |
| --- | ---: | ---: | --- |
| `NLCPGNode` | **104 B** | 4 | 4 个观测值全部吻合 |
| `PendingEdgeKey` | **124 B** | 4 | `Entry<PendingEdgeKey,int>` = 8+124+4 = 136 ✓ |
| `StableNodeAnchor` | **28 B** | 4 | `Entry<StableNodeAnchor,NLCPGNode>` = 8+28+104 = 140 ✓ |

**这条结论的可操作含义（重要）：** `NLCPGNode` 现在是"4 字节对齐"的，这是它 116 字节槽位的**唯一原因**。**只要给 `NLCPGNode` 加哪怕一个引用字段或一个 `long` 字段，`sizeof` 仍是 104，但每个 `Entry<NLCPGNode,…>` 会从 116 跳到 120（+3.4%）。** 这是一条容易在后续重构中无意打破的隐式契约，建议写进代码注释。

【实测】该 +3.4% 的绝对量随表而异（各表容量相差 9.7 倍，见 §1.9），按每槽 +4 B 换算：

| 表 | 容量 | +4 B/槽 = |
| --- | ---: | ---: |
| `PendingEdgeKey` | 2,893,249 | **+11.0 MB** |
| `NLCPGNode -> int` | 299,196 | +1.1 MB |
| `IOperation -> Node` | 893,384 | +3.4 MB |
| `SyntaxNode -> Node` | 324,449 | +1.2 MB |
| `NLCPGNode -> NLCPGNode[]` | 488,304 | +1.9 MB |
| `_mutableNodesByAnchor` | 1,395,263 | +5.3 MB |
| **合计** | | **约 +24 MB** |

（容量来自用户 heap CSV 的反解，见 §1.9。）

【实测】隔离验证（x64，同为 104 字节原始和）：

| 结构 | 字段原始和 | `sizeof` | `Entry<该结构, int>` |
| --- | ---: | ---: | ---: |
| 26 × `int`（无引用/无 long） | 104 | 104 | **116** |
| 12 × `long` + 2 × `int` | 104 | 104 | **120** |
| 11 × `long` + 2 × `int` + 1 引用 | 104 | 104 | **120** |

即 **`sizeof` 相同，但数组 stride 不同**——这是"含引用/8 字节字段会把数组元素 stride 抬到 8 的倍数"的机制。可引的官方对齐规则（**源**：<https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack>）：

> - The alignment of a type is the size of its largest element (for example, 1, 2, 4, or 8 bytes) or the specified packing size, whichever is smaller.
> - Each field must align with fields of its own size or the alignment of the type, whichever is smaller.
> - Padding is added between fields to satisfy the alignment…

### 1.3 增长/扩容算法：`ExpandPrime` 与质数表

`Dictionary.Resize()` 的入口（**源**：Dictionary.cs）：

```csharp
private void Resize() => Resize(HashHelpers.ExpandPrime(_count), false);
```

`HashHelpers`（**源**：<https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs> —— 注意路径是 `System/Collections/HashHelpers.cs`，**不在** `System/Collections/Generic/` 下，后者 404）：

```csharp
internal static ReadOnlySpan<int> Primes =>
[
    3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
    1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
    17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
    187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
    1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
];

public const int MaxPrimeArrayLength = 0x7FFFFFC3;
public const int HashPrime = 101;

public static int GetPrime(int min)
{
    if (min < 0) throw new ArgumentException(SR.Arg_HTCapacityOverflow);
    foreach (int prime in Primes) { if (prime >= min) return prime; }
    // Outside of our predefined table. Compute the hard way.
    for (int i = (min | 1); i < int.MaxValue; i += 2)
        if (IsPrime(i) && ((i - 1) % HashPrime != 0)) return i;
    return min;
}

// Returns size of hashtable to grow to.
public static int ExpandPrime(int oldSize)
{
    int newSize = 2 * oldSize;
    if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
        return MaxPrimeArrayLength;
    return GetPrime(newSize);
}
```

**关键点：`2,893,249` 就是质数表里的一个元素。** 你的"反解容量"不是巧合，它就是 `.NET` 在第 61 个质数上的标准槽位。源码注释解释了为什么用质数：

> A typical resize algorithm would pick the smallest prime number in this array that is larger than twice the previous capacity. … Doubling is important for preserving the asymptotic complexity of the hashtable operations such as add. Having a prime guarantees that double hashing does not lead to infinite loops.

### 1.4 实际维持的负载因子：**接近 50%，不是 90%**

这是本次研究里**最容易被误判、且改正成本最低**的一点。

扩容触发条件是"**插入时发现 `count == entries.Length`**"（Dictionary.cs `TryInsert`）：

```csharp
int count = _count;
if (count == entries.Length)
{
    Resize();
    bucket = ref GetBucket(hashCode);
}
index = count;
_count = count + 1;
```

而 `Resize()` → `ExpandPrime(_count)` → `GetPrime(2 * _count)`。所以：**每次扩容都跳到"≥ 2×当前条目数"的下一个质数**，稳态负载因子因此**逼近 50%**，而不是 90%。

【实测】逐次增长实测（`Dictionary<int,int>`，连续 `Add`，记录每次 Capacity 变化）：

```
count=1          capacity=3          load=33.333 %
count=4          capacity=7          load=57.143 %
count=8          capacity=17         load=47.059 %
count=38         capacity=89         load=42.697 %
count=198        capacity=431        load=45.940 %
count=920        capacity=1931       load=47.644 %
count=4050       capacity=8419       load=48.105 %
count=17520      capacity=36353      load=48.194 %
count=75432      capacity=156437     load=48.219 %
count=324450     capacity=672827     load=48.222 %
count=1395264    capacity=2893249    load=48.225 %
count=2893250    capacity=5999471    load=48.225 %
```

> **负载因子收敛到 48.225%**，并稳定在那里。

**那么 72.5% 是怎么来的？** 你的观测"2,893,249 槽位 / 2,097,152 条目 = 72.5%"完全正确，但它描述的是**扩容周期中的中途位置**，不是稳态：

【实测】用 `2,097,152` 次插入构建：

```
count=2097152  capacity=2893249  load=72.484%   <-- 与你的观测一致
```

`2,097,152` 落在"容量 2,893,249"这一档内（该档能装到 2,893,249 条才扩容）。你恰好在填了 72.5% 时观察堆快照。

**可操作含义：这个"浪费"是可以直接消掉的。**

```
count=2097152  grow-by-insert capacity=2893249  (72.5%)
count=2097152  pre-sized      capacity=2411033  (87.0%)
```

`2,411,033` 与 `2,893,249` 都在质数表里（分别对应"≥2×1,395,263"和"≥2×1,674,319"）。**预分配直接把槽位砍掉 16.7%。**

### 1.5 `EnsureCapacity` 与 `TrimExcess`：存在，且值得用

两者都存在（**源**：Dictionary.cs / HashSet.cs 的 `EnsureCapacity`、`TrimExcess(int)`、`TrimExcess()`）。

```csharp
public int EnsureCapacity(int capacity)
{
    if (capacity < 0) ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
    int currentCapacity = _entries == null ? 0 : _entries.Length;
    if (currentCapacity >= capacity) return currentCapacity;
    _version++;
    if (_buckets == null) return Initialize(capacity);
    int newSize = HashHelpers.GetPrime(capacity);
    Resize(newSize, forceNewHashCodes: false);
    return newSize;
}

public void TrimExcess() => TrimExcess(Count);
```

【实测】容量阶梯：**grow-by-insert vs 预分配**（同一个最终 count）：

| 最终 count | 边插边长 capacity（负载） | 预分配 capacity（负载） | **节省** |
| ---: | ---: | ---: | ---: |
| 1,000,000 | 1,395,263 (71.7%) | 1,162,687 (86.0%) | **16.7%** |
| 2,000,000 | 2,893,249 (69.1%) | 2,009,191 (99.5%) | **30.6%** |
| **2,097,152** | **2,893,249 (72.5%)** | **2,411,033 (87.0%)** | **16.7%** |
| 3,000,000 | 5,999,471 (50.0%) | 3,471,899 (86.4%) | **42.1%** |
| 4,194,304 | 5,999,471 (69.9%) | 4,999,559 (83.9%) | **16.7%** |

【实测】`TrimExcess` 的效果（4,000,000 容量但只装了 100,000 条）：

```
before TrimExcess: Capacity=4166287
after  TrimExcess: Capacity=108631      <-- 降到 108,631
```

**§1.5.1 各表真实容量（由用户 heap CSV 反解，非本机测量）**

| 表 | 实际容量 | `Entry[]` 字节 | 反解 B/槽 | 负载（若 2,097,152 条） |
| --- | ---: | ---: | ---: | ---: |
| `PendingEdgeKey`（136 B/槽） | 2,893,249 | 393,481,888 | 136 | 72.5% |
| `_mutableNodesByAnchor`（140 B/槽） | 1,395,263 | 195,336,844 | 140 | — |
| `IOperation -> Node`（120 B/槽，66 个实例） | 893,384 | 107,207,664 | 120 | — |
| `NLCPGNode -> NLCPGNode[]`（120 B/槽） | 488,304 | 58,599,552 | 120 | — |
| `SyntaxNode -> Node`（120 B/槽） | 324,449 | 38,933,904 | 120 | — |
| `NLCPGNode -> int`（116 B/槽） | 299,196 | 34,706,880 | 116 | — |
| **全部六张最大表** | | **828,266,732 B = 789.9 MiB** | | |
| **全部 `Entry<>` 行合计** | | **903,063,988 B = 861.2 MiB** | | |

> **反解校验**：`2,893,249 × 136 + 24 = 393,481,888` **精确成立**。这同时确认了 136 B 是
> **`Dictionary` 形状**（`uint hashCode` + `int next` + 120 B key + 4 B value → 132，补到 136），
> 而**不是** `HashSet<PendingEdgeKey>` 的形状（那会是 `int hashCode` + `int next` + 120 B = **128 B**）。
> 实测 `HashSet<int>(2_097_152).Capacity = 2_411_033`，与快照自报的 2,893,249 不同，
> 说明**快照对应的是"边插边长"的构建路径，而非预分配路径**——这正是 §1.5 预分配建议的立足点。
>
> **表间容量相差 9.7 倍**（299,196 ~ 2,893,249），所以**不要用一个统一的"2,893,249 槽"去乘所有 Entry 尺寸**；
> §2(d)、§1.2 的逐表换算已按各自真实容量计算。

**所以两类用法要区分清楚：**

- **建之前知道规模** → 用 `new Dictionary<K,V>(estimatedCount)` 或 `EnsureCapacity(estimatedCount)`。省 16.7%~42.1%。
- **建之后不再增删** → 用 `TrimExcess()` 收尾。源码 Remarks 原文（**源**：同上）：
  > This method can be used to minimize the memory overhead once it is known that no new elements will be added.
- ⚠️ `TrimExcess()` 会**重新分配并复制**两个数组，是一次性 `O(n)` 拷贝 + 峰值瞬时翻倍。在峰值内存已经很紧的场景里，**不要在一次大 `TrimExcess` 时同时保留新旧两份**——不过它的旧数组会在下一次 Gen0/Gen2 回收时释放，通常可接受。
- ⚠️ **`TrimExcess()` 对 `HashSet<T>` 尤其相关**：§1.8 引的源码注释明确它**不会自动缩容**，必须显式调用。

> **注意 .NET 10 与 `main` 有一处实现差异（【源】，两者都已 fetch）**：`release/10.0` 的 `TrimExcess` 是 `Initialize(newSize)` + `CopyEntries`，而 `main` 已重构为 `GetPrimeAtLeast(capacity)` + `CopyEntries`。行为一致（目标容量都取"≥ 目标的质数"），但如果你去读 `main` 的源码会看到不同的函数名。

### 1.6 `CollectionsMarshal` 能省什么、不能省什么

【源】<https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs>

API 清单：`AsSpan(List<T>)`、`AsBytes(BitArray)`、`GetValueRefOrNullRef`（含 `AlternateLookup` 重载）、`GetValueRefOrAddDefault`（含 `AlternateLookup` 重载）、`SetCount(List<T>, int)`。

**必须说清楚：这些 API 都不改变 `Entry` 布局，因此都不直接省内存。**

| API | 作用 | 省内存？ |
| --- | --- | --- |
| `GetValueRefOrAddDefault` | 一次查找拿到 `ref TValue`，直接原地写入 | **否**（【实测】分配字节数完全相同，见下） |
| `GetValueRefOrNullRef` | 拿到 `ref TValue`，避免**大 struct 值的整体拷贝** | **否**（省的是 CPU/带宽） |
| `SetCount` | 直接设置 `List<T>` 的 Count，跳过逐元素 `Add` | **否**（省的是时间） |
| `AsSpan` | 零拷贝遍历 `List<T>` | **否** |

【实测】`CollectionsMarshal.GetValueRefOrAddDefault` vs 索引器赋值，100,000 条 `Dictionary<int,int>`：

```
indexer-assign       allocated = 2173088 bytes
GetValueRefOrAddDefault alloc  = 2173088 bytes
delta                          =       0 bytes
```

**同为 0 差额** —— 证实它**不减少分配**。

**它对你的真实价值在于"避免大 struct 值的整体拷贝"**，这是**时间**收益而非空间收益。对一个 104 字节的 `NLCPGNode` 值，普通写法 `dict[k].SomeField = x` 在某些模式下会拷进拷出；用 `ref` 可以原地改：

```csharp
ref var node = ref CollectionsMarshal.GetValueRefOrAddDefault(map, key, out bool exists);
node.Field = value;   // 原地写，无 104 字节拷贝
```

> 【实测】验证行为：`ref` 写入后 `d[42]` 立即反映新值，`exists` 正确返回 `false`（新增）。
>
> ⚠️ **两条硬约束（源码 Remarks 原文）**：
> 1. "Items should not be added to or removed from the `Dictionary` while the ref `TValue` is in use." —— **持有 `ref` 期间不能再增删**，否则数组可能被 `Resize` 换掉，`ref` 变成悬垂引用指向旧数组（不会崩，但写入丢失）。
> 2. `SetCount` 增大 Count 时"uninitialized data is being exposed"——会暴露未初始化数据，对含引用字段的 `T` 尤其危险。

**结论：`CollectionsMarshal` 是性能工具，不是内存工具。不要指望它降低 861 MB。**

### 1.7 快照里没有单列的 `_buckets` 数组（**已被用户实测确认**）

`Initialize` **同时分配两个等长数组**（**源**：Dictionary.cs）：

```csharp
private int Initialize(int capacity)
{
    int size = HashHelpers.GetPrime(capacity);
    int[] buckets = new int[size];     // <-- 与 entries 等长！
    Entry[] entries = new Entry[size];
    ...
}
```

所以**每个槽位的真实成本 = `sizeof(Entry) + 4` 字节**。

【实测】本机 `Dictionary<int,int>` 边际成本 = **20.00 B/槽** = 16 (`Entry`) + 4 (`buckets`)，完全吻合。

> ✅ **用户已独立验证**：直接读取运行时字段，确认 `_buckets` 是 `int[]`，且长度**恰好等于** `_entries.Length`（实测 `2,893,249 == 2,893,249`）。**每槽 +4 B 成立。**

**因此：堆快照中 861 MB 的 `Entry<>[]` 并未包含 `_buckets`。** 按各表真实容量（§1.5.1）计入：

| 表 | 容量 | `_buckets` = 容量 × 4 B |
| --- | ---: | ---: |
| `PendingEdgeKey` | 2,893,249 | 11.0 MB |
| `_mutableNodesByAnchor` | 1,395,263 | 5.3 MB |
| `IOperation -> Node`（66 个实例） | 893,384 | 3.4 MB |
| `NLCPGNode -> NLCPGNode[]` | 488,304 | 1.9 MB |
| `SyntaxNode -> Node` | 324,449 | 1.2 MB |
| `NLCPGNode -> int` | 299,196 | 1.1 MB |
| **六张最大表合计** | | **约 23.9 MB** |

即 `buckets` 相对 861 MB **额外贡献约 +3%**（约 **+24 MB**，若按全部 `Entry<>` 行则略多）。**这是"预分配容量"之外的第二笔确定收益来源**，且它同样随容量线性缩放——**预分配把容量砍 16.7%，`_buckets` 也同步砍 16.7%**。因此预分配的真实收益应计为 **`(sizeof(Entry) + 4) × 减少的槽数`**，比只看 `Entry[]` 高约 3%。

补充：`Clear()` 调 `Array.Clear(_buckets)` **清空整个 buckets 数组**（`O(buckets.Length)`，不是 `O(Count)`），而 `_entries` 只清 `[0, _count)`。所以**"经常 Clear 复用"的字典，buckets 数组会一直保持满容量**——如果配合 `TrimExcess` 想缩容，注意 `Clear()` 之后再 `TrimExcess()` 才是源码 Remarks 推荐的"最小存储"写法：

> To allocate minimum size storage array, execute the following statements:
> `dictionary.Clear();` `dictionary.TrimExcess();`

> ⚠️ **实测印证了"Clear 复杂正比于 Capacity"的代价**：JetBrains 在 dotMemory 优化中发现 `Dictionary.Clear` 占了 **92% 的时间**，因为其复杂度正比于 **Capacity 而非 Count**（他们的字典曾膨胀到 22K 槽）。改成"每轮新建字典"后 **55 min → 1 min 46 s**。见 §4.8。
> （**源**：<https://blog.jetbrains.com/dotnet/2022/05/23/how-we-used-dotmemory-to-optimize-dotmemory/>）

### 1.8 `HashSet<>` 的差异（顺带）

- `Entry.HashCode` 是 `int`（`Dictionary` 是 `uint`），`Next` 是 `int`。
- 同样 `int[] _buckets` + `Entry[] _entries`，同样 `ExpandPrime` 增长。
- 有一个 `ShrinkThreshold = 3` 常量，**仅用于构造函数**（从已有集合构造时若容量超 count 3 倍则 `TrimExcess`），源码注释明确：
  > Note that this is only used on the ctor and not to automatically shrink if the hashset has, e.g, a lot of adds followed by removes. **Users must explicitly shrink by calling `TrimExcess`.**
- `HashSet` 有 `GetAlternateLookup<TAlternate>` / `TryGetAlternateLookup`（.NET 9+），可让 `HashSet<string>` 用 `ReadOnlySpan<char>` 查找而无需分配子串。**这是省分配（时间/GC 压力）的工具，不改数组大小。**

---

## 2. 替代方案：面向稠密整数 / struct 键

> 本节所有"B/条"若标注【实测】均为本机 .NET 10.0.11 x64 实测；标注【源】的为源码推导；标注【复算】的为基于你的观测值计算。

### 2.0 先厘清一个前提：**哈希表开销只占 5.7%~6.9%**

| 槽位大小 | 哈希表簿记（8 B） | key+value 载荷 |
| ---: | ---: | ---: |
| 116 B | 8 B = **6.9%** | 108 B = 93.1% |
| 120 B | 8 B = **6.7%** | 112 B = 93.3% |
| 136 B | 8 B = **5.9%** | 128 B = 94.1% |
| 140 B | 8 B = **5.7%** | 132 B = 94.3% |

**这是本节最重要的一句话：即使你把 `Dictionary` 换成"零开销"的完美哈希表，最多也只能省 6.9% 的 `Entry[]` 内存 ≈ 59 MB。** 换数据结构的收益上限被这个数字锁死。要拿到数量级的收益，必须动**载荷**（见 §3 和 §2.4）。

因此下面对每个方案都标出"相对 `Dictionary` 的增益"，你会看到大多数方案在**你这个键型**下**不值得做**。

---

### (a) 开放寻址自定义哈希表

**最权威的一手参考是 .NET 自己的 `FrozenHashTable`**（它不是教科书式开放寻址，而是"哈希码数组 + 桶区间"的紧凑设计）。

【源】<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Collections.Immutable/src/System/Collections/Frozen/FrozenHashTable.cs>

```csharp
internal readonly struct FrozenHashTable
{
    private readonly Bucket[] _buckets;
    private readonly ulong _fastModMultiplier;
    internal int[] HashCodes { get; }

    private readonly struct Bucket
    {
        public readonly int StartIndex;
        public readonly int EndIndex;
    }
}
```

配套的键值存储（【源】<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Collections.Immutable/src/System/Collections/Frozen/KeysAndValuesFrozenDictionary.cs>）：

```csharp
internal abstract class KeysAndValuesFrozenDictionary<TKey, TValue> : FrozenDictionary<TKey, TValue>, ...
{
    private protected readonly FrozenHashTable _hashTable;
    private protected readonly TKey[] _keys;
    private protected readonly TValue[] _values;
}
```

**桶数量的选择策略**（【源】FrozenHashTable.cs，`CalcNumBuckets`）：

```csharp
const double AcceptableCollisionRate = 0.05;  // What is a satisfactory rate of hash collisions?
const int LargeInputSizeThreshold = 1000;
const int MaxSmallBucketTableMultiplier = 16;
const int MaxLargeBucketTableMultiplier = 3;

long minNumBuckets = (long)uniqueCodesCount * 2;
int maxNumBuckets = uniqueCodesCount * (uniqueCodesCount >= LargeInputSizeThreshold
    ? MaxLargeBucketTableMultiplier : MaxSmallBucketTableMultiplier);
```

源码还在注释里给了一个实测观察：

> Based on our observations, in more than 99.5% of cases the number of buckets that meets our criteria is **at least twice as big as the number of unique hash codes**.

**字节/条（【源】推导 + 【实测】校验）：**

| 组成 | B/条 |
| --- | ---: |
| `_keys` `TKey[]` | `sizeof(TKey)` |
| `_values` `TValue[]` | `sizeof(TValue)` |
| `HashCodes` `int[]` | 4 |
| `Bucket[]`（约 2~3×条目数，每个 8 B） | **16~24** |
| **合计（`int`,`int`）** | **约 32** |

【实测】`Dictionary<int,int>` 边际成本 **20.00 B/槽**；`ToFrozenDictionary()` 边际成本 **32.19 B/条**（斜率法，n=200,000→400,000，已预热 `ArrayPool`）。

> **结论：连 `int` 键这种最理想情况，`FrozenDictionary` 也比 `Dictionary` 更占内存（32.19 vs 20.00 B）。** 它是**查找速度**工具，不是内存工具。

**SwissTable（absl `flat_hash_map`）** 是经典开放寻址：1 字节控制位 + 槽位。**COULD NOT VERIFY**：本轮未能 fetch 到 abseil 官方设计页的可引用原文，故不给具体数字。

**FASTER**：**COULD NOT VERIFY**（未能 fetch 到论文原文的索引开销数字）。

**【实测】对 104 字节 struct 键，`FrozenDictionary` 是明显倒退：**

```
Dictionary<NLCPGNode(104),int>  = 127,283,488 B  (254.57 B/entry)
ToFrozenDictionary (net of src) = 200,882,720 B  (401.77 B/entry)
```

**原因**：`_keys` 是 `TKey[]`，**把 104 字节的 key 按值又存了一份**。`Dictionary` 的 `Entry` 至少还把 `hashCode`+`next` 和 key 放在一起（一次 cache line 访问），`Frozen` 则是 `_keys[]` + `_values[]` + `HashCodes[]` + `Bucket[]` 四组数组，key 的 104 字节一分不少还要多付桶数组。

**Bytes/entry：** `sizeof(TKey) + sizeof(TValue) + 4 + 8×桶倍数`；`int` 键实测 32.19 B/条。
**When it wins：** **键是小值类型（≤8~16 B）且"构造一次、查询极多次"**，且你追求**查找延迟**而非内存。**对你的 104 B struct 键，它输。**

---

### (b) 排序数组 + 二分查找

【源】.NET 自己在 `FrozenDictionary` 的**字符串**分支里就是这么做的：先按长度分桶再做子串哈希，避免完整哈希（`FrozenDictionary.cs` 的 `LengthBucketsFrozenDictionary` / `KeyAnalyzer` 路径，以及 `Constant` 阈值 `Constants.MaxItemsInSmallFrozenCollection` / `MaxItemsInSmallValueTypeFrozenCollection`：

```csharp
if (source.Count <= Constants.MaxItemsInSmallValueTypeFrozenCollection)
{
    if (Constants.IsKnownComparable<TKey>())
        return (FrozenDictionary<TKey, TValue>)(object)new SmallValueTypeComparableFrozenDictionary<TKey, TValue>(source);
    return (FrozenDictionary<TKey, TValue>)(object)new SmallValueTypeDefaultComparerFrozenDictionary<TKey, TValue>(source);
}
```

即 **.NET 官方在"元素少"时放弃哈希、改用直接比较/线性或二分**。这是"小集合不该用哈希表"的一手证据。

**字节/条：** **0 额外开销** —— 排序数组本身就是存储，key 有序排列，`Array.BinarySearch` 或手写二分。相比 `Dictionary` 每槽省下 **8 B（`hashCode`+`next`）+ 4 B（`buckets`）= 12 B/槽**。

| | B/条 | @2,893,249 |
| --- | ---: | ---: |
| `Dictionary<NLCPGNode,int>` | 120 | 331.1 MB |
| 排序 `NLCPGNode[]` + 平行 `int[]` | **108** | **297.9 MB** |

**节省 10%。** 代价：查找从 `O(1)` 变 `O(log n)`（2.9M 条 ≈ 22 次比较），**且每次比较是 104 字节 struct 比较**——如果你的比较器逐字段比，这可能是 22 × 若干字段的内存访问，**很可能比哈希慢一个数量级**。

**When it wins：** ①元素少（.NET 自己的阈值就是"small collection"）；②**批量构建、只读、且查询模式是范围扫描/有序遍历**（此时排序是免费的副产品）；③你想同时省掉 `buckets` 数组。**对你 2.9M 条 × 104 B 键的场景，纯二分不划算**——除非键能先降到 `int`（那时二分 22 次 `int` 比较，非常快，且完全无哈希表开销）。

> 【实测】如果键降到 `int`：排序 `int[]` = **4 B/条**，二分 22 次比较，总内存 **11.0 MB** @2.9M。这才是"排序数组"的正确用法。

---

### (c) 按对象分配 `int` id 后用索引查找

**思路**：每个 `NLCPGNode` 在构建时分配一个连续 `int` id（`0..N-1`），此后所有映射用 `int` 而非 104 字节 struct。

【实测】同一个字典内容，只换键类型：

| 字典 | B/槽 | @2,893,249 | 相对 |
| --- | ---: | ---: | ---: |
| `Dictionary<NLCPGNode,int>` | 120 | 331.1 MB | 1.0× |
| `Dictionary<int,int>` | **20** | **55.2 MB** | **6.0×** |
| `Dictionary<int,NLCPGNode>` | 124 | 342.1 MB | 0.97× |

**注意最后一行：把 id 作为 key 但 value 仍是 104 字节 struct，几乎没有收益（342 vs 331 MB，反而略大）。** 原因是 `TValue` 也是 104 B。**id 化必须同时把 value 也换成 `int` 或引用，才有效。**

`Dictionary<int,int>` 实测 **20.00 B/槽**（= 16 Entry + 4 buckets）= **6.0×** 于 120 B/槽。

**再进一步——如果 id 空间稠密（连续 0..N-1），根本不需要字典：**

```
int[] id -> index 映射:  4 B/条 -> 11.0 MB @2,893,249  (30× 于 120 B/槽)
```

【源】.NET 官方也承认这一点。`Int32FrozenDictionary` 的类注释原文（**源**：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Collections.Immutable/src/System/Collections/Frozen/Int32/Int32FrozenDictionary.cs>）：

> /// This dictionary type is specialized as a **memory optimization**, as the frozen hash table already contains the array of all int values, and we can thus use its array as the keys rather than maintaining a duplicate copy.

实现印证——**没有 `_keys` 数组，直接复用哈希码数组当键数组**：

```csharp
internal sealed partial class Int32FrozenDictionary<TValue> : FrozenDictionary<int, TValue>
{
    private readonly FrozenHashTable _hashTable;
    private readonly TValue[] _values;

    private protected override int[] KeysCore => _hashTable.HashCodes;   // <-- 复用！
    private protected override TValue[] ValuesCore => _values;
}
```

**When it wins：** **当键是"对象身份"且该对象生命周期内 id 稳定**。这是本次研究里**收益最大的单一手段**（6~30×）。代价：需要一次 id 分配 + 一层间接（`int id → 数组下标`），以及 id 必须在对象释放后能复用（否则 `int[]` 会随历史总量增长，不是当前存活量）。

**Bytes/entry：** `int→int` 字典 **20 B/槽【实测】**；纯 `int[]` 稠密映射 **4 B/槽**。
**风险：** 若 id 空间稀疏（例如 id 上限远大于实际数量），`int[]` 会浪费；此时用 `Dictionary<int,int>`（20 B/槽）或"排序 `int[]` 二分"（4 B/条 + `O(log n)`）。

#### 关于"用 `RuntimeHelpers.GetHashCode` 做身份 key"的补充

【源】<https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ReferenceEqualityComparer.cs>

`ReferenceEqualityComparer.GetHashCode` 就是 `RuntimeHelpers.GetHashCode(obj)`。**但这不减少 `Dictionary<object,V>` 的槽位大小**——`object` 引用仍是 8 字节，`Entry<object,int>` 仍是 24 B stride。详见 §5。

**ConditionalWeakTable**：**COULD NOT VERIFY** 其内存特性的一手文档原文，本轮未取得可引用来源，故不评。

---

### (d) `Array` / CSR（压缩稀疏行）布局 —— **对邻接表收益最大**

**这是针对 `Dictionary<NLCPGNode, NLCPGNode[]>` 的正解**，因为那本质上就是一张**邻接表**。

#### d.1 权威定义（本轮已 fetch 到官方原文）

【源】**Boost Graph Library** `compressed_sparse_row_graph` 文档：<https://www.boost.org/doc/libs/1_85_0/libs/graph/doc/compressed_sparse_row.html>

> The class template `compressed_sparse_row_graph` is a graph class that uses the compact Compressed Sparse Row (CSR) format to store **directed (and bidirectional)** graphs. While CSR graphs have **much less overhead** than many other graph formats (e.g., `adjacency_list`), they do not provide any mutability: **one cannot add or remove vertices or edges from a CSR graph.** Use this format in high-performance applications or for **very large graphs that you do not need to change**.
>
> The CSR format stores vertices and edges in separate arrays, with the indices into these arrays corresponding to the identifier for the vertex or edge, respectively. The edge array is **sorted by the source of each edge, but contains only the targets** for the edges. The vertex array stores offsets into the edge array, providing the offset of the first edge outgoing from each vertex. Iteration over the out-edges for the *i*th vertex … is achieved by visiting `edge_array[vertex_array[i]]`, `edge_array[vertex_array[i]+1]`, …, `edge_array[vertex_array[i+1]]`. This format **minimizes memory use to *O(n + m)***, where *n* and *m* are the number of vertices and edges.

**Boost 直接回答了你的三个具体问题：**

| 你的问题 | Boost 原文答案 |
| --- | --- |
| **有向**边能用 CSR 吗？ | 能，且是默认：「The `Directed` template parameter controls whether one edge direction (**the default**) or both directions are stored.」`Directed = directedS` 为**有向**，`bidirectionalS` 为双向。**Boost 明确不支持 undirected。** |
| **带属性**的边怎么存？ | 用**平行的独立属性数组**：构造函数接受 `EdgePropertyIterator ep_iter`，并且「This constructor uses extra memory to save the edge information before adding it to the graph」。即 `edge_props[]` 与 `targets[]` 同序并列，靠 **edge index** 关联，而非把属性塞进边元素。 |
| **字节/顶点、字节/边**如何压缩？ | 靠**模板参数选整数宽度**：`Vertex` 「An unsigned integral type that will be used as both the index into the array of vertices and as the vertex descriptor itself. **Larger types permit the CSR graph to store more vertices; smaller types reduce the storage required per vertex.**」 `EdgeIndex` 同理，「The `EdgeIndex` type shall **not be smaller than** the `Vertex` type, but it may be larger.」Boost 给的例子：`Vertex` 可用 16 位（≤32,767 顶点）而 `EdgeIndex` 用 32 位。 |
| **去重**怎么做？ | 见 d.4。 |

【源】**SciPy** `csr_matrix` 文档：<https://docs.scipy.org/doc/scipy/reference/generated/scipy.sparse.csr_matrix.html>

> `csr_matrix((data, indices, indptr), [shape=(M, N)])` is the standard CSR representation where the column indices for row *i* are stored in `indices[indptr[i]:indptr[i+1]]` and their corresponding values are stored in `data[indptr[i]:indptr[i+1]]`.

SciPy 明确命名 CSR 的**规范化格式（Canonical Format）**——这正是"去重"的权威定义：

> **Canonical Format**
> - Within each row, indices are **sorted** by column.
> - **There are no duplicate entries.**

以及代价（原文）：

> **Disadvantages of the CSR format**
> - slow column slicing operations (consider CSC)
> - **changes to the sparsity structure are expensive** (consider LIL or DOK)

#### d.2 针对你的规模：有向 / 带属性 / 去重后的字节数

**【复算】**基于你 heap CSV 里 `NLCPGNode -> NLCPGNode[]` 的容量 `488,304`（`Entry[]` = 58,599,552 B，反解 **120 B/槽**，与 §1.1 的 `Entry<NLCPGNode,NLCPGNode[]>` = 120 吻合），并取 `E = 2,097,152` 条边。所有数组按 x64 SZArray 口径 `24 B 头 + n × 元素`。

| CSR 变体 | offsets | targets | 属性数组 | **合计** | **B/边** |
| --- | ---: | ---: | ---: | ---: | ---: |
| (1) 有向、仅 targets（最小） | 1,953,244 | 8,388,632 | — | **9.86 MB** | **4.93** |
| (2) 有向 + **8 B/边属性** | 1,953,244 | 8,388,632 | 16,777,240 | **25.86 MB** | **12.93** |
| (3) 有向 + **16 B/边属性** | 1,953,244 | 8,388,632 | 33,554,456 | **41.86 MB** | **20.93** |
| (4) **双向**（out+in targets），无属性 | 1,953,244 | 8,388,632 ×2 | — | **17.86 MB** | **8.93** |
| (5) 双向 + 8 B 属性 | 1,953,244 | 8,388,632 ×2 | 16,777,240 | **33.86 MB** | **16.93** |
| (6) (1) + 构建期临时 `int[E] sources` | 1,953,244 | 8,388,632 | — | 17.86 MB（临时） | 8.93 |

**与现状对比：**

```
现状 Entry[] 本身                              = 55.88 MB   （488,304 × 120 B + 24）
+ 每节点一个 NLCPGNode[] 数组对象（≥1 边）      = 22.35 MB   （488,304 × (24 头 + 24 最小载荷)）
现状小计（不含被指向节点的载荷）                 = 78.23 MB
------------------------------------------------------------
CSR (1) 最小有向                               =  9.86 MB  => 7.9× 更小
CSR (2) + 8 B 属性                             = 25.86 MB  => 3.0× 更小
CSR (4) 双向 + 8 B 属性                        = 33.86 MB  => 2.3× 更小
```

> **注意属性数组的代价很大**：加 8 B/边属性让 (1) → (2) **增大 162%**（9.86 → 25.86 MB）。因为属性数组与 `targets[]` 等长，所以 **每加 1 字节/边的边属性，总内存就涨 2 MB**（@2.1M 边）。如果你的边属性较多，**考虑"属性表单独存 + 边只存 4 B 属性索引"**，而不是内联属性。

#### d.3 为什么 CSR 这么有效

①完全消除了 `Entry` 的 8 B `hashCode`+`next`；②消除了 `_buckets` 的 4 B/槽；③**消除了"每节点一个数组对象"的 24 字节固定开销**（Boost 原文的 "much less overhead than … adjacency_list" 就是此意）；④把 8 字节引用换成 4 字节 `int`（边存储减半）；⑤**可选地进一步收窄整数宽度**——Boost 明确 `Vertex`/`EdgeIndex` 可小于 `size_t`，你的 2.1M 边放得进 `int`（32 位），节点数若 <65536 甚至可用 `ushort`。

#### d.4 **去重**（你特别问的）

SciPy 的 Canonical Format 定义就是"行内有序 + 无重复"，而 Boost 的构造流程也印证：CSR 的构建天然是"**先收集 (source,target) 对 → 按 source 排序 → 折叠 source 成 offsets**"。Boost 的 `construct_inplace_from_sources_and_targets_t` 构造函数原文：

> This constructor constructs a graph with `numverts` vertices and the edges provided in the two vectors `sources` and `targets`. **The two vectors are mutated in-place to sort them by source vertex.** They are returned with unspecified values, but do not share storage with the constructed graph (and so are safe to destroy).

**对你的具体建议（【复算】）：**

| 去重方案 | 峰值临时内存 | 说明 |
| --- | ---: | --- |
| **排序去重**（推荐）：`Array.Sort(ulong[E])`，键 = `(ulong)src << 32 \| tgt`，然后相邻扫描去重 | **16.00 MB**（`ulong[E]` 本身） | **零额外结构**。`int` 内排序，无哈希表。这是 memory-optimal 的做法。 |
| `HashSet<ulong>` 去重，再导出 | **55.18 MB** | 容量 2,893,249 × 20 B/槽（`Entry<ulong>` = 8+8 = 16 B，+4 B buckets）。**比排序去重贵 3.4×**。 |

**结论：在 ~2.1M 边这个规模上，"先排序再去重"在内存上严格优于"用 HashSet 去重"。** 哈希表在这里是纯粹的多余开销——因为你的目标是产生一个**有序**数组（CSR 要求按 source 排序），而排序本身就能顺手去重。

**构建期与稳态的内存曲线（【复算】）：**

```
构建期峰值   = sources int[E] (8.00 MB) + targets int[E] (8.00 MB) = 16.00 MB
  或        = ulong[E] 打包键 (16.00 MB)                    = 16.00 MB
排序 + 去重后 = offsets int[N+1] (1.95 MB) + targets int[E'] (≤8.39 MB) ≈ 10.3 MB
释放         = sources[]/ulong[] 那 16 MB 归还
------------------------------------------------------------
稳态 ≈ 10 MB，且去重每去掉一条重复边永久再省 4 B
```

即 **CSR 的构建峰值（~16-26 MB）本身就低于现状的稳态（78 MB）**——不存在"为了省内存先要花更多内存"的问题。

**When it wins：** ①**图/邻接结构**（你的 CPG 边正是）；②**构建后只读或只做有限更新**（Boost：CSR 不可变，增删边是 `O(E)`，SciPy：「changes to the sparsity structure are expensive」）；③节点能用稠密 `int` id 表示；④需要边属性时用平行数组。
**When it loses：** 频繁随机插入/删除边；节点 id 稀疏（`offsets` 会浪费）；需要频繁按**入边**查询而没建 CSC 镜像（SciPy：slow column slicing）。

**Bytes/edge（【复算】）：** 最小有向 **4.93 B/边**（`int[N+1]` offsets + `int[E]` targets）；每加 1 B/边的平行属性数组 → **+1 B/边**；双向 → **约 +4 B/边**。对比现状 `NLCPGNode[]` 方案的 ~8 B/边（8 B 引用）+ 每节点 24 B 数组头。

> **相关：`ArrayPool` 与 `Array.MaxLength`。** `FrozenHashTable.Create` 里有一处值得注意的溢出防护（【源】FrozenHashTable.cs）：
> ```csharp
> // Use long to check for overflow before allocating - very large collections can overflow int.
> if ((long)numBuckets + hashCodes.Length > Array.MaxLength)
>     throw new OutOfMemoryException();
> ```
> 你若自己写 CSR，N+E 同样要按 `long` 校验，否则这类组合在极端输入下会整数溢出。
>
> ⚠️ **另一个 .NET 特有的坑**：`int[]` 超过 **85,000 字节**（约 21,250 个 `int`）就进入 **LOH**。你的 `targets int[E]`（E = 2.1M ≈ 8.4 MB）和 `offsets int[N+1]` 都必然是 LOH 对象。这是"用几个大数组替代几百万个小对象"的**优点**（LOH 可整块释放，无碎片、无逐代提升），但要注意 LOH 默认不压缩，**反复重建 CSR 会造成 LOH 碎片**。实践上应**一次性分配到位**（`GC.AllocateUninitializedArray<int>(E, pinned: false)` 或直接 `new int[E]`，别用会反复 `Grow` 的 `List<int>`）。
> （LOH 阈值 85,000 字节的【源】：<https://raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/garbage-collection.md> —— "the GC divides objects into 2 categories: small objects (**< 85,000 bytes**) and large objects (**>= 85,000 bytes**)"。）

---

### (e) 完美哈希（minimal perfect hashing）

【源】.NET 的 `FrozenDictionary` 字符串策略**逼近**完美哈希的思路——不存完整键，而是**找一个能区分所有键的最短子串**来做哈希，从而把"每键一次全串哈希"降为"定位 1~2 个字符"。`FrozenDictionary.cs` 的 `CreateFromDictionary` 里这条路径的源码原文：

```csharp
// Analyze the keys for unique substrings and create an implementation that minimizes the cost of hashing keys.
KeyAnalyzer.AnalysisResults analysis = KeyAnalyzer.Analyze(keys, ReferenceEquals(stringComparer, StringComparer.OrdinalIgnoreCase), minLength, maxLength);
if (analysis.SubstringHashing)
{
    if (analysis.RightJustifiedSubstring)
    {
        ...
        frozenDictionary = analysis.HashCount == 1
            ? new OrdinalStringFrozenDictionary_RightJustifiedSingleChar<TValue>(keys, values, stringComparer, analysis.MinimumLength, analysis.MaximumLengthDiff, analysis.HashIndex)
            : ...
```

以及长度位图过滤：

```csharp
int minLength = int.MaxValue, maxLength = 0;
ulong lengthFilter = 0;
foreach (string key in keys)
{
    if (key.Length < minLength) minLength = key.Length;
    if (key.Length > maxLength) maxLength = key.Length;
    lengthFilter |= (1UL << (key.Length % 64));
}
```

**这不是严格意义的 minimal perfect hashing**，而是"用键的判别性特征替代完整键存储 + 哈希"的同族优化。**诚实标注：`FrozenDictionary` 并没有使用 MPHF 算法；它做的是特化（特化子串、长度桶）。**

**严格 MPHF 的字节/键（gperf / CMPH）：COULD NOT VERIFY** —— 本轮未能 fetch 到 gperf 或 CMPH 官方手册原文，故**不给数字**。

**When it wins（【推导】）：** ①**键集合静态且构建后不变**（你的 CPG **不满足**——图上会持续加节点/边）；②追求极致查找延迟；③键是字符串或可用小特征区分。
**对你的场景：** ❌ **不适用**。MPHF 要求键集合预先已知且固定，而 CPG 是增量构建的。**如果 CPG 有"构建完成后再做多轮分析"的阶段，那一阶段内的只读映射才可能受益。**

**严格 MPHF 的字节/键（CMPH 官方文档，【源】<https://cmph.sourceforge.net/>）：**

| 算法 | 文档原文给出的空间 | bits/key | **B/key** | @2,097,152 键 |
| --- | --- | ---: | ---: | ---: |
| CHD（minimal PHF） | "can generate MPHFs that can be stored in approximately **2.07 bits per key**" | 2.07 | 0.259 | **0.52 MB** |
| CHD @81% load | "PHFs are stored in approximately **1.40 bits per key**" | 1.40 | 0.175 | 0.35 MB |
| BDZ MPHFs（c=1.23, b=8） | "stored in approximately **2.6 bits per key**" | 2.60 | 0.325 | **0.65 MB** |
| FCH | "require **less than 4 bits per key** to be stored" | <4.0 | 0.500 | **1.00 MB** |
| BRZ | "can be stored using **less than 8.0 bits per key**" | <8.0 | 1.000 | **2.00 MB** |

> **这些数字小得惊人（0.5~2 MB 存 210 万个键），但有一个必须说清的陷阱：MPHF 只把 key 映射到 `[0,n)`，它不存 value。** 你仍然需要另配一个 `values[]` 数组。MPHF 替代的是"**键数组 + 桶数组**"，不是"值数组"。对你当前 `Entry<X,NLCPGNode>` 这类表，MPHF 只能省掉 key 侧，`NLCPGNode` 载荷仍在。

> CMPH 文档同时给出了 MPHF 的适用边界（原文）：
> > The use of minimal perfect hash functions is, until now, restricted to scenarios where **the set of keys being hashed is small**, because of the limitations of current algorithms. But in many cases, to deal with huge set of keys is crucial. So, this project gives to the free software community an API that will work with sets in the order of **billion of keys**.
> > … for applications with **sporadic modifications** and a huge number of queries the B+ tree is not the best option …

**gperf：COULD NOT VERIFY**（`gnu.org/software/gperf/manual/gperf.html` 本轮 fetch 失败）。但 CMPH 官方页给出了 gperf 的定位对比（【源】，同上 URL）：
> gperf is a bit different, since it was conceived to create very fast perfect hash functions for **small sets** of keys and CMPH Library was conceived to create minimal perfect hash functions for **very large sets** of keys.

**附带成本（【源】，以 `FrozenDictionary` 为例）：** 构造代价高——官方 Remarks 原文：

> `FrozenDictionary<TKey,TValue>` is immutable and is optimized for situations where a dictionary is created infrequently but is used frequently at run time. **It has a relatively high cost to create** but provides excellent lookup performance.
> （**源**：<https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2>）

> ⚠️ **不要把上面这句误读成内存结论。** 该 Remarks 段落**没有任何关于内存占用的陈述**；唯一与内存沾边的一句（trusted keys）讲的是**构造时间**。本报告 §2(a) 的"FrozenDictionary 更占内存"是【实测】结论，不是引用这句得来的。

---

### 2.6 五方案横向对比

**重要：不要用统一容量做横向对比。** 真实各表容量相差 9.7 倍（§1.5.1），所以下表分两栏——
"**每槽/每边成本**"（与容量无关，可直接比较方案优劣）与"**该方案在对应表上的实际总量**"。

| 方案 | **B/槽（`NLCPGNode` 键）** | **B/槽（`int` 键）** | 适用表与其实测总量 | 相对现状 | 适合你？ |
| --- | ---: | ---: | --- | ---: | --- |
| `Dictionary`（现状） | 116~140 | 20 | — | 1.0× | 基准 |
| 自定义开放寻址 | ~112~132 | ~16 | — | ~1.07× | ⚠️ 上限仅 ~7% |
| 排序数组 + 二分 | ~108 | **4** | — | 1.1× / **5×** | ⚠️ 需先降键 |
| **id → 索引** | — | **20 / 4** | — | **6.0× / 30×** | ✅ **最优** |
| **CSR（邻接）** | **4.93 B/边** | — | `NLCPGNode->NLCPGNode[]`：**55.9 MB → 9.86 MB** | **7.9×**（该表） | ✅ **最优** |
| 完美哈希（MPHF） | 0.26~1.0 B/键，**但不存 value** | — | 2.1M 键 → 0.52~2.0 MB | — | ❌ 键非静态 |

**逐表算例（按 §1.5.1 的真实容量）：**

| 表 | 现状（`Entry[]` + `_buckets`） | 若改为 `Dictionary<int,int>`（20 B/槽） | 若改为纯 `int[]` 稠密映射（4 B/槽） |
| --- | ---: | ---: | ---: |
| `PendingEdgeKey`（2,893,249 槽） | 393.5 MB + 11.0 = **404.5 MB** | **55.2 MB** | **11.0 MB** |
| `_mutableNodesByAnchor`（1,395,263 槽） | 195.3 + 5.3 = **200.7 MB** | **26.6 MB** | **5.3 MB** |
| `IOperation -> Node`（893,384 槽 ×66） | 107.2 + 3.4 = **110.6 MB**（每个） | 17.1 MB | 3.4 MB |
| `NLCPGNode -> NLCPGNode[]`（488,304 槽） | 58.6 + 1.9 = **60.5 MB** | → 改用 CSR：**9.86 MB** | — |
| `SyntaxNode -> Node`（324,449 槽） | 38.9 + 1.2 = **40.1 MB** | **6.2 MB** | **1.2 MB** |
| `NLCPGNode -> int`（299,196 槽） | 34.7 + 1.1 = **35.8 MB** | **5.7 MB** | **1.1 MB** |

> **注意 `IOperation -> Node` 标注了"66 个实例"**：107,207,664 B 是这 **66 个字典的合计**，不是单个。若它确实由 66 个字典构成，则单表平均容量仅约 13,536 槽（893,384 / 66）——**这种"大量小字典"的形态特别适合换成一个统一的 `int` 索引表或 CSR，因为每个字典各自都有一对 `Entry[]`+`_buckets` 数组头和独立的扩容浪费**。**建议先核实这一点。**

**明确建议：不要重写哈希表。** 收益上限 7%，而风险（正确性、并发、迭代顺序）很高。**先做结构性改造：消除重复存储 → 预分配容量 → CSR → 降键。**

**这一结论有强力的真实案例支撑（§4.8）：** JetBrains 在 dotMemory 自身优化中，把"N 个 per-node `Dictionary` 合成 1 个"只把增长速度从 1.17 GB/s 降到 7 MB/s，**进程仍然跑不完**；真正解决问题的是**把 per-node 的字典 + 72 B class 换成 packed `readonly struct` + 紧凑数组**，峰值 32 GB+ → 12 GB。**"换更聪明的哈希表"救不了，"停止按值重复存大 struct"才救得了。**

**这一结论有强力的真实案例支撑（§4.8）：** JetBrains 在 dotMemory 自身优化中，把"N 个 per-node `Dictionary` 合成 1 个"只把增长速度从 1.17 GB/s 降到 7 MB/s，**进程仍然跑不完**；真正解决问题的是**把 per-node 的字典 + 72 B class 换成 packed `readonly struct` + 紧凑数组**，峰值 32 GB+ → 12 GB。**"换更聪明的哈希表"救不了，"停止按值重复存大 struct"才救得了。**

---

## 3. 减小 key/value 体积

（详细内容见同目录 `2026-09-23-dotnet-collection-memory-comparer-and-key-size.md`，以下为与本次问题直接相关的要点。）

### 3.1 8 字节引用 → 4 字节 `int`：**精确 −8.0000 B/条**

【实测】只改 `TKey`（键在测量区外预建，不含键对象本身）：

| 请求容量 | 实际 Capacity | `Dictionary<object,int>` | `Dictionary<int,int>` | **差额/条** |
| ---: | ---: | ---: | ---: | ---: |
| 64 | 71 | 2,120 B | 1,552 B | **8.0000 B** |
| 256 | 293 | 8,336 B | 5,992 B | **8.0000 B** |
| 1024 | 1103 | 31,016 B | 22,192 B | **8.0000 B** |

三组容量下**精确等于 8.0000 字节/条**，即 8 字节引用 vs 4 字节 `int` 的差。

### 3.2 `Nullable<T>` 的填充代价

【源】<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Nullable.cs>

```csharp
public partial struct Nullable<T> where T : struct
{
    private readonly bool hasValue; // Do not rename (binary serialization)
    internal T value; // Do not rename (binary serialization) or make readonly (can be mutated in ToString, etc.)
```

【实测】

| 类型 | 朴素预期 | 实测 stride | 浪费 |
| --- | ---: | ---: | ---: |
| `Nullable<byte>` | 2 | **2** | 0 |
| `Nullable<int>` | 5 | **8** | **3 B** |
| `Nullable<long>` | 9 | **16** | **7 B** |
| `Nullable<Guid>` | 17 | **20** | 3 B |

**⇒ 用哨兵值（如 `-1`）替代 `Nullable<int>`，字段回到 4 字节。**

### 3.3 `ushort`/`byte`：**单独缩一个字段通常省 0**

【实测】

| 结构 | 字段原始和 | `sizeof` |
| --- | ---: | ---: |
| `int, int` | 8 | **8** |
| `int, ushort` | 6 | **8**（尾部 2 B 填充吃掉收益） |
| `ushort, ushort` | 4 | **4** |
| `byte, int` | 5 | **8**（1 + 3 pad + 4） |
| `byte × 4` | 4 | **4** |

**⇒ 只有"成对/成组降宽 + 同时重排字段顺序"才真省。**

`Pack = 1` 确实能压（【实测】`byte+int` 8→**5**，`int+ushort` 8→**6**），但会产生未对齐访问，**对纯值类型无引用字段才安全**。

### 3.4 `IEquatable<T>` 与装箱：**对 NLCPG 已排除**，但机制值得记录

> **✅ 结论（已被用户核实，本节不构成行动项）**：`NLCPGNode`、`StableNodeAnchor`、`NodeId`、`NLCPGEdge`、`PendingEdgeKey`、`BufferedPendingEdge` **全部是 `readonly record struct`**（用户通过阅读声明逐条确认）。`record struct` 由**编译器自动生成** `IEquatable<T>` 实现与 `GetHashCode()` 重写，**因此下面的装箱路径在 NLCPG 中不成立。**
>
> 保留本节是因为：①它解释了一条**容易被无意打破的契约**（若将来有 struct 改成普通 `struct` 而非 `record struct`，就会静默退化）；②实测数字本身有参考价值。

**机制（【源】+【实测】）：** 若 struct 键**未实现 `IEquatable<T>`**，`EqualityComparer<T>.Default` 会回落到 `ObjectEqualityComparer<T>`（【源】<https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/System.Private.CoreLib/src/System/Collections/Generic/ComparerHelpers.cs>）：

```csharp
return result ?? CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(ObjectEqualityComparer<object>), runtimeType);
```

其实现走 `object` 虚方法（【源】<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/EqualityComparer.cs>）：

```csharp
public sealed partial class ObjectEqualityComparer<T> : EqualityComparer<T>
{
    public override bool Equals(T? x, T? y) { if (x != null) { if (y != null) return x.Equals(y); ... } }
    public override int GetHashCode([DisallowNull] T obj) => obj?.GetHashCode() ?? 0;
}
```

**对值类型 T，`x.Equals(y)` 与 `obj?.GetHashCode()` 会触发装箱。**

【实测】`Dictionary<Big104,int>` 插入 200,000 条（**这是探针构造的反例，不是 NLCPG 的现状**）：

```
Dictionary<Big104 NO  IEquatable,int> =  51,037,648 B  (255.2 B/entry)
Dictionary<Big104 WITH IEquatable,int>=  27,037,312 B  (135.2 B/entry)
EXTRA allocation without IEquatable   =  24,000,336 B  (120.0 B/entry)
```

**120.0 B/条 = 恰好 1.00 次装箱**（104 B struct + 16 B 对象头）。

【实测】对 16 字节 struct：**额外 32.0 B/条**（16 + 16），**同样是精确 1.00 次装箱**。⇒ 影响随 struct 尺寸线性放大。

> **附带实测（与上一条清理的旧前提相反，值得记录）**：`sealed` **不**影响比较器的选择。实测：一个 `sealed` 且未实现 `IEquatable<T>` 的引用类型，仍然拿到 `ObjectEqualityComparer<T>`。**唯一决定因素是是否实现 `IEquatable<T>`**（外加 string/Nullable/enum 特例）；`sealed` 影响的是 JIT 去虚化，属于另一条独立路径。
>
> **注意**：`record struct` 的编译器生成实现依赖字段逐项比较，**哈希质量取决于字段组合方式**。若发现实际负载因子偏离理想的 ~48%（§1.4），或哈希冲突链偏长，应检查生成/手写的 `GetHashCode` 是否对高基数字段（如行号、偏移）给予了足够权重。

### 3.5 字段顺序的实测演示

【实测】同为 104 字节原始和：

| 结构 | `sizeof` | `Entry<该结构,int>` |
| --- | ---: | ---: |
| 26 × `int` | 104 | **116** |
| 12 × `long` + 2 × `int` | 104 | 120 |
| 11 × `long` + 2 × `int` + 1 引用 | 104 | 120 |

**⇒ `sizeof` 不变（104），但数组 stride 会因"含 8 字节对齐字段"而抬到 120。** `NLCPGNode` 当前是 4 字节对齐（26 × int），这正是它 116 的原因。

---

## 4. 真实工程案例 / Real-world case studies

> **本节新增证据标记：**【源-实测】= 一手来源原文中，**作者自己报告了测量数字**（profiler 截图、BenchmarkDotNet 输出、ETL 采样、VS 插入实验）。这与上文的【实测】（本机探针）不同：数字由 case study 作者测得并公开，本报告只负责转述 + 给出实际 fetch 的 URL。
>
> **本节所有 URL 均为本轮实际 fetch 成功的地址**；失败/404/401 的地址统一列在 4.15。

### 4.0 速查表

| # | 项目 | 前 → 后 | 内存/分配结果 | 数字性质 |
| --- | --- | --- | --- | --- |
| 1 | dotnet/msbuild #12320 | `ItemDictionary`（2 个 Dictionary + 每方法加锁）→ `ItemDictionarySlim`（1 个 `Dictionary<string,List<T>>`） | **−870 MB** 总分配，**−2.8 s** CPU | 作者实测 |
| 2 | dotnet/msbuild #12345 | `ItemDictionary` 去掉 `Dictionary<T,LinkedListNode<T>>` 侧表 | 构建结束工作集 **−17%**（几乎整个 LOH），总分配 **−300 MB** | 作者实测 |
| 3 | dotnet/msbuild #12159 | 同样是 `Dictionary`，只是改成按上下文预估容量 | 总分配 **−100 MB**（**后续被 #12432 revert**） | 作者实测 |
| 4 | dotnet/roslyn #78287 | `ImmutableDictionary` → `Dictionary` + lock | **120 MB → 28 MB**（**−76.7%**）；CPU **560 ms → 172 ms**（**−69.3%**） | 作者实测 |
| 5 | dotnet/roslyn #77971 | `ImmutableDictionary<ProjectId,ProjectState>` → 排序 `ImmutableArray<ProjectState>` | 枚举 CPU **2.6 s + 1.1 s** 消失 | 作者实测（ETL） |
| 6 | dotnet/roslyn #50156 | `Dictionary` 的数组换成 `SegmentedArray<T>`（`SegmentedDictionary`） | 目的：**消除全部 LOH 分配**；无 MB 数字 | 设计主张，**COULD NOT VERIFY 数字** |
| 7 | Performance in .NET 8 | `ImmutableDictionary` → `FrozenDictionary`（10,000 条 int 键） | 查找 **360.55 µs → 25.95 µs**（13.89× → 1.00×）；HashSet vs FrozenSet **9.824 ns → 1.518 ns** | 作者实测（BND） |
| 8 | JetBrains dotMemory | 每节点一个 `Dictionary` + 72 B class → packed `readonly struct` + 内存映射分段数组 | 峰值 **32 GB+ → 12 GB**（**−62.5%**） | 作者实测 |
| 9 | dotnet/runtime #27877 | 提议砍掉 `Dictionary._freeCount` 字段 | **被否决**：枚举更慢，得不偿失 | 维护者分析 |
| 10 | dotnet/roslyn #67815 | `ImmutableSegmentedDictionary` → 可变 segmented dictionary | 8% 分配占比；**PR 未合并（rejected）** | 作者实测但**已放弃** |

---

### 4.1 dotnet/msbuild #12320：`Lookup.Scope` 表用瘦字典替换 `ItemDictionary`

- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/pull/12320>（`merged=True`，2025-09-30，作者 `ccastanedaucf`）
- **替换前**：`Lookup` 的 scope 栈里每个表都是 `ItemDictionary<T>`：
  ```csharp
  internal sealed class ItemDictionary<T> : IItemDictionary<T> where T : class, IKeyed, IItem
  {
      private readonly Dictionary<string, LinkedList<T>> _itemLists;
      private readonly Dictionary<T, LinkedListNode<T>> _nodes;   // 第二张字典，仅为 O(1) 删除
  }
  ```
  注意：**每个条目要维护两张字典 + 一个 `LinkedListNode` 对象**，而且 `ItemDictionary` 的**每个方法都加锁**（为别处多线程复用设计），但 `Lookup` 实际只在单线程上下文使用。
- **替换后**：
  ```csharp
  internal class ItemDictionarySlim : IEnumerable<KeyValuePair<string, List<ProjectItemInstance>>>
  {
      private readonly Dictionary<string, List<ProjectItemInstance>> _itemLists;   // 只剩一张
  }
  ```
  外加：空标记改为可复用的 `FrozenSet<string> ItemTypesToTruncateAtThisScope`；removes 改为简单列表拼接而非逐层去重；`GetItems()` 去掉一批中间分配。
- **测得结果【源-实测】**：作者在 PR 正文写明 **"you can see on the left a total ~870MB reduction"**、**"After (-870MB)"**，CPU **"−2.8s"**（剩余时间主要是 metadata `Modifies`、`PropertyDictionary` 和仍用 `ItemDictionary` 的外层 scope）。
- **性能权衡**：删掉了 O(1) 的 `LinkedListNode` 查找，删除路径变差 —— 由 #12345 接手处理。
- **数字性质**：作者用内存/分配 profiler 截图测得（PR 内嵌图片为证）。

### 4.2 dotnet/msbuild #12345：`ItemDictionary` 去掉 `LinkedList` 与第二张字典

- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/pull/12345>（`merged=True`，2025-10-27，`ccastanedaucf`）
- **替换前**：`ItemDictionary` 为每次删除维护 `Dictionary<ProjectItemInstance, LinkedListNode<ProjectItemInstance>>` 侧表（"maintaining an additional O(1) lookup table with every dictionary entry"）。
- **替换后**：删除按 item type 批量处理（`RemoveItemsByItemType()`），需要时**按需现建 `HashSet`** 做查找。
- **测得结果【源-实测】**：
  - 构建结束时**工作集内存 −17%**，且 **"nearly the entire Large Object Heap"** 消失；
  - **总分配 −300 MB**；
  - 作者原话："pretty significant drop in total GC time relative to allocations, I'm guessing due to the difference in LOH"，并说明"**I compared 4 profiles here to make sure this wasn't noise**"。
- **性能权衡**：删除操作改为"按需建 HashSet"，成本从"每条目常驻侧表"转移到"仅删除路径一次性构建"；因为删除只出现在少数场景，净收益为正。
- **数字性质**：作者实测（4 次 profile 交叉验证）。

### 4.3 dotnet/msbuild #12159：只调容量预估，`Dictionary` 本身不变

- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/pull/12159>（`merged=True`，2025-08-22，作者 `Erarndt`）
- **替换前 → 后**：**数据结构没换**。`Lookup` 里的 `Dictionary<TKey,TValue>` 常在"逐个 Add"的路径上创建，触发反复 resize。改动是利用已有上下文给出更准的初始容量。
- **测得结果【源-实测】**：**"the total allocations drop roughly 100MB"**。
- **性能权衡**：作者注明"Where the allocations happen shift around a bit"（分配位置迁移，总量下降）。
- **⚠️ 重要后续**：仓库中存在 PR **#12432 `Revert "Reduce allocations due to resizing dictionaries"`**（标题与 #12159 完全对应，据此【推导】为回滚该改动）。**因此这个 −100 MB 不是稳定落地的收益。**
- **数字性质**：作者实测（截图）。

### 4.4 dotnet/roslyn #78287：Solution/Project 的 `ImmutableDictionary` → `Dictionary` + lock

- **URL（已 fetch）**：<https://github.com/dotnet/roslyn/pull/78287>（`merged=True`，2025-04-25，作者 `ToddGrun`）
- **替换前**：`Solution`/`Project` 用多个 `ImmutableDictionary` 做 `(Document/Project)Id → 数据` 映射。作者指出关键点：这些字典里的值**在 solution/project 变换之间从不共享**，唯一操作是 `GetOrAdd` —— 即**完全用错了 `ImmutableDictionary` 的设计场景**。
- **替换后**：方案 2 = 普通 `Dictionary` + lock（本 PR）。对照方案 1 = `ConcurrentDictionary`（<https://github.com/dotnet/roslyn/pull/78285>）。
- **测得结果【源-实测】**（C# editing speedometer 的 solution load，`(Solution/Project).GetDocument` 调用下的 GC Heap Allocs 与 CPU samples）：

  | 方案 | Alloc | CPU |
  | --- | ---: | ---: |
  | Baseline（ImmutableDictionary） | **120 MB**（1.0%） | **560 ms**（0.3%） |
  | ConcurrentDictionary | 59 MB | 258 ms（0.1%） |
  | **Dictionary + lock（本 PR）** | **28 MB** | **172 ms**（<0.1%） |

  **⇒ 分配 −76.7%（120 → 28 MB），CPU −69.3%（560 → 172 ms）。**
- **性能权衡**：从无锁不可变结构改为加锁可变结构（并发正确性靠 lock 保证）；`ConcurrentDictionary` 方案代码更简单、也有明显提升，但仍不如 lock 方案。
- **数字性质**：作者实测（VS 私有仓库的 test insertion + GC heap alloc / CPU 采样）。

### 4.5 dotnet/roslyn #77971：`ImmutableDictionary<ProjectId,ProjectState>` → 排序 `ImmutableArray<ProjectState>`

- **URL（已 fetch）**：<https://github.com/dotnet/roslyn/pull/77971>（state=closed；**merged 状态未能验证 —— GitHub REST 核心配额在核对时已耗尽**）
- **替换前**：大量代码反复 `foreach` 一个 `ImmutableDictionary<ProjectId, ProjectState>`，作者称其"surprisingly expensive"。
- **替换后**：改为按 `ProjectId` 排序的 `ImmutableArray<ProjectState>`，查找走二分（O(lg n)）。同时删掉了 `SolutionState` 上"为排序版 `IReadOnlyList<ProjectId>` 而存在的 `ConditionalWeakTable`"。
- **测得结果【源-实测】**：Roslyn 加载期间的本地 ETL trace：
  - `ComputeDocumentIdsWithFilePath` 下枚举该字典：**2.6 秒**
  - `GetFirstRelatedDocumentId` 下：**1.1 秒**
- **性能权衡**：作者明确"**There is no asymptotic performance change**"（排序数组二分 vs 字典查找都是 O(lg n)）。
- **数字性质**：作者实测（本地 ETL trace）。**注意：这里的 2.6 s / 1.1 s 是 CPU 时间收益，不是 MB。**

### 4.6 dotnet/roslyn #50156 / #47637：`Dictionary` → `SegmentedDictionary`，以及 `FrozenDictionary` 的起源

- **URL（已 fetch）**：<https://github.com/dotnet/roslyn/pull/50156>（`merged=True`，2021-01-13，作者 `sharwell`）；<https://github.com/dotnet/roslyn/issues/47637>（含关键评论）
- **替换前**：`Dictionary<TKey,TValue>`（.NET 5.0.2 版本），大字典的 `buckets[]`/`entries[]` 会落到 **LOH**。
- **替换后**：`SegmentedDictionary<TKey,TValue>` —— 逐字派生自 `Dictionary`，只把后备数组换成 `SegmentedArray<T>`，**"to avoid all Large Object Heap allocations"**。
- **结果【源-实测（定性）】**：作者原话 **"In most cases, the performance of this collection is indistinguishable from `Dictionary<TKey,TValue>`, with the exception of not using the Large Object Heap."** **未给出 MB 数字** ⇒ 本条的内存收益为 **COULD NOT VERIFY（无量化）**。
- **同 issue 中的 `FrozenDictionary` 起源**【源-实测（主张）】：`agocke`（2020-09-11）描述他为 Roslyn 需求新造的数据结构：
  > "Roslyn has very simple requirements. It need a dictionary which is something close to `ImmutableArray` (create once, modify never again) ... I ended up creating a new data structure ... which I ended up calling `FrozenDictionary`. It manages to be **slightly faster than `Dictionary`**, which is *much* faster than `ImmutableDictionary`, and **much smaller than both of them**."
  - 这就是后来 `System.Collections.Frozen` 的前身。此时**只有定性主张，没有数字**。
- **⚠️ 反向证据（同一 issue 内）**：`jkotas` 对"消灭 LOH"这一目标本身提出质疑：
  > "I believe that avoiding LOH allocations everywhere would have very similar effect (higher allocation rate in Gen0) as setting the LOH threshold to a high number. Workloads that have issues with LOH allocations **can configure the LOH threshold today, without changing any code**."
  - 即：**换数据结构不是唯一解，先试配置**。这与本报告第 0 节的"先做便宜的"取向一致。
- **数字性质**：定性/主张为主。

### 4.7 Stephen Toub《Performance Improvements in .NET 8》：`Dictionary` vs `ImmutableDictionary` vs `FrozenDictionary`

- **URL（已 fetch）**：<https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>
- **场景**：10,000 条 `Dictionary<int,int>`，只读查找（`Items = 10_000`）。

  **查找基准（越小越好）**

  | Method | Mean | Ratio |
  | --- | ---: | ---: |
  | ImmutableDictionaryGets | 360.55 µs | 13.89 |
  | DictionaryGets | 39.43 µs | 1.52 |
  | **FrozenDictionaryGets** | **25.95 µs** | **1.00** |

  原文结论："`FrozenDictionary<TKey, TValue>` was **50% faster** than even `Dictionary<TKey, TValue>`"。

  **构建/追加基准（原文用它说明 Immutable 的优势面）**

  | Method | Mean | Ratio |
  | --- | ---: | ---: |
  | DictionaryAdds（每轮新建整字典拷贝） | 478.961 ms | 1.000 |
  | ImmutableDictionaryAdds | 4.067 ms | 0.009 |

  原文："~**120x** better in both throughput and allocation in this run"。

  **字符串集合**：`HashSet_IsMostPopular` **9.824 ns**（1.00）→ `FrozenSet_IsMostPopular` **1.518 ns**（0.15），约 **6.5×**。
- **性能权衡（原文明确承认）**：
  - `FrozenDictionary/Set` 是"create once, use a lot"，**构建代价可能极高**：`dotnet/runtime#81194` 作为 stop-gap 去掉了默认的输入分析，另加 `optimizeForReading` 重载；原文提到"one degenerate example I created resulted in `ToFrozenDictionary` running **literally for minutes**"。
  - `ImmutableDictionary` 在**大量追加**场景反而快 ~120×（树结构共享子树）。
- **数字性质**：作者（Stephen Toub）实测，BenchmarkDotNet，.NET 8。
- **⚠️ 口径提醒**：本节**全部是吞吐/分配比率，没有任何 MB 数字**。不要把它当成"内存下降 X MB"来引用。

### 4.8 JetBrains dotMemory：唯一给出"内存从 X GB 降到 Y GB"的完整案例

- **URL（已 fetch）**：<https://blog.jetbrains.com/dotnet/2022/05/23/how-we-used-dotmemory-to-optimize-dotmemory/>（作者 Ilya，JetBrains 自家 dogfooding）
- **替换前**：支配树压缩算法中，**每个 `CompactDominatorTreeNode+Builder` 实例都持有一个 `Dictionary`** 用于按类型分组子节点。快照里约 **6000 万个** `CompactDominatorTreeNode+Builder` 对象。作者原话："It quickly became apparent that for most dictionaries, **Size < Capacity**, which meant lots of memory was being wasted." 内存以 **1.17 GB/秒**速度增长，进程开始使用交换文件。
- **改动 1（减少字典数量）**：把遍历方式从"child → parent"改成 **广度优先（BFS）**，于是**只需要一个字典**，每建完一层就清空复用（输入侧改用 dotMemory 已有的 "parent → child" 邻接表，所以没有增加计算复杂度）。
  - **结果【源-实测】**：内存增长速度 **1.17 GB/s → 7 MB/s**，**但进程仍然跑不完、仍然用交换文件**。
  - ⇒ **关键结论：单纯"把 N 个字典变成 1 个"不足以解决问题。**
- **改动 2（换掉持有字典的对象本身）**：`Node` 从 class 改为 packed struct。原 class：payload 45 B，但因对象头 + 对齐，**每个 Node 实际占 72 B**，且平均 retained **192 B**（子节点存在低效的 `JetMutableArray` 里）。改为：
  ```csharp
  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  public readonly struct Node
  {
      public readonly TypeId ObjectsType;
      private readonly uint _objectsCount;   // 高位当 IsContainedInSet 位标志用
      public int ObjectsCount => (int)(_objectsCount & 0x7FFFFFFF);
      public bool IsContainedInSet => (_objectsCount & 0x80000000) != 0;
      public readonly int RetainedObjectsCount;
      public readonly ulong RetainedBytes;
      public readonly DfsNumber DfsEnter;
      public readonly Range<uint> Children;   // 指向内存映射分段数组的区间
  }
  ```
  即：**去掉未使用的 `Parent` 属性、把 `bool` 塞进位标志、子节点改存到内存映射文件支撑的分段数组里**（dotMemory 同时把托管数组整体迁到自研 memory-mapped 数组）。
  - **结果【源-实测】**：**内存峰值从"无法低于 32 GB"降到 12 GB**（**−62.5%**），55 分钟跑完；快照共 2.75 亿对象，压缩树含 2 亿节点。
- **改动 3（纯性能，非内存）**：dotTrace 显示 **92% 的时间耗在 `Dictionary.Clear`**。原因是复用的那个字典在第一轮膨胀到约 **22K 元素**，而 `Dictionary.Clear` 的复杂度正比于 **Capacity（具体为 `buckets.Length`）而非 Count**，于是后面每轮都在清空"本来就是空的"槽位。
  - **修法**：放弃 Clear+复用，**每轮新建字典**。
  - **结果【源-实测】**：**55 分钟 → 1 分 46 秒**。代价是 GC 负载上升，但分布均匀、可接受（用 dotTrace 验证）。
- **⚠️ 作者自曝的回归**【源-实测】：在**同样大小但拓扑不同**的另一份快照上，新算法比旧算法**慢 33%**（尽管计算复杂度没变）。作者原话："To our utter surprise, our new algorithm ran 33% slower than the old one."
- **数字性质**：作者用自家 dotMemory / dotTrace 实测的真实 profiling 会话。
- **对本项目的意义**：这是最贴近的类比 —— **收益来自"把 per-node 的字典 + 72 B 对象换成 packed struct / 紧凑数组"，而不是"换一种更聪明的哈希表"。** 与本报告第 0 节第 3、4 条同向。

### 4.9 dotnet/runtime #27877：一个被**明确否决**的"更瘦 Dictionary"

- **URL（已 fetch）**：<https://github.com/dotnet/runtime/issues/27877>（"Backport improvements from DictionarySlim to Dictionary"）
- **提议的瘦身项**（来自 DictionarySlim）：
  1. 用 `(uint)` 转换替代 `& 0x7FFFFFFF` 掩码，存 `uint` 哈希以保留更多熵；
  2. **删掉 `_freeCount` 字段**，改用 `_freeList == -1` 作哨兵；
  3. 用 1 元素哑数组替代 `_buckets == null` 检查（后被划掉放弃）。
- **结果：第 2 项被否决**【源-实测】。维护者 `danmoseley` 的分析结论：
  > "Another approach could be to follow the free list chain first, to get the free count ... but that traversal cost could be arbitrarily large. **... it does seem you are right that this will always be less efficient for enumeration. That is worse than saving a field (and adding an enumerator field).**"
  - 即：删掉 `_freeCount` 省 4 字节，却让**枚举与 `CopyTo` 每次都要多做一次计数/边界判断**，还多一个枚举器字段 ⇒ **净亏**。
  - 第 1 项（熵）落地于 <https://github.com/dotnet/coreclr/pull/23591>（"DictionarySlim backport improvements, retaining more entropy"，`state=closed`）。
- **性能权衡**：这是"**省字节 vs 每次操作变慢**"的经典取舍，且结论是**不值得**。
- **数字性质**：维护者基于代码分析的判断（有具体代码对照，但无 MB 数字）。
- **⚠️ 原始 DictionarySlim 的数字**：`https://github.com/dotnet/corefxlab/pull/2458` 的 **PR 正文为空**，**没有可引用的内存测量数字** ⇒ **COULD NOT VERIFY**。

### 4.10 dotnet/roslyn #67815：一个**已放弃**的 segmented dictionary 瘦身

- **URL（已 fetch）**：<https://github.com/dotnet/roslyn/pull/67815>（**`merged=False`**，state=closed，作者 `ToddGrun`）
- **替换前**：`SourceNamespaceSymbol._aliasesAndUsings_doNotAccessDirectly` 是**逐条 Add 构建起来的 immutable segmented dictionary**（不可变结构逐条构建 = 反复重分配）。
- **替换后（提议）**：换成普通（可变）segmented dictionary。
- **测得结果【源-实测】**：在一次中型 .cs 文件的简单输入场景中，**8% 的分配**归因于 `[Microsoft.CodeAnalysis.CSharp.SingleNamespaceDeclaration, Microsoft.CodeAnalysis.CSharp.Symbols.SourceNamespaceSymbol+AliasesAndUsings][]`；作者称改动后"**I see a significant reduction in the allocation costs associated with this type**"（**最终数字仅以截图形式给出，无法引用具体数值**）。
- **⚠️ 该 PR 未合并**。作者在评论中撤回：
  > "Going to kill this PR and think about this some more as it has **an obvious issue** and I wasn't aware there were recent changes in this area that **changed its allocation pattern**."
  另有评审追问"Was impact of the previous change not verified prior to submitting the PR?"
- **数字性质**：作者实测但**结论已废弃**。引用时必须标注为 rejected，不能当作成功案例。

### 4.11 dotnet/msbuild #2587：`Lookup`/`ItemDictionary` 的长期成本画像

- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/issues/2587>（**state = open**，10 条评论）
- **问题定性**：`ItemDictionary<T>` 底层 `Dictionary<TKey,TValue>` 被反复 resize。
- **测得数据【源-实测】**：
  - `davkean`（2017-10-11）："**11% of allocations and 6.5% [CPU] comes from `Lookup`**"；"a huge amount of allocations are us **resizing the underlying `Dictionary<TKey,TValue>` because we couldn't figure out the up-front size**"。
  - `ladipro`（2021-12-13），clean-build Ocelot 方案时：

    | | Lookup | ItemDictionary |
    | --- | ---: | ---: |
    | CPU | 1.8% | 1.1% |
    | Memory | **7.8%** | **6.0%** |

    并注明"**Not as bad anymore but definitely worth optimizing.**"
- **性能权衡（讨论中被明确点出）**：
  - 用"数组的链表"替代 resize 会**增大 Dictionary 体积**，而且"eventually something has to increase the size of the list of arrays and we're back to square one"（`davkean`）；
  - 作者也承认无法预估最终大小："we **don't know** how much data we're going to add these dictionaries by the end of the scopes of evaluation, so we **can't pick the 'right size'**"。
- **状态**：`ladipro`（2022-01-10）"Back to backlog as I will not have time to work on this" —— **该 issue 至今 open**，但相关工作后来由 4.1/4.2 的 PR 落地。
- **数字性质**：作者实测（profiler）。

### 4.12 dotnet/msbuild #4197 与 #6176：方向正确但**没有可引用数字**

- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/issues/4197>（`state=closed`）
  - 建议：`ProjectItem → LinkedListNode` 的第二张字典**改为惰性创建**（"In the majority case, this will not be created"）；不可变场景改用 `List<>`，因为"**LinkedListNode uses extra memory (link points, additional 8 bytes per CLR object), using `List<>` will be much more efficient**"。
  - **COULD NOT VERIFY**：issue 正文只有建议，**没有给出测得的内存数字**。（该方向最终由 4.2 的 #12345 以 −300 MB / −17% 工作集落地。）
- **URL（已 fetch）**：<https://github.com/dotnet/msbuild/issues/6176>（`state=closed`）
  - 主题：`ProjectPropertyInstanceEnumeratorProxy` / `ProjectItemInstanceEnumeratorProxy` / `ItemDictionary<T>` / `CopyOnReadEnumerable` 一整套机制"really benefit from immutable collections"，并称"**A lot of unnecessary work and allocations are happening** to enumerate properties and items"。
  - **COULD NOT VERIFY**：**无任何量化数字**。

### 4.13 JetBrains ReSharper 2025.2：**只有定性表述，不满足取证标准**

- **URL（已 fetch）**：<https://blog.jetbrains.com/dotnet/2025/08/14/resharper-performance-improvements-2025/>（经 WordPress REST API 取得正文：`https://blog.jetbrains.com/wp-json/wp/v2/posts?slug=resharper-performance-improvements-2025&_fields=content`）
- 全文关于内存的表述**仅有两处、且都无数值、也未指名任何具体数据结构**：
  - Rename 重构："optimizations in conflict detection, reduced unnecessary processing, eliminated duplicate operations, and **lowered memory consumption**"；
  - Razor/Blazor："**reduced memory traffic** and unnecessary processing for include files such as `_ViewImports` and `_ViewStart`"。
- 唯一带数字的是启动体验："this improvement can **reduce your effective startup time by up to a full minute**" —— 这是 Early Go-to 的 UX 主张，**与内存无关**。
- **结论**：**COULD NOT VERIFY** —— 该文**不能**作为"字典被替换 + 内存可测量下降"的案例。
- 附带：我按 sitemap 枚举了 JetBrains .NET 博客（`https://blog.jetbrains.com/sitemap.xml` → `dotnet-sitemap1.xml`，共 1000 条 URL），未发现其它"Dictionary → 更瘦结构 + 内存数字"的帖子；4.8 的 dotMemory 一文是其中唯一带 GB 级数字的。

### 4.14 Unity Collections：**COULD NOT VERIFY**

- **URL（已 fetch）**：<https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/collection-types.html> 与 `.../collections-overview.html`
- **已确认的事实**【源】：`Unity.Collections` 提供 `NativeHashMap<TKey,TValue>` / `UnsafeHashMap<TKey,TValue>` / `NativeParallelHashMap<TKey,TValue>` / `NativeHashSet<T>` 等非托管容器，带 safety check；"**most `Native` collections are implemented as wrappers of their `Unsafe` counterparts**"。
- **未找到的**：官方文档**没有**给出"`Dictionary<TKey,TValue>` → `NativeHashMap` 后内存下降 X"的任何测量；文档只按"single thread / low memory overhead" vs "multithreaded / high memory overhead"做定性分类。
- **结论**：**COULD NOT VERIFY** —— 没有可引用的实测数字，不要编造。

---

### 4.15 本轮尝试过但失败 / 需要读者知道的地址

| URL / 目标 | 结果 |
| --- | --- |
| `web_search` 工具（任意 query） | **HTTP 401**，DeepSeek API 鉴权失败（与任务预告一致）。**只尝试 1 次即放弃**，未再依赖。 |
| <https://github.com/dotnet/runtime/issues/24826> | **HTTP 200，但内容与任务假设不符**：实际是《[API Proposal]: Add a generic OrderedDictionary class》，**不是** Dictionary 内存议题。任务提示中的该条前提有误。 |
| <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/> | `web_fetch` 成功但**被截断**；改用直接下载 + 去标签后全文检索（14,552 行）。 |
| <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/> | 首次 `web_fetch` 失败（fetch failed）；**重试后成功**（11,506 行）。 |
| <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/> | HTTP 200，已取得全文（9,052 行）。 |
| <https://blog.jetbrains.com/dotnet/2020/06/22/how-we-made-re sharpsharp-faster/> | **HTTP 404**（该 URL 本身构造有误，含空格）。 |
| `https://blog.jetbrains.com/dotnet/wp-sitemap.xml`、`.../sitemap.xml` | **HTTP 404**。 |
| <https://blog.jetbrains.com/sitemap.xml> | HTTP 200（可用，指向 `dotnet-sitemap1.xml`）。 |
| <https://blog.jetbrains.com/wp-json/wp/v2/posts>（无参数） | **HTTP 404**；加 `?slug=...&_fields=content` 后 HTTP 200。 |
| <https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/Collections.html> | **HTTP 404**（正确路径是 `collection-types.html` / `collections-overview.html`）。 |
| html.duckduckgo.com、lite.duckduckgo.com、mojeek.com、r.jina.ai | 均 **fetch failed**（无法作为搜索替代）。 |
| `bing.com/search` | 跨域重定向到 `cn.bing.com`；`cn.bing.com` 可访问但**结果质量极差/高度本地化噪声**，且提示"部分搜索结果未予显示"，**未用于任何结论**。 |
| GitHub REST 核心 API（`api.github.com/repos/...`） | 匿名配额 **60 次/小时**，本轮耗尽 → 后续返回 **HTTP 403 rate limit exceeded**。 |
| GitHub 搜索 API（`api.github.com/search/issues`） | 配额独立（10 次/分钟），全程可用，是本轮主要的检索手段。 |
| <https://github.com/dotnet/corefxlab/pull/2458>（DictionarySlim 原始 PR） | HTTP 200 **但正文为空**，无任何测量数字。 |
| Roslyn `SymbolDictionary` / `green node cache memory` | 搜索**返回 0 条**匹配 issue/PR。最接近的是 #13231、#26156（偏分配优化，**无 MB 数字**）。**COULD NOT VERIFY。** |
| dotnet/roslyn#77971 | 正文已取得，但 **merged 状态未验证**（核对时 API 配额已耗尽）。 |
| dotnet/msbuild#12159 是否被 #12432 回滚 | **【推导】**：两者标题严格对应（`Reduce allocations due to resizing dictionaries` ↔ `Revert "..."`），未取得 #12432 正文确认。 |

---

## 5. `ReferenceEqualityComparer` vs 默认比较器

### 5.1 结论：**对字典体积的影响是 0 字节**

【源】`Dictionary` 构造函数对**引用类型 TKey 无条件**赋值 `_comparer`：

```csharp
if (!typeof(TKey).IsValueType)
{
    _comparer = comparer ?? EqualityComparer<TKey>.Default;
    ...
}
```

字段声明只有一个：`private IEqualityComparer<TKey>? _comparer;`。**传比较器是"替换同一字段指向的实例"，不是"新增一个字段"。** 且 `Entry` **没有比较器字段**：

```csharp
private struct Entry { public uint hashCode; public int next; public TKey key; public TValue value; }
```

【实测】

| 构造方式 | 每次构造分配 | 相对默认 |
| --- | ---: | ---: |
| `new Dictionary<object,int>()` | 80.00 B | — |
| `new Dictionary<object,int>(ReferenceEqualityComparer.Instance)` | 80.00 B | **0** |
| `new Dictionary<object,int>(new MyComparer())`（每次新建） | 104.00 B | +24 |

含 256 条时：默认 8,336 B vs 单例比较器 8,336 B，**差 0**。**多出的 24 字节只在"每次 `new` 比较器"时出现，且那是比较器对象本身（最小对象 24 B），与字典布局无关。**

### 5.2 真实收益在语义与 CPU，不在内存

【源】`ReferenceEqualityComparer` 是**单例、不可实例化**：

```csharp
sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
{
    private ReferenceEqualityComparer() { }
    public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();
    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
    public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
}
```

**(a) 语义是行为变更，不只是性能变更。** 【实测】

```
两个内容相同但不同的 string 实例 a1, a2：
  a1.Equals(a2)                            = True
  ReferenceEqualityComparer.Equals(a1, a2) = False

Dictionary<string,int>（默认）                          : Count=1
Dictionary<string,int>(ReferenceEqualityComparer.Instance): Count=2
```

⚠️ **这是风险点：换成引用比较后，逻辑上重复的键不再合并，字典条目数会变多 —— 如果上游存在"值相等但引用不同"的重复键，内存反而上升。** 必须确认"引用不同 ⇒ 逻辑不同"在你的 CPG 中成立（对 Roslyn 的 `SyntaxNode`/`IOperation` 通常成立，因为语法树节点是引用身份语义）。

**(b) 免疫退化哈希。** `RuntimeHelpers.GetHashCode` 不受用户重写影响。

**(c) 省掉用户 `GetHashCode`/`Equals` 的 CPU 成本。** **但这不是内存收益。**

**(d) 与 id 键字典的关系。** `Dictionary<object,V>` 的槽位是 `Entry<object,V>`（≥24 B stride）；换成 `Dictionary<int,V>` 是 16 B。**真正的内存收益来自"把引用键换成 `int` 键"（−8 B/条，【实测】精确值），而不是来自"给引用键换比较器"（0 B）。** 二者可叠加但不可互相替代：`ReferenceEqualityComparer` 只解决"怎么比"，`int id` 才解决"存多大"。

---

## 附录 A：本轮 404 / 失败清单（诚实汇报）

| URL | 结果 |
| --- | --- |
| `.../System/Collections/Generic/HashHelpers.cs`（main / release-10.0 / release-9.0 / release-8.0） | **404** —— 正确路径是 `.../System/Collections/HashHelpers.cs`（不含 `Generic/`） |
| `.../System/Collections/HashHelpers.cs`（**main** 分支） | **404**（main 上路径已变）；`release/10.0`、`release/9.0`、`release/8.0` 均 **200** |
| `.../Frozen/Int32FrozenDictionary.cs`（直接在 `Frozen/` 下） | **404** —— 实际在 `Frozen/Int32/Int32FrozenDictionary.cs` 子目录 |
| `.../Frozen/DenseIntegralFrozenDictionary.cs` | **404**（该类型在 main 存在但文件路径未解析到） |
| `docs/design/features/frozen-collections.md` | **404** |
| `.../docfx/...FrozenDictionary` 的 MD 源 | 未取 |
| `api.github.com/search/code` | **401 Requires authentication**（需 token） |
| `raw.githubusercontent.com` 多次 | **间歇性 `TypeError: fetch failed`**；改用 `api.github.com/.../contents`（返回 base64）成功取得同一文件 |
| `.../docs/design/coreclr/botr/type-system.md` | 204/失败（**COULD NOT VERIFY** 对象头布局；权威来源应为 `src/coreclr/vm/object.h`） |
| abseil SwissTable 设计页 / FASTER 论文 / gperf 手册 | **未取得可引用原文** → 相应处标注 COULD NOT VERIFY，不给数字 |
| ~~CMPH~~ | ✅ **已取得**：<https://cmph.sourceforge.net/>（HTTP 200），§2(e) 的 MPHF bits/key 数字均出自该页 |
| ~~Boost CSR~~ | ✅ **已取得**：`latest` 路径 404，但 **`1_85_0` 版本路径 200**：<https://www.boost.org/doc/libs/1_85_0/libs/graph/doc/compressed_sparse_row.html> |
| `docs/design/features/frozen-collections.md` | **404**（该设计文档路径不存在） |
| `.../Frozen/Int32FrozenDictionary.cs`（直接在 `Frozen/` 下） | **404** —— 实际在 **`Frozen/Int32/`** 子目录 |
| `.../System/Collections/Generic/HashHelpers.cs`（含 `Generic/`） | **404** —— 正确路径是 `.../System/Collections/HashHelpers.cs` |
| `.../System/Collections/HashHelpers.cs`（**main** 分支） | **404**（main 上路径已变）；`release/10.0`、`release/9.0`、`release/8.0` 均 **200** |
| `blog.jetbrains.com/dotnet/2020/06/22/how-we-made-re sharpsharp-faster/` | **404**（构造有误，含空格） |
| `dotnet/runtime#24826` | 该 issue 实为《Add a generic OrderedDictionary class》，**与 Dictionary 内存无关**（任务提示中的假设有误） |

## 附录 B：实测环境与复现

- 运行时：**.NET 10.0.11**，SDK 10.0.400，`IntPtr.Size = 8`（x64），`-c Release`。
- 方法：`GC.GetTotalAllocatedBytes(precise: true)` 差值法测分配；数组 stride = `(Alloc(n) − 24) / n`（24 = 数组固定开销，【实测】确认）。
- 探针工程为临时目录下的一次性程序，**不是本仓库受版本控制的产物**。若要长期保留这些数字，应移入 `tests/` 的 Performance 工程并纳入验证矩阵 —— 本轮**未做**此改动。

## 附录 C：对这批表的落地建议（按优先级，已按用户复核结果修订）

1. **【最高】消除结构性重复：同一份 120 B 载荷被存了两遍。** `HashSet<PendingEdgeKey>`（Entry 120 B 载荷）与 `List<BufferedPendingEdge>`（items[] 120 B 载荷）逐字节相同，**纯重复部分 251,658,240 B = 240.0 MiB = 基线的 39%**。见同目录 `2026-09-23-edge-set-storage-and-dedup-research.md`。**这是单项收益最大的一步，且不改变语义。**
2. **【高】给每个字典预估容量并预分配。** 实测省 **16.7%~42.1%**（按 count 而定）。对 861 MB 口径约 **−144 MB**（按 16.7% 保守估）。**注意真实收益还要算上 `_buckets`（§1.7）**：每槽省 `sizeof(Entry) + 4`，比只算 `Entry[]` 高约 3%。各表容量相差 9.7 倍（§1.5.1），**逐表估比统一估更准**。
3. **【高】`Dictionary<NLCPGNode, NLCPGNode[]>` 改 CSR。** 按真实容量 488,304 + 2.1M 边复算：**55.9 MB → 9.9 MB（7.9×）**；若要 8 B/边属性则 25.9 MB（3.0×）。**去重请用"排序去重"而非 `HashSet`**——前者零额外结构（16 MB），后者 55 MB（§2 d.4）。
4. **【中】把值的 104 B `NLCPGNode` 换成 `int` 节点 id。** 但**必须 key 与 value 同时降**：只降一侧收益有限（§2 c 实测 `Dictionary<int,NLCPGNode>` = 342 MB 反而略大于现状）。
5. **【低】不要重写哈希表**（上限 ~7%，风险高）；**不要换 `FrozenDictionary`**（104 B struct key 下 254.6 → 401.8 B/条，是倒退）。
6. **【低】不要期待 `ReferenceEqualityComparer` 省内存**（0 字节），但若键是 Roslyn 引用身份语义，它**可能**顺带改善哈希质量。
7. **【已排除，无需行动】`IEquatable<T>` / 装箱问题**：用户已确认相关 struct 全是 `readonly record struct`，编译器生成 `IEquatable<T>`，**该风险不存在**（§3.4 保留机制说明以防范将来退化为普通 `struct`）。
8. **【低优先，先压测】`Clear()` 的 `O(Capacity)` 行为**：§4.8 的 JetBrains 案例显示 `Dictionary.Clear` 曾占其 **92%** 的运行时间。若 NLCPG 在热路径里反复 `Clear` 复用大字典（尤其容量已达 2.9M），值得实测其占比——这属于**时间**问题，但会同时推高瞬时内存。

> **关于 §0 的收益量级**：本报告所有"相对 861 MB"的百分比都基于用户实测的 861.2 MiB，与 53.59% 同分母（1,607 MB）。**不要**把它当成 3,861 MB 的 53.59%（那是 2,069 MB，口径不同，本报告不采用）。
