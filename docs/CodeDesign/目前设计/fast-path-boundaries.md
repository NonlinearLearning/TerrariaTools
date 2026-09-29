# Fast Path 边界

> 状态：当前设计。
>
> 读者：修改目录分析、compilation-wide 规则或 Host 路由的维护者。
>
> 责任：当前仓库维护者。
>
> 更新时间：2026-08-04。

## 为什么存在

单文件规则可以直接从一棵 syntax tree 开始；目录级规则需要统一的 `CSharpCompilation`、文件索引、跨文件引用和稳定结果聚合。Fast path 是这个 Host 层输入边界，不是另一个可以绕过 staged contract 的规则引擎。

当前入口：

```text
src/NLISSN/Hosting/CommandHost.cs
  -> src/NLISSN/Hosting/DirectoryAnalysisService.cs
  -> src/NLISSN.Application/Analysis/DirectoryAnalysisUseCase.cs
  -> one compilation + stable source indexes
  -> ApplicationService per file
  -> category diff / evidence / rewrite plan
```

当配置启用 `DeleteUnreachableMethods`、`DeleteUnreferencedMethods`、`ClearUnusedInterfaceImplementations` 或 `PrivatizeInternalOnlyPublicMethods` 时，Host 使用显式规则策略创建默认规则集；目录用例再为所有输入构建 compilation，并按稳定文件索引分析、聚合和发布。`MethodLinkageAnalysis` 以 compilation-level cache 复用，不把未知调用降级成“没有引用”。

## Fast path 允许做什么

- 读取和排序目录输入，创建统一 compilation 与 semantic model。
- 执行确实需要跨文件引用、entry/root 或 declaration cleanup 的规则。
- 为每个源文件调用同一 `ApplicationService` staged pipeline。
- 按输入索引聚合 file results、diagnostics、evidence、rewrite plan 和 category diff。
- 在固定的 artifact/run scope 内复用 compilation cache 和运行时 telemetry。

## 明确禁止

- 把局部 Mark、Propagate 或 Propose 复制到 Host 以逃避 rule contract。
- 绕过 `RuleBindingValidator`、`DecisionBindingValidator` 或 topology terminal。
- 根据“目录里没有查到引用”直接删除 public API、动态调用或未知边。
- 让并行完成顺序决定文件、decision、diff 或日志顺序。
- 用 post-rewrite 文本清理替代有 provenance 的规则 proposal；只保留明确、独立且可验证的 cleanup。

## 何时迁回 staged pipeline

当规则只依赖单文件/明确输入端口、无需 compilation-wide 根集合，并能以 Mark -> Propagate -> Lift -> Propose 表达时，应迁回普通 pipeline。迁移顺序是先固定当前 fixture 的 decision、evidence、rewrite 和 diff，再拆出阶段规则，最后删除 fast path 分支；不能先删除旧路径再用绿测试推测等价。

## 验证

目录改动至少覆盖：空目录、单文件、多文件、编译错误、缺 entry point、local function/lambda 引用、目标文件筛选、部分 edit、无 edit、write-back 失败和 DOP 1、2、16。必须比较 file result、聚合 result、evidence、rewrite plan、rewritten source、分类 diff 和发布顺序。

入口测试包括 `tests/NLISSN.UnitTests/Application/DirectoryAnalysisUseCaseTests.cs`、`tests/NLISSN.HostTests/Application/ApplicationServiceFlowTests.cs`、`PipelineComponentTests.cs`、`tests/NLISSN.HostTests/Application/RuleGraphCompilerTests.cs` 和 MethodGlobal focused fixtures。用户运行方式见 [项目概览](项目概览.md) 与 [`docs/quick-start.md`](../../quick-start.md)。
