# `ContextId` 强类型化与后续结构化执行提案

**Status (2026-07-16):** Phase 1 与 Phase 2 均已完成并通过 focused build/test 验证；`ContextId` 保留稳定键角色，callsite 上下文已补结构化字段。

**Goal:** 把 `RoslynCpgEdge` 上的 `string? ContextId` 收紧成 `RoslynCpgContextId?`，先消除裸字符串弱类型契约，再单独评估是否把当前 `"callsite:{file}:{spanStart}:{spanEnd}:{displayName}"` 编码串拆成真正结构化字段。

**Scope:** 只覆盖 `ContextId` 的类型收紧、调用点构造路径、切片查询消费路径、图排序/哈希/测试断言，以及后续结构化字段拆分的执行边界；不改 `StructuredLabel` 契约，不改 `InterproceduralDataFlow` 语义，不改图遍历算法，不引入新依赖。

**Non-goals:**

- 不在本提案第一步里直接把 `ContextId` 拆成 `file/span/displayName` 三段或更多字段
- 不改 `RoslynCpgEdgeLabel` 现有设计
- 不改变 `RoslynCpgSliceQuery` 的 budget、截断原因或路径语义
- 不把这项工作扩展成任意 edge metadata 全量重构

---

## 1. 问题陈述

当前 `ContextId` 已经不是普通展示文本，而是带稳定身份语义的上下文键：

- `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`
  - `ContextId` 仍然是 `string?`
- `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
  - `BuildPendingNodeContextId(...)` 生成 `"callsite:{file}:{spanStart}:{spanEnd}:{displayName}"`
- `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
  - 反向切片在跨过程边上把 `ContextId` 当作调用栈 frame id
  - 递归去重和 `callStackCycle` 截断直接依赖这个值
- `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
  - 边排序和图快照哈希把 `ContextId` 当稳定内容参与

结论：

- 它已经是运行时身份契约，不是“可有可无的备注字符串”
- 继续暴露成裸 `string`，调用方可以随意传任意文本，难以表达“这是上下文身份，不是自由文本”
- 但当前还不能直接假设它只会服务于一种内部结构；现阶段最稳的收口方式是先做强类型包装，再决定是否拆成真正结构化字段

---

## 2. 目标契约

本提案完成后，应分成两个层次：

### Phase 1 完成后的目标契约

- `RoslynCpgEdge.ContextId` 从 `string?` 改为 `RoslynCpgContextId?`
- `RoslynCpgBuilder.BuildPendingNodeContextId(...)` 返回 `RoslynCpgContextId`
- `RoslynCpgSliceQuery`、`RoslynCpgGraphIndex`、测试和辅助函数只通过 `RoslynCpgContextId` 读写上下文键
- `RoslynCpgContextId` 仍包裹一个稳定字符串值；不改变现有 `"callsite:..."` 编码格式

### Phase 2 完成后的目标契约

在当前工作树中，callsite 上下文已经拆成真正结构化字段：

- `RoslynCpgCallSiteContext`
- `filePath`、`spanStart`、`spanEnd`、`displayName` 作为独立字段保存
- `RoslynCpgContextId` 继续保留为稳定键，但当 `CallSiteContext` 存在时必须由该结构派生

---

## 3. 总体策略

采用“两步走”：

1. 先把 `ContextId` 从弱类型字符串收紧成强类型值对象
2. 再基于第一步落地后的实际使用面，决定是否值得继续拆掉内部编码串

这样做的原因：

- 第一阶段风险低，收益清晰
- 第二阶段会改变更深的存储与比较契约，应该单独决策
- 避免把“强类型化”与“内部字段重建”混成一个高风险 patch

---

## 4. Phase 1：把 `string? ContextId` 收紧成 `RoslynCpgContextId?`

### 4.1 目标

把 `ContextId` 从自由字符串升级为语义明确的稳定上下文键值对象，但保持其当前内容编码不变。

### 4.2 推荐设计

新增：

```csharp
public readonly record struct RoslynCpgContextId(string Value)
{
  public override string ToString() => Value;
}
```

然后统一改成：

```csharp
RoslynCpgContextId? ContextId = null
```

选择 `readonly record struct` 的原因：

- 保留值语义、相等语义和哈希语义
- 比 `class` 更轻，适合当前这种排序、哈希、集合键使用方式
- 比 type alias 或注释更能阻止误传任意字符串

### 4.3 影响文件

**Files:**

- Add: `src/MinimalRoslynCpg/Model/RoslynCpgContextId.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/StructureViewBuilderTests.cs`

### 4.4 实施要点

1. `RoslynCpgEdge.ContextId` 改签名
2. `RoslynCpgGraph.AddEdge(...)` 以及 `PendingEdge` 跟进改签名
3. `BuildPendingNodeContextId(...)` 返回 `RoslynCpgContextId`
4. `RoslynCpgSliceQuery.GetCallSiteFrame(...)` 改为读取 `edge.ContextId.Value`
5. `RoslynCpgGraphIndex` 的排序和哈希继续按 `ContextId.Value` 参与稳定计算
6. 手工构造边的测试改成显式传 `new RoslynCpgContextId("...")`

### 4.5 验证

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~StructureViewBuilderTests" -p:UseSharedCompilation=false
pwsh -File .\scripts\check-harness-consistency.ps1
```

### 4.6 Done when

- 代码中不再存在 `string? ContextId` 公共契约
- 所有消费方都显式使用 `RoslynCpgContextId`
- focused tests 通过，且图快照稳定性未破坏

**Execution result (2026-07-16):**

- Done: `RoslynCpgEdge.ContextId`、`RoslynCpgGraph.AddEdge(...)`、`PendingEdge`、`BuildPendingNodeContextId(...)`、`RoslynCpgSliceQuery` 和相关测试都已切到 `RoslynCpgContextId`
- Verified: `dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false`
- Verified: `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests" -p:UseSharedCompilation=false`
- Verified: `pwsh -File .\scripts\check-harness-consistency.ps1`

### 4.7 风险

#### 风险 1：只是包了一层，内部仍是字符串，看起来“改得不够深”

缓解：

- 这是刻意的第一阶段
- 先解决弱类型问题，再评估是否值得做第二阶段结构化拆分

#### 风险 2：排序/哈希/测试辅助函数漏改

缓解：

- 所有稳定排序和快照哈希统一走 `ContextId.Value`
- focused tests 覆盖切片、结构视图和跨过程边构造

---

## 5. Phase 2：把内部 `"callsite:..."` 编码字符串改成真正结构化字段

### 5.1 目标

把 callsite 结构从手工编码字符串提升为真正结构化字段，同时保留 `RoslynCpgContextId` 作为稳定键。

### 5.2 实施设计

新增：

```csharp
public readonly record struct RoslynCpgCallSiteContext(
  string FilePath,
  int SpanStart,
  int SpanEnd,
  string DisplayName);
```

并把 `RoslynCpgEdge` 扩成：

- `RoslynCpgContextId? ContextId`
- `RoslynCpgCallSiteContext? CallSiteContext`

约束：

1. 若 `CallSiteContext` 非空，`ContextId` 必须由 `CallSiteContext.ToContextId()` 派生
2. 若两者同时传入但不一致，构造边时直接抛异常
3. `RoslynCpgSliceQuery` 优先使用 `CallSiteContext` 派生 frame id
4. `RoslynCpgGraphIndex` 的排序和哈希同时覆盖 `ContextId` 与结构化字段

### 5.3 影响文件

**Files:**

- Add: `src/MinimalRoslynCpg/Model/RoslynCpgCallSiteContext.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgEdge.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraph.cs`
- Modify: `src/MinimalRoslynCpg/Model/RoslynCpgGraphIndex.cs`
- Modify: `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- Modify: `src/MinimalRoslynCpg/Analysis/RoslynCpgSliceQuery.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/RoslynCpgSliceQueryTests.cs`
- Modify: `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

### 5.4 验证

```powershell
dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests" -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false
```

### 5.5 Done when

- `RoslynCpgCallSiteContext` 已落地
- builder 构造的跨过程边同时带 `ContextId` 与 `CallSiteContext`
- 切片查询、排序、哈希和 focused tests 通过

**Execution result (2026-07-16):**

- Done: `RoslynCpgCallSiteContext` 已新增，`RoslynCpgEdge`、`RoslynCpgGraph`、`RoslynCpgGraphIndex`、`RoslynCpgBuilder`、`RoslynCpgSliceQuery` 和 focused tests 已完成迁移
- Verified: `dotnet build .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-restore -p:UseSharedCompilation=false`
- Verified: `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build --filter "FullyQualifiedName~RoslynCpgSliceQueryTests|FullyQualifiedName~MinimalRoslynCpgPartitionedBuilderTests|FullyQualifiedName~StructureViewBuilderTests|FullyQualifiedName~RoslynCpgNodeIdContractTests" -p:UseSharedCompilation=false`
- Verified: `pwsh -File .\scripts\check-harness-consistency.ps1`

---

## 6. 实施顺序建议

建议按以下顺序：

1. 先做 Phase 1 强类型包装
2. 跑 focused tests，确认没有语义漂移
3. 回读所有 `ContextId` 生产/消费点，确认是否存在第二类上下文来源
4. 再确认提案、进度和 feature metadata 已同步记录完成状态

不要把两阶段合并成一个 patch。

---

## 7. 主要风险与缓解

### 风险 1：结构化字段与稳定键漂移

缓解：

- 当 `CallSiteContext` 存在时，`ContextId` 必须从它派生
- 构造不一致时立即抛异常，不允许静默漂移

### 风险 2：后续出现非 callsite 上下文，导致当前结构只覆盖一类上下文

缓解：

- 保留通用 `RoslynCpgContextId`
- 仅把当前已确认的 callsite 来源收敛到 `RoslynCpgCallSiteContext`

### 风险 3：测试仍把 `ContextId` 当自由字符串断言

缓解：

- focused tests 改成显式比较 `RoslynCpgContextId`
- 需要看内部值时，通过 `.Value` 断言

---

## 8. 完成条件

- `ContextId` 强类型化已完成
- `RoslynCpgCallSiteContext` 已落地
- 提案、代码、测试和 handoff 文件都已记录完成状态

---

## 9. 建议决策

建议保留当前设计，不继续把所有上下文统一抽象成更重的上下文层次。

原因：

- 当前 callsite 上下文已经结构化
- 稳定键契约和切片语义都还保持兼容
- 继续上升抽象层级只会扩大复杂度，不会带来当前分支需要的收益
