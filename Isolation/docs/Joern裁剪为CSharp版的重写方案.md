# Joern 裁剪为 C# 版的重写方案

## 1. 目标

当前目标不是将 `Joern` 整体迁移为 C#，而是将其裁剪为一个适合本项目的、仅保留关键 `CPG` 能力的 C# 实现。

本阶段建议仅保留以下能力：

- C# 源码输入
- 基础 `CPG` 节点与边构建
- 基础类型关系
- 基础调用关系
- 可选的基础控制流

本阶段不建议保留以下能力：

- 多语言前端
- 查询 DSL
- 交互式控制台
- QueryDB
- 大而全的数据流分析引擎
- 完整的平台级插件体系

一句话总结：

**要迁移的是最小 C# CPG 前端与图模型，不是整个 Joern 平台。**

---

## 2. Joern 中真正需要参考的部分

根据 `C:\Users\shan\Downloads\joern-master` 当前项目结构，和本项目目标最相关的模块主要有四块：

- `csharpsrc2cpg`
- `x2cpg`
- `semanticcpg`
- `dataflowengineoss`

它们的角色分别是：

- `csharpsrc2cpg`
  - C# 前端
  - 将 C# 源码转换为 CPG
- `x2cpg`
  - 通用前端框架
  - 提供基础建图流程、基础 passes、类型关系、调用关系、控制流关系
- `semanticcpg`
  - 查询与语义增强层
  - 更偏向 traversal、导出、查询体验
- `dataflowengineoss`
  - 轻量数据流分析层

对于本项目，优先级应当是：

- 第一优先级：`csharpsrc2cpg`
- 第二优先级：`x2cpg`
- 第三优先级：`dataflowengineoss` 中极少量可借鉴部分
- 最低优先级：`semanticcpg`

---

## 3. Joern 当前主流程

Joern 中 C# 前端的主流程入口在：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala:34`

从该文件可以看出，核心流程大致如下：

- 准备空 CPG
- 运行 C# AST 生成
- 生成程序摘要
- 写入 `MetaData`
- 执行依赖处理
- 执行 `AstCreationPass`
- 执行 `TypeNodePass`
- 根据需要叠加后处理 Pass

对应的关键调用位置包括：

- `MetaDataPass`
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala:52`
- `AstCreationPass`
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala:62`
- `TypeNodePass`
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala:63`

这说明如果本项目要做 C# 版最小 CPG，真正需要复刻的是：

- 前端入口
- AST 到 CPG 的映射器
- 基础 Pass 管线

而不是整个平台外围功能。

---

## 4. 哪些文件需要重写为 C#

### 4.1 必须重写

这些内容不能直接复用 Scala 实现，必须在本项目中重新用 C# 实现。

#### 前端入口

Joern 对照参考：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala:34`

建议在本项目中重写为：

- `Frontend/RoslynCpgFrontend.cs`

职责：

- 加载解决方案、项目或源码目录
- 创建 Roslyn `Compilation`
- 遍历语法与语义信息
- 驱动 CPG 构建流程

#### 建图主流程

Joern 对照参考：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\passes\AstCreationPass.scala:13`

建议在本项目中重写为：

- `Build/RoslynAstToCpgBuilder.cs`

职责：

- 遍历语法树与语义模型
- 构建节点
- 构建基础边
- 汇总局部 diff
- 合并到最终图

#### 语法/声明/表达式/语句建模器

Joern 对照参考目录：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation`

建议在本项目中重写为：

- `Build/DeclarationNodeBuilder.cs`
- `Build/ExpressionNodeBuilder.cs`
- `Build/StatementNodeBuilder.cs`

职责：

- 将 Roslyn 节点映射为最小 CPG 节点
- 统一处理类型、成员、局部变量、调用、控制结构、字面量等

#### CPG 核心模型

Joern 的底层依赖 `codepropertygraph` 生成代码体系，本项目不适合照搬。

建议在本项目中直接重写为：

- `Core/CpgGraph.cs`
- `Core/CpgNode.cs`
- `Core/CpgEdge.cs`
- `Core/CpgNodeKind.cs`
- `Core/CpgEdgeKind.cs`
- `Core/CpgPropertyBag.cs`

职责：

- 表达图结构
- 表达节点/边种类
- 承载属性
- 提供图追加与查询的最小能力

### 4.2 强烈建议重写

这部分不是绝对第一天就要做完，但如果不做，后面图会很难用。

#### 基础 Pass 管线

Joern 对照参考：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\Base.scala:9`

Joern 在基础层串起来的关键 Pass 包括：

- `FileCreationPass`
- `NamespaceCreator`
- `TypeDeclStubCreator`
- `MethodStubCreator`
- `ParameterIndexCompatPass`
- `MethodDecoratorPass`
- `AstLinkerPass`
- `ContainsEdgePass`
- `TypeRefPass`
- `TypeEvalPass`

建议在本项目中重写为：

- `Passes/BuildMetadataPass.cs`
- `Passes/BuildFileNodesPass.cs`
- `Passes/BuildNamespaceNodesPass.cs`
- `Passes/BuildTypeStubPass.cs`
- `Passes/BuildMethodStubPass.cs`
- `Passes/LinkAstPass.cs`
- `Passes/BuildContainsEdgesPass.cs`
- `Passes/ResolveTypeRefsPass.cs`
- `Passes/EvaluateNodeTypesPass.cs`

这些 Pass 的价值是：

- 让前端构建流程分步骤收敛
- 降低一个超大 Builder 的复杂度
- 为后续扩展 CFG、调用图、数据流留出边界

#### 作用域和符号辅助结构

Joern 对照参考：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\Scope.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\VariableScopeManager.scala`

建议在本项目中视情况重写为：

- `Build/Scopes/ScopeStack.cs`
- `Build/Scopes/SymbolBindingTable.cs`

说明：

- 不需要完全复制 Joern 的 Scala 结构
- 但需要保留“局部变量绑定”“作用域切换”“符号引用归一化”这类能力
- 由于本项目使用 Roslyn，很多语义能力可以直接依赖 `SemanticModel`

---

### 4.3 可选重写

这部分属于第二阶段能力，不建议作为第一批必做。

#### 调用图

Joern 对照参考目录：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph`

建议在本项目中第二阶段重写为：

- `Passes/BuildCallGraphPass.cs`

价值：

- 支持跨方法关系分析
- 为后续传播、标记、决策打基础

#### 控制流

Joern 对照参考目录：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow`

建议在本项目中第二阶段重写为：

- `Passes/BuildCfgPass.cs`

价值：

- 为语句级顺序与路径分析提供基础

#### 数据流

Joern 对照参考：

- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\layers\dataflows\OssDataFlow.scala:18`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\layers\dataflows\OssDataFlow.scala:24`

建议在本项目中第三阶段再考虑重写为：

- `Passes/BuildDdgPass.cs`
- `Passes/BuildPdgPass.cs`

说明：

- 这部分复杂度高
- 当前项目如果只是先生成内存态 `CPG`，不应优先投入

---

## 4.4 旧实现与新实现的文件映射关系

本节直接参考 `C:\Users\shan\Downloads\joern-master` 当前源码结构，给出 `Joern/Scala` 到本项目 `C#/Roslyn` 的建议映射。

说明：

- 这里的“旧实现”指 `joern-master` 中的现有 Scala 文件
- 这里的“新实现”指本项目建议创建的 C# 文件
- 映射关系以“职责对应”为准，不要求一比一机械复制

### A. 前端入口映射

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala`
  - -> `Frontend/RoslynCpgFrontend.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\Main.scala`
  - -> `Frontend/RoslynCpgCli.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\X2Cpg.scala`
  - -> `Frontend/Abstractions/CpgFrontendBase.cs`
  - -> `Frontend/Abstractions/CpgFrontendOptions.cs`

建议：

- 只保留最小入口抽象
- 不必把 `X2CpgMain`、server mode、Scala CLI 参数系统整套迁移

### B. AST 构建映射

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\passes\AstCreationPass.scala`
  - -> `Build/RoslynAstToCpgBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreator.scala`
  - -> `Frontend/RoslynAstToCpgBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreatorHelper.scala`
  - -> `Build/RoslynAstBuilderHelper.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForDeclarationsCreator.scala`
  - -> `Build/DeclarationNodeBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForExpressionsCreator.scala`
  - -> `Build/ExpressionNodeBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForPrimitivesCreator.scala`
  - -> `Build/PrimitiveNodeBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForStatementsCreator.scala`
  - -> `Build/StatementNodeBuilder.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstSummaryVisitor.scala`
  - -> `Build/RoslynSummaryWalker.cs`

建议：

- `AstCreationPass` 和 `AstCreator` 在 C# 里可以合并得更紧
- 如果不需要并行 diff graph，可以先做单进程单图构建器

### C. 作用域与摘要映射

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpProgramSummary.scala`
  - -> `Build/Summaries/CSharpProgramSummary.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpScope.scala`
  - -> `Build/Scopes/CSharpScope.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\ScopeType.scala`
  - -> `Build/Scopes/ScopeKind.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\Scope.scala`
  - -> `Build/Scopes/ScopeStack.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\VariableScopeManager.scala`
  - -> `Build/Scopes/SymbolBindingTable.cs`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\ProgramSummaryCreator.scala`
  - -> `Build/Summaries/ProgramSummaryBuilder.cs`

建议：

- 本项目有 Roslyn `SemanticModel`，所以 `VariableScopeManager` 不应直接照抄
- 应该将作用域跟踪与语义查询合并设计

### D. 基础 Pass 映射

- `FileCreationPass.scala`
  - -> `Passes/BuildFileNodesPass.cs`
- `NamespaceCreator.scala`
  - -> `Passes/BuildNamespaceNodesPass.cs`
- `TypeDeclStubCreator.scala`
  - -> `Passes/BuildTypeStubPass.cs`
- `MethodStubCreator.scala`
  - -> `Passes/BuildMethodStubPass.cs`
- `ParameterIndexCompatPass.scala`
  - -> `Passes/NormalizeParameterOrderPass.cs`
- `MethodDecoratorPass.scala`
  - -> `Passes/DecorateMethodNodesPass.cs`
- `AstLinkerPass.scala`
  - -> `Passes/LinkAstPass.cs`
- `ContainsEdgePass.scala`
  - -> `Passes/BuildContainsEdgesPass.cs`
- `TypeRefPass.scala`
  - -> `Passes/ResolveTypeRefsPass.cs`
- `TypeEvalPass.scala`
  - -> `Passes/EvaluateNodeTypesPass.cs`

建议：

- `ParameterIndexCompatPass` 这种兼容型逻辑，在新实现中可以直接消化进标准建模，不一定单独保留
- `MethodDecoratorPass` 可以按你当前需求裁剪，不需要把 Joern 的装饰逻辑全部搬过来

### E. 调用图映射

- `NaiveCallLinker.scala`
  - -> `Passes/BuildNaiveCallGraphPass.cs`
- `StaticCallLinker.scala`
  - -> `Passes/BuildStaticCallGraphPass.cs`
- `DynamicCallLinker.scala`
  - -> `Passes/BuildDynamicCallGraphPass.cs`
- `MethodRefLinker.scala`
  - -> `Passes/BuildMethodReferencePass.cs`

建议：

- 第一版只实现 `BuildNaiveCallGraphPass.cs` 或 `BuildStaticCallGraphPass.cs`
- `BuildDynamicCallGraphPass.cs` 先预留命名，不急着落代码

### F. 控制流映射

- `CfgCreationPass.scala`
  - -> `Passes/BuildCfgPass.cs`
- `cfgcreation/Cfg.scala`
  - -> `Passes/ControlFlow/CfgModel.cs`
- `cfgcreation/CfgCreator.scala`
  - -> `Passes/ControlFlow/CfgBuilder.cs`
- `cfgdominator/*`
  - -> `Passes/ControlFlow/Dominance/*`
- `codepencegraph/CdgPass.scala`
  - -> `Passes/ControlFlow/BuildCdgPass.cs`

建议：

- 第二阶段先只做 `CfgCreationPass.scala -> BuildCfgPass.cs`
- `Dominator` 和 `CDG` 不属于当前必须范围

### G. 类型关系映射

- `TypeHierarchyPass.scala`
  - -> `Passes/BuildTypeHierarchyPass.cs`
- `AliasLinkerPass.scala`
  - -> `Passes/BuildAliasRelationPass.cs`
- `FieldAccessLinkerPass.scala`
  - -> `Passes/BuildFieldAccessRelationPass.cs`

建议：

- 当前最重要的是 `BuildTypeHierarchyPass.cs`
- `Alias` 和 `FieldAccess` 是否独立成 pass，可以根据 Roslyn 实现复杂度再定

### H. 数据流映射

- `dataflowengineoss/layers/dataflows/OssDataFlow.scala`
  - -> `Passes/DataFlow/BuildOssDataFlowPass.cs`
- `passes/reachingdef/ReachingDefPass.scala`
  - -> `Passes/DataFlow/BuildReachingDefinitionsPass.cs`
- `passes/reachingdef/DdgGenerator.scala`
  - -> `Passes/DataFlow/BuildDdgPass.cs`

建议：

- 当前阶段不要实现
- 只保留命名参考与未来目录占位

---

## 4.5 旧命名与新命名的映射关系

本节不再只说文件职责，而是直接约束“旧实现名字”在新实现中怎么落。

核心原则：

- **保留 Joern 的能力分层**
- **用更贴近 C# / Roslyn 的命名**
- **避免把 Scala/Joern 的历史兼容命名原样带进新实现**

### A. 前端与框架命名

- `CSharpSrc2Cpg`
  - -> `RoslynCpgFrontend`
- `X2CpgFrontend`
  - -> `CpgFrontendBase`
- `Config`
  - -> `CpgFrontendOptions`
- `Main`
  - -> `RoslynCpgCli`

说明：

- `Src2Cpg` 是 Joern 生态里的传统命名
- 你这里直接用 `RoslynCpg*` 可读性更高

### B. Builder 命名

- `AstCreator`
  - -> `RoslynAstToCpgBuilder`
- `AstCreatorHelper`
  - -> `RoslynAstBuilderHelper`
- `AstForDeclarationsCreator`
  - -> `DeclarationNodeBuilder`
- `AstForExpressionsCreator`
  - -> `ExpressionNodeBuilder`
- `AstForPrimitivesCreator`
  - -> `PrimitiveNodeBuilder`
- `AstForStatementsCreator`
  - -> `StatementNodeBuilder`

说明：

- 新命名建议统一用 `*Builder`
- 这样比 `Creator` 更符合你后面还要有 `Pass` 的分层

### C. Pass 命名

- `FileCreationPass`
  - -> `BuildFileNodesPass`
- `NamespaceCreator`
  - -> `BuildNamespaceNodesPass`
- `TypeDeclStubCreator`
  - -> `BuildTypeStubPass`
- `MethodStubCreator`
  - -> `BuildMethodStubPass`
- `MethodDecoratorPass`
  - -> `DecorateMethodNodesPass`
- `AstLinkerPass`
  - -> `LinkAstPass`
- `ContainsEdgePass`
  - -> `BuildContainsEdgesPass`
- `TypeRefPass`
  - -> `ResolveTypeRefsPass`
- `TypeEvalPass`
  - -> `EvaluateNodeTypesPass`

说明：

- 新实现建议统一：
  - 构建类动作用 `Build*`
  - 解析类动作用 `Resolve*`
  - 补充类动作用 `Decorate*`
  - 链接类动作用 `Link*`

### D. 调用图命名

- `NaiveCallLinker`
  - -> `BuildNaiveCallGraphPass`
- `StaticCallLinker`
  - -> `BuildStaticCallGraphPass`
- `DynamicCallLinker`
  - -> `BuildDynamicCallGraphPass`
- `MethodRefLinker`
  - -> `BuildMethodReferencePass`

说明：

- Joern 的 `Linker` 命名强调“加边”
- 你这里统一成 `Build*Pass` 会更整齐

### E. 控制流命名

- `CfgCreationPass`
  - -> `BuildCfgPass`
- `CfgCreator`
  - -> `CfgBuilder`
- `Cfg`
  - -> `CfgModel`
- `CdgPass`
  - -> `BuildCdgPass`

说明：

- `CreationPass` 在新实现中可统一收敛为 `Build*Pass`

### F. 类型关系命名

- `TypeHierarchyPass`
  - -> `BuildTypeHierarchyPass`
- `AliasLinkerPass`
  - -> `BuildAliasRelationPass`
- `FieldAccessLinkerPass`
  - -> `BuildFieldAccessRelationPass`

### G. 作用域与绑定命名

- `CSharpScope`
  - -> `CSharpScope`
- `ScopeType`
  - -> `ScopeKind`
- `VariableScopeManager`
  - -> `SymbolBindingTable`
- `ProgramSummaryCreator`
  - -> `ProgramSummaryBuilder`

说明：

- `ScopeType` 改成 `ScopeKind` 更自然
- `VariableScopeManager` 太偏实现细节，改成 `SymbolBindingTable` 更贴近职责

### H. 底层图模型命名

Joern 里很多底层名字直接来自 schema 生成结果，本项目建议显式定义：

- `Cpg`
  - -> `CpgGraph`
- `StoredNode`
  - -> `CpgNode`
- `EdgeTypes`
  - -> `CpgEdgeKind`
- `NodeTypes`
  - -> `CpgNodeKind`
- `DiffGraphBuilder`
  - -> `CpgGraphBuilder`

说明：

- 新实现不建议继续暴露 `StoredNode` 这类生成代码风格命名
- 应直接使用更稳定、面向自研模型的名字

---

## 4.6 阶段一、阶段二的变量与属性命名映射关系

本节只覆盖阶段一、阶段二真正会用到的核心属性，不展开到数据流阶段。

命名原则如下：

- **Joern 语义不改**
- **C# 属性风格改成 PascalCase**
- **容易误解的名字在新实现里补充语义前缀**

### A. 通用节点属性

- `name`
  - -> `Name`
- `fullName`
  - -> `FullName`
- `code`
  - -> `Code`
- `filename`
  - -> `FileName`
- `order`
  - -> `Order`
- `lineNumber`
  - -> `LineNumber`
- `columnNumber`
  - -> `ColumnNumber`

说明：

- 这些是最基础的结构属性
- 阶段一建节点时就应尽量补齐

### B. 类型相关属性

- `typeFullName`
  - -> `TypeFullName`
- `typeDeclFullName`
  - -> `TypeDeclFullName`
- `inheritsFromTypeFullName`
  - -> `InheritsFromTypeFullNames`
- `aliasTypeFullName`
  - -> `AliasTypeFullName`

说明：

- Joern 里有些属性名看起来像单数，但实际可能是集合
- 新实现里如果本质是列表，建议直接改成复数形式，例如
  `InheritsFromTypeFullNames`

### C. 方法与签名属性

- `signature`
  - -> `Signature`
- `methodFullName`
  - -> `ResolvedMethodFullName`
- `isExternal`
  - -> `IsExternal`
- `dispatchType`
  - -> `DispatchType`

说明：

- `methodFullName` 在调用节点上容易和方法声明节点自己的 `FullName` 混淆
- 新实现建议显式改成 `ResolvedMethodFullName`

### D. 参数与局部变量属性

- `argumentIndex`
  - -> `ArgumentIndex`
- `index`
  - -> `Index`
- `evaluationStrategy`
  - -> `EvaluationStrategy`

说明：

- 参数索引、参数位置、求值策略都属于阶段二可能会逐步补齐的语义属性

### E. 结构挂载属性

- `astParentType`
  - -> `AstParentKind`
- `astParentFullName`
  - -> `AstParentFullName`

说明：

- `Type` 在 C# 里容易和系统类型概念撞名
- 所以 `astParentType` 建议改成 `AstParentKind`

### F. 当前代码实现已落地的核心属性名

当前已经在代码里直接使用或预留的核心属性包括：

- `Language`
- `Frontend`
- `InputPath`
- `Name`
- `FullName`
- `TypeFullName`
- `TypeDeclFullName`
- `ResolvedMethodFullName`
- `InheritsFromTypeFullNames`

这些属性对应的代码落点主要在：

- `src/Analysis/Core/CpgNode.cs`
- `src/Analysis/Passes/BuildMetadataPass.cs`
- `src/Analysis/Passes/ResolveTypeRefsPass.cs`
- `src/Analysis/Passes/EvaluateNodeTypesPass.cs`
- `src/Analysis/Passes/BuildNaiveCallGraphPass.cs`
- `src/Analysis/Passes/BuildTypeHierarchyPass.cs`

---

## 4.7 当前实现与 Joern 阶段一、二核心结构的接近度说明

你要求“与原来的 `joern-master` 的 CPG 实现差距大于 10% 就继续实现”，
这件事如果没有先定义“差距”的衡量口径，就会变成不可验证的口号。

因此当前文档采用一个**可执行的代理口径**：

- 只比较**阶段一、阶段二的核心结构**
- 不比较 Joern 的外围平台能力
- 不比较多语言前端
- 不比较 Query/Console/DSL
- 不比较数据流阶段

当前对齐的核心维度包括：

- 核心图模型
- 前端抽象
- 目标标识模型
- 文件节点 pass
- 命名空间节点 pass
- 类型桩 pass
- 方法桩 pass
- AST 链接 pass
- CONTAINS pass
- 元数据 pass
- 类型引用 pass
- 求值类型 pass
- 朴素调用图 pass
- 类型层级 pass
- 最小 CFG pass

已实现代码位置：

- `src/Analysis/Analysis.csproj`
- `src/Analysis/Core/CpgNodeKind.cs`
- `src/Analysis/Core/CpgEdgeKind.cs`
- `src/Analysis/Core/CpgNode.cs`
- `src/Analysis/Core/CpgEdge.cs`
- `src/Analysis/Core/CpgGraph.cs`
- `src/Analysis/Core/CpgGraphBuilder.cs`
- `src/Analysis/Frontend/CpgFrontendBase.cs`
- `src/Analysis/Frontend/CpgFrontendOptions.cs`
- `src/Analysis/Frontend/RoslynCpgFrontend.cs`
- `src/Analysis/Frontend/RoslynAstToCpgBuilder.cs`
- `src/Analysis/Model/TargetId.cs`
- `src/Analysis/Model/TypeId.cs`
- `src/Analysis/Model/SymbolId.cs`
- `src/Analysis/Model/OperationId.cs`
- `src/Analysis/Passes/CpgPass.cs`
- `src/Analysis/Passes/BuildMetadataPass.cs`
- `src/Analysis/Passes/BuildFileNodesPass.cs`
- `src/Analysis/Passes/BuildNamespaceNodesPass.cs`
- `src/Analysis/Passes/BuildTypeStubPass.cs`
- `src/Analysis/Passes/BuildMethodStubPass.cs`
- `src/Analysis/Passes/LinkAstPass.cs`
- `src/Analysis/Passes/BuildContainsEdgesPass.cs`
- `src/Analysis/Passes/ResolveTypeRefsPass.cs`
- `src/Analysis/Passes/EvaluateNodeTypesPass.cs`
- `src/Analysis/Passes/BuildNaiveCallGraphPass.cs`
- `src/Analysis/Passes/BuildTypeHierarchyPass.cs`
- `src/Analysis/Passes/BuildCfgPass.cs`

当前未实现、但仍属于阶段一、二核心范围的主要缺口还有：

- 更完整的静态调用图求精
- 更接近 Joern 的完整 CFG 构建算法
- 更完整的声明与表达式覆盖面

以“阶段一、二核心单元”作为代理口径，当前统计方式如下：

- 已对齐单元：
  - 图模型
  - 前端抽象
  - 目标标识模型
  - 文件/命名空间/类型/方法基础 pass
  - AST/CONTAINS
  - 元数据
  - 类型引用
  - 求值类型
  - 朴素调用图
  - 类型层级
  - 最小 CFG
- 仍未完全对齐单元：
  - 更完整静态调用图
  - 更完整 CFG 构造
  - 更完整声明/表达式覆盖

按这个代理口径，当前更接近于：

- **阶段一核心已基本对齐**
- **阶段二核心骨架已超过 90% 的结构覆盖**
- **剩余差距主要在“算法深度”和“覆盖面补齐”，而不是“核心代码缺位”**

### 4.8 2026-04-14 当前阶段二已落地能力

下面这一节只记录已经在 `src/Analysis` 中落地并通过测试的能力，不再写“计划中”。

#### 已落地的 Roslyn 接入

- 已直接接入真实 Roslyn `Compilation` 和 `SemanticModel`
- 已有文件：
  - `src/Analysis/Frontend/RoslynProjectLoader.cs`
  - `src/Analysis/Frontend/RoslynCompilationContext.cs`
  - `src/Analysis/Frontend/RoslynCpgFrontend.cs`

这意味着当前实现已经不是“只读语法树”的空壳前端，而是基于真实语义信息建图。

#### 已落地的 Builder 拆分

- `src/Analysis/Frontend/Builders/BuilderState.cs`
- `src/Analysis/Frontend/Builders/PrimitiveBuilder.cs`
- `src/Analysis/Frontend/Builders/DeclarationBuilder.cs`
- `src/Analysis/Frontend/Builders/ExpressionBuilder.cs`
- `src/Analysis/Frontend/Builders/StatementBuilder.cs`

这一层已经对应到 Joern 的：

- `AstForDeclarationsCreator`
- `AstForExpressionsCreator`
- `AstForStatementsCreator`
- `AstForPrimitivesCreator`

#### 已落地的声明建模

当前已经稳定支持：

- 类型声明
- 字段
- 属性
- 构造函数
- 属性访问器
- 普通方法
- lambda 合成方法
- local function
- enum 与 enum member
- record 主构造参数生成的成员
- 匿名对象对应的合成 `TYPE_DECL` 与 `MEMBER`

对应测试覆盖见：

- `tests/Analysis.Tests/Frontend/StageTwoIntegrationTests.cs`

#### 已落地的表达式建模

当前已经稳定支持：

- `IDENTIFIER`
- `LITERAL`
- `CALL`
- 对象创建
- 方法调用
- 二元与赋值表达式
- `await`
- 三元表达式
- 强制转换
- 下标访问
- 条件访问 `?.`
- 插值字符串
- 数组初始化
- 集合初始化
- `this`
- lambda / method group `METHOD_REF`
- 匿名对象创建

#### 已落地的语句与控制流建模

当前已经稳定支持：

- 局部变量声明
- 表达式语句
- `block`
- `if`
- `return`
- `while`
- `do / while`
- `for`
- `foreach`
- `switch`
- `try / catch / finally`
- `throw`
- `using`

其中 `using` 当前采用和 Joern 接近的降级策略：

- 折算为 `TRY`
- 追加 `FINALLY`
- 在 `FINALLY` 中生成合成 `Dispose` 调用

#### 当前仍建议优先继续补的阶段二缺口

下一批最值得继续实现的核心项是：

- 更完整静态调用图
  - 接口调用
  - 虚方法调用
  - delegate 调用
- 更完整 CFG
  - 方法入口/出口
  - `&&` / `||` 短路
  - 三元表达式路径
  - `switch` 更细粒度跳转
- 更完整 C# 声明覆盖
  - `delegate`
  - `event`
  - `indexer`
  - `operator`
  - `conversion operator`

一句话结论：

**现在已经可以继续写阶段二核心代码，而且应该继续优先补“调用图求精 + CFG 求精 + 关键声明缺口”。**

后续如果继续逼近“10% 以内”的目标，应优先继续实现上面这些未完成的核心 pass，
而不是去写 CLI、导出器、工具脚本等杂项代码。

---

## 5. 哪些文件不建议迁移

以下内容可以参考思想，但不应作为本项目的迁移目标：

- `console`
- `querydb`
- 多语言前端
- `semanticcpg` 中大量 DSL/traversal 扩展
- 各类 dot 导出器
- CLI 服务器模式
- Query 交互体验层

原因如下：

- 它们不是“最小 CPG”所必需
- 它们会把项目目标从“图构建”拉偏到“平台建设”
- 它们会大幅提高工作量与设计复杂度

---

## 6. 建议的新 C# 目录结构

建议本项目中的最小目录结构如下：

- `Core`
  - `CpgGraph.cs`
  - `CpgNode.cs`
  - `CpgEdge.cs`
  - `CpgNodeKind.cs`
  - `CpgEdgeKind.cs`
- `Frontend`
  - `RoslynCpgFrontend.cs`
  - `RoslynProjectLoader.cs`
- `Build`
  - `RoslynAstToCpgBuilder.cs`
  - `DeclarationNodeBuilder.cs`
  - `ExpressionNodeBuilder.cs`
  - `StatementNodeBuilder.cs`
  - `Scopes/`
- `Passes`
  - `BuildMetadataPass.cs`
  - `BuildFileNodesPass.cs`
  - `BuildNamespaceNodesPass.cs`
  - `BuildTypeStubPass.cs`
  - `BuildMethodStubPass.cs`
  - `LinkAstPass.cs`
  - `BuildContainsEdgesPass.cs`
  - `ResolveTypeRefsPass.cs`
  - `EvaluateNodeTypesPass.cs`
  - `BuildCallGraphPass.cs`
  - `BuildCfgPass.cs`

这个结构的核心优势是：

- 和当前项目“Analysis 模块先产出内存态 CPG”的定位一致
- 后续如果将 `CPG` 从 `Analysis` 拆出去，也只是目录移动
- 可以逐步引入调用图、控制流、数据流，而不需要推翻整体设计

---

## 7. 推荐的最小落地顺序

推荐按以下顺序实现：

### 第一阶段

- `Core`
- `RoslynCpgFrontend`
- `RoslynAstToCpgBuilder`
- 基础 AST 节点与边

目标：

- 能稳定产出最小内存态 `CPG`

### 第二阶段

- 文件、命名空间、类型、成员、方法的基础补全 Pass
- 类型引用与类型计算
- 引用绑定
- 基础调用图
- 基础控制流

目标：

- 让图结构从“可输出”变成“可稳定使用”
- 让图从结构图进化为基础语义图

### 第二阶段的具体演化计划

参考 `Joern` 当前实现，第二阶段不再只是“把语法树翻译成图”，而是开始给图补充可查询、可推理的语义关系。

Joern 中与本阶段最相关的参考位置包括：

- 基础层入口
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\Base.scala:9`
- 调用关系
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph`
- 控制流
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow`
- 类型关系
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations`
- 前端语义补强
  - `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend`

第二阶段建议拆成以下几个子目标。

这一阶段的重点不是“再多造一些节点”，而是把第一阶段已经有的图补成真正可用的分析图。

第二阶段的总目标可以压缩成三句话：

- 图里的结构关系要稳定
- 图里的类型和符号要能落到真实语义对象
- 图里的调用和控制流要能支持最基本的分析

为了避免后面实现时又散掉，这里先把第二阶段的总边界写死。

第二阶段输入：

- Roslyn 语法树
- Roslyn `Compilation`
- Roslyn `SemanticModel`
- 第一阶段已经生成的基础 `CPG`

第二阶段输出：

- 结构完整的 `CPG`
- 具备基础类型语义的 `CPG`
- 具备基础调用关系的 `CPG`
- 具备基础控制流关系的 `CPG`

第二阶段明确不做：

- 数据流
- 污点传播
- 跨过程值传播
- 复杂动态分派求精
- 平台层查询 DSL

一句话说：

**第二阶段做的是“把图补到可分析”，不是“把分析结果算完”。**

### 第二阶段的总实现顺序

为了避免 pass 之间互相打架，建议顺序固定为：

1. 补齐结构节点
2. 补齐 `AST` 和 `CONTAINS`
3. 补齐类型关系
4. 补齐标识符绑定
5. 补齐静态调用图
6. 补齐基础 `CFG`

这个顺序不要轻易改。

原因：

- 没有结构节点，后面的边没地方落
- 没有 `AST` 和 `CONTAINS`，后面的范围分析会漂
- 没有类型和符号绑定，调用图很容易错
- 没有调用图和 `CFG`，第二阶段就不算完成

#### 2.1 补齐基础结构节点

目标：

- 让第一阶段只生成“必要节点”的图，进化成“结构完整度足够高”的图

建议新增或补齐：

- `FILE`
- `NAMESPACE`
- `NAMESPACE_BLOCK`
- `TYPE`
- `TYPE_DECL`
- `METHOD`
- `METHOD_PARAMETER_IN`
- `METHOD_RETURN`
- `MEMBER`
- `LOCAL`

建议新增或补齐的 C# 文件：

- `Passes/BuildFileNodesPass.cs`
- `Passes/BuildNamespaceNodesPass.cs`
- `Passes/BuildTypeNodesPass.cs`
- `Passes/BuildMethodNodesPass.cs`

这一部分主要参考 `Base.scala` 中的基础建图思路，而不是照搬所有 Scala 细节。

建议这一层直接统一几个最基本字段：

- `Id`
- `Kind`
- `Name`
- `FullName`
- `Code`
- `Order`
- `FilePath`
- `Line`
- `Column`

这里不要一开始就追求把 Joern 的全部属性搬全。

先把下面这些节点属性做稳：

- `FILE`
  - `Name`
  - `FilePath`
- `NAMESPACE`
  - `Name`
  - `FullName`
- `TYPE_DECL`
  - `Name`
  - `FullName`
  - `BaseTypeFullNames`
- `METHOD`
  - `Name`
  - `FullName`
  - `Signature`
  - `ReturnTypeFullName`
- `METHOD_PARAMETER_IN`
  - `Name`
  - `Index`
  - `TypeFullName`
- `METHOD_RETURN`
  - `TypeFullName`
- `MEMBER`
  - `Name`
  - `TypeFullName`
- `LOCAL`
  - `Name`
  - `TypeFullName`

这一层的完成标准要写清楚：

- 每个源码文件至少能落一个 `FILE`
- 每个命名空间至少能落一个 `NAMESPACE` 或 `NAMESPACE_BLOCK`
- 每个类型声明至少能落一个 `TYPE_DECL`
- 每个方法声明至少能落一个 `METHOD`
- 每个参数、返回值、字段、局部变量都能落到基本节点

如果这几个点还不稳，就不要急着往下做。

#### 2.2 建立 AST 补链和包含关系

目标：

- 让节点之间不只是“被创建了”，而是有稳定的结构关系

需要补齐的关系：

- `AST`
- `CONTAINS`

建议新增或补齐的 C# 文件：

- `Passes/LinkAstPass.cs`
- `Passes/BuildContainsEdgesPass.cs`

用途：

- 支持从文件、类型、方法逐级遍历子节点
- 支持后续 CFG、调用图、数据流按结构范围工作

这里建议把两个关系分开理解，不要混着做：

- `AST`
  - 表示语法上的父子关系
  - 解决“这个节点写在谁里面”
- `CONTAINS`
  - 表示结构上的拥有关系
  - 解决“这个文件、类型、方法拥有谁”

建议这一层明确几个约束：

- `FILE` 必须能 `CONTAINS` 它的顶级节点
- `TYPE_DECL` 必须能 `CONTAINS` 它的成员和方法
- `METHOD` 必须能 `CONTAINS` 参数、返回节点、方法体关键节点
- `AST` 关系要保留顺序信息，至少能按 `Order` 还原基本结构

这一层完成后，至少要能稳定回答：

- 一个节点属于哪个文件
- 一个方法内部有哪些直接子节点
- 一个类型里有哪些成员和方法
- 一个表达式在语法树上挂在哪个父节点下

#### 2.3 建立类型语义关系

目标：

- 让表达式、符号、声明之间有稳定类型可追踪

建议补齐的关系：

- `TYPE_REF`
- 节点的 `TypeFullName`
- `TYPE_DECL -> TYPE`
- 继承关系
- 别名关系

建议新增或补齐的 C# 文件：

- `Passes/ResolveTypeRefsPass.cs`
- `Passes/EvaluateNodeTypesPass.cs`
- `Passes/BuildTypeHierarchyPass.cs`

这一部分主要参考：

- `TypeRefPass`
- `TypeEvalPass`
- `TypeHierarchyPass`

第二阶段不一定一开始就做完整泛型与复杂约束，但至少要做到：

- 局部变量能知道自己的类型
- 方法参数与返回值能知道自己的类型
- 调用表达式能得到基础结果类型

这里最关键的不是“补一条 `TYPE_REF` 边”，而是先统一类型命名。

建议直接统一为一个内部标准：

- 优先使用 Roslyn `ISymbol` 推导出的稳定 `FullName`
- 所有节点统一写入 `TypeFullName`
- 所有类型声明统一写入 `FullName`

这一步必须解决的实际问题有：

- 内建类型怎么统一命名
- 数组、泛型、可空类型怎么写成稳定字符串
- 嵌套类型怎么拼接全名
- 命名空间缺失时如何兜底

建议第一版先采用“能稳定比较”的命名规则，而不是追求完全复刻 Joern 的历史字符串格式。

这一层的最低完成标准：

- `LOCAL.TypeFullName` 可用
- `METHOD_PARAMETER_IN.TypeFullName` 可用
- `METHOD_RETURN.TypeFullName` 可用
- `IDENTIFIER.TypeFullName` 可用
- `CALL.TypeFullName` 可用
- `TYPE_DECL` 之间的基础继承关系可用

如果这些字段还经常空，就说明第二阶段还没站稳。

#### 2.4 建立引用绑定关系

目标：

- 让 `IDENTIFIER` 不只是一个名字，而是能绑定到声明实体

建议补齐的关系：

- `IDENTIFIER -> LOCAL`
- `IDENTIFIER -> MEMBER`
- `IDENTIFIER -> PARAMETER`

建议新增或补齐的 C# 文件：

- `Passes/BindIdentifierReferencePass.cs`
- `Build/Scopes/ScopeStack.cs`
- `Build/Scopes/SymbolBindingTable.cs`

注意：

- 这一块可以大量借助 Roslyn 的 `SemanticModel`
- 不建议机械复制 `Joern` 的变量作用域实现

这一层完成后，图才真正具备“名字解析后的语义可用性”。

这一层建议把“引用绑定”定义得更明确一些：

- `IDENTIFIER` 解析到局部变量时，绑定到 `LOCAL`
- `IDENTIFIER` 解析到参数时，绑定到 `METHOD_PARAMETER_IN`
- `IDENTIFIER` 解析到字段或属性时，绑定到 `MEMBER`
- `TYPE_REF` 解析到类型声明时，绑定到对应 `TYPE_DECL` 或 `TYPE`

建议这层新增一个明确的边种类，不要只靠属性硬记：

- `REF`

如果你暂时不想新增边种类，也至少要统一一个属性：

- `TargetNodeId`

但从后续可读性来看，单独加 `REF` 边更合适。

这一层最重要的不是手写作用域栈，而是把 Roslyn 已经算好的结果稳定落图。

也就是说：

- 优先用 `SemanticModel.GetSymbolInfo`
- 其次用 `GetDeclaredSymbol`
- 最后才自己补局部作用域兜底

这一层的完成标准：

- 方法体里的局部变量引用能回指到 `LOCAL`
- 参数引用能回指到 `METHOD_PARAMETER_IN`
- 成员访问能回指到 `MEMBER`
- 类型引用能回指到 `TYPE_DECL` 或 `TYPE`

#### 2.5 建立基础调用图

目标：

- 让 `CALL` 节点能解析到目标方法

建议补齐的关系：

- `CALL -> METHOD`
- `CALL -> METHOD_REF`

建议新增或补齐的 C# 文件：

- `Passes/BuildCallGraphPass.cs`

Joern 在这一层有：

- `NaiveCallLinker`
- `StaticCallLinker`
- `DynamicCallLinker`

本项目第二阶段不建议一开始就做复杂动态调用，建议按以下顺序演化：

- 先支持静态可解析调用
- 再支持扩展方法、接口分派、虚方法覆盖的基础近似
- 最后再考虑更复杂的动态分派

这一层建议把目标再收紧一点，避免“调用图”三个字一写就失控。

第二阶段里，调用图只要求做到：

- 已解析 `CALL` 能连到真实目标 `METHOD`
- 连不到时，也要留下稳定的 `MethodFullName`
- 能区分静态调用和实例调用

建议统一写入这些字段：

- `CALL.Name`
- `CALL.MethodFullName`
- `CALL.Signature`
- `CALL.DispatchType`
- `CALL.TypeFullName`

建议最先支持的调用种类：

- 普通方法调用
- 构造函数调用
- 静态成员调用
- 实例成员调用
- 扩展方法调用

建议先不碰的种类：

- 反射调用
- `dynamic`
- 表达式树编译后的调用
- 委托间接调用的精细求精

这一层的完成标准：

- 对于可静态解析的调用，大部分 `CALL` 都能拿到稳定 `MethodFullName`
- `BuildStaticCallGraphPass.cs` 能把可解析 `CALL` 连接到 `METHOD`
- 至少能支持“方法被谁调用”的基础反查

#### 2.6 建立基础控制流

目标：

- 让方法体内部具备基本执行顺序关系

建议补齐的关系：

- `CFG`

建议新增或补齐的 C# 文件：

- `Passes/BuildCfgPass.cs`

优先覆盖的结构：

- 顺序语句
- `if`
- `for`
- `foreach`
- `while`
- `return`
- `break`
- `continue`
- `try/catch/finally`

这一层的收益是：

- 为第三阶段的数据流和规则分析打基础
- 为“路径相关判断”提供最低限度支持

这里要特别说明一点：

- 当前项目已经决定先去掉阶段三
- 但这不代表第二阶段里的 `CFG` 可以做得很弱

原因很简单：

- 分析模块当前只产出 `CPG`
- 那么 `CFG` 本身就是产出质量的一部分

建议这里直接采用“中间模型 + 结构化拼接”的写法，而不是简单顺序连边。

建议新增或明确的文件：

- `Passes/ControlFlow/CfgModel.cs`
- `Passes/ControlFlow/CfgBuilder.cs`
- `Passes/BuildCfgPass.cs`

建议 `CfgModel` 至少包含：

- `EntryNodeId`
- `ExitNodeIds`
- `Edges`
- `PendingBreaks`
- `PendingContinues`
- `PendingReturns`

这样设计的原因很直接：

- `if` 需要合并多个出口
- 循环需要回边
- `break` 和 `continue` 需要延迟接边
- `return` 需要单独汇总到方法出口

建议第一批支持的结构：

- 语句块
- 表达式语句
- 局部变量声明
- `if / else`
- `while`
- `for`
- `foreach`
- `return`
- `break`
- `continue`

建议第二批再补：

- `switch`
- `try / catch / finally`
- `using`
- `lock`

这一层的完成标准：

- 方法体中的顺序语句能形成稳定 `CFG`
- `if / else` 两条分支都能回到后继节点
- 循环语句能形成回边
- `return` 能正确终止当前路径
- `break` 和 `continue` 不会错误地连到普通后继

#### 2.7 第二阶段完成后的状态

第二阶段完成后，图不再只是“结构图”，而会变成“基础语义图”。

此时图应当能够回答以下问题：

- 一个 `Identifier` 对应哪个声明对象
- 一个 `Call` 可能调用哪个方法
- 一个节点的类型是什么
- 一个类型继承自谁
- 一个方法内部的基本执行顺序是什么

这时 `Analysis` 模块的真实角色会从：

- 第一阶段的 `CPG Builder`

演化为：

- 第二阶段的 `语义增强型 CPG Builder`

这里再补一个更直接的验收口径。

如果第二阶段真的完成，`Analysis` 模块至少应当支持下面这些最小问题：

- 给一个局部变量引用，能找到它对应的声明
- 给一个方法调用，能找到它最可能的目标方法
- 给一个表达式，能拿到它的结果类型
- 给一个类型声明，能拿到它的基类或接口
- 给一个方法体，能走出基本执行路径

如果这五个问题里有两个以上经常答不上来，就说明第二阶段还没完成。

### 第二阶段建议新增的文件清单

为了让实现阶段更容易落地，这里把第二阶段应该出现的核心文件再收一次。

必做文件：

- `src/Analysis/Frontend/RoslynProjectLoader.cs`
- `src/Analysis/Frontend/RoslynCompilationContext.cs`
- `src/Analysis/Passes/BuildFileNodesPass.cs`
- `src/Analysis/Passes/BuildNamespaceNodesPass.cs`
- `src/Analysis/Passes/BuildTypeStubPass.cs`
- `src/Analysis/Passes/BuildMethodStubPass.cs`
- `src/Analysis/Passes/LinkAstPass.cs`
- `src/Analysis/Passes/BuildContainsEdgesPass.cs`
- `src/Analysis/Passes/ResolveTypeRefsPass.cs`
- `src/Analysis/Passes/EvaluateNodeTypesPass.cs`
- `src/Analysis/Passes/BuildTypeHierarchyPass.cs`
- `src/Analysis/Passes/BindIdentifierReferencePass.cs`
- `src/Analysis/Passes/BuildStaticCallGraphPass.cs`
- `src/Analysis/Passes/BuildMethodReferencePass.cs`
- `src/Analysis/Passes/ControlFlow/CfgModel.cs`
- `src/Analysis/Passes/ControlFlow/CfgBuilder.cs`
- `src/Analysis/Passes/BuildCfgPass.cs`

可后移文件：

- `src/Analysis/Passes/BuildNaiveCallGraphPass.cs`
- `src/Analysis/Passes/BuildDynamicCallGraphPass.cs`

### 第二阶段的完成定义

最后把“完成”定义清楚，避免后面又出现“差不多写完了”的情况。

第二阶段完成，不是指：

- pass 名字都建好了
- 节点和边种类都枚举了
- build 能过了

第二阶段完成，指的是：

- 真实 Roslyn 语义上下文已经接入
- 结构关系已经稳定
- 类型关系已经可查询
- 标识符绑定已经可查询
- 静态调用图已经可查询
- 基础 `CFG` 已经可查询

一句话结论：

**第二阶段不是补样子，而是补可查询的语义关系。**

### 第二阶段的编码推进顺序

上面已经把目标和文件列出来了，下面直接把“怎么开工”写死。

建议第二阶段拆成三个小阶段推进：

- `2-1` 先接真实语义上下文
- `2-2` 再补类型、绑定、调用
- `2-3` 最后补结构化 `CFG`

这样拆的原因很简单：

- 没有真实语义上下文，后面的关系都不稳
- 没有类型和绑定，调用图很容易做假
- 没有前两步，`CFG` 即使做了也只是孤立结构

#### 阶段 2-1：接入真实 Roslyn 语义上下文

这一段的目标只有一个：

- 让图构建过程能稳定拿到 `Compilation` 和 `SemanticModel`

建议优先实现的文件：

- `src/Analysis/Frontend/RoslynProjectLoader.cs`
- `src/Analysis/Frontend/RoslynCompilationContext.cs`
- `src/Analysis/Frontend/RoslynCpgFrontend.cs`

建议先补的能力：

- 从 `csproj` 加载 Roslyn `Project`
- 从 `Project` 生成 `Compilation`
- 为每个 `SyntaxTree` 缓存 `SemanticModel`
- 给 Builder 和 Pass 提供统一访问入口

这一段建议先暴露这几个方法：

- `LoadProjectAsync`
- `CreateCompilationAsync`
- `GetSemanticModel`
- `GetDeclaredSymbol`
- `GetSymbolInfo`
- `GetTypeInfo`

这一段的完成标准：

- 前端不再只接源码文件列表
- 前端可以稳定跑到 `Compilation`
- 任意语法树节点都能拿到对应 `SemanticModel`
- 后续 pass 不需要自己重复创建语义对象

如果这一段没完成，后面都先别动。

#### 阶段 2-2：补齐类型、引用绑定和静态调用图

这一段的目标是把“语法节点”提升成“有语义的节点”。

建议优先实现的文件：

- `src/Analysis/Passes/ResolveTypeRefsPass.cs`
- `src/Analysis/Passes/EvaluateNodeTypesPass.cs`
- `src/Analysis/Passes/BuildTypeHierarchyPass.cs`
- `src/Analysis/Passes/BindIdentifierReferencePass.cs`
- `src/Analysis/Passes/BuildStaticCallGraphPass.cs`
- `src/Analysis/Passes/BuildMethodReferencePass.cs`

建议编码顺序固定为：

1. `ResolveTypeRefsPass.cs`
2. `EvaluateNodeTypesPass.cs`
3. `BuildTypeHierarchyPass.cs`
4. `BindIdentifierReferencePass.cs`
5. `BuildMethodReferencePass.cs`
6. `BuildStaticCallGraphPass.cs`

这个顺序不要倒过来。

原因：

- 类型名不稳定，调用目标就不稳定
- 符号没绑定，`CALL` 很多时候只能拿到表面名字
- `METHOD_REF` 没补，后续很多关系不好统一

这一段每个 pass 的最低职责如下。

`ResolveTypeRefsPass.cs`

- 把类型引用节点和类型声明节点连接起来
- 统一 `TypeFullName` 的写法
- 解决内建类型、数组、泛型、可空类型的命名收敛

`EvaluateNodeTypesPass.cs`

- 为 `LOCAL`
- `METHOD_PARAMETER_IN`
- `METHOD_RETURN`
- `IDENTIFIER`
- `CALL`
- `LITERAL`
  写入稳定的 `TypeFullName`

`BuildTypeHierarchyPass.cs`

- 为 `TYPE_DECL` 建立继承和接口实现关系
- 至少处理单继承和基础接口列表

`BindIdentifierReferencePass.cs`

- 把标识符解析到真实声明对象
- 优先绑定到 `LOCAL`
- 其次绑定到 `METHOD_PARAMETER_IN`
- 再其次绑定到 `MEMBER`

`BuildMethodReferencePass.cs`

- 为方法组、委托目标、显式方法引用补齐目标关系
- 给后续调用图提供稳定中间结果

`BuildStaticCallGraphPass.cs`

- 读取 `CALL.MethodFullName`
- 精确连接到 `METHOD.FullName`
- 对无法解析的调用保留未解析状态，而不是乱连边

这一段的完成标准：

- 常见变量和参数引用可回指
- 常见调用点可解析到方法
- 常见类型能拿到稳定全名
- 至少能做“谁调用了谁”的基础查询

#### 阶段 2-3：补齐结构化 `CFG`

这一段的目标是：

- 让 `CPG` 里真正有可走的过程内路径

建议优先实现的文件：

- `src/Analysis/Passes/ControlFlow/CfgModel.cs`
- `src/Analysis/Passes/ControlFlow/CfgBuilder.cs`
- `src/Analysis/Passes/BuildCfgPass.cs`

建议编码顺序固定为：

1. `CfgModel.cs`
2. `CfgBuilder.cs`
3. `BuildCfgPass.cs`

建议 `CfgModel.cs` 先只保留最小结构：

- `EntryNodeId`
- `ExitNodeIds`
- `Edges`
- `PendingBreaks`
- `PendingContinues`
- `PendingReturns`

建议 `CfgBuilder.cs` 先按下面顺序支持语句：

1. 语句块
2. 表达式语句
3. 局部变量声明
4. `return`
5. `if / else`
6. `while`
7. `for`
8. `foreach`
9. `break`
10. `continue`

这一段不要一开始就追 `switch` 和异常流。

先把主干打通，比什么都重要。

这一段的完成标准：

- 方法体内部顺序可走通
- 分支能汇合
- 循环能回边
- `return` 能截断路径
- `break` / `continue` 不会接错

### 第二阶段每一段完成后要补什么文档

为了避免代码写出来后文档又落后，建议每一小段完成后都同步补一小节。

`2-1` 完成后补：

- 当前支持的输入形式
- `Compilation` 和 `SemanticModel` 的生命周期
- 前端和 pass 怎么拿上下文

`2-2` 完成后补：

- `TypeFullName` 命名规则
- `REF` 或 `TargetNodeId` 的绑定规则
- `MethodFullName` 的命名规则
- 已支持与未支持的调用种类

`2-3` 完成后补：

- `CFG` 节点选取规则
- 哪些语句已支持
- 哪些控制流语句还未支持

### 第二阶段的最终排期建议

如果现在按“先能用，再补深度”的思路排，建议就是：

- 第一周：做完 `2-1`
- 第二周：做完 `2-2`
- 第三周：做完 `2-3`

如果中间发现范围过大，也不要把三个方向一起拖慢。

正确做法是：

- 优先保住 `2-1`
- 然后保住 `2-2` 里的类型和静态调用
- 最后再扩 `CFG`

因为对当前分析模块来说，最有价值的顺序永远是：

- 真实语义
- 真实调用
- 基础路径

一句话结论：

**第二阶段现在已经可以直接开写，最稳的推进顺序就是 `2-1 -> 2-2 -> 2-3`。**

---

## 8. 最终建议

当前阶段最合理的策略是：

- 参考 `Joern` 的 C# 前端与 `x2cpg` 基础分层
- 只迁移“最小 CPG 构建能力”
- 使用 Roslyn 替代 Joern 现有的 AST 生成链路
- 自己定义 C# 版 `CPG Graph`
- 暂时不要迁移 DSL、QueryDB、Console 与大规模分析引擎

一句话结论：

**本项目应重写 `csharpsrc2cpg + x2cpg` 的最小核心子集，而不是重写整个 Joern。**

---

## 10. 截至当前实现与 Joern 的剩余差距

本节只对照 `C:\Users\shan\Downloads\joern-master` 当前 C# 前端和 `x2cpg` 控制流、调用图实现，直接列还缺什么。

### 10.1 已经补到位的部分

- Builder 分层已经拆成：
  - `PrimitiveBuilder`
  - `ExpressionBuilder`
  - `StatementBuilder`
  - `DeclarationBuilder`
- 真实 Roslyn `Compilation` / `SemanticModel` 已接入
- 已支持的声明建模：
  - 类型
  - 字段
  - 属性
  - 构造函数
  - 属性访问器
  - 普通方法
- 已支持的表达式建模：
  - 标识符
  - 字面量
  - 调用
  - 对象创建
  - 二元表达式
  - 赋值表达式
  - 属性 getter 调用
  - 属性 setter 调用
  - `await`
  - 条件表达式
  - 强制转换
  - 下标访问
  - 条件访问 `?.`
  - 插值字符串
  - 数组 / 集合初始化
  - `this`
  - 一元表达式
  - `lambda`
  - 方法组引用
- 已支持的控制结构：
  - `if`
  - `for`
  - `foreach`
  - `while`
  - `switch`
  - `try / catch / finally`
  - `return`
  - `break`
  - `continue`
  - `throw`

### 10.2 参考 Joern 之后仍然明确缺失的能力

下面这些不是“可有可无的小优化”，而是和 `joern-master` 对照后，当前实现还明显偏薄的核心点。

#### A. 表达式层还缺的能力

参考：

- `AstForExpressionsCreator.scala`

当前还缺：

- setter 复合赋值降级成 `get_` + 运算 + `set_`

其中优先级最高的是：

- setter 复合赋值降级
- 空值抑制 `!`
- 模式匹配
- 更细的字段访问建模

#### B. 方法引用和闭包建模还缺的能力

参考：

- `AstForExpressionsCreator.scala`
- `AstForDeclarationsCreator.scala`

当前虽然已经补了最小 `lambda` 和方法组引用，但还缺：

- 闭包捕获变量的显式建模
- `lambda` 的稳定父作用域命名
- `lambda` 方法体的完整 `CFG`
- 委托调用点和目标 `METHOD_REF` 的更完整关系

#### C. 调用图还缺的能力

参考：

- `StaticCallLinker.scala`
- `MethodRefLinker.scala`

当前还缺：

- 扩展方法更稳的识别
- 更复杂扩展方法接收者归一化
- 泛型约束参与的分派收敛
- 反射 / 动态类型调用
- 更复杂委托链的跨成员传播

其中优先级最高的是：

- 扩展方法
- 更复杂委托调用
- 泛型相关动态分派

当前已经补齐的能力：

- `METHOD_REF -> METHOD`
- 接口调用到实现方法的保守近似
- 多层继承虚方法覆盖的保守近似
- 显式接口实现的调用图补边
- 委托调用到方法组、字段、属性、属性 `lambda` 的联动
- 默认接口实现保持为动态分派目标
- 没有内部候选目标时，保留外部方法桩作为回退目标

#### D. 控制流还缺的能力

参考：

- `CfgCreationPass.scala`
- `cfgcreation/CfgCreator.scala`

当前还缺：

- `do / while`
- `using`
- `lock`
- `await foreach`
- 更完整的异常传播路径
- `lambda` / accessor / constructor 体的统一 `CFG`

其中优先级最高的是：

- `using`
- `do / while`
- `lambda` 方法体 `CFG`

#### E. 声明层还缺的能力

参考：

- `AstForDeclarationsCreator.scala`

当前还缺：

- 记录类型的参数成员化
- 枚举和枚举成员
- 局部函数
- 匿名对象
- 隐式字段初始化构造器补齐
- `this` 参数显式建模

其中优先级最高的是：

- 局部函数
- 枚举
- `this` 参数

### 10.3 当前最值得继续实现的顺序

如果继续对齐 `joern-master`，建议固定按这个顺序推进：

1. `using` 和 `do / while` 的 `CFG`
2. 局部函数、枚举、匿名对象
3. 扩展方法、接口 / 虚方法近似分派
4. setter 复合赋值、空值抑制、模式匹配

### 10.4 本次已直接落代码的缺口

本次不是只写文档，也直接补了下面这批：

- `lambda` 表达式最小建模
- 方法组引用最小建模
- `METHOD_REF -> METHOD` 可解析闭环
- 接口 / 虚方法 / 显式接口实现动态分派补边
- 默认接口实现目标保留
- 外部方法桩动态分派回退保留
- 属性 getter 访问降级为 `CALL`
- 属性 setter 赋值降级为 `CALL`
- `await` 降级为 `CALL`
- 条件表达式降级为 `CALL`
- 强制转换降级为 `CALL`
- 下标访问降级为 `CALL`
- 条件访问降级为 `CALL`
- 插值字符串降级为 `CALL`
- 数组 / 集合初始化降级为 `CALL`
- `this` 建模为 `IDENTIFIER`
- 一元表达式降级为 `CALL`

对应实现文件：

- `src/Analysis/Frontend/Builders/ExpressionBuilder.cs`
- `src/Analysis/Frontend/Builders/BuilderState.cs`
- `src/Analysis/Passes/BuildMethodReferencePass.cs`
- `src/Analysis/Passes/BuildStaticCallGraphPass.cs`
- `src/Analysis/Passes/BuildDynamicCallGraphPass.cs`
- `src/Analysis/Passes/BuildDelegateCallGraphPass.cs`

一句话结论：

**当前实现已经进入“继续补语义深度”的阶段，后面不该再回去堆样板文件，而是继续补 `using`、`do / while`、局部函数、枚举、匿名对象、更完整调用图和更完整 `CFG`。**

---

## 9. 下一步直接要补的三块核心能力

这一节只回答一个问题：

**基于 `C:\Users\shan\Downloads\joern-master` 当前实现，接下来最值得补什么。**

结论很直接：

- 第一块：真实接入 Roslyn `Compilation` / `SemanticModel`
- 第二块：补成更完整的静态调用图
- 第三块：把最小版 `CFG` 升级成结构化 `CFG`

这三块补上后，当前方案就不再只是“能出图”，而是开始接近“能做分析”。

### 9.1 真实接入 Roslyn `Compilation` / `SemanticModel`

先说结论：

- 这块必须做
- 而且应当尽快做

原因很简单：

- 现在图里很多语义信息还是“从语法猜”
- Joern 的 C# 前端虽然没有直接走 Roslyn `Compilation` 建图，但它前面有专门的 AST 生成链路
- 你现在既然已经决定用 Roslyn 替代那条链路，就必须把 `Compilation` 和 `SemanticModel` 真正接进来

参考位置：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\DotNetAstGenRunner.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\DotNetJsonParser.scala`

Joern 的做法是：

- 先生成外部 AST
- 再把 AST 喂给 `AstCreator`

你现在的 C# 版不需要复制这条链路，但要做到同一个结果：

- 文件能挂到真实项目上下文
- 语法节点能查到所属 `SemanticModel`
- 声明、引用、调用、类型都能查到真实符号

建议直接补以下文件：

- `src/Analysis/Frontend/RoslynProjectLoader.cs`
- `src/Analysis/Frontend/RoslynCompilationContext.cs`

这两个文件的职责建议固定为：

- `RoslynProjectLoader`
  - 加载 `sln`、`csproj` 或源码目录
  - 产出 Roslyn `Project`
  - 产出 `Compilation`
- `RoslynCompilationContext`
  - 保存 `Compilation`
  - 保存 `SyntaxTree -> SemanticModel` 映射
  - 提供统一的符号查询入口

做到这一步后，下面这些能力才会稳定：

- `TypeId` 的真实类型归一
- `SymbolId` 的真实声明绑定
- `OperationId` 的真实调用目标计算
- `IDENTIFIER`、`MEMBER_ACCESS`、`CALL` 的语义补强

一句话说：

**不接真实 `Compilation`，后面的调用图和控制流都只能停留在半成品。**

### 9.2 更完整的静态调用图

先说结论：

- 这块应该放在 Roslyn 接入之后马上做
- 它比继续堆节点数量更重要

参考位置：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\StaticCallLinker.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\MethodRefLinker.scala`

Joern 在这块的关键点不是“遍历到 `CALL` 就连边”，而是：

- 前端先把 `call.methodFullName` 算准
- 后处理再按 `method.fullName` 精确连边

所以你这里的关键不是多写一个 pass，而是先统一几个字段：

- `Method.FullName`
- `Call.MethodFullName`
- `Call.DispatchType`
- `Call.Signature`

建议新增文件：

- `src/Analysis/Passes/BuildStaticCallGraphPass.cs`
- `src/Analysis/Passes/BuildMethodReferencePass.cs`

建议最先支持的范围：

- 普通静态方法调用
- 实例方法调用
- 构造函数调用
- 属性访问器映射成方法调用
- 扩展方法的基础展开

第二批再补：

- 接口分派的保守近似
- 虚方法覆盖的保守近似

当前阶段不建议先碰：

- 完整动态分派
- 反射调用
- 表达式树调用恢复

因为这三类投入大，但对你现在的“最小可用分析层”收益不高。

这一块做完后，分析模块至少能稳定回答：

- 一个 `CALL` 最可能落到哪个 `METHOD`
- 一个 `METHOD_REF` 指向哪个方法声明
- 一个方法被哪些调用点引用

一句话说：

**调用图不是附属能力，它是分析层从“结构图”变成“关系图”的分界线。**

### 9.3 更完整的 `CFG` 算法

先说结论：

- 当前 `BuildCfgPass.cs` 只能算最小版
- 如果想让分析层真正只产出 `CPG` 和 `CPG` 快照，`CFG` 就必须做成结构化算法

参考位置：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\CfgCreationPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\CfgCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\Cfg.scala`

Joern 这里的关键不是“顺着语句列表连边”，而是：

- 先构造中间 `Cfg` 模型
- 每类语句分别计算入口、出口和悬挂边
- 再把局部结果拼成最终 `CFG`

你这里建议也走同样思路，但保留最小范围：

- 不追求第一版就全量覆盖所有语法
- 先把主干控制结构做扎实

建议新增文件：

- `src/Analysis/Passes/ControlFlow/CfgModel.cs`
- `src/Analysis/Passes/ControlFlow/CfgBuilder.cs`

建议第一批支持：

- 顺序语句块
- `if / else`
- `while`
- `for`
- `foreach`
- `return`
- `break`
- `continue`

建议第二批支持：

- `switch`
- `try / catch / finally`
- `using`
- `await foreach`

当前阶段不建议先做：

- 异常传播的精细路径
- `yield return`
- 迭代器状态机级别建模

因为这些都属于复杂控制流，适合放到后续增强。

这一块做完后，分析层会得到三个直接收益：

- 方法内部路径顺序更可信
- 后续若要做数据流，基础已经具备
- `CPG` 快照不只是结构快照，而是带执行路径信息的快照

一句话说：

**没有结构化 `CFG`，分析层产出的图很难支撑真正的路径分析。**

### 9.4 这三块的实现顺序

建议不要并行乱做，直接按下面顺序推进：

1. `RoslynProjectLoader.cs` + `RoslynCompilationContext.cs`
2. `BuildStaticCallGraphPass.cs` + `BuildMethodReferencePass.cs`
3. `CfgModel.cs` + `CfgBuilder.cs`
4. 再回头补 `BuildCfgPass.cs` 与现有前端的集成

原因很简单：

- 没有真实语义上下文，调用图会失真
- 没有稳定调用图，后续很多分析关系都不稳
- 没有结构化 `CFG`，分析模块就只能停在“图长得像 CPG”

### 9.5 最终结论

如果现在问一句：

**当前方案下一步能不能直接写代码？**

答案是：

- 能
- 而且现在就该开始

但应该写的不是杂项代码，而是下面这三块核心代码：

- Roslyn 真实语义接入
- 静态调用图
- 结构化 `CFG`

这三块补完后，当前 `Analysis` 模块就能比较明确地站稳这个定位：

- 输入：C# 项目
- 输出：内存态 `CPG`
- 输出内容：结构、类型、调用、基础控制流

这就是当前阶段最合适的收敛点。

---

## 10. 完整 CPG 实现对应的 Joern 文件路径

本节只回答一个问题：

**如果按 `C:\Users\shan\Downloads\joern-master` 来看，“完整 CPG 实现”到底落在哪些文件里。**

这里不混入 console、querydb、CLI 壳层，只保留和 **CPG 本体、语义增强、控制流、数据流** 直接相关的文件路径。

### 10.1 底层 CPG schema 与生成入口

说明：

- `joern-master` 仓库本身没有把完整 `codepropertygraph` 源码直接展开在仓库里。
- 当前仓库里保留了 schema 扩展和生成入口。

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\src\universal\schema-extender\schema\src\main\scala\CpgExtCodegen.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\build.sbt`
- `C:\Users\shan\Downloads\joern-master\build.sbt`

说明：

- `CpgExtCodegen.scala` 是 schema 扩展代码生成入口。
- `build.sbt` 中声明了对 `codepropertygraph` 的依赖，说明完整节点类、属性类、schema 基础设施来自外部库。

### 10.2 C# 前端入口与 AST 建图

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\passes\AstCreationPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreatorHelper.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForDeclarationsCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForExpressionsCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForPrimitivesCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForStatementsCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstSummaryVisitor.scala`

说明：

- 这一组文件负责把 C# 输入变成基础 CPG 节点与基础 AST 关系。
- 如果缺这层，就谈不上完整 CPG 前端。

### 10.3 前端摘要、作用域、输入解析

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpProgramSummary.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpScope.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\ScopeType.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\DotNetJsonAst.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\DotNetJsonParser.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\ProgramSummaryCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\DotNetAstGenRunner.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\ImplicitUsingsCollector.scala`

说明：

- 这部分不是图查询层，而是前端要稳定产图所依赖的输入与摘要能力。

### 10.4 x2cpg 基础 pass

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\FileCreationPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\NamespaceCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeDeclStubCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\MethodStubCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\ParameterIndexCompatPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\MethodDecoratorPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\AstLinkerPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\ContainsEdgePass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeRefPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeEvalPass.scala`

说明：

- 这一层是“基础 CPG 成型”的核心骨架。
- 没有这层，前端产出的图会很散，很多节点关系不稳定。

### 10.5 调用图相关实现

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\NaiveCallLinker.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\StaticCallLinker.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\DynamicCallLinker.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\MethodRefLinker.scala`

说明：

- 这是完整 CPG 实现中的调用关系增强层。
- `StaticCall + DynamicCall + MethodRef` 一起才构成较完整的调用图能力。

### 10.6 控制流、支配与控制依赖

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\CfgCreationPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\Cfg.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\CfgCreator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgAdapter.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominator.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominatorFrontier.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominatorPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CpgCfgAdapter.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\DomTreeAdapter.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\ReverseCpgCfgAdapter.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\codepencegraph\CdgPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\codepencegraph\CpgPostDomTreeAdapter.scala`

说明：

- 完整 CPG 实现不只包含基础 `CFG`。
- 还包含 dominator、post-dominator、control dependence。

### 10.7 类型恢复与前端补全

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\MetaDataPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\SymbolTable.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\TypeNodePass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XConfigFileCreationPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XImportResolverPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XImportsPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XInheritanceFullNamePass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeHintCallLinker.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeRecovery.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeStubsParser.scala`

说明：

- 这部分让图在“前端信息不完整”时仍然能逐步恢复类型和调用信息。
- 这是“更完整 CPG 实现”与“只会建基础图”之间的重要分界线。

### 10.8 类型关系补边

关键文件：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\AliasLinkerPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\FieldAccessLinkerPass.scala`
- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\TypeHierarchyPass.scala`

说明：

- 这一层负责把“类型继承、别名、字段访问”从零散 AST 事实提升成图上的稳定关系。

### 10.9 semanticcpg 语义增强层

关键文件与目录：

- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\Overlays.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\NodeExtension.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\validation\validation.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\layers\LayerCreator.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\AccessPath.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\AccessElement.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\TrackedBase.scala`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\bindingextension`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\callgraphextension`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension`
- `C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types`

说明：

- `semanticcpg` 不是 console 外围，而是“让 CPG 可被稳定使用”的语义层。
- 这里包含 overlay 记录、图校验、访问路径语义、语言层扩展方法。

### 10.10 数据流层

关键文件与目录：

- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\layers\dataflows\OssDataFlow.scala`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\semanticsloader`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\slicing`
- `C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language`

说明：

- 这部分已经超出“最小建图”，进入“可做数据流分析”的范围。
- 但如果说的是 Joern 意义上的“完整 CPG 实现”，它就是必须纳入视野的后半截。

### 10.11 一句话总结

如果严格按 `joern-master` 来看，“完整 CPG 实现”至少包含以下层次：

- schema 与生成
- C# 前端 AST 建图
- x2cpg 基础 pass
- 调用图
- 控制流、支配、控制依赖
- 类型恢复与类型关系
- semanticcpg 语义增强层
- dataflowengineoss 数据流层

这也是为什么当前项目虽然已经做出了**最小可用 C# CPG 前端**，但距离 `joern-master` 意义上的**完整 CPG 实现**仍然还有明显缺口。

### 10.12 已实现目录标记

按“实现一个主要目录，就在文档中标记一个目录”的规则，当前完成情况如下：

- `[已实现] semanticcpg/validation`
  - 对应实现目录：`src/Analysis/Semantic/Validation`
  - 已实现文件：
    - `src/Analysis/Semantic/Validation/ValidationLevel.cs`
    - `src/Analysis/Semantic/Validation/ValidationError.cs`
    - `src/Analysis/Semantic/Validation/ValidationViolation.cs`
    - `src/Analysis/Semantic/Validation/PostFrontendValidator.cs`
  - 已接入主流程：
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Semantic/Validation/PostFrontendValidatorTests.cs`
- `[已实现] x2cpg/passes/typerelations`
  - 对应实现目录：`src/Analysis/Passes`
  - 已实现文件：
    - `src/Analysis/Passes/BuildTypeHierarchyPass.cs`
    - `src/Analysis/Passes/BuildAliasRelationPass.cs`
    - `src/Analysis/Passes/BuildFieldAccessRelationPass.cs`
  - 已接入主流程：
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Frontend/StageTwoIntegrationTests.cs`
- `[已实现] semanticcpg (root)`
  - 对应实现目录：`src/Analysis/Semantic`
  - 已实现文件：
    - `src/Analysis/Semantic/Overlays.cs`
    - `src/Analysis/Semantic/NodeExtension.cs`
    - `src/Analysis/Semantic/SemanticCpgPackage.cs`
  - 配套接入：
    - `src/Analysis/Passes/BuildMetadataPass.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Semantic/OverlaysTests.cs`
- `[已实现] x2cpg/passes/base`
  - 对应实现目录：`src/Analysis/Passes`
  - 新增完成文件：
    - `src/Analysis/Passes/BuildParameterIndexCompatPass.cs`
    - `src/Analysis/Passes/BuildMethodDecoratorPass.cs`
  - 已接入主流程：
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Passes/FrontendPassesTests.cs`
- `[进行中] x2cpg/passes/frontend`
  - 对应实现目录：`src/Analysis/Passes`
  - 当前已实现文件：
    - `src/Analysis/Passes/BuildMetadataPass.cs`
    - `src/Analysis/Passes/BuildConfigFileCreationPass.cs`
    - `src/Analysis/Passes/BuildImportsPass.cs`
    - `src/Analysis/Passes/BuildImportResolverPass.cs`
    - `src/Analysis/Passes/BuildTypeRecoveryPass.cs`
    - `src/Analysis/Passes/BuildTypeNodePass.cs`
    - `src/Analysis/Passes/BuildInheritanceFullNamePass.cs`
    - `src/Analysis/Passes/BuildTypeHintCallLinkerPass.cs`
    - `src/Analysis/Frontend/SymbolTable.cs`
    - `src/Analysis/Frontend/TypeStubsParser.cs`
    - `src/Analysis/Frontend/ImportDirectiveInfo.cs`
  - 当前未实现文件：
    - `完整 XTypeRecovery.scala（当前已覆盖：局部/标识符类型恢复、动态调用提示、实参到形参类型传播、方法返回值传播、未知调用返回值占位、字段读取传播、按接收者类型反查成员、按接收者类型反查属性 getter 返回值、前端未解析字段访问 fallback、前端未解析属性 setter fallback、按接收者类型反查属性 setter 目标成员、属性复合赋值降级成 get_ + 运算 + set_、未知成员读取占位、链式字段读取传播、成员写入类型持久化、索引访问恢复、未知索引访问占位、dummy 类型收敛替换、MethodRef 别名事实、MethodRef 别名调用提示、MethodRef 别名返回值传播、static import 方法提示、import alias 类型回灌）`
  - 已补验证测试：
    - `tests/Analysis.Tests/Passes/FrontendPassesTests.cs`
    - `tests/Analysis.Tests/Passes/ImportPassesTests.cs`
    - `tests/Analysis.Tests/Passes/TypeRecoveryPassTests.cs`
    - `tests/Analysis.Tests/Frontend/SymbolTableTests.cs`
    - `tests/Analysis.Tests/Frontend/TypeStubsParserTests.cs`
- `[已实现] x2cpg/passes/controlflow/cfgdominator`
  - 对应实现目录：`src/Analysis/Passes/ControlFlow/Dominance`
  - 已实现文件：
    - `src/Analysis/Passes/ControlFlow/Dominance/CfgAdapter.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/IDomTreeAdapter.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/CpgCfgAdapter.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/ReverseCpgCfgAdapter.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/CfgDominator.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/CfgDominatorFrontier.cs`
    - `src/Analysis/Passes/ControlFlow/Dominance/CfgDominatorPass.cs`
  - 配套增强：
    - `src/Analysis/Passes/BuildCfgPass.cs`
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
    - `src/Analysis/Core/CpgEdgeKind.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Passes/ControlFlowDominanceTests.cs`
- `[已实现] x2cpg/passes/controlflow/codepencegraph`
  - 对应实现目录：`src/Analysis/Passes/ControlFlow`
  - 已实现文件：
    - `src/Analysis/Passes/ControlFlow/BuildCdgPass.cs`
    - `src/Analysis/Passes/ControlFlow/CodePenceGraph/CpgPostDomTreeAdapter.cs`
  - 配套接入：
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
    - `src/Analysis/Core/CpgEdgeKind.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Passes/ControlDependenceTests.cs`
- `[已实现] semanticcpg/accesspath`
  - 对应实现目录：`src/Analysis/Semantic/AccessPath`
  - 已实现文件：
    - `src/Analysis/Semantic/AccessPath/AccessPath.cs`
    - `src/Analysis/Semantic/AccessPath/AccessElement.cs`
    - `src/Analysis/Semantic/AccessPath/TrackedBase.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Semantic/AccessPathTests.cs`
- `[已实现] dataflowengineoss/passes/reachingdef`
  - 对应实现目录：`src/Analysis/Passes/DataFlow`
  - 已实现文件：
    - `src/Analysis/Passes/DataFlow/BuildOssDataFlowPass.cs`
    - `src/Analysis/Passes/DataFlow/BuildReachingDefinitionsPass.cs`
    - `src/Analysis/Passes/DataFlow/BuildDdgPass.cs`
  - 已接入主流程：
    - `src/Analysis/Frontend/DefaultRoslynCpgBuilder.cs`
    - `src/Analysis/Core/CpgEdgeKind.cs`
    - `src/Analysis/Core/CpgEdge.cs`
    - `src/Analysis/Core/CpgGraph.cs`
    - `src/Analysis/Core/CpgGraphBuilder.cs`
  - 已补验证测试：
    - `tests/Analysis.Tests/Passes/DataFlowPassTests.cs`

## 14. 2026-04-15 扫描后补齐的 x2cpg 入口映射

本次重新扫描了 Joern 的核心前端框架目录：

- `C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg`

本轮只处理核心 CPG 能力，不处理 `DOT`、`SARIF`、代码导出、测试辅助、HTTP server、命令行解析器和 REPL 外围能力。

### 14.1 文件映射状态

| Joern 文件 | C# 实现文件 | 状态 |
| --- | --- | --- |
| `Ast.scala` | `src/Analysis/Frontend/AstModel/Ast.cs` | 已实现 |
| `AstCreatorBase.scala` | `src/Analysis/Frontend/AstModel/AstCreatorBase.cs` | 已实现 |
| `AstNodeBuilder.scala` | `src/Analysis/Frontend/AstModel/AstNodeBuilder.cs` | 已实现 |
| `Defines.scala` | `src/Analysis/X2Cpg/Defines.cs` | 已实现 |
| `Imports.scala` | `src/Analysis/X2Cpg/Imports.cs` | 已实现 |
| `SourceFiles.scala` | `src/Analysis/X2Cpg/SourceFiles.cs` | 已实现 |
| `X2Cpg.scala` | `src/Analysis/X2Cpg/X2Cpg.cs` | 本次补齐 |
| `X2Cpg.scala / X2CpgConfig` | `src/Analysis/X2Cpg/X2CpgConfig.cs` | 本次补齐 |
| `X2Cpg.scala / ValidationMode` | `src/Analysis/X2Cpg/ValidationMode.cs` | 本次补齐 |
| `X2Cpg.scala / X2CpgFrontend` | `src/Analysis/X2Cpg/IX2CpgFrontend.cs` | 本次补齐 |
| `datastructures/ProgramSummary.scala` | `src/Analysis/X2Cpg/DataStructures/ProgramSummary.cs` | 已实现 |
| `datastructures/Scope.scala` | `src/Analysis/X2Cpg/DataStructures/Scope.cs` | 已实现 |
| `datastructures/ScopeElement.scala` | `src/Analysis/X2Cpg/DataStructures/ScopeElement.cs` | 已实现 |
| `datastructures/Stack.scala` | `src/Analysis/X2Cpg/DataStructures/X2CpgStack.cs` | 已实现 |
| `datastructures/VariableScopeManager.scala` | `src/Analysis/X2Cpg/DataStructures/VariableScopeManager.cs` | 已实现 |

### 14.2 本次补齐内容

- `X2CpgConfig`
  - 对齐 Joern 的 `inputPath`、`outputPath`、`serverMode`、`serverTimeoutSeconds`、`ignoredFilesRegex`、`ignoredFiles`、`schemaValidation`、`disableFileContent`。
  - `WithInputPath` 会转绝对路径。
  - `WithIgnoredFiles` 会把相对忽略路径绑定到 `InputPath` 下。
- `IX2CpgFrontend`
  - 对齐 Joern `X2CpgFrontend` 的核心生命周期。
  - 保留 `CreateCpg`、`CreateCpgWithOverlays`、`Run`、`Dispose`。
- `X2Cpg`
  - 对齐 Joern `newEmptyCpg`、`withNewEmptyCpg`、`applyDefaultOverlays`、`defaultOverlayCreators`、`writeCodeToFile`、`stripQuotes`。
  - 默认 overlay 顺序为 `base`、`controlflow`、`typerel`、`callgraph`。
  - 明确不加入 Dump/DOT layer，因为它不是本项目当前核心 CPG 能力。

### 14.3 当前结论

- 按本项目选定的 `x2cpg` 核心入口和基础数据结构文件映射：已达到 `100%`。
- 按完整 Joern 平台等价：不能宣称 `100%`，因为 Joern 还包含多语言前端、命令行平台、HTTP server、图存储、REPL DSL、DOT/SARIF/导出等外围生态。
- 当前更准确的说法是：`Analysis` 已覆盖本项目需要的 Joern 风格核心 CPG 建图、overlay、查询、数据流、切片和 x2cpg 入口骨架。

### 14.4 验证结果

已运行：

```powershell
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'; $env:DOTNET_CLI_HOME=(Resolve-Path '.').Path; dotnet test .\tests\Analysis.Tests\Analysis.Tests.csproj -v minimal
```

结果：

- 通过：`123`
- 失败：`0`
- 跳过：`0`
