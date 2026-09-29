# Rewrite 与 Diff

> 状态：当前设计。
>
> 读者：修改编辑计划、diff 渲染、回放或 write-back 边界的维护者。
>
> 本页回答“已经决定删什么之后，怎样安全地产生可回放输出”；命令入口见 [`docs/cli-reference.md`](../../cli-reference.md)。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 分层

```text
RuleDecision
  -> edit planning
  -> RewriteEdit / RewritePlan
  -> source application
  -> DiffModel
  -> renderer
  -> artifact publish / optional write-back
```

核心模型和应用位于 `src/NLISSN.Core/Rewrite/`：`RewriteEdit`、`RewritePlanModel`、`PrototypeRewriter`、`DiffBuilder` 和 `TextDiffRenderer` 各自只做一层。Host 负责文件路径、artifact 预留和发布，不拼接 diff 正文；runtime log 只记录执行事实，不承载 diff 行。

## Edit 计划契约

一个可执行 edit 必须保存相对路径、原始 span、原始文本、替换文本、来源 decision 和稳定排序键。计划阶段拒绝重叠或越界 span；同一文件按确定顺序应用，不能因为输入 rule 的完成顺序变化而改变结果。

目录分析先按输入索引形成稳定文件序。文件可以并发 render/write，但结果集合、聚合 diff、`DiffFilePath` 和 `diff.written` 事件按稳定顺序提交。写入失败会停止后续发布并保留已经成功发布的独立 diff；只有全部条件满足才发布 summary。

## Diff 和 artifact

diff 是 edit 结果的视图，不是安全证明。当前 category diff 由 `src/NLISSN/Artifacts/CategoryDiffArtifactService.cs` 和 `RuleDiffCategory.cs` 发布；默认删除/可见性规则按声明的输出 category 聚合。结构化 evidence、rewrite plan 和 runtime log 分别记录原因、可回放编辑和执行状态。

默认 artifact 位于 `Build\Result\<runId>\<category>` 下；配置和路径来源由 Schema 2 loader 决定。文件写入使用临时文件和原子替换，避免读到半成品。

## 可移植回放

目录分析可以保存文本级 plan：manifest 记录 schema、相对路径和源文件 SHA-256；每条计划记录有序且不重叠的 edit span、原始文本和替换文本，不持久化 Roslyn、CPG 或规则对象。`RewritePlanReplayService` 回放前依次验证 artifact 哈希、源文件哈希、路径、span 和原始文本；任一失败都停止，不允许先输出部分 write-back。

回放只应用已验证的计划、重新生成最终 diff 和输出，用于把 rewrite/diff 成本与分析成本分开测量。回放不是重新分析，也不会接受新的规则开关。

## 安全不变量

- rewrite 不重新执行 Mark、Propagate、Lift 或 Propose。
- diff renderer 的格式变化不能改变 `RewriteEdit` 语义。
- 所有 edit 必须可以回溯到 decision/evidence；没有 target-derived provenance 的大 hunk 不应进入发布。
- DOP 1、2、16 的 per-file diff bytes、聚合顺序和发布状态必须相同。
- 失败、源文件失配、路径越界和冲突都 fail closed。

## 验证

Contract 覆盖 `DiffModel`、edit overlap、category 和 artifact schema；Host 覆盖单文件、目录、无 edit、并发 diff 和回放失败；Performance 比较多文件 render/write 的稳定性。当前入口包括 `tests/NLISSN.ContractTests/Rewrite/DiffModelTests.cs`、`tests/NLISSN.HostTests/Rewrite/RewritePlanPersistenceTests.cs`、`CategoryDiffArtifactServiceTests.cs`、`RuleDiffCategoryTests.cs` 和 `PipelineComponentTests.cs`。

相关页面：[决策与冲突裁决](decision-resolution.md)、[删除规则流水线](deletion-pipeline.md)、[测试策略](testing-strategy.md)。
