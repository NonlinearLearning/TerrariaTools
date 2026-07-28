# 全局规则 DAG 执行提案

> **状态：框架已落地，默认规则的原子输入映射尚未完成。** 图编译器、ready queue 和部分 typed 边已实现；但当前 `RuleGraphDependencyCatalog` 仍含 family barrier，且若干默认传播规则尚未声明其 seed 输入。因此不能将当前默认管道视为已完成的原子级 DAG 迁移。

## 目标

以单条规则为最小调度单元，由规则间显式依赖决定执行时机。移除 `GroupKey` 对调度的控制作用，在保持当前可观察结果等价的前提下，让无依赖规则和已满足依赖的规则并发执行。

## 箭头约定

本文固定使用下列方向：

```text
Mark1 <- Propagate1
```

表示 `Propagate1` 依赖 `Mark1` 的输出；图中的数据流方向为 `Mark1 -> Propagate1`。

`DependsOn` 永远声明在消费者规则上：

```csharp
Propagate1.DependsOn = new[] { Mark1 };
```

## 迁移前的问题

`ApplicationService` 目前固定调用四个引擎。`MarkingEngine` 按规则运行；`PropagationEngine`、`MarkLiftingEngine` 和 `RuleDecisionEngine` 按 `GroupKey` 将同组规则串行化。该分组同时承担规则家族路由、输入过滤和执行顺序，无法表达：

- 同一 `GroupKey` 内的独立规则；
- 同阶段规则间的精确前置关系；
- 跨规则类别的依赖，例如 `Mark <- Propagate` 或 `Lift <- Propose`；
- 一个消费者只读取指定生产者输出，而不是增长中的全组 mark 集合。

历史上的两条隐式 symbol-reference 依赖已迁移：`SObjectSymbolReferencePropagationRule` 消费 initializer definition typed output，`ClassSymbolReferencePropagationRule` 消费 object-creation definition typed output。其余默认规则的输入边仍须逐条确认，不能从这两条链的完成情况推导全图已原子化。

## 已落地部分与验证

- `RuleGraph`、编译器和执行器位于 `src/NLISSN.Core/Pipeline/`；编译器拒绝重复节点、未知 producer、不兼容 output、重复 producer 依赖和环，并按声明顺序稳定物化结果。
- `ApplicationService` 可通过 `EnableRuleGraphExecution` 选择全局图路径，并由 `RuleGraphAnalysisExecutor` 一次调度 Mark、Propagate、Lift、Propose。图契约允许跨类别边，不要求固定阶段屏障。
- 四个 Engine 的兼容 `Run` / `Decide` 入口可委托给 `RuleGraphExecutor`；`GroupKey` 不参与 ready queue 或依赖推导，但仍保留为记录兼容字段和去重投影。
- 两条 symbol-reference 链使用 `LocalDefinitionFromInitializer` / `LocalDefinitionFromObjectCreation` typed producer output；switch 只依赖 host、if lift 事实，并从这些事实继承 source provenance。
- 一部分 Proposal 规则已按 declaration、parameter-usage、logical 或 if producer 声明单一依赖；Default/Control 仍显式汇聚整个 family。该汇聚目前是语义保守措施，不是原子并发的完成状态。

已验证：规则图聚焦测试 `13/13` 通过，包括 DOP 1/16 快照、跨 GroupKey 依赖、稳定顺序、空 producer 输出、未知 producer、output 类型不匹配和环。此前记录的 Host 全量与三次预热夹具结果可作为回归参考，但不证明默认规则的依赖清单已经完整。

未作为 DAG 缺陷处理的验证边界：完整 Performance 在 304 秒边界超时；Contract Tests 为 `217/220`，失败于旧测试项目路径和架构基线；Unit Tests 因 `StructureViewBuilderTests.cs` 对 `NLISSN.Core.Pipeline` 的项目引用不可见而无法编译；harness 仍引用已迁移的 `src/RoslynPrototype/Program.cs`。

## 设计

### 全局图

`RulePipeline` 编译为单一 `CompiledRuleGraph`。规则的 `RuleKind` 保留 `Mark`、`Propagate`、`Lift`、`Propose` 四类，用于输入输出校验、结果投影和诊断；它不再构成执行屏障。

```text
source facts
  -> Mark rules
  -> Propagate rules
  -> Lift rules
  -> Propose rules
  -> conflict resolution
  -> rewrite
```

上图只是当前默认规则的常见形态。图边可以连接任意规则类别，但必须无环；一个规则只能依赖自身所在类别或任意类别的已完成生产者，不能形成回边。

### 规则节点与结果

新增 Core 级图契约：

```csharp
public sealed record RuleNodeId(string Value);

public sealed record RuleDependency(
    RuleNodeId Producer,
    RuleOutputKind RequiredOutput);

public interface IRuleGraphNode
{
    RuleNodeId NodeId { get; }
    RuleKind Kind { get; }
    IReadOnlyList<RuleDependency> DependsOn { get; }
    RuleNodeResult Execute(RuleContext context, RuleNodeInputs inputs);
}
```

`RuleNodeInputs` 只暴露已声明生产者的稳定输出。一个依赖生产者即使产出空集合也视为完成，消费者得到空集合并执行一次。规则不得读取未声明生产者、全局增长集合或任务完成顺序。

`RuleNodeResult` 使用 typed output，而非 `Reason` 文本：

```text
SeedMark
LocalDefinitionFromInitializer
LocalDefinitionFromObjectCreation
LocalReference
LogicalHost
IfCompletion
ExpressionHost
IfStructure
SwitchStructure
DecisionUnit
```

一个输出可携带多个父输出 ID，支持 logical、if、switch 与 conflict domain 的多输入汇聚。现有 `MarkRecord`、`PropagatedMarkRecord`、`LiftedMarkRecord` 和 `DecisionUnit` 继续作为兼容投影，直到所有 rewrite 与日志消费者完成迁移。

### 编译与调度

`RuleGraphCompiler` 在 `ApplicationService` 构造时执行一次：

1. 验证 `RuleNodeId` 唯一，所有依赖生产者存在，所需 output kind 与生产者兼容。
2. 使用 Kahn 拓扑排序拒绝环，错误信息列出环内 RuleId。
3. 为每个节点保存稳定声明序号，作为结果物化的次级排序键。
4. 生成入度表和下游邻接表，供每次分析创建轻量执行状态。

`RuleGraphExecutor` 在一次分析内维护 ready queue、按 `RuleNodeId` 的不可变结果仓和依赖计数。所有入度为零的规则可并发；节点完成后只唤醒直接下游节点。结果物化以拓扑层、规则声明序和规则自身输出排序为准，绝不使用 Task 完成顺序。

调度批次仍以“单条规则运行一次”为单位。不得把每个 `MarkRecord` 变成 Task；大量候选继续由规则内部以数组或枚举批处理，避免调度成本超过分析收益。

### GroupKey 的保留边界

`GroupKey` 从 scheduler、依赖推导和 ready queue 删除。现有记录上的字段先保留，用于 rewrite 兼容、日志与家族筛选；后续单独评估是否改名为 `RuleFamilyId`。本提案不以删除该字段为验收目标。

### 当前默认规则的初始边

首个迁移切片只落下已由源码确认的边：

```text
DEL-SOBJ-PROP-DECL-INIT-001
  <- DEL-SOBJ-PROP-SYMBOL-001

DEL-CLASS-PROP-NEW-DECL-001
  <- DEL-CLASS-PROP-LOCAL-REF-001

DEL-SOBJ-LIFT-HOST-001
  <- DEL-SOBJ-LIFT-SWITCH-001
DEL-SOBJ-LIFT-IF-001
  <- DEL-SOBJ-LIFT-SWITCH-001

DEL-CLASS-LIFT-HOST-001
  <- DEL-CLASS-LIFT-SWITCH-001
DEL-CLASS-LIFT-IF-001
  <- DEL-CLASS-LIFT-SWITCH-001
```

兼容 lift 入口仍可自行重算 host 和 if 中间结果；图入口则把 host/if 规则发布的 typed lift fact 传给 switch rule。对外输出的 switch `RuleId`、span、reason 和 rewrite 结果不变。

其余 if、logical 与 Proposal 规则的输入依赖必须通过回归夹具和输出追踪逐条确认。禁止把当前 `groupMarks` 的隐式可见性直接翻译为“依赖全部前序规则”，否则图会退化为原来的串行组。

## 默认图依赖审计

下表描述的是当前源码已声明的图边，而不是目标状态。`family` 表示消费者等待同一删除家族的所有列举节点；它保留了旧阶段可见性，必须继续拆分。

| 节点集合 | 当前输入 | 判定 |
| --- | --- | --- |
| 所有 Mark | 无图输入，独立扫描 root | 原子，可并发。 |
| `DEL-SOBJ-PROP-SYMBOL-001` | `DEL-SOBJ-PROP-DECL-INIT-001:LocalDefinitionFromInitializer` | 已精确。 |
| `DEL-CLASS-PROP-LOCAL-REF-001` | `DEL-CLASS-PROP-NEW-DECL-001:LocalDefinitionFromObjectCreation` | 已精确。 |
| 其余 SObject / Class Propagate | 当前没有声明 seed producer | 未完成。旧引擎把同 family 的全部 seed marks 传给每条规则；图路径不能把空依赖解释为“读取全部 seed”。 |
| SObject host / if lift | 16 个 SObject Mark + 6 个 SObject Propagate | `family` barrier，未原子化。 |
| Class host / if lift | 3 个 Class Mark + 9 个 Class Propagate | `family` barrier，未原子化。 |
| 两条 switch lift | 各自的 host `ExpressionHost` 和 if `IfStructure` | 边本身精确；但仍间接受上游 host/if 的 family barrier 限制。 |
| logical / if Proposal 与各 parameter-shrink Proposal | 一个对应 Propagate producer | 已精确，但当前输出适配仍须保证只交付该 output kind。 |
| SObject / Class Default、Control Proposal | 分别汇聚 `16 + 6 + 3`、`3 + 9 + 3` 个节点 | 有意保守的 terminal barrier；必须拆成“专门规则已接管”的显式索引后才能缩小。 |
| 独立提案 `DEL-DEAD-001`、未引用方法、未使用接口实现、内部 public 私有化 | 一个对应 Mark producer | 已精确。 |

当前实现还存在三个必须在删除旧阶段路径前解决的契约缺口：

1. `RulePipeline.CompileRuleGraph()` 会过滤掉不在当前 pipeline 内的 dependency。图编译器本身能拒绝未知 producer，但默认入口先删除了该证据；应改为显式 external-input node，或让编译器报出缺失依赖。
2. `RuleGraphAnalysisExecutor.CreateResult(...)` 将同一记录列表复制到节点声明的每个 `RuleOutputKind`。这只是兼容投影，不是 typed port；必须按记录的 `OutputKind` 分桶，或将 typed payload 建模为独立结果。
3. `RuleGraphAnalysisExecutor` 只向节点传入声明依赖的输出。为保持旧语义，每条消费 seed / propagated / lifted 集合的规则都必须声明完整且最小的 producer 集合，或改为显式、可审计的 selector / aggregator 节点；禁止以空 dependency 作为隐式全量输入。

因此，本提案的“每条默认规则有显式依赖输入”完成条件目前**未满足**。后续实现应先用逐规则输入快照确定最小 producer 集合，再删除这些 family barrier；不得以将所有默认规则同时置零依赖来换取表面的并发。

## 不变条件

- DOP 1 与 DOP 16 的 seed、propagated、lifted、decision、rewrite source、diff 和编译诊断完全等价。
- 规则输出的既有去重键、source order 和 RuleId provenance 保持不变。
- 同一 conflict domain 在所有 Proposal 生产者完成前不得 Resolve；不同 conflict domain 可以并发 Resolve。
- 取消、异常和空输出必须终止或唤醒下游节点，不能令 ready queue 永久等待。
- 默认 DOP、CPG 构建并发和 CLI 参数不在本提案范围内。

## 实施任务

### 任务 0：锁定基线并修复验证前置条件

**文件：**

- 修改：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`
- 修改：`tests/RoslynDeletionPrototype.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- 验证前置条件：`init.ps1` 与各测试项目的 `ProjectReference`

1. 新增结果快照 helper，稳定投影 RuleId、kind、span、payload、decision、rewrite 和 diff。
2. 为两条 symbol-reference 链和两个 switch lift 链建立最小 source fixture。
3. 在当前重命名后的项目路径上修复或明确阻塞 `init.ps1`、测试项目引用；这项工作应由其所属重命名任务单独提交，不能把误指向路径当作 DAG 回归。
4. 对每个 fixture 先运行旧引擎 DOP 1 和 DOP 16，保留相同快照作为迁移基线。

**验收：** 任一后续任务都能比较旧、新执行器的完整可观察输出，而非只比较规则数量。

### 任务 1：定义图契约和编译器

**文件：**

- 新建：`src/NLISSN.Core/Pipeline/RuleGraph.cs`
- 新建：`src/NLISSN.Core/Pipeline/RuleGraphCompiler.cs`
- 修改：`src/NLISSN.Core/Pipeline/RuleDefinition.cs`
- 修改：`src/NLISSN.Core/Pipeline/RulePipeline.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 先写失败测试：唯一节点、未知依赖、重复依赖、output kind 不兼容和同阶段循环分别得到确定性异常。
2. 增加 `RuleNodeId`、`RuleKind`、`RuleOutputKind`、`RuleDependency` 与 `CompiledRuleGraph`。
3. 将四类既有 `RuleDefinition` 包装为图节点；尚未迁移的规则默认只依赖其旧阶段输入适配器。
4. 实现稳定拓扑排序和邻接表，禁止通过 `GroupKey` 建边。
5. 运行图编译测试和现有 RuleRegistry 覆盖测试。

**验收：** 图能用明确的箭头方向解释任一依赖，且无环检查不依赖运行时 Task 时序。

### 任务 2：实现规则级 DAG 执行器和兼容物化

**文件：**

- 新建：`src/NLISSN.Core/Pipeline/RuleGraphExecutor.cs`
- 新建：`src/NLISSN.Core/Pipeline/RuleNodeResult.cs`
- 修改：`src/NLISSN.Core/Pipeline/ExecutionRuntime.cs`
- 修改：`src/NLISSN.Application/Analysis/ApplicationService.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 写失败测试：两个无依赖规则并发；消费者仅在全部依赖完成后运行；依赖产出为空时消费者仍运行一次；取消和生产者异常不会造成等待。
2. 基于现有 `IRuleStageScheduler` 实现 ready queue，去除各 Engine 内的嵌套 `Task.Run` 调度。
3. 将节点结果按 RuleNodeId 存储，并用稳定拓扑序物化回既有四类结果列表。
4. `ApplicationService` 增加新执行器路径，旧四 Engine 路径保持为可比较的兼容基线。
5. 比较新旧路径在 fixture 上的全量快照。

**验收：** 执行器调度单位是规则节点；无依赖节点并发，结果顺序不随并发完成顺序变化。

### 任务 3：迁移两条 typed propagation 依赖

**文件：**

- 修改：`src/NLISSN.Core/Marking/MarkRecord.cs`
- 修改：`src/NLISSN.Core/Propagation/PropagatedMarkRecord.cs`
- 修改：`src/NLISSN.Rules/Propagate/TargetPropagationRules.cs`
- 修改：`src/NLISSN.Rules/Propagate/TypeObjectCreationDeclarationPropagationRule.cs`
- 修改：`src/NLISSN.Rules/Propagate/TypeSymbolReferencePropagationRule.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 写失败测试：symbol-reference 规则从 typed definition output 消费输入，不依赖 reason 字符串。
2. 增加 `LocalDefinitionFromInitializer` 与 `LocalDefinitionFromObjectCreation` output kind，并保留 reason 仅作诊断文本。
3. 声明并接线两条初始 DAG 边。
4. 删除两个 symbol-reference rule 对 `Reason.Contains(...)` 的判断。
5. 比较 shadowing、跨作用域、空 definition、重复 reference 与 DOP 快照。

**验收：** 两条规则链只在指定生产者完成后触发，且现有局部变量边界不变。

### 任务 4：提取 lift 中间事实并迁移 switch 依赖

**文件：**

- 修改：`src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- 修改：`src/NLISSN.Rules/Lift/TargetExpressionHostLiftingRule.cs`
- 修改：`src/NLISSN.Rules/Lift/TargetIfStructureLiftingRule.cs`
- 修改：`src/NLISSN.Rules/Lift/TargetSwitchStructureLiftingRule.cs`
- 修改：`src/NLISSN.Rules/Lift/TypeLiftingRules.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Decision/DecisionStructureValidationTests.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 写失败测试：switch lift 等待 host 和 if producer，且只计算一次每类中间事实。
2. 让 host/if lift 发布内部 typed fact；兼容投影继续输出原有 lifted record。
3. 让两条 switch lift rule 声明 host 与 if 依赖，删除各自的重复计算。
4. 校验 switch section、switch statement、if/else-if/else 和嵌套结构的输出等价。
5. 记录每个 lift node 的输入、输出、耗时与分配，供性能测试使用。

**验收：** switch 路径的重复 host/if 扫描消失，外部 `LiftedMarkRecord` 和 rewrite 结果不变。

### 任务 5：迁移 Propose 与 conflict domain 收口

**文件：**

- 修改：`src/NLISSN.Core/Decision/DecisionModel.cs`
- 修改：`src/NLISSN.Core/Rewrite/PrototypeAnalysisResult.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Decision/DecisionStructureValidationTests.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/Application/PipelineComponentTests.cs`

1. 写失败测试：独立 Proposal rule 并发产出 unit；同一 conflict key 等待全部相关 Proposal producer 后才 Resolve。
2. 将 Proposal rule 的输入声明为明确的 graph dependency，替代按 `GroupKey` 查三类全量列表。
3. 将 conflict resolution 作为终端图节点；按 conflict key 分桶并发 Resolve，保持每个桶的原有排序策略。
4. 验证 Replace、Delete、嵌套 Delete 过滤和跨 rule 冲突的结果稳定。

**验收：** Proposal 调度不依赖组串行，冲突策略仍看到完整 conflict domain。

### 任务 6：迁移剩余规则并删除旧阶段调度

**文件：**

- 修改：`src/NLISSN.Core/Marking/MarkingEngine.cs`
- 修改：`src/NLISSN.Core/Propagation/PropagationEngine.cs`
- 修改：`src/NLISSN.Core/Lifting/MarkLiftingEngine.cs`
- 修改：`src/NLISSN.Core/Decision/DecisionModel.cs`
- 修改：`src/NLISSN.Application/Analysis/ApplicationService.cs`
- 修改：`src/NLISSN.Core/Pipeline/RuleStageGroupKey.cs`
- 测试：`tests/RoslynDeletionPrototype.HostTests/**`

1. 对每条剩余规则建立输入输出追踪，明确其图边；if/logical 规则必须有单独的多输入夹具。
2. 逐条移除旧 `groupMarks` 和各 Engine 的 `RunGroupsInParallel` 调度路径。
3. 在所有消费者完成迁移前保留 `GroupKey` 的兼容投影；删除时不得改变重写或日志字段。
4. 删除无法再触达的阶段分组代码和仅为其服务的测试。
5. 运行全量 Host、Contract、Unit 与 Performance 测试。

**验收：** 唯一调度入口为 `RuleGraphExecutor`；不存在由 `GroupKey` 决定的执行顺序。

### 任务 7：性能测量与发布判定

**文件：**

- 修改：`tests/RoslynDeletionPrototype.PerformanceTests/Performance/PerformanceOptimizationRegressionTests.cs`
- 可选新建：`tests/RoslynDeletionPrototype.Testing/TestCodeSet/Pipeline/RuleGraphSources.cs`

1. 记录每个 rule node 的依赖等待、运行耗时、分配、输入/输出数量、ready queue 峰值和并发度。
2. 用固定 fixture 预热一次、正式运行至少三次，比较旧路径和 DAG 路径的中位数及分配量。
3. 在可完成的真实源码文件上重复该协议，记录输入 hash、SDK、DOP、所有样本和完整等价快照。
4. 没有有效三次样本时，保留 DAG 正确性结论，不宣称性能提升，不改变默认 DOP。

**验收：** 性能数据能区分 CPG 构建、规则图等待、规则执行、冲突收口和 rewrite；收益与规则级并发直接对应。

## 验证

```powershell
$env:DOTNET_CLI_HOME = (Resolve-Path '.').Path
dotnet build .\src\NLISSN\NLISSN.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PipelineComponentTests|FullyQualifiedName~DecisionStructureValidationTests|FullyQualifiedName~MarkRuleEffectTests|FullyQualifiedName~LogicalConditionMarkAnalyzerTests"
dotnet test .\tests\RoslynDeletionPrototype.PerformanceTests\RoslynDeletionPrototype.PerformanceTests.csproj --no-restore -p:UseSharedCompilation=false --filter "FullyQualifiedName~PerformanceOptimizationRegressionTests"
pwsh -File .\scripts\check-harness-consistency.ps1
git diff --check
```

当前 `init.ps1` 与测试项目仍可能保留重命名前路径。若它们未先修复，必须明确记录为验证环境阻塞，不能把构建失败归因于 Rule DAG。

## 完成条件

- 每条默认规则有唯一图节点和显式依赖输入。
- 图编译拒绝未知节点、类型不兼容依赖和环。
- 四类规则不再构成固定执行屏障；只由 DAG 边决定执行时机。
- `GroupKey` 不参与执行调度。
- 全量语义快照在 DOP 1 和 DOP 16 下等价。
- 性能报告包含有效样本，或明确标记为尚未取得可比测量。
