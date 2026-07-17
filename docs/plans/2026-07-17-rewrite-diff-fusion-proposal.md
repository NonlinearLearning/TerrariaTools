# Rewrite-Diff 融合总提案

## 目标

把当前独立但耦合混乱的 diff 产出链路并入 `src/RoslynPrototype/Rewrite/` 子系统，并同时重构 rewrite 模块边界。

这次提案解决两个问题：

1. diff 现在在逻辑上属于 rewrite 结果，但实现上只是 `PrototypeRewriter` 末尾拼出来的一段字符串
2. rewrite 模块现在把决策执行、文本编辑、源码落地前整理、diff 文本渲染和结果契约混在一起

目标状态不是“把更多代码塞进 `PrototypeRewriter`”，而是把 rewrite 子系统拆成稳定分层：

1. rewrite 决策执行
2. rewrite edit 规划与应用
3. diff 模型构建
4. diff 渲染
5. host 输出边界

## 当前问题

### 1. `PrototypeRewriter` 职责过重

当前 [`src/RoslynPrototype/Rewrite/PrototypeRewriter.cs`](D:/ProjectItem/SourceCode/Net/NL/src/RoslynPrototype/Rewrite/PrototypeRewriter.cs:20) 同时负责：

1. 把 `RuleDecision` 转成 rewrite plan
2. 生成 `RewriteEdit`
3. 执行文本替换
4. 规范化最终源码
5. 生成 `DiffText`

这导致 rewrite 核心逻辑和 diff 渲染逻辑绑定在一起。

### 2. diff 结果仍然是字符串优先契约

当前结果契约仍然把 `DiffText` 作为主输出：

- [`PrototypeRewriteResult`](D:/ProjectItem/SourceCode/Net/NL/src/RoslynPrototype/Rewrite/PrototypeRewriteResult.cs:6)
- [`PrototypeAnalysisResult`](D:/ProjectItem/SourceCode/Net/NL/src/RoslynPrototype/Rewrite/PrototypeAnalysisResult.cs:14)

这会把文本格式选择泄漏到 Application、Host 和测试。

### 3. 目录 diff 聚合在 Host 层手写字符串

当前目录模式由 [`DeletionDirectoryAnalysisService`](D:/ProjectItem/SourceCode/Net/NL/src/Host/DeletionDirectoryAnalysisService.cs:662) 持有 `_diffSections` 和 `_diffFilePaths`，并在 [`711`](D:/ProjectItem/SourceCode/Net/NL/src/Host/DeletionDirectoryAnalysisService.cs:711) 行附近直接拼接：

1. `### {filePath}`
2. `{result.DiffText}`

这说明目录 diff 还不是 rewrite 子系统自己的聚合结果。

### 4. cleanup 会回头重建旧 diff 文本

[`DeleteClassPostRewriteCleanupService`](D:/ProjectItem/SourceCode/Net/NL/src/Host/DeleteClassPostRewriteCleanupService.cs:129) 在合并 cleanup edits 时会重新调用 `BuildDiffText(edits)`。

这让 Host 后处理知道了 diff 文本生成方式，边界不对。

## 设计结论

### 结论 1

diff 应该并入 rewrite 子系统。

理由：

1. diff 的唯一稳定输入就是 `RewriteEdit`
2. diff 的文件顺序、edit 顺序、删除/替换语义都依赖 rewrite 阶段的稳定输出
3. Host 不应该知道 diff 是怎么从 edits 变成文本的

### 结论 2

diff 不应该继续并在 `PrototypeRewriter` 这个单类里。

理由：

1. diff 属于 rewrite 子系统，不等于属于 rewrite 核心执行类
2. 渲染层变化不应该迫使 rewrite-core 变化
3. readable diff、summary、sidecar 等后续扩展都不该反向污染 edit 规划逻辑

### 结论 3

这次应当把 rewrite 模块重构成“model-first，renderer-late”的结构。

## 非目标

本提案不做这些事情：

1. 不改 `RuleDecision` 语义
2. 不改 rewrite 删除/替换规则本身
3. 不改 `--diff-out`、`--no-diff`、`DiffFilePath` 语义
4. 不把 diff 接到 text log 系统里
5. 不引入 git-style patch 或 LCS-first diff
6. 不在第一阶段改默认 diff 文本格式

## 约束

### 结果稳定性

现有大量测试依赖这些输出：

1. `DiffText`
2. `DiffFilePath`
3. `.rewrite.diff` 文件名
4. `--- original #n` / `+++ rewritten #n` 片段

第一阶段必须保持兼容。

### 目录确定性

目录模式在并行分析下，文件顺序和 edit 顺序必须稳定。

### 模块边界

diff 归 rewrite 子系统所有，但 text log 系统不能拥有 diff 内容或 diff summary。

## 新的模块边界

建议把 `src/RoslynPrototype/Rewrite/` 重组为下面五层。

### Layer 1: edit planning

职责：

1. 消费 `RuleDecision`
2. 生成有序 rewrite plan
3. 解决重叠、包含、替换优先级等问题

建议保留在 rewrite-core 中的内容：

- `BuildEffectiveRewritePlan(...)`
- 各类 `CreateRewritePlanEntry(...)`
- delete/replace 的语法节点转换逻辑

这一层不负责：

1. diff 模型
2. diff 文本
3. 文件输出

### Layer 2: edit materialization

职责：

1. 把 rewrite plan 变成 `RewriteEdit`
2. 应用文本替换得到 `RewrittenSource`
3. 做最终源码规范化

建议迁入专门类型的内容：

- `ApplyTextRewriteOperations(...)`
- `FinalizeRewrittenSource(...)`
- `NormalizeLineEndings(...)`

### Layer 3: diff model

职责：

1. 从 `RewriteEdit` 生成结构化 diff 文档
2. 提供单文件和多文件的统一模型
3. 为 renderer 和 summary 提供稳定输入

建议新增：

- `DiffDocument`
- `DiffFile`
- `DiffSection`
- `DiffBlock`
- `DiffLine`
- `DiffLineKind`
- `DiffSummary`

第一版保持 edit-driven，不做 hunk diff。

### Layer 4: diff renderer

职责：

1. 把 `DiffDocument` 渲染成文本
2. 支持 `legacy`
3. 以后支持 `readable`

这一层拥有展示细节：

1. 行头格式
2. `<deleted>` 文本
3. 分节样式
4. 多文件总文档布局

### Layer 5: host output boundary

职责：

1. 决定是否写 `.rewrite.diff`
2. 决定写到哪里
3. 决定单文件还是目录聚合如何落盘

这一层不再负责：

1. 手拼 markdown diff 字符串
2. 自己理解 `RewriteEdit`
3. 自己决定 diff section 结构

## 新的建议目录结构

建议在 `src/RoslynPrototype/Rewrite/` 下逐步形成下面的结构：

- `PrototypeRewriteService.cs`
- `RewritePlan.cs`
- `RewriteEdit.cs`
- `RewriteEditPlanner.cs`
- `RewriteEditApplier.cs`
- `DiffModel.cs`
- `DiffBuilder.cs`
- `TextDiffRenderer.cs`
- `PrototypeRewriteResult.cs`
- `PrototypeAnalysisResult.cs`

如果想保持最小 diff，可以先不改文件名，只先引入新类型，再在后续批次改名。

## 核心类型设计

### `PrototypeRewriteResult`

当前：

1. `RewrittenSource`
2. `Edits`
3. `DiffText`

目标：

1. `RewrittenSource`
2. `Edits`
3. `DiffDocument Diff`
4. `string DiffText` 作为兼容字段

### `PrototypeAnalysisResult`

当前：

1. `Edits`
2. `RewrittenSource`
3. `DiffText`
4. `DiffFilePath`

目标：

1. `Edits`
2. `RewrittenSource`
3. `DiffDocument Diff`
4. `string DiffText` 作为兼容字段
5. `string? DiffFilePath`
6. 可选 `DiffSummary`

### `DiffDocument`

建议至少包含：

1. `IReadOnlyList<DiffFile> Files`
2. `DiffSummary Summary`

### `DiffFile`

建议至少包含：

1. `string FilePath`
2. `IReadOnlyList<DiffSection> Sections`

### `DiffSection`

建议至少包含：

1. `int EditIndex`
2. `TextSpan Span`
3. `DiffEditKind`
4. `string OriginalText`
5. `string ReplacementText`
6. `IReadOnlyList<DiffBlock> Blocks`

## 重写后的执行流

目标执行流：

1. Decision engine 输出 `RuleDecision`
2. rewrite-core 生成稳定 rewrite plan
3. edit applier 生成 `RewriteEdit[] + RewrittenSource`
4. diff builder 生成 `DiffDocument`
5. renderer 在需要文本输出时生成 `DiffText`
6. Host 只在最终边界写文件

对应到当前代码，`DeletionApplicationService` 应该在 [`RunAnalysis`](D:/ProjectItem/SourceCode/Net/NL/src/Application/DeletionApplicationService.cs:105) 内部拿到的是结构化 rewrite 结果，而不是字符串优先结果。

## 对现有文件的重构建议

### `PrototypeRewriter.cs`

建议拆分为三部分：

1. rewrite plan / decision execution
2. text edit apply
3. diff construction and rendering hookup

第一阶段可以保留 `PrototypeRewriter` 名字，但内部只做编排，不再自己实现 `BuildDiffText(...)`。

### `DeleteClassPostRewriteCleanupService.cs`

这里不应继续知道如何从 `RewriteEdit` 直接构造文本 diff。

建议改成：

1. 合并 cleanup edits
2. 生成新的 `DiffDocument`
3. 由 renderer 回填兼容 `DiffText`

### `DeletionDirectoryAnalysisService.cs`

这里不应持有 `_diffSections` 这样的字符串聚合状态。

建议改成：

1. 聚合 `DiffFile`
2. 在 `BuildResult(...)` 时形成目录级 `DiffDocument`
3. 只在落盘前调用 renderer

## 与现有 diff plan 的关系

现有 [`2026-07-16-diff-model-and-renderer-execution-plan.md`](D:/ProjectItem/SourceCode/Net/NL/docs/plans/2026-07-16-diff-model-and-renderer-execution-plan.md:1) 的方向是对的：

1. diff model
2. diff builder
3. renderer
4. model-first 契约迁移

但它更偏“diff 迁移计划”，还没有把 rewrite 模块自身拆分说透。

本提案覆盖它的总设计视角，并补上两个结构结论：

1. diff 应并入 rewrite 子系统
2. rewrite 子系统需要拆层，不能继续围绕单个 `PrototypeRewriter` 扩张

## 实施批次

### Batch 0: 固定边界，不改行为

任务：

1. 确认 `RewriteEdit` 是 diff 的唯一输入
2. 确认 `DiffText` 只作为兼容输出保留
3. 记录所有 `DiffText` / `DiffFilePath` 消费点

完成条件：

1. 迁移面已知
2. 不改任何用户可见行为

### Batch 1: 把 diff 模型引入 rewrite 子系统

任务：

1. 新增 `DiffModel`
2. 新增 `DiffBuilder`
3. 新增 `TextDiffRenderer`
4. 让 `PrototypeRewriter` 返回 `DiffDocument + DiffText`

完成条件：

1. 现有 diff 文本保持兼容
2. `BuildDiffText(...)` 被 renderer 接管

### Batch 2: 把 rewrite-core 与 diff 渲染解耦

任务：

1. 从 `PrototypeRewriter` 中抽出 edit apply
2. 从 `PrototypeRewriter` 中抽出 diff 生成
3. 保留一个编排入口

完成条件：

1. renderer 变化不需要改 rewrite-core
2. edit 规划、apply、render 各自有独立测试点

### Batch 3: Application 和 Host 改成 model-first

任务：

1. `PrototypeAnalysisResult` 增加 `DiffDocument`
2. `DeletionApplicationService` 传递结构化 diff
3. `DeletionCommandHost` 在边界写 `.rewrite.diff`
4. `DeletionDirectoryAnalysisService` 改成聚合 `DiffFile`

完成条件：

1. Host 不再拼接原始 diff 字符串
2. 目录 diff 由 model 统一生成

### Batch 4: cleanup 接入新模型

任务：

1. cleanup 合并 edits 后重建 `DiffDocument`
2. 不再直接调用旧 `BuildDiffText(...)`

完成条件：

1. cleanup 只关心 edits 和 rewritten source
2. diff 文本仍由 renderer 提供

### Batch 5: 可选 readable renderer 与 summary

任务：

1. 加 `--diff-view`
2. 新增 `readable` 渲染
3. 新增 `DiffSummary`

完成条件：

1. `legacy` 默认不变
2. summary 不携带完整 diff body

## 测试策略

### 必加测试

1. `DiffBuilder` 单文件 delete / replace
2. `DiffBuilder` 多 edit 同文件顺序稳定
3. `DiffBuilder` 多文件聚合顺序稳定
4. `legacy` renderer 文本兼容
5. cleanup 后 diff 模型重建正确
6. 目录模式不再依赖字符串拼接也能保持相同输出

### 必跑回归

优先覆盖：

- `GraphAnalyzerTests`
- `MarkRuleEffectTests`
- `PipelineComponentTests`

这些测试现在直接或间接锁定了 diff 文本和 rewrite 输出形状。

## 验证命令

```powershell
pwsh -File .\init.ps1
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GraphAnalyzerTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~PipelineComponentTests"
pwsh -File .\scripts\check-harness-consistency.ps1
```

## 验收标准

本提案完成时必须满足：

1. diff 模型已经属于 rewrite 子系统内部主路径
2. `PrototypeRewriter` 不再直接拥有 diff 文本格式化逻辑
3. 单文件与目录 diff 都先生成结构化模型，再在边界渲染文本
4. Host 不再自己拼接 diff 字符串
5. `DiffText` 仅作为兼容字段存在
6. `--diff-out`、`--no-diff`、`DiffFilePath` 行为不变
7. cleanup 后处理不再直接重建旧 diff 字符串

## 风险

### 风险 1：重构名义下扩大范围

如果一开始就改文件名、改 renderer、改 CLI、改 summary，容易把迁移做散。

控制方式：

1. 先上模型
2. 再迁契约
3. 最后才加可选视图

### 风险 2：把 diff 重新塞回单个大类

这会让模块名义上融合，结构上更糟。

控制方式：

1. diff 属于 rewrite 子系统
2. renderer 不属于 rewrite-core

### 风险 3：Host 继续保留旧聚合逻辑

如果 `DeletionDirectoryAnalysisService` 仍然维护 `_diffSections`，那只是加了新模型，没有真正迁移主路径。

控制方式：

1. 目录聚合必须改为 `DiffFile` / `DiffDocument`

### 风险 4：兼容字段长期不退场

如果 `DiffText` 永远作为一等契约存在，后续 readable renderer 和 summary 还是会受限。

控制方式：

1. 先兼容
2. 再逐步把消费者迁到 `DiffDocument`

## 推荐执行顺序

按下面顺序推进：

1. `DiffModel`
2. `DiffBuilder`
3. `TextDiffRenderer`
4. `PrototypeRewriteResult` 扩展为 model-first
5. `PrototypeAnalysisResult` 扩展为 model-first
6. `DeletionDirectoryAnalysisService` 去掉字符串聚合
7. `DeleteClassPostRewriteCleanupService` 去掉旧 diff 回填

这条顺序能把风险集中在 rewrite 子系统内部，先收边界，再动 Host。
