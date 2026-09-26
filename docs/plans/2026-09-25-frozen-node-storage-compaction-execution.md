# M4 冻结节点单份持有与局部缓冲复用 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 冻结后完整节点只存一份，查询与原始枚举通过序号读取；顺带消除同一次排序的重复计数缓冲分配。

**Architecture:** 以 NLCPGGraphIndex 的 canonical 节点数组为唯一完整载荷，复用已有 NodeId→ordinal 字典。graph.Nodes 使用独立的输入序排列视图。构图/导入时的完整节点字典只作为过渡存储，在索引成功发布后清空并释放容量。排序计数缓冲只在单次 Create 内复用。

**Tech Stack:** C# / .NET、现有 OrdinalNodeList、Dictionary<NodeId, int>、稳定计数排序、ContractTests。

---

日期：2026-09-25。feature：`frozen-node-storage-compaction`，状态见 [feature_list.json](../../Context/feature_list.json)。
共同前置和门槛见 [总索引](2026-09-25-memory-optimization-execution-index.md)。本轮只交付执行文档。

## 1. 取舍与成本上限

| 方案 | 决定 | 理由 |
| --- | --- | --- |
| 复用现有节点数组和 NodeId→ordinal | 采用 | 消除一份常驻完整节点字典值，不重写查询体系 |
| 新增输入序 int 排列，复用 OrdinalNodeList | 采用 | 每节点少量索引换取原枚举契约，避免简单返回已排序数组 |
| 单次排序复用 positions 缓冲 | 采用，独立小补丁验收 | 减少已存在的重复分配，不改排序算法 |
| 消除 anchoredNodes/remappedNodes 全部中态 | 本轮不采用 | 牵涉确定性 ID 分配和失败中态，收益归因与改造面明显扩大 |
| 绕过 NLCPGEdge[] 直接构建 CanonicalEdgeStore | 本轮不采用 | 需要改边输入、元数据排名和存储构造接口，不属于局部节点治理 |
| 删除 StableAnchor、压缩 NodeId、改变持久化格式 | 不采用 | 涉及身份、快照和跨组件契约 |

粗估 3–4 人日，是本组成本最高项。只允许 graph/index/现有节点投影视图的局部改动；若需要改 Builder、节点相等语义、序列化协议或查询调用方 API，则停止该方案。

## 2. 当前依据和两种顺序

[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs) 的 `_nodesByNodeId` 与
[NLCPGGraphIndex.Create](../../src/NLCPG/Model/NLCPGGraphIndex.cs) 的 `orderedNodes[]` 各持一份完整节点。
索引的 `OrderedNodes` 和 `CanonicalNodes` 本来就是同一个数组的别名，不能把它们误算为第三份载荷。

- **canonical 序**：按 NodeId，供索引、快照哈希与 CanonicalEdgeStore 使用。
- **graph.Nodes 序**：原 `_nodesByNodeId.Values` 的实际枚举序；CreateFrozen 输入可以乱序且 ID 非连续。
- **graph.Edges 序**：已有独立的 InsertionOrder，必须保留；不可借用它作为节点排列。

GetNode、GetSymbolReferences、GetMethodOwnedCallSites 及邻接/跨度查询中的所有 `_nodesByNodeId` 读取都须迁移。
保留“未知 ID 返回 null”的 GetNode 行为，以及其他依赖已知端点的方法原异常语义。
冻结前 Nodes、冻结幂等性、CreateFrozen 的重复 ID/缺失 ID/未知端点校验均保持。

## 3. 目标构造与发布

1. Create 输入节点只枚举一次，生成将被索引接管的节点数组；同时记录输入 NodeId 序列。输入不得被要求可重复枚举。
2. 确认节点 ID 唯一后，沿用 NodeId 比较语义排序该独占数组；以唯一 ID 形成严格次序，不能改变重复 ID 的错误行为。
3. 构建现有 NodeOrdinals；将输入 NodeId 序列转换为 `int[] nodeInsertionOrder`。它与完整节点数组共同生成一个缓存的 OrdinalNodeList 视图。
4. 索引提供内部 TryGetNode/必要的已知节点读取方法，封装字典和数组，不向外暴露可写数组。
5. 所有索引/快照创建成功后才赋 `_queryIndex`；随后清理 `_nodesByNodeId` 的元素与容量，或释放其唯一字段引用。CreateFrozen 和 FreezeQueryIndex 两条入口都要做。
6. 禁止仅 Clear 后保留装过大值的字典容量；空容器可以释放存储，避免对非空大字典 TrimExcess 产生复制峰值。
7. 失败前不提前释放原节点载荷。记录并保持实施前的异常/重试可观察边界，不借此承诺图冻结具备原本没有的事务性。

数组、视图、CanonicalEdgeStore 和各索引可以共享同一节点数组引用；“单份”指完整载荷存储，不限制只读引用数量。
输入 ID、输入排列和排序数组短暂共存列入峰值账本。暂时保留 anchoredNodes/remappedNodes 和边过渡数组，不把未处理部分写成已优化。

## 4. Task 1：冻结公开顺序与查询 oracle

**Files:**
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGGraphIndexStorageContractTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/FrozenEdgeProjectionEquivalenceTests.cs`。
- Test: `tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs`。
- 新增 Test: `tests/NLISSN.ContractTests/Cpg/FrozenNodeStorageEquivalenceTests.cs`。

1. 基线涵盖空图、单节点、非单调输入 ID、稀疏 ID、节点补全、多边元数据及预分配 ID。
2. 同时记录 graph.Nodes、各查询原始返回顺序、完整边载荷和 GraphSnapshotVersion，不能只比较排序后的集合。
3. CreateFrozen 输入使用只允许枚举一次的 enumerable；重复 ID、空 ID、未知端点与非法输入先冻结原错误。
4. 新结构门槛断言冻结成功后没有第二份常驻完整节点容器；现有“索引内部单数组”测试不足以覆盖 graph 自己的字典。
5. shard 导入/导出回环经现有入口验证，不改序列化断言来迁就新顺序。

## 5. Task 2：单份节点和保序视图

**Files:** Modify `src/NLCPG/Model/NLCPGGraph.cs`、`src/NLCPG/Model/NLCPGGraphIndex.cs`；复用 `src/NLCPG/Model/OrdinalNodeList.cs`。

1. 加入输入序排列与索引内部节点查询方法，先验证 canonical/输入序视图各自正确。
2. 逐个替换 graph 中冻结态字典读取，保持方法原返回/异常边界；冻结前 GetNode 的实际行为单列护栏。
3. 两条成功发布路径统一清理构造字典容量，确保没有缓存视图仍指向旧字典。
4. 跑 Task 1 和节点/边查询测试，保存独立补丁与常驻账本。这一步不改计数排序。

## 6. Task 3：仅复用计数缓冲

当前 CountingSortPass 每次分配 `positions[keyWidth + 1]`。改为一次 Create 范围的私有 scratch，按当前最大需求扩容，复用时清零实际活动区间。
稳定 scatter 顺序不变；传入明确的活动长度，PrefixSum 只处理该长度，不能把上次较大 keyWidth 的脏尾部算进去。
BuildOrdinalsByTwoKeys 可使用同一临时 scratch，但最终 offsets/ordinals 必须独占结果存储，不能与后续会清零的 scratch 别名。

1. 增加交替大/小/零 keyWidth、有相同排序键及空边的测试，保留 tie 顺序。
2. 只修改 CountingSortPass 及必要私有调用签名；不改直方图/CSR 算法，不重做已复用的 insertionOrder buffer。
3. 记录计数缓冲分配次数和总字节。scratch 仅活到 Create 返回，不进 graph 字段或全局池。
4. 独立检验目标阶段分配及峰值，若保留较大的 scratch 反而增加阶段峰值，回退本子补丁，保留已通过的节点单存。

## 7. 验证命令与采纳门槛

```powershell
& ./Build/Tools/Invoke-SerialDotnet.ps1 test ./tests/NLISSN.ContractTests/RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FrozenNodeStorageEquivalenceTests|FullyQualifiedName~NLCPGGraphIndexStorageContractTests|FullyQualifiedName~FrozenEdgeProjectionEquivalenceTests|FullyQualifiedName~NLCPGNodeIdContractTests|FullyQualifiedName~CpgShardContractTests|FullyQualifiedName~CpgInterproceduralEdgeOrderTests'
```

先执行总索引构建并确认新增用例实际执行。拟定门槛：

- 两条冻结入口完成后，完整节点数组恰一份，原始节点/边枚举序、全部查询和完整元数据相等。
- 节点存储子系统的保留字节在至少 8192 节点的受控夹具中下降至少 30%；数组头、输入序、NodeOrdinals 和共享引用去重后统一计数。30% 为拟定决策线。
- 独立记录冻结峰值；新增输入序维护不能提高原峰值，不能把“常驻变小”替代峰值证据。
- GetNode/邻接热点与 Freeze 阶段分别满足共同耗时门槛，避免用建图加速掩盖长期查询变慢。
- 计数缓冲子项应减少分配且不破坏稳定排序；未通过则撤销该子项并保留明确状态。

回退以两个独立补丁进行。本轮不消除完整边过渡数组，不把它列为关闭此方案的隐含前提。

---

## 8. 实施记录（2026-09-25）

> 与第 1–7 节的**拟定**门槛区分：本节只记录**实际执行过**的命令与**实测**结果。
> 未执行的项显式标注为"未验证"。

### 8.1 补丁 M4-A：节点单份持有

**改动文件**

| 文件 | 改动 |
| --- | --- |
| `src/NLCPG/Model/NLCPGGraph.cs` | 删除 `Dictionary<NodeId, NLCPGNode> _nodesByNodeId`；`Nodes` 冻结后改走索引的输入序视图；`GetNode` 改走索引 `TryGetNode`；`GetSymbolReferences` / `GetMethodOwnedCallSites` / `ExtractLocalView` 改走 `GetKnownNode`；两入口不再写字典 |
| `src/NLCPG/Model/NLCPGGraphIndex.cs` | `Create` 输入只枚举一次并**接管**该独占数组（就地按 NodeId 排序）；新增常驻 `nodeInsertionOrder`（输入序→canonical 序，4 B/节点）；新增 `InputOrderedNodes` / `TryGetNode` / `GetKnownNode`；`Array.Sort` 比较器用 `Nullable.Compare` 对齐原 `OrderBy(NodeId)` 的 null 排序 |

**接管是"真接管"，修正了一处早先的过度声明（本轮最重要的一条更正）**

第一版实现把 `Create` 的开头写成 `var orderedNodes = nodes.ToArray();`，同时注释声称"索引接管该数组"。
这条注释是**错的**：`ToArray()` **无条件复制**（即使输入已经是数组），于是

1. 冻结时仍会多出一份完整节点载荷（`nodeCount × 104 B`），把"单份持有"的收益抵消掉一部分；
2. 更严重的是它**抬高了冻结峰值**——调用方那份数组在复制后立刻变成垃圾，
   而新旧两份在 `Create` 期间同时存活，这正好违反第 100 行的"新增输入序维护不能提高原峰值"。

现已改为与相邻 `edgeArray` 同样的写法：

```csharp
var edgeArray = edges as NLCPGEdge[] ?? edges.ToArray();
var orderedNodes = nodes as NLCPGNode[] ?? nodes.ToArray();
```

两个生产调用方（`CreateFrozen` 的 `frozenNodes`、`FreezeQueryIndex` 的 `frozen.Nodes`）传进来的
都是它们**自己刚刚独占新建、此后不再复用**的数组，故 `as NLCPGNode[]` 分支命中，**不再发生第二次
完整载荷复制**；只有传入非数组序列（未来可能的惰性投影）时才走 `ToArray()` 回退，并且必须复制——
因为随后要**就地**排序，绝不能别名调用方的集合。

**基线对照（`git show HEAD`）**：改前的 `Create` 用 `nodes.OrderBy(node => node.NodeId).ToArray()`
（同样是一份**新**数组），**同时** `NLCPGGraph` 还常驻着 `_nodesByNodeId`
（`Dictionary<NodeId, NLCPGNode>`，entry 内联 104 B 值）。故改前冻结后同时存在**两份**完整节点载荷
（字典的值 + `OrderBy` 出的数组），改后只剩**一份**。

**顺序契约**：`nodeInsertionOrder[输入下标] = canonical 位置`，`InputOrderedNodes` = `OrdinalNodeList(canonicalNodes, nodeInsertionOrder)`，
故 `graph.Nodes` 枚举序 == 两条入口的输入序。`FreezeQueryIndex` 路径的输入序由
`_nodesByOrdinal` 的**序数序**给出（`AssignDeterministicNodeIds` 不再经 `remappedNodes` 字典中转，
改为按序直接物化 `frozenNodes[ordinal]`），与原先 `_nodesByNodeId.Values` 的枚举序一致。

**异常边界**：`GetNode` 未知 ID 仍返回 `null`；`GetKnownNode` 未知 ID 仍抛 `KeyNotFoundException`；
`CreateFrozen` 的空 ID / 重复 ID / 未知端点仍抛 `ArgumentException`（重复 ID 保持框架字典消息、无 `ParamName`，
未做"顺手改进"）。冻结前 `GetNode` 恒为 `null`，新增护栏用例锁住。

**回退**：本补丁可单独回退（只涉及上述两个文件），不影响 8.2 的计数缓冲子补丁。

### 8.2 补丁 M4-B：计数缓冲复用（可独立回退）

`CountingSortPass` 原每趟分配 `int[keyWidth + 1]`。改为新增私有 `CountingScratch`
（单次 `Create` 调用栈内唯一、按需扩容、复用时只清**活动区间**），并把显式活动长度传给 `PrefixSum`
（`PrefixSum(int[] offsets, int length)`），避免共享缓冲的脏尾部被前缀和读到。
`BuildOrdinals` / `BuildOrdinalsByTwoKeys` 走同一 scratch；`offsets` 仍是独占的常驻结果，不与 scratch 别名。

**门禁**：`Count` 级私有签名由 `CountingSortPass_TakesCallerProvidedScratch` 锁住；
交替宽度正确性由 `CountingSortPass_ReusesScratchAcrossAlternatingKeyWidths` 直接驱动（大 4096 → 小 4 → 全同键 → 空输入 → keyWidth=1），
并同时断言复用后每次调用分配 < 4096 B（旧行为为 4 × (keyWidth+1) = 16,388 B）。
**回退本子补丁不影响 8.1**。

### 8.3 实际执行的验证

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.UnitTests\RoslynDeletionPrototype.UnitTests.csproj --no-restore -p:UseSharedCompilation=false
& .\Build\Tools\Invoke-SerialDotnet.ps1 test .\tests\NLISSN.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~FrozenNodeStorageEquivalenceTests'
```

新增用例 `tests/NLISSN.ContractTests/Cpg/FrozenNodeStorageEquivalenceTests.cs`（**21 个**，含 §8.4 的
接管/峰值两条）覆盖：
冻结前后 `graph.Nodes` 枚举序、`CreateFrozen` 乱序稀疏输入序、单次枚举输入、
`GetNode` 已知/未知/冻结前行为、`GetSymbolReferences` / `GetMethodOwnedCallSites` 逐节点对拍、
跨度与 `ExtractLocalView` 查询、单份载荷结构门槛、常驻账本、
**`Create` 对数组输入的就地排序与接管（`Assert.Same`）**、**非数组输入必须复制且不改写调用方集合**、
**冻结路径单次 `Create` 的分配额不得超过一份完整节点载荷**、
计数缓冲签名与交替宽度、退化节点数边界、以及 `CreateFrozen` 的四类原错误。

**实际结果**

| 命令 | 结果 |
| --- | --- |
| `NLCPG.csproj` 构建 | **0 警告 / 0 错误** |
| UnitTests 全量 | **117 通过 / 0 失败** |
| 本方案 §7 定向 Contract 过滤器（6 个类） | **66 通过 / 0 失败 / 0 跳过**（改动前同 5 个既有类基线 **45/45** ⇒ 新增 21、**零回归**） |
| `FrozenNodeStorageEquivalenceTests` 单类 | **20 fact / 21 case**，全绿 |

**未跑**全量 `-Fast` 层，原因是外部并发写入者（见 8.7）而非本项改动。

### 8.4 冻结峰值 / 冻结路径分配（第 100 行要求"独立记录冻结峰值"）

用 `GC.GetAllocatedBytesForCurrentThread` 在**同一线程、先预热**的条件下测"一次
`NLCPGGraphIndex.Create`（数组输入）实际分配了多少字节"，节点数 8192、`NLCPGNode` = 104 B：

| | 字节 | 相对一份完整载荷（8192 × 104 = 851,968 B） |
| --- | ---: | ---: |
| 接管实现（当前） | **565,112 B** | **66.3%** |
| 无条件 `nodes.ToArray()`（早先实现） | ≥ 851,968 B | **≥ 100%** |

达到固定点（`fixed`）而非"峰值"口径的理由：托管堆峰值需进程级采样且本机并发噪声很大
（见 `progress.md` 中 M7 记录的 ±28% 噪声底）。**一次 Create 的分配额**是确定性量，
且恰好是判别"是否又复制了一份完整节点载荷"的那条线，故用它作峰值代理。
**仍需注意**：这不是进程峰值 RSS 的测量，也不是 `PeakWorkingSet` 的测量；
`Create` 期间**瞬时同时存活**的临时数组（`int[]` 临时量、字典扩容瞬间的新旧桶）未被逐个采样。

**变异检查（判别力证据）**：把 `Create` 改回无条件 `nodes as ... ?? ToArray()` 之前的写法
`var orderedNodes = nodes.ToArray();` 后：

- `Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace` **失败**（`Assert.Same` 实例不等）；
- `Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath` **失败**（分配额越过一份载荷）；
- 整套 `FrozenNodeStorageEquivalenceTests` 由 **20 通过 / 0 失败** 变为 **1 失败 / 19 通过**。

即这两条断言**在旧代码上会失败**，符合仓库"收益门槛必须在旧代码上失败"的规则，不是装饰性断言。
变异已按原样恢复，并复核 `src/NLCPG/Model/NLCPGGraphIndex.cs` 无残留标记。

### 8.5 阶段耗时门槛（第 101 行）与组件级峰值对照（第 100 行）

测量宿主：`Build/MemoryOptimization/M4/timing/probe/`（**只在 `Build/` 下，不属于仓库 `tools/`**；
程序集名复用 `src/NLCPG/Properties/AssemblyInfo.cs` 中既有的 `InternalsVisibleTo` 友名，
零产品改动）。完整证据与原始样本见 `Build/MemoryOptimization/M4/timing/EVIDENCE.md`
与同目录 `results/`。

**为什么同进程交错 A/B**：本机并发 dotnet 进程多，`progress.md` 的 M7 条目实测
A/A 空对照噪声底可达 ±28%，跨进程比较会把噪声当效应量。故同一二进制内做 ABBA 交错，
并额外测一组 `post-control`（与 POST 走**完全相同**的路径）作为本机噪声底；
**只有 |POST−PRE| 明显大于该噪声底才作结论**。每个计时样本扫全部 id 共 16 遍。

**PRE 等价物逐行对应**：改前 `GetNode` 体为
`_nodesByNodeId.TryGetValue(nodeId, out var node) ? node : null`（`git show HEAD` 行 199–202），
探针的 `legacy` 字典与之同类型、同填充。POST 侧走**真实公开入口** `graph.GetNode(id)`，
还多一次判空 ⇒ POST 侧的额外开销只会**不利于** POST。

**`GetNode`：5 次独立复现 × 2 种访问序 = 10/10 同号，且每组的 |差值| 都大于该组噪声底**

| 复现 | sequential | scattered |
| ---: | ---: | ---: |
| 1 | −72.03%（噪声 +13.82%） | −78.82%（+1.42%） |
| 2 | −53.27%（−14.06%） | −69.02%（−4.69%） |
| 3 | −59.66%（+11.89%） | −71.54%（+0.38%） |
| 4 | **−20.84%（+2.74%）** | −28.43%（−4.58%） |
| 5 | −23.99%（−2.55%） | −34.49%（−0.33%） |

⇒ 第 101 行"避免用建图加速掩盖长期查询变慢"**未被违反**：`GetNode` 不仅没有劣化，反而**变快**。
机理可解释：改前 `Dictionary<NodeId, NLCPGNode>` 的 entry 按值内联 104 B（entry ≈ 120 B），
改后 `Dictionary<NodeId, int>` 的 entry ≈ 16 B 加一次 104 B 数组索引，字典小了约 7.5 倍，
cache 命中率改善。

**邻接热点**（本次**一行未改**，代码路径见 `NLCPGGraph.cs:686–690`）：
`out=130 ns/node`、`in=134 ns/node`（nodes=8192 / edges=4096），仅作"未被顺带拖慢"的参照。

**Freeze 墙钟**：`samples=40,259,12,26,47,10,11`（ms）—— 离群点 259 ms 而中位数 26 ms，
被 GC 主导。按总索引第 66–67 行标为 **Inconclusive**，**不宣称**任何改善或劣化。

**组件级 PRE→POST（第 100 行"新增输入序维护不能提高原峰值"的直接判据）**

不重建整份 PRE 镜像，而是把改前在**节点存储**上多做的两件事与改后取代它的常驻结构分别量一次：

| 项 | 字节 |
| --- | ---: |
| **移除**：一份完整节点载荷复制（改前 `Create` 的 `OrderBy(...).ToArray()`） | **1,803,168 B** |
| **移除**：`Dictionary<NodeId, NLCPGNode>`（entry 内联 104 B） | **1,010,416 B** |
| **新增**：NodeId→ordinal 字典 + 输入序 `int[]` | **201,304 B** |
| **净减少** | **2,612,280 B** |

新增的输入序本身只占 **32,768 B**（8192 × 4 B），改前冻结在节点侧多分配的是它的 **85 倍以上**
⇒ **冻结峰值在节点存储维度上是下降的，不是上升的**，第 100 行成立。

### 8.6 未验证 / 未执行（不得当作已完成）

- **未做** NPC 全量或大边规模的真实语料测量；8.3–8.5 的账本来自 8192 节点的受控夹具。
  - ⚠️ **本条已被第三会话部分关闭**（2026-09-25，见 §8.8）：真实语料 `NPC.cs` 的
    **进程级峰值 RSS** 已测；但 8.3–8.5 的**组件级账本**仍是受控夹具口径，
    未在真实语料上重算。
- **未测** 进程级峰值 RSS / `PeakWorkingSet`；8.4/8.5 用的是确定性分配额与组件级差值，
  **不是**进程峰值，也**未**对 `Create` 期间瞬时同时存活的数组逐个采样。
  - ⚠️ **本条已被第三会话部分关闭**（2026-09-25，见 §8.8）：峰值 RSS 已测；
    **瞬时数组逐个采样仍未做**（5 ms 采样只给出包络）。
- **Freeze 墙钟为 Inconclusive**（GC 主导），既未证明改善也未证明劣化；
  改为用确定性分配额作该门槛的判据。
- 8.5 的组件级数字**只覆盖节点存储维度**，不含边侧与两侧共有部分，**不等于**完整 PRE 冻结的分配总额。
- 未消除完整边过渡数组（`RemapEdgesOrdinal` 仍产生 `NLCPGEdge[]`），本方案不以其为关闭前提。
- **未跑**全量 `-Fast` 层：本轮共享工作树被其他工作流持续改写，其未跟踪的临时探针
  （`tests/NLISSN.ContractTests/Cpg/TempM5DopProbe.cs`，观测到 15:53 仍在编辑）
  处于中间态时令 ContractTests 整体无法编译（`CS1061: StableNodeAnchor 未包含 StableKey`）。
  该失败**全部来自外部文件**（编译错误行全部落在该文件），不是本项改动。
  本方案的收口证据用的是 §7 定向过滤器本身：**66 通过 / 0 失败 / 0 跳过**（见 8.3），
  以及改动前基线 **45/45**、Unit 全量 **117/117**。

### 8.7 共享工作树的并发写入者（非本会话，仅记录不影响结论）

本轮至少一个其他工作流在**同一棵**共享树上持续改写
（`NLCPGBuilder.cs`、`DataFlowPass.cs`、`InterproceduralEdgeIndexSnapshotTests.cs`、
`CpgWorkBatchLocalPassTests.cs`、`NLCPGGraphIndexConstructionReuseTests.cs`、
`CpgFragmentByteCalibrationTests.cs`、`TempM5DopProbe.cs`、`TempM1WideGroupProbe.cs`、
`TempP02Probe.cs`、`NLCPGGraphIndex.T1T2-GOOD.bak` 等）。
其**中间态**会令 ContractTests 整体不可编译，另有一个**外部 `testhost` 进程**长时间锁住
`Build/test/.../NLCPG.dll`（`MSB3021`/`MSB3027`）。上述全部结果均以**同一命令轮询重试**、
待外部写入者与外部 testhost 稳定后取得。

**全程未编辑、未回退、未 kill 任何外部文件或进程，未运行 `git reset`/`clean`。**
陈旧制品陷阱（增量构建可能"成功"却配上旧 `NLCPG.dll`）⇒ **"构建成功"本身从不作为本项证据**。

### 8.8 第三会话补测：真实语料进程级峰值 RSS（2026-09-25）

复验记录：`Build/MemoryOptimization/M4/indep-verify/FINDING-process-peak-rss.md`。
本会话**未修改任何产品代码**。新增探针 `indep-verify/rss-probe/`，
放在 **gitignored 的 `Build/` 下**（与既有 timing 探针同样刻意不进仓库 `tools/`），
复用既有 `InternalsVisibleTo` 友元名 `DataFlowTailMeasurement` ⇒ 产品源码零改动。

**方法**：真实公开入口 `NLCPGBuilder.BuildFromSource`；语料 Terraria `NPC.cs`
（2,147,280 字符，与 M2 立项测量同一夹具）；后台线程每 **5 ms** 与构建**并发**采样
`WorkingSet64` / `PrivateMemorySize64` / `GC.GetTotalMemory(false)`；
构建前、构建后各取一次 `forceFullCollection: true` 读数，用以分离
「构造期瞬时峰值」与「冻结后常驻」；`ServerGarbageCollection=true`。

| 指标 | Run 1 | Run 2 | 复现性 |
| --- | --- | --- | --- |
| 节点 / 边 | 1,599,506 / 3,802,840 | 1,599,506 / 3,802,840 | **逐字节一致** |
| **峰值 WorkingSet** | **2,965,590,016 B (2.76 GiB)** | **3,050,131,456 B (2.84 GiB)** | 差 **2.77%** |
| 峰值 PrivateBytes | 3,359,444,992 B | 3,386,544,128 B | 差 0.80% |
| 峰值托管采样 | 2,319,461,312 B | 2,326,318,872 B | 差 0.30% |
| 构建后常驻托管（full GC） | 644,152,072 B | 568,502,600 B | 差 11.7% |

**可下的结论**：真实语料整次构建的进程级峰值 WorkingSet 约 **2.76–2.84 GiB**，
其中约 **76%** 是托管堆；峰值之后约 **1.6 GiB** 托管内存被回收
⇒ 构造期瞬时占用确实远高于冻结后常驻。

**不得下的结论（边界，必须与数字一起读）**：

- 该探针**只测当前树、没有 PRE 侧**，故这些数字**不能**说明 M4 **降低或提高**了峰值 RSS。
  要得到前后差值需反向应用 M4 补丁重跑——那是**产品改动**，超出独立复验范围。
- **瞬时数组逐个采样仍未做**：5 ms 采样只给出**包络**，无法定位到具体瞬时
  ⇒ 8.6 第二条只被**部分**关闭。
- 构建墙钟**不作判据**（并发 dotnet 进程 4–13 波动；8.6 已记录墙钟在 GC 主导下为 Inconclusive）。
- 常驻 WorkingSet 两次相差 **11.1%**（大于峰值的 2.77%）⇒ 它受 GC 归还策略影响、
  **尚不稳定**，故**不**把常驻 WS 提升为可复现量，只把「常驻托管」当作可复现量。
- 依总索引 §4「容器级改善不能写成端到端峰值改善」：本项**不**把节点载荷单份持有的容器级收益
  外推为端到端峰值改善；它只是**首次给出**该语料上的端到端峰值**基线**，供后续对比使用。

