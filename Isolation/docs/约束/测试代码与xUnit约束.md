# 测试代码与 xUnit 约束

## 1. 文档目的

这份文档把三类信息收敛到一处：

1. **网络权威资料**给出的 xUnit / .NET / ABP 测试原则
2. **本仓库真实测试代码**已经落地的写法
3. AI 在本仓库里**应该如何写测试代码**

目标不是再造一套“教科书测试规范”，而是给本仓库一套**能直接执行、能落到代码、能被现有测试证明**的规则。

---

## 2. 结论先行

### 2.1 本仓库测试框架结论

- 新增测试代码默认使用 **xUnit**
- 当前真实基线：
  - `tests/Isolation.AnalysisTests`：xUnit 测试工程
  - `tests/ArchitectureTests`：控制台式架构冒烟工程
- 因此：
  - **行为测试、回归测试、分层能力测试** -> 默认写进 `Isolation.AnalysisTests`
  - **架构边界烟雾验证** -> 继续保留在 `ArchitectureTests`

### 2.2 为什么是 xUnit

结合官方资料与本仓库现状，选择 xUnit 不是抽象偏好，而是当前最符合事实的约束：

- xUnit 官方把 `[Fact]` 和 `[Theory]` 作为核心测试模型
- Microsoft Learn 的 .NET xUnit 教程直接使用 `dotnet new xunit`、`dotnet test`
- ABP 官方测试文档明确把 xUnit 作为默认测试框架
- 本仓库 `tests/Isolation.AnalysisTests/Isolation.AnalysisTests.csproj` 已经显式引用：
  - `xunit`
  - `xunit.runner.visualstudio`
  - `Microsoft.NET.Test.Sdk`

---

## 3. 外部资料结论

## 3.1 xUnit 官方

来源：

- xUnit Getting Started v2  
  https://xunit.net/docs/getting-started/v2/getting-started
- xUnit Sharing Context  
  https://xunit.net/docs/shared-context

收敛结论：

- `[Fact]` 用于固定事实型测试
- `[Theory]` 用于同一行为的多组数据输入
- xUnit 为**每个测试创建新的测试类实例**
- 共享上下文时不要自己发明容器协议，优先用：
  - 测试类构造函数
  - `IClassFixture<TFixture>`
  - collection fixture

这直接决定了本仓库的测试写法：

- 单场景优先 `[Fact]`
- 多输入同语义优先 `[Theory]`
- 能用构造函数就不用先上 fixture
- 只有初始化昂贵且需要共享时，才使用 `IClassFixture<>`

## 3.2 Microsoft Learn

来源：

- Unit testing C# in .NET using dotnet test and xUnit  
  https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-csharp-with-xunit

收敛结论：

- .NET 官方教程直接使用 `dotnet new xunit`
- 典型测试工程会配置：
  - `Microsoft.NET.Test.Sdk`
  - `xunit`
  - `xunit.runner.visualstudio`
- `dotnet test` 是标准执行入口
- 对重复输入场景，官方明确鼓励从重复 `[Fact]` 收敛到 `[Theory]`

这与本仓库完全吻合：

- `tests/Isolation.AnalysisTests/Isolation.AnalysisTests.csproj` 已是同一套依赖组合
- 仓库验证命令也以 `dotnet test` 为主

## 3.3 ABP 官方

来源：

- Automated Testing  
  https://abp.io/docs/10.0/testing/overall
- Unit Tests  
  https://abp.io/docs/latest/testing/unit-tests
- Integration Tests  
  https://abp.io/docs/10.3/testing/integration-tests

收敛结论：

- ABP 把测试分成 unit / integration / UI 三层
- ABP 默认测试基础设施使用：
  - xUnit
  - NSubstitute
  - Shouldly
- ABP 建议按需要混合 unit test 与 integration test
- 对集成测试，ABP 强调：真实服务协作往往更接近实际应用
- 对数据库集成测试，ABP 明确推荐真实能力更接近的方案，而不是随意简化成失真的假实现

这给本仓库的启发不是“照搬 ABP 工具栈”，而是两点：

1. **按层划分测试责任**
2. **集成程度由测试目标决定，而不是一律 mock 或一律全栈**

---

## 4. 本仓库真实代码验证

## 4.1 测试项目与依赖

验证文件：

- `tests/Isolation.AnalysisTests/Isolation.AnalysisTests.csproj`
- `tests/ArchitectureTests/ArchitectureTests.csproj`

验证结果：

- `Isolation.AnalysisTests` 已经是 xUnit 工程
- `ArchitectureTests` 目前不是 xUnit，而是控制台式烟雾验证项目

因此规则必须写成：

- **新增测试代码默认使用 xUnit**
- **ArchitectureTests 是现有例外，不在本轮强行迁移**

## 4.2 仓库现有测试写法

### A. `[Fact]` 单场景测试

文件：

- `tests/Isolation.AnalysisTests/Analysis/AnalysisInputValidationRulesTests.cs`

可验证模式：

- 一个方法验证一个稳定行为
- 使用 `Assert.True` / `Assert.False` / `Assert.Contains`

### B. 结构型断言

文件：

- `tests/Isolation.AnalysisTests/Query/QueryEngineDetailTests.cs`
- `tests/Isolation.AnalysisTests/Workflow/WorkflowDomainEventsTests.cs`

可验证模式：

- `Assert.Single(...)`
- `Assert.All(...)`
- `Assert.Contains(...)`
- `Assert.Equal(...)`

这说明本仓库更偏好**结构语义断言**，而不是只写“大对象全等”。

### C. 应用层编排测试

文件：

- `tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs`

可验证模式：

- 直接构建工作流上下文
- 调用 `AppService`
- 断言 `RunCorrelationId`、事件流、跨阶段一致性

这说明应用层测试重点是：

- 用例是否闭环
- 编排是否串起来
- DTO / 事件 / 结果是否统一

### D. 基础设施 / Analysis.Engine 测试

文件：

- `tests/Isolation.AnalysisTests/Frontend/AstModelTests.cs`
- `tests/Isolation.AnalysisTests/X2Cpg/X2CpgEntryTests.cs`

可验证模式：

- 用最小图、最小源码驱动技术能力
- 断言 AST / edge / overlay / config 的稳定结果

### E. 参数化测试

文件：

- `tests/Isolation.AnalysisTests/Rewrite/RoslynRewriteConventionsTests.cs`
- `tests/Isolation.AnalysisTests/Semantic/ScopeAndUtilsTests.cs`

验证结论：

- 仓库已实际使用 `[Theory]`
- 所以“多输入同语义写成 `[Theory]`”不是理论要求，而是仓库现状

---

## 5. 本地参考项目验证

## 5.1 ABP 本地资料

已验证资料：

- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\overall.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\unit-tests.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\integration-tests.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\modules\blogging\test\Volo.Blogging.TestBase\Volo\Blogging\Comments\CommentRepository_Tests.cs`

本地 ABP 实际样式说明：

- 测试类通过构造函数获取被测服务
- 使用 `[Fact]`
- 断言重点放在集合、数量、业务结果，而不是框架内部状态

## 5.2 Orleans 本地资料

已验证文件：

- `C:\Users\shan\Downloads\api开源教程学习\orleans-main\test\Orleans.Runtime.Tests\DeactivationTracingTests.cs`

本地 Orleans 实际样式说明：

- 真实使用 `IClassFixture<TFixture>`
- 当测试上下文昂贵且跨多个测试共享时，fixture 是合理做法

这能证明：fixture 规则不仅来自 xUnit 文档，也来自真实大型 .NET 项目实践。

---

## 6. 各层如何写测试

## 6.1 Domain 层

### 应测什么

- 聚合根行为
- 实体状态迁移
- 值对象校验
- 领域事件顺序与载荷
- 不变量是否被守住

### 不该只测什么

- 只测属性 get/set
- 只测 DTO 映射
- 只测“new 以后字段有值”

### 本仓库参考

- `tests/Isolation.AnalysisTests/Workflow/WorkflowDomainEventsTests.cs`
- `tests/Isolation.AnalysisTests/Analysis/AnalysisInputValidationRulesTests.cs`

### 推荐写法

```csharp
[Fact]
public void WorkflowEventSequenceBuilder_buildsOrderedSuccessPath()
{
    RewriteWorkflowArtifacts artifacts = BuildArtifacts(...);
    string[] names = artifacts.DomainEvents.Select(x => x.DomainEvent.EventName).ToArray();

    Assert.Contains("RewritePlanCompiled", names);
    Assert.Contains("ExecutionCompleted", names);
    Assert.True(IndexOf(names, "RewritePlanCompiled") < IndexOf(names, "ExecutionCompleted"));
}
```

这个模式来自：

- xUnit 官方 `[Fact]`
- 本仓库 `WorkflowDomainEventsTests`

## 6.2 Logic 层

### 应测什么

- 单阶段纯能力
- 查询和推理结果
- 中间结构装配
- 小图上的算法/规则结果

### 本仓库参考

- `tests/Isolation.AnalysisTests/Query/QueryEngineDetailTests.cs`

### 推荐写法

```csharp
[Fact]
public void HeldTaskCompletion_resolvesPreviouslyHeldTasksWhenResultArrives()
{
    QueryTask task = new(42);
    DataFlowPath path = new(new long[] { 1, 42 });
    HeldTaskCompletion completion = new();

    completion.Hold(task);
    completion.AddResult(task, path);

    IReadOnlyList<DataFlowPath> completed = completion.CompleteHeldTasks();
    Assert.Equal(path.NodeIds, Assert.Single(completed).NodeIds);
}
```

核心点：

- 输入最小化
- 断言结构化
- 不引入多余基础设施

## 6.3 Application 层

### 应测什么

- 用例编排
- 跨阶段关联 id
- DTO 输出
- 事件汇总与阶段衔接

### 本仓库参考

- `tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs`

### 推荐写法

```csharp
[Fact]
public async Task RewriteWorkflowAppService_uses_single_run_correlation_across_full_stage_chain()
{
    WorkflowTestContext context = await WorkflowTestContext.CreateAsync();
    Guid runCorrelationId = Guid.NewGuid();

    var result = await context.Service.RunAsync(BuildRequest(context, runCorrelationId));

    Assert.Equal(runCorrelationId, result.RunCorrelationId);
    Assert.All(result.DomainEvents, item => Assert.Equal(runCorrelationId, item.CorrelationId));
    Assert.Contains(result.DomainEvents, item => item.EventName == "DecisionCompleted");
}
```

关键不是 mock 得多细，而是：

- 用例有没有跑通
- 关键业务关联是否没串台

## 6.4 Infrastructure / Analysis.Engine 层

### 应测什么

- 技术适配的稳定输入输出
- 图结构、edge、overlay、路径、文件约定
- rewrite / loader / gateway 的外显结果

### 本仓库参考

- `tests/Isolation.AnalysisTests/Frontend/AstModelTests.cs`
- `tests/Isolation.AnalysisTests/X2Cpg/X2CpgEntryTests.cs`

### 推荐写法

```csharp
[Fact]
public void Ast_supportsArgumentReceiverAndConditionEdges()
{
    CpgGraph graph = new();
    CpgNode call = graph.CreateNode(CpgNodeKind.Call);
    CpgNode receiver = graph.CreateNode(CpgNodeKind.Identifier);
    CpgNode argument = graph.CreateNode(CpgNodeKind.Identifier);

    Ast.FromRoot(call)
        .WithChild(Ast.FromRoot(receiver))
        .WithChild(Ast.FromRoot(argument))
        .WithReceiverEdge(call, receiver)
        .WithArgEdges(call, new[] { argument }, 1)
        .StoreInGraph(graph);

    Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.Receiver), edge => edge.TargetId == receiver.Id);
    Assert.Contains(graph.GetOutgoingEdges(call.Id, CpgEdgeKind.Argument), edge => edge.TargetId == argument.Id);
}
```

这里断言的是**稳定技术语义**，不是 Roslyn 私有内部步骤。

## 6.5 Architecture / 边界保护

### 应测什么

- 分层依赖
- 命名空间
- 共享内核标记
- DTO / Mapper / 边界约束

### 当前规则

- 继续保留在 `tests/ArchitectureTests`
- 维持现有控制台式烟雾风格
- 只有在明确做测试基础设施迁移时，才统一改成 xUnit

---

## 7. AI 如何写测试

## 7.1 必守规则

- **先搜现有测试，再写新测试**
- **先决定测试层级，再决定测试目录**
- **新增测试默认使用 xUnit**
- **异步测试返回 `Task`**
- **异步异常用 `Assert.ThrowsAsync<T>`**
- **多组输入同一语义优先 `[Theory]`**
- **需要共享昂贵上下文时才用 fixture**
- **断言优先表达语义，而不是表达实现细节**

## 7.2 命名规则

优先以下风格：

- `Xxx_builds_yyy`
- `Xxx_rejects_yyy_when_zzz`
- `Xxx_uses_yyy_across_zzz`

本仓库已验证的命名样式：

- `WorkflowEventSequenceBuilder_buildsOrderedSuccessPath`
- `RewriteWorkflowAppService_uses_single_run_correlation_across_full_stage_chain`
- `AccessPathUsage_extractsMemberPathFromNestedAst`

## 7.3 断言规则

优先顺序：

1. `Assert.Single`
2. `Assert.All`
3. `Assert.Contains`
4. `Assert.Equal`
5. `Assert.ThrowsAsync`

原因：

- 这些写法已经在仓库中大量存在
- 它们能直接表达“唯一”“全部满足”“包含某语义”“精确相等”“异常契约”

## 7.4 何时用 `[Theory]`

满足以下任一条件就优先考虑 `[Theory]`：

- 同一行为要验证多组输入
- 差异只在参数，不在业务流程
- 重复 `[Fact]` 只是在复制粘贴数据

不满足时就继续 `[Fact]`。

## 7.5 何时用 fixture

先后顺序：

1. 测试类构造函数
2. `IClassFixture<>`
3. collection fixture

只有当上下文昂贵、初始化复杂、跨多个测试共享时，才往后走。

---

## 8. 本仓库最终约束

### 8.1 框架约束

- 新增测试代码默认使用 **xUnit**
- 不再引入第二套 .NET 测试框架作为默认写法

### 8.2 落位约束

- `Isolation.AnalysisTests`：行为、回归、编排、Analysis.Engine、Roslyn、Query、Slicing
- `ArchitectureTests`：架构边界烟雾验证

### 8.3 写法约束

- `[Fact]`：单场景
- `[Theory]`：参数化
- `Task`：异步测试返回类型
- `Assert.ThrowsAsync<T>`：异步异常
- fixture：仅在共享昂贵上下文时使用

### 8.4 AI 约束

- 未搜索现有测试样例前，不要直接新建测试基础设施
- 未确认测试层级前，不要直接写到错误的测试项目
- 未找到网络权威资料与本地真实样例前，不要把“经验写法”升级成仓库硬规则

---

## 9. 本文依据

### 网络资料

- xUnit: Getting Started v2  
  https://xunit.net/docs/getting-started/v2/getting-started
- xUnit: Sharing Context between Tests  
  https://xunit.net/docs/shared-context
- Microsoft Learn: Unit testing C# in .NET using dotnet test and xUnit  
  https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-csharp-with-xunit
- ABP: Automated Testing  
  https://abp.io/docs/10.0/testing/overall
- ABP: Unit Tests  
  https://abp.io/docs/latest/testing/unit-tests
- ABP: Integration Tests  
  https://abp.io/docs/10.3/testing/integration-tests

### 本仓库验证文件

- `tests/Isolation.AnalysisTests/Isolation.AnalysisTests.csproj`
- `tests/Isolation.AnalysisTests/Analysis/AnalysisInputValidationRulesTests.cs`
- `tests/Isolation.AnalysisTests/Query/QueryEngineDetailTests.cs`
- `tests/Isolation.AnalysisTests/Workflow/WorkflowDomainEventsTests.cs`
- `tests/Isolation.AnalysisTests/Workflow/RewriteWorkflowAppServiceCorrelationTests.cs`
- `tests/Isolation.AnalysisTests/Frontend/AstModelTests.cs`
- `tests/Isolation.AnalysisTests/X2Cpg/X2CpgEntryTests.cs`
- `tests/ArchitectureTests/Program.cs`

### 本地参考资料

- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\overall.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\unit-tests.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\docs\en\testing\integration-tests.md`
- `C:\Users\shan\Downloads\api开源教程学习\abp-dev\modules\blogging\test\Volo.Blogging.TestBase\Volo\Blogging\Comments\CommentRepository_Tests.cs`
- `C:\Users\shan\Downloads\api开源教程学习\orleans-main\test\Orleans.Runtime.Tests\DeactivationTracingTests.cs`

## 文档同步与实现约束（2026-04 全量升级）

### 文档类型

本文属于：仓库约束文档。

### 代码对齐文档要求

- 影响本文覆盖范围的代码变更，默认同批更新本文，或在同一任务链路说明无需更新的理由。
- 本文中的路径、类型、方法、流程、默认值、已知问题和验收口径失效时，必须同步修正。
- 关键结论优先绑定真实代码、真实测试、真实计划和真实日志。

### 文档对齐代码要求

- 实现本文覆盖范围内的代码前，先读取 `docs/约束/代码对齐文档约束.md` 与 `docs/约束/文档对齐代码约束.md`。
- 代码与本文冲突时，当轮完成“改代码”或“改文档”的闭环。
- 稳定规则优先继续下沉到测试、ArchitectureTests、构建检查或流程守护。

### 默认代码锚点

- `AGENTS.md`
- `tests/ArchitectureTests/Program.cs`
- `.omx/plans/*.md`
- `log/*.log`

### 交付检查

- 本文与当前代码事实一致；
- 本文与当前测试、计划、日志不冲突；
- 本文涉及的关键约束具备可追踪验证锚点。
