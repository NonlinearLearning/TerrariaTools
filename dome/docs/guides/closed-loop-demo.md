# 闭环示例

本页用 `samples/` 目录演示一个最小闭环：输入源码、运行 Dome、检查产物。

## 适用场景

如果你要快速确认下面这些问题，先跑这个示例：

- CLI 是否能正常工作
- `sourceonly` 加载是否符合预期
- 指令是否会进入计划和重写阶段
- 输出目录里有哪些产物

## 最小示例

`samples/closed-loop/Player.cs` 包含一个最简单的删除指令：

- `Prepare()` 和 `Keep()` 保留
- `// dome:delete` 标记的语句会进入计划和重写

## 运行步骤

假设当前目录是 `dome/`。

1. 执行标准流程：

```bash
dotnet run --project apps/Dome.Cli -- run samples/closed-loop out/closed-loop --loader sourceonly
```

2. 检查输出目录：

- `out/closed-loop/audit-plan.json`
- `out/closed-loop/report.json`
- `out/closed-loop/rewritten/Player.cs`

3. 打开 `rewritten/Player.cs`，确认被标记的语句已经按计划处理。

## 其他样例目录

你还可以用相同方式运行这些目录：

- `samples/expression-loop`
- `samples/promotion-loop`
- `samples/protection-loop`
- `samples/sanitization-loop`

建议做法：

1. 先用 `analyze` 看目标和报告。
2. 再用 `plan` 看决策是否合理。
3. 最后用 `run` 验证重写产物。

## 什么时候不要用样例目录

如果你要验证下面这些行为，不要只看 `samples/`：

- Roslyn 工作区加载
- 项目或解决方案引用
- 运行时工作区准备
- shadow extraction 构建

这时应优先看 `tests/Dome.Tests/Application/Integration` 下的集成测试。
