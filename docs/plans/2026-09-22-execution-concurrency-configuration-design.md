# 执行并发配置设计

**状态：** 已确认设计；本轮实现与短验证完成，完整分层待后续发布验收

## 目标

删除配置字段 `execution.maxDegreeOfParallelism`，不保留旧字段到任何新字段的
fallback 或隐式映射。目录分析不再把总 CPG 并发度通过 `FairCapped` 自动裁成一半；
每个并发边界都从配置文件读取自己的最大并发数。

本设计只改变 NLISSN 的 `execution` 配置和它到运行时调度边界的映射。CPG builder
内部的 `NLCPGBuilderOptions.MaxDegreeOfParallelism`、通用并发池方法参数中的
`maxDegreeOfParallelism`、以及 NLCPG 项目导出的 project worker 配置是局部 API，
不是本次要删除的全局配置。

## 配置契约

`execution` 使用扁平、显式的字段：

```yaml
execution:
  directoryMaxDegreeOfParallelism: 4
  cpgMaxDegreeOfParallelism: 12
  groupMaxDegreeOfParallelism: 4
  helperMaxDegreeOfParallelism: 4
  replayMaxDegreeOfParallelism: 4
  maxConcurrentOperations: 8
```

六个数值字段都是正整数，并且在 Schema 2 中必填。缺少任意一个字段、值为零或
负数时，配置加载器返回该字段自己的诊断；不会读取已经删除的
`execution.maxDegreeOfParallelism` 来补值。旧字段也从 YAML model、JSON Schema、
配置示例和文档中删除，因此带有旧字段的配置会被未知属性校验拒绝，而不是静默
接受。

现有的三个布尔开关继续存在，但只控制是否启用相应的并行阶段：

- `directoryParallelism` 为 `false` 时，目录文件分析走串行路径；目录最大值仍是
  必填配置，但不参与串行调度。
- `groupParallelism` 为 `false` 时，规则 DAG、冲突域以及 Mark/Lift 等规则组执行
  使用 1；为 `true` 时使用 `groupMaxDegreeOfParallelism`。
- `helperParallelism` 为 `false` 时，helper 扫描使用串行路径；为 `true` 时使用
  `helperMaxDegreeOfParallelism`。

## 运行时映射

`RoslynPrototypeExecutionOptions` 删除全局 `MaxDegreeOfParallelism` 和可继承的
CPG override，改为保存六个独立上限及现有开关和取消令牌。每个上限提供自己的
`Effective...` 正值访问器，访问器只负责把程序化调用中的非法值收敛为至少 1；
YAML 配置的合法性仍由配置加载器负责。

各调用点的唯一来源如下：

| 并发边界 | 配置 | 使用位置 |
| --- | --- | --- |
| 目录文件分析 | `directoryMaxDegreeOfParallelism` | `DirectoryAnalysisUseCase` |
| 单文件 CPG builder 和目录内每个 CPG lease | `cpgMaxDegreeOfParallelism` | `ApplicationService`、目录分析准入 |
| 规则 DAG、冲突域、Mark/Lift 等规则组 | `groupMaxDegreeOfParallelism` | `RuleGraphAnalysisExecutor`、Core rule engines |
| ParameterShrink 等 helper 扫描 | `helperMaxDegreeOfParallelism` | `ParameterShrinkAnalyzer` |
| rewrite plan replay | `replayMaxDegreeOfParallelism` | `RewritePlanReplayService` |
| 通用 admission controller 的操作数 | `maxConcurrentOperations` | `AnalysisRuntime` |

`AnalysisRuntime.CreateDefault()` 为程序化调用为每个独立上限建立明确的默认值；
这只是无 YAML 配置的 API 默认构造，不会恢复旧字段语义。`CommandHost` 按配置项
逐项创建 runtime options，不能通过位置参数把一个值传播到其他边界。

## CPG 准入和目录行为

`CpgBuildAdmissionBudget` 只接收 CPG 总预算。删除 `CpgBuildAdmissionPolicy`、
`FairCapped`、policy 参数和租约上的 policy 元数据；每个 lease 最多获得请求的
并发度和剩余总预算中的较小值，默认不再对请求做总预算一半的额外裁剪。

目录分析的两个上限因此独立：目录池最多同时调度
`directoryMaxDegreeOfParallelism` 个文件，当前文件的 builder 最多使用
`cpgMaxDegreeOfParallelism`，所有文件共享同一个 CPG 总预算。目录并行时请求完整
CPG 配置值；当总预算不足时由 budget 正常排队，而不是通过减半制造隐式上限。
单文件路径和目录路径使用同一 CPG 配置值，差异只来自目录池是否同时运行多个文件。

## 错误处理和可观测性

配置诊断使用独立路径和稳定错误码，至少覆盖六个数值字段的缺失、零值和负值。
解析出的 `ExecutionSettings` 和 `resolved-configuration.json` 只包含新字段。
runtime log、performance run identity 和相关遥测必须记录实际使用的命名上限；不再
生成一个代表所有阶段的全局 DOP 快照。现有的 directory/CPG/rule 性能身份字段
可保留，但其值分别来自对应配置；helper、replay 和 admission 的值加入稳定配置
投影或配置指纹，使不同上限的运行不会被误判为同一配置。

遥测写入失败仍然不能改变分析结果。配置校验失败必须在路径解析和运行启动前返回，
不能以运行时的 `Math.Max` 或旧字段回退掩盖配置错误。

## 测试策略

先锁定配置和调度映射的失败测试，再修改生产代码。验收至少包括：

1. 旧字段缺失不会阻止新配置，旧字段出现会被拒绝，六个新字段逐一验证正整数。
2. 目录 DOP、CPG DOP、group、helper、replay、admission 各自只读取对应配置。
3. 总 CPG 为 12 时，目录文件 lease 的 granted degree 可以是 12，不再固定为 6。
4. CPG DOP 1/2/12、目录 DOP 1/2/12 的分析结果保持稳定；串行开关仍有效。
5. rule、rewrite replay、runtime telemetry 和 performance identity 不再依赖全局字段。
6. 旧的 `FairCapped` 合同改为验证无半数裁剪，取消和排队语义保持不变。

验证分层先运行配置 Host 和并发相关 Contract/Performance 定向测试，再运行受影响
项目 build、Fast/Host/Performance tier、harness consistency 和 `git diff --check`。

## 不在本次范围内

- 不删除 CPG WorkBatch 或 project WorkBatch 的局部 `MaxDegreeOfParallelism` API。
- 不修改项目导出 CLI 的 project worker 参数命名；它控制另一层 project pool。
- 不把所有并发合并成一个 admission 上限；`maxConcurrentOperations` 只控制通用
  操作 admission，不能替代目录、CPG、规则、helper 或 replay 上限。
- 不在本次配置迁移中改变规则语义、CPG 图结构、rewrite 结果或项目导出的持久化格式。

## 当前执行状态

本设计已经用于当前工作区的实现。配置模型、Schema 2、示例、运行时映射、CPG
准入、规则组/helper/replay/admission 调用点和相关测试已按本页决策更新。已完成的
短验证包括：配置加载器 `26/26`、CPG/runtime 定向 Host 测试 `7/7`、Schema
Contract `1/1`、Workspace 定向测试 `3/3`，以及 `NLISSN.PerformanceTests` 串行
build `0 warning / 0 error`。更宽的既有定向选择此前也有记录，但本轮只报告实际
重新执行的选择。

本轮短验证已完成：已有 `Miscellaneous/nlissn.yml` 的 CLI smoke 退出码为 `0`，生成的
resolved configuration 含六个新字段；harness consistency 返回 `OK`，静态边界搜索
确认 NLISSN 全局并发字段和 `FairCapped` 无生产路径残留，`git diff --check` 返回 0。
本轮不运行完整
Fast、Host、Performance 分层，也不启动 Terraria、Version4 或其他长时间真实项目
测试；这些属于后续发布验收。
