# .NET 集合内存优化：ReferenceEqualityComparer 与键值体积（第 5、3 节）

> 日期：2026-09-23。状态：**外部一手来源 + 本地实测复核**。
> 本轮未修改生产代码。所有"实测"数字来自本机 .NET 10.0.11 / x64 上独立运行的一次性探针程序，
> 可复现（命令见文末），不是从别处抄来的。
>
> 工具情况：`web_search` 工具返回 **HTTP 401**（DeepSeek 搜索端点鉴权失败），本轮**未依赖**搜索工具；
> 全部外部结论均通过 `web_fetch` 直接抓取具体 URL 原文得到。

## 证据分级

| 标记 | 含义 |
| --- | --- |
| **【源】** | 已 fetch 到一手来源原文（dotnet/runtime 源码或 Microsoft Learn），下文给出 URL。 |
| **【实测】** | 本机 .NET 10 x64 运行探针测得，可复现。 |
| **【推导】** | 由【源】+【实测】推出的结论，**不是**独立测得的数字。 |
| **SPECULATION** | 无法用上述证据支撑的推测。 |
| **COULD NOT VERIFY** | 明确去找但没找到可引用来源。 |

---

## 5. ReferenceEqualityComparer vs default comparer

### 5.1 结论先行

**用 `ReferenceEqualityComparer.Instance` 不会改变 `Dictionary<TKey,TValue>` 或 `Entry<TKey,TValue>` 的体积，一个字节都不会。** 比较器引用是同一个 8 字节字段，传比较器只是**替换**了那个字段指向的实例，而不是**增加**一个字段。ReferenceEqualityComparer 的内存价值不在字典对象本身。

实测（x64，.NET 10.0.11）：

| 构造方式 | 每次构造分配字节 | 相对默认 |
| --- | ---: | ---: |
| `new Dictionary<object,int>()` | 80.00 | — |
| `new Dictionary<object,int>(ReferenceEqualityComparer.Instance)` | 80.00 | **0** |
| `new Dictionary<object,int>(new MyComparer())`（每次新建比较器） | 104.00 | +24 |

【实测】`EnsureCapacity(64)` / `EnsureCapacity(256)` 在两种比较器下分配完全一致：

```
default comparer, EnsureCapacity(64)  -> Capacity=71,  allocated=2120 bytes
RefEq  comparer,  EnsureCapacity(64)  -> Capacity=71,  allocated=2120 bytes
default comparer, EnsureCapacity(256) -> Capacity=293, allocated=8336 bytes
RefEq  comparer,  EnsureCapacity(256) -> Capacity=293, allocated=8336 bytes
```

差额恰好 0。而 `new MyComparer()` 那一行多出的 **24 字节正好等于一个空对象（8 字节头 + 8 字节 MethodTable 指针 → 最小对象 24 字节）**，因为它把"新分配的比较器对象"也计入了。

同时，带 256 个条目的字典（默认比较器 vs 单例比较器）实测都是 **8336 字节**，差 0；传入"每次新建的比较器实例"是 **8360 字节**，差 +24，即那个比较器对象本身。【实测】

> 换句话说：**如果比较器是单例或缓存的，字典大小不变；如果每次 `new` 一个比较器，账单是"多一个对象"，与字典的字段布局无关。**

### 5.2 为什么体积不变：`Dictionary` 对引用类型 TKey **总是**存一个比较器引用

`Dictionary<TKey,TValue>` 的字段声明（源文件原文）：

```csharp
private int[]? _buckets;
private Entry[]? _entries;
#if TARGET_64BIT
private ulong _fastModMultiplier;
#endif
private int _count;
private int _freeList;
private int _freeCount;
private int _version;
private IEqualityComparer<TKey>? _comparer;   // <-- 唯一的比较器字段，引用类型，8 字节
private KeyCollection? _keys;
private ValueCollection? _values;
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs>

构造函数原文（这是本节的**决定性证据**）：

```csharp
public Dictionary(int capacity, IEqualityComparer<TKey>? comparer)
{
    if (capacity < 0)
    {
        ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
    }

    if (capacity > 0)
    {
        Initialize(capacity);
    }

    // For reference types, we always want to store a comparer instance, either
    // the one provided, or if one wasn't provided, the default (accessing
    // EqualityComparer<TKey>.Default with shared generics on every dictionary
    // access can add measurable overhead).  For value types, if no comparer is
    // provided, or if the default is provided, we'd prefer to use
    // EqualityComparer<TKey>.Default.Equals on every use, enabling the JIT to
    // devirtualize and possibly inline the operation.
    if (!typeof(TKey).IsValueType)
    {
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        // ... string 特例见下
    }
    else if (comparer is not null && // first check for null to avoid forcing default comparer instantiation unnecessarily
             comparer != EqualityComparer<TKey>.Default)
    {
        _comparer = comparer;
    }
}
```

关键点，逐条对上你的问题：

1. **引用类型 TKey：`_comparer` 字段无条件被赋值**（`_comparer = comparer ?? EqualityComparer<TKey>.Default`）。所以传不传比较器，这个 8 字节引用字段都存在、都被写。传 `ReferenceEqualityComparer.Instance` 只是让指针指向另一个单例，**字段数、字段宽度、对象大小全都不变**。
2. **值类型 TKey：情况相反。** 只有当调用者传了一个"非 null 且不是 `Default`"的比较器时才写入 `_comparer`；否则字段保持 `null`，每次比较走 `EqualityComparer<TKey>.Default.Equals` 以便 JIT 去虚拟化并内联。注释里那句 "first check for null to avoid forcing default comparer instantiation unnecessarily" 就是为此。
3. 比较器的选择是**构造期一次性决策**，不影响 `Entry` 结构。

### 5.3 `Entry<TKey,TValue>` 长什么样

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

来源同上（Dictionary.cs）。

`Entry` 里**没有比较器字段**。比较器只存在于 `Dictionary` 对象上，是每字典一份，不是每条目一份。所以条目数组的 stride 完全由 `TKey`/`TValue` 决定。

【实测】重建 `Entry` 布局验证（x64）：

| 结构 | 字段原始之和 | `Unsafe.SizeOf` | 数组 stride |
| --- | ---: | ---: | ---: |
| `Entry<object,int>`（uint+int+object+int） | 20 | 24 | **24** |
| `Entry<int,int>`（uint+int+int+int） | 16 | 16 | **16** |
| `Entry<long,int>`（uint+int+long+int） | 20 | 24 | **24** |

注意 `Entry<object,int>` 从 20 → 24 的补齐**不是因为"有比较器"**，而是因为结构体含引用字段（8 字节对齐）。真正该做的是把 TKey 从对象引用换成 `int`，见第 3 节。

`Initialize` 里两个数组同尺寸分配：

```csharp
private int Initialize(int capacity)
{
    int size = HashHelpers.GetPrime(capacity);
    int[] buckets = new int[size];
    Entry[] entries = new Entry[size];
    ...
}
```

### 5.4 `ReferenceEqualityComparer` 的真实实现

完整实现（源文件原文）：

```csharp
#if SYSTEM_PRIVATE_CORELIB
    public
#else
    internal
#endif
    sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
    {
        private ReferenceEqualityComparer() { }

        /// <summary>
        /// Gets the singleton <see cref="ReferenceEqualityComparer"/> instance.
        /// </summary>
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object? obj)
        {
            // Depending on target framework, RuntimeHelpers.GetHashCode might not be annotated
            // with the proper nullability attribute. We'll suppress any warning that might
            // result.
            return RuntimeHelpers.GetHashCode(obj!);
        }
    }
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ReferenceEqualityComparer.cs>

三个事实：

- **不可实例化**（私有构造函数），**`Instance` 是单例**（`static` 自动属性 + 初始化器）。learn.microsoft.com 的 Remarks 也明确写着 "The `ReferenceEqualityComparer` type cannot be instantiated. Instead, use the `Instance` property to access the singleton instance of this type."（<https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.referenceequalitycomparer>）所以它**不产生每字典一次的比较器分配**——这正是它比"每次 `new` 一个自定义比较器"省 24 字节的原因。
- `Equals` 是 `ReferenceEquals(x, y)`，**不是** `x.Equals(y)`。
- `GetHashCode` 包装 `RuntimeHelpers.GetHashCode`，注意它标注了 `new`（隐藏 `object.Equals(object)`），因为它同时实现非泛型 `IEqualityComparer`。

⚠️ 一个容易踩的点：`ReferenceEqualityComparer` 实现的是 `IEqualityComparer<object>`，而 `IEqualityComparer<in T>` 的 T 是**逆变**的（源文件：`public interface IEqualityComparer<in T> where T : allows ref struct`，<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/IEqualityComparer.cs>）。所以它能直接喂给 `Dictionary<AnyRefType, V>` 的构造函数。

### 5.5 默认比较器到底是谁：`EqualityComparer<T>.Default` 的选择树

`EqualityComparer<T>` 本体（公共部分）**不定义** `Default`，源码里只有一行注释：

```csharp
public abstract partial class EqualityComparer<T> : IEqualityComparer, IEqualityComparer<T>
{
    // public static EqualityComparer<T> Default is runtime-specific
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/EqualityComparer.cs>

CoreCLR 侧实现在另一个 partial 文件里：

```csharp
public abstract partial class EqualityComparer<T> : IEqualityComparer, IEqualityComparer<T>
{
    // This method is special cased in R2R, with its implementation replaced by IL helper
    [Intrinsic]
    private static EqualityComparer<T> Create() => (EqualityComparer<T>)ComparerHelpers.CreateDefaultEqualityComparer(typeof(T));

    // To minimize generic instantiation overhead of creating the comparer per type, we keep the generic portion of the code as small
    // as possible and define most of the creation logic in a non-generic class.
    public static EqualityComparer<T> Default { [Intrinsic] get; } = Create();
}
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/System.Private.CoreLib/src/System/Collections/Generic/EqualityComparer.CoreCLR.cs>

**注意 `Default { get; } = Create()`**：这是一个**缓存的泛型静态属性**，`Create()` 每个 T 只跑一次，之后所有访问都读同一个静态字段。这点很重要——`EqualityComparer<T>.Default` 本身**不是**每字典一次分配。

选择树（`ComparerHelpers.CreateDefaultEqualityComparer`，源文件原文）：

```csharp
internal static object CreateDefaultEqualityComparer(Type type)
{
    Debug.Assert(type != null && type is RuntimeType);

    object? result = null;
    var runtimeType = (RuntimeType)type;

    if (type == typeof(string))
    {
        return new StringEqualityComparer();
    }
    else if (type.IsAssignableTo(typeof(IEquatable<>).MakeGenericType(type)))
    {
        // If T implements IEquatable<T> return a GenericEqualityComparer<T>
        result = CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(GenericEqualityComparer<string>), runtimeType);
    }
    else if (runtimeType.GetNullableUnderlyingType() is RuntimeType embeddedType)
    {
        // Nullable does not implement IEquatable<T?> directly because that would add an extra interface call per comparison.
        result = CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(NullableEqualityComparer<int>), embeddedType);
    }
    else if (type.IsEnum)
    {
        // The equality comparer for enums is specialized to avoid boxing.
        result = CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(EnumEqualityComparer<>), runtimeType);
    }

    return result ?? CreateInstanceForAnotherGenericParameter((RuntimeType)typeof(ObjectEqualityComparer<object>), runtimeType);
}
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/System.Private.CoreLib/src/System/Collections/Generic/ComparerHelpers.cs>

**`return result ?? ... ObjectEqualityComparer<object> ...` 这一行就是你问的"不实现 `IEquatable<T>` 的引用类型走 `ObjectEqualityComparer<T>`"的出处。**

`ObjectEqualityComparer<T>` 的实现（源文件原文）：

```csharp
public sealed partial class ObjectEqualityComparer<T> : EqualityComparer<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(T? x, T? y)
    {
        if (x != null)
        {
            if (y != null) return x.Equals(y);
            return false;
        }
        if (y != null) return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode([DisallowNull] T obj) =>
        obj?.GetHashCode() ?? 0;
    ...
}
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/EqualityComparer.cs>（经 GitHub contents API 取得的原文）

**`x.Equals(y)` 和 `obj?.GetHashCode()` 都是对 `object` 上虚方法的调用。** 对引用类型，如果 T 没有重写，虚分派落到 `Object.Equals`/`Object.GetHashCode`；如果重写了，就调用重写版本。无论哪种，都是**虚调用**（`callvirt`），JIT 只有在能做去虚拟化时才会省掉。

对比 `GenericEqualityComparer<T>`（`where T : IEquatable<T>?`）：

```csharp
public sealed partial class GenericEqualityComparer<T> : EqualityComparer<T> where T : IEquatable<T>?
{
    public override bool Equals(T? x, T? y)
    {
        if (x != null)
        {
            if (y != null) return x.Equals(y);   // 绑定到 IEquatable<T>.Equals(T)，非虚，可内联
            return false;
        }
        if (y != null) return false;
        return true;
    }

    public override int GetHashCode([DisallowNull] T obj) =>
        obj?.GetHashCode() ?? 0;
}
```

源码里紧邻的一段注释直说了意图：

```csharp
// The methods in this class look identical to the inherited methods, but the calls
// to Equal bind to IEquatable<T>.Equals(T) instead of Object.Equals(Object)
```

所以：**`IEquatable<T>` 的价值是让 `x.Equals(y)` 绑定到强类型非虚方法，从而可去虚拟化/内联；它不改变内存。**

【实测】验证选择树（打印 `EqualityComparer<T>.Default.GetType().Name`）：

```
class, NOT sealed, NO IEquatable<T>          -> ObjectEqualityComparer`1
class, NOT sealed, implements IEquatable<T>  -> GenericEqualityComparer`1
class, SEALED, NO IEquatable<T>              -> ObjectEqualityComparer`1
struct, NO IEquatable<T>                     -> ObjectEqualityComparer`1
struct, implements IEquatable<T>             -> GenericEqualityComparer`1
enum                                         -> EnumEqualityComparer`1
string                                       -> StringEqualityComparer
object                                       -> ObjectEqualityComparer`1
Nullable<int>                                -> NullableEqualityComparer`1
```

> 更正一处常见误解：**`sealed` 与否并不影响 `EqualityComparer<T>.Default` 的选择**——决定因素只有"是否实现 `IEquatable<T>`"（以及 string / Nullable / enum 特例）。实测中 `sealed` 的引用类型同样落到 `ObjectEqualityComparer<T>`。`sealed` 影响的是 **JIT 能否去虚拟化那些虚调用**，那是另一条路径。

### 5.6 ReferenceEqualityComparer 的真实收益（都在"算法/时间"，不在"字典对象体积"）

**(a) 语义：引用同一性 vs 值相等。** 【源】`ReferenceEqualityComparer.Equals` 是 `ReferenceEquals(x, y)`；`GetHashCode` 是 `RuntimeHelpers.GetHashCode(obj)`，文档说 "The returned hash code is based on the object identity, not on the contents of the object."（<https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.referenceequalitycomparer>）。这是**行为改变**，不只是性能改变。

【实测】语义差异：

```
两个内容相同但不同的 string 实例 a1, a2：
  a1.Equals(a2)                                  = True    (值相等)
  ReferenceEqualityComparer.Equals(a1, a2)       = False   (引用不等)
  EqualityComparer<string>.Default.Equals(a1,a2) = True

Dictionary<string,int>：值相等字典 Count=1（s2 命中 s1 的槽，覆盖）
Dictionary<string,int>(ReferenceEqualityComparer.Instance)：Count=2（s2 是不同对象，新增槽）
```

**这是最容易出事的地方**：换成 ReferenceEqualityComparer 后，"逻辑上相等"的键不再合并，字典条目数会**变多**——如果上游存在重复键，内存反而上升。必须确认"引用不同 = 逻辑不同"成立。

**(b) hash 质量。** 【实测】一个故意写坏 `GetHashCode()`（返回常量 `0x5EED`）的类型：

```
PoorHash.GetHashCode()                = 24301   (编译器/运行时实际返回该常量)
RuntimeHelpers.GetHashCode(PoorHash)  = 22947825
ReferenceEqualityComparer.GetHashCode = 22947825   (同一函数)
```

`RuntimeHelpers.GetHashCode` 不受用户重写影响，因此天然免疫"用户写了退化 hash"这类问题。它基于对象标识，文档（<https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode>）用 string 举例说明：值相等的两个字符串 `Object.GetHashCode` 给出相同值，而 `RuntimeHelpers.GetHashCode` **不会**（其中一个是被驻留的字面量，另一个来自 `String.Format`）。

⚠️ 但**不能反过来说 `RuntimeHelpers.GetHashCode` 的 hash 质量一定更好**：常量 hash 只是极端反例，一个写得好的值 hash 分布可以优于标识 hash。**SPECULATION**：对本仓库而言，若键类型已有质量良好的 `GetHashCode` 且 `Equals` 便宜，ReferenceEqualityComparer 的 hash 优势未必成立，需要用真实数据测。

**(c) 省掉用户 `GetHashCode`/`Equals` 的成本——这是算法成本，不是内存。** 【源】`ObjectEqualityComparer<T>` 走虚分派（5.5 节原文），若用户重写的方法很重（例如 `Equals` 递归比较大对象图、`GetHashCode` 拼字符串），换成引用比较能省掉这部分 CPU。**COULD NOT VERIFY**：本轮没有找到 Microsoft 或工程博客给出的、针对"ReferenceEqualityComparer 省了多少 ns/op"的**测量数字**。这属于需要本仓库自行 benchmark 的项。

**(d) 一条【推导】级别的负面结论**：因为 `Entry` 不含比较器、`Dictionary` 的 `_comparer` 字段对引用类型恒存在，**把 default comparer 换成 `ReferenceEqualityComparer.Instance` 不会减少任何字节**。想省内存，必须动 `TKey`/`TValue` 的类型（见第 3 节），或者减少条目数/容量。

### 5.7 第 5 节小结

| 问题 | 答案 | 依据 |
| --- | --- | --- |
| 改变 `Dictionary` 大小？ | **否**，0 字节 | 【源】引用类型 TKey 恒存 `_comparer`；【实测】80 = 80 |
| 改变 `Entry` 大小？ | **否**，`Entry` 无比较器字段 | 【源】Dictionary.cs `struct Entry` |
| 有额外字段吗？ | 没有，是**替换**同一个 8 字节引用 | 【源】构造函数原文 |
| 单例会多分配吗？ | 不会，`Instance` 是静态单例 | 【源】`static ... Instance { get; } = new()` |
| 每次 `new` 比较器呢？ | 会多 24 字节/字典（最小对象） | 【实测】104 − 80 = 24 |
| 主要影响 | 语义、hash 质量、省 CPU；**不是内存** | 【源】+【实测】 |
| 什么时候真省内存 | 当它能让你**减少条目数或容量**时 | 【推导】 |

---

## 3. Reducing key/value size

### 3.1 结论先行

真正的减重手段只有一条主线：**把 8 字节对象引用换成 4 字节或更小的值类型索引/键**，并接受"数组容量按质数增长"带来的额外系数。其余（`ushort`/`byte`、`Nullable<T>`）单独用往往**省不到**，甚至**变贵**。

【实测】同进程、同容量下只改 `TKey`（键在测量区外预建，故**不含**键对象本身）：

| 请求容量 | 实际 Capacity | `Dictionary<object,int>` 总分配 | `Dictionary<int,int>` 总分配 | 每条目差额 |
| ---: | ---: | ---: | ---: | ---: |
| 64 | 71 | 2,120 B | 1,552 B | **8.0000 B** |
| 256 | 293 | 8,336 B | 5,992 B | **8.0000 B** |
| 1024 | 1103 | 31,016 B | 22,192 B | **8.0000 B** |

三组容量的每条目差额都**精确等于 8.0000 字节**，即一个 8 字节引用 vs 一个 4 字节 `int` 的差。
把固定开销（80 B 字典对象 + `int[] buckets` 的 24 B 头 + 4 B/槽）扣掉后，得到每条目 stride：

| 请求容量 | `Entry<object,int>` | `Entry<int,int>` |
| ---: | ---: | ---: |
| 71 | 24.3944 | 16.3944 |
| 293 | 24.0956 | 16.0956 |
| 1103 | 24.0254 | 16.0254 |

（尾数偏离整数是因为 `Capacity` 取质数、而质数/6 不整除；stride 本身是 **24 与 16**，由上表 5.4 节的 `Unsafe.SizeOf`/数组 stride 直接证实。）

> 注意上表 `Dictionary<object,int>` 的总分配**不含键对象本身的分配**（键在测量区外预建）。若把 `new object()` 键算进去，代价更高：`Dictionary<object,int>` 建 1024 个键并插入实测 **63,896 B**，而 `Dictionary<int,int>` 是 **22,192 B**——单键合计约 62 B vs 22 B。【实测】

### 3.2 对象头与最小对象尺寸（这一条有权威源）

dotnet/runtime 的 CoreCLR 头文件 `object.h` 原文：

```c
#ifdef TARGET_64BIT
#define OBJHEADER_SIZE      (sizeof(DWORD) /* m_alignpad */ + sizeof(DWORD) /* m_SyncBlockValue */)
#else
#define OBJHEADER_SIZE      sizeof(DWORD) /* m_SyncBlockValue */
#endif

#define OBJECT_SIZE         TARGET_POINTER_SIZE /* m_pMethTab */
#define OBJECT_BASESIZE     (OBJHEADER_SIZE + OBJECT_SIZE)
```

以及：

```c
//
// The generational GC requires that every object be at least 12 bytes
// in size.

#define MIN_OBJECT_SIZE     (2*TARGET_POINTER_SIZE + OBJHEADER_SIZE)
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/vm/object.h>

同一文件的对象模型注释：

```
 * Object              - This is the common base part to all CLR objects
 *  |                        it contains the MethodTable pointer and the
 *  |                        sync block index, which is at a negative offset
```

**逐字节推算（x64，`sizeof(DWORD)` = 4，`TARGET_POINTER_SIZE` = 8）：**

- `OBJHEADER_SIZE` = 4 (alignpad) + 4 (SyncBlockValue) = **8 字节**
- `OBJECT_SIZE`（MethodTable 指针）= **8 字节**
- 所以负载起始偏移 = 8 + 8 = **16 字节**
- `MIN_OBJECT_SIZE` = 2×8 + 8 = **24 字节**

【实测】直接印证：`new long[0]` 分配 **24 字节**（负载 0，开销 24）；`new long[1]` = 32；`new long[2]` = 40；`new long[1000]` = 8024（负载 8000，开销固定 24）。

> ⚠️ 关于"数组头是不是 16 字节"的常见说法：`object.h` 里数组另有 `ARRAYBASE_SIZE = OBJECT_SIZE + sizeof(DWORD) m_NumComponents + sizeof(DWORD) pad` = 16，加上 `OBJHEADER_SIZE` 8，合计 **24 字节**——与实测的"固定 24 字节开销"一致。README 式的"16 字节头"说法容易把数组长度字段漏掉或算错，**建议直接用实测的 24**。

> 另一处可引用：learn.microsoft.com 的 GC 设计文档说 "the average size of managed objects are around 35 bytes"（<https://raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/garbage-collection.md>）——可作为"每个对象 24 字节固定开销"的量级感受来源，但它**不是**对象布局的权威定义。

### 3.3 引用字段会把结构体数组的 stride 抬到 8 的倍数

这是"116 → 120"那条规则的**正确表述**。实测隔离了变量（x64）：

| 结构 | 字段原始和 | `Unsafe.SizeOf` | 数组 stride |
| --- | ---: | ---: | ---: |
| 5×`int`（无引用） | 20 | **20** | **20** |
| 3×`int` + 1 引用 | 20 | **24** | **24** |
| 4×`int`（无引用） | 16 | 16 | 16 |
| 2×`int` + 1 引用 | 16 | 16 | 16 |
| `long` + `int`（无引用） | 16 | 16 | 16 |
| `[StructLayout(Explicit, Size=116)]`，仅 `long` | 116 | **116** | **116** |
| `[StructLayout(Explicit, Size=116)]`，含引用 | 116 | **120** | **120** |

【实测】关键对照：同样是 20 字节原始和，**无引用版 stride = 20，含引用版 stride = 24**。这就是 116 → 120 的机制。

**准确的规律（【推导】，与实测一致）：** 一个结构体若不含引用字段，其数组 stride 可以只等于"最大字段对齐"的倍数（全 `int` 就是 4 的倍数，故 20 合法）；一旦含引用字段，stride 必须抬到**指针大小（x64 上 8）**的倍数。所以"116 字节含引用 → 120"成立，但"116 字节无引用 → 116"也成立，**不能一概说"含引用就一定 8 字节对齐"而不提反例**（`long`+`int` = 16 本身就是 8 的倍数，看不出差别，容易误判）。

官方文档侧可引用的对齐规则（`StructLayoutAttribute.Pack` 的 Remarks）：

> - The alignment of a type is the size of its largest element (for example, 1, 2, 4, or 8 bytes) or the specified packing size, whichever is smaller.
> - Each field must align with fields of its own size or the alignment of the type, whichever is smaller. ... even if the largest field in a type is a 64-bit (8-byte) integer or the Pack field is set to 8, `Byte` fields align on 1-byte boundaries, `Int16` fields align on 2-byte boundaries, and `Int32` fields align on 4-byte boundaries.
> - Padding is added between fields to satisfy the alignment ...

来源：<https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack>

`sizeof` 文档补充（重要，别混用两个 API）：

> The `sizeof` operator returns the number of bytes allocated by the common language runtime in managed memory. For struct types, that value includes any padding, as the preceding example demonstrates. The result of the `sizeof` operator might differ from the result of the `Marshal.SizeOf` method, which returns the size of a type in *unmanaged* memory.

来源：<https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/sizeof>（文档里的示例本身就是 `Point{byte,double,double}` → **24**，即 1 + 7 pad + 8 + 8）

【实测】两者确实会分叉：

```
S12Ref（Explicit Size=12，含引用）  managed=16  unmanaged=12
ByteInt（byte+int）                 managed=8   unmanaged=8
IntLong（int+long）                 managed=16  unmanaged=16
```

即**托管视角**会为引用字段做 8 字节对齐，**非托管视角**不会。

### 3.4 `Pack=N` 与 `LayoutKind.Explicit` + `FieldOffset`

**逐字节验证 `Pack=1` 真的能压（【实测】）：**

| 结构 | 默认 | `Pack = 1` |
| --- | ---: | ---: |
| `byte` + `int` | `sizeof`=8, stride=8（1 + 3 pad + 4） | **`sizeof`=5, stride=5** |
| `int` + `ushort` | `sizeof`=8, stride=8（4 + 2 + 2 pad） | **`sizeof`=6, stride=6** |

`Pack` 文档明确取值受限："The value of `Pack` must be 0, 1, 2, 4, 8, 16, 32, 64, or 128. The default value is 0."（同 3.3 的 URL）。

`LayoutKind` 三个成员（<https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.layoutkind>）：

- `Sequential = 0`："The members of the object are laid out sequentially, in the order in which they appear when exported to unmanaged memory. The members are laid out according to the packing specified in `Pack`, and can be **noncontiguous**."
- `Explicit = 2`："The precise position of each member of an object in unmanaged memory is explicitly controlled... Each member must use the `FieldOffsetAttribute` to indicate the position of that field within the type."
- `Auto = 3`："The runtime automatically chooses an appropriate layout... Objects defined with this enumeration member **cannot be exposed outside of managed code**."

【实测】`Explicit` 可以做到重叠/精确控制：

```
UnionIntFloat ([FieldOffset(0)] int + [FieldOffset(0)] float)  sizeof=4   (别名复用同一 4 字节)
OverlappedPair (Size=8, int@0 + int@4)                         sizeof=8
```

⚠️ **风险提示（【推导】）**：`Pack=1` 会产生**未对齐**字段访问，在部分架构上是性能损失甚至需要运行时处理；`Explicit` 的重叠布局会让"同一块字节有两个含义"，对 GC 追踪引用字段尤其危险（重叠的引用/非引用字段会让 GC 描述符失效）。用于**纯值类型、无引用字段**的紧凑存储才安全。**COULD NOT VERIFY**：本轮没找到 Microsoft 明确给出"Pack=1 的性能代价%"或"Explicit 重叠 + 引用字段"的官方警告原文。

### 3.5 `Nullable<T>` 的填充代价（有源码）

`Nullable<T>` 的**实际字段**（源文件原文）：

```csharp
public partial struct Nullable<T> where T : struct
{
    private readonly bool hasValue; // Do not rename (binary serialization)
    internal T value; // Do not rename (binary serialization) or make readonly (can be mutated in ToString, etc.)
```

来源：<https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Nullable.cs>

注意文件顶部还有一条硬约束：

```csharp
// Because we have special type system support that says a boxed Nullable<T>
// can be used where a boxed T is used, Nullable<T> can not implement any interfaces
// at all (since T may not).
//
// Do NOT add any interfaces to Nullable!
```

以及 `[NonVersionable] // This only applies to field layout`——说明字段布局被当作稳定契约。

**逐字节推算：** `bool` 是 1 字节（`sizeof(bool)` = 1，见 3.3 的 sizeof 文档），字段顺序是 `hasValue` 在前、`T value` 在后。`T` 的对齐要求会把 `bool` 之后的 `value` 推到 `T` 的对齐边界，结构体总大小再向上取整到 `T` 的对齐倍数：

| 类型 | 朴素预期 (1 + sizeof(T)) | 实测 `sizeof` / 数组 stride | 浪费 |
| --- | ---: | ---: | ---: |
| `Nullable<byte>` | 2 | **2** / — | 0 |
| `Nullable<int>` | 5 | **8** / stride **8** | **3 字节** |
| `Nullable<long>` | 9 | **16** / stride **16** | **7 字节** |
| `Nullable<double>` | 9 | **16** / — | 7 字节 |

【实测】即"`Nullable<int>` = 8 而非 5"、"`Nullable<long>` = 16"两条都对。

**含义（【推导】）**：`Nullable<int>` 数组相对 `int[]` 是 **2× 内存**（stride 8 vs 4）。如果同一结构里既存一个 `int` 又存一个"该 int 是否有效"的标记，用 `Nullable<int>` 得 8 字节；用"`int` + 独立 `bool`"也要 8 字节（4 + 1 + 3 pad）；**只有当这个 bool 能和别的 bool 合并、或该字段本就落在别的填充里时，拆开写才更省**。若要真正压到 5 字节，需要 `Pack=1`（风险见 3.4）或改用"哨兵值"（例如用 `-1` 表示无效）——后者让字段回到 4 字节。

### 3.6 `uint` / `ushort` / `byte`：单独缩一个字段往往省不到

**规则（【源】+【实测】）**：字段按"自身大小"对齐（`Pack` 文档：`Byte` 对齐 1、`Int16` 对齐 2、`Int32` 对齐 4），但**结构体总大小要向上取整到其最大对齐**。所以把一个 `int` 改成 `ushort` 后：

| 结构 | 字段和 | `sizeof` |
| --- | ---: | ---: |
| `int, int` | 8 | **8** |
| `int, ushort` | 6 | **8**（尾部 2 字节填充） |
| `ushort, ushort` | 4 | **4** |
| `byte, int` | 5 | **8**（1 + 3 pad + 4） |
| `byte` 单独 | 1 | **1** |
| `byte × 4` | 4 | **4** |

【实测】

**结论（【推导】）**：
- **单独**把一个 `int` 降成 `ushort` 而结构里还有 `int`：**省 0 字节**，尾部填充吃掉全部收益。
- 把**成对**的字段一起降（两个 `int` → 两个 `ushort`）：8 → 4，**真省一半**。
- `byte` 只有凑成 4 个（或与其它小字段按顺序紧密排列）才回本；单个 `byte` 夹在 `int` 之间反而制造填充。
- 省小的字段**必须同时重排字段顺序**（大对齐字段在前、小字段聚拢），否则填充只是换了个位置。

### 3.7 字典容量的隐性放大：质数增长

【源】`Dictionary.Initialize` 用 `HashHelpers.GetPrime(capacity)` 把请求容量抬到下一个质数，`buckets` 和 `entries` 都是这个长度：

```csharp
int size = HashHelpers.GetPrime(capacity);
int[] buckets = new int[size];
Entry[] entries = new Entry[size];
```

【实测】`EnsureCapacity(256)` 实际得到 `Capacity = 293`（约 **+14%**）；`EnsureCapacity(1024)` → `Capacity = 1103`（约 **+7.7%**）。

所以"把键从 8 字节换成 4 字节"的净收益要按**真实容量**算，而不是请求容量。3.1 节的三组数据已按真实容量折算。

**COULD NOT VERIFY**：`HashHelpers` 的源文件本轮没抓到——`src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs` 和 `.../Generic/HashHelpers.cs` 两个路径在 GitHub contents API 上都返回 **404**，raw 抓取也失败。因此**质数表的具体数值没有一手来源**，上文的 293 / 1103 是【实测】`Capacity` 属性的返回值，不是读源码得来的。

### 3.8 关于"测得的节省数字"

**【实测】本轮自己产出的数字（可作为本仓库的基线）：**

- 对象最小尺寸 **24 字节**（`new long[0]` 实测）；数组固定开销 **24 字节**。
- 引用键 → `int` 键，每条目 **精确 −8.0000 字节**（`Entry` stride 24 → 16，即槽位内存 −33%），在容量 71 / 293 / 1103 三组下一致。
- 含引用字段的结构体数组 stride 从 20 → 24（同等 20 字节原始和的无引用版仍是 20）。
- `Nullable<int>` stride 8（朴素预期 5）、`Nullable<long>` stride 16（朴素预期 9）。
- 每字典一个新建比较器对象 = +24 字节；用单例比较器 = 0。

**外部来源情况（诚实汇报）：**

- **COULD NOT VERIFY**：没有找到 Microsoft 官方或知名工程博客给出的、针对"`int` 索引替代对象引用"的**带基准的节省百分比**。我抓取的 Microsoft Learn GC 文档（<https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals>、<https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance>）都是**方法论与工具**（`!dumpheap -stat`、`% Time in GC`、ETW），**不给具体数字**。
- **部分可引**：<https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance> 里有一句方向性建议——"Using an integer instead of a string for an ID can be more efficient. If the same string is being repeated thousands of times, consider string interning."——这是**定性**建议，没有数字。
- **可引的设计指引**：Framework Design Guidelines 的 *Choosing Between Class and Struct* 说值类型数组"allocated inline... value type arrays exhibit much better locality of reference"，并给出"instance size under 16 bytes"的经验阈值（<https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct>）。**注意该页自己声明是 2008 年第 2 版重印、部分内容可能过时**，引用时应标注。
- **SPECULATION**：本仓库若采用"8 字节引用 → 4 字节索引"，在**字典槽位**上按 3.1 的 8 字节/槽是可预测的；但**端到端**堆占用还取决于索引所指对象的额外数组（索引间接层本身要占 4 字节/项）以及是否因此减少了对象数量。**没有实测前不要对外报端到端节省比例。**

### 3.9 第 3 节小结（按"证据强度 × 收益"排序）

| 手段 | 收益 | 证据 |
| --- | --- | --- |
| 8 字节引用 → 4 字节 `int` 索引/键 | **精确 −8.0000 字节/条目**（stride 24 → 16） | 【实测】+【源】`Entry` 布局 |
| 消除键对象本身 | 每键约 −24 字节固定开销起 | 【源】`MIN_OBJECT_SIZE` + 【实测】 |
| 成对降宽（两个 `int` → 两个 `ushort`） | 8 → 4 字节 | 【实测】 |
| **单独**降一个字段 | **常常 0** | 【实测】`int,ushort` 仍为 8 |
| `Nullable<int>` → 哨兵值 | 8 → 4 字节 | 【源】字段 + 【实测】 |
| `Pack=1` | 8 → 5、8 → 6 | 【实测】；但有未对齐风险 |
| `[FieldOffset]` 重叠 | 可做到 4 字节别名 | 【实测】；引用字段上危险 |
| 换用 `ReferenceEqualityComparer` | **字典体积 0** | 【源】+【实测】 |

**推荐执行顺序（【推导】）**：先把二级索引/键从引用改 `int`，再考虑成对降宽并**同时重排字段**，最后才谈 `Pack`/`Explicit` 这类高风险手段。每一步都按本仓库基准重新测。

---

## 附录 A：本轮 404 / 失败清单（诚实汇报）

| URL | 结果 |
| --- | --- |
| `https://raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/objects.md` | **404**（该文件不存在；BOTR 目录里没有 `objects.md`，对象布局应查 `src/coreclr/vm/object.h`） |
| `https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/EqualityComparer.CoreLib.cs` | **404**（实际文件名是 `EqualityComparer.CoreCLR.cs`，在 `src/coreclr/` 下） |
| `https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/HashHelpers.cs` | **404** |
| `https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Collections/HashHelpers.cs` | **404** |
| `https://cdn.jsdelivr.net/gh/dotnet/runtime@main/...EqualityComparer.cs` | **失败**：`unsupported content type "application/octet-stream"`（镜像是压缩转发，不可用） |
| `https://raw.githubusercontent.com/...` 多次重试 | **间歇性 `TypeError: fetch failed`**（网络抖动；改用 `api.github.com` contents API 成功取得同一文件原文） |

> `docs/design/coreclr/botr/type-system.md` **存在**（已 fetch，HTTP 200），但内容讲 MethodTable/EEClass/FieldDesc 等**运行时类型系统数据结构**，**不含**"对象头 8 字节 / 最小对象 24 字节"的表述。对象尺寸的权威来源是 `src/coreclr/vm/object.h`。

## 附录 B：实测环境与复现

- 运行时：**.NET 10.0.11**，SDK 10.0.400，`IntPtr.Size = 8`（x64），`-c Release`。
- 探针工程：`layoutprobe`（`net10.0`，`AllowUnsafeBlocks=true`），使用 `GC.GetAllocatedBytesForCurrentThread()` 差值法测分配、
  `Unsafe.SizeOf<T>()` 测托管尺寸、`typeof(...).Name` 验证比较器选择。
- 数组 stride 的求法：`(AllocFor<T>(1001) - AllocFor<T>(1)) / 1000`。
- 复现命令：

```powershell
dotnet run --project <探针目录>\layoutprobe.csproj -c Release
```

> 探针程序位于本机临时目录（`%TEMP%\layoutprobe`），**不是**本仓库的受版本控制产物。若要长期保留这些数字，
> 应把它移入 `tests/` 下的 Performance 工程并纳入验证矩阵；本轮未做此改动。
