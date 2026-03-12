# SpreadingEngine (上下文传播引擎) 特性文档

`SpreadingEngine` 是重构工具链中的核心上下文传播引擎。它采用**微规则策略架构**，基于数据流分析将“标记（Marking）”从源头在语法树中进行上下文扩散。

## 核心架构：微规则策略 (Micro-Rule Strategy)

引擎不再硬编码传播逻辑，而是通过自动发现并执行两类微规则：

1.  **节点守卫 (NodeGuards)**: 无状态/局部规则，负责在污染进入节点前进行“熔断”拦截。
2.  **边传播器 (EdgePropagators)**: 有状态/全局规则，负责定义污染如何在节点之间跨越边进行传播。

---

## 核心特性

### 1. 自动化规则发现与注册
*   **反射扫描**：引擎启动时自动扫描程序集中的 `ISpreadingRule` 实现。
*   **按需索引**：根据 `SyntaxKind` 对规则进行索引，确保高频传播时的毫秒级响应。
*   **优先级控制**：支持 P0-P100 优先级排序，确保拦截逻辑（如继承链保护）优先于传播逻辑执行。

### 2. 节点守卫机制 (NodeGuards)
*   **继承链安全熔断 (Inheritance Shield)**: (P0) 绝对禁止向 `virtual`、`override`、`abstract` 方法或接口实现方法传播标记。
*   **净化机制 (Sanitization)**: (P1) 如果赋值右侧是常量（如 `x = 5;`），该点被视为“净化点”，阻断后续污染。
*   **对象初始化保护 (Object Initializer)**: (P50) 拦截对象初始化列表中的赋值，将其转为默认值重置而非删除整个对象创建。

### 3. 智能边传播 (EdgePropagators)
*   **基础传播规则 (Default Spreading)**: (P100)
    *   **语句 -> 变量**：赋值语句被删，其定义的变量即被污染。
    *   **变量 -> 语句**：变量被污染，其后续所有引用该变量的语句均被标记。
*   **隔离与逃逸 (Isolation & Escape)**: 自动识别字段、属性及 `ref/out` 参数的跨块传播，防止逻辑泄露。

### 4. 注释化与结构清理
*   **注释化策略 (Comment-Out)**：针对白名单操作（如 `Log`, `Add`）采用注释而非删除，保留逻辑痕迹。
*   **结构性坍塌 (Structural Cascading)**：
    *   **Try-Catch 坍塌**：`try` 块为空时删除整个异常处理结构。
    *   **循环体坍塌**：`for/foreach` 循环体为空时删除循环语句。

### 5. 反向清理 (Reverse Cleanup)
*   **生存检查**：删除循环前验证循环变量在外部是否有存活引用。
*   **孤儿逻辑清理**：反向清理仅为已删除逻辑提供数据的“生产者”语句。

---

## 相关文件索引
- **引擎核心**: [SpreadingEngine.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/dome/Concept/Ruls/Mark/SpreadingEngine.cs)
- **规则契约**: [ISpreadingRule.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/dome/Concept/Ruls/Mark/SpreadingRules/ISpreadingRule.cs)
- **节点守卫目录**: [NodeGuards/](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/dome/Concept/Ruls/Mark/SpreadingRules/NodeGuards)
- **边传播器目录**: [EdgePropagators/](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/dome/Concept/Ruls/Mark/SpreadingRules/EdgePropagators)
- **依赖分析**: [DataFlowDependencyAnalysis.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/dome/Concept/Analysis/DataFlowDependencyAnalysis.cs)
