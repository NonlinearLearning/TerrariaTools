# InterproceduralDataFlowPlan 与排序载体缩窄设计

日期：2026-09-25。状态来源：[feature_list.json](../../Context/feature_list.json)，feature `interprocedural-plan-carrier-compaction`。

> **实施状态（2026-09-25 更新）**：本设计交付后，**方案 A 已按本文件第 5 节实施并验证等价**，
> 但**未获得可测的收益**；B/C 仍未动。实测与边界见 `Context/feature_list.json` 的
> `interprocedural-plan-carrier-compaction`。下方第 1～10 节保留**设计时**的原文口径，
> 其中"本次仅交付设计"描述的是设计轮次，不再是当前状态。
>
> 已实测（同运行时 `Unsafe.SizeOf`）：`NLCPGNode` 104 B、计划 428 B、新排序行 **32 B**；
> 旧排序行经隔离副本还原后实测 **456 B**（原"428 B 历史值"仅指计划，行宽更大）。
> 等价性：三份真实语料 × PRE/POST × 2 轮 + DOP=2/16，`GraphSnapshotVersion` 逐字节相同。
> ⚠️ **整轮构建分配量 A/B 为负/不显著**，故**不得**把 456 → 32 B 当成内存或吞吐收益。

本次设计轮次仅交付设计：未修改任何产品类型或排序算法，未启动执行计划、构建、测试或内存实验。下文大小为历史报告或布局推导，不能当作本轮实测收益。配套 [长尾快速测量设计](2026-09-25-dataflow-tail-small-batch-measurement-design.md) 独立于本设计，不互相作为实施前提。

## 1. 结论与范围

推荐先用 **PlanIndex 代替排序行里的整条计划**；随后把调用点元数据移到组级，删除计划中已无消费方的字段。节点序号化只在精确语义、峰值净内存和耗时三个门槛均满足后采用。不能只把计划从 428 B 缩成 16 B，就忽略节点表、索引表及构建时双份数组。

边界限于跨过程数据流计划、排序行和发布窗口的临时存储。保留原来三种 bridge、预算、字符串排序、调用点上下文与串行图提交语义；不迁移并行池，不改图冻结协议，不更换 NodeSortKey 的比较规则。

## 2. 当前事实与已有计划

| 事实 / 既有工作 | 当前证据 | 对本设计的约束 |
| --- | --- | --- |
| 计划是内嵌节点的值类型 | [InterproceduralDataFlowPlan.cs](../../src/NLCPG/Builder/Passes/InterproceduralDataFlowPlan.cs)：4 个 NLCPGNode，BridgeKind、ArgumentOrdinal、StableCallSiteOrder | 不是 4 个廉价对象引用；用户引用的报告测得 428 B/条，本轮不复测 |
| 排序行复制完整计划 | [NLCPGBuilder.cs](../../src/NLCPG/Builder/NLCPGBuilder.cs)：PlanSortRow、BuildAndSortPlanRows、FlushParallelPublishWindow | 一个窗口同时存在计划数组和排序行数组；排序会反复搬动大值 |
| 值类型迁移已有设计 | [domain-model-struct](2026-09-18-domain-model-struct-design.md) | 不重新做 class → struct，不依据旧文档把现有 NLCPGNode 误认成引用 |
| 冗余计划 HashSet 已移除 | [N2 执行计划](../../Build/N2-执行计划.md) 与当前 BuildAndSortPlanRows | 不恢复计划去重；索引下标必须对应原始插入序 |
| 上下文/枚举分配优化已实施 | [alloc-reduction 设计](2026-09-23-interprocedural-pass-alloc-reduction-design.md)及[执行记录](2026-09-23-interprocedural-pass-alloc-reduction-execution.md) | 这些不等于完成计划布局缩窄；当前上下文已经按组计算一次，不重复认领收益 |

截至本次阅读，计划的 `TargetMethodNode`、`StableCallSiteOrder` 没有下游字段读取；`CallSiteNode` 由发布器从 `plans[0]` 读取。构造期局部 targetMethod 仍用于目标选择、外部摘要、return 处理，不能删除那部分语义。源节点与目标节点仍传给 `graph.AddEdge`，必须保留其原始值。

## 3. 不变条件

1. callsite 的遍历次序和窗口 slot 次序不变；组内依次比较 BridgeKind 的 int 值、ArgumentOrdinal、SourceKey 的 Ordinal 字符串序、TargetKey 的 Ordinal 字符串序、原始插入序。
2. 保留现有 CachedNodeSortKey 语义与 fallback。不能在本任务中换字段排序、文化相关排序或重新生成具有不同 tie 行为的 key。
3. return-method 的跨调用点去重门 `recordedReturnMethods` 在原来的串行构造位置执行，生命周期仍是整个 pass，不移进 worker 或窗口局部。
4. MaxBoundaryEdgesPerMethod 仍在当前调用点组截取**排序前**的原始计划前缀；保留现有选项名称、超限统计和三种 bridge 插入顺序，不趁本任务重释预算口径或添加输入归一化。
5. worker 只读密封后的计划/节点表并写自己的排序列表；全部排序结束后才按 slot 顺序调用原来的 `graph.AddEdge`。
6. 继续保留非公开 readonly record struct `InterproceduralDataFlowPlan`。现有 [NodeId 契约](../../tests/NLISSN.ContractTests/Cpg/NLCPGNodeIdContractTests.cs) 检查其类型属性；不通过改名或装箱绕开。

## 4. 三种方案对比与组合关系

令 N 为当前运行时的 NLCPGNode 元素宽度，P0 为原计划宽度，S0 为原排序行宽度。历史 428 B 只能作为起点；待实施时须在同一运行时重新测 N/P0/S0。

三种方案全部保留为候选。**A 缩小排序副本，B/C 缩小计划本身；A 可以与 B 或 C 组合，B/C 是最终计划表示的两种选择。** 以下将改法、收益和代价一起比较，具体所有权与等价约束见第 5～7 节。

| 方案 | 核心改法 | 预计尺寸变化 | 收益 | 代价与风险 |
| --- | --- | --- | --- | --- |
| A：排序行只保存计划下标 | PlanSortRow 保存 PlanIndex、BridgeKind、ArgumentOrdinal、SourceKey 引用、TargetKey 引用，发布时通过下标读取原计划 | 计划本身不变，仍以历史 428 B 为基线；排序行 3 个 int + 2 个引用，x64 目标约 32 B | 去掉排序列表内嵌的整份计划，减少比较排序时搬动的数据量；改动范围最小 | 下标必须绑定原组及原插入序，组发布前计划列表不能重排或复用；吞吐收益尚未实测 |
| B：重复字段移到调用点组 | CallSiteNode、StableCallSiteOrder 移到组级；删除计划中无消费方的 TargetMethodNode 字段；每行保留 SourceNode、TargetNode、BridgeKind、ArgumentOrdinal | 若 N=104 B，则计划约 428 → 216 B；可叠加 A 的约 32 B 排序行 | 减少每行重复数据，源和目标节点仍保留完整值，不引入节点身份转换 | 需要调整计划构造和发布接口；目标选择的局部 targetMethod 逻辑必须保留；须守住原预算前缀和组顺序 |
| C：节点值改为窗口局部序号 | 沿用组级元数据，计划只保存 SourceOrdinal、TargetOrdinal、BridgeKind、ArgumentOrdinal；完整端点载荷集中存放 | 计划 4 个 int，目标 16 B；另有节点表、interning 索引和转换期间的共存开销 | 端点复用充分时，有机会进一步减少重复节点值和数据搬动 | 不能按身份相等直接合并不同载荷；需要不可变快照和明确生命周期；端点复用不足时净内存可能不如 B |

上述 32/216/16 B 均为布局目标或条件推导，不是本轮实测值，也不等于整个窗口或进程的内存大小。

**推荐关系**：A+B 作为优先候选组合；C 保留为进一步压缩的候选。后续如获准实施，应先单独验证 A，再比较 B 与 C 的语义等价、窗口峰值净内存和耗时；并不要求把 A、B、C 依次全部上线。

**共同配套**：三种方案都要统计同时存活的副本、List.Count/Capacity、扩容或裁剪瞬间的双份数组，并治理全部窗口槽位的保留容量。容量治理可以降低峰值，但不能算作对象尺寸缩小；具体账本见第 8 节。

不选择 `class Plan` + 引用排序：增加逐行对象/GC 成本，也违背现有内部值类型契约。不选择“立即用全图 NodeId 替换节点”：冻结后的 NodeId 与当前构建期序号不具有可互换保证。

A/B/C 均为设计候选；**A 已于 2026-09-25 实施**（见文首实施状态），B/C 仍未实施。将三种方案写入报告不构成其余方案的执行授权。不会同时维护两套生产算法或添加自动选型开关。

## 5. 方案 A：下标排序，保留已有比较键

构造时按 `callSitePlans[i]` 生成排序行，存 i 及当前四个排序键。由于当前没有行过滤和前置去重，PlanIndex 正好等于原 InsertionOrder，可以作最终 tie-break；无需再存一份 InsertionOrder。若将来引入过滤，这个不变量必须重新审查。

发布时用 `plans[row.PlanIndex]` 获取原计划。排序比较器只访问行中的键，不在 O(P log P) 次比较中重复查图、生成字符串或拷贝计划。BridgeKind 继续转 int 比较，避免重引入 enum 装箱。

禁止原地排序 plans：PlanIndex 属于该组原始顺序，直到组发布结束才失效。组数据在 worker 运行及发布期间不追加、不移除、不跨组复用。

## 6. 方案 B：把重复元数据移出每一行

定义一个内部调用点组所有者，持有精确的 CallSiteNode 值、StableCallSiteOrder 与计划列表；这里只描述职责，不承诺新增公开类型。目标方法只保留在现有串行构造局部，不再写入每条计划。发布器从组头生成调用点上下文，仍保持当前“一组一次”的调用位置。

计划保留 SourceNode 和 TargetNode 的完整值，因而不需要改变 AddEdge 的节点合并行为。沿用现有构造后空计划判断以及随后的预算截断顺序，组密封发生在截断之后；StableCallSiteOrder 不因跳过组而重编号。组头不得为原本跳过的组额外触发上下文计算。

零预算有独立的源码边界：当前选项记录没有对应 Validate/归一化，空组判断又在 RemoveRange 之前；若非空组被裁成零条，发布器仍读取 plans[0]。这是未运行复现的源码风险，不能宣称已支持零预算，也不能在字段搬迁时静默变成“跳过”。后续先固定实际基线行为，需修复时单列语义变更，不计为缩窄收益。

这部分的收益来自删除冗余行字段，不能把已有上下文缓存、HashSet 移除或 pending-edge 枚举收益再算一次。

## 7. 方案 C：节点序号的安全设计与拒绝条件

### 7.1 为什么不能直接借图的序号数组

[NLCPGGraph.cs](../../src/NLCPG/Model/NLCPGGraph.cs) 的构建数组 `_nodesByOrdinal` 可被 AddNode / MergeNode 替换已有位置的节点值；AddEdge 又会调用 AddNode。现有 ResolveOrdinal 是内部私有构图机制，图没有提供本窗口可安全借用的不可变快照契约。借用活数组可能把计划保存的旧值悄悄替换成合并后的值，改变合并输入和时序。

更关键的是 [NLCPGNode.Equals](../../src/NLCPG/Model/NLCPGNode.cs) 优先比较 StableAnchor，其次 NodeId；**相等节点未必所有载荷字段相等**。直接用 `Dictionary<NLCPGNode, int>` 合并端点可能丢失后续 AddEdge 本该带入的补全字段。仅比较最终边数不足以发现该错误。

### 7.2 若采用 C，使用窗口局部的精确端点快照表

该表只收集本窗口实际使用的 SourceNode/TargetNode，串行构建，密封后给排序与发布只读使用。序号局限于该表的生命周期，不能与图 ordinal、NodeId、字符串 ID 混用或跨图保留。

最保守的验证版本在原预算截断完成后，把 B 的已保留计划转换成 C，并及时释放原组计划；尾部已截断端点不进入表。转换时 B/C 数组与端点表会短暂共存，必须计入峰值，不能声称全程只有 16 B/行。若这一版本不能获得净收益就保留 B；直接流式生成 C 而消除中间载体涉及预算、空组判定和 return 门控，须另证语义，不能偷偷合并进布局改动。

表内部边界只需要 Intern、Seal、Resolve 和读取既有排序 key；不向图 API 暴露临时序号。每个不同**完整载荷**保存一个节点值，同一身份但不同载荷保留为不同项。完整比较明确包含 Kind、NameId、FullNameId、SignatureId、DispatchKind、TypeFullNameId、FilePathId、SpanStart、SpanEnd、IsImplicit、NodeId、StableAnchor 及可空状态；禁止直接比较包含 padding 的原始内存。

为避免字典键再复制一次大节点，可用“完整载荷哈希 → 链头序号”，节点只存数组，另存 next 序号数组；哈希相同仍逐字段核验，不能只凭哈希合并。字典只承担 interning，密封后可释放；计算峰值时仍必须计入其与节点数组共存的一刻。这是候选实现的内部数据结构，尚未引入源码。

每个端点的 key 从**原有** CachedNodeSortKey 语义取得并保存共享字符串引用。即使两个同身份/不同载荷的端点占不同槽，其原缓存查询结果也可能相同；不得重新生成字符串悄悄改变排序。

串行发布时 Resolve 两个端点，再调用原 AddEdge。不得绕过 AddNode 的合并和 PendingEdgeBuffer 的去重。只有最后一个引用该表的组已发布且所有 worker 完成，才能释放表或复用其存储。

### 7.3 是否采用 C 的决定

如果语义等价无法证明，选 B。若端点几乎不复用，端点表及 interning 成本可能超过 B；此时仍选 B。C 的门槛是**含所有附加结构和构建瞬间的窗口峰值**低于 B，且耗时没有超出预先约定的回归预算。不能只凭 16 B 数字上线，也不在生产运行中动态切换 B/C 来规避评估。

## 8. 内存账本与容量生命周期

令 Cp/Cs 为当前同时存活的计划/排序数组总 Capacity，G 为活跃组数，H 为每组头开销，U 为窗口内不同完整端点载荷数，Ct 为端点表 Capacity。Q 为排序 key 引用数组、next 数组、interning 字典和相关容器的总字节。

- 原状：`Cp * P0 + Cs * S0 + 原有缓存与容器`。
- A：`Cp * P0 + Cs * Sindex + 原有缓存与容器`。
- B：`Cp * PtwoNodes + Cs * Sindex + G * H + 原有缓存与容器`。
- C：`Cp * 16 + Cs * Sindex + Ct * N + Q + G * H + 原有缓存与容器`。

这些是分项账本，不是整个进程的精确堆尺寸。数组头、对齐、扩容时旧新数组共存、裁剪时临时副本、排序运行时临时开销、字典空闲槽和保留但 Count=0 的列表均另计。字符串只共享引用时不重复计字符存储；若 fallback 真生成新字符串则必须计入。

以历史 P0=428、假设 N=104 为例，131072 个**有效元素**的计划载荷原为 53.5 MiB，B 约 27 MiB，C 约 2 MiB；A 的排序行目标载荷为 4 MiB。这里只是乘法推导，未包含上列额外成本，也未测得真实峰值下降比例。

> **实测更新（2026-09-25，`NPC.cs` 2,334,144 字符，关 dominance）**：上段的规模前提现已有实数。
> P = **4,604,850** 行 / 3,193 组 / 最大单组 6,693 / 窗口峰值同时存活 **Cp = 133,820**；
> 不同端点 **U = 9,485**（源 5,796 / 目标 3,689），复用率 **2P/U = 971×**，端点仅占 1,883,204 节点的 0.5%。
> 按实测 Cp 算同时存活账本：**原状 112.8 MiB → A 58.7 MiB → B 31.6 MiB（216 B/行）→ C 7.1 MiB**
> （含端点表 0.94 MiB）。即 B 再省 **~27.1 MiB**、C 再省 **~24.6 MiB**。
> 方案 A 的整轮构建分配量同二进制 A/B 为 **−86.6 MiB（−0.68%）**，两轮确认后为 −97.4 MiB。
> 注意：B/C 的 216/16 B 仍是设计推导值；**峰值提交内存与吞吐未测**；C 还须计入第 7 节
> 的 intern 结构与容量治理开销，故 7.1 MiB 是**下界**而非交付承诺。
> ⚠️ 本账本用的是窗口峰值 Cp，**不是累计 P**——用 P×428（1,879.6 MiB）作收益会重犯第 8 节警告的错误。

当前窗口按 64 组 / 131072 行触发刷新，行数阈值在加入组后检查，因此单大组能超过它。当前计划列表的 TrimExcess 与 64 个复用排序列表也不能简单解释为“最多只有 Count 那些元素”。应记录：

| 时点 | 同时记录的内容 |
| --- | --- |
| 计划刚构造、预算裁剪前后 | Count/Capacity、三类 bridge 计数、上界预分配和真实条数 |
| TrimExcess/扩容前后 | 原数组、新数组和瞬态复制字节，不只记录结束状态 |
| 窗口密封、排序结束 | Cp/Cs、G、最大单组、端点复用率 P/U、Ct/Q |
| 每组发布后、窗口结束、下一窗口进入前 | 仍保留的数组容量、字符串/节点引用、待发布组数 |

方案 A 首次验证保持当前容量策略，隔离布局收益。后续生命周期设计是：组发布后 Clear 排序列表以释放 key 引用，并释放组计划；worker 完成后才能清理。保留容量按**全部 64 个槽的总字节**控制，不能每槽都保留各自历史最大值。

可评估的内部保留上限为“一般窗口行阈值 × 实测 Sindex”（Sindex=32 时约 4 MiB）；超大组排序缓冲发布后不保留，普通槽超总额时优先丢弃大缓冲。该阈值是提案，不新增公开配置，也不声称限制了活跃窗口或总进程内存。不能每窗口无条件 TrimExcess，也不默认引入 ArrayPool，把 GC 分配转成长期保留。

异常/取消路径在所有 worker 结束后清理尚未发布的临时所有者，不能让 worker 使用已归还数组。不得为节省容量清空仍供后续组使用的 recordedReturnMethods、全局排序 key 缓存或图存储。

## 9. 后续的小规模验收设计

以下均未执行。先检查托管布局，再用真实生产入口验证语义和净内存；不以 Marshal.SizeOf 或装箱对象大小代替数组元素宽度。

| 检查 | 最小覆盖与通过条件 |
| --- | --- |
| 托管宽度 | 同一 Release/运行时/架构下测 Unsafe.SizeOf 的 N/P0/S0 与候选宽度；保留机器信息，目标值不符先解释布局 |
| 稳定排序 | 同 bridge/ordinal/SourceKey/TargetKey 的重复键仍按原插入序；key 包含文本数字及空值等现有边界，不换比较规则 |
| 三种 bridge + 截断 | argument、return-callsite、return-method 均非空；有效正预算 1 和恰好边界保持原始前缀与原溢出语义；0/负值单独记录现有输入与异常契约，不预先断言支持 |
| 跨窗口语义 | 超过 64 个小调用点的夹具覆盖窗口切换；同一目标方法的 return-method 门不能每窗口重置 |
| 精确节点载荷（C 专属） | 相同 anchor/NodeId、不同补全字段的端点分别参与 AddEdge；比较完整冻结节点、元数据、边及原始发布次序，不只比较 Equals |
| 峰值/保留容量 | 重复端点高/低各一个小夹具，再做大组→小组两窗口；统计布局账本、allocated bytes 与发布后 retained capacity |
| 并行边界 | DOP=1/2 的序列等价；同一快照表并行只读、槽位不交叉、清理在 worker 之后 |

沿用 [CpgInterproceduralEdgeOrderTests](../../tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs) 的固定边序 oracle。将输出排序再比较的测试只能辅助边集等价，不能取代顺序守卫。检查候选实现时不能同时把 oracle 改成新输出。

默认未来验证只选上述关键形态的最小样本，预热 1 次、正式 3 次，DOP=1 做分配和阶段计时，DOP=2 只作等价检查；运行目标控制在 45 秒内，构建另列。布局/容量账本按实际存活范围计算；进程分配与私有字节是辅助观测，不把 GC.GetAllocatedBytesForCurrentThread 当成并行总分配。若用进程总分配，必须隔离其他工作并标出包含的后台活动。

任何对象大小目标、运行预算或收益都要由后续真实数据确认；总进程内存、完整 NPC、所有 DOP 下吞吐均未在本轮验证。小样本不能直接承诺数 GiB 或固定百分比收益。

## 10. 交付与执行边界

设计已确定 A 的字段、比较器与下标生命期，B 的元数据所有权，C 的可行前提与拒绝条件，以及容量账本和最小验收方法。设计轮次没有生成逐任务执行清单，没有改代码或运行实验。

> **实施更新（2026-09-25）**：A 已落地——`PlanSortRow` 改为 `PlanIndex` + 4 键，
> 发布按 `plans[row.PlanIndex]` 回读；护栏见
> `tests/NLISSN.ContractTests/Cpg/CpgInterproceduralEdgeOrderTests.cs` 的
> `PlanSortRow_CarriesPlanIndexInsteadOfFullPlan`。B/C **仍未动**。

这不撤销已执行的 N2、值类型迁移或分配削减工作；它们是当前基线。本次新增“排序下标 + 计划载体缩窄”的 **A 部分已实施并通过等价性与宽度验证**，**B 与 C 保持未开始**；状态与实测/未验证边界统一回到 feature_list.json，不能因文档写完标记优化完成，也不能把元素宽度缩小当成已验证的内存收益。
