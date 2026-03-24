# Joernish CPG Prototype Design

**Status:** Approved

**Date:** 2026-03-24

## Goal

在 `dome/prototypes` 下实现一个接近 Joern `x2cpg` 的完整 CPG 原型，但范围只保留 CPG 相关模型和高相关代码。原型必须对齐 Joern 的前端生成模型、layer creator、overlay 顺序、pass 组织方式，以及 schema 驱动的节点类型与继承关系生成。

## Scope

本轮原型只包含以下内容：

- CPG schema 定义
- schema 驱动的节点类型、接口、kind 常量生成
- 内存图模型与 diff graph
- Roslyn C# frontend
- frontend/base/controlflow/typerelations/callgraph 五类 pass
- 默认 overlays 及其依赖顺序
- 针对 schema、frontend、pass、end-to-end 的测试

## Explicit Exclusions

本轮明确不做以下内容：

- Host
- CLI
- Output
- Persistence
- Plugin/registry
- 多语言 frontend 抽象
- 为未来扩展预留但当前没有直接价值的工厂、注册器、配置壳对象
- 与 CPG 主链路无关的调试导出层

## Target Layout

```text
dome/prototypes/JoernishCpg/
  Schema/
  Generated/
  Graph/
  Frontend/
  Passes/
  Tests/
```

## Architecture

该原型采用和 Joern 相同的两阶段模型：

1. `RoslynCSharpFrontend` 将源码映射为 frontend CPG。
2. `DefaultOverlays` 按固定顺序应用增强层，将 frontend CPG 变为更完整的 CPG。

schema 是唯一事实源。节点类型、接口、kind 常量与继承关系全部从 schema 生成，而不是手写散落在业务代码里。运行时只消费 schema 生成结果和图模型，不重复硬编码类型真相。

## Core Modules

### Schema

`Schema` 目录只定义以下模型：

- `CpgSchema`
- `CpgLayerSchema`
- `CpgNodeSchema`
- `CpgEdgeSchema`
- `CpgPropertySchema`
- `BuiltinSchema`

职责：

- 表达 layer、node、edge、property 及其依赖
- 表达节点主基类与角色接口
- 作为代码生成器的唯一输入

### Generated

`Generated` 目录只保留生成结果：

- `NodeKinds.g.cs`
- `EdgeKinds.g.cs`
- `PropertyKinds.g.cs`
- `NodeInterfaces.g.cs`
- `NodeBaseTypes.g.cs`
- `NodeTypes.g.cs`
- `SchemaIndex.g.cs`

职责：

- 暴露稳定的 kind 常量
- 生成节点类型与继承结构
- 暴露 schema 索引，供 runtime 和 passes 使用

### Graph

`Graph` 目录只保留 CPG 运行时核心：

- `DomeCpg`
- `DiffGraph`
- `DiffGraphApplier`
- `CpgContext`
- `LayerCreator`

职责：

- 保存节点与边
- 支持 pass 以 diff 的方式更新图
- 提供 overlay 运行上下文

### Frontend

`Frontend` 目录只保留 Roslyn 入口和上下文：

- `RoslynCSharpFrontend`
- `RoslynFrontendConfig`
- `RoslynFrontendContext`

职责：

- 收集源码文件
- 构建 Roslyn compilation
- 驱动 frontend passes
- 返回 frontend CPG

### Passes

`Passes` 目录按语义分为：

- `Frontend`
- `Base`
- `ControlFlow`
- `TypeRelations`
- `CallGraph`

职责：

- `Frontend`: 只生成源码直接事实
- `Base`: 补全 file、namespace、stub、AST link、contains、type ref、type eval
- `ControlFlow`: 生成 CFG、dominator、post-dominator、CDG
- `TypeRelations`: 生成继承、别名和字段相关链接
- `CallGraph`: 生成调用图链接

## Execution Model

执行顺序固定为：

1. `RoslynCSharpFrontend.CreateCpg(config)`
2. 运行 frontend passes
3. `DefaultOverlays.Apply(cpg, context)`
4. 依次运行：
   - `BaseLayer`
   - `ControlFlowLayer`
   - `TypeRelationsLayer`
   - `CallGraphLayer`

每个 overlay 必须声明：

- `overlayName`
- `description`
- `dependsOn`

每个 overlay 成功执行后都要回写 `META_DATA.OVERLAYS`。

## Simplifications

为保持首轮聚焦，以下内容做了有意识修剪：

- 不引入 `OverlayRegistry`，默认顺序固定为静态列表
- 不引入 `X2CpgFrontend<T>` 泛型体系，只保留 `RoslynCSharpFrontend`
- 不引入 `RoslynCompilationFactory`，编译构建先内聚在 frontend 内部
- 不引入 `CpgCodeGenerationOptions`，生成器先走固定输出
- 将 `PassContext` 与 `LayerCreatorContext` 合并为 `CpgContext`
- 将 `StaticCallLinkerPass` 与 `DynamicCallLinkerPass` 合并为 `CallGraphLinkerPass`
- 将 `AliasLinkerPass` 与 `FieldAccessLinkerPass` 合并为 `TypeRelationsPass`

这些修剪删除的是过早抽象，不删除 Joern 的关键边界。

## Minimal Node Set

首批必须覆盖的节点：

- `META_DATA`
- `NAMESPACE_BLOCK`
- `TYPE_DECL`
- `MEMBER`
- `METHOD`
- `METHOD_PARAMETER_IN`
- `METHOD_RETURN`
- `BLOCK`
- `CALL`
- `IDENTIFIER`
- `LITERAL`
- `LOCAL`
- `CONTROL_STRUCTURE`
- `RETURN`
- `FIELD_IDENTIFIER`
- `TYPE_REF`
- `METHOD_REF`

由 overlays 自动补齐的节点：

- `FILE`
- `NAMESPACE`
- `METHOD_PARAMETER_OUT`
- `TYPE`

首批必须覆盖的边：

- `AST`
- `ARGUMENT`
- `RECEIVER`
- `REF`
- `SOURCE_FILE`
- `CONTAINS`
- `EVAL_TYPE`
- `CFG`
- `DOMINATE`
- `POST_DOMINATE`
- `CDG`
- `INHERITS_FROM`

## Testing Strategy

测试只围绕 CPG 主链路展开：

- schema generation tests
- frontend contract tests
- base/controlflow/type-relations/call-graph pass tests
- end-to-end tests

不为 Host、CLI、Output、Persistence 编写任何测试。

## Acceptance Criteria

满足以下条件即视为第一轮完成：

- schema 可以稳定生成节点类型、接口和 schema index
- frontend 可以从 C# 源码生成 frontend CPG
- 默认 overlays 可以按固定顺序成功执行
- `META_DATA.OVERLAYS` 正确记录执行顺序
- 核心节点与边可通过端到端测试验证

## References

- Joern x2cpg default overlay order:
  `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\X2Cpg.scala`
- Joern base layer:
  `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\Base.scala`
- Joern control flow layer:
  `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\ControlFlow.scala`
- Joern type relations layer:
  `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\TypeRelations.scala`
- Joern call graph layer:
  `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\CallGraph.scala`
- Joern layer creator contract:
  `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\layers\LayerCreator.scala`
- CPG specification:
  `https://cpg.joern.io/`
