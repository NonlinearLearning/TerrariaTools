# 独立日志模块设计

## 目标

将通用文本日志基础设施从 `src/Host/Logging/` 提取为可单独引用的
`src/Logging/Logging.csproj`，同时保持删除规则 Host 的 CLI 参数、日志
记录顺序和一行文本格式不变。

## 已确认边界

- `Logging.csproj` 目标 `net10.0`，不引用仓库内其他项目。
- 新模块使用 `RoslynPrototype.Logging` 命名空间，并提供公开 API。
- Host 保留业务适配器：它们把 `PrototypeAnalysisResult`、CPG telemetry、mark
  telemetry 和 rewrite/diff 生命周期转换为通用日志事件。
- runtime 与 analysis 继续是两个独立 sink；diff 仍是独立输出契约，只记录其
  生命周期事件，不把 diff 生成移动进日志模块。

## 模块内容

`src/Logging/` 包含以下无业务依赖类型：

- `ITextLogSink` 与 `TextLogFileSink`：有界通道、顺序批量写入、flush、失败传播和
  I/O telemetry。
- `TextLogEvent`、`TextLogField`、`RunLogContext` 与 level/category/event/view 枚举：
  稳定的结构化事件模型。
- `TextLogFilter`：profile、level、category、event 过滤与现有 CLI option 映射。
- `TextLogFormatter`：当前 `key=value` 单行渲染、转义和视图裁剪规则。

`src/Host/Logging/` 仅保留 `RunTextLogWriter` 与
`AnalysisTextLogWriter`。它们依赖 Host 的领域对象，因此继续属于 Host，但改为引用
新模块公开 API。

## 依赖方向

```text
Host -> Logging
Host -> Application / Rules / RoslynPrototype.Core
Logging -> (no project references)
HostTests -> Host + Logging
```

这样日志底座可以被其他可执行项目引用，而不需要引入 Roslyn、规则或改写实现。

## 兼容性与验证

- `--runtime-log`、`--analysis-log`、历史 JSONL 路径别名，以及现有 profile/filter
  选项保持行为不变。
- 先将 Host 日志测试改为直接引用新命名空间，并验证它在模块尚未建立时失败；再创建
  项目并迁移底座代码。
- 测试覆盖新程序集归属、独立构建、Host 日志端到端输出，以及 Logging 无项目依赖。
- 验证顺序为 Logging build、HostTests 的 `TextLogSystemTests`、Host build、架构契约和
  harness 一致性检查。
