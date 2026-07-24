# 快速开始

## 这篇解决什么问题

本页给出在仓库根目录完成最小健康检查、运行最小 CPG 和运行删除规则宿主的路径。它不讲规则设计和完整命令语义。

## 前置条件

- 使用 `global.json` 固定的 .NET SDK。
- 在 PowerShell 中从仓库根目录运行命令。

## 1. 初始化并检查环境

```powershell
pwsh -File .\init.ps1
```

成功时会报告仓库根、SDK 版本，并通过 `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj` 的构建健康检查。

## 2. 运行最小 CPG

先查看支持的 CLI 选项：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj -- --help
```

再构建仓库自带样例：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj -- .\src\MinimalRoslynCpg\samples\analysis-sample.cs
```

成功时标准输出包含 `Nodes:` 和 `Edges:`，后续行按节点类型列出统计值。

CPG 的 shard 持久化是构建器 API 配置，不是当前 CLI 参数；存储布局、恢复和查询限制见[开发者指南](developer-guide.md#streaming-cpg-shard-store)。

## 3. 运行删除规则宿主

不传输入路径时，宿主使用内置 demo：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj
```

对真实输入使用：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- <input-path> --target-name <name> --no-diff
```

`<input-path>` 是一个 `.cs` 文件或目录。先保留 `--no-diff` 或默认的非写回行为；只有确认 diff 后才加入 `--write-back`。

需要诊断目录并发与文件内 CPG 分片并发时，使用 `--cpg-max-degree-of-parallelism` 单独覆盖 CPG 值；三组测量命令与继承规则见 [CLI 参考](cli-reference.md#并发诊断)。

## 4. 运行回归测试

```powershell
pwsh -File .\scripts\Run-TestTiers.ps1 -Fast
```

测试输出以通过/失败计数结束。更窄的验证选择见 [Harness 验证矩阵](harness-verification-matrix.md)。

## 下一步

- 想理解结果：看 [核心概念](concepts.md)。
- 想使用删除规则选项：看 [CLI 参考](cli-reference.md)。
- 想开始修改：看 [开发者指南](developer-guide.md)。
