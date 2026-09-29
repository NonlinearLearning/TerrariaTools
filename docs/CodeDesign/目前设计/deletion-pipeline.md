# 删除规则流水线

> 状态：当前设计。
>
> 读者：新增删除规则、修改阶段契约或排查“为什么没有删除”的维护者。
>
> 本页只定义 staged rule contract；某个规则的 payload 和 rewrite 形状放在专题页。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 一次分析的主链

```text
source files
  -> Roslyn compilation + semantic facts
  -> Mark
  -> Propagate
  -> Lift
  -> Propose
  -> Decision / evidence
  -> RewriteEdit / diff / optional write-back
```

生产路径由 `src/NLISSN.Application/Analysis/ApplicationService.cs` 编排，图分析由 `RuleGraphAnalysisExecutor.cs` 负责一次提交已编译的规则图。`src/NLISSN/Hosting/CommandHost.cs` 负责输入、配置和 artifact 边界；`src/NLISSN/Hosting/DirectoryAnalysisService.cs` 负责目录输入的文件枚举与结果聚合。

## 阶段契约

| 阶段 | 输入 | 允许产物 | 明确禁止 |
| --- | --- | --- | --- |
| Mark | `RuleContext`、语法根、`MarkAnalysisSnapshot` | 原子 `MarkRecord` | 标记整个 `if`/方法以代替证明；扫描全 compilation 代替局部事实 |
| Propagate | 图边提供的 seed marks、Roslyn facts | `PropagatedMarkRecord` 和结构化 payload | 生成最终 replacement syntax；把 payload 当作宽泛删除授权 |
| Lift | seed/propagated marks 和 topology | `LiftedMarkRecord`、结构宿主事实 | 重新做全局 usage 搜索；越过 unary/conditional/assignment terminal |
| Propose | 各阶段已验证的 marks/payloads | `DecisionUnit`、`RuleDecision` | 重做传播；用父节点 span 推测完整覆盖 |
| Rewrite | 最终 decisions | `RewriteEdit`、重写文本、结构化 diff | 重新匹配规则；根据 diff 文本反推安全语义 |

阶段记录必须保留 rule id、syntax kind、span、semantic tag 和 provenance。一个阶段没有输出也必须释放下游依赖；disabled、completed-empty、cancelled 和 failed 是不同状态，不能用空列表混淆。

## 阶段之间如何连接

规则通过 `Consumes` / `Produces` 声明 `SyntaxKind`、`RuleSemanticTag` 和 cardinality。普通依赖由 [规则 DAG](规则DAG.md) 调度；`Propagate` 内部的递归闭包使用显式 fixed-point region。外层仍保持 Mark -> Propagate -> Lift -> Propose 的无环边界。

表达式传播使用 `ExpressionPropagationTopology` 解析 Roslyn 直接操作数。`&&` 只传播到同一链的 direct sibling，`||` 不向 sibling 扩散；一元、三元、赋值和未建模操作到 terminal 停止。Lift 和 Propose 消费 topology 结果，不再各自使用 `Ancestors()` 或宽 span 过滤猜 owner。

## 失败关闭

规则遇到未绑定符号、动态访问、缺少 compilation-wide 证明、查询截断、结构不完整或 proposal 冲突时，输出 `Skip`、diagnostic 或保留源码。只有经过 `RuleBindingValidator`、`DecisionBindingValidator` 和结构检查的 decision 才能进入 rewrite。

配置校验失败、replay artifact 哈希不匹配、源码 span 失配和写回目标不安全时，在分析或回放前停止；不要先生成部分 write-back 再补诊断。

## Fast path 的位置

需要目录或 compilation-wide 信息的规则可以由 Host 走专用入口，但结果必须仍然进入统一的 decision、evidence、rewrite 和 diff 契约。边界和迁入 staged pipeline 的条件见 [Fast Path 边界](fast-path-boundaries.md)。

## 验证要求

修改阶段契约时至少比较：

- Unit：阶段事实、语义 tag、terminal boundary 和负例；
- Contract：项目归属、图边、输出形状和 validator；
- Host：decision、evidence、rewritten source 和最终 diff；
- DOP：1、2、16 的结果集合、顺序、artifact 字节和日志发布顺序。

入口实现：`src/NLISSN.Core/Marking/MarkingEngine.cs`、`Propagation/PropagationEngine.cs`、`Lifting/MarkLiftingEngine.cs`、`src/NLISSN.Application/Analysis/RulePipeline.cs` 和 `src/NLISSN.Core/Rewrite/PrototypeRewriter.cs`。表达式粒度见 [原子表达式标记](atomic-marking.md)，提案前事实见 [从 Propose 提取事实](proposal-fact-extraction.md)。
