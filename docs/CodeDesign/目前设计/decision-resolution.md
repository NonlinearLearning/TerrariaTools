# 决策与冲突裁决

> 状态：当前设计。
>
> 读者：需要解释 proposal 如何收口、多个规则如何竞争同一 span 的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 结论

Propose 只产生候选 `DecisionUnit`；`RuleDecisionEngine` 负责把候选归并成最终 `RuleDecision`。`PrototypeRewriter` 和 diff builder 只消费最终决策，不重新执行 Mark、Propagate、Lift 或 Propose。

主要代码锚点：`src/NLISSN.Core/Decision/DecisionModel.cs`、`src/NLISSN.Core/Decision/AnalysisEvidence.cs`、`src/NLISSN.Core/Validation/DecisionBindingValidator.cs`、`src/NLISSN.Core/Rewrite/PrototypeRewriter.cs`。

## 决策模型

一个 decision 至少包含 rule id、动作、目标 anchor、原始 span、替换文本或结构、理由和 provenance。动作只有三类：

| 动作 | 含义 | 进入 rewrite 的条件 |
| --- | --- | --- |
| `Delete` | 删除目标 span 或结构 | anchor 属于声明的 replacement/delete contract，且没有更高优先级覆盖 |
| `Replace` | 用已验证的 replacement syntax/text 替换 | replacement span、原始文本和绑定事实都通过验证 |
| `Skip` | 保留源码并记录原因 | 证据不足、冲突、未知或规则主动保守退出 |

`Skip` 是可观察的结果，不是静默丢弃。evidence 应能说明是 disabled、empty、unknown、conflict、truncated 还是 budget 原因。

## 冲突规则

同一 anchor 的裁决顺序固定：

1. `Replace` 优先于同一位置的 raw `Delete`，因为替换携带更具体的结构结果。
2. 被祖先 `Delete` 完整覆盖的子删除在 `DeletionApplicationService.FilterNestedDeleteDecisions(...)` 中折叠。
3. `Replace` 覆盖其内部的子 `Delete`，避免产生重叠 edit。
4. 不可安全合并的 proposal 转为 `Skip` 或 diagnostic，不按来源顺序猜一个胜者。

裁决后必须得到不重叠、可排序、可绑定到原始源码的 edit 集。规则注册顺序可以影响稳定 identity，但不能成为未声明优先级的冲突解决算法。

## Binding 和 evidence 门

`RuleBindingValidator` 检查规则输出与 graph port 的类型、syntax kind、semantic tag 和 evidence root；`DecisionBindingValidator` 检查 decision 的 anchor、replacement、tree membership、span 和 evidence。任一错误都在 rewrite 前阻止应用。大 hunk 没有目标 provenance 时，不得因为进程退出码为 0 而放行。

## 验证

验证分三层：

- Unit：动作和单一冲突规则；
- Host：多规则、嵌套删除、replacement、Skip 和 evidence；
- End-to-end：决策 -> edit -> rewritten source -> diff，并覆盖编译或 Roslyn 结构门。

当前入口为 `tests/NLISSN.HostTests/Decision/DecisionComplexTests.cs`、`DecisionStructureValidationTests.cs`、`DecisionEvidenceTests.cs`、`tests/NLISSN.HostTests/Validation/BindingValidatorTests.cs` 和 `PipelineComponentTests.cs`。相关页面：[从 Propose 提取事实](proposal-fact-extraction.md)、[Rewrite 与 Diff](rewrite-and-diff.md)。
