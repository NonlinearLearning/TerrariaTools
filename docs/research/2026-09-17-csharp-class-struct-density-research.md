# C# 高密度对象中的 class 与 struct：内存和访问性能研究

> 状态：已完成网络资料核验；未在本仓库执行 class/struct 微基准。
>
> 日期：2026-09-17。
>
> 问题：在高密度对象场景中，将 class 改为 struct 后，内存可能明显下降、扫描访问可能变快。这是否是 C# 对 class 的设计问题？

## 结论摘要

观察到明显的内存下降和更快的顺序访问是可信的，但它是特定数据布局与工作负载的结果，不是 class 与 struct 之间固定的倍率，更不是 C# 的语言保证。

当大量小对象以 T[] 或 List<T> 形式保存时，class 路径通常包含两层存储：一个连续的引用数组，以及 N 个独立的托管对象。struct 路径则可以把 N 个值直接内联在同一个数组中。当前 CoreCLR 的对象元数据、每个引用槽位、N 次对象分配和更差的局部性可以叠加成很大的差异；顺序扫描也可能少一次间接寻址并改善缓存命中。[S3][S5][S6]

这不是 class 的“缺陷”。class 的独立对象身份、可共享引用、可空性、继承和多态是它提供的语义能力。struct 以复制语义换取内联存储；若类型大、可变、频繁复制或频繁装箱，struct 反而可能更慢、产生更多复制或装箱分配，甚至改变程序行为。[S1][S2][S3][S4]

因此，“内存下降一半、访问更快”应被判为需要用目标数据形状验证的假设；“所有 class 都应改为 struct”与“这是 C# 对 class 的错误设计”都不成立。

## 对消息的判定

| 说法 | 判定 | 依据与边界 |
| --- | --- | --- |
| 高密度的小 class 改为 struct 可能显著减少内存 | 有条件成立 | 值类型数组内联元素；引用类型数组只保存引用，实例另在堆中。[S3] |
| 扫描 struct 集合可能更快 | 有条件成立 | 官方设计指南指出值类型数组多数情况下有更好的 locality of reference；这不等同于每种访问都更快。[S3] |
| 内存一定下降一半 | 不成立 | 字段大小、引用字段、数组容量、对象对齐、运行时、架构、装箱和保留对象图都会改变结果。 |
| struct 一定在栈上 | 不成立 | 值类型可以在栈上，也可以内联于包含类型；T[] 本身仍是托管堆对象。[S3] |
| class 访问天生慢 | 不成立 | 访问速度由对象大小、存储拓扑、访问模式、JIT、CPU 缓存、分配与 GC 压力共同决定。 |
| 现象说明 C# 的 class 设计有问题 | 不成立 | 差异来自引用语义的有意成本与当前运行时表示；是否值得付出该成本取决于领域模型。 |

## 运行时模型

### 语言语义与存储语义不是一回事

Microsoft 的 C# 文档将引用类型变量定义为保存对象引用，将值类型变量定义为直接包含数据；值类型在赋值、传参和返回时默认复制值。[S1][S2] 这解释了两者的基本取舍：

| 维度 | class | struct |
| --- | --- | --- |
| 变量持有内容 | 对象引用 | 值本身 |
| 两个变量能否天然指向同一实例 | 可以 | 不可以；默认各持有一份副本 |
| 赋值与按值传参 | 复制引用 | 复制完整值 |
| 在 T[] / List<T> 中的元素 | 引用槽位，实例通常分散在其他对象中 | 值直接存于数组元素区域 |
| 转为 object 或接口 | 无需把自身再包装成盒 | 可能产生托管堆上的 boxed object |

“struct 在栈上”只是容易记忆但不精确的说法。重要问题不是局部变量使用了哪个关键字，而是数据最终由什么承载：局部、另一个对象的字段、数组元素、泛型集合元素还是 object/接口槽位。对于高密度集合，最关键的通常是数组元素是否内联。

### 高密度数组的实际差异

设有 N 个逻辑元素，每个元素都有两个 int 字段：

    sealed class PairRef { int X; int Y; }
    readonly struct PairValue { int X; int Y; }

在 PairRef[] 中，数组保存 N 个引用；每个 PairRef 又是单独的托管对象。对同一类数据，PairValue[] 把两个 int 直接排入一个数组对象的元素区域。以符号表示：

    class 数组 约为 一个数组头 + N 个引用槽位 + N 个对象头与字段及对齐
    struct 数组 约为 一个数组头 + N 个内联字段及元素布局

当前 CoreCLR 在 2026-09-17 读取的源码中，64 位目标的 OBJHEADER_SIZE 为两个 DWORD，OBJECT_SIZE 为一个指针，OBJECT_BASESIZE 因而为 16 字节；数组基础大小为对象头、方法表指针、元素数和填充组成的 24 字节。对象分配还会做指针对齐。也就是说，两个 int 的独立 class 实例在字段前已有约 16 字节的运行时开销；其数组还需一个 8 字节引用槽位。相同的两个 int struct 在数组中只需它的值布局。这个简化例子会给出远大于“减半”的量级，说明固定比例本身没有意义。[S5]

上述数值是当前 CoreCLR 源码的实现观察，不是 C# 规范合同，也不应直接推广到 Mono、NativeAOT、其他位数、未来运行时或包含不同字段的类型。自动字段布局、对齐、集合容量和其他对象的生命周期都必须在目标环境实测。

List<T> 的当前 CoreCLR 实现使用 T[] 作为 _items 后备字段，因此高密度 List<T> 通常具有相同的核心差异；List 对象和数组头是一次性共享成本，N 很大时主要差异仍在元素布局。[S6]

### 为什么顺序访问可能更快

1. 少分配 N 个独立对象。构造大量 class 元素会带来对象分配、存活期管理和 GC 压力；值元素内联时通常只增加一个数组或其扩容数组。[S3]
2. 少一次引用间接寻址。扫描 class 数组通常先取引用槽位，再访问实例字段；struct 数组可以从元素区域读取字段。
3. 更好的局部性。官方设计指南明确指出，值类型数组在多数情况下具有更好的 locality of reference；连续扫描小型值时，这通常减少缓存未命中的机会。[S3]
4. 更少的引用图。若 struct 只有非引用字段，GC 不需要处理 N 个独立的元素对象；但包含 string、数组或其他引用字段的 struct 仍会保留这些引用及其目标对象。

这些原因只解释“为什么可能变快”。随机访问、对象已经长期存活、元素很大、处理器缓存已被其他数据主导，或访问主要受计算而非数据读取限制时，收益可能很小或消失。

## 何时收益会消失或反转

### 大值与隐式复制

引用类型赋值只复制引用，值类型赋值和默认按值传参会复制整个值。Microsoft 的设计指南因此将“小于 16 字节”列为历史经验法则，并强调可变值类型容易产生意外副本。[S3] 该页面自身标明内容可能过时，故 16 字节只能作为审查起点，不能作为当前 JIT 的硬阈值。

对大 struct，应先测量真实调用链的复制量；必要时评估不可变设计、in 参数或 ref/readonly 访问，但这些是单独的 API 和语义设计问题，不能机械地附加到每个 struct。

### 装箱会重新引入对象分配

将 struct 转为 object 或它实现的接口时，CLR 会创建一个托管堆上的 boxed object；官方文档明确指出，这会分配和构造新对象，频繁装箱/拆箱在计算上昂贵。[S4] 因此以下路径必须在基准中单独检查：

- 非泛型集合、object 槽位或 object 参数；
- 接口 API、格式化、日志和反射边界；
- 委托、闭包或框架扩展点中把值作为 object 传递的路径。

不要仅因使用了泛型集合就假设绝无装箱；应以实际分配数据确认。

### 引用字段不会把引用目标内联

例如包含 string Name 的 struct 只会把 string 引用本身内联，字符串对象仍独立存在。若主要内存来自字符串、数组、字典或嵌套对象，把外层 class 改成 struct 只能省去外层对象头和引用槽位，未必接近一半。

### 语义改变可能比性能风险更大

应保留 class 的典型场景包括：

- 需要稳定对象身份、共享可变状态或 null 表示；
- 需要继承、多态、虚方法或复杂对象图；
- 实例很大、经常传递或经常修改；
- 外部 API 天然以 object 或接口处理值，且无法避免装箱；
- 元素不是“值”，而是具有独立生命周期的实体。

将一个可变 class 改为可变 struct 尤其危险：调用方可能无意中修改副本，而不是集合或宿主中的原值。[S1][S3]

## 实测协议

本报告没有用户的对象定义、集合类型、元素数量、生命周期或基准结果，因此不应从资料直接推出本项目的实际百分比。应以如下协议验证：

1. 保持业务语义一致：相同字段、相同元素数、相同集合容量、相同创建时机和相同访问结果。
2. 分开测量构造与扫描：构造基准观察分配和 GC；扫描基准在预先构造的同等数据上运行，避免把创建成本误判为字段访问成本。
3. 使用 BenchmarkDotNet 的 MemoryDiagnoser，记录 Mean、Allocated、Gen 0、Gen 1 和 Gen 2。该诊断器为跨平台内置能力，官方文档说明它会增加这些 GC 与分配列，并用每线程已分配字节采样。[S7]
4. 记录运行环境：SDK、运行时版本、x64/Arm64、CPU、GC 模式、Release 配置、BenchmarkDotNet 版本、N、预热与重复次数。不同机器或不同 N 的结果不能直接比较。
5. 对长时间或端到端场景，用 dotnet-counters 同时观测 System.Runtime 的分配、GC 次数和 GC 堆大小。官方文档给出了按进程监控 System.Runtime 及只选取 GC/累计分配计数器的命令形式。[S8]
6. 将“累计分配量”和“稳定存活内存”分开报告。Allocated 降低说明分配流量变少，不自动等于一次完整 GC 后的实时对象图也按同样比例缩小。
7. 若扫描变慢，先检查 struct 尺寸、复制点、装箱、引用字段和访问模式；不要只从平均时间推断 CPU 缓存原因。

应至少比较四类结果：构造时分配字节、构造时间、预构造集合扫描时间、完整 GC 后的保留内存。只有四者与业务语义共同成立，才适合把类型替换作为工程决策。

## 建议

对“数十万到数百万个、小型、近似 primitive 的记录，主要放在数组或 List<T> 中并被顺序读取”的热点，struct 是值得建立受控基准的候选。优先选择不可变、没有或很少引用字段、不会跨 object/接口边界频繁传递的值。

对需要身份、共享状态或多态的领域实体，保留 class；若只是希望降低高密度数据的存储成本，可以将热路径数据拆成小型 value record，而不是把整个对象模型一律改成 struct。

在获得目标类型的实际 BDN 和运行时计数器数据前，本报告不建议改变任何现有生产类型。

## 来源

- [S1：Microsoft Learn，Reference types（C# reference）](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/reference-types)，同时核对 [固定版本 Markdown 源](https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/csharp/language-reference/keywords/reference-types.md)。
- [S2：Microsoft Learn，Value types（C# reference）](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/value-types)，同时核对 [固定版本 Markdown 源](https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/csharp/language-reference/builtin-types/value-types.md)。
- [S3：Microsoft Learn，Choosing Between Class and Struct](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct)，同时核对 [固定版本 Markdown 源](https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/standard/design-guidelines/choosing-between-class-and-struct.md)。该页面明确标注部分内容可能过时。
- [S4：Microsoft Learn，Boxing and Unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing)，同时核对 [固定版本 Markdown 源](https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/csharp/programming-guide/types/boxing-and-unboxing.md)。
- [S5：dotnet/runtime，CoreCLR Object 与 ObjHeader 源码，提交 44210ed3](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/coreclr/vm/object.h)；[ObjHeader 定义](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/coreclr/vm/syncblk.h)。
- [S6：dotnet/runtime，List<T> 源码，提交 44210ed3](https://github.com/dotnet/runtime/blob/44210ed3b9e6ee2bb35e765e8a837d7f0c8c95dd/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs)。
- [S7：dotnet/BenchmarkDotNet，Diagnosers 文档，提交 e2eefbcf](https://github.com/dotnet/BenchmarkDotNet/blob/e2eefbcf76d189d9f23cc96e458f298e446b9b2b/docs/articles/configs/diagnosers.md)；[MemoryDiagnoser 源码](https://github.com/dotnet/BenchmarkDotNet/blob/e2eefbcf76d189d9f23cc96e458f298e446b9b2b/src/BenchmarkDotNet/Diagnosers/MemoryDiagnoser.cs)。
- [S8：Microsoft Learn，dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)，同时核对 [固定版本 Markdown 源](https://raw.githubusercontent.com/dotnet/docs/9b6d2a906aae04998aa3cdbbf81b9330993bb38f/docs/core/diagnostics/dotnet-counters.md)。

网络资料读取日期：2026-09-17。CoreCLR 和 BenchmarkDotNet 的提交哈希用于固定本报告引用的源码版本；Microsoft Learn 页面会随文档站点更新。
