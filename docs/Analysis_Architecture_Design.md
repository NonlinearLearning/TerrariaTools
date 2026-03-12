# Analysis 模块架构设计说明 (Analysis Module Architecture Design)

本文档详细说明了 `TerrariaTools.Analysis` 模块的架构设计、核心组件及其工作流程。该模块旨在通过静态与动态结合的方式，对复杂的 C# 代码库进行深度分析、依赖提取及代码切片重构。

## 1. 总体设计 (Overall Design)

Analysis 模块采用 **门面模式 (Facade Pattern)** 封装复杂的分析子系统，结合 **基于图的依赖追踪 (Graph-based Dependency Tracking)** 和 **Roslyn 语义重写 (Semantic Rewriting)** 技术，实现自动化的代码精简与重构。

### 1.1 核心架构目标
- **精确性**：基于 Roslyn 语义模型，确保依赖识别达到符号级精度。
- **性能**：利用并行处理和高效图算法处理大规模解决方案。
- **可扩展性**：通过抽象接口支持多种分析维度（如静态、动态、流分析）。
- **实用性**：直接服务于影子类（Shadow Class）生成，支持按需切片。

---

## 2. 核心组件 (Core Components)

### 2.1 分析门面 (Analysis Facade)
- **[AdvancedCodeAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/AdvancedCodeAnalyzer.cs)**:
    - 充当系统的中央指挥部。
    - 协调 `CodeDependencyAnalyzer` (静态)、`CallChainAnalyzer` (动态)、`PlayerFieldExtractor` (特定逻辑) 和 `TypeConflictAnalyzer` (冲突检测)。
    - 提供 `PerformFullAnalysisAsync` 等高级 API，简化上层调用。

### 2.2 依赖分析引擎 (Dependency Analysis Engine)
- **[CodeDependencyAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/CodeDependencyAnalyzer.cs)**:
    - 核心引擎，负责从 Roslyn `Solution` 中递归提取符号依赖。
    - 支持多种分析模式（Standard, Aggressive, EntryOnly）。
    - 使用并行任务流提升大型项目的处理速度。
- **[DependencyGraph.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/DependencyGraph.cs)**:
    - 基于 **QuikGraph** 实现的后端图结构。
    - 存储 `SymbolNode`（包含符号状态、可达性信息）。
    - 提供图算法支持（DFS, BFS, 拓扑排序, 强连通分量分析）。
- **[CompressedSparseRowGraph.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/CompressedSparseRowGraph.cs)**:
    - 针对高频可达性计算优化的压缩稀疏行图实现。

### 2.3 代码切片与重构 (Slicing & Rewriting)
- **[ShadowClassGenerator.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/ShadowClassGenerator.cs)**:
    - 驱动整个重构流程。
    - 识别受分析结果影响的文档，并利用并行增量生成技术回写源码。
- **[MemberSlicingRewriter.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/MemberSlicingRewriter.cs)**:
    - 核心重写器（基于 `CSharpSyntaxRewriter`）。
    - **逻辑切片**：根据依赖图保留必要成员，清空非必要成员的方法体。
    - **死代码消除**：集成 Roslyn `ControlFlowGraph` 进行方法内部的流分析优化。
    - **冲突防御**：自动识别并处理潜在的命名空间/类型冲突（如 XNA 框架类型）。

### 2.4 专项分析器 (Specialized Analyzers)
- **[TypeConflictAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/TypeConflictAnalyzer.cs)**:
    - 检测全局命名空间下的类型名称冲突。
    - 使用 DFS 遍历命名空间树，并缓存程序集元数据以优化性能。
- **[RoslynatorAnalysisAdapter.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/RoslynatorAnalysisAdapter.cs)**:
    - 适配器模式，集成复杂度分析（圈复杂度、认知复杂度）。
    - 提供基于指标的自动化重构建议。

---

## 3. 核心工作流 (Key Workflows)

### 3.1 全量分析流程 (Full Analysis Workflow)
1.  **输入**：种子符号 (Seed Symbols) 和可选的动态日志。
2.  **静态提取**：`CodeDependencyAnalyzer` 递归搜索引用和定义，构建基础依赖图。
3.  **动态增强**：`CallChainAnalyzer` 解析运行时日志，将动态调用的路径注入图中，标记 `IsDynamicallyReached`。
4.  **冲突扫描**：`TypeConflictAnalyzer` 扫描引用库，识别需要特殊处理的歧义标识符。
5.  **输出**：包含完整状态标记的 `DependencyGraph`。

### 3.2 影子源码生成流程 (Shadow Source Generation)
1.  **准备**：执行全量分析，获取依赖图和冲突标识符集。
2.  **过滤**：识别依赖图中标记为“可达”的符号所对应的源文件。
3.  **并行重写**：
    - 为每个受影响的文件启动 `MemberSlicingRewriter`。
    - 对于动态可达方法：保留实现并尝试流分析优化。
    - 对于静态可达方法：保留签名，清空方法体为桩实现（Stub）。
    - 处理冲突标识符的注解。
4.  **格式化**：调用 Roslyn `Formatter` 确保输出代码的可读性。
5.  **持久化**：将重写后的代码存入字典或写入磁盘。

---

## 4. 文件参考表 (File Reference)

| 文件名 | 职责描述 | 核心技术 |
| :--- | :--- | :--- |
| [Abstractions.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/Abstractions.cs) | 模块接口定义 | Interface, DI |
| [AdvancedCodeAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/AdvancedCodeAnalyzer.cs) | 统一分析门面 | Facade Pattern |
| [CodeDependencyAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/CodeDependencyAnalyzer.cs) | 符号级依赖提取 | Roslyn Semantic API |
| [DependencyGraph.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/DependencyGraph.cs) | 依赖关系存储与图计算 | QuikGraph |
| [MemberSlicingRewriter.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/MemberSlicingRewriter.cs) | 代码切片与流分析优化 | Roslyn CFG, Rewriter |
| [ShadowClassGenerator.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/ShadowClassGenerator.cs) | 并行源码生成调度 | Parallel Tasks |
| [TypeConflictAnalyzer.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/TypeConflictAnalyzer.cs) | 全局类型冲突检测 | Metadata Analysis |
| [RoslynatorAnalysisAdapter.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/RoslynatorAnalysisAdapter.cs) | 代码质量与复杂度度量 | Adapter Pattern |
| [AnalysisException.cs](file:///D:/ProjectItem/SourceCode/Net/TerrariaTools/Analysis/AnalysisException.cs) | 模块特定异常 | Custom Exception |
