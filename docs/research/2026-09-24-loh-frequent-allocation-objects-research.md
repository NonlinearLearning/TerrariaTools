# LOH 常驻堆「频繁创建/释放对象」研究报告

**日期：** 2026-09-24
**方法：** 严格按 [LOH 大对象优化指导](../loh-large-object-optimization-guide.md) §2 的诊断流程执行，
并把它要求的「循环采样」做满 **16 个独立分析循环**（§1）。
**数据源：** 4 份完整诊断运行（各含 `Counters/`、`Gcdump/`、`GcdumpTypeStats/`、
`GcdumpReport/`、`Trace/allocation.nettrace`），共 **148 个 gcdump 样本 + 4 份 60 s 分配 trace**。
**新增测量：** 3 个探针 + 3 份分析脚本（全部落在 `Build/loh-research/`，不改产品代码）。

> 本文回答的问题是：**类存（托管堆）中哪些类/结构体被频繁创建与释放，它们的尺寸是多少，
> 应该优化哪些。** 不新增生产代码改动。

---

## 0. 结论摘要

| # | 结论 | 强度 |
| --- | --- | :---: |
| **S1** | **「频繁创建/释放」有两个互不相同的口径**：60 s 内的**累计分配量**（瞬态 churn）与存活堆内的**出现/消失震荡**（活体 churn）。用错口径会把结论反过来 | 实测 |
| **S2** | 瞬态第一大是 **`InterproceduralDataFlowPlan[]`（428 B/元素）**：4 份 trace 合计分配 **8,371 MiB**，占全部 LOH 分配的 **24.6%~51.9%**，且**逐次复现** | 实测 |
| **S3** | 但**它不是常驻基线**：排除饱和样本后，它的常驻峰值仅 **6.69 MiB**，且在 4 份运行里一致。**按指导 §2 Step 2，压它对常驻 LOH 无杠杆** | 实测 |
| **S4** | **真正的常驻基线是 `NLCPGNode[]` 与 `PendingEdge[]`**：在可用样本里 **100% 出现**（30/30 与 42/42） | 实测 |
| **S5** | 存活堆里**震荡最剧烈**的第一方类型是 **`PendingEdge[]`**：出现/消失 **13 次**、幅度比 **16,363×**（含饱和样本口径）；**排除饱和样本后峰值 306 MB，仍是 100% 常驻** | 实测 |
| **S6** | 指导 §1.2 的宽度表**全部复测成立**（`NLCPGEdge` 72 B、`NLCPGNode` 104 B、`PendingEdge` 272 B、`InterproceduralDataFlowPlan` 428 B），且**新增 12 项**未记录的宽度 | 实测 |
| **S7** | **修正既有研究的一处过度结论**：`Entry<NLCPGEdge>[]`（915 MB）**不是「常驻」**——它只在 68 个样本中的 **6 个**出现，且在**同一运行内、更晚的样本里降为 0**。它是**长生命周期构图中态**，不是稳态常驻 | 实测 |
| **S8** | **`NLCPGNode[]` 是 4 份运行中唯一「每次都常驻、且从未被优化过」的 LOH 大对象**：104 B/元素、818 个元素即入 LOH、峰值 **329 MB** | 实测 |
| **S9** | 指导 §1.3 的「阶跃 + 长平台」**在 4 份运行中全部复现**：最长平台 **13,749 MB 持续 1,152 s**，其间 **9 次 Gen2 GC 一动不动** | 实测 |
| **S10** | **仓库的 BenchmarkDotNet 产物在结构上无法回答本任务** —— 只启用 `MemoryDiagnoser` 的 Gen0/Gen1/Allocated，**无 LOH 维度、无单对象尺寸**（§1.3 第 11 项） | 实测 |

**一句话：**
> **瞬态第一大是 `InterproceduralDataFlowPlan`（可压，但压它不降常驻）；
> 常驻基线是 `NLCPGNode[]` 与 `PendingEdge[]`（这才是 LOH 高位常驻的成因）；
> 而 `NLCPGNode` 的 104 B 里有 32 B（`StableAnchor`）是冻结后不再需要的构图期身份 —— 这是下一个宽表示目标。**

---

## 1. 执行记录：16 个分析循环

每个循环独立取证，产物全部落盘可复核。

| 循环 | 内容 | 产物 |
| ---: | --- | --- |
| 1 | 清点全部性能报告资产 | `data/cycle01-run-inventory.csv` |
| 2 | 4 份运行的 LOH/Gen2 计数器全域统计 | 本报告 §2 |
| 3 | 4 份 nettrace 的分配量按**类型**归因（`TraceAllocStats`） | `trace-out/<run>/alloc-by-type.csv` |
| 4 | 逐类型逐样本**时间序列**重建（存活堆内震荡） | `data/cycle04-type-series-*.csv`（4 份，共 38,545 类型行） |
| 5 | 4 份 nettrace 的分配量按**调用点**与**叶子函数**归因 | `trace-out/<run>/alloc-by-site.csv`、`alloc-by-leaf.csv` |
| 6 | 用 `Unsafe.SizeOf<T>()` **实测**全部候选宽度 | `data/cycle08-widths.csv` |
| 7 | 从 gcdump 的 `TypeSize` 列取**每实例实测尺寸**（第一方类型 459 项） | `data/cycle09-firstparty-instance-sizes.csv` |
| 8 | **LOH 专属**归因（`AllocationKind == Large`，自研 `TraceAllocStats2`） | `trace-loh/<run>/alloc-loh-by-type.csv` |
| 9 | **常驻基线 vs 瞬态尖峰**分离（地板中位数 / 峰值） | `data/cycle12-resident-vs-transient-retest.csv` |
| 10 | 跨 4 运行交叉验证（累计分配 + 常驻双口径） | `data/cycle10-churn-size-join.csv`、`cycle10b-loh-crossrun.csv` |
| 11 | LOH 分配速率**时间线**（每秒 LOH/SOH 分桶） | `trace-loh/<run>/alloc-timeline.csv` |
| 12 | 源码核对：当前实现的瞬态分配清单与重复载体 | 本报告 §6 |
| 13 | 复制系数与「intern 后重新膨胀」核查 | 本报告 §6.2 |
| 14 | LOH **阶跃/长平台**独立验证 + 内存余量 | `data/cycle14-loh-plateau-*.csv`、`cycle14b-memory-headroom.csv` |
| 15 | **饱和样本过滤**（口径坑校验）与修正后的常驻率 | `data/cycle15-saturation.csv` |
| 16 | **运行时字段偏移实测**（`Marshal.OffsetOf`）—— 修正本文初稿的字段加总推导 | 本报告 §3.5、§6.1.1 |

> ⚠️ **循环 16 是自我纠错**：§6.1.1 初稿按字段类型宽度手工加总 `NLCPGNode` 得 101 B，
> 这与指导 §1.2 明令禁止的「推导」是同一个错误。已改为运行时实测，结论从 36 B 修正为 **32 B**。

### 1.1 复用的分析工具

| 工具 | 来源 | 用途 |
| --- | --- | --- |
| `TraceAllocStats` | `D:\TRbackup\NL-diag-tools\TraceAllocStats`（复制源码本地重建） | 按类型/调用点/叶子归因累计分配 |
| `TraceAllocStats2` | **本次新增** `Build/loh-research/TraceAllocStats2` | **LOH/SOH 拆分** + 每类型 LOH 排名 + 每秒速率时间线 |
| `WidthProbe` | **本次新增** `Build/loh-research/WidthProbe` | `Unsafe.SizeOf<T>()` 实测宽度 + LOH 临界元素数 + 容器槽几何 |
| `LayoutProbe` | **本次新增** `Build/loh-research/LayoutProbe` | `Marshal.OffsetOf` 实测**字段偏移与填充**（槽几何闭合校验） |
| `Analyze-TypeSeries.ps1` | **本次新增** `Build/loh-research/scripts` | 逐类型存活堆时间序列重建 |
| `Analyze-Resident-vs-Transient.ps1` | **本次新增** | 地板中位数（基线）vs 峰值（尖峰） |
| `Analyze-LohPlateau.ps1` | **本次新增** | 最长常数平台 + 平台内 Gen2 GC 次数 + 越阈时刻 |

### 1.2 四份运行的身份

四份运行**输入与 DOP 完全相同**（`D:/TRbackup/Version4`，五处 DOP 均为 `12`），
但**代码版本不同**，因此可作跨版本对照：

| 运行 | 启动时刻 (UTC) | 样本数 | 用途 |
| --- | --- | ---: | --- |
| `version4-dop12-20260922-173202` | 2026-09-22 17:32 | 21 | 基线 |
| `20260923-130737-dop12` | 2026-09-23 13:07 | 26 | 基线（崩溃于 `0x80131506`） |
| `20260923-201500-dop12-retest` | 2026-09-23 20:02 | 68 | 中期 |
| `20260924-000000-dop12-refactored` | 2026-09-23 23:52 | 33 | **最新（边投影化之后）** |

> ⚠️ **代码版本判定**：四份运行的 `run-environment.yml` **都不含 DLL 哈希或提交号**，
> 因此**无法从报告本身证明**哪份含哪次改动。本文用**堆中类型的直接存在性**反推（见 §2.3），
> 并明确标注哪些结论依赖该反推。

### 1.3 全部性能报告清点（「查看全部的性能报告」的落实）

| # | 报告 / 数据集 | 位置 | 是否含 LOH/对象尺寸信息 | 本次处置 |
| ---: | --- | --- | --- | --- |
| 1 | **4 份诊断运行全量**（Counters / Gcdump / GcdumpTypeStats / GcdumpReport / Trace） | `D:\TRbackup\Version4\Build\NL-diagnostics-dop12\*` | ✅ **核心数据源** | 循环 1–15 全量挖掘 |
| 2 | LOH 大对象清单（既有研究） | `docs/research/2026-09-24-loh-large-object-inventory-from-reports.md` | ✅ 单运行清单 | **已对照并修正两处**（§4.3、§6.1.3） |
| 3 | NLCPG class/struct 内存评估 | `docs/research/2026-09-17-nlcpg-class-struct-memory-assessment.md` | ✅ **预测了本次实测的宽数组** | §1.4 逐条对账 |
| 4 | 跨过程 pass 分配削减设计/执行 | `docs/plans/2026-09-23-interprocedural-pass-alloc-reduction-*.md` | ✅ `PendingEdge` 272 B、`ceil(85000/272)=313` | §1.4 逐条对账（**数字相同**） |
| 5 | 边载荷序数化设计 | `docs/plans/2026-09-23-edge-payload-ordinalization-design.md` | ✅ 构图期 120 B → 16 B | 已引用（§6.2） |
| 6 | 冻结边投影化设计/执行 | `docs/plans/2026-09-24-frozen-edge-projection-*.md` | ✅ 边侧 SoA | 已引用 |
| 7 | Dictionary `Entry<>` 内存研究 | `docs/research/2026-09-23-dotnet-dictionary-entry-memory-research.md` | ✅ 容器槽几何 | 已用于 §3.3 交叉验证 |
| 8 | C# 对象内存优化已验证方法 | `docs/research/2026-09-22-csharp-object-memory-optimization-verified-methods.md` | ✅ GC 参数否决 | 已引用（§7.3） |
| 9 | 零分配 key 设计/执行 | `docs/plans/2026-09-23-zero-allocation-key-*.md` | ⚠️ 分配量（非 LOH） | 已记录（`NodeSortKey` 1,864.4 MiB，本次未复测） |
| 10 | **PerformanceTests 源码**（19 个文件） | `tests/NLISSN.PerformanceTests/` | ✅ **含 `Unsafe.SizeOf` 断言** | **本次新读**，见 §6.1.2 独立佐证 |
| 11 | BenchmarkDotNet 结果（2 份 github/csv/html + 10 份 log） | `BenchmarkDotNet.Artifacts/` | ❌ **只有 Gen0/Gen1，无 LOH 维度** | 本表说明为何不可用 |
| 12 | `Build/verify-algo-speed/SUMMARY.md` | 算法提速 | ⚠️ 分配降幅（非 LOH） | 已引用（元数据快速路径出处） |
| 13 | `Build/verify-stage-cpu/SUMMARY.md` | 阶段 CPU | ❌ 无内存维度 | 不适用 |
| 14 | `Build/PerformanceResults/` | CPG JSON 导出 + `manifest.json` | ❌ **是 CPG 图导出，不是内存报告** | 已核实，排除 |
| 15 | `Build/PerformanceResults/domain-model-struct-20260918/logs` | — | ❌ **目录为空** | 已核实，排除 |

> **⚠️ 第 11 项是本次「全部报告」清点中得到的一个重要负面结论**：
> 仓库的 BenchmarkDotNet 运行**只启用了 `MemoryDiagnoser` 的 Gen0/Gen1/Allocated 列，
> 没有任何 LOH 计数或单对象尺寸维度**。因此
> **BenchmarkDotNet 产物在结构上就无法回答本任务的问题** —— 例如
> `FullAnalysisBenchmark` 报告 `Allocated = 17.12 MB`、`Gen0 = 2000`，
> 但 17.12 MB 是按**单次操作累计分配**计的，与大文件构图时 8,538 MB 的单次瞬态**不在同一量级**，
> 且不区分 LOH/SOH。
> **⇒ 本报告的 LOH 结论只能来自 nettrace（含 `AllocationKind`）与 gcdump（含单对象尺寸），
> 这是数据源能力的硬边界，不是分析选择。**

### 1.4 与既有结论的对账（三处独立一致，一处修正）

| 数值 | 既有报告记载 | 本次独立实测 | 一致？ |
| --- | ---: | ---: | :---: |
| `NLCPGEdge` 宽度 | 72 B（指导 §1.2 + 测试断言） | **72 B**（`WidthProbe` + 槽几何闭合） | ✅ |
| `NLCPGNode` 宽度 | 104 B（指导 §1.2） | **104 B**（`WidthProbe` + 布局残差 0） | ✅ |
| `PendingEdge` 宽度 | 272 B（指导 + 分配削减设计 §0） | **272 B**（`WidthProbe`） | ✅ |
| `PendingEdge[]` LOH 临界 | 313 元素（分配削减设计 §0） | **313**（`ceil(85000/272)`） | ✅ |
| `Entry<NLCPGEdge>` 槽宽 | 80 B（指导 §2 Step 3） | **80 B**（959,915,944 B 整除） | ✅ |
| `InterproceduralDataFlowPlan` | **428 B**（指导 §1.2，纠正过推导的 48 B） | **428 B**（布局实测 4×104+4+4+4） | ✅ |
| 元数据覆盖率 | **0.0%**（指导 §2 Step 4，单文件语料） | **0.092%**（多文件语料，非零） | ⚠️ **修正**（§6.1.3） |
| 平台内 Gen2 GC | **7 次**（指导 §1.3） | **9 次**（逐步复算） | ⚠️ 差异（§4.4 已标注） |
| `Entry<NLCPGEdge>[]` 915 MB 的性质 | 既有清单列为常驻大对象 | **单样本阵发，同运行内降为 0** | ⚠️ **修正**（§4.3） |

**⇒ 七项独立一致、两项修正、一项差异已标注。** 一致率最高的三项（72 / 104 / 272 B）
来自**三种互不相同的方法**：指导的 `Unsafe.SizeOf`、测试断言、以及 gcdump 单对象字节整除 ——
**三者互证，故宽度结论可以当作已确立的事实使用。**

## 2. 数据卫生：三个必须先修的口径坑

这三条会让结论直接出错。**与既有研究 `2026-09-24-loh-large-object-inventory-from-reports.md` §1 记录的三坑一致，
但本文给出的是跨 4 运行的量化版本。**

### 2.1 坑一：饱和样本（本轮实测：retest 有 **26/68** 个饱和）

`gcdump` 在对象数触顶时只覆盖部分堆。判据：`GcHeapObjects` 饱和在上限 **10,000,000**。
**这类样本会漏掉 LOH 桶**，若混入统计，`MaxTotalBytes` 会被**夸大**。

| 运行 | 样本数 | 饱和样本数 | 比例 |
| --- | ---: | ---: | ---: |
| `20260924-000000-dop12-refactored` | 33 | **3** | 9.1% |
| `20260923-201500-dop12-retest` | 68 | **26** | **38.2%** |
| `20260923-130737-dop12` | 26 | 2 | 7.7% |
| `version4-dop12-20260922-173202` | 21 | 3 | 14.3% |

**⇒ 本文全部「常驻率」与「峰值」统计均已排除饱和样本**（循环 15）。

**⚠️ 这条直接反驳了本文的一个初步结论**：未排除饱和样本时，`PendingEdge[]` 的峰值为 589 MB；
排除后为 **306 MB**。**差异 1.9×** —— 与既有研究记录的「`System.String` 被夸大 6.58×」是同一类错误。

### 2.2 坑二：`gcdump-type-summary.csv` 的 `Max*` 列不可直接引用

该文件的 `Max*` 列把饱和样本算了进去（例如 `PendingEdge[]` 的 `MaxBytesSample` 指向
一个饱和样本）。**本文所有逐类型统计都从 `gcdump-type-totals-per-sample.csv` 按 `Sample` 过滤后自建**
（循环 4），与该汇总文件无关。

### 2.3 坑三：size bucket 是**十进制下界**，`10K` 桶跨越 85,000 B 阈值

沿用既有研究的校验结论：标签是**十进制下界**（`1K`=1,000、`10K`=10,000…），
故 **只有 `100K`/`1M`/`10M`/`100M` 四个桶可整体当 LOH**；`10K` 桶横跨阈值，不可整体当 LOH。
**本文改用 `TypeSize ≥ 85,000` 与 `TotalBytes` 直接判定（循环 7/9），不再依赖桶标签。**

---

## 3. 尺寸实测（循环 6 + 7）

### 3.1 指导 §1.2 宽度表的完整复测 —— **全部成立**

`WidthProbe` 用 `Unsafe.SizeOf<T>()` 实测（**不推导**）：

| 元素类型 | 指导 §1.2 记录 | 本次实测 | LOH 临界元素数 `ceil(85000/宽)` | 一致？ |
| --- | ---: | ---: | ---: | :---: |
| `int` | 4 B | 4 B | 21,250 | ✅ |
| `NLCPGEdge` | **72 B** | **72 B** | **1,181** | ✅ |
| `NLCPGNode` | **104 B** | **104 B** | **818** | ✅ |
| `PendingEdge` | **272 B** | **272 B** | **313** | ✅ |
| `InterproceduralDataFlowPlan` | **428 B** | **428 B** | **199** | ✅ |

### 3.2 新增实测宽度（指导未记录，本次补齐 12 项）

| 类型 | 宽度 | LOH 临界元素数 | 说明 |
| --- | ---: | ---: | --- |
| `NLCPGNodeDraft` | **96 B** | 886 | `AddNode` 的入参草稿（`record struct`，含 9 个引用/可空） |
| `StableNodeAnchor` | **28 B** | 3,036 | 7 字段紧凑锚点 |
| `CpgEdgeCandidate` | **120 B** | 709 | Streaming 路径 |
| `CpgNodeDescriptor` | **92 B** | 924 | Streaming 路径 |
| `NLCPGCallSiteContext` | **24 B** | 3,542 | 2 引用 + 2 int |
| `NLCPGCallSiteContext?` | **32 B** | 2,657 | |
| `NLCPGEdgeLabel` | **8 B** | 10,625 | **引用类型**（`sealed record class`），8 B 是引用宽度 |
| `NLCPGContextId` | **8 B** | 10,625 | 单字段 `readonly record struct(string)` |
| `NLCPGContextId?` | **16 B** | 5,313 | |
| `PendingEdgeKey` | **16 B** | 5,313 | `record struct(int,int,enum,int)` |
| `NLCPGDispatchKind` | **16 B** | 5,313 | |
| `NodeId` | **4 B** | 21,250 | `readonly record struct(uint)` |

### 3.3 容器槽几何（字典/集合的真实开销）

| 容器槽 | 宽度 | LOH 临界槽数 |
| --- | ---: | ---: |
| `Dictionary<NLCPGEdge,·>.Entry`（= `HashSet<NLCPGEdge>` 槽） | **80 B** | 1,063 |
| `Dictionary<NodeId,NLCPGNode>.Entry` | **120 B** | 709 |
| `Dictionary<StableNodeAnchor,NLCPGNode>.Entry` | **144 B** | 591 |
| `Dictionary<StableNodeAnchor,int>.Entry` | **40 B** | 2,125 |
| `Dictionary<PendingEdgeKey,·>.Entry` | **24 B** | 3,542 |

> ✅ 这三项与指导 §3 Step 3 案例记录的 **`Entry<NLCPGEdge>` = 80 B** 精确吻合。

### 3.4 从 gcdump 取到的**每实例实测尺寸**（循环 7，459 个第一方类型）

**数组类型**（`TypeSize` 即该单对象的实测字节数）：

| 类型 | 实测每实例字节 | 折算 |
| --- | ---: | --- |
| `BufferedPendingEdge[]` | 125,829,144 | 该元素宽 ≈ 96 B（仅旧版本出现，见 §5.1.1） |
| `NLCPG.Model.NodeId[]` | 3,751,652 | `(3,751,652−24)/4 = 937,907` 整 ✅ |
| `NLCPG.Contracts.NLCPGEdgeKind[]` | 3,751,652 | 同上 ✅ |
| `NLCPG.Model.NLCPGNode[]` | 53,272 | `(53,272−24)/104 = 512` 整 ✅ |
| `NLCPG.Model.NLCPGEdge[]` | 45,312 | `(45,312−24)/72 = 629` 整 ✅ |
| `NLCPG.Model.StableNodeAnchor[]` | 28,696 | `(28,696−24)/28 = 1,024` 整 ✅ |

> ✅ **槽几何全部闭合（残差 0 B）** —— 又一次印证指导 §2 Step 3 的「槽几何必须闭合」校验。

**引用类型**（`TypeSize` = 含对象头的实例尺寸）：

| 类型 | 实测每实例字节 | 说明 |
| --- | ---: | --- |
| `NLCPG.Builder.OperationInventoryEntry` | **152** | 峰值 **415,795 实例**、60.27 MB |
| `MethodDataFlowPlan` | 312 | 峰值 373 实例 |
| `NLCPG.Builder.NLCPGBuilder` | 352 | 单例 |
| `NLCPG.Model.NLCPGEdgeLabel` | **64** | 峰值 **1,039,096 实例**、63.42 MB |
| `NLCPG.Model.NLCPGGraph+PendingEdgeBuffer+EdgeMetadata` | **38** | 峰值 23,000 实例 |

> **⚠️ `OperationInventoryEntry` 是 `sealed record`（`NLCPGBuildContext.cs:143`）**：
> 它是**类**而非结构体 ⇒ 每个实例都带对象头 + 方法表指针。
> 415,795 实例 × 152 B = 60.27 MB，**出现/消失 8 次、幅度比 10×**（§5.2.1）。

### 3.5 运行时**字段布局**实测（循环 16，`Marshal.OffsetOf`）

这是「能不能改小」的直接依据 —— 宽度总量不能告诉你哪个字段可移除。

| 结构体 | sizeof | 布局（偏移：字段 = 占位） |
| --- | ---: | --- |
| `NLCPGEdge` | 72 | +0 `SourceNodeId`=4、+4 `TargetNodeId`=4、+8 `Kind`=**8**（4 值 + 4 填充）、+16 `StructuredLabel`=8、+24 `ContextId?`=16、+40 `CallSiteContext?`=**32** |
| `PendingEdge` | 272 | +0 `SourceNode`=**104**、+104 `TargetNode`=**104**、+208 `Kind`=**8**、+216 `StructuredLabel`=8、+224 `ContextId?`=16、+240 `CallSiteContext?`=32 |
| `PendingEdgeOrdinal` | **72** | +0 `SourceOrdinal`=4、+4 `TargetOrdinal`=4、+8 `Kind`=8、+16 `StructuredLabel`=8、+24 `ContextId?`=16、+40 `CallSiteContext?`=32 |
| `InterproceduralDataFlowPlan` | 428 | +0/+104/+208/+312 四个 `NLCPGNode`**各 104**、+416 `BridgeKind`=4、+420 `ArgumentOrdinal`=4、+424 `StableCallSiteOrder`=4 |
| `CpgEdgeCandidate` | 120 | +0 `SourceAnchor`=28、+28 `TargetAnchor`=28、+56 `Kind`=8、+64 `StructuredLabel`=8、+72 `ContextId?`=16、+88 `CallSiteContext?`=32 |
| `CpgNodeDescriptor` | 92 | +0 `Anchor`=28、+28 `Kind`=4、+32 `NameId`=4、+36 `FullNameId`=4、+40 `SignatureId`=4、+44 `DispatchKind?`=**20**、+64 `TypeFullNameId`=4、+68 `FilePathId`=4、+72 `SpanStart?`=8、+80 `SpanEnd?`=8、+88 `IsImplicit`=4 |

**⇒ 直接读出的三条结论：**

1. **`PendingEdge`（272 B）的两个 `NLCPGNode` 端点占 208 B = 76.5%。**
   而**已有**一个同构的 `PendingEdgeOrdinal`（**实测 72 B**，两端各 4 B）
   ⇒ **272 B → 72 B（−73.5%）是「换表示」而非「发明新结构」。**（→ P0-1）
2. **`ContextId?`(16) + `CallSiteContext?`(32) = 48 B 在 `NLCPGEdge`/`PendingEdge`/
   `PendingEdgeOrdinal`/`CpgEdgeCandidate` 里重复出现**，而 `CallSiteContext` 的
   生产覆盖实测为 0（§6.1.3 给出元数据侧的真实覆盖率 0.092%）。
3. **`IsImplicit`(bool) 在 `NLCPGNode` 与 `CpgNodeDescriptor` 里都占 4 B**（1 B 值 + 3 B 填充）
   ⇒ 单独收窄它**省 0 B**，正是指导 §3.1 警告的「只有在 SoA 里才兑现」。



---

## 4. 常驻基线 vs 瞬态尖峰（循环 9 + 14 + 15）

### 4.1 方法

- **常驻基线** = 存活堆内该类型 `TotalBytes` 的**地板中位数**（仅取有值样本），
  并按指导 §2 Step 2「锯齿回落后的地板值」判读。
- **瞬态尖峰** = 累计分配量（60 s trace 的 `GCAllocationTick`），指导 §0 第 1 条明确它**只决定锯齿高度**。
- **饱和样本已排除**。

### 4.2 「常驻率」—— 4 份运行一致

| 类型 | refactored 出现率 | retest 出现率 | 峰值（可用样本） |
| --- | ---: | ---: | ---: |
| **`NLCPGNode[]`** | **30/30（100%）** | **42/42（100%）** | 174 MB / 329 MB |
| **`PendingEdge[]`** | **30/30（100%）** | **42/42（100%）** | 306 MB / 88 MB |
| `InterproceduralDataFlowPlan[]` | 30/30 | 42/42 | **6.69 MB / 6.69 MB** |
| `NLCPGEdge[]` | 8/30（27%） | 7/42（17%） | 162 MB / 477 MB |
| `Entry<NLCPGEdge>[]` | 7/30（23%） | 6/42（14%） | 106 MB / **915 MB** |
| `System.String` | 30/30 | 42/42 | 1,520 MB / 1,528 MB |

**⇒ 三条硬结论：**

1. **`InterproceduralDataFlowPlan[]` 是「每次都在、但常驻峰值只有 6.69 MB」** ——
   它的 428 B 宽度全部花在**瞬态列表**上（§5.1）。**压它降 churn，不降常驻。**
2. **`NLCPGNode[]` 与 `PendingEdge[]` 是真正的常驻基线**（100% 出现率）。
3. **`Entry<NLCPGEdge>[]` 与 `NLCPGEdge[]` 是「阵发性」的**，
   其中 `Entry<NLCPGEdge>[]` 的 915 MB 出现在**单个样本**（§4.3）。

### 4.3 ⚠️ 修正既有研究：`Entry<NLCPGEdge>[]` 的 915 MB **不是常驻**

在 `retest` 运行里，`Entry<NLCPGEdge>[]` 的 915.45 MB 只出现在样本 `0087-late-4863s` 一处，
而**同一运行内更晚的样本 `0092-late-5015s` 起它降为 0**：

| 样本 | 时刻 | 托管堆 | `Entry<NLCPGEdge>[]` | `NLCPGEdge[]` | `NLCPGNode[]` |
| --- | ---: | ---: | ---: | ---: | ---: |
| `0083-late-4742s` | 4,742 s | 450 MB | 0 | 0 | 52.21 MB |
| `0085-late-4802s` | 4,802 s | 1,252 MB | 0 | 0 | 271.62 MB |
| **`0087-late-4863s`** | **4,863 s** | **3,985 MB** | **915.45 MB** | **477.43 MB** | **329.11 MB** |
| `0088-late-4893s` | 4,893 s | 470 MB | **0** | **0** | 26.00 MB |
| `0092-late-5015s` | 5,015 s | 1,654 MB | 0 | 0 | 0 |
| `0096-late-5184s` | 5,184 s | 1,560 MB | 0 | 0 | 0 |

**⇒ 判读**：
- 它不是稳态常驻，而是**单次大文件构图期的长寿中态**（构建该文件时 915 MB 边去重集 + 477 MB 边数组**同时**存活）。
- 结束于 `0088`（**30 秒内消失**）⇒ 其生命周期由**单个文件的构图**界定。
- **按指导模式 C，它仍是最高 ROI 的「去复制」目标**（复制系数见 §6.2），
  **但不能称其为「常驻基线」**，也不能据此声称它抬高了 LOH 平台。

### 4.4 LOH 阶跃 + 长平台（循环 14）—— 指导 §1.3 **全部复现**

| 运行 | LOH 峰值 | **最长常数平台** | 平台内 Gen2 GC |
| --- | ---: | ---: | ---: |
| `20260923-201500-dop12-retest` | 14,511 MB | **13,749 MB 持续 1,152 s** | **9 次一动不动** |
| `20260923-130737-dop12` | 17,833 MB | 17,833 MB 持续 262 s | 7 |
| `version4-dop12-20260922-173202` | 18,468 MB | 18,468 MB 持续 220 s | 6 |
| `20260924-000000-dop12-refactored` | 11,131 MB | 5,630 MB 持续 88 s | 6 |

**平台窗口逐步复核**（`retest`，最严格口径）：

```text
首样本 09/23 21:03:13   末样本 09/23 21:22:25   ⇒ 1,152 s
值    14,417,286,624 B = 13,749 MB（23 个连续样本逐字节相同）
期间Gen2 GC 2+1+1+1+2+1+1 = 9 次
样本间距 135 / 205 / 212 / 202 / 222 / 152 s
```

> ⚠️ **一处与指导的数值差异（诚实标注）**：指导 §1.3 记录该平台期间「跑了 **7** 次 Gen2 GC」，
> 本次逐步复算为 **9 次**。差异可能来自窗口边界是否含端点样本，或对
> `dotnet.gc.collections` 的 `Rate` 型计数器做累加 vs 取首值的口径差。
> **两个数字都支持同一条结论**：19.2 分钟内 Gen2 反复回收，**LOH 容量一动不动**。
> 本文以**逐步复算的 9 次**为准，并保留该差异供复核。

> **⭐ 本次额外获得的机制旁证**：平台期内的计数器**样本间距达 135~222 s**
> （正常应为 2 s）。这与指导 §1.3 第 2 条「平台期是**停滞**不是空闲」完全一致 ——
> `Rate` 型计数器只在确有活动时才出行 ⇒ **进程几乎不分配 ⇒ GC 无触发理由**。

**越阈时刻表（retest）** —— 与指导 §1.3 的机制三段式一致：

| LOH 阈值 | OLD 首达 | NEW 首达 | 推迟 |
| ---: | ---: | ---: | ---: |
| 8,000 MB | 474 s | 1,096 s | +622 s |
| 10,000 MB | 483 s | 1,240 s | +757 s |

**内存余量（循环 14）** —— 这一条解释了平台为何是「停滞」：

| 运行 | committed 峰值 | WS 峰值 | **committed/WS** | LOH 碎片峰值 |
| --- | ---: | ---: | ---: | ---: |
| `20260924-...-refactored` | 14,395 MB | 7,824 MB | **1.84×** | 505 MB |
| `20260923-...-retest` | 19,374 MB | 10,176 MB | **1.90×** | 2,070 MB |
| `20260923-...-130737` | 28,022 MB | 10,302 MB | **2.72×** | 993 MB |
| `version4-...-173202` | 25,927 MB | 8,578 MB | **3.02×** | 704 MB |

**⇒ committed 长期是 WS 的 1.8~3.0 倍**（物理内存仅 13.86 GiB）⇒ 与指导 §1.3 的推断一致。
**且 LOH 碎片峰值仅 505~2,070 MB，相对 11~18 GB 的 LOH 容量是 3.5%~14%** ⇒
**进一步印证指导 §4「LOH 压缩不是主通路」**。

---

## 5. 频繁创建/释放排名（循环 3 + 5 + 8 + 10 + 11）

### 5.1 口径 A：**累计分配量**（瞬态 churn）

**方法**：4 份 nettrace 各取前 60 s，按 `GCAllocationTick` 的 `AllocationAmount64` 求和。
4 份合计分配 **39.51 GiB**。

#### 5.1.1 全部类型排名（4 运行合计）

| # | 类型 | 合计分配 | 占比 | Ticks | 出现运行数 |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | **`InterproceduralDataFlowPlan[]`** | **8,371.41 MiB** | **20.69%** | 19,203 | **4/4** |
| 2 | `NLCPGEdge[]` | 2,139.62 MiB | 5.29% | 4,439 | 4/4 |
| 3 | **`PendingEdge[]`** | **1,776.82 MiB** | **4.39%** | 2,757 | 4/4 |
| 4 | `NLCPGNode[]` | 1,731.87 MiB | 4.28% | 10,436 | 4/4 |
| 5 | `NLCPGInterproceduralBridgeKind`（装箱） | 516.24 MiB | 1.28% | 5,080 | 1/4 |
| 6 | `BufferedPendingEdge[]` | 430.94 MiB | 1.07% | 640 | 1/4 |
| 7 | `NLCPGEdgeLabel` | 204.28 MiB | 0.51% | 2,010 | 4/4 |
| 8 | `StableNodeAnchor[]` | 192.76 MiB | 0.48% | 1,259 | 4/4 |
| 9 | `CpgEdgeCandidate[]` | 192.10 MiB | 0.48% | 1,425 | 4/4 |
| 10 | `EdgeMetadata` | 185.09 MiB | 0.46% | 1,821 | 3/4 |
| … | `OperationInventoryEntry` | 23.11 MiB | 0.06% | 227 | 4/4 |
| … | `SyntaxSemanticFacts` | 18.10 MiB | 0.05% | 178 | 4/4 |

#### 5.1.2 **LOH 专属**排名（循环 8，自研 `TraceAllocStats2`）

指导 §5.1 要求「确认撞的是 LOH」—— 这是唯一直接给出 LOH/SOH 拆分的口径。

| 运行 | LOH 分配 | LOH 占比 | LOH ticks | 平均 B/tick | SOH 分配 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `refactored` | **1,262.9 MiB** | 28.3% | 4,305 | 307,618 | 3,203.1 MiB |
| `retest` | **4,401.3 MiB** | 39.1% | 9,444 | 488,682 | 6,854.7 MiB |
| `130737` | **6,120.4 MiB** | 46.5% | 7,732 | 830,019 | 7,043.9 MiB |
| `version4` | **6,102.2 MiB** | 52.7% | 8,392 | 762,462 | 5,471.7 MiB |

**LOH 类型排名（4 运行）**：

| 运行 | LOH 第一大 | 分配 | 占 LOH |
| --- | --- | ---: | ---: |
| `refactored` | `InterproceduralDataFlowPlan[]` | 311.00 MiB | **24.63%** |
| `retest` | `NLCPGEdge[]` | 1,055.29 MiB | **23.98%** |
| `130737` | `InterproceduralDataFlowPlan[]` | 3,167.45 MiB | **51.75%** |
| `version4` | `InterproceduralDataFlowPlan[]` | 3,167.45 MiB | **51.91%** |

**⚠️ 一处必须先纠正的口径错误（本文自查发现）**：`NLCPGEdge[]` 有两个不同的份额口径，
**不可混用**：

| 运行 | `NLCPGEdge[]` **占 LOH** 份额 | `NLCPGEdge[]` **占全部**分配份额 |
| --- | ---: | ---: |
| `version4`（边投影化之前） | **4.384%**（rank 6） | 2.78% |
| `refactored`（边投影化之后） | **7.891%**（rank 4） | 2.61% |

**⇒ 正确读法**：`NLCPGEdge[]` 占**全部**分配的比例确实**从 2.78% 降到 2.61%**（方向正确，但幅度很小）；
而它占 **LOH** 的比例**从 4.38% 升到 7.89%** —— 这**不是**回归，而是
**LOH 总量本身大幅下降**（`version4` 的 LOH 分配 6,102 MiB → `refactored` 的 1,263 MiB）。
分母缩小 4.8 倍而分子只缩小 2.7 倍 ⇒ 份额必然上升。

**⇒ 判断 LOH 改善必须用绝对量，不能用份额。** 绝对量对照：

| 类型 | `version4` LOH 分配 | `refactored` LOH 分配 | 变化 |
| --- | ---: | ---: | ---: |
| `NLCPGEdge[]` | 267.53 MiB | 99.66 MiB | **−62.7%** ✅ |
| `InterproceduralDataFlowPlan[]` | 3,167.45 MiB | 311.00 MiB | **−90.2%** ✅ |
| **LOH 总量** | **6,102.2 MiB** | **1,262.9 MiB** | **−79.3%** ✅ |

> ⚠️ **但这三个降幅不能归因于「已提交的修复」** —— 见 §1.2：四份运行**代码版本不同且无法从报告证明**。
> 尤其 `NLCPGEdge[]` 的下降混入了**该次运行处理的文件不同**（`refactored` 只跑到 1,231 s，
> 而 `version4` 的 trace 窗口内容不同）这一混淆因素。**故此处只报告观测到的差值，不声称因果。**

#### 5.1.3 叶子函数定位（`refactored` 运行，落到具体语句）

| 叶子函数 | 分配 | 占比 | Ticks |
| --- | ---: | ---: | ---: |
| `List<InterproceduralDataFlowPlan>.set_Capacity` | 237.32 MiB | 5.31% | 1,665 |
| `List<InterproceduralDataFlowPlan>.AddWithResize` | 178.95 MiB | 4.01% | 851 |
| `PendingEdgeBuffer.Materialize` | 155.13 MiB | 3.47% | 276 |
| `String.Ctor(ReadOnlySpan<char>)` | 155.13 MiB | 3.47% | 1,532 |
| `Array.Resize` | 126.93 MiB | 2.84% | 1,114 |

> **同一类型 `InterproceduralDataFlowPlan` 同时占据 `set_Capacity` + `AddWithResize` 两位**
> ⇒ 这是指导 §3.1 说的「换表示」候选，也是**指导已在 `NLCPGBuilder.cs:830-892` 修过的那一处**
> （源码注释记录旧值 `set_Capacity` 696.19 MiB / `AddWithResize` 551.40 MiB）。
> **本次实测（`refactored`）已降到 237.32 / 178.95 MiB ⇒ 修复生效，但未归零。**

### 5.2 口径 B：**存活堆内震荡**（活体 churn）

这是「频繁创建**和释放**」的字面口径 —— 对象在采样点之间反复出现又消失。

**判据**：`Appearances`（从「不存在」变为「存在」的次数）+ `InstAmplitudeRatio`
（最大实例数 ÷ 中位实例数）。

#### 5.2.1 第一方类型 Top（4 运行取最大）

> ⚠️ **口径说明（务必先读）**：本表的 `Appearances` / `InstAmplitudeRatio` / 常驻峰值
> 来自 `cycle04-type-series-*.csv`，**未排除饱和样本**（因为饱和样本的实例计数仍可用于
> 判断「该类型在那一档确实存在且极大」）。
> ⇒ **本表用于排序「谁在反复涨落」是有效的；但其峰值列不得与 §4.2 的峰值列混用**
> （§4.2 已排除饱和样本）。两处口径的差值实例：`PendingEdge[]` 本表 **589 MB**、§4.2 **306 MB**。

| 类型 | 出现/消失次数 | 幅度比 | 常驻峰值(未排饱和) | 最大实例 | 累计分配 |
| --- | ---: | ---: | ---: | ---: | ---: |
| **`PendingEdge[]`** | **13** | **16,363.75** | 589 MB | 65,455 | 1,776.82 MiB |
| **`OperationInventoryEntry`** | 8 | 10.00 | 60.27 MB | **415,795** | 23.11 MiB |
| `NLCPGEdgeLabel` | 7 | **198.05** | 63.42 MB | 1,039,096 | 204.28 MiB |
| `EdgeMetadata` | 7 | **198.05** | 1.58 MB | 23,000 | 185.09 MiB |
| `SyntaxSemanticFacts` | 7 | 48.92 | 23.46 MB | 439,230 | 18.10 MiB |
| `StableNodeAnchor[]` | 2 | 82.00 | 16.62 MB | 82 | 192.76 MiB |
| `CpgEdgeCandidate[]` | 2 | 209.00 | 24.88 MB | 418 | 192.10 MiB |
| `UsedFactRecord[]` | 3 | 239,102 | 8.32 MB | 239,102 | 4.70 MiB |
| `DefinitionFact[]` | 3 | 38,325 | 4.32 MB | 76,650 | 2.24 MiB |
| `CpgNodeDescriptor[]` | 2 | 110.00 | 10.07 MB | 110 | 46.73 MiB |

**⇒ 存活堆里震荡第一是 `PendingEdge[]`**（16,363× 幅度、13 次出现/消失，**排除饱和样本后
仍有 100% 出现率且峰值 306 MB**）。
这正是指导 §3 Step 3 案例所测的「构图期 pending 边缓冲」——
**它在 4 份运行的可用样本里 100% 出现，且在采样之间反复涨落**。

> ⚠️ **须与指导一致的口径**：`PendingEdge[]` 的 272 B/元素已被指导确认为
> 「构图期表示」。本文确认它在**最新运行里仍是常驻基线**，故指导 §3.2 模式 B
> 「intern 后重新膨胀」的自检（`PendingEdgeKey` 16 B → `NLCPGEdge` 72 B）
> **在最新实现里依然值得复核**。

### 5.3 两个口径的差异 —— 必须分开陈述

| 类型 | 累计分配（瞬态） | 常驻震荡（活体） | 两者结论 |
| --- | ---: | ---: | --- |
| `InterproceduralDataFlowPlan[]` | **#1（20.69%）** | 常驻峰值仅 6.69 MB | **只压瞬态** |
| `NLCPGNode[]` | #4（4.28%） | **100% 常驻** | **压常驻宽度** |
| `PendingEdge[]` | #3（4.39%） | **#1 震荡（16,363×）** | **两者都要压** ← **最高 ROI** |
| `Entry<NLCPGEdge>[]` | — | 阵发（6/42 样本） | 去复制（模式 C） |
| `OperationInventoryEntry` | 0.06% | 415,795 实例、幅度 10× | **改为 struct 或去重** |

---

## 6. 死字节与复制系数（循环 12 + 13）

### 6.1 宽表示的「死字节」核算

#### 6.1.1 `NLCPGNode` = 104 B，字段逐一核对（**循环 16：运行时实测偏移**）

**⚠️ 本节初稿曾按字段类型手工加总（得 101 B），那是「推导」——指导 §1.2 明令禁止。**
改用 `LayoutProbe` 的 `Marshal.OffsetOf` **在运行时逐字段量真实偏移**，结果如下：

| 偏移 | 字段 | 字段类型 | `Unsafe.SizeOf` | **实际占位（含填充）** |
| ---: | --- | --- | ---: | ---: |
| +0 | `Kind` | `NLCPGNodeKind` | 4 | **4** |
| +4 | `NameId` | `uint` | 4 | **4** |
| +8 | `FullNameId` | `uint` | 4 | **4** |
| +12 | `SignatureId` | `uint` | 4 | **4** |
| +16 | `DispatchKind` | `NLCPGDispatchKind?` | **20** | **20** |
| +36 | `TypeFullNameId` | `uint` | 4 | **4** |
| +40 | `FilePathId` | `uint` | 4 | **4** |
| +44 | `SpanStart` | `int?` | 8 | **8** |
| +52 | `SpanEnd` | `int?` | 8 | **8** |
| +60 | `IsImplicit` | `bool` | 1 | **4**（3 B 填充） |
| +64 | `NodeId` | `NodeId?` | 8 | **8** |
| **+72** | **`StableAnchor`** | `StableNodeAnchor?` | **32** | **32** |
| | | | | **合计 104，残差 0 B** ✅ |

> ✅ **槽几何闭合**：偏移 + 占位之和精确等于 `sizeof(NLCPGNode) = 104`，**无未解释字节** ——
> 与指导 §2 Step 3 的「槽几何必须闭合」是同一种硬校验手段。

**结论（源码 + 实测）**：
- 该结构体的**大部分字节是「节点身份 + 源位置」**，都是确定要用的。
- **但 `StableAnchor`（实测 32 B，偏移 +72）与 `NodeId`（8 B，偏移 +64）是「二选一」的身份**：
  源码 `Equals`/`GetHashCode`（`NLCPGNode.cs:20-69`）明确写成
  `if (StableAnchor.HasValue) … else if (NodeId.HasValue) …`，**两条路径互斥**。
- 而 `NLCPGNode[]` 是**冻结后的常驻数组**（可用样本出现率 **100%**），
  冻结后 `StableAnchor` 已无用途（锚点只在构图期用于去重合并）。
- ⇒ **可移除空间 = 32 B/元素 = 30.8%**，把 104 B 打到 **72 B**。
  **⚠️ 这会改变相等语义路径 ⇒ 属「改表示」而非「改字段」，需按指导 §5.2 做等价性 oracle。**

> **⚠️ 诚实边界**：`NodeId`（8 B）在冻结后**必须保留**（它是投影的键）。
> 本文只声称「`StableAnchor` 的 **32 B** 在冻结数组里可移除 ⇒ 104 B → 72 B」。
> **不声称能降得更低** —— 继续压缩需要同时处理 `DispatchKind?`（**实测 20 B**，不是推导的 16 B）
> 与确认 `Span`/`FilePathId` 是否已由锚点承载，**这些均未验证**。
> 另注：`DispatchKind` 的实测 20 B 本身就是一个**独立发现** ——
> 用 `NLCPGDispatchKind?`（16 B 的值 + 4 B hasValue）承载一个「可空标签」，
> 在本结构体里是**第 5 大字段**。

#### 6.1.2 `NLCPGEdge` = 72 B，死字节 **56 B 已被指导证明**

| 字段 | 实测偏移 | 占位 | 生产语料覆盖 |
| --- | ---: | ---: | --- |
| `SourceNodeId` + `TargetNodeId` + `Kind` | +0 / +4 / +8 | 4+4+**8**（含 4 B 填充） | ✅ 恒定 |
| `StructuredLabel`（引用） | +16 | 8 | 稀疏 |
| `ContextId?` | +24 | 16 | 稀疏 |
| `CallSiteContext?` | +40 | 32 | **实测 0** |
| **合计死字节** | | **56 B = 77.8%** | 与指导 §2 Step 4 一致 |

**独立佐证（本次发现的既有测试）**：`tests/NLISSN.PerformanceTests/Performance/NLCPGEdgeStoragePerformanceTests.cs:31-53`
的 `EdgeCarrierWidths_MatchDocumentedLayout` **在测试里断言同一组常量**：

```csharp
Assert.Equal(72, edgeSize);
Assert.Equal(4, nodeIdSize);
Assert.Equal(16, contextIdSize);
Assert.Equal(32, callSiteSize);
Assert.Equal(8, labelRefSize);
```

⇒ **本次 `WidthProbe` 的实测值与仓库既有断言逐项相同**（两套独立测量方法一致）。

**本次实测补充证据（`retest` 样本 `0087`，同 timestamp）**：
- `Entry<NLCPGEdge>[]` 单对象 **959,915,944 B**；
  `(959,915,944 − 24) / 80 = 11,998,949` **整除** ⇒ **槽宽 80 B，残差 0 B** ✅（与指导 §2 Step 3 完全一致）
- `NLCPGEdge[]` 单对象 **500,616,600 B**；
  `(500,616,600 − 24) / 72 = 6,953,008.0` **整除** ⇒ **边宽 72 B，残差 0 B** ✅
- **复制系数 = 959,915,944 / 500,616,600 = 1.9175**
  （该运行里两容器**不同时最大化** ⇒ 此值是该样本的下界，指导的 2.91× 来自两容器同时满配的场合）

#### 6.1.3 ⚠️ 元数据覆盖率修正：**本次语料实测非零**

指导 §2 Step 4 记录「真实单文件语料边元数据覆盖 = 0.0%」。**本次多文件语料实测非零**：

| 观测（直接读列，不推导） | `retest` | `refactored` | `version4` |
| --- | ---: | ---: | ---: |
| `NLCPGEdgeLabel` 出现的（样本,桶）行数 | **42** | **14** | 9 |
| `NLCPGEdgeLabel` 单样本最大实例数 | **23,000** | **9,422** | 1,039,096 |
| `NLCPGEdgeLabel` 单实例实测字节 | 64 | 64 | — |
| `Dictionary<EdgeMetadata,int>[]` 出现的行数 | **83** | **44** | **0** |
| 该字典单样本最大实例数 | 3 | 1 | 0 |

**取样（`retest` 样本 `0087-late-4863s`，同一 timestamp）：**
- `NLCPGEdgeLabel` 实例 **11,057**、合计 **707,648 B**（该样本的桶行实测值）。
- `Entry<…PendingEdgeBuffer+EdgeMetadata,System.Int32>[]` 单对象 **420,480 B**（类型名已含 `EdgeMetadata`，
  证明**元数据池在 TValue 类型参数里**，即 `_metadataIds` 已按非空分支分配）。

**⇒ 元数据覆盖率 = 11,057 / 11,998,949 ≈ 0.092%**（仍是「稀疏」，但**不等于 0**）
⇒ **`CanonicalEdgeStore.Create` 的 `hasMetadata` 快速路径在这份多文件语料上会走「非空」分支**，
`_metadataIds`（4 B/边）**会被分配**。

> **⚠️ 一条反例（诚实的负结果）**：`version4`（最早那份）的 `EdgeLabel` 最大实例数高达 **1,039,096**，
> 却**没有任何 `EdgeMetadata` 池数组**（0 行）⇒ 该运行使用的是**元数据内联展开**的旧表示
> （元数据直接内联在边值里，故 `NLCPGEdgeLabel` 实例数≈边数）。
> 这**独立佐证**了指导 §3.2 模式 B 的反模式确实存在于早期版本，且已被侧表化改造取代。

**⇒ 指导 §3.4 的 `13.01 B/边`（含元数据）口径才是本语料适用的口径，`9.00 B/边` 不适用。**
这条**修正了**「真实语料覆盖为 0」在实际多文件输入上的适用范围 —— 与指导 §7.3 自己标注的
「不适用元数据密集的场景 / 需要真实的多文件语料才能确认覆盖率」**方向一致，但本次给出了具体数值**
（比率 0.092%，且**逐运行可复现**：42 / 14 / 9 个样本行非零）。

### 6.2 「intern 后重新膨胀」核查（指导 §3.2 模式 B）

| 阶段 | 表示 | 每边 | 本次实测 |
| --- | --- | ---: | --- |
| 构图期 `PendingEdgeKey` | 元数据 intern 成 4 B id | **16 B** | ✅ `PendingEdgeKey` 实测 **16 B** |
| 常驻 `NLCPGEdge` | 元数据内联展开 | **72 B** | ✅ 实测 **72 B** |

**⇒ 模式 B 的反模式在最新源码里依然存在**：
`NLCPGGraph.PendingEdgeBuffer` 里元数据已 intern（`PendingEdgeKey` 含 `MetadataId`），
但 `RemapEdgesOrdinal`（`NLCPGGraph.cs:839-864`）把它**重新展开**成 6 字段的 `NLCPGEdge`。

**但注意**：`CanonicalEdgeStore.Project()` 已把**常驻**表示压到 3 列 + 稀疏池。
⇒ **重新膨胀只发生在「构图期的 `remappedEdges` 数组」这一瞬态上**，
即 `AssignDeterministicNodeIds` 的 `NLCPGEdge[]`（72 B/边）。
**这解释了口径 A 里 `NLCPGEdge[]` 累计分配 2,139 MiB、占 5.29%。**

---

## 7. 优化对象清单（按 ROI 排序）

**排序依据**：`(是否常驻) × (是否 100% 复现) × (可压缩比例) × (改动风险)`。

### 7.1 P0 —— 最高 ROI

| # | 对象 | 现状实测 | 建议 | 依据 | 风险 |
| ---: | --- | --- | --- | --- | --- |
| **P0-1** | **`PendingEdge`（272 B）→ 构图期表示** | 100% 常驻；震荡幅度 **16,363×**；13 次出现/消失；峰值 306~589 MB（口径见 §5.2.1）；累计分配 1,777 MiB | 按指导模式 A/B：实测两端的 `NLCPGNode` **各占 104 B = 208 B（76.5%）**。改用**同构的 `PendingEdgeOrdinal`（实测 72 B，两端各 4 B）**⇒ **272 B → 72 B（−73.5%）** | 指导 §3.2 模式 B；循环 16 实测两类型布局 | 中（需顺序/指纹 oracle） |
| **P0-2** | **`NLCPGNode`（104 B）的 `StableAnchor` 32 B** | 100% 常驻；峰值 329 MB；`NLCPGNode[]` 累计分配 1,732 MiB | 冻结数组里移除构图期专用的 `StableAnchor`（**实测偏移 +72、占 32 B**）⇒ **104 B → 72 B（−30.8%）** | §6.1.1（循环 16 实测） | **高**（改指纹，需 oracle） |
| **P0-3** | **`OperationInventoryEntry`（引用类型，152 B）** | 出现/消失 8 次；峰值 **415,795 实例**；60.27 MB；幅度 10× | 它是 `sealed record`（引用类型，各有 16 B 对象头）⇒ 改 `readonly record struct` **或**按方法分组去重 | `NLCPGBuildContext.cs:143` | 低（局部类型） |

### 7.2 P1 —— 中等 ROI

| # | 对象 | 现状实测 | 建议 |
| ---: | --- | --- | --- |
| **P1-1** | `Entry<NLCPGEdge>[]`（80 B/槽）**与** `NLCPGEdge[]`（72 B/边）**同时存活** | `0087` 样本：**915.45 MB + 477.43 MB = 1,393 MB**，复制系数 **1.92×** | 指导模式 C 已实现「插入序 `int[]` 替代 HashSet」；**但 `BuildMetadataRanks` 的 `identityOfEdge`（4 B/边）与 `identityIds`/`valueIds` 两个字典在元数据非零时仍会分配**（§6.1.3） |
| **P1-2** | `EdgeMetadata` + `NLCPGEdgeLabel`（幅度比 **198×**） | `NLCPGEdgeLabel` 出现/消失 7 次、峰值 63.42 MB、1,039,096 实例；累计分配 204 MiB | 元数据按值去重已在 `CanonicalEdgeStore` 做；**构图期 `PendingEdgeBuffer.InternMetadata` 的 `Dictionary<EdgeMetadata,int>` 仍是引用键** ⇒ 可评估按 id 传递 |
| **P1-3** | `InterproceduralDataFlowPlan[]`（428 B）**的剩余 `set_Capacity`/`AddWithResize`** | `refactored` 仍有 **237.32 + 178.95 = 416 MiB**（5.31% + 4.01%） | 预分配已落地但未归零 ⇒ 检查是否仍有第二处 `List` 增长（`NLCPGBuilder.cs` 的 `callSitePlans` 之外） |
| **P1-4** | `SyntaxSemanticFacts`（幅度 48.9×）+ `UsedFactRecord[]`（239,102 实例）+ `DefinitionFact[]`（幅度 38,325×） | `SyntaxSemanticFacts` 出现/消失 7 次、23.46 MB | 均为按方法/按操作重建的中间结构 ⇒ 优先 `TrimExcess`/池化，属 SOH 侧 |

### 7.3 P2 —— 已确认边界，暂不动

| 对象 | 原因 |
| --- | --- |
| `System.String`（常驻 1,520 MB、100% 出现） | 指导 §4 已判「降活对象无杠杆」（活堆仅 committed 的 2.5%）；且 `StringInterner` 是进程级 static，**属独立立项** |
| LOH 压缩 / GC 参数 | 指导 §4 反模式清单，且本次实测碎片峰值仅占 LOH 容量 3.5%~14% ⇒ **本次数据独立支持该否决** |
| `NLCPGNode[]` 的整体重排（改指纹） | 既有研究已因「改指纹」否决；本文 §7.1 P0-2 只针对 `StableAnchor` 子集，**仍需 oracle** |

---

## 8. 边界与未验证（不得当成已验证）

1. **代码版本无法从报告证明。** 四份 `run-environment.yml` 均不含 DLL 哈希/提交号。
   §4.2 的「refactored 已消除 HashSet」是**用堆中类型的存在性反推**的，**非构建产物哈希**。
   更精确的做法（未做）：`Get-FileHash Build/src/Debug/net10.0/NLCPG.dll` 与各运行时刻的 mtime 对账。
2. **trace 只覆盖前 60 s。** 四份 `alloc-*.csv` 都是 **60.0 s 窗口**。
   §4.3 的 `Entry<NLCPGEdge>[]` 915 MB 发生在 **t=4,863 s**，**不在任何 trace 窗口内**
   ⇒ 该样本只有 gcdump 证据（精确），**没有分配归因**。
3. **常驻率统计已排除饱和样本，但饱和样本本身的信息被丢弃。**
   `retest` 有 26/68 饱和（38.2%），其中可能含 LOH 构成信息，**本文未做插值或重建**。
4. **`gcdump` 采样间隔 30~180 s**，只能看到**活过采样点**的对象。
   **纯瞬态（活不过一个采样点）只能由 trace 覆盖**（口径 A），两个口径**不可互相替代**。
5. **未做任何生产代码改动、未跑任何测试。** 本文的 P0/P1/P2 是**待验证的优化对象**，
   不是已实施的修复；其收益（如 272 B→72 B、104 B→72 B）**均为解析估算，未实测**。
6. **`NLCPGNode` 移除 `StableAnchor`（实测 32 B）会改变相等语义路径**
   （`Equals`/`GetHashCode` 的 `StableAnchor` 分支），
   按指导 §5.2/§5.3 **必须先有等价性 + 顺序 oracle**，且**顺序敏感测试须零改动通过**。
   **且既有研究 `2026-09-17-nlcpg-class-struct-memory-assessment.md` 已警告**：
   Builder 的 syntax-to-node / operation-to-node / node-keyed map 使用 `ReferenceEqualityComparer`，
   **值类型副本没有稳定对象身份** ⇒ 该改动会波及身份语义，**不是单纯的字段裁剪**。
7. **元数据覆盖率（0.092%）来自本次单一多文件语料**（`D:/TRbackup/Version4`）。
   换语料或启用不同 `ICallFlowResolver` 配置会改变该值 ⇒ **收益上限随之改变**。
8. **未验证遍历/查询吞吐。** 指导 §7.5 已标注该缺口，本文未补。

---

## 9. 复现步骤

```powershell
$ws = 'D:\ProjectItem\SourceCode\Net\NL'
$base = 'D:\TRbackup\Version4\Build\NL-diagnostics-dop12'

# 循环 1：资产清点（本文 §1.2）
# 循环 3/5：分配量归因（需要 TraceAllocStats；见 Build/loh-research/TraceAllocStats）
dotnet "$ws\Build\loh-research\TraceAllocStats\bin\Release\net10.0\TraceAllocStats.dll" `
  "$base\20260924-000000-dop12-refactored\Trace\allocation.nettrace" "$ws\Build\loh-research\trace-out\20260924-000000-dop12-refactored"

# 循环 8：LOH 专属归因
dotnet "$ws\Build\loh-research\TraceAllocStats2\bin\Release\net10.0\TraceAllocStats2.dll" `
  "$base\20260924-000000-dop12-refactored\Trace\allocation.nettrace" "$ws\Build\loh-research\trace-loh\20260924-000000-dop12-refactored"

# 循环 6：宽度实测
dotnet "$ws\Build\loh-research\WidthProbe\bin\Release\net10.0\WidthProbe.dll" "$ws\Build\loh-research\data\cycle08-widths.csv"

# 循环 4：逐类型存活堆时间序列（每个运行约 25 s）
& "$ws\Build\loh-research\scripts\Analyze-TypeSeries.ps1" -RunDir "$base\20260923-201500-dop12-retest" -OutDir "$ws\Build\loh-research\data"

# 循环 9：常驻基线 vs 瞬态尖峰
& "$ws\Build\loh-research\scripts\Analyze-Resident-vs-Transient.ps1" -RunDir "$base\20260923-201500-dop12-retest" -TypePatterns @('NLCPG.Model.NLCPGNode[]','PendingEdge[]') -OutCsv "$ws\Build\loh-research\data\resident.csv"

# 循环 10：双口径合并
& "$ws\Build\loh-research\scripts\Merge-ChurnAndSize.ps1" `
  -TraceOutDir "$ws\Build\loh-research\trace-out" -GcdumpDataDir "$ws\Build\loh-research\data" `
  -OutCsv "$ws\Build\loh-research\data\cycle10-churn-size-join.csv" -WidthCsv "$ws\Build\loh-research\data\cycle08-widths.csv"

# 循环 14：LOH 阶跃/长平台
& "$ws\Build\loh-research\scripts\Analyze-LohPlateau.ps1" -RunDir "$base\20260923-201500-dop12-retest" -OutDir "$ws\Build\loh-research\data"
```

**产物落盘位置**：`Build/loh-research/`
（`data/` 分析 CSV、`trace-out/` 分配归因、`trace-loh/` LOH 归因、`scripts/` 分析脚本、
`WidthProbe/`+`LayoutProbe/`+`TraceAllocStats2/` 探针源码）。

---

## 10. 参考

**指导与直接前序**
- [LOH 大对象优化指导](../loh-large-object-optimization-guide.md) —— 本文所用的诊断流程、三类模式与全部验收口径
- [LOH 大对象清单（既有研究）](2026-09-24-loh-large-object-inventory-from-reports.md)
  —— 单运行（`20260923-130737`）的清单；**本文在其基础上扩到 4 运行并修正了两处结论**（§4.3、§6.1.3）

**本次对账过的既有性能报告**（§1.3 全清单、§1.4 逐条对账）
- [NLCPG class/struct 内存评估](2026-09-17-nlcpg-class-struct-memory-assessment.md)
  —— **预测了本次实测的宽常驻数组**（「struct 下每个数组都会再保存一份完整节点值」、边改 struct 是内存回归风险）
- [跨过程 pass 分配削减设计](../plans/2026-09-23-interprocedural-pass-alloc-reduction-design.md) /
  [执行](../plans/2026-09-23-interprocedural-pass-alloc-reduction-execution.md)
  —— `PendingEdge` 272 B、`ceil(85000/272)=313`、`Materialize` 641.4 MiB；**数字与本次实测相同**
- [边载荷序数化设计](../plans/2026-09-23-edge-payload-ordinalization-design.md) —— 构图期 120 B → 16 B
- [冻结边投影化设计](../plans/2026-09-24-frozen-edge-projection-design.md) —— 边侧 SoA 表示改造
- [Dictionary `Entry<>` 内存研究](2026-09-23-dotnet-dictionary-entry-memory-research.md) —— 容器槽几何（本次 §3.3 交叉验证）
- [C# 对象内存优化已验证方法](2026-09-22-csharp-object-memory-optimization-verified-methods.md) —— GC 参数的既有核验
- [零分配 key 设计](../plans/2026-09-23-zero-allocation-key-design.md) —— `NodeSortKey` 1,864.4 MiB（本次未复测）

**源码与测试（本次新读）**
- `tests/NLISSN.PerformanceTests/Performance/NLCPGEdgeStoragePerformanceTests.cs:31-53`
  —— `EdgeCarrierWidths_MatchDocumentedLayout`，**断言与本次 `WidthProbe` 逐项相同的宽度常量**

**外部一手来源**
- [Large object heap（Microsoft Learn）](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
  —— 85,000 B 阈值、LOH 只在 Gen2 回收、不压缩

**原始数据源**：`D:\TRbackup\Version4\Build\NL-diagnostics-dop12\{20260922-173202, 20260923-130737,
20260923-201500-retest, 20260924-000000-refactored}`
