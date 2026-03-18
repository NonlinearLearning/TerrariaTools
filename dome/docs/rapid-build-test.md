# Dome 快速构建与验证

这份文档只保留当前仍有效的本地执行入口。

## 1. 工作目录

固定工作目录：

- `D:\ProjectItem\SourceCode\Net\TerrariaTools\.worktrees\dda\dome`

## 2. 建议环境变量

```powershell
$env:DOTNET_CLI_HOME='D:\ProjectItem\SourceCode\Net\TerrariaTools\.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
```

## 3. restore / build / test

### Restore

```powershell
dotnet restore .\tests\Dome.Tests\Dome.Tests.csproj
```

### Build

```powershell
dotnet build .\tests\Dome.Tests\Dome.Tests.csproj --no-restore -v minimal /p:MSBuildEnableWorkloadResolver=false -m:1 -p:UseSharedCompilation=false
```

### Full Test

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --no-restore /p:MSBuildEnableWorkloadResolver=false -m:1 -p:UseSharedCompilation=false
```

## 4. 常用 focused 组合

### 边界与主链路

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --no-restore --filter "FullyQualifiedName~PublicContractBoundaryTests|FullyQualifiedName~DomeApplicationTests|FullyQualifiedName~DomeApplicationSemanticTests"
```

### Analysis

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalysisNativePathTests|FullyQualifiedName~AnalysisGraphTests|FullyQualifiedName~AnalysisQueryServiceTests"
```

### Rules

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --no-restore --filter "FullyQualifiedName~MarkingRuleEngineBuildDecisionsTests|FullyQualifiedName~MarkingRuleEngineUnitTests"
```

### Planning / Rewrite

```powershell
dotnet test .\tests\Dome.Tests\Dome.Tests.csproj --no-restore --filter "FullyQualifiedName~AuditPlanCompilerTests|FullyQualifiedName~AuditPlanDedupTests|FullyQualifiedName~RewriteExecutorTests"
```

## 5. CLI smoke

```powershell
dotnet run --project .\src\Cli\Dome.Cli.csproj -- analyze .\samples\closed-loop .\.tmp\closed-loop-demo\analyze
dotnet run --project .\src\Cli\Dome.Cli.csproj -- plan .\samples\closed-loop .\.tmp\closed-loop-demo\plan
dotnet run --project .\src\Cli\Dome.Cli.csproj -- run .\samples\closed-loop .\.tmp\closed-loop-demo\run
```

## 6. 当前验证目标

一次完整本地验证至少包括：

1. `restore`
2. `build`
3. `test`
4. 至少一个 sample 的 `analyze + plan + run`
