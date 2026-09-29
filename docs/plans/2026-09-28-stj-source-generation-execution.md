# System.Text.Json 源生成（System.Text.Json Source Generation）执行方案

> # ⛔ 状态：**目前不做（DEFERRED，2026-09-28 决定）**
>
> **本方案不执行，T1 探针也不启动。** 保留此文档仅为记录设计与下面这三条否证理由，
> 避免以后重复推导。若将来要重启，须先满足 §「重启的前置条件」。
>
> **为什么不做（简单版）：收益上限是个位数百分比，且当前连不上。**
>
> 1. **量级本来就小**：官方 "up to 40%" 是**启动时间 + 吞吐**的通用数字。
>    导出进程只启动一次 ⇒ 启动收益**无意义**，只剩序列化吞吐那一项。
> 2. **占比未知，且上界低**：本项目**没有**「元数据解析 vs 写出字节」的拆分读数。
>    最重文档分段为 `build=12851 / projection=8474 / write=3434 ms`，
>    即便序列化整段提速 40%，总导出降幅大概率**个位数百分比**。
> 3. **当前形态接不上**：fast-path 不支持异步，而本仓库被硬约束必须走
>    `SerializeAsync(stream,...)`；唯一接法是 `Serialize(Utf8JsonWriter,...)`,
>    正是源码记载崩过的那个重载（§1.4、§1.5）。
>
> **对比：优先做「复用构图」**（`docs/plans/2026-09-27-export-cpg-build-reuse-execution.md`）
> ——它针对 `build` 整整 **12851 ms** 的重复构图，量级更大、也更确定。

**目标：** 用 `System.Text.Json` 的**源生成（source generation）**替换运行时反射序列化，
消除每片 payload 的元数据解析与反射分派开销，**且不改变输出一个字节**。

**思路来源：** AOT/编译期代码生成——把「运行时按类型查元数据」换成「编译期生成直写
`Utf8JsonWriter` 的代码」。与本项目已有的 `NLISSN.Rule.Generator`（Roslyn 增量生成器）
是同一类手段。

**Tech Stack：** C#/.NET 10、`System.Text.Json` 源生成器
（`JsonSerializerContext` + `JsonSourceGenerationOptions`）、既有 Contract 测试。

---

日期：2026-09-28。

> ## 🛑 结论先行：本方案**当前形态不可行**，除非先推翻一条阻塞项
>
> 本轮为写文档而做的**官方文档核对**（§1.5）推翻了本方案原本的落点：
> **源生成的 fast-path（`JsonSourceGenerationMode.Serialization`）不支持异步序列化，
> 且"接受 `Stream` 的同步 `Serialize` 重载也算异步"。**
> 而本仓库的 payload 写入**被硬性约束**为必须走 stream
> （`JsonSerializer.SerializeAsync(stream, ...)`，见 §1.4）。
>
> ⇒ **fast-path 无法接进当前的写入调用点。** 唯一接法是改成
> `JsonSerializer.Serialize(Utf8JsonWriter, ...)`——而**恰好就是这个重载**，
> 源码注释记载它在 2.29 GB 的 NPC.cs payload 上崩过（§1.4）。
>
> ⇒ 因此本方案的真实定位从「正交提速」降级为
> **「先花一天做 Go/No-Go 探针，再决定是否值得做」**（§4 T1）。
> 若 T1 不通过，本方案的可行子集只剩 manifest（几十 KB），**收益可忽略**。

> 📌 **本轮性质：只写文档，不改产品代码、不跑测试。**
> 标注口径：「官方」= 微软文档/运行时 issue（本轮已核对）；「源码」= 仓库内既有注释；
> 「实测」= `Build/shard-perf/MEASUREMENT.md`；「推算」= 由上述推出的结论。

> 📌 **行号基准**：`src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs` 1,608 行。
> 引用同时给出**符号名**，行号漂移时以符号为准。

## 0. 结论摘要

| 问题 | 结论 |
| --- | --- |
| 当前怎么序列化？ | `JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct)`（`:1476`），`JsonOptions` 是**运行时反射**配置（`:36`） |
| fast-path 能直接用吗？ | **不能**。官方：`Serialization` 模式**不支持异步**，且接受 `Stream` 的同步 `Serialize` **也算异步**（§1.5） |
| 那唯一的接法是什么？ | 自建 `Utf8JsonWriter` 包住 `FileStream`，调 `Serialize(writer, value, typeInfo)` |
| 这条接法安全吗？ | **源码记载它崩过**：`Cannot allocate a buffer of size 2147487655`（§1.4）。⚠ 但**该证据已不可复现**（§1.6） |
| `WriteIndented` 会漂吗？ | **不会**——官方：传入自己的 writer 时，以 **writer 的 `JsonWriterOptions.Indented`** 为准，不看 attribute（§1.5） |
| DTO 能覆盖吗？ | 3 个 manifest DTO 是 `private`，源生成只支持 `public`/`internal`，须放宽（§3.3） |
| 预计收益？ | **无法先验量化**。官方 "up to 40%" 是启动+吞吐的通用量级，本场景只启动一次；序列化占比本项目**无拆分读数**（§7） |
| 该做吗？ | **不做（2026-09-28 决定）**。三名否决理由见文档顶部；重启须先满足「重启的前置条件」（§10） |

## 1. 事实基线

### 1.1 唯一序列化入口

```
WriteJsonAtomicallyAsync(path, value, ct)   // :1483  写 path+".tmp" → File.Move
  → WriteJsonAsync(path, value, ct)         // :1449
      → FileStream(FileMode.Create, FileShare.None, bufferSize: 65536, useAsync: true)
      → stream.WriteAsync(Encoding.UTF8.GetPreamble())    // UTF-8 BOM，刻意
      → JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct)
      → stream.FlushAsync() → return stream.Length
```

泛型实参实际只用两种：各片 payload = `FileExportProjection`（`:1523`），
manifest = `ManifestHeader`（`:1530`）。

### 1.2 payload DTO 全景

| 类型 | 声明 | 可见性 | 用于 |
| --- | --- | --- | --- |
| `FileExportProjection` | `internal sealed record`（`:1523`） | ✅ | payload 根 |
| `FileNodeEntry` | `internal sealed record`（`:1570`） | ✅ | payload 节点（每片可达 27 万条） |
| `FileEdgeEntry` | `internal sealed record`（`:1585`） | ✅ | payload 边 |
| `CallSiteEntry` | `internal sealed record`（`:1594`） | ✅ | 边内嵌 |
| `FileExportEntry` | `internal sealed record`（`:1562`） | ✅ | manifest 文件表 |
| `ManifestHeader` | `private sealed record`（`:1530`） | ❌ | manifest 根 |
| `ProjectEntry` | `private sealed record`（`:1545`） | ❌ | manifest 项目 |
| `ExportDiagnostic` | `private sealed record`（`:1600`） | ❌ | manifest 诊断 |

⚠ `FileExportProjection.Nodes` 是 `IReadOnlyList<FileNodeEntry>`——**接口类型**。
源生成器对接口集合的处理需在 T2 实测确认（通用支持表里 `IEnumerable<T>` 序列化 ✔️，
但**源生成**是否覆盖接口需验证，不可假设）。

### 1.3 项目配置现状

`NLCPG.ProjectJson.csproj`：`net10.0`、`LangVersion: preview`、无
`IsAotCompatible`/`PublishTrimmed`/`JsonSerializerIsReflectionEnabledByDefault`。
⇒ 反射序列化当前是**默认且唯一**路径；没有 AOT 压力在推这件事。

### 1.4 源码记载的失败（本方案的阻塞来源）

`ProjectJsonExporter.cs:1454-1466`（**源码注释**，非本轮实测）记载：

| 尝试 | 结果 |
| --- | --- |
| `File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, opts))` | ❌ 先物化成**一个连续 string** |
| `JsonSerializer.Serialize(writer, value, opts)` | ❌ 抛 `Cannot allocate a buffer of size 2147487655`（≈ `Array.MaxLength`） |
| `JsonSerializer.SerializeAsync(stream, value, opts)` | ✅ 写出 4.19 GiB，峰值内存 **+0.01 GB** |

并据此写下硬约束：**"不得改成上面任何一种"**。

### 1.5 官方文档核对（本轮新增，推翻原设计）

| 事实 | 出处 |
| --- | --- |
| `JsonSourceGenerationMode.Serialization`（fast-path）**不支持异步序列化**；且**接受 `Stream` 的同步 `Serialize` 重载也算异步** | [dotnet/docs#32897](https://github.com/dotnet/docs/issues/32897)（原始出处 dotnet/runtime#75139） |
| 传入自己的 `Utf8JsonWriter` 时，**以 writer 的 `JsonWriterOptions.Indented` 为准**，不读 `JsonSourceGenerationOptionsAttribute.WriteIndented` | [How to use source generation](https://raw.githubusercontent.com/dotnet/docs/289d55283e35d35c215ab44be9cdc1e6251e26c9/docs/standard/serialization/system-text-json/source-generation.md) |
| fast-path "doesn't support all of the serialization features"；不支持时**回退到默认代码** | [Source-generation modes](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation-modes)（前轮已核，本轮该页 fetch 失败，标记为未复验） |
| 源生成只支持 `public`/`internal` 成员 | 同上 |
| 可用 MSBuild 属性 `JsonSerializerIsReflectionEnabledByDefault=false` 让**回退变抛异常** | 同 §1.5 第二行 |

⇒ **两条推论：**
1. 想拿 fast-path，**必须**走 `Utf8JsonWriter`，别无他法（stream 与 async 都被排除）。
2. `WriteIndented` **不是**字节漂移风险（用自建 writer 时由 writer 决定），
   这消除了我上一稿 §3.2 的猜测；但也意味着 `JsonSourceGenerationOptionsAttribute`
   上的 `WriteIndented` 对本场景**无效**，必须显式配到 writer 上。

### 1.6 ⚠ 阻塞项的证据已不可复现（本轮静态审查发现）

§1.4 里那条 `2147487655` 的失败，源码引用 `Build/worker8-utilization/Utf8Probe` 为证。
**但本轮核对发现该探针已不是当时那一份**：

- 现有 `Utf8Probe/Program.cs` 自述为 **"探针 v5"**，内容是 **2 个节点**的格式比对
  （`old` / `new-a` / `new-a2` / `new-b`），**没有任何 2 千万实体、没有 4.19 GiB**
  （`Grep` 全 `Build/` 树无 `2147487655`、无 `20000000` 命中）。
- 该探针的注释把 `new-b`（`FileStream` + `Utf8JsonWriter`）标注为
  **"← 会缓冲，仅作对照"**——即当时已观察到 writer 变体会缓冲。
- `Build/` 在 `.gitignore:5` 中被忽略，**故该探针无 git 历史**，旧版本**已丢失**。

⇒ **结论：那条"`Serialize(writer,...)` 会崩"的证据，今天既无法复现也无法回溯。**
它可能是 `MemoryStream` 支撑的 writer（那"写出 4.19 GiB"本身就会撞 `Array.MaxLength`，
与源生成无关），也可能是 `Indented=true` 触发的真实缓冲。
**两种解释的结论完全相反**，故 T1 必须重做这个实验（§4 T1）。

## 2. 设计（仅在 T1 通过后）

### 2.1 目标形态

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Serialization)]   // fast-path
[JsonSerializable(typeof(FileExportProjection))]
[JsonSerializable(typeof(ManifestHeader))]
internal sealed partial class ProjectJsonSerializerContext : JsonSerializerContext;
```

写入路径（**必须**自建 writer，见 §1.5）：

```csharp
await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
    FileShare.None, bufferSize: 1 << 16, useAsync: true);
await stream.WriteAsync(Encoding.UTF8.GetPreamble(), ct);
// ⚠ Indented 必须显式设在这里：attribute 上的 WriteIndented 对自建 writer 无效（§1.5）
using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
{
    JsonSerializer.Serialize(writer, value, context.FileExportProjection);  // fast-path
}
await stream.FlushAsync(ct);
return stream.Length;
```

⚠ 两处与现状的**语义差异**必须逐一确认：
1. **同步写**：`Serialize(writer, ...)` 是同步 API，而现状是 `await SerializeAsync`。
   本方法会从"真异步"变为"CPU 同步 + 底层异步 flush"。对导出器是**可接受**的
   （文档循环本身串行、每片由 worker 承载），但**会改变大文档的线程占用特征**。
2. **`Indented` 的来源变了**：从 `JsonOptions.WriteIndented` 变为 writer 的
   `JsonWriterOptions.Indented`。二者应产出相同字节，但**必须实测**（§5.2）。

### 2.2 字节中性靠什么保证

| 因素 | 现状 | 目标 | 风险 |
| --- | --- | --- | --- |
| 缩进 | `JsonOptions.WriteIndented=true`（`:38`） | writer 的 `Indented=true` | 官方说以 writer 为准（§1.5）⇒ 需实测等于现状 |
| 命名 | `PropertyNamingPolicy=CamelCase`（`:39`） | `JsonKnownNamingPolicy.CamelCase` | 需实测键名一致 |
| 属性顺序 | 反射按声明序 | 生成器按声明序 | 需实测 |
| null 处理 | 无 `DefaultIgnoreCondition` ⇒ null 会写出 | 需确认生成器不改默认 | 需实测 |
| BOM | 手工 `GetPreamble()` | **不变**（仍在 writer 之前写） | 无 |
| 换行/缩进字符 | writer 默认 | 同一 writer 默认 | 低 |

**判据只能是逐字节比对**（§5.2），不得靠推断。

## 3. 阻塞项与取舍

> ⛔ **本节的「处置」列写的是"若将来重启该怎么做"，不是待办。当前一律不执行（见文档顶部）。**

### 3.1 阻塞一：`Array.MaxLength`（Go/No-Go）

| 项 | 内容 |
| --- | --- |
| 事实 | 源码记载 `Serialize(writer, ...)` 在 2.29 GB payload 上抛 `Cannot allocate a buffer of size 2147487655` |
| 为何致命 | fast-path **只能**走 writer（§1.5），**恰好撞上这条记载** |
| 未知 | 当时 writer 是否 `MemoryStream` 支撑；`Indented=true` 是否导致缓冲；FileStream 支撑是否可行（§1.6） |
| 处置 | **T1 先探测**。未通过则**不提交**任何 `WriteJsonAsync` 改动 |
| 退路 | **只对 manifest 启用源生成**（几十 KB，无 Array.MaxLength 风险）。⚠ 但 manifest 每文档只写一次，**收益可忽略** ⇒ 退路≈放弃 |

### 3.2 阻塞二：静默回退使收益为 0 且无报错

fast-path 不支持某特性时**回退到默认代码**，且**不报错**。

**处置**：把回退变成**可失败的断言**——在**测试**项目设
`JsonSerializerIsReflectionEnabledByDefault=false`（§1.5）。
⚠ 该属性是**链接期常量**（官方原文："treated as a link-time constant"），
故应设在**测试**的 `csproj`/运行时配置，而不是生产程序集；
生产程序集设它会影响**其他**反射序列化调用（若有）。**待 T2 确认影响面。**

### 3.3 `private` DTO 的处置

三个 manifest DTO 是 `private`（§1.2）：

| 选项 | 代价 | 收益 |
| --- | --- | --- |
| **A. 放宽为 `internal`** | 与 `FileExportEntry` 一致；已有先例与注释 | 全部类型可入 context |
| **B. manifest 继续反射** | 两套配置 | payload 走 fast-path |

**推荐 A。** 注意 `ManifestHeader`/`ProjectEntry`/`ExportDiagnostic` 是
`ProjectJsonExporter` 的**嵌套** private 类型；放宽为 `internal` 后生成代码在**同程序集**内
可访问 ⇒ 可行，但**需 T2 验证生成器接受嵌套 internal 类型**。

### 3.4 `WriteIndented = false`（不在本方案内，但可能收益更大）

`WriteIndented=true` 对 2.29 GB payload 是**实打实的额外字节与 CPU**
（缩进多为每行 2~8 空格）。改成 `false` 可能**比源生成收益大**，
但**改变输出格式**、破坏既有字节中性契约 ⇒ **独立评估，不混入本方案**。

## 4. 任务拆分

> ⛔ **下表是"若将来重启"的拆分，全部未执行、当前不执行。**
> 没有任何一项被启动，也没有任何产品代码被修改。

| # | 任务 | 交付物 | 完成判据 |
| --- | --- | --- | --- |
| **T1** | **Go/No-Go 探针** | 新探针（`Build/` 下，非版本控制） | ①`FileStream` + `Utf8JsonWriter(Indented=true)` + fast-path 能写出 **≥2.3 GB 不抛异常**；②证明 fast-path 真被使用（非回退）；③**同时**判定 §1.6 的歧义：writer 是否随 payload 增长而累积内存 |
| **T2** | 加 `JsonSerializerContext` | 新文件 + DTO 放宽 | `EmitCompilerGeneratedFiles=true` 下可见生成源；接口集合 `IReadOnlyList<T>` 与嵌套 internal 类型均被生成；构建 0 warning |
| **T3** | 字节中性验证 | 比对脚本输出 | 同 fixture 下 **源生成 vs 反射** 的 payload 逐文件 SHA-256 **全部相同**（含 BOM） |
| **T4** | 接入写入路径 | `WriteJsonAsync` | **仅 T1 通过后**；保留 BOM、`FileStream`+`FileShare.None`+`bufferSize` |
| **T5** | 契约测试 | 新增断言 | 断言生成源存在（防静默回退）；断言默认配置仍逐字节中性 |
| **T6** | 小批量实测 | 数据 | `Build/shard-perf/` 上 projection/write 前后对比，各 3 次取最小值 |
| **T7** | 文档 | `cli-reference.md`、`MEASUREMENT.md` | 记录实测收益；**若为 0 必须写明** |

⚠ **T1 是闸门**：不通过 ⇒ T4–T6 不做。此时**建议整条放弃**而非退回 manifest 子集（§3.1）。

## 5. 验证

> ⛔ **本节全部未执行。** 以下是"若将来重启"该遵守的口径。

### 5.1 探针口径（吸取 `Build/shard-perf/` 的迭代教训）

1. **一个进程只测一个配置**——同进程跑多档会互相污染（早期产出负值）。
2. **必须预热透**——只预热 1 遍会让 dop=1 落在 tier0，出现「dop=8 的 CPU 时间低于 dop=1」
   这种不可能结果（实测踩过）。
3. **计数流而非物化 `byte[]`**——早期探针为测字节物化了 173 MB 数组。
4. **基线要在输入驻留之后取**——否则把输入驻留误记成被测对象开销。
5. **多轮取最小值**。

⚠ 本探针的**额外要求**：必须真的写出 ≥2.3 GB 到**磁盘**，并**采样进程工作集曲线**
（而非只看最终峰值）——因为要判定的正是"writer 是否随增长而累积"（§1.6）。

### 5.2 字节中性的判据（沿用既有脚本）

用 `Build/worker8-utilization/Export-PayloadSha256.ps1` 生成清单，比对**原始字节**哈希
（payload 带 UTF-8 BOM，**不得**先解码为文本）。

⚠ **已知坑 1**：相对路径切分必须用 `.Substring($root.Length).TrimStart('\')`。
上一轮我用 `$root.Length + 1` 写出错误键，脚本报出 50 个 MISSING 的假故障。
⚠ **已知坑 2**：`baseline-payload-sha256.txt` 只有 **966 条**、生成于导出运行**之前**，
与当前 `out/` **0/966 命中** ⇒ **不能**作基线（该脚本头部已自述）。
本方案的 A/B 必须是**同一次改动前后、同机、同语料**的两份清单。

### 5.3 实测判据

| 指标 | 目标 | 判定 |
| --- | --- | --- |
| 输出 | 逐字节相同 | **硬性**，不通过则回退 |
| projection/write | 下降 | 记录实测；无下降**不算失败**，但须如实记录 |
| 峰值内存 | 不显著上升 | 记录（writer 若累积则**显著上升** ⇒ T1 直接否决） |
| 启动 | 不算收益 | 导出只启动一次，忽略 |

## 6. 风险

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| **fast-path 撞 `Array.MaxLength`** | **高** | T1 闸门；不通过则整条放弃 |
| **静默回退 ⇒ 收益 0 且无报错** | **高** | T1 需证明生成代码被执行；T5 断言生成源存在 |
| 从异步写变同步写，改变线程占用 | 中 | T6 观察；必要时保留两条路径 |
| 字节漂移（缩进/键名/顺序） | 中 | T3 逐字节比对 |
| 接口集合 / 嵌套 internal 类型不被生成 | 中 | T2 实测；否则改 DTO 形态 |
| 收益本就很小 | 中 | §7 已标注；T6 如实测 |
| 与并发会话的改动冲突 | 中 | 该文件已被并发编辑多次；每次编辑前重读 + 比对 mtime/hash |
| `JsonSerializerIsReflectionEnabledByDefault` 影响面未知 | 低 | 只设在测试 |

## 7. 收益的量级判断（诚实版）

最重文档 `Big1.cs` 的分段读数（`MEASUREMENT.md`，8 片串行）：

```
build=12851 ms   projection=8474 ms   write=3434 ms
```

- 源生成作用在 `projection` 与 `write` 中的**序列化部分**。
- 但"元数据解析" vs "写出字节"各占多少，**本项目无任何拆分读数**
  ⇒ 官方 "up to 40%" 是**启动时间+吞吐**的通用量级，**不能直接套用**；
  且导出只启动一次，**启动收益无意义**。
- 上界推算：即便序列化整段提速 40%，而序列化只占 `projection+write` 的一部分，
  总导出降幅大概率是**个位数百分比**。
- 对比：`docs/plans/2026-09-27-export-cpg-build-reuse-execution.md` 针对的**重复构图**
  是 `build=12851 ms` **整段**——**量级更大、更确定**。

⇒ **排序建议：低于「复用构图」。** 本方案的优势仅是**改动小、一天内可结论**（T1）。

## 8. 与其它方案的关系

| 方案 | 关系 |
| --- | --- |
| `docs/plans/2026-09-28-shard-parallel-projection-budget-gate-execution.md` | **正交**，可叠加；但两者都受「写盘/序列化占比」限制 |
| `docs/plans/2026-09-27-export-cpg-build-reuse-execution.md`（复用构图） | **量级更大**，优先 |
| `WriteIndented = false`（未开计划） | 可能比本方案收益更大，但破坏字节中性；独立评估 |

## 9. 未做的事（明确边界）

- **未做 T1 探针** ⇒ 不知道 fast-path 在 2.3 GB 上是否可行。这是头号未知，也是本方案的闸门。
- **未复现 §1.4 的历史失败**，且发现其证据（旧版 `Utf8Probe`）**已丢失**（§1.6）。
  源码注释的这条约束**至今仍被遵守**，但其依据今天无法验证——**这本身是个需要登记的风险**
  （见 §11，与是否重启本方案无关）。
- 未测量「元数据解析」在 `projection`/`write` 中的占比 ⇒ 收益无法先验量化。
- 未在真实 967 文件语料（NPC.cs 2.29 GB）上验证。
- 未验证 `WriteIndented=true` 下 fast-path 是否被实际使用（T1 的一部分）。
- 未评估 `private`→`internal` 放宽对**其他**消费者的影响。
- `WriteIndented=false` 未评估。

## 10. 重启的前置条件（Decision Gate）

**本方案在下列条件全部满足前不重启。** 任何一条不满足，直接维持「不做」：

| # | 前置条件 | 为什么 |
| --- | --- | --- |
| **G1** | 「复用构图」（`2026-09-27-export-cpg-build-reuse-execution.md`）已交付或已明确放弃 | 它针对 `build` **整段 12851 ms**，量级远大于本方案。先把它做完再看这里 |
| **G2** | 有实测读数证明**序列化在 `projection+write` 中占比可观** | 否则即便提速 40%，总收益仍是零头。当前**无此读数**（§7） |
| **G3** | 先解决 §11 的 `Array.MaxLength` 证据缺失问题 | 该约束若成立，fast-path 接不上；若不成立，结论反转。**必须先有可复核的证据** |
| **G4** | 需求方明确接受「总导出降幅可能为个位数百分比」 | 避免投入后才发现收益不可见 |

⚠ **G3 是真正的技术闸门**，G1/G2 是排序闸门。若将来只想做一件事，
**应该做 §11 的证据重建，而不是本方案**——因为那个发现无论本方案做不做都有价值。

## 11. 独立于本方案的一个隐患（建议单独处理）

§1.6 的发现**不是本方案的问题，而是现存代码的问题**：

`src/NLISSN.Infrastructure/ProjectJson/ProjectJsonExporter.cs:1454-1466` 有一条
**硬性约束**（源码原文："**不得**改成上面任何一种"），它约束了导出器最关键的写入路径。
但它的证据今天**无法复核**：

- 被引用的 `Build/worker8-utilization/Utf8Probe` 现为 **v5**，只做 2 个节点的格式比对；
- 全 `Build/` 树无 `2147487655`、无 2 千万实体；
- `Build/` 在 `.gitignore:5` 被忽略 ⇒ **无 git 历史，旧版已丢失**。

**建议**：花约半小时重做那个探针（大 payload + `FileStream` 支撑的 `Utf8JsonWriter`，
采样工作集曲线）。两种结果都有价值：

- **确认** ⇒ 把该约束与探针**移入版本控制**，让它从此可复核；
- **推翻** ⇒ 同时解锁源生成与 `Indented` 相关的调整空间。

本方案**不做**，此事仍值得做。建议登记为独立条目。
