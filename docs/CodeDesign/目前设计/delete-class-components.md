# 原子规则组件

> 状态：当前设计。
>
> 读者：新增或拆分删除规则、决定规则注册位置的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 组件地图

默认规则集由 `src/NLISSN/Composition/RuleRegistry.cs` 组装，规则实现位于 `src/NLISSN.Rules/`，通过统一的 Mark -> Propagate -> Lift -> Propose contract 接入应用层。目录按阶段和语义风险拆分，文件名表达规则职责，不用一个聚合类隐藏多个 rewrite 形状。

| 风险/形状 | 主要目录 | 典型结果 |
| --- | --- | --- |
| 原子表达式 | `Rules/Mark/AtomicExpressions/` | 目标表达式的最小 `MarkRecord` |
| 声明与类型语法 | `Rules/Mark/Declarations/`、`Rules/Propose/` | 类型、基类、返回值和成员 proposal |
| MethodGlobal | `Rules/Mark/MethodGlobal/`、`Rules/Propose/MethodGlobal/` | 经过 compilation-level linkage 证明的方法删除候选 |
| expression flow | `Rules/Propagate/ExpressionFlow/` | symbol、initializer、assignment-left-value 等局部事实 |
| 声明关系 | `Rules/Propagate/` | host、callsite、delegate、extension method payload |
| 控制结构 | `Rules/Lift/ControlStructures/`、`Rules/Propose/` | if、switch、条件表达式的结构动作 |
| 接口可见性 | `Rules/Mark/`、`Propagate/`、`Lift/`、`Propose/` | interface implementation 清理或内部 public 方法私有化 |
| 参数收缩 | `Rules/Propose/` 的 typed proposal files | named、optional、params、delegate、indexer 等安全替换 |

## 每个组件的边界

一个组件只回答一种可验证的 rewrite 形状：

1. Mark 只找最小目标，不代表整个宿主已经可删。
2. Propagate 只收集符号、调用点、delegate、拓扑或声明宿主等事实，并保留来源 mark。
3. Lift 只把阶段事实提升为结构候选，且必须尊重 topology terminal 和结构完整性。
4. Propose 只选择 `Delete`、`Replace` 或 `Skip`，不重扫 compilation。

公共 API、动态绑定、重载漂移、多跳 delegate、混合调用形状和无法区分的 receiver 默认保留。拆文件不能改变注册顺序、rule id、端口、decision category 或 DOP 输出。

## 新规则决策树

```text
命中是否是最小 syntax/semantic target?
  否 -> 先缩小 Mark 或放入明确的 global analysis
  是 -> 是否需要引用、调用点、祖先 host 或跨文件事实?
          是 -> Propagate payload
          否 -> 是否只是结构 owner?
                   是 -> Lift
                   否 -> Propose action
```

如果动作影响 public API、参数列表或方法可达性，必须附带正向证明和保守负例；“没有查到引用”不是“证明没有引用”。

## 验证

新增组件至少需要一个安全命中 fixture、一个应保留 fixture，并在所属阶段断言 mark/payload/lift/decision。最终还要比较 rewritten source、per-file diff 和 evidence provenance。接口可见性与 MethodGlobal 的阶段拆分需额外比较 DOP 1、2、16 的 registry、计划和分类 diff。

主要入口是 `tests/NLISSN.HostTests/Application/PipelineComponentTests.cs`、`ApplicationServiceFlowTests.cs`、`RuleStructureContractTests.cs` 和 `tests/NLISSN.HostTests/Propagation/PropagationRuleExpansionTests.cs`。传播事实的详细格式见 [原子规则传播事实](delete-class-propagation.md)，提案翻译边界见 [从 Propose 提取事实](proposal-fact-extraction.md)。
