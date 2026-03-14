# Dome 首轮快速落地执行说明

这份文档只解决一件事：在当前本机环境下，用固定工作目录、固定 SDK、固定命令执行 `dome` 的首轮构建、测试和小范围试跑。

## 1. 固定执行入口

固定工作目录：

- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome`

固定 SDK 选择来源：

- [global.json](/D:/ProjectItem/SourceCode/Net/TerrariaTools/.worktrees/global.json)

固定 SDK 版本：

- `10.0.200-preview.0.26103.119`

执行前先确认：

```powershell
dotnet --version
```

返回结果必须是：

- `10.0.200-preview.0.26103.119`

## 2. 统一环境变量

在所有命令前固定：

```powershell
$env:DOTNET_CLI_HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
```

执行要求固定为：

- 从 `dome` worktree 根目录执行
- 使用 `-NoProfile` 或等价的无 profile PowerShell 会话
- 读取文件时统一 `Get-Content -Encoding utf8`

## 3. 主工程构建命令

```powershell
dotnet build .\src\Cli\Dome.Cli.csproj -nologo
```

这是快速落地阶段的主构建入口。当前 `net10.0` + `.NET 10 preview` 组合下，这条命令已稳定通过。

## 4. 最小定向测试集

首轮快速落地可以直接跑全量测试；若需要缩小排障面，优先使用下面的定向测试集。

### 全量测试

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj
```

当前基线结果：

- `150` 个测试通过
- 预览 SDK 提示 `NETSDK1057` 可忽略，不视为阻塞

### Analysis

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~AnalysisQueryServiceTests'
```

### Rules

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~MarkingRuleEngineTests'
```

### Application

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~DomeApplicationTests'
```

### Rewrite

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~RewriteExecutorTests'
```

## 5. 首轮 TR 试跑命令模板

以 `ItemDropRules` 低风险样本为例：

### AnalyzeOnly

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- analyze `
  'D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules' `
  '.\.tmp\tr-rollout\itemdrop\analyze'
```

### PlanOnly

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan `
  'D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules' `
  '.\.tmp\tr-rollout\itemdrop\plan'
```

### Standard

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run `
  'D:\lodes\TR\Backup\New1.27\TR\Terraria.GameContent.ItemDropRules' `
  '.\.tmp\tr-rollout\itemdrop\run'
```

## 6. 执行顺序

固定顺序：

1. `dotnet --version`
2. 主 CLI build
3. 全量测试或最小定向测试集
4. `AnalyzeOnly`
5. `PlanOnly`
6. `Standard`

如果第 2 步或第 3 步不稳定，不进入后续试跑。

## 7. 首轮快速落地判定

满足以下条件才允许扩大试跑范围：

- 主 CLI build 稳定通过
- 测试工程通过
- `AnalyzeOnly`、`PlanOnly`、`Standard` 三种模式都能拿到可读诊断
- `report.json`、`audit-plan.json`、`rewritten/**` 与 `GeneratedArtifacts` 一致
