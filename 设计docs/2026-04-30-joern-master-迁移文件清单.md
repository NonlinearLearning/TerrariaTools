# joern-master 迁移文件清单

## 1. 这份清单负责什么

这份清单只回答一个问题：

```text
如果目标是做“简化版 joern-master”，
只保留依赖建图能力，
那么 joern-master 里哪些文件必须迁，哪些可选迁，哪些明确不迁。
```

这里的“迁移”不是指原样复制 Scala 文件，而是指：

1. 这些文件承载的职责必须在新实现里有等价落点。
2. 新实现可以换语言、换 API、换结构，但不能丢掉对应能力。
3. 迁移范围只围绕：前端 lowering、图形状、依赖边、最小 CFG、最小 call graph、最小 type relation。

这份清单明确排除：

1. CLI。
2. 导出器。
3. querydb、scan、reporting、console。
4. 多语言前端。
5. 发行壳、脚本壳、产品化外设。

## 2. 划分原则

本清单按 2 类组织：

1. **迁移列表**：只要不是外围壳，就进入迁移范围，只区分优先级与批次。
2. **明确不迁**：不属于依赖建图核心。

判断标准只有一条：

```text
它是否直接决定 AST / CONDITION / ARGUMENT / REF / CALL / CFG / type relation / call graph 这些核心依赖图能力。
```

## 3. 高优先级迁移文件全量路径

这一节不是代表性文件，也不是抽样。

这一节按当前目录扫描结果，列出“简化版 joern-master”第一优先级需要迁移的全部主干源码文件路径。

当前纳入高优先级的源码树只有 4 棵：

1. `joern-cli/frontends/x2cpg`
2. `joern-cli/frontends/csharpsrc2cpg`
3. `semanticcpg`
4. `dataflowengineoss`

当前明确不纳入这一节高优先级全量清单的有：

1. 其他语言 frontend。
2. `src/test/**`。
3. 明显导出 / 展示壳，如 `dotgenerator/*`、`Dump*`。
4. 与当前 C# 主链无关的多语言 `frontendspecific/*`。

### 3.1 x2cpg

```text
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\Ast.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Ast.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\AstCreatorBase.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/AstCreatorBase.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\AstNodeBuilder.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/AstNodeBuilder.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\Defines.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Defines.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\Imports.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Imports.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\SourceFiles.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/SourceFiles.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\X2Cpg.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/X2Cpg.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\astgen\AstGenNodeBuilder.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/AstGenNodeBuilder.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\astgen\AstGenRunner.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/AstGenRunner.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\astgen\package.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/Package.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\ProgramSummary.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/ProgramSummary.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\Scope.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/Scope.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\ScopeElement.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/ScopeElement.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\Stack.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/Stack.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\datastructures\VariableScopeManager.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/VariableScopeManager.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\frontendspecific\package.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/frontendspecific/Package.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\Base.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/Base.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\CallGraph.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/CallGraph.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\ControlFlow.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/ControlFlow.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\layers\TypeRelations.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/TypeRelations.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\AstLinkerPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/AstLinkerPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\ContainsEdgePass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/ContainsEdgePass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\FileCreationPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/FileCreationPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\MethodDecoratorPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/MethodDecoratorPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\MethodStubCreator.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/MethodStubCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\NamespaceCreator.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/NamespaceCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\ParameterIndexCompatPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/ParameterIndexCompatPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeDeclStubCreator.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeDeclStubCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeEvalPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeEvalPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\base\TypeRefPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeRefPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\DynamicCallLinker.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/DynamicCallLinker.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\MethodRefLinker.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/MethodRefLinker.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\NaiveCallLinker.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/NaiveCallLinker.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\callgraph\StaticCallLinker.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/StaticCallLinker.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\CfgCreationPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/CfgCreationPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\Cfg.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgcreation/Cfg.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgcreation\CfgCreator.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgcreation/CfgCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgAdapter.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CfgAdapter.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominator.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CfgDominator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominatorFrontier.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CfgDominatorFrontier.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CfgDominatorPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CfgDominatorPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\CpgCfgAdapter.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CpgCfgAdapter.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\DomTreeAdapter.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/DomTreeAdapter.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\cfgdominator\ReverseCpgCfgAdapter.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/ReverseCpgCfgAdapter.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\codepencegraph\CdgPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/codepencegraph/CdgPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\controlflow\codepencegraph\CpgPostDomTreeAdapter.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/codepencegraph/CpgPostDomTreeAdapter.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\MetaDataPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/MetaDataPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\SymbolTable.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/SymbolTable.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\TypeNodePass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/TypeNodePass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XConfigFileCreationPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XConfigFileCreationPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XImportResolverPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XImportResolverPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XImportsPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XImportsPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XInheritanceFullNamePass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XInheritanceFullNamePass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeHintCallLinker.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeHintCallLinker.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeRecovery.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeRecovery.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\frontend\XTypeStubsParser.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeStubsParser.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\AliasLinkerPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/AliasLinkerPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\FieldAccessLinkerPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/FieldAccessLinkerPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\passes\typerelations\TypeHierarchyPass.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/TypeHierarchyPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\typestub\TypeStubConfig.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/typestub/TypeStubConfig.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\typestub\TypeStubUtil.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/typestub/TypeStubUtil.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\ArtifactFetcher.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/ArtifactFetcher.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\AstPropertiesUtil.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/AstPropertiesUtil.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\ConcurrentTaskUtil.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/ConcurrentTaskUtil.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\Environment.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/Environment.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\HashUtil.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/HashUtil.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\KeyPool.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/KeyPool.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\LinkingUtil.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/LinkingUtil.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\ListUtils.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/ListUtils.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\NodeBuilders.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/NodeBuilders.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\OffsetUtils.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/OffsetUtils.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\Report.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/Report.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\StringUtils.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/StringUtils.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\x2cpg\src\main\scala\io\joern\x2cpg\utils\TimeUtils.scala [x] -> src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/TimeUtils.cs
```

### 3.2 csharpsrc2cpg

```text
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\Constants.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/Constants.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\CSharpSrc2Cpg.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/CSharpSrc2Cpg.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\Main.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/Main.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstCreatorHelper.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstCreatorHelper.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForDeclarationsCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForDeclarationsCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForExpressionsCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForExpressionsCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForPrimitivesCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForPrimitivesCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstForStatementsCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForStatementsCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\astcreation\AstSummaryVisitor.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstSummaryVisitor.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpProgramSummary.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/datastructures/CSharpProgramSummary.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\CSharpScope.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/datastructures/CSharpScope.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\datastructures\ScopeType.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/datastructures/ScopeType.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\DotNetJsonAst.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/parser/DotNetJsonAst.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\DotNetJsonParser.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/parser/DotNetJsonParser.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\parser\package.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/parser/Package.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\passes\AstCreationPass.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/passes/AstCreationPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\passes\DependencyPass.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/passes/DependencyPass.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\DependencyDownloader.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/DependencyDownloader.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\DotNetAstGenRunner.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/DotNetAstGenRunner.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\ImplicitUsingsCollector.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/ImplicitUsingsCollector.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\ProgramSummaryCreator.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/ProgramSummaryCreator.cs
C:\Users\shan\Downloads\joern-master\joern-cli\frontends\csharpsrc2cpg\src\main\scala\io\joern\csharpsrc2cpg\utils\Utils.scala [x] -> src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/Utils.cs
```

### 3.3 semanticcpg

```text
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\NodeExtension.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/NodeExtension.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\Overlays.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/Overlays.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\package.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/Package.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\AccessElement.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/accesspath/AccessElement.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\AccessPath.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/accesspath/AccessPath.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\accesspath\TrackedBase.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/accesspath/TrackedBase.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\codedumper\CodeDumper.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/codedumper/CodeDumper.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\codedumper\SourceHighlighter.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/codedumper/SourceHighlighter.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\AccessPathHandling.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/AccessPathHandling.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\ICallResolver.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/ICallResolver.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\Location.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/Location.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NewNodeSteps.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NewNodeSteps.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NewTagNodePairTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NewTagNodePairTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NodeExtensionFinder.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NodeExtensionFinder.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NodeOrdering.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NodeOrdering.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NodeSteps.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NodeSteps.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\NodeTypeStarters.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/NodeTypeStarters.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\package.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/Package.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\SarifExtension.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/SarifExtension.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\Show.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/Show.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\Steps.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/Steps.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\TagTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/TagTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\bindingextension\MethodTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/bindingextension/MethodTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\bindingextension\TypeDeclTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/bindingextension/TypeDeclTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\callgraphextension\CallTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/callgraphextension/CallTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\callgraphextension\MethodTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/callgraphextension/MethodTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\importresolver\Implicits.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/importresolver/Implicits.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\importresolver\package.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/importresolver/Package.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\importresolver\ResolvedImportAsTagTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/importresolver/ResolvedImportAsTagTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\Implicits.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/Implicits.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\ModuleVariableAsNodeTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/ModuleVariableAsNodeTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\ModuleVariableTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/ModuleVariableTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\NodeTypeStarters.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/NodeTypeStarters.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\OpNodes.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/OpNodes.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\nodemethods\ModuleVariableAsNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/nodemethods/ModuleVariableAsNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\modulevariable\nodemethods\ModuleVariableMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/modulevariable/nodemethods/ModuleVariableMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\AnnotationLiteralNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/AnnotationLiteralNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\AnnotationNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/AnnotationNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\AstNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/AstNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\CallMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/CallMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\CfgNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/CfgNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\ExpressionMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/ExpressionMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\IdentifierMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/IdentifierMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\LiteralMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/LiteralMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\LocalMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/LocalMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\MethodMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/MethodMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\MethodParameterInMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/MethodParameterInMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\MethodParameterOutMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/MethodParameterOutMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\MethodRefMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/MethodRefMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\MethodReturnMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/MethodReturnMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\NodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/NodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\nodemethods\StoredNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/StoredNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\ArrayAccessTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/ArrayAccessTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\AssignmentTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/AssignmentTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\FieldAccessTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/FieldAccessTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\Implicits.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/Implicits.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\NodeTypeStarters.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/NodeTypeStarters.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\OpAstNodeTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/OpAstNodeTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\OpNodes.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/OpNodes.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\package.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/Package.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\TargetTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/TargetTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\nodemethods\ArrayAccessMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/nodemethods/ArrayAccessMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\nodemethods\AssignmentMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/nodemethods/AssignmentMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\nodemethods\FieldAccessMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/nodemethods/FieldAccessMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\nodemethods\OpAstNodeMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/nodemethods/OpAstNodeMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\operatorextension\nodemethods\TargetMethods.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/nodemethods/TargetMethods.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\CallTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/CallTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\ControlStructureTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/ControlStructureTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\IdentifierTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/IdentifierTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\generalizations\AstNodeTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/generalizations/AstNodeTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\generalizations\CfgNodeTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/generalizations/CfgNodeTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\generalizations\DeclarationTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/generalizations/DeclarationTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\expressions\generalizations\ExpressionTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/expressions/generalizations/ExpressionTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\propertyaccessors\EvalTypeAccessors.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/propertyaccessors/EvalTypeAccessors.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\propertyaccessors\ModifierAccessors.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/propertyaccessors/ModifierAccessors.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\propertyaccessors\SourceCodeAccessors.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/propertyaccessors/SourceCodeAccessors.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\AnnotationParameterAssignTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/AnnotationParameterAssignTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\AnnotationTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/AnnotationTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\DependencyTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/DependencyTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\FileTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/FileTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\ImportTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/ImportTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\LiteralTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/LiteralTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\LocalTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/LocalTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\MemberTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/MemberTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\MethodParameterOutTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/MethodParameterOutTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\MethodParameterTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/MethodParameterTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\MethodReturnTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/MethodReturnTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\MethodTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/MethodTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\NamespaceBlockTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/NamespaceBlockTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\NamespaceTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/NamespaceTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\TypeDeclTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/TypeDeclTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\language\types\structure\TypeTraversal.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/types/structure/TypeTraversal.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\layers\LayerCreator.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/layers/LayerCreator.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\ExternalCommand.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/ExternalCommand.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\ExternalCommandResult.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/ExternalCommandResult.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\FileUtil.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/FileUtil.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\MemberAccess.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/MemberAccess.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\SecureXmlParsing.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/SecureXmlParsing.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\utils\Statements.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/utils/Statements.cs
C:\Users\shan\Downloads\joern-master\semanticcpg\src\main\scala\io\shiftleft\semanticcpg\validation\validation.scala [x] -> src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/validation/Validation.cs
```

### 3.4 dataflowengineoss

```text
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\antlr4\io\joern\dataflowengineoss\Semantics.g4 [x] -> src/CPG/dataflowengineoss/src/main/antlr4/io/joern/dataflowengineoss/Semantics.g4
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\DefaultSemantics.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/DefaultSemantics.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\package.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/Package.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language\ExtendedCfgNode.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/ExtendedCfgNode.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language\package.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/Package.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language\Path.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/Path.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language\nodemethods\ExpressionMethods.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/nodemethods/ExpressionMethods.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\language\nodemethods\ExtendedCfgNodeMethods.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/nodemethods/ExtendedCfgNodeMethods.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\layers\dataflows\OssDataFlow.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/layers/dataflows/OssDataFlow.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\DataFlowProblem.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/DataFlowProblem.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\DataFlowSolver.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/DataFlowSolver.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\DdgGenerator.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/DdgGenerator.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\EdgeValidator.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/EdgeValidator.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\package.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/Package.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\ReachingDefPass.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/ReachingDefPass.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\passes\reachingdef\ReachingDefProblem.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/ReachingDefProblem.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\AccessPathUsage.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/AccessPathUsage.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\Engine.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/Engine.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\HeldTaskCompletion.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/HeldTaskCompletion.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\package.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/Package.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\SourcesToStartingPoints.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/SourcesToStartingPoints.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\TaskCreator.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/TaskCreator.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\queryengine\TaskSolver.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/TaskSolver.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\semanticsloader\FullNameSemantics.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/semanticsloader/FullNameSemantics.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\semanticsloader\FullNameSemanticsParser.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/semanticsloader/FullNameSemanticsParser.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\semanticsloader\Semantics.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/semanticsloader/Semantics.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\slicing\DataFlowSlicing.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/slicing/DataFlowSlicing.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\slicing\package.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/slicing/Package.cs
C:\Users\shan\Downloads\joern-master\dataflowengineoss\src\main\scala\io\joern\dataflowengineoss\slicing\UsageSlicing.scala [x] -> src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/slicing/UsageSlicing.cs
```

## 4. 迁移列表中的主干文件说明

## 4.1 x2cpg 通用骨架

这些文件是简化版 joern-master 的基础设施核心。

### 1. [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/Ast.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Ast.cs`

**必须迁的原因**：

- 定义了 `Ast` 作为前端 lowering 的中间承载对象。
- 统一存储：
  - `AST`
  - `CONDITION`
  - `TRUE_BODY`
  - `FALSE_BODY`
  - `DO_BODY`
  - `TRY_BODY`
  - `CATCH_BODY`
  - `FINALLY_BODY`
  - `FOR_INIT`
  - `FOR_UPDATE`
  - `FOR_BODY`
  - `REF`
  - `ARGUMENT`
  - `RECEIVER`
  - `BINDS`
  - `CAPTURE`
- 统一负责 `order` 补齐和 schema 邻接校验。

**新实现的等价职责**：

- 前端 lowering 的临时 AST/edge 聚合结构。
- 统一 child order 规则。
- 统一 condition/body/argument/ref 边挂载规则。

### 2. [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/AstNodeBuilder.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/AstNodeBuilder.cs`

**必须迁的原因**：

- 定义了所有常用节点 builder：
  - `callNode`
  - `operatorCallNode`
  - `controlStructureNode`
  - `blockNode`
  - `literalNode`
  - `localNode`
  - `memberNode`
  - `methodRefNode`
  - `typeRefNode`
- 这里是 Joern 把表达式统一降成 canonical 节点形状的关键入口。

**新实现的等价职责**：

- 统一节点工厂。
- 统一 operator lowering 入口。
- 统一 source location / code / typeFullName 注入。

### 3. [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/AstCreatorBase.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/AstCreatorBase.cs`

**必须迁的原因**：

- 定义了前端最重要的结构 helper：
  - `controlStructureAst(...)`
  - `forAst(...)`
  - `whileAst(...)`
  - `doWhileAst(...)`
  - `tryCatchAst(...)`
  - `callAst(...)`
  - `methodAst(...)`
  - `blockAst(...)`
- 尤其关键的是：
  - `controlStructureAst(...)` 会统一补 `condition edges`
  - `callAst(...)` 会统一补参数和 receiver 关系

**新实现的等价职责**：

- 统一语句/表达式/控制结构/调用的 AST 拼装协议。
- 保证 control structure 与 condition 不是靠字符串粘住。

### 4. [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/Defines.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Defines.cs`

**必须迁的原因**：

- 定义了 canonical CPG 的一些关键约定：
  - `ANY`
  - `UnresolvedNamespace`
  - `UnresolvedSignature`
  - `<clinit>`
  - `<init>`
  - `<lambda>`
  - `<unknown>`
  - `<unknownField>`

**新实现的等价职责**：

- unresolved / synthetic 命名规范。
- constructor / static-init / lambda 等合成实体命名规范。

### 5. [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/SourceFiles.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/SourceFiles.cs`

**必须迁的原因**：

- 简化版 joern-master 仍然需要稳定的输入文件发现和相对路径语义。

**新实现的等价职责**：

- 输入源文件发现。
- 统一相对路径规则。

### 5.1 [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/Imports.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/Imports.cs`

**迁移列表，优先级低于前面的 lowering 主干。**

原因：

- `IMPORT` 节点及其 `ImportedEntity` / `ImportedAs` 收口需要一个统一入口。
- 当前 Roslyn 前端已经产生 `IMPORT` 节点，这个文件负责把 import 视图从图查询里稳定收出来。

### 5.2 [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/X2Cpg.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/X2Cpg.cs`

**迁移列表，优先级低于前面的 lowering 主干。**

原因：

- 它承载前端公共装配抽象，不是单个算法本体。
- 新实现里保留了 `CreateGraph(...)` 抽象和 source/import 公共抓手，便于后续 frontends 复用。

### 5.3 [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/astgen/AstGenNodeBuilder.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/AstGenNodeBuilder.cs`

**迁移列表，优先级低于前面的 Roslyn lowering 主干。**

原因：

- 当前主线不依赖外部 AST generator，但仍需要一个最小 node builder 壳承接 astgen 目录职责。

### 5.4 [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/astgen/AstGenRunner.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/AstGenRunner.cs`

**迁移列表，优先级低于前面的 Roslyn lowering 主干。**

原因：

- 当前不复刻 joern 的外部 astgen 进程链，但保留了最小 runner 抽象，避免目录职责悬空。

### 5.5 [x] `joern-cli/frontends/x2cpg/src/main/scala/io/joern/x2cpg/astgen/package.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/astgen/Package.cs`

**迁移列表，优先级低于前面的 Roslyn lowering 主干。**

原因：

- 上游 `package.scala` 在 C# 中以目录级公共 helper 承接更合适。
- 当前实现把 astgen 输入文件发现和相对路径公共逻辑收口到这个文件。

## 4.2 x2cpg 基础 pass

这些文件构成“建完前端 AST 后如何补成基础图”的最小 pass 集。

### 6. [x] `.../x2cpg/passes/base/AstLinkerPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/AstLinkerPass.cs`

**必须迁的原因**：

- AST 结构要从前端中间表示真正落进图。

### 7. [x] `.../x2cpg/passes/base/ContainsEdgePass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/ContainsEdgePass.cs`

**必须迁的原因**：

- `CONTAINS` 是很多 query 与后续 layer 的基础边。

### 8. [x] `.../x2cpg/passes/base/FileCreationPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/FileCreationPass.cs`

**必须迁的原因**：

- 没有 `File` 节点，图上的定位和 contains/namespace 归属会不稳定。

### 9. [x] `.../x2cpg/passes/base/NamespaceCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/NamespaceCreator.cs`

**必须迁的原因**：

- `NamespaceBlock` 是静态语言 CPG 的正式结构节点，不应缺席。

### 10. [x] `.../x2cpg/passes/base/MethodStubCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/MethodStubCreator.cs`

**必须迁的原因**：

- call graph / unresolved references / external methods 都依赖 method stub 兜底。

### 11. [x] `.../x2cpg/passes/base/TypeDeclStubCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeDeclStubCreator.cs`

**必须迁的原因**：

- unresolved/external type 的图占位需要稳定 type stub。

### 12. [x] `.../x2cpg/passes/base/TypeRefPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeRefPass.cs`

**必须迁的原因**：

- type reference 需要系统性连回 type declarations / type stubs。

### 13. [x] `.../x2cpg/passes/base/TypeEvalPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/TypeEvalPass.cs`

**必须迁的原因**：

- type propagation / typeFullName 修补是基础分析图能力的一部分。

### 14. [x] `.../x2cpg/passes/base/MethodDecoratorPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/MethodDecoratorPass.cs`

**迁移列表，优先级低于前面的主干文件。**

原因：

- method 节点常常需要统一补全签名、修饰符、fullName 等元信息。

### 15. [x] `.../x2cpg/passes/base/ParameterIndexCompatPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/base/ParameterIndexCompatPass.cs`

**迁移列表，优先级低于前面的主干文件。**

原因：

- 参数索引是调用、operator、CFG、query 稳定性的底层契约。

## 4.3 x2cpg 调用图 / 控制流 / 类型关系

### 16. [x] `.../x2cpg/passes/callgraph/MethodRefLinker.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/MethodRefLinker.cs`

**必须迁的原因**：

- method ref 和 method 之间的绑定是静态调用图的基础。

### 17. [x] `.../x2cpg/passes/callgraph/StaticCallLinker.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/StaticCallLinker.cs`

**必须迁的原因**：

- 静态调用边是“依赖建图能力”的核心一部分。

### 18. [x] `.../x2cpg/passes/callgraph/DynamicCallLinker.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/callgraph/DynamicCallLinker.cs`

**迁移列表，优先级低于静态调用图主干。**

原因：

- 如果第一版只做静态调用图，可以先不迁。
- 如果要支持更真实的对象调用/虚调用依赖，则要补。

### 19. [x] `.../x2cpg/passes/controlflow/CfgCreationPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/CfgCreationPass.cs`

**必须迁的原因**：

- 用户要求“CPG 图的所有依赖建图能力”，没有 CFG，后续很多依赖关系都站不住。

### 20. [x] `.../x2cpg/passes/controlflow/cfgcreation/*`

已迁移到：
`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgcreation/Cfg.cs`
`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgcreation/CfgCreator.cs`

**必须迁的原因**：

- 这里是真正按 control structure 形状生成 CFG 的实现细节。

### 21. [x] `.../x2cpg/passes/controlflow/cfgdominator/CfgDominatorPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/cfgdominator/CfgDominatorPass.cs`

**迁移列表，优先级低于 CFG 主干。**

原因：

- 如果当前目标只是“最小依赖建图”，可以先不迁 dominator。
- 若要更接近完整 control dependence，后续再补。

### 22. [x] `.../x2cpg/passes/controlflow/codepencegraph/CdgPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/controlflow/codepencegraph/CdgPass.cs`

**迁移列表，优先级低于 CFG 主干。**

原因：

- CDG 很有价值，但可作为 CFG 之后的第二批补强。

### 23. [x] `.../x2cpg/passes/typerelations/TypeHierarchyPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/TypeHierarchyPass.cs`

**必须迁的原因**：

- 继承 / 接口层次是类型依赖图的一部分。

### 24. [x] `.../x2cpg/passes/typerelations/FieldAccessLinkerPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/FieldAccessLinkerPass.cs`

注：上游实际文件名是 `FieldAccessLinkerPass.scala`，原清单这里曾写成 `FieldAccessLinker.scala`。

**必须迁的原因**：

- 字段访问要能连回 member，不能只停在 `<operator>.fieldAccess` 文本层。

### 25. [x] `.../x2cpg/passes/typerelations/AliasLinkerPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/typerelations/AliasLinkerPass.cs`

**迁移列表，优先级低于 type relation 主干。**

原因：

- 若第一版不深做 alias / using alias / type alias，可先不迁。

## 4.4 C# 前端必须迁移的文件

### 26. [x] `joern-cli/frontends/csharpsrc2cpg/src/main/scala/io/joern/csharpsrc2cpg/CSharpSrc2Cpg.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/CSharpSrc2Cpg.cs`

**必须迁的原因**：

- 它给出了 C# 前端的最小装配顺序：
  - metadata
  - dependency pass
  - ast creation
  - type nodes
  - post processing

新实现不一定保留同名文件，但必须保留这类“前端装配器”。

### 27. [x] `.../csharpsrc2cpg/astcreation/AstCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstCreator.cs`

**必须迁的原因**：

- C# 前端 AST 创建的总入口。

### 28. [x] `.../csharpsrc2cpg/astcreation/AstCreatorHelper.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstCreatorHelper.cs`

**必须迁的原因**：

- 前端共用 lowering 帮助逻辑一般都在这里，不能散丢。

### 29. [x] `.../csharpsrc2cpg/astcreation/AstForDeclarationsCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForDeclarationsCreator.cs`

**必须迁的原因**：

- 类型、方法、参数、字段、属性等声明建图主链。

### 30. [x] `.../csharpsrc2cpg/astcreation/AstForExpressionsCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForExpressionsCreator.cs`

**必须迁的原因**：

- 这是 C# 前端最关键的文件之一。
- operator、assignment、call、member access、index access、conditional expression 都在这里 lower。

### 31. [x] `.../csharpsrc2cpg/astcreation/AstForStatementsCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForStatementsCreator.cs`

**必须迁的原因**：

- `if / while / do / for / foreach / switch / try` 的 control structure 与 condition 关系全在这里。

### 32. [x] `.../csharpsrc2cpg/astcreation/AstForPrimitivesCreator.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstForPrimitivesCreator.cs`

**必须迁的原因**：

- 标识符、字面量、基础节点 lowering 需要统一入口。

### 33. [x] `.../csharpsrc2cpg/astcreation/AstSummaryVisitor.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/astcreation/AstSummaryVisitor.cs`

**迁移列表，优先级低于前面的 lowering 主干。**

原因：

- 若只是先做最小建图能力，summary visitor 可后补。

### 34. [x] `.../csharpsrc2cpg/passes/AstCreationPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/passes/AstCreationPass.cs`

**必须迁的原因**：

- 它定义了前端并行把 AST creator 结果吸进图的最小执行壳。

### 35. [x] `.../csharpsrc2cpg/Constants.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/Constants.cs`

**必须迁的原因**：

- C# lowering 里的常量命名、特殊标识一般集中在这里。

### 36. [x] `.../csharpsrc2cpg/utils/*`

已迁移到：
`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/Utils.cs`
`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/RelativePathResolver.cs`
`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/CSharpTypeNameUtility.cs`
`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/utils/CSharpOperatorMap.cs`

**迁移列表，按文件细拆并分批纳入。**

其中优先检查并迁移：

- method full name / signature 组合逻辑
- parser result 到相对路径映射逻辑
- C# type full name / operator 映射工具

注：当前先落了这 4 个高优先级工具文件，并已回接 `CSharpSrc2Cpg.cs` 与 `AstCreatorHelper.cs`。

## 4.5 semanticcpg 中必须迁的文件

### 37. [x] `semanticcpg/src/main/scala/io/shiftleft/semanticcpg/language/nodemethods/AstNodeMethods.scala`

已迁移到：`src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/nodemethods/AstNodeMethods.cs`

**必须迁的原因**：

- 定义 canonical AST 查询能力：
  - `astParent`
  - `astChildren`
  - `ast`
  - `statement`
  - `sourceCode`

### 38. [x] `semanticcpg/.../language/operatorextension/package.scala`

已迁移到：`src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/OperatorTypes.cs`

**必须迁的原因**：

- 定义 operator 分类集合：
  - assignment
  - arithmetic
  - array access
  - field access

### 39. [x] `semanticcpg/.../language/operatorextension/OpNodes.scala`

已迁移到：`src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/language/operatorextension/OpNodes.cs`

**必须迁的原因**：

- 给 operator call 做类型化分层，是后续 query ergonomics 的最小基础。

### 40. [x] `semanticcpg/.../validation/validation.scala`

已迁移到：`src/CPG/semanticcpg/src/main/csharp/io/shiftleft/semanticcpg/validation/Validation.cs`

**第一版可选，但建议尽早迁**。

原因：

- 简化版 CPG 若没有最低限度 schema/shape 验证，后面前端一改很容易图漂移。

## 5. 迁移列表中的后续批次与扩展项

这一节中的文件同样属于迁移列表，只是优先级低于前面的主干文件。

### A. x2cpg / dataflowengineoss 的 layer 定义壳

- [x] `x2cpg/layers/Base.scala`

  已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/Base.cs`
- [x] `x2cpg/layers/CallGraph.scala`

  已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/CallGraph.cs`
- [x] `x2cpg/layers/ControlFlow.scala`

  已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/ControlFlow.cs`
- [x] `x2cpg/layers/TypeRelations.scala`

  已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/layers/TypeRelations.cs`
- [x] `dataflowengineoss/layers/dataflows/OssDataFlow.scala`

理由：

- 它们主要是 layer 装配壳，不是能力本体。
- 但如果目标是“简化版 joern-master”而不是只有能力本体，那么这组文件**建议保留**。
- 它们提供的不是单个算法，而是：
  - layer 名称
  - layer 依赖顺序
  - layer 执行顺序
  - 图能力叠加的显式边界
- 新实现不一定保留同名 Scala 文件，但应保留这一层架构体验。

### B. [x] `joern-cli/frontends/csharpsrc2cpg/passes/DependencyPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/passes/DependencyPass.cs`

理由：

- 这是 `.csproj` 包依赖节点灌图，不是程序控制/数据/调用/类型依赖主链。
- 如果第一版只关心源码内程序依赖图，可不迁。
- 如果你想保留 project/package 依赖节点，再补。

### C. [x] `x2cpg/passes/callgraph/NaiveCallLinker.scala`

理由：

- 在 C# 前端主线上，`CSharpSrc2Cpg.postProcessingPasses` 里用了 `NaiveCallLinker`。
- 如果新实现用 Roslyn semantic binding 直接生成更强 call edges，可不照搬 naive linker。

### C1. [x] `x2cpg/passes/frontend/MetaDataPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/MetaDataPass.cs`

理由：

- 负责灌入最小 `META_DATA` 节点，是前端装配起点。

### C2. [x] `x2cpg/passes/frontend/SymbolTable.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/SymbolTable.cs`

理由：

- 为 import、继承、type hint 等后处理提供声明查询抓手。

### C3. [x] `x2cpg/passes/frontend/TypeNodePass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/TypeNodePass.cs`

理由：

- 根据图中的 `TypeFullName` 补最小 `TYPE` 节点。

### C4. [x] `x2cpg/passes/frontend/XImportsPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XImportsPass.cs`

理由：

- 规范化 `IMPORT` 节点的 `Name`、`CanonicalName`、`FileName`。

### C5. [x] `x2cpg/passes/frontend/XConfigFileCreationPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XConfigFileCreationPass.cs`

理由：

- 为配置输入补最小 `FILE` 节点。

### C6. [x] `x2cpg/passes/frontend/XImportResolverPass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XImportResolverPass.cs`

理由：

- 把 `IMPORT` 解析回声明节点，补最小绑定。

### C7. [x] `x2cpg/passes/frontend/XInheritanceFullNamePass.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XInheritanceFullNamePass.cs`

理由：

- 把继承列表统一收口到 full name。

### C8. [x] `x2cpg/passes/frontend/XTypeHintCallLinker.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeHintCallLinker.cs`

理由：

- 基于动态 type hint 为调用补最小候选方法边。

### C9. [x] `x2cpg/passes/frontend/XTypeRecovery.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeRecovery.cs`

理由：

- 对缺失的调用和标识符类型做轻量恢复。

### C10. [x] `x2cpg/passes/frontend/XTypeStubsParser.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/passes/frontend/XTypeStubsParser.cs`

理由：

- 解析简化 type stub 文本输入。

### C11. [x] `x2cpg/typestub/TypeStubConfig.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/typestub/TypeStubConfig.cs`

理由：

- 承接 stub 类型清单和继承配置。

### C12. [x] `x2cpg/typestub/TypeStubUtil.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/typestub/TypeStubUtil.cs`

理由：

- 把 stub 配置灌成最小外部 `TYPE_DECL` 节点。

### C13. [x] `x2cpg/utils/ArtifactFetcher.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/utils/ArtifactFetcher.cs`

理由：

- 为 type stub 和其他配置型输入提供最小文本装载抓手。

### D. [x] `semanticcpg/language/nodemethods/CallMethods.scala` 及其他 query sugar

理由：

- 第一版只要最小 query API，不需要把整个 semanticcpg 语言层全搬过来。

### E. `dataflowengineoss` 中的高级数据流能力

这里不能再粗暴按整个目录打包成“可选”。按“简化版 joern-master”口径，应拆成两层：

#### E1. 应纳入核心后续批次的路径

- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/DefaultSemantics.scala`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/DefaultSemantics.cs`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/passes/reachingdef/*`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/passes/reachingdef/`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/queryengine/*`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/queryengine/`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/semanticsloader/*`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/semanticsloader/`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/slicing/*`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/slicing/`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/language/ExtendedCfgNode.scala`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/ExtendedCfgNode.cs`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/language/Path.scala`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/Path.cs`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/language/nodemethods/*`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/language/nodemethods/`
- [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/layers/dataflows/OssDataFlow.scala`

  已迁移到：`src/CPG/dataflowengineoss/src/main/csharp/io/joern/dataflowengineoss/layers/dataflows/OssDataFlow.cs`

理由：

- 这些路径已经超出“基础 CFG/静态调用图”的范围，属于 joern-master 的核心高级分析链。
- 它们承载：
  - `REACHING_DEF` pass
  - DDG 生成
  - backwards query engine
  - slicing
  - external semantics loading
  - dataflow layer 装配
- 如果目标是“简化版 joern-master”，这些能力不应整体排除，只应按批次后移。

#### E2. 仍可延后但仍属于迁移范围的路径

- `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/dotgenerator/*`
- `dataflowengineoss/src/test/*`

理由：

- `dotgenerator` 属于导出/展示壳，不是核心分析能力。
- `src/test` 是验证资产，不是运行主链。

## 6. 明确不迁移的文件和目录

### 顶层产品壳

- `joern`
- `joern-cli` 顶层运行壳
- `joern-parse`
- `joern-flow`
- `joern-slice`
- `joern-scan`
- `joern-export`
- 各种 `.bat` / `.sh` 启动脚本

### 产品外围设施

- `console/`
- `querydb/`
- `querydb-install.sh`
- `codedumper/`
- `dotgenerator/`
- `sarif/`
- `joern-vectors`

### 非 C# / 非简化版目标前端

- `c2cpg/`
- `javasrc2cpg/`
- `jssrc2cpg/`
- `gosrc2cpg/`
- `php2cpg/`
- `pysrc2cpg/`
- `rubysrc2cpg/`
- `swiftsrc2cpg/`
- `kotlin2cpg/`
- `ghidra2cpg/`
- `jimple2cpg/`

### 测试与发行辅助

- `tests/`
- `testDistro.py`
- `ci/`
- `.github/`
- `project/`
- `scripts/` 中的发行脚本

这些都不属于“简化版 CPG 的依赖建图核心”。

## 7. 已排除路径说明

这一节专门解释前面已经从“高优先级全量迁移清单”里排除的路径。

这些路径不是“永远没价值”，而是当前阶段不纳入高优先级主链。主要原因只有 4 类：

1. 导出 / 展示壳。
2. 结果输出壳。
3. 测试 / 模拟支撑。
4. 与当前 C# 主线无关的多语言或产品化外围。

### 7.1 `semanticcpg/.../dotgenerator/*`

这些文件是 `semanticcpg` 的 DOT 图导出器。

典型职责包括：

1. 把 AST 导成 DOT。
2. 把 CFG / CDG / CallGraph / TypeHierarchy 导成 DOT。
3. 序列化图展示结果。

为什么当前不迁：

1. 它们依赖图已经先被建好。
2. 它们解决的是“怎么展示图”，不是“怎么建图”。
3. 当前目标是先保住 CPG 主干能力，不是先补可视化导出。

### 7.2 `semanticcpg/.../sarif/*`

这些文件是 SARIF 输出相关路径。

典型职责包括：

1. 定义 SARIF schema。
2. 把扫描结果转换成 SARIF。
3. 对接 IDE / 安全平台 / code scanning 输出格式。

为什么当前不迁：

1. 它们属于结果对外表达层。
2. 它们不决定 AST / CFG / call graph / dataflow 的建图逻辑。
3. 当前阶段先保主链，结果格式导出后补即可。

### 7.3 `semanticcpg/.../testing/*`

这些文件是测试支撑代码。

典型职责包括：

1. 构造假图。
2. 提供 mock CPG。
3. 支撑 traversal / query 的单元测试。

为什么当前不迁：

1. 它们不是运行时主链。
2. 它们服务验证，不是服务生产图构建。
3. 当前文档关注的是主实现边界，不是测试资产全量迁移。

### 7.4 `dataflowengineoss/.../dotgenerator/*`

这些文件是数据流图导出器。

典型职责包括：

1. 把 DDG / PDG 导成 DOT。
2. 把已经生成好的数据流图吐成可视化结果。

为什么当前不迁：

1. 它们属于展示壳，不是求解器本体。
2. 要先有 `reachingdef`、DDG、slicing 主链，导出器才有意义。
3. 当前优先级在数据流能力本身，不在展示。

### 7.5 `dataflowengineoss/.../layers/dataflows/DumpCpg14.scala`
### 7.6 `dataflowengineoss/.../layers/dataflows/DumpDdg.scala`
### 7.7 `dataflowengineoss/.../layers/dataflows/DumpPdg.scala`

这些文件是 dataflow layer 上的 dump / export 壳。

它们的作用更接近：

1. 把现成 layer 结果导出来。
2. 帮助调试或展示数据流图。

为什么当前不迁：

1. 它们不是 `reachingdef`、DDG、query engine、slicing 的本体。
2. 它们不决定数据流边怎么生成。
3. 当前阶段保留 `OssDataFlow.scala` 即可，`Dump*` 先延后。

### 7.8 `x2cpg/.../layers/DumpAst.scala`
### 7.9 `x2cpg/.../layers/DumpCdg.scala`
### 7.10 `x2cpg/.../layers/DumpCfg.scala`

这些文件是 x2cpg 的导出 layer。

它们的职责是：

1. 导出 AST。
2. 导出 CDG。
3. 导出 CFG。

为什么当前不迁：

1. 它们解决的是图结果展示，不是图生成。
2. `Base.scala`、`ControlFlow.scala` 这类 layer 主壳更重要。
3. 当前目标是先保分析主链，不是先保 dump 体验。

### 7.11 `x2cpg/.../utils/dependency/*`

这组文件是依赖解析工具集。

已确认的典型内容包括：

1. `DependencyResolver.scala`
2. `GradleDependencies.scala`
3. `MavenCoordinates.scala`
4. `MavenDependencies.scala`

为什么当前不迁：

1. 它们更偏包依赖 / 构建生态，不是 AST / CFG / call graph 主链。
2. 它们明显偏 JVM 构建生态，不是当前 C# 主线最核心的依赖图能力。
3. 如果以后补 project/package dependency graph，再单独引入更合适。

### 7.12 `x2cpg/.../utils/server/*`

这组文件是前端 HTTP 服务壳。

已确认的典型内容包括：

1. `FrontendHTTPClient.scala`
2. `FrontendHTTPServer.scala`

为什么当前不迁：

1. 它们服务的是前端进程通信和服务化运行。
2. 它们不是图构建算法的一部分。
3. 当前目标是本地实现能力，不是复刻服务壳。

### 7.13 `x2cpg/.../frontendspecific/javasrc2cpg/*`
### 7.14 `x2cpg/.../frontendspecific/jssrc2cpg/*`
### 7.15 `x2cpg/.../frontendspecific/php2cpg/*`
### 7.16 `x2cpg/.../frontendspecific/pysrc2cpg/*`
### 7.17 `x2cpg/.../frontendspecific/rubysrc2cpg/*`
### 7.18 `x2cpg/.../frontendspecific/swiftsrc2cpg/*`

这些路径是 x2cpg 里给其他语言前端准备的专用后处理 / 补强逻辑。

它们通常负责：

1. 某语言自己的 import 解析。
2. 某语言自己的 type recovery。
3. 某语言自己的 type hint / inheritance / closure / builtins 处理。

为什么当前不迁：

1. 它们不是语言无关的 x2cpg 最小骨架。
2. 它们明显依赖对应语言的语义规则。
3. 当前目标明确是 C# 主链，不是全语言 joern 复刻。

### 7.19 这一组排除项的统一结论

这些路径当前被排除，不代表未来永远不迁。

当前不迁的统一理由是：

1. 它们不直接决定 `AST / CONDITION / ARGUMENT / REF / CALL / CFG / type relation / call graph / reaching-def / DDG / slicing` 主链能力。
2. 它们大多处于展示层、输出层、测试层或多语言外围层。
3. 当前先把 `C# -> canonical CPG -> overlay/layer -> dataflow` 这条主链做稳，再决定是否把这些外围能力补回去。

## 8. 推荐迁移顺序

### 第一批：先迁骨架与前端 lowering

1. [x] `Ast.scala`
2. [x] `AstNodeBuilder.scala`
3. [x] `AstCreatorBase.scala`
4. [x] `Defines.scala`
5. [x] `SourceFiles.scala`
6. [x] `AstCreator.scala`
7. [x] `AstCreatorHelper.scala`
8. [x] `AstForPrimitivesCreator.scala`
9. [x] `AstForExpressionsCreator.scala`
10. [x] `AstForStatementsCreator.scala`
11. [x] `AstForDeclarationsCreator.scala`
12. [x] `AstCreationPass.scala`
13. [x] `ProgramSummary.scala`、`Scope.scala`、`ScopeElement.scala`、`Stack.scala`、`VariableScopeManager.scala`

已迁移到：`src/CPG/joern-cli/frontends/x2cpg/src/main/csharp/io/joern/x2cpg/datastructures/`

14. [x] `CSharpProgramSummary.scala`、`CSharpScope.scala`、`ScopeType.scala`

已迁移到：`src/CPG/joern-cli/frontends/csharpsrc2cpg/src/main/csharp/io/joern/csharpsrc2cpg/datastructures/`

### 第二批：补基础图 pass

13. [x] `AstLinkerPass.scala`
14. [x] `ContainsEdgePass.scala`
15. [x] `FileCreationPass.scala`
16. [x] `NamespaceCreator.scala`
17. [x] `MethodStubCreator.scala`
18. [x] `TypeDeclStubCreator.scala`
19. [x] `TypeRefPass.scala`
20. [x] `TypeEvalPass.scala`
21. [x] `MethodDecoratorPass.scala`
22. [x] `ParameterIndexCompatPass.scala`

### 第三批：补最小依赖关系

23. [x] `MethodRefLinker.scala`
24. [x] `StaticCallLinker.scala`
25. [x] `CfgCreationPass.scala`
26. [x] `cfgcreation/*`
27. [x] `TypeHierarchyPass.scala`
28. [x] `FieldAccessLinker.scala`

### 第四批：补查询与校验

29. [x] `AstNodeMethods.scala`
30. [x] `operatorextension/package.scala`
31. [x] `OpNodes.scala`
32. [x] `validation.scala`

### 第五批：扩到简化版 joern-master 的高级能力

33. [x] `DynamicCallLinker.scala`
34. [x] `AliasLinkerPass.scala`
35. [x] `CfgDominatorPass.scala`
36. [x] `CdgPass.scala`
37. [x] `DependencyPass.scala`
38. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/DefaultSemantics.scala`
39. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/passes/reachingdef/*`
40. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/queryengine/*`
41. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/semanticsloader/*`
42. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/slicing/*`
43. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/language/*`
44. [x] `dataflowengineoss/src/main/scala/io/joern/dataflowengineoss/layers/dataflows/OssDataFlow.scala`

## 9. Overlay / Layer 边界

如果目标只是“把能力做出来”，那么很多 `layers/*.scala` 看起来像壳。

但如果目标是你当前明确要求的：

```text
保留 Joern 风格的分层体验
```

那么 layer 不是装饰，而是架构边界的一部分。

建议显式保留下面这些 layer：

1. [x] `x2cpg/layers/Base.scala`
2. [x] `x2cpg/layers/TypeRelations.scala`
3. [x] `x2cpg/layers/CallGraph.scala`
4. [x] `x2cpg/layers/ControlFlow.scala`
5. [x] `dataflowengineoss/layers/dataflows/OssDataFlow.scala`

它们共同表达的是这条叠加链：

```text
Base
  -> TypeRelations
    -> CallGraph
      -> ControlFlow
        -> OssDataFlow
```

这条链的价值在于：

1. 上层能清楚知道每一层图能力什么时候被补进来。
2. 调试时可以定位“某条边是哪个 layer 负责生成的”。
3. 以后扩 layer 时，不会把所有 pass 混成一个不可维护的大装配器。

结论：

- 对“简化版 joern-master”来说，layer 壳属于迁移列表。
- 是否第一批就落地，由实现优先级决定，但不再视为迁移范围外内容。

## 10. 最终建议

如果目标是“基于 Roslyn 三层做简化版 joern-master”，当前最合理的迁移范围是：

```text
迁移列表：x2cpg 骨架 + C# lowering 主链 + base/callgraph/controlflow/typerel pass + semanticcpg 主干 + Joern 风格 layer/overlay 壳 + dataflowengineoss 主链 + 各类后续批次文件
明确不迁：CLI、导出、扫描、querydb、console、多语言前端、发行壳
```

这份清单的目的不是“复制 Joern”，而是：

```text
把所有不属于外围壳的核心文件职责都纳入迁移范围，再按批次实现，保证你的简化版 joern-master 最终具备 AST、condition、operator、call、ref、cfg、type relation、reaching-def、ddg、slicing 这些主干能力。
```
