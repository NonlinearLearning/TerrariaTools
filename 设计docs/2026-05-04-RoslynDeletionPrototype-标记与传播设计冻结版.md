# RoslynDeletionPrototype 标记与传播设计冻结版

## 1. 冻结结论

当前版本固定为：

1. 标记层设计为 `MarkingEngine`
2. 传播层设计为 `PropagationEngine`
3. `IDeletionRule` 直接继承 `IRuleMarker` 和 `IRulePropagator`
4. 规则模型实现引擎定义的 API，形成独特行为
5. 引擎负责统一调度，不把全局流程控制交给规则
6. `RuleHitNode` 保持纯绑定对象，不回填规则解释信息

## 2. 核心原则

### 2.1 分工

- 标记引擎负责 direct marking
- 传播引擎负责 propagated marking
- 规则模型实现标记和传播接口
- `GraphAnalyzer` 只做阶段编排
- 决策层只消费标记结果
- rewrite 层只消费决策结果

### 2.2 不做的事

当前版本不做：

1. 不把 `RuleId`、`Reason` 放回 `RuleHitNode`
2. 不让规则控制全局传播循环
3. 不引入更复杂的传播分类模型
4. 不做外部配置化加载
5. 不做跨文件传播

## 3. 阶段主线

```text
规则 direct marking
  -> SeedMarks
  -> propagation
  -> PropagatedMarks
  -> 合并 EffectiveMarks
  -> decision
  -> rewrite
```

## 4. 数据模型

### 4.1 RuleHitNode

`RuleHitNode` 是纯绑定对象。

```csharp
public sealed record RuleHitNode(
  SyntaxNode SyntaxNode,
  SyntaxAnnotation? Annotation,
  RoslynCpgNode? PrimaryGraphNode);
```

职责：

1. 表达命中的语法节点
2. 表达 rewrite 跟踪注解
3. 表达绑定到的主图节点

不负责：

1. 规则来源
2. 命中原因
3. 传播深度
4. 传播来源

### 4.2 MarkRecord

`MarkRecord` 表达 direct mark。

```csharp
public sealed record MarkRecord(
  string RuleId,
  RuleHitNode HitNode,
  string Reason);
```

职责：

1. 表达哪条规则直接命中了该节点
2. 保留最小解释文本
3. 作为传播输入

### 4.3 PropagatedMarkRecord

`PropagatedMarkRecord` 表达 propagation 产生的新标记。

```csharp
public sealed record PropagatedMarkRecord(
  string RuleId,
  RuleHitNode HitNode,
  RuleHitNode SourceHitNode,
  int Depth);
```

职责：

1. 表达传播后得到的新节点
2. 表达它来自哪个旧节点
3. 表达传播深度

### 4.4 PrototypeAnalysisResult

`PrototypeAnalysisResult` 是阶段结果容器。

```csharp
public sealed record PrototypeAnalysisResult(
  IReadOnlyList<MarkRecord> SeedMarks,
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  IReadOnlyList<RuleDecision> Decisions,
  IReadOnlyList<RewriteEdit> Edits,
  string RewrittenSource);
```

## 5. 接口模型

### 5.1 IRuleMarker

```csharp
public interface IRuleMarker
{
  IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}
```

职责：

1. 定义 direct marking 协议
2. 由规则模型实现具体 marking 行为

### 5.2 IRulePropagator

```csharp
public interface IRulePropagator
{
  IEnumerable<PropagatedMarkRecord> Propagate(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks);
}
```

职责：

1. 定义 propagation 协议
2. 由规则模型实现具体 propagation 行为

### 5.3 IDeletionRule

冻结选择：`IDeletionRule` 直接继承两个接口。

```csharp
public interface IDeletionRule : IRuleMarker, IRulePropagator
{
  RuleMetadata Metadata { get; }

  IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; }
}
```

## 6. 引擎职责

### 6.1 MarkingEngine

`MarkingEngine` 负责：

1. 加载规则
2. 调用规则的 `Mark(...)`
3. 校验命中节点 kind
4. 补 `Annotation`
5. 绑定 `PrimaryGraphNode`
6. 产出 `SeedMarks`

### 6.2 PropagationEngine

`PropagationEngine` 负责：

1. 加载规则
2. 读取某条规则自己的 seed marks
3. 调用规则的 `Propagate(...)`
4. 校验传播结果
5. 去重
6. 产出 `PropagatedMarks`

### 6.3 RuleDecisionEngine

`RuleDecisionEngine` 负责：

1. 消费 `SeedMarks + PropagatedMarks`
2. 生成 `RuleDecision`

### 6.4 PrototypeRewriter

`PrototypeRewriter` 负责：

1. 消费 `RuleDecision`
2. 生成 rewrite 结果

## 7. GraphAnalyzer 职责

`GraphAnalyzer` 只做编排：

```text
MarkingEngine
  -> SeedMarks
PropagationEngine
  -> PropagatedMarks
RuleDecisionEngine
  -> Decisions
PrototypeRewriter
  -> RewrittenSource / Edits
```

也就是：

1. 构图
2. 准备 `RuleContext`
3. 跑 marking
4. 跑 propagation
5. 跑 decision
6. 跑 rewrite
7. 组装 `PrototypeAnalysisResult`

## 8. 当前约束

当前版本固定这些约束：

1. `RuleHitNode` 保持纯绑定对象
2. direct mark 与 propagated mark 分层保存
3. `RuleId` 和 `Reason` 不回到 `RuleHitNode`
4. 规则实现局部行为
5. 引擎掌控全局流程
6. 先不引入更复杂传播分类

## 9. 实施顺序

后续实现按这个顺序：

1. 新增 `MarkRecord`
2. 新增 `PropagatedMarkRecord`
3. 修改 `PrototypeAnalysisResult`
4. 新增 `IRuleMarker`
5. 新增 `IRulePropagator`
6. 让 `IDeletionRule` 继承这两个接口
7. 让现有规则实现 `Mark(...)` 和 `Propagate(...)`
8. 新增 `MarkingEngine`
9. 新增 `PropagationEngine`
10. 改 `GraphAnalyzer` 为阶段编排
11. 更新测试与 CLI 输出

## 10. 引擎 API 形式

当前版本继续固定引擎 API 形式，避免实现前再次漂移。

### 10.1 两层接口

接口分成两层：

1. 引擎对外接口
2. 规则行为接口

原则：

- 引擎接口负责流程调度
- 规则接口负责局部行为

### 10.2 引擎对外接口

标记引擎对外暴露：

```csharp
public interface IMarkingEngine
{
  IReadOnlyList<MarkRecord> Run(
    RuleContext context,
    SyntaxNode root,
    IReadOnlyList<IDeletionRule> rules);
}
```

传播引擎对外暴露：

```csharp
public interface IPropagationEngine
{
  IReadOnlyList<PropagatedMarkRecord> Run(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<IDeletionRule> rules);
}
```

职责：

1. 对上层只暴露统一入口
2. 引擎内部负责遍历规则
3. 引擎内部负责分组、去重、停止条件

### 10.3 规则行为接口

规则实现 direct marking 行为：

```csharp
public interface IRuleMarker
{
  IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root);
}
```

规则实现 propagation 行为：

```csharp
public interface IRulePropagator
{
  IEnumerable<PropagatedMarkRecord> Propagate(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks);
}
```

冻结选择：

```csharp
public interface IDeletionRule : IRuleMarker, IRulePropagator
{
  RuleMetadata Metadata { get; }

  IReadOnlyList<SyntaxKind> AllowedNodeKinds { get; }
}
```

### 10.4 固定的调用关系

当前固定调用关系为：

```text
GraphAnalyzer
  -> IMarkingEngine.Run(...)
  -> IPropagationEngine.Run(...)

IMarkingEngine
  -> rule.Mark(...)

IPropagationEngine
  -> rule.Propagate(...)
```

### 10.5 当前不采用的形式

当前不采用：

1. 由 `GraphAnalyzer` 自己逐条规则循环，再单条调用引擎
2. 由规则自己控制全局传播轮次
3. 由引擎直接写死每条规则的特殊分支

原因：

1. 会把流程责任重新打散到 `GraphAnalyzer`
2. 会让规则重新掌控全局流程
3. 会让引擎变成硬编码分支集合

## 11. 决策层设计讨论结论

这一节固定最近关于决策层职责、布尔表达式规约和停止条件的讨论结果。

### 11.1 决策层的职责边界

当前固定为：

1. 标记层回答“直接命中了谁”
2. 传播层回答“还能扩到哪些候选宿主”
3. 决策层回答“当前候选是否已经能收敛成最终 rewrite 动作”

决策层不应：

1. 反向大量读取规则模型的私有逻辑
2. 自己掌控无限传播循环
3. 重新承担标记层职责

决策层更适合处理：

1. 最终删除单位选择
2. 是否升级到更大父结构
3. direct mark 与 propagated mark 的合并
4. 逻辑表达式的规约判断

### 11.2 关于 `&&` 和 `||` 的层次划分

对如下情形：

```csharp
if (x && i)
if (x || i)
```

如果 `i` 被标记，当前固定理解为：

1. 传播层最多只把候选提升到外层逻辑二元表达式
2. 传播层不负责解释 `&&` / `||` 的语义差异
3. `&&` / `||` 的不同处理放在决策层或后续 rewrite 策略中

也就是：

- 传播层负责找到相关宿主
- 决策层负责判断当前表达式如何规约

### 11.3 关于布尔表达式规约的结论

换个角度后，当前把这类问题视为“布尔表达式归约”问题，而不是单纯传播问题。

对逻辑表达式候选，决策层至少需要考虑：

1. 删除某个操作数后，当前表达式是否仍合法
2. 当前表达式是否能规约成剩余一侧
3. 当前表达式是否必须整体失效
4. 当前表达式若不能收敛，是否需要继续升级到更大父结构

当前固定结论：

1. 不把 `&&` / `||` 的语义规约放进通用传播层
2. 传播层只提供候选宿主
3. 规约行为放在决策层一侧讨论和收口

### 11.4 决策与传播的交互方式

当前固定为“分阶段迭代”，而不是单次线性流水线。

即：

```text
mark
  -> propagate candidate
  -> decide reduction
  -> if unresolved, escalate candidate
  -> decide again
```

这意味着：

1. 决策层结果可能要求继续升级候选
2. 继续升级不是由决策层自己无限递归完成
3. 编排层负责控制是否进入下一轮

### 11.5 传播层停止条件

当前固定最小停止条件：

1. 当前轮没有新增 `PropagatedMarkRecord`
2. 达到最大传播深度
3. 新候选去重后为 0
4. 当前节点类型不允许继续传播
5. 当前规则声明禁止继续传播
6. 已到达终止节点

传播层停止，本质上表示：

- 结构上已经找不到更大的有效候选宿主

### 11.6 决策层停止条件

当前固定最小停止条件：

1. 已得到稳定的最终删除单位
2. 已得到稳定的局部规约结果
3. 当前候选不需要继续升级
4. 当前动作已经可以直接交给 rewrite
5. 当前候选被判定为无效并丢弃

决策层停止，本质上表示：

- 语义上已经能够收敛成最终 rewrite 动作

### 11.7 编排层的总停止条件

整个分析流程最终停在以下任一条件满足时：

1. 决策层产出最终结果
2. 传播层无法再产生新候选
3. 达到全局最大轮次
4. 已命中终止节点
5. 当前候选被判定为不可处理并丢弃

### 11.8 当前不再继续扩大的点

当前先不在文档中固定更重的结构，例如：

1. `PropagationKind`
2. `DecisionHint`
3. `DecisionInputRecord`
4. `Final / Continue / Drop` 三态结果模型

这些方向已进入关注范围，但本版冻结稿先只固定职责边界和停止条件，不继续做类型扩张。

## 12. 应用层固定流

这一节固定当前 `RoslynDeletionPrototype` 的最小应用层编排流。

### 12.1 当前落地版本

当前版本固定为 5 个主阶段：

```text
分析
  -> 标记
  -> 传播
  -> 决策
  -> 改写
```

当前采用的设计方式是：

1. 先用 `AppService` 直接编排全部阶段
2. 先不引入独立 `UseCase`
3. 先不引入统一 pipeline 框架
4. 先用已有 `MarkingEngine / PropagationEngine / RuleDecisionEngine / PrototypeRewriter` 固定主链

### 12.2 各阶段职责

#### 分析

当前 `分析` 阶段统一吸收原本更细的前置准备步骤：

1. 基于 `source + filePath` 构建 `RoslynCpgGraph`
2. 构建 `SyntaxTree`
3. 取得 `SyntaxNode root`
4. 构建 `Compilation`
5. 取得 `SemanticModel`
6. 组装 `RuleContext`

固定结论：

1. 当前源码已经直接传入分析过程
2. 当前不单独拆 `LoadInput` 或 `LoadWorkspace` 阶段
3. 当前把所有运行前技术准备统一视为 `分析`

#### 标记

`标记` 阶段负责：

1. 调 `MarkingEngine`
2. 执行各规则的 `Mark(...)`
3. 产出 `SeedMarks`

它回答的问题是：

- 哪些节点被规则直接命中

#### 传播

`传播` 阶段负责：

1. 调 `PropagationEngine`
2. 执行各规则的 `Propagate(...)`
3. 产出 `PropagatedMarks`

它回答的问题是：

- direct mark 还能扩成哪些更大的候选宿主

#### 决策

`决策` 阶段负责：

1. 调 `RuleDecisionEngine`
2. 合并 `SeedMarks + PropagatedMarks`
3. 选择最终删除单位
4. 过滤被更大删除动作覆盖的 nested delete decisions

它回答的问题是：

- 最终删谁

#### 改写

`改写` 阶段负责：

1. 调 `PrototypeRewriter.Rewrite(...)`
2. 调 `PrototypeRewriter.ToEdits(...)`
3. 基于最终 `RuleDecision` 回写源码

它回答的问题是：

- 最终源码如何变化

### 12.3 当前明确不做的阶段

这些阶段保留为可选设计，当前版本只提示，不落实现：

1. `LoadInput`
2. `LoadWorkspace`
3. `Plan`
4. `Evidence`
5. `Report`
6. `MultiRoundDecision`
7. `ExecutionAudit`
8. 独立 `Result` 阶段
9. 独立 `Output` 阶段

当前不做这些阶段的原因：

1. 原型当前已经直接接收源码字符串，不需要先做工作区级加载壳
2. 当前重点是固定 `mark -> propagate -> decide -> rewrite` 主链
3. 当前还没有稳定的计划产物、证据产物和报告产物模型
4. 当前不希望为了未来产品化流程提前引入空壳阶段

### 12.4 当前应用层编排边界

当前固定边界为：

1. `Program` 只保留 CLI 入口
2. `DeletionApplicationService` 直接编排 `分析 -> 标记 -> 传播 -> 决策 -> 改写`
3. 规则只实现局部 `Mark(...) / Propagate(...)`
4. rewrite 只消费 `RuleDecision`

当前明确不采用：

1. 由规则自己控制全局流程
2. 由 rewrite 回头补规则推理
3. 由 CLI 入口直接拼接阶段细节
4. 先为未来扩展引入完整 `UseCase + Pipeline + Report` 体系

## 13. 参考历史与演进路线

这一节固定当前应用层固定流的参考来源，防止后续再次回到无证据争论。

### 13.1 TerrariaTools `dome` 的参考点

`TerrariaTools/docs/plans/2026-03-12-dome-architecture-design.md` 给出的 v1 固定流是：

```text
Analysis -> Mark -> Plan -> Rewrite -> Report
```

这里提供了两个关键参考：

1. 应用层应以固定阶段流组织主链
2. `Rewrite` 应只消费前序阶段结果，不回头重跑规则逻辑

同时，`dome` 文档也明确指出旧问题：

1. `Program.cs` 仍是 demo-menu 入口
2. `RuleEngine` 曾把 analysis setup、propagation、decision、rewrite annotation 混在一起

当前 `RoslynDeletionPrototype` 采用这个参考时做了缩减：

1. 保留 `Analysis / Mark / Rewrite` 这条主线思路
2. 暂时不引入 `Plan / Report`
3. 用 `传播 / 决策` 补齐删除原型当前最需要的中间阶段

### 13.2 TerrariaTools `Isolation` 的参考点

`Isolation` 当前给出的稳定应用层主线是：

1. `AppService` 保持薄入口
2. `UseCase` 保持主编排壳
3. 传播、决策、工件装配继续下沉到 `Logic.Workflow`

关键代码与文档证据：

1. `TerrariaTools/Isolation/docs/DDD/07-应用层设计.md`
2. `TerrariaTools/Isolation/src/Application/Services/RewriteWorkflowAppService.cs`
3. `TerrariaTools/Isolation/src/Application/Services/RewriteWorkflow/RewriteWorkflowUseCase.cs`

当前 `RewriteWorkflowUseCase.RunAsync(...)` 的现行顺序是：

```text
PrepareRunContext
  -> ExecutePropagation
  -> ExecuteDecision
  -> AssembleArtifacts
```

它给当前原型的主要启发是：

1. 应用层可以固定阶段顺序
2. 阶段逻辑应下沉到独立 stage / engine
3. 主编排对象应只串联阶段，不把规则细节重新塞回入口层

### 13.3 当前原型为什么先不直接照抄 `Isolation`

当前版本没有直接采用 `薄 AppService + UseCase`，而是先落 `AppService` 直接编排，原因固定为：

1. 当前项目仍是最小删除原型，规模远小于 `Isolation`
2. 当前要先把阶段名词和实际代码链固定下来
3. 当前先减少对象数量，避免在阶段还未稳定前提前引入 `UseCase`

### 13.4 当前原型的演进路线

当前演进路线固定为三步：

#### 第一步：Roslyn-first 最小主线

参考：`2026-05-02-删除规则-Roslyn-first-一般重设计.md`

这一步先把删除原型压成：

```text
命中 / 扩展 / 裁决 / 重写
```

重点是：

1. 优先直接依赖 Roslyn facts
2. 只保留少量结果对象
3. 先跑通基础功能

#### 第二步：标记与传播冻结

参考：本文当前冻结稿

这一步进一步固定：

1. `MarkingEngine`
2. `PropagationEngine`
3. `IRuleMarker`
4. `IRulePropagator`
5. `RuleHitNode / MarkRecord / PropagatedMarkRecord`

重点是：

1. direct mark 与 propagated mark 分层保存
2. 规则只实现局部行为
3. 编排层掌控全局流程

#### 第三步：应用层固定五阶段流

本次在第二步基础上再向前收口，把当前真实执行主线固定为：

```text
分析
  -> 标记
  -> 传播
  -> 决策
  -> 改写
```

这一步的目标是：

1. 用最少阶段覆盖当前真实实现
2. 保留未来 `Plan / Evidence / Report` 的可选扩展空间
3. 避免当前文档继续在“细步骤”与“空壳阶段”之间漂移

## 14. 后续设计关注点

这一节不是当前立即实现项，而是后续持续审查清单。

### 14.1 规则身份

后续要持续考虑：

1. `RuleId` 是否只用于调试
2. 多条规则命中同一节点时谁优先
3. 是否需要显式规则优先级
4. 是否需要规则分组

### 14.2 标记语义

后续要明确：

1. mark 表达的是危险点、候选删除点，还是传播观察点
2. `SeedMarks` 和 `PropagatedMarks` 是否语义等价
3. propagated mark 是否能直接进入 decision

### 14.3 传播边界

后续要持续限制：

1. 传播是单跳还是多跳
2. propagated mark 能否继续传播
3. 是否限制在同一方法内
4. 是否允许跨声明、跨类型、跨文件

### 14.4 去重策略

后续要明确：

1. 如何判定两个命中是同一节点
2. direct mark 和 propagated mark 冲突时谁优先
3. 多条传播链打到同一目标时如何合并

### 14.5 引擎与规则边界

后续要持续检查：

1. 去重是在规则里做还是引擎里做
2. 深度控制是在规则里做还是引擎里做
3. 异常由规则抛出还是由引擎包装
4. 规则是否开始偷偷接管全局流程

### 14.6 RuleHitNode 稳定性

后续要防止：

1. 把 `RuleId` 放回 `RuleHitNode`
2. 把 `Reason` 放回 `RuleHitNode`
3. 把 `Depth`、来源、传播分类重新塞进绑定对象

### 14.7 Roslyn 与图绑定关系

后续要继续观察：

1. `SyntaxNode` 是否始终是主锚点
2. `PrimaryGraphNode` 是否只是辅助定位
3. `SyntaxAnnotation` 是否真正进入 rewrite 跟踪主线
4. 当语法树与图绑定不稳定时谁是主真相

### 14.8 CPG 参与深度

后续要提前想清楚：

1. 传播是沿语法树还是沿图边
2. 两者是否混用
3. 图节点是否只用于定位，还是参与传播计算

### 14.9 结果对象用途

后续要考虑 `PrototypeAnalysisResult` 是否同时服务：

1. CLI 输出
2. 测试断言
3. 调试分析
4. 以后可能的可视化或工具集成

### 14.10 测试策略

后续测试不应只保留端到端，还要逐步覆盖：

1. 标记层单测
2. 传播层单测
3. 去重策略单测
4. direct 与 propagated 冲突单测
5. 绑定失败单测

### 14.11 性能与规模

后续要考虑：

1. 是否需要为 `PrimaryGraphNode` 绑定预建索引
2. 传播是否会重复扫描整棵树
3. 去重是否需要专门索引结构

### 14.12 术语稳定性

后续要保持这些概念不混用：

1. hit
2. mark
3. propagated mark
4. decision
5. rewrite target
6. propagation
