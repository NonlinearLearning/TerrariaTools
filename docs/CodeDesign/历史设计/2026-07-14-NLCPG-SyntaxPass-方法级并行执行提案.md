# MinimalRoslynCpg SyntaxPass 方法级并行执行提案

> AI 阅读入口：任务涉及 `SyntaxPass`、`RoslynCpgSyntaxPassTelemetry`、`RoslynCpgBuilder` 的超大文件吞吐、方法级并行、Syntax `HasType`、symbol/type 节点收口或 legacy/partitioned 图等价时，先读本文。

## 1. 目标与边界

目标是在不改变图结构的前提下，缩短超大单文件的 SyntaxPass wall-clock 时间。

WorldGen.cs 的最近一次 partitioned 构图中，外层构图为 `30146ms`，SyntaxPass 为 `21089ms`。其中 `ResolveTypeInfo` 为 `8923ms`，Syntax node 创建为 `1131ms`，引用 symbol 边为 `499ms`。SyntaxPass 仍是当前最大的单 pass 时间来源。

本提案只并行方法体内的语义事实收集。以下操作必须继续串行：

1. Syntax node、token 和 `SyntaxChild` 边创建；
2. 主图写入；
3. `_syntaxNodes`、symbol/type/method 节点字典和 sequence 字段写入；
4. `HasType`、TypeRef、declared/reference symbol 边 materialize；
5. operation-backed Syntax type 的 pending/fallback 收口；
6. 后续 `MethodDecorationPass`、OperationPass、CallGraphPass、MemberAccessPass、ControlFlowPass 和 DataFlowPass。

不做：

- worker 直接写 `RoslynCpgGraph`；
- 将 local function、lambda 或 anonymous method 变成独立 partition；
- 改写删除规则执行单元；
- 删除或缩减 Syntax node、token、`HasType`、TypeRef 边；
- 默认对所有输入启用并行 SyntaxPass。

## 2. 当前事实

`SyntaxPass.CreateSyntaxNode(...)` 在一次深度优先遍历中同时完成结构图写入、声明 symbol、引用 symbol、TypeInfo 和 TypeRef 边。它直接修改 `_syntaxNodes`，并通过 `GetOrCreate` 路径修改共享图和节点字典，因此不能直接包进 `Parallel.ForEach`。

现有 `PartitionedOperationPass` 已采用安全模式：并发对每个 `OperationRootPlan` 生成只读 record，再按 `Order` 串行 materialize。这是 SyntaxPass 的复用模式。

Operation root 的当前边界是普通方法、构造函数、属性 accessor 和 global statement。方法体外的成员头、参数、attribute、base type、field/property initializer、类型约束仍需要文件级处理。

## 3. 设计

### 3.1 四阶段管线

```text
serial skeleton
  -> partition planning
  -> bounded semantic analysis
  -> stable semantic materialization
```

#### A. serial skeleton

沿用当前 SyntaxPass 的确定性深度优先顺序，但只负责：

- 创建 Syntax node、token、`SyntaxChild` 和 token child 边；
- 填充 `_syntaxNodes`；
- 给每个 Syntax node 分配单调递增的 `SyntaxOrdinal`；
- 标记属于方法体 partition 的 Syntax subtree；
- 对文件级语法创建串行 semantic work item。

`SyntaxOrdinal` 必须反映 legacy 版本调用语义边 materialize 的顺序。不能只用 `SpanStart`，因为父子节点可以具有相同或嵌套 span。

#### B. partition planning

复用 `GetOperationRootPlans(...)` 的方法、构造函数、accessor 和 global statement 边界。每个 plan 的 `BodySyntax` 形成一个 Syntax semantic partition。

分片只包含 `BodySyntax.DescendantNodesAndSelf()`。local function、lambda 和 anonymous method 留在外层 body partition，避免 capture、owner 和调用归属被拆开。

文件级 work item 负责所有不属于任一 partition 的 Syntax node，包含成员声明、方法头、参数、attribute、field initializer、type syntax 和泛型约束。

#### C. bounded semantic analysis

新增只读 `SyntaxSemanticPartitionAnalyzer`。它接收 `SemanticModel` 和 partition plan，输出按 `SyntaxOrdinal` 排序的 `SyntaxSemanticRecord`：

```csharp
private sealed record SyntaxSemanticRecord(
    SyntaxNode Syntax,
    int SyntaxOrdinal,
    ISymbol? DeclaredSymbol,
    ISymbol? ReferencedSymbol,
    ITypeSymbol? SyntaxType,
    ITypeSymbol? TypeReferenceTarget,
    SyntaxTypeSource TypeSource);
```

`SyntaxTypeSource` 至少表达：未请求、SymbolInfo 复用、直接 SemanticModel、等待 operation、operation fallback。

worker 使用与当前实现相同的查询规则：`GetDeclaredSymbol(...)`、受白名单控制的 `GetSymbolInfo(...)`、`ResolveSyntaxTypeSymbol(...)` 和 TypeRef 目标解析。worker 不调用图写入或 builder 的共享 `GetOrCreate` 方法。

worker 调度使用有界窗口，最多保留 `EffectiveMaxDegreeOfParallelism` 个未 materialize 的结果。禁止先创建全部 partition record；WorldGen 的百万级 Syntax node 会使 `ISymbol` 和 `ITypeSymbol` 引用的峰值内存不可控。

#### D. stable semantic materialization

主线程将文件级 work item 和 partition result 合并为一个按 `SyntaxOrdinal` 的流，调用从现有方法拆出的 materialize helper：

- `AddDeclaredSymbolEdges(syntaxNode, declaredSymbol, graph)`；
- `AddReferencedSymbolEdges(syntax, syntaxNode, referencedSymbol, graph)`；
- `AddTypeEdges(syntaxNode, syntaxType, graph)`；
- `AddTypeReferenceEdges(...)` 的无查询 materialize 版本；
- operation-backed pending 标记。

所有 symbol/type/reference sequence 和图写入仍在这一阶段发生。这样 partition 即使完成顺序不同，图结果仍与 legacy 的语义边发射顺序一致。

## 4. 配置与 telemetry

新增独立配置，避免改变现有 operation partition 默认行为：

```csharp
public enum RoslynCpgSyntaxPassMode
{
    Legacy,
    Auto,
    Partitioned,
}
```

`Auto` 的第一版启用条件：

1. source line count 达到现有 large-file 阈值；
2. syntax partition 数量至少为 2；
3. 最大 partition 不占全部可分片 Syntax node 的大多数。

telemetry 新增：

- `SyntaxSkeletonElapsedMilliseconds`；
- `SyntaxSemanticAnalysisElapsedMilliseconds`；
- `SyntaxSemanticMaterializationElapsedMilliseconds`；
- `SyntaxPartitionCount`；
- `SyntaxPartitionMaxDegreeOfParallelism`；
- 每 partition 的 Syntax node 数、直接 TypeInfo 查询数和分析耗时。

既有 SyntaxPass telemetry 保持兼容；新分项只补充定位能力。

## 5. 实施步骤

### 阶段一：并发可行性探针

1. 新增测试，在同一 `SemanticModel` 上并发执行既有三类查询：`GetDeclaredSymbol`、`GetSymbolInfo`、`GetTypeInfo`。
2. 连续多次比较每个 Syntax node 的 symbol/type 显示 key、null 状态和诊断结果。
3. 与单线程基线不一致、抛异常或存在不稳定结果时停止；本提案不假设该 Roslyn 版本的惰性缓存可安全并发。

### 阶段二：拆分 SyntaxPass

1. 提取 skeleton materialize、semantic analyze、semantic materialize 三个私有路径。
2. 保持 `Legacy` 模式仍按原 traversal 顺序调用三段逻辑，先锁住完整图等价。
3. 为每个 Syntax node 登记稳定 `SyntaxOrdinal`，并验证它与 legacy semantic 边发射顺序一致。

### 阶段三：文件级与方法体 work item

1. 复用 `OperationRootPlan` 生成方法体 partition。
2. 标记 partition subtree，构造文件级 work item，保证没有漏节点和重叠节点。
3. 新增“所有 SyntaxOrdinal 恰好被一个 semantic work item 消费”的调试断言和测试。

### 阶段四：有界 worker 与稳定收口

1. 实现 `SyntaxSemanticPartitionAnalyzer` 和只读 record。
2. 采用有界任务窗口和按 ordinal drain；worker 结果不进入共享 graph。
3. 在主线程 materialize 全部 semantic record，并复用现有边发射 helper。
4. 将 operation-backed pending/fallback 路径留在主线程，保持当前 TypeInfo telemetry 语义。

### 阶段五：Auto 与 telemetry

1. 新增 SyntaxPass mode 和独立阈值配置。
2. `Auto` 默认保持 legacy，先以 opt-in `Partitioned` 验证。
3. 记录 serial skeleton、parallel analysis、serial materialization 和队列等待时间。

## 6. 测试矩阵

| 类别 | 验证 |
| --- | --- |
| 图等价 | legacy 与 partitioned SyntaxPass 的完整 node/edge 集合相等 |
| 类型图 | `HasType`、TypeRef、相关边集合相等 |
| 组合模式 | partitioned SyntaxPass + partitioned OperationPass 与 legacy 完整图相等 |
| 语法边界 | method header、field initializer、attribute、base type、泛型约束、accessor、global statement |
| 嵌套边界 | local function、lambda、anonymous method、method group、dynamic、错误恢复 |
| 稳定性 | DOP `1`、`2`、`16` 各连续构建至少 10 次，序列化图相等 |
| 并发探针 | 同一 SemanticModel 的并发读结果与串行基线相等 |
| 性能 | 小样例、PipelineComponentTests.cs、WorldGen.cs 使用固定 Release 配置复测 |

## 7. 验收与停止条件

只有同时满足以下条件时才允许保留 partitioned SyntaxPass：

1. 所有图等价和稳定性测试通过；
2. 共享 SemanticModel 并发读探针稳定；
3. WorldGen.cs 的 partitioned SyntaxPass wall-clock 低于 legacy 基线；
4. 峰值内存没有因为 shard record 缓冲显著恶化；
5. 不把 graph、symbol/type dictionary 或 sequence 写入移入 worker。

发生以下任一情况时停止并保留 legacy：

- 图快照、`HasType`、TypeRef 或 reference 边出现差异；
- 同一 DOP 的重复构建不稳定；
- SemanticModel 并发查询不稳定；
- serial materialization 和 record 缓冲抵消并发收益；
- 最大方法体导致 worker 粒度不足。

## 8. 代码影响面

- `src/MinimalRoslynCpg/Builder/Passes/SyntaxPass.cs`
- `src/MinimalRoslynCpg/Builder/RoslynCpgBuilder.cs`
- `src/MinimalRoslynCpg/Builder/RoslynCpgBuilderOptions.cs`
- 新增 `src/MinimalRoslynCpg/Builder/Passes/PartitionedSyntaxPass.cs`
- `tests/RoslynDeletionPrototype.Tests/MinimalRoslynCpgPartitionedBuilderTests.cs`

`OperationPass`、CFG、DataFlow、删除规则和 CLI 不在第一版改动范围内。
