# Workspace / MSBuild 输入

> 状态：当前设计。
>
> 读者：需要让 NLISSN 分析 `.sln`、`.csproj` 或带项目语义的单个 `.cs`，以及修改 MSBuild/Roslyn 输入边界的维护者。
>
> 责任边界：Infrastructure 负责加载和快照化 Workspace；Hosting 负责把已加载项目交给既有 Application 流水线；Application、Core 和 Rules 不引用 MSBuild API。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-06。

## 本页回答什么

Workspace 输入使分析可以从一个 solution、MSBuild project，或带 owning project 的单个 `.cs` 文档开始，同时保留真实的 Roslyn compilation、项目引用、目标框架和生成源码事实。没有 `input.project` 的 `.cs` 与目录输入维持既有路由；任何输入都不能绕过 Mark -> Propagate -> Lift -> Propose -> Rewrite 的统一契约。

## 加载与执行

```text
.sln or .csproj
  -> MsBuildWorkspaceInputLoader
  -> WorkspaceSolutionSnapshot / WorkspaceProjectSnapshot
  -> WorkspaceAnalysisService
  -> DirectoryAnalysisUseCase.AnalyzeCompiled
  -> existing decision, evidence, rewrite-plan and diff publication
```

单文档路径只缩小分析和发布范围，不缩小 compilation：

```
.cs + owning .csproj
  -> MsBuildWorkspaceInputLoader opens the project
  -> full project CSharpCompilation
  -> select the matching WorkspaceDocumentSnapshot
  -> DirectoryAnalysisUseCase.AnalyzeCompiled
  -> target-document decisions and artifacts only
```

`src/NLISSN.Infrastructure/Workspace/MsBuildWorkspaceInputLoader.cs` 创建 `MSBuildWorkspace`，按配置选择 solution project，并构建稳定的 project/document/reference snapshot。`src/NLISSN/Hosting/WorkspaceAnalysisService.cs` 为每个已加载项目创建 `CompiledDirectorySourceFile`，将原有 `CSharpCompilation` 传入 `DirectoryAnalysisUseCase`；它不自行运行规则或生成编辑。

## 选择与语义

- `.sln` 需要明确的 project 选择；输入无法唯一确定时失败关闭。
- `.cs` 使用项目语义时必须显式提供 owning `.csproj`；目标文档不在所选项目中时返回 `NLISSNWS025`。
- `TargetFramework`、`Configuration`、`Platform` 和条件属性由 MSBuild evaluation 决定。多目标项目没有可确定选择时拒绝分析，不猜测默认 TFM。
- 项目、框架、包、metadata 和 `ProjectReference` 以 snapshot 保留来源；`ProjectReference` 的目标框架选择受 MSBuild metadata 约束。
- 编译错误、未解析引用、加载失败和取消保留稳定 diagnostics，并在进入 rewrite 前停止。

## 生成源码与写回

MSBuild 可见的 compile item 与显式启用的 source generator 输出都可以进入 compilation 和语义分析。snapshot 为每个文档记录 `IsGenerated`、生成来源和 `CanWrite`：生成文档不能写回，也不能进入物理 `RewritePlan`。`AdditionalFiles`、analyzer config 和 analyzer references 通过实际 Workspace/generator 路径输入，不由测试替身模拟成普通源码。

## 不变量

- Workspace API 只位于 `NLISSN.Infrastructure.Workspace`；Hosting 是唯一的窄适配层。
- 加载后的每个项目仍使用同一个 Application 分析入口和同一套 decision/evidence/rewrite/diff 规则。
- 未提供 `input.project` 的 `.cs` 与目录输入维持独立的既有路由；它们不会隐式搜索或猜测 owning project。
- 单文档输入使用完整项目 compilation，但结果、diff、writeback 和 rewrite plan 只允许目标文档；capture manifest 的 `sourceFileCount` 是 `1`。
- 输入、project、document、reference、diagnostic 和输出均按稳定键排序，不能把加载或并行完成顺序暴露为结果顺序。

## 验证

`tests/NLISSN.HostTests/Workspace/` 覆盖 solution/project 选择、TFM、条件属性、真实引用、生成源码、source generator、AdditionalFiles、analyzer config、失败关闭、取消、稳定 snapshot 和生成文档 rewrite exclusion。扩大回归和串行命令见 [测试策略](testing-strategy.md)。用户配置入口见 [配置与制品](配置与制品.md)，分析阶段边界见 [删除规则流水线](deletion-pipeline.md)。
