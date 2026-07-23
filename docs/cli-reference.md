# 删除规则 CLI 参考

## 适用范围

本页说明 `src/RoslynPrototype/RoslynPrototype.csproj` 的当前宿主行为。该 CLI 是研究原型；规则覆盖范围以测试和设计文档为准。

## 调用形式

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- <input-path> [options]
```

`<input-path>` 可以是单个 `.cs` 文件或包含 C# 文件的目录。省略输入路径时，宿主分析内置 demo 源码。

## 常用路径

### 运行内置 demo

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj
```

该命令不传输入路径，宿主会分析内置 demo 源码。默认不写回任何文件。

### 分析你的目录中的目标类

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj -- <source-directory> --delete-class <class-name> --no-diff
```

先在副本或不带 `--write-back` 的路径执行。`--write-back` 会将改写后的内容写回源文件。

## 选项

| 选项 | 作用 |
| --- | --- |
| `--target-name <name>` | 将规则分析的目标名传给原型流水线。 |
| `--delete-class <name>` | 启用以类名为目标的删除路径。 |
| `--write-back` | 将有编辑结果的输入文件或目录文件写回。 |
| `--no-diff` / `--skip-diff` | 禁止写出 diff。 |
| `--diff-view legacy|readable` | 选择 diff 文本视图；非 `readable` 值按 `legacy` 处理。 |
| `--diff-out <path>` | 设置 diff 输出路径或目录。 |
| `--max-degree-of-parallelism <N>` | 限制目录、规则阶段和 CPG 分片的并发量；缺省或无效值使用处理器数量，最小值为 1。 |
| `--runtime-log <path>` | 写入运行汇总日志。 |
| `--analysis-log <path>` | 写入文件和阶段分析日志。 |
| `--log-profile <name>`、`--log-level <level>`、`--log-categories <list>`、`--log-events <list>`、`--log-view <name>` | 控制文本日志的过滤与呈现。 |
| `--rewrite-plan-out <directory>` | 对目录分析保存可移植的文本编辑计划，不隐含写回。 |
| `--rewrite-plan-in <directory>` | 验证源文件哈希后回放已保存计划；不能与分析驱动选项或 `--skip-rewrite` 并用。 |
| `--fast-delete-class-directory` | 对 `--delete-class` 目录任务启用快速路径，并跳过目录级改写后诊断。 |
| `--filter-delete-class-files-by-target-name` | 仅在快速目录类删除路径下，跳过源码文本中不含目标类名的文件。 |

旧日志选项 `--runtime-metrics-log`、`--per-file-timing-log`、`--per-file-phase-timing-log-directory` 和 `--per-file-memory-diagnostics-log` 仍可用，但宿主会输出弃用提示。

## 输出与安全边界

- 未指定 `--write-back` 时，分析不改写源文件。
- diff 仅在输入路径存在、产生编辑且未关闭 diff 时写出。
- rewrite plan 回放在 diff 或写回前核对源文件哈希、计划校验和和原始文本；任一检查失败都会中止。
- 文件与目录完成后可查看 runtime / analysis 文本日志，或用测试固定行为。

## 相关页面

- 需要可复制的最短命令：看 [快速开始](quick-start.md)。
- 需要理解阶段语义：看 [核心概念](concepts.md) 与 [删除规则流水线](../设计docs/目前设计/deletion-pipeline.md)。
