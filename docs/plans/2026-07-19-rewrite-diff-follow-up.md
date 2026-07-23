# Rewrite、Diff 与回放后续执行计划

> **状态：当前执行。** 当前设计与不变量见 [`设计docs/目前设计/rewrite-and-diff.md`](../../设计docs/目前设计/rewrite-and-diff.md)。

## 目标

完成 rewrite/diff 分层、目录 diff 的有界并发写入，以及可移植 rewrite plan 的 capture/replay；保持所有现有删除决策、编辑、源码改写和 legacy diff 输出兼容。

## 当前边界

- `RewriteEdit` 是 diff 的唯一稳定输入；Host 不再拥有 diff 文本拼接规则。
- 文本日志只记录 diff 生命周期和执行统计，不能承载 diff 文本。
- artifact 只保存文本级数据；回放前必须完成 manifest、计划、源文件和 edit span 的完整验证。
- 工作区已有相关未提交实现和测试文件；先恢复 build 与 focused regression，再决定是否补充实现。

## 执行顺序

1. 运行 Rewrite、TextLog、Pipeline 和目录多文件 focused 回归，冻结 legacy/rendered diff、rewrite 输出和日志顺序。
2. 完成或校验 `DiffDocument`、builder 与 legacy/readable renderer 的分层；不改 `RuleDecision` 或 edit 规划语义。
3. 以 DOP 1、2、16 验证目录 diff 的字节级文件输出、聚合顺序、`DiffFilePath` 和 `diff.*` 发布顺序；失败时保留已写文件并停止成功汇总。
4. 验证 rewrite plan capture/replay 的成功路径、manifest 或 source hash 失配、edit span 失配、拒绝部分写回和不构建分析对象的 replay 路径。
5. 在隔离多文件 fixture 记录 render/write 与 replay 阶段耗时；真实工程 replay 基准不进入普通 `dotnet test`。

## 完成条件

- DOP 1、2、16 的 rewrite 结果、diff 文件字节和发布顺序等价。
- legacy CLI 行为、`--diff-out`、`--no-diff`、`--write-back` 与现有文本日志过滤保持兼容。
- 任一 artifact 完整性失败都在输出前中止，且不会写回任何源文件。
- 当前工作树的实现获得 focused build/test 证据；无法通过时在 `progress.md` 记录精确阻塞，而不把计划标为完成。

## 验证

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~DiffModelTests|FullyQualifiedName~RewritePlanPersistenceTests|FullyQualifiedName~TextLogSystemTests|FullyQualifiedName~PipelineComponentTests"
pwsh -File .\scripts\check-harness-consistency.ps1
```
