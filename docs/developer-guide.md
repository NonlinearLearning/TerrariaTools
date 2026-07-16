# 开发者指南

## 这篇解决什么问题

这篇只回答一件事：如果你要在这个仓库里继续开发、修改、扩展、验证代码，应该按什么顺序做。

这篇不负责讲完整设计历史，也不负责替代设计文档。

## 读这篇前需要什么

1. 已看过根目录 `README.md`
2. 已看过 `docs/quick-start.md`
3. 已读 `AGENTS.md`
4. 已知道当前主要有 `MinimalRoslynCpg` 和 `RoslynPrototype` 两条主线
5. 已知道当前设计入口在 `设计docs/目前设计/项目概览.md`

## 你会学到什么

1. 该先读哪些仓库级文件
2. 不同开发任务该走哪条主线
3. 改代码前如何验证当前状态
4. 测试怎么跑
5. 文档怎么改

## 1. 开始前先读什么

按这个顺序：

1. `AGENTS.md`
2. `progress.md`
3. `feature_list.json`
4. `init.ps1`
5. 你要改动的项目代码

原因很简单：

1. `AGENTS.md` 定义仓库级工作顺序和约束
2. `progress.md` 告诉你当前事实和下一步
3. `feature_list.json` 告诉你特性状态和验收条件
4. `init.ps1` 给你最小健康检查

## 2. 开发前先跑什么

```powershell
pwsh -File .\init.ps1
```

如果你正在动核心逻辑，建议再跑一次你要改的项目：

### MinimalRoslynCpg

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

### RoslynPrototype

```powershell
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s
```

## 3. 改哪条主线

### 3.1 改 `MinimalRoslynCpg`

适合做这些事：

1. 图结构扩展
2. CFG 改进
3. DataFlow 改进
4. 调用关系增强
5. 方法边界抽象增强

优先读这些位置：

1. `src/MinimalRoslynCpg/Builder/`
2. 图节点和边模型
3. 样例代码

### 3.2 改 `RoslynPrototype`

适合做这些事：

1. 规则分析
2. 标记与传播
3. 决策策略
4. rewrite 逻辑
5. 测试样例和回归样例

优先读这些位置：

1. `src/Application/DeletionApplicationService.cs`
2. `设计docs/目前设计/项目概览.md`
3. `约束/删除规则分阶段分析约束.md`
4. `设计docs/目前设计/proposal-fact-extraction.md`
5. `设计docs/目前设计/delete-class-components.md`
6. `设计docs/目前设计/delete-class-propagation.md`
7. `设计docs/目前设计/decision-resolution.md`
8. `设计docs/目前设计/fast-path-boundaries.md`
9. `设计docs/目前设计/cpg-capabilities.md`
10. `设计docs/目前设计/testing-strategy.md`
11. `设计docs/目前设计/atomic-marking.md`
12. `设计docs/README.md`
13. `src/Rules/`
14. `tests/RoslynDeletionPrototype.Tests/`

如果你改的是删除规则架构，先分清楚当前逻辑属于哪一层：

1. `Mark`：原子命中
2. `Propagate`：语义扩展和中间态收集
3. `Lift`：父级结构提升
4. `Propose`：最终 `Delete / Replace / Skip` 决策

当前默认要求是：

1. 不要把 compilation-wide 调用点扫描继续堆进 `Propose`
2. 不要把最终 replacement syntax 提前塞进 `Propagate`
3. 参数收缩、delegate 使用形态、调用点同步这类“中间计划”优先落到 `Propagate`
4. 某个专题设计稿如果还没独立落盘，先以项目级 PRD 和 `设计docs/README.md` 为准，不要凭空补引用

### 并行度与 ThreadPool

运行时只提供一个全局 DOP：`--max-degree-of-parallelism N`。它由 `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs` 解析，并传入目录分析、规则阶段和 `RoslynCpgBuilder`。默认值是 `Environment.ProcessorCount`。

并行入口分别位于：

1. `src/Host/DeletionDirectoryAnalysisService.cs`：目录内文件分析
2. `src/RoslynPrototype/RuleServices/ExecutionRuntime.cs`：有界规则阶段 scheduler
3. `src/MinimalRoslynCpg/Builder/BoundedPartitionWorkWindow.cs`：CPG 分片 worker window
4. `src/RoslynPrototype/RuleServices/RuleHelpers/DeleteClassParameterShrinkAnalyzer.cs`：辅助扫描 `Parallel.ForEach`

CPG 的 `LargeFileLineThreshold`、`LargeFileMethodThreshold`、`LargeMethodLineSpanThreshold` 和 `SyntaxLargeFileLineThreshold` 目前是内部 builder 配置。CLI 没有大文件专用 DOP；所有文件都复用全局 DOP。新增大文件专用 DOP 时，应保持目录级并发受全局上限控制，并明确 CPG 分片的独立上限与阈值触发条件。

目录分析可通过 `--per-file-memory-diagnostics-log <path>` 输出每个完成文件的 GC、进程和 CPG 结构计数。并发运行时，日志记录的是进程共享状态快照，不能当作单个文件的独占内存。

## 4. 测试怎么写

如果你要改测试，先读：

1. `约束/测试代码编写教程.md`
2. 现有测试项目

基本原则：

1. 测试 public 行为
2. 一次只验证一个行为
3. 用 AAA 结构
4. 输入维度、分支维度、异常维度、状态维度、协作对象维度都要考虑

## 5. 文档怎么写

如果你要改项目文档，先读：

1. `约束/高星开源项目文档写法研究与落地约束.md`

项目文档的基本规则：

1. 首页负责分流
2. 学习路径和参考手册分开
3. 页面要有边界
4. 示例要可复制、可运行、可定位
5. 版本边界要显式
6. 涉及删除规则分层时，同步检查 `约束/删除规则分阶段分析约束.md` 和最新设计提案是否仍与代码一致

## 6. 常见开发路径

### 路径 A：你想先把功能做出来

1. 先看 `progress.md`
2. 再看 `feature_list.json`
3. 定位当前特性状态
4. 改代码
5. 跑对应项目
6. 跑测试

### 路径 B：你想修一个 bug

1. 先找到最小复现样例
2. 在测试里把它固化
3. 再改代码
4. 跑测试确认通过

### 路径 C：你想扩展一个分析能力

1. 先看该能力当前在哪一层处理
2. 分清是图层、分析层、规则层还是 rewrite 层
3. 只改必要层
4. 跑样例和测试

## 7. 提交前检查什么

1. 相关项目能 build
2. 相关样例能 run
3. 相关测试能过
4. 文档没有和代码状态冲突
5. `progress.md` 和 `feature_list.json` 没有明显过期

## 8. 不要做什么

1. 不要先猜再改
2. 不要把一处改动扩成全仓库重构
3. 不要在测试里验证实现细节
4. 不要让文档失去页面边界
5. 不要忽略当前仓库的研究型属性

## 9. 下一步

1. 想看项目入口，回根目录 `README.md`
2. 想开始跑，去 `docs/quick-start.md`
3. 想改代码，按上面的路径进入对应项目
