# 独立日志模块 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将可复用的文本日志基础设施迁移到零项目依赖的 `src/Logging/Logging.csproj`，并保持 Host 的日志输出契约。

**Architecture:** `Logging` 持有通用事件、过滤、格式化和文件 sink。Host 继续持有将 CPG、规则分析与改写 telemetry 映射为日志事件的两个 writer，并只向 `Logging` 依赖。

**Tech Stack:** .NET 10、C#、xUnit、MSBuild project references。

---

### Task 1: 锁定可独立引用的模块边界

**Files:**
- Modify: `tests/RoslynDeletionPrototype.HostTests/RoslynDeletionPrototype.HostTests.csproj`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`
- Create: `src/Logging/Logging.csproj`

**Step 1: Write the failing test**

让 `TextLogSystemTests` 直接 `using RoslynPrototype.Logging;`，新增
`LoggingModule_ExportsPublicTextLogContract`，直接构造 `TextLogFilter` 和
`TextLogEvent` 并断言允许的 `Info/Run/Started` 事件。向 HostTests 添加对尚不存在的
`src/Logging/Logging.csproj` 的 `ProjectReference`。

**Step 2: Run test to verify it fails**

Run: `dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~LoggingModule_ExportsPublicTextLogContract`

Expected: 编译失败，报缺少 `Logging.csproj` 或 `RoslynPrototype.Logging` 命名空间；失败原因必须是模块尚不存在。

**Step 3: Write minimal implementation**

创建 `src/Logging/Logging.csproj`，使用仓库标准的 `net10.0`、nullable、implicit usings 与
preview language version。项目不含 `PackageReference` 或 `ProjectReference`。

**Step 4: Run test to verify it passes**

此步在 Task 2 移入类型后执行。

### Task 2: 迁移通用文本日志底座

**Files:**
- Create: `src/Logging/ITextLogSink.cs`
- Create: `src/Logging/TextLogFileSink.cs`
- Create: `src/Logging/TextLogFormatter.cs`
- Create: `src/Logging/TextLogFilter.cs`
- Create: `src/Logging/TextLogEvent.cs`
- Create: `src/Logging/TextLogField.cs`
- Create: `src/Logging/RunLogContext.cs`
- Create: `src/Logging/TextLogCategory.cs`
- Create: `src/Logging/TextLogEventType.cs`
- Create: `src/Logging/TextLogLevel.cs`
- Create: `src/Logging/TextLogView.cs`
- Delete: matching infrastructure files under `src/Host/Logging/`

**Step 1: Move the dependency-free types**

复制现有实现到 `src/Logging/`，把命名空间改为 `RoslynPrototype.Logging`，并把构成外部
模块 API 的类型设为 `public`。不改变 formatter 的字段顺序、filter 的 profile 规则、sink 的
通道容量或批处理语义。

**Step 2: Verify the red test is now green**

Run: `dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~LoggingModule_ExportsPublicTextLogContract`

Expected: PASS；测试使用新程序集的公开 API。

**Step 3: Verify standalone compilation**

Run: `dotnet build .\src\Logging\Logging.csproj --no-restore -p:UseSharedCompilation=false`

Expected: 成功，且构建输出不需要 Application、Rules、Host 或 RoslynPrototype.Core 项目。

### Task 3: 让 Host 仅保留业务日志适配器

**Files:**
- Modify: `src/Host/Host.csproj`
- Modify: `src/Host/DeletionApplicationOptions.cs`
- Modify: `src/Host/DeletionCommandHost.cs`
- Modify: `src/Host/DeletionDirectoryAnalysisService.cs`
- Modify: `src/Host/Logging/RunTextLogWriter.cs`
- Modify: `src/Host/Logging/AnalysisTextLogWriter.cs`
- Modify: `tests/RoslynDeletionPrototype.HostTests/Logging/TextLogSystemTests.cs`

**Step 1: Update Host dependencies**

添加 `Host -> Logging` 项目引用。Host writer 保持在
`RoslynPrototype.Application.Logging`，通过 `using RoslynPrototype.Logging;` 使用新模块类型；
三个 Host 入口改用新命名空间。

**Step 2: Preserve the existing writer seam**

将反射测试改为从 `typeof(TextLogFilter).Assembly` 获取 filter/enum 类型，从
`typeof(DeletionCommandHost).Assembly` 获取 `AnalysisTextLogWriter`。这样测试同时锁定
日志底座与 Host 适配器位于不同程序集。

**Step 3: Run focused Host regression**

Run: `dotnet test .\tests\RoslynDeletionPrototype.HostTests\RoslynDeletionPrototype.HostTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~TextLogSystemTests`

Expected: PASS；原有 CLI alias、profile、filter、batch、runtime/analysis sink 和 diff lifecycle
断言均不变。

### Task 4: 验证项目边界和交付质量

**Files:**
- Modify: `tests/RoslynDeletionPrototype.ContractTests/Architecture/ArchitectureBoundaryTests.cs`（仅当现有边界测试适合加入 Logging 无项目依赖断言）
- Modify: `progress.md`（仅在需要人工交接时）

**Step 1: Add a structural regression if the existing architecture suite owns project boundaries**

断言 `src/Logging/Logging.csproj` 没有 `ProjectReference` 与 `PackageReference`，并保留现有
Host 不反向被 Application 或 Core 引用的断言。

**Step 2: Run project and architecture verification**

Run: `dotnet build .\src\Host\Host.csproj --no-restore -p:UseSharedCompilation=false`

Expected: PASS。

Run: `dotnet test .\tests\RoslynDeletionPrototype.ContractTests\RoslynDeletionPrototype.ContractTests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~ArchitectureBoundaryTests`

Expected: PASS。

**Step 3: Run repository consistency checks**

Run: `pwsh -File .\scripts\check-harness-consistency.ps1`

Expected: PASS。

Run: `git diff --check`

Expected: 无空白错误。

**Step 4: Commit**

暂存本任务涉及的 Logging、Host、HostTests、ContractTests 和计划文件；提交信息遵循仓库 Lore
trailers，明确零依赖边界、拒绝的整包迁移方案、验证命令和未验证项。
