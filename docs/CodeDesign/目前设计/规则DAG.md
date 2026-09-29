# 规则 DAG

> 状态：当前设计。
>
> 读者：新增规则依赖、修改调度器或解释 DOP 行为的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-09-08。

## 结论

生产路径只编译并提交一张显式规则图。规则之间的边来自 `Consumes` / `Produces` 端口，不来自 `RuleId` 前缀、规则家族、文件顺序或 AST 祖先关系。需要递归闭包时使用显式 fixed-point region，而不是把循环伪装成 DAG。

## 编译和执行

```text
RulePipeline
  -> RuleGraphCompiler
  -> CompiledRuleGraph
  -> RuleGraphAnalysisExecutor
  -> RuleGraphExecutor
  -> ordered node results
```

物理归属分为两部分：`src/NLISSN.Application/Analysis/RulePipeline.cs` 组装应用层规则；`src/NLISSN.Rule/RuleGraph.cs`、`RuleGraphCompiler.cs` 和 `RuleGraphExecutor.cs` 保存阶段图契约和调度实现，命名空间是 `NLISSN.Core.Pipeline`。Mark、Propagate、Lift 和 Propose 的生产执行不在节点内部重新编译或提交整张图。

## 边怎样成立

每个输入/输出端口包含：

- 允许的 `SyntaxKind`；
- 一个受控的 `RuleFactKind`；外部/测试扩展才使用 legacy `RuleSemanticTag`；
- consumer 的 `RuleInputCardinality`；
- producer 和 output 的稳定身份。

编译器对内建事实只连接 `RuleFactKind` 相同、且 producer 声明的每个节点种类都被 consumer 接受的端口；legacy tag 仅用于未注册的自定义测试/扩展事实。`All` 等待全部匹配 producer，`ExactlyOne` 在零个或多个 producer 时报告错误，`Optional` 允许没有匹配 producer。相同 `(producer, output)` 的重复声明无效；多个 output 指向同一 consumer 不会造成重复执行。

`GroupKey` 只保留领域分组和兼容数据，不参与 scheduler。运行时还校验产出的 record 类型、syntax kind 和 fact kind，防止“图连上了但值的形状不对”。

当前内建事实端口已由 `RuleFactKind` 表示，`RuleSemanticTag` 仅保留为自定义测试事实和旧 artifact 显示 adapter。事实端口类型化的实现见 [规则事实端口类型化](规则事实端口-枚举化.md)：它改变事实身份的类型保护，不改变边的依赖语义。边仍由事实 kind 和 syntax contract 匹配产生；Target、Flow、Relation 和 Lift 也不会因此自动变成互不相干的独立图。

## 运行状态

ready 节点按稳定 node identity 排序，并由运行时 DOP 和 `EnableGroupParallelism` 控制并发。节点结果至少区分：

| 状态 | 含义 | 下游行为 |
| --- | --- | --- |
| Completed | 有效完成，可有或没有值 | 释放依赖者 |
| Disabled | 配置或规则策略关闭 | 记录状态，按契约释放依赖者 |
| Cancelled | run-local cancellation 后未完成 | 不启动新的未开始工作 |
| Failed | 产生不可恢复错误 | 触发 run-local 取消并 drain 已开始任务 |

completed-empty 不是 failed，也不能让下游永久等待。第一个失败触发取消，但已开始任务必须按取消契约排空；未开始的 ready 节点不得被偷偷启动。

## Fixed-point 例外

表达式或关系传播可在 `src/NLISSN.Core/Propagation/PropagationFixedPointExecutor.cs` 内部反复消费 worklist。事实按 rule、file path、span、syntax kind 和 `RuleFactKind` 去重（自定义事实才回退到 legacy tag），seed depth 为 0，传播深度随来源事实增加。worklist 清空后只把收敛快照交给 Lift 和 Propose；外层仍是无环图。

## 确定性与验证

改变图契约或调度时，必须比较 DOP 1、2、16 的 ordered stage snapshots、evidence、decisions、rewritten source、per-file diff bytes 和 graph telemetry。`tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs` 覆盖缺 producer、端口匹配、cardinality、状态传播和稳定节点；`RuleStructureContractTests.cs`、`PipelineComponentTests.cs` 和 Contract architecture tests 覆盖输出形状与物理归属。

图编译错误、部分节点种类重叠、未知 producer、重复端口和环都应在分析前暴露。相关阶段解释见 [删除规则流水线](deletion-pipeline.md)；共享并发机制见 [运行时基础设施](运行时基础设施.md)。
