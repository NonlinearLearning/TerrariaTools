# GC 内存总量配置（`System.GC.ConserveMemory=7`）

**状态：** 现行配置（2026-09-26 启用）。
**回答的问题：** 为什么两个 Exe 入口都固化了这条 GC 配置？它降的是什么、不降什么、怎么关掉？
**适用对象：** 在本仓跑全语料分析、遇到进程内存偏高的人。

---

## 0. 结论先行

1. **两个可执行入口均已固化** `System.GC.ConserveMemory = 7`（见 §1）。
2. **它降低的是「内存总量」**，不是把字节从一个区域搬到另一个区域。
   实测（单文件 `NPC.cs`，每侧 3 次）：工作集峰值 **4,701 → 3,799 MB（−19.2%）**，
   committed 峰值 **4,890 → 3,961 MB（−19.0%）**，LOH 碎片 **1,014 → 946 MB（−6.7%）**。
   四项指标的两侧区间**互不重叠**。
3. **分析结果零影响**：11 次运行 `nodes=1,599,506 / edges=7,756,984` 逐位一致。
4. **它不能放 `nlissn.yml`** —— 时序上不可能生效（§3）。
5. **⚠️ 它与既有文档的「已实测无效」结论冲突，两者都对，问的不是同一件事**（§5）。
   写这份文档的主要目的之一就是把该冲突讲清楚，而不是掩盖。

---

## 1. 配置在哪、长什么样

两个入口项目各有一个独立 `<ItemGroup>`：

| 文件 | 作用 |
| --- | --- |
| `src/NLISSN/NLISSN.csproj` | 主分析入口；项目级 JSON 导出作为后置步骤在同进程内运行，因此也受此配置覆盖 |
| `src/NLCPG/NLCPG.csproj` | CPG 入口 |

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="7" />
</ItemGroup>
```

`RuntimeHostConfigurationOption` 是 MSBuild 项，构建时合并进各项目自己的
`*.runtimeconfig.json`。产物中可见：

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.ConserveMemory": 7,
      "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
    }
  }
}
```

> ⚠️ **配置名与运行时变量名不同**：JSON 键是 `System.GC.ConserveMemory`，
> 但 `GC.GetConfigurationVariables()` 报出的名字是 **`GCConserveMem`**。
> 核对生效值时不要按前者去查（本仓曾因此需要额外确认）。
> 环境变量名则是 `DOTNET_GCConserveMemory`。

**为什么用 `RuntimeHostConfigurationOption` 而不是 `runtimeconfig.template.json`**：
本仓原本**零 GC 配置**（无 template 文件、csproj 无 `ServerGarbageCollection`），
入口项目风格一致；用 MSBuild 项的改动最小，也更贴合既有写法。

---

## 2. 它降低的是什么（口径）

三个量必须分清，否则容易得出"改了等于没改"的错误结论：

| 量 | 含义 | `ConserveMemory` 是否降低它 |
| --- | --- | --- |
| **LIVE** | 某时刻可达对象 | ❌ 不改变（它不动你的数据结构） |
| **COMMITTED** | live + 未回收垃圾 + GC 保留的空闲 | ✅ **下降** |
| **WORKING SET / Private** | committed 中驻留的部分 + native | ✅ **下降** |

关键在于：**本负载的 committed 里约 86% 不是数据**。
对同一语料 dop12 运行的完整分解：

```
live / committed   ≈ 13.4%     ← committed 里只有约 13% 是真数据
累计分配 / live    ≈ 228x      （260 GB 累计分配 vs 典型 ~1.1 GB 存活）
```

⇒ 想降总量，有效方向是**让 GC 更早、更紧地回收**，而不是把对象结构改小
（后者只作用于那 13%）。这正是本配置的靶心。

**官方定义**（[garbage-collector.md](https://raw.githubusercontent.com/dotnet/docs/main/docs/core/runtime-config/garbage-collector.md)）：
取值范围 0–9，0 表示不变；值越大 GC 越努力保持堆小；
**非零值会在 LOH 碎片过多时自动压缩 LOH**；官方建议**从 5–7 起步**。

---

## 3. 为什么不能放 `nlissn.yml`

GC 配置**只在进程启动时、GC 初始化阶段由宿主读取一次**。
而 `nlissn.yml` 是应用启动**之后**才由 `YamlConfigurationLoader` 解析的
—— 等它读到，GC 早已初始化完毕。**放进去会是个"看起来配了、实际没生效"的陷阱。**

官方原文："These configurations are only read by the runtime when the GC is initialized
(usually this means during the process startup time)."
（[garbage-collector.md](https://raw.githubusercontent.com/dotnet/docs/main/docs/core/runtime-config/garbage-collector.md)）

同理，**运行中改环境变量无效**，必须重启进程。
`nlissn.yml` 的根键只有 `input` / `analysis` / `execution` / `artifacts` / `logging`
（加 `schemaVersion` / `tool` / `runId`），`execution` 下是 6 个 DOP 字段与若干布尔，
**没有任何 GC 或运行时项** —— 架构上也没有承载它的位置。

---

## 4. 实测证据

### 4.1 单文件 A/B（`Terraria\NPC.cs`，2,147,283 B，DOP 全 1）

每次运行都产出一致的 `nodes=1,599,506 / edges=7,756,984`，故差异只能归因于 GC 配置。

| 指标 | 基线（无配置，n=3） | **ConserveMemory=7（n=3）** | 差异 |
| --- | ---: | ---: | ---: |
| **工作集峰值** | 4,678 / 4,734 / 4,692（均 4,701） | **4,085 / 3,930 / 3,382（均 3,799）** | **−19.2%** |
| **Private 峰值** | 5,016 / 5,046 / 5,043（均 5,035） | **4,299 / 4,212 / 4,232（均 4,248）** | **−15.6%** |
| **committed 峰值** | 4,874 / 4,907（均 4,890） | **4,014 / 3,908（均 3,961）** | **−19.0%** |
| **LOH 碎片峰值** | 993 / 1,002 / 1,047（均 1,014） | **963 / 940 / 936（均 946）** | **−6.7%** |
| 累计分配 | 9.99 GB | 10.00 GB | 持平（符合预期） |
| gen2 收集次数 | 16 / 16 / 16 | **45 / 43** | 约 2.7×（代价） |

**显著性**：上述四项的两侧区间**互不重叠**。
最差的一次 ConserveMemory（WS 4,085 MB）仍**低于**最好的一次基线（4,678 MB），差 **593 MB**。
⇒ −19% 是稳健结论，不是噪声。

**机制已被独立确认**：`committed` 的**峰值与均值同时下降**
（均值 2,140/2,151 → 1,782/1,703），说明不是区域间搬运，而是真实总量下降。
配套代价是 gen2 回收 16 → 43–46 次 —— 这正是"更激进回收"的必然结果。
GC 停顿总量反而略降（5.03/5.77 s → 4.47/4.52 s）。

### 4.2 固化后的端到端验证

**不设任何环境变量**，仅靠 csproj 固化跑 `NPC.cs`：

| 检查 | 结果 |
| --- | --- |
| 退出码 / 耗时 | `EXIT=0` / 96.1 s |
| WS 峰值 | **4,142 MB**（落在 ConserveMemory 一侧，远低于基线 4,678–4,734） |
| committed 峰值 | **4,024 MB**（与 env 版 3,908/4,014 一致） |
| **gen2 收集次数** | **46**（基线恒为 16 —— 这是配置生效的**指纹**） |
| 工作等价性 | `nodes=1599506 edges=7756984`，与全部 10 次历史运行逐位一致 |

**`gen2 = 46` 是最硬的证据**：该值不可能因巧合出现，证明 runtimeconfig 确实被宿主读取。
另外用**同样的 MSBuild 机制**建最小探针独立验证：环境变量全空时
`GC.GetConfigurationVariables()` 报 **`GCConserveMem = 7`**。

---

## 5. ⚠️ 与既有「已实测无效」结论的冲突（必读）

本仓两处文档记载 `GCConserveMemory` **实测无效**、并把推荐它的判断**推翻**：

- `docs/loh-large-object-optimization-guide.md` §0 第 4 条、§4 反模式清单
- `Context/progress.md` 轮次 14「环境变量 A/B 实测：两项 GC 设置都无效」

**两者都对 —— 它们问的不是同一个问题。** 不澄清就会留下一个静默矛盾。

### 5.1 既有实测测的是什么

那次用的探针是 `D:\TRbackup\NL-diag-tools\GcReturnProbe`。
读其源码（`Program.cs:90-107`）可见关键结构：part 1 在释放引用后，
**显式调用 12 次 `GC.Collect(2, Forced, blocking: true, compacting: true)`**，
然后才读 `committed`。

```csharp
keep.Clear();
Snapshot("after release");
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);   // 显式强制
...
for (var i = 0; i < 10; i++) {
    Thread.Sleep(500);
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
}
```

**这构成天花板效应**：手动反复强制完全压缩回收，**任何** GC 配置都会归还段容量。
而 `ConserveMemory` 的机制恰恰是"**让 GC 自己更早、更积极地回收**" ——
既然你已经手动做了它要做的事，它自然"看不出差别"。
⇒ 该 A/B 的结论应**限定**为："在显式强制压缩回收的场景下，本配置无额外效果"。

四组实测也确实全部收敛到同一终值（`final committed` 均 **700 MB**、
`final private` **716–723 MB**），包括 BASE 组 —— 即**基线本身就归还了段**。

**次要差异**：该探针 part 1 的总分配量仅 6×700 MB = **4.2 GB**，
而真实负载单文件累计分配 **~10 GB**、全语料 **260 GB**；规模上也不是同一场景。
（其源码注释 `:114-119` 自己就提示：part 1 能归还，"很可能是因为它是一次性脉冲 +
显式 compacting GC"，并为此另设了 part 2/3 去检验持续性分配与长期大根。）

### 5.2 本次实测测的是什么

本次是**真实产品负载**（`NLISSN.exe` + `NPC.cs`）、**生产路径**、
**不做任何手动 `GC.Collect`**，看 GC 在自然回收下的稳态总量。
此时 `ConserveMemory` 有稳定、可重复的 −19% 效果（§4）。

### 5.3 结论如何共存

| 问题 | 答案 |
| --- | --- |
| 显式强制压缩回收后，`ConserveMemory` 还能额外降 committed 吗？ | **不能**（既有 A/B，正确） |
| 真实负载自然回收下，`ConserveMemory=7` 能降总量吗？ | **能，−19%**（本次 A/B） |
| 本仓该不该启用它？ | **该** —— 生产路径不会手动 `GC.Collect`，属第二种情形 |

**既有文档的措辞（"已实测无效"未加限定）过宽**，本次据实收窄。
`docs/loh-large-object-optimization-guide.md` 已就地加注作用域说明，未删原证据。

---

## 6. 怎么关掉 / 怎么调

**临时关闭（不改代码、不重新构建）** —— 环境变量优先于 runtimeconfig：

```powershell
$env:DOTNET_GCConserveMemory = '0'   # 0 = 默认行为 = 不变
```

**调档**：取值 **0–9**。官方建议 5–7 起步；值越大越省内存，代价是 GC 更频繁、
暂停可能更长。若要更激进可试 **9**，但需重新测量，不要假定线性外推。

**核对实际生效值**（注意变量名是 `GCConserveMem`）：

```powershell
# 在任何 .NET 10 进程内
GC.GetConfigurationVariables()   # 查 GCConserveMem
```

**注意与 DATAS 的耦合**：官方记载 DATAS 的 gen0 预算公式把 `conserve_memory` 作为直接输入
（未指定时取默认 5）。故调整本值**也会影响 DATAS 行为**，不是纯粹独立的旋钮。

---

## 7. 边界与未验证项（勿越读）

1. **⚠️ 只有单文件结论。** §4 全部来自 `NPC.cs`。
   全语料（967 文件）下的幅度**未验证** —— 单文件无跨文件驻留叠加，
   幅度可能不同（方向应一致，因为机制是"更早回收"，与规模无关）。
2. **⚠️ 两侧是分批运行、非交错。** 基线 3 次与 ConserveMemory 3 次是**先后两批**，
   不是 A/F 交替。**分批可能被系统内存压力漂移混淆**（GC 会经
   `GCHighMemPercent=90` 响应物理负载），且当时机器上有其他高占用进程。
   一次交错（A/F 交替）复测**已设计但未执行**（按指示停止测试）。
   ⇒ 结论方向可信（区间不重叠且机制有独立支持），但**严格性弱于交错 A/B**。
3. **耗时不可判定。** 基线 88.1/91.1/90.0 s vs ConserveMemory 80.5/82.4/**125.6** s，
   区间重叠。**不得声称更快或更慢** —— 本仓已证本机墙钟噪声底约 ±3.5%，
   `BASELINE.md §5.4` 记载同二进制两次运行可差 16%–71%。
4. **未测 ConserveMemory=9**，也未测它与 `GCHeapHardLimitPercent` 的组合。
5. **未做 gcdump 复核** `live` 是否不变（按定义不应变，且 nodes/edges 相同，但未直接验）。
6. **未经验证是否应扩到测试宿主**：本次只固化**可执行入口**；
   `Directory.Build.props` 未动，测试进程 GC 行为不变。
   （原为两个入口 `NLISSN` + `NLCPG`；项目级 JSON 导出曾由独立的
   `NLCPG.ProjectExport` 承担，该工程已删除，导出改为 `NLISSN` 内联后置步骤，
   因此该配置对导出路径的覆盖**未被削弱**。）
7. **`HeapHardLimitPercent` 未启用。** 它把"换页"转成"GC"，
   但本仓已验证 `GCHeapHardLimit` 曾致进程死亡（`0x80131506`，
   见 `docs/loh-large-object-optimization-guide.md` §4）。**不要在没有测量前启用。**

---

## 8. 参考

- [GC 运行时配置（Microsoft Learn / dotnet/docs）](https://raw.githubusercontent.com/dotnet/docs/main/docs/core/runtime-config/garbage-collector.md)
  —— `System.GC.ConserveMemory` 定义、0–9 取值、"start with 5–7"、启动时只读一次
- [LOH 大对象优化指导](loh-large-object-optimization-guide.md) —— 常驻宽度优化主路径；§4 含 GC 参数的作用域注记
- [Version4 DOP12 诊断](benchmarks/nlissn-version4-dop12-diagnostics.md) —— 撞内存墙的完整实测
- [C# 对象内存优化已验证方法](research/2026-09-22-csharp-object-memory-optimization-verified-methods.md) —— GC 参数备选清单
- 完整测量记录（仓库外）：`D:\TRbackup\Version4\Build\NL-gc-singlefile\REPORT-TOTAL-MEMORY.md`
