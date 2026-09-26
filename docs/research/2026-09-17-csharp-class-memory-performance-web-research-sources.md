# C# class 内存性能问题：联网资料与处置路线

> 状态：通用资料调研，不包含 NLCPG 或当前提交的性能测量。
>
> 日期：2026-09-17。
>
> 资料范围：Microsoft Learn、dotnet 官方源码文档与 BenchmarkDotNet 官方文档。所有结论都应以目标工作负载的实际测量为准。

## 简短结论

class 本身不是需要“修复”的缺陷。reference type 的成本主要来自四件事：

1. 每次独立实例分配带来的 allocation flow 和 GC 工作；
2. 仍被 root、缓存或索引持有的对象形成的 retained heap；
3. 大型数组、字符串和缓冲区造成的 LOH 与碎片问题；
4. 间接引用、重复索引和临时对象造成的访问局部性与复制成本。

把 class 一律改成 struct 只能覆盖其中一部分场景，还可能让多个数组、字典或索引复制完整值，并丢失对象身份和共享语义。应先识别是哪一种成本，再针对性处理。

## 先把症状分开

| 观察到的现象 | 首先验证什么 | 通常优先处理的方向 | 不应直接推导 |
| --- | --- | --- | --- |
| 分配率高，但请求后堆很快回落 | 每操作 allocated bytes、Gen0/Gen1 次数 | 消除临时对象，复用高频数组或缓冲区 | “所有长期模型都必须改 struct” |
| 进程堆持续增长 | 哪些类型 retained，谁持有 root | 释放过期 cache、去除重复 payload、缩短生命周期 | “GC 没有工作” |
| 大数组或大字符串后出现 Gen2/LOH 压力 | 单次分配大小、LOH 大小、存活率 | 分块、流式处理、必要时 ArrayPool | “小 class 的对象头是主因” |
| 热扫描很慢，且数据是密集、小、不可变值 | 数组布局、缓存未命中、拷贝次数 | 评估紧凑的值数组、ID/ordinal 索引 | “struct 总会更快” |
| value type 改造后分配没有下降 | object/interface 边界与格式化路径 | 消除 boxing，保留泛型 API | “值类型一定不分配” |

CLR 的 GC 文档指出，reference type 在 managed heap 上分配；分配量和 survived memory 共同决定 collection 的频率与持续时间。它也明确建议减少不必要的堆对象和过度分配的数组容量。[GC fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals)

## 推荐的诊断顺序

1. 固定输入、运行时版本、配置和并发度，先取得 warm-up 后的重复 baseline。
2. 用 BenchmarkDotNet 的 MemoryDiagnoser 对微观候选记录每次操作的 Allocated 与 Gen 0/1/2；它不是默认启用的诊断器。[BenchmarkDotNet diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
3. 用 dotnet-counters 观察运行中的 System.Runtime 指标和 allocation / GC 趋势。该工具定位的是 first-level investigation，不能替代堆归因。

       dotnet-counters monitor --process-id <pid> --counters System.Runtime

   [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
4. 在代表性阶段前后采集 gcdump，按 type count、retained size 与 root 路径检查真正存活的对象。

       dotnet-gcdump collect --process-id <pid> --output before.gcdump

   dotnet-gcdump 可以比较对象数、检查 roots，并分析堆对象统计；但 collect 会触发 full Gen2 GC，大堆或性能敏感环境不能把它混入正常延迟基准。[dotnet-gcdump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
5. 每次只改变一个表示或生命周期决策，同时测 build / request / query 时间、allocated bytes、retained heap、GC count 与语义等价性。

## 针对性手段

### 1. 减少短命 class 与临时数组

适用信号：每操作 allocation 很高，但 gcdump 中对应类型并不长期存活。

- 把一次性中间集合改为单次遍历、批处理或流式处理。
- 对高频创建和销毁的数组使用 ArrayPool<T>.Shared.Rent / Return。
- 同步的局部 buffer 参数优先使用 Span<T> 或 ReadOnlySpan<T>；跨 async 边界、需长期持有或跨组件传递时，使用 Memory<T> / IMemoryOwner<T> 并明确所有权。

ArrayPool<T> 的 API 文档说明，频繁创建销毁数组而产生显著 GC memory pressure 时，租借和归还 buffer 可能改善性能。[ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1) Memory<T> 与 Span<T> 指南则要求明确 buffer owner、consumer 和释放时机，尤其禁止在方法或 Task 生命周期结束后继续使用无权保留的 buffer。[Memory<T> and Span<T> usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)

风险：pool 中的数组可能比请求容量大，且未清理的引用元素会延长对象存活；租借 buffer 也不能被异步或其他调用方越过所有权边界继续使用。池化只适合被测量证实的高频临时 buffer，不适合把所有领域对象都塞入池。

### 2. 降低长期存活 class 的 retained heap

适用信号：gcdump 显示特定 class、string、dictionary entry 或 cache value 的 retained bytes 很高。

- 从 root 路径开始处理，而不是从对象头开始猜测：移除过期缓存、缩短集合持有时间、避免无界 memoization。
- 把重复的长字符串、元数据和跨索引重复 payload 规范化为共享表加整数 ID；只有确实反复出现且生命周期可控的数据才值得去重。
- 一份 canonical record 保留丰富 metadata；二级索引保存 NodeId、edge ordinal 或紧凑 key，而不是再次保存完整 record。

GC 通过 application roots 构建可达对象图；对象只要仍可达就不会回收。[GC fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals) 因此“heap 高”通常首先是所有权或缓存策略问题。把所有字符串放入永久 interning/cache 的做法会反向扩大 retained graph，必须用快照验证而不能作为默认优化。

### 3. 处理稠密数据和多个索引

适用信号：大量相同实体被排序数组、kind 索引、文件索引或邻接索引重复引用，热路径主要是顺序扫描。

优先比较两种布局：

- 保持 reference class 作为唯一 canonical 实体，二级索引只保存 int ordinal 或稳定 ID；
- 对真正小、不可变、只存一份且没有身份依赖的数据，试验一个紧凑的 struct 数组。

Microsoft 的 class/struct 指南说明，value-type array 内联存储元素，可能具备更好的 locality；但值传递会复制完整值，而 reference type 的赋值只复制引用。[Choosing between class and struct](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct) 因而一个大型或引用丰富的 struct 被放进多个 index 时，可能比共享的 class 消耗更多内存。

struct 试验的必要条件：

- 类型语义是真正的值，没有 reference equality、可变共享状态或多态需求；
- 值足够小且不可变；
- 没有热 boxing 路径；
- 不会被多个 value array、dictionary 或排序副本完整复制；
- 基准同时覆盖构建、查找、序列化和 GC，而不仅是一个 for 循环。

该指南中的“16 bytes”属于历史设计启发式，不是当前 JIT 的硬阈值；类型大小、复制次数和容器布局都必须实测。

### 4. 避免 boxing 重新制造堆对象

适用信号：把某个类型改成 struct 后，allocation 没有按预期下降，或 profiler 显示 object / interface 相关分配。

- 保留泛型集合和泛型 helper，避免通过 object 或非泛型 API 传递值。
- 审查 interface dispatch、格式化、日志参数和 equality comparer 是否把值装箱。

C# 官方文档明确说明，boxing 会在 managed heap 上创建 object 并复制 value；boxing / unboxing 相比简单赋值有额外计算和分配成本。[Boxing and unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing)

### 5. 只为昂贵、可重置的对象使用 ObjectPool

适用信号：构造或初始化很昂贵，实例可安全 reset，且 workload 已显示大量重复创建。

Microsoft 的 ObjectPool 指南建议仅在真实场景收集性能数据后采用；初始化成本不高时，从池中获取对象通常更慢，且 pool 限制的是保留数量而非创建数量。[ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool)

适合的候选通常是可清空的 StringBuilder、解析 scratch state 或显式 reset 的临时工作对象。不适合的候选包括长期领域实体、携带请求/用户状态的对象和难以彻底 reset 的对象。

### 6. 避免 LOH 触发的错误归因

大于等于 85,000 bytes 的对象会进入 LOH；频繁的临时大对象会与 Gen2 collection 相关，并可能带来清零成本和碎片。[Large object heap](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)

对于大 buffer，优先比较分块、流式、容量上界和 ArrayPool；不要因为看到 LOH 就把普通小 class 改为 struct。LOH compaction 是特殊诊断/恢复工具，不是稳定的吞吐优化手段。

### 7. 减少不必要的 string 工作

字符串通常不是 class 对象头问题，而是重复创建、拼接、切片和作为重复字段持有的问题。

- 对内部协议符号、路径、标记和键使用正确的 ordinal 比较。
- 在同步解析路径中，比较或切片时优先让 API 接受 ReadOnlySpan<char>，避免只为取子段而构造短期 string。
- 对会重复出现的 schema/name，使用由工作负载控制生命周期的 ID / intern table，而不是全局无界缓存。

Microsoft 的 string 指南建议内部文化无关比较使用 StringComparison.Ordinal 或 OrdinalIgnoreCase，并指出这通常有更好的性能。[String best practices](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)

## 不应采用的捷径

- 不要仅凭 Task Manager working set 判断 managed heap；GC 性能文档明确说 working set 不能代表虚拟内存使用或对象归因。[GC performance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance)
- 不要把 GC.Collect 当作通常的内存优化。GC fundamentals 将其限定为少数特殊和测试情形。
- 不要把 pool 作为每一个 class 的默认容器。
- 不要为“连续内存”将含大量引用字段、对象身份或多索引共享需求的实体直接改为 struct。
- 不要只报告进程累计 allocated bytes；它不能区分临时 allocation 与真正 retained heap。

## 在 NLCPG 中的直接含义

本仓库当前图模型的专项审查已经得出相同边界：NLCPGNode 和 NLCPGEdge 是引用丰富、被多个索引共享、部分路径依赖实例身份的实体，不宜直接全量改成 struct。更优先的试验是让邻接索引保存 edge ordinal 或 NodeId，同时保留一份 canonical node / edge record。详见 [NLCPG 高密度图中的 class 与 struct 内存评估](2026-09-17-nlcpg-class-struct-memory-assessment.md)。

## 验收标准

任何 class 表示优化至少同时提供：

1. 同一输入、SDK、配置和并发度下的 warm baseline 与重复样本；
2. 每操作 allocated bytes 和 Gen0/1/2 数据；
3. 改造前后的 retained heap、top types 与 root 路径；
4. build / query / request 的 median 与 tail latency；
5. semantic result、序列化、并发和错误路径的等价证据；
6. 只有在上述证据一致时，才把实验推广到整个模型。

## 已核验来源

- [Garbage collection fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals)
- [Garbage collection and performance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance)
- [Large object heap](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
- [ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
- [Memory<T> and Span<T> usage guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)
- [Object reuse with ObjectPool](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool)
- [Boxing and unboxing](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing)
- [Choosing between class and struct](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct)
- [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- [dotnet-gcdump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump)
- [BenchmarkDotNet diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
- [String best practices](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings)
