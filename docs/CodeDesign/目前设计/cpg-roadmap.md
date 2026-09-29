# CPG 演进优先级

> 状态：当前设计。
>
> 读者：准备扩展节点、边、查询或持久化能力的维护者。
>
> 本页是优先级判断，不是承诺表；feature 的完成状态以 [`Context/feature_list.json`](../../../Context/feature_list.json) 为准。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 判断标准

只有同时满足以下条件，才值得扩展 CPG：

1. 有一个当前规则或查询明确遇到的安全边界。
2. 新事实能比直接读取 Roslyn 提供可重复的证明，而不是增加一个别名节点。
3. 能写出正例、负例、预算截断和 DOP 等价验证。
4. 能说明失败、未知和外部边界怎样 fail closed。

节点数量、图数据库兼容度和单次 benchmark 的偶然下降都不是优先级依据。

## 当前优先级

| 顺序 | 能力 | 为什么现在需要 | 最小验收 |
| ---: | --- | --- | --- |
| 1 | 调用关系分层 | `UnreachableMethod` / `UnreferencedMethod` 需要区分已解析、保守候选和未知调用 | 根集合、闭包、编译错误、缺 entry point、local function/lambda 引用的结果分别可解释 |
| 2 | 成员访问语义 | `delete-s-object`、参数收缩和 receiver 判断需要更可靠的 property/indexer/member 关联 | 直接成员、隐式 receiver、重载和动态访问各有正负例 |
| 3 | CFG-sensitive 数据流 | 现有局部 reaching-definition 不能覆盖所有分支合流 | 先补最小 branch-sensitive def-use，不宣称完整 DDG |
| 4 | 外部类型与调用摘要 | 只有规则真正需要跨程序集证明时才引入 | summary 来源、失效、未知调用和缓存边界有稳定契约 |

## 性能证据单独验收

持久化构建已经有 verified routing sidecar 和旧 catalog 回退。它仍需要在同一份真实规模输入上做 warmed 样本，比较 catalog 时间、队列压力、StoreRoot 占用和查询延迟。没有这组数据时，不改变默认 DOP，不宣传“更快”，也不把 sidecar 存在本身当作性能完成条件。

同理，查询 overlay、线程池内存、稳定提交、目录 I/O、声明符号查询和 Mark snapshot 的完成状态都必须从当前 `Context/feature_list.json` 读取，不能从本页的旧文字推导。

## 不在当前优先级

- 完整 joern schema 对齐。
- 通用查询 DSL 或跨项目图数据库；当前 shard store 只服务于同一 source/profile/schema 的已完成构建和有界读取。
- 没有明确规则消费者的节点细分。
- 为了 benchmark 方便而放宽未知、截断或缺 frontier 的保守语义。

## 验证与进入实施前的检查清单

```text
规则/查询提出缺口
  -> [能力边界]确认现有事实不足
  -> 定义最小节点/边/summary
  -> 先写失败与 unknown 语义
  -> 写 Unit / Contract / Host / Performance 验收
  -> 验证 DOP 1、2、16 的图、决策和 diff 等价
  -> 更新 Context/feature_list.json 和本页
```

实现入口在 `src/NLCPG/Builder/Passes/`、`src/NLCPG/Analysis/` 和 `src/NLCPG/Persistence/`。完成后还要运行 `pwsh -File .\Miscellaneous\scripts\check-harness-consistency.ps1` 与 `git diff --check`。详见 [最小 CPG 架构](cpg-architecture.md) 和 [CPG 能力边界](cpg-capabilities.md)。
