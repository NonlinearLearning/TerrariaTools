# NLCPGGraphIndex.BuildMetadataRanks 身份去重失效修复设计（N3-d）

- 状态：设计完成，待实施
- 主题：`BuildMetadataRanks` 阶段 A（引用身份去重）恒不生效
- 关联代码：`src/NLCPG/Model/NLCPGGraphIndex.cs`
- 上游文档：`Build/10pass-optimization-plan.md` §6.2.4（N3-d）、grilling Q20/Q21

## 1. 目标

修复 `BuildMetadataRanks` 的阶段 A，使其**真正压缩**元数据候选集，
从而消掉阶段 B 的 3,954,144 次昂贵探针，且**最终元数据秩逐位不变**。

## 2. 已测事实

语料：`D:\TRbackup\Version4\Terraria\NPC.cs`（单文件）
日志：`Build/verify-pass-optimization/identity-decomp-npc.log`、`str-ref-npc.log`、`invariant-npc.log`

### 2.1 阶段分解与压缩实测

| 量 | 值 | 说明 |
| --- | ---: | --- |
| 总边数 | 7,756,984 | |
| 元数据边数 | **3,954,144** | 标签/ContextId/调用点任一非空 |
| 元数据自由边数 | 3,802,840 | 三键全空 |
| 标签：不同**实例** | 4,240 | 标签**已被共享** |
| 标签：不同**值** | 3 | |
| ContextId 不同值 | **2,384** | |
| 调用点不同值 | **2,384** | |
| 调用点不同引用元组 | 2,384 | |
| **阶段 A 身份数** | **3,954,145** | |
| **阶段 B 值数** | **4,241** | |

### 2.2 关键结论（算术即证明）

```
3,954,145 = 1 + 3,954,144
             ↑   ↑
             │   └── 每一条元数据边各占 1 个身份（1:1，零压缩）
             └────── 3,802,840 条自由边全部塌缩为同一个"全 null"身份
```

⇒ **阶段 A 对元数据边完全没有压缩**：`3,954,144 → 3,954,144`。
它唯一的实际作用是把 3,802,840 条自由边合成 1 个身份。

### 2.3 唯一熵源：`ContextId.Value` 的字符串引用

| 字符串 | 不同**引用**数 | 不同**值**数 | 是否共享 |
| --- | ---: | ---: | --- |
| `CallSiteContext.FilePath` | 1 | 1 | ✅ |
| `CallSiteContext.DisplayName` | 310 | 310 | ✅ |
| **`ContextId.Value`** | **3,954,144** | 2,384 | ❌ **每条边一个新字符串** |

`NLCPGEdge` 构造函数（`NLCPGEdge.cs:11`）：

```csharp
var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
```

`ToContextId()`（`NLCPGCallSiteContext.cs:13`）每次插值：

```csharp
new NLCPGContextId($"callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}");
```

而 `EdgeMetadataKey` 的**身份**比较器用 `RuntimeHelpers.GetHashCode` 取字符串引用哈希
（`NLCPGGraphIndex.cs:214`）⇒ 每条边的 `ContextId.Value` 引用都不同 ⇒ 身份必然互异。

### 2.4 判据 B：修复后身份数会降到多少（与负载无关）

用"调用点存在时不再比较 `ContextId` 引用"的比较器重跑一遍去重计数：

```
★ 判据B（不看 ContextId 引用）身份数=4,241   vs 现状=3,954,145
```

⇒ 修复后阶段 A 的身份数 **3,954,145 → 4,241**，与阶段 B 的值数 **完全相等**。
即阶段 A 直接达到"全值去重"的压缩效果，阶段 B 退化为一次 4,241 规模的校验。

### 2.5 判据 C：等价性的前置不变式（实测 0 违反）

```
★ 判据C 不变式违反数（ContextId 必须等于调用点派生值）=0
```

即对全部 3,954,144 条带调用点的边：
`edge.ContextId.Value == edge.CallSiteContext.ToContextId().Value` 恒成立。

该不变式由构造函数**强制**（`NLCPGEdge.cs:12-18`）：调用点与显式 `contextId` 同时给出时，
若 `contextId.Value != resolvedContextId` 则抛 `ArgumentException`。

## 3. 根因

阶段 A 的**键包含一个"每边新分配"的派生字符串**，却用**引用相等**去比较它。

- `ContextId` 在调用点存在时**不是独立信息**——它是 `CallSiteContext` 的纯函数。
- 于是"比较 `ContextId` 引用"这一项在调用点存在时**不提供任何额外区分能力**，
  却把本可共享的身份全部拆散。

这与 N3-c 的形态一致：**去重键里混入了一个恒不区分的成分**（N3-c 是 `PendingEdgeBuffer`
里恒不命中的整条边去重）。区别是 N3-c 的成分**多余**（删除后无损失），
本轮的成分**有害**（它主动破坏压缩）。

## 4. 设计

### 4.1 改法（最小改动）

`EdgeMetadataIdentityComparer`（阶段 A 专用）改为：

- 调用点**存在**时：只比较**调用点引用元组**（`FilePath` 引用 + `SpanStart` + `SpanEnd` + `DisplayName` 引用），
  **不再比较 `ContextId` 引用**；哈希同理（打一个 presence 标记 + 调用点引用元组）。
- 调用点**不存在**时：沿用原逻辑，比较 `ContextId` 引用。

### 4.2 为什么安全（等价性论证）

**引理 1（引用相等 ⇒ 值相等）**：`NLCPGEdgeLabel` 不可变；`string` 引用相等即值相等；
`SpanStart`/`SpanEnd` 是值类型。故引用元组相等 ⇒ 6 个值键全相等。

**引理 2（派生 ContextId 一致）**：引用元组相等 ⇒ `ToContextId()` 的 4 个输入全相等
⇒ 派生字符串值相等。由**判据 C**（0 违反），`EdgeMetadataKey.ContextId` 恰为该派生值。
⇒ 值比较器中 `ContextId` 一项也必相等，不会误合。

**引理 3（阶段 A 变粗不影响最终秩）**：阶段 A 只负责产出**候选集**，最终秩由阶段 B 的
值去重 + `CompareMetadata` 全序排序决定。`Array.Sort` 的比较器以 `left.CompareTo(right)`
补齐为**严格全序**（`NLCPGGraphIndex.cs:640-646`），故排序结果与输入顺序无关；
`valueKeys` 的**集合**与阶段 A 无关（引理 1、2 保证阶段 A 只合并值相等的键）。
⇒ 改变阶段 A 的粒度不改变 `rankOfValue`，也就不改变 `ranks`。

**结论**：本改动等价，且不会改变 `GraphSnapshotVersion`。

### 4.3 为什么"修 4a"优于"跳过 4a 直接值去重"

GQ21 曾提出"能否跳过 `4a`"。实测否：

| 方案 | 全程代价 |
| --- | --- |
| 跳过 4a，直接值去重 | 3,954,144 × 值比较（含 `StableKey` 插值 + 3 次字符串哈希）≈ 全量昂贵探针 |
| **修好 4a 再值去重** | 3,954,144 × 引用哈希（廉价）+ **4,241** × 值比较（昂贵部分降到 0.1%） |

阶段 A 的"按引用粗去重、零字符串工作"的思路本身**是对的**，错的是它的键。
故**修键**，不**弃阶段**。

## 5. 等价性对照表

| 维度 | 改前 | 改后 | 等价理由 |
| --- | --- | --- | --- |
| 阶段 A 身份数 | 3,954,145 | **4,241** | 判据 B |
| 阶段 B 值数 | 4,241 | 4,241 | 引理 1/2：阶段 A 只合并值相等的键 |
| 阶段 B 值**集合** | 4,241 个值 | 同 | 同上 |
| `rankOfValue` | 由值集合决定 | 同 | 引理 3（严格全序，与输入序无关） |
| `ranks[]` | — | 逐位相同 | 引理 3 |
| `GraphSnapshotVersion` | `9B59A35A…E896` | 必须逐位相同 | 由 `ranks` 决定 |
| 阶段 B 探针数 | 3,954,144 | **4,241** | 阶段 A 身份数 |
| 元数据宽度 | 4,241 | 4,241 | `valueKeys.Count` |

## 6. 验证方案

| # | 判据 | 手段 |
| ---: | --- | --- |
| V1 | **字节级等价** | `GraphSnapshotVersion` 逐位同 `9B59A35AEE4378F65D6C3B27CB380AC12336486EA25526E38E059C676E17E896` |
| V2 | 不变式 | 判据 C 违反数 = 0（已测） |
| V3 | **同二进制交叉 A/B** | 内部开关 `UseLegacyIdentityContextId`，交替各跑 2 次（教训 11） |
| V4 | 计数不变 | 身份数 4,241、值数 4,241；`metadataWidth` 不变 |
| V5 | 小批量测试 | `~Snapshot\|~Freeze\|~GraphIndex\|~ShardingEquivalence\|~Interprocedural` |
| V6 | 构建 | 0 错误 0 警告 |

## 7. 风险与回滚

| 风险 | 等级 | 处置 |
| --- | --- | --- |
| 引理 2 依赖判据 C，而判据 C 只测了单文件语料 | 中 | 构造函数已强制该不变式；A/B 与 oracle 再兜一层 |
| 调用点不存在但 `ContextId` 非空 的分支被误改 | 中 | **明确保留原分支**，只改调用点存在的分支 |
| `FilePath`/`DisplayName` 在本语料共享、他语料不共享 | 低 | 引用相等**永远 sound**，只是可能不够粗；阶段 B 兜底正确性 |
| 并发写入者正在改同文件 | 中 | `NLCPGGraphIndex.cs` 属本轮自有改动区；动手前核对 mtime |

**回滚**：`Build/verify-pass-optimization/NLCPGGraphIndex.N3d-PRE.bak`（`84A41997FEBFA781`）

## 8. 诚实边界

- 预期收益来自**去除阶段 B 的 3.95M 昂贵探针**（阶段 B 实测 5,657 ms），
  以及阶段 A 字典条目数由 3.95M 降到 4,241（阶段 A 实测 4,552 ms）。
  **实测结论以 V3 的 A/B 为准**，本设计不预填未测数字。
- 数据全部来自 `NPC.cs` **单文件**语料。
- 本改动**只修阶段 A 的键**，**不**并行化 `Create`，**不**触及 `FreezeQueryIndex` 的其余部分。
- `BuildMetadataRanks` 顶部的**过时注释**（声称"真实单文件语料实测元数据覆盖为 0"、
  `IMPLEMENTATION-EVIDENCE.md:67` 的"边元数据覆盖 = 0.0%"）已被本轮实测推翻
  （元数据边数 **3,954,144**，快速路径 `hasMetadata=True` 不生效）⇒ 一并更正。
