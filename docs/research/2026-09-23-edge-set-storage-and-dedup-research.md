# 大边集存储与去重：内存高效设计研究（第 1、3 节优先）

> 日期：2026-09-23。状态：**外部一手来源 + 本地实测复核**。
> 本轮未修改生产代码。所有标【实测】的数字来自本机 .NET 10.0.11 / x64 上一次独立运行的探针程序，
> 可复现（命令见文末）。
>
> 工具情况：`web_search` 返回 **HTTP 401**（鉴权失败），本轮**未依赖**搜索工具；
> 全部外部结论均通过 `web_fetch` 直接抓取具体 URL 原文得到。部分猜测的 GitHub raw 路径返回 404，
> 已在下文标注 **COULD NOT VERIFY**，未用推测填补。

## 证据分级

| 标记 | 含义 |
| --- | --- |
| **【源】** | 已 fetch 到一手来源原文，下文给出实际 URL。 |
| **【实测】** | 本机 .NET 10.0.11 x64 运行探针测得，可复现。 |
| **【推导】** | 由【源】+【实测】推出的结论，**不是**独立测得的数字。 |
| **SPECULATION** | 无法用上述证据支撑的推测。 |
| **COULD NOT VERIFY** | 明确去找但没找到可引用来源。 |

---

## 0. 结论先行（给决策者）

### 0.1 一个必须先纠正的事实：快照里的 136 B/Entry 不是 `HashSet<PendingEdgeKey>` 的形状

实测（【实测】，`Unsafe.SizeOf` + 精确分配量反解）：

| 类型 | Entry 字节 | 组成 |
| --- | ---: | --- |
| `HashSet<PendingEdgeKey>.Entry`（嵌套私有类型） | **128** | `int hashCode`(4) + `int next`(4) + `T value`(120) |
| `Dictionary<PendingEdgeKey,int>.Entry<TKey,TValue>`（共享泛型） | **136** | `uint hashCode`(4) + `int next`(4) + `TKey`(120) + `TValue`(4) → 132，按 8 对齐补到 136 |

反解过程是无歧义的：先读 `HashSet<int>(2_097_152).Capacity` 得到真实质数
`2_411_033`（【实测】），再用 `总分配 = align8(16 + 4*prime) + (16 + entrySize*prime)` 解出
`entrySize`，得到 `HashSet<PendingEdgeKey>` → **128.00**、`Dictionary<PendingEdgeKey,int>` → **136.00**，两者都精确命中整数。

而问题描述里的快照数字 `2,893,249 × 136 + 24 = 393,481,888` **精确成立**（【推导】）：

```
2,893,249 × 136 B + 24 B 头 = 393,481,888 B   ← 与快照完全一致（Dictionary 形状）
2,893,249 × 128 B + 24 B 头 = 370,335,896 B   ← HashSet 形状，比快照少 23,145,992 B
3,074,077.06 槽 @128 B                          ← 非整数，不可能
```

**含义（重要）**：代码注释写着"原先记录的序号从未被读取，故不再保存"——那个序号正是
`Dictionary<PendingEdgeKey,int>` 的 `TValue`。所以 **615.3 MiB 这个基线是 `Dictionary` 时代的旧数字**。
当前已经切到 `HashSet<PendingEdgeKey>` 的代码，Entry[] 应当已经降到 128 B/槽，
即 **已经省下 23,145,992 B ≈ 22.1 MiB**，但代价是去重能力没有任何变化，
而**结构性重复（同一份 120 B 载荷存两遍）一点没动**。

> 附带两处口径差异，标记 **COULD NOT VERIFY**（都不影响结论，但必须说清楚）：
>
> 1. **`items[]` 的口径**。`2,097,152 × 120 = 251,658,240`，快照给 `251,658,288`，**多 48 B**。
>    x64 SZArray 头是 24 B（8 同步块 + 8 方法表 + 4 长度 + 4 对齐），
>    所以快照比"数据 + 标准头"还多 **24 B**。gcdump 类工具常把对象按 8/16 B 向上对齐后上报，
>    24 B 的差额很可能是这种对齐舍入。**结论：`items[]` 的 2,097,152 槽是饱满的，无容量浪费**，
>    这一项自洽。
> 2. **质数**。`.NET 10` 实测 `GetPrime(2,097,152) = 2,411,033`，
>    而快照自报槽数 `2,893,249`。两者都对应不同的 `Entry[]` 总量。
>    说明快照与当前运行时/代码版本不完全同一。
>
> 下文所有方案算术**统一采用**：`数组字节 = n × 元素尺寸 + 24 B`（标准 x64 头），
> 而基线一行沿用快照自报的 645,140,176 B。
> 两套口径相差 ≤ 48 B（占 615 MiB 的 0.000008%），对本报告的任何排序或百分比结论**无影响**。

### 0.2 五个方案的每边字节数（n = 2,097,152 边，槽位 P = 2,893,249）

【推导】基于【实测】的 Entry 尺寸与【源】的 BCL 布局：

每行都按"每个托管数组 24 B 头（x64 SZArray）"计。

| 方案 | 活跃字节 | MiB | B/边 | 相对 615.3 MiB | 保序 |
| --- | ---: | ---: | ---: | ---: | --- |
| **快照基线（Entry 按 Dictionary 形状 136 B）** | 645,140,176 | 615.25 | 307.6 | — | 是 |
| **当前代码（`HashSet<PendingEdgeKey>`，Entry 实为 128 B）** | 633,567,180 | 604.22 | 302.1 | −1.8% | 是（实现细节） |
| (a) 只留 `HashSet<PendingEdgeKey>`，末尾排序导出 | 381,908,916 | 364.22 | 182.1 | **−40.8%** | 否（需重排） |
| (b) `Dictionary<T,int>` → 索引进 `T[]`（键仍是 120 B） | 656,713,196 | 626.29 | 313.1 | **+1.8%（更差）** | 是 |
| (c) 开放寻址索引 + 120 B 载荷（链式） | 271,619,916 | 259.04 | 129.5 | **−57.9%** | 是 |
| (c′) 开放寻址索引（仅 head 槽数组，无 next 链） | 264,241,204 | 252.00 | 126.0 | **−59.0%** | 是 |
| (d) `HashSet<long>` 仅存 64 位哈希 | 57,865,028 | 55.18 | 27.6 | −91.0% | 是 |
| **(c+s) 开放寻址索引 + `SlimEdge`(24 B)** | **70,293,324** | **67.04** | **33.5** | **−89.1%** | 是 |
| **(a+s) `HashSet<SlimEdge>` + `List<SlimEdge>`（纯 BCL）** | **154,488,684** | **147.33** | **73.7** | **−76.1%** | 是 |

（(a+s) 的 154,488,684 B 与【实测】端到端"最终活跃"154,488,680 B 相差 4 B —— 纯数组对齐舍入，
可作为上表算法的交叉验证。下文所有数组字节 = 24 B（x64 SZArray 头）+ n × 元素尺寸。）

### 0.3 建议的落地顺序

1. **先做第 3 节字段瘦身**（把 120 B 记录变成 24 B）——仅载荷部分就从 480 MiB 降到 96 MiB，
   是全部方案里收益最大、风险最低、且**不改变任何语义**的一步。
2. **再做第 1 节 (a+s) 或 (c+s)**：前者用现成 BCL 类型拿到 147.3 MiB（−76.1%）；
   后者需手写哈希表但只 67.0 MiB（−89.1%）。
3. 第 2 节 CSR 只在"冻结后确实要按源节点频繁邻接查询"时才上（37.8 MiB，再省约 29 MiB），
   收益递减而改动最大，排在最后。

---

## 1. 结构上完全相同的 key/value 重复

### 1.1 问题本质

`HashSet<PendingEdgeKey>` 把 key 存进 `Entry[]`，`List<BufferedPendingEdge>` 又把**逐字节相同**的记录
存进第二个数组。这不是"哈希表开销"，而是**同一份数据被写了两遍**。

【推导】在当前快照里：

```
Entry[] 中的载荷 = 2,097,152 × 120 = 251,658,240 B
items[]   中的载荷 = 2,097,152 × 120 = 251,658,240 B
纯重复部分                          = 251,658,240 B = 240.0 MiB
Entry[] 的非载荷开销（hash+next+补位+空闲槽）= 141,823,648 B
```

也就是说：**光"存两遍"就吃掉 240 MiB，占基线的 39%。**

> **口径说明**：下面的对比表里，基线一行用的是快照自报的两个数组之和
> （393,481,888 + 251,658,288 = 645,140,176），而**快照没有把 `HashSet` 的 `int[] _buckets` 单列出来**。
> 为了公平比较，其余每一行**都显式计入** bucket 数组（2,893,249 × 4 B + 24 B = 11,573,020 B）。
> 因此"当前代码"一行相对基线的 −1.8%，是 **Entry 从 136 B 降到 128 B 的收益（−23,145,992 B）**
> 叠加 **补记 bucket 数组的成本（+11,573,020 B）** 后的净结果。
> 这不是"改坏了"，而是**口径补齐**。若两行都不计 bucket，"当前代码"相对基线省 3.6%。

### 1.2 BCL 布局事实（【源】）

.NET 的 `HashSet<T>` 与 `Dictionary<TKey,TValue>` 共用同一套数组实现，源码注释明确写着
"This uses the same array-based implementation as Dictionary<TKey, TValue>"。

- `HashSet<T>` 源码，URL 已 fetch：
  <https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/HashSet.cs>
- `Dictionary<TKey,TValue>` 源码，URL 已 fetch：
  <https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs>
- `List<T>` 源码（确认 `DefaultCapacity = 4` 与倍增策略），URL 已 fetch：
  <https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs>

【源】`HashSet<T>` 内部持有 `int[]? _buckets` 与 `Entry[]? _entries`，Entry 含 `HashCode`/`Next`/`Value`。
`Dictionary<TKey,TValue>` 额外把 `TValue` 塞进 Entry。这正是 128 vs 136 的 8 B 之差。

### 1.3 方案 (a)：只留 `HashSet<T>`，末尾排序/建索引

**做法**：删掉 `_items` 与 `BufferedPendingEdge`，直接把 `HashSet<PendingEdgeKey>` 当作唯一容器，
在 `Materialize` 时遍历它构造 `PendingEdge[]`。

**字节数**【推导】：

```
Entry  [2,893,249] × 128 B + 24 B 头 = 370,335,896 B
bucket [2,893,249] ×   4 B + 24 B 头 =  11,573,020 B
合计                                 = 381,908,916 B = 364.22 MiB  (182.1 B/边)
```

**相对快照基线 645,140,176 B 省 40.8%。**

**正确性caveat**：
- 唯一性语义**完全不变**（同一个 HashSet 判定）。
- 但 `_items.Count` 就没有了，需要改成 `_keys.Count`。
- 顺序：`HashSet<T>` 的枚举顺序**没有契约保证**。当前实现是遍历 `_entries` 数组下标 0..count-1，
  在"只增不删"的场景下**恰好等于插入顺序**，但这是**实现细节，不是 API 保证**
  （【源】见上 HashSet.cs；文档页 <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1> 未承诺顺序）。
  依赖它等价于依赖 internals。
- 若需要**确定的**插入顺序，方案 (a) 必须显式排序，而排序键必须包含足够判别力
  （即整个 120 B 键），代价是 O(n log n) 与额外比较开销。

**结论**：作为"止血"可行，但因为它把"顺序"这个隐式不变量变成了"实现细节依赖"，不建议长期停留在这里。

### 1.4 方案 (b)：`Dictionary<T,int>` 映射到独立数组下标

**做法**：`Dictionary<PendingEdgeKey,int> _index` + `List<BufferedPendingEdge> _items`，
key 命中则用 `int` 找到 `_items` 中的位置（这正是代码注释里被删掉的"序号"）。

**字节数**【实测+推导】：

```
Entry<PendingEdgeKey,int>[2,893,249] × 136 B + 24 B 头 = 393,481,888 B
bucket int[2,893,249] × 4 B + 24 B 头                  =  11,573,020 B
items[] 2,097,152 × 120 B + 24 B 头                    = 251,658,264 B
合计                                                    = 656,713,172 B = 626.29 MiB (313.1 B/边)
```

**比"什么都不做"还差 1.8%。** 原因很简单：`Entry<PendingEdgeKey,int>` **仍然内嵌完整的 120 B key**。
`TValue=int` 甚至因为 8 字节对齐白付了 4 B 补齐（132 → 136）。

**关键判断**：方案 (b) **只有在键本身很瘦的时候才有意义**。
把键换成第 3 节的 24 B `SlimEdge` 后，`Entry<SlimEdge,int>` = 4+4+24+4 = 36 → 对齐 40 B（【实测】），
这才开始划算。换句话说：**(b) 的价值 100% 来自第 3 节，不来自 (b) 本身。**

**正确性caveat**：`Dictionary` 的插入顺序同样不是契约；但与方案 (a) 不同，这里 `_items` 本身就是权威顺序，
字典只做去重与查找，所以**顺序是被显式保住的**。这也是它相较 (a) 的唯一真实优势。

### 1.5 方案 (c)：开放寻址哈希集合 / 索引表（**推荐**）

**做法**：不再让哈希表持有 key 分毫。改为

```csharp
private SlimEdge[] _items;      // 或 BufferedPendingEdge[] —— 载荷只存这一份
private int[] _buckets;         // 槽 -> 载荷下标+1（0 表示空）
private int[] _next;            // 下标 -> 同链下一项下标+1（0 表示链尾）
```

插入时先算 key 的哈希，沿 `_buckets[h]` 走 `_next` 链**逐项与 `_items[i]` 比较**，
未命中则 `_items[_count++] = key` 并把下标挂到链头。比较用的是已经存在数组里的载荷，
**不额外复制 key**。

**字节数**【推导】（P = 2,893,249，即当前 72.5% 负载）：

```
payload  2,097,152 × 120 B + 24 B = 251,658,264 B
buckets  2,893,249 ×   4 B + 24 B =  11,573,020 B
next     2,097,152 ×   4 B + 24 B =   8,388,632 B
合计                               = 271,619,916 B = 259.0 MiB  (129.5 B/边)
```

**相对基线省 57.9%**，且**顺序被自然保存**（`_items` 是 append-only 的插入序）。

**变体 (c′)：去掉 `_next`，改为在槽数组内部线性探测**（存下标+1，开放寻址）：

```
payload 251,658,264 B
slots   P' × 4 B + 24 B，P' = 3,145,729（负载 66.7%） = 12,582,940 B
合计    264,241,204 B = 252.0 MiB  (126.0 B/边，省 59.0%)
```

比带链版本再省 7.4 MB，但**依赖负载因子**：负载一旦超过 ~70%，线性探测的平均探测长度迅速上升。
链式版本对高负载更稳健。**建议先用链式**，等有实测探测长度数据再考虑切换。

**正确性caveat（必须做到）**：

1. **哈希必须与 `EqualityComparer<PendingEdgeKey>.Default` 语义一致**。
   最安全的做法是 `HashCode.Combine(...)` 逐字段，**或**直接复用 BCL 的
   `EqualityComparer<T>.Default.GetHashCode(item)` 与 `.Equals(...)`
   ——后者等于把"哈希与相等"的权威实现留给 BCL，避免两者漂移。
2. **`_items[i]` 是可变的**（如果将来允许修改），则索引会失效。当前记录是 `readonly record struct` 且
   append-only，安全。
3. **扩容**：`_buckets` 需要重建（重新哈希全部 `_items`），`_next` 与 `_items` 只需倍增。
   重建 `_buckets` 是 O(n)，发生在每次扩容，注意与 `List<T>` 倍增叠加后的峰值。
4. 需要自己处理 `Resize`、空表、单元素等边界，**必须写针对性测试**。
5. 递减收益：这一方案的复杂度是**手写哈希表**。若第一步（第 3 节瘦身）已经把记录降到 24 B，
   那么 (c+s) 是 67.0 MiB，而现成的 `HashSet<SlimEdge> + List<SlimEdge>` 是 147.3 MiB。
   **多写一个手写哈希表换 80 MiB**——请按项目对"手写数据结构"的容忍度决定，见 §1.7。

### 1.6 方案 (d)：只存 64 位哈希的 `HashSet<long>`

**字节数**【推导】：

```
Entry<long>[2,893,249] × 16 B + 24 B = 46,292,008 B   (hash 4 + next 4 + long 8 = 16，无 TValue)
bucket int[2,893,249] ×  4 B + 24 B  = 11,573,020 B
合计                                  = 57,865,028 B = 55.18 MiB  (27.6 B/边，省 91.0%)
```

**但它是错的（作为唯一判重结构）**。两处硬伤：

1. **碰撞会静默丢边**。生日问题，n = 2,097,152，64 位空间【实测】：

   ```
   P(至少一次意外碰撞) = 1 - exp(-n(n-1) / 2^64) = 1.19e-7  ≈ 1 / 8,388,613
   ```

   对一个每次构建都要跑的 CPG builder，这是**必然会发生**的量级——不是"理论上可能"。
   一旦发生，两条**不同**的边被判为同一条，图上少一条边，且**没有任何信号**。
2. **载荷无法还原**。哈希不可逆，仍然需要第二个数组存真实数据，
   于是又回到"存两遍"，只是第二份变便宜了。

**唯一安全的用法**：把哈希当**预筛**——先用 `HashSet<long>` 快速排除绝大多数不重复项，
命中哈希的少数候选再去精确比较。但这只在"重复率极高"时有收益；
当前场景去重后 2.1 M 条全部留存，说明重复率不高，预筛省不下多少。

**结论：不采用。**

### 1.7 方案对比小结（按"改一行 vs 改一周"排序）

| 方案 | 省 | 改动量 | 顺序契约 | 风险 |
| --- | ---: | --- | --- | --- |
| (a) 只留 HashSet | 40.8% | 极小（删一个 List + 改 Materialize） | **破坏**（依赖实现细节） | 低 |
| (b) Dictionary + 索引 | +1.8%（变差） | 小 | 保住 | 低，但无收益 |
| **(c) 开放寻址索引** | **57.9%** | 中（手写哈希表） | **保住（显式）** | 中，需测试 |
| (c′) 线性探测变体 | 59.0% | 中 | 保住 | 中高（负载敏感） |
| (d) 哈希-only | 91.0% | 小 | 保住 | **不可接受（静默丢边）** |

---

## 2. CSR / 邻接数组

### 2.1 CSR 是什么（【源】）

**scipy** 文档对 CSR 的定义最精炼：以 `(data, indices, indptr)` 三元组表示，
第 i 行的列索引在 `indices[indptr[i]:indptr[i+1]]`，对应值在 `data[indptr[i]:indptr[i+1]]`。
URL 已 fetch：<https://docs.scipy.org/doc/scipy/reference/generated/scipy.sparse.csr_matrix.html>

**GAPBS** 的 `CSRGraph` 更贴近我们要的"图"视角。源码 URL 已 fetch：
<https://raw.githubusercontent.com/sbeamer/gapbs/master/src/graph.h>

它持有 `out_index_`（`DestID_**`，每个节点一个指针）与 `out_neighbors_`（`DestID_*`），
有向图再加 `in_index_`/`in_neighbors_`。构造由 `Builder` 完成，源码 URL 已 fetch：
<https://raw.githubusercontent.com/sbeamer/gapbs/master/src/builder.h>

构造流程是教科书式的两步（【源】，builder.h）：

```cpp
pvector<NodeID_> CountDegrees(const EdgeList &el, bool transpose);  // 1. 数度数
static pvector<SGOffset> PrefixSum(const pvector<NodeID_> &degrees); // 2. 前缀和 -> offset
// 3. 按 offset 把目标节点写进 neighs[]，然后逐节点 std::sort
```

即 **"先计数 → 前缀和 → 再填充"**，需要遍历边表两遍。

### 2.2 CSR 的每边字节数（含属性）

【推导】对 (src, dst, kind, label) 四属性的有向图，标准做法是**属性数组与目标数组平行**
（这也是 flatgraph "columnar" 的思路，见 §5）：

```
indptr  : (源节点数 + 1) × 4 B      —— 只与源节点数有关，与边数无关
dst[]   : 边数 × 4 B                —— 目标节点序号
kind[]  : 边数 × 1 B                —— 若枚举 <= 255（当前 NLCPGEdgeKind 有 36 个）
label[] : 边数 × 4 B                —— 若 label 内联成 int id
ctx[]   : 边数 × 4 B
callsite[]: 边数 × 4 B
```

以 2.1 M 边、假设 1,000,000 个源节点计【推导】：

```
indptr  : (1,000,001 × 4 B) + 24 B 头 =   4,000,028 B
dst     : (2,097,152 × 4 B) + 24 B 头 =   8,388,632 B
kind    : (2,097,152 × 1 B) + 24 B 头 =   2,097,176 B
label   : (2,097,152 × 4 B) + 24 B 头 =   8,388,632 B
ctx     : (2,097,152 × 4 B) + 24 B 头 =   8,388,632 B
callsite: (2,097,152 × 4 B) + 24 B 头 =   8,388,632 B
合计                                   =  39,651,732 B ≈ 37.8 MiB  (18.9 B/边)
```

**这是全部方案里最小的：约 37.8 MiB，比 615.25 MiB 基线省 93.9%。**
注意它**连源节点序号都不存**——源由 `indptr` 的分段隐含表达。这是 CSR 最漂亮的地方。
（`kind` 用 1 B 是因为 `NLCPGEdgeKind` 只有 36 个成员，见 §3.1；若用 4 B 则总计 45.9 MiB。）

### 2.3 CSR 的去重怎么做（【源】）

这是 CSR 相对哈希法的**核心差异**，必须讲清楚：

- **CSR 本身不是去重结构**。`scipy` 的 COO→CSR 转换在遇到重复 `(row, col)` 时**默认把值相加**
  （这是稀疏矩阵"装配/assembly"的语义），而不是保留一条或丢掉一条。
  【源】见上面的 scipy 文档页。
- **GAPBS 的做法是排序 + `std::sort` 后处理**。【源】graph.h 里 `NodeWeight` 的
  `operator==` 注释明确写着 *"doesn't check WeightT_s, needed to remove duplicate edges"*
  ——即**相等只比较目标节点、故意忽略权重**，这样排序后 `std::unique` 就能删掉重复边。
  builder.h 里也有 `std::sort(index[...], index[...+1])`。
  **这是"先排序、再用相邻判等"的标准去重路径**，与哈希去重是两条不同的路。

**CSR 去重的代价**：排序是 O(E log E)（GAPBS 是逐节点排序，实际是 Σ d_i log d_i，
对幂律图远好于全局排序）。而且必须先有完整边表才能构造 CSR，**无法边生成边去重**。

### 2.4 CSR vs 哈希去重的权衡

| 维度 | 哈希去重（HashSet/开放寻址） | CSR + 排序去重 |
| --- | --- | --- |
| 边生成期去重 | **可以**（流式，逐条判） | 不可以（要先集齐） |
| 内存峰值 | 表结构常驻（+4~8 B/槽） | **无表结构**，但排序期需要 O(E) 边表 |
| 每边字节 | 129.5（120 B 载荷）→ 33.5（24 B 载荷） | **18.9** |
| 顺序 | 插入序（易得） | 排序序（稳定，但不是插入序） |
| 按源节点邻接查询 | 需额外建索引 | **天然**（indptr 直接定位） |
| 实现 | 现成 BCL / 手写哈希表 | 自己写计数-前缀和-填充 |

**对本项目的判断**：需求是"有向、带属性、去重，且后续要按源节点做邻接查询"——
CSR 在**后两个需求**上明显更好，但在**去重**上更麻烦。
最自然的组合是：

> **哈希去重（边生成期）→ 冻结时一次性转 CSR（按源节点分组、组内排序去重/稳定化）**

这样生成期拿到哈希的流式与去重能力，冻结后拿到 CSR 的邻接与体积优势。
`_items` 在转 CSR 后即可释放，峰值出现在转换瞬间，需要实测。

### 2.5 GraphBLAS / LAGraph

【源】LAGraph README（URL 已 fetch）：
<https://raw.githubusercontent.com/GraphBLAS/LAGraph/stable/README.md>
—— LAGraph 是"库 + 测试框架"，收集使用 GraphBLAS 的图算法，依赖 SuiteSparse:GraphBLAS ≥ 9.0.0。

【源】SuiteSparse:GraphBLAS README（URL 已 fetch）：
<https://raw.githubusercontent.com/DrTimothyAldenDavis/GraphBLAS/stable/README.md>
—— 明确定位是"稀疏矩阵上的扩展代数（semiring）运算"，"When applied to sparse adjacency
matrices, these algebraic operations are equivalent to computations on graphs"。
它被 RedisGraph 用作底层图引擎，也是 MATLAB R2021a 的内建稀疏矩阵乘法。

**对我们的价值**：GraphBLAS 的语义是**矩阵装配**，重复 `(i,j)` 的合并由 semiring 的
"dup" 算子决定（通常是相加或取最后）。这对"边权重可累加"的图很自然，
但**对"边身份必须精确保留、不能合并"的 CPG 并不合适**——
CPG 里两条 `CallTargets` 若源/目标/kind/label 全同，是要**判为同一条**而不是"值相加"。
所以 GraphBLAS 的**数据布局**（CSR/CSC + 平行属性数组）值得借鉴，
**去重语义**不能照搬。

### 2.6 其它稀疏格式（【源】）

scipy 文档页同时给出了 COO/CSC/CSR/BSR/DIA/LIL/DOF 等格式的入口，
并明确提示 SciPy 正在从 `spmatrix` 迁移到 `sparray` 接口，
`csr_matrix` 属 legacy 接口、未来会弃用。若要参考 scipy 的实现细节，注意这条迁移路径。

---

## 3. 字段瘦身（**最高优先级**）

### 3.1 现状分解

【实测】当前记录（`Unsafe.SizeOf`，.NET 10.0.11 x64）：

| 字段 | 声明 | 实测字节 |
| --- | --- | ---: |
| `StableNodeAnchor` | `readonly record struct`（7 字段：`Kind` 枚举, `uint`, `int`, `int`, `Role` 枚举, `int`, `uint`） | **28** |
| `NLCPGContextId` | `readonly record struct NLCPGContextId(string Value)` | **8** |
| `NLCPGContextId?` | | **16**（多 8 B 的 `bool hasValue` + 对齐） |
| `NLCPGCallSiteContext` | 4 字段：`string`, `int`, `int`, `string` | **24** |
| `NLCPGCallSiteContext?` | | **32** |
| `NLCPGEdgeLabel?` | `sealed record`（引用类型，可空） | 8 |
| **`PendingEdgeKey`** | 2 锚点 + kind + 3 可空 | **120** |
| **`BufferedPendingEdge`** | 与 key 逐字段相同 | **120** |

原始字段相加【实测】：

```
2 × StableNodeAnchor        = 2 × 28 = 56 B
NLCPGEdgeKind Kind          =            4 B
NLCPGEdgeLabel? Label       =            8 B   (引用类型，可空即引用本身)
NLCPGContextId? Context     =           16 B   ← Nullable<8 B struct> = 8 + bool(1) → 补到 16
NLCPGCallSiteContext? CS    =           32 B   ← Nullable<24 B struct> = 24 + bool(1) → 补到 32
小计                        =          116 B
按 8 字节对齐补到            =          120 B   ✓ 与实测吻合
```

> **这里有一个容易看错的地方**：`NLCPGContextId?` 不是 8 B 而是 **16 B**。
> `NLCPGContextId` 本身是 8 B（一个 `string` 引用），但套上 `Nullable<T>` 后要加一个 `bool hasValue`，
> 9 B 再按 8 字节对齐就变成 16 B —— **可空包装让这个字段的代价翻了一倍**。
> 这正是 §3.3(c) "可空 → 哨兵" 的动机：不是省 4 B，是省 **12 B**。

本地源码核对：`src/NLCPG/Model/StableNodeAnchor.cs` 确实是 7 字段 record struct；
`src/NLCPG/Model/NLCPGCallSiteContext.cs` 确实是 `(string FilePath, int SpanStart, int SpanEnd, string DisplayName)`；
`src/NLCPG/Model/NLCPGEdgeLabel.cs` 确实是 `sealed record`（**引用类型**，所以 `NLCPGEdgeLabel?` 就是 8 B 引用）；
`src/NLCPG/Contracts/NLCPGEdgeKind.cs` 有 **36** 个成员（放得进 1 字节）。

【实测】**直接引用真实的 `NLCPG.csproj` 重新量了一遍**（不是本报告里的复刻类型），结果逐项一致：

```
StableNodeAnchor          = 28
NLCPGContextId            = 8
NLCPGContextId?           = 16
NLCPGCallSiteContext      = 24
NLCPGCallSiteContext?     = 32
PendingEdgeKey (REAL)     = 120      ← 与快照/sizeof 完全吻合
NLCPGEdgeKind count       = 36
HashSet<PendingEdgeKey>.Capacity @2,097,152 = 2,411,033
NLCPGEdgeLabel is class   = True
```

复现命令见 §7.3。

### 3.2 瘦身阶梯

| 步骤 | 手法 | 字段变化 | 节省 |
| --- | --- | --- | ---: |
| 1 | 两个 `StableNodeAnchor`(28) → `int` 节点序号 | 56 → 8 | **48 B** |
| 2 | `NLCPGCallSiteContext?`(32) → 侧表 `int` id | 32 → 4 | **28 B** |
| 3 | `NLCPGContextId?`(16) → 驻留 `int` id | 16 → 4 | **12 B** |
| 4 | `NLCPGEdgeLabel?`(8 引用) → 驻留 `int` id | 8 → 4 | **4 B** |
| 5 | `NLCPGEdgeKind`(4) → `byte`（36 个成员） | 4 → 1 | **3 B**（被对齐吸收） |
| | **合计** | 116 → **21**，对齐后 **24** | **92 B 有效** |

**结果**【实测】：

```
readonly record struct SlimEdge(
    int SourceOrdinal, int TargetOrdinal, NLCPGEdgeKind Kind,
    int LabelId, int ContextId, int CallSiteId);   // Unsafe.SizeOf = 24 B
```

若把 `Kind` 显式声明为 `byte`（36 个枚举值完全放得下）并取消对齐补位：

```
[StructLayout(LayoutKind.Sequential, Pack = 1)]
21 B  （实测）
```

> **注意**：`Pack = 1` 会带来**非对齐访问**。在 x64 上 .NET 能正确处理非对齐的托管 struct 字段读取，
> 但数组元素的非对齐会拖慢 SIMD/向量化路径，且 `Unsafe.As`/`MemoryMarshal` 相关的写法需要重新审视。
> **建议先用 24 B 的自然对齐版本**，`Pack = 1` 的 3 B 收益不值得这个风险。

### 3.3 各项手法的具体做法与 caveat

**(a) `StableNodeAnchor`(28 B) → 4 B 节点序号**

- 做法：给每个 `NLCPGNode` 分配一个 0..N-1 的稠密序号（构建期单调递增计数器），边上只存序号。
  `Materialize` 时用 `nodes[ordinal]` 直接取节点，**顺带消掉了
  `nodesByAnchor[item.SourceAnchor]` 这个字典查找**——当前 `Materialize` 里每个边要做两次
  `IReadOnlyDictionary<StableNodeAnchor, NLCPGNode>` 查找，这既是 CPU 也是内存（那个字典本身）。
- 前提：序号必须在 builder 生命周期内稳定。`NLCPGGraph` 当前已有 `NodeId` 概念（见 `NLCPGEdge.cs`
  的 `NodeId sourceNodeId`），**很可能序号已经存在，只是没被边缓冲用上**——值得先确认。
- caveat：`Materialize` 目前依赖 `nodesByAnchor`，改成序号后该字典可整体删除，
  但**任何按 anchor 查找的其它调用点**需要一并迁移。这是一个**连锁改动**，不是纯字段替换。

**(b) 驻留 `NLCPGContextId` / `NLCPGCallSiteContext` 到侧表 + int id**

- 做法：`Dictionary<NLCPGContextId,int>` 与 `Dictionary<NLCPGCallSiteContext,int>` 各一份，
  真实 id 从 1 开始，0 保留给 null。
- 收益：
  - `NLCPGContextId?` 16 B → `int` 4 B，净省 **12 B**（注意不是 4 B，见 §3.1 的说明）。
  - `NLCPGCallSiteContext?` 32 B → 4 B，净省 **28 B**。
- caveat：
  - **驻留表本身的成本**。若 callsite 上下文种类很多（每个调用点一个），
    侧表会退化成一个和边数同规模的字典，**反而更费内存**。
    必须在真实样本上测 `distinct(callsite) / edges` 这个比值。
    - 若比值 ≈ 1（几乎每个边一个独特 callsite），驻留**没有收益**，
      此时更该做的是**把 callsite 拆进 CSR 的平行属性数组**（§2.2），而不是驻留成 int id。
    - 若比值很小（比如 < 0.1，大量边共享同一调用点），驻留收益接近理论上限。
    - 【推导】当 `distinct × 32 B`（字典 Entry<vector 32 B 的 key>+int ≈ 8+8+32+4 → 56 B/条）
      超过 `edges × 28 B` 时，驻留就是亏的。粗略阈值：**distinct > 0.5 × edges 时不划算**。
  - **必须实测这个比值**，这是本节唯一需要先做实验才能定的参数。
  - 用 `NLCPGContextId` 作字典键时，它包着一个 `string`，默认 `record struct` 的相等用的是
    `string` 的序数相等——**正确但慢**。若在热路径上，考虑改用
    `Dictionary<string,int>` 直接驻留底层字符串。

**(c) 可空 → 哨兵 / int 索引，0 表示 null**

- 这正是 (b) 的实现手法：把 `NLCPGContextId?` / `NLCPGCallSiteContext?` / `NLCPGEdgeLabel?`
  统统换成 `int`，**约定 `0` = null**，真实 id 从 1 开始。
- 收益不只是字节：消掉了三个 `Nullable<T>` 的 `hasValue` 分支，
  `Materialize` 时也不用再做 `item.ContextId` / `item.CallSiteContext` 的可空解包。
- caveat：**必须把 "0 表示 null" 这条不变量写在类型上**（比如包一个 `readonly record struct
  InternedId(int Value)`，并提供 `IsNone`），否则"0 是合法 id"这种 bug 会在很久以后才暴露。
  当前 `NLCPGEdgeLabel?` 是**引用类型**，null 语义清晰；换成 int 后语义靠约定，
  **这是本方案引入的唯一真实风险**，用类型包装 + 断言可以消除。
- 另一个 caveat：当前 `StableNodeAnchor.ExtraKeyId` 已经用 `0` 表示"无"（见
  `src/NLCPG/Model/StableNodeAnchor.cs` 第 18-19 行的三元表达式），
  说明 **0-as-null 已经是本仓库的既有约定**，沿用它是**一致**的，不是新发明。

**(d) `NLCPGCallSiteContext?`(32 B) → 侧表 4 B**

见 (b)。补充一点：`NLCPGCallSiteContext` 内部是 `string, int, int, string`，
两个 `string` 是引用（指向堆上已有的字符串，不重复分配内容），
所以 32 B **只是栈上/数组内的结构体本身**，不含字符串内容。
这意味着即使不去重，**它也没有造成字符串内容的重复**——重复的只是那 32 B 的"指针+整数"打包。
所以这里的收益是 28 B/边，**不要期待更多**。

### 3.4 瘦身的总体账（n = 2,097,152）

【推导】：

```
单份载荷：120 B → 24 B，省 96 B/边
两份载荷：2 × 2,097,152 × 96 B = 402,653,184 B = 384.0 MiB
等价地说：两份载荷 2 × 2,097,152 × 120 B = 480.0 MiB → 2 × 2,097,152 × 24 B = 96.0 MiB
```

即**只做字段瘦身、不改任何数据结构**，仅载荷部分就省 384 MiB。
这是全部方案里**性价比最高的一步**。

【实测】端到端对照（2,097,152 次去重插入，从空开始，统计**累计分配**含倍增垃圾）：

| 方案 | 累计分配 | 最终活跃 | 活跃 B/边 |
| --- | ---: | ---: | ---: |
| 今天：`HashSet<PendingEdgeKey>` + `List<120B>` | 1,240,885,288 B (1,183.4 MiB) | 633,567,176 B (604.2 MiB) | 302.1 |
| 瘦身后：`HashSet<SlimEdge>` + `List<24B>` | 301,819,368 B (287.8 MiB) | 154,488,680 B (147.3 MiB) | 73.7 |
| | **4.11×** | **4.10×** | |

【实测】耗时对照（同一进程、同机）：

```
今天 HashSet<PendingEdgeKey> + List<120B> :  992 ms
瘦身 HashSet<SlimEdge>       + List<24B>  :  704 ms   (−29%)
```

**瘦身同时更快**——因为哈希的是 24 B 而不是 120 B，比较与拷贝都变便宜。
这一条推翻了"瘦身必然牺牲速度"的直觉。

### 3.5 瘦身必须守住的正确性边界

1. **去重语义不能变**。今天的去重键是"两锚点 + kind + label + context + callsite"六元组。
   换成序号 + 驻留 id 后，六元组必须**一一对应**到新的六元组。若序号分配有重复、
   或驻留表把两个不同 context 映到同一个 id，**去重就会错误合并边**。
   建议加一条**契约测试**：用原始 120 B 键和瘦身 24 B 键分别跑一遍去重，
   断言 `Count` 与每一条边的六元组集合**完全相等**。
2. **`StableNodeAnchor.CreateFallback` 的 Ordinal 恒为 0**（`StableNodeAnchor.cs` 第 16 行）。
   若改用序号，要确认 fallback 路径的节点也能拿到**唯一**序号，
   否则所有 fallback 锚点的边会互相判重。**这是最容易踩的坑**。
3. `Materialize` 的返回类型 `IReadOnlyList<PendingEdge>` 与下游契约不变。

---

## 4. Arena / 分块分配

### 4.1 LOH 阈值（【源】）

Microsoft Learn 明确：**对象 ≥ 85,000 字节即进入大对象堆（LOH）**；
阈值"was determined by performance tuning"。
URL 已 fetch：<https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap>

【源】.NET GC 设计文档补充了原因：**large objects 不走 allocation context/quantum，直接分配到 segment**，
且因为"compacting large objects is more expensive"才做了这个区分。
URL 已 fetch：<https://raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/garbage-collection.md>

### 4.2 当前边表的具体后果

【推导】以 120 B/元素计，LOH 阈值的临界容量：

```
cap   512 × 120 =    61,440 B   SOH
cap   708 × 120 =    84,960 B   SOH   ← 最后一个 SOH 容量
cap   709 × 120 =    85,080 B   LOH   ← 第一个 LOH 容量
cap 2,097,152 × 120 = 251,658,240 B  LOH
```

**只要 `List<BufferedPendingEdge>` 超过 708 个元素，它的后备数组就永久落在 LOH 上。**
对本场景（2.1 M 边）而言这**不是"会不会"的问题，是"必然"**。

【推导】瘦身到 24 B/元素后阈值变为：

```
cap 3,541 × 24 = 84,984 B   SOH
cap 3,542 × 24 = 85,008 B   LOH
```

### 4.3 `List<T>` 倍增的代价

【源】`List<T>` 源码：`DefaultCapacity = 4`，按"multiples of two"增长。
URL 已 fetch：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs>

【推导】2,097,152 恰好是 2^21，所以倍增序列会**精确**落在这个容量上
（4 → 8 → … → 1,048,576 → 2,097,152）。但**扩容瞬间两份数组同时存活**：

```
旧数组 1,048,576 × 120 B = 125,829,120 B
新数组 2,097,152 × 120 B = 251,658,240 B
峰值                      = 377,487,360 B = 360.0 MiB
```

加上此时 `HashSet` 的 Entry[]（370 MB+），**峰值瞬时接近 3.6× 最终边表**。
【实测】累计分配 1,240,885,288 B vs 最终活跃 633,567,176 B
——**多分配的 607 MB 全是倍增过程产生的垃圾**。这是"峰值堆占比 15.94%"这个数字背后的真实机制。

### 4.4 什么时候分块胜过单个大数组

分块（list-of-blocks，每块固定大小）胜过单个大数组的条件：

1. **要绕开 LOH**。单数组只要超过 85,000 B 就上 LOH；分块可以让每个块停在 SOH 内
   （比如 120 B × 512 = 61,440 B/块），从而享受 gen0/gen1 的廉价回收。
   **这是分块最硬的理由。**
2. **要消除倍增拷贝峰值**。分块增长只分配**一个新块**，不搬旧数据，
   峰值增量是 O(块大小) 而不是 O(总量)。
3. **总量不可预知**。`List<T>` 必须整块连续；分块不受此限。
4. **随机访问模式友好**。分块牺牲常数级的随机访问（需要 `block = i / B; offset = i % B`，
   或用 `int[] _blockStarts` 前缀和做 O(log blocks) 定位），
   但**追加**与**顺序遍历**都是 O(1) 摊还。

**反过来，以下情况单数组更好**：

- **需要把数组交给下游当 `T[]` / `Span<T>`**。这是决定性的：
  分块无法零成本地暴露为 `Span<T>`。（可以用 `Span<T>` 的 `MemoryMarshal.CreateSpan`
  骗过去，但那要求块**物理连续**，等于没有分块。）
- **需要 `BinarySearch` / 随机访问密集**。
- **总量已知且可预分配**（本场景 `_items` 的最终容量已知，可以用 `EnsureCapacity` 一次到位，
  直接消灭倍增峰值——**这是最省事的一招**）。

【推导】对本项目的具体判断：

- `Materialize` 目前把 `_items` 转成 `PendingEdge[]`（一次性大数组），
  说明**下游需要数组**。这种情况下分块只在"中间积累期"有用。
- **最低成本的改进**：保留单数组，但**用 `EnsureCapacity` 一次性预分配到预期上限**，
  消除倍增峰值（省掉那 607 MB 垃圾）。
- 若最终要走 CSR（§2），那么 `_items` 只是**中转**，分块 + 转 CSR 后整体释放是合理的：
  中转期用分块避免 LOH 与拷贝峰值，转完即刻丢弃。

### 4.5 `ArrayPool<T>`

【源】`ArrayPool<T>` 提供 `Shared` 单例与 `Rent`/`Return`，
其价值是"reusing instances of arrays ... can increase performance in situations where arrays
are created and destroyed frequently, resulting in significant memory pressure on the GC"。
URL 已 fetch：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Buffers/ArrayPool.cs>

【源】`SharedArrayPool<T>` 实现细节（URL 已 fetch）：
<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Buffers/SharedArrayPool.cs>
—— 分层缓存：每线程 TLS 桶 + 每核 stack，`NumBuckets = 27`（约 `SelectBucketIndex(2^30 + 1)`），
桶长从 16 起按 2 的幂次。

【源】`ConfigurableArrayPool<T>`（URL 已 fetch）：
<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Buffers/ConfigurableArrayPool.cs>
—— `DefaultMaxArrayLength = 1024 * 1024`（2^20），`DefaultMaxNumberOfArraysPerBucket = 50`，
内部桶最小 2^4、最大 2^30。

【源】`ValueListBuilder<T>` 的 `Grow()` 是 .NET 内部**"池化 + 增长"**的范例（URL 已 fetch）：
<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/Common/src/System/Collections/Generic/ValueListBuilder.cs>
—— 用 `ArrayPool<T>.Shared.Rent(nextCapacity)` 取新数组、`CopyTo`、然后把旧数组 `Return`；
并对"含引用"与"纯值类型"分别处理（前者需 `Return(toReturn, _pos)` 清理，后者直接 `Return`）。

**对本项目的适用性判断**：`ArrayPool` 在**短生命周期、反复分配同尺寸**的场景才划算。
`PendingEdgeBuffer` 的 `_items` 是**长生命周期**（整个 builder 期间持有），
`Rent` 之后要一直握到冻结，**没有复用机会**，反而会因为"必须 `Return` 同一实例"而
增加所有权复杂度。**建议不用 `ArrayPool` 管 `_items`。**
真正适合 `ArrayPool` 的是**临时缓冲**，例如：
- CSR 转换时的分桶计数/前缀和临时数组；
- `HashSet` 扩容时构造 `_buckets` 的临时工作区；
- 逐节点分组时的 per-node 小缓冲。

【实测】`ValueListBuilder<T>` 的这条经验有一个直接可用的推论：
`Grow()` 里对 `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` 的分支——
`SlimEdge` 全是值类型、**不含引用**，走的是"直接 `Return` 无需清理"的快路径。
这进一步支持第 3 节的瘦身：**去掉引用不仅省字节，还让数组回收变便宜**
（GC 不需要扫描 2.1 M 个引用槽）。

---

## 5. 真实项目先例

### 5.1 Joern / flatgraph（**最贴近本项目**）

【源】Joern 4.0 迁移说明（URL 已 fetch）：
<https://raw.githubusercontent.com/joernio/joern/master/changelog/4.0.0-flatgraph.md>

它**明确给出了实测内存数字**，据我所知是 CPG 领域最直接可引用的一组：

> "flatgraph brings us about 40% less memory usage as well as faster traversals.
> The reduced memory footprint is achieved by flatgraph's efficient columnar layout,
> essentially we hold everything in few (albeit very large) arrays."

以 Linux 4.1.16 用 c2cpg 生成的 CPG（**48M 节点 / 630M 属性 / 431M 边 / 115M 边属性**）为例：

| 指标 | joern 2 (overflowdb) | joern 4 (flatgraph) |
| --- | ---: | ---: |
| import 后堆（GC 后） | 33 GB | **20 GB** |
| import 所需最小 Xmx | 80 GB | **30 GB** |
| importCpg 耗时 | 18 分钟 | **11 分钟** |
| 磁盘文件 | 2600 MB | **400 MB** |

（原文：*"Numbers are based on my workstation and just rough measurements."*）

【源】flatgraph README（URL 已 fetch）：
<https://raw.githubusercontent.com/joernio/flatgraph/master/README.md>

**它的数据布局选择**，逐条对应我们讨论的问题：

1. **"The graph is stored in adjacency lists"** —— 用邻接表，不是全局边表。对应 §2。
2. **"edges are not explicitly stored in the graph"** —— 一条有向边 A→B 拆成两个 half-edge
   （B 加到 A 的出邻，A 加到 B 的入邻），**边对象根本不分配**。
   这正是消除"key/value 重复"的极端形态：**连一份都不存，只存邻接**。
3. **边身份靠位置配对**："we are using the obvious pairing between the first `A->B` half-edge
   (in the order of out-neighbors of A) against the first `B<-A` edge (in the order of in-neighbors
   of B), second-against-second and so on." —— 不存 id，靠**序号对应**建立身份。
   **这与我们 §3 用"节点序号"替代 28 B 锚点是同一个思想的两种应用。**
4. **列式 + 按需分配属性数组**："Until edge properties of an edgeKind are actually set,
   we store them as `DefaultValue(default)` instead of `Array[PropertyType]`, i.e.
   the edge property feature incurs **negligible O(1) memory cost unless actually used**."
   —— **属性的存储是惰性的**。这是我们可以直接抄的一招：
   `label`/`context`/`callsite` 三个属性在绝大多数边上都是 null，
   **不该为每条边都留出它们的槽位**。改成"稀疏属性侧表"（`Dictionary<int, X>` 只记非默认值）
   可能比 §3 的"每条边固定 4 B"更省。
5. **每条边最多一个属性**："Each edge has exactly one property." 且迁移说明里明确
   "Edges can only have zero or one properties. Since the codepropertygraph schema never defined
   more than one property per edge type, this should not affect you"。
   —— **schema 层面的约束换来了布局的简化**。我们的 36 种 `NLCPGEdgeKind` 是否也能
   约束成"每种 kind 最多一个附加属性"，值得评估。
6. **flatgraph 自己也记录了未做项**：README 的 "Performance and memory features" 里有
   `[ ] Support compressed memory representation if actual quantity is 0-1`
   —— 即"当某属性几乎总是 0/1 时用压缩表示"**还没做**。
   这反过来印证第 4 点的惰性属性数组是当前的有效手段。
7. **放弃了溢出到磁盘**："one of overflowdb's features was the overflowing-to-disk mechanism.
   While it sounds nice to be able to handle graphs larger than the available memory,
   in practice it was too slow to be useful, so we didn't reimplement it in flatgraph."
   —— **不要指望"落盘换内存"能解决问题**，这是踩过坑的团队的结论。

### 5.2 GAP Benchmark Suite（CSR 的权威参考实现）

【源】`graph.h` 与 `builder.h`，URL 见 §2.1。
`CSRGraph` 用 `out_index_`/`out_neighbors_`（有向再加 in 方向）；
`NodeWeight` 的 `operator==` **故意忽略权重**以便 `std::unique` 去重
（源码注释：*"doesn't check WeightT_s, needed to remove duplicate edges"*）。
构造是 `CountDegrees` → `PrefixSum` → 填充 → 逐节点 `std::sort`，**两遍遍历 + 排序**。

### 5.3 LLVM `BumpPtrAllocator`（arena 分配的权威实现）

【源】URL 已 fetch：
<https://raw.githubusercontent.com/llvm/llvm-project/main/llvm/lib/Support/Allocator.cpp>

该文件本身很薄，但暴露了 API 形态：`printBumpPtrAllocatorStats(unsigned NumSlabs, size_t TotalMemory)`
—— **slab（块）是 arena 的分配单位**，且提供"块数 + 总字节"的统计。
`PrintRecyclerStats(Size, Align, FreeListSize)` 则说明 LLVM 另有**按元素尺寸的回收链**
（`Recycler`），用于"同一尺寸对象反复申请释放"——这与 `ArrayPool` 的桶化思路同源。

【源】`SmallVector`（URL 已 fetch）：
<https://raw.githubusercontent.com/llvm/llvm-project/main/llvm/include/llvm/ADT/SmallVector.h>
—— 关键设计：`SmallVectorSizeType` 用
`sizeof(T) < 4 && sizeof(void*) >= 8 ? uint64_t : uint32_t`
来决定**尺寸字段的宽度**（源码注释：*"Using 32 bit size is desirable to shrink the size of
the SmallVector"*）。这与 §3 "把 8 B 引用换成 4 B int"是同一手法：
**用够用的最小宽度存索引**。

`SmallVectorBase` 布局为 `void *BeginX; Size_T Size; Size_T Capacity;`
—— 即"内联小缓冲 + 溢出后堆分配"，是分块/arena 思路的另一个变体。

### 5.4 scipy / SuiteSparse:GraphBLAS / LAGraph

见 §2.1 与 §2.5。核心结论：这些库的 CSR 布局值得借鉴，但**矩阵装配的去重语义
（重复项相加/合并）与 CPG 需要的"精确边身份"不兼容**，只能借布局不借语义。

### 5.5 Roslyn

**COULD NOT VERIFY**。我尝试了以下路径，全部返回 HTTP 404：

- `dotnet/roslyn` `src/Compilers/Core/Portable/InternalUtilities/README.md`
- `dotnet/roslyn` `src/Compilers/Core/Portable/InternalUtilities/PooledObjects/README.md`
- `dotnet/roslyn` `src/Compilers/Core/Portable/PooledObjects/PooledHashSet.cs`
- `dotnet/roslyn` `src/Compilers/Core/Portable/PooledObjects/ArrayBuilder.cs`
- `dotnet/roslyn` `src/Compilers/Core/Portable/PooledObjects/PooledDictionary.cs`

我对 Roslyn 的 `PooledObjects`（`PooledHashSet`/`ArrayBuilder`）与 `GreenNode` 的内部布局
**没有拿到任何一手来源**，因此**不做任何陈述**。
（`ArrayBuilder` 的"池化 + chunks 分块"是广为人知的，但本轮无法引用，故不写入结论。）
若要补，建议下一次直接用 GitHub 代码搜索页而非猜 raw 路径。

### 5.6 其它尝试但失败的来源

以下 URL 均 fetch 失败（404 或网络错误），**未用于任何结论**：

- `dotnet/runtime` `docs/design/coreclr/jit/large-object-heap.md` → 404
  （LOH 阈值改用了 Microsoft Learn 的权威页，见 §4.1）
- `dotnet/runtime` `HashHelpers.cs` 的多个路径 → 404
  （改为**实测反解**质数行为，见 §0.1）
- `dotnet/runtime` `TlsOverPerCoreLockedStacksArrayPool.cs` → 404
  （改用 `SharedArrayPool.cs`，已 fetch）
- `microsoft/SegmentedList` README → 404（该仓库可能不存在或已改名，**COULD NOT VERIFY**）
- `ImmutableList<T>` 的 Node 分块大小：我 fetch 到了
  <https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableList_1.cs>，
  确认它是**AVL 树**（源码："The root node of the AVL tree that stores this set"），
  但**没有拿到叶子节点的数组块大小**，故不引用具体数字。

---

## 6. 相对项目现状的具体建议

基于 `src/NLCPG/Model/NLCPGGraph.cs` 第 836-909 行的现状：

1. **立即消除"存两遍"**：无论选 (a) 还是 (c)，`PendingEdgeKey` 与 `BufferedPendingEdge`
   逐字段相同这件事本身就是缺陷——两个类型的存在只为了让 `HashSet` 和 `List` 各持一份，
   而且它们会**独立漂移**（改一个忘了另一个，去重键就和载荷不一致了）。
   **至少应该让其中一个从另一个派生**，用一个共享的 `EdgeRecord` 类型。
2. **先做 §3 瘦身**（120 B → 24 B），这是收益最大、风险最低、且**不改变语义**的一步：
   单次存储从 240 MiB 降到 48 MiB，两次存储省 384 MiB。
3. **再考虑 (c)**：开放寻址索引 + 瘦载荷 → 67 MiB（−89%）。
   若团队不接受手写哈希表，(a+slim) 的 `HashSet<SlimEdge> + List<SlimEdge>` 是 147.3 MiB（−76%），
   **用现成的 BCL 类型就能拿到四分之三的收益**，是很划算的折中。
4. **`Materialize` 里的两次字典查找**（`nodesByAnchor[item.SourceAnchor]`）在瘦身改用序号后
   可以完全消除，那个 `IReadOnlyDictionary<StableNodeAnchor, NLCPGNode>` 本身也是一大块内存
   （**未实测**，建议纳入下一次快照的量测范围）。
5. **确认 `StableNodeAnchor.CreateFallback` 的序号语义**（§3.5 第 2 点）——
   这是瘦身方案最可能出错的地方。
6. **惰性属性**（§5.1 第 4 点）值得评估：若 `callsite`/`label`/`context` 在绝大多数边上是 null，
   用"稀疏侧表"可能比"每条边固定 4 B id"更省。

---

## 7. 复现方式

### 7.1 复刻类型探针（一次性，未入库）

- `%TEMP%\nlcpgsize\Program.cs` —— 结构体尺寸、各方案字节账、LOH 边界、计时对照
- `%TEMP%\nlentry2\Program.cs` —— `Entry<T>` 精确尺寸反解、快照数字对账
- `%TEMP%\nlverify\Program.cs` —— 本报告 §0.2 / §2.2 / §3 / §4 全部表格数字的独立复算

```pwsh
Set-Location (Join-Path $env:TEMP 'nlcpgsize')
dotnet run -c Release
```

### 7.2 真实类型探针（引用 `src/NLCPG`，§3.1 的复核）

```pwsh
Set-Location (Join-Path $env:TEMP 'nlreal')
dotnet run -c Release
```

该项目的 `.csproj` 直接 `<ProjectReference>` 到
`D:\ProjectItem\SourceCode\Net\NL\src\NLCPG\NLCPG.csproj`，
因此量到的是**仓库里真实的** `StableNodeAnchor` / `NLCPGContextId` / `NLCPGCallSiteContext` /
`NLCPGEdgeKind`，而非本报告的复刻定义。

### 7.3 环境

.NET SDK 10.0.400 / 运行时 10.0.11 / x64 / `Environment.ProcessorCount = 16`。
所有尺寸用 `Unsafe.SizeOf<T>()` 或"精确分配量反解"得到，**不是** `Marshal.SizeOf`
（后者对含引用的泛型 `Entry<T>` 会抛
`Type ... cannot be marshaled as an unmanaged structure`，本轮已实测到该异常）。

**注意**：`HashSet<int>(2_097_152).Capacity` 在本机返回 `2_411_033`，
与快照自报的 `2_893_249` 不同（见 §0.1 的 COULD NOT VERIFY）。
这不影响各方案的**每边字节数**（那些用 Entry 尺寸与载荷尺寸算，
与具体质数无关，只有 bucket 数组的 4 B/槽受轻微影响）。
