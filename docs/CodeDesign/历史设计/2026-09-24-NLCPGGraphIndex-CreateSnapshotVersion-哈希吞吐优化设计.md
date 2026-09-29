# N3 设计文档：`CreateSnapshotVersion` 哈希吞吐优化

> AI 阅读入口：任务涉及 `NLCPGGraphIndex.CreateSnapshotVersion`、`GraphSnapshotVersion`、SHA-256 指纹、`AppendInt32`/`AppendUInt32`/`AppendString`、`FreezeQueryIndex` 耗时或快照指纹等价性时，先读本文。

## 1. 目标与边界

目标：降低 `NLCPGGraphIndex.CreateSnapshotVersion` 的 CPU 时间，**字节序列与最终 SHA-256 完全不变**。

**不做**：

- 不改变哈希算法（仍是 SHA-256）、字段顺序、长度前缀约定或小端编码；
- 不改变 `GraphSnapshotVersion` 的取值（这是对外可见的图指纹，字节不变是硬约束）；
- 不改 `CanonicalEdgeStore`、基数排序、CSR 表或 `FreezeQueryIndex` 的其余阶段；
- 不并行化（SHA-256 是有状态顺序 API，本项是**串行吞吐优化**）。

## 2. 当前事实（全部实测）

### 2.1 规模（NPC.cs，`nodom 8 16`，节点 1,599,506 / 边 7,756,984）

| 观测量 | 值 |
| --- | ---: |
| `Append*` 调用次数 | **113,636,842** |
| ↳ 定长 4 B（`AppendInt32`/`AppendUInt32`） | 83,088,176 |
| ↳ 字符串（`AppendString`） | **31,118,359** |
| 哈希总字节 | **1,553,494,108 B（1,481.5 MiB）** |
| 自身耗时 | **12,305 – 16,590 ms** |
| 每次调用均摊 | **108.3 – 146.0 ns** |

`CreateSnapshotVersion` 占 `FreezeQueryIndex`（42,084–53,041 ms）的 **约 23–33%**，占总墙钟 **约 10–15%**。

### 2.2 与 N0 同类的结构性缺陷

每个字段值都走一次独立的 `stackalloc` + 写字节 + `hash.AppendData(...)` 调用：

```csharp
private static void AppendInt32(IncrementalHash hash, int value)
{
    Span<byte> bytes = stackalloc byte[sizeof(int)];     // ← 每次调用栈操作
    BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
    hash.AppendData(bytes);                               // ← 每次调用一次哈希 API
}
```

83.1M 次定长调用中，绝大多数相邻字段本可**拼进同一个缓冲区一次性追加**。`AppendData` 的每次调用都有固定开销（108 ns × 113.6M），而 SHA-256 对 1.4 GiB 的理论耗时远低于此 ⇒ **开销在调用而不是在哈希计算**。

### 2.3 字符串路径

每条边的 `StructuredLabel?.StableKey` / `ContextId?.Value` / `CallSiteContext?.FilePath` / `DisplayName` 各一次 `AppendString`（4 个字段 ⇒ 31.1M/7.76M ≈ 4.01，与代码一致）。每次做 `Encoding.UTF8.GetByteCount` + `GetBytes` 到一个 `stackalloc byte[512]`。

### 2.4 CPU 采样交叉验证

第 3 轮 trace 中 `IncrementalHash.AppendData` 是**全 trace 最大的 Exclusive 帧**：

| 函数 | Inclusive | Exclusive |
| --- | ---: | ---: |
| `IncrementalHash.AppendData` | 1.43% | **1.43%** |
| `NLCPGGraphIndex.AppendString` | 0.77% | 0.02% |
| `NLCPGGraphIndex.CreateSnapshotVersion` | 1.41% | 0.01% |
| `NLCPGGraphIndex.AppendUInt32` | 0.30% | 0.01% |

`CreateSnapshotVersion` 自身 Exclusive 仅 0.01% ⇒ 时间全在 `AppendData`。除以 13.61% 真实工作份额：`AppendData` ≈ **10.5% 的真实工作样本**。

## 3. 设计

### 3.1 核心思路：批量编码到复用缓冲，减少 `AppendData` 调用次数

把"每字段一次 `AppendData`"改为"**攒满一个缓冲区再追一次**"。字节序列不变，只是由多次小追加合并为多次大追加。

```text
旧：AppendInt32 × 83.1M + AppendString × 31.1M  →  113.6M 次 AppendData
新：字段值顺序写入复用缓冲；缓冲满（或段落结束）时一次 AppendData  →  调用次数降 1~2 个数量级
```

### 3.2 等价性的关键约束

SHA-256 是**流式**哈希：`SHA256(x ∥ y ∥ z) == SHA256(x) → SHA256(y) → SHA256(z)`。因此只要**写入的字节序列与顺序完全一致**，把多次 `AppendData` 合并成一次，结果**逐位相同**。

必须保持的约定（来自现有实现）：

1. 整数：**小端**、固定 4 B（`BinaryPrimitives.WriteInt32LittleEndian`）；
2. `null` 字符串 → 写 `-1`（int）；
3. 非空字符串 → 先写 `UTF8 字节数`（int），再写字节；空串只写 `0` 不写字节；
4. 字段顺序与现在逐一相同。

### 3.3 缓冲区策略

- 复用一块 `byte[]`（如 64 KiB），只在 `CreateSnapshotVersion` 内使用，方法返回即弃；
- 每次写入前若剩余空间不足，先 `Flush` 再写；
- **注意**：单次写入可能超过缓冲容量（长字符串），此时需分批 `AppendData` 而不复制整串——保持"不分配临时 byte[]"这一既有优化（注释见 §2.3）。

### 3.4 预期收益

| 项 | 现状 | 预期 |
| --- | ---: | ---: |
| `AppendData` 调用次数 | 113,636,842 | 下降 1–2 个数量级 |
| `CreateSnapshotVersion` 耗时 | 12,305–16,590 ms | 需实测确认（上界 = 全免，但编码与 SHA 计算本身仍需时间） |

**收益上界**：若调用开销占 108 ns 的全部，则上界 12.3 s；但 UTF-8 编码与 SHA-256 计算不可避免，故**实际收益必然低于 12.3 s**。按 §7 教训 3，先实测改造后的真实值，不预设比例。

## 4. 验证证据（改造后实测）

### 4.1 正确性 oracle（强证据）

`NLCPGGraph.GraphSnapshotVersion`（`NLCPGGraph.cs:84`，public）是对外可见的字节级图指纹，覆盖每个节点的全部 `StableAnchor` 字段与每条边的全部元数据。

**改造前基线（同输入两次运行，已验证稳定）**：

```text
run1: 9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896
run2: 9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896   ← 逐字节相同
```

**改造后（共 7 次运行，含 4 次同二进制 A/B 交叉对照）**：**全部为 `9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896`，逐字节相同。**

> **附：一个被排除的错误 oracle。** 我先前自建的"顺序无关图指纹"（对 `NLCPGNode.GetHashCode` 异或）**在同一输入下跨运行就不稳定**：
> `06601951AD42E0F1` vs `7FD94FCA4A3E1B1D`。原因是 `NLCPGNode.GetHashCode`（`NLCPGNode.cs:45-54`）在 `StableAnchor` 存在时**直接返回锚点哈希、忽略 `NodeId`**，故该指纹几乎不覆盖 `NodeId`，且受 `HashCode.Combine` 随机种子影响。
> **结论：该自建指纹不可用作等价性判据，已改用产品自带的 `GraphSnapshotVersion`。** 记录于此避免后续重犯。

### 4.2 性能实测（同二进制交叉对照）

单次进程采样受机器负载漂移影响很大（同一改造在 5,866–7,712 ms 间波动），故采用**同一二进制内 `N3_LEGACY` 开关交替执行**的方式对照，消除构建配置与负载漂移：

| 模式 | `AppendData` 调用 | 墙钟 | 进程 CPU |
| --- | ---: | ---: | ---: |
| 改造前（LEGACY） | 113,636,842 | 13,471 / 13,070 ms | **13,375 / 13,016 ms** |
| 改造后（NEW） | **23,692** | 7,316 / 7,227 ms | **7,203 / 7,203 ms** |

**净省约 6.0 s CPU（1.83×）**，`GraphSnapshotVersion` 四次运行逐字节相同（1,551,576,960 B 哈希输入完全一致）。

`AppendData` 调用数 113,636,842 → 23,692（**减少 4,797×**），与 1,479.7 MiB ÷ 64 KiB ≈ 23,700 完全吻合，证明确实是"缓冲攒批"在生效。

### 4.3 一次被推翻的中间判断（诚实记录）

我在只拿到**单次**改造后采样（11,358 ms，恰逢高负载）时，曾判断"调用数降了 4,797× 但耗时几乎没变，故假设'开销在调用'是错的"。

**该判断是错的**，错在方法：拿**跨运行的两次单点采样**做对比，而 stage 级负载在运行间波动极大（同一改造 5,866–7,712 ms）。§4.2 的同二进制交替对照才是可靠做法，结论反转为 **1.83× 真实收益**。
这与 §7 教训 5（墙钟在未受控阶段漂移时不可用）是同一类错误，已记入 §7 教训 11。

### 4.4 未解释的余量

`sha-bench`（Release）实测：SHA-256 计算 1,481.5 MiB 仅 **1,348 ms**，逐次 UTF-8 编码 31.1M 次短串 **887 ms**，合计约 2,235 ms。改造后仍有 7,203 ms CPU，**差额约 5.0 s 未解释**。

推测方向（**未验证**，不在本项范围）：探针以 **Debug** 配置引用 `NLCPG`（`bin\Debug\net10.0\`，`Optimize` 对项目引用不生效），而 `sha-bench` 是 Release；Release 下此项收益可能更大。此边界如实记录，不据此宣称额外收益。

### 4.4.1 自审：1.83× 会不会是"计数器假象"？

**质疑**：A/B 时 LEGACY 与 NEW 两条路径都带临时计数器，且**计数次数不同**（LEGACY 每次定长写更新 3 个字段、NEW 只更新 2 个），那 5,813 ms 的差是不是计数器造成的？

**证伪实验**（`sha-bench`，Release，无任何产品计数器，输入形状取自真实统计 82,608,906 次定长 + 31,118,359 次字符串）：

| 变体 | 耗时 | 说明 |
| --- | ---: | --- |
| LEGACY，**完全无计数器** | **8,908 ms** | 纯逐字段 `AppendData` |
| LEGACY，带计数器 | 9,522 ms | 计数器自身仅贡献 **614 ms** |
| BUFFERED 4 KiB | 1,508 ms | |
| **BUFFERED 64 KiB** | **1,348 ms** | 本设计采用的容量 |
| BUFFERED 1 MiB | 1,348 ms | 与 64 KiB 持平，但白占内存 |

**结论**：

1. **即使把所有计数器都去掉，批量编码仍比逐字段快 7,560 ms**（8,908 → 1,348）——比产品 A/B 实测的 5,813 ms **还大**。故 1.83× 不是计数器假象，反而偏保守。
2. 计数器贡献上界 614 ms，且 NEW 路径的计数次数**少于** LEGACY，故被夸大的部分 ≲0.4 s（占 5.8 s 的 ≈7%），**不足以逆转结论**。
3. **64 KiB 容量经对照验证最优**：4 KiB 差 160 ms（flush 次数 197,221 vs 12,323），1 MiB 无进一步收益。原选值成立。

**顺带发现（重要，易踩）**：`stackalloc` 在**循环内不会逐次释放**（只在方法返回时释放）。编写该基准时我最初把 `stackalloc` 放进循环，8,260 万次 × 4 B **直接栈溢出**。产品原实现把每次追加放进**独立方法**（`AppendInt32`/`AppendUInt32`），正是栈安全的关键——改造后改用复用堆缓冲，反而消除了这一约束。

### 4.6 与并发工作流的共存复验（重要）

本文件 `NLCPGGraphIndex.cs` **由另一工作流同时维护**。N3 完成并验证后，该工作流于 18:55 对同一文件做了一次**大规模重构**（diff：937 增 / 172 删；`CsrEdgeTable`、`CanonicalEdgeStore`、`EdgeMetadataKey` 等一并改写），文件从 1,012 行增至 1,112 行。

**复验结果（他人改动落地后重跑）**：

| 项 | 结果 |
| --- | --- |
| 构建 | **0 错误**（2 个既有 NU1903 警告） |
| `GraphSnapshotVersion` | **`9B59A35A…E896`，仍逐字节相同** |
| N3 改动是否残留 | ✅ `SnapshotHasher` 及其 `Flush` 完好；全文件仅剩我的 2 处 `_hash.AppendData` |

**结论**：N3 的缓冲写入器与旁人的重构**可组合**——因为 N3 只改「追加切分方式」，依赖的是"写入 hash 的字节序列不变"，与索引内部数据结构无关。

**规矩（补 §7 教训 4）**：在并发工作流下改热点文件，**验证不是一次性的**。他人后续改动落地后必须**重新构建 + 重跑等价性 oracle**，否则"已通过"会随别人的提交失效。本项因此保留了 §4.2 的基线指纹作为**长期回归判据**。

### 4.5 构建与测试

```powershell
dotnet build .\src\NLCPG\NLCPG.csproj                                    # 0 错误（2 个既有 NU1903 警告）
dotnet test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj `
  --no-restore -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName~Snapshot|FullyQualifiedName~Freeze|FullyQualifiedName~GraphIndex|FullyQualifiedName~ShardingEquivalence"
```

## 5. 风险与未验证边界

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| 字节序列改变 ⇒ 指纹变化（破坏对外契约） | **高** | 以 `GraphSnapshotVersion` 逐字符比对为硬门禁 |
| 长字符串跨越缓冲边界处理错误 | 中 | 单独覆盖 >64 KiB 字符串路径 |
| 缓冲区复用引入跨调用污染 | 中 | 缓冲只在方法内创建，返回即弃 |
| `stackalloc` 改 `byte[]` 增加 GC 压力 | 低 | 单块复用缓冲，反而减少栈操作 |

未验证边界：

- 未在真实 967 文件语料上复测；
- 未验证 `Create` 其余阶段（基数排序 / CSR 建表）的占比细分；
- `FreezeQueryIndex` 并行化（真正的多核收益）仍待 N3-b，本项只降低串行成本。

## 6. 相关

- 总体方案与十轮记录：`Build/10pass-optimization-plan.md`
- 执行计划：`Build/N3-执行计划.md`
- N0（同类"每元素无效开销"先例）：`设计docs/历史设计/2026-09-24-DataFlowPass-DefinitionFactIndex-候选查找优化设计.md`
- 性能分析组件：`目前设计/性能分析组件.md`
