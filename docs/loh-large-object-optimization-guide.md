# LOH 大对象优化指导

**状态：** 现行指导（2026-09-24）。
**适用对象：** 本仓库中任何「托管堆高位常驻、撞物理内存墙」的问题。
**证据基础：** 冻结边投影化一役的实测数据（[设计](plans/2026-09-24-frozen-edge-projection-design.md) /
[执行](plans/2026-09-24-frozen-edge-projection-execution.md)）与既有的诊断记录
（[Version4 DOP12 诊断](benchmarks/nlissn-version4-dop12-diagnostics.md)）。

---

## 0. 结论先行

1. **LOH 的高位常驻由「常驻表示的宽度」决定，不由瞬态峰值的高度决定。**
   实测反证：既有瞬态优化 P0#1 把瞬态降了 **−80.4%**，同一时刻 LOH 只降 **−2.1%**，
   且 `System.String` 几乎不动（1,454.4 → 1,448.4 MB）。
   瞬态只决定**每个锯齿的高度**，常驻决定**基线**——而 committed 里留下的是基线。

2. **可用的手段只有两类：降「宽度」、去「复制」。** 其余都是对症。

3. **顺序不能反**：先量宽度 → 再找重复 → 最后换表示。
   跳过前两步直接换数据结构，是本仓库历史上多次踩空的模式。

4. **"调 GC 参数"不是本指导的路径**（§4）。`GCConserveMemory` / `GCHighMemPercent`
   在**显式强制压缩回收**的受控探针下无额外效果；`GCHeapHardLimit` 会让进程直接死。
   ⚠️ **作用域已收窄（2026-09-26）**：该"无效"结论仅适用于**手动反复强制
   `GC.Collect(compacting)`** 的场景。在**真实产品负载、不做手动 GC** 的自然回收下，
   `GCConserveMemory=7` 实测工作集峰值 **−19.2%**（区间不重叠，每侧 n=3）。
   两者不矛盾，详见 [GC 内存总量配置](gc-conserve-memory.md) §5。

5. **本指导不承诺降低端到端 LOH 峰值。** 它承诺的是可验证的一件事：
   把**常驻表示的每边/每元素字节**降下来（案例中实测 **−92.35%**，容器级口径）。

---

## 1. 机制前提：为什么 LOH 这么难降

先建立正确的心智模型。以下每一条都是本仓库或官方文档的实测/原文，不是推测。

### 1.1 阈值与回收规则

- 对象 **≥ 85,000 B** 进入 LOH（官方原文："the GC divides objects into 2 categories:
  small objects (< 85,000 bytes) and large objects (>= 85,000 bytes)"）。
- LOH **只在 Gen2 回收**，且**默认不压缩**。

⇒ 一个数组一旦跨过阈值，它的回收代价就被绑到最昂贵的那一档 GC 上。

### 1.2 临界元素数（**按 `ceil(85000 / 宽度)` 计算**）

这张表是判断"某个数组会不会进 LOH"的最快工具。本仓库实测的宽度：

| 元素类型 | 实测宽度 | LOH 临界元素数 |
| --- | ---: | ---: |
| `int` | 4 B | 21,250 |
| `PendingEdgeKey` | 16 B | 5,313 |
| `NLCPGEdge` | **72 B** | **1,181** |
| `NLCPGNode` | **104 B** | **818** |
| `PendingEdge` | **272 B** | **313** |
| `InterproceduralDataFlowPlan` | **428 B** | **199** |

> 宽度全部由 `Unsafe.SizeOf<T>()` 实测，不是按字段推导。
> ⚠️ **教训**：本仓库曾把 `InterproceduralDataFlowPlan` 按"4 refs + 3 int"**推导**为 48 B，
> 实际是 **428 B**（因为 `NLCPGNode` 是 104 B 的 `record struct` 而非引用）——
> 由此导致的元素数估算**高估了约 8.9 倍**。**永远实测，不要推导。**

### 1.3 决定性观测：LOH 是「阶跃 + 长平台」

这是最反直觉、也最关键的一条实测：

- 866 个样本中最长的「数值完全不变」区间是 **13,749 MB，持续 1,152 s（19.2 分钟）**，
  期间**跑了 7 次 Gen2 GC 一动不动**。
- 同期活对象极小：`0083`(t=4,742) 活 LOH 209 MB / **活堆仅 450 MB**，
  而 LOH 段容量 13,749 MB ⇒ **30.6 倍**。
- 碎片只有 **993 MB**，而 committed 是 **18,558 MB**。
- 活堆全域仅 **471.7 MB**，占 committed 的 **2.5%**。

**⇒ 机制三段式：**

```text
① 大文件产生巨量瞬态（NPC.cs 6 数组 8,538 MB）把 LOH 顶到高位
② 之后进入低分配期，但「已提交的段容量不还」⇒ private 恒超物理内存
③ 换页使 CPU 空转、运行变慢，又把低分配期拉长 ⇒ 平台持续 19.2 分钟
```

这条同时解释了那个著名的悖论：**「分配降 32% 而 LOH 不变」**——
**决定 LOH 的是最大单次瞬态，不是累积分配。**

### 1.4 由此推出的三个判断

| 观测 | 正确解读 | 错误解读（已犯过） |
| --- | --- | --- |
| LOH 13,749 MB 长期不降 | 已提交段容量未归还，靠 GC 参数压不动 | "存在内存泄漏" |
| 碎片仅 993 MB | 压缩不是主通路 | "LOH 碎片导致膨胀" |
| 活堆 471.7 MB | 降活对象没有杠杆 | "少建对象就能降 LOH" |

---

## 2. 诊断流程

按顺序执行。每一步都有明确产出，不要跳步。

### Step 1 —— 确认撞的是 LOH，不是托管堆

看 `Counters/` 里 `LOH heap.size` 与 Gen2 与 committed 的**同刻**关系。

⚠️ **口径纪律**：必须用**严格同 timestamp** 的值。本仓库曾把 LOH 峰值（20:58:27）
与 2 秒之前的 WS（20:58:25）当作"同刻"对比，结论方向虽不变但数字错了。

### Step 2 —— 分清「常驻基线」与「瞬态尖峰」

判据：**锯齿回落后的地板值**是常驻基线，**锯齿高度**是瞬态。

只有基线才是 LOH 高位常驻的成因。压尖峰是无效功（§0 第 1 条）。

### Step 3 —— 量出每一项的宽度与复制系数

对"常驻基线"里的每个大数组，量三件事：

1. **宽度**：`Unsafe.SizeOf<T>()` —— 实测，不推导。
2. **复制系数**：同一批逻辑数据在**几个常驻容器**里各存一份。
3. **放大倍数**：实际字节 ÷ 纯信息字节。

案例实测（Projectile.cs，样本 `0087`，6,953,008 边）：

| 组件 | 实测字节 | 每边 |
| --- | ---: | ---: |
| `Entry<NLCPGEdge>[]`（`HashSet` 的 entry 数组） | 959,915,944 | 138.06 |
| `NLCPGEdge[]`（canonical 数组） | 500,616,600 | 72.00 |
| **合计** | **1,460,532,544** | **210.06** |
| 纯端点信息（src+tgt+kind = 12 B） | 83,436,096 | 12.00 |
| **放大倍数** | | **17.5×** |

**复制系数 2.92×**（= 1,460,532,544 / 500,616,600 = 2.9175）；
**相对纯信息放大 17.5×**（= 210.06 / 12）。

**槽几何必须闭合**（这是验证测量正确性的硬手段）：
`(959,915,944 − 24) / 80 = 11,998,949` **整除** ⇒ `Entry<NLCPGEdge>` = 80 B，
与 `hashCode(4) + next(4) + payload(72) = 80` 精确吻合；两处**残差均为 0 B**。

### Step 4 —— 判定哪些字节是「死」的

**死字节** = 类型定义了、内存占着、但生产路径**从不填充**的字段。

案例实测：

| 字段 | 宽度 | 生产环境覆盖 |
| --- | ---: | --- |
| `SourceNodeId` + `TargetNodeId` + `Kind` | 12 B | ✅ 恒定填充 |
| `StructuredLabel` | 8 B | 峰值 17,026 实例 vs 20,723,806 边 = **0.08%** |
| `ContextId` | 16 B | 稀疏 |
| `CallSiteContext` | 32 B | **全部 69 份 gcdump 中实例数为 0** |

⇒ **72 B 中 56 B = 77.8% 是死字节**。独立对拍 oracle 亦证实真实单文件语料
**边元数据覆盖 = 0.0%**。

⚠️ **诚实边界**：覆盖率为 0 来自**单文件语料**。元数据非 0 需多文件联合编译 +
`ICallFlowResolver`。故**正确性不依赖**"元数据恒为 0"，只有**收益上限**依赖它。

### Step 5 —— 只改表示，不改语义

换表示时守住三条不变量：**语义不变、顺序不变、指纹不变**。
违反任何一条都会被既有契约测试抓住（这是好事，见 §5）。

---

## 3. 三类可复用模式

### 3.1 模式 A：SoA 序数表示 + 延迟投影 ★ 主模式

**适用信号**：定宽值类型数组常驻，且其中大部分字节是"类型上必有、值上恒空"的字段。

**做法**：把「一个宽值类型数组」拆成「几个窄平行数组」+「稀疏侧表」，
查询时**按需投影**成原值类型。

```csharp
// 常驻边存储：结构化数组（SoA）+ 稀疏元数据池，实测 9~13 B/边
private readonly int[]  _sourceOrdinals;
private readonly int[]  _targetOrdinals;
private readonly byte[] _kinds;

// 元数据稀疏侧表：覆盖率为 0 时为 null，此时不分配任何元数据结构
private readonly int[]? _metadataIds;
private readonly EdgeMetadataEntry[]? _metadataPool;

// 投影：readonly record struct ⇒ 零堆分配
internal NLCPGEdge Project(int canonicalIndex) =>
    NLCPGEdge.CreateProjected(
      _orderedNodes[_sourceOrdinals[canonicalIndex]].NodeId!.Value,
      _orderedNodes[_targetOrdinals[canonicalIndex]].NodeId!.Value,
      (NLCPGEdgeKind)_kinds[canonicalIndex],
      /* 元数据经 metadataId 查池 */);
```

**为什么这是主模式**：
- `NLCPGEdge` 声明为 `public readonly record struct` ⇒ 投影**零堆分配**。
- 宽度从 **72 B/边 → 13.01 B/边**（实测口径见 §3.4）。
- `byte[]` 在此**真正有效**（枚举 36 个取值 < 256）。
  ⚠️ 注意：把 `Kind` 从 `int` 收窄成 `byte` 在**原来的 72 B 结构体里省 0 字节**
  （对齐填充吃掉），**只有在 SoA 里才兑现**——这是"换表示"而非"换字段"的典型例证。

**代价**：随机访问变成 N 次数组读 + 1 次结构体构造。本仓库的结论是**可接受**，
但**遍历/查询吞吐未单独对拍** ⇒ 标记为未完全验证。

---

### 3.2 模式 B：元数据侧表（**警惕 "intern 后重新膨胀"**）

这是本仓库最有价值的一条通用教训。

**反模式长这样**：流水线在**前一段**已经做对了 interning，却在**后一段**又把它展开：

| 阶段 | 表示 | 每边 | 状态 |
| --- | --- | ---: | --- |
| 构图期 `PendingEdgeBuffer` | `PendingEdgeKey`（元数据 intern 成 4 B id） | **16 B** | ✅ 已实现 |
| `FreezeQueryIndex` → 常驻 | `NLCPGEdge`（元数据**内联展开**） | **72 B** | ❌ 问题所在 |

⇒ **恰好在内存最紧的时刻丢掉了 interning。**

**可操作的自检**：在 pipeline 的任何阶段边界问一句——
**"上一段用的是紧凑形式，这一段是不是又变宽了？"**

**修复**：元数据按**值**去重后分配 id，边只存 id；覆盖率为 0 时**整个池不分配**
（`_metadataIds == null` ⇒ 连 `int[]` 都不存在）。

---

### 3.3 模式 C：删除重复载体

**适用信号**：同一批逻辑数据在 ≥2 个常驻容器里各存一份。

**案例**：`HashSet<NLCPGEdge>`（80 B/槽）与 canonical `NLCPGEdge[]`（72 B/边）
**同时常驻**，实测复制系数 **2.91×**。

**关键手法——用「序数表」替代「集合」**：若集合是"只增不删"的，其枚举序**即插入序**，
故可用一个 `int[]` 记录顺序，把集合删掉：

```csharp
// 插入序 -> canonical 序（即置换的逆）。原先常驻的 HashSet 按插入序枚举，
// 该数组以 4 B/边复现同一枚举序，从而取代 80 B/槽的 HashSet。
// 复用 buffer 这块已分配的 scratch（此后不再被读），避免再分配一个 int[edgeCount]。
var insertionOrder = buffer;
for (var canonicalIndex = 0; canonicalIndex < current.Length; canonicalIndex += 1)
{
    insertionOrder[current[canonicalIndex]] = canonicalIndex;
}
```

**这条手法有两个额外红利**：
1. **复用 scratch 缓冲** ⇒ 换表示**不新增任何分配**。
2. **枚举序逐一相同** ⇒ 顺序敏感的既有测试**零改动通过**（§5.3）。

**收益（实测整体常驻降幅）**：

| 阶段 | 常驻/边 | 整体降幅 |
| --- | ---: | ---: |
| 改造前 | 80 B/槽 + 72 B/边 | — |
| 仅换 canonical 存储（模式 A） | 80 B/槽 + 9 B/边 | 26.07% |
| **再删 HashSet（模式 C）** | **4 B/边 + 9 B/边** | **92.35%** |

⇒ **模式 A 只拿到 26%，模式 C 才兑现了剩下的大头。**
单独用模式 A 就宣布"已优化 LOH"是**过早收工**。

### 3.4 三个模式合起来的口径表

**A. store 自身**（模式 A + B）：

| 口径 | 每边 | 相对 210.06 |
| --- | ---: | ---: |
| 纯端点信息（理论下界） | 12 B | −94.3% |
| store：3 列（src + tgt + kind） | **9.00 B** | −95.7% |
| store：3 列 + 元数据侧表 | **13.01 B** | −93.8% |

**B. 整体常驻**（再加模式 C 的插入序表 4.00 B/边）：

| 组合 | 每边 | 相对 210.06 |
| --- | ---: | ---: |
| store（无元数据）+ 插入序表 | **13.01 B** | −93.8% |
| store（有元数据）+ 插入序表 | **17.01 B** | −91.9% |

> ⚠️ **注意两处 13.01 B 是不同组成，不要混用**：
> "store 有元数据（13.01）"与"store 无元数据 + 插入序表（9.00 + 4.00 ≈ 13.01）"
> 数值相近纯属巧合。判断收益时必须说明**是哪一个口径**。

**受控实测输入**：2,000 节点 / 50,000 边 → 去重后 **18,000 边**。
原始 3,061,956 B → 234,096 B；旧 `HashSet` 槽位/桶 = 21,023 / 21,023（80 B/槽）。

---

## 4. 反模式清单（每条都有实测/来源，不要重复提）

| 方案 | 否决理由 |
| --- | --- |
| `GCHeapHardLimit` | 官方定义是「GC 堆的最大**提交**大小」，超限走 OOM。姊妹运行 `20260923-130737` 死于 `0x80131506`（`COR_E_EXECUTIONENGINE`） |
| `GCConserveMemory` / `GCHighMemPercent` | 在**显式强制压缩回收**的受控探针下**无额外效果**（探针 `GcReturnProbe` 已手动 `GC.Collect(compacting)` 12 次，构成天花板效应）；且 GC 暂停占空比已达 **45.39%**，进一步换页代价不可接受。⚠️ **作用域已收窄**：真实负载自然回收下 `ConserveMemory=7` 实测 −19.2%，见 [GC 内存总量配置](gc-conserve-memory.md) §5 |
| LOH 压缩 | 碎片仅 **993 MB**，而 committed **18,558 MB** |
| 调高 `GCLOHThreshold` | 方向相反（只让**更多**对象进 LOH） |
| 降并发 | **已实测无效**（真实并发 ≈ 0.70） |
| 减少活对象 | 活堆仅 **471.7 MB** = committed 的 **2.5%** |
| 压缩瞬态峰值 | 天花板由常驻**宽度**决定（§0 第 1 条） |
| `FrozenDictionary` | 已实测为**回归**（104 B 键下 254.6 → 401.8 B/条） |
| 重写哈希表 | ≤7%。治不了 17.5× 放大 |
| 仅用 64 位哈希代替存 key | P ≈ 1/8.4M ⇒ **静默丢边** |
| 在 72 B 结构体内收窄 `Kind` 为 `byte` | **省 0 字节**（对齐填充）。只有在 SoA 里才有效（§3.1） |
| 固定常量预分配 | 预设容量只有**精确等于**实际需要时才划算，否则与"降 LOH"相反 |
| 只删集合、不换存储 | 只省一份 72 B，其余仍在；风险/收益比劣于换表示 |

**通用教训**：**"不要用调参解决表示层缺陷"**。
LOH 高位是「已提交的段容量」，而段容量由**活对象的宽度**乘出来。

---

## 5. 验证方法

LOH 优化的验证**不能用端到端峰值当门槛**——它受机器、peer、换页影响，不可复现。
本仓库采用的验收口径是**容器级字节**。

### 5.1 字节验收（主口径）

在**固定的受控小输入**上，从容器结构直接算字节，并把它写成**断言**（而非只打日志）：

```csharp
// 整体常驻降幅必须显著高于中间阶段的 26%：
// 两侧都换成按边计的紧凑数组（9 B/边 + 4 B/边），而旧表示是
// HashSet（80 B/槽，含装填因子放大）+ 72 B/边 canonical 数组。
Assert.True(
  reduction >= 80d,
  $"expected >=80% overall resident reduction, got {reduction:F2}%");
```

同时**重建**被删掉的旧表示来对照，保证对比是诚实的：

```csharp
// 旧表示里的 HashSet 已删除，故由投影出的边【重建】一个同元素 HashSet
// 来测量其槽位容量——同一批元素、同一比较器，容量与旧代码实际持有的相同。
var legacySet = new HashSet<NLCPGEdge>(canonical);
```

### 5.2 等价性测试（护住语义）

换表示必须证明**投影结果与原值逐字段一致**，且**顺序**与**指纹**不变。
案例新增 7 个用例，覆盖：边集合等价、边数守恒、快照版本确定性且**有区分度**、
邻居查询顺序、带元数据时逐字段等价、零元数据时不分配元数据池、常驻宽度对比。

⚠️ **测试强度必须有护栏**，否则退化成恒真：

```csharp
// 输入必须真的产生这么多边，否则本测试会退化成恒真的空断言。
Assert.Equal(240, expected.Length);
```

本仓库曾因生成器太弱（期望 120 条、实际只有 15 条被去重成 15 条）而**差点**通过一个
无意义的断言。

### 5.3 顺序敏感的既有测试必须原样保留

**不要为了让新实现通过而重定基线。** 先问："能否用保序设计让它**零改动**通过？"
案例中模式 C 的 `int[]` 正好做到了这点，于是：

- `CpgInterproceduralEdgeOrderTests`（硬编码**非单调**基线 `273,277,288,284,286`）✅ 零改动通过
- `PendingEdgeOrdinalizationDeterminismTests`（声明测插入序）✅ 零改动通过
- 顺序敏感关键回归 **79/79**

### 5.4 变异验证（**必做，且本仓库有重要发现**）

等价性测试**通过**不等于它**有效**。故意破坏生产代码，确认被捕获，再还原。

案例做了两处变异：

| 变异 | 被谁捕获 |
| --- | --- |
| ① 插入序表写成正置换（未取逆） | `CpgInterproceduralEdgeOrderTests` **1 个失败** |
| ② `Project()` 内 source/target 互换 | `FrozenEdgeProjectionEquivalenceTests` **2 个失败** |

**⭐ 重要发现（务必记住）**：变异 ① **只**被 `CpgInterproceduralEdgeOrderTests` 捕获，
而 `PendingEdgeOrdinalizationDeterminismTests` **未捕获**——因为后者比较的是
"同一次构建 vs 另一次构建"，两侧**同步位移**后仍然自洽，故它对"插入序表写错"是**盲**的。

**⇒ 护住边序的真正护栏是那个硬编码非单调基线的测试。后续再改边序相关代码，必须保留它。**

**变异后必须证明还原干净**：

```powershell
Select-String -Path <改动文件> -Pattern 'MUTATION-T8'   # 应为 0 残留
```

⚠️ **注意**：用 `MUTATION` 做模式会误命中注释里的 `perMUTATION`（`permutation`）。
必须用**唯一**标记（如 `MUTATION-T8`）。

---

## 6. 案例：冻结边投影化（完整走一遍）

**目标**：把冻结后常驻的边表示从实测 **210.06 B/边** 降下来。

| 步骤 | 产出 |
| --- | --- |
| Step 1 确认撞 LOH | private 峰值 **21,551 MB** = 物理 13.86 GiB 的 1.52 倍；LOH 峰值 14.17 GiB |
| Step 2 分基线与尖峰 | LOH 呈锯齿+**19.2 分钟平台**；瞬态优化只降 LOH **−2.1%** ⇒ 基线是常驻 |
| Step 3 量宽度与复制 | 210.06 B/边；`Entry[]` **80 B/槽**；复制系数 **2.91×**；放大 **17.5×** |
| Step 4 判死字节 | 72 B 中 **56 B = 77.8%** 从不填充（`CallSiteContext` 69 份 gcdump 中 0 实例） |
| Step 5 换表示 | 模式 A（SoA）+ 模式 B（元数据侧表）+ 模式 C（删 HashSet） |

**第一原因（一句话）**：

> 72 字节定宽边值同时充当「去重单位」和「查询单位」，并在 ≥2 个常驻容器里被复制，
> 而这 72 字节里有 56 字节可证从不被填充。

**改动**（4 个阶段，全部落地）：
1. 新增 `CanonicalEdgeStore`（SoA + 稀疏元数据池）
2. `CsrEdgeTable` / `OrdinalEdgeList` / `CreateSnapshotVersion` 切投影
3. 删除常驻 `HashSet<NLCPGEdge>`，改由插入序 `int[]`（4 B/边）投影
4. `Create()` 不再物化 `orderedEdges`；`AssignDeterministicNodeIds` 不再物化
   `NLCPGEdge[]`（72 B/边）中间数组 ⇒ **峰值同步下降**

**结果（受控小批量实测）**：

| 指标 | 值 |
| --- | ---: |
| `sizeof(NLCPGEdge)` | 72 B |
| store 每边（无元数据） | **9.00 B** |
| store 每边（有元数据） | **13.01 B** |
| canonical 分量降幅 | **87.49%** |
| **整体常驻降幅** | **92.35%** |

**验证**：性能 3/3 · 等价性 7/7 · 顺序敏感回归 79/79 · `UnitTests` 117/117 ·
`ContractTests` 499/500（唯一失败为 peer 改 `.csproj` 引入，与本次无关）·
`HostTests` 327/330（3 个为已记录既有失败）· 变异验证 2/2 捕获 ·
`check-harness-consistency.ps1` OK · `git diff --check` exit 0。

**副作用（正面）**：`FreezeQueryIndex` 占 CPG 构建 CPU 的 **80%**
（去重后 `sum(FreezeMs) = 2,937,333 ms = 49.0 min` vs `sum(BuildMs) = 3,677,472 ms`），
其主工作是复制与哈希 ⇒ 预计**同步下降**（**未实测**，不作为承诺）。

---

## 7. 边界：本指导不解决什么

1. **不降低端到端 LOH 峰值。** 案例证明的是**常驻宽度**从 210.06 B/边降到 13.01 B/边；
   `21,551 MB` 私有峰值与 `14.17 GiB` LOH 峰值**未解决**，也没有在完整语料上重测。
2. **不解决 LOH 的"段容量不归还"机制。** §1.3 的三段式里，②③ 属 GC/OS 行为，
   本指导只压①的高度与常驻宽度。
3. **不适用元数据密集的场景。** 模式 A 的收益上限依赖元数据稀疏；
   元数据密集时退化到 17.01 B/边（仍有 −91.9%，但不再是 9 B）。
4. **需要真实的多文件语料才能确认覆盖率。** 单文件语料实测 0.0%，
   非 0 需 `ICallFlowResolver` 配置。
5. **未验证遍历/查询吞吐。** 投影把随机访问变成多次数组读；延迟影响**未单独对拍**。
6. **不启用内存有界架构。** shard / streaming / persistence 路径（`NLCPGBuilderOptions.Persistence`、
  `CpgPersistenceOptions.StreamingMode`、`CpgFrozenShardStore`）在 `src/` 中**从未接线**，
  是**架构级独立立项**，不属于本指导范围。

---

## 8. 速查清单

优化 LOH 大对象时逐项确认：

- [ ] 用**同 timestamp** 的值确认撞的是 LOH，不是托管堆
- [ ] 区分**常驻基线**与**瞬态尖峰**（看锯齿地板）
- [ ] 每个大数组的宽度用 `Unsafe.SizeOf<T>()` **实测**，不推导
- [ ] 算 `ceil(85000 / 宽度)`，确认哪些数组进 LOH
- [ ] 数**复制系数**：同一批数据存了几份
- [ ] 算**放大倍数**：实际字节 ÷ 纯信息字节
- [ ] 找**死字节**：类型上必有、生产上恒空的字段
- [ ] 检查 pipeline 阶段边界有没有 **"intern 后重新膨胀"**
- [ ] 换表示时守住：语义不变、顺序不变、指纹不变
- [ ] 顺序敏感的既有测试**零改动**通过（否则先用保序设计）
- [ ] 字节验收写成**断言**，并**重建旧表示**做诚实对照
- [ ] 测试强度有**护栏断言**（否则会退化成恒真）
- [ ] 做**变异验证**，并证明还原干净（用唯一标记）
- [ ] 声明边界：**不声称**降低了端到端峰值

---

## 9. 参考

- [冻结边投影化与常驻宽度收敛设计](plans/2026-09-24-frozen-edge-projection-design.md) —— 本指导的**主案例**
- [冻结边投影化执行计划](plans/2026-09-24-frozen-edge-projection-execution.md) —— 分阶段执行与验证记录
- [边载荷序数化与稀疏属性侧表设计](plans/2026-09-23-edge-payload-ordinalization-design.md) —— **前序**：构图期 120 B → 16 B
- [Version4 DOP12 诊断](benchmarks/nlissn-version4-dop12-diagnostics.md) —— LOH 撞墙的完整实测
- [GC 内存总量配置](gc-conserve-memory.md) —— 三个入口固化的 `ConserveMemory=7`；含与 §4「无效」结论的作用域辨析
- [C# 对象内存优化已验证方法](research/2026-09-22-csharp-object-memory-optimization-verified-methods.md) —— GC 参数与方案的既有核验
- [Dictionary `Entry<>` 与边缓冲内存优化研究](plans/2026-09-23-dictionary-edge-memory-optimization-research.md) —— 事实基础与 9 项否决
- [边集合存储与去重研究](research/2026-09-23-edge-set-storage-and-dedup-research.md) —— LOH 阈值与分块讨论
- [Large object heap（Microsoft Learn）](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
  —— 85,000 B 阈值、LOH 只在 Gen2 回收、不压缩、碎片
- [garbage-collection.md（dotnet/runtime）](https://raw.githubusercontent.com/dotnet/runtime/main/docs/design/coreclr/botr/garbage-collection.md)
  —— 阈值原文
