# Dome 最短闭环示例

这份文档只证明一件事：

> `dome` 不仅能识别源码里的标记，还能把被标记的代码真正删掉。

最短闭环固定为 3 步：

1. 在源码里写 directive
2. 看 `audit-plan.json` 确认“计划删什么”
3. 看 `rewritten/*.cs` 确认“实际删了什么”

## 1. 验证样本

仓内固定样本文件：

- [Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\samples\closed-loop\Player.cs)

核心代码如下：

```csharp
public void Update()
{
    Prepare();

    // dome:delete
    int count = 1;

    Keep();
}
```

这里故意只放一条最简单的 statement directive：

- `// dome:delete`
- 紧跟普通语句 `int count = 1;`

这样首轮闭环只验证最纯粹的 statement direct hit：

- `TargetKind.Statement`
- `Action.Kind = Delete`
- `Reason.RuleId = dome:delete`

这里故意不用 `Run();` 这类 invocation statement，因为它可能继续触发：

- `expression-mark`
- `boundary-promotion`

那样会把“statement 是否能删掉”与“method promotion 是否成立”混在一起。

## 2. 实际执行命令

工作目录固定为：

- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome`

先跑 `plan`：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\closed-loop .\.tmp\closed-loop-demo\plan
```

再跑 `run`：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\closed-loop .\.tmp\closed-loop-demo\run
```

本轮实际产物目录：

- [plan](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\plan)
- [run](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\run)

## 3. 看 `audit-plan.json`

先看：

- [audit-plan.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\plan\audit-plan.json)

这一步只关心 4 个字段：

- `Changes[*].Target.TargetKind`
- `Changes[*].Action.Kind`
- `Changes[*].Reason.RuleId`
- `Changes[*].Target.DisplayText`

本轮闭环成功的判断标准是：

- `Changes` 里有 1 条针对 `int count = 1;` 的变更
- `TargetKind = Statement`
- `Action.Kind = Delete`
- `Reason.RuleId = dome:delete`

如果这里已经为空，说明标记没有形成计划，不能进入下一步。

## 4. 看 `report.json`

再看：

- [report.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\run\report.json)

这一步主要核对：

- `IsSuccess`
- `PlannedChanges`
- `GeneratedArtifacts`

本轮闭环成功的判断标准是：

- `IsSuccess = true`
- `PlannedChanges = 1`
- `GeneratedArtifacts` 至少包含：
  - `audit-plan.json`
  - `report.json`
  - `rewritten/Player.cs`

## 5. 看 `rewritten/Player.cs`

最后看：

- [rewritten/Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\closed-loop-demo\run\rewritten\Player.cs)

本轮真正要证明的是：

- `Prepare();` 还在
- `Keep();` 还在
- `int count = 1;` 已经消失

如果 `audit-plan.json` 里有 `Delete`，但 `rewritten/Player.cs` 里 `int count = 1;` 还在，就说明 rewrite 没有正确执行。

## 6. 结论

当以下三件事同时成立时，可以认定闭环成立：

1. `audit-plan.json` 里确实出现了针对 `int count = 1;` 的 `Delete`
2. `report.json` 里 `PlannedChanges = 1`，且产物列表完整
3. `rewritten/Player.cs` 里 `int count = 1;` 真的被删除

这比直接看 `rewritten/*.cs` 更好，因为它能同时回答：

- 标记有没有被识别
- 标记打到了什么 target
- 为什么会删
- 最终是否真的删掉了

## 7. expression-mark 闭环

如果要继续证明“表达式命中投影到 statement”这条链路，再看第二个样本：

- [Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\samples\expression-loop\Player.cs)

核心代码如下：

```csharp
public bool Update(int value)
{
    // dome:delete
    bool allowed = Run(value) && (value > 0);
    return allowed;
}
```

对应命令：

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\expression-loop .\.tmp\expression-loop-demo\plan
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\expression-loop .\.tmp\expression-loop-demo\run
```

实际产物目录：

- [plan](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\plan)
- [run](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\run)

看 [audit-plan.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\plan\audit-plan.json) 时，这组样本要重点确认两条 change：

1. 第一条是投影结果
   - `TargetKind = Statement`
   - `DisplayText = bool allowed = Run(value) && (value > 0);`
   - `Reason.RuleId = expression-mark`
2. 第二条是传播结果
   - `DisplayText = return allowed;`
   - `Reason.RuleId = dataflow-propagation`

看 [report.json](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\run\report.json) 时，本轮结果是：

- `IsSuccess = true`
- `PlannedChanges = 2`
- `GeneratedArtifacts` 包含：
  - `audit-plan.json`
  - `report.json`
  - `rewritten\Player.cs`

看 [rewritten/Player.cs](D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\expression-loop-demo\run\rewritten\Player.cs) 时，可以直接看到：

- `bool allowed = Run(value) && (value > 0);` 已消失
- `return allowed;` 也已消失
- `Update(int value)` 被保留为空方法体

这组样本证明的是另一条链路：

- directive 先触发表达式命中
- 规则把命中投影成 statement delete
- 后续数据流传播继续删掉依赖它的 `return allowed;`

所以现在仓里已经有两条完整闭环：

1. `closed-loop`
   - 证明纯 `dome:delete` statement direct hit 会真的删代码
2. `expression-loop`
   - 证明 `expression-mark` 和后续 `dataflow-propagation` 也会真的落到 rewrite 结果上
