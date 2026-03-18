# Dome 测试说明

本文描述当前测试结构和分类原则。

## 1. 测试目录

`tests/Dome.Tests` 当前按域和测试类型分层：

- `Analysis`
  - `Contracts`
  - `Integration`
  - `Unit`
- `Application`
  - `Contracts`
  - `Integration`
  - `Unit`
- `Cli`
  - `Integration`
  - `Unit`
- `Plan`
  - `Unit`
- `Reporting`
  - `Contracts`
  - `Unit`
- `Rewrite`
  - `Contracts`
  - `Golden`
  - `Unit`
- `Rules`
  - `Slice`
  - `Unit`
- `DomeTesting`
  - `TestBuilders`
  - `TestDoubles`
  - `Compatibility`

共享测试基础设施位于：

- `tests/TerrariaTools.Testing`

## 2. 当前测试分层规则

- `Unit`：本地逻辑、builder 驱动、无真实文件系统或最少真实依赖
- `Integration`：真实 Roslyn、真实 artifact、真实 workspace、真实 IO 组合
- `Contracts`：跨模块行为契约与边界门禁
- `Golden`：重写结果或产物的基线比较
- `Slice`：规则或分析链路的小切面验证
- `Compatibility`：旧行为覆盖，但底层对象仍应使用 native contracts

## 3. 共享测试代码约束

- `tests/TerrariaTools.Testing` 放通用 fixture、builder、double、断言
- `tests/Dome.Tests/DomeTesting` 放 Dome 专用测试支持
- 标准测试默认使用 native contracts
- `Compatibility` 目录表示“兼容行为覆盖”，不再表示 `Core` 兼容

## 4. 目录约束

- 域根目录下不直接放测试 `.cs`
- snapshot/verify 文件应与所属测试相邻
- `Application/Integration` 只保留真实端到端组合
- 需要真实环境行为时，优先通过 fixture/builder，不在测试方法内堆文件系统样板

## 5. 当前文档与代码的关系

虽然测试里仍保留 `Plan/Unit` 目录名，但计划能力本身现在来自：

- `src/Model/Planning`
- `src/Model/Rules/AuditPlanCompiler.cs`
- `src/Application/DomeApplicationStages.cs`

这里的 `Plan` 更接近“计划编译测试分组”，不是现行独立项目。
