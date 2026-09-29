# CPG 能力边界

> 状态：当前设计。
>
> 读者：需要判断一条删除规则能否依赖 CPG 事实的维护者。
>
> 使用原则：先查本页的“保证”，再查实现和测试；找不到证明时按限制处理。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 快速判断

CPG 能证明的是“图中已物化且通过验证的事实”，不是“所有真实语义都已覆盖”。规则可以组合 Roslyn `SyntaxNode`、`SemanticModel`、`IOperation` 和下表能力；如果事实缺失、被预算截断或标为 unavailable，规则必须保留源码或输出 `Skip`。

## 当前可依赖能力

| 能力 | 可以依赖的事实 | 主要代码锚点 |
| --- | --- | --- |
| 源码与符号 | 节点、token、声明、引用和源码 span 关联 | `src/NLCPG/Builder/Passes/SyntaxPass.cs` |
| operation | 语法节点与 `IOperation` 的关联、操作结构 | `OperationPass.cs`、`PartitionedOperationPass.cs` |
| 方法边界 | 方法、参数、返回值、entry、exit | `MethodDecorationPass.cs` |
| 控制流 | 方法内顺序、条件、循环、返回及部分异常边 | `ControlFlowPass.cs` |
| 局部数据流 | 局部变量和参数的保守 reaching-definition | `DataFlowPass.cs` |
| 调用与成员 | 直接调用目标、参数连接、部分 receiver/member 关联 | `CallGraphPass.cs`、`MemberAccessPass.cs` |
| 结构视图 | 已知语法片段的节点和最短连接路径 | `src/NLISSN.Core/Analysis/View/NLCPGStructureViewBuilder.cs` |
| 有界切片 | 稳定索引上的正向/反向遍历，显式报告截断 | `src/NLCPG/Analysis/NLCPGSliceQuery.cs` |
| 分片查询 | 按 node、symbol 或 span 定位已完成 shard；缺 frontier 返回 unavailable | `CpgShardQueryResolver.cs`、`CpgShardRelationQueryService.cs` |
| routing sidecar | 对已完成构建做版本、长度和 SHA-256 校验后的路由恢复 | `src/NLCPG/Persistence/CpgBuildRoutingIndexReader.cs` |

## 规则使用方式

```text
需要一个语义结论
  -> 查 capability 和查询预算
  -> 读取 Roslyn/CPG fact
  -> 检查 unknown / unavailable / truncated
  -> 有完整证明才生成下阶段事实
  -> 否则保留源码并记录 Skip 或诊断
```

“存在 `CallTargets` 边”只能说明当前图记录了一条直接调用关系，不能自动升级为完整跨过程可达性、虚派发闭包或外部库行为证明。规则若需要 compilation-wide 根集合，应走 [Fast Path 边界](fast-path-boundaries.md) 或明确的 MethodGlobal 分析，而不是从局部切片猜全局结论。

## 明确限制

- 没有完整 interprocedural data flow、全局调用图或所有 virtual/dynamic dispatch。
- 外部程序集 summary、类型恢复和成员访问所有权不完整。
- CFG 不是支配分析引擎；DataFlow 不是通用 DDG。
- `NLCPGStructureView` 只复制主图事实，不生成新的分析边。
- 分片查询不负责跨项目解析，并受到 hop、path、call-depth、打开 shard 数和读取字节预算限制。
- 构建失败、源文件缺失、路由校验失败和 frontier 不完整都不能被降级成“没有边”。

## 验证入口

核心行为由 `tests/NLISSN.ContractTests/Cpg/NLCPGPartitionedBuilderTests.cs`、`CpgExecutionMatrixTests.cs`、`CpgBuildRoutingIndexTests.cs`、`tests/NLISSN.UnitTests/Application/CpgRelationQueryUnitTests.cs` 和 `StructureViewBuilderTests.cs` 覆盖。新增 capability 至少需要一个正例、一个边界或失败例，以及 DOP 等价断言。

## 相关页面

- 构图顺序、worker 约束和冻结发布：[CPG 架构](cpg-architecture.md)
- 能力扩展的优先级和性能证据门槛：[CPG 演进优先级](cpg-roadmap.md)
