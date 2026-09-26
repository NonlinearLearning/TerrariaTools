# 内存优化执行文档总索引

日期：2026-09-25。范围：六个主方向及 SparseSet 补充项，各一份执行文档。

用户要求：

> 对于这些我需要较强的优化方案代价过大的不采用,上述的优化反向每一个都写成一份执行文档

本轮交付执行文档，不实施产品改造。实现状态、完成条件和实际验收证据以
[feature_list.json](../../Context/feature_list.json) 为唯一来源；计划中的门槛是拟定验收标准，不是测量结果。

## 1. 采用哪些方案

“较强”指减少完整载荷副本、缩短强引用生命周期、消除每节点容器；不以新增框架或改写整个分析器换取收益。
人日为熟悉本仓库的工程师、单人串行实施含定向测试的粗估，不是承诺；不含外部阻塞和全量语料实验。

| 编号 | 独立执行文档 | 采用的方案 | 估计成本 | 超出本轮成本范围的方案 |
| --- | --- | --- | --- | --- |
| M1 | [跨过程计划与窗口](2026-09-25-interprocedural-plan-compaction-execution.md) | 组级元数据、两端点计划、预算前缀保留、排序槽总容量限制 | 2–3 人日 | 全图/全窗口端点 interning、改变节点身份或预算语义 |
| M2 | [跨过程边索引](2026-09-25-interprocedural-edge-index-compaction-execution.md) | 一份完整边快照池，四个索引只存 int 序号；精确预分配 | 2–3 人日 | 按节点身份合并载荷、回读可变图、通用压缩边框架 |
| M3 | [WorkBatch 消费即释放](2026-09-25-workbatch-consume-results-execution.md) | 新增不收集结果的入口，复用现有执行内核 | 1–2 人日 | 调度内核迁移、改变 reducer 顺序、改 channel 容量 |
| M4 | [冻结节点单份持有](2026-09-25-frozen-node-storage-compaction-execution.md) | 复用索引已有节点数组和 NodeId→ordinal，保留节点枚举排列；复用计数缓冲 | 3–4 人日 | 直接从 pending 边构建 SoA、移除 StableAnchor、改变持久化协议 |
| M5 | [DataFlow 邻接序号化](2026-09-25-dataflow-adjacency-compaction-execution.md) | 方法局部 CSR，复用流节点序号，保持缓存邻接原顺序 | 2–3 人日 | 方法内并行、重写候选索引、全局 CFG 格式迁移 |
| M6 | [LocalCpgFragment 所有权转移](2026-09-25-local-fragment-ownership-execution.md) | 内部专用接管数组入口；公开构造器继续复制 | 0.5–1 人日 | 公开借用内存协议、共享数组池、取消边界校验 |
| M7 | [SparseSet 合并分配](2026-09-25-sparse-set-union-allocation-execution.md) | 子集/自合并快速返回，直接写一次最终 spill，保留不可变共享 | 1–2 人日 | 通用自适应集合、全局池化、改写整个 fixpoint |

这些估计是每项的工作量上界参考，不构成七项一起开工的要求。若发现必须修改排除项，停止该项并记录不采用原因，不自动扩大项目。

## 2. 推荐执行顺序与文件冲突

按收益规模排序仍是 M1/M2、M3、M4、M5、M6/M7；按最快得到独立可验收益的交付顺序建议：

1. M3 → M6：先去掉无用结果强引用和明确重复物化。
2. M1 → M2：同一 `NLCPGBuilder.cs`，串行实施、逐项留基线。
3. M4：单独验证冻结前后枚举、查询和导出契约。
4. M5 → M7：同一 `DataFlowPass.cs`，串行实施并分别测量。

M3、M5、M7 与当前统一调度内核/其他 DataFlow 工作共享文件，开始前核对写入者；本计划不授权迁移调度器。
M5 不依赖 M7；M2 不依赖 M1 的表示，但二者不能同时改同一文件。
完整测量均串行，同一时间只运行一个性能批次。

## 3. 共同前置条件

1. 阅读 [当前交接](../../Context/progress.md)、[贡献指南](../contributing.md)、[领域不变量](../../CONTEXT.md)、[CPG 当前设计](../../设计docs/目前设计/cpg-architecture.md)、[测试入口](../../tests/AGENTS.md)、[测试规范](../../约束/测试代码编写教程.md)、[Harness Runtime](../harness-runtime.md)。修改 C# 前再读 [C# 规范](../../约束/Google-CSharp-Style-Guide-约束.md)。
2. 保存本项涉及文件的工作树 diff、源码 SHA256、构建产物 SHA256、运行时/输入/选项/预算/DOP。工作树已有大量其他修改，`HEAD` 不能替代实施前基线。备份和诊断放 `Build/MemoryOptimization/<M编号>/<唯一运行号>/`，不提交派生二进制。
3. 用当前二进制冻结小夹具的完整节点字段、完整边元数据、节点/边枚举序和候选提交序列；先验证既有测试在实施前的真实状态。原 oracle 不能用优化后的输出覆盖。
4. 产品代码只保留一种正常输入实现；临时 A/B 探针可以在独立诊断宿主使用，完成后清除产品开关。改变 GC 设置、DOP 或预算的运行不能作为单变量 A/B。
5. 文档模板中的 `superpowers:executing-plans` 对应会话中可用的 `executing-plans` 技能；不引入新依赖。这里提供任务分解，不自动提交、推送或启动全部产品改造。

## 4. 共同正确性与收益门槛

### 正确性

- 同一输入、预算、DOP 下，完整有序节点/边及元数据逐项相等；哈希和数量只是快速辅助。
- 必须覆盖空输入、重复项、同身份不同载荷、预算边界、窗口切换及适用的取消/异常路径。
- 保持现有公开 API 和输出契约；新增消费入口是显式选择，不改变旧入口语义。
- 顺序敏感测试保持原期望值；不能通过排序测试结果或删减字段“获得等价”。

### 证据口径

分别记录：累计分配字节、受控容器存活字节、强引用可达性、扩容时双数组、保留 Capacity、阶段耗时。
分配量下降不等于常驻内存下降；容器级改善不能写成端到端峰值改善。
`Unsafe.SizeOf<T>()` 与真实 Capacity 用当前相同运行时测量；428/216/272 B 仅是历史值或推导。

每项在正文定义结构性成功条件。统一运行成本门槛拟定为：受控小批次 1 次预热 + 至少 3 次交错配对；
相关阶段耗时中位数劣化不超过 5%，且不能靠降低工作量过关。小于计时分辨率或噪声大时标记 Inconclusive，
追加至多 2 对后仍不确定就保留文档、不宣称通过。5% 是本轮提出的准入标准，不是已有实测。
默认每个实验批次 60 秒内，超时保留失败/取消证据并缩小夹具；不自动启动 NPC 全量或 dump。

### 不采用/回退条件

输出任何一项不等价、引用生命周期变长、净存活内存增加，均回退该项补丁。
若净收益依赖高重复度而真实小样本不具备，记录负结论，不堆叠更复杂表示去追模型收益。
仅删除自己的改动或应用自己的反向补丁；不对共享工作树做 `reset --hard`、`clean` 或整文件覆盖。

## 5. 共同命令

仓库根 PowerShell；先运行初始化，再串行执行正文的定向过滤器。所有命令结果均留存完整日志及退出码。

```powershell
pwsh -File .\Miscellaneous\init.ps1
& .\Build\Tools\Invoke-SerialDotnet.ps1 build .\src\NLCPG\NLCPG.csproj --no-restore -p:UseSharedCompilation=false
```

每份文档给出对应项目与真实既有测试类；拟新增测试明确标注“新增”。定向绿色后再按 [测试分层](../../tests/AGENTS.md)
执行 `Run-TestTiers.ps1 -Fast`，触及 Host/调度契约再执行对应 Host 定向测试，性能测试只运行本项确定性夹具。
测试返回 0 不足以证明覆盖：TRX 必须包含预期测试，不能 0 tests。未关联的既有失败保留名称和复现证据，不顺手改期望清单。

仅修改本组文档时：

```powershell
pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1
git diff --check -- docs/plans docs/README.md Context/progress.md Context/feature_list.json
```

新建未跟踪 Markdown 不在普通 `git diff --check` 覆盖范围内，须另外检查相对链接、标题、代码围栏、尾随空格和文件存在性。
文档验收不等于七项实现验收。

## 6. 已有工作和报告边界

- [旧 DOP12 诊断](../benchmarks/nlissn-version4-dop12-diagnostics.md)的 1,515.6 MB 是旧版计划数组类型峰值。
- [9 月 23 日分配削减](2026-09-23-interprocedural-pass-alloc-reduction-execution.md)已消除跨过程入口的全量 pending 数组和重复上下文插值。
- [边载荷序号化](2026-09-23-edge-payload-ordinalization-execution.md)作用于图的构图缓冲，不等于四个跨过程索引已序号化。
- [冻结边投影](2026-09-24-frozen-edge-projection-execution.md)已消除常驻宽边和索引内部的排序宽边副本；`NLCPGGraph.RemapEdgesOrdinal` 仍产生过渡 `NLCPGEdge[]`。
- [计划缩窄设计](2026-09-25-interprocedural-plan-compaction-design.md)的 A 已见于当前 `PlanSortRow.PlanIndex`；M1 从 B 和容量治理开始。
- [同次测量报告](../benchmarks/2026-09-25-dataflow-tail-small-batch-measurement-report.md)只支持小夹具扫描量判断，不证明 NPC 长尾或本组优化收益。
- [G5 报告](../../Build/g5-报告-N5-N6-N1.md)否证的是调大 channel 对长尾的收益，不是否证 M3 对已提交结果生命周期的改进。

已稀疏化位集、候选整数索引/复用去重缓冲、PlanIndex、哈希字符串编码缓存均保留，不重复认领收益。
