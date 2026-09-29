# 从 Propose 提取事实

> 状态：当前设计。
>
> 读者：发现 proposal 规则变大、重复扫描或难以解释时的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 问题

参数收缩、delegate 分类、调用点同步和声明宿主解析如果全部写在 Propose 中，每条规则都会重复读取 semantic model，事实无法共享，DOP 和缓存也难以稳定。更危险的是，proposal 可能用一个最终文本动作掩盖了前面没有建立证明的事实缺口。

## 当前分工

```text
Mark    = 找到最小目标
Propagate = 收集并验证跨节点/跨文件事实
Lift    = 把事实提升成结构候选
Propose = 将已有事实翻译为 action
Rewrite = 应用最终 decision
```

`PropagatedMarkRecord.Payload` 承载结构化中间结果；`src/NLISSN.Rules/Propose/Support/ProposalFacts.cs` 只提供对阶段事实的读取和筛选。Propose 可以选择 `Delete`、`Replace` 或 `Skip`，但不能偷偷回退到 compilation-wide 扫描或宽泛 seed mark。

## 放置规则的决策树

| 问题 | 放置位置 | 典型输出 |
| --- | --- | --- |
| 只是一个语法/符号目标吗 | Mark | `MarkRecord` |
| 需要遍历调用点、引用、delegate、host 或 fixed point 吗 | Propagate | typed payload + provenance |
| 需要决定 if、switch、expression host 等结构 owner 吗 | Lift | `LiftedMarkRecord` |
| 只是在已证明事实间选择 action 或 replacement 吗 | Propose | `DecisionUnit` |
| 只把 edit 应用到文本吗 | Rewrite | `RewriteEdit` / diff |

新增逻辑前必须写清楚“输入事实”和“不会做的搜索”。如果一句 proposal 规则同时包含“找到所有调用点”“决定参数是否使用”和“删掉声明”，它通常跨越了至少两个阶段。

## 一个参数收缩例子

以方法参数为例，安全链应是：

```text
parameter mark
  -> callsite mapping payload
  -> named/optional/params/delegate compatibility facts
  -> declaration and invocation lift
  -> Replace decision for declaration + synchronized callsites
```

如果任一调用点是动态绑定、重载无法唯一解析、method group/lambda 形状不明或存在公共 API 风险，payload 应标记未知，Propose 选择 `Skip`。不能因为普通调用点都成功就忽略一个未知调用点。

同一原则适用于逻辑操作数和 `if` 完整态：逻辑 sibling、terminal、结构宿主先在传播/提升阶段产出，提案只做最小动作翻译。

## 可解释性要求

每个 payload 至少能够回到：来源 rule id、source mark、文件与 span、semantic tag、必要的绑定身份和失败原因。`RuleEvidenceOrigin`、decision evidence 和 rewrite plan 共同构成“为什么做/为什么不做”的链；最终 diff 不能作为唯一证据。

## 验证

对每个新 payload：

1. 在 Propagate/Lift 层断言字段和 provenance。
2. 为未知、冲突、空集合和截断输入写保守负例。
3. 在 Propose 层断言 action、replacement span 和 skip reason。
4. 在 Host 层比较决策、evidence、rewritten source 和 diff。
5. 在 DOP 1、2、16 下比较相同事实签名和 artifact 字节。

主要代码入口是 `src/NLISSN.Rules/Propagate/`、`src/NLISSN.Rules/Propose/Support/ProposalFacts.cs`、`src/NLISSN.Core/Decision/DecisionModel.cs` 和 `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`。具体事实类型见 [原子规则传播事实](delete-class-propagation.md)，结构边界见 [原子表达式标记](atomic-marking.md)。
