# 原子规则传播事实

> 状态：当前设计。
>
> 读者：需要把一个原子命中扩展为调用点、声明宿主、逻辑拓扑或参数事实的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 传播的职责

`Propagate` 是“收集可复用事实”的阶段，不是“扩大删除 span”的阶段。规则位于 `src/NLISSN.Rules/Propagate/`，产物是带来源 `MarkRecord` 的 `PropagatedMarkRecord`；其 `Payload` 保存后续 Lift/Propose 所需的结构化结果。

```text
atomic MarkRecord
  -> binding / symbol / topology query
  -> PropagatedMarkRecord + typed payload
  -> Lift consumes structure facts
  -> Propose chooses action
```

payload-bearing mark 不进入通用 lift/default-delete 路径，除非它的 producer contract 明确允许。这样可以避免一个“找到调用点”的事实被误读成“声明或整个方法都可以删除”。

## 事实类型

| 事实 | 需要的证明 | 下游用途 |
| --- | --- | --- |
| 声明宿主 | seed 的类型语法/声明与可改写宿主绑定 | Lift 找到成员、返回值或类型 owner |
| symbol reference | source mark 对应符号和同一 scope 内的引用 | 判断局部定义、initializer 和使用形态 |
| callsite | 编译后的 method/argument/receiver 绑定 | 参数收缩和调用点同步 |
| delegate chain | delegate、method group、lambda 和 invocation 分类 | 只在调用形状稳定时生成替换 |
| extension mapping | extension receiver 与实际调用参数映射 | 只收缩可证明的非 receiver 参数 |
| expression topology | Roslyn direct operand、`&&`/`||` barrier 和 terminal | 局部逻辑改写、结构 owner lifting |
| control structure | if/elseif/else 或 switch 的完整覆盖事实 | 选择结构删、分支保留或条件替换 |
| MethodGlobal linkage | entry/root、直接调用闭包和未知边分类 | 方法级删除候选的 fail-closed 判定 |

每个 payload 必须带 rule id、源 mark、稳定 key、必要的 syntax/span 和语义 tag。不同规则产生的相似 payload 不能只按 span 合并，否则会丢失 provenance，导致 proposal 无法解释。

## 传播不做什么

- 不生成最终 `ReplacementSyntax` 或文本 diff。
- 不通过 `Ancestors()` 或 `Span.Contains` 直接推断结构 owner。
- 不把 `||` 的一侧命中传播给另一侧。
- 不把未知绑定、动态调用或预算截断当作“没有引用”。
- 不在每条 proposal 中重复扫描 compilation-wide usage。

需要递归关系时，`PropagationFixedPointExecutor` 在局部 worklist 中去重并收敛；外层规则图仍然只连接阶段结果。传播深度和来源 fact 进入 evidence，便于判断一个远距离 proposal 是否真的有来源链。

## 与 Propose 的接口

Propose 读取 payload，并将它翻译成 `Delete`、`Replace` 或 `Skip`。如果 payload 不完整、冲突、无法绑定到 replacement span，Propose 必须选择 `Skip` 或诊断，而不是 fallback 到宽泛的 seed mark。详情见 [从 Propose 提取事实](proposal-fact-extraction.md) 和 [决策与冲突裁决](decision-resolution.md)。

## 验证

每种新事实先写阶段断言，再写最终 rewrite 回归：

1. 正例证明 payload 字段、来源 mark、semantic tag 和 depth 正确。
2. 负例证明未知/动态/混合调用/缺宿主时不会扩大删除。
3. 多个事实证明去重保留 rule id，且重复运行不会重复入队或重复 evidence。
4. DOP 1、2、16 比较事实、evidence、decision、rewritten source 和 diff 字节。

当前回归入口是 `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`、`PipelineComponentTests.cs`、`tests/NLISSN.HostTests/Mark/LogicalConditionMarkAnalyzerTests.cs` 和对应的 MethodGlobal/接口可见性 fixture。组件分层见 [原子规则组件](delete-class-components.md)。
