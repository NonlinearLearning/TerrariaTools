# `FreezeQueryIndex` 性能优化可执行提案

**Goal:** 降低 `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs` 与 `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs` 中 `FreezeQueryIndex()` 的 wall-clock 和瞬时分配，优先处理可验证的重复遍历、重复分组、重复 anchor 计算和全量复制式重建。

**Scope:** 只优化冻结阶段：

- `AssignDeterministicNodeIds()`
- `DeterministicNodeIdTable.Create(...)`
- `RoslynCpgGraphIndex.Create(...)`
- `RoslynCpgFreezeTelemetry` 及其 host 输出

**Non-goals:**

- 不改变图节点/边结果
- 不改变 `NodeId` 稳定性契约
- 不改变 `SnapshotVersion` 语义
- 不把本提案扩展成 Syntax / DataFlow / Host 全链路性能总修复
- 不在本提案内改变默认 DOP

---

## 1. 现状与证据

最新 `NPC.cs` 单文件 DOP16 细分结果：

- `FreezeMs: 24551`
- `FreezeAssignNodeIdsMs: 6403`
- `FreezeBuildQueryIndexMs: 18145`
- `FreezeOrderEdgesMs: 2047`
- `FreezeOrderNodesMs: 786`
- `FreezeSnapshotHashMs: 2910`
- `FreezeBuildAdjacencyMs: 2813`
- `FreezeBuildKindAdjacencyMs: 7922`
- `FreezeBuildEdgeKindIndexMs: 224`
- `FreezeBuildNodeKindIndexMs: 545`
- `FreezeBuildFilePathIndexMs: 894`
- `FreezeDistinctAnchors: 1953134`

当前结论：

1. `Freeze` 的大头不在 `SnapshotHash`，而在 `QueryIndex` 建表。
2. `BuildKindAdjacency` 是目前已观测的单项最大热点。
3. `AssignDeterministicNodeIds` 也不轻，且包含重复 anchor 计算和全量 remap。
4. `FreezeDistinctAnchors == NodeCount` 的样例表明这类大文件上 anchor 重复率至少不高，`Distinct()` 值得怀疑。

---

## 2. 问题分解

### 2.1 `AssignDeterministicNodeIds()` 里 anchor 被重复计算

当前代码在同一批节点上做了两轮 anchor 计算：

- 第一轮生成 `anchors`
- 第二轮在 `remappedNodes` 中再次计算相同 anchor

相关位置：

- `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- `src/MinimalRoslynCpg/Model/StableNodeAnchor.cs`

问题：

- 同一个 node 的 `StableAnchor.CreateFallback(...)` 被执行两次
- `CreateFallback(...)` 内部又会调用 `StringInterner.Intern(...)`
- 这会额外带来字符串字典查找和 fallback anchor 构造成本

### 2.2 `DeterministicNodeIdTable.Create(...)` 做了去重 + 排序 + 物化

当前实现：

- `Distinct()`
- `OrderBy(...).ThenBy(...)`
- `ToArray()`

相关位置：

- `src/MinimalRoslynCpg/Model/DeterministicNodeIdTable.cs`

问题：

- 这是一次额外的全量哈希去重
- 之后还有一次全量排序
- 再之后还有一次数组物化
- 若上游已能保证 anchor 唯一，则 `Distinct()` 是纯成本

### 2.3 `remappedNodes / remappedEdges` 是全量复制式 finalize

当前冻结不是“就地补 NodeId 并收口”，而是：

- 重建整批节点
- 重建整批边
- 清空旧容器
- 重建新容器

相关位置：

- `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`

问题：

- 分配大
- GC 压力高
- cache locality 差
- 虽然 record 不可变限制了彻底消除，但中间 `ToArray()` / `ToDictionary()` 仍有明显压缩空间

### 2.4 `RoslynCpgGraphIndex.Create(...)` 对同一批边做了多轮分组

当前按 `orderedEdges` 分别构建：

- outgoing adjacency
- incoming adjacency
- outgoing kind adjacency
- incoming kind adjacency
- `EdgesByKind`

相关位置：

- `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`

问题：

- 多次 `GroupBy(...).ToDictionary(...).ToArray()`
- 每轮都重新枚举整批边
- `BuildKindAdjacencyMs` 已经显示这是主要热点

---

## 3. 优化目标

优化后应满足：

1. `NodeId` 分配结果与当前完全一致。
2. `GraphSnapshotVersion` 结果与当前完全一致。
3. `GetOutgoingEdges` / `GetIncomingEdges` / `GetEdges(kind)` / `GetNodes(kind)` / `GetNodesInFileSpan(...)` 查询结果完全一致。
4. `FreezeTelemetry` 继续保留细分项，便于对比优化前后收益。
5. 至少在 `NPC.cs` / `Player.cs` / `WorldGen.cs` 三个样本上，`FreezeMs` 均值下降；若下降不明显，不继续扩大改动范围。

---

## 4. 方案选择

### 方案 A：先只合并 `CreateKindAdjacency(...)`

优点：

- 风险最低
- 直接命中当前最大热点

缺点：

- 只处理部分重复遍历
- `AssignDeterministicNodeIds()` 和普通 adjacency 仍保留冗余成本

### 方案 B：按冻结子阶段分 3 步落地

1. 缓存 `node -> anchor`
2. 合并 `kind adjacency` 与 `adjacency` 多轮建表
3. 评估并压缩 `Distinct()` / remap 中间物化

优点：

- 收益路径清晰
- 每步都能独立验证
- 失败时容易回退到上一步

缺点：

- 需要 2-3 轮提交或至少 2-3 轮验证

### 方案 C：重写 freeze 为单次流式 finalize

优点：

- 理论收益最大

缺点：

- 风险过高
- 一次性改动太大
- 难以定位回归来源

**Recommendation:** 采用 **方案 B**。

---

## 5. 可执行实施步骤

### Phase 0：先锁定回归与基线

**Files:**

- Read/verify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Read/verify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/PipelineComponentTests.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Read/verify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

**Goal:**

- 确认 freeze 细分 telemetry 已在测试中可见
- 记录优化前 `NPC.cs / Player.cs / WorldGen.cs` 的 `Freeze*Ms` 均值

**Exit criteria:**

- 有可对照的 freeze 子阶段基线

### Phase 1：缓存 `node -> anchor`

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`

**Changes:**

- 在 `AssignDeterministicNodeIds()` 中一次性生成 `anchoredNodes`
- 后续 `CreateNodeIdTable` 与 `remappedNodes` 都复用同一批 anchor
- 不再对同一 node 调两次 `StableNodeAnchor.CreateFallback(...)`

**Expected impact:**

- 降低 `FreezeCreateAnchorsMs`
- 轻微降低 `FreezeAssignNodeIdsMs`
- 不改变外部语义

**Verification:**

- `dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false`
- `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false`
- 单文件复跑 `NPC.cs`

**Exit criteria:**

- 查询/图结果无回归
- `FreezeCreateAnchorsMs` 下降或持平

### Phase 2：合并 `CreateKindAdjacency(...)` 的重复分组/分配

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`

**Changes:**

- 用单次顺序扫描 `orderedEdges` 同时构建：
  - outgoing kind adjacency
  - incoming kind adjacency
  - `EdgesByKind`
- 去掉基于同一批边的多轮 `GroupBy(...).ToDictionary(...).ToArray()`

**Expected impact:**

- 直接降低 `FreezeBuildKindAdjacencyMs`
- 可能同时降低 `FreezeBuildEdgeKindIndexMs`
- 降低中间数组与 group object 分配

**Verification:**

- 同 Phase 1
- 再跑 `NPC.cs / Player.cs / WorldGen.cs`

**Exit criteria:**

- `FreezeBuildKindAdjacencyMs` 显著下降
- 整体 `FreezeMs` 下降

### Phase 3：合并 adjacency 构建的多轮遍历

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`

**Changes:**

- 单次扫描 `orderedEdges` 同时构建：
  - outgoing adjacency
  - incoming adjacency
- 视实现情况，可与 Phase 2 的 kind adjacency 合并成一次总扫描

**Expected impact:**

- 降低 `FreezeBuildAdjacencyMs`
- 进一步减少边分组的中间物化

**Verification:**

- 同 Phase 2

**Exit criteria:**

- `FreezeBuildAdjacencyMs` 下降
- `FreezeMs` 继续下降，且没有引入查询结果回归

### Phase 4：评估 `Distinct()` 与 remap 全量复制的必要性

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/DeterministicNodeIdTable.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`

**Changes:**

- 先加观测而不是直接删逻辑：
  - `AnchorCount`
  - `DistinctAnchorCount`
  - `DuplicateAnchorCount`
- 若多文件基线证明重复率接近 0：
  - 评估删掉 `Distinct()`，改由上游保证唯一性
- 审查 `remappedNodes` / `remappedEdges` 的中间容器是否能减少一轮 `ToArray()` 或 `ToDictionary()`

**Expected impact:**

- 可能降低 `FreezeCreateNodeIdTableMs`
- 可能降低 `FreezeAssignNodeIdsMs`
- 但风险高于前 3 个 phase

**Verification:**

- 同前
- 必须增加 `NodeId` 稳定性相关回归全跑

**Exit criteria:**

- 只有在重复率低且回归安全时才推进
- 若收益不显著，停止，不继续扩大

### Phase 5：最后再评估 `SnapshotHash`

**Files:**

- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`

**Why last:**

- 当前数据里它不是最大热点
- 它绑定 `GraphSnapshotVersion` 契约，回归风险比索引建表更高

**Only if:**

- 前 4 个 phase 做完后，`FreezeSnapshotHashMs` 仍占 freeze 大头

**Possible directions:**

- 减少 `AppendString()` 的重复 UTF8 分配
- 评估更低分配的编码写法
- 保持 hash 输入语义完全不变

---

## 6. 验证矩阵

每个 phase 完成后至少执行：

```powershell
$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~PipelineComponentTests" -p:UseSharedCompilation=false
```

阶段性性能验证：

```powershell
dotnet run --no-build --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR\Terraria\NPC.cs" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff
dotnet run --no-build --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR\Terraria\Player.cs" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff
dotnet run --no-build --project .\src\RoslynPrototype\RoslynPrototype.csproj -- "D:\lodes\TR\Backup\New1.27\1.45 2\TR\Terraria\WorldGen.cs" --delete-class PlayerInput --max-degree-of-parallelism 16 --skip-rewrite --no-diff
```

最终验收建议：

- 三个样本各跑 10 次取均值
- 关注：
  - `FreezeMs`
  - `FreezeAssignNodeIdsMs`
  - `FreezeBuildAdjacencyMs`
  - `FreezeBuildKindAdjacencyMs`
  - `FreezeSnapshotHashMs`

---

## 7. 风险与回退

### 主要风险

1. adjacency / kind adjacency 合并后，边顺序或分组键细节变化，影响查询稳定性
2. `Distinct()` 处理过早删除，若实际有重复 anchor，会破坏 `NodeId` 分配
3. 降低中间容器数时误伤 `GraphSnapshotVersion` 或 `NodeId` 稳定性

### 回退策略

- 每个 phase 单独提交或至少单独 patch
- 一旦 `NodeId` / query / structure view 回归，直接回退最近一个 phase
- 不跨 phase 混改

---

## 8. 完成条件

本提案可标记完成，当且仅当：

1. 至少完成 Phase 1-3
2. `NPC.cs / Player.cs / WorldGen.cs` 的 10 次均值显示 `FreezeMs` 相对当前基线下降
3. 所有定向冻结/query 回归通过
4. 若进入 Phase 4/5，必须有数据证明值得继续；否则停止在较低风险阶段

---

## 9. 建议执行顺序

1. `node -> anchor` 复用
2. 合并 `kind adjacency`
3. 合并普通 adjacency
4. 观测后再决定 `Distinct()` / remap 压缩
5. 最后才看 `snapshot hash`

这个顺序的理由是：

- 前三项都属于“重复工作压缩”，风险低、收益直接
- `Distinct()` 和 remap 结构调整需要更强的正确性验证
- `snapshot hash` 当前不是第一热点，且契约风险更高，不值得先动
