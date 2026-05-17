# NL

文档大部分都是写给AI看的看这些文档没有什么意义,文档包括脚本很多并不能离开本机的环境
`NL` 是一个以 Roslyn 为核心的 .NET 研究型仓库，当前主线在做两件事：

1. 构建一个 Roslyn-native 的最小 CPG 原型
2. 验证一个 Roslyn-first 的代码删除规则原型

这个仓库当前不是一个已经产品化的通用框架，也不是一个稳定发布的终端工具集合。它更接近一个持续推进中的研究与验证工作区：一部分代码在验证图构建与分析抽象，一部分代码在验证基于 Roslyn 事实的删除规则、传播、决策与回写主线。

如果你第一次进入这个仓库，最重要的事实是：

1. 当前实际活跃代码主线不在 `NewJoern/`
2. 当前最小可运行项目是 `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`
3. 当前独立删除原型项目是 `src/RoslynPrototype/RoslynPrototype.csproj`
4. 设计过程文档主要放在 `设计docs/`
5. 测试项目当前在 `tests/RoslynDeletionPrototype.Tests/`

## 适合谁

这个仓库当前更适合以下读者：

1. 想研究 Roslyn-native CPG 最小实现路径的人
2. 想研究基于 Roslyn 的删除规则系统该如何最小落地的人
3. 想看一个研究型 .NET 仓库如何把设计稿逐步压成可运行原型的人
4. 要继续维护、补齐、重构本仓库的人

它当前不太适合这些预期：

1. 想直接拿来当成熟产品使用
2. 想得到完整稳定 CLI 的用户
3. 想查完整 API 手册的用户
4. 想找已经冻结的对外协议或插件机制的用户

## 这份 README 负责什么

这份 README 只负责做项目总入口和阅读分流。

它负责：

1. 说明仓库当前在做什么
2. 说明当前有哪些活跃项目
3. 说明从哪里开始运行和验证
4. 说明应该先看哪些代码和文档
5. 给出后续阅读入口

它不负责：

1. 解释全部内部模型
2. 替代设计文档
3. 列出全部 CLI 细节
4. 替代参考手册
5. 解释每一个实验性分支设计

## 当前仓库主线

当前仓库并行推进两条主线。

### 1. `MinimalRoslynCpg`

路径：`src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`

这条主线负责验证一个 Roslyn-native 的最小 CPG 核心。当前已经覆盖的重点包括：

1. syntax / token / symbol / operation 节点
2. 第一批图抽象层，例如 `TypeDecl`、`TypeRef`、`Reference`、`CallSite`
3. method-local CFG
4. intraprocedural reaching-def 风格 `DataFlow`
5. 更稳定的方法身份归一
6. 第一批 member access 抽象
7. 第一批动态调用候选扩展
8. 方法边界抽象，例如 `Method`、`MethodParameter`、`MethodEntry`、`MethodExit`、`MethodReturn`

如果你更关心“图怎么建”“调用边怎么补”“CFG / DDG 先做到什么程度”，优先看这条主线。

### 2. `RoslynPrototype`

路径：`src/RoslynPrototype/RoslynPrototype.csproj`

这条主线负责验证一个 Roslyn-first 的删除规则原型。当前主线不是完整产品化删除引擎，而是一个围绕以下五阶段组织起来的最小原型：

```text
分析 -> 标记 -> 传播 -> 决策 -> 改写
```

当前已经落地并可验证的要点包括：

1. `DeletionApplicationService` 作为最小应用层编排入口
2. 规则通过 `IDeletionRule` 接入
3. `MarkingEngine` 和 `PropagationEngine` 分开负责 direct mark 与 propagated mark
4. `RuleDecisionEngine` 消费标记结果，生成决策
5. `PrototypeRewriter` 负责把决策落到源码改写结果上
6. 当前已有 `s` 相关表达式删除样例和不可达函数删除样例
7. 当前已有测试项目覆盖最小端到端流程

如果你更关心“如何围绕 Roslyn 事实做删除规则”“如何从语法/语义命中推进到 rewrite”，优先看这条主线。

## 快速开始

### 前置条件

当前仓库固定了 .NET SDK 版本信息，见 `global.json`。

建议最先做的事：

1. 在仓库根执行一次 `pwsh -File .\init.ps1`
2. 让仓库自己探测当前活跃可执行项目并做健康检查

命令：

```powershell
pwsh -File .\init.ps1
```

这一步的目标不是跑完整功能，而是确认：

1. 当前仓库根路径正常
2. `DOTNET_CLI_HOME` 已设置
3. 当前活跃项目至少能通过最小构建健康检查

### 最小成功路径 1：运行 `MinimalRoslynCpg`

如果你想先确认最小 CPG 主线可运行，可以执行：

```powershell
dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj
```

如果你想用仓库里现成样例再跑一次：

```powershell
dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj .\src\MinimalRoslynCpg\samples\analysis-sample.cs
```

### 最小成功路径 2：运行 `RoslynPrototype`

如果你想先确认删除规则原型可运行，可以执行：

```powershell
dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s
```

这个最小样例当前会围绕 `s` 目标对象进行分析，并输出：

1. `SeedMarks`
2. `PropagatedMarks`
3. `Decisions`
4. `Edits`
5. `RewrittenSource`

### 最小成功路径 3：运行测试

如果你想先确认删除原型当前最小端到端测试是否通过，可以执行：

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj
```

## 仓库结构总览

下面这份总览不是完整目录树，而是按阅读和工作优先级组织的入口图。

### 根目录

先看这些：

1. `AGENTS.md`
2. `progress.md`
3. `feature_list.json`
4. `init.ps1`
5. `约束/`
6. `设计docs/`

### `src/`

当前最重要的两个项目：

1. `src/MinimalRoslynCpg/MinimalRoslynCpg.csproj`
2. `src/RoslynPrototype/RoslynPrototype.csproj`

此外还有一个当前直接被 `RoslynPrototype` 编译使用的应用层路径：

1. `src/Application/DeletionApplicationService.cs`

### `tests/`

当前测试主线：

1. `tests/RoslynDeletionPrototype.Tests/RoslynDeletionPrototype.Tests.csproj`

### `设计docs/`

建议优先看：

1. `2026-05-02-删除规则-Roslyn-first-一般重设计.md`
2. `2026-05-04-RoslynDeletionPrototype-标记与传播设计冻结版.md`
3. `2026-04-29-内部模型总览.md`

## 从哪里开始读代码

### 如果你关心 `RoslynPrototype`

建议按这个顺序读：

1. `src/RoslynPrototype/Program.cs`
2. `src/Application/DeletionApplicationService.cs`
3. `src/Analysis/` 相关实现
4. `src/Rules/` 相关规则实现
5. `tests/RoslynDeletionPrototype.Tests/`

### 如果你关心 `MinimalRoslynCpg`

建议按这个顺序读：

1. `src/MinimalRoslynCpg/Program.cs`
2. `src/MinimalRoslynCpg/Builder/`
3. 图节点与边模型
4. 调用图、CFG、DataFlow 相关实现
5. `samples/`

## 当前已经验证到什么程度

当前仓库已经有可运行、可测试、可回写的最小验证主线，但仍然处于研究型阶段。

### 已经验证通过的部分

根据当前仓库状态，至少已经验证过：

1. `pwsh -File .\init.ps1`
2. `dotnet build .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj`
3. `dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj`
4. `dotnet run --project .\src\MinimalRoslynCpg\MinimalRoslynCpg.csproj .\src\MinimalRoslynCpg\samples\analysis-sample.cs`
5. `dotnet build .\src\RoslynPrototype\RoslynPrototype.csproj`
6. `dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s`
7. `dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj`

### 还没有做完的部分

当前明确还没做完的高价值事项包括：

1. 基于真实调用图的不可达性求解
2. 基于真实 CPG fact 的 rule matching
3. 参数删除 / 参数列表收缩
4. member-access 局部保留优先于整表达式替换的更细 rewrite
5. 跨文件回写与多文档编辑
6. 更完整的产品化命令接口
7. 全仓库统一参考手册和对外 API 文档

## 当前项目的核心概念

### `Roslyn-first`

在当前仓库语境里，`Roslyn-first` 的意思不是“完全不要图”，而是：

1. 规则命中优先依赖 Roslyn 已经稳定提供的语法和语义事实
2. 图层继续保留，但不是每一步都必须先抽成厚重内部模型
3. 结果对象尽量少而硬
4. 先把基础流程跑通，再决定哪些抽象值得长期保留

### `CPG`

在当前仓库语境里，`CPG` 不是要一步复刻完整 joern，而是：

1. 先用 Roslyn 事实构出最小图核心
2. 逐步补 syntax / symbol / operation 之间的映射
3. 逐步补 CFG、DataFlow、调用关系
4. 在每一步都优先验证“是否已经能支撑当前分析问题”

### 五阶段删除原型主线

当前 `RoslynPrototype` 的核心主线是：

```text
分析 -> 标记 -> 传播 -> 决策 -> 改写
```

## 常见任务

### 任务 1：我只想先把仓库跑起来

先执行：

```powershell
pwsh -File .\init.ps1
```

### 任务 2：我想看删除原型当前效果

执行：

```powershell
dotnet run --project .\src\RoslynPrototype\RoslynPrototype.csproj .\src\RoslynPrototype\samples\delete-s-object-sample.cs --target-name s
```

### 任务 3：我想看当前测试基线

执行：

```powershell
dotnet test .\tests\RoslynDeletionPrototype.Tests\RoslynDeletionPrototype.Tests.csproj
```

### 任务 4：我想先理解设计再看代码

优先看：

1. `设计docs/2026-05-02-删除规则-Roslyn-first-一般重设计.md`
2. `设计docs/2026-05-04-RoslynDeletionPrototype-标记与传播设计冻结版.md`
3. `progress.md`
4. `feature_list.json`

## 文档入口

### 项目文档

优先入口：

1. `docs/README.md`
2. `docs/quick-start.md`
3. `docs/developer-guide.md`

### 设计与架构

优先入口：

1. `设计docs/2026-05-02-删除规则-Roslyn-first-一般重设计.md`
2. `设计docs/2026-05-04-RoslynDeletionPrototype-标记与传播设计冻结版.md`
3. `设计docs/2026-04-29-内部模型总览.md`

### 当前状态与执行约束

优先入口：

1. `AGENTS.md`
2. `progress.md`
3. `feature_list.json`
4. `约束/`

### 测试与验证

优先入口：

1. `tests/RoslynDeletionPrototype.Tests/`
2. `约束/测试代码编写教程.md`

## 贡献与开发

如果你要继续在这个仓库里开发，建议遵循这条最短路径：

1. 先读 `AGENTS.md`
2. 再读 `progress.md`
3. 再读 `feature_list.json`
4. 先跑 `pwsh -File .\init.ps1`
5. 再进入你要改的主线项目

如果你是要改测试：

1. 先读 `约束/测试代码编写教程.md`
2. 再读现有测试项目
3. 测试优先验证 public 行为，不要优先写实现细节测试

如果你是要改文档：

1. 先读 `约束/高星开源项目文档写法研究与落地约束.md`
2. 文档先判断类型，再决定写法
3. 大文档优先做清晰边界，不要直接堆内容

## 当前限制与非目标

当前非目标至少包括：

1. 不是完整产品级删除引擎
2. 不是完整 joern 等价实现
3. 不是稳定发布的 CLI 套件
4. 不是已经冻结的插件平台
5. 不是完备 API 参考站点

## 下一步看哪里

如果你已经看完这份 README，下一步按你的目标继续：

1. 想先跑仓库：看上面的“快速开始”
2. 想理解删除原型：看 `设计docs/2026-05-02-删除规则-Roslyn-first-一般重设计.md`
3. 想理解标记与传播：看 `设计docs/2026-05-04-RoslynDeletionPrototype-标记与传播设计冻结版.md`
4. 想理解当前事实和待办：看 `progress.md` 与 `feature_list.json`
5. 想直接看实现：从 `src/Application/DeletionApplicationService.cs` 开始
6. 想确认测试基线：跑 `tests/RoslynDeletionPrototype.Tests`

这份 README 的目标不是替你读完整个仓库，而是让你在进入仓库后的前几分钟内，知道现在的主线是什么、应该先去哪、哪些东西已经能跑、哪些东西还只是设计验证。
