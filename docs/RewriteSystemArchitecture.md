# 重写系统架构设计与扩展方案

## 1. 背景与动机 (Background & Motivation)

当前的重写管道（Rewrite Pipeline）虽然在处理局部表达式（Expression）方面表现良好，但在处理需要全局上下文（Global Context）或跨语句（Cross-Statement）逻辑时存在局限性。主要问题包括：
*   **缺乏全局视野**：当前的 `SyntaxTransformerLayer` 主要关注单个节点，难以感知当前节点在整个方法体或控制流中的位置。
*   **上下文传递困难**：难以在不同的重写操作之间传递状态信息（例如：变量定义、控制流分支信息）。
*   **组合行为复杂**：难以将多个简单的 Handler 组合成复杂的重写逻辑。

为了解决这些问题，我们需要引入一个**上下文系统（Context System）**来管理**最小重写单元（Minimal Rewrite Unit）**，并在该系统中实现复杂的 Handler 组合行为。

## 2. 核心概念扩展 (Core Concepts Expansion)

### 2.1 上下文系统 (Context System)
这是一个全局管理器，负责：
*   维护重写过程中的状态（如变量作用域、当前所在的控制流块）。
*   作为信息中枢，允许 Handler 之间交换信息。
*   协调最小重写单元的执行顺序。

### 2.2 最小重写单元 (Minimal Rewrite Unit - MRU)
这是重写逻辑处理的最小原子单位。Handler 直接作用于 MRU。
*   **定义**：在语言语义上不可再分的、具有独立执行意义的语句或表达式。
*   **包含元素**：
    *   **普通语句 (Normal Statements)**：`ExpressionStatement` (如赋值、方法调用), `LocalDeclarationStatement`, `ReturnStatement`, `ThrowStatement`。
    *   **特定表达式 (Specific Expressions)**：在某些上下文中，独立的表达式也可以是 MRU。
    *   **注意**：函数声明本身不是 MRU，但函数定义块（带函数体）是 MRU 的容器。

### 2.3 复合节点 (Composite Nodes)
这些节点本身不直接由 Handler 处理，而是作为 MRU 的容器或结构骨架。它们需要被拆分（Decompose）或遍历，直到触达叶子节点的 MRU。
*   **定义**：包含其他 MRU 或复合节点的语法结构。
*   **包含元素**：
    *   **块 (Block)**：`BlockSyntax`。
    *   **条件控制语句 (Conditional Control)**：`IfStatement`, `SwitchStatement`。
    *   **循环语句 (Loop Statements)**：`ForStatement`, `WhileStatement`, `ForEachStatement`。
    *   **异常处理 (Exception Handling)**：`TryStatement`, `CatchClause`。
    *   **函数定义 (Method Definition)**：`MethodDeclaration` (及其 Body)。

### 2.4 简化语法树 (Simplified Rewrite Tree)
为了消除 Roslyn 语法树中与重写逻辑无关的冗余信息（如格式化Trivia、无关的标记），我们需要建立一种专门用于重写的树结构。
*   **构建方式**：基于 Roslyn SyntaxTree 进行映射。
*   **节点类型**：
    *   **Leaf Node (Minimal Node)** -> 对应具体的 `RewriteHandler`。
    *   **Structural Node (Composite Node)** -> 对应 `StructureVisitor` 或 `CompositeHandler`（负责分发）。
*   **信息剔除**：移除不影响语义的 Trivia（除非特定保留）、移除纯语法标记（如分号、花括号对象，只保留结构关系）。

## 3. 复杂 Handler 的组合行为 (Complex Handler Composition)
在上下文系统中，Handler 不再是孤立的。可以通过以下方式组合：
*   **管道式 (Pipeline)**：一个 MRU 依次经过多个 Handler（如：先记录日志 -> 再修改参数 -> 最后重命名）。
*   **条件触发 (Conditional Trigger)**：Handler A 的输出结果决定是否触发 Handler B。
*   **聚合 (Aggregation)**：父节点收集所有子节点 Handler 的结果，进行统一处理（如：将多个变量声明合并为一个）。

---

## 4. 10种 解决方案提案 (10 Proposed Solutions)

以下方案按实现复杂度从低到高、架构风格从传统到现代排序。

### 方案 1：增强型 Visitor + 状态栈 (Enhanced Visitor with State Stack)
*   **描述**：保留现有的 Visitor 模式，但在遍历过程中维护一个全局的 `Stack<Context>`。每进入一个复合节点（如 IfStatement），压入新上下文；离开时弹出。
*   **优点**：实现简单，与 Roslyn 原生模式兼容性好。
*   **缺点**：对于复杂的跨节点依赖（如后向引用）处理较弱，耦合度较高。

### 方案 2：双遍扫描架构 (Two-Pass Architecture: Analyze then Rewrite)
*   **描述**：
    *   **Pass 1 (Analysis)**：遍历语法树，构建“简化重写树”和“语义上下文图”，不修改代码，只收集信息并标记需要重写的节点（MRU）。
    *   **Pass 2 (Rewrite)**：基于 Pass 1 的标记和上下文，直接执行重写操作。
*   **优点**：重写时拥有完整的全局信息，决策更准确。
*   **缺点**：性能开销稍大（两次遍历），状态同步可能复杂。

### 方案 3：中间表示层 (Intermediate Representation - IR)
*   **描述**：将 Roslyn 语法树转换为一个扁平化的线性列表（Linear List of Operations），类似于汇编或三地址码。Handler 处理这个线性列表，最后再重建回语法树。
*   **优点**：极大地简化了控制流分析，容易处理插入和删除。
*   **缺点**：重建语法树（Code Generation）难度大，丢失了原始代码的层级结构。

### 方案 4：基于规则的引擎 (Rule-Based Engine)
*   **描述**：定义一套声明式的规则（Rules），每条规则包含 `Pattern`（匹配条件）和 `Action`（重写逻辑）。引擎自动匹配树中的 MRU 并应用规则。
*   **优点**：逻辑解耦，易于扩展新规则，类似于 Roslyn Analyzers。
*   **缺点**：难以处理复杂的、依赖上下文顺序的逻辑。

### 方案 5：中间件管道模式 (Middleware Pipeline per Node)
*   **描述**：借鉴 ASP.NET Core 中间件设计。为每种类型的 MRU 定义一个独立的 `Pipeline`。Context 对象在管道中流动，每个 Middleware 可以读取/修改 Context 或终止处理。
*   **优点**：极高的灵活性和可组合性，易于单元测试。
*   **缺点**：对象分配开销大，调用栈深。

### 方案 6：分层任务树 (Hierarchical Task Tree)
*   **描述**：将重写过程建模为一棵任务树。复合节点生成“复合任务”，MRU 生成“原子任务”。任务之间可以声明依赖关系。
*   **优点**：清晰的任务边界，支持并行处理（理论上），易于追踪进度。
*   **缺点**：构建任务树的开销，系统复杂度高。

### 方案 7：事件驱动/观察者模式 (Event-Driven / Observer)
*   **描述**：遍历器（Walker）只负责走树，并在关键点触发事件（`OnEnterBlock`, `OnVisitStatement`）。Handlers 订阅感兴趣的事件。Context 作为事件参数传递。
*   **优点**：完全解耦遍历逻辑和业务逻辑。
*   **缺点**：执行流不直观，调试困难（Callback Hell）。

### 方案 8：实体组件系统 (ECS - Entity Component System) for Code
*   **描述**：
    *   **Entity**: 每个语法节点 ID。
    *   **Component**: 节点的属性（如 `IsAsync`, `HasReturn`, `IsRewritable`）。
    *   **System**: 具体的 Handler 逻辑，查询具有特定 Component 的 Entity 并进行处理。
*   **优点**：数据局部性好，极其灵活的组合能力。
*   **缺点**：对于树形结构的层级关系处理不如传统 OOP 直观，属于过度设计。

### 方案 9：LSP 风格的文本编辑聚合 (LSP-Style Text Edit Aggregation)
*   **描述**：Handler 不直接修改树，而是返回“编辑意图”（TextEdit: Range + NewText）。最后由一个协调器（Coordinator）解决冲突并一次性应用所有编辑。
*   **优点**：避免了在遍历过程中修改树导致的索引失效问题，原子性提交。
*   **缺点**：对 AST 的结构性修改（如移动大块代码）支持较弱，更多用于文本层面。

### 方案 10：函数式状态单子 (Functional State Monad)
*   **描述**：使用函数式编程思想。重写函数签名设计为 `Func<Node, State, (NewNode, NewState)>`。通过 Monad 将状态在函数间隐式传递。
*   **优点**：纯函数，无副作用，极易测试和并发安全。
*   **缺点**：C# 对 Monad 支持有限，代码可读性对非 FP 开发者不友好。

## 建议选择

*   **如果追求稳健性和可维护性**：推荐 **方案 2（双遍扫描架构）** 或 **方案 5（中间件管道模式）**。
*   **如果追求极致的解耦**：推荐 **方案 4（基于规则的引擎）**。
*   **如果需要处理极其复杂的控制流重写**：推荐 **方案 3（中间表示层 IR）**。

请根据当前项目的规模和团队熟悉度选择最合适的方案。
