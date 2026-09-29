# 原子表达式标记

> 状态：当前设计。
>
> 读者：修改 seed mark 粒度、逻辑操作数传播或表达式结构提升的维护者。
>
> 关键决定：Atomic 是 Mark 的粒度，不是运行时路由；结构结论必须在 Lift/Propose 形成。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 目标

Mark 阶段只定位可以独立判断的最小表达式，避免直接标记整个 `if`、方法或二元逻辑树。一个 seed mark 记录目标、span、syntax kind、semantic tag 和来源，但不声明删除范围，也不生成 replacement syntax。

统一分析器位于 `src/NLISSN.Core/Analysis/otherAnalyzers/AtomicExpressionAnalyzer.cs` 与 `LogicalConditionMarkAnalyzer.cs`；规则入口按语义拆在 `src/NLISSN.Rules/Mark/AtomicExpressions/`、`Mark/Declarations/` 和 `Mark/MethodGlobal/`。`MarkAnalysisSnapshot` 复用同一次分析的 binding、operation、目标匹配和 region facts。

## 直接操作数原则

Roslyn syntax tree 是表达式优先级和分组的事实来源。传播先把 seed 对齐到最近运算符的 direct operand，再把完整结果作为更低优先级运算符的一个操作数。比较表达式、括号表达式和完整 `||` 链都应作为整体参与上层判断，不能按 token 或 ancestor span 拆开。

| 形态 | seed/传播语义 | 结构边界 |
| --- | --- | --- |
| `&&` | 同一链中的 direct sibling 可互传 | 链整体可以成为逻辑宿主 |
| `||` | 不向自身左右 sibling 传播 | 完整 `||` 链可作为外层 `&&` 的一个操作数 |
| 比较和普通二元 | 保留完整表达式作为外层操作数 | 不向另一侧 sibling 扩散 |
| 括号 | 保留显式分组，不抹平优先级 | 由 topology path 记录分组 |
| `!`、`~`、一元正负、`++`/`--`、`await`、nullable `!` | 只保留该单元表达式 | 终止，不暴露外层 sibling 或结构 owner |
| 三元 | 任一子表达式命中时产出整体条件表达式事实 | 不把一个分支当成另一个分支的证明 |
| 赋值、复合赋值、lambda 非表达式右侧 | 停止或跳过 | 不把副作用语法当作可删操作数 |

例如：

```csharp
if (alive && (count > 0 || force) && !isPaused)
{
    Consume();
}
```

`count > 0` 是一个比较操作数，`count > 0 || force` 是外层 `&&` 的一个整体操作数，`!isPaused` 在一元 terminal 停止。某个原子命中不能仅凭祖先 span 删除整个 `if`；只有 Lift 证明完整条件覆盖，Propose 才能选择结构动作。

## 事实端口

原子规则通过 `Target.*`、`Flow.*`、`Lift.*` 和 `Relation.*` 中性的事实端口连接。`RuleEvidenceOrigin` 只说明 evidence 来源，不能代替语义 tag 选边。声明签名、delegate 和 extension method 的安全证明使用带类型的 `Relation.*` payload。

`&&` / `||` 的逻辑宿主、unary/conditional terminal 和普通表达式 topology 由 `ExpressionPropagationTopology` 统一产生。Lift 与默认删除提案消费 terminal boundary，不能通过后续规约重新暴露 terminal 内部的 structural owner。

## 验证

规则变化必须同时覆盖：

- 原子 seed 的 syntax kind、span、语义 tag 和去重；
- `&&` sibling、`||` barrier、比较、括号、混合逻辑链；
- unary、await、conditional、assignment 的 terminal negative cases；
- Mark -> Propagate -> Lift -> Propose -> Rewrite 的最终 diff。

当前入口包括 `tests/NLISSN.HostTests/Mark/LogicalConditionMarkAnalyzerTests.cs`、`MarkRuleEffectTests.cs`、`tests/NLISSN.UnitTests/Analysis/ExpressionPropagationTopologyTests.cs` 和 `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`。DOP 比较必须约束 topology facts、evidence、decision、rewritten source 和 diff，而不是只看测试退出码。

下一步阅读：[原子规则传播事实](delete-class-propagation.md)、[从 Propose 提取事实](proposal-fact-extraction.md) 和 [决策与冲突裁决](decision-resolution.md)。
