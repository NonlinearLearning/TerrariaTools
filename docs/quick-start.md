# 快速开始

## 这篇解决什么问题

这篇只回答一件事：如何最快把这个仓库跑起来，并确认当前最小主线是正常的。

这篇不负责解释完整架构，也不负责讲设计细节。

## 你会学到什么

1. 仓库当前有哪两个活跃主线项目
2. 应该先跑哪个健康检查
3. 如何运行最小 CPG 原型
4. 如何运行删除规则原型
5. 如何运行测试

## 前置条件

1. 已安装与仓库 `global.json` 对应的 .NET SDK
2. 能在 PowerShell 中执行 `dotnet`
3. 在仓库根目录下工作

建议先执行：

```powershell
pwsh -File .\init.ps1
```

这一步会做三件事：

1. 设置 `DOTNET_CLI_HOME`
2. 探测当前活跃项目
3. 执行最小构建健康检查

## 1. 先确认仓库能正常初始化

```powershell
pwsh -File .\init.ps1
```

如果这一步通过，说明：

1. 仓库根路径有效
2. 当前活跃项目可被探测到
3. 最小构建路径至少能走到检查阶段

## 2. 运行最小 CPG 原型

先构建：

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

再运行：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

再跑样例：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj .\src\MinimalRoslynCpg\samples\analysis-sample.cs
```

## 3. 运行删除规则原型

先构建：

```powershell
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj
```

再运行最小样例：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s
```

这个样例会输出：

1. `SeedMarks`
2. `PropagatedMarks`
3. `Decisions`
4. `Edits`
5. `RewrittenSource`

## 4. 跑测试

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj
```

## 5. 如何判断是否“跑对了”

### 最小 CPG 原型

重点看：

1. 是否能构建成功
2. 是否能输出样例结果
3. 是否能继续推进 CFG / DataFlow / 调用关系

### 删除规则原型

重点看：

1. 是否能产出 seed marks
2. 是否能产出 propagated marks
3. 是否能生成 decisions
4. 是否能生成 rewrite edits
5. 是否能得到 rewritten source

### 测试

重点看：

1. 是否全部通过
2. 是否覆盖当前最小样例主线
3. 是否能作为后续改动的回归基线

## 6. 如果失败，先看什么

1. `progress.md`
2. `feature_list.json`
3. `AGENTS.md`
4. `init.ps1`

## 7. 下一步

1. 想继续改代码，去看 `developer-guide.md`
2. 想先看项目入口，回到根目录 `README.md`
