# AGENTS.md

先读这些文件，再行动：

- `AGENTS.md`
- `progress.md`：先看上次做到哪、哪些还没验证
- `feature_list.json`：先看当前 feature 状态、definition of done、下一项任务
- `init.ps1`：开始前跑一次，统一环境与最小健康检查
- `约束/Google-CSharp-Style-Guide-约束.md`：只要改 `*.cs` 就适用
- `约束/GPT-5.4黑话与冗词抑制约束.md`：回答和文档改写都适用
- `约束/教 AI 怎么搜准.md`：只有本地证据不够，或用户明确要求联网时再扩大搜索
- `约束/测试代码编写教程.md`：只要写、改、解释测试代码就先读

## Repo Shape

- 当前可执行项目只有 `NewJoern/NewJoern.csproj`。根目录没有 `.sln`、测试工程、CI workflow、`Directory.Build.*` 或 `.editorconfig`。
- `src/` 目前是空目录，不是实际代码入口。
- 设计稿和过程文档主要在 `设计docs/`，运行相关文档在 `NewJoern/docs/`。
- 当前 harness 入口文件在仓库根：`AGENTS.md`、`progress.md`、`feature_list.json`、`init.ps1`。
- `NewJoern/AGENTS.md` 负责代码目录局部导航，`NewJoern/docs/AGENTS.md` 负责文档目录局部导航。

## Runtime Facts

- SDK 固定在 `global.json`：`.NET SDK 10.0.200-preview.0.26103.119`。
- 目标框架是 `net10.0`，项目类型是可执行程序。
- 运行 `dotnet` 前先设置：`$env:DOTNET_CLI_HOME=(Resolve-Path '.').Path`。这不是保证成功，只是避免 first-time use/权限类干扰。
- 2026-04-29 当前环境已实跑通过：`pwsh -File .\init.ps1` 内的 `dotnet build .\NewJoern\NewJoern.csproj` 健康检查。

## Real Entrypoints

- CLI 入口：`NewJoern/Program.cs`
- Frontend 总入口：`NewJoern/Frontend/NewJoernFrontend.cs`
- 输入加载：`NewJoern/Frontend/Roslyn/RoslynProjectLoader.cs`
- 图结构：`NewJoern/Graph/NewJoernGraph.cs`
- Schema 契约：`NewJoern/Schema/NewJoernSchemaContract.cs`
- Overlay 契约和默认顺序：`NewJoern/Overlays/NewJoernOverlayContract.cs`
- 图校验：`NewJoern/Validation/NewJoernGraphValidator.cs`
- 切片入口：`NewJoern/Slicing/DataFlowSlicer.cs`、`NewJoern/Slicing/UsageSlicer.cs`
- 导出：`NewJoern/Export/GraphExporter.cs`

首次接手 `NewJoern` 时，优先按上面顺序读，不要随机翻叶子文件。

## CLI Contract

`Program.cs` 目前只有 3 个一级命令：

```text
dotnet run --project .\NewJoern\NewJoern.csproj -- parse <input-path> [--json-out <path>]
dotnet run --project .\NewJoern\NewJoern.csproj -- export <input-path> --format <json|dot> --out <path>
dotnet run --project .\NewJoern\NewJoern.csproj -- slice <input-path> [--mode data-flow|usages] --out <path> ...
```

- `slice` 默认是 `data-flow`。
- `data-flow` 必须传 `--sink-code`，`--slice-depth` 默认 `20`。
- `usages` 支持 `--target-name`、`--min-num-calls`、`--exclude-operators`。
- CLI 层很薄。新增分析能力时，优先改 frontend / schema / overlay / slicing，不要把逻辑塞回 `Program.cs`。

## Input Rules

`RoslynProjectLoader` 当前支持的输入只有 4 类：

- `.csproj`
- `.sln`
- 单个 `.cs`
- 包含 `.cs` 的目录

目录输入会递归扫描 `*.cs`，并跳过 `bin/`、`obj/`。

## Structural Constraints

- `NewJoernFrontend` 默认会先构图，再跑默认 overlays，再执行 graph validation。
- 默认 overlay 顺序由 `NewJoernOverlayContract.DefaultOverlayOrder` 定义：`base -> typerefs -> controlflow -> dominators -> controldependence -> callgraph -> reachingdef`。
- `NewJoernGraph.AddEdge` 会去重边，并在新增 AST 边时自动补 `ORDER`。
- 改 schema、node kind、edge kind、ownership、method child 结构、`Argument/ParameterLink/Call/Ref` 边形状时，通常必须同步改 `NewJoernGraphValidator`。

## Verification

- 这个仓库当前没有已发现的测试项目可跑，验证主要靠 `dotnet build` / `dotnet run`。
- 开始非平凡任务前，先执行：`pwsh -File .\init.ps1`。
- 如果后续环境再次出现 restore 阻塞，明确说明“已读源码确认契约，但未完成端到端运行验证”。
- 改 CLI 或文档时，优先同步 `NewJoern/docs/quick-start.md`、`NewJoern/docs/cli-reference.md`、`NewJoern/docs/developer-guide.md` 中受影响部分。
- 需要人工交接时，更新根目录 `progress.md`，至少写清楚：当前事实、阻塞、下一步、哪些验证没做。
- 做任务切换时，同步更新 `feature_list.json`，不要只改 `progress.md`。
- 更完整的验证口径见 `NewJoern/docs/verification-playbook.md`。
- 机械化一致性检查入口：`pwsh -File .\scripts\check-harness-consistency.ps1`。
- 如需 commit 前本地阻断，执行：`git config core.hooksPath .githooks`。

## 文档改写约束

- 改 `*.md`，尤其是 `设计docs/` 下的大文档时，优先使用**小批次 patch**，一次只改一个文件，或只改一个清晰子块。
- 不要一次提交跨多个大文档的大重写 patch。先落主入口，再落专题，再落跳转页或附录。
- 如果某份长文档要重写，先定新边界和目录，再分批落内容，不要在单个 patch 里同时重写整组文档。
- 每个小批次 patch 后都要立刻回读改动文件，确认结构、标题层级、交叉引用和文件名没有漂移，再继续下一批。
