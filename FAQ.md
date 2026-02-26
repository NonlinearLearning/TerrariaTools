# 常见问题解答 (FAQ)

本文档汇总了在使用 TerrariaTools 进行大规模 C# 代码重构时可能遇到的常见问题及其解决方案。

---

### **1. 解决方案与项目加载**

#### **Q: 为什么在加载 `.sln` 或 `.csproj` 时提示 "MSBuild not found"？**
**A:** TerrariaTools 依赖于系统安装的 MSBuild 环境。请确保：
- 已安装 Visual Studio 2022 或最新的 .NET SDK。
- 运行环境配置了正确的 MSBuild 路径。可以通过在代码中调用 `MSBuildLocator.RegisterDefaults()`（已在 `Load` 类中集成）来自动发现。

#### **Q: 加载大型解决方案时速度非常慢，如何优化？**
**A:**
- 第一次加载会触发 NuGet 包还原，建议先在命令行运行 `dotnet restore`。
- 检查 `Directory.Build.props` 是否包含过多的全局配置。
- 在 `Load` 类中，可以通过配置 `Properties` 字典来禁用一些非必要的加载项（如编译优化）。

---

### **2. 重构逻辑与安全性**

#### **Q: 为什么某些未使用的类没有被 `ClassRefactorer` 删除？**
**A:**
- **反射引用**: 如果类通过反射、配置文件或依赖注入框架动态加载，静态分析可能无法识别。
- **跨程序集引用**: 如果该类被其他未加载到当前 `Solution` 的项目引用，它将被视为“正在使用”。
- **入口点**: 包含 `Main` 方法或具有特定特性（如 `[EntryPoint]`）的类会被保护。

#### **Q: `ExpressionSimplifier` 生成的占位符（如 `0` 或 `null`）会导致编译错误吗？**
**A:** 我们尽可能保证类型安全。重写器会查询 `SemanticModel` 以确定表达式的预期类型：
- 数值类型 -> `0`
- 布尔类型 -> `false`
- 引用类型 -> `null`
- 如果上下文不要求返回值（如表达式语句），则直接删除。
但在某些极端复杂的泛型约束下，可能需要手动调整生成的占位符。

#### **Q: 这种自动重构会破坏代码的行为吗？**
**A:** 自动化重构始终存在风险。为了降低风险，我们提供了：
- **差分测试 (`DifferentialTester`)**: 并行运行新旧逻辑并对比输出。
- **两阶段重写**: 确保语法树在转换过程中始终保持结构完整。

**代码示例：使用 `DifferentialTester` 验证逻辑一致性** (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)
```csharp
var tester = new DifferentialTester(traceContext);
var originalResult = OldLogic.Calculate(data);
var newResult = NewLogic.Calculate(data);

// 如果结果不一致，将自动记录详细的诊断信息
if (!tester.Compare(originalResult, newResult, "CalculationParity")) {
    // 处理不一致情况
}
```
**建议**: 在生产环境应用前，务必通过 `UnitTests` 进行全量回归。

---

### **4. 配置与测试**

#### **Q: 如何自定义重构参数（如忽略特定文件）？**
**A:** 项目使用 `appsettings.json` 进行配置。您可以修改其中的 `Refactoring` 节点：
- `IgnoredFiles`: 添加不需要重构的文件名（如 `["AssemblyInfo.cs", "GlobalSuppressions.cs"]`）。
- `Parallelism`: 设置并行线程数。
- `EnableDryRun`: 设置为 `true` 可进行空跑测试，不实际修改文件。

#### **Q: 如何在不依赖真实文件系统的情况下测试重构逻辑？**
**A:** 我们引入了 `IWorkspaceLoader` 接口。在编写单元测试时，可以使用 `Moq` 等框架 Mock 该接口，拦截 `SaveDocumentAsync` 方法的调用，从而验证文件是否按预期被修改，而无需产生磁盘 IO。参考 `UnitTests/RefactoringTests/ClassRefactorerTests.cs`。

---

### **4. 进阶重构场景**

#### **Q: 如何自定义占位符生成逻辑？**
**A:** 您可以通过重写 `ExpressionSimplifier` 中的 `TryCreatePlaceholder` 方法来实现自定义。

**代码示例：自定义占位符生成** (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)
```csharp
public class MySimplifier : ExpressionSimplifier {
    protected override ExpressionSyntax? TryCreatePlaceholder(SyntaxNode node) {
        // 针对特定类型生成自定义占位符
        var type = GetExpectedType(node);
        if (type?.Name == "MyCustomType") {
            return SyntaxFactory.ParseExpression("MyCustomType.Default");
        }
        return base.TryCreatePlaceholder(node);
    }
}
```

#### **Q: 如何批量处理多个文件并保持语义模型有效？**
**A:** 推荐使用 `ExpressionProcessor.RemoveParts`。它采用两阶段模式，首先在当前的语义模型下标记所有节点，然后一次性执行重写。

**代码示例：批量重构模式** (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)
```csharp
// 1. 定义移除谓词
Func<SyntaxNode, bool> predicate = node => /* 判定逻辑 */;

// 2. 一次性处理，保持语义一致性
var newRoot = ExpressionProcessor.RemoveParts(root, predicate, semanticModel);
```

#### **Q: 如何处理异步方法中的 `await` 移除？**
**A:** `ExpressionSimplifier` 会自动识别 `await` 表达式。如果 `await` 的目标被移除，它会根据返回类型（如 `Task` 或 `Task<T>`）决定是移除整个语句还是保留占位符。 (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)
#### **Q: 在多次转换过程中，如何找回被修改后的节点？**
**A:** 使用 `SyntaxAnnotation`。它可以附加在任何语法节点上，并在树的转换过程中被 Roslyn 尽量保留。 (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)

#### **Q: 什么是“影子类 (Shadow Class)”生成？**
**A:** 影子类是原始类的精简版本。通过 `ShadowClassGenerator`，我们可以根据依赖分析结果，仅保留那些被外部引用的成员（字段、方法、属性），并自动删除所有死代码。这对于提取最小功能库（如精简版协议解析器）非常有用。

#### **Q: 如何处理循环依赖？**
**A:** 引擎集成了 **Tarjan 强连通组件 (SCC)** 算法。在构建依赖图时，如果发现 A 依赖 B 且 B 依赖 A，算法会将它们识别为一个 SCC 块。在进行代码提取或拓扑排序时，这些块将被视为一个不可分割的整体。

#### **Q: 动态分析 (Harmony AOP) 与静态分析有何不同？**
**A:**
- **静态分析 (Roslyn)**: 扫描源代码以查找显式的引用。优点是速度快、覆盖面广，但无法处理反射或复杂的间接字段访问。
- **动态分析 (Harmony)**: 在运行时拦截方法调用（如 `MessageBuffer.GetData`）。它可以精确记录在特定执行路径下实际访问了哪些字段（如 `Player.whoAmI`），从而为静态分析提供更精准的“切片”依据。

#### **Q: 为什么删除一行代码后，空的 `try-catch` 或 `if` 块也被删除了？**
**A:** 这是通过“结构传播 (Structural Propagation)”实现的。引擎会自动检测块是否变为空，并根据上下文决定是否向上清理父节点。 (完整代码见 **[FAQ.cs](file:///d:/ProjectItem/SourceCode/Net/TerrariaTools/Example/FAQ.cs)**)

---

### **4. 性能与资源**

---

### **4. 性能与资源**

#### **Q: 运行重构时内存占用极高，甚至出现 OOM？**
**A:** Roslyn 的 `SemanticModel` 和 `SymbolFinder` 是内存密集型的。对于超大型项目：
- 建议分模块（Project by Project）进行重构，而不是一次性加载整个大型 Solution。
- `PreprocessedSymbolPropagator` 会缓存引用映射，可以通过减小作用域来降低内存压力。

---

### **4. 其它**

#### **Q: 如何自定义重构规则？**
**A:** 您可以继承 `ExpressionSimplifier` 并重写 `Visit` 系列方法，或者在 `CollectNodesToMark` 中添加自定义的谓词逻辑。

#### **Q: 支持 C# 的最新语法吗？**
**A:** 基于 .NET 8.0 和最新的 `Microsoft.CodeAnalysis` 包，项目支持包括 C# 12 在内的大部分现代语法（如记录类型、集合表达式、switch 表达式等）。
