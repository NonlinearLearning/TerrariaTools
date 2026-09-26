# CPG WorkBatch Version4 验收记录

> 状态：未完成。
>
> 本页记录一次真实 Version4 长尾运行的边界，不把未生成 manifest 的中间目录当作成功验收。
>
> 测量日期：2026-09-21。

## 运行配置

输入为 `D:\TRbackup\Version4\TerrariaServer.csproj`，目标框架为 `net40`。以下是与本次
DOP1 记录等价的统一配置；当前入口不再接受旧的 `--project`、`--output` 或
`--max-degree-of-parallelism` 参数：

```yaml
schemaVersion: 3
tool: nlcpg-project-export
input:
  path: D:/TRbackup/Version4/TerrariaServer.csproj
  targetFramework: net40
  configuration: Debug
  platform: AnyCPU
  restore: disabled
  generatedSources: exclude
projectExport:
  output: D:/TRbackup/Version4/Build/NLCPG-workbatch-version4-dop-1
  projectWorkerCount: 1
  resume: false
```

从该文件所在目录启动已构建的入口，不附带任何业务参数：

```powershell
dotnet run --no-build --project .\src\NLCPG.ProjectExport\NLCPG.ProjectExport.csproj
```

affected exporter build 已通过：0 warning、0 error，产物为
`Build/src/Debug/net10.0/NLCPG.ProjectExport.dll`。

## DOP1 结果

导出运行约 32 分钟 CPU 后由当前任务向自己启动的 wrapper 发送 Ctrl+C 停止。停止前：

| fact | value |
| --- | ---: |
| source `.cs` files discovered | 995 |
| JSON files written | 961 |
| partial output size | about 3.93 GiB |
| `manifest.json` | absent |
| largest remaining source files | `Terraria/Projectile.cs`, `Terraria/WorldGen.cs` |
| DOP2 / DOP16 | not started |

输出目录是新建的 `D:\TRbackup\Version4\Build\NLCPG-workbatch-version4-dop-1`。
既有 `D:\TRbackup\Version4\Build\NLCPG-json` 未被覆盖或删除。因为缺少 manifest，
尚未执行文件镜像计数、manifest node index、edge endpoint 解析和跨 DOP 比较。

## 判定

本次运行不能证明 Version4 的 graph/query/rule/rewrite/diff 等价，也不能证明峰值内存、
freeze/catalog、P95/P99 或 WorkBatch 相对旧路径的性能变化。Task 14 的 Version4
acceptance 保持未完成。

下一次运行前需要为真实大文件增加有界超时或可恢复的逐文档导出，并在 `manifest.json`
存在且边端点验证通过后，才启动 DOP2/16 配对样本。部分输出目录只能作为诊断材料，
不能作为可查询构建或性能样本。
