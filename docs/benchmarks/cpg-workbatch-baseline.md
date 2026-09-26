# CPG WorkBatch 遥测基线

> 状态：已完成遥测契约样本；不是吞吐或加速承诺。
>
> 测量日期：2026-09-21。

## 范围

本页记录 `CpgWorkBatchPerformanceTests` 对固定 worker、成本/字节估算、结果
归并和背压水位的原始证据。测试使用 small、mixed、large 三个固定源码样本，
每个样本分别使用 DOP 1、2、16；`WorkBatchMaxMethodsPerBatch=2`，因此 large
样本可以实际产生多个 batch。

测试只断言以下不变量：事件阶段属于稳定的 WorkBatch stage id，batch 输入和成本
估算为正，输出节点/边和 fragment bytes 非负，worker index 在配置范围内，队列和
completed-not-reduced 水位有记录，所有阶段的时延非负。它不把 DOP 较高或 worker
数量较多解释成速度提升。

## 命令与环境

通过仓库串行 wrapper 执行：

```powershell
$scriptPath = Join-Path (Get-Location) 'Build/Tools/Invoke-SerialDotnet.ps1'
$dotnetArguments = @(
  'test',
  './tests/NLISSN.PerformanceTests/RoslynDeletionPrototype.PerformanceTests.csproj',
  '--no-restore',
  '-m:1',
  '-nr:false',
  '-p:UseSharedCompilation=false',
  '-p:MSBuildNodeReuse=false',
  '-p:BuildInParallel=false',
  '--filter',
  'FullyQualifiedName~CpgWorkBatchPerformanceTests',
  '--logger',
  'console;verbosity=detailed')
& $scriptPath -DotnetArguments $dotnetArguments
```

结果：`1/1` 通过；测试程序集为
`Build/test/Debug/net10.0/RoslynDeletionPrototype.PerformanceTests.dll`；运行时为
`.NET 10.0.11`。同一变更点的 WorkBatch 合同临时套件为 `58/58`，executor
专项为 `5/5`。

## 原始样本

以下是测试日志中的单次原始样本。DOP1 的 small 样本包含首次 Roslyn 初始化，
因此不能拿这组单次 elapsed 与 DOP2/16 做性能结论；正式性能判断仍需要固定输入、
预热和多次 measurement。

| sample | DOP | elapsed ms | events | DataFlow batches | peak active | queue high-water | completed-not-reduced high-water | fragment bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| small | 1 | 1136 | 9 | 1 | 1 | 2 | 1 | 2032 |
| small | 2 | 114 | 9 | 1 | 2 | 2 | 2 | 2032 |
| small | 16 | 106 | 9 | 1 | 2 | 2 | 2 | 2032 |
| mixed | 1 | 164 | 26 | 1 | 1 | 4 | 4 | 1696 |
| mixed | 2 | 126 | 26 | 1 | 2 | 4 | 4 | 1696 |
| mixed | 16 | 145 | 26 | 1 | 4 | 4 | 4 | 1696 |
| large | 1 | 392 | 384 | 48 | 1 | 10 | 10 | 107520 |
| large | 2 | 416 | 384 | 48 | 2 | 10 | 12 | 107520 |
| large | 16 | 374 | 384 | 48 | 16 | 34 | 48 | 107520 |

事件由 `NLCPGBuildMetrics.WorkBatchPerformanceEvents` 保留，并同时通过
`ICpgWorkBatchPerformanceEventSink` 发布。sink 是 fail-open 的；sink 抛异常不能
改变图构建结果。每个 event 还包含 stage、batch id、stable order、method/item
数量、estimated cost/bytes、actual output node/edge/fragment bytes、queue wait、
worker processing、reducer wait、worker index 和三个峰值指标。

## 证据边界

本页没有旧的函数级调度器在同一输入上的配对时间，因此不声称 WorkBatch 更快、
更省内存或尾延迟更低。large 样本只证明固定 worker 和有界队列在真实多 batch
输入上能发出可校验的遥测；Version4 级别的冷/暖、多次 DOP 对照另存于
`cpg-workbatch-version4.md`，不覆盖本页的小型契约样本。
