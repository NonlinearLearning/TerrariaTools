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

## 4. CI 对齐测试集

CI 现在固定为三层验证：

- `build`
- `6` 组定向测试集
- `full-test-and-smoke`

本地排障时优先按下面这 6 组与 CI 对齐执行，再决定是否补跑全量测试。

### 全量测试

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj
```

当前基线结果：

- `153` 个测试通过
- 预览 SDK 提示 `NETSDK1057` 可忽略，不视为阻塞

### Analysis

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~AnalysisGraphTests|FullyQualifiedName~AnalysisQueryServiceTests|FullyQualifiedName~DataFlowAnalysisTests|FullyQualifiedName~FunctionImpactAnalyzerTests|FullyQualifiedName~MemberIdBuilderTests|FullyQualifiedName~ReferenceZeroPredictionAnalyzerTests|FullyQualifiedName~WorkspaceLoadCoordinatorTests'
```

### Application

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~ArtifactPlanBuilderTests|FullyQualifiedName~DomeApplicationTests|FullyQualifiedName~RunReportBuilderTests'
```

### Rules

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~BoundaryPromotionEngineTests|FullyQualifiedName~MarkingRuleEngineTests|FullyQualifiedName~StatementPropagationEngineTests'
```

### Plan

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~AuditPlanCompilerTests|FullyQualifiedName~AuditPlanDedupTests'
```

### Cli

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~DomeCliParserTests'
```

### Rewrite

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --filter 'FullyQualifiedName~RewriteExecutorTests'
```

### Full Test

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj
```

### Smoke 样本

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\closed-loop .\.tmp\ci\closed-loop\plan
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\closed-loop .\.tmp\ci\closed-loop\run
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\expression-loop .\.tmp\ci\expression-loop\plan
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\expression-loop .\.tmp\ci\expression-loop\run
```

## 5. TR 专用运行命令

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- tr-run
```

固定行为：

- 输入解决方案：
  - `D:\lodes\TR\Backup\New1.27\1.45\TR\TerrariaServer.sln`
- 输出根目录：
  - `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome\.tmp\tr-runtime`
- 运行后目录：
  - `dependency-env\`
  - `workspace\`
  - `artifacts\`
- 编译命令：
  - `dotnet build <workspace>\TerrariaServer.sln --no-restore -m`

## 6. 首轮 TR 试跑命令模板

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

## 7. 执行顺序

固定顺序：

1. `dotnet --version`
2. 主 CLI build
3. 全量测试或最小定向测试集
4. `AnalyzeOnly`
5. `PlanOnly`
6. `Standard`

如果第 2 步或第 3 步不稳定，不进入后续试跑。

## 8. 首轮快速落地判定

满足以下条件才允许扩大试跑范围：

- 主 CLI build 稳定通过
- 测试工程通过
- `AnalyzeOnly`、`PlanOnly`、`Standard` 三种模式都能拿到可读诊断
- `report.json`、`audit-plan.json`、`rewritten/**` 与 `GeneratedArtifacts` 一致
