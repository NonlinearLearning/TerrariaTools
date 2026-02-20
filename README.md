# TerrariaTools

TerrariaTools 是一个基于 Roslyn (Microsoft.CodeAnalysis) 开发的强大 C# 代码重构与优化工具集。它专注于大规模代码库的自动化清理、重构以及逻辑等价性验证,Vibe Code产物代码以及.md文件.

## **项目文档**

为了帮助您全面了解和参与本项目，我们提供了以下详细文档：

- **[项目架构设计 (ARCHITECTURE)](ARCHITECTURE.md)**: 深入了解项目的内部设计、核心组件及协作流程。
- **[设计理念与重写思路 (DESIGN CONCEPTS)](DESIGN_CONCEPTS.md)**: 详细阐述了最小化重写的核心思路、算法选择及执行阶段。
- **[常见问题解答 (FAQ)](FAQ.md)**: 解决在安装、运行及重构过程中的各类疑难杂症。
- **[贡献指南 (CONTRIBUTING)](CONTRIBUTING.md)**: 了解开发规范、测试要求及 PR 提交流程。

## **核心功能**

### **1. 智能重构 (Refactoring)**
- **类重构 (`ClassRefactorer`)**:
    - 自动识别并移除整个解决方案中未被引用的类。
    - 能够识别跨程序集的引用关系，确保只移除真正孤立的代码。
- **方法重构 (`MethodRefactorer`)**:
    - **自动死代码移除**: 移除没有任何调用方的非入口方法。
    - **封装性优化 (Privatization)**: 扫描所有方法调用。如果一个 `public` 或 `internal` 方法仅在所属类的内部被调用，则将其访问修饰符自动降级为 `private`。
- **表达式简化 (`ExpressionSimplifier`)**:
    - **智能占位符**: 当表达式的某个子部分（如逻辑与 `&&` 的右侧，或三元运算符的分支）被标记为移除时，工具不会直接破坏代码结构，而是根据上下文自动生成类型匹配的占位符（例如：`bool` 类型替换为 `false`，引用类型替换为 `null`，数值类型替换为 `0`）。
    - **链式调用简化**: 自动处理连续的成员访问（如 `a.b.c`），如果中间某个环节失效，则智能处理整个链条。

### **2. 语义与结构传播 (Propagation)**
- **两阶段重写引擎**:
    - **第一阶段 (标记)**: 通过 `CollectNodesToMark` 算法，从一个种子节点开始，利用 `SemanticModel` 追踪所有受影响的引用。
    - **第二阶段 (执行)**: 使用 `CSharpSyntaxRewriter` 执行原子级的树转换，保证转换过程中的代码始终符合语法。
- **语义传播 (`SemanticPropagation`)**:
    - 追踪变量在不同作用域（闭包、局部函数、lambda 表达式）中的生命周期。
    - 支持别名 (Alias) 和泛型符号的精准识别。
- **结构传播 (`StructuralPropagation`)**:
    - **冒泡标记 (Upward Propagation)**: 如果一个 `Block` 中的所有语句都被标记为移除，则该 `Block` 本身及其父级结构（如 `try-catch` 或 `if`）也会自动进入待移除列表。
    - **边界保护**: 预设了关键的结构边界（如命名空间、类声明），防止重构操作过度扩张导致破坏项目基本骨架。

### **3. 行为一致性保证 (Behavior Guarantee)**
- **影子执行 (`ShadowExecutor`)**: 在受控环境下并行运行原始逻辑与重构逻辑。
- **差分测试 (`DifferentialTester`)**:
    - 实时对比执行输出、副作用及性能指标。
    - 提供详细的差异化诊断报告，精确定位逻辑偏差。
- **逻辑奇偶性校验 (`LogicParityVerifier`)**: 通过静态分析确保重构后的逻辑路径覆盖与原始代码完全对称。

### **3. 深度分析与精细提取 (Advanced Analysis)**
- **递归依赖图 (`DependencyGraph`)**:
    - 基于 Roslyn 语义模型构建全量符号依赖图。
    - 集成 **Tarjan 算法** 识别强连通组件 (SCC)，精准处理复杂的循环依赖。
    - 支持拓扑排序，确定代码提取与生成的逻辑顺序。
- **成员级切片 (`MemberSlicingRewriter`)**:
    - **精细化死代码消除**: 不再局限于类或方法级，可针对单个字段、属性进行提取。
    - **影子类生成 (`ShadowClassGenerator`)**: 自动为原始类生成仅包含必要成员的“影子版本”，实现代码库的极限压缩。
- **动态运行时追踪 (`FieldAccessTracer`)**:
    - 利用 **Harmony AOP** 技术在运行时拦截 IL 指令。
    - 精确记录方法执行过程中实际访问的字段，为静态分析提供真实世界的“热点数据”支撑。

## **工作原理**

TerrariaTools 的重构流程遵循 **分析 -> 标记 -> 传播 -> 验证** 的流水线：

1.  **加载**: 使用 `Microsoft.CodeAnalysis.MSBuild` 加载完整的 `.sln` 或 `.csproj`。
2.  **符号映射**: 预先构建整个解决方案的符号引用表（Symbol Reference Map），以支持亚秒级的语义查询。
3.  **标记收集**:
    - 结构化遍历：识别死代码模式。
    - 语义追踪：将标记从定义传播到所有引用。
4.  **语法重写**:
    - 基于 Roslyn 的不可变语法树，生成变换后的新树。
    - 应用 `ExpressionSimplifier` 进行占位符填充，维持代码的可编译性。
5.  **验证**: 执行单元测试或差分测试，确认重构后的语义等价性。

## **技术栈**

- **运行环境**: .NET 8.0
- **核心库**:
    - `Microsoft.CodeAnalysis.CSharp.Workspaces`: 提供强大的 C# 语法树分析与重写能力。
    - `Microsoft.Build`: 集成 MSBuild 解决方案加载。
- **测试框架**: xUnit

## **项目结构**

```
TerrariaTools/
├── RewriteCodeExpressions/   # 重构核心逻辑 (类/方法重构, 表达式简化, 传播逻辑)
├── ConsistentBehaviorGuarantee/ # 行为一致性验证工具 (差分测试, 影子执行)
├── Analysis/                 # 深度分析模块 (依赖图, 代码切片, 影子类生成)
├── DynamicAnalysis/          # 动态分析模块 (运行时追踪)
├── Load/                     # 解决方案与项目加载逻辑
├── Diagnostics/              # 重构过程中的诊断与日志记录
├── UnitTests/                # 单元测试集
│   ├── RewriteCodeExpressionsTest/ # 重构逻辑测试
│   ├── AnalysisTests/              # 静态分析测试
│   └── DynamicAnalysisTests/       # 动态分析测试
└── Main.cs                   # 命令行程序入口
```

## **快速开始**

### **环境要求**
- 安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 建议使用 Visual Studio 2022 或 JetBrains Rider 进行开发

### **运行重构**
在 `Main.cs` 中配置目标解决方案路径，然后运行程序：
```bash
dotnet run
```

### **运行测试**
项目包含覆盖 50+ 种 C# 语法场景的单元测试：
```bash
dotnet test
```

## **使用示例**

项目在 `Example/` 目录下提供了多个场景的使用示例：

- **[ExpressionRewriteExample.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/ExpressionRewriteExample.cs)**: 演示如何对单个代码片段进行精细化重写，包括自动占位符填充逻辑。
- **[SolutionRefactoringExample.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/SolutionRefactoringExample.cs)**: 演示如何在整个解决方案范围内自动化执行类和方法的清理与优化。
- **[RewriteCodeExpressions.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/RewriteCodeExpressions.cs)**: 一个高性能的集成示例，展示了并行处理、符号批量查找以及内存缓存写回的最佳实践。
- **[SemanticRefactoringWorkflow.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/SemanticRefactoringWorkflow.cs)**: 演示如何结合语义分析（SemanticModel）执行精确的跨项目符号重构。
- **[DifferentialTestingWorkflow.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/DifferentialTestingWorkflow.cs)**: 演示如何利用差分测试框架验证重构前后的逻辑一致性。
- **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**: 汇总了常见疑难问题的代码实现，包括自定义占位符、异步重构及结构自动化清理。

## **开发规范**
[MIT License](LICENSE)
